# Content saves and hook diagnostics

## Content editors

Direct file saves in the Music, Voice, Animation List, Sprite Sheet, How To Play,
and Tutorial editors use `ContentFileSave`. Definition files and imported pictures
are staged together before any target is replaced. Existing files get separate
backups named `<filename>.<UTC timestamp>-<unique ID>.bak` beside the target.

Each replacement is atomic. If a later replacement fails, earlier replacements
are rolled back from their backups, and newly created files are removed. A batch
is not atomic across a process crash or power failure; retain the backups until
the content has been checked. If rollback also fails, the error identifies the
original backups needed for recovery.

Failed saves retain pending edits and imported pictures for retry. A Sprite Sheet
save now writes an imported replacement PNG even when the target PNG exists.
Game archive writes continue to use the existing archive writer.

## Plugin startup

Console, Core tweaks, AnimeCards, MoreCards, Effects, GUI and PatchMeOut report
hook transaction begin/update/commit failures through `HookDiagnostics`. Errors
include the hook group, transaction stage, Windows error code and message, and
the failed original-pointer slot when Detours provides it. Success is reported
after the transaction commits. Enable Console to collect plugin logs; failures
also go to Windows debug output when logging is unavailable.

These diagnostics describe hook installation, not a complete compatibility test
for every game executable or plugin option.

## Verification

- `Tests/ContentSafety`: Windows file-lock and read-only failures, retry,
  paired definition/image saves, rollback of existing and new files, backup
  contents, temporary file cleanup, and UTF-8 encoding (12 checks).
- `Tests/HookDiagnostics`: real Detours begin/commit failures and a successful
  attach/run/detach cycle (11 checks).
- Manual WolfX check: hold `music.json` open exclusively, edit its volume, save,
  check the failure status and retained edit, release the lock, and retry.
  Check the original backup and saved JSON.
- Game smoke check: launch with the rebuilt DLLs and an isolated save profile,
  compare loaded-module hashes with the build, inspect all changed hook groups
  in the log, open the GUI overlay and Deck Edit, and check that Music loads the
  edited JSON. Exit normally and confirm the Steam save is unchanged.
