#include "BulkPacks.h"

#include <Windows.h>
#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <format>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include "Detours.h"
#include "HookDiagnostics.h"
#include "Logger.h"
#include "Save.h"
#include "Yu-Gi-Oh-Memory.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-RIX.h"
#include "YuGiOh/YuGiOh-Addresses.h"

namespace R = YGO::RIX;

namespace
{
    // ---- the game (names and types in the IDB: ScreenRandomPackStore, GridCursor, docs/Packs.md) ----

    struct GameWString            // MSVC std::wstring as the game builds it (0x20)
    {
        union { wchar_t* Ptr; wchar_t Buf[8]; } Data;
        uint64_t Size;
        uint64_t Capacity;
        const wchar_t* c_str() const { return Capacity >= 8 ? Data.Ptr : Data.Buf; }
    };

    struct PackRecord             // g_PackRecords entry (0x68)
    {
        uint32_t Id, Series, Cost, Kind;
        const char* Name;
        uint16_t* Contents;       // u16 common count, u16 rare count, common ids, rare ids
        void* BattleData;
        GameWString Title, Text;
    };
    static_assert(sizeof(PackRecord) == 0x68, "PackRecord layout drifted");

    struct U16Vector { uint16_t* First; uint16_t* Last; uint16_t* End; };

    // ScreenRandomPackStore (screen id 29, one object: g_ScreenRandomPackStore 0x1433280D0), the fields used here
    constexpr size_t kDialog = 432;           // widget_Dialog: the screen's Yes/No box, centred at 960, 540; Yes / No at 960 -+ 270, y 729
    constexpr size_t kDialogTextLayer = 96;   // widget_Dialog: the text layer SetText writes (DFX::TLayerText, style at +152)
    constexpr size_t kCardInfo = 1328;        // widget_CardInfo beside the grid
    constexpr size_t kResultGrid = 2256;      // widget_CardDisplay (a widget_CardGrid): the pulled cards, 4 x 2 cells, scrolls by rows
    constexpr size_t kGridCursor = kResultGrid + 432;   // RIX::GridCursor: +12 Rows, +20 FirstRow, +28 VisibleRows
    constexpr size_t kState = 4432;           // 1 = choosing a pack, 2 = wrapper, 3 = flip, 4 = cards, 5 = one card, 6/7 = leaving
    constexpr size_t kPulledCards = 4448;     // std::vector<u16>: what OpenPackAndGrantCards pulled
    constexpr size_t kBuyingPack = 4592;      // PackRecord* of the pack in the Yes/No box
    constexpr size_t kDpShown = 4624;         // the profile's DP, read when the shop opens and after every pack
    constexpr uintptr_t kShopScreen = 0x1433280D0;          // g_ScreenRandomPackStore
    constexpr uintptr_t kBuyYesVftable = 0x140A77390;       // the shop's Yes lambda (std::_Func_impl_no_alloc), BoosterShop_BuyYes_DoCall
    constexpr uintptr_t kStringLocations = 0x143329E80;     // YGO::LOCAL::g_iStringLocations[7] -> { u32 count; wchar_t** strings (+8) }
    constexpr int kBuyTextId = 717;                         // "buy <title> for <cost> DP?" (BoosterShop_HandleInput reads strings + 5736)
    constexpr uint8_t kSavedCopies = 7;                     // saved card table byte: bits 0-2 = copies

    // Calls inside OpenPackAndGrantCards (each a 5-byte call rel32) that a bulk purchase holds back. They are gated at the call, not at
    // the function, so other plugins' hooks on those functions (Campaign's RebuildLiveUnlockCounts: new decks) don't run once per pack.
    constexpr uintptr_t kCallRecount = 0x14085C349;         // YGO::SAVE::RecountOwnedCards (+ the Steam "unique cards" upload)
    constexpr uintptr_t kCallSetCards = 0x14085C376;        // RIX::widget_CardGrid::SetCards on the result grid (loads 8 cards' art)
    constexpr uintptr_t kCallRebuild = 0x14085C380;         // YGO::SAVE::RebuildLiveUnlockCounts (every deck template + 732 decks)
    constexpr uintptr_t kCallAutosave = 0x14085C385;        // YGO::SAVE::RequestAutosave

    // Input bits (RIX::ScreenBase::Tick hands the box GetPressed | GetRepeat; the wheel is taken separately, as the deck editor does)
    constexpr int kUp = 1, kDown = 2, kLeft = 4, kRight = 8, kConfirm = 0x1000;
    constexpr unsigned kWheelUp = 0x20000000u, kWheelDown = 0x40000000u;
    constexpr int kSoundStep = 16, kSoundUnable = 71;

    using ShowYesNoText_t = int64_t(__fastcall*)(void* screen, int sfx, const wchar_t* text, void* onYes);
    using DialogInput_t = char(__fastcall*)(void* dialog, int pressed);
    using OpenPack_t = void(__fastcall*)(void* screen);
    using SetState_t = void(__fastcall*)(void* screen, int state, double seconds);
    using ShopUpdate_t = void(__fastcall*)(void* screen, void* ui, double seconds);

