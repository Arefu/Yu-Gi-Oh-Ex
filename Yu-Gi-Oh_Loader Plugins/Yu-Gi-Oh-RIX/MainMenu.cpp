#include "MainMenu.h"

#include <Windows.h>
#include <detours.h>
#include <intrin.h>
#include <algorithm>
#include <cstddef>
#include <cstring>
#include <deque>
#include <format>
#include <mutex>
#include <vector>

#include "Logger.h"
#include "YuGiOh/YuGiOh-RIX.h"
#include "YuGiOh/YuGiOh-SAVE.h"

using namespace YGO::RIX;

namespace
{
    constexpr int kScreenMainMenu = 8;         // RIX_SCREEN_MAIN_MENU
    constexpr size_t kMaxMainButtons = 80;     // ids 13..92; the widget loop bound is a byte
    constexpr int kOptionsFirstId = 100;       // options item ids: 100 and up (the game's own are 0..8)
    constexpr size_t kMaxOptionButtons = 100;
    constexpr int kOptionsSkinBase = 2;        // menu items use artwork id (index + 2)
    constexpr int kOptionsSkinLast = HelpMenu::VanillaItemCount - 1 + kOptionsSkinBase;

    struct Entry
    {
        std::wstring Label;
        std::wstring Description;
        int Page = RIX_PAGE_MAIN;
        int Skin = RIX_ITEM_DECK_EDITOR;
        RIX_ButtonCallback OnPress = nullptr;
        void* User = nullptr;
        bool Active = true;
        bool Pinned = false;                 // stays visible while the menu is exclusive (see SetExclusive)
    };

    // The menus read label and description through raw pointers on the game's thread, so a string is never freed while it could be in
    // use: an update moves the old one here and keeps it.
    std::deque<std::wstring> g_Retired;
    std::deque<Entry> g_Main;                // entry i is main menu button id 13 + i; entries are never erased so ids stay valid
    std::deque<Entry> g_Options;             // entry i is options button id 100 + i
    std::mutex g_Lock;
    bool g_Dirty = false;                    // a change is waiting for the next menu frame
    int g_PendingPress = -1;                 // a vanilla item somebody asked to press
    DWORD g_PendingPressTime = 0;            // when (GetTickCount); a press nobody picked up within 2 seconds is dropped
    ScreenMainMenu* g_Screen = nullptr;
    DWORD g_LastUpdate = 0;                  // GetTickCount() of the main menu's last Update
    thread_local void* g_CallbackSource = nullptr;
    bool g_Installed = false;
    std::vector<MainMenuItemDef> g_HelpTable;

    // Changes to the game's own main menu buttons (a menu file's "edit" list).
    struct VanillaEdit
    {
        std::wstring Label, Description;
        bool HasLabel = false, HasDescription = false, Hidden = false;
    };
    VanillaEdit g_Vanilla[MMI_VANILLA_COUNT];
    bool g_HasVanillaEdits = false;

    // A new action for one of the game's own buttons (RIX_SetMainMenuItemAction).
    struct VanillaAction
    {
        RIX_ButtonCallback OnPress = nullptr;
        void* User = nullptr;
    };
    VanillaAction g_VanillaActions[MMI_VANILLA_COUNT];

    void (*g_FrameCallback)() = nullptr;     // run on every frame of the main menu, on the game's thread
    void (*g_BuildCallback)() = nullptr;     // run once, just before the main menu is first extended (buttons added by it are in the first build)
    bool g_BuildCallbackRan = false;
    void (*g_ReentryCallback)() = nullptr;   // run when the main menu is shown again after a screen change that left exclusive mode
    bool g_ReentryPending = false;

    // Exclusive mode: only the pinned buttons are listed, the pages as they were are kept in g_SavedPages and put back on leaving.
    bool g_ExclusiveWanted = false;
    bool g_Exclusive = false;
    std::vector<int> g_SavedPages[MMP_COUNT];

    using SetupArrays_t = void(__fastcall*)(ScreenMainMenu*);
    using LoadScreenTable_t = int64_t(__fastcall*)(ScreenMainMenu*);
    using ActivateItem_t = void(__fastcall*)(ScreenMainMenu*, int, char);
    using Update_t = void(__fastcall*)(ScreenMainMenu*);
    using CreateFromLayout_t = void(__fastcall*)(void*, void*, int, int64_t, float, float, int64_t);
    using SetDefinition_t = int64_t(__fastcall*)(void*, const MainMenuItemDef*);
    using ShowItem_t = uint64_t(__fastcall*)(void*, unsigned int);
    using HelpActivated_t = void(__fastcall*)(void*, int64_t, int, unsigned int);

