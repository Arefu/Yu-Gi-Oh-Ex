#include <Windows.h>
#include <iostream>
#include <format>

#include "detours.h"
#include "Cards.h"
#include "Logger.h"
#include "Memory.h"
#include "Targets.h"

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

void SetupLimitBreaks()
{
	Memory::PatchBytes(reinterpret_cast<void*>(0x14076D6C5), reinterpret_cast<const uint8_t*>("\xFF\xFF"), 2, true);

	Logger::WriteLog("Limit Breaks Set.", MODULE_NAME, 0);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		DetourRestoreAfterWith();

		Logger::SetupLogger();
		SetupJumpCalls();
		SetupTombstones();
	//	SetupLimitBreaks();

		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		DetourAttach(&(PVOID&)ORIGINAL_MEMCPY, Memory::_H_MEMCPY);

	//	DetourAttach(&(PVOID&)_Setup_CardPropTable, Cards::Setup_CardPropTable);

		DetourAttach(&(PVOID&)_Get_InternalID, Cards::Get_InternalID);
	//	DetourAttach(&(PVOID&)_Get_CardProps, Cards::Get_CardProps);
		DetourAttach(&(PVOID&)_Get_CardID, Cards::Get_CardID);

		//Logger::WriteLog("Loading Card IDs From: 0x" + std::format("{:X}", reinterpret_cast<uintptr_t>(&Cards::CARD_IDs)), MODULE_NAME, 0);
		//Logger::WriteLog("Loading Internal IDs From: 0x" + std::format("{:X}", reinterpret_cast<uintptr_t>(&Cards::INTERNAL_IDs)), MODULE_NAME, 0);
		//Logger::WriteLog("Loading Card Properties From: 0x" + std::format("{:X}", reinterpret_cast<uintptr_t>(&Cards::CARD_PROPS)), MODULE_NAME, 0);

		DetourTransactionCommit();

		Logger::WriteLog("It's Time To Du-Du-Du-Duel!.", MODULE_NAME, 0);

		break;

	}
	return TRUE;
}
