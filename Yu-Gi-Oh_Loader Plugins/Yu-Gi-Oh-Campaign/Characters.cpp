#include "Characters.h"
#include "Common.h"

#include <Windows.h>
#include <algorithm>
#include <cstdlib>
#include <intrin.h>
#include <cctype>
#include <cstdint>
#include <deque>
#include <format>
#include <fstream>
#include <map>
#include <optional>
#include <string>
#include <vector>

#include <json.hpp>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"
#include "Yu-Gi-Oh-Memory.h"
#include "YuGiOh/YuGiOh-Addresses.h"

namespace
{
    using namespace Campaign;

    // ---- the game (names and layouts in the IDB) ----

    struct CharacterRecord        // g_CharacterRecords entry (0x68)
    {
        uint32_t Id;
        int32_t Series;           // 0-5 tab, -1 none
        int32_t Deck;             // deckdata index, -1 none
        uint32_t Selectable;
        int32_t Sku;              // content pack (the loader turns the file's -1 into 1)
        int32_t Arena;
        uint32_t Field18;
        uint32_t Pad1C;
        const char* Key;          // portrait "<key>_neutral"; non-null = defined (Character_IsDefined)
        GameWString Name, Bio;
    };
    static_assert(sizeof(CharacterRecord) == 0x68, "CharacterRecord layout drifted");

    constexpr uintptr_t kCharacterRecords = YGO::Addr::g_CharacterRecords;   // g_CharacterRecords[240]
    constexpr uint32_t kCharacterSlots = 240;
    constexpr size_t kUnlockedCharacters = 0x18;           // player section: 240-bit set, bit = character id

    using LoadCharacterData_t = void(__fastcall*)();
    using FreeDuelBuild_t = void(__fastcall*)(int64_t screen);
    using CollectionCount_t = void*(__fastcall*)(void* outString);

    uintptr_t orig_LoadCharacterData = 0x1407FED60;        // YGO::GAME::LoadCharacterData
    uintptr_t orig_FreeDuelBuild = 0x140841900;            // YGO::UI::FreeDuel_BuildOpponentList
    uintptr_t orig_CollectionCount = 0x1408A5320;          // YGO::UI::Collection_FormatCharacterCount

    CharacterRecord* Record(uint32_t id) { return id < kCharacterSlots ? reinterpret_cast<CharacterRecord*>(kCharacterRecords) + id : nullptr; }

    // Free Duel / Duelist Challenges share one screen class. Its OnEnter (0x1408415F0, named RIX::ScreenHardChallenge::OnEnter in the
    // IDB) fills the series tab list (vector<int> at +4096) with 0-5, hands it to widget_pickSeries::SetList (+656) and refreshes
    // (sub_140841B10). characters.json "hideEmptySeries": true drops the tabs no selectable character with a defined deck is in.
    using FreeDuelOnEnter_t = void(__fastcall*)(int64_t screen, int64_t previous);
    uintptr_t orig_FreeDuelOnEnter = 0x1408415F0;
    const auto PickSeriesSetList = reinterpret_cast<void(__fastcall*)(int64_t list, int64_t ids)>(0x1408A76E0);
    const auto FreeDuelRefreshSeries = reinterpret_cast<void(__fastcall*)(int64_t screen)>(0x140841B10);
    const auto DeckIsDefined = reinterpret_cast<bool(__fastcall*)(int deck)>(0x1407FE4D0);
    bool g_HideEmptySeries = false;

    // characters.json "seriesNames": { "0": { "E": "Forbidden Memories" } } - the Free Duel / Duelist Challenges series tab's text.
    // Campaign_GetSeriesLabel (0x1407564C0) serves the campaign's series list and rewards too; only widget_pickSeries' two calls get ours.
    std::map<int, std::map<char, std::wstring>> g_SeriesNames;
    using SeriesLabel_t = const wchar_t*(__fastcall*)(unsigned int series);
    uintptr_t orig_SeriesLabel = 0x1407564C0;

    const wchar_t* __fastcall Hook_SeriesLabel(unsigned int series)
    {
        const uintptr_t caller = reinterpret_cast<uintptr_t>(_ReturnAddress());
        const bool pickSeries = (caller >= 0x1408A7530 && caller < 0x1408A76D9) || (caller >= 0x1408A7740 && caller < 0x1408A786C);
        if (pickSeries)
            if (auto it = g_SeriesNames.find(static_cast<int>(series)); it != g_SeriesNames.end())
                if (const std::wstring* text = Pick(it->second))
                    return text->c_str();
        return reinterpret_cast<SeriesLabel_t>(orig_SeriesLabel)(series);
    }

