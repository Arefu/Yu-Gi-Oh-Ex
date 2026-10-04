#include "Packs.h"

#include <Windows.h>
#include <algorithm>
#include <cctype>
#include <cstdint>
#include <deque>
#include <fstream>
#include <map>
#include <string>
#include <unordered_set>
#include <vector>

#include <detours.h>
#include <json.hpp>

#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Log.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Mods.h"

namespace
{
    constexpr const char* MODULE_NAME = "Yu-Gi-Oh-BetterCardShop";

    // ---- the game (names and layouts in the IDB, docs/Packs.md) ----

    struct GameWString            // MSVC std::wstring as the game builds it (0x20)
    {
        union { wchar_t* Ptr; wchar_t Buf[8]; } Data;
        uint64_t Size;
        uint64_t Capacity;
    };

    struct PackRecord             // g_PackRecords entry (0x68)
    {
        uint32_t Id, Series, Cost, Kind;
        const char* Name;         // "1_1": packs.zib/packdata_<Name>.bin, the "wrap_<Name>" picture
        uint16_t* Contents;       // u16 common count, u16 rare count, common ids, rare ids
        void* BattleData;
        GameWString Title, Text;
    };
    static_assert(sizeof(PackRecord) == 0x68, "PackRecord layout drifted");

    constexpr uintptr_t kPackRecords = 0x1429241C0;      // g_PackRecords[128]
    constexpr uint32_t kPackCount = 128;
    constexpr uint32_t kRewardPack = 'R';
    constexpr size_t kUnlockBits = 0xB80;                // player section: u32[4], bit = pack id
    constexpr uintptr_t kGameLanguageId = 0x14332A344;       // g_iGameLanguageID
    constexpr uintptr_t kLanguageLetterTable = 0x140A51D30;  // {int id, char letter at +4}, ended by id -1

    using LoadPackDefinitions_t = char(__fastcall*)();
    using GetArtName_t = char*(__fastcall*)(PackRecord* record, char* outString);
    using BuildPackList_t = void(__fastcall*)(int64_t screen, int series);
    using WStringAssign_t = void*(__fastcall*)(GameWString* self, const wchar_t* text, size_t count);
    using GetPlayerSection_t = uint8_t*(__fastcall*)(unsigned int profile);

    uintptr_t orig_LoadPackDefinitions = 0x14080E3C0;    // YGO::GAME::LoadPackDefinitions
    uintptr_t orig_Pack_GetArtName = 0x14080E390;        // YGO::GAME::Pack_GetArtName
    uintptr_t orig_BuildPackList = 0x14085D6E0;          // YGO::UI::BoosterShop_BuildPackList
    const auto WStringAssign = reinterpret_cast<WStringAssign_t>(0x140751310);        // the game's std::wstring::assign (its allocator)
    const auto GetPlayerSection = reinterpret_cast<GetPlayerSection_t>(0x1407F80A0);  // YGO::SAVE::Get_PlayerSection(0xFFFFFFFD = current)

    PackRecord* Record(uint32_t id) { return id < kPackCount ? reinterpret_cast<PackRecord*>(kPackRecords) + id : nullptr; }

    char CurrentLanguage()
    {
        const int language = *reinterpret_cast<const int*>(kGameLanguageId);
        for (auto entry = reinterpret_cast<const int*>(kLanguageLetterTable); entry[0] != -1; entry += 2)
            if (entry[0] == language)
                return static_cast<char>(std::toupper(static_cast<unsigned char>(entry[1] & 0xFF)));
        return 'E';
    }

