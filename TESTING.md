# Things to test

Nothing below has been run in the game yet. Do them in order and stop at the first thing that looks wrong.

## Before you start

- **Back up your save.** Copy `...\userdata\<id>\1150640\remote\savegame.dat` somewhere safe. The plugin can write to it.
- Build is `Binaries\Debug\Plugins\Yu-Gi-Oh-MoreCards.dll` (plugin) and `Binaries\Debug\Tools\WolfEx.exe` (editor).
- Game folder needs an `Yu-Gi-Oh-Ex` folder with `cards.json`, `unlocks.json`, `packs.json` (examples are in
  `Yu-Gi-Oh_Loader Plugins\Yu-Gi-Oh-Cards\Yu-Gi-Oh-Ex\`) and the art file named in `cards.json`.
- The example cards use id **15000**. Ids must be 14969 to 19999.

## 1. Baseline (nothing new enabled)

- [x] Game starts, console shows no `FAILED` lines.
- [x] Trunk shows your normal cards, art loads, your new card shows art and can be clicked / dragged.
- [x] Empty Extra / Side deck slots do **nothing** on "Show Details" (same as vanilla).

## 2. Unlocks replace the defaults (`unlocks.json`, `"replaceDefaults": true`)

The game always uses `savegame-ex.dat` now (see 4), so your Steam save is untouched. Delete `savegame-ex.dat` to get a fresh profile.

- [x] Console shows `unlocks.json replaces the game's starting cards`.
- [x] On a **new** profile the trunk holds only the cards in `unlocks.json` (not the 149 starter cards).
- [x] Set `"replaceDefaults": false` and restart: the starter cards are back, plus yours.
- [x] A vanilla card id in `unlocks.json` (e.g. one of the starter cards from "Add from game data") shows the right copy count.

## 3. Packs (`packs.json`)

- [ ] Console shows `Loaded 1 pack change(s)` and `Pack 1_1: ... (1 added)`.
- [ ] Open shop pack `1_1` (Grandpa Muto) repeatedly. Your card should turn up eventually.
- [ ] A pulled custom card lands in the trunk (this needed a patch, so it is the main thing to check).
- [ ] Other packs are unchanged; the game does not crash opening or previewing packs.

## 4. Local save (always on)

No setting needed. Optional in `Config.ini` under `[Yu-Gi-Oh-MoreCards]`: `GameSaveName=` (file name) and `SeedFromGameSave=0` (start a new profile instead of copying the Steam save).

- [ ] `savegame-ex.dat` appears in the game folder (first run copies your Steam save).
- [ ] Changes made in game persist across restarts; the Steam `savegame.dat` does not change.

## 5. WolfEx

- [ ] Pick the game folder; all three tabs (New cards, Unlocks, Packs) load.
- [ ] Unlocks: "Add from game data" fills ~149 rows. Save, restart the game, check the trunk.
- [ ] Packs: pick a pack, add an id, Save all, restart, confirm the pack log line.
- [ ] New cards: change a stat, replace the art (png or jpg), Save all, restart.

## Known gaps (not bugs to report)

Duelist reward drops and battle packs are not supported yet.

## If something breaks, send me

The console output from launch until the problem, what you did, and the crash address if it crashed.
