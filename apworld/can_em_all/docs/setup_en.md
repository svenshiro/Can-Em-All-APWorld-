# Can 'Em All setup

## Install
1. Download `CanEmAllAP-<version>-with-BepInEx.zip` from the releases page and extract it into your Can 'Em All folder
   (Steam: right-click the game > Manage > Browse local files). If you already have BepInEx 6 IL2CPP (be.793 or newer),
   use the plugin-only zip instead.
2. Start the game once. The first launch with BepInEx takes a while.

## Connect
1. In game, press **F8** to open the Archipelago panel.
2. Click each field and type (or paste with Ctrl+V): server (for example `archipelago.gg:12345`), slot name, password.
3. Press Enter or click Connect.

The settings are saved in `BepInEx/config/tyler.canemall.archipelago.cfg`. With AutoConnect on, the game connects by
itself a few seconds after starting.

Start a **new game** for each seed. If you load a save that already has skills bought, the mod asks before sending them
all as checks.
