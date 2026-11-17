from dataclasses import dataclass

from Options import Choice, DefaultOnToggle, PerGameCommonOptions, Range, StartInventoryPool, Toggle, DeathLink


class Goal(Choice):
    """What you need to do to finish.
    ending: buy the Master Key (it appears once the whole skill tree is bought) and leave the factory.
    children_fed: feed a set number of children in total (see Goal Children Fed)."""
    display_name = "Goal"
    option_ending = 0
    option_children_fed = 1
    default = 0


class GoalChildrenFed(Range):
    """Lifetime Children Fed needed when the goal is children_fed."""
    display_name = "Goal Children Fed"
    range_start = 10_000
    range_end = 1_000_000
    default = 250_000


class PrestigeUpgrades(DefaultOnToggle):
    """Include the 21 prestige (sell the factory) upgrades as checks and as items."""
    display_name = "Prestige Upgrades"


class ChildrenFedMilestones(Range):
    """Most Children Fed milestones (lifetime cans) that are checks. 0 turns them off.
    Milestones go 5, 10, 20, 30, 40, 50, 100, 150, 200, 250, 500, 750, 1,000, 1,500 ... up to Children Fed Max
    (28 milestones with the default max). If this is lower, milestones are skipped evenly."""
    display_name = "Children Fed Milestones"
    range_start = 0
    range_end = 50
    default = 50


class ChildrenFedMax(Range):
    """Children Fed needed for the last milestone. A normal first run ends somewhere around 280,000."""
    display_name = "Children Fed Max"
    range_start = 1_000
    range_end = 1_000_000
    default = 250_000


class SacrificeMilestones(Range):
    """Most Sacrifices milestones (lifetime kills) that are checks. 0 turns them off.
    They use the same 5, 10, 20, 30 ... steps as Children Fed, up to Sacrifices Max (21 with the default max)."""
    display_name = "Sacrifice Milestones"
    range_start = 0
    range_end = 50
    default = 0


class SacrificesMax(Range):
    """Sacrifices needed for the last Sacrifice milestone. A normal first run is roughly 20,000."""
    display_name = "Sacrifices Max"
    range_start = 100
    range_end = 100_000
    default = 20_000


class Achievements(DefaultOnToggle):
    """Make the game's achievements checks (the ones tied to the ending are left out)."""
    display_name = "Achievements"


class CashBundleSize(Range):
    """A Cash Bundle gives this many seconds' worth of your current income, so it stays useful all game."""
    display_name = "Cash Bundle Size"
    range_start = 10
    range_end = 600
    default = 60


class UpgradePointSize(Range):
    """How many prestige upgrade points one Upgrade Point item gives. Prestige upgrades cost 2 to 100 points each
    (567 for all of them)."""
    display_name = "Upgrade Point Size"
    range_start = 1
    range_end = 50
    default = 10


class DeathLinkChance(Range):
    """With Death Link on: the chance, in percent, that a day ships a cursed can and sends a death link.
    It's rolled once per day so it doesn't grow with your factory. A full run is roughly 60-80 days, so 7 sends
    about 4-6 death links a run. Received death links end your current shift (the player can't die)."""
    display_name = "Death Link Chance"
    range_start = 0
    range_end = 100
    default = 7


class EarlyUpgrades(DefaultOnToggle):
    """Put a few income upgrades in the cheapest skill tree checks (the ones you can buy right away) so you can
    reach the $1,000 checks without getting stuck: 2 More Creatures, 2 Conveyors, 1 More Cans, 1 Can Value
    and the Vacuum."""
    display_name = "Early Upgrades"


@dataclass
class CanEmAllOptions(PerGameCommonOptions):
    early_upgrades: EarlyUpgrades
    goal: Goal
    goal_children_fed: GoalChildrenFed
    prestige_upgrades: PrestigeUpgrades
    children_fed_milestones: ChildrenFedMilestones
    children_fed_max: ChildrenFedMax
    sacrifice_milestones: SacrificeMilestones
    sacrifices_max: SacrificesMax
    achievements: Achievements
    cash_bundle_size: CashBundleSize
    upgrade_point_size: UpgradePointSize
    start_inventory_from_pool: StartInventoryPool
    death_link: DeathLink
    death_link_chance: DeathLinkChance
