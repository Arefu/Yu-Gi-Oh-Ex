#include "StoryDuels.h"
#include "Common.h"

#include <Windows.h>
#include <intrin.h>
#include <cstring>
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
#include "Yu-Gi-Oh-Memory.h"
#include "Yu-Gi-Oh-Mods.h"
#include "YuGiOh/YuGiOh-Addresses.h"

namespace
{
    using namespace Campaign;

    // ---- the game (names and layouts in the IDB) ----

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

    constexpr uintptr_t kStoryDuelRecords = YGO::Addr::g_StoryDuelRecords;   // g_StoryDuelRecords[226]
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
        std::optional<int> Tutorial;  // "tutorial": the tutorial number this duel plays as (21-99), 0 = none (the game: order 1 only)
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
                entry.Tutorial = Int(json, "tutorial");
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
        if (r.Series < 0 || r.Series > 63)
            return "series must be 0-63 (6+ = a new series from campaigns.json)";
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

    // ---- any duel can be a tutorial ----
    //
    // The game: CampaignMenu_Update (0x140860040) makes a campaign duel a tutorial only when it is order 1, not the reverse duel, and the
    // menu's flag (+5736) is set; the tutorial number is g_CampaignSeriesTutorial[series] (0x140A77E68, 21-26). TutorialStoryDuel_SetupSeats
    // (0x14081EE80) then seats order 1 of series (number - 21) and adds that tutorial's extra cards (table 0x140A710B0: 2 u16* lists per
    // tutorial 21-26, 0-ended). Here "tutorial": N on a storyduels.json duel decides instead: N > 0 plays the duel as tutorial N
    // (Campaign Tutorials.cpp has the new numbers 27-99), 0 = never a tutorial; the seats are the duel's own, with tutorial N's extra
    // cards when N is one of the game's.

    using SetFlag_t = void(__fastcall*)(int value);
    using GetInt_t = int(__fastcall*)();
    using SetupSeats_t = void(__fastcall*)();
    uintptr_t orig_SetIsTutorialDuel = 0x140769B00;     // YGO::DUEL::Set_IsTutorialDuel
    uintptr_t orig_SetupTutorialSeats = 0x14081EE80;    // TutorialStoryDuel_SetupSeats
    const auto SetTutorialDuelIndex = reinterpret_cast<SetFlag_t>(0x140769B20);   // YGO::DUEL::Set_TutorialDuelIndex
    const auto GetCurrentStoryDuelId = reinterpret_cast<GetInt_t>(0x140768D60);  // Get_CurrentStoryDuelId (set by Set_ArenaIdFromDuel)
    const auto IsReverseDuel = reinterpret_cast<GetInt_t>(0x1407694C0);          // Campaign_IsReverseDuel
    constexpr uintptr_t kCampaignMenuUpdate = 0x140860040, kCampaignMenuUpdateEnd = 0x140860040 + 0x5FC;
    constexpr uintptr_t kTutorialExtraCards = 0x140A710B0;   // const uint16_t* [6][2]

    using SetSide_t = void(__fastcall*)(int side, int value);
    using SideValue_t = int(__fastcall*)(int side);
    using DeckFromId_t = void*(__fastcall*)(unsigned int profile, unsigned int deck);
    using AddCard_t = char(__fastcall*)(void* deck, int section, int16_t card);
    using Portrait_t = void*(__fastcall*)(void* out, unsigned int duel, unsigned int side);
    using Extra_t = int64_t*(__fastcall*)(unsigned int duel, unsigned int side);
    using SetupSeat_t = void(__fastcall*)(int seat, unsigned int character, const wchar_t* name, void* deck, int profile, void* portrait, int64_t* extra);
    const auto SetSideCharacter = reinterpret_cast<SetSide_t>(0x140769670);   // Duel_Set_DuelSideCharacter
    const auto SetSideDeck = reinterpret_cast<SetSide_t>(0x140769690);        // Set_DuelSideDeck
    const auto SideDeck = reinterpret_cast<SideValue_t>(0x140768DA0);          // the side's deck (as set)
    const auto SideCharacter = reinterpret_cast<SideValue_t>(0x140768D90);     // the side's character (as set)
    const auto DeckFromId = reinterpret_cast<DeckFromId_t>(0x14081AB40);       // Deck_FromId
    const auto AddCard = reinterpret_cast<AddCard_t>(0x14076EC30);             // deck: add a card to a section
    const auto SidePortrait = reinterpret_cast<Portrait_t>(0x140768C70);
    const auto SideExtra = reinterpret_cast<Extra_t>(0x140768AB0);
    const auto SetupSeat = reinterpret_cast<SetupSeat_t>(0x14076A7B0);         // Duel_SetupSeat

    std::map<uint32_t, int> g_tutorialByDuel;   // story duel id -> its "tutorial"
    int g_customTutorial = 0;                    // the tutorial number a storyduels.json duel is playing as now, 0 = none

