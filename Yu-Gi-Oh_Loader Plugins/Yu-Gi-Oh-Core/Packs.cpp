#include "Packs.h"

#include <Windows.h>
#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <limits>
#include <deque>
#include <fstream>
#include <map>
#include <cstdio>
#include <string>
#include <unordered_set>
#include <vector>

#include <detours.h>
#include <json.hpp>

#include "Logger.h"   // Core starts before Yu-Gi-Oh-Console: its own log file
#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Mods.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-RIX.h"
#include "YuGiOh/YuGiOh-Addresses.h"
#include "YuGiOh/YuGiOh-RIX.h"

namespace
{

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

    constexpr uintptr_t kPackRecords = YGO::Addr::g_PackRecords;      // g_PackRecords[128]
    constexpr uint32_t kPackCount = 128;
    constexpr uint32_t kRewardPack = 'R';
    constexpr size_t kUnlockBits = 0xB80;                // player section: u32[4], bit = pack id
    constexpr uintptr_t kGameLanguageId = YGO::Addr::g_iGameLanguageID;       // g_iGameLanguageID
    constexpr uintptr_t kLanguageLetterTable = YGO::Addr::LanguageLetterTable;  // {int id, char letter at +4}, ended by id -1

    using LoadPackDefinitions_t = char(__fastcall*)();
    using GetArtName_t = char*(__fastcall*)(PackRecord* record, char* outString);
    using BuildPackList_t = void(__fastcall*)(int64_t screen, int series);
    using SetupShopHelp_t = void(__fastcall*)(int64_t screen);
    using WStringAssign_t = void*(__fastcall*)(GameWString* self, const wchar_t* text, size_t count);
    using GetPlayerSection_t = uint8_t*(__fastcall*)(unsigned int profile);
    using ShopOpen_t = void(__fastcall*)(int64_t screen);
    using ShowSeriesArt_t = void(__fastcall*)(int64_t screen);
    using SelectLayerByName_t = void(__fastcall*)(void* animoo, const char* name);
    using ShopSetState_t = void(__fastcall*)(int64_t screen, int state, double unused);

    // Shop rows (the tabs, "series"): BoosterShop_Open sets the row list to the game's 6 (ListState_Init(screen+4472, 6, 6, wrap)), and
    // that is the only place the 6 lives. The row's banner is the animoo layer "wrap_series_<row+1>" (BoosterShop_ShowSeriesArt), which
    // only exists for the game's rows, so a new row borrows one. The pack list of a row already scrolls (ListState at screen+4528).
    constexpr uint32_t kGameRows = 6;
    constexpr int64_t kRowList = 4472;                   // ListState {+0 cursor, +4 count, +8 top, +12 shown, ...}
    constexpr int64_t kShopAnimoo = 1032;                // DFX::TLayerAnimoo* the banner layers live in
    uintptr_t orig_ShopOpen = 0x14085D5B0;               // YGO::UI::BoosterShop_Open
    uintptr_t orig_ShowSeriesArt = 0x14085E0C0;          // YGO::UI::BoosterShop_ShowSeriesArt
    uintptr_t orig_ShopSetState = 0x14085D790;           // BoosterShop_SetState: 6 = leaving (NavigateToScreen back), 7 = gone
    const auto SelectLayerByName = reinterpret_cast<SelectLayerByName_t>(0x14075BB00);  // DFX::TLayerAnimoo::SelectByName

