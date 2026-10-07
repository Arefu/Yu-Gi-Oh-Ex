#pragma once

// The crash log (moved from PatchMeOut 2026-10-07: any plugin can crash the game, and Core is the one that is always on). On a fatal
// exception (access violation, heap corruption, fail fast...) it writes crash_log.txt (address, module+offset, the stack) and crash.dmp
// next to the game. Harmless exceptions the game raises all the time (C++ throws, thread names) are ignored.
namespace Crash
{
    void Install();
}
