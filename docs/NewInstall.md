# A new install, and turning things on and off

What happens from a stock game to a working Yu-Gi-Oh-Ex, which switches exist, and what a mod can and can't set. Written 2026-11-01
against a stock game folder (everything the suite had added was moved out first).

## 1. A stock game folder

A Steam install of Legacy of the Duelist: Link Evolution (app 1150640) holds only:

```
YuGiOh.exe  YGO_2020.dat  YGO_2020.toc  binkw64.dll  steam_api.dll  steam_api64.dll  steam_appid.txt  FLAVOR.txt
```

Steam > the game > Properties > Installed Files > **Verify integrity of game files** puts back any of these that were changed (a
swapped `YGO_2020.dat`, a patched exe). It does not remove files the suite added; those are listed in section 3.

Your Steam save is not in this folder (Steam keeps it under `Steam\userdata\`), and Yu-Gi-Oh-Ex never writes it.

## 2. From nothing to a working game

1. Install .NET 8, .NET 9 and the VC++ redistributables (2012 to latest).
2. Build `WolfX.sln`, **Debug | x64**. Everything lands in `Binaries\Debug\` (the loader, `Plugins\`, `Tools\`).
3. Start `Binaries\Debug\Yu-Gi-Oh_Loader.exe`. On its first run it:
    - finds the game from Steam's registry entry (or asks you for `YuGiOh.exe` once and remembers it);
    - creates `<game>\Config.ini` with `[Yu-Gi-Oh-Core] PluginsPath=` its own `Plugins\` folder (rewritten every launch);
    - lists every plugin DLL it finds in `[Yu-Gi-Oh-Core]` as `Name=0`, **off**, except Core and RIX, which are always on;
    - starts the game with Core and RIX only.
4. Core then creates, as they are needed: `savegame-ex.dat` (your save; the Steam save is never touched), `console.log` (once
   Console is on), `crash_log.txt` / `crash.dmp` (only after a crash), `<game>\Yu-Gi-Oh-Ex\` (when WolfX or a plugin writes content).
5. After the title screen comes Core's **save-select screen** (pick or make a save), then the main menu.
6. Switch plugins on in **Help & Options > Plugins** (or section 4), then restart the game through the loader.

So a first start is the stock game plus what Core and RIX always do (section 5).

## 3. What the suite adds to the game folder

| File / folder | Made by | What it is |
| --- | --- | --- |
| `Config.ini` | the loader, every plugin | the plugin list and every setting |
| `savegame-ex.dat`, `savegame-ex-N.dat` | Core | your saves (slot 1, slots 2-99) |
| `Yu-Gi-Oh-Ex\` | WolfX, plugins | your content (JSON, art, pages, music) and `saves.json` (save slot names) |
| `YGO_2020-Ex.toc/.dat` | WolfX | the game files WolfX changed (the patch archive); the real archive is never written |
| `YGO_2020\` | you | loose game files, only read with `LooseLoading=1` |
| `Mods\` | the Mod Manager | installed mods and `modlist.json` (the order, on / off) |
| `console.log`, `Duels.log` | Console, Multiplayer | logs |
| `crash_log.txt`, `crash.dmp` | Core | after a crash |
| `imgui.ini` | GUI | the F1 window's layout |

Deleting all of these gives back a stock folder (keep a copy of your saves and `Yu-Gi-Oh-Ex\`).

## 4. Plugins: on and off

Three places, all writing the same `[Yu-Gi-Oh-Core]` lines in `Config.ini`:

- **In the game:** Help & Options > Plugins. Takes effect at the next start.
- **WolfX:** Tools > Config Editor.
- **By hand:** `Yu-Gi-Oh-Media=1` (on) / `=0` (off). Plugins in `Plugins\YGO-Ex\` (the ones Core starts: GUI tools such as Funky,
  SpeedHacks) are written `YGO-Ex/Yu-Gi-Oh-Funky=1`.

```ini
[Yu-Gi-Oh-Core]
Yu-Gi-Oh-Core=1          ; always on (written as 1 whatever you put)
Yu-Gi-Oh-RIX=1           ; always on
Yu-Gi-Oh-Cards=0
Yu-Gi-Oh-Effects=0
Yu-Gi-Oh-Campaign=0
Yu-Gi-Oh-Media=0
Yu-Gi-Oh-AnimeCards=0
Yu-Gi-Oh-PatchMeOut=0
Yu-Gi-Oh-TagDuel=0
Yu-Gi-Oh-FM=0
Yu-Gi-Oh-MP=0
Yu-Gi-Oh-Console=0
Yu-Gi-Oh-GUI=0
YGO-Ex/Yu-Gi-Oh-Funky=0
YGO-Ex/Yu-Gi-Oh-SpeedHacks=0
```

Things that switch plugins **on** for you (never off):

- a plugin's manifest (`<DLL>.json`): `"enforced": true` = always on; `"requires"` = it doesn't load unless those are on too;
- `<game>\Yu-Gi-Oh-Ex\content.json` (WolfX writes it): the plugins your content needs;
- a switched-on mod: its `mod.json` `"requires"` and its own `Yu-Gi-Oh-Ex\content.json`. Missing plugins are named in a message box.

A plugin that is on but whose requirements are off doesn't load (the loader says why in its debugger output).

## 5. What the always-on plugins change, and how to turn it off

Core and RIX can't be switched off, so everything they change on their own has a setting. With only Core and RIX on, these differ
from the stock game:

| Change | Setting (`[Yu-Gi-Oh-Core]` unless noted) | Stock-like value |
| --- | --- | --- |
| Your save is `savegame-ex.dat`, not Steam's | `GameSaveName` | none: this is what keeps your Steam save safe |
| Save-select screen after the title | `SaveSlot=0` (pick) | `SaveSlot=1` (always slot 1, no screen) |
| The game keeps running when its window loses focus | `PauseInBackground=0` | `PauseInBackground=1` |
| Card Shop: buy many packs at once; the reveal flips the eight cards 0.12 s apart with no extra wait before the rare | `MaxPacksPerPurchase=999` | `MaxPacksPerPurchase=1` (also keeps the game's reveal timing) |
| Deck sizes | `MinMainDeck=40` `MaxMainDeck=60` `MaxExtraDeck=15` `MaxSideDeck=15` | these are the game's own |
| Duel format | `DuelFormat=master` | the game's own |
| Credits | Yu-Gi-Oh-Ex's thanks and the plugin list come before Konami's | none |
| Help & Options gains a Plugins entry (RIX) | none | none |
| Booster packs from `Yu-Gi-Oh-Ex\packs.json`, user decks past 32, card ids past 19,999 | only when there is content that uses them | no content = no change |

A stock-like `Config.ini` for testing the base game with the suite installed:

```ini
[Yu-Gi-Oh-Core]
SaveSlot=1
PauseInBackground=1
MaxPacksPerPurchase=1
```

Every other setting of every plugin (defaults and what they do) is in WolfX's Config Editor, which reads `Tools/WolfX/ConfigCatalog.cs`;
GLOSSARY.md says what each plugin is for.

## 6. For mod developers

A mod is a folder in `<game>\Mods\` (or a .zip the Mod Manager installs there). Full details: [Mods.md](Mods.md).

- **Plugins your mod needs:** list them in `mod.json` `"requires"`. When the player switches your mod on, the loader switches those on.
  A mod can't switch plugins off, and can't set the `Yu-Gi-Oh-*=` lines in `Config.ini` (use `requires`).
- **Settings your mod needs:** your own `Config.ini` next to `mod.json`, same layout as the game's. While your mod is on, its values win
  over the player's; switched off, the player's come back. This covers every setting in section 5 except `SaveSlot`, `GameSaveName`,
  `PluginsPath`, `ModConfig`, `ReplacedNotice` and all of `[Yu-Gi-Oh-MP]`. Examples:
    - a mod that wants one pack at a time (its own shop pacing): `MaxPacksPerPurchase=1`;
    - Speed Duel rules: `DuelFormat=speed`, `MinMainDeck=20`, `MaxMainDeck=30`, `MaxExtraDeck=5`.
  The player can refuse all mod settings with `ModConfig=0`.
- **Content:** your `Yu-Gi-Oh-Ex\` (JSON the plugins read) and `YGO_2020-Ex.toc/.dat` (changed game files). WolfX's File > Edit a mod
  edits a mod in place; File > Export as mod makes one from your workspace.
- **A clean slate:** `"exclusive": true` in `mod.json` loads your mod alone (no other mod, no player workspace, no loose files).
- **A new save:** `"newSave": "<tag>"` for overhauls whose saves don't mix with others (Core only loads slots made with the same tags).
- **Checking:** `console.log` lists every setting a mod set (`[Section] Key=Value (from mod '...')`); the Config Editor shows them in blue.

## 7. A from-nothing test

1. Stock folder (section 1), Steam's Verify done.
2. Start the Debug loader. Expect: a `Config.ini` with `PluginsPath` and every plugin `=0` but Core and RIX; the game starts; the save
   screen after the title; New Save; the main menu.
3. Card Shop: a pack you can afford more than once shows `«  ‹  1 pack  ›  »` in the buy box (section 5 to turn it off).
4. Help & Options > Plugins: switch on Console (and anything else), restart through the loader, check `console.log`.
5. Install a mod with the Mod Manager (WolfX > File > Open the Mod Manager), switch it on, start: its required plugins are on, its settings
   show in `console.log`.
6. Put the stock-like settings of section 5 in `Config.ini` and check the game plays as stock.