    SetupArrays_t orig_SetupArrays = reinterpret_cast<SetupArrays_t>(MainMenu::SetupArrays);
    LoadScreenTable_t orig_LoadScreenTable = reinterpret_cast<LoadScreenTable_t>(MainMenu::LoadScreenTable);
    ActivateItem_t orig_ActivateItem = reinterpret_cast<ActivateItem_t>(MainMenu::ActivateItem);
    Update_t orig_Update = reinterpret_cast<Update_t>(MainMenu::Update);
    CreateFromLayout_t orig_CreateFromLayout = reinterpret_cast<CreateFromLayout_t>(MainMenu::CreateFromLayout);
    SetDefinition_t orig_SetDefinition = reinterpret_cast<SetDefinition_t>(MenuKit::SetDefinition);
    ShowItem_t orig_ShowItem = reinterpret_cast<ShowItem_t>(MenuKit::ShowItem);
    CreateFromLayout_t orig_ItemCreateFromLayout = reinterpret_cast<CreateFromLayout_t>(MenuKit::ItemCreateFromLayout);
    HelpActivated_t orig_HelpActivated = reinterpret_cast<HelpActivated_t>(HelpMenu::OnItemActivated);

    size_t ItemCount(const Vector& vector, size_t stride)
    {
        return (static_cast<uint8_t*>(vector.End) - static_cast<uint8_t*>(vector.Begin)) / stride;
    }

    void WriteBytes(uintptr_t address, const void* data, size_t size)
    {
        DWORD old;
        VirtualProtect(reinterpret_cast<void*>(address), size, PAGE_EXECUTE_READWRITE, &old);
        memcpy(reinterpret_cast<void*>(address), data, size);
        VirtualProtect(reinterpret_cast<void*>(address), size, old, &old);
    }

    // The widget creation loop of SetupWidgets ends with `cmp r15d, 13`: the number of buttons is that immediate.
    void PatchLoopCount()
    {
        uint8_t count = static_cast<uint8_t>(MMI_VANILLA_COUNT + g_Main.size());
        WriteBytes(MainMenu::SetupWidgetsLoopCount, &count, 1);
    }

    // ---------------------------------------------------------------- main menu

    void FillDefinitions(ScreenMainMenu* screen)
    {
        const size_t have = ItemCount(screen->Defs, sizeof(MainMenuItemDef));
        auto* defs = static_cast<MainMenuItemDef*>(screen->Defs.Begin);
        for (int item = 0; item < MMI_VANILLA_COUNT && static_cast<size_t>(item) < have; ++item)
        {
            if (g_Vanilla[item].HasLabel)
                defs[item].LabelText = reinterpret_cast<int64_t>(g_Vanilla[item].Label.c_str());
            if (g_Vanilla[item].HasDescription)
                defs[item].DescriptionText = reinterpret_cast<int64_t>(g_Vanilla[item].Description.c_str());
        }
        for (size_t i = 0; i < g_Main.size() && MMI_VANILLA_COUNT + i < have; ++i)
        {
            size_t id = MMI_VANILLA_COUNT + i;
            defs[id].Id = static_cast<int64_t>(id);
            // Any value above 2214 is used by SetTextById as a pointer to the text (see RIX::widget_Text::SetTextById).
            defs[id].LabelText = reinterpret_cast<int64_t>(g_Main[i].Label.c_str());
            defs[id].DescriptionText = reinterpret_cast<int64_t>(g_Main[i].Description.c_str());
        }
    }

    void RemoveFromPage(Vector& vector, int id)
    {
        auto* begin = static_cast<int*>(vector.Begin);
        auto* end = static_cast<int*>(vector.End);
        vector.End = std::remove(begin, end, id);
    }

    void AddToPage(Vector& vector, int id)
    {
        auto* begin = static_cast<int*>(vector.Begin);
        auto* end = static_cast<int*>(vector.End);
        if (std::find(begin, end, id) != end)
            return;

        if (vector.End != vector.Capacity)
        {
            *end = id;
            vector.End = end + 1;
        }
        else
            MainMenu::VectorIntEmplaceReallocate(&vector, end, &id);
    }

