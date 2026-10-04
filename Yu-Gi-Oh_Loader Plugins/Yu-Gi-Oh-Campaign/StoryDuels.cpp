#include "StoryDuels.h"
#include "Common.h"

#include <Windows.h>
#include <array>
#include <cstdint>
#include <deque>
#include <format>
#include <fstream>
#include <map>
#include <optional>
#include <string>

#include <json.hpp>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    using namespace Campaign;

    // ---- the game (names and layouts in the IDB) ----

    struct StoryDuelSide
    {
        int32_t Character;        // chardata id
        int32_t Deck;             // deckdata index (the duel uses + 32)
        const char* Costume;      // portrait "<character key>_<costume>_neutral"; "" = normal
    };

    struct StoryDuelRecord        // g_StoryDuelRecords entry (0xB0)
    {
        int32_t Id;               // 0 = empty slot
        int32_t Pad4;
        StoryDuelSide Sides[2];   // [0] player, [1] opponent
        int32_t Arena;
        int32_t Sku;              // the loader turns the file's -1 into 1
        int32_t RewardPack;       // pack unlocked on the first win; the loader turns -1 into 0 (= none)
        int32_t Pad34;
        const char* Key;          // dialog scripts <key>_INTRO/_OUTRO/_OUTRO_LOSE (scriptdata)
        GameWString Title, Description, Tip;
        int32_t Series;
        int32_t Order;
        int32_t ExcludeFromDeckUnlock;
        int32_t PadAC;
    };
    static_assert(sizeof(StoryDuelRecord) == 0xB0, "StoryDuelRecord layout drifted");

#pragma pack(push, 4)
    struct StoryDuelFileRecord    // dueldata record after the loader made the offsets absolute (0x5C)
    {
        int32_t Id, Series, Order;
        int32_t Character[2], Deck[2];
        int32_t Arena, RewardPack, Sku, ExcludeFromDeckUnlock;
        const char* Key;
        const char* Costume[2];
        const wchar_t* Title;
        const wchar_t* Description;
        const wchar_t* Tip;
    };
