#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <fstream>
#include <string>

#include "Yu-Gi-Oh.h"

#include "DRAW_Table.h"



void Setup_CardDrawCountTable();

std::map<short, short> DRAW_Table::Table = std::map<short, short>();

bool ChangeMemoryProtection(void* address, size_t size, DWORD& oldProtect) {
    return VirtualProtect(address, size, PAGE_READWRITE, &oldProtect) != 0;
}

bool RestoreMemoryProtection(void* address, size_t size, DWORD oldProtect) {
    return VirtualProtect(address, size, oldProtect, &oldProtect) != 0;
}
template <typename T>
bool UpdateMemoryValue(uintptr_t address, const T& newValue) {
    // Cast address to the proper type
    T* target = reinterpret_cast<T*>(address);

    // Save the original protection to restore later
    DWORD oldProtect;

    // Change protection to read/write
    if (!ChangeMemoryProtection(target, sizeof(T), oldProtect)) {
        return false;  // Failed to change protection
    }

    // Update the value at the target address
    *target = newValue;

    // Restore the original protection
    return RestoreMemoryProtection(target, sizeof(T), oldProtect);
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
        if (Start == NULL)
            continue;
      
        auto Card = std::to_string(*reinterpret_cast<short*>(Start));
        
        short Value = GetPrivateProfileIntA("Yu-Gi-Oh-Effects", (std::string("DRAW_") + Card).c_str(), *reinterpret_cast<short*>(Start + 0x2), ".\\Config.ini");
        if (Value != *reinterpret_cast<short*>(Start + 0x2))
        {
			std::cout << "[Yu-Gi-Oh-Effects]: Card: " << Card << " Requested New Value: " << Value << " !{Draw Table Change}!" << std::endl;
            UpdateMemoryValue<int>((Start + 0x2), Value);      
        }
		DRAW_Table::Table.emplace(*reinterpret_cast<short*>(Start), *reinterpret_cast<short*>(Start + 0x2));
    }
}