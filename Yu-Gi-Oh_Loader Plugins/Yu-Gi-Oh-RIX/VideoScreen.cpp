#include "VideoScreen.h"

#include <Windows.h>
#include <detours.h>
#include <intrin.h>
#include <cstdint>
#include <cstring>
#include <format>

#include "Logger.h"
#include "PluginMenu.h"
#include "YuGiOh/YuGiOh-RIX.h"

// Traced in YuGiOh.exe.i64 (ScreenHelpVideo, vftable 0x140A75A18). The screen keeps its widgets as 32 byte "entries" (two shared_ptrs: [0..1] the
// positioned wrapper, [2..3] the text widget or sprite) at fixed offsets:
//   +0x2B0 label of row 0 (Resolution), +0x2D0 label of row 1 (Display Mode)   (a 48 byte template, layout node 12 + row)
//   +0x330 box, +0x350 / +0x370 / +0x390 resolution texts, +0x3B0 sprite, +0x3D0 the Display Mode value text (node 8)
//   +0x3F0 / +0x410 the two arrows, moved to the selected row by UpdateVisuals from a table with one row per selectable row
//   +0x4B0 the selection (index at +0, count at +4)     +0x4F0 the two MenuKit buttons (Apply, Back), 424 bytes each
// The new row is created the same way as row 1, into entries of our own.
namespace
{
    constexpr uintptr_t kOnEnter = 0x14084FF40;         // builds the widgets
    constexpr uintptr_t kUpdate = 0x14084FB60;          // input
    constexpr uintptr_t kUpdateVisuals = 0x140850790;   // highlights, values, arrow positions, the buttons' positions (tables with four rows)
    constexpr uintptr_t kSelectionNav = 0x140868460;    // the selection object's up / down handling (shared by every screen)
    constexpr uintptr_t kDescription = 0x14089F6E0;     // MenuKit description text: the game reads the row's text id from a four row table
    constexpr uintptr_t kRowCountPatch = 0x14084ECFC;   // constructor: mov edx, 4 (the row count, also the page size)
    constexpr uintptr_t kTextTemplate = 0x140C8E958;    // the 48 byte template the label loop fills; the statics in it are kept
    constexpr uintptr_t kTemplateColor = 0x140C8E950;

    constexpr int kRow = 4;                             // the new row's index in the game's logic
    constexpr int kDrawnOrder[5] = { 0, 1, kRow, 2, 3 };// the order the rows are drawn (and navigated) in: Resolution, Display Mode, ours, Apply, Back
    constexpr float kRaiseResolution = 50.0f;           // Resolution moves up this much (it was at 230 / 330)
    constexpr float kRaiseMode = 90.0f;                 // Display Mode moves up this much (it was at 470 / 570)
    constexpr float kLabelY = 590.0f;                   // where the new row is drawn (its label sits just above the box, clear of Display Mode's)
    constexpr float kValueY = 660.0f;
    constexpr float kApplyY = 780.0f;                   // Apply and Back (they were at 710 and 830)
    constexpr float kBackY = 890.0f;
    constexpr uintptr_t kHitTemplate = 0x140A75B88;     // the template of the box behind Display Mode's value (474 x 99, node 7); the game hovers with it too
    constexpr uintptr_t kArrowYTable = 0x140C8E9BC;     // the arrows' y for each row (48 bytes apart): they follow the rows up
    constexpr uintptr_t kMouse = 0x142924070;           // the mouse position as the game keeps it (two ints)
    constexpr uintptr_t kRowHit = 0x14084F980;          // which row the mouse is over
    constexpr uintptr_t kDescriptionUpdate = 0x140850720; // puts the selected row's description under the rows
    constexpr float kArrowLeftX = 674.0f;               // Display Mode's arrow positions: outside the 474 wide box (723 to 1197) so it does not cover them
    constexpr float kArrowRightX = 1247.0f;
    constexpr int kMin = 3;
    constexpr int kMax = 5;

    struct SharedRef
    {
        void* Ptr;
        void* Control;
    };

    struct Entry
    {
        void* Part[4];
    };

