#include <Windows.h>
#include <detours.h>
#include <vector>
#include <iostream>
#include <string>

#include "Tracer.h"

#include "YGO_DuelSetup.h"
#include "YGO_DuelUtility.h"

std::vector<Trace> Tracer::Traces;

void AddTrace();
void SetupTrace();

BOOL APIENTRY DllMain( HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
		AddTrace();
        SetupTrace();
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}



void AddTrace()
{
    Tracer::Traces.push_back({ "YGO::DuelSetup::MP_Get_IsRoundBasedDuel", (void*)0x1407F1E80, (void*)YGO::DuelSetup::_D_MP_Get_IsRoundBasedDuel });
	Tracer::Traces.push_back({ "YGO::DuelUtility::Get_TutorialDuelIndex", (void*)0x140769180, (void*)YGO::DuelUtility::_D_Get_TutorialDuelIndex });
    Tracer::Traces.push_back({ "YGO::DuelSetup::Do_JankenMiniGame", (void*)0x1407AFD80, (void*)YGO::DuelSetup::_D_DoJankenMiniGame });
}

void SetupTrace()
{
    DetourRestoreAfterWith();

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    for (auto& _Trace : Tracer::Traces)
    {
        std::cout << "[Yu-Gi-Oh-Tracer]: Tracing Execution Of: " << _Trace._Name << std::endl;
        DetourAttach((PVOID*)&_Trace._Address, _Trace._Detour);
    }

	DetourTransactionCommit();


}
