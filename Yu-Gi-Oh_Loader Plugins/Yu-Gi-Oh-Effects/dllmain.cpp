#include <Windows.h>
#include <detours.h>
#include <conmanip.h>
#include <iostream>

#include "Yu-Gi-Oh.h"

using namespace conmanip;

__int64 __fastcall e_DealDamageToLP(unsigned __int16* ID)
{
	std::cout << "[Yu-Gi-Oh-Effects]: Damage Dealt By CardID: " << *(int*)ID <<  std::endl;
	YGO::CardEffects::_DealDamageToLP(ID);
    return 0;
}

BOOL APIENTRY DllMain( HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{      
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());

		DetourAttach((PVOID*)&YGO::CardEffects::_DealDamageToLP, e_DealDamageToLP);

		DetourTransactionCommit();

        break;;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

