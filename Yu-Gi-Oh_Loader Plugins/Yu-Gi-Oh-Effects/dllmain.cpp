#include <Windows.h>
#include "HookDiagnostics.h"
#include <detours.h>
#include <iostream>
#include <vector>
#include <string>


#include "Fusion.h"
#include "Ritual.h"
#include "SynchroXyz.h"
#include "EffectClone.h"
#include "EffectDispatch.h"
#include "Lock.h"
#include "Logger.h"
#include "Config.h"
#include "Yu-Gi-Oh-Mods.h"
#include "Yu-Gi-Oh-Core.h"

// Effects runs on Master Duel's duel engine (docs/MasterDuelEngine.md), which Core loads ([Yu-Gi-Oh-Core] MasterDuelEngine=1, duel.dll in
// Plugins\Yu-Gi-Oh-Core). Core_EngineProc answers as soon as duel.dll is loaded (Core_EngineReady waits for its card data, which comes later).
static bool MasterDuelEngineLoaded()
{
    return Core::Load() && Core::Functions().EngineProc && Core::Functions().EngineProc("DLL_DuelSysAct") != nullptr;
}

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

        if (!MasterDuelEngineLoaded())
        {
            Logger::WriteLog("Effects can't load: Core's Master Duel engine is off. Set [Yu-Gi-Oh-Core] MasterDuelEngine=1 and put duel.dll in "
                "Plugins\\Yu-Gi-Oh-Core.", MODULE_NAME, 2);
            MessageBoxA(NULL, "Effects can't load: Core's Master Duel engine is off.\nSet MasterDuelEngine=1 under [Yu-Gi-Oh-Core] in Config.ini.",
                "Yu-Gi-Oh-Effects", MB_ICONERROR);
            break;
        }

        if (!HookDiagnostics::Begin("Effects", MODULE_NAME, Logger::WriteLog))
            break;

        // Effects are added by hooking, not by patching or copying the game's tables (see EffectClone.h and docs/EffectSystem.md).
        // The earlier table-relocation code (copy the effect tables into vectors, zero the originals, patch instruction bytes to a code
        // cave) was removed: it zeroed the tables the lookup needs and had a wrong size. It is in git history if it is ever wanted.

        Fusion::Setup();
        Ritual::Setup();
        SynchroXyz::Setup();
        EffectClone::Setup();
        EffectDispatch::Setup();
        Lock::Setup();
        HookDiagnostics::Commit("Effects", MODULE_NAME, Logger::WriteLog);
        YGO::Mods::ReleaseShared();   // the readers above are done with the shared cards.json / effects.json

        break;;
    }
    return TRUE;
}
