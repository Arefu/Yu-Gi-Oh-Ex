# Mods

A **mod** is a content pack: new cards, packs, decks, music, pages, changed game files (ban list, card art, frames), and optionally plugins.
Any number of mods can be on at once; they load on top of each other. People install them with the **Mod Manager**
(`Binaries\<config>\Tools\ModManager.exe`), and authors make them with the Mod Manager's **Create mod** or WolfX's **File > Export as mod**
(the same dialog, `Tools/Shared/Mods`).

## What a mod is

A `.zip` that unpacks to `<game>\Mods\<id>\`:

```
mod.json                 who made it and what it needs
Yu-Gi-Oh-Ex\             new content: the same files WolfX writes to <game>\Yu-Gi-Oh-Ex (cards.json, packs.json, art\, music\, pages\, ...)
YGO_2020-Ex.toc / .dat   changed game files: WolfX's patch archive (Core Patch.h)
YGO_2020\                changed game files as loose files, at their path in the archive (YGO_2020\bin\pd_limits.bin)
Plugins\                 plugin DLLs (+ their .json manifests) the Mod Manager copies into the loader's Plugins folder
Plugins\YGO-Ex\          the same for plugins Yu-Gi-Oh-Core starts
Config.ini               plugin settings the mod needs (see "Settings" below)
```

Every part is optional. In the zip they can sit at the top or inside one folder. A zip with only an `Info.ini`
(`title=`, `desc=`, `long-desc=`, the older hand-made format) still installs; the Mod Manager writes a `mod.json` for it.

### mod.json

```json
{
  "name": "Yugi-Kaiba Format (May 2002)",
  "version": "1.0",
  "author": "...",
  "website": "https://...",
  "description": "One line.",
  "details": "Longer text: credits, what changed.",
  "requires": [
    "Yu-Gi-Oh-MoreCards",
    { "plugin": "Yu-Gi-Oh-Foo", "url": "https://where.to/get/it" }
  ]
}
```

`"newSave": "forbidden-memories"` (or `true` = the folder name) marks an overhaul that needs a save made with it, the same as a
plugin manifest's `newSave` ([PluginManifests.md](PluginManifests.md#plugins-that-need-a-new-save)): Yu-Gi-Oh-Core only loads save slots
made with exactly the tags of the plugins and mods that are on.

`"exclusive": true` makes the mod a clean slate (`YGO::Mods::Active` / `IsExclusive`):
* While it is on, it loads alone; the last exclusive mod in the load order wins.
* No other mod's content or game files load.
* `<game>\Yu-Gi-Oh-Ex` (WolfX's workspace JSON) isn't read, and neither are the game folder's loose `YGO_2020\` files and the
  `YGO_2020-Ex` patch (Core Patch.cpp).
* Files the game writes still go to `<game>\Yu-Gi-Oh-Ex` (`LocalContentFolder`).

`requires` lists plugin DLL names (no `.dll`). A plain name is fine for plugins that come with Yu-Gi-Oh-Ex; give a `url` for anything
else. The mod's `Yu-Gi-Oh-Ex\content.json` (WolfX writes it) also counts: the plugins it names are needed too.

### Settings (Config.ini)

A mod that needs plugin settings brings them in its own `Config.ini`, in the same `[Section] Key=Value` layout as the game's, so
players never edit their Config.ini for it. The Anime Frames mod uses it to move the ATK/DEF text and the art onto its frames;
Forbidden Memories sets the whole FM card layout and the FM rules.

```ini
[Yu-Gi-Oh-AnimeCards]
CardArtScale=1.05
AtkLabel="ATK "

[Yu-Gi-Oh-FM]
NoTributes=1
```

* **How:** Yu-Gi-Oh-Core (`ModConfig.cpp`, installed before anything else reads a setting) hooks the four `GetPrivateProfile*` functions
  (Int and String, A and W). When one reads the game's `Config.ini`, a `[Section] Key` that an active mod sets comes from the mod;
  everything else comes from the player's file. No plugin needs changing: every one reads its settings through those functions.
* **Who wins:** a mod's value wins over the player's while the mod is on; between mods, the later one in the load order wins. An
  exclusive mod loads alone, so only its settings apply. Switching the mod off brings the player's own values back.
* **Rules:** the same as Windows: spaces around keys and values are trimmed, a value in matching quotes loses them (`"ATK "` keeps its
  space), lines starting with `;` or `#` are comments. UTF-8 (with or without a BOM) or UTF-16 with a BOM.
* **What a mod can't set** (Core skips it and logs a warning): in `[Yu-Gi-Oh-Core]` the plugin switches (`Yu-Gi-Oh-*`, `YGO-Ex/*`; use
  `requires` instead), `PluginsPath`, `ModConfig`, `GameSaveName`, `SeedFromGameSave`, `SaveSlot` and `ReplacedNotice`; and all of
  `[Yu-Gi-Oh-MP]`, so a mod can never point the server address or the sign-in code somewhere else.