#pragma pack(pop)
    static_assert(sizeof(StoryDuelFileRecord) == 0x5C, "StoryDuelFileRecord layout drifted");

    constexpr uintptr_t kStoryDuelRecords = 0x142919DA0;   // g_StoryDuelRecords[226]
    constexpr uint32_t kStoryDuelSlots = 226;
    constexpr int kMaxOrder = 48;                          // the save keeps 24 bytes per (series, order) in 1208 per series
    constexpr int kDeckSlots = 700;                        // Get_DeckRecord returns null past this and the game writes through it

    using LoadStoryDuelData_t = void(__fastcall*)();
    using FromFileRecord_t = void(__fastcall*)(StoryDuelRecord* dst, const StoryDuelFileRecord* src);
    using BuildSeriesDuelLists_t = void(__fastcall*)();

    uintptr_t orig_LoadStoryDuelData = 0x1407FF6D0;        // LoadStoryDuelData
    const auto FromFileRecord = reinterpret_cast<FromFileRecord_t>(0x1407FF810);              // StoryDuel_FromFileRecord
    const auto BuildSeriesDuelLists = reinterpret_cast<BuildSeriesDuelLists_t>(0x14074A3B0);  // Campaign_BuildSeriesDuelLists

    StoryDuelRecord* Record(uint32_t id) { return id < kStoryDuelSlots ? reinterpret_cast<StoryDuelRecord*>(kStoryDuelRecords) + id : nullptr; }

    // ---- storyduels.json ----

    struct Side
    {
        std::optional<int> Character, Deck;
        std::optional<std::string> Costume;
    };

    struct Entry
    {
        uint32_t Id = 0;
        std::optional<int> Series, Order, Arena, RewardPack, Sku, ExcludeFromDeckUnlock;
        std::optional<std::string> Key;
        std::array<Side, 2> Sides;
        std::map<char, std::wstring> Titles, Descriptions, Tips;
        bool IsNew = false;       // decided when first applied: the game had no duel in the slot
    };

    std::deque<Entry> g_entries;  // deque: records point at Key/Costume strings

    void ReadSide(const nlohmann::json& json, const char* key, Side& side)
    {
        auto it = json.find(key);
        if (it == json.end() || !it->is_object())
            return;
        side.Character = Int(*it, "character");
        side.Deck = Int(*it, "deck");
        side.Costume = Str(*it, "costume");
    }

    void Load()
    {
        // every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h): later mods win for the same id
        std::vector<std::string> problems;
        auto root = YGO::Mods::ReadMerged("storyduels.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", it is left out", MODULE_NAME, 2);
        if (root.is_null())
            return; // storyduels.json is optional
        try
        {
            auto list = root.find("duels");
            if (list == root.end() || !list->is_array())
                return;
            for (auto& json : *list)
            {
                if (!json.is_object())
                    continue;
                Entry entry;
                auto id = Int(json, "id");
                if (!id || *id <= 0 || *id >= static_cast<int>(kStoryDuelSlots))
                {
                    Logger::WriteLog("storyduels.json: an entry without an id 1-225 was skipped", MODULE_NAME, 2);
                    continue;
                }
                entry.Id = static_cast<uint32_t>(*id);
                entry.Series = Int(json, "series");
                entry.Order = Int(json, "order");
                entry.Arena = Int(json, "arena");
                entry.RewardPack = Int(json, "rewardPack");
                entry.Sku = Int(json, "sku");
                entry.ExcludeFromDeckUnlock = Int(json, "excludeFromDeckUnlock");
                entry.Key = Str(json, "key");
                ReadSide(json, "player", entry.Sides[0]);
                ReadSide(json, "opponent", entry.Sides[1]);
                ReadTexts(json, "title", entry.Titles);
                ReadTexts(json, "description", entry.Descriptions);
                ReadTexts(json, "tip", entry.Tips);
                g_entries.push_back(std::move(entry));
            }
            Logger::WriteLog(std::format("storyduels.json: {} duel(s)", g_entries.size()), MODULE_NAME, 0);
        }
        catch (const std::exception& ex)
        {
            g_entries.clear();
            Logger::WriteLog(std::string("storyduels.json couldn't be read: ") + ex.what(), MODULE_NAME, 3);
        }
    }

    // Whether the numbers would make the game misbehave (they go straight into its tables).
    std::string Problem(const StoryDuelFileRecord& r)
    {
        if (r.Series < 0 || r.Series > 5)
            return "series must be 0-5";
        if (r.Order < 1 || r.Order > kMaxOrder)
            return std::format("order must be 1-{}", kMaxOrder);
        for (int side = 0; side < 2; ++side)
            if (r.Deck[side] < 0 || r.Deck[side] >= kDeckSlots)
                return std::format("the {} deck must be 0-{}", side == 0 ? "player" : "opponent", kDeckSlots - 1);
        if (!r.Key || !*r.Key)
            return "needs a \"key\"";
        return {};
    }

    // Runs after every LoadStoryDuelData: the game rewrote its slots (language change), ours are written again.
    void Apply()
    {
        size_t changed = 0, added = 0;
        for (Entry& entry : g_entries)
        {
            StoryDuelRecord* record = Record(entry.Id);
            const bool ours = entry.IsNew && entry.Key && record->Key == entry.Key->c_str();
            const bool isNew = record->Id == 0 || ours;

            // Start from the game's duel (a changed one) or empty (a new one), then lay the entry's fields over it.
            StoryDuelFileRecord r{};
            std::wstring title, description, tip;    // copies: FromFileRecord frees the record's old strings
            if (isNew)
            {
                r.Sku = 1;
                r.RewardPack = -1;
                r.Key = r.Costume[0] = r.Costume[1] = "";
            }
            else
            {
                r.Series = record->Series;
                r.Order = record->Order;
                r.Arena = record->Arena;
                r.RewardPack = record->RewardPack == 0 ? -1 : record->RewardPack;
                r.Sku = record->Sku;
                r.ExcludeFromDeckUnlock = record->ExcludeFromDeckUnlock;
                r.Key = record->Key;
                for (int side = 0; side < 2; ++side)
                {
                    r.Character[side] = record->Sides[side].Character;
                    r.Deck[side] = record->Sides[side].Deck;
                    r.Costume[side] = record->Sides[side].Costume;
                }
                title = record->Title.c_str();
                description = record->Description.c_str();
                tip = record->Tip.c_str();
            }
            r.Id = static_cast<int32_t>(entry.Id);
            if (entry.Series) r.Series = *entry.Series;
            if (entry.Order) r.Order = *entry.Order;
            if (entry.Arena) r.Arena = *entry.Arena;
            if (entry.RewardPack) r.RewardPack = *entry.RewardPack;
            if (entry.Sku) r.Sku = *entry.Sku;
            if (entry.ExcludeFromDeckUnlock) r.ExcludeFromDeckUnlock = *entry.ExcludeFromDeckUnlock;
            if (entry.Key) r.Key = entry.Key->c_str();
            for (int side = 0; side < 2; ++side)
            {
                const Side& s = entry.Sides[side];
                if (s.Character) r.Character[side] = *s.Character;
                if (s.Deck) r.Deck[side] = *s.Deck;
                if (s.Costume) r.Costume[side] = s.Costume->c_str();
                if (!r.Costume[side]) r.Costume[side] = "";
            }
            if (const std::wstring* text = Pick(entry.Titles)) title = *text;
            if (const std::wstring* text = Pick(entry.Descriptions)) description = *text;
            if (const std::wstring* text = Pick(entry.Tips)) tip = *text;
            r.Title = title.c_str();
            r.Description = description.c_str();
            r.Tip = tip.c_str();

            if (std::string problem = Problem(r); !problem.empty())
            {
                Logger::WriteLog(std::format("storyduels.json: duel {} skipped: {}", entry.Id, problem), MODULE_NAME, 2);
                continue;
            }
            FromFileRecord(record, &r);
            entry.IsNew = isNew;
            ++(isNew ? added : changed);
        }
        if (!g_entries.empty())
            Logger::WriteLog(std::format("Story duels: {} changed, {} new", changed, added), MODULE_NAME, 0);
    }

    void __fastcall Hook_LoadStoryDuelData()
    {
        reinterpret_cast<LoadStoryDuelData_t>(orig_LoadStoryDuelData)();
        Apply();   // before Campaign_BuildSeriesDuelLists, which LoadLanguageContent calls next
    }
}

namespace StoryDuels
{
    void Attach()
    {
        Load();
        if (g_entries.empty())
            return;
        DetourAttach(&(PVOID&)orig_LoadStoryDuelData, Hook_LoadStoryDuelData);
    }

    void ApplyIfLoaded()
    {
        if (g_entries.empty())
            return;
        for (uint32_t id = 1; id < kStoryDuelSlots; ++id)
            if (Record(id)->Id != 0)
            {
                Apply();                  // the game loaded its duels before this plugin was injected
                BuildSeriesDuelLists();   // so the series lists see the new orders
                return;
            }
    }
}
