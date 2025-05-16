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
uintptr_t te = 0x1407EB9E0;
typedef __int64(__fastcall* sub_14076BFC0)(unsigned __int16 a1, int a2);
__int64 __fastcall I_Dont_Know(unsigned short a1, int a2)
{
    //__int64 __fastcall sub_1407EB9E0(unsigned __int16 a1, int a2)
   // auto res = -((sub_14076BFC0)te)(a1, a2);
  //  std::cout << a1 << " - " << res;
    
    return 0;
}

void AddTrace()
{
    Tracer::Traces.push_back({ "YGO::DuelSetup::MP_Get_IsRoundBasedDuel", (void*)0x1407F1E80, (void*)YGO::DuelSetup::_D_MP_Get_IsRoundBasedDuel });
	Tracer::Traces.push_back({ "YGO::DuelUtility::Get_TutorialDuelIndex", (void*)0x140769180, (void*)YGO::DuelUtility::_D_Get_TutorialDuelIndex });
  //  Tracer::Traces.push_back({ "I don't know!", (void*)te, (void*)I_Dont_Know });
}

void SetupTrace()
{
    DetourRestoreAfterWith();

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    for (auto& _Trace : Tracer::Traces)
    {
        WriteLog("Tracing Execution Of : " + _Trace._Name, MODULE_NAME, 1);
        std::cout << "Tracing " << _Trace._Address << std::endl;
        auto res = DetourAttach((PVOID*)&_Trace._Address, _Trace._Detour);

        std::cout << "RES: " << res << std::endl;
    }

    DetourAttach((PVOID*)&te, I_Dont_Know);

	DetourTransactionCommit();
}
