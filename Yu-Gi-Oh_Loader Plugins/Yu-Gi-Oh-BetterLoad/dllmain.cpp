#include <Windows.h>
#include <iostream>
#include <cstdio>
#include <fstream>
#include <detours.h>
#include <format>
#include <io.h>

#include <DbgHelp.h>
#include <fstream>
#include "Logger.h"

uintptr_t LoadArchive = 0x14080D3D0;

bool AllowMultiInstance = false;

auto _Archive = new CHAR[MAX_PATH];
__int64 __fastcall _hLoadArchive(__int64* Struct, const char* Archive)
{
	Logger::WriteLog("Loading Archive: " + std::string(Archive), MODULE_NAME, 0);

	return ((int(__fastcall*)(__int64*, const char*))LoadArchive)(Struct, _Archive);
}

//The game doesn't save the HANDLE, we're just going to return ourselves.
static FARPROC pCreateMutex = GetProcAddress(GetModuleHandle(L"kernel32.dll"), "CreateMutexW");
HANDLE WINAPI HookCreateMutex(LPSECURITY_ATTRIBUTES lpMutexAttributes, BOOL bInitialOwner, LPCWSTR lpName)
{	
	return GetModuleHandle(NULL);
}

typedef FILE* (__cdecl* fopen_t)(const char* filename, const char* mode);
typedef errno_t(__cdecl* fopen_s_t)(FILE** file, const char* filename, const char* mode);
typedef size_t(__cdecl* fread_t)(void* ptr, size_t size, size_t count, FILE* stream);

fopen_t original_fopen = nullptr;
fopen_s_t original_fopen_s = nullptr;
fread_t original_fread = nullptr;

FILE* __cdecl hooked_fopen(const char* filename, const char* mode)
{
	Logger::WriteLog("Requested file (fopen): " + std::string(filename), MODULE_NAME, 0);
	
	return original_fopen(filename, mode);
}

errno_t __cdecl hooked_fopen_s(FILE** file, const char* filename, const char* mode)
{
Logger::WriteLog("Requested file (fopen_s): " + std::string(filename), MODULE_NAME, 0);
	return original_fopen_s(file, filename, mode);
}

size_t __cdecl hooked_fread(void* ptr, size_t size, size_t count, FILE* stream)
{
	// Just log and call original  int fd = _fileno(file);
	Logger::WriteLog(std::format("Reading From {}", count),MODULE_NAME,0);
	return original_fread(ptr, size, count, stream);
}

LONG WINAPI CrashHandler(EXCEPTION_POINTERS* ExceptionInfo) {

	std::ofstream log("crash_log.txt");
	log << "Crash Address: 0x" << std::hex << (uintptr_t)ExceptionInfo->ExceptionRecord->ExceptionAddress << "\n";
	log << "Exception Code: 0x" << std::hex << ExceptionInfo->ExceptionRecord->ExceptionCode << "\n";
	log.close();
	Sleep(2000000000000);
	return EXCEPTION_EXECUTE_HANDLER;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	Logger::SetupLogger();
	SetUnhandledExceptionFilter(CrashHandler);



	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		HMODULE stdio = GetModuleHandleA("api-ms-win-crt-stdio-l1-1-0.dll.dll");
		original_fopen = (fopen_t)GetProcAddress(stdio, "fopen");
		original_fopen_s = (fopen_s_t)GetProcAddress(stdio, "fopen_s");
		original_fread = (fread_t)GetProcAddress(stdio, "fread");

		GetPrivateProfileStringA("Yu-Gi-Oh-BetterLoad", "Archive", "YGO_2020", _Archive, 255, ".\\Config.ini");
		
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		DetourAttach(&(PVOID&)LoadArchive, _hLoadArchive);
		DetourAttach(&(PVOID&)original_fopen, hooked_fopen);
		DetourAttach(&(PVOID&)original_fopen_s, hooked_fopen_s);
		
		if (GetPrivateProfileIntA("Yu-Gi-Oh-BetterLoad", "AllowMultiInstance", 0, ".\\Config.ini") == 1)
			DetourAttach(&(PVOID&)pCreateMutex, HookCreateMutex);
		
		DetourTransactionCommit();

		break;
	}
	return TRUE;
}

