#include <Windows.h>
#include <format>
#include <string>

#include <detours.h>
#include "Loading.h"
#include "Logger.h"

namespace
{
    // FS::LoadDAT(fs, name): opens <name>.toc and <name>.dat (the game passes "YGO_2020")
    using LoadArchive_t = __int64(__fastcall*)(__int64* fs, const char* name);
    uintptr_t orig_LoadArchive = 0x14080D3D0;
    std::string g_archive;

    __int64 __fastcall Hook_LoadArchive(__int64* fs, const char* name)
    {
        Logger::WriteLog(std::format("Opening archive {} (the game asked for {})", g_archive, name ? name : "?"), MODULE_NAME, 0);
        return reinterpret_cast<LoadArchive_t>(orig_LoadArchive)(fs, g_archive.c_str());
    }

    // The game makes a named mutex so a second copy quits; handing it something that isn't that mutex lets more copies run.
    using CreateMutexW_t = HANDLE(WINAPI*)(LPSECURITY_ATTRIBUTES, BOOL, LPCWSTR);
    CreateMutexW_t orig_CreateMutexW = nullptr;

    HANDLE WINAPI Hook_CreateMutexW(LPSECURITY_ATTRIBUTES, BOOL, LPCWSTR)
    {
        return GetModuleHandleW(nullptr);
    }
}

__int64 Loading::OpenArchive(void* archive, const char* name)
{
    // orig_LoadArchive is Detours' trampoline once Archive is set, else the game's function
    return reinterpret_cast<LoadArchive_t>(orig_LoadArchive)(static_cast<__int64*>(archive), name);
}

const std::string& Loading::GameFolder()
{
    static const std::string folder = []
    {
        char path[MAX_PATH]{};
        GetModuleFileNameA(nullptr, path, MAX_PATH);
        std::string full(path);
        return full.substr(0, full.find_last_of('\\') + 1);
    }();
    return folder;
}

std::string Loading::Setting(const char* key, const char* fallback)
{
    const std::string ini = GameFolder() + "Config.ini";
    char value[MAX_PATH] = {};
    GetPrivateProfileStringA(MODULE_NAME, key, fallback, value, MAX_PATH, ini.c_str());
    return value;
}

bool Loading::Install()
{
    g_archive = Setting("Archive", "YGO_2020");
    const bool multiInstance = Setting("AllowMultiInstance", "0") == "1";
    const bool otherArchive = !g_archive.empty() && _stricmp(g_archive.c_str(), "YGO_2020") != 0;
    if (!otherArchive && !multiInstance)
        return true;

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    if (otherArchive)
        DetourAttach(&(PVOID&)orig_LoadArchive, Hook_LoadArchive);
    if (multiInstance)
    {
        orig_CreateMutexW = reinterpret_cast<CreateMutexW_t>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "CreateMutexW"));
        if (orig_CreateMutexW)
            DetourAttach(&(PVOID&)orig_CreateMutexW, Hook_CreateMutexW);
    }
    LONG err = DetourTransactionCommit();
    Logger::WriteLog(std::format("Loading: archive {}{}{}", g_archive, multiInstance ? ", several game windows allowed" : "",
                                 err == NO_ERROR ? "" : std::format(" - hooks failed ({})", err)), MODULE_NAME, err == NO_ERROR ? 0 : 2);
    return err == NO_ERROR;
}
