#include <Windows.h>
#include <string>

#include "Targets.h"
#include <iostream>

#include "detours.h"

#include "Memory.h"
#include "Cards.h"

//Yu-Gi-Oh-Console Requirements.
#define MODULE_NAME "Yu-Gi-Oh-MoreCards"
void _WriteLog(std::string Message, std::string Module, int LogLevel) {}
void (*WriteLog)(std::string Message, std::string Module, int LogLevel) = _WriteLog;
void SetupLogger()
{
	WriteLog = (void (*)(std::string, std::string, int))GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-Console.dll"), "WriteLog");
	if (WriteLog == nullptr)
	{
		WriteLog = _WriteLog;
	}
}

uint16_t Cards::INTERNAL_IDs[11072];
uint16_t Cards::CARD_IDs[11072];


void* __cdecl MemoryCopy(void* dest_str, const void* Src, size_t Size)
{
	
	if (reinterpret_cast<uintptr_t>(dest_str) == 0x140D55480)
	{
		dest_str = reinterpret_cast<void*>(&Cards::INTERNAL_IDs);
		
		for (int i = 3900; i < 14969; i++)
		{
			auto ID = Cards::Get_InternalID(i);
			Cards::CARD_IDs[ID] = i;
		}
	}

	auto result = reinterpret_cast<void* (__cdecl*)(void*, const void*, size_t)>(MemCopy)(dest_str, Src, Size);
	return result;
}

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
	WriteLog("Jump Calls Setup.", MODULE_NAME, 0);
}

void SetInternalIDTombstone()
{
	DWORD oldProtect;
	VirtualProtect(reinterpret_cast<void*>(0x140D55480), 64, PAGE_EXECUTE_READWRITE, &oldProtect);

	Memory::EmplaceMOV(reinterpret_cast<void*>(0x140D55480), reinterpret_cast<uintptr_t>(&Cards::INTERNAL_IDs), false);
	Memory::EmplaceRET(reinterpret_cast<void*>(0x140D55480 + 10), false);
}


BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		SetupLogger();
		SetupJumpCalls();
		SetInternalIDTombstone();

		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		DetourAttach(&(PVOID&)MemCopy, MemoryCopy);

		DetourAttach(&(PVOID&)_Get_InternalID, Cards::Get_InternalID);
		DetourAttach(&(PVOID&)_GetCardID, Cards::Get_CardID);


		DetourTransactionCommit();

		WriteLog("It's Time To Du-Du-Du-Duel!.", MODULE_NAME, 0);

		break;

	}
	return TRUE;
}
