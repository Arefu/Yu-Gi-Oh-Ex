# Save safety and content compatibility

WolfX's **Files > Save editor** identifies the loaded save beside its path:

* **Steam save** includes the Steam account. Normal Steam launches use it; Steam Cloud may sync it.
* **Toolkit profile** includes the slot (1–5), using `GameSaveName` in the open game's `Config.ini`. Custom filenames are supported.
* Other game-folder saves and standalone files are labelled separately, without assuming they are the active profile.

The Open menu excludes files whose size is not 44,008 bytes. Loading additionally validates the magic, version and declared size.
A supported save with an invalid checksum can still be opened for repair; saving recomputes its checksum.

Saving writes and flushes a temporary file in the target directory, then atomically replaces an existing target. No backup is kept:
copy the save yourself before editing it. On failure the original target is retained and the in-memory checksum/counter are not
advanced, so retrying does not add extra save-counter increments.

**Complete save** unlocks everything in the open save (read from the open game data and Yu-Gi-Oh-Ex JSON) and sets the DP to 999,999;
it is written on the next Save.

**Files > Game files & loading > Check content compatibility…** reports each saved additional-content entry, its required plugins,
missing DLLs (including transitive manifest dependencies and companion DLLs), unreadable JSON and exports with no runtime consumer.
It uses the configured `PluginsPath`, including when that directory is missing. Installed required plugins need not already be
enabled: the loader enables them from `content.json`. Compiled effects in `cards.json` also require Yu-Gi-Oh-Effects.

The check examines saved files in the open data's `Yu-Gi-Oh-Ex` folder. It does not validate effect behavior, unsaved edits,
active mod packages or whether a plugin successfully loaded. Verify saved changes in game before sharing them.

## Additional-content files and the effect library

Cards, packs, menus, unlocks, effects and summoning changes cannot be saved after their file fails to load.
Repair the reported JSON file, then reopen the game folder. For the user effect library, repair
`%APPDATA%\WolfX\effect_library.json` and restart WolfX. Missing files can still be created normally.

Saving these files stages the new contents before replacing the target and retains its previous contents
in a timestamped `.bak` file. Removing an empty override or deleting a designer page also retains the original
in a backup. A failed save or page deletion keeps pending edits available for retry.

The effect library participates in **File > Save all** and the save-before-close prompt. If saving fails,
choosing Yes in that prompt keeps WolfX open. Deleting or reverting a library entry also remains unsaved
until the library file is successfully written.

## Regression checks (Windows, .NET 8+)

From the repository root:

```powershell
dotnet run --project Tests/EditorSafety/EditorSafety.csproj --configuration Release
```

The dependency-free executable exercises atomic replacement (no backup or temporary file left), repeated saves, locked-target failure/retry,
invalid headers, new saves, profile labels, compiled-effect requirements, unsupported content, malformed JSON, transitive dependencies,
and a configured plugin directory that does not exist. It uses temporary fixtures and never opens the user's actual saves.