    uintptr_t orig_ShowYesNoText = 0x1408228D0;          // RIX::Screen::ShowYesNoText(screen, sfx, text, std::function<void()>* onYes)
    uintptr_t orig_DialogInput = 0x140898040;            // RIX::widget_Dialog::ProcessInput: 1 = a button was pressed (its callback already ran)
    uintptr_t orig_OpenPack = 0x14085BFC0;               // GAME::Store::OpenPackAndGrantCards
    uintptr_t orig_SetState = 0x14085D790;               // YGO::UI::BoosterShop_SetState
    uintptr_t orig_ShopUpdate = 0x14085C7C0;             // RIX::ScreenRandomPackStore::Update

    const auto DialogSetText = reinterpret_cast<void(__fastcall*)(void* dialog, const wchar_t* text)>(0x1408988E0);  // RIX::widget_Dialog::SetText
    const auto GetCardUnlockTable = reinterpret_cast<uint8_t*(__fastcall*)(unsigned int profile)>(0x1407F8130);   // Core's 64K table (CardTable.h)
    const auto Vector_EmplaceAt = reinterpret_cast<void(__fastcall*)(U16Vector* vector, uint16_t* where, const uint16_t* value)>(0x14077C310);  // grows with the game's allocator
    const auto NumberGrouped = reinterpret_cast<GameWString*(__fastcall*)(GameWString* out, unsigned int value)>(0x14086A3F0);  // WString_FromNumberGrouped
    const auto GameFree = reinterpret_cast<void(__cdecl*)(void* block)>(0x14090D2F4);                             // the game's free
    const auto TakeWheel = reinterpret_cast<int(__fastcall*)(void* input, unsigned up, unsigned down)>(0x140800900);  // YGO::Input::TakeWheel: the wheel as button bits

    template <typename T> T& At(void* screen, size_t offset) { return *reinterpret_cast<T*>(static_cast<uint8_t*>(screen) + offset); }
    void* ShopScreen() { return *reinterpret_cast<void**>(kShopScreen); }

    struct Point { float X, Y; };
    Point Mouse()
    {
        const int64_t mouse = R::Input::MousePosition();
        return { static_cast<float>(static_cast<int32_t>(mouse)), static_cast<float>(static_cast<int32_t>(mouse >> 32)) };
    }
    bool MouseIn(float left, float top, float width, float height)
    {
        const Point mouse = Mouse();
        return mouse.X >= left && mouse.X < left + width && mouse.Y >= top && mouse.Y < top + height;
    }

    // ---- state ----

    int g_maxPerPurchase = 999;

    struct Buying                 // the Yes/No box while it is open
    {
        bool Active = false;
        void* Screen = nullptr;
        const PackRecord* Pack = nullptr;
        int Count = 1, Most = 1;
    } g_buy;
    int g_pendingCount = 1;       // what Yes buys (read by Hook_OpenPack inside the box's input call)
    bool g_holdSaveWork = false;  // all but the last pack: recount, deck rebuild and autosave are skipped (read by the call gates)
    bool g_holdGrid = false;      // every pack of a bulk purchase: the grid is filled once at the end (read by the call gate)
    wchar_t g_boxText[2][512];    // the box's text, two buffers so the shown one is never the one being written
    int g_boxBuffer = 0;

    struct Result                 // the last purchase of more than one pack, while its cards are shown
    {
        bool Active = false;
        int Packs = 0;
    } g_result;

    // ---- text in the box's own font ----

    // The game's sans font, as the Yes/No box writes with (font id from its text layer's style, +152 +32); 2 = PD 32 px if it looks wrong.
    int BoxFont(void* screen)
    {
        auto* layer = At<uint8_t*>(static_cast<uint8_t*>(screen) + kDialog, kDialogTextLayer);
        const int font = layer ? *reinterpret_cast<int*>(layer + 152 + 32) : -1;
        return font >= 0 && font < 64 ? font : 2;
    }

    // One line of text centred on X with its top at Y, in the given font at its own size (R::Dfx::AddText picks PD fonts by pixel size
    // instead). Text must outlive the node.
    R::SharedNode AddLine(void* screen, const wchar_t* text, int font, int z, float x, float y)
    {
        R::SharedNode layer{};
        R::Dfx::MakeLayerText(&layer);
        if (!layer.Node)
            return {};
        char* style = static_cast<char*>(layer.Node) + 152;
        *reinterpret_cast<uint32_t*>(style + 8) = 0xFFFFFFFF;
        *reinterpret_cast<uint32_t*>(style + 12) = 0xFFFFFFFF;
        *reinterpret_cast<uint32_t*>(style + 24) = 0x100;   // no wrap
        *reinterpret_cast<float*>(style + 28) = 0.0f;
        *reinterpret_cast<int*>(style + 32) = font;
        *reinterpret_cast<int*>(style + 40) = 8 | 2;        // top, centred on x
        R::Dfx::LayerSetText(layer.Node, reinterpret_cast<int64_t>(text));
        R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
        R::SharedNode node{};
        R::Dfx::MakeChildWithContent(&node, &parent, &layer, z, x, y);   // parent and layer consumed
        return node;
    }

    void Remove(void* screen, R::SharedNode& node)
    {
        if (node.Node && screen)
            R::Dfx::RemoveImage(R::ScreenRoot(screen), node);
        node = {};
    }

