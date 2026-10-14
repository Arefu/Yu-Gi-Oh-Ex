#pragma once
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <format>
#include <initializer_list>
#include <string>

// Writing to the game's code and read-only data (header only). Real hooks stay Detours (DetourAttach); this is for byte patches (an
// immediate, a jump size) and for values in the game's tables. Every write makes the range writable, writes, puts the protection back
// (unless asked not to) and flushes the instruction cache.
//
// PatchBytes checks the bytes that are there before it writes, and logs both when they differ - a wrong expectation can no longer skip a
// patch without a word. Logging needs the plugin's Logger.h (MODULE_NAME) included before this header; without it, nothing is logged.
namespace YGO::Memory
{
    enum class Protection { Restore, LeaveWritable };   // LeaveWritable: PAGE_EXECUTE_READWRITE stays (data that later holds a jump stub)

    // Copies Size bytes to Address. False when the range could not be made writable (nothing written).
    inline bool WriteBytes(uintptr_t Address, const void* Data, size_t Size, Protection After = Protection::Restore)
    {
        // volatile: in Release the address is a constant (0x140C8E9BC...) once this is inlined, and MSVC then stored straight to it with a
        // 32-bit absolute operand - [0] after the high bits were lost - which crashed Yu-Gi-Oh-RIX on load (2026-10-11). Read at run time instead.
        void* volatile target = reinterpret_cast<void*>(Address);
        DWORD old = 0;
        if (!VirtualProtect(target, Size, PAGE_EXECUTE_READWRITE, &old))
            return false;
        std::memcpy(target, Data, Size);
        if (After == Protection::Restore)
            VirtualProtect(target, Size, old, &old);
        FlushInstructionCache(GetCurrentProcess(), target, Size);
        return true;
    }

    // One value (an immediate, a pointer in a table, a float in a layout table).
    template <typename T>
    inline bool WriteValue(uintptr_t Address, const T& Value, Protection After = Protection::Restore)
    {
        return WriteBytes(Address, &Value, sizeof(T), After);
    }

    inline std::string HexBytes(const uint8_t* Bytes, size_t Size)
    {
        std::string text;
        for (size_t i = 0; i < Size; ++i)
            text += std::format("{}{:02X}", i ? " " : "", Bytes[i]);
        return text;
    }

    // Writes Replacement at Address only when the bytes there are Expected (same length). Name says what the patch is for in the log.
    inline bool PatchBytes(uintptr_t Address, std::initializer_list<uint8_t> Expected, std::initializer_list<uint8_t> Replacement,
                           const char* Name, Protection After = Protection::Restore)
    {
        const auto* current = reinterpret_cast<const uint8_t*>(Address);
        const bool sizesMatch = Expected.size() == Replacement.size();
        if (!sizesMatch || std::memcmp(current, Expected.begin(), Expected.size()) != 0)
        {
#ifdef MODULE_NAME
            Logger::WriteLog(std::format("{}: not patched - expected {} at {:#x}, found {}", Name, HexBytes(Expected.begin(), Expected.size()), Address,
                HexBytes(current, Expected.size())) + (sizesMatch ? "" : " (expected and replacement differ in length)"), MODULE_NAME, 1);
#endif
            return false;
        }
        if (!WriteBytes(Address, Replacement.begin(), Replacement.size(), After))
        {
#ifdef MODULE_NAME
            Logger::WriteLog(std::format("{}: not patched - {:#x} could not be made writable", Name, Address), MODULE_NAME, 2);
#endif
            return false;
        }
        return true;
    }

    // Makes a range writable and leaves it so, for tables a plugin rewrites while the game runs (a row swapped in for one call).
    inline bool MakeWritable(uintptr_t Address, size_t Size)
    {
        DWORD old = 0;
        return VirtualProtect(reinterpret_cast<void*>(Address), Size, PAGE_READWRITE, &old) != 0;
    }
}
