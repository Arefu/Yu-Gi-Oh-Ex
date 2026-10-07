#include <Windows.h>
#include <algorithm>
#include <cctype>
#include <cstring>
#include <deque>
#include <fstream>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

#include <json.hpp>

#include "Card.h"
#include "Detours.h"
#include "Logger.h"
#include "Related.h"
#include "Yu-Gi-Oh-Mods.h"
#include "Text.h"

namespace
{
    // taginfo_<L>.bin record (TagInfoRecord in the IDB). Key/Text are pointers once the game has loaded the file.
#pragma pack(push, 1)
    struct TagCondition
    {
        uint8_t Type;    // 0 ATK, 1 ATTR, 2 DEF, 3 KIND, 4 LEVEL, 5 ICON, 6 TYPE, 7 DECK, 8 RANK, 9 SPECIALSUMMON, 10 TRIBUTE, 11 TRIBUTE2, 12 LINK, 0xFF none
        uint8_t Op;      // 0 <=, 1 <, 2 =, 3 >=, 4 >, 5 !=
        uint16_t Value;
    };
    struct TagInfoRecord
    {
        uint8_t Group;   // 0 name ("Related to: ..."), 1 affects ({AD}), 2 matches ({FIND})
        uint8_t Flag1;
        uint8_t HasNumber;
        uint8_t Pad3;
        TagCondition Conditions[8];
        const wchar_t* Key;
        const wchar_t* Text;
    };
#pragma pack(pop)
    static_assert(sizeof(TagInfoRecord) == 0x34, "TagInfoRecord layout drifted");

    using Count_t = uint64_t(__fastcall*)(uint16_t konamiId);
    using List_t = const uint32_t*(__fastcall*)(uint16_t konamiId);
    using Tag_t = TagInfoRecord*(__fastcall*)(uint32_t tagId);

    uintptr_t orig_GetRelatedCardCount = 0x14076D640;   // YGO::CARDS::Get_RelatedCardCount
    uintptr_t orig_GetRelatedCardList = 0x14076D690;    // YGO::CARDS::Get_RelatedCardList
    uintptr_t orig_GetTagInfoRecord = 0x14076DF60;      // YGO::CARDS::Get_TagInfoRecord

    constexpr const char* kTypeKeys[] = { "ATK", "ATTR", "DEF", "KIND", "LEVEL", "ICON", "TYPE", "DECK", "RANK", "SPECIALSUMMON", "TRIBUTE", "TRIBUTE2", "LINK" };
    constexpr const char* kOps[] = { "<=", "<", "=", ">=", ">", "!=" };

    std::unordered_map<uint16_t, std::vector<uint32_t>> g_lists;   // Konami id -> its related units (u16 card | u16 tag << 16), sorted by card
    std::unordered_map<uint32_t, TagInfoRecord> g_tags;           // tag id -> replacement or new record
    std::deque<std::wstring> g_strings;                           // Key/Text storage (a deque keeps c_str() pointers stable)
    std::mutex g_lock;
    const uint32_t kNoUnits = 0;                                  // what an empty list points at (the game's lists end in a 0 unit too)

