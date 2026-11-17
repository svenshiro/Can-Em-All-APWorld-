# Can 'Em All Archipelago

[Archipelago](https://archipelago.gg) multiworld randomizer support for
[Can 'Em All](https://store.steampowered.com/app/5133900/). This repository has two parts:

- **`apworld/can_em_all`**: the Archipelago world (`can_em_all.apworld`) used to generate seeds.
- **`mod/CanEmAllAP`**: a BepInEx 6 plugin that connects the game to the Archipelago server.

This is a fan project and is not affiliated with the developer of Can 'Em All.

## What's randomized
- Every **skill tree node** is a check. Buying a node sends whatever item is there; the node's own effect only
  applies when its item arrives. Repeated skills are progressive items (for example, Progressive Turret Damage).
- **Progressive Conveyor** (25) unlocks conveyors in tree order, line by line. A conveyor for a machine you don't
  have yet waits until that **Progressive Machine** arrives (the F8 panel shows how many are waiting).
- **Prestige upgrades** are checks (option `prestige_upgrades`). Selling the factory is offered by a story email late in a run, so in logic these need the Bat, Knife, Pistol, Uzi and AK-47 plus a mid-to-late factory.
- **Children Fed milestones** (lifetime cans) are checks. **Achievements** are checks too (on by default), and **Sacrifice milestones** can be turned on.
- Filler is **Cash Bundle** (some seconds of your current income) and **Upgrade Point** (prestige points, option `upgrade_point_size`).

## Goal
- `ending` (default): buy the Master Key and leave the factory.
- `children_fed`: reach a lifetime Children Fed total (`goal_children_fed`).

## Options
| Option | Default | What it does |
| --- | --- | --- |
| `goal` | `ending` | Win condition |
| `goal_children_fed` | 250000 | Target for the `children_fed` goal |
| `early_upgrades` | on | Puts 2 More Creatures, 2 Conveyors, More Cans, Can Value and the Vacuum in the cheapest checks |
| `prestige_upgrades` | on | Prestige upgrades are checks and items |
| `children_fed_milestones` | 50 | Most Children Fed checks (milestones go 5, 10, 20, 30, 40, 50, 100, 150, 200, 250, 500, 750, 1000, ...; 28 with the default max) |
| `children_fed_max` | 250000 | Highest Children Fed milestone |
| `sacrifice_milestones` | 0 | Most Sacrifice checks (same steps as Children Fed) |
| `sacrifices_max` | 20000 | Highest Sacrifice milestone |
| `achievements` | on | Steam achievements are checks |
| `cash_bundle_size` | 60 | Seconds of income per Cash Bundle |
| `upgrade_point_size` | 10 | Prestige points per Upgrade Point item (all prestige upgrades cost 567 together) |
| `death_link` | off | Received death links end your current shift (day and night) so the elevator comes down. Each day may ship a cursed can that sends one and ends your own shift too |
| `death_link_chance` | 7 | Percent chance per day of a cursed can (about 4-6 death links in a full run) |

## Installing
**Archipelago:** put `can_em_all.apworld` in your Archipelago `custom_worlds` folder (or double-click it).
Requires Archipelago 0.6.4 or newer.

**Game:** see [the setup guide](apworld/can_em_all/docs/setup_en.md). In short: extract the release zip into the game
folder, start the game, press **F8**, enter server, slot and password, and connect.

## Known issues
- Logic thresholds (how much production is needed for expensive nodes) are first guesses.

## Building
**apworld:** zip the `can_em_all` folder so the zip contains `can_em_all/...`, and rename it to `can_em_all.apworld`.

**Mod:** needs the .NET 6 SDK and the game with BepInEx 6 IL2CPP installed and run once (so `BepInEx/interop` exists).

```
cd mod/CanEmAllAP
dotnet build -c Release -p:GameDir="E:\SteamLibrary\steamapps\common\Can 'Em All"
```

Copy `CanEmAllAP.dll`, `Archipelago.MultiClient.Net.dll` and `Newtonsoft.Json.dll` from `bin/Release/net6.0` to
`BepInEx/plugins/CanEmAllAP`.

## License
MIT. BepInEx (bundled in the "with-BepInEx" release zip) is LGPL-2.1.
