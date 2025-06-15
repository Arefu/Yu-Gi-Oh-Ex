#pragma once
#include <string>
#include <windows.h>

#define MODULE_NAME "Yu-Gi-Oh-Effects"

class Logger {
public:
    static void(__cdecl* WriteLog)(std::string, std::string, int);
    static void SetupLogger();
    static void Log(std::string message, std::string module, int logLevel) {
        if (WriteLog) {
            WriteLog(message, module, logLevel);
        }
    }
};

void _WriteLog(std::string message, std::string module, int logLevel);