    // doShared's arrow_left1 (the right one is it flipped), shrunk by kArrowScale
    float g_arrowW = 0, g_arrowH = 0;
    void MeasureArrow()
    {
        if (g_arrowW != 0)
            return;
        R::SharedNode probe{};
        R::Dfx::MakeLayerAnimoo(&probe);
        int w = 0, h = 0;
        if (probe.Node)
        {
            R::Dfx::LayerSetResource(probe.Node, "pdui/doshared");
            R::Dfx::LayerSelectByName(probe.Node, "arrow_left1");
            if (R::Dfx::LayerSpriteSize(probe.Node, w, h))
                g_arrowW = static_cast<float>(w), g_arrowH = static_cast<float>(h);
            R::ReleaseRef(probe);
        }
    }

    // ---- the box ----

    std::wstring Grouped(unsigned int value)
    {
        GameWString text = {};
        NumberGrouped(&text, value);
        std::wstring copy = text.c_str();
        if (text.Capacity >= 8)   // MSVC's big-block rule, as the game frees it: blocks of 0x1000+ bytes keep the real pointer just before
        {
            void* block = text.Data.Ptr;
            if (2 * text.Capacity + 2 >= 0x1000)
                block = reinterpret_cast<void**>(text.Data.Ptr)[-1];
            GameFree(block);
        }
        return copy;
    }

    const wchar_t* GameString(int id)
    {
        const int language = *reinterpret_cast<const int*>(YGO::Addr::g_iGameLanguageID);
        if (language < 0 || language >= 7)
            return nullptr;
        auto table = reinterpret_cast<uint8_t* const*>(kStringLocations)[language];
        if (!table || id >= static_cast<int>(*reinterpret_cast<const uint32_t*>(table)))
            return nullptr;
        return (*reinterpret_cast<wchar_t* const* const*>(table + 8))[id];
    }

    // The count row inside the box, between its text and its Yes / No:  «  ‹  3 packs  ›  »  in the box's own font. doShared's
    // arrow_right1 is a single chevron (1 pack, flipped for the left), arrow_left1 a double one (10 packs, flipped for the right); both
    // 58 x 71, drawn at 60%.
    constexpr float kRowY = 640, kRowHalfGap = 110;      // the row's middle; the single arrows' inner edges are this far from x 960
    constexpr float kRowArrowGap = 12;                   // between a single arrow and the double one outside it
    constexpr float kRowLine = 34;                       // the box font's line height (desc 0x140D2AA60 size 34)
    constexpr float kRowArrowScale = 0.6f;
    constexpr int kRowZ = 10010;                         // over the box (widget_Dialog is z 10001 on the screen root)

    struct RowArrow
    {
        int Step;                 // what a click adds
        const char* Sprite;
        bool Flip;
        R::SharedNode Node;
    };
    RowArrow g_rowArrows[] = {
        { -10, "arrow_left1", false },
        { -1, "arrow_right1", true },
        { 1, "arrow_right1", false },
        { 10, "arrow_left1", true },
    };
    R::SharedNode g_countNode;
    std::wstring g_countText[2];                         // the node keeps the pointer: the shown text is never the one rewritten
    int g_countBuffer = 0;

    float RowArrowW() { return g_arrowW * kRowArrowScale; }
    float RowArrowH() { return g_arrowH * kRowArrowScale; }
    float ArrowTop() { return kRowY - RowArrowH() / 2; }
    float RowArrowX(const RowArrow& arrow)
    {
        const float inner = arrow.Step < 0 ? 960 - kRowHalfGap - RowArrowW() : 960 + kRowHalfGap;
        const float outer = RowArrowW() + kRowArrowGap;
        return arrow.Step == -10 ? inner - outer : arrow.Step == 10 ? inner + outer : inner;
    }

    void HideCountRow()
    {
        for (RowArrow& arrow : g_rowArrows)
            Remove(g_buy.Screen, arrow.Node);
        Remove(g_buy.Screen, g_countNode);
    }

    void ShowCountRow()
    {
        const R::SharedNode* root = R::ScreenRoot(g_buy.Screen);
        MeasureArrow();
        if (g_arrowW > 0)
            for (RowArrow& arrow : g_rowArrows)
                if (!arrow.Node.Node)
                    arrow.Node = R::Dfx::AddImage(root, "pdui/doshared", arrow.Sprite, kRowZ, RowArrowX(arrow), ArrowTop(),
                        arrow.Flip ? -RowArrowW() : RowArrowW(), RowArrowH());
        Remove(g_buy.Screen, g_countNode);
        std::wstring& text = g_countText[g_countBuffer ^= 1];
        text = std::format(L"{} {}", g_buy.Count, g_buy.Count == 1 ? L"pack" : L"packs");
        g_countNode = AddLine(g_buy.Screen, text.c_str(), BoxFont(g_buy.Screen), kRowZ, 960, kRowY - kRowLine / 2);
    }

    // The box's text: the game's own buy line with the total cost.
    void ShowBoxText()
    {
        const std::wstring total = Grouped(g_buy.Pack->Cost * static_cast<unsigned>(g_buy.Count));
        wchar_t* text = g_boxText[g_boxBuffer ^= 1];
        text[0] = 0;
        if (const wchar_t* format = GameString(kBuyTextId))
            _snwprintf_s(text, std::size(g_boxText[0]), _TRUNCATE, format, g_buy.Pack->Title.c_str(), total.c_str());
        DialogSetText(static_cast<uint8_t*>(g_buy.Screen) + kDialog, text);
        ShowCountRow();
    }

