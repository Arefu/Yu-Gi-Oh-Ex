#include <Windows.h>
#include <iostream>
#include <cstdio>
#include <fstream>
#include <detours.h>

uintptr_t LoadArchive = 0x14080D3D0;
uintptr_t te = 0x140766F70;

bool AllowMultiInstance = false;

auto _Archive = new CHAR[MAX_PATH];
__int64 __fastcall _hLoadArchive(__int64* Struct, const char* Archive)
{
	std::cout << "[Yu-Gi-Oh-BetterLoad]: Loading Archive: " << _Archive << std::endl;

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
typedef size_t(__cdecl* fread_t)(void* buffer, size_t size, size_t count, FILE* stream);

fopen_t original_fopen = nullptr;
fopen_s_t original_fopen_s = nullptr;
fread_t original_fread = nullptr;

FILE* __cdecl hooked_fopen(const char* filename, const char* mode)
{
	std::cout << "[Yu-Gi-Oh-BetterLoad] Requested file (fopen): " << filename << std::endl;
	
	return original_fopen(filename, mode);
}

errno_t __cdecl hooked_fopen_s(FILE** file, const char* filename, const char* mode)
{
	std::cout << "[Yu-Gi-Oh-BetterLoad] Requested file (fopen_s): " << filename << std::endl;

	return original_fopen_s(file, filename, mode);
}



BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	HMODULE stdio = GetModuleHandleA("api-ms-win-crt-stdio-l1-1-0.dll.dll");
	original_fopen = (fopen_t)GetProcAddress(stdio, "fopen");
	original_fopen_s = (fopen_s_t)GetProcAddress(stdio, "fopen_s");
	original_fread = (fread_t)GetProcAddress(stdio, "fread");

	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
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
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}