    uintptr_t orig_LoadPackDefinitions = 0x14080E3C0;    // YGO::GAME::LoadPackDefinitions
    uintptr_t orig_Pack_GetArtName = 0x14080E390;        // YGO::GAME::Pack_GetArtName
    uintptr_t orig_BuildPackList = 0x14085D6E0;          // YGO::UI::BoosterShop_BuildPackList
    uintptr_t orig_SetupShopHelp = 0x14085DDB0;
    constexpr size_t kShopCursor = 0x11B0; // selected index, total count, first visible index, visible count
    constexpr size_t kShopState = 0x1150;  // 1 = choosing a booster pack
    constexpr size_t kShopHelp = 0x108;
    constexpr int kVisiblePacks = 6;
    const auto WStringAssign = reinterpret_cast<WStringAssign_t>(YGO::Addr::WString_Assign);        // the game's std::wstring::assign (its allocator)
    const auto GetPlayerSection = reinterpret_cast<GetPlayerSection_t>(YGO::Addr::Save_Get_PlayerSection);  // YGO::SAVE::Get_PlayerSection(0xFFFFFFFD = current)

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
        bool AutoRow = false;             // no "series": goes in the first new row with room
        bool Native = false;              // "native": WolfX wrote it into the game's pack files; only its picture and unlock are ours
        std::map<char, std::wstring> Titles, Texts;
        std::vector<uint16_t> Common, Rare;
        std::vector<uint16_t> Contents;   // the list the record points at (built once, never freed: the game may be reading it)
    };

    std::vector<PackChange> g_changes;
    std::deque<NewPack> g_newPacks;       // deque: records point into Name/Contents
    std::unordered_set<uint32_t> g_ours;  // ids of the new packs
    struct ShopRow                        // "rows": a new row's name and the game row (1-6) whose banner it borrows
    {
        std::map<char, std::wstring> Names;
        uint32_t Art = 0;
        std::string ArtKey;                // or "wrap_series_<key>": a banner added to pdui\SeriesLogo
    };
    std::map<uint32_t, ShopRow> g_rows;
    struct { float X = 960, Y = 200, Size = 48; uint32_t Colour = 0xFFFFFFFF; } g_rowLabel;  // "rowLabel": where the name is drawn
    void* g_labelNode = nullptr;          // RIX text node on the shop screen (the row name), or null
    uint32_t g_rowCount = kGameRows;      // shop rows: the game's 6 + new ones
    bool g_rowsAssigned = false;

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
            Logger::WriteLog(problem + ", its packs are left out", MODULE_NAME, 2);
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
            Logger::WriteLog("packs.json couldn't be read: " + std::string(e.what()), MODULE_NAME, 2);
            return;
        }
        Logger::WriteLog("packs.json: " + std::to_string(g_changes.size()) + " pack change(s), " + std::to_string(g_newPacks.size()) + " new pack(s)", MODULE_NAME, 0);
    }

    void Parse(const nlohmann::json& root)
    {
        const bool replaceAll = root.is_object() && root.value("replaceDefaults", false);
        // const json: operator[] on a missing key is undefined (the debug CRT fast-fails), so look "packs" up with value()
        const nlohmann::json changes = root.is_array() ? root : root.is_object() ? root.value("packs", nlohmann::json()) : nlohmann::json();
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
                pack.AutoRow = !entry.contains("series") || !entry["series"].is_number_integer();
                pack.Series = pack.AutoRow ? 0u : entry.value("series", 0u);
                pack.Native = entry.value("native", false) && !pack.AutoRow;
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
                else if (!pack.Native && (pack.Common.empty() || pack.Rare.empty()))
                    why = "it needs cards in both \"common\" and \"rare\"";
                if (!why.empty())
                {
                    Logger::WriteLog("packs.json: new pack \"" + pack.Name + "\" skipped: " + why, MODULE_NAME, 2);
                    continue;
                }
                pack.Contents.push_back(static_cast<uint16_t>(pack.Common.size()));
                pack.Contents.push_back(static_cast<uint16_t>(pack.Rare.size()));
                pack.Contents.insert(pack.Contents.end(), pack.Common.begin(), pack.Common.end());
                pack.Contents.insert(pack.Contents.end(), pack.Rare.begin(), pack.Rare.end());
                g_ours.insert(pack.Id);
                g_newPacks.push_back(std::move(pack));
            }

        // "rows": [ { "row": 6, "name": { "E": "TEST" }, "art": 2 } ]   (art = the game row 1-6 whose banner it shows)
        if (root.is_object() && root.contains("rows") && root["rows"].is_array())
            for (const auto& entry : root["rows"])
            {
                if (!entry.is_object() || !entry.contains("row") || !entry["row"].is_number_integer())
                    continue;
                const int row = entry["row"].get<int>();
                if (row < static_cast<int>(kGameRows))
                {
                    Logger::WriteLog("packs.json: rows: row " + std::to_string(row) + " is the game's, skipped (new rows start at 6)", MODULE_NAME, 2);
                    continue;
                }
                ShopRow& shopRow = g_rows[static_cast<uint32_t>(row)];
                ReadTexts(entry, "name", shopRow.Names);
                if (entry.contains("art") && entry["art"].is_string())
                    shopRow.ArtKey = entry["art"].get<std::string>();
                const int art = entry.contains("art") && entry["art"].is_number_integer() ? entry["art"].get<int>() : 0;
                shopRow.Art = art >= 1 && art <= static_cast<int>(kGameRows) ? static_cast<uint32_t>(art) : 0;
            }
        if (root.is_object() && root.contains("rowLabel") && root["rowLabel"].is_object())
        {
            const auto& label = root["rowLabel"];
            g_rowLabel.X = label.value("x", g_rowLabel.X);
            g_rowLabel.Y = label.value("y", g_rowLabel.Y);
            g_rowLabel.Size = label.value("size", g_rowLabel.Size);
            g_rowLabel.Colour = label.value("colour", g_rowLabel.Colour);
        }
    }

    // Puts the new packs in rows, once, after the game's packs are loaded: a row holds at most as many packs as the game's fullest row.
    // Packs with a "series" keep it (a full row overflows into the next new row); packs without one fill the new rows after the game's.
    void AssignRows()
    {
        if (g_rowsAssigned)
            return;
        g_rowsAssigned = true;
        std::map<uint32_t, uint32_t> used;
        uint32_t cap = 0;
        for (uint32_t i = 0; i < kPackCount; ++i)
        {
            const PackRecord* record = Record(i);
            if (record->Kind == kRewardPack && record->Series < kGameRows && !g_ours.count(i))
                cap = (std::max)(cap, ++used[record->Series]);
        }
        if (cap == 0)
            cap = 6;
        auto place = [&](NewPack& pack, uint32_t row)
        {
            while (used[row] >= cap)
                row = (std::max)(row + 1, kGameRows);
            if (!pack.AutoRow && row != pack.Series)
                Logger::WriteLog("New pack \"" + pack.Name + "\": row " + std::to_string(pack.Series) + " is full (" + std::to_string(cap) +
                    " packs), put in row " + std::to_string(row), MODULE_NAME, 1);
            pack.Series = row;
            ++used[row];
            g_rowCount = (std::max)(g_rowCount, row + 1);
        };
        for (NewPack& pack : g_newPacks)
            if (!pack.AutoRow)
                place(pack, pack.Series);
        for (NewPack& pack : g_newPacks)
            if (pack.AutoRow)
                place(pack, kGameRows);
        for (const auto& [row, shopRow] : g_rows)
            g_rowCount = (std::max)(g_rowCount, row + 1);
        Logger::WriteLog("Shop rows: " + std::to_string(g_rowCount) + " (" + std::to_string(cap) + " packs per row)", MODULE_NAME, 0);
    }

    // The slot holds this new pack: ours (filled from packs.json) or the game's own record of it ("native").
    bool IsPack(const PackRecord* record, const NewPack& pack)
    {
        return record && record->Name && (record->Name == pack.Name.c_str() || pack.Name == record->Name);
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
                Logger::WriteLog("Pack " + change.Pack + ": " + std::to_string(common.size()) + " common, " + std::to_string(rare.size()) + " rare" +
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
            if (pack.Native)
            {
                if (!IsPack(record, pack))
                    Logger::WriteLog("New pack \"" + pack.Name + "\" should be in the game's pack files (id " + std::to_string(pack.Id) +
                        ") but isn't: save it again in WolfX", MODULE_NAME, 2);
                continue;   // the game loaded it (cards, texts, row, cost)
            }
            if (record->Kind != 0 && record->Name != pack.Name.c_str())
            {
                Logger::WriteLog("New pack \"" + pack.Name + "\": id " + std::to_string(pack.Id) + " is the game's pack \"" +
                    std::string(record->Name ? record->Name : "?") + "\", skipped (pick an id the game doesn't use)", MODULE_NAME, 2);
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
        AssignRows();
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
            if (has(pack.Id) || _stricmp(pack.UnlockWith.c_str(), "never") == 0 || !IsPack(Record(pack.Id), pack))
                continue;
            bool open = pack.UnlockWith.empty();
            if (!open)
                for (uint32_t i = 0; i < kPackCount && !open; ++i)
                    if (Record(i)->Name && pack.UnlockWith == Record(i)->Name)
                        open = has(i);
            if (open)
            {
                bits[pack.Id >> 5] |= 1u << (pack.Id & 31);
                Logger::WriteLog("New pack \"" + pack.Name + "\" unlocked", MODULE_NAME, 0);
            }
        }
    }

    // ---- hooks ----

    // The list scrolls, but HandleInput checks the full-set flag in a six-widget
    // array using the absolute selection. Replace only its stride calculation:
    // RAX = absolute selection, RDI = screen, RCX = visible widget byte offset.
    // The following instruction overwrites RAX; no other registers are changed.
    __declspec(noinline) bool WriteShopCode(void* address, const void* bytes, size_t count)
    {
        DWORD old = 0;
        if (!VirtualProtect(address, count, PAGE_EXECUTE_READWRITE, &old))
            return false;
        std::memcpy(address, bytes, count);
        DWORD ignored = 0;
        VirtualProtect(address, count, old, &ignored);
        FlushInstructionCache(GetCurrentProcess(), address, count);
        return true;
    }

    bool FixScrolledPurchaseIndex()
    {
        constexpr uintptr_t site = 0x14085BCA5;
        const uint8_t expected[] = { 0x48, 0x69, 0xC8, 0xD8, 0, 0, 0 }; // imul rcx, rax, 0xD8
        if (std::memcmp(reinterpret_cast<const void*>(site), expected, sizeof(expected)) != 0)
        {
            Logger::WriteLog("Scrolled pack purchase check differs from the supported game; new packs disabled", MODULE_NAME, 2);
            return false;
        }
        DWORD size = 0;
        auto* code = static_cast<uint8_t*>(DetourAllocateRegionWithinJumpBounds(reinterpret_cast<void*>(site), &size));
        if (!code)
            return false;
        const intptr_t displacement = reinterpret_cast<intptr_t>(code) - static_cast<intptr_t>(site + 5);
        if (displacement < (std::numeric_limits<int32_t>::min)() || displacement > (std::numeric_limits<int32_t>::max)())
        {
            VirtualFree(code, 0, MEM_RELEASE);
            return false;
        }
        const uint8_t thunk[] = {
            0x2B, 0x87, 0xB8, 0x11, 0, 0,       // sub eax, [rdi+0x11B8] (first visible index)
            0x48, 0x69, 0xC8, 0xD8, 0, 0, 0,   // imul rcx, rax, 0xD8
            0xC3                              // ret
        };
        uint8_t replacement[] = { 0xE8, 0, 0, 0, 0, 0x90, 0x90 }; // call thunk; nop; nop
        const int32_t relative = static_cast<int32_t>(displacement);
        std::memcpy(replacement + 1, &relative, sizeof(relative));
        if (!WriteShopCode(code, thunk, sizeof(thunk)) ||
            !WriteShopCode(reinterpret_cast<void*>(site), replacement, sizeof(replacement)))
        {
            VirtualFree(code, 0, MEM_RELEASE);
            return false;
        }
        // Kept for the process lifetime, just like the installed Detours hooks.
        return true;
    }

    void __fastcall Hook_SetupShopHelp(int64_t screen)
    {
        reinterpret_cast<SetupShopHelp_t>(orig_SetupShopHelp)(screen);
        const auto* cursor = reinterpret_cast<const int*>(screen + kShopCursor);
        if (*reinterpret_cast<const int*>(screen + kShopState) != 1 || cursor[1] <= kVisiblePacks)
            return;
        void* help = reinterpret_cast<void*>(screen + kShopHelp);
        for (const auto& entry : {
            YGO::RIX::HelpEntry{4, 1, reinterpret_cast<int64_t>(L"Previous pack")},
            YGO::RIX::HelpEntry{8, 1, reinterpret_cast<int64_t>(L"Next pack")}})
            YGO::RIX::HelpAdd(help, &entry, 1);
        YGO::RIX::HelpLayout(help);
    }

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
            if (const NewPack* pack = FindNew(record->Id); pack && !pack->Art.empty() && IsPack(record, *pack))
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
        // A tab change can switch between an overflowing and an ordinary list.
        Hook_SetupShopHelp(screen);
    }

    // The game opens the shop with 6 rows; give it all of them (the row list wraps, so L/R walks through every row).
    void __fastcall Hook_ShopOpen(int64_t screen)
    {
        reinterpret_cast<ShopOpen_t>(orig_ShopOpen)(screen);
        auto* rows = reinterpret_cast<int32_t*>(screen + kRowList);
        rows[1] = static_cast<int32_t>(g_rowCount);
        rows[3] = static_cast<int32_t>(g_rowCount);
    }

    void RemoveRowLabel()
    {
        if (g_labelNode && RIX::Load())
            RIX::Functions().RemoveNode(g_labelNode);
        g_labelNode = nullptr;
    }

    // A new row has no "wrap_series_<n>" layer: show the banner of the game row its "rows" entry names (default: cycles 1-6), with the
    // row's name drawn over it (RIX text, needs Yu-Gi-Oh-RIX).
    void __fastcall Hook_ShowSeriesArt(int64_t screen)
    {
        RemoveRowLabel();
        const uint32_t row = *reinterpret_cast<uint32_t*>(screen + kRowList);
        void* animoo = *reinterpret_cast<void**>(screen + kShopAnimoo);
        if (row < kGameRows || !animoo)
            return reinterpret_cast<ShowSeriesArt_t>(orig_ShowSeriesArt)(screen);
        auto it = g_rows.find(row);
        const uint32_t art = it != g_rows.end() && it->second.Art ? it->second.Art : row % kGameRows + 1;
        char name[96];
        if (it != g_rows.end() && !it->second.ArtKey.empty())
            std::snprintf(name, sizeof(name), "wrap_series_%s", it->second.ArtKey.c_str());
        else
            std::snprintf(name, sizeof(name), "wrap_series_%u", art);
        SelectLayerByName(animoo, name);
        if (it != g_rows.end() && !it->second.Names.empty() && RIX::Load())
        {
            const std::wstring& label = Pick(it->second.Names);
            g_labelNode = RIX::Functions().AddText(reinterpret_cast<void*>(screen), label.c_str(), 1000, g_rowLabel.X, g_rowLabel.Y, 0.0f,
                g_rowLabel.Size, RIX_ALIGN_CENTRE, g_rowLabel.Colour);
        }
    }

    // The label belongs to the shop screen: take it off as the shop is left.
    void __fastcall Hook_ShopSetState(int64_t screen, int state, double unused)
    {
        if (state == 6 || state == 7)
            RemoveRowLabel();
        reinterpret_cast<ShopSetState_t>(orig_ShopSetState)(screen, state, unused);
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

        if (!g_newPacks.empty() && !FixScrolledPurchaseIndex())
        {
            Logger::WriteLog("Could not install the scrolled purchase fix; new packs disabled", MODULE_NAME, 2);
            g_newPacks.clear();
            g_ours.clear();
        }

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_LoadPackDefinitions, Hook_LoadPackDefinitions);
        if (!g_newPacks.empty())
        {
            DetourAttach(&(PVOID&)orig_Pack_GetArtName, Hook_Pack_GetArtName);
            DetourAttach(&(PVOID&)orig_BuildPackList, Hook_BuildPackList);
            DetourAttach(&(PVOID&)orig_SetupShopHelp, Hook_SetupShopHelp);
        }
        LONG error = DetourTransactionCommit();
        Logger::WriteLog("Pack hooks attached: " + std::to_string(error), MODULE_NAME, error == 0 ? 0 : 2);

        if (error != NO_ERROR)
            return; // Do not publish packs without their required UI/art hooks.

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
