#include "Series.h"
#include "Common.h"

#include <Windows.h>
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <format>
#include <fstream>
#include <map>
#include <mutex>
#include <string>
#include <vector>

#include <json.hpp>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Core.h"
#include "Yu-Gi-Oh-Mods.h"
#include "Yu-Gi-Oh-RIX.h"

namespace
{
    using namespace Campaign;

    // ---- the game (names in the IDB) ----

    constexpr int kGameSeries = 6;
    constexpr int kHostSlot = 5;                 // the game series slot a new series borrows
    constexpr size_t kSeriesBlock = 1208;        // per series in the save: u32 unlocked, u32, then 24 bytes per duel order
    constexpr size_t kSectionOffset = 0x1B70;    // Section_0x1B70 in the blob
    constexpr size_t kBlocksOffset = 8;          // the series blocks start 8 bytes into the section (u32 current series, u32)
    constexpr int kVisiblePanels = 6;

    // the series select screen (CampaignSeriesSelect_*)
    constexpr int64_t kList = 656;               // ListState {+0 cursor, +4 count, +8 top, +12 shown, +16 dirty, +18 wrap}
    constexpr int64_t kState = 712;              // 0 browsing, 1 confirmed, 2 going back, 3 left
    constexpr int64_t kPanels = 720;             // 6 panel widgets, 424 bytes each
    constexpr int64_t kPanelSize = 424;
    constexpr int64_t kIds = 3424;               // std::vector<int> series ids, one per list row

    using ScreenFn_t = void(__fastcall*)(int64_t screen);
    using BuildList_t = int64_t(__fastcall*)(int64_t screen);
    using HandleInput_t = void(__fastcall*)(int64_t screen, int64_t ui);
    using GetLabel_t = const wchar_t*(__fastcall*)(unsigned int series);
    using GetLogoKey_t = const char*(__fastcall*)(unsigned int series);
    using GetDuelCount_t = int64_t(__fastcall*)(unsigned int series);
    using GetDuelIdAt_t = int*(__fastcall*)(int series, int index);
    using PanelSetLabel_t = void(__fastcall*)(int64_t panel, const wchar_t* text);
    using VectorEmplace_t = void(__fastcall*)(void* vector, void* where, const int* value);
    using GetSection_t = uint8_t*(__fastcall*)(unsigned int profile);
    using GetCompletion_t = float(__fastcall*)(int series, int* won, int* total);

    uintptr_t orig_BuildList = 0x140862140;      // CampaignSeriesSelect_BuildSeriesList
    uintptr_t orig_Refresh = 0x140862B90;        // CampaignSeriesSelect_Refresh
    uintptr_t orig_HandleInput = 0x1408621D0;    // CampaignSeriesSelect_HandleInput
    uintptr_t orig_GetLabel = 0x1407564C0;       // Campaign_GetSeriesLabel
    uintptr_t orig_GetLogoKey = 0x1407564A0;     // Campaign_GetSeriesLogoKey
    uintptr_t orig_GetDuelCount = 0x14074A220;   // Campaign_GetSeriesDuelCount
    uintptr_t orig_GetDuelIdAt = 0x14074A200;    // Campaign_GetDuelIdAt
    uintptr_t orig_FreeDuelEnter = 0x1408415F0;   // Free Duel's OnEnter: fills its series list (screen+4096) with 0-5, then the series picker
    using FreeDuelEnter_t = void(__fastcall*)(int64_t screen, int64_t ui);
    using SetSeriesList_t = void(__fastcall*)(int64_t picker, int64_t ids);
    const auto SeriesPickerSetList = reinterpret_cast<SetSeriesList_t>(0x1408A76E0);   // copies the ids, sets the list count, relabels (it scrolls)
    uintptr_t orig_OnEnter = 0x140862970;        // CampaignSeriesSelect_OnEnter: "Campaign x% complete" = sum of series 0-5 / 6
    uintptr_t orig_GetCompletion = 0x14085F910;  // Campaign_GetSeriesCompletion
    using OnEnter_t = void(__fastcall*)(int64_t screen, int64_t ui);
    const auto PanelSetLabel = reinterpret_cast<PanelSetLabel_t>(0x1408A1950);
    const auto VectorEmplace = reinterpret_cast<VectorEmplace_t>(0x140746A20);   // std::vector<int>::_Emplace_reallocate (the game's allocator)
    const auto GetSection = reinterpret_cast<GetSection_t>(0x1407F8070);         // YGO::SAVE::Get_Section_0x1B70(0xFFFFFFFD = current)
    float GetCompletion(int series, int* won, int* total) { return reinterpret_cast<GetCompletion_t>(orig_GetCompletion)(series, won, total); }
    constexpr uintptr_t kMouse = 0x142924070;    // the UI's pointer position: int x, int y (the panels' hit test compares ints)
    constexpr int64_t kPanelHitBox = 96;         // panel+96: an object whose vfunc +32 gives the panel's box {left, top, right, bottom}