    // Makes the pages match the entries: every active button on its page, every removed one off all of them.
    void SyncPages(ScreenMainMenu* screen)
    {
        // A button added after the menu was built has no widget yet (the item and definition arrays are only sized when the menu is built),
        // so it stays off the pages until the next build: listing it would lay out and select a widget that does not exist.
        const size_t built = ItemCount(screen->Items, sizeof(WidgetItem));

        if (g_Exclusive)
        {
            for (int page = 0; page < MMP_COUNT; ++page)
                screen->Pages[page].End = screen->Pages[page].Begin;
            for (size_t i = 0; i < g_Main.size(); ++i)
            {
                if (g_Main[i].Active && g_Main[i].Pinned && MMI_VANILLA_COUNT + i < built)
                    AddToPage(screen->Pages[g_Main[i].Page], static_cast<int>(MMI_VANILLA_COUNT + i));
            }
            return;
        }

        for (int item = 0; item < MMI_VANILLA_COUNT; ++item)
        {
            if (!g_Vanilla[item].Hidden)
                continue;
            for (int page = 0; page < MMP_COUNT; ++page)
                RemoveFromPage(screen->Pages[page], item);
        }

        for (size_t i = 0; i < g_Main.size(); ++i)
        {
            const int id = static_cast<int>(MMI_VANILLA_COUNT + i);
            const Entry& entry = g_Main[i];
            for (int page = 0; page < MMP_COUNT; ++page)
            {
                if (entry.Active && page == entry.Page && static_cast<size_t>(id) < built)
                    AddToPage(screen->Pages[page], id);
                else
                    RemoveFromPage(screen->Pages[page], id);
            }
        }
    }

    // Grows the two vectors that are sized to the game's 13, then fills them.
    void Extend(ScreenMainMenu* screen)
    {
        if (g_Main.empty() && !g_HasVanillaEdits)
            return;

        const size_t total = MMI_VANILLA_COUNT + g_Main.size();
        if (ItemCount(screen->Items, sizeof(WidgetItem)) < total)
        {
            static const char zero = 0;
            MainMenu::ReallocateItems(&screen->Items, total, &zero);
        }
        if (ItemCount(screen->Defs, sizeof(MainMenuItemDef)) < total)
            MainMenu::ReallocateDefs(&screen->Defs, total);

        FillDefinitions(screen);
        SyncPages(screen);
    }

    void __fastcall Hook_SetupArrays(ScreenMainMenu* screen)
    {
        orig_SetupArrays(screen);

        // Plugins that add buttons start here the first time, so their buttons are part of the first build instead of arriving late.
        if (g_BuildCallback && !g_BuildCallbackRan)
        {
            g_BuildCallbackRan = true;
            g_BuildCallback();
        }

        std::lock_guard<std::mutex> guard(g_Lock);
        g_Screen = screen;
        Extend(screen);
    }

    // The table is copied into the definitions here; the extra entries are put back afterwards.
    int64_t __fastcall Hook_LoadScreenTable(ScreenMainMenu* screen)
    {
        int64_t result = orig_LoadScreenTable(screen);
        std::lock_guard<std::mutex> guard(g_Lock);
        FillDefinitions(screen);
        return result;
    }

    // Runs every frame of the main menu, on the game's thread: the place to apply changes other threads asked for.
    void __fastcall Hook_Update(ScreenMainMenu* screen)
    {
        // Leaving exclusive mode for a screen change: the pages are put back only when the main menu is next shown (it stops updating while
        // another screen is up), so the menu does not flash up during the fade out.
        bool reentered = false;
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            reentered = g_ReentryPending && g_LastUpdate != 0 && GetTickCount() - g_LastUpdate > 300;
        }
        if (reentered)
        {
            if (g_ReentryCallback)
                g_ReentryCallback();
            std::lock_guard<std::mutex> guard(g_Lock);
            g_ReentryPending = false;
            g_ExclusiveWanted = false;
            g_Dirty = true;
        }