    std::wstring Utf8ToWide(const std::string& s)
    {
        if (s.empty())
            return {};
        int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0);
        std::wstring w(n, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), w.data(), n);
        return w;
    }

    // ---- packs.json ----

    struct PackChange             // "packs": cards for a game pack
    {
        std::string Pack;
        bool Replace = false;
        std::vector<uint16_t> Common, Rare;
    };

    struct NewPack                // "newPacks"
    {
        uint32_t Id = 0;
        std::string Name, Art, UnlockWith;
        uint32_t Series = 0, Cost = 0;
        std::map<char, std::wstring> Titles, Texts;
        std::vector<uint16_t> Common, Rare;
        std::vector<uint16_t> Contents;   // the list the record points at (built once, never freed: the game may be reading it)
    };

    std::vector<PackChange> g_changes;
    std::deque<NewPack> g_newPacks;       // deque: records point into Name/Contents
    std::unordered_set<uint32_t> g_ours;  // ids of the new packs

    void ReadIds(const nlohmann::json& entry, const char* key, std::vector<uint16_t>& out)
    {
        auto it = entry.find(key);
        if (it == entry.end() || !it->is_array())
            return;
        for (const auto& value : *it)
            if (value.is_number_integer() && value.get<int>() >= 1 && value.get<int>() <= 0xFFFF)
                out.push_back(static_cast<uint16_t>(value.get<int>()));
    }

    void ReadTexts(const nlohmann::json& entry, const char* key, std::map<char, std::wstring>& out)
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

    void Parse(const nlohmann::json& root);

    void Load()
    {
        // every mod's packs.json and the game folder's, merged (Yu-Gi-Oh-Mods.h): pack changes apply in load order
        std::vector<std::string> problems;
        const nlohmann::json root = YGO::Mods::ReadMerged("packs.json", nullptr, &problems);
        for (const std::string& problem : problems)
            YGO::Log(problem + ", its packs are left out", MODULE_NAME, 2);
        if (root.is_null())
            return; // packs.json is optional

        try
        {
            Parse(root);
        }
        catch (const std::exception& e)
        {
            g_changes.clear();
            g_newPacks.clear();
            g_ours.clear();
            YGO::Log("packs.json couldn't be read: " + std::string(e.what()), MODULE_NAME, 2);
            return;
        }
        YGO::Log("packs.json: " + std::to_string(g_changes.size()) + " pack change(s), " + std::to_string(g_newPacks.size()) + " new pack(s)", MODULE_NAME, 0);
    }

    void Parse(const nlohmann::json& root)
    {
        const bool replaceAll = root.is_object() && root.value("replaceDefaults", false);
        const nlohmann::json& changes = root.is_array() ? root : root["packs"];
        if (changes.is_array())
            for (const auto& entry : changes)
            {
                if (!entry.is_object() || !entry.contains("pack") || !entry["pack"].is_string())
                    continue;
                PackChange change;
                change.Pack = entry["pack"].get<std::string>();
                ReadIds(entry, "common", change.Common);
                ReadIds(entry, "rare", change.Rare);
                change.Replace = entry.value("replace", replaceAll);
                g_changes.push_back(std::move(change));
            }

        if (root.is_object() && root.contains("newPacks") && root["newPacks"].is_array())
            for (const auto& entry : root["newPacks"])
            {
                if (!entry.is_object())
                    continue;
                NewPack pack;
                pack.Id = entry.value("id", 0u);
                pack.Name = entry.value("name", std::string());
                pack.Art = entry.value("art", std::string());
                pack.UnlockWith = entry.value("unlockWith", std::string());
                pack.Series = entry.value("series", 0u);
                pack.Cost = entry.value("cost", 200u);
                ReadTexts(entry, "title", pack.Titles);
                ReadTexts(entry, "text", pack.Texts);
                ReadIds(entry, "common", pack.Common);
                ReadIds(entry, "rare", pack.Rare);

                std::string why;
                if (pack.Name.empty())
                    why = "no \"name\"";
                else if (pack.Id >= kPackCount)
                    why = "\"id\" must be 0-127";
                else if (g_ours.count(pack.Id))
                    why = "another new pack has id " + std::to_string(pack.Id);
                else if (pack.Common.empty() || pack.Rare.empty())
                    why = "it needs cards in both \"common\" and \"rare\"";
                if (!why.empty())
                {
                    YGO::Log("packs.json: new pack \"" + pack.Name + "\" skipped: " + why, MODULE_NAME, 2);
                    continue;
                }
                pack.Contents.push_back(static_cast<uint16_t>(pack.Common.size()));
                pack.Contents.push_back(static_cast<uint16_t>(pack.Rare.size()));
                pack.Contents.insert(pack.Contents.end(), pack.Common.begin(), pack.Common.end());
                pack.Contents.insert(pack.Contents.end(), pack.Rare.begin(), pack.Rare.end());
                g_ours.insert(pack.Id);
                g_newPacks.push_back(std::move(pack));
            }
    }

    const NewPack* FindNew(uint32_t id)
    {
        for (const NewPack& pack : g_newPacks)
            if (pack.Id == id)
                return &pack;
        return nullptr;
    }

    // ---- applying ----

    // "packs": the game packs' card lists with the additions (or replacements). The game may still be reading an old list while packs
    // reload, so the lists are never freed.
    void ApplyChanges()
    {
        static std::deque<std::vector<uint16_t>> lists;
        for (uint32_t i = 0; i < kPackCount; ++i)
        {
            PackRecord* record = Record(i);
            if (!record->Name || !record->Contents || record->Kind != kRewardPack || g_ours.count(i))
                continue;
            for (const PackChange& change : g_changes)
            {
                if (change.Pack != record->Name)
                    continue;
                const uint16_t* contents = record->Contents;
                std::vector<uint16_t> common(contents + 2, contents + 2 + contents[0]);
                std::vector<uint16_t> rare(contents + 2 + contents[0], contents + 2 + contents[0] + contents[1]);
                const size_t before = common.size() + rare.size();
                // a replaced list that would be empty keeps the game's cards (the pack can't draw from nothing)
                if (change.Replace && !change.Common.empty())
                    common.clear();
                if (change.Replace && !change.Rare.empty())
                    rare.clear();
                for (uint16_t id : change.Common)
                    if (std::find(common.begin(), common.end(), id) == common.end())
                        common.push_back(id);
                for (uint16_t id : change.Rare)
                    if (std::find(rare.begin(), rare.end(), id) == rare.end())
                        rare.push_back(id);

                std::vector<uint16_t>& list = lists.emplace_back();
                list.push_back(static_cast<uint16_t>(common.size()));
                list.push_back(static_cast<uint16_t>(rare.size()));
                list.insert(list.end(), common.begin(), common.end());
                list.insert(list.end(), rare.begin(), rare.end());
                record->Contents = list.data();
                YGO::Log("Pack " + change.Pack + ": " + std::to_string(common.size()) + " common, " + std::to_string(rare.size()) + " rare" +
                    (change.Replace ? " (replaced)" : " (" + std::to_string(common.size() + rare.size() - before) + " added)"), MODULE_NAME, 0);
            }
        }
    }

    const std::wstring& Pick(const std::map<char, std::wstring>& texts)
    {
        static const std::wstring none;
        for (char language : { CurrentLanguage(), 'E' })
            if (auto it = texts.find(language); it != texts.end())
                return it->second;
        return texts.empty() ? none : texts.begin()->second;
    }

    // "newPacks": fills their slots (again after every reload: the language, and so the title, may have changed).
    void ApplyNewPacks()
    {
        for (NewPack& pack : g_newPacks)
        {
            PackRecord* record = Record(pack.Id);
            if (record->Kind != 0 && record->Name != pack.Name.c_str())
            {
                YGO::Log("New pack \"" + pack.Name + "\": id " + std::to_string(pack.Id) + " is the game's pack \"" +
                    std::string(record->Name ? record->Name : "?") + "\", skipped (pick a free id, 36-127)", MODULE_NAME, 2);
                continue;
            }
            record->Id = pack.Id;
            record->Series = pack.Series;
            record->Cost = pack.Cost;
            record->Kind = kRewardPack;
            record->Name = pack.Name.c_str();
            record->Contents = pack.Contents.data();
            record->BattleData = nullptr;
            const std::wstring& title = Pick(pack.Titles);
            const std::wstring& text = Pick(pack.Texts);
            WStringAssign(&record->Title, title.c_str(), title.size());
            WStringAssign(&record->Text, text.c_str(), text.size());
        }
    }

    void Apply()
    {
        ApplyChanges();
        ApplyNewPacks();
    }

    // Sets the unlock bits of the new packs whose rule is met (always, or when their "unlockWith" pack is unlocked).
    void UnlockNewPacks()
    {
        uint8_t* player = GetPlayerSection(0xFFFFFFFD);
        if (!player || g_newPacks.empty())
            return;
        auto* bits = reinterpret_cast<uint32_t*>(player + kUnlockBits);
        auto has = [bits](uint32_t id) { return (bits[id >> 5] >> (id & 31) & 1) != 0; };
        for (const NewPack& pack : g_newPacks)
        {
            if (has(pack.Id) || _stricmp(pack.UnlockWith.c_str(), "never") == 0 || Record(pack.Id)->Name != pack.Name.c_str())
                continue;
            bool open = pack.UnlockWith.empty();
            if (!open)
                for (uint32_t i = 0; i < kPackCount && !open; ++i)
                    if (Record(i)->Name && pack.UnlockWith == Record(i)->Name)
                        open = has(i);
            if (open)
            {
                bits[pack.Id >> 5] |= 1u << (pack.Id & 31);
                YGO::Log("New pack \"" + pack.Name + "\" unlocked", MODULE_NAME, 0);
            }
        }
    }

    // ---- hooks ----

    char __fastcall Hook_LoadPackDefinitions()
    {
        char result = reinterpret_cast<LoadPackDefinitions_t>(orig_LoadPackDefinitions)();
        if (result)
            Apply();
        return result;
    }

    char* __fastcall Hook_Pack_GetArtName(PackRecord* record, char* outString)
    {
        if (record && g_ours.count(record->Id))
            if (const NewPack* pack = FindNew(record->Id); pack && !pack->Art.empty() && record->Name == pack->Name.c_str())
            {
                PackRecord borrowed = {};
                borrowed.Name = pack->Art.c_str();   // "wrap_<art>": the picture of an existing pack
                return reinterpret_cast<GetArtName_t>(orig_Pack_GetArtName)(&borrowed, outString);
            }
        return reinterpret_cast<GetArtName_t>(orig_Pack_GetArtName)(record, outString);
    }

    void __fastcall Hook_BuildPackList(int64_t screen, int series)
    {
        UnlockNewPacks();
        reinterpret_cast<BuildPackList_t>(orig_BuildPackList)(screen, series);
    }
}