    using Hook_t = void(__fastcall*)(char*);
    using Nav_t = int64_t(__fastcall*)(char* Selection, int Mask);
    using Description_t = void(__fastcall*)(void* Description, const wchar_t* Text);
    using CreateText_t = void(__fastcall*)(const void* Template, void* Dest, SharedRef* Layout, int Node);
    using SetText_t = void(__fastcall*)(void* Widget, int64_t Text);
    using Style_t = int64_t(__fastcall*)(char Selected);
    using Move_t = void(__fastcall*)(void* Element, float X, float Y);
    using MoveY_t = void(__fastcall*)(void* Element, float Y);
    using SetEnabled_t = void(__fastcall*)(void* Widget, char Enabled);
    using Changed_t = int64_t(__fastcall*)(char* Selection);
    using Events_t = int64_t(__fastcall*)(void* Input);
    using Confirm_t = int64_t(__fastcall*)(void* Input, unsigned int Mask);
    using Inside_t = bool(__fastcall*)(const void* Entry, int64_t Mouse);
    using RowHit_t = int64_t(__fastcall*)(char* Screen, int64_t Mouse);
    using Visible_t = bool(__fastcall*)(void* Widget);
    using Transform_t = void(__fastcall*)(void* Widget, float* Out, int64_t Mouse);

    Hook_t orig_OnEnter = reinterpret_cast<Hook_t>(kOnEnter);
    Hook_t orig_Update = reinterpret_cast<Hook_t>(kUpdate);
    Hook_t orig_UpdateVisuals = reinterpret_cast<Hook_t>(kUpdateVisuals);
    Nav_t orig_Nav = reinterpret_cast<Nav_t>(kSelectionNav);
    Description_t orig_Description = reinterpret_cast<Description_t>(kDescription);
    RowHit_t orig_RowHit = reinterpret_cast<RowHit_t>(kRowHit);

    const CreateText_t CreateText = reinterpret_cast<CreateText_t>(0x14077A110);
    const SetText_t SetText = reinterpret_cast<SetText_t>(0x14075E000);
    const Style_t StyleFor = reinterpret_cast<Style_t>(0x140869E80);
    const Move_t Move = reinterpret_cast<Move_t>(0x14075A300);
    const MoveY_t MoveY = reinterpret_cast<MoveY_t>(0x14075A310);
    const SetEnabled_t SetEnabled = reinterpret_cast<SetEnabled_t>(0x14075A490);
    const Changed_t SelectionChanged = reinterpret_cast<Changed_t>(0x140868710);
    const Events_t EventsA = reinterpret_cast<Events_t>(0x1408001B0);   // left / right pressed this frame (bits 4 / 8)
    const Events_t EventsB = reinterpret_cast<Events_t>(0x140800290);
    const Confirm_t Clicked = reinterpret_cast<Confirm_t>(0x1408006B0);  // the mask back when a press (mouse button or confirm) happened this frame
    const Inside_t MouseInside = reinterpret_cast<Inside_t>(0x1408506A0);
    const Hook_t UpdateDescription = reinterpret_cast<Hook_t>(kDescriptionUpdate);
    const Visible_t IsVisible = reinterpret_cast<Visible_t>(0x140759AB0);
    const Transform_t ToLocal = reinterpret_cast<Transform_t>(0x14075A070);          // a screen position in a widget's own space
    const CreateText_t CreateHitBox = reinterpret_cast<CreateText_t>(0x140776AC0);   // same arguments as the text one: template, entry, layout, node

    Entry g_Label = {};
    Entry g_Value = {};
    Entry g_Hit = {};
    char* g_Screen = nullptr;   // the screen the entries were built for

    int SelectedRow(char* screen)
    {
        return *reinterpret_cast<int*>(screen + 0x4B0);
    }

    SharedRef LayoutOf(char* screen)
    {
        SharedRef layout = { *reinterpret_cast<void**>(screen + 0x48), *reinterpret_cast<void**>(screen + 0x50) };
        if (layout.Control)
            _InterlockedIncrement(reinterpret_cast<volatile long*>(static_cast<char*>(layout.Control) + 8));   // the callee lets go of one reference
        return layout;
    }

