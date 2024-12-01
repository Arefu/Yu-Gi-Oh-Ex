#include <Windows.h>
#include <detours.h>
#include <fstream>
#include <iostream>
BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		AllocConsole();
		FILE* consoleOut;
		freopen_s(&consoleOut, "CONOUT$", "w", stdout);
		SetWindowText(GetConsoleWindow(), L"Yu-Gi-Oh! Console");

		std::cout << "Yu-Gi-Oh! Console" << std::endl;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}

