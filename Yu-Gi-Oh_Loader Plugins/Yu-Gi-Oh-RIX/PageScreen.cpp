// The page engine: version 2 page files on a screen of RIX's own (id 102). Everything a page shows is one of the GAME'S widgets, built
// the way the screen that owns it builds it (the recipe per widget is in docs/PageEngine.md, addresses named in YuGiOh.exe.i64):
//
//   header   widget_TextScroll at screen+152 (ScreenBattlePackStore::SetupWidgets: CreatePart2 then Screen::SetHeaderText)
//   help bar widget_Help at screen+264 (ScreenBase::Load builds it), Select / Back prompts and the focused item's description
//   menu     RIX::MenuKit::menu (152 bytes, the Battle Pack / Help / Pause button list): Constructor, SetDefinition, ShowAll, a TBase node,
//            CreateItems, then ProcessInput every frame with a listener {OnItemActivated, IsItemEnabled}
//   slider   ScreenHelpSetting::tickLine_t (408 bytes, the 10-tick volume bar): Constructor, Setup, a tick node's SetEnabled per level
//   digits   widget_EntryDigit (the Live setting code wheel), one per digit
//   cardInfo widget_CardDetails (the deck editor's card panel)
//   cardBox  widget_CardDisplay (the Random Pack Store's box of pulled cards): Constructor, CreateFromLayout(desc), SetCards, ProcessInput
//   message / confirm  the screen's widget_Dialog (screen+432): ShowMessageText / ShowYesNo
//   image / text       plain DFX layers (decoration)
//
// Widgets are built the first time their page shows and never freed (the game keeps pointers into them); a page that is covered or closed
// hides them. Values live in page variables or Config.ini; actions and conditions are JSON (see docs/PageEngine.md).
#define RIX_EXPORTS
#include "PageScreen.h"

#include <Windows.h>
#include <detours.h>
#include <algorithm>
#include <cctype>
#include <unordered_set>
#include <set>
#include <cmath>
#include <cstring>
#include <deque>
#include <filesystem>
#include <format>
#include <fstream>
#include <functional>
#include <map>
#include <memory>
#include <string>
#include <vector>

#include <json.hpp>

#include "Logger.h"
#include "MainMenu.h"
#include "YuGiOh/YuGiOh-RIX.h"
#include "YuGiOh/YuGiOh-Addresses.h"
#include "YuGiOh/YuGiOh-CARDS.h"
#include "YuGiOh/YuGiOh-SAVE.h"
#include "YuGiOh/YuGiOh-PASS.h"
#include "Yu-Gi-Oh-Mods.h"

namespace R = YGO::RIX;
using json = nlohmann::json;

namespace
{
    // ---------------------------------------------------------------- the game's functions

    constexpr int kScreenId = 102;                      // 100 = Core's save select, 101 free (was RIX settings)
    constexpr int kScreenMainMenu = 8;
    constexpr int kScreenTypeOptions = 4;               // ScreenCommonBg's backdrop for the option screens
    constexpr size_t kScreenSize = 0x290 + 0x70;
    constexpr uintptr_t kScreenBaseVftable = 0x140A716B8;
    constexpr int kVftableSlots = 22;
    constexpr uintptr_t kGlobalInstance = 0x1429275D8;
    constexpr size_t kMainScheduler = 0x200;
    constexpr int kHelp = 264;
    constexpr int kHeader = 152;
    constexpr uintptr_t kHelpSelect = YGO::Addr::HelpEntry_Select;
    constexpr uintptr_t kHelpBack = 0x140A7A190;

    constexpr int kUp = 1, kDown = 2, kLeft = 4, kRight = 8, kConfirm = 0x1000, kCancel = 0x2000;
    constexpr int kSoundMove = 37, kSoundStep = 16, kSoundConfirm = 39, kSoundBack = 18, kSoundError = 71;

    auto GameNew = reinterpret_cast<void*(__fastcall*)(size_t)>(0x14090D2B8);
    auto ScreenBase_Constructor = reinterpret_cast<void*(__fastcall*)(void*)>(0x140821B20);
    auto ScreenBase_SetScreenId = reinterpret_cast<void(__fastcall*)(void*, int)>(0x140822E00);
    auto ScreenBase_SetScreenType = reinterpret_cast<void(__fastcall*)(void*, int)>(0x1408228C0);
    auto ScreenBase_Load = reinterpret_cast<void(__fastcall*)(void*, int64_t)>(0x140822470);
    auto ScreenBase_Activate = reinterpret_cast<void(__fastcall*)(void*)>(0x140822390);
    auto ScreenBase_Tick = reinterpret_cast<void(__fastcall*)(void*, int64_t)>(0x140821EC0);
    uintptr_t orig_RunJobs = 0x140821170;

