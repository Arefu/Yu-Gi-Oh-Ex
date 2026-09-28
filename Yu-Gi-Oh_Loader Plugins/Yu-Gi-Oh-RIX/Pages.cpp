#define RIX_EXPORTS   // calls RIX_GotoScreen from Api.cpp
#include "Pages.h"

#include <Windows.h>
#include <detours.h>
#include <cstdint>
#include <format>
#include <memory>
#include <string>
#include <vector>

#include "Logger.h"
#include "MainMenu.h"
#include "YuGiOh/YuGiOh-RIX.h"

// Traced in YuGiOh.exe.i64 (RIX::ScreenBattlePackStore, vftable 0x140A72030; the constructor's comment has the layout):
//   +264 help bar, +7688 the MenuKit menu (definition g_BattlePackMenuItemTable), +7856 the state (0 = top level), +7860 the item pressed.
// A MenuKit item is 480 bytes (items vector at menu + 128): +48 id (int), +56 label, +96 description (string id or raw wchar_t*).
// Everything here runs on the game's thread (the hooks, and the callbacks that open or close pages), so there is no lock.
namespace
{
    constexpr uintptr_t kVftable = 0x140A72030;
    constexpr uintptr_t kOnEnter = 0x14082B060;       // RIX::ScreenBattlePackStore::OnEnter(screen, ui)
    constexpr uintptr_t kHandleItem = 0x140828710;    // RIX::ScreenBattlePackStore::HandleItem(screen, id, fromInput)
    constexpr uintptr_t kUpdate = 0x14082A890;        // RIX::ScreenBattlePackStore::Update(screen, ui, seconds)
    constexpr uintptr_t kSetHeaderText = 0x140822F00; // RIX::Screen::SetHeaderText(screen, text), shared by every screen
    constexpr uintptr_t kHelpSelect = 0x140A7A1A0;    // the two help bar prompts the screen's top level shows
    constexpr uintptr_t kHelpBack = 0x140A7A190;
    constexpr int64_t kHeaderText = 475;              // "Battle Pack"
    constexpr int kHelp = 264;
    constexpr int kMenu = 7688;
    constexpr int kState = 7856;
    constexpr int kPendingItem = 7860;
    constexpr int kItemSize = 480;
    constexpr int kMenuStart = 7704;                  // menu + 16: the first item's y (negative = centre the items on minus it)
    constexpr int kMenuSpacing = 7708;                // menu + 20
    constexpr float kCentredStart = -540.0f;          // what OnEnter sets for the top level
    constexpr float kSpacing = 100.0f;
    constexpr int kMenuNode = 7840;                   // the menu's root node (its x is what OnEnter sets to 960)
    constexpr float kCentreX = 960.0f;
    constexpr int kItems[RIX_PAGE_MAX_BUTTONS] = { 1, 2, 10, 8 }; // menu items used for page buttons, top to bottom (1 and 2 are the top level's own)
    constexpr int kBack = -2;
    constexpr int kCancel = 0x2000;

    struct Button
    {
        std::wstring Label;
        std::wstring Description;
        RIX_ButtonCallback OnPress = nullptr;
        void* User = nullptr;
    };

    struct Page
    {
        std::wstring Header;
        int ButtonCount = 0;
        Button Buttons[RIX_PAGE_MAX_BUTTONS];
        RIX_PageCallback OnShow = nullptr;
        RIX_PageCallback OnHide = nullptr;
        RIX_PageFrameCallback OnFrame = nullptr;
        void* User = nullptr;
        float ButtonsY = 0.0f;
        float ButtonsX = 0.0f;
    };

    std::vector<std::unique_ptr<Page>> g_Stack;     // the open pages, top last
    std::vector<std::unique_ptr<Page>> g_Retired;   // closed pages: the menu items may still point at their text
    char* g_Screen = nullptr;                       // the Battle Pack screen, once seen

    // The game's own label / description of the items we relabel, read before the first relabel.
    bool g_Relabelled = false;
    int64_t g_GameText[RIX_PAGE_MAX_BUTTONS][2] = {};

