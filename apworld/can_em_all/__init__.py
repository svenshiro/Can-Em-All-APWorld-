from collections import Counter
from typing import Any, Dict, List

from BaseClasses import Item, ItemClassification, Location, Region, Tutorial
from worlds.AutoWorld import WebWorld, World

from . import names as N
from .logic import (GOAL_PRODUCTION, effective_prices, has_production, production_for_cans, production_for_prestige,
                    production_for_price, PRODUCTION_ITEMS, SELLING_WEAPONS)
from .options import CanEmAllOptions

GAME = "Can 'Em All"

# ---- static tables (same for every seed, so ids never change) ----
_tree_items = Counter(N.tree_item_name(n) for n in N.TREE_NODES)
_prestige_items = Counter(N.prestige_item_name(u) for u in N.PRESTIGE)
_weapon_items = {"Bat", "Knife", "Pistol", "Uzi", "AK-47", "Shotgun", "Rocket", "Flamethrower", "Chainsaw"}

ALL_ITEM_NAMES: List[str] = sorted(set(_tree_items) | set(_prestige_items)) + N.FILLER_ITEMS
ITEM_NAME_TO_ID = {name: N.ITEM_ID_BASE + i for i, name in enumerate(ALL_ITEM_NAMES)}

# Milestones climb a ladder of round numbers: 5, 10, 20, 30, 40, 50, then 1, 1.5, 2, 2.5, 5 and 7.5 times each
# power of ten (100, 150, 200, 250, 500, 750, 1000, 1500, ...), so they come quickly at first and spread out later.
_LADDER_START = [5, 10, 20, 30, 40, 50]
_LADDER_STEPS = [1, 1.5, 2, 2.5, 5, 7.5]


def _ladder(end: int) -> List[int]:
    out = [v for v in _LADDER_START if v < end]
    mag = 100
    while mag < end:
        out += [int(mag * m) for m in _LADDER_STEPS if mag * m < end]
        mag *= 10
    return out + [end]


def _milestones(count: int, end: int) -> List[int]:
    """The ladder up to `end`. If it has more steps than `count`, steps are dropped evenly (first and last kept)."""
    if count <= 0: return []
    ladder = _ladder(end)
    if count >= len(ladder): return ladder
    if count == 1: return [end]
    picks = {round(i * (len(ladder) - 1) / (count - 1)) for i in range(count)}
    return [ladder[i] for i in sorted(picks)]

LOCATION_NAME_TO_ID: Dict[str, int] = {}
for n in N.TREE_NODES:
    LOCATION_NAME_TO_ID[N.tree_location_name(n)] = N.LOCATION_ID_BASE + N.SKILL_ENUM_INDEX[n["id"]]
for u in N.PRESTIGE:
    LOCATION_NAME_TO_ID[N.prestige_location_name(u)] = N.LOCATION_ID_BASE + N.SKILL_ENUM_INDEX[u["id"]]
# Milestone locations: id = offset + slot index; the threshold for each slot is decided per seed and sent in slot_data.
for i in range(50):
    LOCATION_NAME_TO_ID[f"Children Fed Milestone {i + 1}"] = N.LOCATION_ID_BASE + N.MILESTONE_OFFSET + i
    LOCATION_NAME_TO_ID[f"Sacrifices Milestone {i + 1}"] = N.LOCATION_ID_BASE + N.SACRIFICE_OFFSET + i
for i, key in enumerate(N.ACHIEVEMENT_ORDER):
    if key in N.ACHIEVEMENTS:
        LOCATION_NAME_TO_ID[N.achievement_location_name(key)] = N.LOCATION_ID_BASE + N.ACHIEVEMENT_OFFSET + i

EFFECTIVE_PRICE = effective_prices(N.TREE_NODES)

# Early Upgrades option: item -> copies placed in the starting checks (39 skill tree nodes cost $100 or less).
EARLY_UPGRADES = {
    "Progressive More Creatures": 2,
    "Progressive Conveyor": 2,
    "Progressive More Cans": 1,
    "Progressive Can Value": 1,
    "Vacuum": 1,
}


class CanEmAllItem(Item):
    game = GAME


class CanEmAllLocation(Location):
    game = GAME


class CanEmAllWeb(WebWorld):
    theme = "partyTime"
    tutorials = [Tutorial("Setup Guide", "How to set up Can 'Em All for Archipelago.", "English", "setup_en.md",
                          "setup/en", ["Tyler"])]