    bool SeriesHasOpponents(int series)
    {
        for (uint32_t id = 0; id < kCharacterSlots; ++id)
        {
            const CharacterRecord* record = Record(id);
            if (record->Key && record->Selectable && record->Series == series && record->Deck >= 0 && DeckIsDefined(record->Deck))
                return true;
        }
        return false;
    }

    void __fastcall Hook_FreeDuelOnEnter(int64_t screen, int64_t previous)
    {
        const int* before = *reinterpret_cast<int**>(screen + 4104);
        reinterpret_cast<FreeDuelOnEnter_t>(orig_FreeDuelOnEnter)(screen, previous);
        int* begin = *reinterpret_cast<int**>(screen + 4096);
        int*& end = *reinterpret_cast<int**>(screen + 4104);
        if (end == before || begin == end)
            return;   // the game kept its list (coming back from a duel or deck pick)
        int* kept = std::remove_if(begin, end, [](int series) { return !SeriesHasOpponents(series); });
        if (kept == begin || kept == end)
            return;   // every tab empty, or none: leave the game's list
        end = kept;
        PickSeriesSetList(screen + 656, screen + 4096);
        FreeDuelRefreshSeries(screen);
    }

    // ---- characters.json ----

    struct Entry
    {
        uint32_t Id = 0;
        std::optional<int> Series, Deck, Selectable, Sku, Arena;
        std::optional<std::string> Key;
        std::map<char, std::wstring> Names, Bios;
        std::optional<bool> Unlocked; // absent: preserve existing progress, unlock new characters
        bool Native = false;      // "native": WolfX wrote it into chardata; only its unlock is ours
        bool IsNew = false;       // decided when applied: the game had no character in the slot
    };

    std::deque<Entry> g_entries;  // deque: records point at Key strings

