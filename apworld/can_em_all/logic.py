"""Logic: what you can afford depends on how much the factory makes.
'Production' counts the received items that raise income (machines, conveyors, enemy types, cans, vacuum)."""
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from BaseClasses import CollectionState

PRODUCTION_ITEMS = (
    "Vacuum", "Progressive Machine", "Progressive Enemy Type", "Progressive More Cans", "Progressive Can Value",
    "Progressive More Creatures", "Progressive Production Speed",
    "Progressive Conveyor Line 1", "Progressive Conveyor Line 2", "Progressive Conveyor Line 3",
    "Progressive Conveyor Line 4", "Progressive Conveyor Line 5",
)
# 67 production items exist in total.

# (max price, production needed)
PRICE_TIERS = (
    (100, 0),
    (1_000, 3),
    (10_000, 8),
    (100_000, 15),
    (1_000_000, 25),
    (10_000_000, 35),
    (50_000_000, 45),
    (100_000_000, 55),
)
GOAL_PRODUCTION = 58

def production_for_price(price: int) -> int:
    for limit, need in PRICE_TIERS:
        if price <= limit:
            return need
    return PRICE_TIERS[-1][1]

def production_for_cans(cans: int) -> int:
    if cans <= 1_000: return 3
    if cans <= 10_000: return 8
    if cans <= 50_000: return 15
    if cans <= 150_000: return 25
    if cans <= 300_000: return 35
    if cans <= 600_000: return 45
    return 55

def production_for_prestige(points: int) -> int:
    # Points come from selling the factory; a decent run pays for the cheap ones.
    if points <= 20: return 25
    if points <= 50: return 35
    return 45

def production(state: "CollectionState", player: int) -> int:
    return state.count_from_list(PRODUCTION_ITEMS, player)

def has_production(state: "CollectionState", player: int, need: int) -> bool:
    return need <= 0 or production(state, player) >= need

def effective_prices(nodes):
    """In game a node needs its parents bought first, so the money needed is the most expensive node on the way."""
    by_id = {n["id"]: n for n in nodes}
    memo = {}
    def eff(i, stack=()):
        if i in memo: return memo[i]
        if i in stack: return by_id[i]["price"]
        n = by_id[i]
        p = max([n["price"]] + [eff(r, stack + (i,)) for r in n["requires"] if r in by_id])
        memo[i] = p
        return p
    return {i: eff(i) for i in by_id}
