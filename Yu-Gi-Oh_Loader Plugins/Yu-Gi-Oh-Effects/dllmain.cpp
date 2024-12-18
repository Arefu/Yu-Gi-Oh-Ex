#include <Windows.h>
#include <detours.h>
#include <iostream>

#include "Yu-Gi-Oh.h"

#include "DRAW_Table.h"



void Setup_CardDrawCountTable();

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

        Setup_CardDrawCountTable();

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


void Setup_CardDrawCountTable()
{
	std::cout << "[Yu-Gi-Oh-Effects]: Setting Up Card Draw Count Table" << std::endl;
    for (long long Start = DRAW_Table::Base; Start < DRAW_Table::End; Start += 0x4)
	{
		std::cout << "[Yu-Gi-Oh-Effects]: CardID: " << *reinterpret_cast<short*>(Start) << " Count: " << *reinterpret_cast<short*>(Start + 0x2 ) << std::endl;
	}
}