#include <Windows.h>
#include <detours.h>
#include <vector>
#include <iostream>
#include <string>

#include "Tracer.h"

#include "YGO_DuelSetup.h"
#include "YGO_DuelUtility.h"

#define MODULE_NAME "Yu-Gi-Oh-Tracer"
void _WriteLog(std::string Message, std::string Module, int LogLevel) {}
void (*WriteLog)(std::string Message, std::string Module, int LogLevel) = _WriteLog;
void SetupLogger()
{
    WriteLog = (void (*)(std::string, std::string, int))GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-Console.dll"), "WriteLog");
    if (WriteLog == nullptr)
    {
        WriteLog = _WriteLog;
    }
}

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
}

void SetupTrace()
{
    DetourRestoreAfterWith();

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    for (auto& _Trace : Tracer::Traces)
    {
        WriteLog("Tracing Execution Of : " + _Trace._Name, MODULE_NAME, 1);
        DetourAttach((PVOID*)&_Trace._Address, _Trace._Detour);
    }

	DetourTransactionCommit();
}