    using OnEnter_t = void(__fastcall*)(char*, int64_t);
    using HandleItem_t = void(__fastcall*)(char*, int, char);
    using Update_t = void(__fastcall*)(char*, int64_t, double);
    using SetHeaderText_t = void(__fastcall*)(char*, int64_t);

    OnEnter_t orig_OnEnter = reinterpret_cast<OnEnter_t>(kOnEnter);
    HandleItem_t orig_HandleItem = reinterpret_cast<HandleItem_t>(kHandleItem);
    Update_t orig_Update = reinterpret_cast<Update_t>(kUpdate);
    SetHeaderText_t orig_SetHeaderText = reinterpret_cast<SetHeaderText_t>(kSetHeaderText);

    Page* Top()
    {
        return g_Stack.empty() ? nullptr : g_Stack.back().get();
    }

    char* FindItem(char* screen, int id)
    {
        char* menu = screen + kMenu;
        char* begin = *reinterpret_cast<char**>(menu + 128);
        char* end = *reinterpret_cast<char**>(menu + 136);
        for (char* item = begin; item && item < end; item += kItemSize)
        {
            if (*reinterpret_cast<int*>(item + 48) == id)
                return item;
        }
        return nullptr;
    }

    // The page's text on the items it uses, or the game's text back on all of them (page null).
    void Relabel(char* screen, const Page* page)
    {
        for (int i = 0; i < RIX_PAGE_MAX_BUTTONS; ++i)
        {
            char* item = FindItem(screen, kItems[i]);
            if (!item)
                continue;
            auto* label = reinterpret_cast<int64_t*>(item + 56);
            auto* description = reinterpret_cast<int64_t*>(item + 96);
            if (!g_Relabelled)
            {
                g_GameText[i][0] = *label;
                g_GameText[i][1] = *description;
            }
            if (page && i < page->ButtonCount)
            {
                *label = reinterpret_cast<int64_t>(page->Buttons[i].Label.c_str());
                *description = reinterpret_cast<int64_t>(page->Buttons[i].Description.c_str());
            }
            else if (g_Relabelled)
            {
                *label = g_GameText[i][0];
                *description = g_GameText[i][1];
            }
        }
        if (page)
            g_Relabelled = true;
    }

    void RunPageCallback(RIX_PageCallback callback, const Page* page)
    {
        if (!callback || !g_Screen)
            return;
        Menu::SetCallbackSource(g_Screen);
        callback(g_Screen, page->User);
        Menu::SetCallbackSource(nullptr);
    }

    // Shows the top page on the screen: header, menu (or no menu), help bar, then the page's OnShow.
    void Apply(char* screen)
    {
        Page* page = Top();
        if (!page)
            return;

        orig_SetHeaderText(screen, reinterpret_cast<int64_t>(page->Header.c_str()));   // SetTextById shows any value above 2214 as a wchar_t*
        Relabel(screen, page);

        *reinterpret_cast<float*>(screen + kMenuStart) = page->ButtonsY > 0.0f ? page->ButtonsY : kCentredStart;
        *reinterpret_cast<float*>(screen + kMenuSpacing) = kSpacing;
        if (void* node = *reinterpret_cast<void**>(screen + kMenuNode))
            YGO::RIX::NodeSetX(node, page->ButtonsX > 0.0f ? page->ButtonsX : kCentreX);

        void* menu = screen + kMenu;
        YGO::RIX::MenuKit::ClearVisible(menu);
        for (int i = 0; i < page->ButtonCount; ++i)
            YGO::RIX::MenuKit::ShowItemNow(menu, kItems[i]);
        YGO::RIX::MenuKit::Layout(menu);
        if (page->ButtonCount > 0)
        {
            YGO::RIX::MenuKit::SelectIndex(menu, 0);
            YGO::RIX::MenuKit::ResetItems(menu);
        }

        void* help = screen + kHelp;
        YGO::RIX::HelpClear(help);
        if (page->ButtonCount > 0)
        {
            YGO::RIX::HelpAdd(help, reinterpret_cast<const YGO::RIX::HelpEntry*>(kHelpSelect), 1);
            YGO::RIX::HelpAdd(help, reinterpret_cast<const YGO::RIX::HelpEntry*>(kHelpBack), 1);
        }
        else
        {
            static const YGO::RIX::HelpEntry back = { kCancel, 1, 880 };   // the game's own "Back" prompt
            YGO::RIX::HelpAdd(help, &back, 1);
        }
        YGO::RIX::HelpLayout(help);

        *reinterpret_cast<int*>(screen + kPendingItem) = 0;
        RunPageCallback(page->OnShow, page);
    }

