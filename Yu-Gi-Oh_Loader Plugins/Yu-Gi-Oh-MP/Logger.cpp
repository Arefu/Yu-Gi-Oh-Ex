#include "Logger.h"

void(__cdecl* Logger::WriteLog)(std::string, std::string, int) = _WriteLog;

namespace
{
    void __cdecl NoWriteLogTo(std::string, std::string, std::string, int) {}
    void __cdecl NoFlushLogTo(std::string) {}
}

void(__cdecl* Logger::WriteLogTo)(std::string, std::string, std::string, int) = NoWriteLogTo;
void(__cdecl* Logger::FlushLogTo)(std::string) = NoFlushLogTo;

void _WriteLog(std::string message, std::string module, int logLevel) {
}

void Logger::SetupLogger() {
    HMODULE console = GetModuleHandleA("Yu-Gi-Oh-Console.dll");

    WriteLog = (void(__cdecl*)(std::string, std::string, int))GetProcAddress(console, "WriteLog");
    if (WriteLog == nullptr) {
        WriteLog = _WriteLog;
    }

    // Older Yu-Gi-Oh-Console builds don't have the split-log exports; the duel record is then simply not written.
    WriteLogTo = (void(__cdecl*)(std::string, std::string, std::string, int))GetProcAddress(console, "WriteLogTo");
    if (WriteLogTo == nullptr) {
        WriteLogTo = NoWriteLogTo;
    }
    FlushLogTo = (void(__cdecl*)(std::string))GetProcAddress(console, "FlushLogTo");
    if (FlushLogTo == nullptr) {
        FlushLogTo = NoFlushLogTo;
    }
}
