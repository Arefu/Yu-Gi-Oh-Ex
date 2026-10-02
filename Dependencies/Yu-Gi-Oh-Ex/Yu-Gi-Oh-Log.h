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

    // Writes to a split log instead: <file>.log next to console.log (e.g. "Duels" -> Duels.log). Appended across runs, never shown
    // in the console window or console.log. For plugins that want their own record or diagnostic trace.
    inline void LogTo(const std::string& file, const std::string& message, const char* module, int level = 0)
    {
        using WriteLogToFn = void(__cdecl*)(std::string, std::string, std::string, int);
        static const auto write = reinterpret_cast<WriteLogToFn>(GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-Console.dll"), "WriteLogTo"));
        if (write)
            write(file, message, module, level);
    }

    // Flushes a split log now (the console batches writes), e.g. when a record is complete.
    inline void FlushLog(const std::string& file)
    {
        using FlushLogToFn = void(__cdecl*)(std::string);
        static const auto flush = reinterpret_cast<FlushLogToFn>(GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-Console.dll"), "FlushLogTo"));
        if (flush)
            flush(file);
    }
}
