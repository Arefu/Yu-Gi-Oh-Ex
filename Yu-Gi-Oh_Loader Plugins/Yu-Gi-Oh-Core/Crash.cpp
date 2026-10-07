#include "Crash.h"

#include <Windows.h>
#include <DbgHelp.h>
#include <cstring>
#include <format>
#include <fstream>
#include <string>

#pragma comment(lib, "Dbghelp.lib")

namespace
{
    // Only these are fatal on their own; the game raises plenty of harmless exceptions (C++ throws, thread names)
    // that used to overwrite the log with something useless.
    bool IsFatalCode(DWORD code)
    {
        return code == EXCEPTION_ACCESS_VIOLATION || code == EXCEPTION_ILLEGAL_INSTRUCTION ||
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
    void WriteStack(std::ofstream& log, const CONTEXT* source)
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

    LONG WINAPI CrashHandler(EXCEPTION_POINTERS* ExceptionInfo)
    {
        volatile LONG handling = 0;
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
}

namespace Crash
{
    void Install()
    {
        SetUnhandledExceptionFilter(CrashHandler);
        AddVectoredExceptionHandler(1, CrashHandler);
    }
}
