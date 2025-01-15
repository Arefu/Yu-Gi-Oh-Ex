#include <Windows.h>
#include <iostream>
#include <string>

#include <detours.h>

uintptr_t LoadArchive = 0x14080D3D0;
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


BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{

	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		GetPrivateProfileStringA("Yu-Gi-Oh-BetterLoad", "Archive", "YGO_2020", _Archive, 255, ".\\Config.ini");

	
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		DetourAttach(&(PVOID&)LoadArchive, _hLoadArchive);

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