    // Holding the mouse on an arrow repeats it: after kHoldDelay, every kHoldRepeat. The game keeps only "clicked this frame", so the
    // button's state comes from Windows (the primary button, so swapped buttons work).
    constexpr auto kHoldDelay = std::chrono::milliseconds(400), kHoldRepeat = std::chrono::milliseconds(70);
    struct Hold
    {
        int Step = 0;
        std::chrono::steady_clock::time_point Next;
    } g_hold;

    bool PrimaryButtonDown()
    {
        return (GetAsyncKeyState(GetSystemMetrics(SM_SWAPBUTTON) ? VK_RBUTTON : VK_LBUTTON) & 0x8000) != 0;
    }

    // The step of the arrow under the mouse (-10, -1, 1, 10; half the gap between arrows as slack), else 0. Not gated on MouseActive
    // (it drops while the mouse stands still); a click is still needed.
    int ArrowUnderMouse()
    {
        if (g_arrowW <= 0)
            return 0;
        constexpr float slack = kRowArrowGap / 2;
        for (const RowArrow& arrow : g_rowArrows)
            if (MouseIn(RowArrowX(arrow) - slack, ArrowTop() - 12, RowArrowW() + 2 * slack, RowArrowH() + 24))
                return arrow.Step;
        return 0;
    }

    // The shop's buy box (BoosterShop_HandleInput, the only ShowYesNoText with the shop's Yes lambda): it gets a count when the player
    // can afford more than one.
    int64_t __fastcall Hook_ShowYesNoText(void* screen, int sfx, const wchar_t* text, void* onYes)
    {
        HideCountRow();
        g_hold = {};
        g_buy.Active = false;
        g_pendingCount = 1;
        bool shop = false;
        if (screen && screen == ShopScreen() && onYes)
            if (void* impl = *reinterpret_cast<void**>(static_cast<uint8_t*>(onYes) + 56))   // std::function: impl pointer after 56 bytes of storage
                shop = *reinterpret_cast<uintptr_t*>(impl) == kBuyYesVftable;
        const int64_t result = reinterpret_cast<ShowYesNoText_t>(orig_ShowYesNoText)(screen, sfx, text, onYes);
        if (!shop)
            return result;
        const auto* pack = At<const PackRecord*>(screen, kBuyingPack);
        const int dp = At<int>(screen, kDpShown);
        if (!pack)
            return result;
        const int affordable = pack->Cost ? dp / static_cast<int>(pack->Cost) : g_maxPerPurchase;
        const int most = (std::min)(g_maxPerPurchase, affordable);
        if (most <= 1)
            return result;   // the game's own box
        g_buy = { true, screen, pack, 1, most };
        ShowBoxText();
        return result;
    }

    char __fastcall Hook_DialogInput(void* dialog, int pressed)
    {
        if (!g_buy.Active || dialog != static_cast<uint8_t*>(g_buy.Screen) + kDialog)
            return reinterpret_cast<DialogInput_t>(orig_DialogInput)(dialog, pressed);

        int step = 0;
        const int arrow = ArrowUnderMouse();
        if (arrow != 0 && R::Input::TakeMouseClick(R::InputState, kConfirm))
        {
            step = arrow;   // a click on an arrow (taken, so the box does not see it); holding it repeats
            g_hold = { arrow, std::chrono::steady_clock::now() + kHoldDelay };
        }
        else if (g_hold.Step != 0 && (arrow != g_hold.Step || !PrimaryButtonDown()))
            g_hold = {};    // let go, or slid off the arrow
        else if (g_hold.Step != 0 && std::chrono::steady_clock::now() >= g_hold.Next)
        {
            step = g_hold.Step;
            g_hold.Next = std::chrono::steady_clock::now() + kHoldRepeat;
        }
        else if (pressed & kRight) step = 1;
        else if (pressed & kLeft) step = -1;
        else if (pressed & kUp) step = 10;
        else if (pressed & kDown) step = -10;
        else if (const int wheel = TakeWheel(R::InputState, kWheelUp, kWheelDown))
            step = (wheel & kWheelUp) ? 1 : -1;
        pressed &= ~(kUp | kDown | kLeft | kRight);

        if (step != 0)
        {
            int count = g_buy.Count;
            // both wrap between 1 and the most: 1 at a time always; 10 at a time once it is at the end (999 + 10 = 1, 1 - 10 = 999),
            // stopping on the end first (995 + 10 = 999); from 1, +10 goes to 10
            if (step > 0 && count == g_buy.Most)
                count = 1;
            else if (step < 0 && count == 1)
                count = g_buy.Most;
            else
                count = std::clamp(count == 1 && step == 10 ? 10 : count + step, 1, g_buy.Most);
            R::PlayUISound(count != g_buy.Count ? kSoundStep : kSoundUnable);
            if (count != g_buy.Count)
            {
                g_buy.Count = count;
                ShowBoxText();
            }
        }

        g_pendingCount = g_buy.Count;   // Yes runs OpenPackAndGrantCards inside this call (widget_Dialog::ActivateItem)
        const char closed = reinterpret_cast<DialogInput_t>(orig_DialogInput)(dialog, pressed);
        if (closed)
        {
            g_hold = {};
            HideCountRow();
            g_buy.Active = false;
            g_pendingCount = 1;
        }
        return closed;
    }

