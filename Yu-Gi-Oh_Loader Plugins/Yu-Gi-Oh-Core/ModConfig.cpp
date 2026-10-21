#include "ModConfig.h"

#include <Windows.h>
#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <filesystem>
#include <format>
#include <fstream>
#include <map>
#include <set>
#include <string>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    struct Setting
    {
        std::string Section, Key, Value;   // as the mod wrote them (UTF-8)
        std::string Mod;                   // the mod's name, for the log
    };

    // Lower-case section -> lower-case key -> the winning mod's value.
    std::map<std::string, std::map<std::string, Setting>> g_Settings;
    std::wstring g_ConfigPath;   // <game>\Config.ini, full path

    UINT(WINAPI* Real_GetPrivateProfileIntA)(LPCSTR, LPCSTR, INT, LPCSTR) = GetPrivateProfileIntA;
    UINT(WINAPI* Real_GetPrivateProfileIntW)(LPCWSTR, LPCWSTR, INT, LPCWSTR) = GetPrivateProfileIntW;
    DWORD(WINAPI* Real_GetPrivateProfileStringA)(LPCSTR, LPCSTR, LPCSTR, LPSTR, DWORD, LPCSTR) = GetPrivateProfileStringA;
    DWORD(WINAPI* Real_GetPrivateProfileStringW)(LPCWSTR, LPCWSTR, LPCWSTR, LPWSTR, DWORD, LPCWSTR) = GetPrivateProfileStringW;

    std::string Lower(std::string text)
    {
        std::transform(text.begin(), text.end(), text.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return text;
    }

    std::string Trim(const std::string& text)
    {
        const size_t first = text.find_first_not_of(" \t\r\n");
        if (first == std::string::npos)
            return {};
        return text.substr(first, text.find_last_not_of(" \t\r\n") - first + 1);
    }

    std::string Ansi(const std::wstring& text)
    {
        if (text.empty())
            return {};
        const int size = WideCharToMultiByte(CP_ACP, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
        std::string out(size, '\0');
        WideCharToMultiByte(CP_ACP, 0, text.data(), static_cast<int>(text.size()), out.data(), size, nullptr, nullptr);
        return out;
    }

    std::wstring FromAnsi(const char* text)
    {
        if (!text || !*text)
            return {};
        const int size = MultiByteToWideChar(CP_ACP, 0, text, -1, nullptr, 0);
        std::wstring out(size > 0 ? size - 1 : 0, L'\0');
        if (size > 1)
            MultiByteToWideChar(CP_ACP, 0, text, -1, out.data(), size);
        return out;
    }

    // What a mod may not set: what loads, which save is used, and the multiplayer server and sign-in (ModConfig.h).
    bool Blocked(const std::string& section, const std::string& key)
    {
        if (section == "yu-gi-oh-mp")
            return true;
        if (section != "yu-gi-oh-core")
            return false;
        static const std::set<std::string> kCore = { "pluginspath", "modconfig", "gamesavename", "seedfromgamesave", "saveslot", "replacednotice" };
        return kCore.count(key) || key.rfind("yu-gi-oh-", 0) == 0 || key.find('/') != std::string::npos;
    }

    // The file as UTF-8 text (UTF-8 with or without a BOM, or UTF-16 LE with a BOM, which Notepad can save).
    std::string ReadText(const std::filesystem::path& path)
    {
        std::ifstream file(path, std::ios::binary);
        std::string bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        if (bytes.size() >= 2 && static_cast<unsigned char>(bytes[0]) == 0xFF && static_cast<unsigned char>(bytes[1]) == 0xFE)
            return YGO::Mods::Utf8(std::wstring(reinterpret_cast<const wchar_t*>(bytes.data() + 2), (bytes.size() - 2) / 2));
        if (bytes.size() >= 3 && bytes.compare(0, 3, "\xEF\xBB\xBF") == 0)
            bytes.erase(0, 3);
        return bytes;
    }

    // Reads one mod's Config.ini into g_Settings (a later mod replaces an earlier one's value). Same rules as Windows: [Section] lines,
    // Key=Value split at the first '=', spaces trimmed, a value in matching quotes loses them, lines starting with ';' are comments.
    void ReadModConfig(const std::filesystem::path& path, const std::string& mod)
    {
        const std::string text = ReadText(path);
        std::string section;
        int count = 0;
        size_t start = 0;
        while (start <= text.size())
        {
            size_t end = text.find('\n', start);
            if (end == std::string::npos)
                end = text.size();
            const std::string line = Trim(text.substr(start, end - start));
            start = end + 1;
            if (line.empty() || line[0] == ';' || line[0] == '#')
                continue;
            if (line[0] == '[')
            {
                const size_t close = line.find(']');
                section = Trim(line.substr(1, close == std::string::npos ? std::string::npos : close - 1));
                continue;
            }
            const size_t equals = line.find('=');
            if (section.empty() || equals == std::string::npos)
                continue;
            const std::string key = Trim(line.substr(0, equals));
            std::string value = Trim(line.substr(equals + 1));
            if (value.size() >= 2 && (value.front() == '"' || value.front() == '\'') && value.back() == value.front())
                value = value.substr(1, value.size() - 2);
            if (key.empty())
                continue;
            if (Blocked(Lower(section), Lower(key)))
            {
                Logger::WriteLog(std::format("Mod '{}' can't set [{}] {} (that one stays in your Config.ini)", mod, section, key), MODULE_NAME, 1);
                continue;
            }
            g_Settings[Lower(section)][Lower(key)] = Setting{ section, key, value, mod };
            ++count;
        }
        Logger::WriteLog(std::format("Mod '{}' sets {} setting(s) from its Config.ini", mod, count), MODULE_NAME, 0);
    }

    const Setting* Find(const std::string& section, const std::string& key)
    {
        auto sectionIt = g_Settings.find(Lower(section));
        if (sectionIt == g_Settings.end())
            return nullptr;
        auto keyIt = sectionIt->second.find(Lower(key));
        return keyIt == sectionIt->second.end() ? nullptr : &keyIt->second;
    }

    // True when a profile call reads the game's Config.ini. A bare file name means the Windows folder to Windows, so it never matches.
    bool IsGameConfig(const std::wstring& file)
    {
        if (file.find_first_of(L"\\/") == std::wstring::npos)
            return false;
        wchar_t full[MAX_PATH * 2]{};
        const DWORD length = GetFullPathNameW(file.c_str(), static_cast<DWORD>(std::size(full)), full, nullptr);
        return length > 0 && length < std::size(full) && _wcsicmp(full, g_ConfigPath.c_str()) == 0;
    }

    // GetPrivateProfileInt's parsing: optional sign, then decimal digits, or hex after "0x"; no digits = 0.
    UINT ParseInt(const std::string& value)
    {
        const char* text = value.c_str();
        const bool hex = (text[0] == '0' && (text[1] == 'x' || text[1] == 'X'));
        return static_cast<UINT>(std::strtol(text, nullptr, hex ? 16 : 10));
    }

    template <typename Char>
    DWORD CopyOut(const std::basic_string<Char>& value, Char* out, DWORD size)
    {
        const DWORD count = (std::min)(static_cast<DWORD>(value.size()), size - 1);
        std::copy_n(value.data(), count, out);
        out[count] = 0;
        return count;
    }

    UINT WINAPI Hook_GetPrivateProfileIntA(LPCSTR section, LPCSTR key, INT fallback, LPCSTR file)
    {
        if (section && key && file && IsGameConfig(FromAnsi(file)))
        {
            if (const Setting* setting = Find(YGO::Mods::Utf8(FromAnsi(section)), YGO::Mods::Utf8(FromAnsi(key))))
                return ParseInt(setting->Value);
        }
        return Real_GetPrivateProfileIntA(section, key, fallback, file);
    }

    UINT WINAPI Hook_GetPrivateProfileIntW(LPCWSTR section, LPCWSTR key, INT fallback, LPCWSTR file)
    {
        if (section && key && file && IsGameConfig(file))
        {
            if (const Setting* setting = Find(YGO::Mods::Utf8(section), YGO::Mods::Utf8(key)))
                return ParseInt(setting->Value);
        }
        return Real_GetPrivateProfileIntW(section, key, fallback, file);
    }

    // A null section or key lists names (Core's plugin list does that): those always come from the player's file.
    DWORD WINAPI Hook_GetPrivateProfileStringA(LPCSTR section, LPCSTR key, LPCSTR fallback, LPSTR out, DWORD size, LPCSTR file)
    {
        if (section && key && out && size && file && IsGameConfig(FromAnsi(file)))
        {
            if (const Setting* setting = Find(YGO::Mods::Utf8(FromAnsi(section)), YGO::Mods::Utf8(FromAnsi(key))))
                return CopyOut(Ansi(YGO::Mods::Wide(setting->Value)), out, size);
        }
        return Real_GetPrivateProfileStringA(section, key, fallback, out, size, file);
    }

    DWORD WINAPI Hook_GetPrivateProfileStringW(LPCWSTR section, LPCWSTR key, LPCWSTR fallback, LPWSTR out, DWORD size, LPCWSTR file)
    {
        if (section && key && out && size && file && IsGameConfig(file))
        {
            if (const Setting* setting = Find(YGO::Mods::Utf8(section), YGO::Mods::Utf8(key)))
                return CopyOut(YGO::Mods::Wide(setting->Value), out, size);
        }
        return Real_GetPrivateProfileStringW(section, key, fallback, out, size, file);
    }
}

namespace ModConfig
{
    void Install()
    {
        g_ConfigPath = (YGO::Mods::GameFolder() / L"Config.ini").wstring();
        if (Real_GetPrivateProfileIntW(L"Yu-Gi-Oh-Core", L"ModConfig", 1, g_ConfigPath.c_str()) == 0)
        {
            Logger::WriteLog("Mod settings are off ([Yu-Gi-Oh-Core] ModConfig=0): only your Config.ini is read", MODULE_NAME, 0);
            return;
        }

        std::error_code error;
        for (const YGO::Mods::Mod& mod : YGO::Mods::Active(YGO::Mods::GameFolder()))
        {
            const auto path = mod.Folder / L"Config.ini";
            if (std::filesystem::is_regular_file(path, error))
                ReadModConfig(path, mod.Name);
        }
        if (g_Settings.empty())
            return;   // no mod has settings: nothing to hook

        for (const auto& [section, keys] : g_Settings)
        {
            for (const auto& [key, setting] : keys)
                Logger::WriteLog(std::format("[{}] {}={} (from mod '{}')", setting.Section, setting.Key, setting.Value, setting.Mod), MODULE_NAME, 0);
        }

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&reinterpret_cast<PVOID&>(Real_GetPrivateProfileIntA), Hook_GetPrivateProfileIntA);
        DetourAttach(&reinterpret_cast<PVOID&>(Real_GetPrivateProfileIntW), Hook_GetPrivateProfileIntW);
        DetourAttach(&reinterpret_cast<PVOID&>(Real_GetPrivateProfileStringA), Hook_GetPrivateProfileStringA);
        DetourAttach(&reinterpret_cast<PVOID&>(Real_GetPrivateProfileStringW), Hook_GetPrivateProfileStringW);
        const LONG result = DetourTransactionCommit();
        Logger::WriteLog(result == NO_ERROR ? "Mod settings hooks attached" : std::format("Mod settings hooks failed ({})", result), MODULE_NAME, result == NO_ERROR ? 0 : 2);
    }
}
