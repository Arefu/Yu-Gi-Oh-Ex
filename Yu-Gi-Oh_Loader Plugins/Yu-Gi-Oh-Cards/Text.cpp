#include <Windows.h>
#include <cctype>
#include <fstream>
#include <mutex>
#include <string>
#include <unordered_map>

#include <json.hpp>

#include "Card.h"
#include "Detours.h"
#include "Logger.h"
#include "Text.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    using Getter_t = const wchar_t*(__fastcall*)(int index);

    uintptr_t orig_GetWordText = 0x14076D7B0;   // YGO::CARDS::Get_WordText
    uintptr_t orig_GetDlgText = 0x14076D770;    // YGO::CARDS::Get_DlgText

    constexpr uintptr_t kGameLanguageId = 0x14332A344;       // g_iGameLanguageID
    constexpr uintptr_t kLanguageLetterTable = 0x140A51D30;  // g_LanguageLetterTable: {int id, char letter at +4}, ended by id -1

    // entry number -> language letter (upper case) -> text
    using Table = std::unordered_map<int, std::unordered_map<char, std::wstring>>;
    Table g_word, g_dlg;
    std::mutex g_lock;

    std::wstring Utf8ToWide(const std::string& s)
    {
        if (s.empty())
            return {};
        int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0);
        std::wstring w(n, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), w.data(), n);
        return w;
    }

    // The letter the game puts in its file names for the current language, as MakeDataPath finds it ('E' if not listed).
    char CurrentLanguage()
    {
        const int language = *reinterpret_cast<const int*>(kGameLanguageId);
        for (auto entry = reinterpret_cast<const int*>(kLanguageLetterTable); entry[0] != -1; entry += 2)
            if (entry[0] == language)
                return static_cast<char>(std::toupper(static_cast<unsigned char>(entry[1] & 0xFF)));
        return 'E';
    }

    void ReadTable(const nlohmann::json& root, const char* key, Table& out)
    {
        out.clear();
        auto it = root.find(key);
        if (it == root.end() || !it->is_object())
            return;
        for (auto& [number, languages] : it->items())
        {
            int index = 0;
            try
            {
                index = std::stoi(number);
            }
            catch (...)
            {
                Logger::Log("text.json: \"" + std::string(key) + "\" entry \"" + number + "\" isn't a number, skipped", MODULE_NAME, 2);
                continue;
            }
            if (index < 0 || !languages.is_object())
                continue;
            for (auto& [letter, text] : languages.items())
                if (!letter.empty() && text.is_string())
                    out[index][static_cast<char>(std::toupper(static_cast<unsigned char>(letter[0])))] = Utf8ToWide(text.get<std::string>());
        }
    }

    // The text for this entry in the current language (else English, else any), or null if text.json doesn't have it.
    const wchar_t* Find(const Table& table, int index)
    {
        auto entry = table.find(index);
        if (entry == table.end() || entry->second.empty())
            return nullptr;
        auto& texts = entry->second;
        for (char language : { CurrentLanguage(), 'E' })
            if (auto text = texts.find(language); text != texts.end())
                return text->second.c_str();
        return texts.begin()->second.c_str();
    }

    const wchar_t* __fastcall Hook_GetWordText(int index)
    {
        if (const wchar_t* text = Find(g_word, index))
            return text;
        return reinterpret_cast<Getter_t>(orig_GetWordText)(index);
    }

    const wchar_t* __fastcall Hook_GetDlgText(int index)
    {
        if (const wchar_t* text = Find(g_dlg, index))
            return text;
        return reinterpret_cast<Getter_t>(orig_GetDlgText)(index);
    }
}

namespace Text
{
    void Load()
    {
        std::lock_guard guard(g_lock);
        // every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h): a later mod's entry for an index replaces an earlier one's
        std::vector<std::string> problems;
        auto root = YGO::Mods::ReadMerged("text.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::Log(problem + ", its text is left out", MODULE_NAME, 3);
        if (root.is_null())
        {
            g_word.clear();
            g_dlg.clear();
            return; // text.json is optional
        }
        try
        {
            ReadTable(root, "word", g_word);
            ReadTable(root, "dlg", g_dlg);
            Logger::Log("text.json: " + std::to_string(g_word.size()) + " WORD and " + std::to_string(g_dlg.size()) + " DLG entries", MODULE_NAME, 1);
        }
        catch (const std::exception& ex)
        {
            Logger::Log(std::string("text.json couldn't be read: ") + ex.what(), MODULE_NAME, 3);
        }
    }

    char CurrentLanguage() { return ::CurrentLanguage(); }

    void Attach()
    {
        DetourAttach(&(PVOID&)orig_GetWordText, Hook_GetWordText);
        DetourAttach(&(PVOID&)orig_GetDlgText, Hook_GetDlgText);
    }
}
