#include "Decks.h"
#include "Common.h"

#include <Windows.h>
#include <cstdint>
#include <cstring>
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

namespace
{
    using namespace Campaign;

    // ---- the game (names and layouts in the IDB) ----

    struct DeckRecord             // g_DeckRecords entry (0x88)
    {
        int32_t Slot;             // = the deck id
        int32_t Series;           // 0-5, -1 starter
        int32_t CharacterId;      // owner
        uint16_t SignatureCard;   // Konami id, 0xFFFF none
        uint16_t PadE;
        const char* FileName;     // decks.zib/<FileName>.ydc; non-null = the deck exists
        GameWString Title, Text2, Text3;
        int32_t StoryDuelId;      // set by StoryDuel_FromFileRecord
        int32_t Role;             // 0 starter, 1 story deck, 2 a character's deck, 3 granted with its SKU (AssignDeckRoles), -1 none
        int32_t Sku;              // the loader turns the file's -1 into 1
        int32_t Pad84;
    };
    static_assert(sizeof(DeckRecord) == 0x88, "DeckRecord layout drifted");

    struct DeckTemplate           // YGO::SAVE::DeckListItem (0x130): a deck's cards, as Konami ids
    {
        wchar_t Name[33];
        int16_t MainCount, ExtraCount, SideCount;
        uint16_t Main[60];
        uint16_t Extra[15];
        uint16_t Side[15];
        uint8_t FieldFC[0x24];
        int32_t RecordSlot;       // the record's first 8 bytes
        int32_t RecordSeries;
        int32_t Field128;
        uint8_t IsValid;          // DeckTemplate_SetValid
        uint8_t Pad12D[3];
    };
    static_assert(sizeof(DeckTemplate) == 0x130, "DeckTemplate layout drifted");

    constexpr uintptr_t kDeckRecords = 0x1428FC080;        // g_DeckRecords[700]
    constexpr uintptr_t kDeckTemplates = 0x14275AC50;      // YGO::GAME::DeckTemplateList[700]
    constexpr uint32_t kDeckSlots = 700;
    constexpr size_t kDeckStates = 0x38;                   // player section: u32 per deck id, 1 = unlocked

    using Void_t = void(__fastcall*)();
    using RebuildLiveUnlockCounts_t = int64_t(__fastcall*)(unsigned int profile);

    uintptr_t orig_LoadDeckDataFile = 0x1407FE500;         // YGO::GAME::LoadDeckDataFile
    uintptr_t orig_LoadDeckTemplates = 0x1407BD2E0;        // YGO::GAME::LoadDeckTemplatesFromDecksZib
    uintptr_t orig_RebuildLiveUnlockCounts = 0x1407BB160;  // YGO::SAVE::RebuildLiveUnlockCounts

    DeckRecord* Record(uint32_t id) { return id < kDeckSlots ? reinterpret_cast<DeckRecord*>(kDeckRecords) + id : nullptr; }
    DeckTemplate* Template(uint32_t id) { return id < kDeckSlots ? reinterpret_cast<DeckTemplate*>(kDeckTemplates) + id : nullptr; }

    // ---- decks.json ----

    struct Entry
    {
        uint32_t Id = 0;
        std::optional<int> Series, Character, SignatureCard, Sku;
        std::optional<std::string> FileName;
        std::map<char, std::wstring> Titles, Texts2, Texts3;
        bool HasCards = false;
        std::vector<uint16_t> Main, Extra, Side;
        bool Unlocked = false;    // new decks only
        bool IsNew = false;       // decided when applied: the game had no deck in the slot
    };

    std::deque<Entry> g_entries;  // deque: records point at FileName strings

    std::vector<uint16_t> Cards(const nlohmann::json& cards, const char* key)
    {
        std::vector<uint16_t> ids;
        if (auto it = cards.find(key); it != cards.end() && it->is_array())
            for (auto& id : *it)
                if (id.is_number_integer() && id.get<int>() > 0 && id.get<int>() <= 0xFFFF)
                    ids.push_back(static_cast<uint16_t>(id.get<int>()));
        return ids;
    }

