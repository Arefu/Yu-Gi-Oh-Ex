#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <string>
#include "Yu-Gi-Oh-Ex.h"


typedef __int64 (*OriginalPDEFunctionType)(__int64 a1, const char* a2);
OriginalPDEFunctionType OriginalPDE = nullptr;
__int64 PDLimits = 0x0;

static __int64 __fastcall Patch_UkLoading(__int64 a1, const char* a2)
{
    std::string File(a2);

    if (File == "bin/pd_limits.bin")
    {
		std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched PDLimits." << std::endl;
        a2 = "bin/CARD_Prop.bin"; //Setthing this to empty does not let the game continue.
    }
    //Call Original function
    auto result = reinterpret_cast<HRESULT(__stdcall*)(__int64, const char*)>(PDLimits)(a1, a2);

    return a1;
}

 void __fastcall Patch_DeductMoneyFromStoreTransaction(__int64 a1, const char *a2)
{
    return; //NO OPERATION
}


BOOL APIENTRY DllMain( HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
		std::cout << "[Yu-Gi-Oh-PatchMeOut] DLL_PROCESS_ATTACH - Ready to BREAK THINGS!" << std::endl;
        OriginalPDE = reinterpret_cast<OriginalPDEFunctionType>(YuGiOhEx::UnkFuncForLoading);
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched Store." << std::endl;
		std::cout << "[Yu-Gi-Oh-PatchMeOut] Store Address: " << std::hex << std::uppercase << "0x" << YuGiOhEx::DeductMoneyFromStoreTransaction << std::endl;
		DetourAttach(&(PVOID&)YuGiOhEx::DeductMoneyFromStoreTransaction, Patch_DeductMoneyFromStoreTransaction);

       
		std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched PDLimits." << std::endl;
        DetourAttach(&(PVOID&)YuGiOhEx::UnkFuncForLoading, Patch_UkLoading);

        DetourTransactionCommit();
		PDLimits = YuGiOhEx::UnkFuncForLoading;
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