    // ---- campaigns.json ----

    enum class Unlock { Start, Never, After };

    struct NewSeries
    {
        int Id = 0;
        std::map<char, std::wstring> Names;
        std::wstring Shown;                      // the name in the game's language (the panel keeps the pointer)
        int Logo = 1;                            // 1-6
        std::string LogoKey;                     // or "logo_<key>" in pdui\SeriesLogo (a new logo in a patched sheet)
        Unlock Rule = Unlock::Start;
        int After = 0;
        std::vector<int> Duels;                  // index = order -> story duel id (0 = none), like the game's lists
    };

    std::vector<NewSeries> g_series;             // in list order
    std::vector<int> g_hiddenSeries;             // campaigns.json "hiddenSeries": game series (0-5) left out of the list ("deleted")
    std::vector<int> g_rowSeries;                // the list's rows: a game series 0-5, or kGameSeries + index into g_series
    bool Active() { return !g_series.empty() || !g_hiddenSeries.empty(); }

    // ---- borrowing slot 5 ----

    using Block = std::array<uint8_t, kSeriesBlock>;
    std::mutex g_lock;                           // the save thread (write filter) and the UI thread (host / unhost)
    int g_hosted = -1;                           // index into g_series, or -1
    Block g_stash{};                             // slot 5's own block while it is borrowed
    std::map<int, Block> g_progress;             // new series id -> its block (kept in the save file, see LoadProgress)
    bool g_bypass = false;                       // the hooks show the game's own slot 5 (relabelling the panels)

    uint8_t* SeriesBlock(uint8_t* blob, int slot) { return blob + kSectionOffset + kBlocksOffset + kSeriesBlock * slot; }
    uint8_t* LiveBlock(int slot) { return GetSection(0xFFFFFFFD) + kBlocksOffset + kSeriesBlock * slot; }

    // New series progress lives IN the save file: Yu-Gi-Oh-Core's save extension (data after the game's 44008 bytes, tag
    // "campaign.series"), which only exists once a new series has progress. Its bytes: "YSER", u32 count, then count x (u32 series id,
    // 1208-byte block). A <save>.series.bin from the first build (2026-10-11) is taken in once and then left alone.
    constexpr const char* kProgressTag = "campaign.series";

    bool ParseProgress(const std::vector<uint8_t>& bytes, std::map<int, Block>& out)
    {
        if (bytes.size() < 8 || std::memcmp(bytes.data(), "YSER", 4) != 0)
            return false;
        uint32_t count = 0;
        std::memcpy(&count, bytes.data() + 4, 4);
        size_t at = 8;
        for (uint32_t i = 0; i < count && i < 64 && at + 4 + kSeriesBlock <= bytes.size(); ++i)
        {
            uint32_t id = 0;
            std::memcpy(&id, bytes.data() + at, 4);
            std::memcpy(out[static_cast<int>(id)].data(), bytes.data() + at + 4, kSeriesBlock);
            at += 4 + kSeriesBlock;
        }
        return true;
    }

    std::vector<uint8_t> OldSidecar()
    {
        char name[MAX_PATH]{};
        const std::string save = Core::Functions().GetSaveFile && Core::Functions().GetSaveFile(name, MAX_PATH) > 0 ? name : "savegame-ex.dat";
        std::ifstream file(GameFolder() + save + ".series.bin", std::ios::binary | std::ios::ate);
        if (!file)
            return {};
        std::vector<uint8_t> bytes(static_cast<size_t>(file.tellg()));
        file.seekg(0);
        file.read(reinterpret_cast<char*>(bytes.data()), bytes.size());
        return bytes;
    }

