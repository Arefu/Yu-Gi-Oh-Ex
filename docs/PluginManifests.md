# Plugin manifests, Yu-Gi-Oh-Core and the plugin list

## Yu-Gi-Oh-Core

`Yu-Gi-Oh-Core.dll` is always loaded (the loader injects it first and it can not be switched off). It holds what every plugin can share and
nothing that belongs to one feature:

* **The save.** The game's save I/O is redirected to `savegame-ex.dat` (never the Steam save), whether or not the Cards plugin is on.
  `GameSaveName` is in `[Yu-Gi-Oh-Core]`. The Steam save is copied in only on request from the save-select screen, never automatically.
* **The plugin list.** Which plugins exist, are on, and can load; switching them (`Core_SetPluginEnabled`, which also switches on what a plugin
  requires and off what requires it). The in-game Plugins menu (RIX), the GUI window and the loader all agree through
  `Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Manifest.h`.
* **The host for `Plugins\YGO-Ex\`.** Core loads the ones that are on and can load, and runs `ProcessConfig` and `ProcessDetours` (guarded: a plugin that
  faults is logged and switched off). RIX asks it to start them once the main menu is up. So those plugins work **without** Yu-Gi-Oh-GUI;
  the GUI only hands them the ImGui context and draws their windows.

Its interface is `Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Core.h` (`Core::Load()` finds the loaded DLL).

## The plugin list in Config.ini

`[Yu-Gi-Oh-Core]` (next to Core's own settings; older `[Yu-Gi-Oh-RIX]` / `[Plugins]` lists are not read) has one line per plugin: `Yu-Gi-Oh-MoreCards=1` for a DLL in `Plugins\` (the loader injects it when it can load) and
`YGO-Ex/Yu-Gi-Oh-Funky=1` for one in `Plugins\YGO-Ex\` (Core starts it). New plugins are added as `0`. Enforced plugins are always written as `1`.
`PluginsPerPage` (3 to 5) is in the same section.

## Manifests

A `<DLL name>.json` next to a DLL (`Plugins\Yu-Gi-Oh-Cards.json`, `Plugins\YGO-Ex\Yu-Gi-Oh-Funky.json`). Every field is optional:

```json
{
  "title": "Speed Hacks",
  "description": "Faster game, no animations or movies.",
  "enforced": false,
  "requires": [ "Yu-Gi-Oh-Core" ],
  "dlls": [ "Yu-Gi-Oh-SpeedHacksHelper" ],
  "content": [ "packs.json" ]
}
```

| Field | What it does |
|---|---|
| `title` | What the plugin list calls it. Default: the DLL's name. |
| `description` | One short line under it. It does not scroll in the game's box, keep it under about 56 characters. Default: nothing. |
| `enforced` | Always on; the list shows it as "Always on" and does not switch it. Yu-Gi-Oh-Core and Yu-Gi-Oh-RIX are enforced even without a manifest. |
| `requires` | DLL names (no `.dll`) that have to be on for this one to load. List **real** dependencies only: Core is always loaded, so a plugin that does not call it does not list it. |
| `dlls` | More DLLs (same folder) that belong to this plugin. They have no entry of their own and load with it, just before it. |
| `content` | The files in `<game>\Yu-Gi-Oh-Ex` this plugin applies (`"cards.json"`, `"pages\\"` for a folder). WolfX reads it to keep `Yu-Gi-Oh-Ex\content.json` up to date, and the loader switches on the plugins that file names (see [WolfXContent.md](WolfXContent.md)). |

| `newSave` | The plugin changes the game so much that it needs a save made with it (an overhaul such as Forbidden Memories). `true` uses the DLL's name as the save tag; a string (`"forbidden-memories"`) names the tag, so several plugins of one overhaul can share it. See below. |

### Plugins that need a new save

Each save slot remembers the save tags of the plugins that were loaded when its profile was started (`"tags"` in `saves.json`, see
[SaveSlots.md](SaveSlots.md)). A slot with a save file can only be played when its tags are **exactly** the tags of the plugins loaded now:

- an old save while a `newSave` plugin is on: "Needs a new save: <title>";
- a save made with that plugin while it is off: "Made with <title> (off)".

Such a slot shows the reasons in red on the save-select screen, and picking it says why and points to **New Save**. Empty slots and New Save
always work and take the current tags. `SaveSlot=N` in Config.ini is ignored (the screen shows) when slot N can't be played. Nothing is deleted
or changed in the old saves: switching the plugin off again makes them playable again. The tags are taken once, when the game starts
(what loads this run); switching plugins in game only counts after a restart, as for everything else.

A plugin that is on but whose requirement is off or missing is **blocked**: it is not loaded and the list says why ("Not loaded: it needs Yu-Gi-Oh-GUI,
which is off"). Switching a plugin on in the list switches on what it requires; switching one off switches off what requires it. Requirements load
before the plugins that need them.

## Replaced plugins

`"replaces"` lists older plugins this one took over (renamed, merged in, or retired): each entry is a name, or `{ "name": "...", "note": "..." }`.
Copying a new build over an old folder leaves the old DLLs behind, so:

- The loader never loads a replaced plugin. Anything that asks for it by name (`requires`, content.json) gets the plugin that replaced it.
- The old Config.ini switch moves to the new plugin and the old line is removed.
- The player is told once which leftover files can go. `[Yu-Gi-Oh-Core] ReplacedNotice` remembers which ones were already shown.
- To clean up, use WolfX **Files > Game files & loading > Retired plugins...** or the Mod Manager's orange **Leftover** rows (double-click). Both move the files to `Plugins\_retired\<date>`; nothing is deleted.

Current entries: Media replaces Music; MoreCards replaces Yu-Gi-Oh-Cards and ZCards; Core replaces BetterLoad, SaveMe, BetterCardShop (now BOB pages + Core packs) and Tracer.

## The tool

`Tools/PluginManifest` (`PluginManifest.exe`, built to `Binaries\<config>\Tools`) makes and edits these files: pick a DLL, set the title and
description, tick Enforced, tick the plugins it requires and the extra DLLs that belong to it, and Save. It warns about long descriptions,
a plugin requiring itself and requirement cycles.

## Yu-Gi-Oh-GUI

Yu-Gi-Oh-GUI is the shared ImGui host: it is the one plugin that hooks the game's Direct3D present and owns the ImGui context, and it hands that context to
every plugin that draws a window (Funky, SpeedHacks) and calls their `ProcessWindow` each frame. That way no plugin hooks DirectX itself, and two
hooks cannot fight over the swap chain. A plugin that draws a window lists `Yu-Gi-Oh-GUI` in `requires`; that is a real dependency, not a leftover.

What the GUI does **not** do any more is manage plugins: the tick list it used to show is gone. Which plugins are on is decided in the in-game Plugins menu
(Help & Options), WolfX or the plugin lines in `[Yu-Gi-Oh-Core]` of `Config.ini`, and Yu-Gi-Oh-Core loads and starts them.
The loader's `PluginsPath` setting lives in `[Yu-Gi-Oh-Core]` (the old `[Yu-Gi-Oh-GUI]` key is still read).

## settings (2026-10-02)

`"settings": [ { "key", "section", "title", "kind", "default", "choices", "help" } ]` lists the plugin's Config.ini settings
(`section` defaults to the DLL's name; `kind` is toggle, choice, number, text or path). WolfX's Config Editor reads them
(`ConfigCatalog.WithManifests`), so a new plugin brings its own settings to it. The lists in this repo's manifests were generated from WolfX's
built-in catalog. (An in-game Plugins > Plugin Settings screen read them too from 2026-10-02 until it was removed on 2026-10-04, together
with Core's `Core_GetPluginSetting` / `CoreSettingInfo`; the loader and Core no longer parse `"settings"`.)

Optional per setting (the fields describe how a value is picked; WolfX uses `from` for its Pick... button):

| field | what it means |
| --- | --- |
| `"hidden": true` | not listed in game (WolfX still lists it). Core's `PluginsPath`. |
| `"min"`, `"max"`, `"step"` | numbers: left / right step by `step` (1 if not set), wrapping round when both ends are set (SpeedHacks `Speed` 1-10). |
| `"from": "archives"` | choices = every `<name>.toc` with its `.dat` in the game folder (`Archive`, `PatchArchive`). |
| `"from": "folders"` | choices = the game folder's subfolders (Core's `FolderName`). |
| `"from": "files:<folder>\\*.ext"` | choices = those files, as paths from the game folder (Funky's test decks). |
| `"empty": "Off"` | with `from`: the empty value is a choice too, shown as this. |
| `"labels": { "0": "Pick at start" }` | what a value is shown as (`SaveSlot`, `HideLevelBadge`, `DuelTestPlayer`). |

WolfX's Config Editor has a **Pick...** button (or double-click the value) for paths, archives and folders.
