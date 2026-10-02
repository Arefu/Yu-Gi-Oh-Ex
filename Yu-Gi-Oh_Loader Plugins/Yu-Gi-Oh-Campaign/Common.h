#pragma once

#include <Windows.h>
#include <cctype>
#include <cstdint>
#include <map>
#include <optional>
#include <string>

#include <json.hpp>

// Helpers and game layouts shared by the Campaign plugin's content (Characters.cpp, StoryDuels.cpp).
namespace Campaign
{
    struct GameWString            // MSVC std::wstring as the game builds it (0x20)
    {
        union { wchar_t* Ptr; wchar_t Buf[8]; } Data;
        uint64_t Size;
        uint64_t Capacity;

        const wchar_t* c_str() const { return Capacity >= 8 ? Data.Ptr : Data.Buf; }
    };
    static_assert(sizeof(GameWString) == 0x20, "GameWString layout drifted");

    using WStringAssign_t = void*(__fastcall*)(GameWString* self, const wchar_t* text, size_t count);
    using GetPlayerSection_t = uint8_t*(__fastcall*)(unsigned int profile);

    inline const auto WStringAssign = reinterpret_cast<WStringAssign_t>(0x140751310);        // the game's std::wstring::assign (its allocator)
    inline const auto GetPlayerSection = reinterpret_cast<GetPlayerSection_t>(0x1407F80A0);  // YGO::SAVE::Get_PlayerSection(0xFFFFFFFD = current)

    // The game's language as its file letter (E F G I J S...): g_iGameLanguageID looked up in the {int id, char letter} table.
    inline char CurrentLanguage()
    {
        const int language = *reinterpret_cast<const int*>(0x14332A344);
        for (auto entry = reinterpret_cast<const int*>(0x140A51D30); entry[0] != -1; entry += 2)
            if (entry[0] == language)
                return static_cast<char>(std::toupper(static_cast<unsigned char>(entry[1] & 0xFF)));
        return 'E';
    }

    inline std::wstring Utf8ToWide(const std::string& s)
    {
        if (s.empty())
            return {};
        int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0);
        std::wstring w(n, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), w.data(), n);
        return w;
    }

    inline std::string GameFolder()
    {
        char path[MAX_PATH]{};
        GetModuleFileNameA(nullptr, path, MAX_PATH);
        std::string full(path);
        return full.substr(0, full.find_last_of('\\') + 1);
    }

    inline std::optional<int> Int(const nlohmann::json& entry, const char* key)
    {
        auto it = entry.find(key);
        return it != entry.end() && it->is_number_integer() ? std::optional<int>(it->get<int>()) : std::nullopt;
    }

    inline std::optional<std::string> Str(const nlohmann::json& entry, const char* key)
    {
        auto it = entry.find(key);
        return it != entry.end() && it->is_string() ? std::optional<std::string>(it->get<std::string>()) : std::nullopt;
    }

    // {"E": "...", "F": "..."} or a plain string (= English).
    inline void ReadTexts(const nlohmann::json& entry, const char* key, std::map<char, std::wstring>& out)
    {
        auto it = entry.find(key);
        if (it == entry.end())
            return;
        if (it->is_string())
            out['E'] = Utf8ToWide(it->get<std::string>());
        else if (it->is_object())
            for (auto& [letter, text] : it->items())
                if (!letter.empty() && text.is_string())
                    out[static_cast<char>(std::toupper(static_cast<unsigned char>(letter[0])))] = Utf8ToWide(text.get<std::string>());
    }

    // The text for the game's language, else English, else any.
    inline const std::wstring* Pick(const std::map<char, std::wstring>& texts)
    {
        for (char language : { CurrentLanguage(), 'E' })
            if (auto it = texts.find(language); it != texts.end())
                return &it->second;
        return texts.empty() ? nullptr : &texts.begin()->second;
    }
}
