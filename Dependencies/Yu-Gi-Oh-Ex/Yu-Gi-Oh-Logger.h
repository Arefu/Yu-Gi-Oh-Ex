#pragma once
#include <string>
#include <windows.h>

// The one logger every plugin uses (header only). Writes go to Yu-Gi-Oh-Console.dll (the console window and console.log) through the
// functions it exports, looked up once by Logger::SetupLogger(); without the console every write is a no-op.
//
// Each plugin has a Logger.h of its own that names the module and includes this:
//     #pragma once
//     #define MODULE_NAME "Yu-Gi-Oh-MyPlugin"
//     #include "Yu-Gi-Oh-Logger.h"
// then calls Logger::SetupLogger() in DLL_PROCESS_ATTACH and Logger::WriteLog(message, MODULE_NAME, level).
#ifndef MODULE_NAME
#error "Define MODULE_NAME (the plugin's name in the log) before including Yu-Gi-Oh-Logger.h"
#endif

// The levels Yu-Gi-Oh-Console understands.
namespace LogLevel
{
    constexpr int Info = 0;
    constexpr int Warn = 1;
    constexpr int Error = 2;
    constexpr int Debug = 69;
}

class Logger
{
    // The no-op fallbacks (declared first: the static members below start out pointing at them).
    static void __cdecl NoWriteLog(std::string, std::string, int) {}
    static void __cdecl NoWriteLogTo(std::string, std::string, std::string, int) {}
    static void __cdecl NoFlushLogTo(std::string) {}

public:
    using WriteLog_t = void(__cdecl*)(std::string message, std::string module, int level);
    // Split log (Yu-Gi-Oh-Console's WriteLogTo): <file>.log next to console.log, not shown in the console (Yu-Gi-Oh-MP's Duels.log).
    using WriteLogTo_t = void(__cdecl*)(std::string file, std::string message, std::string module, int level);
    using FlushLogTo_t = void(__cdecl*)(std::string file);

    inline static WriteLog_t WriteLog = &NoWriteLog;
    inline static WriteLogTo_t WriteLogTo = &NoWriteLogTo;
    inline static FlushLogTo_t FlushLogTo = &NoFlushLogTo;

    static void SetupLogger()
    {
        const HMODULE console = GetModuleHandleA("Yu-Gi-Oh-Console.dll");
        if (!console)
            return;
        if (const auto write = reinterpret_cast<WriteLog_t>(GetProcAddress(console, "WriteLog")))
            WriteLog = write;
        // Older Yu-Gi-Oh-Console builds don't have the split-log exports: those writes stay no-ops.
        if (const auto writeTo = reinterpret_cast<WriteLogTo_t>(GetProcAddress(console, "WriteLogTo")))
            WriteLogTo = writeTo;
        if (const auto flushTo = reinterpret_cast<FlushLogTo_t>(GetProcAddress(console, "FlushLogTo")))
            FlushLogTo = flushTo;
    }

    static void Log(std::string message, std::string module, int level)
    {
        WriteLog(message, module, level);
    }
};
