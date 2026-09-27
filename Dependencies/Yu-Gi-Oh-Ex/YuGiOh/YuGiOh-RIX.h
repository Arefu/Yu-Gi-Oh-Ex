#include <cstddef>
#include <cstdint>
#include <functional>

// The game's UI framework ("RIX"): a screen manager that switches between screens (ScreenMainMenu, ScreenHelp...) and a small widget
// kit (MenuKit) the menus are made of. Everything here was traced in YuGiOh.exe.i64 (names and types are in the IDB too).
namespace YGO
{
    namespace RIX
    {
        // ScreenIds are in YuGiOh-UI.h (YGO::UI::RIX::ScreenID).

        // std::vector as the game lays it out (MSVC): begin, end, end of capacity.
        struct Vector
        {
            void* Begin;
            void* End;
            void* Capacity;
        };

        // One button of the main menu, see g_MainMenuItemTable (0x140A76680). LabelText and DescriptionText are a string id (1..1213) or,
        // when larger than 2214, a raw `const wchar_t*` that is shown as it is.
        struct MainMenuItemDef
        {
            int64_t Id;
            int64_t LabelText;
            int64_t DescriptionText;
        };

        // The ids of the 13 buttons the game ships.
        enum MainMenuItem : int
        {
            MMI_SINGLE_PLAYER_MENU = 0, // opens page 1
            MMI_SOLO_DUEL = 1,          // -> DUEL_SELECT
            MMI_DUELIST_CHALLENGE = 2,  // -> DUELIST_CHALLENGE, needs save flag DUELIST_CHALLENGE
            MMI_MULTIPLAYER_MENU = 3,   // opens page 2
            MMI_MULTIPLAYER_A = 4,      // -> PLAYER_MATCH
            MMI_MULTIPLAYER_B = 5,      // -> PLAYER_MATCH
            MMI_LEADERBOARD = 6,        // -> LEADERBOARD
            MMI_BATTLE_PACK = 7,        // -> BATTLEPACK, needs save flag BATTLE_PACKS
            MMI_DECK_EDITOR = 8,        // -> DECK_EDITOR
            MMI_CARD_SHOP = 9,          // -> CARD_SHOP, needs save flag CARD_SHOP
            MMI_HELP_AND_OPTIONS = 10,  // -> HELP_AND_OPTIONS
            MMI_TUTORIALS = 11,         // -> TUTORIAL_LIST
            MMI_QUIT_GAME = 12,
            MMI_VANILLA_COUNT = 13,
        };

        // The three pages of the main menu (each is a vector<int> of item ids, in on-screen order).
        enum MainMenuPage : int
        {
            MMP_MAIN = 0,          // 0, 3, 7, 8, 9, 10, 12
            MMP_SINGLE_PLAYER = 1, // 1, 2, 11
            MMP_MULTIPLAYER = 2,   // 4, 5, 6
            MMP_COUNT = 3,
        };

        // A menu button widget (424 bytes). Only the traced fields are named.
        struct WidgetItem
        {
            void* VFTable;             // +0x00
            char pad_08[8];
            void* Node;                // +0x10 layout node (its Y is set by LayoutPage)
            char pad_18[24];
            char Block_30[16];
            void* LabelText;           // +0x40 the label's text widget
            char pad_48[40];
            void* LockIcon;            // +0x70
            char pad_78[296];
            char Locked;               // +0x1A0
            char pad_1A1[7];
        };
        static_assert(sizeof(WidgetItem) == 424);

        // What has been traced of RIX::ScreenMainMenu.
        struct ScreenMainMenu
        {
            void* VFTable;             // +0x000 RIX::ScreenMainMenu::vftable (0x140A765C0)
            char pad_008[144];
            char HeaderWidget[112];    // +0x098
            char DescriptionWidget[392]; // +0x108 (the text shown for the highlighted button)
            Vector Items;              // +0x290 vector<WidgetItem>, 13 of them
            char Cursor[56];           // +0x2A8 the selected slot is the first int
            Vector Defs;               // +0x2E0 vector<MainMenuItemDef>, 13 of them
            Vector Pages[MMP_COUNT];   // +0x2F8 vector<int> of item ids per page
            int CurrentPage;           // +0x340
            int PendingItem;           // +0x344 item pressed this frame, -1 when none
        };
        static_assert(offsetof(ScreenMainMenu, Items) == 0x290);
        static_assert(offsetof(ScreenMainMenu, Defs) == 0x2E0);
        static_assert(offsetof(ScreenMainMenu, Pages) == 0x2F8);
        static_assert(offsetof(ScreenMainMenu, CurrentPage) == 0x340);
        static_assert(offsetof(ScreenMainMenu, PendingItem) == 0x344);

