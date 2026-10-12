# Custom content save safety

Windows x64 regression harness that calls the rebuilt WolfX assembly's actual save methods. It does not open the installed game or write to its files.

Build `Tools/WolfX/WolfX.csproj` in Release, then run:

```powershell
dotnet run --project Tests/CustomContentSafety -c Release -- <absolute-path-to-WolfX.dll> <new-empty-fixture-folder>
```

Use a fresh fixture folder for each run. The harness checks rollback after a late commit failure, artwork/JSON/removal batching, retained pending changes and backups, malformed archetype preservation, archetype retry/unknown fields, Card Manager failure propagation (including a missing callback), and page rename failure/retry. It also produces a normal custom card with blue JPEG artwork and a page that can be used for a separate in-game smoke test.

This covers handled write failures, not process termination or power loss. Multi-file saves retain timestamped `.bak` originals. If rollback itself fails, the error identifies the affected paths and backups.
