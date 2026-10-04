# Plugin manifests, Yu-Gi-Oh-Core and the plugin list

## Yu-Gi-Oh-Core

`Yu-Gi-Oh-Core.dll` is always loaded (the loader injects it first and it can not be switched off). It holds what every plugin can share and
nothing that belongs to one feature:

* **The save.** The game's save I/O is redirected to `savegame-ex.dat` (never the Steam save), whether or not the Cards plugin is on.
  `SeedFromGameSave` and `GameSaveName` are in `[Yu-Gi-Oh-Core]` (`[Yu-Gi-Oh-MoreCards]` is still read for them).
* **The plugin list.** Which plugins exist, are on, and can load; switching them (`Core_SetPluginEnabled`, which also switches on what a plugin
  requires and off what requires it). The in-game Plugins menu (RIX), the GUI window and the loader all agree through
  `Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Manifest.h`.
* **The host for `Plugins\YGO-Ex\`.** Core loads the ones that are on and can load, and runs `ProcessConfig` and `ProcessDetours` (guarded: a plugin that
  faults is logged and switched off). RIX asks it to start them once the main menu is up. So those plugins work **without** Yu-Gi-Oh-GUI;
  the GUI only hands them the ImGui context and draws their windows.

Its interface is `Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Core.h` (`Core::Load()` finds the loaded DLL).

## The plugin list in Config.ini

`[Yu-Gi-Oh-Core]` (next to Core's own settings; older `[Yu-Gi-Oh-RIX]` / `[Plugins]` lists are not read) has one line per plugin: `Yu-Gi-Oh-Cards=1` for a DLL in `Plugins\` (the loader injects it when it can load) and
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

A plugin that is on but whose requirement is off or missing is **blocked**: it is not loaded and the list says why ("Not loaded: it needs Yu-Gi-Oh-GUI,
which is off"). Switching a plugin on in the list switches on what it requires; switching one off switches off what requires it. Requirements load
before the plugins that need them.

## The tool

`Tools/PluginManifest` (`PluginManifest.exe`, built to `Binaries\<config>\Tools`) makes and edits these files: pick a DLL, set the title and
description, tick Enforced, tick the plugins it requires and the extra DLLs that belong to it, and Save. It warns about long descriptions,
a plugin requiring itself and requirement cycles.

## Yu-Gi-Oh-GUI

Yu-Gi-Oh-GUI is the shared ImGui host: it is the one plugin that hooks the game's Direct3D present and owns the ImGui context, and it hands that context to
every plugin that draws a window (Funky, SpeedHacks) and calls their `ProcessWindow` each frame. (BetterCardShop no longer has a window: it is
made of Yu-Gi-Oh-RIX pages in the game's own UI, so it requires Yu-Gi-Oh-RIX instead.) That way no plugin hooks DirectX itself, and two
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