        // ---- functions ----

        inline auto GetUI = reinterpret_cast<int64_t(__fastcall*)()>(0x1408121B0);

        // Switch to a screen (a YGO::UI::RIX::ScreenID) with a fade of `FadeSeconds`. The game itself passes 0.15, 273, 1.
        inline auto NavigateToScreen = reinterpret_cast<char(__fastcall*)(int64_t UI, int ScreenId, double FadeSeconds, int Flags, char Force)>(0x1408087A0);

        inline auto PlayUISound = reinterpret_cast<void(__fastcall*)(int SoundId)>(0x14086C280);

        // Changes screen the way the game's own buttons do (RIX::Screen::GotoScreen): `FromScreen` is the screen object you are leaving. Besides
        // navigating it records where you came from in the target screen, which is what its Back button returns to. Calling NavigateToScreen
        // alone skips that, so Back leads nowhere.
        // The screen's own Yes/No box (see docs/Widgets.md): opens it on Screen with the given text. Text is a string id or a wide string that
        // must stay valid while the box is open. OnYes is taken over by the game (moved from and freed by it), so hand it a std::function
        // that fits in the small buffer (a plain function pointer does); No only closes the box. Sfx -1 = default.
        inline auto ShowYesNo = reinterpret_cast<void(__fastcall*)(void* Screen, int Sfx, const wchar_t* Text, std::function<void()>* OnYes)>(0x1408229D0);
        // The help bar at the bottom of a screen (widget_Help, at Screen + 264): a list of button prompts. An entry is the input mask of the button
        // (0x2000 cancel, 0x1000 confirm), 1, and a string id. Clear, add entries, then Layout to show them.
        struct HelpEntry
        {
            int32_t Mask;
            int32_t Alt;
            int64_t TextId;
        };
        inline auto HelpClear = reinterpret_cast<void(__fastcall*)(void* Help)>(0x14089F630);
        inline auto HelpAdd = reinterpret_cast<void(__fastcall*)(void* Help, const HelpEntry* Entry, unsigned char Flag)>(0x14089E9D0);
        inline auto HelpLayout = reinterpret_cast<void(__fastcall*)(void* Help)>(0x14089EC90);

        // The game's input state and its "cancel pressed" query (Esc / Backspace / the pad's cancel): returns the mask when it was pressed.
        inline void* const InputState = reinterpret_cast<void*>(0x142924010);
        inline auto InputCancelPressed = reinterpret_cast<int64_t(__fastcall*)(void* Input, unsigned int Mask)>(0x1408007E0);

        // The pieces ShowYesNo is made of, for a box whose No button does something too (the dialog object is Screen + 432).
        // Order: Clear, SetMode(1), SetText, AddItem(918 = Yes) and AddItem(900 = No), then Show(true) and Screen + 48 = 1. Both AddItem calls take
        // over their std::function like ShowYesNo does.
        inline auto DialogClear = reinterpret_cast<void(__fastcall*)(void* Dialog)>(0x1408987F0);
        inline auto DialogSetMode = reinterpret_cast<void(__fastcall*)(void* Dialog, int Mode)>(0x1408988A0);
        inline auto DialogSetText = reinterpret_cast<void(__fastcall*)(void* Dialog, const wchar_t* Text)>(0x1408988E0);
        inline auto DialogAddItem = reinterpret_cast<void(__fastcall*)(void* Dialog, int64_t LabelId, std::function<void()>* OnPress, int Sfx)>(0x140897BC0);
        constexpr int DialogLabelYes = 918;
        constexpr int DialogLabelNo = 900;
        inline auto GotoScreenFrom = reinterpret_cast<char(__fastcall*)(void* FromScreen, int ScreenId)>(0x1408227A0);

        // std::map<int screenId, ScreenBase*> of every screen object (g_ScreenMap); the variable holds the map's head node. MSVC node layout:
        // left +0, parent +8, right +16, isnil +25, key +32, value +40.
        inline void* FindScreenObject(int ScreenId)
        {
            auto head = *reinterpret_cast<uint8_t**>(0x143327FE8);
            if (!head)
                return nullptr;

            uint8_t* best = head;
            uint8_t* node = *reinterpret_cast<uint8_t**>(head + 8);
            while (node && !node[25])
            {
                if (*reinterpret_cast<int*>(node + 32) >= ScreenId)
                {
                    best = node;
                    node = *reinterpret_cast<uint8_t**>(node);
                }
                else
                    node = *reinterpret_cast<uint8_t**>(node + 16);
            }

            if (best == head || ScreenId < *reinterpret_cast<int*>(best + 32))
                return nullptr;
            return *reinterpret_cast<void**>(best + 40);
        }

