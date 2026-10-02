#include <Windows.h>
#include <tlhelp32.h>

#include <algorithm>
#include <cwctype>
#include <format>
#include <set>
#include <string>
#include <utility>
#include <vector>

#include "AntiCheat.h"
#include "Logger.h"

namespace
{
    std::set<std::wstring> g_KnownModules;

    std::wstring ToLower(std::wstring text)
    {
        std::transform(text.begin(), text.end(), text.begin(), [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
        return text;
    }

    // Module names/paths are ASCII in practice (Windows/Steam/our own DLLs) - a naive narrowing is fine for a log line.
    std::string Narrow(const std::wstring& text)
    {
        return std::string(text.begin(), text.end());
    }

    // System32/SysWOW64 DLLs are "known" without listing them by name - there are hundreds and they change with every
    // Windows update. Anything else has to either be in g_KnownModules (captured at Setup()) or be recognisably one of
    // this project's own plugin DLLs (so a normal load-order difference between machines isn't a false positive).
    bool IsSystemModule(const std::wstring& path)
    {
        wchar_t systemDir[MAX_PATH]{};
        GetSystemDirectoryW(systemDir, MAX_PATH);
        return ToLower(path).find(ToLower(systemDir)) == 0;
    }

    bool IsOurOwnPlugin(const std::wstring& fileName)
    {
        static const wchar_t* kOwnPrefixes[] = { L"yu-gi-oh-", L"steam_api", L"d3dcompiler", L"dxgidebug" };
        const std::wstring lower = ToLower(fileName);
        for (const wchar_t* prefix : kOwnPrefixes)
        {
            if (lower.find(prefix) == 0)
                return true;
        }
        return false;
    }

    std::vector<std::pair<std::wstring, std::wstring>> SnapshotModules()   // { fileName, fullPath }
    {
        std::vector<std::pair<std::wstring, std::wstring>> modules;
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, GetCurrentProcessId());
        if (snapshot == INVALID_HANDLE_VALUE)
            return modules;

        MODULEENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        if (Module32FirstW(snapshot, &entry))
        {
            do
            {
                modules.emplace_back(entry.szModule, entry.szExePath);
            } while (Module32NextW(snapshot, &entry));
        }
        CloseHandle(snapshot);
        return modules;
    }
}

void AntiCheat::Setup()
{
    g_KnownModules.clear();
    for (const auto& [name, path] : SnapshotModules())
        g_KnownModules.insert(ToLower(name));
    Logger::WriteLog(std::format("AntiCheat::Setup: baselined {} loaded module(s)", g_KnownModules.size()), MODULE_NAME, 69);
}

bool AntiCheat::IsEnvironmentSuspicious()
{
    bool suspicious = false;

    if (IsDebuggerPresent())
    {
        Logger::WriteLog("AntiCheat: IsDebuggerPresent() = true", MODULE_NAME, 1);
        suspicious = true;
    }
    BOOL remoteDebugger = FALSE;
    if (CheckRemoteDebuggerPresent(GetCurrentProcess(), &remoteDebugger) && remoteDebugger)
    {
        Logger::WriteLog("AntiCheat: CheckRemoteDebuggerPresent() = true", MODULE_NAME, 1);
        suspicious = true;
    }

    for (const auto& [name, path] : SnapshotModules())
    {
        const std::wstring lowerName = ToLower(name);
        if (g_KnownModules.count(lowerName) || IsSystemModule(path) || IsOurOwnPlugin(name))
            continue;

        Logger::WriteLog(std::format("AntiCheat: unrecognised module loaded: {} ({})", Narrow(name), Narrow(path)), MODULE_NAME, 1);
        suspicious = true;
    }

    Logger::WriteLog(std::format("AntiCheat::IsEnvironmentSuspicious() = {}", suspicious), MODULE_NAME, 69);
    return suspicious;
}

namespace
{
    // __try/__except can't share a function with anything requiring C++ object unwinding (MSVC C2712) - split out so
    // this one has only POD locals; ComputeStateHash below does the logging (a std::string via std::format) instead.
    bool TryReadPlayerStateHash(uint64_t& outHash)
    {
        // See the UNVERIFIED warning in AntiCheat.h. Player block size/stride from ygo-playerstate-struct-map-2026-09-30.
        constexpr uintptr_t kPlayerStateBase = 0x143497C40;
        constexpr size_t kPlayerBlockSize = 0xD94;
        constexpr int kPlayerCount = 2;

        uint64_t hash = 0xcbf29ce484222325ULL;   // FNV-1a 64-bit offset basis
        __try
        {
            const auto* bytes = reinterpret_cast<const uint8_t*>(kPlayerStateBase);
            for (size_t i = 0; i < kPlayerBlockSize * kPlayerCount; ++i)
            {
                hash ^= bytes[i];
                hash *= 0x100000001b3ULL;   // FNV-1a 64-bit prime
            }
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
        outHash = hash;
        return true;
    }
}

uint64_t AntiCheat::ComputeStateHash()
{
    uint64_t hash = 0;
    if (!TryReadPlayerStateHash(hash))
    {
        Logger::WriteLog("AntiCheat::ComputeStateHash: reading Duel::PlayerState faulted - engine layout may have moved since this was mapped", MODULE_NAME, 2);
        return 0;
    }
    return hash;
}

void AntiCheat::ReportHeartbeat(uint32_t sequenceNumber)
{
    const uint64_t hash = ComputeStateHash();
    const bool suspicious = IsEnvironmentSuspicious();
    Logger::WriteLog(std::format("AntiCheat::ReportHeartbeat(seq={}): stateHash={:016x} suspicious={} - not sent, no anticheat endpoint exists yet",
        sequenceNumber, hash, suspicious), MODULE_NAME, 69);
}