    // header: YGO_FRONT::widget_TextScroll::CreatePart2(widget, parent consumed, z, owner, x, y, text id)
    auto TextScroll_CreatePart2 = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*, float, float, int64_t)>(0x1408B5C80);
    // help bar text: a std::wstring made from a wchar_t* (0x140827F90), handed to widget_Help (0x14089F730) - the Battle Pack screen's own way
    auto WString_FromChars = reinterpret_cast<void*(__fastcall*)(void* out, const wchar_t*)>(0x140827F90);
    auto Help_SetDescription = reinterpret_cast<void(__fastcall*)(void* help, void* wstring)>(0x14089F730);

    // RIX::MenuKit::menu
    constexpr size_t kMenuSize = 152;
    auto Menu_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x140808C20);
    auto Menu_SetDefinition = reinterpret_cast<int64_t(__fastcall*)(void*, const R::MainMenuItemDef*)>(0x14080A210);
    auto Menu_ShowAll = reinterpret_cast<void(__fastcall*)(void*)>(0x140809970);
    struct MenuLayout { float StartY, Spacing, X; int Z; };   // CreateItems' third argument (menu+16 keeps the first two)
    auto Menu_CreateItems = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, const MenuLayout*, void* owner, void* screenData)>(0x140809E90);
    auto Menu_ProcessInput = reinterpret_cast<char(__fastcall*)(void*, int, void* listener, unsigned)>(0x1408096C0);
    auto Menu_Cursor = reinterpret_cast<unsigned(__fastcall*)(void*)>(0x140809A50);
    auto Menu_IdAt = reinterpret_cast<int(__fastcall*)(void*, unsigned)>(0x140809B50);
    constexpr int kMenuItemSize = 480, kMenuItemId = 48, kMenuItemLabel = 56, kMenuItemDescription = 96;

    // DFX::TBase
    auto TBase_MakeShared = reinterpret_cast<R::SharedNode*(__fastcall*)(R::SharedNode*)>(0x140759190);
    auto TBase_AddChild = reinterpret_cast<void(__fastcall*)(void* parent, R::SharedNode* child)>(0x140758EA0);   // child consumed
    auto TBase_SetZ = reinterpret_cast<void(__fastcall*)(void*, int)>(0x14075A4B0);
    auto Node_SetEnabled = reinterpret_cast<void(__fastcall*)(void*, char)>(0x14075A490);

    // RIX::ScreenHelpSetting::tickLine_t
    constexpr size_t kTickSize = 408;
    auto Tick_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x14084B770);
    auto Tick_Setup = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode* parent, int z, void* owner, float x, float y)>(0x14084B9B0);
    constexpr int kTickNodes = 80, kTickStride = 32, kTickLevel = 400;
    constexpr float kTickFirstX = 949, kTickStep = 49;
    constexpr int kTickBar = 48;    // the bar picture's layer holder (Setup: LayerAnimoo_CreateFromDesc(desc, tick+48, ...))
    // the Settings screen's mouse code (SoundSettings_HandleInput 0x14084BE30): is the mouse on a layer holder's picture (screen bounds of
    // holder+16's layer), and the mouse in a node's own space (TBase::ToLocal(node, out xy, mouse xy as two floats))
    auto Layer_HitTest = reinterpret_cast<bool(__fastcall*)(void* holder, int64_t mouse)>(0x1407858E0);
    auto Node_ToLocal = reinterpret_cast<float*(__fastcall*)(void* node, float* out, int64_t mouseFloats)>(0x14075A070);

    // ---- Free Duel's widgets (RIX::ScreenHardChallenge::SetupWidgets 0x140841110, OnEnter 0x1408415F0, FreeDuel_HandleInput 0x140840B10)
    constexpr size_t kBigWidget = 4096;   // pickSeries 216, pickCharacter 536, RecipeZone 1408, RecipeInfo 584 bytes (the screen's member gaps)
    // RIX::widget_pickSeries (Free Duel +656): the series row. SetList copies a vector<int> of series ids (OnEnter: 0..5).
    auto Series_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x1408A6DD0);
    auto Series_Create = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*, float, float, void* screenData)>(0x1408A7220);
    auto Series_SetList = reinterpret_cast<void(__fastcall*)(void*, R::Vector*)>(0x1408A76E0);
    auto Series_Current = reinterpret_cast<int(__fastcall*)(void*)>(0x1408A7110);      // the highlighted series id (-1 none)
    auto Series_Input = reinterpret_cast<char(__fastcall*)(void*, int pressed)>(0x1408A6F90);   // true when the highlight moved
    auto Series_HitTest = reinterpret_cast<int(__fastcall*)(void*, int64_t mouse)>(0x1408A7140);   // the tab under the mouse, -1 none
    // RIX::widget_pickCharacter (Free Duel +904): the opponent grid. SetList takes a vector of {int character, int unlocked}.
    auto Chars_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x1408A4C50);
    auto Chars_Create = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*, float, float, int64_t)>(0x1408A5910);
    auto Chars_SetList = reinterpret_cast<void(__fastcall*)(void*, R::Vector*)>(0x1408A67E0);
    auto Chars_Input = reinterpret_cast<void(__fastcall*)(void*, int pressed, int held, char* confirmed)>(0x1408A50B0);
    auto Chars_Selected = reinterpret_cast<unsigned(__fastcall*)(void*)>(0x1408A52A0);   // g_CharacterRecords index, 0 = none / locked
    // RIX::widget_RecipeZone (Free Duel +1456, YuGiOh-RIX.h DeckSelectList) and RIX::widget_RecipeInfo (+2864 yours, +3448 the opponent's)
    auto Decks_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x1408AD1C0);
    auto Decks_Create = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*, float, float, int, float)>(0x1408ADC70);
    auto Info_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x1408AA5C0);
    auto Info_Create = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*, float, float, int mode, int side, float)>(0x1408AACC0);
    // vectors the game grows itself: vector<int> (std::vector_int::_Emplace_reallocate) and the 8-byte {character, unlocked} pairs
    auto VectorGrowInt = reinterpret_cast<void(__fastcall*)(R::Vector*, void* where, const int* value)>(0x140746A20);
    auto VectorGrowPair = reinterpret_cast<void(__fastcall*)(R::Vector*, void* where, const int64_t* value)>(0x140751770);
    // the character table (FreeDuel_BuildOpponentList 0x140841900): 240 records of 104 bytes, +4 series, +8 deck, +16 sku
    constexpr uintptr_t kCharacterRecords = 0x142913470;
    constexpr int kCharacterCount = 240, kCharacterSize = 104;
    auto Character_IsDefined = reinterpret_cast<bool(__fastcall*)(int)>(0x1407FED00);
    auto Character_IsSelectable = reinterpret_cast<bool(__fastcall*)(int)>(0x1407FED30);
    auto Sku_IsOwned = reinterpret_cast<int(__fastcall*)(int)>(0x1407FFAF0);
    auto Deck_IsDefined = reinterpret_cast<bool(__fastcall*)(int)>(0x1407FE4D0);
    auto Deck_OwnerCharacter = reinterpret_cast<int(__fastcall*)(int)>(0x1407FE4B0);
    auto Get_PlayerSection = reinterpret_cast<char*(__fastcall*)(unsigned)>(0x1407F80A0);

    // ---- the deck editor's card grid filter (RIX::ScreenDeckEditor: SetupWidgets 0x140838200, Update state 6, DeckEdit_TrunkInputHandler)
    // widget_TrunkFilterCategories (editor +11616, 3976 bytes): built at (1568.5, 120), the centre of the editor's right panel. The grid
    // reports its filter button as action 2; the editor then shows and focuses the panel, puts the grid in mode 4 and runs the panel's
    // input every frame while the byte at panel+8 says it is open. The panel writes the shared filter state (0x140C91C60), which the
    // grid's ApplyFilter reads: when its "changed" flag is set the grid is filtered, sorted and refreshed again.
    auto Filter_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x1408B7160);
    auto Filter_Create = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*, float, float)>(0x1408B8B00);
    auto Filter_Input = reinterpret_cast<void(__fastcall*)(void*, int pressed)>(0x1408B7D50);
    constexpr int kFilterOpen = 8;
    auto Filter_TakeChanged = reinterpret_cast<int(__fastcall*)(void* state, int)>(0x140870060);
    void* const kFilterState = reinterpret_cast<void*>(0x140C91C60);
    auto Trunk_SetMode = reinterpret_cast<void(__fastcall*)(void*, int)>(0x1408C01A0);         // 4 while the filter is open, -1 normal
    auto Trunk_Reapply = reinterpret_cast<void(__fastcall*)(void*)>(0x1408BEF90);             // ApplyFilter + SortAndFillGrid + Refresh
    auto Trunk_AfterFilter = reinterpret_cast<void(__fastcall*)(void*)>(0x1408C0880);
    // the grid's own hit area (the deck editor's region 7: sub_140826760(trunk+1488, mouse) - a node + rect in its own space)
    auto Area_HitTest = reinterpret_cast<bool(__fastcall*)(void* area, int64_t mouse)>(0x140826760);
    constexpr int kTrunkArea = 1488;
    auto Filter_Reset = reinterpret_cast<int64_t(__fastcall*)(void* state)>(0x14086FD10);   // clears the filter, marks every part changed
    // ---- the full card view (widget_CardDetailsFull, deck editor +44888, 1608 bytes): CreateFromLayout(w, root, 19, owner, 33, 18);
    // DeckEdit_OpenCardDetails sets the card (0x14088A160) and goes to state 20, whose Update runs 0x14088A1D0(w, seconds, pressed, held
    // | the right button held) every frame and closes on confirm / Back (0xA000). The grid opens it on a right-click (0x8000).
    auto Full_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x140888080);
    auto Full_Create = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*, float, float)>(0x140888660);
    auto Full_SetCard = reinterpret_cast<void(__fastcall*)(void*, uint16_t)>(0x14088A160);
    auto Full_Update = reinterpret_cast<void(__fastcall*)(void*, float seconds, int pressed, int held)>(0x14088A1D0);
    auto Input_TakeRightClick = reinterpret_cast<int(__fastcall*)(void* input, unsigned mask)>(0x140800870);
    auto Input_RightHeldA = reinterpret_cast<int(__fastcall*)(void* input, unsigned mask)>(0x140800A20);
    auto Input_RightHeldB = reinterpret_cast<int(__fastcall*)(void* input, unsigned mask)>(0x140800A40);
    void* g_Full = nullptr;
    bool g_FullOpen = false;

    // ---- widget_User (Battle Pack +664, 344 bytes): the player's name, avatar and DP. CreateFromLayout(w, root, z, owner) puts it at the
    // top right (1887, 18); SetProfile(w, profile) fills it (OnEnter: -3 = the current profile), RefreshWallet(w) the DP.
    auto User_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x1408C0A60);
    auto User_Create = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*)>(0x1408C0C90);
    auto User_SetProfile = reinterpret_cast<void(__fastcall*)(void*, unsigned)>(0x1408C0EA0);
    auto User_RefreshWallet = reinterpret_cast<void(__fastcall*)(void*)>(0x1408C1070);

    // ---- widget_RecommendedList (deck editor +4664 / +42416, 1544 bytes): the related cards of a card (RelatedCardsPanel_SetCard with the
    // game's related-card table). Editor state 5 (DeckEdit_DeckInputHandler_B): ProcessInput(w, pressed, held, &kind, &flag, &action),
    // kind 3 = confirm, action -7 = the card's full details; GetSelected = the highlighted card.
    auto Related_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x1408AE6F0);
    auto Related_Create = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*, float, float)>(0x1408AEEA0);
    auto Related_SetCard = reinterpret_cast<void(__fastcall*)(void*, uint64_t count, const void* list, uint16_t card)>(0x1408AF3B0);
    auto Related_Count = reinterpret_cast<uint64_t(__fastcall*)(uint16_t)>(0x14076D640);
    auto Related_List = reinterpret_cast<const void*(__fastcall*)(uint16_t)>(0x14076D690);
    auto Related_Input = reinterpret_cast<char(__fastcall*)(void*, int pressed, int held, int* kind, char* flag, int* action)>(0x1408AE900);
    auto Related_Selected = reinterpret_cast<uint16_t(__fastcall*)(void*)>(0x1408AEE00);
    auto Related_DetailsCard = reinterpret_cast<uint16_t(__fastcall*)(void*)>(0x1408AEE10);

    // ---- widget_CommandMenu (deck editor +3272, 464 bytes): the pop-up list of choices (editor state 15). CreateFromLayout(w, root, 17,
    // owner, x, y); SetTitle(w, text); SetItems(w, vector of 24-byte {id, icon (-1 none), label, description}) copies them;
    // ProcessInput(w, pressed) answers the chosen id, -2 Back, -1 nothing yet (it reads Back and the mouse itself).
    struct PopupItem { int Id; int Icon; int64_t Label; int64_t Description; };
    auto Popup_Construct = reinterpret_cast<void*(__fastcall*)(void*)>(0x1408900A0);
    auto Popup_Create = reinterpret_cast<void(__fastcall*)(void*, R::SharedNode*, int, void*, float, float)>(0x140890890);
    auto Popup_SetTitle = reinterpret_cast<void(__fastcall*)(void*, int64_t)>(0x140891130);
    auto Popup_SetItems = reinterpret_cast<void(__fastcall*)(void*, R::Vector*)>(0x140891080);
    auto Popup_Input = reinterpret_cast<int64_t(__fastcall*)(void*, uint64_t pressed)>(0x1408903E0);
    void* g_Popup = nullptr;
    bool g_PopupOpen = false;
    std::wstring g_PopupTitle;
    std::deque<std::wstring> g_PopupTexts;    // the items' labels / descriptions, read by the game while the pop-up is open
    std::vector<PopupItem> g_PopupItems;
    json g_PopupActions;

    // the mouse wheel as button bits (Input+120 accumulates the wheel): the editor ORs (0x20000000, 0x40000000) in over the grid
    auto Input_TakeWheel = reinterpret_cast<int(__fastcall*)(void* input, unsigned up, unsigned down)>(0x140800900);

    template <typename T, typename Grow>
    void Push(R::Vector& vector, const T& value, Grow grow)
    {
        if (vector.End == vector.Capacity)
            grow(&vector, vector.End, &value);
        else
        {
            *static_cast<T*>(vector.End) = value;
            vector.End = static_cast<T*>(vector.End) + 1;
        }
    }

    bool SliderHit(void* tick, int64_t mouse)
    {
        return Layer_HitTest(static_cast<char*>(tick) + kTickBar, mouse);
    }

    // The level the mouse points at: tick i (949 + 49 * i in the bar's space, 36 wide) = level i + 1, left of the first tick = 0.
    int SliderLevelAt(void* tick, int64_t mouse)
    {
        float xy[2] = { static_cast<float>(static_cast<int32_t>(mouse)), static_cast<float>(static_cast<int32_t>(mouse >> 32)) };
        int64_t packed = 0;
        std::memcpy(&packed, xy, sizeof(packed));
        float local[2]{};
        Node_ToLocal(*reinterpret_cast<void**>(static_cast<char*>(tick) + 16), local, packed);
        return local[0] < kTickFirstX ? 0 : static_cast<int>((local[0] - kTickFirstX) / kTickStep) + 1;
    }

    // Memory for a game widget: zeroed, 16-byte aligned, kept as long as the game runs.
    void* NewWidget(size_t size)
    {
        void* memory = _aligned_malloc(size, 16);
        if (memory)
            std::memset(memory, 0, size);
        return memory;
    }

    void* WidgetNode(void* widget) { return *reinterpret_cast<void**>(static_cast<char*>(widget) + 16); }

    std::wstring Wide(const std::string& text)
    {
        int length = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring wide(length, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), length);
        return wide;
    }

    std::string Narrow(const std::wstring& text)
    {
        int length = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
        std::string narrow(length, '\0');
        WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), narrow.data(), length, nullptr, nullptr);
        return narrow;
    }

    std::string Str(const json& node, const char* key, const std::string& fallback = {})
    {
        auto it = node.find(key);
        if (it == node.end())
            return fallback;
        if (it->is_string())
            return it->get<std::string>();
        if (it->is_number_integer())
            return std::to_string(it->get<int64_t>());
        if (it->is_boolean())
            return it->get<bool>() ? "1" : "0";
        return fallback;
    }

    // node[key], or null when node is not an object or has no such key (list source rows have no JSON at all)
    const json& Field(const json& node, const char* key)
    {
        static const json none;
        if (!node.is_object())
            return none;
        auto it = node.find(key);
        return it != node.end() ? *it : none;
    }

    float Num(const json& node, const char* key, float fallback)
    {
        auto it = node.find(key);
        return it != node.end() && it->is_number() ? it->get<float>() : fallback;
    }

    // ---------------------------------------------------------------- values: page variables and Config.ini

    std::map<std::string, std::string> g_Vars;

    std::filesystem::path ConfigFile()
    {
        char exe[MAX_PATH]{};
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        return std::filesystem::path(exe).parent_path() / "Config.ini";
    }

    // "Section/Key"
    bool SplitConfig(const std::string& path, std::string& section, std::string& key)
    {
        const size_t slash = path.find('/');
        if (slash == std::string::npos || slash == 0 || slash + 1 >= path.size())
            return false;
        section = path.substr(0, slash);
        key = path.substr(slash + 1);
        return true;
    }

    std::string ReadConfig(const std::string& path)
    {
        std::string section, key;
        if (!SplitConfig(path, section, key))
            return {};
        char value[1024]{};
        GetPrivateProfileStringA(section.c_str(), key.c_str(), "", value, sizeof(value), ConfigFile().string().c_str());
        return value;
    }

    void WriteConfig(const std::string& path, const std::string& value)
    {
        std::string section, key;
        if (SplitConfig(path, section, key))
            WritePrivateProfileStringA(section.c_str(), key.c_str(), value.c_str(), ConfigFile().string().c_str());
    }

    // A binding: "var:name" or "config:Section/Key".
    // ---- the game's own values pages can read and change (docs/PageScript.md "Game"): the profile's DP and cards, guarded like the
    // Card Store reads them (the save sections are only there once a profile is loaded)
    constexpr int kSaveCardCount = 20000;   // the save's card table: one byte per card id, bits 0-2 = copies owned, bit 3 = seen
    constexpr int kFullSet = 3;

    uint64_t* GameWallet()
    {
        __try
        {
            uint8_t* section = YGO::SAVE::Get_PlayerSection(YGO::SAVE::CURRENT_PROFILE);
            return section ? reinterpret_cast<uint64_t*>(section + YGO::SAVE::PlayerSection::Wallet) : nullptr;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    uint8_t* GameCardTable()
    {
        __try
        {
            return YGO::SAVE::Get_CardUnlockTable(YGO::SAVE::CURRENT_PROFILE);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    // menu names for the unlock action / game:unlocked.<name> -> PlayerSection::MenuUnlock bits
    uint32_t MenuUnlockBit(const std::string& name)
    {
        if (name == "duelist_challenge") return YGO::SAVE::PlayerSection::DUELIST_CHALLENGE;
        if (name == "battle_packs") return YGO::SAVE::PlayerSection::BATTLE_PACKS;
        if (name == "card_shop") return YGO::SAVE::PlayerSection::CARD_SHOP;
        return 0;
    }

    uint8_t* GamePlayer()
    {
        __try
        {
            return YGO::SAVE::Get_PlayerSection(YGO::SAVE::CURRENT_PROFILE);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    const wchar_t* SafeCardName(short id)
    {
        __try
        {
            return reinterpret_cast<const wchar_t*>(YGO::CARDS::Get_CardNameFromKonamiId(id));
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    // ---- page values (docs/PageScript.md "Values and maths"): named texts a page file defines ("values": { "name": "text" }, BOB
    // `value name = "text";`), read as {value:name} and by name inside {= ...}. They are worked out when read, so a value that names
    // {var:card} follows the card. Shared by every page (a later definition replaces an earlier one).
    std::map<std::string, std::string> g_PageValues;
    std::string Expand(const std::string& text);

    // CARD_PROPS::Kind (File Type Libraries/CARD_Props)
    constexpr int kKindToken = 10, kKindSpell = 13, kKindTrap = 14, kKindLink = 42, kKindLinkEffect = 43;
    constexpr int kMaxStat = 5000;   // the highest real ATK / DEF; above it (or below 0) is "?"

    const YGO::CARDS::CARD_PROPS* SafeProps(short id)
    {
        __try
        {
            return YGO::CARDS::Get_CardPropsFromKonamiId(id);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    // The cards in a rare slot of any booster pack loaded now: g_PackRecords[128], 0x68 each, Kind at +12 ('R' = a booster), Contents at
    // +24 = u16 common count, u16 rare count, common ids, rare ids. Built when first asked for, again after a page opens.
    std::unordered_set<uint16_t> g_RareCards;
    bool g_RareCardsBuilt = false;

    bool IsRareCard(uint16_t id)
    {
        if (!g_RareCardsBuilt)
        {
            g_RareCards.clear();
            const auto* records = reinterpret_cast<const uint8_t*>(YGO::Addr::g_PackRecords);
            for (uint32_t i = 0; i < 128; ++i)
            {
                const uint8_t* record = records + i * 0x68;
                const uint16_t* contents = *reinterpret_cast<uint16_t* const*>(record + 24);
                if (*reinterpret_cast<const uint32_t*>(record + 12) != 'R' || !contents)
                    continue;
                const uint16_t* ids = contents + 2 + contents[0];
                g_RareCards.insert(ids, ids + contents[1]);
            }
            g_RareCardsBuilt = true;
        }
        return g_RareCards.count(id) != 0;
    }

    // game:atk.<id> ... : a card's own values. ATK / DEF are empty when they are "?" (game:known.<id> says which).
    std::string CardValue(const std::string& what, int id)
    {
        const YGO::CARDS::CARD_PROPS* card = id > 0 && id < 0x8000 ? SafeProps(static_cast<short>(id)) : nullptr;
        if (!card || card->KonamiID == 0)
            return {};
        const bool spell = card->Kind == kKindSpell, trap = card->Kind == kKindTrap;
        const bool link = card->Kind == kKindLink || card->Kind == kKindLinkEffect;
        auto stat = [](int v) { return v >= 0 && v <= kMaxStat ? std::to_string(v) : std::string(); };
        if (what == "atk") return stat(card->ATK);
        if (what == "def") return link ? std::string() : stat(card->DEF);
        if (what == "level") return std::to_string(card->Level);
        if (what == "kind") return std::to_string(card->Kind);
        if (what == "attribute") return std::to_string(card->Attribute);
        if (what == "type") return std::to_string(card->Type);
        if (what == "spell") return spell ? "1" : "0";
        if (what == "trap") return trap ? "1" : "0";
        if (what == "monster") return !spell && !trap && card->Kind != kKindToken ? "1" : "0";
        if (what == "link") return link ? "1" : "0";
        if (what == "known") return card->ATK >= 0 && card->ATK <= kMaxStat ? "1" : "0";
        if (what == "rare") return IsRareCard(static_cast<uint16_t>(id)) ? "1" : "0";
        if (what == "rarity") return IsRareCard(static_cast<uint16_t>(id)) ? "rare" : "common";
        return {};
    }

    // game:median.<value>.<rare|common>: the median of a page value over every monster with a known ATK of that pack rarity, worked out
    // with var:card set to each one in turn (the Card Store prices spells and traps like a typical monster). Kept until a page opens.
    std::map<std::string, std::string> g_Medians;

    std::string Median(const std::string& value, const std::string& rarity)
    {
        const std::string key = value + "." + rarity;
        if (auto it = g_Medians.find(key); it != g_Medians.end())
            return it->second;
        const bool wantRare = rarity == "rare";
        const std::string saved = g_Vars["card"];
        std::vector<double> numbers;
        auto propsOf = reinterpret_cast<YGO::CARDS::CARD_PROPS*(__fastcall*)(unsigned int)>(YGO::CARDS::Get_CardPropsFromInternalId);
        const uint32_t limit = R::Trunk::InternalIdLimit();
        for (uint32_t internal = 1; internal < limit; ++internal)
        {
            const YGO::CARDS::CARD_PROPS* card = propsOf(internal);
            if (!card || card->KonamiID == 0 || card->Kind == kKindToken || card->Kind == kKindSpell || card->Kind == kKindTrap ||
                card->ATK < 0 || card->ATK > kMaxStat || IsRareCard(static_cast<uint16_t>(card->KonamiID)) != wantRare)
                continue;
            g_Vars["card"] = std::to_string(card->KonamiID);
            const std::string number = Expand("{value:" + value + "}");
            if (!number.empty())
                numbers.push_back(std::atof(number.c_str()));
        }
        g_Vars["card"] = saved;
        std::string result;
        if (!numbers.empty())
        {
            std::nth_element(numbers.begin(), numbers.begin() + numbers.size() / 2, numbers.end());
            result = std::format("{}", numbers[numbers.size() / 2]);
        }
        Logger::WriteLog(std::format("Page values: median of '{}' over {} {} monsters = {}", value, numbers.size(), rarity, result), MODULE_NAME, 0);
        return g_Medians[key] = result;
    }

    // Forgets the rarities and medians (a page opened: packs.json or the values may have changed).
    void ResetComputed()
    {
        g_RareCardsBuilt = false;
        g_Medians.clear();
    }

    // Values plugins supply (RIX_RegisterPageValue): game:<name>.<argument> asks the plugin that registered <name>, e.g.
    // game:mymod.thing -> what the plugin that registered "mymod" answers for "thing".
    struct ValueSource { RIX_PageValueCallback Callback = nullptr; void* User = nullptr; };
    std::map<std::string, ValueSource> g_ValueSources;

    // game:dp, game:owned.<card id> (copies, 0-3), game:cardname.<card id>, game:unlocked.<menu>, game:character.<id> (1 = unlocked),
    // game:language (the game's language id), game:password.<8 digits> (the card id, 0 = none), then the plugins' values
    std::string GameValue(const std::string& name)
    {
        if (name.rfind("median.", 0) == 0)
        {
            const size_t dot = name.rfind('.');
            return dot > 7 ? Median(name.substr(7, dot - 7), name.substr(dot + 1)) : std::string();
        }
        {
            static const char* kCardValues[] = { "atk", "def", "level", "kind", "attribute", "type", "spell", "trap", "monster", "link", "known", "rare", "rarity" };
            const size_t dot = name.find('.');
            if (dot != std::string::npos)
                for (const char* what : kCardValues)
                    if (name.compare(0, dot, what) == 0)
                        return CardValue(what, std::atoi(name.c_str() + dot + 1));
        }
        if (name.rfind("password.", 0) == 0)
            return std::to_string(YGO::CARDS::FindCardByPassword(static_cast<uint32_t>(std::strtoul(name.c_str() + 9, nullptr, 10))));
        if (name.rfind("cardname.", 0) == 0)
        {
            const int id = std::atoi(name.c_str() + 9);
            if (id <= 0 || id >= kSaveCardCount)
                return {};
            const wchar_t* text = SafeCardName(static_cast<short>(id));
            return text ? Narrow(std::wstring(text)) : std::string();
        }
        if (name.rfind("unlocked.", 0) == 0)
        {
            const uint8_t* player = GamePlayer();
            const uint32_t bit = MenuUnlockBit(name.substr(9));
            return player && bit ? ((*reinterpret_cast<const uint32_t*>(player + YGO::SAVE::PlayerSection::MenuUnlockFlags) & bit) ? "1" : "0") : std::string();
        }
        if (name.rfind("character.", 0) == 0)
        {
            const uint8_t* player = GamePlayer();
            const int id = std::atoi(name.c_str() + 10);
            if (!player || id < 0 || id >= static_cast<int>(YGO::SAVE::PlayerSection::CharacterCount))
                return {};
            return (player[YGO::SAVE::PlayerSection::UnlockedCharacters + id / 8] >> (id % 8)) & 1 ? "1" : "0";
        }
        if (name == "language")
            return std::to_string(*reinterpret_cast<const int*>(YGO::Addr::g_iGameLanguageID));
        const size_t dot = name.find('.');
        const auto source = g_ValueSources.find(name.substr(0, dot));
        if (source != g_ValueSources.end())
        {
            char buffer[512] = {};
            const int length = source->second.Callback(dot == std::string::npos ? "" : name.c_str() + dot + 1, buffer, sizeof(buffer), source->second.User);
            return length > 0 ? std::string(buffer, (std::min)(static_cast<size_t>(length), sizeof(buffer) - 1)) : std::string();
        }
        if (name == "dp")
        {
            const uint64_t* wallet = GameWallet();
            return wallet ? std::to_string(*wallet) : std::string();
        }
        if (name.rfind("owned.", 0) == 0)
        {
            const int id = std::atoi(name.c_str() + 6);
            const uint8_t* table = GameCardTable();
            return table && id > 0 && id < kSaveCardCount ? std::to_string(table[id] & 7) : std::string();
        }
        return {};
    }

    struct Binding
    {
        enum Kind { None, Var, Config, Game, Value } Type = None;
        std::string Name;

        static Binding Parse(const std::string& text)
        {
            Binding b;
            if (text.rfind("var:", 0) == 0) { b.Type = Var; b.Name = text.substr(4); }
            else if (text.rfind("config:", 0) == 0) { b.Type = Config; b.Name = text.substr(7); }
            else if (text.rfind("game:", 0) == 0) { b.Type = Game; b.Name = text.substr(5); }
            else if (text.rfind("value:", 0) == 0) { b.Type = Value; b.Name = text.substr(6); }
            return b;
        }

        std::string Get(const std::string& fallback = {}) const
        {
            std::string value;
            if (Type == Var) { auto it = g_Vars.find(Name); value = it != g_Vars.end() ? it->second : std::string(); }
            else if (Type == Config) value = ReadConfig(Name);
            else if (Type == Game) value = GameValue(Name);
            else if (Type == Value)
            {
                static int depth = 0;   // a value naming itself would never end
                auto it = g_PageValues.find(Name);
                if (it != g_PageValues.end() && depth < 32)
                {
                    ++depth;
                    value = Expand(it->second);
                    --depth;
                }
            }
            return value.empty() ? fallback : value;
        }

        void Set(const std::string& value) const
        {
            if (Type == Var) g_Vars[Name] = value;
            else if (Type == Config) WriteConfig(Name, value);
            // game: values are read only; the giveDp / takeDp / giveCard actions change them
        }
    };

    // {= ...}: + - * / ^ ( ), comparisons (< <= > >= == != give 1 or 0), "and" / "or" / "not", numbers, page values by name (ATK = the
    // value ATK) and min, max, clamp(x, lo, hi), round, floor, ceil, abs, exp, log, sqrt, if(test, then, else). Values inside are expanded
    // first, so {= {game:atk.{var:card}} * 1.8 / 5} works too. Empty = 0.
    class Maths
    {
    public:
        explicit Maths(const std::string& text) : m_Text(text) {}

        bool Evaluate(double& result)
        {
            result = Or();
            Skip();
            return m_Ok && m_At == m_Text.size();
        }

    private:
        const std::string& m_Text;
        size_t m_At = 0;
        bool m_Ok = true;

        void Skip() { while (m_At < m_Text.size() && std::isspace(static_cast<unsigned char>(m_Text[m_At]))) ++m_At; }

        bool Take(const char* token)
        {
            Skip();
            const size_t length = std::strlen(token);
            if (m_Text.compare(m_At, length, token) != 0)
                return false;
            // a word must end there ("and" isn't the start of "android")
            if (std::isalpha(static_cast<unsigned char>(token[0])) && m_At + length < m_Text.size() &&
                (std::isalnum(static_cast<unsigned char>(m_Text[m_At + length])) || m_Text[m_At + length] == '_'))
                return false;
            m_At += length;
            return true;
        }

        double Or()
        {
            double value = And();
            while (Take("or") || Take("||"))
                value = (And() != 0 || value != 0) ? 1 : 0;
            return value;
        }

        double And()
        {
            double value = Compare();
            while (Take("and") || Take("&&"))
            {
                const double right = Compare();
                value = (value != 0 && right != 0) ? 1 : 0;
            }
            return value;
        }

        double Compare()
        {
            const double left = Sum();
            if (Take("<=")) return left <= Sum() ? 1 : 0;
            if (Take(">=")) return left >= Sum() ? 1 : 0;
            if (Take("==")) return left == Sum() ? 1 : 0;
            if (Take("!=")) return left != Sum() ? 1 : 0;
            if (Take("<")) return left < Sum() ? 1 : 0;
            if (Take(">")) return left > Sum() ? 1 : 0;
            return left;
        }

        double Sum()
        {
            double value = Product();
            for (;;)
            {
                if (Take("+")) value += Product();
                else if (Take("-")) value -= Product();
                else return value;
            }
        }

        double Product()
        {
            double value = Power();
            for (;;)
            {
                if (Take("*")) value *= Power();
                else if (Take("/")) { const double by = Power(); value = by == 0 ? 0 : value / by; }
                else if (Take("%")) { const double by = Power(); value = by == 0 ? 0 : std::fmod(value, by); }
                else return value;
            }
        }

        double Power()
        {
            const double base = Unary();
            return Take("^") ? std::pow(base, Power()) : base;
        }

        double Unary()
        {
            if (Take("not") || Take("!")) return Unary() == 0 ? 1 : 0;
            if (Take("-")) return -Unary();
            if (Take("+")) return Unary();
            return Atom();
        }

        double Atom()
        {
            Skip();
            if (Take("("))
            {
                const double value = Or();
                if (!Take(")")) m_Ok = false;
                return value;
            }
            const size_t start = m_At;
            if (m_At < m_Text.size() && (std::isdigit(static_cast<unsigned char>(m_Text[m_At])) || m_Text[m_At] == '.'))
            {
                while (m_At < m_Text.size() && (std::isdigit(static_cast<unsigned char>(m_Text[m_At])) || m_Text[m_At] == '.')) ++m_At;
                return std::atof(m_Text.substr(start, m_At - start).c_str());
            }
            while (m_At < m_Text.size() && (std::isalnum(static_cast<unsigned char>(m_Text[m_At])) || m_Text[m_At] == '_' || m_Text[m_At] == '.')) ++m_At;
            const std::string name = m_Text.substr(start, m_At - start);
            if (name.empty())
            {
                m_Ok = false;
                return 0;
            }
            if (!Take("("))
            {
                // a page value by name
                const std::string text = Binding{ Binding::Value, name }.Get();
                return std::atof(text.c_str());
            }
            std::vector<double> args{ Or() };
            while (Take(",")) args.push_back(Or());
            if (!Take(")")) m_Ok = false;
            const double a = args[0];
            auto arg = [&](size_t i) { return i < args.size() ? args[i] : 0.0; };
            if (name == "min") return *std::min_element(args.begin(), args.end());
            if (name == "max") return *std::max_element(args.begin(), args.end());
            if (name == "clamp") return std::clamp(a, arg(1), (std::max)(arg(1), arg(2)));
            if (name == "if") return a != 0 ? arg(1) : arg(2);
            if (name == "round") return std::round(a);
            if (name == "floor") return std::floor(a);
            if (name == "ceil") return std::ceil(a);
            if (name == "abs") return std::fabs(a);
            if (name == "exp") return std::exp(a);
            if (name == "log") return a > 0 ? std::log(a) : 0;
            if (name == "sqrt") return a > 0 ? std::sqrt(a) : 0;
            m_Ok = false;
            return 0;
        }
    };

    std::string Calculate(const std::string& expression)
    {
        double result = 0;
        if (!Maths(expression).Evaluate(result) || !std::isfinite(result))
        {
            static std::set<std::string> logged;
            if (logged.insert(expression).second)
                Logger::WriteLog(std::format("Page maths can't be read: {}", expression), MODULE_NAME, 1);
            return {};
        }
        if (result == std::floor(result) && std::fabs(result) < 1e15)
            return std::to_string(static_cast<long long>(result));
        std::string text = std::format("{:.6f}", result);
        text.erase(text.find_last_not_of('0') + 1);
        if (!text.empty() && text.back() == '.') text.pop_back();
        return text;
    }

    // "{var:name}" / "{config:Section/Key}" / "{game:...}" in text -> the value. Names may hold values of their own:
    // "{game:cardname.{var:card}}" reads var:card first.
    std::string Expand(const std::string& text)
    {
        std::string out;
        for (size_t i = 0; i < text.size();)
        {
            if (text[i] == '{')
            {
                size_t end = i + 1;
                for (int depth = 1; end < text.size(); ++end)
                {
                    if (text[end] == '{') ++depth;
                    else if (text[end] == '}' && --depth == 0) break;
                }
                if (end < text.size())
                {
                    const std::string inner = text.substr(i + 1, end - i - 1);
                    if (!inner.empty() && inner[0] == '=')
                    {
                        out += Calculate(Expand(inner.substr(1)));
                        i = end + 1;
                        continue;
                    }
                    // {name|fallback}: the fallback (expanded) when the value is empty
                    size_t bar = std::string::npos;
                    for (size_t k = 0, depth = 0; k < inner.size(); ++k)
                    {
                        if (inner[k] == '{') ++depth;
                        else if (inner[k] == '}') --depth;
                        else if (inner[k] == '|' && depth == 0) { bar = k; break; }
                    }
                    const std::string head = bar == std::string::npos ? inner : inner.substr(0, bar);
                    Binding b = Binding::Parse(head.find('{') != std::string::npos ? Expand(head) : head);
                    if (b.Type != Binding::None)
                    {
                        std::string value = b.Get();
                        if (value.empty() && bar != std::string::npos)
                            value = Expand(inner.substr(bar + 1));
                        out += value;
                        i = end + 1;
                        continue;
                    }
                }
            }
            out += text[i++];
        }
        return out;
    }

    // { "var" | "config" | "game": name, "equals": value, "atLeast": n, "not": true } ; an array = all of them. No "equals" / "atLeast" = set
    // and not "0".
    bool Holds(const json& when)
    {
        if (when.is_null())
            return true;
        if (when.is_array())
        {
            for (const json& part : when)
                if (!Holds(part))
                    return false;
            return true;
        }
        if (!when.is_object())
            return true;
        std::string value;
        if (when.contains("var")) value = Binding{ Binding::Var, Expand(Str(when, "var")) }.Get();
        else if (when.contains("config")) value = Binding{ Binding::Config, Expand(Str(when, "config")) }.Get();
        else if (when.contains("game")) value = GameValue(Expand(Str(when, "game")));
        else if (when.contains("value")) value = Binding{ Binding::Value, Expand(Str(when, "value")) }.Get();
        bool result;
        if (when.contains("atLeast"))
            result = !value.empty() && std::strtoll(value.c_str(), nullptr, 10) >= std::strtoll(Expand(when["atLeast"].is_string() ? when["atLeast"].get<std::string>() : when["atLeast"].dump()).c_str(), nullptr, 10);
        else
            result = when.contains("equals") ? value == Expand(Str(when, "equals")) : !value.empty() && value != "0";
        return when.value("not", false) ? !result : result;
    }

    // ---------------------------------------------------------------- list sources plugins register

    struct ListItem { std::wstring Label, Description; std::string Value; };
    struct ListSource { RIX_PageListCallback Callback = nullptr; void* User = nullptr; };
    std::map<std::string, ListSource> g_Sources;

    // ---------------------------------------------------------------- pages

    struct Page;

    struct MenuItem
    {
        int Id = 0;
        std::wstring Label, Description;     // what the game's item points at (kept alive with the page)
        json Source;                         // the item's JSON (label, action, values, bind, visible, ...)
        std::string Value;                   // a list source's value
        bool Choice = false;                 // values cycle with left / right / confirm
    };

    struct Element
    {
        json Source;
        std::string Kind, Id;
        float X = 0, Y = 0, Width = 0, Height = 0;
        int Z = 0;
        bool Focusable = false;

        // decoration (image / text)
        R::SharedNode Node{};
        std::wstring Text;                   // the text node reads it
        std::string Shown;                   // the expanded text it was built with

        // game widgets
        void* Menu = nullptr;
        R::SharedNode MenuNode{};
        std::vector<MenuItem> Items;
        std::vector<R::MainMenuItemDef> Table;
        void* Tick = nullptr;
        std::vector<void*> Digits;
        int DigitFocus = 0;
        void* CardInfo = nullptr;
        void* Series = nullptr;
        R::Vector SeriesList{};
        void* Chars = nullptr;
        R::Vector CharList{};
        std::string CharSeries = "\x1";      // the series the list was built for
        void* Decks = nullptr;
        void* DeckInfo = nullptr;
        std::string ShownDeck = "\x1";
        void* Trunk = nullptr;
        void* DeckState = nullptr;
        void* Filter = nullptr;
        void* User = nullptr;
        void* Related = nullptr;
        std::string RelatedCard = "\x1";
        void* CardBox = nullptr;
        std::string BoxCards = "\x1";        // the expanded "cards" the box was filled with
        bool FilterOpen = false;
        std::string ShownCards;              // the "cards" value the grid was filled with
        int Selected = -1;                   // the character / deck / card last written to the binding
        int ShownCard = -1;
        Binding Bind;
    };

    struct Page
    {
        std::string Name;
        json Root;
        std::wstring Header;
        std::deque<Element> Elements;        // stable addresses: the game's widgets live in them
        int Focus = -1;
        bool Built = false;
        std::wstring HelpText;               // the description last shown on the help bar
    };

    void* g_Screen = nullptr;
    bool g_Failed = false;
    void* g_Vftable[1 + kVftableSlots];
    std::vector<std::unique_ptr<Page>> g_Stack;
    std::vector<std::unique_ptr<Page>> g_Retired;   // closed pages: the game's widgets point into them
    bool g_HeaderBuilt = false;
    bool g_Shown = false;                           // the top page's widgets are on screen
    void* g_PendingFrom = nullptr;                  // a screen change to the page screen that has to wait for the running one
    int g_PendingFrames = 0;

    Page* Top() { return g_Stack.empty() ? nullptr : g_Stack.back().get(); }

    // The full card view over the page (built the first time it is needed). Ids outside the game's card range play the "unable" sound,
    // as DeckEdit_OpenCardDetails does.
    void ShowFullCard(int card)
    {
        if (card < 3900 || card > 0x3A78 || !g_Screen)
        {
            R::PlayUISound(kSoundError);
            return;
        }
        if (!g_Full)
        {
            g_Full = NewWidget(2048);
            Full_Construct(g_Full);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(g_Screen));
            Full_Create(g_Full, &parent, 60, R::ScreenOwner(g_Screen), 33.0f, 18.0f);
        }
        R::PlayUISound(kSoundConfirm);
        Full_SetCard(g_Full, static_cast<uint16_t>(card));
        R::WidgetSetVisible(g_Full, true);
        g_FullOpen = true;
    }

    void ClosePopup()
    {
        if (g_Popup)
        {
            R::WidgetSetFocused(g_Popup, false);
            R::WidgetSetVisible(g_Popup, false);
        }
        g_PopupOpen = false;
    }

    // { "popup": { "title": "...", "x": 960, "y": 400, "items": [ { "label", "description", "action" } ] } }
    void OpenPopup(const json& spec)
    {
        if (!g_Screen || !spec.is_object())
            return;
        if (!g_Popup)
        {
            g_Popup = NewWidget(2048);
            Popup_Construct(g_Popup);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(g_Screen));
            Popup_Create(g_Popup, &parent, 55, R::ScreenOwner(g_Screen), 0.0f, 0.0f);
        }
        g_PopupTexts.clear();
        g_PopupItems.clear();
        g_PopupActions = json::array();
        const json& items = Field(spec, "items");
        for (size_t i = 0; items.is_array() && i < items.size(); ++i)
        {
            const std::wstring& label = g_PopupTexts.emplace_back(Wide(Expand(Str(items[i], "label"))));
            const std::wstring& description = g_PopupTexts.emplace_back(Wide(Expand(Str(items[i], "description"))));
            g_PopupItems.push_back({ static_cast<int>(i), -1, reinterpret_cast<int64_t>(label.c_str()), reinterpret_cast<int64_t>(description.c_str()) });
            g_PopupActions.push_back(Field(items[i], "action"));
        }
        if (g_PopupItems.empty())
            return;
        g_PopupTitle = Wide(Expand(Str(spec, "title")));
        R::NodeSetPosition(WidgetNode(g_Popup), Num(spec, "x", 960), Num(spec, "y", 400));
        Popup_SetTitle(g_Popup, reinterpret_cast<int64_t>(g_PopupTitle.c_str()));
        R::Vector items_{ g_PopupItems.data(), g_PopupItems.data() + g_PopupItems.size(), g_PopupItems.data() + g_PopupItems.size() };
        Popup_SetItems(g_Popup, &items_);
        R::WidgetSetVisible(g_Popup, true);
        R::WidgetSetFocused(g_Popup, true);
        g_PopupOpen = true;
        R::PlayUISound(kSoundConfirm);
    }

    void HideFullCard()
    {
        if (g_Full)
            R::WidgetSetVisible(g_Full, false);
        g_FullOpen = false;
    }

    std::filesystem::path PageFile(const std::string& name)
    {
        std::filesystem::path path = YGO::Mods::Find("pages\\" + name + ".json");
        if (path.empty())
        {
            char exe[MAX_PATH]{};
            GetModuleFileNameA(nullptr, exe, MAX_PATH);
            path = std::filesystem::path(exe).parent_path() / "Yu-Gi-Oh-Ex" / "pages" / (name + ".json");
        }
        return path;
    }

    bool ReadJson(const std::filesystem::path& path, json& root)
    {
        try
        {
            std::ifstream stream(path);
            if (!stream)
                return false;
            root = json::parse(stream, nullptr, true, true);
            return true;
        }
        catch (const std::exception& e)
        {
            Logger::WriteLog(std::format("Page {}: {}", path.filename().string(), e.what()), MODULE_NAME, 2);
            return false;
        }
    }

    void RunAction(const json& action);

    // ---------------------------------------------------------------- the header and the help bar

    void ShowHeader(const Page& page)
    {
        char* screen = static_cast<char*>(g_Screen);
        if (!g_HeaderBuilt)
        {
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            TextScroll_CreatePart2(screen + kHeader, &parent, 0, R::ScreenOwner(screen), 960.0f, 0.0f, 475);
            g_HeaderBuilt = true;
        }
        R::SetHeaderText(screen, reinterpret_cast<int64_t>(page.Header.c_str()));
    }

    void ShowHelp(Page& page, const std::wstring& description)
    {
        void* help = static_cast<char*>(g_Screen) + kHelp;
        if (description != page.HelpText)
        {
            page.HelpText = description;
            alignas(16) char text[32]{};   // a std::wstring; widget_Help takes it over (the Battle Pack screen hands it one per frame)
            WString_FromChars(text, page.HelpText.c_str());
            Help_SetDescription(help, text);
        }
    }

    void ResetHelp(Page& page)
    {
        void* help = static_cast<char*>(g_Screen) + kHelp;
        R::HelpClear(help);
        R::HelpAdd(help, reinterpret_cast<const R::HelpEntry*>(kHelpSelect), 1);
        R::HelpAdd(help, reinterpret_cast<const R::HelpEntry*>(kHelpBack), 1);
        R::HelpLayout(help);
        page.HelpText = L"\x1";   // forces the next description
    }

    // ---------------------------------------------------------------- menus (RIX::MenuKit::menu)

    // The listener ProcessInput calls: slot 0 OnItemActivated(listener, ui, id, profile) (id -2 = Back), slot 1 IsItemEnabled(listener, id).
    struct MenuListener
    {
        void** Vftable;
        Element* Owner;
    };

    void ActivateItem(Element& element, int id);
    void GoBack();

    void __fastcall Listener_Activated(MenuListener* self, int64_t, int id, unsigned)
    {
        if (id == -2)
            GoBack();
        else
            ActivateItem(*self->Owner, id);
    }

    char __fastcall Listener_Enabled(MenuListener* self, int id)
    {
        for (const MenuItem& item : self->Owner->Items)
            if (item.Id == id)
                return Holds(Field(item.Source, "enabled")) ? 1 : 0;
        return 0;
    }

    void* g_ListenerVftable[2] = { reinterpret_cast<void*>(&Listener_Activated), reinterpret_cast<void*>(&Listener_Enabled) };

    MenuItem* FindItem(Element& element, int id)
    {
        for (MenuItem& item : element.Items)
            if (item.Id == id)
                return &item;
        return nullptr;
    }

    // A choice item's values: [ "a", "b" ] or [ { "label": "Easy", "value": "0" } ], or "toggle": true (Off / On = 0 / 1).
    std::vector<std::pair<std::string, std::string>> ChoiceValues(const json& item)
    {
        std::vector<std::pair<std::string, std::string>> values;
        if (item.value("toggle", false))
            return { { "Off", "0" }, { "On", "1" } };
        if (item.contains("values") && item["values"].is_array())
            for (const json& v : item["values"])
            {
                if (v.is_object())
                    values.emplace_back(Str(v, "label", Str(v, "value")), Str(v, "value", Str(v, "label")));
                else if (v.is_string() || v.is_number())
                    values.emplace_back(Str(json{ { "v", v } }, "v"), Str(json{ { "v", v } }, "v"));
            }
        return values;
    }

    int ChoiceIndex(const MenuItem& item)
    {
        const auto values = ChoiceValues(item.Source);
        const std::string current = Binding::Parse(Str(item.Source, "bind")).Get(Str(item.Source, "default"));
        for (size_t i = 0; i < values.size(); ++i)
            if (values[i].second == current)
                return static_cast<int>(i);
        return 0;
    }

    std::wstring ItemLabel(const MenuItem& item)
    {
        if (!item.Choice)
            return Wide(Expand(Narrow(item.Label)));
        const auto values = ChoiceValues(item.Source);
        const std::string shown = values.empty() ? std::string() : values[ChoiceIndex(item)].first;
        return Wide(Expand(Str(item.Source, "label")) + ": " + shown);
    }

    // Writes every item's label / description pointer into the game's item and shows the visible ones.
    void RefreshMenu(Element& element, bool keepCursor)
    {
        if (!element.Menu)
            return;
        char* menu = static_cast<char*>(element.Menu);
        char* begin = *reinterpret_cast<char**>(menu + 128);
        char* end = *reinterpret_cast<char**>(menu + 136);
        const unsigned cursor = Menu_Cursor(element.Menu);

        for (char* gameItem = begin; gameItem && gameItem < end; gameItem += kMenuItemSize)
        {
            MenuItem* item = FindItem(element, *reinterpret_cast<int*>(gameItem + kMenuItemId));
            if (!item)
                continue;
            if (item->Choice || item->Source.contains("label"))
                item->Label = ItemLabel(*item);
            *reinterpret_cast<int64_t*>(gameItem + kMenuItemLabel) = reinterpret_cast<int64_t>(item->Label.c_str());
            *reinterpret_cast<int64_t*>(gameItem + kMenuItemDescription) = reinterpret_cast<int64_t>(item->Description.c_str());
        }

        R::MenuKit::ClearVisible(element.Menu);
        for (const MenuItem& item : element.Items)
            if (Holds(Field(item.Source, "visible")))
                R::MenuKit::ShowItemNow(element.Menu, static_cast<unsigned>(item.Id));
        R::MenuKit::Layout(element.Menu);
        if (keepCursor)
            R::MenuKit::SelectIndex(element.Menu, cursor);
    }

    // Items: "items" in the file, then a list source's rows ("source": name).
    void CollectItems(Element& element)
    {
        int id = 1;
        if (element.Source.contains("items") && element.Source["items"].is_array())
            for (const json& source : element.Source["items"])
            {
                if (id >= 100)   // ids 100+ are Help & Options' own (MainMenu.cpp's CreateFromLayout hook)
                    break;
                MenuItem item;
                item.Id = id++;
                item.Source = source;
                item.Choice = source.contains("values") || source.value("toggle", false);
                item.Label = Wide(Expand(Str(source, "label")));
                item.Description = Wide(Expand(Str(source, "description")));
                element.Items.push_back(std::move(item));
            }

        const std::string sourceName = Str(element.Source, "source");
        if (!sourceName.empty())
        {
            auto it = g_Sources.find(sourceName);
            if (it == g_Sources.end())
                Logger::WriteLog(std::format("Page list source '{}' is not registered by any plugin", sourceName), MODULE_NAME, 1);
            else
            {
                std::vector<ListItem> rows;
                it->second.Callback(&rows, it->second.User);
                for (ListItem& row : rows)
                {
                    if (id >= 100)
                        break;
                    MenuItem item;
                    item.Id = id++;
                    item.Label = std::move(row.Label);
                    item.Description = std::move(row.Description);
                    item.Value = std::move(row.Value);
                    element.Items.push_back(std::move(item));
                }
            }
        }
    }

    void BuildMenu(Element& element)
    {
        CollectItems(element);
        if (element.Items.empty())
            return;

        // The definition table lists the items; MenuKit lays the visible ones out in table order from StartY down, Spacing apart.
        for (const MenuItem& item : element.Items)
            element.Table.push_back({ item.Id, reinterpret_cast<int64_t>(item.Label.c_str()), reinterpret_cast<int64_t>(item.Description.c_str()) });
        element.Table.push_back({ -1, 0, 0 });

        element.Menu = NewWidget(256);
        Menu_Construct(element.Menu);
        Menu_SetDefinition(element.Menu, element.Table.data());
        Menu_ShowAll(element.Menu);

        // The menu's own node under the screen's root (as SetupWidgets does): the items are placed relative to it.
        R::SharedNode node{};
        TBase_MakeShared(&node);
        R::SharedNode child = R::ParentRef(&node);
        TBase_AddChild(R::ScreenRoot(g_Screen)->Node, &child);
        TBase_SetZ(node.Node, 3 + element.Z);
        R::NodeSetPosition(node.Node, element.X + element.Width / 2.0f, 0.0f);
        element.MenuNode = node;

        const float spacing = Num(element.Source, "spacing", 100.0f);
        const MenuLayout layout{ element.Y + spacing / 2.0f, spacing, 0.0f, 3 + element.Z };
        R::SharedNode parent = R::ParentRef(&element.MenuNode);   // CreateItems releases it
        Menu_CreateItems(element.Menu, &parent, &layout, R::ScreenOwner(g_Screen), R::ScreenData(g_Screen));
        RefreshMenu(element, false);
        R::MenuKit::SelectIndex(element.Menu, 0);
        R::MenuKit::ResetItems(element.Menu);
    }

    void ChangeChoice(Element& element, MenuItem& item, int by)
    {
        const auto values = ChoiceValues(item.Source);
        if (values.empty())
            return;
        const int index = (ChoiceIndex(item) + by + static_cast<int>(values.size())) % static_cast<int>(values.size());
        Binding::Parse(Str(item.Source, "bind")).Set(values[index].second);
        R::PlayUISound(kSoundStep);
        RefreshMenu(element, true);
        if (item.Source.contains("onChange"))
            RunAction(item.Source["onChange"]);
    }

    void ActivateItem(Element& element, int id)
    {
        MenuItem* item = FindItem(element, id);
        if (!item)
            return;
        if (item->Choice)
        {
            ChangeChoice(element, *item, 1);
            return;
        }
        R::PlayUISound(kSoundConfirm);
        if (!element.Id.empty())
            g_Vars[element.Id] = item->Value.empty() ? Narrow(item->Label) : item->Value;
        const json action = item->Source.contains("action") ? item->Source["action"] : element.Source.value("action", json());
        if (!action.is_null())
            RunAction(action);
    }

    // ---------------------------------------------------------------- slider (tickLine_t)

    int SliderLevel(const Element& element)
    {
        const float min = Num(element.Source, "min", 0), max = Num(element.Source, "max", 10);
        const float value = std::strtof(element.Bind.Get(Str(element.Source, "default", "0")).c_str(), nullptr);
        return max > min ? std::clamp(static_cast<int>((value - min) * 10.0f / (max - min) + 0.5f), 0, 10) : 0;
    }

    void ShowSlider(Element& element)
    {
        if (!element.Tick)
            return;
        const int level = SliderLevel(element);
        char* tick = static_cast<char*>(element.Tick);
        *reinterpret_cast<int*>(tick + kTickLevel) = level;
        for (int i = 0; i < 10; ++i)
            if (void* node = *reinterpret_cast<void**>(tick + kTickNodes + kTickStride * i))
                Node_SetEnabled(node, i < level);
    }

    void SetSlider(Element& element, int level)
    {
        level = std::clamp(level, 0, 10);
        if (level == SliderLevel(element))
            return;
        const float min = Num(element.Source, "min", 0), max = Num(element.Source, "max", 10);
        const float value = min + (max - min) * level / 10.0f;
        element.Bind.Set(value == static_cast<int>(value) ? std::to_string(static_cast<int>(value)) : std::format("{}", value));
        R::PlayUISound(kSoundStep);
        ShowSlider(element);
        if (element.Source.contains("onChange"))
            RunAction(element.Source["onChange"]);
    }

    // ---------------------------------------------------------------- building and showing a page

    void BuildDecoration(Element& element)
    {
        const R::SharedNode* root = R::ScreenRoot(g_Screen);
        if (element.Kind == "image")
        {
            element.Node = R::Dfx::AddImage(root, Str(element.Source, "resource").c_str(), Str(element.Source, "sprite").c_str(), 20 + element.Z,
                                            element.X, element.Y, element.Width, element.Height);
            if (element.Node.Node && element.Source.contains("alpha"))
                R::Dfx::SetAlpha(element.Node.Node, Num(element.Source, "alpha", 1.0f));
        }
        else if (element.Kind == "slider" && element.Source.contains("label"))
        {
            // where the Settings screen puts its row labels (its text descriptors at 0x140C8E8C0): right-aligned, ending at x 589 of the
            // bar's own space, 61 px above the bar's y, in the 32 px font
            element.Text = Wide(Expand(Str(element.Source, "label")));
            element.Node = R::Dfx::AddText(R::ScreenRoot(g_Screen), element.Text.c_str(), 20 + element.Z, element.X - kTickFirstX + 589.0f,
                                           element.Y - 61.0f - 16.0f, 0, 32, R::Dfx::TextAlign::Right);
        }
        else if (element.Kind == "text")
        {
            element.Shown = Expand(Str(element.Source, "text"));
            element.Text = Wide(element.Shown);
            const std::string align = Str(element.Source, "align");
            const auto alignment = align == "center" || align == "centre" ? R::Dfx::TextAlign::Centre : align == "right" ? R::Dfx::TextAlign::Right : R::Dfx::TextAlign::Left;
            uint32_t colour = 0xFFFFFFFF;
            const std::string hex = Str(element.Source, "colour");
            if ((hex.size() == 7 || hex.size() == 9) && hex[0] == '#')
                colour = static_cast<uint32_t>(std::stoul(hex.substr(1), nullptr, 16)) | (hex.size() == 7 ? 0xFF000000u : 0u);
            element.Node = R::Dfx::AddText(root, element.Text.c_str(), 20 + element.Z, element.X, element.Y, element.Width, Num(element.Source, "textSize", 30), alignment, colour);
        }
    }

    void RemoveDecoration(Element& element)
    {
        R::Dfx::RemoveImage(R::ScreenRoot(g_Screen), element.Node);
    }

    // The opponents of a series, as FreeDuel_BuildOpponentList lists them: defined, selectable, in the series, sku owned, and their deck is
    // a defined deck they own; "unlocked" from the player section (+56 + 4 * deck).
    void FillCharacters(Element& element)
    {
        element.CharSeries = Expand(Str(element.Source, "series", "0"));
        const int series = std::atoi(element.CharSeries.c_str());
        const char* section = Get_PlayerSection(0xFFFFFFFD);
        element.CharList.End = element.CharList.Begin;
        for (int character = 0; character < kCharacterCount; ++character)
        {
            const char* record = reinterpret_cast<const char*>(kCharacterRecords) + kCharacterSize * character;
            if (!Character_IsDefined(character) || !Character_IsSelectable(character) || *reinterpret_cast<const int*>(record + 4) != series
                || !Sku_IsOwned(*reinterpret_cast<const int*>(record + 16)))
                continue;
            const int deck = *reinterpret_cast<const int*>(record + 8);
            if (!Deck_IsDefined(deck) || Deck_OwnerCharacter(deck) != character)
                continue;
            const int unlocked = section && deck >= 0 && deck < 700 ? *reinterpret_cast<const int*>(section + 56 + 4 * deck) : 0;
            const int64_t pair = static_cast<uint32_t>(character) | (static_cast<int64_t>(unlocked) << 32);
            Push(element.CharList, pair, VectorGrowPair);
        }
        Chars_SetList(element.Chars, &element.CharList);
    }

    uint8_t* SaveCardTable()
    {
        __try { return YGO::SAVE::Get_CardUnlockTable(YGO::SAVE::CURRENT_PROFILE); }
        __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
    }

    // The grid's own copy of the counts (and the numbers on screen) from the save: the game's count function answers 0 above id 14968
    // (BetterCardShop StorePages.cpp found this).
    void RefreshGridCounts(char* trunk)
    {
        const uint8_t* owned = SaveCardTable();
        auto ownedOf = [owned](uint16_t id) { return owned && id < 20000 ? owned[id] & 7 : 0; };
        auto* counts = reinterpret_cast<R::Vector*>(trunk + R::Trunk::GridCounts);
        for (auto* count = static_cast<R::Trunk::GridCount*>(counts->Begin); count && count < counts->End; ++count)
            count->Owned = ownedOf(count->CardId);
        auto* cells = reinterpret_cast<R::Vector*>(trunk + R::Trunk::GridCells);
        for (char* cell = static_cast<char*>(cells->Begin); cell && cell < cells->End; cell += R::Trunk::CellSize)
            if (const uint16_t id = *reinterpret_cast<uint16_t*>(cell + R::Trunk::CellCardId); id != 0xFFFF)
                R::Trunk::CellSetOwned(cell, ownedOf(id));
    }

    // The cards, built the way TrunkView_BuildCardList does (internal ids 1 up to the game's bound, no tokens). "cards": "owned" (default:
    // the cards the profile has, as the deck editor) or "all"; the number on each card is the copies owned.
    void FillCards(Element& element)
    {
        char* trunk = static_cast<char*>(element.Trunk);
        element.ShownCards = Expand(Str(element.Source, "cards", "owned"));
        const bool all = element.ShownCards == "all";
        R::Trunk::GridReset(trunk + R::Trunk::Grid, 0);
        R::Trunk::GridSetMode(trunk + R::Trunk::Grid, 10);
        auto* index = reinterpret_cast<R::Vector*>(trunk + R::Trunk::IdToEntry);
        auto* entries = reinterpret_cast<R::Vector*>(trunk + R::Trunk::Entries);
        const size_t indexSize = R::Trunk::IdToEntrySize();
        const int none = -1;
        index->End = index->Begin;
        R::Trunk::IntVectorResizeFill(index, indexSize, &none);
        entries->End = entries->Begin;

        auto propsOf = reinterpret_cast<YGO::CARDS::CARD_PROPS*(__fastcall*)(unsigned int)>(YGO::CARDS::Get_CardPropsFromInternalId);
        const uint8_t* owned = SaveCardTable();
        const uint32_t limit = R::Trunk::InternalIdLimit();
        int* ids = static_cast<int*>(index->Begin);
        for (uint32_t internal = 1; internal < limit; ++internal)
        {
            const YGO::CARDS::CARD_PROPS* props = propsOf(internal);
            if (!props)
                continue;
            const uint16_t id = static_cast<uint16_t>(props->KonamiID);
            const size_t slot = static_cast<size_t>(id) - R::Trunk::FirstCardId;
            if (id == 0 || props->Kind == 10 || id < R::Trunk::FirstCardId || slot >= indexSize)   // 10 = token
                continue;
            R::Trunk::Entry entry{};
            entry.CardId = id;
            entry.Count = owned && id < 20000 ? (owned[id] & 7) : 0;
            if (!all && entry.Count == 0)
                continue;
            ids[slot] = static_cast<int>((static_cast<char*>(entries->End) - static_cast<char*>(entries->Begin)) / sizeof(R::Trunk::Entry));
            if (entries->End == entries->Capacity)
                R::Trunk::EntryVectorEmplace(entries, entries->End, &entry);
            else
            {
                *static_cast<R::Trunk::Entry*>(entries->End) = entry;
                entries->End = static_cast<char*>(entries->End) + sizeof(R::Trunk::Entry);
            }
        }
        R::Trunk::ApplyFilter(trunk);
        R::Trunk::SortAndFillGrid(trunk);
        RefreshGridCounts(trunk);
        R::Trunk::Refresh(trunk);
    }

    void ShowDeckInfo(Element& element)
    {
        const std::string deck = Expand(Str(element.Source, "deck"));
        if (deck == element.ShownDeck)
            return;
        element.ShownDeck = deck;
        const unsigned id = static_cast<unsigned>(std::atoi(deck.c_str()));
        R::DeckInfoPanel::ShowDeck(element.DeckInfo, deck.empty() || deck == "-1" ? nullptr : &id);
    }

    void ShowRelated(Element& element)
    {
        const std::string card = Expand(Str(element.Source, "card"));
        if (card == element.RelatedCard)
            return;
        element.RelatedCard = card;
        const int id = std::atoi(card.c_str());
        const uint16_t konami = id > 0 && id < 0xFFFF ? static_cast<uint16_t>(id) : 0xFFFF;
        Related_SetCard(element.Related, konami == 0xFFFF ? 0 : Related_Count(konami), konami == 0xFFFF ? nullptr : Related_List(konami), konami);
    }

    // "cards": ids separated by anything that is not a digit ("4007, 4008", "{var:pulled}"), or a JSON array of ids.
    void FillCardBox(Element& element)
    {
        const json& cards = Field(element.Source, "cards");
        std::string text;
        if (cards.is_array())
            for (const json& card : cards)
                text += (card.is_string() ? Expand(card.get<std::string>()) : card.is_number() ? std::to_string(card.get<int>()) : std::string()) + ",";
        else
            text = Expand(Str(element.Source, "cards"));
        if (text == element.BoxCards)
            return;
        element.BoxCards = text;
        std::vector<uint16_t> ids;
        int value = -1;
        for (size_t i = 0; i <= text.size(); ++i)
        {
            if (i < text.size() && std::isdigit(static_cast<unsigned char>(text[i])))
                value = (value < 0 ? 0 : value * 10) + (text[i] - '0');
            else if (value >= 0)
            {
                if (value >= 3900 && value < 0xFFFF)
                    ids.push_back(static_cast<uint16_t>(value));
                value = -1;
            }
        }
        R::CardBox::SetCards(element.CardBox, static_cast<unsigned>(ids.size()), ids.data(), -1, nullptr, nullptr);   // as the store
    }

    void BuildWidgets(Element& element)
    {
        char* screen = static_cast<char*>(g_Screen);
        if (element.Kind == "menu")
            BuildMenu(element);
        else if (element.Kind == "slider")
        {
            element.Tick = NewWidget(kTickSize);
            Tick_Construct(element.Tick);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            // the ticks sit at 949 + 49 * i in the bar's own space (the Settings screen puts the bar at x 0): x moves the whole bar
            Tick_Setup(element.Tick, &parent, 10 + element.Z, R::ScreenOwner(screen), element.X - kTickFirstX, element.Y);
        }
        else if (element.Kind == "digits")
        {
            const int count = std::clamp(static_cast<int>(Num(element.Source, "count", 4)), 1, 12);
            const float spacing = Num(element.Source, "spacing", 140);
            for (int i = 0; i < count; ++i)
            {
                void* digit = NewWidget(R::EntryDigit::Size);
                R::EntryDigit::Construct(digit);
                R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
                R::EntryDigit::CreateFromLayout(digit, &parent, 20 + element.Z, R::ScreenOwner(screen), element.X + i * spacing, element.Y, R::ScreenData(screen));
                element.Digits.push_back(digit);
            }
        }
        else if (element.Kind == "seriesTabs")
        {
            element.Series = NewWidget(kBigWidget);
            Series_Construct(element.Series);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            Series_Create(element.Series, &parent, 2 + element.Z, R::ScreenOwner(screen), element.X, element.Y, R::ScreenData(screen));
            for (int series = 0; series < 6; ++series)   // what Free Duel's OnEnter lists
                Push(element.SeriesList, series, VectorGrowInt);
            Series_SetList(element.Series, &element.SeriesList);
        }
        else if (element.Kind == "characters")
        {
            element.Chars = NewWidget(kBigWidget);
            Chars_Construct(element.Chars);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            Chars_Create(element.Chars, &parent, 3 + element.Z, R::ScreenOwner(screen), element.X, element.Y, 0);
            FillCharacters(element);
        }
        else if (element.Kind == "decks")
        {
            element.Decks = NewWidget(kBigWidget);
            Decks_Construct(element.Decks);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            Decks_Create(element.Decks, &parent, 5 + element.Z, R::ScreenOwner(screen), element.X, element.Y, 1, 0.0f);
            R::DeckSelectList::Refresh(element.Decks, true);   // OnEnter
            R::DeckSelectList::FillPlayerDecks(element.Decks);  // SetState(2)
            R::DeckSelectList::Refresh(element.Decks, false);
            const std::string bound = element.Bind.Get();
            if (!bound.empty())
                R::DeckSelectList::SelectDeckId(element.Decks, std::atoi(bound.c_str()));
            element.Selected = R::DeckSelectList::GetSelectedDeckId(element.Decks);
            element.Bind.Set(std::to_string(element.Selected));
        }
        else if (element.Kind == "deckInfo")
        {
            element.DeckInfo = NewWidget(kBigWidget);
            Info_Construct(element.DeckInfo);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            const int side = static_cast<int>(Num(element.Source, "side", 0));   // 0 = yours (mode 2), 1 = the opponent's (mode 3)
            Info_Create(element.DeckInfo, &parent, 6 + element.Z, R::ScreenOwner(screen), element.X, element.Y, 2 + side, side, 0.0f);
        }
        else if (element.Kind == "cardGrid")
        {
            element.Trunk = NewWidget(R::Trunk::Size);
            element.DeckState = NewWidget(R::DeckState::Size);
            R::Trunk::Construct(element.Trunk);
            R::DeckState::Construct(element.DeckState);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            R::Trunk::CreateFromLayout(element.Trunk, &parent, 15 + element.Z, R::ScreenOwner(screen), element.X, element.Y);
            R::Trunk::SetDeckInfo(element.Trunk, element.DeckState);
            element.Filter = NewWidget(8192);
            Filter_Construct(element.Filter);
            parent = R::ParentRef(R::ScreenRoot(screen));
            Filter_Create(element.Filter, &parent, 16 + element.Z, R::ScreenOwner(screen), Num(element.Source, "filterX", 1568.5f), Num(element.Source, "filterY", 120.0f));
            R::WidgetSetVisible(element.Filter, false);
        }
        else if (element.Kind == "user")
        {
            element.User = NewWidget(2048);
            User_Construct(element.User);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            User_Create(element.User, &parent, 2 + element.Z, R::ScreenOwner(screen));
            if (element.Source.contains("x") || element.Source.contains("y"))
                R::NodeSetPosition(WidgetNode(element.User), Num(element.Source, "x", 1887), Num(element.Source, "y", 18));
        }
        else if (element.Kind == "relatedCards")
        {
            element.Related = NewWidget(4096);
            Related_Construct(element.Related);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            Related_Create(element.Related, &parent, 13 + element.Z, R::ScreenOwner(screen), element.X, element.Y);
        }
        else if (element.Kind == "cardBox")
        {
            // ScreenRandomPackStore::SetupWidgets' desc, with the grid and cell size from the page
            element.CardBox = NewWidget(1024);
            R::CardBox::Construct(element.CardBox);
            R::CardBox::Desc desc;
            desc.X = element.X;
            desc.Y = element.Y;
            desc.Cols = (std::max)(1, static_cast<int>(Num(element.Source, "cols", 4)));
            desc.Rows = (std::max)(1, static_cast<int>(Num(element.Source, "rows", 2)));
            desc.CellW = Num(element.Source, "cellWidth", 255.17f);
            desc.CellH = Num(element.Source, "cellHeight", 370.0f);
            desc.GapX = desc.GapY = Num(element.Source, "gap", 10.0f);
            desc.ShowFrame = !Field(element.Source, "frame").is_boolean() || Field(element.Source, "frame").get<bool>();
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            R::CardBox::CreateFromLayout(element.CardBox, &parent, 2 + element.Z, R::ScreenOwner(screen), &desc);
            R::CardBox::SetCursorScale(element.CardBox, 1.03f);
            if (element.Width <= 0)   // the mouse box = the frame (CreateFromLayout's sum)
                element.Width = desc.Cols * desc.CellW + (desc.Cols - 1) * desc.GapX + 2 * desc.PadX;
            if (element.Height <= 0)
                element.Height = desc.Rows * desc.CellH + (desc.Rows - 1) * desc.GapY + 2 * desc.PadY;
        }
        else if (element.Kind == "cardInfo")
        {
            element.CardInfo = NewWidget(R::CardInfo::Size);
            R::CardInfo::Construct(element.CardInfo);
            R::SharedNode parent = R::ParentRef(R::ScreenRoot(screen));
            R::CardInfo::CreateFromLayout(element.CardInfo, &parent, 30 + element.Z, R::ScreenOwner(screen), element.X, element.Y);
            R::CardInfo::SetWidth(element.CardInfo, element.Width > 0 ? element.Width : 486.5f);
        }
    }

    void ShowDigits(Element& element)
    {
        const std::string value = element.Bind.Get(Str(element.Source, "default"));
        const size_t count = element.Digits.size();
        for (size_t i = 0; i < count; ++i)
        {
            // right-aligned: "42" in four digits = 0042
            const size_t from = count - i;
            const int digit = from <= value.size() && std::isdigit(static_cast<unsigned char>(value[value.size() - from])) ? value[value.size() - from] - '0' : 0;
            R::EntryDigit::SetValue(element.Digits[i], digit);
        }
    }

    std::string DigitsValue(const Element& element)
    {
        std::string value;
        for (void* digit : element.Digits)
            value += static_cast<char>('0' + *reinterpret_cast<int*>(static_cast<char*>(digit) + R::EntryDigit::Value));
        return value;
    }

    void ShowCard(Element& element)
    {
        const int card = std::atoi(Expand(Str(element.Source, "card")).c_str());
        if (card == element.ShownCard)
            return;
        element.ShownCard = card;
        R::CardInfo::SetCard(element.CardInfo, card > 0 ? static_cast<uint16_t>(card) : 0xFFFF);
        R::WidgetSetVisible(element.CardInfo, card > 0);
    }

    bool Visible(const Element& element) { return Holds(Field(element.Source, "visible")); }

    void SetWidgetsVisible(Element& element, bool on)
    {
        on = on && Visible(element);
        if (element.MenuNode.Node)
            Node_SetEnabled(element.MenuNode.Node, on);
        if (element.Tick)
            R::WidgetSetVisible(element.Tick, on);
        for (void* digit : element.Digits)
            R::WidgetSetVisible(digit, on);
        for (void* widget : { element.Series, element.Chars, element.Decks, element.DeckInfo, element.Trunk, element.User, element.Related, element.CardBox })
            if (widget)
                R::WidgetSetVisible(widget, on);
        if (element.Filter && (!on || !element.FilterOpen))
        {
            R::WidgetSetVisible(element.Filter, false);
            R::WidgetSetFocused(element.Filter, false);
            element.FilterOpen = false;
        }
        if (on && element.Trunk)
            FillCards(element);
        if (on && element.CardBox)
            FillCardBox(element);
        if (on && element.User)
        {
            User_SetProfile(element.User, 0xFFFFFFFD);   // as the Battle Pack screen's OnEnter
            User_RefreshWallet(element.User);
        }
        if (on && element.Related)
        {
            element.RelatedCard = "\x1";
            ShowRelated(element);
        }   // again each time it shows: the counts follow the save
        if (on && element.DeckInfo)
        {
            element.ShownDeck = "\x1";
            ShowDeckInfo(element);
        }
        if (element.CardInfo)
        {
            if (on)
            {
                element.ShownCard = -1;
                ShowCard(element);
            }
            else
                R::WidgetSetVisible(element.CardInfo, false);
        }
    }

    void FocusChanged(Page& page);

    void ShowPage(Page& page)
    {
        if (!g_Screen)
            return;
        if (!page.Built)
        {
            for (Element& element : page.Elements)
                BuildWidgets(element);
            page.Built = true;
            for (size_t i = 0; i < page.Elements.size() && page.Focus < 0; ++i)
                if (page.Elements[i].Focusable && Visible(page.Elements[i]))
                    page.Focus = static_cast<int>(i);
        }
        ShowHeader(page);
        ResetHelp(page);
        for (Element& element : page.Elements)
        {
            if (Visible(element))
                BuildDecoration(element);
            SetWidgetsVisible(element, true);
            if (element.Menu)
                RefreshMenu(element, true);
            ShowSlider(element);
            if (!element.Digits.empty())
                ShowDigits(element);
        }
        FocusChanged(page);
        g_Shown = true;
    }

    void HidePage(Page& page)
    {
        if (!g_Screen)
            return;
        HideFullCard();
        ClosePopup();
        for (Element& element : page.Elements)
        {
            RemoveDecoration(element);
            SetWidgetsVisible(element, false);
        }
        g_Shown = false;
    }

    // Conditions and texts may have changed (an action ran): re-evaluate what is shown.
    void RefreshPage(Page& page)
    {
        for (Element& element : page.Elements)
        {
            const bool visible = Visible(element);
            const bool decorated = element.Node.Node != nullptr;
            if (element.Kind == "text" && decorated && visible && Expand(Str(element.Source, "text")) == element.Shown)
                ;   // unchanged
            else if (element.Kind == "image" || element.Kind == "text")
            {
                RemoveDecoration(element);
                if (visible)
                    BuildDecoration(element);
            }
            SetWidgetsVisible(element, true);
            if (element.Menu)
                RefreshMenu(element, true);
            ShowSlider(element);
            if (!element.Digits.empty())
                ShowDigits(element);
        }
    }

    // A page file's "values" (and those of the files it "use"s, e.g. a values-only file shared by several pages) join the page values.
    void LoadValues(const json& root, int depth = 0)
    {
        if (root.contains("use") && root["use"].is_array() && depth < 8)
            for (const json& used : root["use"])
            {
                json other;
                if (used.is_string() && ReadJson(PageFile(used.get<std::string>()), other))
                    LoadValues(other, depth + 1);
                else
                    Logger::WriteLog(std::format("Page values: can't read the page file '{}' a page uses", used.dump()), MODULE_NAME, 1);
            }
        if (root.contains("values") && root["values"].is_object())
            for (const auto& [key, value] : root["values"].items())
                g_PageValues[key] = value.is_string() ? value.get<std::string>() : value.dump();
    }

    std::unique_ptr<Page> LoadPage(const std::string& name, const json& root)
    {
        ResetComputed();
        LoadValues(root);
        auto page = std::make_unique<Page>();
        page->Name = name;
        page->Root = root;
        page->Header = Wide(Expand(Str(root, "header", name)));
        if (root.contains("elements") && root["elements"].is_array())
            for (const json& source : root["elements"])
            {
                Element& element = page->Elements.emplace_back();
                element.Source = source;
                element.Kind = Str(source, "kind");
                element.Id = Str(source, "id");
                element.X = Num(source, "x", 0);
                element.Y = Num(source, "y", 0);
                element.Width = Num(source, "width", 0);
                element.Height = Num(source, "height", 0);
                element.Z = static_cast<int>(Num(source, "z", 0));
                element.Bind = Binding::Parse(Str(source, "bind"));
                static const char* kFocusable[] = { "menu", "slider", "digits", "seriesTabs", "characters", "decks", "cardGrid", "relatedCards", "cardBox" };
                element.Focusable = std::any_of(std::begin(kFocusable), std::end(kFocusable), [&](const char* k) { return element.Kind == k; });
                static const char* kKnown[] = { "menu", "slider", "digits", "cardInfo", "image", "text", "seriesTabs", "characters", "decks", "deckInfo", "cardGrid", "user", "relatedCards", "cardBox" };
                if (std::none_of(std::begin(kKnown), std::end(kKnown), [&](const char* k) { return element.Kind == k; }))
                    Logger::WriteLog(std::format("Page '{}': element kind '{}' is not one the engine builds (yet)", name, element.Kind), MODULE_NAME, 1);
            }
        return page;
    }

    // ---------------------------------------------------------------- focus and input

    void FocusChanged(Page& page)
    {
        for (int i = 0; i < static_cast<int>(page.Elements.size()); ++i)
        {
            Element& element = page.Elements[i];
            const bool focused = i == page.Focus;
            if (element.Tick)
                if (void* node = WidgetNode(element.Tick))
                    R::Dfx::SetAlpha(node, focused ? 1.0f : 0.6f);
            for (size_t d = 0; d < element.Digits.size(); ++d)
                R::EntryDigit::SetSelected(element.Digits[d], focused && static_cast<int>(d) == element.DigitFocus);
            if (element.Decks)
                R::WidgetSetFocused(element.Decks, focused);
            if (element.Trunk)
                R::WidgetSetFocused(element.Trunk, focused);
            if (element.Related)
                R::WidgetSetFocused(element.Related, focused);
            if (element.CardBox)
                R::WidgetSetFocused(element.CardBox, focused);
        }
    }

    std::wstring FocusedDescription(Page& page)
    {
        if (page.Focus < 0)
            return {};
        Element& element = page.Elements[page.Focus];
        if (element.Menu)
        {
            const int id = Menu_IdAt(element.Menu, Menu_Cursor(element.Menu));
            if (MenuItem* item = FindItem(element, id))
                return item->Description;
        }
        return Wide(Expand(Str(element.Source, "description")));
    }

    // Up / down at a control's edge, or left / right, move to the previous / next focusable control.
    bool MoveFocus(Page& page, int by)
    {
        const int count = static_cast<int>(page.Elements.size());
        for (int step = 1; step < count; ++step)
        {
            const int i = ((page.Focus + by * step) % count + count) % count;
            Element& element = page.Elements[i];
            if (element.Focusable && Visible(element))
            {
                page.Focus = i;
                R::PlayUISound(kSoundMove);
                FocusChanged(page);
                return true;
            }
        }
        return false;
    }

    bool Inside(const Element& element, float x, float y, float width, float height)
    {
        (void)element;
        return x >= 0 && y >= 0 && x < width && y < height;
    }

    void Update(Page& page)
    {
        int pressed = R::Input::GetPressed(R::InputState) | R::Input::GetRepeat(R::InputState);
        pressed |= R::Input::HelpBarPressed(static_cast<char*>(g_Screen) + kHelp);
        const int held = R::Input::GetHeld(R::InputState);

        if (g_PopupOpen)
        {
            // ScreenDeckEditor::Update state 15: the pop-up answers an id, -2 for Back, -1 while nothing is chosen
            const int64_t chosen = Popup_Input(g_Popup, static_cast<uint32_t>(pressed));
            if (chosen == -2)
            {
                R::PlayUISound(kSoundBack);
                ClosePopup();
            }
            else if (chosen >= 0 && chosen < static_cast<int64_t>(g_PopupActions.size()))
            {
                ClosePopup();
                R::PlayUISound(kSoundConfirm);
                const json action = g_PopupActions[static_cast<size_t>(chosen)];
                if (!action.is_null())
                    RunAction(action);
            }
            return;
        }
        if (g_FullOpen)
        {
            // ScreenDeckEditor::Update state 20
            const int fullHeld = held | Input_RightHeldA(R::InputState, 0x8000u) | Input_RightHeldB(R::InputState, 0x8000u);
            const int fullPressed = pressed | static_cast<int>(R::InputCancelPressed(R::InputState, kCancel));
            if (fullPressed & 0xA000)
            {
                R::PlayUISound(kSoundConfirm);
                HideFullCard();
            }
            else
                Full_Update(g_Full, 1.0f / 60.0f, fullPressed, fullHeld);
            return;
        }
        const int64_t mouse = R::Input::MousePosition();
        const float mx = static_cast<float>(static_cast<int32_t>(mouse)), my = static_cast<float>(static_cast<int32_t>(mouse >> 32));

        for (Element& element : page.Elements)
        {
            if (!Visible(element))
                continue;
            if (element.Chars && Expand(Str(element.Source, "series", "0")) != element.CharSeries)
                FillCharacters(element);
            if (element.DeckInfo)
                ShowDeckInfo(element);
            if (element.Related)
                ShowRelated(element);
            if (element.CardBox)
                FillCardBox(element);
            if (element.Decks)
                R::DeckSelectList::Tick(element.Decks);
            if (element.Trunk)
            {
                if (Expand(Str(element.Source, "cards", "owned")) != element.ShownCards)
                    FillCards(element);
                if (&element != (page.Focus >= 0 ? &page.Elements[page.Focus] : nullptr))
                    R::Trunk::Update(element.Trunk);
                RefreshGridCounts(static_cast<char*>(element.Trunk));   // sorting / filtering rebuilds the grid's counts from the game's
            }
            if (element.CardInfo)
            {
                ShowCard(element);
                R::CardInfo::Update(element.CardInfo, 1.0f / 60.0f, held, 0);
            }
        }

        // the mouse over a control focuses it (each control's own box)
        if (R::Input::MouseActive())
            for (int i = 0; i < static_cast<int>(page.Elements.size()); ++i)
            {
                Element& element = page.Elements[i];
                if (!element.Focusable || i == page.Focus || !Visible(element))
                    continue;
                const bool over = element.Tick ? SliderHit(element.Tick, mouse)
                                : element.Trunk ? Area_HitTest(static_cast<char*>(element.Trunk) + kTrunkArea, mouse)
                                : element.Width > 0 && element.Height > 0 && Inside(element, mx - element.X, my - element.Y, element.Width, element.Height);
                if (over)
                {
                    page.Focus = i;
                    FocusChanged(page);
                    break;
                }
            }

        if (page.Focus < 0)
        {
            if (R::InputCancelPressed(R::InputState, kCancel) || (pressed & kCancel))
                GoBack();
            return;
        }

        Element& element = page.Elements[page.Focus];
        if (element.Menu)
        {
            // the visible items, so up at the first / down at the last leaves the menu
            int visible = 0;
            for (const MenuItem& item : element.Items)
                visible += Holds(Field(item.Source, "visible")) ? 1 : 0;
            const unsigned cursor = Menu_Cursor(element.Menu);
            if ((pressed & kDown) && cursor + 1 >= static_cast<unsigned>(visible) && MoveFocus(page, 1))
                return;
            if ((pressed & kUp) && cursor == 0 && MoveFocus(page, -1))
                return;

            const int id = Menu_IdAt(element.Menu, cursor);
            MenuItem* item = FindItem(element, id);
            if (item && item->Choice && (pressed & (kLeft | kRight)))
            {
                ChangeChoice(element, *item, (pressed & kLeft) ? -1 : 1);
                pressed &= ~(kLeft | kRight);
            }
            else if (pressed & (kLeft | kRight))
            {
                MoveFocus(page, (pressed & kLeft) ? -1 : 1);
                return;
            }

            MenuListener listener{ g_ListenerVftable, &element };
            Menu_ProcessInput(element.Menu, pressed, &listener, 0xFFFFFFFD);   // reads cancel itself (-2 = Back through the listener)
        }
        else
        {
            // an open filter panel takes all input, its own Back included (the editor's state 6 hands it the frame's buttons)
            if (element.Trunk && element.FilterOpen)
            {
                Filter_Input(element.Filter, pressed);
                if (!*(static_cast<char*>(element.Filter) + kFilterOpen))
                {
                    // the panel closed itself (its own Back / OK): the grid gets the input again
                    element.FilterOpen = false;
                    R::WidgetSetVisible(element.Filter, false);
                    R::WidgetSetFocused(element.Filter, false);
                    Trunk_SetMode(element.Trunk, -1);
                    R::WidgetSetFocused(element.Trunk, true);
                }
                else
                    Trunk_SetMode(element.Trunk, 4);
                if (Filter_TakeChanged(kFilterState, 1))
                {
                    Trunk_Reapply(element.Trunk);
                    RefreshGridCounts(static_cast<char*>(element.Trunk));
                    Trunk_AfterFilter(element.Trunk);
                }
                return;   // the panel had the input (and its own Back)
            }
            if (R::InputCancelPressed(R::InputState, kCancel))
                pressed |= kCancel;
            if (pressed & kCancel)
            {
                GoBack();
                return;
            }
            if (element.Tick)
            {
                // a click anywhere on the bar's picture sets the level under the mouse
                if (R::Input::MouseActive() && R::Input::MouseClickPending() && SliderHit(element.Tick, mouse))
                {
                    R::Input::TakeMouseClick(R::InputState, kConfirm);
                    SetSlider(element, SliderLevelAt(element.Tick, mouse));
                }
                if (pressed & kLeft) SetSlider(element, SliderLevel(element) - 1);
                if (pressed & kRight) SetSlider(element, SliderLevel(element) + 1);
                if (pressed & kUp) MoveFocus(page, -1);
                else if (pressed & kDown) MoveFocus(page, 1);
            }
            else if (element.Series)
            {
                if (R::Input::MouseActive() && Series_HitTest(element.Series, mouse) >= 0 && R::Input::TakeMouseClick(R::InputState, kConfirm))
                    pressed |= kConfirm;
                if (Series_Input(element.Series, pressed & ~(kUp | kDown | kConfirm)))
                {
                    element.Bind.Set(std::to_string(Series_Current(element.Series)));
                    if (element.Source.contains("onChange"))
                        RunAction(element.Source["onChange"]);
                }
                if (pressed & kConfirm)
                {
                    element.Bind.Set(std::to_string(Series_Current(element.Series)));
                    R::PlayUISound(kSoundConfirm);
                    if (element.Source.contains("action"))
                        RunAction(element.Source["action"]);
                }
                else if (pressed & kUp) MoveFocus(page, -1);
                else if (pressed & kDown) MoveFocus(page, 1);
            }
            else if (element.Chars)
            {
                char confirmed = 0;
                Chars_Input(element.Chars, pressed, held, &confirmed);
                const int selected = static_cast<int>(Chars_Selected(element.Chars));
                if (selected != element.Selected)
                {
                    element.Selected = selected;
                    element.Bind.Set(std::to_string(selected));
                    if (element.Source.contains("onChange"))
                        RunAction(element.Source["onChange"]);
                }
                if (confirmed || (pressed & kConfirm))
                {
                    if (selected == 0)
                        R::PlayUISound(kSoundError);   // none, or locked (Free Duel says so with message 710)
                    else
                    {
                        R::PlayUISound(kSoundConfirm);
                        if (element.Source.contains("action"))
                            RunAction(element.Source["action"]);
                    }
                }
                const int tabs = R::DeckSelectList::TabButtons(R::InputState);   // L / R leave the grid
                if (tabs & 0x20000000) MoveFocus(page, -1);
                else if (tabs & 0x40000000) MoveFocus(page, 1);
            }
            else if (element.Decks)
            {
                int clicked = -1;
                if (R::DeckSelectList::HandleInput(element.Decks, pressed | R::DeckSelectList::TabButtons(R::InputState), &clicked))
                {
                    element.Selected = R::DeckSelectList::GetSelectedDeckId(element.Decks);
                    element.Bind.Set(std::to_string(element.Selected));
                    if (element.Source.contains("onChange"))
                        RunAction(element.Source["onChange"]);
                }
                if (clicked >= 0 || (pressed & kConfirm))
                {
                    if (R::DeckSelectList::GetSelectedDeckId(element.Decks) == -1)
                        R::PlayUISound(kSoundError);
                    else
                    {
                        R::PlayUISound(kSoundConfirm);
                        if (element.Source.contains("action"))
                            RunAction(element.Source["action"]);
                    }
                }
            }
            else if (element.Related)
            {
                int kind = 0, action = -1;
                char flag = 0;
                pressed |= Input_TakeRightClick(R::InputState, 0x8000u);
                Related_Input(element.Related, pressed, held, &kind, &flag, &action);
                const int selected = Related_Selected(element.Related);
                if (selected != element.Selected)
                {
                    element.Selected = selected;
                    element.Bind.Set(selected == 0xFFFF ? std::string() : std::to_string(selected));
                    if (element.Source.contains("onChange"))
                        RunAction(element.Source["onChange"]);
                }
                if (action == -7 || (pressed & 0x8000))
                    ShowFullCard(Related_DetailsCard(element.Related));
                else if (kind == 3 || (pressed & kConfirm))
                {
                    R::PlayUISound(kSoundConfirm);
                    if (element.Source.contains("action"))
                        RunAction(element.Source["action"]);
                }
            }
            else if (element.CardBox)
            {
                // ScreenRandomPackStore::Update state 4: a right-click / 0x8000 = the full card view; L / R leave the box
                pressed |= Input_TakeRightClick(R::InputState, 0x8000u);
                int action = 0;
                char clicked = 0;
                R::CardBox::ProcessInput(element.CardBox, pressed, held, &action, &clicked);
                const int selected = R::CardBox::GetSelectedCardId(element.CardBox);
                if (selected != element.Selected)
                {
                    element.Selected = selected;
                    element.Bind.Set(selected == 0xFFFF ? std::string() : std::to_string(selected));
                    if (element.Source.contains("onChange"))
                        RunAction(element.Source["onChange"]);
                }
                if (pressed & 0x8000)
                    ShowFullCard(selected);
                else if (action == 3)
                {
                    if (selected == 0xFFFF)
                        R::PlayUISound(kSoundError);
                    else
                    {
                        R::PlayUISound(kSoundConfirm);
                        if (element.Source.contains("action"))
                            RunAction(element.Source["action"]);
                    }
                }
                const int tabs = R::DeckSelectList::TabButtons(R::InputState);
                if (tabs & 0x20000000) MoveFocus(page, -1);
                else if (tabs & 0x40000000) MoveFocus(page, 1);
            }
            else if (element.Trunk)
            {
                // the mouse wheel scrolls the grid while the mouse is over it (DeckEdit_TrunkInputHandler)
                // DeckEdit_TrunkInputHandler: the wheel goes to the grid while the mouse is on the grid's own area; then ProcessInput, Update
                // when it changed something, and Update once more at the end of the frame (ScreenDeckEditor::Update)
                if (Area_HitTest(static_cast<char*>(element.Trunk) + kTrunkArea, mouse))
                    pressed |= Input_TakeWheel(R::InputState, 0x20000000u, 0x40000000u);
                pressed |= Input_TakeRightClick(R::InputState, 0x8000u);   // a right-click = the card's full details
                int action = -1, kind = 0;
                char flag = 0;
                if (R::Trunk::ProcessInput(element.Trunk, pressed, held, &action, &kind, &flag))
                    R::Trunk::Update(element.Trunk);
                R::Trunk::Update(element.Trunk);
                if ((pressed & 0x8000) && action != 2)
                    ShowFullCard(R::Trunk::GetSelectedCardId(element.Trunk));
                else if (action == 2)
                {
                    // the filter button: as the editor's state 6
                    element.FilterOpen = true;
                    R::WidgetSetFocused(element.Trunk, false);
                    Trunk_SetMode(element.Trunk, 4);
                    R::WidgetSetVisible(element.Filter, true);
                    R::WidgetSetFocused(element.Filter, true);
                    R::PlayUISound(kSoundConfirm);
                }
                const int selected = R::Trunk::GetSelectedCardId(element.Trunk);
                if (selected != element.Selected)
                {
                    element.Selected = selected;
                    element.Bind.Set(selected == 0xFFFF ? std::string() : std::to_string(selected));
                    if (element.Source.contains("onChange"))
                        RunAction(element.Source["onChange"]);
                }
                if (action == 0 && kind == 3 && selected != 0xFFFF)   // a card confirmed (clicked, or confirm on the highlighted one)
                {
                    R::PlayUISound(kSoundConfirm);
                    if (element.Source.contains("action"))
                        RunAction(element.Source["action"]);
                }
            }
            else if (!element.Digits.empty())
            {
                if (R::Input::MouseClickPending())
                    for (size_t d = 0; d < element.Digits.size(); ++d)
                    {
                        const int hit = R::EntryDigit::HitTest(element.Digits[d], mouse);
                        if (hit < 0)
                            continue;
                        R::Input::TakeMouseClick(R::InputState, kConfirm);
                        element.DigitFocus = static_cast<int>(d);
                        if (hit == 0) pressed |= kUp;
                        else if (hit == 1) pressed |= kDown;
                        FocusChanged(page);
                        break;
                    }
                void* digit = element.Digits[element.DigitFocus];
                const int value = *reinterpret_cast<int*>(static_cast<char*>(digit) + R::EntryDigit::Value);
                if (pressed & (kUp | kDown))
                {
                    R::EntryDigit::SetValue(digit, value + ((pressed & kUp) ? 1 : 9));   // SetValue keeps % 10
                    R::PlayUISound(kSoundStep);
                    element.Bind.Set(DigitsValue(element));
                    if (element.Source.contains("onChange"))
                        RunAction(element.Source["onChange"]);
                }
                else if (pressed & kLeft)
                {
                    if (element.DigitFocus > 0) { --element.DigitFocus; R::PlayUISound(kSoundMove); FocusChanged(page); }
                    else MoveFocus(page, -1);
                }
                else if (pressed & kRight)
                {
                    if (element.DigitFocus + 1 < static_cast<int>(element.Digits.size())) { ++element.DigitFocus; R::PlayUISound(kSoundMove); FocusChanged(page); }
                    else MoveFocus(page, 1);
                }
                else if ((pressed & kConfirm) && element.Source.contains("action"))
                {
                    R::PlayUISound(kSoundConfirm);
                    RunAction(element.Source["action"]);
                }
            }
        }

        if (Top() == &page && g_Shown)
            ShowHelp(page, FocusedDescription(page));
    }

    // ---------------------------------------------------------------- opening, closing

    void OpenLoaded(std::unique_ptr<Page> page)
    {
        // On the page screen already (its OnEnter showed the top page): open on top. R::CurrentScreenId reads the screen record's +8,
        // which is the PENDING id (0 once a change is done; +0 is the current one), so it can't tell.
        const bool here = g_Screen && g_Shown;
        if (here)
        {
            if (Page* below = Top())
                HidePage(*below);
            g_Stack.push_back(std::move(page));
            ShowPage(*Top());
            return;
        }

        for (auto& stale : g_Stack)
            g_Retired.push_back(std::move(stale));
        g_Stack.clear();
        g_Stack.push_back(std::move(page));

        // The screen object to leave from (its id becomes the page screen's Back target): the one whose button is running, else the one
        // showing; the main menu's record id can read differently while its buttons animate, so the main menu itself is the last resort.
        void* from = Menu::CallbackSource();
        const int current = R::CurrentScreenId();
        if (!from)
            from = R::FindScreenObject(current);
        if (!from && Menu::IsOpen())
            from = R::FindScreenObject(kScreenMainMenu);
        Logger::WriteLog(std::format("Page screen: opening '{}' from screen object {:p} (callback source {:p}, current screen {}, main menu open {})",
            Top()->Name, from, Menu::CallbackSource(), current, Menu::IsOpen()), MODULE_NAME, 0);
        if (!from)
        {
            Logger::WriteLog("Page screen: no screen to go to it from", MODULE_NAME, 2);
            return;
        }
        // NavigateToScreen refuses while a screen change is still running (the UI transition's busy flag, transition+88): the main
        // menu's button press is often still fading. Hook_RunJobs tries again every frame for a few seconds.
        if (!R::GotoScreenFrom(from, kScreenId))
        {
            g_PendingFrom = from;
            g_PendingFrames = 300;
        }
    }

    void GoBack()
    {
        R::PlayUISound(kSoundBack);
        if (g_Stack.size() > 1)
        {
            HidePage(*Top());
            g_Retired.push_back(std::move(g_Stack.back()));
            g_Stack.pop_back();
            ShowPage(*Top());
            return;
        }
        R::GoBack(g_Screen);   // to the screen the first page was opened from (screen+60)
    }

    // ---------------------------------------------------------------- actions

    void __cdecl RunPendingYes();
    json g_PendingYes, g_PendingNo;

    void RunStep(const json& step)
    {
        if (!step.is_object())
            return;
        if (step.contains("page"))
            PageScreen::TryOpen(Str(step, "page")) || Menu::OpenPage(Str(step, "page"));
        else if (step.contains("back"))
            GoBack();
        else if (step.contains("goto"))
        {
            const json& target = step["goto"];
            const int id = target.is_number_integer() ? target.get<int>() : Menu::ScreenIdByName(Str(step, "goto"));
            if (id < 0 || !R::GotoScreenFrom(g_Screen, id))
                Logger::WriteLog(std::format("Page action: can't go to screen {}", target.dump()), MODULE_NAME, 1);
        }
        else if (step.contains("video"))
        {
            // Yu-Gi-Oh-Media registers "video:<name>" for every movies\<name>.mp4 (full screen, Esc / click skips)
            if (!Menu::RunAction("video:" + Str(step, "video")))
                Logger::WriteLog(std::format("Page action: no video '{}' (needs Yu-Gi-Oh-Media and movies\\{}.mp4)", Str(step, "video"), Str(step, "video")), MODULE_NAME, 1);
        }
        else if (step.contains("call"))
        {
            if (!Menu::RunAction(Str(step, "call")))
                Logger::WriteLog(std::format("Page action: '{}' is not registered by any plugin", Str(step, "call")), MODULE_NAME, 1);
        }
        else if (step.contains("quit"))
            R::RequestQuit(R::GetGlobalInstance());
        else if (step.contains("set"))
            Binding::Parse(Str(step, "set")).Set(Expand(Str(step, "value")));
        else if (step.contains("toggle"))
        {
            const Binding b = Binding::Parse(Str(step, "toggle"));
            b.Set(b.Get() == "1" ? "0" : "1");
        }
        else if (step.contains("add"))
        {
            const Binding b = Binding::Parse(Str(step, "add"));
            b.Set(std::to_string(std::atoi(b.Get("0").c_str()) + static_cast<int>(Num(step, "value", 1))));
        }
        else if (step.contains("popup"))
            OpenPopup(step["popup"]);
        else if (step.contains("showCard"))
            ShowFullCard(std::atoi(Expand(Str(step, "showCard")).c_str()));
        else if (step.contains("resetFilter"))
        {
            // the card grid filter's own reset (the panel's Reset prompt): every grid on the page is filtered again next frame
            Filter_Reset(kFilterState);
            if (Page* page = Top())
                for (Element& element : page->Elements)
                    if (element.Trunk)
                    {
                        Trunk_Reapply(element.Trunk);
                        RefreshGridCounts(static_cast<char*>(element.Trunk));
                    }
        }
        else if (step.contains("giveDp") || step.contains("takeDp"))
        {
            // { "giveDp": 500 } / { "takeDp": 500, "else": <action when the profile has less> } - amounts may be "{var:x}"
            const bool take = step.contains("takeDp");
            const json& amount = step[take ? "takeDp" : "giveDp"];
            const long long dp = std::strtoll(Expand(amount.is_string() ? amount.get<std::string>() : amount.dump()).c_str(), nullptr, 10);
            uint64_t* wallet = GameWallet();
            if (!wallet || dp < 0)
                Logger::WriteLog("Page action: no profile is loaded (or a negative amount), DP unchanged", MODULE_NAME, 1);
            else if (take && *wallet < static_cast<uint64_t>(dp))
            {
                if (step.contains("else"))
                    RunAction(step["else"]);
                else
                    R::PlayUISound(kSoundError);
            }
            else
            {
                *wallet = take ? *wallet - dp : (std::min)(*wallet + dp, 999999999ull);   // the game shows up to 9 digits
                if (step.contains("then"))
                    RunAction(step["then"]);
            }
        }
        else if (step.contains("giveCard"))
        {
            // { "giveCard": 4007, "copies": 1 } - up to the save's 3 copies, marked as seen
            const json& card = step["giveCard"];
            const int id = std::atoi(Expand(card.is_string() ? card.get<std::string>() : card.dump()).c_str());
            const int copies = static_cast<int>(Num(step, "copies", 1));
            uint8_t* table = GameCardTable();
            if (!table || id <= 0 || id >= kSaveCardCount)
                Logger::WriteLog(std::format("Page action: can't give card {} (no profile loaded, or not a card id)", id), MODULE_NAME, 1);
            else
            {
                const int owned = (std::min)(kFullSet, (std::max)(0, (table[id] & 7) + copies));
                table[id] = static_cast<uint8_t>((table[id] & ~7) | owned | 8);
            }
        }
        else if (step.contains("takeCard"))
        {
            // { "takeCard": 4007, "copies": 1, "else": <action when the profile has fewer> }
            const json& card = step["takeCard"];
            const int id = std::atoi(Expand(card.is_string() ? card.get<std::string>() : card.dump()).c_str());
            const int copies = static_cast<int>(Num(step, "copies", 1));
            uint8_t* table = GameCardTable();
            if (!table || id <= 0 || id >= kSaveCardCount || copies < 0)
                Logger::WriteLog(std::format("Page action: can't take card {} (no profile loaded, or not a card id)", id), MODULE_NAME, 1);
            else if ((table[id] & 7) < copies)
            {
                if (step.contains("else"))
                    RunAction(step["else"]);
                else
                    R::PlayUISound(kSoundError);
            }
            else
            {
                table[id] = static_cast<uint8_t>((table[id] & ~7) | ((table[id] & 7) - copies));
                if (step.contains("then"))
                    RunAction(step["then"]);
            }
        }
        else if (step.contains("unlock") || step.contains("lock"))
        {
            // { "unlock": "card_shop" | "battle_packs" | "duelist_challenge" } - the main menu buttons a new profile has greyed out
            const bool on = step.contains("unlock");
            const uint32_t bit = MenuUnlockBit(Str(step, on ? "unlock" : "lock"));
            uint8_t* player = GamePlayer();
            if (!player || !bit)
                Logger::WriteLog("Page action: (un)lock needs a loaded profile and card_shop, battle_packs or duelist_challenge", MODULE_NAME, 1);
            else
            {
                uint32_t& flags = *reinterpret_cast<uint32_t*>(player + YGO::SAVE::PlayerSection::MenuUnlockFlags);
                flags = on ? flags | bit : flags & ~bit;
            }
        }
        else if (step.contains("unlockCharacter"))
        {
            // { "unlockCharacter": 12 } - a chardata id (0-239): the character shows in Free Duel
            const json& c = step["unlockCharacter"];
            const int id = std::atoi(Expand(c.is_string() ? c.get<std::string>() : c.dump()).c_str());
            uint8_t* player = GamePlayer();
            if (!player || id < 0 || id >= static_cast<int>(YGO::SAVE::PlayerSection::CharacterCount))
                Logger::WriteLog(std::format("Page action: can't unlock character {}", id), MODULE_NAME, 1);
            else
                player[YGO::SAVE::PlayerSection::UnlockedCharacters + id / 8] |= static_cast<uint8_t>(1 << (id % 8));
        }
        else if (step.contains("sound"))
            R::PlayUISound(static_cast<int>(Num(step, "sound", kSoundConfirm)));
        else if (step.contains("message"))
        {
            static std::wstring text;   // the box reads it while it is open
            text = Wide(Expand(Str(step, "message")));
            R::EmptyFunction none;
            R::ShowMessageText(g_Screen, -1, text.c_str(), &none);
        }
        else if (step.contains("confirm"))
        {
            static std::wstring text;
            text = Wide(Expand(Str(step, "confirm")));
            g_PendingYes = step.value("yes", json());
            std::function<void()> onYes = &RunPendingYes;   // a plain function pointer fits the small buffer the game takes over
            R::ShowYesNo(g_Screen, -1, text.c_str(), &onYes);
        }
        else if (step.contains("if"))
        {
            const json branch = Holds(step["if"]) ? step.value("then", json()) : step.value("else", json());
            if (!branch.is_null())
                RunAction(branch);
        }
        else
            Logger::WriteLog(std::format("Page action not understood: {}", step.dump()), MODULE_NAME, 1);
    }

    void RunAction(const json& action)
    {
        Page* before = Top();
        if (action.is_array())
            for (const json& step : action)
                RunStep(step);
        else
            RunStep(action);
        if (Page* page = Top(); page && page == before && g_Shown)
            RefreshPage(*page);   // values and conditions may have changed
    }

    void __cdecl RunPendingYes()
    {
        const json yes = g_PendingYes;
        g_PendingYes = json();
        if (!yes.is_null())
            RunAction(yes);
    }

    // ---------------------------------------------------------------- the screen

    void* __fastcall Vt_Destructor(void* self, unsigned int) { return self; }
    void __fastcall Vt_SetupWidgets(void*, int64_t) {}

    void __fastcall Vt_OnEnter(void*, int64_t)
    {
        if (Page* page = Top())
            ShowPage(*page);
    }

    void __fastcall Vt_OnLeave(void*, int64_t)
    {
        if (Page* page = Top())
            HidePage(*page);
    }

    void __fastcall Vt_Update(void*, int64_t)
    {
        if (Page* page = Top(); page && g_Shown)
            Update(*page);
    }

    void Create(int64_t ui)
    {
        if (R::FindScreenObject(kScreenId))
        {
            Logger::WriteLog(std::format("Screen id {} is already taken: version 2 pages are off", kScreenId), MODULE_NAME, 2);
            g_Failed = true;
            return;
        }
        void** base = reinterpret_cast<void**>(kScreenBaseVftable);
        g_Vftable[0] = base[-1];
        std::memcpy(&g_Vftable[1], base, sizeof(void*) * kVftableSlots);
        g_Vftable[1 + 0] = reinterpret_cast<void*>(&Vt_Destructor);
        g_Vftable[1 + 8] = reinterpret_cast<void*>(&Vt_SetupWidgets);
        g_Vftable[1 + 12] = reinterpret_cast<void*>(&Vt_OnEnter);
        g_Vftable[1 + 13] = reinterpret_cast<void*>(&Vt_OnLeave);
        g_Vftable[1 + 14] = reinterpret_cast<void*>(&Vt_Update);

        void* screen = GameNew(kScreenSize);
        if (!screen)
        {
            g_Failed = true;
            return;
        }
        std::memset(screen, 0, kScreenSize);
        ScreenBase_Constructor(screen);
        *reinterpret_cast<void***>(screen) = &g_Vftable[1];
        ScreenBase_SetScreenId(screen, kScreenId);
        ScreenBase_SetScreenType(screen, kScreenTypeOptions);
        ScreenBase_Load(screen, ui);
        ScreenBase_Activate(screen);
        g_Screen = screen;
        Logger::WriteLog(std::format("Page screen ready (screen id {})", kScreenId), MODULE_NAME, 0);
    }

    void __fastcall Hook_RunJobs(int64_t scheduler, int64_t ui, unsigned int limit)
    {
        reinterpret_cast<void(__fastcall*)(int64_t, int64_t, unsigned int)>(orig_RunJobs)(scheduler, ui, limit);
        char* instance = *reinterpret_cast<char**>(kGlobalInstance);
        if (!instance || scheduler != *reinterpret_cast<int64_t*>(instance + kMainScheduler) || g_Failed)
            return;
        if (!g_Screen)
        {
            if (R::CurrentScreenId() == kScreenMainMenu)
                Create(ui);
            return;
        }
        ScreenBase_Tick(g_Screen, ui);

        if (g_PendingFrom)
        {
            if (g_Shown || R::GotoScreenFrom(g_PendingFrom, kScreenId))
                g_PendingFrom = nullptr;
            else if (--g_PendingFrames <= 0)
            {
                g_PendingFrom = nullptr;
                Logger::WriteLog("Page screen: the game didn't allow the screen change", MODULE_NAME, 2);
            }
        }
    }

    // ---------------------------------------------------------------- the demo list source

    void __cdecl DemoNumbers(void* list, void*)
    {
        for (int i = 1; i <= 5; ++i)
            PageScreen::ListAdd(list, std::format(L"Row {} from a plugin", i).c_str(), std::format(L"rix.demoRows gave this row the value {}.", i * 10).c_str(),
                                std::to_string(i * 10).c_str());
    }
}

namespace PageScreen
{
    bool TryOpen(const std::string& name)
    {
        if (name.empty() || name.find_first_of("/\\:") != std::string::npos)
            return false;
        json root;
        if (!ReadJson(PageFile(name), root) || root.value("version", 1) < 2)
            return false;
        if (!g_Screen)
        {
            Logger::WriteLog(std::format("Page '{}': the page screen isn't ready yet", name), MODULE_NAME, 1);
            return true;
        }
        OpenLoaded(LoadPage(name, root));
        return true;
    }

    void SetVar(const std::string& name, const std::string& value) { g_Vars[name] = value; }

    std::string GetVar(const std::string& name)
    {
        auto it = g_Vars.find(name);
        return it != g_Vars.end() ? it->second : std::string();
    }

    void RegisterList(const std::string& name, RIX_PageListCallback callback, void* user) { g_Sources[name] = { callback, user }; }

    void RegisterValue(const std::string& name, RIX_PageValueCallback callback, void* user) { g_ValueSources[name] = { callback, user }; }

    void ListAdd(void* list, const wchar_t* label, const wchar_t* description, const char* value)
    {
        if (list)
            static_cast<std::vector<ListItem>*>(list)->push_back({ label ? label : L"", description ? description : L"", value ? value : "" });
    }

    void Install()
    {
        RegisterList("rix.demoRows", &DemoNumbers, nullptr);
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_RunJobs, Hook_RunJobs);
        const LONG result = DetourTransactionCommit();
        Logger::WriteLog(std::format("Page screen hook: {}", result), MODULE_NAME, result == 0 ? 0 : 2);
    }
}
