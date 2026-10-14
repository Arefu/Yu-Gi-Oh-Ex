![YuGiOh Legacy of the Duelist Link Evolution](https://cdn.ygorganization.com/2020/02/Yugioh-Legacy-of-the-Duelist-Link-Evolution-Siliconera-2-800x400-1.jpg)

# Wolf 2 - Electric Boogaloo

Designed and tested with the latest build of [Yu-Gi-Oh Legacy of the Duelist Link Evolution](https://store.steampowered.com/app/1150640/YuGiOh_Legacy_of_the_Duelist__Link_Evolution/) on Steam. These tools have **not** been tested for the Nintendo Switch [version](https://www.nintendo.com/us/store/products/yu-gi-oh-legacy-of-the-duelist-link-evolution-switch/). They likely never will be since all of my effort is purely into the Windows executable for the game. You might be lucky to transfer the knowledge across but don't expect everything to work, especially not the plugins the loader injects.

Yu-Gi-Oh-Ex is a little play on Microsoft's convention of putting "Ex" at the end of a function that extends another one. When talking about the suite of tools I'd rather call it WolfX.

## Getting Started

You should always be using the latest commit that I have: issues are frequent and there is no real version checking to make sure everything still works. You will need .NET 8 and .NET 9 and the VC++ redistributables (2012 to latest).

1. Build the solution (`WolfX.sln`) in **Debug** (x64). Everything lands in `Binaries\Debug\`.
2. Launch `Binaries\Debug\Yu-Gi-Oh_Loader.exe`. It starts the game with the plugins next to it: every launch it points `Config.ini` (`[Yu-Gi-Oh-Core] PluginsPath`) at its own `Plugins` folder, so the Release loader runs the Release plugins and the Debug loader the Debug ones.
3. Turn plugins on and off in the game (Help & Options > Plugins) or in `Config.ini`.
4. Make content with **WolfX** (`Binaries\Debug\Tools\WolfX.exe`): File > Open the game folder.

Your save is never the Steam one: Yu-Gi-Oh-Core keeps it in `savegame-ex.dat` in the game folder (and `savegame-ex-N.dat` for the other save slots).

I would suggest checking the Project's [Wiki Page](https://github.com/Arefu/Yu-Gi-Oh-Ex/wiki) and the `docs\` folder: almost everything I know about the game is written down there.

## Features

Hey you, I see you looking at this page wondering what on Earth you're doing being dragged here by no doubt some random guy on a Discord saying "Hey bro, trust me it's good". Well I am sure it is.
To save time, and brain power remembering them all, here are the important parts.

### Tools (`Tools\`)

1. **WolfX** - the edit hub. It opens the game's data and edits it, and makes new content:
   - **Cards:** Card Manager (every card's properties, text, art, genres, related cards, same name, links, passwords), new custom cards with effects (a script language and drag-and-drop blocks), name sort, card ids.
   - **Campaign:** characters (portraits and story sprites too), decks (import / export `.ydk` and `.ydc`), the story editor (duels and their scenes, played back), new campaign series, tutorials.
   - **Shop:** the game's packs, new packs and new shop rows, unlocks.
   - **Look and sound:** sprite sheets (replace or add sprites), animlists, fonts, How to Play, strings and text tables, music and voice lines.
   - **Menus:** RIX pages and menus, designed on a canvas.
   - **Saves**, archives (`YGO_2020.dat`), deck files, and **File > Export as mod**.
2. **ModManager** - installs and orders mods (`<game>\Mods`).
3. **DuelIt** - reads `Duels.log` and replays recorded duels.
4. **PluginManifest** - writes a plugin's `<DLL>.json` (title, description, what it requires).
5. **Yami-Yugi** - a simple command line program that parses and extracts `YGO_2020.dat`.

### Where content goes

- **Changes the game's files can hold** go into the game's own formats, in the patch archive **`YGO_2020-Ex.dat`** (Yu-Gi-Oh-Core mounts it); the game's `YGO_2020.dat` is never written.
- **What the game can't hold** (custom cards, series past the game's 6, rows past its shop tabs...) goes to **JSON in `<game>\Yu-Gi-Oh-Ex\`**, which a plugin applies.
- **Mods** are folders in `<game>\Mods`, stacked at runtime.

### Plugins (`Yu-Gi-Oh_Loader Plugins\`, `Yu-Gi-Oh-GUI Plugins\`)

| Plugin | What it does |
|---|---|
| **Core** | Always on. The save redirect and save slots, the plugin list, game data loading (the Ex patch), the crash log, credits. |
| **RIX** | Always on. The in-game menus: new main menu buttons, RIX pages, the Plugins list, widgets for other plugins. |
| **More Cards** | Custom cards, unlocks, card genres, related cards and text tables. |
| **Effects** | Custom card effects, Fusion / Ritual / Synchro / Xyz / Link rules, and changed effects for game cards. |
| **Campaign** | Characters, decks, story duels and scenes, new tutorials, and new campaign series past the game's 6. |
| **Better Card Shop** | The Card Store (packs, every card, passwords), new packs and new shop rows. |
| **Music** | Your own music in place of the game's (per arena, per opponent, per story duel), and voice lines in duels. |
| **Tag Duel** | Local tag duels (2 vs 2) and handicap duels (1 vs 2, 2 vs 1) with solo life points. |
| **Anime Cards** | Makes the anime style cards draw properly. |
| **Patch Me Out** | Quick duels: skip rock-paper-scissors, choose who goes first, set the starting LP. |
| **Speed Hacks** | A faster game, no animations or movies. |
| **Console** | The log window (`console.log`). |
| **GUI** | The F1 window; plugins that draw windows need it. |
| **Funky** | My debug tools. |
| **Multiplayer** | Logs the online duel system, for reverse engineering. |

### Reverse engineering

The IDA database of the game, `YuGiOh.exe.i64`, is in the repository (Git LFS). It holds the names, types and comments the plugins are built on. It has its own license terms, see below.

## Switch

If you have a Nintendo Switch, I'm sorry, you're currently out of luck for the majority of things.
If I ever bother to pick up my Switch more I'll see if anything is possible, but currently you're limited to DAT/TOC modifications.
Even if I can get the core suite of tools compiled for Switch, I am not aware of any way to inject my own code into a running process on Switch like Detours does, so you're out of luck there.

## License

Free for personal, hobby, research and educational use under the [PolyForm Noncommercial License 1.0.0](LICENSE.md), with [an addendum](LICENSE-ADDENDUM.md): **no money of any kind may be made from anything in this repository** - no sales, donations, tips, sponsorships, paid access, commissions or monetised content. The IDA database (`YuGiOh.exe.i64`) also has [its own terms](LICENSE-IDB.md) on AI use. Versions published before 2026-10-11 were MIT. Third-party code in `Dependencies/` keeps its own licenses, and the game belongs to its owners.

ssjriou - "EH WHO CARES - I answered a question 8 months ago, I guess?"
Death- I don't mod LE anymore but will give you an answer that is three times longer than needed and make you forget what you asked in the first place
Very Tired Anna - We are gonna make Link Evolution great again and yugiboomers are gonna pay for it.