    // The progress of the save read last (Core keeps it per save; a save slot switch reads another).
    void LoadProgress()
    {
        g_progress.clear();
        const int size = Core::Functions().GetSaveExtension(kProgressTag, nullptr, 0);
        std::vector<uint8_t> bytes(static_cast<size_t>((std::max)(size, 0)));
        if (size > 0)
            Core::Functions().GetSaveExtension(kProgressTag, bytes.data(), size);
        else
            bytes = OldSidecar();
        ParseProgress(bytes, g_progress);
    }

    // Hands the progress to Core; it goes into the save file with the game's next save write.
    void SaveProgress()
    {
        std::vector<uint8_t> bytes(8);
        std::memcpy(bytes.data(), "YSER", 4);
        const uint32_t count = static_cast<uint32_t>(g_progress.size());
        std::memcpy(bytes.data() + 4, &count, 4);
        for (const auto& [id, block] : g_progress)
        {
            const uint32_t value = static_cast<uint32_t>(id);
            const auto* v = reinterpret_cast<const uint8_t*>(&value);
            bytes.insert(bytes.end(), v, v + 4);
            bytes.insert(bytes.end(), block.begin(), block.end());
        }
        Core::Functions().SetSaveExtension(kProgressTag, bytes.data(), static_cast<int>(bytes.size()));
    }

    void BuildDuelList(NewSeries& series)
    {
        series.Duels.assign(1, 0);
        for (uint32_t id = 0; id < kStoryDuelCount; ++id)
        {
            const auto* record = reinterpret_cast<const StoryDuelRecord*>(kStoryDuelTable) + id;
            if (record->Id == 0 || record->Series != series.Id || record->Order < 1 || record->Order > 48)
                continue;
            if (series.Duels.size() <= static_cast<size_t>(record->Order))
                series.Duels.resize(record->Order + 1, 0);
            series.Duels[record->Order] = static_cast<int>(id);
        }
    }

    // Whether the LAST duel of a series (its highest order) is won: its first state (block +8 + 24 * order) is 3. Called with slot 5 the
    // game's own (Host unhosts first).
    bool LastDuelWon(int seriesId)
    {
        std::vector<int> duels;
        const uint8_t* block = nullptr;
        Block progress{};
        if (seriesId < kGameSeries)
        {
            bool bypass = g_bypass;
            g_bypass = true;
            const int64_t count = reinterpret_cast<GetDuelCount_t>(orig_GetDuelCount)(seriesId);
            for (int64_t i = 0; i < count; ++i)
                duels.push_back(*reinterpret_cast<GetDuelIdAt_t>(orig_GetDuelIdAt)(seriesId, static_cast<int>(i)));
            g_bypass = bypass;
            block = LiveBlock(seriesId);
        }
        else
        {
            auto it = std::find_if(g_series.begin(), g_series.end(), [&](const NewSeries& s) { return s.Id == seriesId; });
            if (it == g_series.end())
                return true;    // no such series: nothing to wait for
            BuildDuelList(*it);
            duels = it->Duels;
            LoadProgress();
            if (auto found = g_progress.find(seriesId); found != g_progress.end())
                progress = found->second;
            block = progress.data();
        }
        for (size_t i = duels.size(); i-- > 1;)
            if (duels[i] > 0)
            {
                int state = 0;
                std::memcpy(&state, block + 8 + 24 * i, 4);
                return state == 3;
            }
        return true;            // the series has no duels
    }

    // Open from the start unless campaigns.json says "never" or { "after": n } (n's last duel won; n is a game series or a new one).
    bool IsUnlocked(const NewSeries& series)
    {
        switch (series.Rule)
        {
        case Unlock::Never: return false;
        case Unlock::After: return LastDuelWon(series.After);
        default: return true;
        }
    }

    void Unhost()
    {
        std::lock_guard<std::mutex> guard(g_lock);
        if (g_hosted < 0)
            return;
        uint8_t* live = LiveBlock(kHostSlot);
        std::memcpy(g_progress[g_series[g_hosted].Id].data(), live, kSeriesBlock);
        std::memcpy(live, g_stash.data(), kSeriesBlock);
        g_hosted = -1;
        SaveProgress();
    }