        // Quitting: the main menu's Quit button just calls RequestQuit(GetGlobalInstance()), which sets a flag (instance + 0xB98) the main loop watches.
        inline auto GetGlobalInstance = reinterpret_cast<void*(__fastcall*)()>(0x1408121A0);
        inline auto RequestQuit = reinterpret_cast<void(__fastcall*)(void* Instance)>(0x1408189C0);

        // The state of the screen manager: index into the record array, records are 48 bytes with the screen id at +8.
        inline int CurrentScreenId()
        {
            int64_t ui = GetUI();
            int index = *reinterpret_cast<int*>(ui + 852);
            int64_t records = *reinterpret_cast<int64_t*>(ui + 856);
            return records ? *reinterpret_cast<int*>(records + 48LL * index + 8) : -1;
        }

        // The MenuKit menu object every ScreenBaseMenu (options, pause, live menu) owns at screen+664.
        namespace MenuKit
        {
            inline uintptr_t SetDefinition = 0x14080A210;   // (menu, MainMenuItemDef* table): items from a table that ends with id -1
            inline uintptr_t ShowItem = 0x140809580;        // (menu, id): appends to the visible list (vector<int> at menu+88)
            inline uintptr_t ItemCreateFromLayout = 0x140809CC0;
            inline uintptr_t CreateItems = 0x140809E90;
            constexpr uintptr_t CreateItemsEnd = 0x140809FE4;

            inline auto ShowItemNow = reinterpret_cast<uint64_t(__fastcall*)(void* Menu, unsigned int Id)>(ShowItem);
        }

        // The Help & Options screen (RIX::ScreenHelp, screen id 12): a ScreenBaseMenu with 9 defined items (0..8).
        namespace HelpMenu
        {
            constexpr uintptr_t ItemTable = 0x140A74200;    // g_HelpMenuItemTable
            constexpr int VanillaItemCount = 9;
            constexpr int BackItem = 7;                     // always shown last
            inline uintptr_t OnEnter = 0x140841EE0;         // decides which items are shown, in order
            constexpr uintptr_t OnEnterEnd = 0x140841FC0;
            inline uintptr_t OnItemActivated = 0x140841CD0; // (listener, ui, itemId, profile); -2 = back
        }

        namespace MainMenu
        {
            inline uintptr_t SetupArrays = 0x140857FA0;      // fills the three page vectors
            inline uintptr_t LoadScreenTable = 0x140857DA0;  // g_MainMenuItemTable -> Defs
            inline uintptr_t ActivateItem = 0x140856C40;     // a button was pressed
            inline uintptr_t Update = 0x140857840;           // per frame
            inline uintptr_t LayoutPage = 0x1408583D0;
            inline uintptr_t CreateFromLayout = 0x1408A1260; // RIX::MenuKit::widget_Item::CreateFromLayout

            // Grow helpers (they allocate, move the old items and set End to Begin + NewSize).
            // ReallocateItems takes a third argument: a pointer to a byte the game reads for every new item (it passes the address of a
            // zero byte). Leaving it out crashes on the read of garbage (found from the crash log at 0x14084E1D0).
            inline auto ReallocateItems = reinterpret_cast<void(__fastcall*)(Vector* Items, size_t NewSize, const char* Value)>(0x14084E0C0);
            inline auto ReallocateDefs = reinterpret_cast<void(__fastcall*)(Vector* Defs, size_t NewSize)>(0x1407AE2A0);
            inline auto VectorIntEmplaceReallocate = reinterpret_cast<void(__fastcall*)(Vector* Vec, void* Where, const int* Value)>(0x140746A20);

            inline auto SetEnabled = reinterpret_cast<void(__fastcall*)(void* Widget, char Enabled)>(0x14075A490);

            // `cmp r15d, 0Dh` closing the widget creation loop of ScreenMainMenu::SetupWidgets: the 13 is the byte at +3.
            constexpr uintptr_t SetupWidgetsLoopCount = 0x140857D17;
            constexpr uintptr_t SetupWidgetsBegin = 0x1408579B0;
            constexpr uintptr_t SetupWidgetsEnd = 0x140857DA0;

            inline void LayoutPageNow(ScreenMainMenu* Screen, bool Animate)
            {
                reinterpret_cast<int64_t(__fastcall*)(ScreenMainMenu*, char)>(LayoutPage)(Screen, Animate);
            }
        }
    }
}