    std::wstring Utf8ToWide(const std::string& s)
    {
        if (s.empty())
            return {};
        int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0);
        std::wstring w(n, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), w.data(), n);
        return w;
    }

    const wchar_t* Keep(std::wstring text)
    {
        g_strings.push_back(std::move(text));
        return g_strings.back().c_str();
    }

    int IndexOf(const std::string& text, const char* const* table, size_t count)
    {
        for (size_t i = 0; i < count; ++i)
            if (_stricmp(text.c_str(), table[i]) == 0)
                return static_cast<int>(i);
        return -1;
    }

    bool IsNumeric(uint8_t type) { return type == 0 || type == 2 || type == 4 || type == 8 || type == 12; }

    // A tag from JSON, on top of the game's record (a changed tag) or an empty one (a new tag).
    TagInfoRecord ReadTag(const nlohmann::json& json, const TagInfoRecord* game)
    {
        TagInfoRecord tag{};
        if (game)
            tag = *game;
        else
        {
            for (auto& c : tag.Conditions)
                c = { 0xFF, 0xFF, 0 };
            tag.Key = Keep(L"");
            tag.Text = Keep(L"");
        }

        if (auto group = json.find("group"); group != json.end() && group->is_string())
        {
            const std::string g = group->get<std::string>();
            tag.Group = (_stricmp(g.c_str(), "affects") == 0 || _stricmp(g.c_str(), "ad") == 0) ? 1
                : (_stricmp(g.c_str(), "matches") == 0 || _stricmp(g.c_str(), "find") == 0) ? 2 : 0;
        }
        if (auto flag = json.find("flag1"); flag != json.end() && flag->is_number_integer())
            tag.Flag1 = static_cast<uint8_t>(flag->get<int>());

        if (auto conditions = json.find("conditions"); conditions != json.end() && conditions->is_array())
        {
            for (auto& c : tag.Conditions)
                c = { 0xFF, 0xFF, 0 };
            size_t slot = 0;
            tag.HasNumber = 0;
            for (auto& condition : *conditions)
            {
                if (slot >= std::size(tag.Conditions) || !condition.is_object())
                    break;
                int type = -1;
                if (auto t = condition.find("type"); t != condition.end())
                    type = t->is_string() ? IndexOf(t->get<std::string>(), kTypeKeys, std::size(kTypeKeys)) : t->is_number_integer() ? t->get<int>() : -1;
                if (type < 0 || type >= 0xFF)
                    continue;
                int op = 2;
                if (auto o = condition.find("op"); o != condition.end() && o->is_string())
                    if (int found = IndexOf(o->get<std::string>(), kOps, std::size(kOps)); found >= 0)
                        op = found;
                int value = condition.value("value", 0);
                tag.Conditions[slot++] = { static_cast<uint8_t>(type), static_cast<uint8_t>(op), static_cast<uint16_t>(value) };
                if (IsNumeric(static_cast<uint8_t>(type)))
                    tag.HasNumber = 1;
            }
        }

        if (auto key = json.find("key"); key != json.end() && key->is_string())
            tag.Key = Keep(Utf8ToWide(key->get<std::string>()));

        // the text for the game's current language, else English, else any
        if (auto texts = json.find("text"); texts != json.end() && texts->is_object() && !texts->empty())
        {
            const nlohmann::json* chosen = nullptr;
            for (char language : { Text::CurrentLanguage(), 'E' })
                for (auto& [letter, text] : texts->items())
                    if (!chosen && !letter.empty() && std::toupper(static_cast<unsigned char>(letter[0])) == language && text.is_string())
                        chosen = &text;
            if (!chosen && texts->begin()->is_string())
                chosen = &*texts->begin();
            if (chosen)
                tag.Text = Keep(Utf8ToWide(chosen->get<std::string>()));
        }
        return tag;
    }

    std::vector<uint32_t> ReadUnits(const nlohmann::json& entry, const char* key)
    {
        std::vector<uint32_t> units;
        auto list = entry.find(key);
        if (list == entry.end() || !list->is_array())
            return units;
        for (auto& unit : *list)
        {
            if (!unit.is_object())
                continue;
            int card = unit.value("card", 0), tag = unit.value("tag", -1);
            if (card >= 1 && card <= 0xFFFF && tag >= 0 && tag <= 0xFFFF)
                units.push_back(static_cast<uint32_t>(card) | static_cast<uint32_t>(tag) << 16);
        }
        return units;
    }

    uint64_t __fastcall Hook_GetRelatedCardCount(uint16_t konamiId)
    {
        {
            std::lock_guard guard(g_lock);
            if (auto it = g_lists.find(konamiId); it != g_lists.end())
                return it->second.size();
        }
        return reinterpret_cast<Count_t>(orig_GetRelatedCardCount)(konamiId);
    }

    const uint32_t* __fastcall Hook_GetRelatedCardList(uint16_t konamiId)
    {
        {
            std::lock_guard guard(g_lock);
            if (auto it = g_lists.find(konamiId); it != g_lists.end())
                return it->second.empty() ? &kNoUnits : it->second.data();
        }
        return reinterpret_cast<List_t>(orig_GetRelatedCardList)(konamiId);
    }

    TagInfoRecord* __fastcall Hook_GetTagInfoRecord(uint32_t tagId)
    {
        {
            std::lock_guard guard(g_lock);
            if (auto it = g_tags.find(tagId); it != g_tags.end())
                return &it->second;
        }
        return reinterpret_cast<Tag_t>(orig_GetTagInfoRecord)(tagId);
    }
}

