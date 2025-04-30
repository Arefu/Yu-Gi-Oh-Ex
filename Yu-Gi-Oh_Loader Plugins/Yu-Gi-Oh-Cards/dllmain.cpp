#include <Windows.h>
#include <iostream>
#include <string>
#include <vector>
#include <format>

#include "detours.h"
#include "Cards.h"
#include "Logger.h"
#include "Memory.h"
#include "Targets.h"

void SetupInternalIDJumpCalls()
{
	//Internal IDs.
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D11E), INTERNAL_CARD_ID_LOCATION, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D09E), INTERNAL_CARD_ID_LOCATION, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D44B), INTERNAL_CARD_ID_LOCATION, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D4B5), INTERNAL_CARD_ID_LOCATION, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D5D8), INTERNAL_CARD_ID_LOCATION, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D668), INTERNAL_CARD_ID_LOCATION, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D6B8), INTERNAL_CARD_ID_LOCATION, true);
}
void SetupRedirectionForInternalIDs()
{
	Memory::EmplaceMOV(reinterpret_cast<void*>(INTERNAL_CARD_ID_LOCATION), reinterpret_cast<uintptr_t>(Cards::InternalIDs.data()), Memory::X64Register::RCX, true);
	Memory::EmplaceRET(reinterpret_cast<void*>(INTERNAL_CARD_ID_LOCATION + 10), true);
}

void SetupCardIDJumpCalls()
{
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076C0A9), KONAMI_CARD_ID_LOCATION, true);
	Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D7FA), KONAMI_CARD_ID_LOCATION, true);
}
void SetupRedirectionForCardIDs()
{
	Memory::EmplaceMOV(reinterpret_cast<void*>(KONAMI_CARD_ID_LOCATION), reinterpret_cast<uintptr_t>(Cards::CardIDs.data()), Memory::X64Register::RDI, true);
	Memory::EmplaceRET(reinterpret_cast<void*>(KONAMI_CARD_ID_LOCATION + 10), true);
}

void SetupLimitRemover()
{
	Memory::EmplaceNOP(reinterpret_cast<void*>(0x140753BB1), true, 5);
	Memory::EmplaceNOP(reinterpret_cast<void*>(0x140753BB6), true, 2);
	Sleep(6000);
}

void SetupDetours()
{
	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());
	DetourAttach(&(PVOID&)Cards::orig_getInternalCardID, Cards::Get_InternalID);
	DetourAttach(&(PVOID&)Cards::orig_getKonamiCardID, Cards::Get_KonamiID);


	DetourTransactionCommit();

}



//Make a Detour for char __fastcall sub_14076BFC0(const void **a1, Language Lang)
typedef char(__fastcall* sub_14076BFC0)(const void** a1, int Lang);
uintptr_t orig_sub_14076BFC0 = 0x14076BFC0;
char __fastcall _sub_14076BFC0(const void** a1, int Lang)
{
	//Call the original function
	((sub_14076BFC0)orig_sub_14076BFC0)(a1, Lang);

	memcpy(Cards::CardIDs.data(), reinterpret_cast<void*>(KONAMI_CARD_ID_LOCATION), 10168 * sizeof(unsigned __int16));
    Logger::WriteLog(std::format("Setup {} Card IDs, At: {}", Cards::CardIDs.size(), reinterpret_cast<void*>(Cards::CardIDs.data())), MODULE_NAME, 0);
    memcpy(Cards::InternalIDs.data(), reinterpret_cast<void*>(INTERNAL_CARD_ID_LOCATION), 11072 * sizeof(unsigned __int16));
    Logger::WriteLog(std::format("Setup {} Internal IDs, At: {}", Cards::InternalIDs.size(), reinterpret_cast<void*>(Cards::InternalIDs.data())), MODULE_NAME, 0);
    Logger::WriteLog("Wiping Original Memory Locations for Internal and KonamiCard IDs.", MODULE_NAME, 0);
	memset(reinterpret_cast<void*>(KONAMI_CARD_ID_LOCATION), 0, 10168);
	memset(reinterpret_cast<void*>(INTERNAL_CARD_ID_LOCATION), 0, 11072);

	Cards::CardIDs.push_back(14969);
	Cards::InternalIDs.push_back(10166);

	Logger::WriteLog("Writing Jump Calls For Internal IDs Graveyard", MODULE_NAME, 0);
	SetupInternalIDJumpCalls();
	Logger::WriteLog("Seting Redirection For Internal IDs", MODULE_NAME, 0);
	SetupRedirectionForInternalIDs();

	Logger::WriteLog("Writing Jump Calls For Card IDs Graveyard", MODULE_NAME, 0);
	SetupCardIDJumpCalls();
	Logger::WriteLog("Setup Card IDs Jump Calls", MODULE_NAME, 0);
	SetupRedirectionForCardIDs();

	SetupLimitRemover();

	SetupDetours();

	Cards::isHooked = true;

	return 0;
}
BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		DetourRestoreAfterWith();

		Logger::SetupLogger();
		
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		DetourAttach(&(PVOID&)orig_sub_14076BFC0, _sub_14076BFC0);

		DetourTransactionCommit();

		Logger::WriteLog("It's Time To Du-Du-Du-Duel!.", MODULE_NAME, 0);

		break;

	}
	return TRUE;
}
