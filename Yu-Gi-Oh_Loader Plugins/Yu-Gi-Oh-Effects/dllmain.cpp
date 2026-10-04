#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <vector>
#include <string>


#include "Fusion.h"
#include "Ritual.h"
#include "SynchroXyz.h"
#include "EffectClone.h"
#include "EffectDispatch.h"
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

        // Effects are added by hooking, not by patching or copying the game's tables (see EffectClone.h and docs/EffectSystem.md).
        // The earlier table-relocation code (copy the effect tables into vectors, zero the originals, patch instruction bytes to a code
        // cave) was removed: it zeroed the tables the lookup needs and had a wrong size. It is in git history if it is ever wanted.

        Fusion::Setup();
        Ritual::Setup();
        SynchroXyz::Setup();
        EffectClone::Setup();
        EffectDispatch::Setup();
        DetourTransactionCommit();

        break;;
    }
    return TRUE;
}