    void Build(char* screen)
    {
        g_Label = {};
        g_Value = {};
        g_Hit = {};

        // The label: like Display Mode's (node 13), with our own text. A text id above 2214 is a pointer to the text.
        static const wchar_t* label = L"Plugins per page";
        uint8_t text[48];
        std::memcpy(text, reinterpret_cast<const void*>(kTextTemplate), sizeof(text));
        *reinterpret_cast<float*>(text + 0) = 960.0f;
        *reinterpret_cast<float*>(text + 4) = kLabelY;
        *reinterpret_cast<int32_t*>(text + 8) = 0x22;
        *reinterpret_cast<int64_t*>(text + 24) = reinterpret_cast<int64_t>(label);
        *reinterpret_cast<float*>(text + 32) = 1550.0f;
        SharedRef layout = LayoutOf(screen);
        CreateText(text, &g_Label, &layout, 13);

        // The value: like Display Mode's (node 8).
        uint8_t value[48] = {};
        *reinterpret_cast<float*>(value) = 960.0f;
        *reinterpret_cast<float*>(value + 4) = kValueY;
        *reinterpret_cast<int32_t*>(value + 8) = 0x12;
        *reinterpret_cast<int32_t*>(value + 12) = 9;
        *reinterpret_cast<int32_t*>(value + 16) = *reinterpret_cast<const int32_t*>(kTemplateColor);
        layout = LayoutOf(screen);
        CreateText(value, &g_Value, &layout, 8);

        // The box behind the value, the same one Display Mode has (it is also what the game hovers Display Mode with): at the value's height, unchanged in size.
        uint8_t hit[48];
        std::memcpy(hit, reinterpret_cast<const void*>(kHitTemplate), sizeof(hit));
        *reinterpret_cast<float*>(hit + 12) = kValueY;
        layout = LayoutOf(screen);
        CreateHitBox(hit, &g_Hit, &layout, 7);

        g_Screen = screen;
    }

    // The game's own rows move up so the three are closer together: Resolution (label, box and texts) a little, Display Mode (label, hover box, value) more.
    void Raise(char* screen)
    {
        struct Shift { int Offset; float Up; };
        static const Shift entries[] =
        {
            { 0x2B0, kRaiseResolution }, { 0x330, kRaiseResolution }, { 0x350, kRaiseResolution }, { 0x370, kRaiseResolution }, { 0x390, kRaiseResolution },
            { 0x2D0, kRaiseMode }, { 0x3B0, kRaiseMode }, { 0x3D0, kRaiseMode },
        };
        for (const Shift& entry : entries)
        {
            if (char* element = *reinterpret_cast<char**>(screen + entry.Offset))
                MoveY(element, *reinterpret_cast<float*>(element + 92) - entry.Up);
        }
    }

    // The mouse over one of our hover boxes, tested the way the game tests its own (the box's rectangle, in the box's own space).
    bool OverBoxWidget(const Entry& box, int64_t mouse)
    {
        if (!box.Part[0] || !box.Part[2] || !IsVisible(box.Part[0]))
            return false;
        // The mouse is two ints (pixels in the 1920 x 1080 layout space); the game turns them into floats before it moves them into a widget's space.
        const float asFloats[2] = { static_cast<float>(static_cast<int32_t>(mouse)), static_cast<float>(static_cast<int32_t>(mouse >> 32)) };
        int64_t packed;
        std::memcpy(&packed, asFloats, sizeof(packed));
        float local[2] = {};
        ToLocal(box.Part[0], local, packed);
        const float* rectangle = static_cast<const float*>(box.Part[2]);
        return local[0] >= rectangle[7] && local[0] <= rectangle[9] && local[1] >= rectangle[8] && local[1] <= rectangle[10];
    }

    // The same test from the numbers alone: the mouse as two ints in the 1920 x 1080 space the layout uses, against where the box and label are drawn.
    bool OverBoxGeometry(int64_t mouse)
    {
        const float x = static_cast<float>(static_cast<int32_t>(mouse));
        const float y = static_cast<float>(static_cast<int32_t>(mouse >> 32));
        return x >= 960.0f - 237.0f && x <= 960.0f + 237.0f && y >= kLabelY - 30.0f && y <= kValueY + 50.0f;
    }