namespace Packs
{
    std::unordered_set<uint16_t> RareCards()
    {
        std::unordered_set<uint16_t> rares;
        for (uint32_t i = 0; i < kPackCount; ++i)
        {
            const PackRecord* record = Record(i);
            if (record->Kind != kRewardPack || !record->Contents)
                continue;
            const uint16_t commons = record->Contents[0];
            const uint16_t count = record->Contents[1];
            const uint16_t* ids = record->Contents + 2 + commons;
            rares.insert(ids, ids + count);
        }
        return rares;
    }

    void Install()
    {
        Load();
        if (g_changes.empty() && g_newPacks.empty())
            return;

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_LoadPackDefinitions, Hook_LoadPackDefinitions);
        if (!g_newPacks.empty())
        {
            DetourAttach(&(PVOID&)orig_Pack_GetArtName, Hook_Pack_GetArtName);
            DetourAttach(&(PVOID&)orig_BuildPackList, Hook_BuildPackList);
        }
        LONG error = DetourTransactionCommit();
        YGO::Log("Pack hooks attached: " + std::to_string(error), MODULE_NAME, error == 0 ? 0 : 2);

        // Core starts this plugin once the main menu is up, after the game's first pack load: apply to what is loaded now; the hook
        // above handles later reloads (language changes).
        for (uint32_t i = 0; i < kPackCount; ++i)
            if (Record(i)->Name)
            {
                Apply();
                break;
            }
    }
}
