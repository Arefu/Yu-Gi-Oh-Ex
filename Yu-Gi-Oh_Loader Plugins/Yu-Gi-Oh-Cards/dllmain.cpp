#include <Windows.h>
#include <string>

#include "detours.h"

//Yu-Gi-Oh-Console Requirements.
#define MODULE_NAME "Yu-Gi-Oh-MoreCards"
void _WriteLog(std::string Message, std::string Module, int LogLevel){}
void (*WriteLog)(std::string Message, std::string Module, int LogLevel) = _WriteLog;
void SetupLogger()
{
	WriteLog = (void (*)(std::string, std::string, int))GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-Console.dll"), "WriteLog");
	if (WriteLog == nullptr)
	{
		WriteLog = _WriteLog;
	}
}

//Functions We're Hooking.
//uintptr_t _14076D0F0 = 0x14076D0F0;
uintptr_t _1407CA820 = 0x1407CA820;
uintptr_t _14076D070 = 0x14076D070;
uintptr_t _14076D480 = 0x14076D5B0;
uintptr_t _14081A080 = 0x14081A080;

//Function Definitions For Hooked Functions.

BOOL __fastcall Get_IsCardIDValid(__int16 a1)
{
	bool result = ((bool(__fastcall*)(__int16))_1407CA820)(a1);
	return result;
}
LPCTSTR* __fastcall Get_CardDescFromID(__int16 a1)
{
	LPCTSTR* result = ((LPCTSTR * (__fastcall*)(__int16))_14076D070)(a1);
	return result;
}


__int64 __fastcall sub_14076D5B0(__int16 a1)
{
	auto result = ((__int64(__fastcall*)(unsigned __int16))_14076D480)(a1);
	return result;
}


BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		SetupLogger();

		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());


		DetourAttach(&(PVOID&)_1407CA820, Get_IsCardIDValid);
		DetourAttach(&(PVOID&)_14076D070, Get_CardDescFromID);

		DetourTransactionCommit();
	
		WriteLog("It's Time To Du-Du-Du-Duel!.", MODULE_NAME, 0);
		SetupLogger();
		break;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}
