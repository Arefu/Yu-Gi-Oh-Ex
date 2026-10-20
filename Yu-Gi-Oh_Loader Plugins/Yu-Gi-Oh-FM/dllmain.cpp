#include <Windows.h>
#include <format>

#include "Detours.h"
#include "Fusion.h"
#include "Logger.h"
#include "Rules.h"

// Yu-Gi-Oh-FM: Yu-Gi-Oh! Forbidden Memories' duel rules, for the Forbidden Memories mod (its fm.json holds FM's cards and tables).
//   Fusion.cpp -> fusion from the hand, step by step, without Polymerization
//   Rules.cpp  -> no Tributes ([Yu-Gi-Oh-FM] NoTributes)
// Still to come: Guardian Stars, FM field and equip boosts, drops and starchips.

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
    {
        Logger::SetupLogger();
        Logger::WriteLog("Yu-Gi-Oh-FM starting", MODULE_NAME, 0);

        DetourRestoreAfterWith();
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        Rules::Attach();
        Fusion::Attach();
        const LONG error = DetourTransactionCommit();
        Logger::WriteLog(std::format("FM hooks attached: {}", error), MODULE_NAME, error == 0 ? 0 : 2);
    }
    return TRUE;
}
