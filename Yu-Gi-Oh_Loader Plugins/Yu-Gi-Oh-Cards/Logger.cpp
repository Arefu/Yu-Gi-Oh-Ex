#include "Logger.h"

void(__cdecl* Logger::WriteLog)(std::string, std::string, int) = _WriteLog;

void _WriteLog(std::string message, std::string module, int logLevel) {
}

void Logger::SetupLogger() {
	WriteLog = (void(__cdecl*)(std::string, std::string, int))GetProcAddress(
		GetModuleHandleA("Yu-Gi-Oh-Console.dll"), "WriteLog");

	if (WriteLog == nullptr) {
		WriteLog = _WriteLog;
	}
}