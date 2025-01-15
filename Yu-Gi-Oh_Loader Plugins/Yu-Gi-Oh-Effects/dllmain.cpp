#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <string>

#include "Yu-Gi-Oh.h"

#include "Effects.h"
#include "Config.h"



BOOL APIENTRY DllMain( HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    ;
    std::string Path = "C:\\";

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        return 1;
        Path = Config::Get_WorkingDirectory(); //We Don't Want Content To Be Loaded From The Wrong Directory, This Will Be Where *ALL* Stuff Goes.
        if(Path == "")
		{
			char Buffer[256];
			MessageBoxA(NULL, "Failed To Load/Parse Config.ini, Please Make Sure It Exists/ Is Setup And In The Same Directory As The Game!", "Yu-Gi-Oh-Effects", MB_ICONERROR);
		}
        
		std::cout << "[Yu-Gi-Oh-Effects]: Loading From: " << Path << std::endl;

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());


		  DetourAttach(reinterpret_cast<PVOID*>(&Effects_Functions::o_GetNumberOfCardsToDraw), Effects::h_FindDrawAmountForCard);
       
		DetourTransactionCommit();

        break;;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