    void Host(int index)
    {
        Unhost();
        NewSeries& series = g_series[index];
        BuildDuelList(series);
        const bool unlocked = IsUnlocked(series);   // before slot 5 changes (an "after" may be series 5)
        std::lock_guard<std::mutex> guard(g_lock);
        LoadProgress();
        uint8_t* live = LiveBlock(kHostSlot);
        std::memcpy(g_stash.data(), live, kSeriesBlock);
        auto it = g_progress.find(series.Id);
        if (it == g_progress.end())
        {
            Block fresh{};
            it = g_progress.emplace(series.Id, fresh).first;
        }
        uint32_t flag = unlocked ? 1u : 0u;
        std::memcpy(it->second.data(), &flag, 4);   // the rule decides, every time
        std::memcpy(live, it->second.data(), kSeriesBlock);
        g_hosted = index;
    }

    // Core's save write filter: the new series' progress goes to Core's save extension, slot 5's own block back into what is written.
    int __cdecl SaveWriteFilter(unsigned char* blob, int size, void*)
    {
        std::lock_guard<std::mutex> guard(g_lock);
        if (g_hosted < 0 || size < static_cast<int>(kSectionOffset + kBlocksOffset + kSeriesBlock * kGameSeries))
            return 0;
        uint8_t* block = SeriesBlock(blob, kHostSlot);
        std::memcpy(g_progress[g_series[g_hosted].Id].data(), block, kSeriesBlock);
        SaveProgress();
        std::memcpy(block, g_stash.data(), kSeriesBlock);
        return 1;
    }

    // ---- scrolling, with the campaign duel list's arrows ----
    //
    // The 6 panels are a window on the list (panel i shows row top + i). Above and below them sit the arrows the campaign's duel list
    // uses to say there is more (CampaignMenu: pdui/doShared "arrow_1", "arrow_2" while the pointer is on it, at screen+5088 / +5120),
    // shown only when there is more that way; clicking one scrolls a row. Keys and pad scroll as ever.
    // Tried and dropped (2026-10-11): a drawn scroll bar (the game's scroll bar art, loose PNGs in pdui\shared, can't be loaded on this
    // screen; naming a PNG path as a sheet CRASHES the layer loader, YuGiOh.exe+0x747A98), "More series" text, and page panels.

    struct Arrow { void* Node = nullptr; bool Hot = false; };
    Arrow g_arrows[2];                           // [0] up (above the panels), [1] down (below)
    int64_t g_arrowScreen = 0;
    int g_panels[4] = {};                        // the panels' box: left, top, right, bottom (pointer pixels = UI pixels at 1920 x 1080)
    constexpr float kArrowWidth = 73, kArrowHeight = 60;   // doShared arrow_1
    constexpr float kArrowGap = 6;

    int Top(int64_t screen) { return *reinterpret_cast<int32_t*>(screen + kList + 8); }
    int Rows(int64_t screen) { return *reinterpret_cast<int32_t*>(screen + kList + 4); }

    bool PanelBox(int64_t screen, int panel, int (&box)[4])
    {
        void* hit = *reinterpret_cast<void**>(screen + kPanels + kPanelSize * panel + kPanelHitBox);
        if (!hit)
            return false;
        using GetBox_t = void(__fastcall*)(void* self, int* box);
        (*reinterpret_cast<GetBox_t**>(hit))[4](hit, box);
        return box[2] > box[0] && box[3] > box[1];
    }

    void RemoveArrow(Arrow& arrow)
    {
        if (arrow.Node && RIX::Load())
            RIX::Functions().RemoveNode(arrow.Node);
        arrow = {};
    }

    void RemoveArrows()
    {
        RemoveArrow(g_arrows[0]);
        RemoveArrow(g_arrows[1]);
        g_arrowScreen = 0;
    }

    // Where an arrow goes (its box): centred on the panels, just above / below them.
    void ArrowBox(int which, float& x, float& y)
    {
        x = (g_panels[0] + g_panels[2]) / 2.0f - kArrowWidth / 2;
        y = which == 0 ? g_panels[1] - kArrowGap - kArrowHeight : g_panels[3] + kArrowGap;
    }

    bool PointerOn(int which)
    {
        const int* mouse = reinterpret_cast<const int*>(kMouse);
        float x, y;
        ArrowBox(which, x, y);
        return mouse[0] >= x && mouse[0] <= x + kArrowWidth && mouse[1] >= y && mouse[1] <= y + kArrowHeight;
    }