* **Writes** (a plugin saving a setting) always go to the player's Config.ini.
* **Turning it off:** `[Yu-Gi-Oh-Core] ModConfig=0` in the player's Config.ini.
* **Seeing it:** console.log lists every setting a mod sets (`[Section] Key=Value (from mod '...')`). WolfX's Config Editor shows the
  settings a switched-on mod overrides in blue, with the mod's value. The Mod Manager lists a mod's settings in its details.
* **Making one:** WolfX's File > Edit a mod, then Tools > Config Editor, edits that mod's `Config.ini` (rows a mod can't set are left
  out). Export as mod and the Mod Manager carry the file with the rest of the mod.

## Load order

`<game>\Mods\modlist.json` (the Mod Manager writes it) is the order and what is on:

```json
{ "mods": [ { "id": "anime-frames", "enabled": true }, { "id": "yugi-kaiba-format", "enabled": false } ] }
```

* Mods load from the top of the list down; **a later mod wins** when two change the same thing.
* A folder in `Mods` that isn't listed comes after the listed ones (by name) and is **off**: no mod loads until the player switches it on in
  the Mod Manager (File > Open the Mod Manager in WolfX). Installing a mod with the Mod Manager also adds it switched off.
* WolfX's File > Edit a mod opens an installed mod as its workspace: every page reads and saves that mod's `Yu-Gi-Oh-Ex\`,
  `YGO_2020-Ex.toc/.dat` and `YGO_2020\` over the game's own archive (other mods and your own files are not read).
* Only folders with a `mod.json` are mods (the Mod Manager writes one when it installs a .zip without it). Anything else in `Mods` (archive copies such as `Vanilla\YGO_2020.toc`, extracted files) is ignored by the game and hidden by the Mod Manager.
* The game folder's own `Yu-Gi-Oh-Ex` and `YGO_2020-Ex` (what you make with WolfX) **always load last**, on top of every mod.
* Changes apply the next time the game starts (like plugins).

The game side is `Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Mods.h` (header only, used by the loader, Core and every content plugin); the tool side
is `Tools/Shared/Mods/ModLibrary.cs`. Both follow the rules above.

## How each kind of content combines

| What | How mods combine |
|---|---|
| Game files (patch or loose) | The Mod Manager merges changes against the original game archive. Known tables combine by ID or slot; ZIBs combine by entry name; other fixed-layout `.bin` files combine by aligned record. Conflicting records use the later mod's value and produce a warning. Your own files remain whole-file overrides. Inside each mod `[Yu-Gi-Oh-Core] FileOrder` decides loose vs patch. |
| `cards.json`, `unlocks.json`, `summoning.json` | Cards from every mod are joined. The same new-card id in two mods: the later mod's card replaces the earlier one (logged). Changes to game cards apply in order. Art paths (`"image"`) are relative to the mod they came from. |
| `genres.json`, `relatedcards.json`, `packs.json`, `prices.json`, `characters.json`, `decks.json`, `storyduels.json`, `storyscripts.json`, `credits.json` | Merged: lists are joined (so a later entry for the same id wins), objects merged key by key, single values replaced by the later copy. |
| `text.json` | Merged per entry number and language: the later mod wins. |
| `music.json` | Read one by one: a later copy replaces the tracks of the slots (and arena / opponent / story duel scopes) it sets; the rest stay. File names are relative to that copy's folder. |
| `voices.json` | Lines from every mod add up; `volume` / `duck` / `gap` come from the last copy that sets them. |
| `pages\<name>.json` | The copy that wins (your own, else the last mod that has it). |
| `menus\*.json` | All of them; a file with the same name in a later mod replaces the earlier one. |
| `saves.json`, `Exported Decks\` | Yours only: never read from mods, never packed into one. |

How a plugin reads its content (instead of `<game>\Yu-Gi-Oh-Ex\<file>`), from `Yu-Gi-Oh-Mods.h`:

* `YGO::Mods::ReadMerged("packs.json")` - every copy merged as above; each object in a top-level list gets `"$folder"` (the content folder
  it came from, ends in `\`), read it with `YGO::Mods::FolderOf(entry)` for relative paths. `ReadMerged("cards.json", "cards")` also
  accepts a bare list.
* `YGO::Mods::Files("music.json")` - every copy, lowest priority first.
* `YGO::Mods::Find("pages\\x.json")` / `FilesIn("menus", L".json")` - the copy that wins / a folder across mods.
* `YGO::Mods::LocalContentFolder()` - `<game>\Yu-Gi-Oh-Ex`, for files the game writes.

## The Mod Manager

`Tools/ModManager` (`ModManager.exe [--game <folder>] [--create] [mod.zip ...]`):

* **Install** a `.zip` (button, drag it onto the window, or open it with the Mod Manager). Installing a newer copy of a mod keeps its place
  and on/off state. A mod that brings plugins asks first (plugins are programs that run in the game) and can be installed without them.
* Tick to switch on/off, **Move up / down** for the order; saved straight away.
* The selected mod: what it changes, the plugins it needs (installed / missing, with **links** to get them), what clashes.
* **Things to know**: missing plugins, two mods adding cards with the **same ids** (only one can exist, and the save keeps owned cards by id,
  so swapping mods turns your copies into the other mod's cards - authors should pick different ids in 15300-65534), game files changed by
  conflicting binary records or archive entries (the later change wins), characters/decks changed by more than one.
* **Export .zip** re-packs an installed mod; **Create mod** packs your own content; **Play** starts `Yu-Gi-Oh_Loader.exe`.

The loader does the same plugin check when the game starts: a mod that is on switches on the plugins it needs, and a message names the
ones that are missing (with the mod's link).

## Binary merges

Installing, enabling, disabling, reordering, refreshing, or playing through the Mod Manager prepares a separate merged archive in
`Mods/.merged/<generation>/overlay.toc/.dat`. Neither the original game archive nor the installed mods are changed by this preparation.
Only files shared by multiple active mods need a merged copy. An unchanged copy from a later mod does not revert an earlier change.

The merge compares each mod with the untouched original archive selected by `[Yu-Gi-Oh-Core] Archive` (default `YGO_2020`).
Within each mod, `FileOrder` selects the loose or patch source first. The generated archive loads above the mods and below your own files.
It supports streamed reads as well as whole-file reads, including ZIBs supplied as loose files when a merged copy is generated.

| Format | Merge unit |
|---|---|
| `chardata`, `deckdata`, `packdefdata`, `dueldata` | Character, deck, pack, or duel ID; string pools and offsets rebuilt |
| `scriptdata` | Scene name (case insensitive); script ranges and string offsets rebuilt |
| `pd_limits.bin` | Card ID and its ban-list category; counts rebuilt |
| `tagdata`, `taginfo` | Related-card list or tag slot; pools and offsets rebuilt |
| `WORD` / `DLG` index + text pairs | Text slot; components merged together and offsets rebuilt |
| `CARD_Indx` + `CARD_Name` + `CARD_Desc` | Card text slot; all three components regenerated together |
| UI `.BND`, `credits.dat` | String or line slot |
| `.zib` (including card art, decks, packs) | Entry name, additions and removals; headers, sizes and offsets rebuilt. Large archives stream in chunks. |
| Reward pack lists, battle pack lists, `.ydc` | Card counts (preserving duplicates/weights), battle-pack slot, or deck section |
| `CARD_Prop`, `CARD_Genre`, `CARD_INTID`, `CARD_Pass` | Complete 8-, 8-, 2-, or 4-byte record respectively |
| Other fixed-layout `.bin` files | Aligned 8-byte unit (final short unit allowed); layout must stay unchanged |

Different edits to the same unit are reported under **Things to know**, with later mods winning that unit. A same-ID addition,
delete-versus-edit, incompatible resize of an opaque file, malformed structure, or exceeded capacity is also a conflict. Media and other
opaque assets are indivisible replacements. Without an untouched baseline, ordinary whole-file load order applies and the manager warns.
Binary merging cannot infer relocations or checksums in unknown formats; offset-changing replacements require a format-specific adapter.

Core validates the active order, archive name, `FileOrder`, source file inventory, sizes and modification times, and generated archive
stamps before mounting the cache. If any source or output changes, it ignores the stale cache and logs that a Mod Manager refresh is needed.
The manager rebuilds automatically on refresh or Play. Old generated archives are collected when the game is closed; while playing,
generations stay immutable so open streams remain valid. Hand-copied mods participate after opening or refreshing the Mod Manager.

## Limits / not done

* WolfX shows the game's data plus your own patch, not the mods' changes.
* Installing with the Mod Manager compares game files against an untouched `YGO_2020.toc/.dat`. Identical files are omitted.
  Additive `main/chardata_<lang>.bin` tables become `characters.json` entries, so independent duelist additions combine through
  Yu-Gi-Oh-Campaign. Existing JSON entries take precedence. Character ids still must be distinct; associated decks and portraits
  need their own compatible content. Other binary tables participate in the merge archive described above. Without a baseline, or with
  an archive marked modified, installation preserves the original package.
  Hand-copied mod folders bypass this conversion; reinstall their zip through the Mod Manager to normalize them.
* A mod folder name should be plain ASCII (the Mod Manager makes ids like that): the game opens a mod's patch through an 8-bit path.
* Card ids are not moved for you; clashing mods need an author to renumber.
