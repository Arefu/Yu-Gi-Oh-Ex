#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <string>
#include "Yu-Gi-Oh-Ex.h"

typedef __int64 (*OriginalPDEFunctionType)(__int64 a1, const char* a2);
typedef void (*SetLanguage)(__int64 a1);
typedef void (*Test)();
OriginalPDEFunctionType OriginalPDE = nullptr;
SetLanguage OriginalSetLanaguage = nullptr;

__int64 PDLimits = 0x0;
__int64 SetLang = 0x0;

BOOL Store = 1;
BOOL PATCHPDLimits = 1;
BOOL AutoPause = 1;
BOOL UseJP = 1;
BOOL NoJanken = 1;

static __int64 __fastcall Patch_UkLoading(__int64 a1, const char* a2)
{
	std::string File(a2);

	if (File == "bin/pd_limits.bin")
		a2 = "bin/CARD_Prop.bin"; //Setthing this to empty does not let the game continue.

	//Call Original function
	auto result = reinterpret_cast<HRESULT(__stdcall*)(__int64, const char*)>(PDLimits)(a1, a2);

	return a1;
}

void __fastcall Patch_DeductMoneyFromStoreTransaction(__int64 a1, const char* a2)
{
	return; //NO OPERATION
}

void __fastcall Patch_NoPause()
{
	return; //NO OPERATION
}

void __fastcall Patch_UseJP(__int64 a1)
{

	YuGiOhEx::g_bUseJpLogo = 255;
	return;
}

__int64 __fastcall Patch_DoJankenAndPlayerSelection(__int64 a1)
{
	//Currently bugged, it not reward player with finishing the duel.
	return 0; //NO OPERATION
}

void ProcessConfig();

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	long long Test = 0x140767AF0;
	ProcessConfig();

	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		std::cout << "[Yu-Gi-Oh-PatchMeOut] DLL_PROCESS_ATTACH - Ready to BREAK THINGS!" << std::endl;
		OriginalPDE = reinterpret_cast<OriginalPDEFunctionType>(YuGiOhEx::UnkFuncForLoading);
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		if (Store == true)
		{
			std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched Store." << std::endl;
			DetourAttach(&(PVOID&)YuGiOhEx::DeductMoneyFromStoreTransaction, Patch_DeductMoneyFromStoreTransaction);
		}
		if (PATCHPDLimits == true)
		{
			std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched PDLimits." << std::endl;
			DetourAttach(&(PVOID&)YuGiOhEx::UnkFuncForLoading, Patch_UkLoading);
		}
		if (AutoPause == true)
		{
			std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched AutoPause." << std::endl;
			DetourAttach(&(PVOID&)YuGiOhEx::AutoPauseOnLostFocus, Patch_NoPause);
		}
		if (UseJP == true)
		{
			std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched UseJP." << std::endl;
			DetourAttach(&(PVOID&)YuGiOhEx::UseJPLogo, Patch_UseJP);
		}
		if (NoJanken == true)
		{
			std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched Janken" << std::endl;
		//	DetourAttach(&(PVOID&)YuGiOhEx::JankenAndPlayerSelection, Patch_DoJankenAndPlayerSelection);
		}

		DetourTransactionCommit();
		PDLimits = YuGiOhEx::UnkFuncForLoading;
		SetLang = YuGiOhEx::UseJPLogo;

		break;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}

void ProcessConfig()
{
	Store = GetPrivateProfileIntW(L"Yu-Gi-Oh-PatchMeOut", L"FreeStore", 1, L".\\Config.ini");
	PATCHPDLimits = GetPrivateProfileIntW(L"Yu-Gi-Oh-PatchMeOut", L"NoBan", 1, L".\\Config.ini");
	AutoPause = GetPrivateProfileIntW(L"Yu-Gi-Oh-PatchMeOut", L"AutoPause", 1, L".\\Config.ini");
	UseJP = GetPrivateProfileIntW(L"Yu-Gi-Oh-PatchMeOut", L"UseJP", 0, L".\\Config.ini");

	NoJanken = GetPrivateProfileIntW(L"Yu-Gi-Oh-PatchMeOut", L"NoJanken", 1, L".\\Config.ini");
}
