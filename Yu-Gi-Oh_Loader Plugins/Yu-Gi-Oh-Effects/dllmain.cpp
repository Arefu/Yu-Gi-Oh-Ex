#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <vector>
#include <string>

#include "Memory.h"
#include "CardsThatMakeYouDraw.h"

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

		Logger::WriteLog("Start Copying Table for CardsThatMakeYouDraw", MODULE_NAME, 0);
		Setup_CardsThatMakeYouDraw(std::format("{}{}", Path, "\\CardsThatMakeYouDraw\\CardsThatMakeYouDraw.json"));
		Logger::WriteLog("Done Copying Table for CardsThatMakeYouDraw", MODULE_NAME, 0);
		DetourTransactionCommit();

		break;;
	}
	return TRUE;
}