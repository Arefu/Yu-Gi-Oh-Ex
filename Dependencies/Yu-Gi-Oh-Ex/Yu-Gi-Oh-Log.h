#pragma once
#include <Windows.h>
#include <string>

// Writes a line to the Yu-Gi-Oh-Console plugin's window (a no-op when that plugin is not loaded).
// Levels: 0 info, 1 warning, 2 error, 69 debug (hidden unless LogLevel=debug in Config.ini).
namespace YGO
{
    inline void Log(const std::string& message, const char* module, int level = 0)
    {
        using WriteLogFn = void(__cdecl*)(std::string, std::string, int);
        static const auto write = reinterpret_cast<WriteLogFn>(GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-Console.dll"), "WriteLog"));
        if (write)
            write(message, module, level);
    }
}