        int press = -1;
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            g_Screen = screen;
            g_LastUpdate = GetTickCount();
            if (g_Dirty)
            {
                g_Dirty = false;
                if (ItemCount(screen->Defs, sizeof(MainMenuItemDef)) >= MMI_VANILLA_COUNT)
                {
                    if (g_ExclusiveWanted && !g_Exclusive)
                    {
                        for (int page = 0; page < MMP_COUNT; ++page)
                        {
                            const int* begin = static_cast<int*>(screen->Pages[page].Begin);
                            const int* end = static_cast<int*>(screen->Pages[page].End);
                            g_SavedPages[page].assign(begin, end);
                        }
                        g_Exclusive = true;
                    }
                    else if (!g_ExclusiveWanted && g_Exclusive)
                    {
                        g_Exclusive = false;
                        for (int page = 0; page < MMP_COUNT; ++page)
                        {
                            screen->Pages[page].End = screen->Pages[page].Begin;
                            for (int id : g_SavedPages[page])
                                AddToPage(screen->Pages[page], id);
                        }
                    }

                    FillDefinitions(screen);
                    SyncPages(screen);
                    MainMenu::LayoutPageNow(screen, false);
                }
            }
            press = g_PendingPress;
            g_PendingPress = -1;
            if (press >= 0 && GetTickCount() - g_PendingPressTime > 2000)
                press = -1;
        }

        if (g_FrameCallback)
            g_FrameCallback();

        if (press >= 0 && screen->PendingItem == -1)
            orig_ActivateItem(screen, press, 0);

        orig_Update(screen);
    }

    // Artwork ids for the widgets are item id + 3 (3..15). The extra buttons would ask for 16 and up, which do not exist, so they borrow one.
    void __fastcall Hook_CreateFromLayout(void* widget, void* resource, int skin, int64_t parent, float width, float height, int64_t screenData)
    {
        uintptr_t caller = reinterpret_cast<uintptr_t>(_ReturnAddress());
        if (caller >= MainMenu::SetupWidgetsBegin && caller < MainMenu::SetupWidgetsEnd && skin >= MMI_VANILLA_COUNT + 3)
        {
            size_t index = static_cast<size_t>(skin - (MMI_VANILLA_COUNT + 3));
            int borrowed = RIX_ITEM_DECK_EDITOR;
            {
                std::lock_guard<std::mutex> guard(g_Lock);
                if (index < g_Main.size())
                    borrowed = std::clamp(g_Main[index].Skin, 0, MMI_VANILLA_COUNT - 1);
            }
            skin = borrowed + 3;
        }
        orig_CreateFromLayout(widget, resource, skin, parent, width, height, screenData);
    }

    // The game's cases end by clearing the pending item and re-enabling the widgets; a custom item does the same or the menu locks up.
    void FinishPress(ScreenMainMenu* screen)
    {
        screen->PendingItem = -1;
        auto* lockObject = reinterpret_cast<char*>(screen) + 1184;
        (*reinterpret_cast<void(__fastcall***)(void*, int64_t)>(lockObject))[3](lockObject, 0);
        MainMenu::SetEnabled(*reinterpret_cast<void**>(reinterpret_cast<char*>(screen) + 1296), 1);
    }

    // ActivateItem checks these unlock bits (PlayerSection + 2964) itself and shows a "locked" message when one is missing.
    bool Unlocked(int item)
    {
        uint32_t bit = item == MMI_DUELIST_CHALLENGE ? 1 : item == MMI_BATTLE_PACK ? 2 : item == MMI_CARD_SHOP ? 4 : 0;
        if (!bit)
            return true;
        uint8_t* section = YGO::SAVE::Get_PlayerSection(YGO::SAVE::CURRENT_PROFILE);
        return section && (*reinterpret_cast<uint32_t*>(section + YGO::SAVE::PlayerSection::MenuUnlockFlags) & bit) != 0;
    }

    void __fastcall Hook_ActivateItem(ScreenMainMenu* screen, int item, char fromInput)
    {
        if (item >= 0 && item < MMI_VANILLA_COUNT)
        {
            VanillaAction action;
            {
                std::lock_guard<std::mutex> guard(g_Lock);
                action = g_VanillaActions[item];
            }
            // A locked button is left to the game, which shows its "locked" message.
            if (!action.OnPress || !Unlocked(item))
            {
                orig_ActivateItem(screen, item, fromInput);
                return;
            }

            YGO::RIX::PlayUISound(39);
            g_CallbackSource = screen;
            action.OnPress(item, action.User);
            g_CallbackSource = nullptr;
            FinishPress(screen);
            return;
        }
        if (item < MMI_VANILLA_COUNT)
        {
            orig_ActivateItem(screen, item, fromInput);
            return;
        }

        RIX_ButtonCallback callback = nullptr;
        void* user = nullptr;
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            size_t index = static_cast<size_t>(item - MMI_VANILLA_COUNT);
            if (index < g_Main.size() && g_Main[index].Active)
            {
                callback = g_Main[index].OnPress;
                user = g_Main[index].User;
            }
        }

        YGO::RIX::PlayUISound(39);
        if (callback)
        {
            g_CallbackSource = screen;
            callback(item, user);
            g_CallbackSource = nullptr;
        }
        FinishPress(screen);
    }

    // ---------------------------------------------------------------- options menu (ScreenHelp, a MenuKit menu)
    //  - LoadGraphics calls menu::SetDefinition with g_HelpMenuItemTable; a copy of it with the extra items is passed instead.
    //  - OnEnter shows the buttons one by one with menu::ShowItem, Back (7) last; the extra ones are shown just before it.
    //  - OnItemActivated is a switch on the id.
    //  - Each item is created with artwork (index + 2); the extra ones borrow the artwork of one of the game's.

    int64_t __fastcall Hook_SetDefinition(void* menu, const MainMenuItemDef* table)
    {
        if (reinterpret_cast<uintptr_t>(table) != HelpMenu::ItemTable)
            return orig_SetDefinition(menu, table);

        std::lock_guard<std::mutex> guard(g_Lock);
        g_HelpTable.assign(table, table + HelpMenu::VanillaItemCount);
        for (size_t i = 0; i < g_Options.size(); ++i)
        {
            if (!g_Options[i].Active)
                continue;
            g_HelpTable.push_back({ kOptionsFirstId + static_cast<int64_t>(i),
                reinterpret_cast<int64_t>(g_Options[i].Label.c_str()), reinterpret_cast<int64_t>(g_Options[i].Description.c_str()) });
        }
        g_HelpTable.push_back({ -1, 0, 0 });
        return orig_SetDefinition(menu, g_HelpTable.data());
    }

    uint64_t __fastcall Hook_ShowItem(void* menu, unsigned int id)
    {
        uintptr_t caller = reinterpret_cast<uintptr_t>(_ReturnAddress());
        if (id == HelpMenu::BackItem && caller >= HelpMenu::OnEnter && caller < HelpMenu::OnEnterEnd)
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            for (size_t i = 0; i < g_Options.size(); ++i)
            {
                if (g_Options[i].Active)
                    orig_ShowItem(menu, static_cast<unsigned int>(kOptionsFirstId + i));
            }
        }
        return orig_ShowItem(menu, id);
    }

    void __fastcall Hook_ItemCreateFromLayout(void* item, void* resource, int skin, int64_t parent, float width, float height, int64_t screenData)
    {
        uintptr_t caller = reinterpret_cast<uintptr_t>(_ReturnAddress());
        if (caller >= MenuKit::CreateItems && caller < MenuKit::CreateItemsEnd)
        {
            int id = *reinterpret_cast<int*>(static_cast<char*>(item) + 48);
            size_t index = static_cast<size_t>(id - kOptionsFirstId);
            if (id >= kOptionsFirstId)
            {
                int borrowed = 0;
                {
                    std::lock_guard<std::mutex> guard(g_Lock);
                    if (index < g_Options.size())
                        borrowed = std::clamp(g_Options[index].Skin, 0, HelpMenu::VanillaItemCount - 1);
                }
                skin = borrowed + kOptionsSkinBase;
            }
        }
        orig_ItemCreateFromLayout(item, resource, skin, parent, width, height, screenData);
    }

    void __fastcall Hook_HelpActivated(void* listener, int64_t ui, int id, unsigned int profile)
    {
        if (id < kOptionsFirstId)
        {
            orig_HelpActivated(listener, ui, id, profile);
            return;
        }

        RIX_ButtonCallback callback = nullptr;
        void* user = nullptr;
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            size_t index = static_cast<size_t>(id - kOptionsFirstId);
            if (index < g_Options.size() && g_Options[index].Active)
            {
                callback = g_Options[index].OnPress;
                user = g_Options[index].User;
            }
        }

        YGO::RIX::PlayUISound(39);
        if (callback)
        {
            g_CallbackSource = static_cast<char*>(listener) - 656; // the listener is the screen's secondary vtable at +656
            callback(id, user);
            g_CallbackSource = nullptr;
        }
    }

    // ---------------------------------------------------------------- bookkeeping

    // Copies what the caller passed; text is never shared with the caller.
    void Assign(Entry& entry, const RIX_ButtonDesc& desc)
    {
        g_Retired.push_back(std::move(entry.Label));
        g_Retired.push_back(std::move(entry.Description));
        entry.Label = desc.Label ? desc.Label : L"";
        entry.Description = desc.Description ? desc.Description : L"";
        entry.Page = std::clamp(desc.Page, 0, MMP_COUNT - 1);
        entry.Skin = desc.Skin;
        entry.OnPress = desc.OnPress;
        entry.User = desc.User;
    }

    bool Valid(const RIX_ButtonDesc& desc)
    {
        return desc.Size >= offsetof(RIX_ButtonDesc, User) + sizeof(void*) && desc.Label && desc.Label[0]
            && (desc.Menu == RIX_MENU_MAIN || desc.Menu == RIX_MENU_OPTIONS);
    }

    Entry* Find(int id, bool* isOptions = nullptr)
    {
        if (id >= kOptionsFirstId)
        {
            size_t index = static_cast<size_t>(id - kOptionsFirstId);
            if (isOptions)
                *isOptions = true;
            return index < g_Options.size() ? &g_Options[index] : nullptr;
        }

        size_t index = static_cast<size_t>(id - MMI_VANILLA_COUNT);
        if (isOptions)
            *isOptions = false;
        return id >= MMI_VANILLA_COUNT && index < g_Main.size() ? &g_Main[index] : nullptr;
    }
}

