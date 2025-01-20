#include <iostream>
#include <queue>
#include <string>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <atomic>
#include <vector>

#include "conmanip.h"
using namespace conmanip;
console_out_context ctxout;
console_out conout(ctxout);

#define MODULE_NAME "Yu-Gi-Oh-Console"

extern "C" __declspec(dllexport)
void WriteLog(std::string Message, std::string Module, int LogLevel)
{
	if (Module == "")
        std::cout << settextcolor(console_text_colors::light_yellow) 
        << "[" << MODULE_NAME << "]: " 
        << settextcolor(console_text_colors::white) 
        << "Doesn't Have An Owner Module! Please Set One!\n";

    switch (LogLevel)
    {
    case 0: //Informational.
		std::cout << "[" << settextcolor(console_text_colors::light_blue) << Module  << settextcolor(console_text_colors::white) << "]: " << Message <<  "\r\n";
        break;
	case 1: //Warning.
		std::cout << "[" << settextcolor(console_text_colors::light_yellow) << Module << settextcolor(console_text_colors::white) << "]: " << Message <<  "\r\n";
		break;
	case 2: //Error.
		std::cout << "[" << settextcolor(console_text_colors::light_red) << Module << settextcolor(console_text_colors::white) << "]: " << Message <<  "\r\n";
		break;
	case 69: //Debug.
		std::cout << "[";
		for (int i = 0; i < Module.length(); i++)
		{
			switch (i % 7)
			{
			case 0:
				std::cout << settextcolor(console_text_colors::red) << Module[i];
				break;
			case 1:
				std::cout << settextcolor(console_text_colors::yellow) << Module[i];
				break;
			case 2:
				std::cout << settextcolor(console_text_colors::green) << Module[i];
				break;
			case 3:
				std::cout << settextcolor(console_text_colors::cyan) << Module[i];
				break;
			case 4:
				std::cout << settextcolor(console_text_colors::blue) << Module[i];
				break;
			case 5:
				std::cout << settextcolor(console_text_colors::magenta) << Module[i];
				break;
			case 6:
				std::cout << settextcolor(console_text_colors::white) << Module[i];
				break;
			}
		}
		std::cout << settextcolor(console_text_colors::white) << "]: " << Message << "\r\n";
		break;
	default:
		std::cout << "[" << settextcolor(console_text_colors::green) << Module << settextcolor(console_text_colors::white) << "]: " << Message << "\r\n";
		break;
    }
    ctxout.restore(console_cleanup_options::restore_attibutes);
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

		WriteLog("Ready!", MODULE_NAME, 0);

      //  std::thread(ProcessInput).detach();
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
