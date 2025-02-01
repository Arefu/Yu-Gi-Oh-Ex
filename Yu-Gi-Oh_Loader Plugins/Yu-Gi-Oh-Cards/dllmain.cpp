#include <Windows.h>
#include <iostream>

#include "detours.h"
#include "Cards.h"
#include "Logger.h"
#include "Memory.h"
#include "Targets.h"


uint16_t Cards::INTERNAL_IDs[];
uint16_t Cards::CARD_IDs[];
Cards::MEMORY_CARD_PROP Cards::CARD_PROPS[];



void SetupJumpCalls()
{
	//Internal IDs.
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D11E), 0x140D55480, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D09E), 0x140D55480, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D44B), 0x140D55480, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D4B5), 0x140D55480, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D5D8), 0x140D55480, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D668), 0x140D55480, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D6B8), 0x140D55480, true);

	Logger::WriteLog("Jump Calls Setup.", MODULE_NAME, 0);
}

void SetupTombstones()
{
	DWORD oldProtect;
	VirtualProtect(reinterpret_cast<void*>(0x140D55480), 64, PAGE_EXECUTE_READWRITE, &oldProtect);

	Memory::EmplaceMOV(reinterpret_cast<void*>(0x140D55480), reinterpret_cast<uintptr_t>(&Cards::INTERNAL_IDs), Memory::X64Register::RCX, false);
	Memory::EmplaceRET(reinterpret_cast<void*>(0x140D55480 + 10), false);

	
	Logger::WriteLog("Tombstone Set.", MODULE_NAME, 0);
}


uintptr_t _Get_CardProps = 0x1407CAB30;

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		Logger::SetupLogger();
		SetupJumpCalls();
		SetupTombstones();

		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		DetourAttach(&(PVOID&)ORIGINAL_MEMCPY, Memory::_H_MEMCPY);

		DetourAttach(&(PVOID&)_Get_CardProps, Cards::Get_CardProps);
		DetourAttach(&(PVOID&)_Get_InternalID, Cards::Get_InternalID);
		DetourAttach(&(PVOID&)_Get_CardID, Cards::Get_CardID);

		std::cout << "Card ID: " << &Cards::CARD_IDs << std::endl;
		std::cout << "Internal ID: " << &Cards::INTERNAL_IDs << std::endl;
		std::cout << "Card Props: " << &Cards::CARD_PROPS << std::endl;
		DetourTransactionCommit();

		Logger::WriteLog("It's Time To Du-Du-Du-Duel!.", MODULE_NAME, 0);

		break;

	}
	return TRUE;
}
