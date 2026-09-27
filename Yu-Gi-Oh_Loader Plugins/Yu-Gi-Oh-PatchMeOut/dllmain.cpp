#include <Windows.h>
#include <DbgHelp.h>
#include <detours.h>
#include <iostream>
#include <fstream>
#include <string>
#include <format>
#include "Yu-Gi-Oh-Ex.h"
#include "Logger.h"

#pragma comment(lib, "Dbghelp.lib")

typedef __int64 (*OriginalPDEFunctionType)(__int64 a1, const char* a2);
typedef void (*SetLanguage)(__int64 a1);
OriginalPDEFunctionType OriginalPDE = nullptr;
SetLanguage OriginalSetLanaguage = nullptr;

__int64 PDLimits = 0x0;
__int64 SetLang = 0x0;

BOOL Store = 1;
BOOL PATCHPDLimits = 1;
BOOL AutoPause = 1;
BOOL UseJP = 1;
BOOL NoJanken = 1;
INT SetLP = 8000;

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
// Only these are fatal on their own; the game raises plenty of harmless exceptions (C++ throws, thread names)
// that used to overwrite the log with something useless.
static bool IsFatalCode(DWORD code)
{
    return code == EXCEPTION_ACCESS_VIOLATION || code == EXCEPTION_ILLEGAL_INSTRUCTION ||
        code == EXCEPTION_INT_DIVIDE_BY_ZERO || code == 0xC0000374 /* heap corruption */ ||
        code == 0xC0000409 /* fail fast */ || code == 0xC0000417 /* invalid parameter */;
}

// "Module.dll+0x1234" for an address, or the bare address when it isn't in a module.
static std::string DescribeAddress(uintptr_t address)
{
    HMODULE module = nullptr;
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCSTR>(address), &module) && module)
    {
        char path[MAX_PATH]{};
        GetModuleFileNameA(module, path, MAX_PATH);
        const char* name = strrchr(path, '\\');
        return std::format("{}+0x{:X}", name ? name + 1 : path, address - reinterpret_cast<uintptr_t>(module));
    }
    return std::format("0x{:X}", address);
}

// Walks the faulting thread's stack from the exception context using the unwind data.
static void WriteStack(std::ofstream& log, const CONTEXT* source)
{
    CONTEXT context = *source;
    log << "Stack:\n";
    for (int frame = 0; frame < 64 && context.Rip; ++frame)
    {
        log << "  " << DescribeAddress(context.Rip) << "\n";

        DWORD64 imageBase = 0;
        RUNTIME_FUNCTION* function = RtlLookupFunctionEntry(context.Rip, &imageBase, nullptr);
        if (!function)
        {
            // Leaf function without unwind data: the return address is at the top of the stack.
            context.Rip = *reinterpret_cast<DWORD64*>(context.Rsp);
            context.Rsp += 8;
            continue;
        }

        void* handlerData = nullptr;
        DWORD64 establisher = 0;
        RtlVirtualUnwind(UNW_FLAG_NHANDLER, imageBase, context.Rip, function, &context, &handlerData, &establisher, nullptr);
    }
}

LONG WINAPI CrashHandler(EXCEPTION_POINTERS* ExceptionInfo) {
    static volatile LONG handling = 0;
    const DWORD code = ExceptionInfo->ExceptionRecord->ExceptionCode;
    if (!IsFatalCode(code) || InterlockedCompareExchange(&handling, 1, 0) != 0)
        return EXCEPTION_CONTINUE_SEARCH;

    std::ofstream log("crash_log.txt");
    const uintptr_t address = reinterpret_cast<uintptr_t>(ExceptionInfo->ExceptionRecord->ExceptionAddress);
    log << "Crash Address: 0x" << std::hex << address << "\n";
    log << "Exception Code: 0x" << std::hex << code << "\n";
    log << "Crash Location: " << DescribeAddress(address) << "\n";
    WriteStack(log, ExceptionInfo->ContextRecord);

    // A minidump next to the game (open it in Visual Studio) shows every thread and the heap state.
    HANDLE file = CreateFileA("crash.dmp", GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file != INVALID_HANDLE_VALUE)
    {
        MINIDUMP_EXCEPTION_INFORMATION info{ GetCurrentThreadId(), ExceptionInfo, FALSE };
        MiniDumpWriteDump(GetCurrentProcess(), GetCurrentProcessId(), file,
            static_cast<MINIDUMP_TYPE>(MiniDumpWithThreadInfo | MiniDumpWithIndirectlyReferencedMemory), &info, nullptr, nullptr);
        CloseHandle(file);
        log << "Minidump: crash.dmp\n";
    }

    log.close();
    InterlockedExchange(&handling, 0);

    return EXCEPTION_CONTINUE_SEARCH;
}

void __fastcall Patch_DoJankenAndPlayerSelection(__int64 a1)
{
    //Currently bugged, it not reward player with finishing the duel.
    return; //NO OPERATION
}

void Patch_SetLPToCustomValue()
{
    using SetLPFunc = void(*)();
    ((SetLPFunc)YuGiOhEx::SetLP)();

    *(int*)0x140C8D370 = SetLP;
}

void ProcessConfig();

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    Logger::SetupLogger();
    ProcessConfig();
    SetUnhandledExceptionFilter(CrashHandler);
    AddVectoredExceptionHandler(1, CrashHandler);
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        Logger::WriteLog("DLL_PROCESS_ATTACH - Ready to BREAK THINGS!", MODULE_NAME, 0);
        OriginalPDE = reinterpret_cast<OriginalPDEFunctionType>(YuGiOhEx::UnkFuncForLoading);
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());

        if (Store == true)
        {
            Logger::WriteLog("Patched Store.", MODULE_NAME, 0);
            DetourAttach(&(PVOID&)YuGiOhEx::DeductMoneyFromStoreTransaction, Patch_DeductMoneyFromStoreTransaction);
        }
        if (PATCHPDLimits == true)
        {
            Logger::WriteLog("Patched PDLimits.", MODULE_NAME, 0);
            DetourAttach(&(PVOID&)YuGiOhEx::UnkFuncForLoading, Patch_UkLoading);
        }
        if (AutoPause == true)
        {
            Logger::WriteLog("Patched AutoPause.", MODULE_NAME, 0);; static void* pAutoPause = (void*)0x14083C9F0;
            DetourAttach(&pAutoPause, Patch_NoPause);
        }
        if (UseJP == true)
        {
            Logger::WriteLog("Patched UseJP.", MODULE_NAME, 0);;
            DetourAttach(&(PVOID&)YuGiOhEx::UseJPLogo, Patch_UseJP);
        }
        if (NoJanken == true)
        {
            Logger::WriteLog("Patched Janken", MODULE_NAME, 0);;
            DetourAttach(&(PVOID&)YuGiOhEx::JankenAndPlayerSelection, Patch_DoJankenAndPlayerSelection);
        }
        if (SetLP != 8000)
        {
            Logger::WriteLog(std::format("Changed LP To {}", SetLP), MODULE_NAME, 0);
            DetourAttach(&(PVOID&)YuGiOhEx::SetLP, Patch_SetLPToCustomValue);
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

    SetLP = GetPrivateProfileIntW(L"Yu-Gi-Oh-PatchMeOut", L"StartingLP", 8000, L".\\Config.ini");
}
