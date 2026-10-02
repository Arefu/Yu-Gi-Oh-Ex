#pragma once
#include <cstddef>
#include <cstdint>
#include <intrin.h>
#include <functional>
#include <iterator>

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

        // ---- more MenuKit menu functions (menu = the object a ScreenBaseMenu or ScreenBattlePackStore owns)
        namespace MenuKit
        {
            inline auto ClearVisible = reinterpret_cast<void(__fastcall*)(void* Menu)>(0x14080A4C0);        // empties the visible list (menu+88)
            inline auto Layout = reinterpret_cast<void(__fastcall*)(void* Menu)>(0x14080A930);              // hides every item, then shows and labels the visible ones
            inline auto SelectIndex = reinterpret_cast<void(__fastcall*)(void* Menu, unsigned int Index)>(0x14080A5D0);
            inline auto ResetItems = reinterpret_cast<void(__fastcall*)(void* Menu)>(0x14080A000);          // resets the item animations, then Layout
        }

        // RIX::Screen::GoBack: back to the screen this one was entered from (screen+60). RIX::Screen::SetHeaderText: the title at the top.
        inline auto GoBack = reinterpret_cast<void(__fastcall*)(void* Screen)>(0x140822750);

        // RIX::Screen::ShowMessageText: the screen's message box with a text (kept by the caller while the box is open). Sfx -1 = none (71 is
        // the game's error sound). OnOk is a std::function<void()>* the game takes over (moves from):
        //  - a real one (MSVC's std::function is 64 bytes with its callable at +56, the same in the game and our builds) adds an OK button
        //    (text 901) that highlights under the mouse and runs the function when pressed; Back also closes the box;
        //  - an empty one (EmptyFunction: 64 zero bytes) adds no button: the box shows OK as a prompt only, and any confirm / cancel / click
        //    closes it (the game's own locked-button messages are like that - nothing highlights).
        // Screen::Tick sends the input to the box while it is open, so the screen's own Update is paused.
        struct EmptyFunction
        {
            uint8_t Storage[56] = {};
            void* Impl = nullptr;
        };
        static_assert(sizeof(std::function<void()>) == sizeof(EmptyFunction), "std::function is not the layout the game uses");
        inline auto ShowMessageText = reinterpret_cast<void(__fastcall*)(void* Screen, int Sfx, const wchar_t* Text, void* OnOk)>(0x140822BA0);
        inline auto SetHeaderText = reinterpret_cast<void(__fastcall*)(void* Screen, int64_t Text)>(0x140822F00);

        // ---- input (the object is InputState). Masks: 1 up, 2 down, 4 left, 8 right, 0x1000 confirm, 0x2000 cancel.
        namespace Input
        {
            inline auto GetPressed = reinterpret_cast<int(__fastcall*)(void* Input)>(0x1408001B0);    // pressed this frame
            inline auto GetRepeat = reinterpret_cast<int(__fastcall*)(void* Input)>(0x140800290);     // auto-repeat of held directions
            inline auto GetHeld = reinterpret_cast<int(__fastcall*)(void* Input)>(0x140800340);       // held (the trunk and card info take it as their second mask)
            inline auto HelpBarPressed = reinterpret_cast<int(__fastcall*)(void* Help)>(0x14089F410);  // a prompt on the help bar (screen+264) was clicked

            // The mouse as the game keeps it (in InputState): x and y (two ints) at +0x60, "the mouse is in use" at +0x68 (the game selects what
            // is under it then - hover, not a click), a click this frame at bit 0 of +0x6C.
            inline int64_t MousePosition() { return *reinterpret_cast<int64_t*>(0x142924070); }
            inline bool MouseActive() { return *reinterpret_cast<int*>(0x142924078) != 0; }
            inline bool MouseClickPending() { return (*reinterpret_cast<uint8_t*>(0x14292407C) & 1) != 0; }
            // Returns Mask when the mouse was clicked this frame and consumes the click (menus use it with 0x1000 as "confirm by mouse").
            inline auto TakeMouseClick = reinterpret_cast<int(__fastcall*)(void* Input, unsigned int Mask)>(0x1408006B0);
        }

        // ---- widgets. Every widget is built the same way: construct it in memory of your own (zeroed, 16 byte aligned, kept while the screen
        // lives), then CreateFromLayout(widget, &parent, z, owner, x, y[, screenData]) where parent is a counted copy of the screen's root node (the
        // shared_ptr at screen+72), owner is screen+88 and screenData screen+120. Nothing is read from a layout file
        // (RIX::widget_Base::CreateNode, 0x14087C030). vftable slot 1 = SetFocused(bool) (highlight), slot 3 = SetVisible(bool).
        struct SharedNode
        {
            void* Node;
            void* Control;
        };

        // The parent is a std::shared_ptr passed BY VALUE: CreateFromLayout releases it before returning, exactly as the game's callers expect
        // (they always pass a fresh copy). So never pass the screen's own shared_ptr - pass a copy from ParentRef (one reference each call),
        // or the screen's root loses a reference per widget and is freed while in use (heap corruption, found 2026-09-28).
        inline SharedNode* ScreenRoot(void* Screen) { return reinterpret_cast<SharedNode*>(static_cast<char*>(Screen) + 72); }
        inline SharedNode ParentRef(const SharedNode* Parent)
        {
            SharedNode copy = *Parent;
            if (copy.Control)
                _InterlockedIncrement(reinterpret_cast<volatile long*>(static_cast<char*>(copy.Control) + 8));   // the use count
            return copy;
        }
        inline void* ScreenOwner(void* Screen) { return static_cast<char*>(Screen) + 88; }
        inline void* ScreenData(void* Screen) { return static_cast<char*>(Screen) + 120; }

        // A scene node's position (RIX::Node::SetX / SetY / SetPosition: floats at node+88 / +92, relative to its parent).
        inline auto NodeSetX = reinterpret_cast<void(__fastcall*)(void* Node, float X)>(0x14075A2F0);
        inline auto NodeSetY = reinterpret_cast<void(__fastcall*)(void* Node, float Y)>(0x14075A310);
        inline auto NodeSetPosition = reinterpret_cast<void(__fastcall*)(void* Node, float X, float Y)>(0x14075A300);

        // Drops one reference of a shared_ptr the plugin owns (what std::shared_ptr's destructor does: use count at control+8, weak at +12).
        inline void ReleaseRef(SharedNode& Ref)
        {
            if (auto* control = static_cast<char*>(Ref.Control))
            {
                auto** vftable = *reinterpret_cast<void(__fastcall***)(void*)>(control);
                if (_InterlockedExchangeAdd(reinterpret_cast<volatile long*>(control + 8), -1) == 1)
                {
                    vftable[0](control);   // destroy the object
                    if (_InterlockedExchangeAdd(reinterpret_cast<volatile long*>(control + 12), -1) == 1)
                        vftable[1](control);   // free the control block
                }
            }
            Ref = {};
        }

        // ---- plain pictures: a DFX::TLayerAnimoo (one sprite of a sheet) inside a DFX::TBase node, the way the game builds its own
        // (RIX::LayerAnimoo_CreateFromDesc 0x140785190). The sheet is a resource name ("pdui/doShared" = pdui\doShared.png + .dfymoo; loaded on
        // first use by DFX::Resource_FindOrLoad) and the sprite a name in it. Every shared_ptr argument marked "consumed" is taken BY VALUE by
        // the game: pass a counted copy (ParentRef), never a pointer you still own.
        namespace Dfx
        {
            inline auto MakeLayerAnimoo = reinterpret_cast<SharedNode*(__fastcall*)(SharedNode* Out)>(0x14075B580);
            inline auto LayerSetResource = reinterpret_cast<void(__fastcall*)(void* Layer, const char* Resource)>(0x14075BAB0);
            inline auto LayerSelectByName = reinterpret_cast<void(__fastcall*)(void* Layer, const char* Sprite)>(0x14075BB00);
            inline auto LayerGetPlayer = reinterpret_cast<char*(__fastcall*)(void* Layer)>(0x14075B970);   // +16 sprite index, +48 alignment bits
            // (out, parent consumed, content consumed, z, x, y): a new TBase under parent holding content, at x, y. Out holds one reference.
            inline auto MakeChildWithContent = reinterpret_cast<SharedNode*(__fastcall*)(SharedNode* Out, SharedNode* Parent, SharedNode* Content, int Z, float X, float Y)>(0x140744A10);
            inline auto RemoveChild = reinterpret_cast<void(__fastcall*)(void* Parent, SharedNode* Child)>(0x140759DD0);   // child consumed
            inline auto SetScaleXY = reinterpret_cast<void(__fastcall*)(void* Node, float X, float Y)>(0x14075A350);
            inline auto SetAlpha = reinterpret_cast<void(__fastcall*)(void* Node, float Alpha)>(0x14075A1E0);
            inline auto SetFlag = reinterpret_cast<void(__fastcall*)(void* Node, unsigned Mask, char On)>(0x14075A2A0);
            constexpr unsigned FlagVisible = 0x8;

            // The sprite a layer shows: its full (untrimmed) size, or false when the sheet or the name was not found.
            inline bool LayerSpriteSize(void* Layer, int& Width, int& Height)
            {
                auto* resource = *reinterpret_cast<char**>(static_cast<char*>(Layer) + 152);
                if (!resource)
                    return false;
                const int index = *reinterpret_cast<int*>(LayerGetPlayer(Layer) + 16);
                const uint32_t count = *reinterpret_cast<uint32_t*>(resource);
                if (index < 0 || static_cast<uint32_t>(index) >= count)
                    return false;
                const char* entry = *reinterpret_cast<char**>(resource + 8) + 56 * index;
                Width = *reinterpret_cast<const int*>(entry + 24);
                Height = *reinterpret_cast<const int*>(entry + 28);
                return Width > 0 && Height > 0;
            }

            // Puts one sprite on a node (normally ScreenRoot(screen)) with its top-left at X, Y, stretched to Width x Height (0 = its own size).
            // Returns the node holding it (the caller owns one reference: take it off with RemoveImage), or an empty SharedNode when the
            // sheet or sprite does not exist.
            inline SharedNode AddImage(const SharedNode* Parent, const char* Resource, const char* Sprite, int Z, float X, float Y, float Width = 0, float Height = 0)
            {
                SharedNode layer{};
                MakeLayerAnimoo(&layer);
                if (!layer.Node)
                    return {};
                LayerSetResource(layer.Node, Resource);
                LayerSelectByName(layer.Node, Sprite);
                *reinterpret_cast<int*>(LayerGetPlayer(layer.Node) + 48) = 0;   // top-left anchored (no alignment bits)

                int w = 0, h = 0;
                if (!LayerSpriteSize(layer.Node, w, h))
                {
                    ReleaseRef(layer);
                    return {};
                }

                SharedNode parent = ParentRef(Parent);
                SharedNode node{};
                MakeChildWithContent(&node, &parent, &layer, Z, X, Y);   // parent and layer consumed
                if (node.Node && (Width > 0 || Height > 0))
                    SetScaleXY(node.Node, Width > 0 ? Width / w : 1.0f, Height > 0 ? Height / h : 1.0f);
                return node;
            }

            // ---- text: a DFX::TLayerText in a TBase node. Its style (DFX::TTextSpec) is at layer+152: +8/+12 colour (ARGB), +24 flags
            // (0x100 no wrap), +28 wrap width, +32 font id, +40 align (1 left 2 centre 4 right: lines sit around x = 0; 8 top). Glyphs are
            // drawn at the font's own pixel size, so bigger text = a bigger font or the node scaled.
            inline auto MakeLayerText = reinterpret_cast<SharedNode*(__fastcall*)(SharedNode* Out)>(0x14075DC60);
            // Text: a string id (1..1213) or a wchar_t* that must stay valid while the node lives (the game re-reads it on every rebuild).
            inline auto LayerSetText = reinterpret_cast<void(__fastcall*)(void* Layer, int64_t IdOrText)>(0x14075E000);

            enum class TextAlign { Left, Centre, Right };

            // The game's UI face (FONT_ID_PD_*): font ids 0..7 at these pixel sizes.
            inline constexpr int PdFontSizes[] = { 88, 44, 32, 23, 20, 16, 14, 12 };

            // Puts text on a node: the box's top-left at X, Y (Width 0 = no box: X is the text's left / centre / right edge), PixelSize high,
            // aligned in the box, wrapped to the box's width when it has one. Text must outlive the node. Returns the node (caller owns one
            // reference, take it off with RemoveImage).
            inline SharedNode AddText(const SharedNode* Parent, const wchar_t* Text, int Z, float X, float Y, float Width, float PixelSize,
                                      TextAlign Align = TextAlign::Left, uint32_t Colour = 0xFFFFFFFF)
            {
                if (!Text || PixelSize <= 0)
                    return {};
                // the smallest PD font that is at least as big (scaling down stays sharp), else the biggest
                int font = 0;
                for (int i = 0; i < static_cast<int>(std::size(PdFontSizes)); ++i)
                    if (PdFontSizes[i] >= PixelSize)
                        font = i;
                const float scale = PixelSize / PdFontSizes[font];

                SharedNode layer{};
                MakeLayerText(&layer);
                if (!layer.Node)
                    return {};
                char* style = static_cast<char*>(layer.Node) + 152;
                *reinterpret_cast<uint32_t*>(style + 8) = Colour;
                *reinterpret_cast<uint32_t*>(style + 12) = Colour;
                *reinterpret_cast<uint32_t*>(style + 24) = Width > 0 ? 0 : 0x100;
                *reinterpret_cast<float*>(style + 28) = Width > 0 ? Width / scale : 0.0f;
                *reinterpret_cast<int*>(style + 32) = font;
                *reinterpret_cast<int*>(style + 40) = 8 | (Align == TextAlign::Left ? 1 : Align == TextAlign::Centre ? 2 : 4);
                LayerSetText(layer.Node, reinterpret_cast<int64_t>(Text));

                const float x = Align == TextAlign::Left ? X : Align == TextAlign::Centre ? X + Width / 2 : X + Width;
                SharedNode parent = ParentRef(Parent);
                SharedNode node{};
                MakeChildWithContent(&node, &parent, &layer, Z, x, Y);   // parent and layer consumed
                if (node.Node && scale != 1.0f)
                    SetScaleXY(node.Node, scale, scale);
                return node;
            }

            // Takes a node made by AddImage or AddText off its parent and drops the caller's reference.
            inline void RemoveImage(const SharedNode* Parent, SharedNode& Node)
            {
                if (!Node.Node)
                    return;
                if (Parent && Parent->Node)
                {
                    SharedNode child = ParentRef(&Node);
                    RemoveChild(Parent->Node, &child);   // consumed
                }
                ReleaseRef(Node);
            }
        }

        inline void WidgetSetFocused(void* Widget, bool On) { (*reinterpret_cast<void(__fastcall***)(void*, char)>(Widget))[1](Widget, On); }
        inline void WidgetSetVisible(void* Widget, bool On) { (*reinterpret_cast<void(__fastcall***)(void*, char)>(Widget))[3](Widget, On); }

        // The card trunk (RIX::widget_TrunkZone): the deck editor's grid of cards with its filter bar. Only the deck editor has one (at +9984).
        namespace Trunk
        {
            constexpr size_t Size = 1632;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Trunk)>(0x1408BE760);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Trunk, SharedNode* Parent, int Z, void* Owner, float X, float Y)>(0x1408BF050);
            inline auto SetDeckInfo = reinterpret_cast<void(__fastcall*)(void* Trunk, void* DeckInfo)>(0x1408C01B0);   // where the grid reads in-deck counts
            inline auto ProcessInput = reinterpret_cast<char(__fastcall*)(void* Trunk, int Pressed, int Held, int* Action, int* Kind, char* Flag)>(0x1408BEB30);
            inline auto Update = reinterpret_cast<void(__fastcall*)(void* Trunk)>(0x1408C0860);           // every frame
            inline auto GetSelectedCardId = reinterpret_cast<uint16_t(__fastcall*)(void* Trunk)>(0x1408BF000);

            // The list is built the way TrunkView_BuildCardList (0x1408BFEF0) does it: entries {u16 card id, u32 count, u32 0} in the vector at +1576,
            // the vector<int> at +1552 maps (card id - 3900) to an entry; then ApplyFilter, SortAndFillGrid, Refresh.
            constexpr size_t Entries = 1576;
            constexpr size_t IdToEntry = 1552;
            constexpr size_t Grid = 56;
            constexpr int FirstCardId = 3900;
            struct Entry
            {
                uint16_t CardId;
                uint16_t Pad;
                uint32_t Count;
                uint32_t Unused;
            };
            static_assert(sizeof(Entry) == 12);
            inline auto GridReset = reinterpret_cast<void(__fastcall*)(void* Grid, int)>(0x14088FA40);    // (trunk+56, 0), then GridSetMode(trunk+56, 10)
            inline auto GridSetMode = reinterpret_cast<void(__fastcall*)(void* Grid, int)>(0x14088F610);
            inline auto IntVectorResizeFill = reinterpret_cast<void(__fastcall*)(Vector* Vec, size_t Size, const int* Value)>(0x14088CE10);
            inline auto EntryVectorEmplace = reinterpret_cast<void(__fastcall*)(Vector* Vec, void* Where, const Entry* Value)>(0x1408BE270);
            inline auto ApplyFilter = reinterpret_cast<void(__fastcall*)(void* Trunk)>(0x1408BF9E0);
            inline auto SortAndFillGrid = reinterpret_cast<void(__fastcall*)(void* Trunk)>(0x1408BFC30);
            inline auto Refresh = reinterpret_cast<void(__fastcall*)(void* Trunk)>(0x1408C08D0);

            // Immediates in TrunkView_BuildCardList: the internal id loop bound (cmp ebx, imm32 at 0x1408C0061) and the size of the id -> entry
            // vector (mov ebx, imm32 at 0x1408BFF49). Yu-Gi-Oh-Cards raises both for its extra cards, so they are read, not assumed.
            inline uint32_t InternalIdLimit() { return *reinterpret_cast<uint32_t*>(0x1408C0063); }
            inline uint32_t IdToEntrySize() { return *reinterpret_cast<uint32_t*>(0x1408BFF4A); }
        }

        // RIX::DeckStateHelper: a deck's contents. An empty one is enough for the trunk grid when there is no deck.
        namespace DeckState
        {
            constexpr size_t Size = 52 + 0x567A;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Helper)>(0x140755DE0);
        }

        // RIX::widget_CardInfo: the deck editor's card picture + ATK / DEF / level panel (editor +41488).
        namespace CardInfo
        {
            constexpr size_t Size = 928;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Info)>(0x140883380);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Info, SharedNode* Parent, int Z, void* Owner, float X, float Y)>(0x140883BA0);
            inline auto SetWidth = reinterpret_cast<void(__fastcall*)(void* Info, float Width)>(0x140884AA0);
            inline auto SetCard = reinterpret_cast<void(__fastcall*)(void* Info, uint16_t CardId)>(0x140884300);   // 0xFFFF = none
            inline auto Update = reinterpret_cast<void(__fastcall*)(void* Info, float Seconds, int Held, int)>(0x140884ED0);
        }

        // One digit wheel of the player match code entry (widget_EntryDigit: a number in a box, an arrow above and below).
        namespace EntryDigit
        {
            constexpr size_t Size = 184;
            constexpr size_t Value = 176;       // int, 0..9
            inline auto Construct = reinterpret_cast<void(__fastcall*)(void* Digit)>(0x140853240);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Digit, SharedNode* Parent, int Z, void* Owner, float X, float Y, void* ScreenData)>(0x14089DE70);
            inline auto SetSelected = reinterpret_cast<void(__fastcall*)(void* Digit, char Selected)>(0x14089E150);
            inline auto SetValue = reinterpret_cast<void(__fastcall*)(void* Digit, int Value)>(0x14089E220);     // stores Value % 10
            inline auto HitTest = reinterpret_cast<int(__fastcall*)(void* Digit, int64_t Mouse)>(0x14089DAC0);  // 0 upper arrow, 1 lower arrow, 2 box, -1 none
        }
    }
}
