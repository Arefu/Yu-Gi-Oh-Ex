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

`requires` lists plugin DLL names (no `.dll`). A plain name is fine for plugins that come with Yu-Gi-Oh-Ex; give a `url` for anything
else. The mod's `Yu-Gi-Oh-Ex\content.json` (WolfX writes it) also counts: the plugins it names are needed too.

## Load order

`<game>\Mods\modlist.json` (the Mod Manager writes it) is the order and what is on:

```json
{ "mods": [ { "id": "anime-frames", "enabled": true }, { "id": "yugi-kaiba-format", "enabled": false } ] }
```

* Mods load from the top of the list down; **a later mod wins** when two change the same thing.
* A folder in `Mods` that isn't listed loads after the listed ones (by name) and is **on**, so copying a mod folder in by hand works.
* Only folders with a `mod.json` are mods (the Mod Manager writes one when it installs a .zip without it). Anything else in `Mods` (archive copies such as `Vanilla\YGO_2020.toc`, extracted files) is ignored by the game and hidden by the Mod Manager.
* The game folder's own `Yu-Gi-Oh-Ex` and `YGO_2020-Ex` (what you make with WolfX) **always load last**, on top of every mod.
* Changes apply the next time the game starts (like plugins).

The game side is `Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Mods.h` (header only, used by the loader, Core and every content plugin); the tool side
is `Tools/Shared/Mods/ModLibrary.cs`. Both follow the rules above.

## How each kind of content combines

| What | How mods combine |
|---|---|
| Game files (patch or loose) | Whole files: the first layer that has the file wins - your own files, then the last mod, ..., then the game's archive. Inside one layer `[Yu-Gi-Oh-Core] FileOrder` decides loose vs patch. Big streamed files (card art .zibs) only come from patches, never loose files. |
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
  so swapping mods turns your copies into the other mod's cards - authors should pick different ids in 15300-19999), game files changed by
  more than one mod (the later wins), characters/decks changed by more than one.
* **Export .zip** re-packs an installed mod; **Create mod** packs your own content; **Play** starts `Yu-Gi-Oh_Loader.exe`.

The loader does the same plugin check when the game starts: a mod that is on switches on the plugins it needs, and a message names the
ones that are missing (with the mod's link).

## Limits / not done

* WolfX shows the game's data plus your own patch, not the mods' changes.
* Two mods can't both partly change one game file (e.g. both edit `bin\CARD_Prop.bin`): files are whole, the later wins. Content JSON
  merges; game files don't.
* A mod folder name should be plain ASCII (the Mod Manager makes ids like that): the game opens a mod's patch through an 8-bit path.
* Card ids are not moved for you; clashing mods need an author to renumber.
