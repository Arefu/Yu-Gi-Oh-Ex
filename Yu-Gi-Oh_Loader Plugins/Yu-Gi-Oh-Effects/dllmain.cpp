#include <Windows.h>
#include <detours.h>
#include <conmanip.h>
#include <iostream>

#include "Yu-Gi-Oh.h"

using namespace conmanip;

__int64 __fastcall e_DealDamageToLP(unsigned __int16* Resolve)
{
	std::cout << "[Yu-Gi-Oh-Effects]: Damage Dealt By CardID: " << *(int*)Resolve << std::endl;
	YGO::CardEffects::_DealDamageToLP(Resolve);
    return 0;
}

BOOL APIENTRY DllMain( HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{      
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:


		std::cout << "[Yu-Gi-Oh-Effects]: I'm here at least." << std::endl;
		std::cout << sizeof(unsigned __int16*) << std::endl;
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());

		std::cout << YGO::CardEffects::_DealDamageToLP << std::endl;
		DetourAttach((PVOID*)&YGO::CardEffects::_DealDamageToLP, e_DealDamageToLP);

		DetourTransactionCommit();
		std::cout << YGO::CardEffects::_DealDamageToLP << std::endl;

        break;;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