    // ---- opening ----

    // A gate on one call instruction: while *Flag is set the call is skipped, otherwise it runs as before (through the Detours
    // trampoline, which holds the relocated call). Installed with DetourAttach on the call's address (feedback: every hook via Detours).
    //   mov rax, &flag ; cmp byte [rax], 0 ; jne skip ; mov rax, &trampoline ; jmp [rax] ; skip: mov rax, site + 5 ; jmp rax
    // rax and the flags are free at a call (the call itself clobbers them; none of these four calls' results are read).
    struct CallGate
    {
        uintptr_t Site;
        const bool* Flag;
        PVOID Trampoline;         // DetourAttach's pointer: the site before, the trampoline after
        uint8_t* Stub;
    };
    CallGate g_gates[] = {
        { kCallRecount, &g_holdSaveWork },
        { kCallSetCards, &g_holdGrid },
        { kCallRebuild, &g_holdSaveWork },
        { kCallAutosave, &g_holdSaveWork },
    };

    bool BuildGate(CallGate& gate)
    {
        if (*reinterpret_cast<const uint8_t*>(gate.Site) != 0xE8)
        {
            Logger::WriteLog(std::format("Bulk packs: no call at {:#x}; the game differs from the one traced", gate.Site), MODULE_NAME, 2);
            return false;
        }
        gate.Stub = static_cast<uint8_t*>(VirtualAlloc(nullptr, 64, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
        if (!gate.Stub)
            return false;
        gate.Trampoline = reinterpret_cast<PVOID>(gate.Site);
        const uint64_t flag = reinterpret_cast<uint64_t>(gate.Flag);
        const uint64_t slot = reinterpret_cast<uint64_t>(&gate.Trampoline);
        const uint64_t next = gate.Site + 5;
        uint8_t* p = gate.Stub;
        auto imm64 = [&](uint64_t value) { std::memcpy(p, &value, 8); p += 8; };
        *p++ = 0x48; *p++ = 0xB8; imm64(flag);          // mov rax, &flag
        *p++ = 0x80; *p++ = 0x38; *p++ = 0x00;          // cmp byte ptr [rax], 0
        *p++ = 0x75; *p++ = 0x0C;                       // jne skip (+12)
        *p++ = 0x48; *p++ = 0xB8; imm64(slot);          // mov rax, &trampoline
        *p++ = 0xFF; *p++ = 0x20;                       // jmp qword ptr [rax]
        *p++ = 0x48; *p++ = 0xB8; imm64(next);          // skip: mov rax, site + 5
        *p++ = 0xFF; *p++ = 0xE0;                       // jmp rax
        FlushInstructionCache(GetCurrentProcess(), gate.Stub, 64);
        return true;
    }

    // A bulk purchase opens a few packs per frame while the big wrapper waits for the player (state 2), so the game never stops for it.
    // Yes only starts the job (no label: the user wants the wrapper screen plain). Unwrapping (SetState 3) before the last pack is held until
    // it is done (the packs then open with a bigger budget per frame).
    using Clock = std::chrono::steady_clock;
    constexpr float kFlipStagger = 0.12f;   // seconds between the reveal's card flips (the game's 0.25), see Install
    constexpr double kBudgetMs = 4.0, kBudgetUnwrapMs = 12.0;   // per frame: while waiting / once the player unwrapped

    struct OpenJob
    {
        bool Running = false, UnwrapWaiting = false;
        void* Screen = nullptr;
        const PackRecord* Pack = nullptr;
        int Asked = 0, Opened = 0, Cards = 0;
        std::unordered_set<uint16_t> Rares, OwnedBefore;
        std::unordered_map<uint16_t, int> Pulled;   // card -> copies pulled
        std::vector<uint16_t> Order;                // first pull order, for a stable sort
        Clock::time_point Started;
        int64_t LastPackMs = 0;
    } g_job;

    void FinishJob()
    {
        void* screen = g_job.Screen;
        // new cards first, then rares, then the most pulled: the first eight are what the flip shows
        std::vector<uint16_t> shown = g_job.Order;
        std::stable_sort(shown.begin(), shown.end(), [](uint16_t a, uint16_t b)
        {
            const bool newA = !g_job.OwnedBefore.contains(a), newB = !g_job.OwnedBefore.contains(b);
            if (newA != newB) return newA;
            const bool rareA = g_job.Rares.contains(a), rareB = g_job.Rares.contains(b);
            if (rareA != rareB) return rareA;
            return g_job.Pulled[a] > g_job.Pulled[b];
        });
        const size_t fresh = static_cast<size_t>(std::count_if(shown.begin(), shown.end(), [](uint16_t id) { return !g_job.OwnedBefore.contains(id); }));

        // the grid shows the screen's pulled list: put each card in once (the game's own vector growth, so the game can free it)
        auto& pulled = At<U16Vector>(screen, kPulledCards);
        pulled.Last = pulled.First;
        for (uint16_t id : shown)
            if (pulled.Last == pulled.End)
                Vector_EmplaceAt(&pulled, pulled.Last, &id);
            else
                *pulled.Last++ = id;
        const auto gridStarted = Clock::now();
        R::CardBox::SetCards(static_cast<uint8_t*>(screen) + kResultGrid, static_cast<unsigned>(pulled.Last - pulled.First), pulled.First, -1, nullptr, nullptr);
        const auto now = Clock::now();

        g_result = { true, g_job.Opened };
        g_job.Running = false;
        Logger::WriteLog(std::format("Bought {} x {} ({} asked): {} cards, {} different, {} new; {} ms over the wrapper (last pack with the save work {} ms, grid {} ms)",
            g_job.Opened, g_job.Pack->Name ? g_job.Pack->Name : "?", g_job.Asked, g_job.Cards, shown.size(), fresh,
            std::chrono::duration_cast<std::chrono::milliseconds>(now - g_job.Started).count(), g_job.LastPackMs,
            std::chrono::duration_cast<std::chrono::milliseconds>(now - gridStarted).count()), MODULE_NAME, 0);
        if (g_job.UnwrapWaiting)
        {
            g_job.UnwrapWaiting = false;
            // the unwrap the player asked for, through the function's entry so every hook on it (Packs.cpp's too) sees it
            reinterpret_cast<SetState_t>(0x14085D790)(screen, 3, 0.0);
        }
    }

    // Opens packs until the frame's budget is used. The DP is checked before each pack (the box's count came from the DP when it opened).
    void StepJob(double budgetMs)
    {
        void* screen = g_job.Screen;
        const auto& pulled = At<U16Vector>(screen, kPulledCards);
        const int cost = static_cast<int>(g_job.Pack->Cost);
        const auto frameStarted = Clock::now();
        g_holdGrid = true;
        while (g_job.Running)
        {
            if (cost && At<int>(screen, kDpShown) < cost)
            {
                // out of DP before the last pack (the DP changed elsewhere): the save work the packs so far held back
                g_holdSaveWork = false;
                g_holdGrid = false;
                reinterpret_cast<void(__fastcall*)()>(0x140870570)();                  // YGO::SAVE::RecountOwnedCards
                reinterpret_cast<int64_t(__fastcall*)(unsigned)>(0x1407BB160)(0xFFFFFFFD);   // YGO::SAVE::RebuildLiveUnlockCounts
                reinterpret_cast<int64_t(__fastcall*)()>(0x140806950)();               // YGO::SAVE::RequestAutosave
                FinishJob();
                break;
            }
            const bool last = g_job.Opened + 1 == g_job.Asked || (cost && At<int>(screen, kDpShown) < 2 * cost);
            g_holdSaveWork = !last;   // recount, deck rebuild and the autosave once, with the last pack
            const auto packStarted = Clock::now();
            reinterpret_cast<OpenPack_t>(orig_OpenPack)(screen);
            ++g_job.Opened;
            for (const uint16_t* id = pulled.First; id != pulled.Last; ++id)
            {
                ++g_job.Cards;
                if (g_job.Pulled[*id]++ == 0)
                    g_job.Order.push_back(*id);
            }
            if (last)
            {
                g_job.LastPackMs = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - packStarted).count();
                g_holdSaveWork = false;
                g_holdGrid = false;
                FinishJob();
                break;
            }
            if (std::chrono::duration<double, std::milli>(Clock::now() - frameStarted).count() >= budgetMs)
                break;
        }
        g_holdSaveWork = false;
        g_holdGrid = false;
    }

    void __fastcall Hook_OpenPack(void* screen)
    {
        g_result = {};
        const int count = g_pendingCount;
        g_pendingCount = 1;
        const auto* pack = At<const PackRecord*>(screen, kBuyingPack);
        if (count <= 1 || !pack || !pack->Contents || g_job.Running)
            return reinterpret_cast<OpenPack_t>(orig_OpenPack)(screen);

        g_job = {};
        g_job.Running = true;
        g_job.Screen = screen;
        g_job.Pack = pack;
        g_job.Asked = count;
        g_job.Started = Clock::now();
        // owned before, for "new": the saved table (Core's, every Konami id), copies in bits 0-2
        const uint8_t* table = GetCardUnlockTable(0xFFFFFFFD);
        const uint16_t* contents = pack->Contents;
        g_job.Rares.insert(contents + 2 + contents[0], contents + 2 + contents[0] + contents[1]);
        if (table)
            for (int i = 0; i < contents[0] + contents[1]; ++i)
                if (table[contents[2 + i]] & kSavedCopies)
                    g_job.OwnedBefore.insert(contents[2 + i]);
        // the grid is empty until the job is done (the flip waits for it); the first packs open with this frame's budget
        auto& pulled = At<U16Vector>(screen, kPulledCards);
        pulled.Last = pulled.First;
        StepJob(kBudgetMs);
    }

    // ---- page control under the result grid: arrows either side of a meter of one block per page (tall = the page shown),
    // like the shop's "more packs" indicator (Packs.cpp). A click on an arrow or a block turns the page. ----

    constexpr float kGridLeft = 757.65f, kGridRight = 1848, kGridTop = 145, kGridBottom = 935;   // CardDisplay 757.66, 145, 4 x 2 cells
    constexpr float kPagerMiddle = 990;                               // between the grid and the help bar, clear of a hovered card's frame
    constexpr float kPagerArrowScale = 0.55f;
    constexpr float kBlockWidth = 30, kBlockGap = 8, kTallHeight = 36, kShortHeight = 18;
    constexpr float kPagerWidth = 640;                                 // the meter's most width, centred under the grid
    constexpr int kPagerZ = 900;

    struct Pager
    {
        std::vector<R::SharedNode> Nodes;
        float LeftX = 0, RightX = 0, ArrowTop = 0, ArrowW = 0, ArrowH = 0;   // the arrows' boxes
        float BlocksLeft = 0, Step = 0;
        int Pages = 0, Page = -1;
    } g_pager;

    void HidePager(void* screen)
    {
        for (R::SharedNode& node : g_pager.Nodes)
            Remove(screen, node);
        g_pager = {};
    }

    void ShowPager(void* screen)
    {
        const int* cursor = &At<int>(screen, kGridCursor);
        const int rows = cursor[3], first = cursor[5], visible = (std::max)(1, cursor[7]);
        const int pages = (rows + visible - 1) / visible;
        const int page = first + visible >= rows ? pages - 1 : first / visible;
        if (pages == g_pager.Pages && page == g_pager.Page)
            return;   // nothing moved
        HidePager(screen);
        if (pages <= 1)
            return;
        MeasureArrow();
        const R::SharedNode* root = R::ScreenRoot(screen);
        const float middleX = (kGridLeft + kGridRight) / 2;
        const float step = (std::min)(kBlockWidth + kBlockGap, kPagerWidth / pages);
        const float block = (std::max)(2.0f, (std::min)(kBlockWidth, step - 2));
        g_pager.Pages = pages;
        g_pager.Page = page;
        g_pager.Step = step;
        g_pager.BlocksLeft = middleX - step * pages / 2;
        g_pager.ArrowW = g_arrowW * kPagerArrowScale;
        g_pager.ArrowH = g_arrowH * kPagerArrowScale;
        g_pager.ArrowTop = kPagerMiddle - g_pager.ArrowH / 2;
        g_pager.LeftX = g_pager.BlocksLeft - 16 - g_pager.ArrowW;
        g_pager.RightX = g_pager.BlocksLeft + step * pages + 16;
        if (g_arrowW > 0)   // both always: the pages wrap
        {
            g_pager.Nodes.push_back(R::Dfx::AddImage(root, "pdui/doshared", "arrow_left1", kPagerZ, g_pager.LeftX, g_pager.ArrowTop, g_pager.ArrowW, g_pager.ArrowH));
            g_pager.Nodes.push_back(R::Dfx::AddImage(root, "pdui/doshared", "arrow_left1", kPagerZ, g_pager.RightX, g_pager.ArrowTop, -g_pager.ArrowW, g_pager.ArrowH));
        }
        for (int i = 0; i < pages; ++i)
        {
            const float height = i == page ? kTallHeight : kShortHeight;
            g_pager.Nodes.push_back(R::Dfx::AddImage(root, "pdui/Settings", i == page ? "Level4" : "Level2", kPagerZ,
                g_pager.BlocksLeft + step * i + (step - block) / 2, kPagerMiddle + kTallHeight / 2 - height, block, height));
        }
    }

    // Scrolls the grid by rows (the wheel bits GridCursor::Move takes, one row each) and shows the new card in the info panel.
    void ScrollGrid(void* screen, int rows)
    {
        void* grid = static_cast<uint8_t*>(screen) + kResultGrid;
        bool moved = false;
        for (int i = 0; i < (rows < 0 ? -rows : rows); ++i)
            moved |= R::CardBox::ProcessInput(grid, rows < 0 ? kWheelUp : kWheelDown, 0, nullptr, nullptr) != 0;
        if (moved)
            R::CardInfo::SetCard(static_cast<uint8_t*>(screen) + kCardInfo, R::CardBox::GetSelectedCardId(grid));
    }

    // Before the game's state 4: the wheel over the grid, and clicks on the page control (taken so the grid does not see them).
    void PagerInput(void* screen)
    {
        // the wheel anywhere over the grid or the page control (not gated on MouseActive: that flag drops after a key press or while the
        // mouse stands still, and a notch was then lost); one row per notch, as GridCursor's wheel scroll does
        int* cursor = &At<int>(screen, kGridCursor);
        // the grid's own wrap switches (GridCursor +40: up on the first row goes to the last and back; +44: left / right run on into the
        // row before / after); the shop leaves them off
        cursor[10] = 1;
        cursor[11] = 1;
        const int rows = cursor[3], first = cursor[5], visible = (std::max)(1, cursor[7]);
        if (MouseIn(kGridLeft - 20, kGridTop, kGridRight - kGridLeft + 60, kPagerMiddle + kTallHeight - kGridTop))
            if (const int wheel = TakeWheel(R::InputState, kWheelUp, kWheelDown))
            {
                // the wheel wraps too: down at the bottom goes back to the top, up at the top to the bottom
                if ((wheel & kWheelDown) && rows > visible && first + visible >= rows)
                    ScrollGrid(screen, -first);
                else if ((wheel & kWheelUp) && rows > visible && first == 0)
                    ScrollGrid(screen, rows - visible);
                else
                    ScrollGrid(screen, (wheel & kWheelUp) ? -1 : 1);
            }
        if (g_pager.Pages <= 1 || !R::Input::MouseClickPending())
            return;
        int target = -1;
        if (MouseIn(g_pager.LeftX - 10, g_pager.ArrowTop - 10, g_pager.ArrowW + 20, g_pager.ArrowH + 20))
            target = (g_pager.Page + g_pager.Pages - 1) % g_pager.Pages;
        else if (MouseIn(g_pager.RightX - 10, g_pager.ArrowTop - 10, g_pager.ArrowW + 20, g_pager.ArrowH + 20))
            target = (g_pager.Page + 1) % g_pager.Pages;
        else if (MouseIn(g_pager.BlocksLeft, kPagerMiddle - kTallHeight / 2 - 6, g_pager.Step * g_pager.Pages, kTallHeight + 12))
            target = std::clamp(static_cast<int>((Mouse().X - g_pager.BlocksLeft) / g_pager.Step), 0, g_pager.Pages - 1);
        if (target < 0 || !R::Input::TakeMouseClick(R::InputState, kConfirm))
            return;
        ScrollGrid(screen, target * visible - first);
    }

    void __fastcall Hook_SetState(void* screen, int state, double seconds)
    {
        // unwrapping before the last pack opened: held until it has (FinishJob makes this call), the packs open faster meanwhile
        if (state == 3 && g_job.Running && g_job.Screen == screen)
        {
            g_job.UnwrapWaiting = true;
            return;
        }
        if (state != 4)
            HidePager(screen);
        if (state == 1 || state >= 6)   // back to the packs, or leaving: the purchase is over
            g_result.Active = false;
        if (state >= 6)
        {
            HideCountRow();
            g_buy.Active = false;
        }
        reinterpret_cast<SetState_t>(orig_SetState)(screen, state, seconds);
    }

    void __fastcall Hook_ShopUpdate(void* screen, void* ui, double seconds)
    {
        const bool shop = screen == ShopScreen();
        if (shop && At<int>(screen, kState) == 4)
            PagerInput(screen);   // state 4 hands the grid no wheel, and the page control is ours
        reinterpret_cast<ShopUpdate_t>(orig_ShopUpdate)(screen, ui, seconds);
        if (!shop)
            return;
        const int now = At<int>(screen, kState);
        if (now == 2 && g_job.Running && g_job.Screen == screen)
            StepJob(g_job.UnwrapWaiting ? kBudgetUnwrapMs : kBudgetMs);
        else if (now == 4)
            ShowPager(screen);
    }
}

namespace BulkPacks
{
    void Install()
    {
        const std::string ini = Save::GameFolder() + "Config.ini";
        g_maxPerPurchase = std::clamp(static_cast<int>(GetPrivateProfileIntA("Yu-Gi-Oh-Core", "MaxPacksPerPurchase", 999, ini.c_str())), 1, 9999);
        if (g_maxPerPurchase <= 1)
        {
            Logger::WriteLog("Buying many packs at once: off (MaxPacksPerPurchase=1)", MODULE_NAME, 0);
            return;
        }
        // the Bulk Booster Purchase mod (a winmm.dll beside the game) changes the same box: both at once would count every key twice
        if (HMODULE winmm = GetModuleHandleA("winmm.dll"))
        {
            char path[MAX_PATH] = {};
            GetModuleFileNameA(winmm, path, MAX_PATH);
            std::string folder = Save::GameFolder();
            if (_strnicmp(path, folder.c_str(), folder.size()) == 0)
                Logger::WriteLog(std::string("A winmm.dll is loaded from the game folder (") + path + "): if it is the Bulk Booster Purchase mod, remove it, Core does this now", MODULE_NAME, 1);
        }
        for (CallGate& gate : g_gates)
            if (!BuildGate(gate))
                return;

        // The reveal's flip timings (BoosterShop_SetState state 3 copies them into the reveal lambdas; nothing else reads them):
        // {0.25 s between card flips, 1.0 s extra wait before the rare (8th) card, 0.6 s the rare's flip, 0.1}. The rare's wait goes and
        // the cards flip closer together, so all eight just turn over in a row. A data write, not a hook.
        constexpr uintptr_t kRevealTimings = 0x140A776E0;
        const float* timings = reinterpret_cast<const float*>(kRevealTimings);
        if (timings[0] == 0.25f && timings[1] == 1.0f)
        {
            YGO::Memory::WriteValue<float>(kRevealTimings, kFlipStagger);
            YGO::Memory::WriteValue<float>(kRevealTimings + 4, 0.0f);
        }
        else
            Logger::WriteLog(std::format("Bulk packs: reveal timings are {} / {}, not the traced 0.25 / 1.0; left alone", timings[0], timings[1]), MODULE_NAME, 1);

        if (!HookDiagnostics::Begin("Bulk packs", MODULE_NAME, Logger::WriteLog))
            return;
        DetourAttach(&(PVOID&)orig_ShowYesNoText, Hook_ShowYesNoText);
        DetourAttach(&(PVOID&)orig_DialogInput, Hook_DialogInput);
        DetourAttach(&(PVOID&)orig_OpenPack, Hook_OpenPack);
        DetourAttach(&(PVOID&)orig_SetState, Hook_SetState);
        DetourAttach(&(PVOID&)orig_ShopUpdate, Hook_ShopUpdate);
        for (CallGate& gate : g_gates)
            DetourAttach(&gate.Trampoline, gate.Stub);
        if (HookDiagnostics::Commit("Bulk packs", MODULE_NAME, Logger::WriteLog) != NO_ERROR)
            return;
        Logger::WriteLog(std::format("Buying many packs at once: up to {} per purchase", g_maxPerPurchase), MODULE_NAME, 0);
    }
}
