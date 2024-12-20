#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <fstream>
#include <string>

#include "Yu-Gi-Oh.h"

#include "DRAWCARDSCOUNT_Table.h"
#include "REMOVESPELLCOUNTER_Table.h"
#include "NEGATEMONSTERSUMMON_Table.h"


void Setup_CardDrawCountTable();
void Setup_RemoveSpellTokenTable();
void Setup_NegateMonsterSummonList();

std::map<short, short> REMOVESPELLCOUNTER_Table::Table = std::map<short, short>();
std::map<short, short> DRAW_Table::Table = std::map<short, short>();
std::vector<short> NEGATEMONSTERSUMMON_List::List = std::vector<short>();

bool ChangeMemoryProtection(void* address, size_t size, DWORD& oldProtect) {
    return VirtualProtect(address, size, PAGE_READWRITE, &oldProtect) != 0;
}
bool RestoreMemoryProtection(void* address, size_t size, DWORD oldProtect) {
    return VirtualProtect(address, size, oldProtect, &oldProtect) != 0;
}

template <typename T>
bool UpdateMemoryValue(uintptr_t address, const T& newValue) {
    T* target = reinterpret_cast<T*>(address);
    DWORD oldProtect;
    if (!ChangeMemoryProtection(target, sizeof(T), oldProtect)) {
        return false; 
    }
    *target = newValue;
    return RestoreMemoryProtection(target, sizeof(T), oldProtect);
}
bool UpdateMemoryValue(uintptr_t address, const short& newValue) {
    short* target = reinterpret_cast<short*>(address);
    DWORD oldProtect;
    if (!ChangeMemoryProtection(target, sizeof(short), oldProtect)) {
        return false;
    }
    *target = newValue;
    return RestoreMemoryProtection(target, sizeof(short), oldProtect);
}


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
        Setup_RemoveSpellTokenTable();
        Setup_NegateMonsterSummonList();

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

void Setup_NegateMonsterSummonList()
{
	std::cout << "[Yu-Gi-Oh-Effects]: Setting Up Negate Monster Summon List" << std::endl;
    for (long long Start = NEGATEMONSTERSUMMON_List::Base; Start < NEGATEMONSTERSUMMON_List::End; Start += 0x2)
    {
        if (Start == NULL)
            continue;
        auto Card = std::to_string(*reinterpret_cast<short*>(Start));
        
		std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Will Negate Monster Summons" << std::endl;

		NEGATEMONSTERSUMMON_List::List.push_back(*reinterpret_cast<short*>(Start));
    }	
}

void Setup_RemoveSpellTokenTable()
{
	std::cout << "[Yu-Gi-Oh-Effects]: Setting Up Remove Spell Token Table" << std::endl;
	for (long long Start = REMOVESPELLCOUNTER_Table::Base; Start < REMOVESPELLCOUNTER_Table::End; Start += 0x4)
	{
		if (Start == NULL)
			continue;

		auto Card = std::to_string(*reinterpret_cast<short*>(Start));

		short Value = GetPrivateProfileIntA("Yu-Gi-Oh-Effects", (std::string("REMOVE_SPELL_TOKEN_") + Card).c_str(), *reinterpret_cast<short*>(Start + 0x2), ".\\Config.ini");
		if (Value != *reinterpret_cast<short*>(Start + 0x2))
		{
			std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Requested New Value: " << Value << " !{Remove Spell Token Table Change}!" << std::endl;
			UpdateMemoryValue<int>((Start + 0x2), Value);
		}
		std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Will Remove: " << *reinterpret_cast<short*>(Start + 0x2) << " Spell Tokens" << std::endl;

        REMOVESPELLCOUNTER_Table::Table.emplace(*reinterpret_cast<short*>(Start), *reinterpret_cast<short*>(Start + 0x2));
	}
    }

void Setup_CardDrawCountTable()
{
    std::cout << "[Yu-Gi-Oh-Effects]: Setting Up Card Draw Count Table" << std::endl;
    for (long long Start = DRAW_Table::Base; Start < DRAW_Table::End; Start += 0x4)
    {
        if (Start == NULL)
            continue;
      
        auto Card = std::to_string(*reinterpret_cast<short*>(Start));
        
        short Value = GetPrivateProfileIntA("Yu-Gi-Oh-Effects", (std::string("DRAW_COUNT_") + Card).c_str(), *reinterpret_cast<short*>(Start + 0x2), ".\\Config.ini");
        if (Value != *reinterpret_cast<short*>(Start + 0x2))
        {
			std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Requested New Value: " << Value << " !{Draw Table Change}!" << std::endl;
            UpdateMemoryValue<short>((Start + 0x2), Value);      
        }
        else
        {
            std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Will Allow Player To Draw: " << *reinterpret_cast<short*>(Start + 0x2) << " Cards" << std::endl;
        }
		

		DRAW_Table::Table.emplace(*reinterpret_cast<short*>(Start), *reinterpret_cast<short*>(Start + 0x2));
    }
}