    void Load()
    {
        // every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h): later mods win for the same id
        std::vector<std::string> problems;
        auto root = YGO::Mods::ReadMerged("decks.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", it is left out", MODULE_NAME, 2);
        if (root.is_null())
            return; // decks.json is optional
        try
        {
            auto list = root.find("decks");
            if (list == root.end() || !list->is_array())
                return;
            for (auto& json : *list)
            {
                if (!json.is_object())
                    continue;
                auto id = Int(json, "id");
                if (!id || *id < 0 || *id >= static_cast<int>(kDeckSlots))
                {
                    Logger::WriteLog("decks.json: an entry without an id 0-699 was skipped", MODULE_NAME, 2);
                    continue;
                }
                Entry entry;
                entry.Id = static_cast<uint32_t>(*id);
                entry.Series = Int(json, "series");
                entry.Character = Int(json, "character");
                entry.SignatureCard = Int(json, "signatureCard");
                entry.Sku = Int(json, "sku");
                entry.FileName = Str(json, "file");
                ReadTexts(json, "title", entry.Titles);
                ReadTexts(json, "text2", entry.Texts2);
                ReadTexts(json, "text3", entry.Texts3);
                if (auto cards = json.find("cards"); cards != json.end() && cards->is_object())
                {
                    entry.HasCards = true;
                    entry.Main = Cards(*cards, "main");
                    entry.Extra = Cards(*cards, "extra");
                    entry.Side = Cards(*cards, "side");
                    if (entry.Main.size() > 60 || entry.Extra.size() > 15 || entry.Side.size() > 15)
                    {
                        Logger::WriteLog(std::format("decks.json: deck {} has more than 60 main / 15 extra / 15 side cards, its cards were skipped", entry.Id), MODULE_NAME, 2);
                        entry.HasCards = false;
                    }
                }
                if (auto unlocked = json.find("unlocked"); unlocked != json.end() && unlocked->is_boolean())
                    entry.Unlocked = unlocked->get<bool>();
                g_entries.push_back(std::move(entry));
            }
            Logger::WriteLog(std::format("decks.json: {} deck(s)", g_entries.size()), MODULE_NAME, 0);
        }
        catch (const std::exception& ex)
        {
            g_entries.clear();
            Logger::WriteLog(std::string("decks.json couldn't be read: ") + ex.what(), MODULE_NAME, 3);
        }
    }

    // Runs after every LoadDeckDataFile: the game rewrote its records (language change), ours are written again.
    void ApplyRecords()
    {
        size_t changed = 0, added = 0;
        for (Entry& entry : g_entries)
        {
            DeckRecord* record = Record(entry.Id);
            const bool ours = entry.IsNew && entry.FileName && record->FileName == entry.FileName->c_str();
            if (!record->FileName || ours)
            {
                if (!entry.FileName || entry.FileName->empty())
                {
                    Logger::WriteLog(std::format("decks.json: new deck {} needs a \"file\" name, skipped", entry.Id), MODULE_NAME, 2);
                    continue;
                }
                entry.IsNew = true;
                record->Slot = static_cast<int32_t>(entry.Id);
                record->Series = entry.Series.value_or(0);
                record->CharacterId = entry.Character.value_or(0);
                record->SignatureCard = static_cast<uint16_t>(entry.SignatureCard.value_or(0xFFFF));
                record->Sku = entry.Sku.value_or(1);
                record->StoryDuelId = -1;
                record->Role = -1;   // the game's story duels and AssignDeckRoles (both run after this) set it
                ++added;
            }
            else
            {
                if (entry.Series) record->Series = *entry.Series;
                if (entry.Character) record->CharacterId = *entry.Character;
                if (entry.SignatureCard) record->SignatureCard = static_cast<uint16_t>(*entry.SignatureCard);
                if (entry.Sku) record->Sku = *entry.Sku;
                ++changed;
            }
            if (record->Sku == -1)
                record->Sku = 1;   // as the game's loader does
            if (entry.FileName && !entry.FileName->empty())
                record->FileName = entry.FileName->c_str();
            if (const std::wstring* text = Pick(entry.Titles)) WStringAssign(&record->Title, text->c_str(), text->size());
            if (const std::wstring* text = Pick(entry.Texts2)) WStringAssign(&record->Text2, text->c_str(), text->size());
            if (const std::wstring* text = Pick(entry.Texts3)) WStringAssign(&record->Text3, text->c_str(), text->size());
        }
        if (!g_entries.empty())
            Logger::WriteLog(std::format("Decks: {} changed, {} new", changed, added), MODULE_NAME, 0);
    }

    // After the game read decks.zib: the decks with "cards" get them (Konami ids, as a .ydc holds them).
    void ApplyCards()
    {
        size_t filled = 0;
        for (const Entry& entry : g_entries)
        {
            DeckTemplate* deck = Template(entry.Id);
            const DeckRecord* record = Record(entry.Id);
            if (!entry.HasCards || !deck || !record->FileName)
                continue;
            std::memset(deck, 0, sizeof(DeckTemplate));
            if (const std::wstring* title = Pick(entry.Titles))
                wcsncpy_s(deck->Name, title->c_str(), _TRUNCATE);
            else
                wcsncpy_s(deck->Name, record->Title.c_str(), _TRUNCATE);
            deck->MainCount = static_cast<int16_t>(entry.Main.size());
            deck->ExtraCount = static_cast<int16_t>(entry.Extra.size());
            deck->SideCount = static_cast<int16_t>(entry.Side.size());
            std::copy(entry.Main.begin(), entry.Main.end(), deck->Main);
            std::copy(entry.Extra.begin(), entry.Extra.end(), deck->Extra);
            std::copy(entry.Side.begin(), entry.Side.end(), deck->Side);
            deck->RecordSlot = record->Slot;
            deck->RecordSeries = record->Series;
            deck->IsValid = 1;
            ++filled;
        }
        if (filled)
            Logger::WriteLog(std::format("Decks: cards for {} deck(s)", filled), MODULE_NAME, 0);
    }

    void UnlockNewDecks()
    {
        uint8_t* player = GetPlayerSection(0xFFFFFFFD);
        if (!player)
            return;
        auto* states = reinterpret_cast<uint32_t*>(player + kDeckStates);
        for (const Entry& entry : g_entries)
        {
            if (!entry.IsNew || !entry.Unlocked || states[entry.Id] != 0)
                continue;
            states[entry.Id] = 1;   // MarkDeckStateKnown
            Logger::WriteLog(std::format("Deck {} unlocked", entry.Id), MODULE_NAME, 0);
        }
    }

    void __fastcall Hook_LoadDeckDataFile()
    {
        reinterpret_cast<Void_t>(orig_LoadDeckDataFile)();
        ApplyRecords();
    }

    void __fastcall Hook_LoadDeckTemplates()
    {
        reinterpret_cast<Void_t>(orig_LoadDeckTemplates)();
        ApplyCards();
    }

    int64_t __fastcall Hook_RebuildLiveUnlockCounts(unsigned int profile)
    {
        UnlockNewDecks();
        return reinterpret_cast<RebuildLiveUnlockCounts_t>(orig_RebuildLiveUnlockCounts)(profile);
    }
}

namespace Decks
{
    void Attach()
    {
        Load();
        if (g_entries.empty())
            return;
        DetourAttach(&(PVOID&)orig_LoadDeckDataFile, Hook_LoadDeckDataFile);
        DetourAttach(&(PVOID&)orig_LoadDeckTemplates, Hook_LoadDeckTemplates);
        DetourAttach(&(PVOID&)orig_RebuildLiveUnlockCounts, Hook_RebuildLiveUnlockCounts);
    }

    void ApplyIfLoaded()
    {
        if (g_entries.empty())
            return;
        for (uint32_t id = 0; id < kDeckSlots; ++id)
            if (Record(id)->FileName)
            {
                // the game loaded its decks before this plugin was injected (then the story duels and roles already ran too)
                ApplyRecords();
                if (Template(id)->IsValid || Template(380)->IsValid)
                    ApplyCards();
                return;
            }
    }
}
