#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <string>

#include "Yu-Gi-Oh.h"

#include "Memory.h"
#include "Config.h"


#include "XYZSummonRequirements.h" //Changes XYZ Summon Requirements
#include "CardID_WithNumberOfCardsToDraw.h" //Changes How Many Card Each "Draw" Card Draws


std::map<XYZSummonRequirements::Requirements, uintptr_t> XYZSummonRequirements::Table = std::map<XYZSummonRequirements::Requirements, uintptr_t>();
std::map<CardID_WithNumberOfCardsToDraw::Requirements, uintptr_t> CardID_WithNumberOfCardsToDraw::Table = std::map<CardID_WithNumberOfCardsToDraw::Requirements, uintptr_t>();

__int64 __fastcall e_DealDamageToLP(unsigned __int16* ID)
{
	std::cout << "[Yu-Gi-Oh-Effects]: Damage Dealt By CardID: " << *(int*)ID <<  std::endl;
	YGO::CardEffects::_DealDamageToLP(ID);
    return 0;
}

BOOL APIENTRY DllMain( HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    std::string Path = "C:\\";
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        Path = Config::Get_WorkingDirectory(); //We Don't Want Content To Be Loaded From The Wrong Directory, This Will Be Where *ALL* Stuff Goes.
        if(Path == "")
		{
			char Buffer[256];
			MessageBoxA(NULL, "Failed To Load/Parse Config.ini, Please Make Sure It Exists/ Is Setup And In The Same Directory As The Game!", "Yu-Gi-Oh-Effects", MB_ICONERROR);
		}
        
		std::cout << "[Yu-Gi-Oh-Effects]: Loading From: " << Path << std::endl;

        //Parse IN-GAME Content to setup stuff first.
        XYZSummonRequirements::Setup();
		CardID_WithNumberOfCardsToDraw::Setup();

		CardID_WithNumberOfCardsToDraw::ProcessChanges(Path);
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

//void Setup_NegateMonsterSummonList()
//{
//	std::cout << "[Yu-Gi-Oh-Effects]: Setting Up Negate Monster Summon List" << std::endl;
//    for (long long Start = NEGATEMONSTERSUMMON_List::Base; Start < NEGATEMONSTERSUMMON_List::End; Start += 0x2)
//    {
//        if (Start == NULL)
//            continue;
//        auto Card = std::to_string(*reinterpret_cast<short*>(Start));
//        
//		std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Will Negate Monster Summons" << std::endl;
//
//		//NEGATEMONSTERSUMMON_List::List.push_back(*reinterpret_cast<short*>(Start));
//    }	
//}
//
//void Setup_RemoveSpellTokenTable()
//{
//	std::cout << "[Yu-Gi-Oh-Effects]: Setting Up Remove Spell Token Table" << std::endl;
//	for (long long Start = REMOVESPELLCOUNTER_Table::Base; Start < REMOVESPELLCOUNTER_Table::End; Start += 0x4)
//	{
//		if (Start == NULL)
//			continue;
//
//		auto Card = std::to_string(*reinterpret_cast<short*>(Start));
//
//		short Value = GetPrivateProfileIntA("Yu-Gi-Oh-Effects", (std::string("REMOVE_SPELL_TOKEN_") + Card).c_str(), *reinterpret_cast<short*>(Start + 0x2), ".\\Config.ini");
//		if (Value != *reinterpret_cast<short*>(Start + 0x2))
//		{
//			std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Requested New Value: " << Value << " !{Remove Spell Token Table Change}!" << std::endl;
//	//		UpdateMemoryValue<int>((Start + 0x2), Value);
//		}
//		std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Will Remove: " << *reinterpret_cast<short*>(Start + 0x2) << " Spell Tokens" << std::endl;
//
//       // REMOVESPELLCOUNTER_Table::Table.emplace(*reinterpret_cast<short*>(Start), *reinterpret_cast<short*>(Start + 0x2));
//	}
//    }
//
//void Setup_CardDrawCountTable()
//{
//    std::cout << "[Yu-Gi-Oh-Effects]: Setting Up Card Draw Count Table" << std::endl;
//    for (long long Start = DRAW_Table::Base; Start < DRAW_Table::End; Start += 0x4)
//    {
//        if (Start == NULL)
//            continue;
//      
//        auto Card = std::to_string(*reinterpret_cast<short*>(Start));
//        
//        short Value = GetPrivateProfileIntA("Yu-Gi-Oh-Effects", (std::string("DRAW_COUNT_") + Card).c_str(), *reinterpret_cast<short*>(Start + 0x2), ".\\Config.ini");
//        if (Value != *reinterpret_cast<short*>(Start + 0x2))
//        {
//			std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Requested New Value: " << Value << " !{Draw Table Change}!" << std::endl;
//         //   Memory::UpdateMemoryValue<short>((Start + 0x2), Value,);      
//        }
//        else
//        {
//            std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Will Allow Player To Draw: " << *reinterpret_cast<short*>(Start + 0x2) << " Cards" << std::endl;
//        }
//		
//
//		//DRAW_Table::Table.emplace(*reinterpret_cast<short*>(Start), *reinterpret_cast<short*>(Start + 0x2));
//    }
//}