    bool OverBox(const Entry& box, int64_t mouse)
    {
        return OverBoxWidget(box, mouse) || OverBoxGeometry(mouse);
    }

    void __fastcall Hook_OnEnter(char* screen)
    {
        orig_OnEnter(screen);
        Raise(screen);
        Build(screen);
    }

    // The mouse over the new row selects it, like it does the game's own rows.
    int64_t __fastcall Hook_RowHit(char* screen, int64_t mouse)
    {
        const int64_t row = orig_RowHit(screen, mouse);
        // -1 is "no row"; -8 is the Back area, which does not cover the new row either.
        if ((row == -1 || row == -8) && screen == g_Screen && OverBox(g_Hit, mouse))
            return kRow;
        return row;
    }

    // After the game's own pass (which read row 4 from four row tables: it only disabled the arrows and moved them to a meaningless place).
    void __fastcall Hook_UpdateVisuals(char* screen)
    {
        orig_UpdateVisuals(screen);
        if (screen != g_Screen || !g_Label.Part[2] || !g_Value.Part[2])
            return;

        // Apply and Back make room for the new row.
        if (char* buttons = *reinterpret_cast<char**>(screen + 0x4F0))
        {
            if (void* apply = *reinterpret_cast<void**>(buttons + 16))
                MoveY(apply, kApplyY);
            if (void* back = *reinterpret_cast<void**>(buttons + 424 + 16))
                MoveY(back, kBackY);
        }

        const bool selected = SelectedRow(screen) == kRow;

        char* label = static_cast<char*>(g_Label.Part[2]);
        const int style = static_cast<int>(StyleFor(selected));
        if (*reinterpret_cast<int*>(label + 160) != style)
        {
            *reinterpret_cast<int*>(label + 160) = style;
            *reinterpret_cast<int*>(label + 344) = 0;
        }

        static const wchar_t* numbers[] = { L"3", L"4", L"5" };
        const int count = PluginMenu::PerPage();
        SetText(g_Value.Part[2], reinterpret_cast<int64_t>(numbers[count - kMin]));

        if (selected)
        {
            void* left = *reinterpret_cast<void**>(screen + 0x3F0);
            void* right = *reinterpret_cast<void**>(screen + 0x410);
            if (left && right)
            {
                SetEnabled(left, 1);
                SetEnabled(right, 1);
                Move(left, kArrowLeftX, kValueY);
                Move(right, kArrowRightX, kValueY);
            }
        }
    }

    // Up / down follow the drawn order instead of the game's index order. The game's own function is used for everything else.
    int64_t __fastcall Hook_Nav(char* selection, int mask)
    {
        if (g_Screen && selection == g_Screen + 0x4B0)
        {
            const bool up = (mask & *reinterpret_cast<int*>(selection + 28)) != 0;
            const bool down = !up && (mask & *reinterpret_cast<int*>(selection + 32)) != 0;
            if (up || down)
            {
                int position = 0;
                for (int i = 0; i < 5; ++i)
                {
                    if (kDrawnOrder[i] == *reinterpret_cast<int*>(selection))
                        position = i;
                }
                position += down ? 1 : -1;
                if (position < 0 || position > 4)
                    return 2;   // the end of the list, as the game answers

                *reinterpret_cast<int*>(selection) = kDrawnOrder[position];
                SelectionChanged(selection);
                *reinterpret_cast<uint8_t*>(selection + 16) = 1;
                YGO::RIX::PlayUISound(*reinterpret_cast<int*>(selection + 20));
                return 1;
            }
        }
        return orig_Nav(selection, mask);
    }

    // The description under the selected row: the game reads the text from a four row table, so the new row gives its own.
    void __fastcall Hook_Description(void* description, const wchar_t* text)
    {
        static const wchar_t* help = L"How many plugins the plugin list shows on one page.";
        if (g_Screen && description == g_Screen + 264 && SelectedRow(g_Screen) == kRow)
            text = help;
        orig_Description(description, text);
    }