    // Shows each arrow only while there is more that way; arrow_2 while the pointer is on it (like the duel list).
    void DrawArrows(int64_t screen)
    {
        if (!RIX::Load())
            return;
        if (g_arrowScreen != screen)
        {
            RemoveArrows();
            int first[4]{}, last[4]{};
            if (!PanelBox(screen, 0, first) || !PanelBox(screen, kVisiblePanels - 1, last))
                return;
            g_panels[0] = (std::min)(first[0], last[0]);
            g_panels[1] = first[1];
            g_panels[2] = (std::max)(first[2], last[2]);
            g_panels[3] = last[3];
            g_arrowScreen = screen;
        }
        const int top = Top(screen);
        const bool want[2] = { top > 0, top + kVisiblePanels < Rows(screen) };
        for (int i = 0; i < 2; ++i)
        {
            const bool hot = want[i] && PointerOn(i);
            if (g_arrows[i].Node && (!want[i] || hot != g_arrows[i].Hot))
                RemoveArrow(g_arrows[i]);
            if (!want[i] || g_arrows[i].Node)
                continue;
            float x, y;
            ArrowBox(i, x, y);
            // arrow_1 points down; the up arrow is the same picture flipped. The flip happens in place (2026-10-11: placing it a height lower
            // to make up for a flip from its top edge put it on the first panel), so both use the same box.
            g_arrows[i].Node = RIX::Functions().AddImage(reinterpret_cast<void*>(screen), "pdui/doShared", hot ? "arrow_2" : "arrow_1", 900, x, y,
                kArrowWidth, i == 0 ? -kArrowHeight : kArrowHeight);
            g_arrows[i].Hot = hot;
        }
    }

    // ---- the overall campaign % ----

    // Campaign_GetSeriesCompletion's rule on one series' block and duel list (index = order): base-game (sku 1) duels count; a duel is
    // done when its first state (block +8 + 24 * index) is 3, and every duel but order 1 also counts its second state (+12) being 3.
    float Completion(const uint8_t* block, const std::vector<int>& duels)
    {
        int won = 0, total = 0, extraWon = 0, extraTotal = 0;
        for (size_t i = 0; i < duels.size(); ++i)
        {
            const auto* record = reinterpret_cast<const StoryDuelRecord*>(kStoryDuelTable) + duels[i];
            if (duels[i] <= 0 || duels[i] >= static_cast<int>(kStoryDuelCount) || record->Sku != 1)
                continue;
            int first = 0, second = 0;
            std::memcpy(&first, block + 8 + 24 * i, 4);
            std::memcpy(&second, block + 12 + 24 * i, 4);
            ++total;
            won += first == 3;
            if (i != 1)
            {
                ++extraTotal;
                extraWon += second == 3;
            }
        }
        return total + extraTotal ? 100.0f * (won + extraWon) / (total + extraTotal) : 100.0f;
    }

    // Every series, the new ones too (a new one with no progress counts 0 %, so adding series lowers the total until they are played).
    float OverallCompletion()
    {
        std::lock_guard<std::mutex> guard(g_lock);
        if (g_hosted < 0)
            LoadProgress();
        float sum = 0;
        for (int s = 0; s < kGameSeries; ++s)
        {
            if (s == kHostSlot && g_hosted >= 0)
            {
                // slot 5 holds a new series right now: the game's own series 5 is in the stash
                std::vector<int> duels;
                bool bypass = g_bypass;
                g_bypass = true;
                const int64_t count = reinterpret_cast<GetDuelCount_t>(orig_GetDuelCount)(s);
                for (int64_t i = 0; i < count; ++i)
                    duels.push_back(*reinterpret_cast<GetDuelIdAt_t>(orig_GetDuelIdAt)(s, static_cast<int>(i)));
                g_bypass = bypass;
                sum += Completion(g_stash.data(), duels);
            }
            else
                sum += GetCompletion(s, nullptr, nullptr);
        }
        for (int i = 0; i < static_cast<int>(g_series.size()); ++i)
        {
            NewSeries& series = g_series[i];
            BuildDuelList(series);
            Block empty{};
            const uint8_t* block = i == g_hosted ? LiveBlock(kHostSlot) : g_progress.count(series.Id) ? g_progress[series.Id].data() : empty.data();
            sum += Completion(block, series.Duels);
        }
        return sum / (kGameSeries + static_cast<int>(g_series.size()));
    }