    void __fastcall Hook_SetIsTutorialDuel(int value)
    {
        const auto from = reinterpret_cast<uintptr_t>(_ReturnAddress());
        g_customTutorial = 0;
        if (from >= kCampaignMenuUpdate && from < kCampaignMenuUpdateEnd)
            if (auto it = g_tutorialByDuel.find(static_cast<uint32_t>(GetCurrentStoryDuelId())); it != g_tutorialByDuel.end())
            {
                const bool on = it->second > 0 && !IsReverseDuel();
                reinterpret_cast<SetFlag_t>(orig_SetIsTutorialDuel)(on ? 1 : 0);
                if (on)
                {
                    SetTutorialDuelIndex(it->second);
                    g_customTutorial = it->second;
                }
                return;
            }
        reinterpret_cast<SetFlag_t>(orig_SetIsTutorialDuel)(value);
    }

    // The tutorial's seats: the duel's own (not order 1 of series number - 21), with the game tutorial's extra cards for 21-26.
    void __fastcall Hook_SetupTutorialSeats()
    {
        if (g_customTutorial == 0)
            return reinterpret_cast<SetupSeats_t>(orig_SetupTutorialSeats)();
        const unsigned int duel = static_cast<unsigned int>(GetCurrentStoryDuelId());
        const StoryDuelRecord* record = reinterpret_cast<const StoryDuelRecord*>(kStoryDuelTable) + duel;
        const auto* extras = g_customTutorial >= 21 && g_customTutorial <= 26
            ? reinterpret_cast<const uint16_t* const*>(kTutorialExtraCards) + 2 * (g_customTutorial - 21) : nullptr;
        for (int side = 0; side < 2; ++side)
        {
            SetSideCharacter(side, record->Sides[side].Character);
            SetSideDeck(side, record->Sides[side].Deck + 32);
            uint8_t deck[256]{};
            std::memcpy(deck, DeckFromId(0xFFFFFFFD, static_cast<unsigned int>(SideDeck(side))), 252);
            if (extras && extras[side])
                for (const uint16_t* card = extras[side]; *card; ++card)
                    AddCard(deck, 0, static_cast<int16_t>(*card));
            uint8_t portrait[64]{};
            void* name = SidePortrait(portrait, duel, side);
            int64_t* extra = SideExtra(duel, side);
            SetupSeat(side, static_cast<unsigned int>(SideCharacter(side)), nullptr, deck, -1, name, extra);
        }
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
        // Reverse duels for order 1 (the series' tutorial duel), which the game leaves out in two places:
        //  - CampaignMenu_Update 0x14086058A: "test ecx, 0xFFFFFFFD" on order + 1 refuses the reverse button for order 1 (and -1 = nothing
        //    picked); mask 0xFFFFFFFF refuses only -1.
        //  - DuelResult_GrantRewards 0x14088066B: "cmp dword [rax+0xA4], 1" (the duel's Order) skips unlocking the reverse duel after a win;
        //    comparing with 0 (no duel has order 0) unlocks it like any other.
        // The reverse duel is a normal duel: the tutorial only runs when the duel is not the reverse one (the game's rule and ours).
        const bool menu = YGO::Memory::PatchBytes(0x14086058A, { 0xF7, 0xC1, 0xFD, 0xFF, 0xFF, 0xFF }, { 0xF7, 0xC1, 0xFF, 0xFF, 0xFF, 0xFF },
            "Reverse duel button for order 1");
        const bool reward = YGO::Memory::PatchBytes(0x14088066B, { 0x83, 0xB8, 0xA4, 0x00, 0x00, 0x00, 0x01 }, { 0x83, 0xB8, 0xA4, 0x00, 0x00, 0x00, 0x00 },
            "Reverse duel unlock for order 1");
        Logger::WriteLog(std::format("Reverse duels for tutorial duels (order 1): {}", menu && reward ? "on" : "not patched"), MODULE_NAME, menu && reward ? 0 : 1);

        Load();
        if (g_entries.empty())
            return;
        DetourAttach(&(PVOID&)orig_LoadStoryDuelData, Hook_LoadStoryDuelData);
        for (const Entry& entry : g_entries)
            if (entry.Tutorial)
                g_tutorialByDuel[entry.Id] = *entry.Tutorial;
        if (!g_tutorialByDuel.empty())
        {
            DetourAttach(&(PVOID&)orig_SetIsTutorialDuel, Hook_SetIsTutorialDuel);
            DetourAttach(&(PVOID&)orig_SetupTutorialSeats, Hook_SetupTutorialSeats);
            Logger::WriteLog(std::format("Story duels: {} with their own tutorial setting", g_tutorialByDuel.size()), MODULE_NAME, 0);
        }
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
