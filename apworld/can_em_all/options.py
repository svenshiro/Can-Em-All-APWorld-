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
    """How many Children Fed milestones are checks (lifetime cans). 0 turns them off."""
    display_name = "Children Fed Milestones"
    range_start = 0
    range_end = 50
    default = 15


class ChildrenFedMax(Range):
    """Children Fed needed for the last milestone. Milestones are spread between 100 and this, growing faster
    towards the end. A normal first run ends somewhere around 280,000."""
    display_name = "Children Fed Max"
    range_start = 1_000
    range_end = 1_000_000
    default = 250_000


class SacrificeMilestones(Range):
    """How many Sacrifices (lifetime kills) milestones are checks. 0 turns them off."""
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


class Achievements(Toggle):
    """Make the game's achievements checks (the ones tied to the ending are left out)."""
    display_name = "Achievements"


class CashBundleSize(Range):
    """A Cash Bundle gives this many seconds' worth of your current income, so it stays useful all game."""
    display_name = "Cash Bundle Size"
    range_start = 10
    range_end = 600
    default = 60


@dataclass
class CanEmAllOptions(PerGameCommonOptions):
    goal: Goal
    goal_children_fed: GoalChildrenFed
    prestige_upgrades: PrestigeUpgrades
    children_fed_milestones: ChildrenFedMilestones
    children_fed_max: ChildrenFedMax
    sacrifice_milestones: SacrificeMilestones
    sacrifices_max: SacrificesMax
    achievements: Achievements
    cash_bundle_size: CashBundleSize
    start_inventory_from_pool: StartInventoryPool
    death_link: DeathLink