    bool g_inOnEnter = false;                    // OnEnter's own loop asks for series 0-5 and averages them
    bool g_inRefresh = false;                    // ... and calls Refresh, whose per-series % must stay real
    float g_overall = 0;

    // Free Duel: its series picker lists 0-5; add the new series (their duelists - chardata "series" 6+ - are listed under them) and
    // leave out the deleted ones. The picker is a scrolling list of 6 panels, so it shows any number.
    void __fastcall Hook_FreeDuelEnter(int64_t screen, int64_t ui)
    {
        reinterpret_cast<FreeDuelEnter_t>(orig_FreeDuelEnter)(screen, ui);
        auto** ids = reinterpret_cast<int**>(screen + 4096);   // std::vector<int>: begin, end, capacity
        if (!ids[0] || ids[1] - ids[0] != kGameSeries)
            return;   // not the list the game just built (another screen state)
        int* write = ids[0];
        for (int* read = ids[0]; read != ids[1]; ++read)
            if (std::find(g_hiddenSeries.begin(), g_hiddenSeries.end(), *read) == g_hiddenSeries.end())
                *write++ = *read;
        ids[1] = write;
        for (const NewSeries& series : g_series)
        {
            const int id = series.Id;
            if (ids[1] == ids[2])
                VectorEmplace(ids, ids[1], &id);
            else
                *ids[1]++ = id;
        }
        SeriesPickerSetList(screen + 656, screen + 4096);
    }

    void __fastcall Hook_OnEnter(int64_t screen, int64_t ui)
    {
        g_overall = OverallCompletion();
        g_inOnEnter = true;
        reinterpret_cast<OnEnter_t>(orig_OnEnter)(screen, ui);
        g_inOnEnter = false;
    }

    // Inside OnEnter's average every series answers the overall %, so the game's "/ 6" comes out as the total of all series.
    float __fastcall Hook_GetCompletion(int series, int* won, int* total)
    {
        if (g_inOnEnter && !g_inRefresh && !won && !total)
            return g_overall;
        return reinterpret_cast<GetCompletion_t>(orig_GetCompletion)(series, won, total);
    }

    // ---- hooks ----

    // The list: the game's 6, then one row per new series (all pointing at slot 5, which the row borrows), 6 panels shown.
    int64_t __fastcall Hook_BuildList(int64_t screen)
    {
        int64_t result = reinterpret_cast<BuildList_t>(orig_BuildList)(screen);
        if (!Active())
            return result;
        // the rows: the game's series that aren't hidden, then the new ones; the id vector gets each row's game slot (a new series
        // borrows slot 5)
        g_rowSeries.clear();
        for (int series = 0; series < kGameSeries; ++series)
            if (std::find(g_hiddenSeries.begin(), g_hiddenSeries.end(), series) == g_hiddenSeries.end())
                g_rowSeries.push_back(series);
        for (size_t i = 0; i < g_series.size(); ++i)
            g_rowSeries.push_back(kGameSeries + static_cast<int>(i));
        auto** vector = reinterpret_cast<int**>(screen + kIds);   // begin, end, capacity (the game filled 6)
        vector[1] = vector[0];
        for (int row : g_rowSeries)
        {
            const int slot = row < kGameSeries ? row : kHostSlot;
            if (vector[1] == vector[2])
                VectorEmplace(vector, vector[1], &slot);
            else
                *vector[1]++ = slot;
        }
        auto* list = reinterpret_cast<int32_t*>(screen + kList);
        list[1] = static_cast<int32_t>(g_rowSeries.size());
        list[3] = kVisiblePanels;
        list[0] = std::clamp(list[0], 0, (std::max)(0, list[1] - 1));
        return result;
    }

