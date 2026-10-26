#include "Crash.h"

#include <Windows.h>
#include <DbgHelp.h>
#include <csignal>
#include <cstring>
#include <exception>
#include <format>
#include <fstream>
#include <intrin.h>
#include <sstream>
#include <string>

#include "Detours.h"
#include "Logger.h"

#pragma comment(lib, "Dbghelp.lib")

namespace
{
    // Only these are fatal on their own; the game raises plenty of harmless exceptions (C++ throws, thread names)
    // that used to overwrite the log with something useless.
    bool IsFatalCode(DWORD code)
    {
        return code == EXCEPTION_ACCESS_VIOLATION || code == EXCEPTION_ILLEGAL_INSTRUCTION || code == EXCEPTION_STACK_OVERFLOW ||
            code == EXCEPTION_INT_DIVIDE_BY_ZERO || code == 0xC0000374 /* heap corruption */ ||
            code == 0xC0000409 /* fail fast */ || code == 0xC0000417 /* invalid parameter */;
    }

    // "Module.dll+0x1234" for an address, or the bare address when it isn't in a module.
    std::string DescribeAddress(uintptr_t address)
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
    void WriteStack(std::ostream& log, const CONTEXT* source)
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

    // ---- abort / terminate (an uncaught C++ exception, a Debug assertion's Abort): no SEH exception reaches CrashHandler for those, and
    // the game just vanished (2026-10-25). The plugins share the CRT (/MD(d)), so one SIGABRT handler sees every module's abort; it writes
    // the stack at the abort and the last C++ exception thrown (an uncaught one is still alive then: terminate runs before unwinding).
    constexpr DWORD kCppException = 0xE06D7363;
    const ULONG_PTR* volatile g_LastThrow = nullptr;   // the last C++ throw's ExceptionInformation (object, ThrowInfo, image base)
    ULONG_PTR g_LastThrowInfo[4]{};
    DWORD g_LastThrowThread = 0;

    // MSVC x64 throw data, every pointer an offset from the throwing module's base.
    struct CatchableType { DWORD Properties; int Type; int Mdisp, Pdisp, Vdisp; int Size; int Copy; };
    struct TypeDescriptor { void* Vftable; void* Spare; char Name[1]; };

