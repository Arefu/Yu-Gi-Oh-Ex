#pragma once
#include <cstddef>
#include <cstdint>
#include "YuGiOh/YuGiOh-Addresses.h"

// Shared by the table movers (Ritual.cpp, SynchroXyz.cpp): memory the game's image-relative operands can reach, operand writes, and entry
// stubs that run a refresh function before a game function without changing anything the caller can see.
namespace CodePatch
{
    constexpr uintptr_t kImageBase = YGO::Addr::ImageBase;

    // Memory within +2 GB of the image, so image-base-relative and rip-relative operands can reach it. nullptr when there is none.
    void* AllocNearImage(size_t size);

    // Writes over code (VirtualProtect + flush).
    bool WriteBytes(uintptr_t address, const void* bytes, size_t size);

    // Detours `entry` through a stub that saves flags, rax/rcx/rdx/r8-r11 and xmm0-5, calls `refresh`, restores them and continues into the
    // original function. Call between DetourTransactionBegin and DetourTransactionCommit. `label` names the caller in the log.
    bool AttachRefresh(uintptr_t entry, void(__cdecl* refresh)(), const char* label);
    // The same stub: rcx and rdx are still the game function's first two arguments when `refresh` is called, so it can look at them.
    bool AttachRefresh(uintptr_t entry, void(__cdecl* refresh)(uintptr_t rcx, uintptr_t rdx), const char* label);
}
