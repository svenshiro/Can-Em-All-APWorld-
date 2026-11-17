"""Item/location naming and grouping for Can 'Em All, built from the dumped skill tree."""
import json
import pkgutil
import re

ITEM_ID_BASE = 6_330_000
LOCATION_ID_BASE = 6_330_000
MILESTONE_OFFSET = 1000      # Children Fed milestones: +1000..+1099
SACRIFICE_OFFSET = 1100      # Sacrifices milestones:   +1100..+1199
ACHIEVEMENT_OFFSET = 1200    # Achievements:            +1200..

def _load(name):
    return pkgutil.get_data(__name__, "data/" + name).decode("utf-8")

SKILL_TREE = json.loads(_load("skilltree.json"))
PRESTIGE = json.loads(_load("permanent_upgrades.json"))
# SkillId enum order (None = 0); location ids for skills are LOCATION_ID_BASE + enum value
SKILL_ENUM = ["None"] + [s for s in _load("skill_ids.txt").split() if s]
SKILL_ENUM_INDEX = {s: i for i, s in enumerate(SKILL_ENUM)}

TREE_NODES = SKILL_TREE["nodes"]
ROOT = SKILL_TREE["rootNode"]

def pretty(skill_id: str) -> str:
    m = re.fullmatch(r"UnlockConveyor(\d)_(\d)", skill_id)
    if m:
        return f"Conveyor Line {m.group(1)} Tier {m.group(2)}"
    m = re.fullmatch(r"UnlockEnemy(\d)", skill_id)
    if m:
        return f"Enemy Type {m.group(1)}"
    s = re.sub(r"(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ", skill_id)
    s = s.replace("Ak 47", "AK-47").replace("Flame Thrower", "Flamethrower")
    return s.replace("_", " ").strip()

# Skill type -> progressive item name. Types not listed become one item per node (pretty name).
PROGRESSIVE_BY_TYPE = {
    "AttackSpeedSkill": "Progressive Attack Speed",
    "BleedingDurationSkill": "Progressive Bleeding",
    "CanProcessingSpeedSkill": "Progressive Production Speed",
    "CanValueMultiplierSkill": "Progressive Can Value",
    "ConveyorSpeedSkill": "Progressive Conveyor Speed",
    "CriticalDamageSkill": "Progressive Critical Chance",
    "DamageSkill": "Progressive Damage",
    "DayDurationSkill": "Progressive Day Length",
    "DuplicateMoneyChanceSkill": "Progressive Duplicate Coins",
    "ExplosiveBulletDamageSkill": "Progressive Explosive Damage",
    "ExplosiveBulletSkill": "Progressive Explosive Chance",
    "ExtraCanChanceSkill": "Progressive Duplicate Can Chance",
    "FreeBulletProbability": "Progressive Free Bullet Chance",
    "HoleInOneSkill": "Progressive Hole In One",
    "InstantKillSkill": "Progressive Instant Kill",
    "MoreBulletSkill": "Progressive More Bullets",
    "MoreCansSkill": "Progressive More Cans",
    "MoreCreaturesSkill": "Progressive More Creatures",
    "MoveSpeedSkill": "Progressive Run Speed",
    "NightShiftDurationSkill": "Progressive Night Shift",
    "PunchDamageSkill": "Progressive Punch Damage",
    "RobotPusherSkill": "Progressive Robot Size",
    "RobotSpeedSkill": "Progressive Robot Speed",
    "ShinyCanChanceSkill": "Progressive Shiny Can Chance",
    "ShinyChanceSkill": "Progressive Shiny Enemy Chance",
    "ShinyValueSkill": "Progressive Shiny Can Value",
    "ShockwaveDamageSkill": "Progressive Shockwave Damage",
    "ShockwaveSkill": "Progressive Shockwave Chance",
    "ShootSpeedSkill": "Progressive Shoot Speed",
    "StaminaSkill": "Progressive Stamina",
    "TurretBulletSkill": "Progressive Turret Bullets",
    "TurretDamageSkill": "Progressive Turret Damage",
    "UnlockEnemySkill": "Progressive Enemy Type",
    "UnlockMachineSkill": "Progressive Machine",
    "UnlockRobotSkill": "Progressive Robot Cleaner",
    "UnlockTurretSkill": "Progressive Turret",
    "VacuumRangeSkill": "Progressive Vacuum Range",
    "VacuumShootDistanceSkill": "Progressive Vacuum Force",
    "VacuumShootSpeedSkill": "Progressive Vacuum Speed",
    "VacuumStorageSkill": "Progressive Vacuum Storage",
}

def tree_item_name(node) -> str:
    if node["type"] == "UnlockConveyorSkill":
        line = re.fullmatch(r"UnlockConveyor(\d)_\d", node["id"]).group(1)
        return f"Progressive Conveyor Line {line}"
    return PROGRESSIVE_BY_TYPE.get(node["type"], pretty(node["id"]))

PRESTIGE_PROGRESSIVE = {
    "StartingCashSkill": "Progressive Starting Cash",
    "BruteForceSkill": "Progressive Brute Force",
    "ProfitBoostSkill": "Progressive Profit Boost",
}

def prestige_item_name(up) -> str:
    return PRESTIGE_PROGRESSIVE.get(up["type"], pretty(up["id"]))

def tree_location_name(node) -> str:
    return f"Skill Tree - {pretty(node['id'])}"

def prestige_location_name(up) -> str:
    return f"Prestige - {pretty(up['id'])}"

def milestone_name(kind: str, n: int) -> str:
    return f"{kind} - {n:,}"

ACHIEVEMENTS = {
    # name -> display; ending-related ones are left out
    "HumanMeat": "Human Meat", "SpecialDelivery": "Special Delivery", "FreshMeat": "Fresh Meat",
    "NotACan": "Not A Can", "FirstBlood": "First Blood", "CollateralDamage": "Collateral Damage",
    "FeedingTheFuture": "Feeding The Future", "AFreshStart": "A Fresh Start",
}
ACHIEVEMENT_ORDER = ["HumanMeat", "SpecialDelivery", "FreshMeat", "NotACan", "FirstBlood", "CollateralDamage",
                     "FeedingTheFuture", "EndingWorldHunger", "WorldsGreatestHumanitarian", "AFreshStart",
                     "FreedomAtLast", "ThePerfectEmployee"]

def achievement_location_name(key: str) -> str:
    return f"Achievement - {ACHIEVEMENTS[key]}"

FILLER_ITEMS = ["Cash Bundle", "Upgrade Point"]
VICTORY_ITEM = "Freedom"
GOAL_LOCATION = "Leave the Factory"