    void __fastcall Hook_Refresh(int64_t screen)
    {
        // the row under the cursor decides what slot 5 is
        const int cursor = *reinterpret_cast<int32_t*>(screen + kList);
        const int cursorRow = cursor >= 0 && cursor < static_cast<int>(g_rowSeries.size()) ? g_rowSeries[cursor] : -1;
        const int want = cursorRow >= kGameSeries ? cursorRow - kGameSeries : -1;
        if (want != g_hosted)
        {
            if (want >= 0)
                Host(want);
            else
                Unhost();
        }

        g_inRefresh = true;
        reinterpret_cast<ScreenFn_t>(orig_Refresh)(screen);
        g_inRefresh = false;
        if (!Active())
            return;

        // the 6 panels are a window on the list: panel i shows row top + i
        const int top = Top(screen);
        for (int i = 0; i < kVisiblePanels; ++i)
        {
            const int index = top + i;
            const int row = index < static_cast<int>(g_rowSeries.size()) ? g_rowSeries[index] : -1;
            const wchar_t* label = L"";
            if (row >= 0 && row < kGameSeries)
            {
                g_bypass = true;
                label = reinterpret_cast<GetLabel_t>(orig_GetLabel)(row);
                g_bypass = false;
            }
            else if (row >= kGameSeries && row - kGameSeries < static_cast<int>(g_series.size()))
                label = g_series[row - kGameSeries].Shown.c_str();
            PanelSetLabel(screen + kPanels + kPanelSize * i, label ? label : L"");
        }
        DrawArrows(screen);
    }

    // Scrolls the window a row (the cursor stays in it).
    void ScrollBy(int64_t screen, int rows)
    {
        auto* list = reinterpret_cast<int32_t*>(screen + kList);
        const int top = std::clamp(list[2] + rows, 0, (std::max)(0, list[1] - kVisiblePanels));
        if (top == list[2])
            return;
        list[2] = top;
        list[0] = std::clamp(list[0], top, top + kVisiblePanels - 1);
        Hook_Refresh(screen);
    }

    void __fastcall Hook_HandleInput(int64_t screen, int64_t ui)
    {
        if (Active() && g_arrowScreen == screen && *reinterpret_cast<int32_t*>(screen + kState) == 0)
        {
            static bool wasDown = false;
            const bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            if (down && !wasDown)
                for (int i = 0; i < 2; ++i)
                    if (g_arrows[i].Node && PointerOn(i))
                        ScrollBy(screen, i == 0 ? -1 : 1);
            wasDown = down;
            DrawArrows(screen);   // hover look
        }

        reinterpret_cast<HandleInput_t>(orig_HandleInput)(screen, ui);

        if (*reinterpret_cast<int32_t*>(screen + kState) == 2)   // going back out of the campaign: slot 5 is the game's again
        {
            Unhost();
            RemoveArrows();
        }
    }

    NewSeries* FindSeries(unsigned int id)
    {
        for (NewSeries& series : g_series)
            if (series.Id == static_cast<int>(id))
                return &series;
        return nullptr;
    }

    const wchar_t* __fastcall Hook_GetLabel(unsigned int series)
    {
        if (!g_bypass && g_hosted >= 0 && series == kHostSlot)
            return g_series[g_hosted].Shown.c_str();
        if (series >= kGameSeries)
            if (NewSeries* mine = FindSeries(series))
                return mine->Shown.c_str();
        return reinterpret_cast<GetLabel_t>(orig_GetLabel)(series);
    }

    const char* __fastcall Hook_GetLogoKey(unsigned int series)
    {
        if (series >= kGameSeries)
            if (NewSeries* mine = FindSeries(series))
                return mine->LogoKey.empty() ? reinterpret_cast<GetLogoKey_t>(orig_GetLogoKey)(mine->Logo - 1) : mine->LogoKey.c_str();
        if (!g_bypass && g_hosted >= 0 && series == kHostSlot)
            return g_series[g_hosted].LogoKey.empty() ? reinterpret_cast<GetLogoKey_t>(orig_GetLogoKey)(g_series[g_hosted].Logo - 1)
                                                      : g_series[g_hosted].LogoKey.c_str();   // the game shows "logo_<key>"
        return reinterpret_cast<GetLogoKey_t>(orig_GetLogoKey)(series);
    }

    int64_t __fastcall Hook_GetDuelCount(unsigned int series)
    {
        if (!g_bypass && g_hosted >= 0 && series == kHostSlot)
            return static_cast<int64_t>(g_series[g_hosted].Duels.size());
        return reinterpret_cast<GetDuelCount_t>(orig_GetDuelCount)(series);
    }

