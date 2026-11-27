#include "TrunkNewSort.h"

#include <Windows.h>
#include <algorithm>
#include <cstdint>
#include <format>
#include <vector>

#include "Detours.h"
#include "Logger.h"

namespace
{
    // widget_TrunkZone (only made by ScreenDeckEditor, at +9984)
    constexpr size_t kTrunkCounter = 48;         // the trunk's own owned-count/NEW interface (vtable 0x140A81F38): Card_IsNewlyOwned is its slot 1
    constexpr size_t kTrunkSortBar = 56;         // widget_CardSortBar
    constexpr size_t kTrunkGrid = 720;           // widget_CardDisplay, SetCards' grid
    constexpr size_t kTrunkEntries = 1576;       // vector of 12 byte { u16 card id, u32 count, u32 } (every owned card)
    constexpr size_t kTrunkShown = 1600;         // vector<u16> of what passes the filter, in display order
    constexpr size_t kTrunkInDeck = 1624;        // the in-deck counter SetCards gets
    constexpr size_t kEntrySize = 12;

    // widget_CardSortBar
    constexpr size_t kBarFitter = 112;           // sub_14086A2E0(bar+112, 210) squeezes the label to 210 wide
    constexpr size_t kBarText = 128;             // widget_Text*
    constexpr size_t kBarMode = 192;             // int: index into g_CardSortModes
    constexpr size_t kBarEnabled = 196;          // u32: bit per mode (the ctor sets all, the trunk clears 10)

    constexpr int kGameModes = 11;               // sub_14088F580's `% 11`
    constexpr int kNewStep = kGameModes;         // our step, after the game's last
    constexpr uintptr_t kSortModeLabels = 0x140C8C690;   // g_CardSortModes[0].StringId, 5 qwords apart
    constexpr float kLabelWidth = 210.0f;
    const wchar_t* const kNewLabel = L"New";     // SetTextById takes a wide string pointer for anything above 2214

    uintptr_t orig_BuildCardList = 0x1408BFEF0;  // YGO::UI::TrunkView_BuildCardList(trunk): ScreenDeckEditor::OnEnter's only list build
    uintptr_t orig_SortAndFillGrid = 0x1408BFC30;    // RIX::widget_TrunkZone::SortAndFillGrid(trunk)
    uintptr_t orig_StepSortMode = 0x14088F580;   // widget_CardSortBar step(bar, +1/-1): every sort bar (trunk, deck zones, recommended list)

    const auto Card_IsNewlyOwned = reinterpret_cast<bool(__fastcall*)(void* counter, uint16_t cardId)>(0x1408BF720);
    const auto SetTextById = reinterpret_cast<void(__fastcall*)(void* text, intptr_t idOrString)>(0x14075E000);
    const auto FitLabel = reinterpret_cast<void(__fastcall*)(void* fitter, float width)>(0x14086A2E0);
    const auto Grid_SetCards = reinterpret_cast<void(__fastcall*)(void* grid, unsigned count, uint16_t* ids, int expected, void* owned, void* inDeck)>(0x14088C490);

    template <typename T> T& At(void* base, size_t offset) { return *reinterpret_cast<T*>(static_cast<char*>(base) + offset); }

    void* g_Trunk = nullptr;                     // the deck editor's trunk, known from its list build
    bool g_NewFirst = false;                     // the "New" step is picked on it
    std::vector<bool> g_WasNew;                  // by Konami id: NEW when the editor opened

    void TakeSnapshot(void* trunk)
    {
        g_WasNew.assign(0x10000, false);
        void* counter = static_cast<char*>(trunk) + kTrunkCounter;
        const char* first = At<char*>(trunk, kTrunkEntries);
        const char* last = At<char*>(trunk, kTrunkEntries + 8);
        size_t count = 0;
        for (const char* entry = first; entry && entry < last; entry += kEntrySize)
        {
            const uint16_t id = *reinterpret_cast<const uint16_t*>(entry);
            if (Card_IsNewlyOwned(counter, id))
            {
                g_WasNew[id] = true;
                ++count;
            }
        }
        Logger::WriteLog(std::format("Trunk New sort: {} new card(s) of {}", count, (last - first) / static_cast<ptrdiff_t>(kEntrySize)), MODULE_NAME, 0);
    }

