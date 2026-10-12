#pragma once
#include <windows.h>
#include <detours.h>
#include <sstream>
#include <string>

namespace HookDiagnostics
{
    using Log = void(__cdecl*)(std::string, std::string, int);

    inline void Failure(const char* group, const char* stage, LONG error, PVOID* failed, const char* module, Log log)
    {
        char description[512] = {};
        FormatMessageA(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, nullptr,
            error, 0, description, sizeof(description), nullptr);
        std::ostringstream message;
        message << group << ": hook installation failed during " << stage << " (Windows error " << error << ")";
        if (failed) message << ", original-pointer slot " << failed;
        if (description[0]) message << ": " << description;
        message << ". This hook group was not installed.";
        OutputDebugStringA((std::string(module) + ": " + message.str() + "\n").c_str());
        log(message.str(), module, 2);
    }

    inline bool Begin(const char* group, const char* module, Log log)
    {
        LONG error = DetourTransactionBegin();
        if (error != NO_ERROR)
        { Failure(group, "transaction begin", error, nullptr, module, log); return false; }
        error = DetourUpdateThread(GetCurrentThread());
        if (error != NO_ERROR)
        {
            DetourTransactionAbort();
            Failure(group, "thread update", error, nullptr, module, log);
            return false;
        }
        return true;
    }

    inline LONG Commit(const char* group, const char* module, Log log)
    {
        PVOID* failed = nullptr;
        const LONG error = DetourTransactionCommitEx(&failed);
        if (error != NO_ERROR) Failure(group, "transaction commit", error, failed, module, log);
        else log(std::string(group) + ": hooks installed", module, 0);
        return error;
    }
}