class CanEmAllWorld(World):
    """Run a cuteling-canning factory: buy skills, feed the children, and earn your way out.
    Skill tree nodes, prestige upgrades and Children Fed milestones are checks; the skills themselves are items."""
    game = GAME
    web = CanEmAllWeb()
    options_dataclass = CanEmAllOptions
    options: CanEmAllOptions
    item_name_to_id = ITEM_NAME_TO_ID
    location_name_to_id = LOCATION_NAME_TO_ID
    item_name_groups = {"Production": set(PRODUCTION_ITEMS), "Weapons": _weapon_items}
    topology_present = False

    def generate_early(self) -> None:
        o = self.options
        self.children_fed_thresholds = _milestones(o.children_fed_milestones.value, o.children_fed_max.value)
        self.sacrifice_thresholds = _milestones(o.sacrifice_milestones.value, o.sacrifices_max.value)
        if o.early_upgrades:
            # Placed in this world's first sphere: checks you can buy before receiving anything.
            early = self.multiworld.local_early_items[self.player]
            for name, count in EARLY_UPGRADES.items():
                early[name] = max(early.get(name, 0), count)

    # ---- items ----
    def create_item(self, name: str) -> CanEmAllItem:
        if name in N.FILLER_ITEMS:
            cls = ItemClassification.filler
        elif name in PRODUCTION_ITEMS:
            cls = ItemClassification.progression
        elif name == N.VICTORY_ITEM:
            cls = ItemClassification.progression
        elif name in SELLING_WEAPONS and self.options.prestige_upgrades:
            cls = ItemClassification.progression   # needed to be offered the factory sale (prestige checks)
        else:
            cls = ItemClassification.useful
        return CanEmAllItem(name, cls, self.item_name_to_id.get(name), self.player)

    def get_filler_item_name(self) -> str:
        return self.random.choice(N.FILLER_ITEMS)

    def create_items(self) -> None:
        pool: List[CanEmAllItem] = []
        for name, count in _tree_items.items():
            pool += [self.create_item(name) for _ in range(count)]
        if self.options.prestige_upgrades:
            for name, count in _prestige_items.items():
                pool += [self.create_item(name) for _ in range(count)]
        unfilled = len(self.multiworld.get_unfilled_locations(self.player))
        while len(pool) < unfilled:
            pool.append(self.create_item(self.get_filler_item_name()))
        self.multiworld.itempool += pool

    # ---- regions / locations ----
    def _add(self, region: Region, name: str, need: int) -> None:
        loc = CanEmAllLocation(self.player, name, self.location_name_to_id[name], region)
        p = self.player
        loc.access_rule = lambda state, need=need: has_production(state, p, need)
        region.locations.append(loc)

    def create_regions(self) -> None:
        menu = Region("Menu", self.player, self.multiworld)
        factory = Region("Factory", self.player, self.multiworld)
        self.multiworld.regions += [menu, factory]
        menu.connect(factory)

        for n in N.TREE_NODES:
            self._add(factory, N.tree_location_name(n), production_for_price(EFFECTIVE_PRICE[n["id"]]))
        if self.options.prestige_upgrades:
            p = self.player
            for u in N.PRESTIGE:
                self._add(factory, N.prestige_location_name(u), production_for_prestige(u["price"]))
                # Selling the factory opens with a story email that only comes after the AK-47 one.
                loc = factory.locations[-1]
                rule = loc.access_rule
                loc.access_rule = lambda state, rule=rule: rule(state) and state.has_all(SELLING_WEAPONS, p)
        for i, t in enumerate(self.children_fed_thresholds):
            self._add(factory, f"Children Fed Milestone {i + 1}", production_for_cans(t))
        for i, t in enumerate(self.sacrifice_thresholds):
            self._add(factory, f"Sacrifices Milestone {i + 1}", production_for_cans(t * 12))
        if self.options.achievements:
            for key in N.ACHIEVEMENT_ORDER:
                if key in N.ACHIEVEMENTS:
                    self._add(factory, N.achievement_location_name(key), 15)

        goal = CanEmAllLocation(self.player, N.GOAL_LOCATION, None, factory)
        if self.options.goal == self.options.goal.option_children_fed:
            need = production_for_cans(self.options.goal_children_fed.value)
        else:
            need = GOAL_PRODUCTION
        p = self.player
        goal.access_rule = lambda state, need=need: has_production(state, p, need)
        goal.place_locked_item(CanEmAllItem(N.VICTORY_ITEM, ItemClassification.progression, None, self.player))
        factory.locations.append(goal)

    def set_rules(self) -> None:
        self.multiworld.completion_condition[self.player] = lambda state: state.has(N.VICTORY_ITEM, self.player)

    def fill_slot_data(self) -> Dict[str, Any]:
        o = self.options
        return {
            "version": "0.3.0",
            "goal": o.goal.current_key,
            "goal_children_fed": o.goal_children_fed.value,
            "prestige_upgrades": bool(o.prestige_upgrades),
            # which items are prestige upgrades, so the client can list them separately
            "prestige_items": sorted(_prestige_items),
            "children_fed_thresholds": self.children_fed_thresholds,
            "sacrifice_thresholds": self.sacrifice_thresholds,
            "achievements": bool(o.achievements),
            "cash_bundle_seconds": o.cash_bundle_size.value,
            "upgrade_point_size": o.upgrade_point_size.value,
            "death_link": bool(o.death_link),
            "death_link_chance": o.death_link_chance.value,
            # skill id each tree/prestige location stands for (location id - base = SkillId enum value)
            "location_id_base": N.LOCATION_ID_BASE,
            # progressive item -> skill ids in the order they're applied (cheapest first)
            "item_skills": _item_skill_order(),
            # skill id -> skill id that must be applied first (a conveyor waits for its machine)
            "skill_requires": {n["id"]: m for n in N.TREE_NODES
                               if n["type"] == "UnlockConveyorSkill" and (m := N.conveyor_machine(n["id"]))},
        }


def _item_skill_order() -> Dict[str, List[str]]:
    out: Dict[str, List[str]] = {}
    for n in sorted(N.TREE_NODES, key=lambda n: (n["price"], n["id"])):
        out.setdefault(N.tree_item_name(n), []).append(n["id"])
    for u in sorted(N.PRESTIGE, key=lambda u: (u["price"], u["id"])):
        out.setdefault(N.prestige_item_name(u), []).append(u["id"])
    return out