    void __fastcall Hook_BuildCardList(void* trunk)
    {
        // The game resets its sort to Name here (sub_14088FA40(bar, 0)), so New goes back off too.
        g_Trunk = trunk;
        g_NewFirst = false;
        reinterpret_cast<void(__fastcall*)(void*)>(orig_BuildCardList)(trunk);
        TakeSnapshot(trunk);
    }

    void __fastcall Hook_SortAndFillGrid(void* trunk)
    {
        reinterpret_cast<void(__fastcall*)(void*)>(orig_SortAndFillGrid)(trunk);    // sorts by name (the bar's mode is 0) and fills the grid
        if (trunk != g_Trunk || !g_NewFirst || g_WasNew.empty())
            return;

        uint16_t* first = At<uint16_t*>(trunk, kTrunkShown);
        uint16_t* last = At<uint16_t*>(trunk, kTrunkShown + 8);
        if (!first || last - first < 2)
            return;
        std::stable_partition(first, last, [](uint16_t id) { return g_WasNew[id]; });

        // The same call the game just made, with the new order.
        const ptrdiff_t entries = (At<char*>(trunk, kTrunkEntries + 8) - At<char*>(trunk, kTrunkEntries)) / static_cast<ptrdiff_t>(kEntrySize);
        Grid_SetCards(static_cast<char*>(trunk) + kTrunkGrid, static_cast<unsigned>(last - first), first, static_cast<int>(entries),
            static_cast<char*>(trunk) + kTrunkCounter, At<void*>(trunk, kTrunkInDeck));
    }

    void __fastcall Hook_StepSortMode(void* bar, int direction)
    {
        if (!g_Trunk || bar != static_cast<char*>(g_Trunk) + kTrunkSortBar)
        {
            reinterpret_cast<void(__fastcall*)(void*, int)>(orig_StepSortMode)(bar, direction);
            return;
        }

        // The game's loop with one more step: 0..10 where the bar's mask allows, then New.
        const uint32_t enabled = At<uint32_t>(bar, kBarEnabled);
        int step = g_NewFirst ? kNewStep : At<int>(bar, kBarMode);
        int tries = 0;
        do
        {
            step = ((step + direction) % (kGameModes + 1) + kGameModes + 1) % (kGameModes + 1);
            if (step == kNewStep || (enabled >> step & 1))
                break;
        } while (++tries <= kGameModes);
        if (tries > kGameModes)
            return;

        g_NewFirst = step == kNewStep;
        const int mode = g_NewFirst ? 0 : step;   // New sorts by name inside each half, and keeps Name's group jump and row headers
        At<int>(bar, kBarMode) = mode;
        const intptr_t label = g_NewFirst ? reinterpret_cast<intptr_t>(kNewLabel)
                                          : *reinterpret_cast<const volatile intptr_t*>(kSortModeLabels + 40 * static_cast<size_t>(mode));   // volatile: Release folds constant game addresses
        SetTextById(At<void*>(bar, kBarText), label);
        FitLabel(static_cast<char*>(bar) + kBarFitter, kLabelWidth);
    }

    struct Site
    {
        uintptr_t* Target;
        void* Hook;
    };
}

void TrunkNewSort::Install()
{
    const Site sites[] = {
        { &orig_BuildCardList, Hook_BuildCardList },
        { &orig_SortAndFillGrid, Hook_SortAndFillGrid },
        { &orig_StepSortMode, Hook_StepSortMode },
    };
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    for (const Site& site : sites)
        DetourAttach(&reinterpret_cast<PVOID&>(*site.Target), site.Hook);
    const LONG error = DetourTransactionCommit();
    if (error != NO_ERROR)
    {
        Logger::WriteLog(std::format("Trunk New sort: the hooks failed ({}), the trunk has no New sort", error), MODULE_NAME, 2);
        return;
    }
    Logger::WriteLog("Trunk New sort: on (the trunk's sort button has a New step)", MODULE_NAME, 0);
}
