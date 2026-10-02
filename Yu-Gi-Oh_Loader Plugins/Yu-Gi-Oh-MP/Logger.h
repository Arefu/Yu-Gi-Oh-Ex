#pragma once
#include <string>
#include <windows.h>

#define MODULE_NAME "Yu-Gi-Oh-MP"

class Logger {
public:
    static void(__cdecl* WriteLog)(std::string, std::string, int);
    // Split log (Yu-Gi-Oh-Console's WriteLogTo): <file>.log next to console.log, not shown in the console. Used for Duels.log.
    static void(__cdecl* WriteLogTo)(std::string file, std::string message, std::string module, int level);
    static void(__cdecl* FlushLogTo)(std::string file);
    static void SetupLogger();
    static void Log(std::string message, std::string module, int logLevel) {
        if (WriteLog) {
            WriteLog(message, module, logLevel);
        }
    }
};

void _WriteLog(std::string message, std::string module, int logLevel);
