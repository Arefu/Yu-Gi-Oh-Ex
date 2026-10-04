#pragma once
#include <cstdint>
#include <cstring>
#include <windows.h>

// Reading the game's memory from the GUI. Every read is guarded (SEH) so a bad address or a block that is not set up yet shows
// a fallback value instead of taking the game down. The addresses are the ones named in YuGiOh.exe.i64; see Inspector.cpp.
namespace GameMemory
{
    inline bool ReadRaw(uintptr_t address, void* out, size_t size)
    {
        __try
        {
            memcpy(out, reinterpret_cast<const void*>(address), size);
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    template <class T>
    T Read(uintptr_t address, T fallback = T{})
    {
        T value;
        return ReadRaw(address, &value, sizeof(value)) ? value : fallback;
    }

    template <class T>
    bool Write(uintptr_t address, T value)
    {
        __try
        {
            *reinterpret_cast<T*>(address) = value;
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    // Calls a game function that takes a Konami id and returns a wide string (name, description), guarded.
    inline const wchar_t* CallCardText(uintptr_t function, uint16_t id)
    {
        __try
        {
            return reinterpret_cast<const wchar_t*(__fastcall*)(short)>(function)(static_cast<short>(id));
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }
}
