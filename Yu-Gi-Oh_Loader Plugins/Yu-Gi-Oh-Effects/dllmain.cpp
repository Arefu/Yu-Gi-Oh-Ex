#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <vector>
#include <string>

#include "Memory.h"
#include "Table_140B15B30.h"
#include "CardsThatMakeYouDraw.h"

#include "Effects.h"
#include "Logger.h"
#include "Config.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	Logger::SetupLogger();

	std::string Path = "C:\\";

	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		Path = Config::Get_WorkingDirectory();
		if (Path == "")
		{
			char Buffer[256];
			MessageBoxA(NULL, "Failed To Load/Parse Config.ini, Please Make Sure It Exists/ Is Setup And In The Same Directory As The Game!", "Yu-Gi-Oh-Effects", MB_ICONERROR);
		}
		Logger::WriteLog(std::format("Running from: {}", Path), MODULE_NAME, 0);
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());
		Table_140B15B30::Setup();
		Table_140B15B30::Patch();
		EffectTable_SpellAndTrap::SetupInternalEffectTable(0x140BA8730);

		CardsThatMakeYouDraw::Setup(std::format("{}{}", Path, "\\CardsThatMakeYouDraw\\CardsThatMakeYouDraw.json"));

		EffectTable_SpellAndTrap::InsertRedirects(0x140BA8730);
		DetourTransactionCommit();

		break;;
	}
	return TRUE;
}