    // The thrown type's decorated name (".?AVtype_error@detail@nlohmann@@") and, for a std::exception, what() (copied). No C++ objects
    // here: __try can't sit beside them.
    bool ReadThrow(const ULONG_PTR* info, char* type, char* message, size_t size)
    {
        __try
        {
            const auto base = static_cast<uintptr_t>(info[3]);
            const auto* throwInfo = reinterpret_cast<const int*>(info[2]);
            const auto* types = reinterpret_cast<const int*>(base + throwInfo[3]);   // CatchableTypeArray: count, then types
            for (int i = 0; i < types[0] && i < 16; ++i)
            {
                const auto* catchable = reinterpret_cast<const CatchableType*>(base + types[1 + i]);
                const auto* descriptor = reinterpret_cast<const TypeDescriptor*>(base + catchable->Type);
                if (!type[0])
                    strncpy_s(type, size, descriptor->Name, _TRUNCATE);
                if (std::strcmp(descriptor->Name, ".?AVexception@std@@") == 0)
                    strncpy_s(message, size, reinterpret_cast<const std::exception*>(info[1] + catchable->Mdisp)->what(), _TRUNCATE);
            }
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    std::string DescribeThrow(const ULONG_PTR* info)
    {
        char type[512]{}, message[512]{};
        if (!ReadThrow(info, type, message, sizeof(type)))
            return "(could not be read)";
        return message[0] ? std::string(type) + " - " + message : std::string(type);
    }

    void WriteDump(std::ofstream& log, EXCEPTION_POINTERS* pointers);

    // console.log is written in batches: without this the last lines before a crash were lost (Yu-Gi-Oh-Console FlushAllLogs).
    void FlushConsoleLog()
    {
        if (const HMODULE console = GetModuleHandleA("Yu-Gi-Oh-Console.dll"))
            if (const auto flush = reinterpret_cast<void(__cdecl*)()>(GetProcAddress(console, "FlushAllLogs")))
                flush();
    }

    void __cdecl AbortHandler(int)
    {
        FlushConsoleLog();
        std::ofstream log("crash_log.txt");
        log << "abort() / std::terminate (an uncaught C++ exception, or a Debug assertion's Abort)\n";
        if (g_LastThrow)
            log << "Last C++ exception (thread " << std::dec << g_LastThrowThread << ", this thread " << GetCurrentThreadId() << "): "
                << DescribeThrow(g_LastThrowInfo) << "\n";
        CONTEXT context{};
        RtlCaptureContext(&context);
        WriteStack(log, &context);
        EXCEPTION_RECORD record{};
        record.ExceptionCode = 0xC0000409;
        record.ExceptionAddress = reinterpret_cast<void*>(context.Rip);
        EXCEPTION_POINTERS pointers{ &record, &context };
        WriteDump(log, &pointers);
    }

    LONG WINAPI CrashHandler(EXCEPTION_POINTERS* ExceptionInfo)
    {
        volatile LONG handling = 0;
        const DWORD code = ExceptionInfo->ExceptionRecord->ExceptionCode;
        if (code == kCppException && ExceptionInfo->ExceptionRecord->NumberParameters >= 4)
        {
            // only kept: most throws are caught (the game's and nlohmann's); AbortHandler reports it if one wasn't
            std::memcpy(g_LastThrowInfo, ExceptionInfo->ExceptionRecord->ExceptionInformation, sizeof(g_LastThrowInfo));
            g_LastThrowThread = GetCurrentThreadId();
            g_LastThrow = g_LastThrowInfo;
            return EXCEPTION_CONTINUE_SEARCH;
        }
        if (!IsFatalCode(code) || InterlockedCompareExchange(&handling, 1, 0) != 0)
            return EXCEPTION_CONTINUE_SEARCH;

        FlushConsoleLog();
        std::ofstream log("crash_log.txt");
        const uintptr_t address = reinterpret_cast<uintptr_t>(ExceptionInfo->ExceptionRecord->ExceptionAddress);
        log << "Crash Address: 0x" << std::hex << address << "\n";
        log << "Exception Code: 0x" << std::hex << code << "\n";
        log << "Crash Location: " << DescribeAddress(address) << "\n";
        WriteStack(log, ExceptionInfo->ContextRecord);
        WriteDump(log, ExceptionInfo);
        log.close();
        InterlockedExchange(&handling, 0);

        return EXCEPTION_CONTINUE_SEARCH;
    }

    // A minidump next to the game (open it in Visual Studio) shows every thread and the heap state.
    void WriteDump(std::ofstream& log, EXCEPTION_POINTERS* pointers)
    {
        HANDLE file = CreateFileA("crash.dmp", GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
            return;
        MINIDUMP_EXCEPTION_INFORMATION info{ GetCurrentThreadId(), pointers, FALSE };
        MiniDumpWriteDump(GetCurrentProcess(), GetCurrentProcessId(), file,
            static_cast<MINIDUMP_TYPE>(MiniDumpWithThreadInfo | MiniDumpWithIndirectlyReferencedMemory), &info, nullptr, nullptr);
        CloseHandle(file);
        log << "Minidump: crash.dmp\n";
    }
}

namespace
{
    // ---- the game going away without an exception (2026-10-25: it vanished twice, no crash log, no Windows error report, console.log cut
    // mid-line). Every way out of the process is logged to console.log with the caller's stack, then the log is flushed: ExitProcess (a
    // normal quit goes through it too), TerminateProcess on ourselves, the game's own quit request (RIX::App::RequestQuit) and the console
    // window being closed / Ctrl+C / log-off (they end the process without DLL_PROCESS_DETACH, so nothing else is flushed).
    void LogExit(const std::string& reason)
    {
        CONTEXT context{};
        RtlCaptureContext(&context);
        std::ostringstream stack;
        WriteStack(stack, &context);
        Logger::WriteLog(reason + "\n" + stack.str(), MODULE_NAME, 1);
        FlushConsoleLog();
    }

    decltype(&ExitProcess) orig_ExitProcess = ExitProcess;
    decltype(&TerminateProcess) orig_TerminateProcess = TerminateProcess;
    using RequestQuit_t = void(__fastcall*)(void* instance);
    RequestQuit_t orig_RequestQuit = reinterpret_cast<RequestQuit_t>(0x1408189C0);   // RIX::App::RequestQuit

    void WINAPI Hook_ExitProcess(UINT code)
    {
        LogExit(std::format("The game is exiting: ExitProcess({}) called from {}", code, DescribeAddress(reinterpret_cast<uintptr_t>(_ReturnAddress()))));
        orig_ExitProcess(code);
    }

    BOOL WINAPI Hook_TerminateProcess(HANDLE process, UINT code)
    {
        if (process == GetCurrentProcess() || GetProcessId(process) == GetCurrentProcessId())
            LogExit(std::format("The game is being terminated: TerminateProcess(self, {}) called from {}", code, DescribeAddress(reinterpret_cast<uintptr_t>(_ReturnAddress()))));
        return orig_TerminateProcess(process, code);
    }

    void __fastcall Hook_RequestQuit(void* instance)
    {
        LogExit("The game was asked to quit (RIX::App::RequestQuit) from " + DescribeAddress(reinterpret_cast<uintptr_t>(_ReturnAddress())));
        orig_RequestQuit(instance);
    }

    BOOL WINAPI ConsoleCtrl(DWORD type)
    {
        static const char* const kNames[] = { "Ctrl+C", "Ctrl+Break", "the console window was closed", "?", "?", "log-off", "shut-down" };
        Logger::WriteLog(std::format("Console event: {} - the game ends now", type < 7 ? kNames[type] : "?"), MODULE_NAME, 1);
        FlushConsoleLog();
        return FALSE;   // the default handling (the process ends)
    }
}

namespace Crash
{
    void Install()
    {
        SetUnhandledExceptionFilter(CrashHandler);
        AddVectoredExceptionHandler(1, CrashHandler);
        signal(SIGABRT, AbortHandler);   // abort / terminate: see AbortHandler
        SetConsoleCtrlHandler(ConsoleCtrl, TRUE);
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&reinterpret_cast<PVOID&>(orig_ExitProcess), Hook_ExitProcess);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_TerminateProcess), Hook_TerminateProcess);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_RequestQuit), Hook_RequestQuit);
        if (const LONG error = DetourTransactionCommit())
            Logger::WriteLog(std::format("Exit logging hooks: {}", error), MODULE_NAME, 2);
    }
}