namespace Related
{
    void Load()
    {
        std::lock_guard guard(g_lock);
        g_lists.clear();
        g_tags.clear();
        g_strings.clear();

        // cards.json's custom cards with their own "related" list: theirs, whatever relatedcards.json says for them
        std::unordered_map<uint16_t, bool> ownLists;
        for (const Card::ExtraCard& card : Card::ExtraCards)
            if (card.Related)
            {
                g_lists[card.ID] = *card.Related;
                ownLists[card.ID] = true;
            }
        if (!ownLists.empty())
            Logger::Log("cards.json: related cards for " + std::to_string(ownLists.size()) + " custom card(s)", MODULE_NAME, 1);

        // every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h): each mod's add/remove lists apply in load order
        std::vector<std::string> problems;
        auto root = YGO::Mods::ReadMerged("relatedcards.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::Log(problem + ", its related cards are left out", MODULE_NAME, 3);
        if (root.is_null())
            return; // relatedcards.json is optional
        try
        {
            size_t newTags = 0, changedTags = 0;
            if (auto tags = root.find("tags"); tags != root.end() && tags->is_array())
                for (auto& json : *tags)
                {
                    if (!json.is_object() || !json.contains("id") || !json["id"].is_number_integer() || json["id"].get<int>() < 0)
                        continue;
                    const uint32_t id = json["id"].get<uint32_t>();
                    // the game's own record (null past its tags); the hook isn't consulted here because g_tags was just cleared
                    const TagInfoRecord* game = reinterpret_cast<Tag_t>(orig_GetTagInfoRecord)(id);
                    g_tags[id] = ReadTag(json, game);
                    ++(game ? changedTags : newTags);
                }

            size_t links = 0;
            if (auto cards = root.find("cards"); cards != root.end() && cards->is_array())
                for (auto& entry : *cards)
                {
                    if (!entry.is_object() || !entry.contains("card") || !entry["card"].is_number_integer())
                        continue;
                    const int card = entry["card"].get<int>();
                    if (card < 1 || card > kLastExtraCardId)
                        continue;
                    const uint16_t id = static_cast<uint16_t>(card);
                    if (ownLists.contains(id))
                        continue;   // its cards.json entry has its list

                    auto& list = g_lists[id];
                    if (list.empty() && card < kFirstExtraCardId)   // start from the game's list (custom cards have none)
                    {
                        const uint64_t count = reinterpret_cast<Count_t>(orig_GetRelatedCardCount)(id);
                        const uint32_t* units = reinterpret_cast<List_t>(orig_GetRelatedCardList)(id);
                        if (units && count)
                            list.assign(units, units + count);
                    }
                    for (uint32_t unit : ReadUnits(entry, "remove"))
                        if (auto it = std::find(list.begin(), list.end(), unit); it != list.end())
                            list.erase(it);
                    for (uint32_t unit : ReadUnits(entry, "add"))
                        if (std::find(list.begin(), list.end(), unit) == list.end())
                            list.push_back(unit);
                    // the game's order: by related card (stable, so two units for one card keep their order)
                    std::stable_sort(list.begin(), list.end(), [](uint32_t a, uint32_t b) { return (a & 0xFFFF) < (b & 0xFFFF); });
                    links += list.size();
                }

            Logger::Log("relatedcards.json: " + std::to_string(newTags) + " new and " + std::to_string(changedTags) + " changed tag(s), related cards for " +
                std::to_string(g_lists.size()) + " card(s) (" + std::to_string(links) + " links)", MODULE_NAME, 1);
        }
        catch (const std::exception& ex)
        {
            g_lists.clear();
            g_tags.clear();
            Logger::Log(std::string("relatedcards.json couldn't be read: ") + ex.what(), MODULE_NAME, 3);
        }
    }

    void Attach()
    {
        DetourAttach(&(PVOID&)orig_GetRelatedCardCount, Hook_GetRelatedCardCount);
        DetourAttach(&(PVOID&)orig_GetRelatedCardList, Hook_GetRelatedCardList);
        DetourAttach(&(PVOID&)orig_GetTagInfoRecord, Hook_GetTagInfoRecord);
    }
}