namespace Menu
{
    int Add(const RIX_ButtonDesc& desc)
    {
        if (!Valid(desc))
            return -1;

        std::lock_guard<std::mutex> guard(g_Lock);
        const bool options = desc.Menu == RIX_MENU_OPTIONS;
        std::deque<Entry>& entries = options ? g_Options : g_Main;
        if (entries.size() >= (options ? kMaxOptionButtons : kMaxMainButtons))
            return -1;

        Entry& entry = entries.emplace_back();
        Assign(entry, desc);
        g_Dirty = true;
        if (!options && g_Installed)
            PatchLoopCount();

        int id = options ? static_cast<int>(kOptionsFirstId + entries.size() - 1) : static_cast<int>(MMI_VANILLA_COUNT + entries.size() - 1);
        Logger::WriteLog(std::format("Added {} menu button {}", options ? "options" : "main", id), MODULE_NAME, 0);
        return id;
    }

    bool Remove(int id)
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        Entry* entry = Find(id);
        if (!entry || !entry->Active)
            return false;

        entry->Active = false;
        g_Dirty = true;
        return true;
    }

    bool Update(int id, const RIX_ButtonDesc& desc)
    {
        if (!Valid(desc))
            return false;

        std::lock_guard<std::mutex> guard(g_Lock);
        Entry* entry = Find(id);
        if (!entry)
            return false;

        Assign(*entry, desc);
        entry->Active = true;
        g_Dirty = true;
        return true;
    }

    bool Get(int id, RIX_ButtonDesc& out)
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        bool options = false;
        Entry* entry = Find(id, &options);
        if (!entry)
            return false;

        out.Size = sizeof(RIX_ButtonDesc);
        out.Label = entry->Label.c_str();
        out.Description = entry->Description.c_str();
        out.Menu = options ? RIX_MENU_OPTIONS : RIX_MENU_MAIN;
        out.Page = entry->Page;
        out.Skin = entry->Skin;
        out.OnPress = entry->OnPress;
        out.User = entry->User;
        return true;
    }

    int ActiveCount()
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        auto active = [](const Entry& entry) { return entry.Active; };
        return static_cast<int>(std::count_if(g_Main.begin(), g_Main.end(), active) + std::count_if(g_Options.begin(), g_Options.end(), active));
    }

    // Main menu buttons first, then the options ones.
    int ActiveIdAt(int index)
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        for (size_t i = 0; i < g_Main.size(); ++i)
        {
            if (g_Main[i].Active && index-- == 0)
                return static_cast<int>(MMI_VANILLA_COUNT + i);
        }
        for (size_t i = 0; i < g_Options.size(); ++i)
        {
            if (g_Options[i].Active && index-- == 0)
                return static_cast<int>(kOptionsFirstId + i);
        }
        return -1;
    }

    void EditVanilla(int item, const std::wstring* label, const std::wstring* description, bool hidden)
    {
        if (item < 0 || item >= MMI_VANILLA_COUNT)
            return;

        std::lock_guard<std::mutex> guard(g_Lock);
        VanillaEdit& edit = g_Vanilla[item];
        if (label)
        {
            g_Retired.push_back(std::move(edit.Label));
            edit.Label = *label;
            edit.HasLabel = true;
        }
        if (description)
        {
            g_Retired.push_back(std::move(edit.Description));
            edit.Description = *description;
            edit.HasDescription = true;
        }
        edit.Hidden = hidden;
        g_HasVanillaEdits = true;
        g_Dirty = true;
    }

    bool SetVanillaAction(int item, RIX_ButtonCallback callback, void* user)
    {
        if (item < 0 || item >= MMI_VANILLA_COUNT)
            return false;

        std::lock_guard<std::mutex> guard(g_Lock);
        g_VanillaActions[item] = { callback, user };
        Logger::WriteLog(std::format("Game button {} {}", item, callback ? "now runs a plugin's action" : "does what the game does again"), MODULE_NAME, 0);
        return true;
    }

    void* MainScreen()
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        return g_Screen;
    }

    void SetFrameCallback(void (*callback)())
    {
        g_FrameCallback = callback;
    }

    void SetBuildCallback(void (*callback)())
    {
        g_BuildCallback = callback;
    }

    void Pin(int id)
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        Entry* entry = Find(id);
        if (entry)
            entry->Pinned = true;
    }

    void SetExclusive(bool on, bool whenShownAgain)
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        if (!on && whenShownAgain)
        {
            g_ReentryPending = true;   // stays exclusive until the main menu is next shown
            return;
        }
        g_ReentryPending = false;
        g_ExclusiveWanted = on;
        g_Dirty = true;
    }

    void SetReentryCallback(void (*callback)())
    {
        g_ReentryCallback = callback;
    }

    void* CallbackSource()
    {
        return g_CallbackSource;
    }

    void SetCallbackSource(void* screen)
    {
        g_CallbackSource = screen;
    }

    bool IsOpen()
    {
        // The main menu updates every frame while it is showing. (The UI manager's screen record did not read back the current id.)
        return g_LastUpdate != 0 && GetTickCount() - g_LastUpdate < 300;
    }

    bool Press(int item)
    {
        // No IsOpen() check: this is also called from a button of the main menu, and the press is only ever carried out by the
        // menu's own update, so it does nothing when the menu isn't running.
        if (item < 0 || item >= MMI_VANILLA_COUNT)
            return false;

        std::lock_guard<std::mutex> guard(g_Lock);
        g_PendingPress = item;
        g_PendingPressTime = GetTickCount();
        Logger::WriteLog(std::format("Pressing game button {}", item), MODULE_NAME, 0);
        return true;
    }

    void Install()
    {
        if (g_Installed)
            return;
        g_Installed = true;

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_SetupArrays, Hook_SetupArrays);
        DetourAttach(&(PVOID&)orig_LoadScreenTable, Hook_LoadScreenTable);
        DetourAttach(&(PVOID&)orig_ActivateItem, Hook_ActivateItem);
        DetourAttach(&(PVOID&)orig_Update, Hook_Update);
        DetourAttach(&(PVOID&)orig_CreateFromLayout, Hook_CreateFromLayout);
        DetourAttach(&(PVOID&)orig_SetDefinition, Hook_SetDefinition);
        DetourAttach(&(PVOID&)orig_ShowItem, Hook_ShowItem);
        DetourAttach(&(PVOID&)orig_ItemCreateFromLayout, Hook_ItemCreateFromLayout);
        DetourAttach(&(PVOID&)orig_HelpActivated, Hook_HelpActivated);
        LONG result = DetourTransactionCommit();
        Logger::WriteLog(std::format("Menu hooks: {}", result), MODULE_NAME, result == 0 ? 0 : 2);

        std::lock_guard<std::mutex> guard(g_Lock);
        if (!g_Main.empty())
            PatchLoopCount();
    }
}
