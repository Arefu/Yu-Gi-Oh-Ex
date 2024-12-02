#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <string>
#include "Yu-Gi-Oh-Ex.h"


typedef __int64 (*OriginalPDEFunctionType)(__int64 a1, const char* a2);
OriginalPDEFunctionType OriginalPDE = nullptr;
__int64 PDLimits = 0x0;

unsigned __int64 __fastcall Patch_DeductMoneyFromStoreTransaction(__int64 a1, unsigned __int64 a2)
{
    return 0;
}
__int64 CallOriginalPDE(__int64 a1, const char *a2) {
    if (OriginalPDE) {
        return OriginalPDE(a1, a2);
    }
    return 0; 
}
__int64 __fastcall Patch_GetPDLimits(__int64 a1, const char* a2)
{
    std::cout << a2;
    return CallOriginalPDE(a1, a2);
}

BOOL APIENTRY DllMain( HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    __int64 PDE = YuGiOhEx::UnkFuncForLoading;

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
		std::cout << "[Yu-Gi-Oh-PatchMeOut] DLL_PROCESS_ATTACH - Ready to BREAK THINGS!" << std::endl;
        OriginalPDE = reinterpret_cast<OriginalPDEFunctionType>(PDE);
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched Store." << std::endl;
		DetourAttach(&(PVOID&)YuGiOhEx::DeductMoneyFromStoreTransaction, Patch_DeductMoneyFromStoreTransaction);

       
		std::cout << "[Yu-Gi-Oh-PatchMeOut] Patched PDLimits." << std::endl;
		DetourAttach(&(PVOID&)PDE, Patch_GetPDLimits);
		PDLimits = PDE;

        DetourTransactionCommit();
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