    int* __fastcall Hook_GetDuelIdAt(int series, int index)
    {
        if (!g_bypass && g_hosted >= 0 && series == kHostSlot)
        {
            std::vector<int>& duels = g_series[g_hosted].Duels;
            static int none = 0;
            return index >= 0 && index < static_cast<int>(duels.size()) ? &duels[index] : &none;
        }
        return reinterpret_cast<GetDuelIdAt_t>(orig_GetDuelIdAt)(series, index);
    }

    void Load()
    {
        std::vector<std::string> problems;
        const nlohmann::json root = YGO::Mods::ReadMerged("campaigns.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", it is left out", MODULE_NAME, 2);
        if (!root.is_object())
            return;
        try
        {
            if (auto hidden = root.find("hiddenSeries"); hidden != root.end() && hidden->is_array())
                for (const auto& value : *hidden)
                    if (value.is_number_integer() && value.get<int>() >= 0 && value.get<int>() < kGameSeries)
                        g_hiddenSeries.push_back(value.get<int>());
            if (auto it = root.find("series"); it != root.end() && it->is_array())
                for (const auto& entry : *it)
                {
                    if (!entry.is_object())
                        continue;
                    NewSeries series;
                    series.Id = Int(entry, "id").value_or(-1);
                    if (series.Id < kGameSeries || series.Id > 63)
                    {
                        Logger::WriteLog(std::format("campaigns.json: series {} skipped: \"id\" must be 6-63", series.Id), MODULE_NAME, 2);
                        continue;
                    }
                    if (std::any_of(g_series.begin(), g_series.end(), [&](const NewSeries& s) { return s.Id == series.Id; }))
                        continue;
                    ReadTexts(entry, "name", series.Names);
                    series.Logo = std::clamp(Int(entry, "logo").value_or(1), 1, kGameSeries);
                    series.LogoKey = Str(entry, "logo").value_or("");
                    if (auto unlock = entry.find("unlock"); unlock != entry.end())
                    {
                        if (unlock->is_string() && unlock->get<std::string>() == "never")
                            series.Rule = Unlock::Never;
                        else if (unlock->is_object() && Int(*unlock, "after"))
                        {
                            series.Rule = Unlock::After;
                            series.After = std::clamp(*Int(*unlock, "after"), 0, 63);
                        }
                    }
                    const std::wstring* name = Pick(series.Names);
                    series.Shown = name ? *name : std::format(L"Series {}", series.Id + 1);
                    g_series.push_back(std::move(series));
                }

        }
        catch (const std::exception& e)
        {
            g_series.clear();
            Logger::WriteLog(std::string("campaigns.json couldn't be read: ") + e.what(), MODULE_NAME, 2);
        }
    }
}

namespace Series
{
    void Attach()
    {
        Load();
        if (!Active())
            return;
        if (!g_series.empty() && (!Core::Load() || !Core::Functions().AddSaveWriteFilter || !Core::Functions().GetSaveExtension))
        {
            Logger::WriteLog("New campaign series need a Yu-Gi-Oh-Core with save write filters and save extensions; they are left out", MODULE_NAME, 2);
            g_series.clear();
            if (!Active())
                return;
        }
        if (!g_series.empty())
            Core::Functions().AddSaveWriteFilter(SaveWriteFilter, nullptr);
        DetourAttach(&(PVOID&)orig_BuildList, Hook_BuildList);
        DetourAttach(&(PVOID&)orig_Refresh, Hook_Refresh);
        DetourAttach(&(PVOID&)orig_HandleInput, Hook_HandleInput);
        DetourAttach(&(PVOID&)orig_GetLabel, Hook_GetLabel);
        DetourAttach(&(PVOID&)orig_GetLogoKey, Hook_GetLogoKey);
        DetourAttach(&(PVOID&)orig_GetDuelCount, Hook_GetDuelCount);
        DetourAttach(&(PVOID&)orig_GetDuelIdAt, Hook_GetDuelIdAt);
        DetourAttach(&(PVOID&)orig_OnEnter, Hook_OnEnter);
        DetourAttach(&(PVOID&)orig_FreeDuelEnter, Hook_FreeDuelEnter);
        DetourAttach(&(PVOID&)orig_GetCompletion, Hook_GetCompletion);
        Logger::WriteLog(std::format("campaigns.json: {} new series (slot {} is borrowed while one is picked), {} game series hidden", g_series.size(), kHostSlot + 1,
            g_hiddenSeries.size()), MODULE_NAME, 0);
    }
}