    void Load()
    {
        // every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h): later mods win for the same id
        std::vector<std::string> problems;
        auto root = YGO::Mods::ReadMerged("characters.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", it is left out", MODULE_NAME, 2);
        if (root.is_null())
            return; // characters.json is optional
        try
        {
            if (root.is_object())
                g_HideEmptySeries = root.value("hideEmptySeries", false);
            if (auto names = root.find("seriesNames"); names != root.end() && names->is_object())
                for (auto& [key, texts] : names->items())
                {
                    std::map<char, std::wstring> byLanguage;
                    nlohmann::json wrapper{ { "t", texts } };
                    ReadTexts(wrapper, "t", byLanguage);
                    if (!byLanguage.empty())
                        g_SeriesNames[std::atoi(key.c_str())] = std::move(byLanguage);
                }
            auto list = root.find("characters");
            if (list == root.end() || !list->is_array())
                return;
            for (auto& json : *list)
            {
                if (!json.is_object())
                    continue;
                Entry entry;
                auto id = Int(json, "id");
                if (!id || *id < 0 || *id >= static_cast<int>(kCharacterSlots))
                {
                    Logger::WriteLog("characters.json: an entry without an id 0-239 was skipped", MODULE_NAME, 2);
                    continue;
                }
                entry.Id = static_cast<uint32_t>(*id);
                entry.Series = Int(json, "series");
                entry.Deck = Int(json, "deck");
                entry.Selectable = Int(json, "selectable");
                entry.Sku = Int(json, "sku");
                entry.Arena = Int(json, "arena");
                if (auto key = json.find("key"); key != json.end() && key->is_string())
                    entry.Key = key->get<std::string>();
                ReadTexts(json, "name", entry.Names);
                ReadTexts(json, "bio", entry.Bios);
                if (auto unlocked = json.find("unlocked"); unlocked != json.end() && unlocked->is_boolean())
                    entry.Unlocked = unlocked->get<bool>();
                entry.Native = json.value("native", false);
                g_entries.push_back(std::move(entry));
            }
            Logger::WriteLog(std::format("characters.json: {} character(s)", g_entries.size()), MODULE_NAME, 0);
        }
        catch (const std::exception& ex)
        {
            g_entries.clear();
            Logger::WriteLog(std::string("characters.json couldn't be read: ") + ex.what(), MODULE_NAME, 3);
        }
    }

    // Runs after every LoadCharacterData: the game rewrote the slots it has (language change), ours are written again.
    void Apply()
    {
        size_t changed = 0, added = 0;
        for (Entry& entry : g_entries)
        {
            CharacterRecord* record = Record(entry.Id);
            if (entry.Native)
            {
                // the game loaded it from chardata: only check it is there (for the unlock)
                entry.IsNew = record->Key && entry.Key && _stricmp(record->Key, entry.Key->c_str()) == 0;
                if (!entry.IsNew)
                    Logger::WriteLog(std::format("characters.json: archive missing data: duelist {} isn't in chardata (is the YGO_2020-Ex patch archive there?)", entry.Id), MODULE_NAME, 2);
                continue;
            }
            const bool ours = entry.IsNew && entry.Key && record->Key == entry.Key->c_str();
            if (!record->Key || ours)
            {
                // a new character: every field from the entry (defaults for the ones it leaves out)
                if (!entry.Key)
                {
                    Logger::WriteLog(std::format("characters.json: new character {} needs a \"key\" (portrait), skipped", entry.Id), MODULE_NAME, 2);
                    continue;
                }
                entry.IsNew = true;
                record->Id = entry.Id;
                record->Series = entry.Series.value_or(0);
                record->Deck = entry.Deck.value_or(-1);
                record->Selectable = static_cast<uint32_t>(entry.Selectable.value_or(1));
                record->Sku = entry.Sku.value_or(1);
                record->Arena = entry.Arena.value_or(0);
                record->Field18 = 0;
                ++added;
            }
            else
            {
                if (entry.Series) record->Series = *entry.Series;
                if (entry.Deck) record->Deck = *entry.Deck;
                if (entry.Selectable) record->Selectable = static_cast<uint32_t>(*entry.Selectable);
                if (entry.Sku) record->Sku = *entry.Sku;
                if (entry.Arena) record->Arena = *entry.Arena;
                ++changed;
            }
            if (record->Sku == -1)
                record->Sku = 1;   // as the game's loader does
            if (entry.Key)
                record->Key = entry.Key->c_str();
            if (const std::wstring* name = Pick(entry.Names))
                WStringAssign(&record->Name, name->c_str(), name->size());
            if (const std::wstring* bio = Pick(entry.Bios))
                WStringAssign(&record->Bio, bio->c_str(), bio->size());
        }
        if (!g_entries.empty())
            Logger::WriteLog(std::format("Characters: {} changed, {} new", changed, added), MODULE_NAME, 0);
    }

    void UnlockCharacters()
    {
        uint8_t* player = GetPlayerSection(0xFFFFFFFD);
        if (!player)
            return;
        auto* bits = reinterpret_cast<uint32_t*>(player + kUnlockedCharacters);
        for (const Entry& entry : g_entries)
        {
            if (!entry.Unlocked.value_or(entry.IsNew) || !Record(entry.Id)->Key || (bits[entry.Id >> 5] >> (entry.Id & 31) & 1))
                continue;
            bits[entry.Id >> 5] |= 1u << (entry.Id & 31);
            Logger::WriteLog(std::format("Character {} unlocked", entry.Id), MODULE_NAME, 0);
        }
    }

    void __fastcall Hook_LoadCharacterData()
    {
        reinterpret_cast<LoadCharacterData_t>(orig_LoadCharacterData)();
        Apply();
    }

    void __fastcall Hook_FreeDuelBuild(int64_t screen)
    {
        UnlockCharacters();
        reinterpret_cast<FreeDuelBuild_t>(orig_FreeDuelBuild)(screen);
    }

    void* __fastcall Hook_CollectionCount(void* outString)
    {
        UnlockCharacters();
        return reinterpret_cast<CollectionCount_t>(orig_CollectionCount)(outString);
    }
}

namespace Characters
{
    void Attach()
    {
        Load();
        // FreeDuel_BuildOpponentList already checks character, SKU and deck validity.
        // Allow its assigned deck to be shared without changing the deck's canonical owner.
        const auto* ownerCheck = reinterpret_cast<const uint8_t*>(0x1408419AF);
        if (ownerCheck[0] == 0x75 && ownerCheck[1] == 0x4A)
        {
            const uint8_t nop[] = {0x90, 0x90};
            YGO::Memory::WriteBytes(0x1408419AF, nop, sizeof(nop));
        }
        else
            Logger::WriteLog("Shared character decks unavailable: opponent-list ownership check differs", MODULE_NAME, 2);
        if (g_HideEmptySeries)
            DetourAttach(&(PVOID&)orig_FreeDuelOnEnter, Hook_FreeDuelOnEnter);
        if (!g_SeriesNames.empty())
            DetourAttach(&(PVOID&)orig_SeriesLabel, Hook_SeriesLabel);
        if (g_entries.empty())
            return;
        DetourAttach(&(PVOID&)orig_LoadCharacterData, Hook_LoadCharacterData);
        DetourAttach(&(PVOID&)orig_FreeDuelBuild, Hook_FreeDuelBuild);
        DetourAttach(&(PVOID&)orig_CollectionCount, Hook_CollectionCount);
    }

    void ApplyIfLoaded()
    {
        if (g_entries.empty())
            return;
        for (uint32_t id = 0; id < kCharacterSlots; ++id)
            if (Record(id)->Key)
            {
                Apply();   // the game loaded its characters before this plugin was injected
                return;
            }
    }
}
