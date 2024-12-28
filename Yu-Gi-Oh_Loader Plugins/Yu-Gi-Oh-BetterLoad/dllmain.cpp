#include <Windows.h>
#include <iostream>
#include <string>

#include <detours.h>

//
long long Address = 0x14080D3D0;

auto _Archive = new CHAR[MAX_PATH];
__int64 __fastcall _hLoadArchive(__int64* Struct, const char* Archive)
{
	std::cout << "[Yu-Gi-Oh-BetterLoad]: Loading Archive: " << _Archive << std::endl;
	return ((int(__fastcall*)(__int64*, const char*))Address)(Struct, _Archive);
}

BOOL APIENTRY DllMain( HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        GetPrivateProfileStringA("Yu-Gi-Oh-BetterLoad", "Archive", "YGO_2020", _Archive, 255, ".\\Config.ini");
		
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());

        DetourAttach(&(PVOID&)Address, _hLoadArchive);
        DetourTransactionCommit();
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

