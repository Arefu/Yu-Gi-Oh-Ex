#include <Windows.h>
#include <thread>
#include <iostream>
#include <string>
#include <vector>

#include "Yu-Gi-Oh-Ex.h"

void ProcessInput()
{
  
    do
    {
		std::string Input;
		std::getline(std::cin, Input);
		if (Input == "quit")
		{
			YuGiOhEx::g_bIsQuitReady = true;
		}
		else
		{
			std::cout << "[Yu-Gi-Oh-Console]: Invalid Command!" << std::endl;
		}
		std::cout << "[Yu-Gi-Oh-Console]: ";

    } while (!YuGiOhEx::g_bIsQuitReady);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        AllocConsole();
        FILE* consoleOut;
        freopen_s(&consoleOut, "CONOUT$", "w", stdout);
        freopen_s(&consoleOut, "CONOUT$", "w", stderr);
        freopen_s(&consoleOut, "CONIN$", "r", stdin);

        SetWindowText(GetConsoleWindow(), L"Yu-Gi-Oh! Console");

        std::cout << "[Yu-Gi-Oh-Console]: Ready!" << std::endl;

      //  std::thread(ProcessInput).detach();
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