    // Back: the top page goes; the one under it shows again, or the screen is left when it was the last.
    void CloseTop(char* screen, bool leaveScreen)
    {
        if (g_Stack.empty())
            return;

        Page* page = Top();
        RunPageCallback(page->OnHide, page);
        g_Retired.push_back(std::move(g_Stack.back()));
        g_Stack.pop_back();

        YGO::RIX::PlayUISound(18);
        if (!g_Stack.empty())
        {
            Apply(screen);
            return;
        }
        if (leaveScreen)
            YGO::RIX::GoBack(screen);
    }

    void __fastcall Hook_OnEnter(char* screen, int64_t ui)
    {
        g_Screen = screen;
        const bool ours = !g_Stack.empty();
        if (!ours)
            Relabel(screen, nullptr);

        // OnEnter only shows the top level when it was entered from the main menu (or a game result); from anywhere else it reopens the pack
        // details. Pages always want the top level (the screen being left is one a page button opened), so the screen record's previous
        // screen id (RIX::UI::GetPreviousScreenId) reads "main menu" for the call.
        int* previous = nullptr;
        int saved = 0;
        if (ours)
        {
            auto records = *reinterpret_cast<char**>(ui + 856);
            int index = *reinterpret_cast<int*>(ui + 852);
            if (records)
            {
                previous = reinterpret_cast<int*>(records + 48LL * index + 4);
                saved = *previous;
                *previous = RIX_SCREEN_MAIN_MENU;
            }
        }

        orig_OnEnter(screen, ui);

        if (previous)
            *previous = saved;
        if (ours)
            Apply(screen);
    }

    void __fastcall Hook_SetHeaderText(char* screen, int64_t text)
    {
        Page* page = Top();
        if (page && text == kHeaderText && *reinterpret_cast<uintptr_t*>(screen) == kVftable)
            text = reinterpret_cast<int64_t>(page->Header.c_str());
        orig_SetHeaderText(screen, text);
    }

    void __fastcall Hook_HandleItem(char* screen, int id, char fromInput)
    {
        Page* page = Top();
        if (!page || *reinterpret_cast<int*>(screen + kState) != 0)
        {
            orig_HandleItem(screen, id, fromInput);
            return;
        }

        if (id == kBack)
        {
            // The last page leaves through the game's own Back (sound, GoBack); an inner one closes in place.
            if (g_Stack.size() > 1)
                CloseTop(screen, false);
            else
            {
                RunPageCallback(page->OnHide, page);
                g_Retired.push_back(std::move(g_Stack.back()));
                g_Stack.pop_back();
                orig_HandleItem(screen, id, fromInput);
            }
            *reinterpret_cast<int*>(screen + kPendingItem) = 0;
            return;
        }

        int index = -1;
        for (int i = 0; i < page->ButtonCount; ++i)
        {
            if (id == kItems[i])
                index = i;
        }
        if (index < 0)
        {
            orig_HandleItem(screen, id, fromInput);
            return;
        }

        // The game's cases clear the pending item when they are done, or Update presses it again every frame.
        *reinterpret_cast<int*>(screen + kPendingItem) = 0;
        YGO::RIX::PlayUISound(39);
        const Button button = page->Buttons[index];   // the callback may open or close pages
        if (button.OnPress)
        {
            Menu::SetCallbackSource(screen);   // RIX_GotoScreen from the callback changes screen FROM here, so Back comes back here
            button.OnPress(index, button.User);
            Menu::SetCallbackSource(nullptr);
        }
    }

