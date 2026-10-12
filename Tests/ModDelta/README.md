# Mod delta and binary merge regression checks

On Windows with .NET 8 and the Windows SDK:

```powershell
dotnet build Tests/ModDelta/ModDelta.csproj -m:1
dotnet run --project Tests/ModDelta/ModDelta.csproj --no-build
```

The fixtures are synthetic and never open or write installed game data. Checks cover unchanged copies, independent changes,
same-record conflicts, additions/removals, rebuilt strings/offsets, duplicate card weights, streamed ZIB output, source precedence,
order/enabled-state changes, damaged output, and a modified baseline. Temporary fixture folders are created below the current directory.

For the native Core cache validator, use VS 2022 Build Tools with C++ and the Windows SDK. Generate a persistent fixture, build the
native project, then point it at the fixture:

```powershell
dotnet run --project Tests/ModDelta/ModDelta.csproj --no-build -- --fixture Tests/ModDelta/bin/fixture
msbuild Tests/ModDelta/MergeCacheTests.vcxproj /p:Configuration=Release /p:Platform=x64 /p:PreferredToolArchitecture=x64
Tests/ModDelta/bin/native/MergeCacheTests.exe Tests/ModDelta/bin/fixture
```

Native checks verify compatibility with the .NET-generated manifest and reject stale order, enable state, archive/settings, changed
input/output stamps, newly added loose files, malformed manifests and paths outside the cache namespace. The validator tests do not
launch or inject into the game.

## Live verification

Verified on Link Evolution for Windows using a separate copy of the game archive and an isolated unlocked test save:

1. Mod A supplied a loose `main/chardata_E.bin` copied from the original, renaming Alister (ID 75) to `MERGE ALPHA`.
2. Mod B supplied the same original table in `YGO_2020-Ex.toc/.dat`, renaming Arkana (ID 76) to `MERGE BETA`.
3. Preparing the active list produced one shared-table overlay with no conflicts.
4. With the rebuilt Core injected, both names appeared together under Duelist Challenges > Yu-Gi-Oh!.
5. Selecting `MERGE ALPHA` loaded its original Dragon Buster deck and reached the first playable turn, with the merged name displayed.

This verifies the generated overlay with mixed loose/patch inputs in the running game. Other adapters are covered by the synthetic
regressions above; this is not a live gameplay test of every supported format. Installed game files, mods, and the normal save were not modified.