    // Left / right on the new row. The game ignores them there, so they are read here, before its own pass.
    void __fastcall Hook_Update(char* screen)
    {
        // Hover, a second way besides the game's own row test (which is also hooked): while the mouse moves, over the new row selects it.
        if (screen == g_Screen && *reinterpret_cast<const uint8_t*>(kMouse + 8) != 0)
        {
            const int64_t mouse = *reinterpret_cast<const int64_t*>(kMouse);
            const bool widget = OverBoxWidget(g_Hit, mouse);
            const bool geometry = OverBoxGeometry(mouse);
            if ((widget || geometry) && SelectedRow(screen) != kRow)
            {
                char* selection = screen + 0x4B0;
                *reinterpret_cast<int*>(selection) = kRow;
                SelectionChanged(selection);
                *reinterpret_cast<uint8_t*>(selection + 16) = 1;
                YGO::RIX::PlayUISound(*reinterpret_cast<int*>(selection + 20));
                UpdateDescription(screen);
            }
        }

        if (screen == g_Screen && SelectedRow(screen) == kRow)
        {
            int64_t pressed = EventsA(YGO::RIX::InputState) | EventsB(YGO::RIX::InputState);
            int count = PluginMenu::PerPage();
            // The mouse: the arrows step down / up, the value and label step up. (The game handles clicks on its own rows' arrows itself.)
            const int64_t mouse = *reinterpret_cast<const int64_t*>(kMouse);
            if (Clicked(YGO::RIX::InputState, 1))
            {
                if (MouseInside(screen + 0x3F0, mouse))
                    pressed |= 4;
                else if (MouseInside(screen + 0x410, mouse) || OverBox(g_Hit, mouse))
                    pressed |= 8;
            }

            if (pressed & 4)
                count = count <= kMin ? kMax : count - 1;
            else if (pressed & 8)
                count = count >= kMax ? kMin : count + 1;

            if (count != PluginMenu::PerPage())
            {
                PluginMenu::SetPerPage(count);
                YGO::RIX::PlayUISound(16);
            }
        }
        orig_Update(screen);
    }
}

namespace VideoScreen
{
    void Install()
    {
        // The row count: `mov edx, 4` (BA 04 00 00 00); the same register is the page size argument.
        uint8_t* patch = reinterpret_cast<uint8_t*>(kRowCountPatch);
        if (patch[0] != 0xBA || patch[1] != 4)
        {
            Logger::WriteLog("Video settings: the game's code is not what was expected, the extra row was not added", MODULE_NAME, 1);
            return;
        }
        DWORD old;
        VirtualProtect(patch, 5, PAGE_EXECUTE_READWRITE, &old);
        patch[1] = kRow + 1;
        VirtualProtect(patch, 5, old, &old);

        // The arrows follow Resolution (330) and Display Mode (570) up with their rows; only touched when the table is what was expected.
        float* arrowY = reinterpret_cast<float*>(kArrowYTable);
        if (arrowY[0] == 330.0f && arrowY[12] == 570.0f)
        {
            DWORD protect;
            VirtualProtect(arrowY, 13 * sizeof(float), PAGE_READWRITE, &protect);
            arrowY[0] -= kRaiseResolution;
            arrowY[12] -= kRaiseMode;
            VirtualProtect(arrowY, 13 * sizeof(float), protect, &protect);
        }

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_RowHit, Hook_RowHit);
        DetourAttach(&(PVOID&)orig_OnEnter, Hook_OnEnter);
        DetourAttach(&(PVOID&)orig_Update, Hook_Update);
        DetourAttach(&(PVOID&)orig_UpdateVisuals, Hook_UpdateVisuals);
        DetourAttach(&(PVOID&)orig_Nav, Hook_Nav);
        DetourAttach(&(PVOID&)orig_Description, Hook_Description);
        const LONG result = DetourTransactionCommit();
        Logger::WriteLog(std::format("Video settings hooks: {}", result), MODULE_NAME, result == 0 ? 0 : 2);
    }
}