    // Pages without buttons: the screen's own update (its menu) is not run; RIX reads the input, closes the page on cancel and the page gets
    // the rest. Pages with buttons and an OnFrame: OnFrame first (without cancel), then the screen's update runs the buttons and Back.
    void __fastcall Hook_Update(char* screen, int64_t ui, double seconds)
    {
        Page* page = Top();
        if (!page || *reinterpret_cast<int*>(screen + kState) != 0 || (page->ButtonCount > 0 && !page->OnFrame))
        {
            orig_Update(screen, ui, seconds);
            return;
        }

        void* input = YGO::RIX::InputState;
        int pressed = YGO::RIX::Input::GetPressed(input) | YGO::RIX::Input::GetRepeat(input) | YGO::RIX::Input::HelpBarPressed(screen + kHelp);
        const int held = YGO::RIX::Input::GetHeld(input);
        if (YGO::RIX::InputCancelPressed(input, kCancel))
            pressed |= kCancel;

        const bool withButtons = page->ButtonCount > 0;
        if ((pressed & kCancel) && !withButtons)
        {
            CloseTop(screen, true);
            return;
        }

        if (page->OnFrame && !(pressed & kCancel))
        {
            const RIX_PageFrameCallback frame = page->OnFrame;
            void* user = page->User;
            Menu::SetCallbackSource(screen);
            frame(screen, pressed, held, static_cast<float>(seconds), user);
            Menu::SetCallbackSource(nullptr);
        }

        if (withButtons && Top() == page)
            orig_Update(screen, ui, seconds);
    }
}

namespace Pages
{
    bool Open(const RIX_PageDesc& desc)
    {
        if (desc.Size < sizeof(RIX_PageDesc) || !desc.Header || desc.ButtonCount < 0 || desc.ButtonCount > RIX_PAGE_MAX_BUTTONS)
            return false;

        auto page = std::make_unique<Page>();
        page->Header = desc.Header;
        page->ButtonCount = desc.ButtonCount;
        for (int i = 0; i < desc.ButtonCount; ++i)
        {
            const RIX_PageButton& from = desc.Buttons[i];
            page->Buttons[i].Label = from.Label ? from.Label : L"";
            page->Buttons[i].Description = from.Description ? from.Description : L"";
            page->Buttons[i].OnPress = from.OnPress;
            page->Buttons[i].User = from.User;
        }
        page->OnShow = desc.OnShow;
        page->OnHide = desc.OnHide;
        page->OnFrame = desc.OnFrame;
        page->User = desc.User;
        page->ButtonsY = desc.ButtonsY;
        page->ButtonsX = desc.ButtonsX;

        // From a page (its button or frame callback): on top of it, in place.
        if (g_Screen && !g_Stack.empty() && Menu::CallbackSource() == g_Screen)
        {
            Page* below = Top();
            RunPageCallback(below->OnHide, below);
            g_Stack.push_back(std::move(page));
            Apply(g_Screen);
            return true;
        }

        // From anywhere else: the screen is entered, and OnEnter shows the page.
        for (auto& stale : g_Stack)
            g_Retired.push_back(std::move(stale));
        g_Stack.clear();
        g_Stack.push_back(std::move(page));
        if (RIX_GotoScreen(RIX_SCREEN_BATTLEPACK))
            return true;

        g_Retired.push_back(std::move(g_Stack.back()));
        g_Stack.clear();
        return false;
    }

    bool Close()
    {
        if (g_Stack.empty() || !g_Screen)
            return false;
        CloseTop(g_Screen, true);
        return true;
    }

    void Install()
    {
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_OnEnter, Hook_OnEnter);
        DetourAttach(&(PVOID&)orig_HandleItem, Hook_HandleItem);
        DetourAttach(&(PVOID&)orig_Update, Hook_Update);
        DetourAttach(&(PVOID&)orig_SetHeaderText, Hook_SetHeaderText);
        LONG result = DetourTransactionCommit();
        Logger::WriteLog(std::format("Page hooks: {}", result), MODULE_NAME, result == 0 ? 0 : 2);
    }
}
