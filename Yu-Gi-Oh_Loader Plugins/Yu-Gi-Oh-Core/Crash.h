#pragma once

// The crash log (moved from PatchMeOut 2026-10-07: any plugin can crash the game, and Core is the one that is always on). On a fatal
// exception (access violation, heap corruption, fail fast...) it writes crash_log.txt (address, module+offset, the stack) and crash.dmp
// next to the game. Harmless exceptions the game raises all the time (C++ throws, thread names) are ignored.
// Since 2026-10-25 also: abort / std::terminate (an uncaught C++ exception, with its type and message) and stack overflow write the same
// files; every crash flushes console.log first (Yu-Gi-Oh-Console FlushAllLogs); and every way out of the process without an exception
// (ExitProcess, TerminateProcess on itself, RIX::App::RequestQuit, the console window closing) is logged to console.log with its stack.
namespace Crash
{
    void Install();
}
