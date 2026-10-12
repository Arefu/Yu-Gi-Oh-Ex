# Save safety and content compatibility

WolfX's **Files > Save editor** identifies the loaded save beside its path:

* **Steam save** includes the Steam account. Normal Steam launches use it; Steam Cloud may sync it.
* **Toolkit profile** includes the slot (1–5), using `GameSaveName` in the open game's `Config.ini`. Custom filenames are supported.
* Other game-folder saves and standalone files are labelled separately, without assuming they are the active profile.

The Open menu excludes files whose size is not 44,008 bytes. Loading additionally validates the magic, version and declared size.
A supported save with an invalid checksum can still be opened for repair; saving recomputes its checksum.

Saving writes and flushes a temporary file in the target directory, then atomically replaces an existing target. Every replacement
retains the previous target in a separate `<save>.<UTC date/time>-<unique id>.bak`; the success dialog gives its path. A new target
does not get a backup. On failure the original target is retained and the in-memory checksum/counter are not advanced, so retrying
does not add extra save-counter increments. Existing `.bak` files are left intact.

**Files > Game files & loading > Check content compatibility…** reports each saved additional-content entry, its required plugins,
missing DLLs (including transitive manifest dependencies and companion DLLs), unreadable JSON and exports with no runtime consumer.
It uses the configured `PluginsPath`, including when that directory is missing. Installed required plugins need not already be
enabled: the loader enables them from `content.json`. Compiled effects in `cards.json` also require Yu-Gi-Oh-Effects.

The check examines saved files in the open data's `Yu-Gi-Oh-Ex` folder. It does not validate effect behavior, unsaved edits,
active mod packages or whether a plugin successfully loaded. Verify saved changes in game before sharing them.

## Regression checks (Windows, .NET 8+)

From the repository root:

```powershell
dotnet run --project Tests/EditorSafety/EditorSafety.csproj --configuration Release
```

The dependency-free executable exercises atomic replacement, exact backup contents, repeated saves, locked-target failure/retry,
invalid headers, new saves, profile labels, compiled-effect requirements, unsupported content, malformed JSON, transitive dependencies,
and a configured plugin directory that does not exist. It uses temporary fixtures and never opens the user's actual saves.
