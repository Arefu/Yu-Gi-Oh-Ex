# Yu-Gi-Oh-Ex Feature Glossary

Everything Yu-Gi-Oh-Ex (WolfX) can do, who does it, and how far along it is. It covers every plugin, tool, mod, content file, name and limit in the project, and ends with the open work and the known traps.

If you only want to *make* something, the [wiki guides](https://github.com/Arefu/Yu-Gi-Oh-Ex/wiki) are the place to start. This page is the map. The `docs\` folder has the deep detail, and each section here links to its doc.

Last updated 2026-10-22.

## Send someone here

Every heading on this page is a link you can paste (right-click the heading on GitHub, or use the anchors below). Common questions:

| They ask... | Send them |
|---|---|
| "What is all this? Where do I start?" | [How it fits together](#how-it-fits-together), then the [README](README.md#getting-started) |
| "What does plugin X do?" | [Plugins](#plugins) (each plugin has its own heading) |
| "What's this word / old name?" | [A to Z of names](#a-to-z-of-names), [Retired and renamed plugins](#retired-and-renamed-plugins) |
| "Does feature X work yet?" | Its row's status tag, and [Open work (TODO)](#open-work-todo) |
| "How do I make content?" | [WolfX](#wolfx-toolswolfx-binariesconfigtoolswolfxexe) and the [wiki guides](https://github.com/Arefu/Yu-Gi-Oh-Ex/wiki) |
| "How do I install or make a mod?" | [Mods](#mods), then [docs/Mods.md](docs/Mods.md) |
| "Do I have to edit Config.ini for this mod?" | No: [Mod settings](#mod-settings-configini) |
| "Which file does what?" | [Content files](#content-files) |
| "Why can't my card use id X?" | [Card ids and game limits](#card-ids-and-game-limits) |
| "How do I write card effects?" | [BOB and BAB](#bob-and-bab-scripting), [Effects](#effects-yu-gi-oh-effects) |
| "My change did nothing / it crashed" | [Known traps](#known-traps) |
| "I want to write a plugin" | [For plugin developers](#for-plugin-developers) |

## Status tags

| Tag | Meaning |
|---|---|
| **Working** | Confirmed in the game. |
| **Built** | Written and compiling, not confirmed in the game yet. Most of the project is here: expect bugs. |
| **Partial** | Some of it confirmed, some of it not. |
| **Parked** | Started, then put down on purpose. Don't build on it without testing first. |
| **Planned** | Wanted, not started. |
| **Retired** | Gone or merged into something else. Listed so old names still make sense. |

## Contents

1. [A to Z of names](#a-to-z-of-names)
2. [How it fits together](#how-it-fits-together)
3. [Plugins](#plugins)
   - [Loader](#loader-yu-gi-oh_loaderexe) · [Core](#core-yu-gi-oh-core) · [RIX](#rix-yu-gi-oh-rix) · [More Cards](#more-cards-yu-gi-oh-morecards) · [Effects](#effects-yu-gi-oh-effects) · [Campaign](#campaign-yu-gi-oh-campaign) · [Media](#media-yu-gi-oh-media) · [Tag Duel](#tag-duel-yu-gi-oh-tagduel) · [Multiplayer](#multiplayer-yu-gi-oh-mp) · [Forbidden Memories rules](#forbidden-memories-rules-yu-gi-oh-fm) · [Anime Cards](#anime-cards-yu-gi-oh-animecards) · [Patch Me Out](#patch-me-out-yu-gi-oh-patchmeout) · [Speed Hacks](#speed-hacks-yu-gi-oh-speedhacks) · [Console](#console-yu-gi-oh-console) · [GUI](#gui-yu-gi-oh-gui) · [Funky](#funky-yu-gi-oh-funky)
   - [Retired and renamed plugins](#retired-and-renamed-plugins)
4. [BOB and BAB (scripting)](#bob-and-bab-scripting)
5. [Tools](#tools)
6. [Mods](#mods)
7. [Content files](#content-files)
8. [Card ids and game limits](#card-ids-and-game-limits)
9. [Game systems we've mapped](#game-systems-weve-mapped)
10. [For plugin developers](#for-plugin-developers)
11. [Open work (TODO)](#open-work-todo)
12. [Known traps](#known-traps)

---

## A to Z of names

| Name | What it is |
|---|---|
| **AI deck** | Campaign feature: choose which deck the opponent plays in Campaign and Free Duel. |
| **Anime Cards** | Plugin that lays the card face out in the anime style (and the Forbidden Memories style). |
| **Arenas** | Duel backgrounds. Campaign can add new ones (`arenas.json`). |
| **BAB** | *Build-a-Block*: the drag-and-drop block editor for effects in WolfX. The main way to write effects. |
| **Ban Lists** | A mod holding every real TCG and OCG ban list as a single-player PD Limits mod (the online lists ship with Yu-Gi-Oh-MP). |
| **BetterCardShop** | **Retired.** The old Card Store plugin. The store is now a BOB page and booster packs moved to Core. |
| **BetterLoad** | **Retired.** Its jobs (archive name, loose files, the patch archive) were folded into Core. |
| **BOB** | The script language. It's used for card effects (EffectScript) and for in-game pages (page scripts). |
| **Booster Blast** | A mod with 105 booster packs and 16 structure decks built from real sets released after the game. |
| **Card Manager** | The WolfX page that edits every game card (properties, text, art, genres, related cards, same name, links, passwords, summoning). |
| **Card Store** | The in-game store (packs, buy any card, enter a password). It's a BOB page now. |
| **Cards (mod)** | `Mods\Cards`, the source of truth for the 4,413 cards released after the game. Every new card's effect is built and tested against it. |
| **content.json** | A file WolfX writes so the loader knows which plugins your content needs. |
| **Core** | The always-on base plugin: save, plugin list, data loading, mod settings, crash log, credits, limit breakers. |
| **Deck limits** | Core settings for the smallest and largest Main Deck and the largest Extra and Side Decks (they can only go down from 60/15/15). |
| **Duel format** | Core setting that switches on the engine's own dormant Speed Duel and Rush Duel rules (offline). |
| **DuelIt** | A tool that reads `Duels.log` and replays recorded duels with the real card art. |
| **Duels.log** | The duel recording written by the Multiplayer plugin's DuelRecorder. |
| **effectClone** | The cards.json key that gives a custom card an effect by borrowing a game card's effect handlers. |
| **EffectScript** | The formal name of BOB for effects (`EffectScript.g4` grammar, ANTLR). |
| **Ex patch** | `YGO_2020-Ex.toc/.dat`: the patch archive where WolfX saves changes to the game's own files. The game's `YGO_2020.dat` is never written. |
| **FM** | Forbidden Memories: the overhaul mod plus the `Yu-Gi-Oh-FM` rules plugin. |
| **Funky** | Debug and testing tools in an ImGui window (runs on GUI). |
| **Ghost ids** | Ids 14969-15234. The game has effect data for them but no cards. Never use them. |
| **GUI** | The F1 ImGui window host plus a read-only memory inspector. |
| **IDB** | `YuGiOh.exe.i64`, the IDA database of the game, in the repo (Git LFS) with its own license. |
| **Limit breaker** | A generic hook that lifts a fixed game limit (pack slots, card ids). These live in Core. |
| **Loader** | `Yu-Gi-Oh_Loader.exe`: starts the game and injects the plugins. |
| **Manifest** | `<DLL>.json` next to a plugin: title, description, requirements, settings, content files, what it replaces. |
| **Media** | Plugin for your own music, voice lines and mp4 videos. Was called Music. |
| **Mod settings** | A mod's own `Config.ini`. Core reads it over yours while the mod is on, so a mod never needs you to edit your settings. |
| **ModManager** | A tool that installs, orders and switches mods. |
| **More Cards** | Plugin for custom cards, unlocks, genres, related cards and text. Its source folder is `Yu-Gi-Oh-Cards`. |
| **Page engine** | RIX's version 2 pages: brand-new screens built from the game's own widgets. |
| **Patch Me Out** | Quick-duel tweaks: skip rock-paper-scissors, choose who goes first, starting LP. |
| **PluginManifest** | A tool that writes a plugin's manifest. |
| **RIX** | The game's own UI framework, and the always-on plugin that extends it with menus, buttons and pages. |
| **Save extension** | Extra data Core appends to `savegame-ex.dat` (tagged blocks such as `cards.high`, `campaign.series`, `rix.save`). |
| **savegame-ex.dat** | Your Yu-Gi-Oh-Ex save. The Steam save is never used after the first copy. |
| **Scratch-pool borrowing** | How cards with ids above 16383 play in duels: they borrow a free low id for one duel. |
| **Source-id lending** | How effect clones play in duels: they play under their effect source's id, so every engine rule for that card applies. |
| **Speed Hacks** | A faster game: game speed, no animations, no movies. |
| **Tag Duel** | Plugin for local 2 vs 2 and handicap duels. |
| **Vanilla** | The game as Konami shipped it. |
| **WolfEx** | **Retired** name. It was the content editor and is now part of WolfX (some code namespaces still say WolfEx). |
| **WolfX** | The edit hub app, and the name of the whole tool suite. |
| **Yami-Yugi** | A command-line tool that unpacks and repacks `YGO_2020.dat`. |
| **YgoMp** | The C# multiplayer server in `Server\`. |
| **Yu-Gi-Oh-Ex folder** | `<game>\Yu-Gi-Oh-Ex\`: new content as readable JSON (plus art). |

---

## How it fits together

```
Yu-Gi-Oh_Loader.exe ──starts──> YuGiOh.exe
        │
        └─ injects plugins (Binaries\<config>\Plugins\)
             ├─ Core   (always on: save, plugin list, data loading, Ex patch, packs)
             ├─ RIX    (always on: menus, buttons, pages, BOB page runtime)
             └─ the plugins you switch on (More Cards, Effects, Campaign, Media, ...)

WolfX ──writes──> YGO_2020-Ex.toc/.dat  changes to the game's own files (Core serves them)
      ──writes──> <game>\Yu-Gi-Oh-Ex\    new content as JSON (plugins read it)
      ──exports─> <game>\Mods\<id>\      mods (stacked at runtime, opt-in)
```

The rules behind that layout:

- **Changes the game's files can hold** go into the game's own formats, in the Ex patch. Deleting a game entry is a change too, so it goes there as well.
- **What the game can't hold** (custom cards, series past 6, shop rows past the game's tabs, more entries than a table allows) goes to JSON in `Yu-Gi-Oh-Ex\`. A plugin reads it and injects it in the game's own format at runtime.
- **Mods** are folders under `<game>\Mods\`, each with the same layout, loaded in order. Your own `Yu-Gi-Oh-Ex` and Ex patch always load last.
- Plugins stay **mod-agnostic**: a shared plugin only gains generic features any mod can use. Mod-specific logic lives in the mod's data or the mod's own plugin.
- **Feature logic goes in BOB** where it can (stores, prices, menus). Plugins provide the generic engine pieces.

Docs: [WolfXContent.md](docs/WolfXContent.md), [Mods.md](docs/Mods.md), [PluginManifests.md](docs/PluginManifests.md).

---

## Plugins

Plugin source is in `Yu-Gi-Oh_Loader Plugins\` (injected by the loader) and `Yu-Gi-Oh-GUI Plugins\` (started by GUI). Switch plugins on and off in game (Help & Options > Plugins), in WolfX's Config editor, or in `Config.ini` under `[Yu-Gi-Oh-Core]`.

### Loader (`Yu-Gi-Oh_Loader.exe`)

| Feature | Status |
|---|---|
| Starts the game and injects every switched-on plugin from its own `Plugins` folder (points `PluginsPath` at itself each launch, so Debug runs Debug plugins and Release runs Release). | Working |
| Finds the game: Steam's registry key, else an HKCU copy of it. If neither is set (a non-Steam copy, or Steam didn't record it), asks you to locate `YuGiOh.exe` with an open-file dialog and saves the folder to HKCU (no admin needed). | Built |
| Reads plugin manifests: a plugin with a missing requirement is *blocked* (not loaded, and the list says why). | Built |
| Applies mods: each mod's `requires` and `content.json`, with a message box naming missing plugins. | Built |
| `content.json`: switches on the plugins your WolfX content needs. | Built |
| Replaced plugins: never loads a plugin a manifest says was replaced, moves its on/off switch to the successor, and shows a one-time notice. | Built |
| `--wait <pid>` restart support (used by the in-game Plugins list to restart the game). | Built |

### Core (`Yu-Gi-Oh-Core`)

Always on. Kept small on purpose: the save, the plugin list, game data loading, mod settings, credits, save slots, a few light tweaks, and generic limit breakers and rule switches. Docs: [SaveSlots.md](docs/SaveSlots.md), [PluginManifests.md](docs/PluginManifests.md), [CustomScreens.md](docs/CustomScreens.md), [Packs.md](docs/Packs.md), [Mods.md](docs/Mods.md), [DuelFormat.md](docs/DuelFormat.md), [DeckLimits.md](docs/DeckLimits.md), [UserDecks.md](docs/UserDecks.md).

| Feature | What it does | Status |
|---|---|---|
| **Save redirect** | Uses `savegame-ex.dat` in the game folder instead of the Steam save. The Steam save is only ever copied in on request (see Steam-save seeding), never automatically. | Working |
| **Save slots** | Up to 99 slots (`savegame-ex-N.dat`), names and pictures in `saves.json`. A brand-new save-select screen (screen id 100) appears after the title screen, with a scroll bar, Delete, New Save and Change Picture. `SaveSlot=N` skips the screen. Volume is shared across slots. Back from the main menu returns to the slot screen. A new slot reuses the lowest free file number instead of climbing forever. | Working (the scroll bar's own clicks do nothing) |
| **Steam-save seeding** | Picking the empty primary slot offers to copy your Steam save (your vanilla progress) into it, or start fresh. With mods on it warns first that the save was made without them. Nothing is seeded automatically, so a deleted save stays deleted. | Built, untested |
| **New-save tags** | A plugin or mod can demand a fresh save (`"newSave": true` or a tag in its manifest or `mod.json`). Slots that don't match are shown in red with the reason, with a "play anyway" choice. | Built |
| **Save extension** | Tagged extra blocks after the game's 44,008 bytes (`Core_GetSaveExtension` / `Core_SetSaveExtension`), so plugins never need to change the game's save layout. Save-write filters (`Core_AddSaveWriteFilter`) can rewrite the save before it's written, with the checksum fixed. | Built |
| **Mod settings** | A mod's own `Config.ini` is read over yours while the mod is on (every plugin's settings, no plugin changes needed). See [Mod settings](#mod-settings-configini). | Built |
| **Card id limit breaker** | Card unlock table for ids up to 65,534 (the game's table stops at 19,999). Ids from 20,000 up are saved in the `cards.high` extension. | Built |
| **Booster packs** | `packs.json`: change the game's packs, add new packs into free pack slots, borrow pack art, unlock rules (`unlockWith`), new shop rows with names and banners, no pack limit. Packs the game's own rows can hold are written into the game's files by WolfX instead. | Built |
| **Plugin list and host** | Owns which plugins load (`[Yu-Gi-Oh-Core]` in `Config.ini`), the on/off toggles with dependency cascade, and starts the GUI-hosted plugins in `Plugins\YGO-Ex\`. C API in `Yu-Gi-Oh-Core.h`. | Working |
| **Game data loading** | Mounts the Ex patch (`PatchArchive`, default `YGO_2020-Ex`) as a second archive, serves loose files from `YGO_2020\` (`LooseLoading`), the archive name (`Archive`), file order (`FileOrder`), several game instances at once (`AllowMultiInstance`). Mod patches and loose folders are stacked in. | Built (the patch is used by many features; check it in game per feature) |
| **Credits** | Adds credits before Konami's: built-in thanks, `credits.json` sections, sections other plugins add (`Core_AddCredits`), then a plugin list. | Built |
| **Crash log** | `crash_log.txt` (module + offset, call stack) and `crash.dmp` in the game folder on a fatal crash. Since 2026-10-25 also on abort / `std::terminate` (uncaught C++ exception with its type and message) and stack overflow; `console.log` is flushed first (Console's `FlushAllLogs`), and a quiet exit (`ExitProcess`, `TerminateProcess` on itself, `RIX::App::RequestQuit`, closing the console window) is logged to `console.log` with its stack. | Working (the 2026-10-25 additions untested) |
| **Tweaks** | `PauseInBackground` (default off) and `JapaneseVersion`. `EngineRules2020` is still written but the game never reads the bit (see [Duel rules](#game-systems-weve-mapped)). | Built |
| **Deck size limits** | `MinMainDeck` / `MaxMainDeck` / `MaxExtraDeck` / `MaxSideDeck` (e.g. 20-30 / 5 for Speed Duel): patches the game's one legality check and the deck editor caps, and the numbers in the game's text. Limits only go down (a deck holds 60/15/15). [DeckLimits.md](docs/DeckLimits.md) | Built, untested |
| **Extra user decks** | Toon Jinzo's Yu-Gi-Oh-DeckSlots, now in Core (it replaces the plugin): more user deck slots (ids 1024 up) after the save's 32, as many as the save uses + 256 free (no fixed cap, so no deck is dropped), kept in the save extension `UserDecks` so they follow save slots; a DeckSlots `<save>-extradecks.dat` is imported once. `Core_GetExtraUserDecks` for plugins. [UserDecks.md](docs/UserDecks.md) | Built, untested |
| **Duel format** | `DuelFormat=speed / rush` switches on Konami's Speed and Rush Duel rules that are already in the duel engine but never enabled (LP, hand, 3 zones, no Main Phase 2, Rush draw-to-5, unlimited summons, no chains). Online: the host's **More Options** page, opened from the match settings screen (Yu-Gi-Oh-MP, lobby data `exformat`, deck sizes follow). The field still draws 5 zones. [DuelFormat.md](docs/DuelFormat.md) | Built, untested |

### RIX (`Yu-Gi-Oh-RIX`)

Always on, UI only. RIX is also the name of the game's own UI framework (screens, MenuKit menus, widgets, the DFX scene graph). Public API: `Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-RIX.h` (API version 7). Docs: [MenuSystem.md](docs/MenuSystem.md), [MenuFiles.md](docs/MenuFiles.md), [PageEngine.md](docs/PageEngine.md), [PageDesigner.md](docs/PageDesigner.md), [PageScript.md](docs/PageScript.md), [Widgets.md](docs/Widgets.md), [WidgetTracker.md](docs/WidgetTracker.md), [CustomScreens.md](docs/CustomScreens.md).

| Feature | What it does | Status |
|---|---|---|
| **Menu buttons API** | Add, remove, rename and re-point main menu and options menu buttons from any plugin. Buttons can run named actions. | Working |
| **Menu files** | `menus\*.json`: add buttons, rename or hide the game's own menu items, and give them actions (`goto` a screen, open a `page`, play a `video`, `reload`, call a plugin action). Edited in WolfX > Menus. | Working |
| **Plugins list** | An in-game list of plugins with on/off toggles (Core does the switching). Asks to restart when something changed. | Built |
| **Video Settings row** | An extra row on the game's Video Settings screen. | Working |
| **Version 1 pages** | `pages\*.json` opened on a borrowed screen: header, buttons, images, text, simple widgets. | Working (superseded by version 2) |
| **Page engine (version 2 pages)** | Brand-new pages on RIX's own screen (id 102) made only of the game's real widgets, lifted with their constructor, builder, update and input. Bindings (`var:`, `config:`, `game:`, `value:`), conditions and actions. | Partial |
| **Working page widgets** | Header, help bar text, MenuKit menu, volume bar, password digits, card details, dialogs, card grid, card box (pulled-cards box), deck view, progress bar, option line, pack wrapper, scroll bar, images and text. | Working (grid and box sizing is a little off; accepted) |
| **Built page widgets** | `tag` (text frame) and `spinner` (load spinner). | Built |
| **Page actions** | page, back, goto, call, quit, set, toggle, add, message, confirm, if, reload, video, giveDp/takeDp, giveCard/takeCard, give_deck, unlock/lock, unlockCharacter, sound. | Partial |
| **Game values** | `game:dp`, owned counts, card name/ATK/DEF/level/kind/rarity, deck names, free deck slots, passwords, medians, `{= maths}` with if/clamp/compare, `{x\|fallback}`. Plugins can add their own values (`RIX_RegisterPageValue`). | Partial |
| **Saved page variables** | `save:` variables kept in the save extension (`rix.save`), for things like "buy once". | Built |
| **Demo Page** | A main-menu button that walks through every wrapped widget (`menus\rix-demo.json`, copies in `docs/examples/rix-demo`). | Working |
| **Image and text nodes** | `RIX_AddImage` / `RIX_AddText` / `RIX_RemoveNode` for any plugin (negative width or height flips the image). | Working |

### More Cards (`Yu-Gi-Oh-MoreCards`)

Source folder `Yu-Gi-Oh-Cards`, built as `Yu-Gi-Oh-MoreCards.dll`. Docs: [CustomCards.md](docs/CustomCards.md), [CardGenre.md](docs/CardGenre.md), [RelatedCards.md](docs/RelatedCards.md), [CardLink.md](docs/CardLink.md), [CardSame.md](docs/CardSame.md), [CardKana.md](docs/CardKana.md), [TextTables.md](docs/TextTables.md).

| Feature | What it does | Status |
|---|---|---|
| **Custom cards** | `cards.json`: new cards with any id from 15,300 to 65,534, kept as-is (never remapped). Name, text per language, kind, stats (including `?` ATK/DEF), art (and an uncensored art), archetypes, password, genres, related cards, links, same name, sort name, index letters. | Working |
| **Exact card kinds** | Cards keep their true type line (`typeline`, for example Ritual Pendulum) next to the nearest kind the game has. | Built |
| **Game card overrides** | Change a game card's stats, art and more from `cards.json` without touching Konami's files. | Built |
| **Card pool** | `cardpool.json`: only the listed cards exist for the player (others are removed from the trunk, packs and store). | Built |
| **Unlocks** | `unlocks.json`: own at least N copies of any card; `replaceDefaults` skips the starter grants; `starter` deals a random starter deck from weighted groups on a new save. | Partial |
| **Archetypes** | Custom cards join game archetypes (so game support cards work with them), and brand-new archetypes get codes from 419 up. | Built |
| **Card genres** | `genres.json`: the genre bits used by the card details page and by the duel AI. | Built |
| **Related cards** | `relatedcards.json`: the deck editor's Related cards panel, for game and custom cards. | Built |
| **Text tables** | `text.json`: additions to the game's WORD and DLG text (card kinds, attributes, duel prompts), per language. | Built |
| **Name sort and search** | Custom cards get a proper name-order rank and are findable in the deck editor search. | Built |
| **Big ids in duels** | Cards above the duel engine's 14-bit limit (16,383) borrow a free low id for one duel (scratch-pool borrowing). | Working |
| **Source-id lending** | An effect clone plays under its effect source's game id for the duel, so every id-keyed engine rule (about 290 lists, the AI, win conditions) treats it as that card. | Working |
| **Effect only** | `as(...) effect_only` (`"effectOnly": true`): copy just a game card's effect; the card is not lent that id and keeps its own kind, stats and identity. `on <trigger>: as(...)` lets a monster use a Spell's or Trap's effect. | Built |
| **Summon from several zones** | `special_summon(hand or deck or grave or banished or extra, ...)`: one list over every zone named (the game's own banished / Extra Deck summon scans), picked and summoned by the Effects plugin's own step. Blocks: "Special Summon from any of", a box per zone. | Built |
| **Missing art** | Cards without art show a checkerboard instead of another card's art. | Built |
| **Custom cards in saved decks** | Custom cards in your saved decks are always owned (protects against save editors that strip them). | Built |
| **`PreserveUnavailableCards`** | Keeps cards from a mod that's switched off. | Built |

### Effects (`Yu-Gi-Oh-Effects`)

The card effect engine for new cards, and changed effects for game cards. The game's effects turn out to be table driven (four per-card tables of handler rows) plus about 290 id lists. Effects reuses the game's own machinery first and only writes new logic where it has to. Docs: [EffectSystem.md](docs/EffectSystem.md) (90 sections, read it first), [EffectLanguage.md](docs/EffectLanguage.md).

| Feature | What it does | Status |
|---|---|---|
| **effectClone** | A custom card borrows a game card's effect handlers, with parameters: draw, destroy-all and other filters, Deck search (by archetype or card), revive, LP, send to GY, targeted destroy. | Working |
| **Triggers** | Summon, Flip Summon, FLIP (by attack and by effect), destroyed by battle, sent to GY (from hand, Deck, field), leaves the field, ignition (with once per turn), Standby / End Phase handlers, attack declared, battle damage, named-card helpers. All 42 event types are identified. | Partial (summon, FLIP, battle destruction, GY, leave field, ignition confirmed; the rest built) |
| **Composed effects** | Multi-effect cards ("parts"), trigger from one card with the action of another (`actionFrom`), conditions (`require`, `if no_monsters`, `if controls`), costs (discard, Tribute, banish self, detach, pay LP), gain ATK, return to hand, special summon from hand/Deck/GY, add from GY, effects in the GY (`on in_grave`). | Partial |
| **Chains** | Steps before an action (draw/LP then destroy); our own resolver that searches, summons, sends to the GY and banishes (Deck / hand) with game primitives instead of copying a donor. | Partial (most chain test cards confirmed; send/banish steps built 2026-10-22, untested) |
| **Effect choice (stacking)** | Two effects of the same kind on one card use the game's own "choose an effect" prompt, with labels. The chosen effect travels in the effect record (word20 tag), so a Spell activated from the hand resolves the effect picked (issue #38, docs/EffectSystem.md section 97). WolfX notes a monster trigger on a Spell/Trap, several Activate effects (a choice), and a Field/Continuous/Equip source used as one of several effects. | Built; #38 fix 2026-10-28, tests pass, not run in a duel |
| **Choice bullets ("activate 1 of these")** | EffectScript `or` joins effects into one choice sharing one once-per-turn limit (`limitGroup`); a card gets up to 4 independent once-per-turn limits (engine classes 5-8). The card-text translator reads "[head:] [cost,] activate 1 of these effects" headers; BAB "or the next effect" box. docs/EffectSystem.md section 58. | Built 2026-10-25, tests pass, not run in a duel |
| **Once per turn: per copy / per name** | `once_per_turn(copy)` ("Once per turn: ...", engine classes 1-4, a marker on the card's zone) vs `once_per_turn` (per card name, classes 5-8); the translator reads "Once per turn, <head>". docs/EffectSystem.md section 59. | Built 2026-10-25, IDA-transcribed state-machine test passes, not run in a duel |
| **Whose phase triggers** | `opponent_standby_phase`, `each_standby_phase`, `opponent_end_phase`, `each_end_phase`: the engine's phase handler families, traced. docs/EffectSystem.md section 60. | Built 2026-10-25, IDA-transcribed phase-driver test passes, not run in a duel |
| **Own triggers (no donor)** | `"ownTrigger"`: the plugin's own effect row (action composed onto it) + its own phase offer (a virtual row after the phase table's last row calling the game's handler family); no game card borrowed. All phase triggers without an exact game match use it, and every `ignition` effect (a row in the field ignition table, offered by the game's own activation menu; section 96), and "When this card is Normal / Special Summoned" (offered from the summon evaluator with its own ref layout; section 99), and `on in_grave:` plus every self cost (`banish_self`, `discard_self`, `tribute_self` and the send / banish / Tribute-from-hand-or-field ones: the own row of the GY / hand / field table with the game's cost function in slot 3; section 102). docs/EffectSystem.md section 61. | Built 2026-10-25 (self costs 2026-10-30), tests pass, not run in a duel |
| **Donor-free actions** | Search (`search(deck or grave ..., N, X)`), add from the GY / banished, Special Summon from any zone, send / banish from a zone, "Special Summon this card from your hand", `revive(...)` (Monster Reborn's own functions, section 101), `draw(N)` and destroy / return / banish 1 target (section 103) run the game's own functions under the card's own id, with no game card's row (Reinforcement of the Army, Magnet Circle LV2, Unexpected Dai, Foolish Burial, Watch Cat, Monster Reborn, Rose Lover, Hecatrice, Planet Pathfinder, Pot of Greed, Remove Trap, Spiritualism, Legendary Knight Hermos retired). 83% of the Cards mod's compiled effect steps are donor-free (23% on 2026-10-28 morning). docs/EffectSystem.md sections 98-103. | Built 2026-10-28 / 2026-10-30, tests pass, not run in a duel |
| **Master Duel engine** | Offline duels played on Master Duel's own duel engine (`Plugins\Yu-Gi-Oh-Core\duel.dll`, `[Yu-Gi-Oh-Core] MasterDuelEngine=1`): Core loads it, gives it the game's cards (MoreCards' cards by their Konami id, `"konamiId"` in cards.json) and redirects duel start, the tick, the screen's state and its commands to it; duel.dll animates through the game's own animation calls. The Cards mod's new cards play with Konami's own effects. A single-effect clone plays AS its source (`Core_EngineLend`, any card the engine knows). Card-name declarations open the game's list picker with the legal names. Yu-Gi-Oh-Effects requires it (refuses to load without it) and hooks its effect row lookup: raw-row effects whose slots have duel.dll twins (`MasterDuelFunctionMap.h`, game -> duel.dll function map; `Core_EngineToDisplay` for the ids) run on it, the rest of its composed effects are not ported yet and log why (docs/Overnight-2026-10-30.md section 6b). Tag / online / tutorial duels stay on the game's engine. docs/MasterDuelEngine.md. | Built 2026-10-30, experimental; duels run in the game (user 2026-10-30), lending / name picker untested |
| **Summon conditions** | Conditions on how a card was summoned (from the GY, by effect, Xyz and so on). | Built |
| **Negation** | `negate(...)`: negate activations through the game's negation table. | Built |
| **Continuous locks** | `lock(...)`: Jinzo / Royal Decree / Skill Drain style locks and narrower monster locks. | Built |
| **Equips and boosts** | `equip(atk, def)` and stat boosts through the game's ATK/DEF tables. | Built |
| **Fusion recipes** | Fixed materials, or generic ones ("Dragon", "DARK", "level>=5", archetypes, "tuner", "non-X", any-of). 246 of 250 new Fusions have recipes written from their text. | Built |
| **Ritual** | Ritual Monster to Ritual Spell pairs, beyond the game's 96-row table. Custom Ritual Spells use `as("Black Luster Ritual")`. | Built |
| **Synchro / Xyz / Link** | Material rules (counts, tuner and non-tuner codes, Link materials, named-card materials). Custom archetypes as materials and 2-Tuner Synchros (`"tuners": 2`) added 2026-10-23, untested. | Built |
| **Game card effect overrides** | `effects.json` with `"overridden": true`: a game card gets a different effect (or none). | Built |
| **`summoning.json`** | Summoning rules for game cards, edited from the Card Manager. | Built |
| **Tracing** | An empty `trace.txt` next to the plugin turns on detailed effect tracing. | Working |

### Campaign (`Yu-Gi-Oh-Campaign`)

Characters, decks, story duels and scenes, tutorials, campaign series and duel setup. Docs: [Characters.md](docs/Characters.md), [Decks.md](docs/Decks.md), [StoryDuels.md](docs/StoryDuels.md), [StoryScenes.md](docs/StoryScenes.md), [Tutorials.md](docs/Tutorials.md), [CampaignSeries.md](docs/CampaignSeries.md), [CampaignOptions.md](docs/CampaignOptions.md), [Arenas.md](docs/Arenas.md).

| Feature | What it does | Status |
|---|---|---|
| **Characters** | New and changed duelists (`characters.json`, or written into the game's files when they fit): portrait, story sprite, bio, series tab, arena, deck. `hideEmptySeries`, `seriesNames` for Free Duel tabs. | Built |
| **Decks** | New and changed decks (`decks.json` or the game's files): owner, signature card, cards, unlock state. `random` decks re-dealt from weighted pools; `hideGameDecks` hides the game's deck recipes. | Built |
| **Story duels and scenes** | New and changed story duels and their intro, win and lose scenes. Scenes can play videos, voice lines and sounds. A story duel can play as a tutorial. | Built |
| **New campaign series** | Series past the game's 6 (`campaigns.json`), with logos, unlock rules (`after` a series), arrow scrolling, overall completion, progress kept in the save extension. `hiddenSeries` hides game series. | Built |
| **New tutorials** | Tutorials numbered 27-99 from `tutorials\*.json` appear in Help > Tutorial, with their own done marks. | Built |
| **AI deck** | Campaign and Free Duel ask for Your Deck, then the Opponent's Deck (any deck). In Free Duel the opponent shows the picked deck's own duelist, as the game does for your deck; the campaign keeps the story's character. Duelist Challenges use the challenge deck unless `[Yu-Gi-Oh-Campaign] ChallengePickedDeck=1` (then the picked deck, same duelist; a win still clears the challenge). | Built |
| **New arenas** | `arenas.json`: new duel backgrounds (a picture or an animated animlist folder). | Built |
| **Per-duelist rules** | `duelistrules.json`: starting LP against a given opponent. | Built |
| **Campaign progress** | `progress.json`: unlock every character, and switch modes on (Duelist Challenges, Battle Packs, Card Shop). | Built |

### Media (`Yu-Gi-Oh-Media`)

Was Yu-Gi-Oh-Music. Plays its own audio (miniaudio) and video (Windows Media Foundation) alongside the game's Wwise and Bink. Docs: [MediaPlugin.md](docs/MediaPlugin.md), [AudioSystem.md](docs/AudioSystem.md).

| Feature | What it does | Status |
|---|---|---|
| **Music** | `music.json`: your mp3/wav/flac in place of any game music slot, or a different game track, per arena, per opponent and per story duel, with LP-pinch music. Follows the game's music volume. | Built |
| **Voice over (VOX)** | `voices.json`: characters say lines on duel events (duel start, turn start, battle phase, attack, direct and lethal attack, damage, low LP, summon by card and by method, card in hand, activate, set, destroyed, win/lose with reason). No-repeat rules (once per duel or turn, shuffle bags, quiet gap, chance), music ducking, result line before the finish video. | Built |
| **Videos** | `videos.json` and `movies\`: mp4 in place of any `.bik`, summon cut-ins for any card, full-screen videos at startup and before/after story duels, a RIX `video` action, and a scene step. Input is blocked while a full-screen video plays. | Built |

### Tag Duel (`Yu-Gi-Oh-TagDuel`)

Needs RIX. Uses the engine's own tag mode (teams share one field by seat parity). Docs: [MultiplayerSystem.md](docs/MultiplayerSystem.md) (tag sections).

| Feature | What it does | Status |
|---|---|---|
| **Local tag duel** | Tag Duel button > Local: 2 vs 2 with your partner and both opponents picked as characters on the Free Duel screen, a deck for each seat, and a review step before "Let's Duel!". Each seat shows its deck's duelist (the picked character when the deck has none). Lasts one duel: the result screen or the main menu turns tag off again, so the next Free Duel is a normal one. | Partial |
| **Seat controllers** | Each of the 4 seats can be AI or Human (hot-seat). | Built |
| **Handicap duels** | 1 vs 2 and 2 vs 1, with solo life points (8,000 / 12,000 / 16,000 / 24,000). | Built |
| **Partner decks** | Separate or shared partner decks (`DeckMode`, `PartnerDeck`, `OpponentPartnerDeck`). | Built |
| **Online co-op tag** | You and a friend vs AI over the game's online mode. | Parked (not done) |

### Multiplayer (`Yu-Gi-Oh-MP`)

Needs RIX. The game's Multiplayer menu is Konami's real Steam online mode (lobbies plus a lockstep duel event stream), and it's fully mapped. Docs: [MultiplayerSystem.md](docs/MultiplayerSystem.md), [MultiplayerService.md](docs/MultiplayerService.md), [StatsAndMatchResults.md](docs/StatsAndMatchResults.md), [DuelIt.md](docs/DuelIt.md).

| Feature | What it does | Status |
|---|---|---|
| **Duel recorder** | Writes `Duels.log`: every duel's events, readable and replayable in DuelIt. | Working |
| **Engine logging** | About 150 passthrough hooks that log the online and duel-flow functions for reverse engineering. | Working |
| **Connect to Server** | Multiplayer > Connect to Server: sign in (claim code or Steam), get the server's ban list, stream duels live for tracking and stats. | Built |
| **More Options page** | Casual hosting: a "More Options" button beside Create opens a page with the duel format (a < value > row like Video Settings') and the ban list button. The pages (`mp-match-settings.bob`, `banlists.bob`) ship with Yu-Gi-Oh-MP (source `Content\pages`, built to `Plugins\Yu-Gi-Oh-MP\pages`); a copy in `Yu-Gi-Oh-Ex\pages` overrides them. Ranked gets none (fixed rules). | Built, untested |
| **Ban lists online** | Game / Off / a custom list per lobby (set on the More Options page), sent to joiners through lobby data; a notice for players without the plugin. | Built |
| **Ban list picker** | Pick any of the 170 lists Yu-Gi-Oh-MP ships (plus your own in `Yu-Gi-Oh-Ex\banlists` and any mod's) by TCG/OCG, era and year (the setting screens' < value > rows, plus a Page row, on the left) from a BOB page; the lists are on the right, 8 a page, the one in use ticked (the game's checkmark icon); the help bar's "3 Preview" opens a page with the list under the cursor in the deck editor's deck panel (rows titled Forbidden / Limited / Semi-Limited, with counts), the hovered card's picture and text sliding out from the left edge (Back / right-click puts it away); Game's list and No list are its first rows; "No ban lists found." when there are none. | Built, untested |
| **No ban list** | "No list" in the ban list picker (More Options > Ban list). | Built |
| **Anti-cheat helpers** | Environment checks and a duel state hash. Whether the hash matches across two clients is not verified. | Built |

### Forbidden Memories rules (`Yu-Gi-Oh-FM`)

The duel rules for the Forbidden Memories mod. Work in progress; it logs every step. Docs: the mod's `README.txt`.

| Feature | What it does | Status |
|---|---|---|
| **Fuse from the hand** | A Fuse command on hand monsters, FM-style chains (each pair fuses or the old card is discarded), queued cards marked, fusion animation, Summon or Set finishes. Materials are removed from the duel. | Working |
| **No Tributes** | `NoTributes`: any monster can be Normal Summoned. | Built |
| **Fusions in the Main Deck** | `FusionsInMainDeck`: Fusion monsters can be summoned from the hand. | Working |

### Anime Cards (`Yu-Gi-Oh-AnimeCards`)

Makes the anime-style card frames draw properly, and lays out card faces your way. Docs: [CardRendering.md](docs/CardRendering.md).

| Feature | Status |
|---|---|
| Position and size the ATK/DEF text, attribute, stars, name, Spell/Trap icon and card art (about 30 settings, all in WolfX's Config editor). | Working |
| `HideFieldStats` / `HideLevelBadge`: hide the stat plate under monsters on the field. | Built |
| FM layout options (labels, right-aligned stars, attribute icon for Spells and Traps). | Built |

### Patch Me Out (`Yu-Gi-Oh-PatchMeOut`)

| Feature | Status |
|---|---|
| `NoJanken`: skip rock-paper-scissors offline (DP and unlocks still count). `JankenFirst`: random, P1 or P2. Online duels still play it. | Built |
| `StartingLP`. | Built |

### Speed Hacks (`Yu-Gi-Oh-SpeedHacks`)

A GUI plugin: `Speed` (default 2), `NoAnimations`, `NoMovies`. Status: Working. `SkipLogos` (default 0) skips the start-up logos (timed screens 1-4, nothing loads behind them) straight to the title, with a black one-frame fade instead of the logo's red one; the title still waits for Play. Status: Built 2026-10-30, untested.

### Console (`Yu-Gi-Oh-Console`)

The log window and `console.log`, filtered by `LogLevel`. Other plugins can write split logs (such as `Duels.log`). Steam's own console noise is dropped. `FlushAllLogs` (exported) writes out every log at once; Core's crash handler calls it so a crash's log ends at the real last line. Status: Working.

### GUI (`Yu-Gi-Oh-GUI`)

The F1 ImGui window and the one DirectX hook other plugins share (plugins that draw windows require it). Also a **read-only Inspector**: players 1-4 (tag partners too), game decks, named globals, a hex memory view, and exporting decks as `.ydc`. Status: Working.

### Funky (`Yu-Gi-Oh-Funky`)

Debug and test tools on GUI. Status: Partial (some widgets are known broken and left alone).

- Debug Tools window: save, screens, main menu, duel.
- Duel test bench: test hands (`DuelTestHand`), bulk add by id or name, put an archetype on top of the Deck, preset Fusion tests.
- UI tab: fire safe duel animations by id.
- UI Witchcraft test bench.

### Retired and renamed plugins

| Old name | What happened |
|---|---|
| Yu-Gi-Oh-BetterCardShop | Retired 2026-10-19. The Card Store and prices are BOB pages (`docs/examples/card-store`); booster packs moved to Core. |
| Yu-Gi-Oh-BetterLoad | Merged into Core (archive name, loose files, the Ex patch). |
| Yu-Gi-Oh-SaveMe | Merged into Core (save redirect). |
| Yu-Gi-Oh-Music | Renamed Yu-Gi-Oh-Media. |
| Yu-Gi-Oh-Cards, Yu-Gi-Oh-ZCards | Now Yu-Gi-Oh-MoreCards. |
| Yu-Gi-Oh-Tracer | Retired (debug only, no longer needed). |
| RIX Plugin Settings screen (id 101) | Removed. Plugin settings are edited in WolfX, not in game. |
| PatchMeOut FreeStore / NoBan, crash log | FreeStore removed; NoBan moved to Multiplayer; the crash log moved to Core. |

WolfX ("Retired plugins...") and ModManager move leftover DLLs to `Plugins\_retired`.

---

## BOB and BAB (scripting)

**BOB** is the script language. **BAB** (Build-a-Block) is the block editor on top of it. Every BOB feature for effects gets a BAB block too.

| Use | What it does | Status |
|---|---|---|
| **Effect scripts** | Card effects as text (`EffectScript.g4`, compiled by WolfX to `effectClone` data). Triggers, costs, conditions, actions, `as(...)` to copy a card, `equip`, `negate`, `lock`, `hand_limit`, `aura` (continuous ATK/DEF; `per <count>` = "for each"), `while <tests>:` (a condition on aura / protection / attack_directly: you control / in GY / in hand / banished counts, materials, Defense Position, LP), `indestructible` (cannot be destroyed by battle / effects), `untargetable` (by effects / for attacks), `unaffected` (by Spell / Trap / monster effects), `name_as` (this card's name becomes ...), `only_one` (you can only control 1), `attack_directly`, `once_per_duel` / `uses_per_turn(N)`, `no_special_summon` (the turn you activate this, or `resolved`: for the rest of the turn after it resolves), `if controls(card where name = N)`, `grave_end_phase` / `banished_end_phase` / `damage_step_battles` / `damage_step_attacks` triggers, `if battle_phase:` (whose Battle Phase), `if lp_at_most(N):` / `if lp_lower:`, `cost send_self` / `send_self_field` (send this card from the hand / field to the GY), `banish_self_hand` / `banish_self_field` / `tribute_self_hand`, "from your hand or field" costs (two effects joined with `or`), `banish_grave(N, X)` / `banish_self_and(N, X)` (banish cards from the GY as a cost), `discard(N, X)` / `tribute(N, X)` / `send_hand(N, X)` / `reveal(X)` / `send_hand_field(X)` (pick costs with no donor card: the game's own cost machines run as the card, section 90), watcher triggers `card_destroyed` / `card_left_field` / `card_sent_to_grave` / `card_banished` (ANOTHER card destroyed / left the field / sent to the GY / banished, from the field or `in_grave`, section 91), target verbs `change_position` / `set_face_down` / `take_control` / `negate_effects` / `change_atk` (the game's own target machines and result markers, section 92), phase windows `if main_phase / opponent_main_phase / main_or_battle_phase / your_turn / opponent_turn / battled` joined with `and`, Battle Phase triggers `battle_phase_start` / `battle_phase_end` (section 93), `fusion_summon(..., banish_materials)` and `xyz_also(...)` (alternative Xyz Summon, section 94), reveal-this-card costs (read as hand effects), labels. | Partial |
| **Effect blocks (BAB)** | The same effects as drag-and-drop blocks, in sync with the script; the first tab on the Effects page. | Built |
| **Card text translator** | Writes effects from a card's English text where every part is understood; also writes Fusion recipes and Ritual pairs. Headless: `--attach-effects`, `--attach-summoning`. | Partial (whole translations only) |
| **Effect library** | The game's own cards written in BOB (about 1,170), browsable by what they do and by script action; editable. Since 2026-10-31 also **every card Master Duel's engine plays** (13,571 in all, the post-game cards included) as `as(<id>)` under "Every card (Master Duel engine)": under that engine a custom card plays as its source (Core `Core_EngineLend`), so every effect, specially handled ones included, runs. Made by Tools/Scripts/make_full_effect_library.py. | Built; lending untested in a duel |
| **Page scripts** | `pages\<name>.bob` compiled to version 2 RIX pages (and main-menu buttons). Decompiles back exactly. Headless: `--compile-page`, `--decompile-page`. | Working |
| **Card Store** | The example store made entirely in BOB: packs, buy any card (prices from formulas in `card-store-prices.bob`), enter a password, Booster Blast structure decks. | Working |

Docs: [EffectLanguage.md](docs/EffectLanguage.md), [PageScript.md](docs/PageScript.md), [CardStore.md](docs/CardStore.md), `docs/effect-scripts/`, `docs/examples/`.

---

## Tools

### WolfX (`Tools\WolfX`, `Binaries\<config>\Tools\WolfX.exe`)

The edit hub. Open the game folder (or an extracted folder, or a mod) and every page reads the same open game data. Changes to game files go to the Ex patch, new content to JSON. Every page that adds or edits can also delete, game entries included (with warnings about what uses them). There's a wiki guide for every page; the README lists them.

| Group | Pages |
|---|---|
| **Cards** | Card Manager · New cards · Effects (BAB / script, game cards I changed) · Effect library · Card genres · Related cards · Card links · Card ids · Name sort · Forbidden & Limited (also MP ban lists) |
| **Campaign** | Characters · Decks (import / export `.ydk` and `.ydc`) · Campaign series · Arenas · Story editor (duels and scenes with playback) · Tutorials (visual and script, frame playback) |
| **Shop** | Packs (incl. battle pack slots, shop rows) · New packs · Unlocks |
| **Text** | Text tables · Strings (and credits) · How to Play (visual and script) |
| **Art & menus** | Sprite sheets (replace or add sprites) · Animlists · Fonts · Pages (designer) · Page scripts (BOB) · Menus |
| **Sound** | Music · Voice Over · Videos |
| **Files** | Game files & loading · Archives (.zib) · Deck files (.ydc) · Save editor · Config editor |

Config editor: a setting put back to its default is taken out of `Config.ini` on Save (unset = vanilla), and "Reset plugin" puts all of one plugin's settings back.

Also: File > Export as mod / Edit a mod (only ever opened on purpose: WolfX starts on your own files, never in a mod) / Open the Mod Manager, File > Export changed game files, Restore original archive, pop-out previews, the vanilla-style card preview, self-update from GitHub releases (Tools > Check for updates, Help > About), "Retired plugins...". Headless switches: `--page`, `--card`, `--render-pages`, `--compile-page`, `--decompile-page`, `--attach-effects`, `--attach-summoning`.

Docs: [WolfXContent.md](docs/WolfXContent.md), [EditorSafety.md](docs/EditorSafety.md), [WolfX-Tutorial-Guide.md](docs/WolfX-Tutorial-Guide.md).

### Other tools

| Tool | What it does | Status |
|---|---|---|
| **ModManager** (`Tools\ModManager`) | Install, order and switch mods (opt-in), shows leftover retired plugins. | Built |
| **DuelIt** (`Tools\DuelIt`) | Replays `Duels.log` with game art, hover cards, uncensored toggle. | Working |
| **PluginManifest** (`Tools\PluginManifest`) | Writes a plugin's `<DLL>.json`. | Working |
| **Yami-Yugi** (`Tools\Yami-Yugi`) | Unpack and repack `YGO_2020.dat` from the command line (streams; about 40 MB of memory). | Working |
| **Scripts** (`Tools\Scripts`) | Generators for the Cards and Booster Blast mods, card and effect status lists (`docs/Card Lists`), the missing-art image, the Cheat Engine table (`YuGiOh.CT`). | Working |
| **File Type Libraries** | One C# library per game file format, all byte-exact round trips (BND, CARD_*, CharData, DeckData, Dfymoo, Animlist, DuelData, FontBin, HowToPlay, PackDef, ScriptData, TextTable, Tutorial, YDC, ZIB (and extracted zib folders), savegame, and more). The tools share them. | Working |
| **YgoMp server** (`Server\`) | The .NET multiplayer server: identity, ban lists, live duel tracking, stats. Self-host first, a cheap cloud host later. | Built (tested by simulating the client) |

---

## Mods

A mod is a folder in `<game>\Mods\<id>\` with a `mod.json`, and any of `Yu-Gi-Oh-Ex\` (JSON content), `YGO_2020-Ex.toc/.dat` (a patch), `YGO_2020\` (loose files), `Plugins\` and `Config.ini` (settings). `Mods\modlist.json` holds the order (later wins) and on/off. Mods are **opt-in**: a new mod starts switched off. An **exclusive** mod (`"exclusive": true`) hides everything else, for total conversions. Docs: [Mods.md](docs/Mods.md).

Mods in this repo (`Mods\`):

| Mod | What it adds | Status |
|---|---|---|
| **Cards** | The 4,413 cards released after the game (from YGOPRODeck), including 53 tokens, with fixed ids, art, archetypes, Fusion recipes and Ritual pairs. The source of truth for new cards. | Built |
| **Cards - Unlocked** | Owns every card from the Cards mod. Off by default. | Built |
| **Booster Blast** | 105 booster packs (whole real sets, by year) and 16 structure decks with exact lists, sold from the Card Store's Structure Decks page. Needs Cards. | Built |
| **Ban Lists** | `PD Limits\TCG` / `PD Limits\OCG` single-player mods (Yugi-Kaiba Format included). The online lists and picker page moved into Yu-Gi-Oh-MP (`Content\banlists`, `Content\pages`) on 2026-10-25. | Built |
| **Forbidden Memories** | An exclusive overhaul: FM's 722 cards with FM stats and art, FM fusions, FM duelists as new characters with their faces and deck pools, random FM starter, FM card frames. Needs Yu-Gi-Oh-FM. | Partial |
| **Anime Frames** | Anime-style card frames. | Working |

`Mods\_downloads` is a download cache for the generator scripts, not a mod.

### Mod settings (Config.ini)

A mod can bring the plugin settings it needs in its own `Config.ini`, so players never edit theirs. Anime Frames uses it to move the ATK/DEF text and the art onto its frames; Forbidden Memories sets the whole FM card layout and the FM rules. Status: **Built**.

- Same `[Section] Key=Value` layout as the game's `Config.ini`. While the mod is on, its values win over yours; between mods, the later one wins. Switch the mod off and your own values are back.
- Works for every plugin's settings with no plugin changes: Core answers the Windows settings functions the plugins already use.
- A mod can't switch plugins on or off (that's what `requires` is for), change which save is used, or touch `[Yu-Gi-Oh-MP]` (the server and sign-in).
- `[Yu-Gi-Oh-Core] ModConfig=0` in your own `Config.ini` turns mod settings off.
- `console.log` lists what each mod sets. WolfX's Config Editor shows overridden settings in blue, and the Mod Manager lists a mod's settings.
- To make one: WolfX > File > Edit a mod > Tools > Config Editor edits that mod's `Config.ini`.

Docs: [Mods.md](docs/Mods.md) "Settings".

---

## Content files

Everything in `<game>\Yu-Gi-Oh-Ex\` (or a mod's `Yu-Gi-Oh-Ex\`), and who reads it.

| File | Read by | What's in it |
|---|---|---|
| `cards.json` | More Cards, Effects | Custom cards and game card overrides, with everything per card (effects, recipes, genres, related, links, password...). |
| `unlocks.json` | More Cards | Owned copies, starter grants, random starter. |
| `cardpool.json` | More Cards | The only cards that exist for the player. |
| `genres.json`, `relatedcards.json` | More Cards | Genres and related cards for game cards. |
| `text.json` | More Cards | WORD/DLG text additions. |
| `Archetypes.json` | Tools, More Cards | Archetype names (per language) and new archetype codes. |
| `effects.json` | Effects | Game card effect overrides. |
| `summoning.json` | Effects | Fusion / Ritual / Synchro / Xyz / Link rules for game cards. |
| `characters.json`, `decks.json`, `storyduels.json`, `storyscripts.json` | Campaign | New characters, decks, story duels and scenes. |
| `campaigns.json` | Campaign | New and hidden campaign series. |
| `tutorials\*.json` | Campaign | New tutorials (and `done.json` per save). |
| `arenas.json`, `duelistrules.json`, `progress.json` | Campaign | New arenas, per-opponent rules, campaign progress. |
| `music.json`, `voices.json`, `videos.json`, `movies\`, `voices\` | Media | Music, voice lines, videos. |
| `menus\*.json` | RIX | Menu buttons and edits. |
| `pages\*.json`, `pages\*.bob` | RIX (JSON), WolfX (BOB source) | In-game pages. |
| `packs.json` | Core | Pack changes, new packs, shop rows. |
| `credits.json` | Core | Extra credits. |
| `saves.json` | Core | Save slot names, pictures, tags. |
| `banlists\**\*.json` (also `<mod>\banlists\`) | Multiplayer | Your own ban lists for online play (WolfX Save as MP list); the 170 defaults ship in `Plugins\Yu-Gi-Oh-MP\banlists`. |
| `fm.json` | Yu-Gi-Oh-FM | Forbidden Memories data (fusions, duelists, starter). |
| `content.json` | Loader | Which plugins this content needs. |

---

## Card ids and game limits

| Range / limit | What it is |
|---|---|
| 3900-14968 | The game's card ids. About 900 of them map to no card but still have data in the exe: treat them as used. |
| 14969-15234 | Ghost ids: effect data, no cards. Never use. |
| 15300-19999 | Your own custom cards. |
| 21000-21099 | Tokens from the Cards mod. |
| 21100+ | Cards from the Cards mod. |
| 30000+ | Forbidden Memories cards the game lacks (30000 + FM number). |
| 65534 | Highest custom id (65535 means "no card"). |
| 16383 | The duel engine keeps 14 bits of an id. Higher cards borrow a low id per duel. |
| 60 / 15 / 15 | Largest Main / Extra / Side Deck. Core's deck limits can lower them (and the 40-card minimum), never raise them. |
| 128 pack slots | About 92 are free; new packs share them per shop row, so there's effectively no pack limit. |
| 6 campaign series | Hard-wired in the game; Campaign adds more through a borrowed slot. |
| 99 save slots | Core's save-select screen. |
| Screen ids | 100 = save select (Core), 101 = free, 102 = RIX page engine. The main menu is screen 8; 6 is the sign-in screen. |

Docs: [CustomCards.md](docs/CustomCards.md), [EffectSystem.md](docs/EffectSystem.md) section 23, [`docs/Card Lists`](docs/Card%20Lists).

---

## Game systems we've mapped

What's been reverse engineered (names, types and comments are in the IDB), useful when you build on the game.

| System | Where it's written up |
|---|---|
| Effects: per-card handler tables, id lists, event types 1-42, triggers, negation, locks, chains, ATK/DEF tables, effect choice | [EffectSystem.md](docs/EffectSystem.md) |
| Fusion / Ritual / Synchro / Xyz / Link material codes and tables | [EffectSystem.md](docs/EffectSystem.md) sections 36, 40-42 |
| Card faces: how a card is drawn (400x580 texture), fonts, the field stat plate | [CardRendering.md](docs/CardRendering.md) |
| Card kinds (the game's kind table) | [CustomCards.md](docs/CustomCards.md) |
| UI: RIX screens, MenuKit menus, all 111 widget classes, DFX scene graph, input | [MenuSystem.md](docs/MenuSystem.md), [Widgets.md](docs/Widgets.md), [WidgetTracker.md](docs/WidgetTracker.md) |
| Duel animations (every animation id, which are safe to fire) | [DuelAnimations.md](docs/DuelAnimations.md) |
| Online mode: Steam lobbies, wire format, the lockstep event stream (34 types), engine message codes, anti-cheat holes | [MultiplayerSystem.md](docs/MultiplayerSystem.md) |
| Stats, achievements, match results, win reasons | [StatsAndMatchResults.md](docs/StatsAndMatchResults.md) |
| Audio (Wwise slots) and video (Bink) | [AudioSystem.md](docs/AudioSystem.md), [MediaPlugin.md](docs/MediaPlugin.md) |
| Save file layout, save slots | [SaveSlots.md](docs/SaveSlots.md) |
| Sprite sheets (`.dfymoo`), animlists, fonts (`.fbin`) | [SpriteSheetsAndAnimlists.md](docs/SpriteSheetsAndAnimlists.md) |
| Every data file the game loads (and the leftovers it never reads: old ban lists, `card_extra.bin`, `CARD_Pass.bin`, charsets, `arenadata`) | the docs per format |
| Master Duel's `duel.dll` is the same engine; 2,838 new cards have Master Duel effect rows made only of functions this game has | [EffectSystem.md](docs/EffectSystem.md) |
| Duel rules: `EngineRules2020` does nothing; the duel format field (Master / Speed / Rush) and the zone mask | [DuelFormat.md](docs/DuelFormat.md) |
| Deck legality: the one check (`Deck_ValidateAndCountByInternalId`), its flags and the editor caps | [DeckLimits.md](docs/DeckLimits.md) |

---

## For plugin developers

| Piece | What to use |
|---|---|
| Hooks | Microsoft Detours for every code hook (including mid-function stubs). Data patches go through `YGO::Memory` (`Yu-Gi-Oh-Memory.h`). |
| Game addresses | `YGO::Addr` in `YuGiOh/YuGiOh-Addresses.h` for any address two plugins use. |
| Logging | `Yu-Gi-Oh-Logger.h` (a three-line `Logger.h` per plugin). |
| UI | The RIX API in `Yu-Gi-Oh-RIX.h` (the raw game UI part at its end only for what the API lacks). |
| Core | `Yu-Gi-Oh-Core.h`: plugin list, save file, save extension, save-write filters, credits. |
| Content | `Yu-Gi-Oh-Mods.h` for reading content (it stacks mods); never hard-code `<game>\Yu-Gi-Oh-Ex`. |
| Duel state | `YuGiOh/YuGiOh-DUELSTATE.h`: player state, LP key, tag partner block, piles. |
| Duel ids | Anything touching a card id during a duel goes through the duel-id resolver (see [Card ids](#card-ids-and-game-limits)). |
| Config | Every `Config.ini` key a plugin reads gets an entry in WolfX's `ConfigCatalog.cs` and in the manifest's settings. Read settings with the normal `GetPrivateProfile*` functions so mods' own `Config.ini` applies to them. |
| Manifest | Every plugin has a `<DLL>.json` (write it with PluginManifest). Use `replaces` when renaming or retiring. |
| New screens | Make a new screen (copy Core's `SaveScreen.cpp`), never borrow a game screen. See [CustomScreens.md](docs/CustomScreens.md). |
| Build | Debug x64 through `WolfX.sln` (or pass `SolutionDir`) so everything lands in `Binaries\Debug\`. |

---

## Open work (TODO)

### Needs testing in game

Most of the project is **Built** and waiting for a first real test. The big ones: card ids above 19,999 and the 65,534 cap, the Ex patch for each feature, new packs and shop rows, new campaign series, new characters/decks/story duels, new tutorials, new arenas, the Media plugin (music, voices, videos), handicap tag duels, Fusion/Ritual/Synchro/Xyz/Link rules, negation, locks, effect choice, the Cards and Booster Blast mods, Ban Lists, the multiplayer server client, the WolfX updater, and the mods system itself.

### Effects

- "While" rules, Quick Effects, effects from the hand (needs the hand-effect lists), Extra Deck summon procedures, a Fusion Summon action.
- A battle-position selector (`position = attack`).
- Conditional effects with branches (`if ... else ...`) and genuinely new resolve code for cards no game card resembles.
- Negate a Summon, negate an attack, continuous "negate its effects"; field-spell style "all X gain N".
- Win-condition cards (Exodia-style) for new cards.
- A converter from Master Duel effect rows to `effectClone`.
- About 4,000 new cards still need their effects (`docs/Card Lists/Effects Coverage.xlsx`: Fully read / Partially read / Not read tables with the reason, made by `Tools/Scripts/effects_coverage.py` from a WolfX `--attach-effects` run).

### Cards

- A borrowed low id can clash with a game card in player 1's deck (still open).
- New glyphs for fonts (render from a TTF and repack the atlas).
- Portrait offsets for new characters (the game's offset table is hard-coded).
- The game loading extracted `.zib` folders (tools can; the game can't yet).

### Campaign

- Per-duelist hand size and ban list.
- Deleting a whole tutorial.
- Mouse wheel on the series select screen.
- Hiding Help > How to Play (RIX can't hide that options entry yet).
- Some tutorial script operations and pointer zones are still guesses.

### RIX and BOB

- More widgets: user deck zone, campaign rewards, duel stats, leaderboard row, user slot, key entry, a ticker.
- WolfX page designer support for version 2 pages, and moving old version 1 pages over.
- BAB blocks for page scripts.
- BOB page actions: start a duel, open packs, music, story.
- A yes/no dialog in the RIX API.

### Multiplayer and the platform

- Check the duel state hash matches across two clients.
- Plugin side of the server: state hash and result frames, log upload, shared RNG seed, claim login; Steam OpenID sign-in.
- Filter unknown P2P senders (the game accepts packets from anyone).
- Online co-op tag (parked); 4-human tag needs our own transport.
- A cloud host for the server.
- The long-term goal: a community site (tracking, lobbies, deck sharing, tournaments with tamper checks).

### Forbidden Memories

- Guardian Stars, FM field and equip rules, drops and starchips (and the password shop), no Graveyard at all.
- FM duelists' story and dialogue.
- FM art for game cards (served by the plugin).

### Duel format

- Hide the two unused monster zones in the duel UI for Speed Duels (the engine already doesn't use them).
- A host-lobby duel format setting for online play (both players must match, or lockstep desyncs).

### Tools

- A Bink (`.bik`) viewer in WolfX.
- Switching off "your own files" in the Mod Manager.
- Card Manager layout polish.

### Unknowns

- What `card_extra.bin` was for.
- Part of the save's section `0x1B70` and some duelist slot fields.
- Where the game decides which zone an Extra Deck monster may use (Master Rule 4 vs 5; parked).

---

## Known traps

- **Debug and Release have separate Plugins folders.** If a change did nothing, check which loader you launched and that its DLLs are current.
- **Build through the solution** (or pass `SolutionDir`), or WolfX lands in `Tools\WolfX\Binaries` and you run a stale copy. An open WolfX or game locks the DLLs and the build fails at the copy step.
- **Back up your save before testing card-table changes.** A bad unlock-table hook once wiped a save.
- **Never play tutorials numbered 27 or higher without Campaign**: the game writes past the save's tutorial bits.
- **A duel started from a RIX page runs in Battle Pack mode** unless the duel modes are cleared first (empty decks, instant draw).
- **Never detour the duel message queue's Push**: its callers keep registers live across the call and the AI's turn hangs.
- **Release-only crash on load**: the compiler folded a constant game address into an absolute store. `YGO::Memory::WriteBytes` uses a volatile pointer now; scan Release DLLs for `ptr [0]` after touching shared headers.
- **`RIX_AddImage` with a PNG path as the sheet crashes the game.** Use a sprite sheet resource.
- **The game's `steam_api64.dll` is old**: use the `SteamClient()` interfaces, not the newer `SteamAPI_SteamUser` accessors.
- **Some game input checks consume the input**: reading Back or the help bar before the game's own update steals the press.
- **Mods are off until you switch them on** in the Mod Manager.
