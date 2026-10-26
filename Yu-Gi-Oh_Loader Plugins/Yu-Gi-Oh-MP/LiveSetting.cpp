#include "LiveSetting.h"

#include <Windows.h>
#include <cstdint>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Memory.h"
#include "BanList.h"
#include "MatchSettingsPage.h"
#include "Steam.h"
#include "Yu-Gi-Oh-RIX.h"

#include <algorithm>
#include <set>
#include <cstdlib>
#include <cstring>
#include <format>
#include <functional>
#include <malloc.h>
#include <map>
#include <string>
#include <utility>
#include <vector>
#include "Yu-Gi-Oh-Core.h"
#include "YuGiOh/YuGiOh-Addresses.h"

namespace
{
    constexpr int kSlot = 5;                 // the free setting slot: our "More Options" row
    constexpr int kCreate = 6;               // the action row when hosting
    constexpr size_t kSlots = 3848, kSlotSize = 48;          // +0 label, +8 description, +16 options (begin, end, capacity)
    constexpr size_t kChosen = 4328;         // int per slot
    constexpr size_t kRows = 3824;           // std::vector<int> of slot numbers
    constexpr size_t kCursor = 4368;         // ListState: +0 cursor
    constexpr int kLeft = 4, kRight = 8, kConfirm = 0x1000;   // the input masks LiveSetting_RowsInput / ChangeOption get

    struct Vector { char* Begin; char* End; char* Capacity; };
    struct Option { int64_t Text; int64_t Unused; };

    constexpr int kRankedMatch = 1;          // g_LiveMatchType 1: fixed rules, the game shows it no settings, so no custom settings row either
    // Lobby data (only Yu-Gi-Oh-MP reads it): "exbanmode" = game / off / custom; the custom list (BanList::Encode) in "exbanlist0",
    // "exbanlist1"... with "exbanparts" = how many, so a joiner plays the host's list without having the file. Steam keeps up to 8 KB per
    // value, so the list is split; any list fits (the game's own is about 620 characters).
    // Member data "exmp" = "1": the player runs Yu-Gi-Oh-MP. The host is told about players without it ("Vanilla Client").
    constexpr const char* kLobbyMode = "exbanmode";
    constexpr const char* kLobbyParts = "exbanparts";
    constexpr const char* kLobbyListPrefix = "exbanlist";
    constexpr size_t kLobbyChunk = 7000;
    constexpr const char* kMemberMark = "exmp";
    // "extag" = "coop": the host armed an online co-op tag duel (Yu-Gi-Oh-TagDuel); a joining player's TagDuel is armed to match, so both
    // games run the same 4-seat duel. Any other value / missing = a normal duel (a joiner's own arming is cleared).
    constexpr const char* kLobbyTag = "extag";

    // Yu-Gi-Oh-TagDuel's exports (optional plugin).
    bool TagCoopArmed()
    {
        static auto get = reinterpret_cast<bool(__cdecl*)()>(GetModuleHandleA("Yu-Gi-Oh-TagDuel.dll") ? GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-TagDuel.dll"), "TagDuel_IsOnlineCoop") : nullptr);
        return get && get();
    }

    bool SetTagCoop(bool on)
    {
        static auto set = reinterpret_cast<void(__cdecl*)(bool)>(GetModuleHandleA("Yu-Gi-Oh-TagDuel.dll") ? GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-TagDuel.dll"), "TagDuel_SetOnlineCoop") : nullptr);
        if (set)
            set(on);
        return set != nullptr;
    }

    using Screen_t = void(__fastcall*)(char* screen);
    using Input_t = void(__fastcall*)(char* screen, int pressed);
    using Change_t = char(__fastcall*)(char* screen, int64_t pressed);

    Screen_t orig_BuildOptions = reinterpret_cast<Screen_t>(0x1408546A0);   // LiveSetting_BuildOptions
    Screen_t orig_BuildRows = reinterpret_cast<Screen_t>(0x140855B00);      // LiveSetting_BuildRows
    Input_t orig_RowsInput = reinterpret_cast<Input_t>(0x140853D70);        // LiveSetting_RowsInput
    Change_t orig_ChangeOption = reinterpret_cast<Change_t>(0x140855DD0);   // LiveSetting_ChangeOption
    using Rows_t = int64_t(__fastcall*)(char* screen);
    Rows_t orig_LayoutRows = reinterpret_cast<Rows_t>(0x140856180);         // LiveSetting_LayoutRows
    Rows_t orig_RefreshRows = reinterpret_cast<Rows_t>(0x140856500);        // LiveSetting_RefreshRows (LiveSetting_OnEnter returns its value)
    using Update_t = void(__fastcall*)(char* screen, void* ui);
    Update_t orig_Update = reinterpret_cast<Update_t>(0x140854CD0);         // RIX::ScreenLiveSetting::Update
    Screen_t orig_Back = reinterpret_cast<Screen_t>(0x1408539B0);           // LiveSetting_Back (cancel: saves the settings, GoBack)

    // The game's Steam lobby calls (QNet), hooked to add our lobby data.
    using Publish_t = void(__fastcall*)(void* qnet, uint64_t lobby);
    using LobbyEnter_t = void(__fastcall*)(void* qnet, void* lobbyEnter, char ioFailure);
    using Leave_t = void(__fastcall*)(void* qnet, uint64_t lobby);
    Publish_t orig_PublishHostLobbyData = reinterpret_cast<Publish_t>(0x1408D8020);   // QNet__PublishHostLobbyData
    LobbyEnter_t orig_OnLobbyEnter = reinterpret_cast<LobbyEnter_t>(0x1408D8D90);      // QNet__OnLobbyEnter (LobbyEnter_t: +0 the lobby id)
    Leave_t orig_LeaveLobby = reinterpret_cast<Leave_t>(0x1408D8A90);                   // QNet__LeaveLobbyAndCloseP2P

    const auto LiveMatchType = reinterpret_cast<uint8_t(__fastcall*)()>(0x140768E20);             // YGO__DUEL__Get_LiveMatchType
    const auto IsLiveHost = reinterpret_cast<uint8_t(__fastcall*)()>(0x1407691E0);                // YGO__DUEL__Get_IsLiveHost

    const char* GetLobbyData(uint64_t lobby, const char* key) { return Steam::GetLobbyData(lobby, key); }
    // Writes a lobby value only when it differs, like QNet__PublishHostLobbyData does: every Steam write fires LobbyDataUpdate, which makes
    // the game publish again, so writing unchanged values looped forever (the host stuck on "Creating session", 2026-10-07).
    void SetLobbyData(uint64_t lobby, const char* key, const char* value)
    {
        const char* current = GetLobbyData(lobby, key);
        if (current && std::strcmp(current, value) == 0)
            return;
        if (!Steam::SetLobbyData(lobby, key, value))
            Logger::WriteLog("Could not write the ban list setting to the lobby (Steam not ready)", MODULE_NAME, 1);
    }

    // Our "exmp" member mark. Written only while Steam doesn't show it on us (member data changes fire an update too, so an unconditional
    // write loops), and re-checked every second while in a lobby: a write made right after entering could be lost, and the host then
    // reported a modded player as a Vanilla Client (2026-10-07).
    uint64_t g_MarkedLobby = 0;
    ULONGLONG g_NextMarkCheck = 0;
    void MarkMember(uint64_t lobby)
    {
        g_MarkedLobby = lobby;
        const char* mark = Steam::GetLobbyMemberData(lobby, Steam::Get_SteamId64(), "exmp");
        if (mark && mark[0] == '1')
            return;
        Steam::SetLobbyMemberData(lobby, "exmp", "1");
    }

    void KeepMemberMark()
    {
        const ULONGLONG now = GetTickCount64();
        if (!g_MarkedLobby || now < g_NextMarkCheck)
            return;
        g_NextMarkCheck = now + 1000;
        MarkMember(g_MarkedLobby);
    }

    // The game's own vector growth (its allocator): std::vector<Option>::_Emplace_reallocate and std::vector<int>::_Emplace_reallocate.
    const auto GrowOptions = reinterpret_cast<void*(__fastcall*)(Vector* vec, char* where, const Option* value)>(0x140852C20);
    const auto GrowInts = reinterpret_cast<void*(__fastcall*)(Vector* vec, char* where, const int* value)>(0x140746A20);

    std::wstring Wide(const std::string& text)
    {
        if (text.empty())
            return {};
        const int n = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring wide(n, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), n);
        return wide;
    }

    char* Slot(char* screen, int slot) { return screen + kSlots + kSlotSize * slot; }
    int& Chosen(char* screen, int slot) { return *reinterpret_cast<int*>(screen + kChosen + 4 * slot); }
    Vector* Rows(char* screen) { return reinterpret_cast<Vector*>(screen + kRows); }

    // ---- the duel format: Master / Speed Duel / Rush Duel, the duel engine's own rules (Core DuelFormat.h, docs/DuelFormat.md), chosen on
    // the More Options page (MatchSettingsPage.h). Every player's engine must run the same format (the online duel is lockstep), so the
    // host's choice goes into lobby data "exformat" and every Yu-Gi-Oh-MP player in the lobby sets it with Core_SetOnlineDuelFormat. Deck
    // sizes follow it: Speed 20-30 main / 5 extra, Rush 40-60 / 15, Master = the player's own [Yu-Gi-Oh-Core] limits (Core_SetDeckLimits).
    // The page is its own screen, so coming back runs LiveSetting_OnEnter, which fills the deck list again under the new limits. The deck
    // check on this screen runs before a joiner reads the lobby, so a joiner's own limits decide which decks they can pick.
    constexpr const char* kLobbyFormat = "exformat";
    const wchar_t* const kFormatLabel = L"Duel format";
    const wchar_t* const kFormatNames[] = { L"Master", L"Speed Duel", L"Rush Duel" };
    const char* const kFormatKeys[] = { "master", "speed", "rush" };
    int g_Format = 0;                    // the lobby's format (host: the More Options page; joiner: the host's lobby data)

    int FormatFromKey(const char* key)
    {
        for (int i = 0; key && i < 3; ++i)
            if (std::strcmp(key, kFormatKeys[i]) == 0)
                return i;
        return 0;
    }

    // Tells Core the format for online duels and the deck limits that go with it.
    void ApplyFormat(int format)
    {
        g_Format = format >= 0 && format < 3 ? format : 0;
        if (!Core::Load() || !Core::Functions().SetOnlineDuelFormat || !Core::Functions().SetDeckLimits)
        {
            if (g_Format != 0)
                Logger::WriteLog("Duel format: this Yu-Gi-Oh-Core is too old for online duel formats; the duel will be Master here", MODULE_NAME, 2);
            return;
        }
        Core::Functions().SetOnlineDuelFormat(g_Format);
        if (g_Format == 1)
            Core::Functions().SetDeckLimits(20, 30, 5, 15);
        else if (g_Format == 2)
            Core::Functions().SetDeckLimits(40, 60, 15, 15);
        else
            Core::Functions().SetDeckLimits(-1, 0, 0, 0);
    }

    // ListState +0 is an int cursor (+4 the row count: reading 8 bytes made every cursor look out of range)
    int CursorSlot(char* screen)
    {
        const Vector* rows = Rows(screen);
        const int cursor = *reinterpret_cast<int32_t*>(screen + kCursor);
        const int64_t count = (rows->End - rows->Begin) / 4;
        return cursor >= 0 && cursor < count ? reinterpret_cast<int*>(rows->Begin)[cursor] : -1;
    }

    // ---- the "More Options" button (hosting a casual match): a second copy of the Create button (RIX::ScreenLiveSetting::widget_Item,
    // screen+2768) beside it, opening the More Options page. Create moves left to make room. For the keys it is slot 5, a row of the
    // settings list just before Create whose own row widget is hidden: down from the last setting lands on it, down again on Create,
    // left/right go between the two buttons. Its description (the help line) sums up what the page set.
    //
    // widget_Item (112 bytes): widget_Base (+16 node), +48 text layer holder (+64 the text layer: widget_Text::SetTextById), +80 animoo
    // holder (+96 its layer). vftable 0x140A76178: +8 SetHighlighted(bool) (0x140855D60), +24 SetVisible(bool). Built by 0x140854B40
    // (parent node, z, owner = screen+88, x, y: the centre), as RIX::ScreenLiveSetting::SetupWidgets builds Create at (960, 920). The mouse
    // test is LayerHolder_HitTest(item+80, packed x/y), as LiveSetting_RowsInput's hover test does for Create (-18).
    const wchar_t* const kLabel = L"More Options";
    wchar_t g_Summary[160] = L"";   // the slot's description (the help line) points at it, so it is rewritten in place
    constexpr float kCreateX = 960.0f, kButtonY = 920.0f, kCreateShiftedX = 650.0f, kButtonX = 1270.0f;
    constexpr size_t kItemSize = 112, kCreateItem = 2768, kRowWidgets = 2696, kRowWidgetSize = 240, kListMode = 3736, kScroll = 4376;
    constexpr size_t kLeaving = 3816, kDialogOpen = 48;

    const auto ItemBaseConstruct = reinterpret_cast<void(__fastcall*)(char* item)>(0x14087BF90);   // RIX::widget_Base::Constructor
    const auto ItemCreate = reinterpret_cast<void(__fastcall*)(char* item, YGO::RIX::SharedNode* parent, int z, void* owner, float x, float y)>(0x140854B40);
    const auto TextSet = reinterpret_cast<void(__fastcall*)(void* text, int64_t idOrText)>(0x14075E000);   // RIX::widget_Text::SetTextById
    const auto LayerHitTest = reinterpret_cast<bool(__fastcall*)(char* holder, int64_t mouse)>(0x1407858E0);   // RIX::LayerHolder_HitTest
    const auto SelectVisibleRow = reinterpret_cast<char(__fastcall*)(char* list, int row)>(0x1408688A0);   // RIX::ListState_SelectVisibleRow
    const auto TakeMouseClick = reinterpret_cast<int(__fastcall*)(void* input, unsigned mask)>(0x1408006B0);
    const auto RefreshRows = reinterpret_cast<void(__fastcall*)(char* screen)>(0x140856500);   // LiveSetting_RefreshRows (through our hook)
    constexpr uintptr_t kItemVftable = 0x140A76178, kInput = 0x142924010, kMouse = 0x142924070;   // +0 x/y (2 ints), +8 moved this frame

    std::map<char*, char*> g_Buttons;   // screen -> our button (kept for the game's life, like the lobby lines)

    void ItemHighlight(char* item, bool on) { (*reinterpret_cast<void(__fastcall***)(char*, bool)>(item))[1](item, on); }
    void ItemShow(char* item, bool on) { (*reinterpret_cast<void(__fastcall***)(char*, bool)>(item))[3](item, on); }

    char* SettingsButton(char* screen)
    {
        if (auto found = g_Buttons.find(screen); found != g_Buttons.end())
            return found->second;
        auto* item = static_cast<char*>(_aligned_malloc(kItemSize, 16));
        if (!item)
            return nullptr;
        std::memset(item, 0, kItemSize);
        ItemBaseConstruct(item);
        *reinterpret_cast<uintptr_t*>(item) = kItemVftable;   // RIX::ScreenLiveSetting::Constructor does the same for Create
        YGO::RIX::SharedNode parent = YGO::RIX::ParentRef(YGO::RIX::ScreenRoot(screen));   // consumed
        ItemCreate(item, &parent, 13, screen + 88, kButtonX, kButtonY);
        TextSet(*reinterpret_cast<void**>(item + 64), reinterpret_cast<int64_t>(kLabel));
        ItemHighlight(item, false);
        g_Buttons[screen] = item;
        return item;
    }

    int RowIndex(char* screen, int slot)
    {
        const Vector* rows = Rows(screen);
        const int64_t count = (rows->End - rows->Begin) / 4;
        for (int64_t i = 0; i < count; ++i)
            if (reinterpret_cast<int*>(rows->Begin)[i] == slot)
                return static_cast<int>(i);
        return -1;
    }

    int RowCount(char* screen) { return static_cast<int>((Rows(screen)->End - Rows(screen)->Begin) / 4); }

    // Moves the cursor to a row (absolute index) as a mouse hover does, and redraws.
    void SelectRow(char* screen, int row)
    {
        if (SelectVisibleRow(screen + kCursor, row - *reinterpret_cast<int32_t*>(screen + kScroll)))
            RefreshRows(screen);
    }

    void UpdateSummary()
    {
        const std::wstring ban = BanList::CurrentMode() == BanList::Mode::Off ? L"off (every card at 3)"
            : BanList::CurrentMode() == BanList::Mode::Custom ? Wide(BanList::CurrentList().Name) : L"the game's";
        swprintf_s(g_Summary, L"Duel format: %s. Ban list: %s. Every player in the lobby plays these; Ranked always uses the game's rules.",
            kFormatNames[g_Format], ban.c_str());
    }

    // No page (the plugin's pages folder missing, or an old RIX): the screen's own message box says so, rather than the click doing nothing.
    const wchar_t* const kNoPageText = L"More Options couldn't be opened.\nReinstall Yu-Gi-Oh-MP (its pages folder is missing).";

    void OpenSettingsPage(char* screen)
    {
        if (MatchSettingsPage::Open())
            return;
        Logger::WriteLog("More Options: can't open pages\\mp-match-settings (Plugins\\Yu-Gi-Oh-MP\\pages missing, or Yu-Gi-Oh-RIX too old)", MODULE_NAME, 1);
        YGO::RIX::EmptyFunction none;
        YGO::RIX::ShowMessageText(screen, -1, kNoPageText, &none);
    }

    void PushOption(Vector* options, int64_t text)
    {
        const Option option{ text, 0 };
        if (options->End == options->Capacity)
            GrowOptions(options, options->End, &option);
        else
        {
            *reinterpret_cast<Option*>(options->End) = option;
            options->End += sizeof(Option);
        }
    }

    void __fastcall Hook_BuildOptions(char* screen)
    {
        orig_BuildOptions(screen);   // it clears every slot's options first, ours too
        char* slot = Slot(screen, kSlot);
        *reinterpret_cast<int64_t*>(slot + 0) = reinterpret_cast<int64_t>(kLabel);
        UpdateSummary();   // LiveSetting_OnEnter builds the options, so coming back from the page shows what it set
        *reinterpret_cast<int64_t*>(slot + 8) = reinterpret_cast<int64_t>(g_Summary);
        Vector* options = reinterpret_cast<Vector*>(slot + 16);
        options->End = options->Begin;
        PushOption(options, reinterpret_cast<int64_t>(kLabel));   // never shown (the row widget is hidden); keeps the slot like the others
        Chosen(screen, kSlot) = 0;
        MatchSettingsPage::Register();   // RIX is loaded by now (it may be injected after this plugin)
    }

    void InsertRowBeforeLast(Vector* rows, int slot)
    {
        const int64_t count = (rows->End - rows->Begin) / 4;
        const int last = reinterpret_cast<int*>(rows->Begin)[count - 1];
        reinterpret_cast<int*>(rows->Begin)[count - 1] = slot;
        if (rows->End == rows->Capacity)
            GrowInts(rows, rows->End, &last);
        else
        {
            *reinterpret_cast<int*>(rows->End) = last;
            rows->End += 4;
        }
    }

    // Hosting a casual match (the last row is Create): slot 5 goes in just before it (the button's place in the key order). Ranked gets
    // none (fixed rules).
    void __fastcall Hook_BuildRows(char* screen)
    {
        orig_BuildRows(screen);
        Vector* rows = Rows(screen);
        const int64_t count = (rows->End - rows->Begin) / 4;
        if (count == 0 || reinterpret_cast<int*>(rows->Begin)[count - 1] != kCreate || LiveMatchType() == kRankedMatch)
            return;
        InsertRowBeforeLast(rows, kSlot);
    }

    // After the game places the rows: hide slot 5's row widget, show the button and move Create aside (or put Create back when not hosting).
    int64_t __fastcall Hook_LayoutRows(char* screen)
    {
        const int64_t result = orig_LayoutRows(screen);
        const int row = RowIndex(screen, kSlot);
        if (row < 0 && !g_Buttons.contains(screen))
            return result;
        char* button = SettingsButton(screen);
        if (!button)
            return result;
        if (row >= 0)
            ItemShow(*reinterpret_cast<char**>(screen + kRowWidgets) + kRowWidgetSize * row, false);
        ItemShow(button, row >= 0);
        YGO::RIX::NodeSetX(*reinterpret_cast<void**>(screen + kCreateItem + 16), row >= 0 ? kCreateShiftedX : kCreateX);
        return result;
    }

    // The button is highlighted like Create: the settings list has the input and the cursor is on its row.
    int64_t __fastcall Hook_RefreshRows(char* screen)
    {
        const int64_t result = orig_RefreshRows(screen);
        auto found = g_Buttons.find(screen);
        if (found == g_Buttons.end())
            return result;
        const int row = RowIndex(screen, kSlot);
        ItemHighlight(found->second, row >= 0 && *reinterpret_cast<int*>(screen + kListMode) == 1 && *reinterpret_cast<int32_t*>(screen + kCursor) == row);
        return result;
    }

    // The mouse on the button, before the game's input (which never looks at it): hovering selects it, a click opens the page (taken, so
    // the game doesn't see the click).
    void ButtonMouse(char* screen)
    {
        auto found = g_Buttons.find(screen);
        const int row = RowIndex(screen, kSlot);
        if (found == g_Buttons.end() || row < 0 || *(screen + kLeaving) || *reinterpret_cast<int*>(screen + kDialogOpen) != 0)
            return;
        const int64_t mouse = *reinterpret_cast<int64_t*>(kMouse);
        if (!LayerHitTest(found->second + 80, mouse))
            return;
        if (*reinterpret_cast<char*>(kMouse + 8) && *reinterpret_cast<int*>(screen + kListMode) == 1)
            SelectRow(screen, row);
        if (TakeMouseClick(reinterpret_cast<void*>(kInput), kConfirm))
            OpenSettingsPage(screen);
    }

    void __fastcall Hook_Update(char* screen, void* ui)
    {
        ButtonMouse(screen);
        orig_Update(screen, ui);
    }

    // Keys: confirm on the button's row opens the page (LiveSetting_RowsInput would treat slot 5 as "not an action" and jump to Create, so
    // the confirm is taken out); right on Create goes to the button (Create gets no left/right from the game: slot 6).
    void __fastcall Hook_RowsInput(char* screen, int pressed)
    {
        const int slot = CursorSlot(screen);
        const bool open = (pressed & kConfirm) != 0 && slot == kSlot;
        const bool toButton = (pressed & kRight) != 0 && slot == kCreate && RowIndex(screen, kSlot) >= 0;
        orig_RowsInput(screen, open ? pressed & ~kConfirm : toButton ? pressed & ~kRight : pressed);
        if (open)
            OpenSettingsPage(screen);
        else if (toButton)
            SelectRow(screen, RowIndex(screen, kSlot));
    }

    // Left on the button goes back to Create; a confirm reaching here (a click on the hidden row can't, but to be safe) opens the page.
    char __fastcall Hook_ChangeOption(char* screen, int64_t pressed)
    {
        if (CursorSlot(screen) != kSlot)
            return orig_ChangeOption(screen, pressed);
        if (pressed & kConfirm)
            OpenSettingsPage(screen);
        else if (pressed & kLeft)
            SelectRow(screen, RowCount(screen) - 1);
        return 0;
    }

    // ---- the lobby screen (RIX::ScreenLiveLobby, screen 36): its match settings panel is a widget_SessionInfo (screen+4192) with five
    // widget_SettingsLine rows (vector at +192, 240 bytes each: type, single/match, LP, time, open to). SessionInfo_Show (0x1408B2E30)
    // fills them from the lobby's settings and stacks the visible ones at 133 + n * 130. A sixth row, "Ban list: Off / <list name>", is
    // built the same way (widget_SettingsLine::Constructor + CreatePart2, in memory of ours kept for the game's life) and shown under
    // them unless the lobby plays the game's list: the host's own choice, or what the host's lobby data said when we joined.
    using Show_t = void(__fastcall*)(char* info, void* settings);
    Show_t orig_SessionInfoShow = reinterpret_cast<Show_t>(0x1408B2E30);   // SessionInfo_Show

    const auto LineConstruct = reinterpret_cast<char*(__fastcall*)(void* vec, char* at, int64_t count)>(0x1408568E0);  // widget_SettingsLine::Constructor
    const auto LineCreate = reinterpret_cast<void(__fastcall*)(char* line, YGO::RIX::SharedNode* parent, int z, void* owner, float x, float y,
        unsigned char arrows, float width, int textWidth)>(0x1408B3350);                                               // widget_SessionInfo::CreatePart2
    const auto LineSetLabel = reinterpret_cast<void(__fastcall*)(char* line, int64_t text)>(0x1408B3850);
    const auto LineSetValue = reinterpret_cast<void(__fastcall*)(char* line, const wchar_t* text)>(0x1408B38B0);
    const auto NodeIsVisible = reinterpret_cast<bool(__fastcall*)(void* node)>(0x140759AE0);

    constexpr size_t kInfoLines = 192, kLineSize = 240, kInfoLineCount = 5;
    std::map<char*, char*> g_BanLines;       // SessionInfo widget -> our "Ban list" row
    std::map<char*, char*> g_FormatLines;    // SessionInfo widget -> our "Duel format" row
    std::map<char*, char*> g_VanillaLines;   // SessionInfo widget -> our "No Yu-Gi-Oh-MP" row (host only)
    std::wstring g_LineValue;                // the rows' value texts (kept while shown)
    std::wstring g_VanillaValue;
    std::vector<std::string> g_VanillaNames; // members of the hosted lobby without the "exmp" mark
    char* g_LastInfo = nullptr;              // the lobby's SessionInfo widget, to update the rows when members change

    char* MakeLine(char* info, std::map<char*, char*>& lines, const wchar_t* label)
    {
        auto found = lines.find(info);
        if (found != lines.end())
            return found->second;
        auto* line = static_cast<char*>(_aligned_malloc(kLineSize, 16));
        if (!line)
            return nullptr;
        LineConstruct(nullptr, line, 1);
        YGO::RIX::SharedNode parent = YGO::RIX::ParentRef(reinterpret_cast<YGO::RIX::SharedNode*>(info + 16));   // consumed by CreatePart2
        LineCreate(line, &parent, 7, *reinterpret_cast<void**>(info + 32), 259.0f, 133.0f, 0, 0.0f, 1139736576);   // as SessionInfo makes its rows
        LineSetLabel(line, reinterpret_cast<int64_t>(label));
        lines[info] = line;
        return line;
    }

    char* BanLine(char* info) { return MakeLine(info, g_BanLines, L"Ban list"); }
    char* FormatLine(char* info) { return MakeLine(info, g_FormatLines, kFormatLabel); }
    char* VanillaLine(char* info) { return MakeLine(info, g_VanillaLines, L"No Yu-Gi-Oh-MP"); }

    void SetVisible(char* widget, bool visible)
    {
        using SetVisible_t = void(__fastcall*)(char* self, bool visible);
        (*reinterpret_cast<SetVisible_t**>(widget))[3](widget, visible);   // vftable slot 3 = SetVisible
    }

    // Shows/places our rows under the game's visible ones: "Ban list" (unless the game's list), "Duel format" (unless Master), then
    // "No Yu-Gi-Oh-MP: <names>".
    void LayoutExtraLines(char* info)
    {
        int visible = 0;
        char* rows = *reinterpret_cast<char**>(info + kInfoLines);
        for (size_t i = 0; rows && i < kInfoLineCount; ++i)
            if (void* node = *reinterpret_cast<void**>(rows + i * kLineSize + 16); node && NodeIsVisible(node))
                ++visible;

        const bool showBan = BanList::CurrentMode() != BanList::Mode::Game;
        if (showBan || g_BanLines.contains(info))
            if (char* line = BanLine(info))
            {
                SetVisible(line, showBan);
                if (showBan)
                {
                    g_LineValue = BanList::CurrentMode() == BanList::Mode::Off ? L"Off" : Wide(BanList::CurrentList().Name);
                    LineSetValue(line, g_LineValue.c_str());
                    YGO::RIX::NodeSetY(*reinterpret_cast<void**>(line + 16), visible++ * 130.0f + 133.0f);
                }
            }

        const bool showFormat = g_Format != 0;
        if (showFormat || g_FormatLines.contains(info))
            if (char* line = FormatLine(info))
            {
                SetVisible(line, showFormat);
                if (showFormat)
                {
                    LineSetValue(line, kFormatNames[g_Format]);
                    YGO::RIX::NodeSetY(*reinterpret_cast<void**>(line + 16), visible++ * 130.0f + 133.0f);
                }
            }

        const bool showVanilla = !g_VanillaNames.empty();
        if (showVanilla || g_VanillaLines.contains(info))
            if (char* line = VanillaLine(info))
            {
                SetVisible(line, showVanilla);
                if (showVanilla)
                {
                    g_VanillaValue.clear();
                    for (const auto& name : g_VanillaNames)
                        g_VanillaValue += (g_VanillaValue.empty() ? L"" : L", ") + Wide(name);
                    LineSetValue(line, g_VanillaValue.c_str());
                    YGO::RIX::NodeSetY(*reinterpret_cast<void**>(line + 16), visible++ * 130.0f + 133.0f);
                }
            }
    }

    void __fastcall Hook_SessionInfoShow(char* info, void* settings)
    {
        orig_SessionInfoShow(info, settings);
        g_LastInfo = info;
        LayoutExtraLines(info);
    }

    // Leaving the screen without hosting: the ban list is back (it is only ever off for a lobby), and the format is Master again.
    void __fastcall Hook_Back(char* screen)
    {
        BanList::UseGame();
        if (g_Format != 0)
            ApplyFormat(0);
        orig_Back(screen);
    }

    // ---- a joiner is asked before playing the host's list (Yes = play it, No = leave the lobby). The box is the lobby screen's own dialog
    // (screen+432), built like RIX::Screen::ShowYesNo but with a No that does something (Yu-Gi-Oh-RIX.h Dialog*), on the lobby screen's
    // first update after joining.
    struct Pending
    {
        bool Ask = false;
        BanList::Mode Mode = BanList::Mode::Game;
        BanList::List List;
    } g_Pending, g_Asked;
    char* g_PromptScreen = nullptr;
    std::wstring g_PromptText;

    using LobbyUpdate_t = void(__fastcall*)(char* screen, void* ui, float seconds);
    LobbyUpdate_t orig_LobbyUpdate = reinterpret_cast<LobbyUpdate_t>(0x1408CFAA0);   // RIX::ScreenLiveLobby::Update
    const auto LobbyExit = reinterpret_cast<void(__fastcall*)(char* screen)>(0x1408CF990);   // RIX::ScreenLiveLobby::ExitToMenu

    // The buttons only record what comes next: a button's callback runs while the game is still closing the box, so the next box (a page of
    // the list, the question again) or leaving is done from the lobby screen's update once the dialog is closed (screen+48 back to 0).
    // Button labels: widget_Dialog item labels above 2214 are taken as a wchar_t* (sub_1408988F0), so ours are plain strings.
    enum class PromptStep { None, Ask, Page, Leave };
    PromptStep g_Next = PromptStep::None;
    size_t g_Page = 0;
    void* g_JoinQnet = nullptr;     // QNet and lobby from OnLobbyEnter, to leave the lobby on No
    uint64_t g_JoinedLobby = 0;
    ULONGLONG g_LeaveAt = 0;        // No: the "declined" mark goes out first, the leave a second later so the host sees it
    constexpr size_t kNamesPerPage = 10;
    constexpr const wchar_t* kViewLabel = L"View list";
    constexpr const wchar_t* kNextLabel = L"Next page";
    constexpr const wchar_t* kBackLabel = L"Back";
    constexpr const char* kMemberBan = "exban";   // member data: "declined" = this joiner said No to the host's list and is leaving
    const auto CardName = reinterpret_cast<const wchar_t*(__fastcall*)(short konamiId)>(YGO::Addr::Card_GetNameFromKonamiId);   // Get_CardNameFromKonamiId

    void OnAccept()
    {
        if (g_Asked.Mode == BanList::Mode::Off)
            BanList::UseOff();
        else if (g_Asked.Mode == BanList::Mode::Custom)
            BanList::UseCustom(g_Asked.List);
        Logger::WriteLog("Accepted the host's ban list", MODULE_NAME, 0);
    }

    void OnDecline() { g_Next = PromptStep::Leave; }
    void OnView() { g_Page = 0; g_Next = PromptStep::Page; }
    void OnNextPage() { ++g_Page; g_Next = PromptStep::Page; }
    void OnBack() { g_Next = PromptStep::Ask; }

    void ShowDialog(char* screen, const std::wstring& text, std::initializer_list<std::pair<int64_t, void(*)()>> items)
    {
        char* dialog = screen + 432;
        YGO::RIX::DialogClear(dialog);
        YGO::RIX::DialogSetMode(dialog, 1);
        YGO::RIX::DialogSetText(dialog, text.c_str());
        for (const auto& [label, callback] : items)
        {
            std::function<void()> press = callback;   // plain function pointers fit the small buffer; the game takes them over
            YGO::RIX::DialogAddItem(dialog, label, &press, -1);
        }
        using SetVisible_t = void(__fastcall*)(char* self, bool visible);
        (*reinterpret_cast<SetVisible_t**>(dialog))[3](dialog, true);
        *reinterpret_cast<int*>(screen + 48) = 1;   // the screen sends its input to the dialog
    }

    void ShowQuestion(char* screen)
    {
        if (g_Asked.Mode == BanList::Mode::Off)
        {
            g_PromptText = L"The host plays with the ban list off: every card can be used at 3 copies.\n\nPlay with it?";
            ShowDialog(screen, g_PromptText, { { YGO::RIX::DialogLabelYes, &OnAccept }, { YGO::RIX::DialogLabelNo, &OnDecline } });
            return;
        }
        g_PromptText = L"The host plays with the ban list \"" + Wide(g_Asked.List.Name) + L"\" (" + std::to_wstring(g_Asked.List.Forbidden.size()) +
            L" forbidden, " + std::to_wstring(g_Asked.List.Limited.size()) + L" limited, " + std::to_wstring(g_Asked.List.SemiLimited.size()) +
            L" semi-limited).\n\nPlay with it?";
        ShowDialog(screen, g_PromptText, { { YGO::RIX::DialogLabelYes, &OnAccept }, { YGO::RIX::DialogLabelNo, &OnDecline },
            { reinterpret_cast<int64_t>(kViewLabel), &OnView } });
    }

    void ShowPage(char* screen)
    {
        std::vector<std::pair<const wchar_t*, uint16_t>> entries;
        for (uint16_t id : g_Asked.List.Forbidden) entries.emplace_back(L"Forbidden", id);
        for (uint16_t id : g_Asked.List.Limited) entries.emplace_back(L"Limited", id);
        for (uint16_t id : g_Asked.List.SemiLimited) entries.emplace_back(L"Semi-Limited", id);
        const size_t pages = (std::max<size_t>)(1, (entries.size() + kNamesPerPage - 1) / kNamesPerPage);
        g_Page = (std::min)(g_Page, pages - 1);
        g_PromptText = L"\"" + Wide(g_Asked.List.Name) + L"\", page " + std::to_wstring(g_Page + 1) + L" of " + std::to_wstring(pages) + L"\n";
        if (entries.empty())
            g_PromptText += L"\nNo limits: every card at 3 copies.";
        for (size_t i = g_Page * kNamesPerPage; i < entries.size() && i < (g_Page + 1) * kNamesPerPage; ++i)
        {
            const wchar_t* name = CardName(static_cast<short>(entries[i].second));
            g_PromptText += L"\n" + std::wstring(entries[i].first) + L": " + (name && *name ? std::wstring(name) : L"#" + std::to_wstring(entries[i].second));
        }
        if (g_Page + 1 < pages)
            ShowDialog(screen, g_PromptText, { { reinterpret_cast<int64_t>(kNextLabel), &OnNextPage }, { reinterpret_cast<int64_t>(kBackLabel), &OnBack } });
        else
            ShowDialog(screen, g_PromptText, { { reinterpret_cast<int64_t>(kBackLabel), &OnBack } });
    }

    void LeaveNow(char* screen);

    // Runs every lobby update on a joiner: shows the next box once the last one is closed, and leaves after a No.
    void RunPrompt(char* screen)
    {
        if (g_LeaveAt && GetTickCount64() >= g_LeaveAt)
        {
            g_LeaveAt = 0;
            LeaveNow(screen);
            return;
        }
        if (g_Next == PromptStep::None || *reinterpret_cast<int*>(screen + 48) != 0)
            return;
        const PromptStep step = g_Next;
        g_Next = PromptStep::None;
        if (step == PromptStep::Ask)
            ShowQuestion(screen);
        else if (step == PromptStep::Page)
            ShowPage(screen);
        else if (step == PromptStep::Leave)
        {
            Logger::WriteLog("Declined the host's ban list: leaving the lobby", MODULE_NAME, 0);
            BanList::UseGame();
            if (g_JoinedLobby)
                Steam::SetLobbyMemberData(g_JoinedLobby, kMemberBan, "declined");
            g_LeaveAt = GetTickCount64() + 1000;
        }
    }

    void AskForBanList(char* screen)
    {
        g_Asked = g_Pending;
        g_Pending.Ask = false;
        g_PromptScreen = screen;
        ShowQuestion(screen);
    }

    // ---- the host sees which players don't run Yu-Gi-Oh-MP: they can't see this lobby's ban list and play with the game's own. A member
    // without the "exmp" mark 8 seconds after we first saw them gets a console line and is listed on the settings panel's
    // "No Yu-Gi-Oh-MP" row (no popup), so the host can kick them. Members who leave drop off the row.
    uint64_t g_HostLobby = 0;
    std::map<uint64_t, ULONGLONG> g_FirstSeen;
    std::map<uint64_t, bool> g_Reported;
    std::map<uint64_t, std::string> g_Vanilla;   // member -> name, still in the lobby
    std::set<uint64_t> g_Declined;               // members who said No to the ban list (reported once)
    ULONGLONG g_NextCheck = 0;

    void CheckForVanillaClients()
    {
        const ULONGLONG now = GetTickCount64();
        if (!g_HostLobby || now < g_NextCheck)
            return;
        g_NextCheck = now + 1000;
        const uint64_t self = Steam::Get_SteamId64();
        const std::vector<uint64_t> members = Steam::LobbyMembers(g_HostLobby);
        for (uint64_t member : members)
            if (member != self && !g_Declined.contains(member))
                if (const char* ban = Steam::GetLobbyMemberData(g_HostLobby, member, kMemberBan); ban && std::strcmp(ban, "declined") == 0)
                {
                    g_Declined.insert(member);
                    Logger::WriteLog(Steam::PersonaName(member) + " declined the ban list and is leaving", MODULE_NAME, 1);
                }
        bool changed = false;
        for (auto it = g_Vanilla.begin(); it != g_Vanilla.end();)
            if (std::find(members.begin(), members.end(), it->first) == members.end())
            {
                g_Reported.erase(it->first);
                g_FirstSeen.erase(it->first);
                it = g_Vanilla.erase(it);
                changed = true;
            }
            else
                ++it;
        for (uint64_t member : members)
        {
            if (member == 0 || member == self || g_Reported[member])
                continue;
            auto [seen, added] = g_FirstSeen.try_emplace(member, now);
            const char* mark = Steam::GetLobbyMemberData(g_HostLobby, member, kMemberMark);
            if (mark && mark[0] == '1')
            {
                g_Reported[member] = true;   // runs Yu-Gi-Oh-MP
                continue;
            }
            if (now - seen->second < 8000)
                continue;
            g_Reported[member] = true;
            g_Vanilla[member] = Steam::PersonaName(member);
            Logger::WriteLog(g_Vanilla[member] + " does not use Yu-Gi-Oh-MP", MODULE_NAME, 1);
            changed = true;
        }
        if (changed)
        {
            g_VanillaNames.clear();
            for (const auto& [id, name] : g_Vanilla)
                g_VanillaNames.push_back(name);
            if (g_LastInfo)
                LayoutExtraLines(g_LastInfo);
        }
    }

    void __fastcall Hook_LobbyUpdate(char* screen, void* ui, float seconds)
    {
        orig_LobbyUpdate(screen, ui, seconds);
        KeepMemberMark();
        if (IsLiveHost())
            CheckForVanillaClients();
        else
        {
            if (g_Pending.Ask)
                AskForBanList(screen);
            RunPrompt(screen);
        }
    }

    // The host tells the lobby. Players without Yu-Gi-Oh-MP never read the key: their own ban list applies to their own deck.
    void __fastcall Hook_PublishHostLobbyData(void* qnet, uint64_t lobby)
    {
        orig_PublishHostLobbyData(qnet, lobby);
        const BanList::Mode mode = BanList::CurrentMode();
        SetLobbyData(lobby, kLobbyMode, mode == BanList::Mode::Off ? "off" : mode == BanList::Mode::Custom ? "custom" : "game");
        const std::string list = mode == BanList::Mode::Custom ? BanList::Encode(BanList::CurrentList()) : std::string();
        const size_t parts = (list.size() + kLobbyChunk - 1) / kLobbyChunk;
        for (size_t i = 0; i < parts; ++i)
            SetLobbyData(lobby, (kLobbyListPrefix + std::to_string(i)).c_str(), list.substr(i * kLobbyChunk, kLobbyChunk).c_str());
        SetLobbyData(lobby, kLobbyParts, std::to_string(parts).c_str());
        SetLobbyData(lobby, kLobbyTag, TagCoopArmed() ? "coop" : "off");
        SetLobbyData(lobby, kLobbyFormat, kFormatKeys[g_Format]);
        MarkMember(lobby);
        g_HostLobby = lobby;
    }

    void __fastcall Hook_OnLobbyEnter(void* qnet, void* lobbyEnter, char ioFailure)
    {
        orig_OnLobbyEnter(qnet, lobbyEnter, ioFailure);
        if (ioFailure || !lobbyEnter || IsLiveHost())
            return;
        const uint64_t lobby = *static_cast<uint64_t*>(lobbyEnter);
        g_JoinQnet = qnet;
        g_JoinedLobby = lobby;
        g_Next = PromptStep::None;
        g_LeaveAt = 0;
        MarkMember(lobby);   // tells the host this player runs Yu-Gi-Oh-MP
        const char* mode = GetLobbyData(lobby, kLobbyMode);
        const std::string value = mode ? mode : "";
        BanList::UseGame();   // until the player accepts the host's list
        g_Pending = {};
        if (value == "off")
        {
            g_Pending.Mode = BanList::Mode::Off;
            g_Pending.Ask = true;
        }
        else if (value == "custom")
        {
            const char* partsText = GetLobbyData(lobby, kLobbyParts);
            const int parts = partsText ? std::atoi(partsText) : 0;
            std::string data;
            for (int i = 0; i < parts && i < 64; ++i)
                if (const char* part = GetLobbyData(lobby, (kLobbyListPrefix + std::to_string(i)).c_str()))
                    data += part;
            if (!data.empty() && BanList::Decode(data, g_Pending.List))
            {
                if (g_Pending.List.Forbidden.empty() && g_Pending.List.Limited.empty() && g_Pending.List.SemiLimited.empty() && g_Pending.List.Allowed.empty())   // sent by name only
                    for (const auto& saved : BanList::Saved())
                        if (saved.Name == g_Pending.List.Name)
                            g_Pending.List = saved;
                g_Pending.Mode = BanList::Mode::Custom;
                g_Pending.Ask = true;
            }
        }
        Logger::WriteLog("Joined a lobby (ban list: " + (value.empty() ? std::string("the game's") : value) + ")", MODULE_NAME, 0);

        // The host's tag duel setting, so both games set the duel up the same way.
        const char* tag = GetLobbyData(lobby, kLobbyTag);
        const bool coop = tag && std::string(tag) == "coop";
        if (!SetTagCoop(coop) && coop)
            Logger::WriteLog("The host set up an online co-op tag duel but Yu-Gi-Oh-TagDuel isn't loaded here: the two games will not match", MODULE_NAME, 2);
        else if (coop)
            Logger::WriteLog("Joined an online co-op tag duel lobby (you + an AI partner vs the host + theirs)", MODULE_NAME, 0);

        // The host's duel format: both engines must run it (no key = a host without this row = Master).
        ApplyFormat(FormatFromKey(GetLobbyData(lobby, kLobbyFormat)));
        if (g_Format != 0)
            Logger::WriteLog(std::format("The lobby plays {} rules", kFormatKeys[g_Format]), MODULE_NAME, 0);
    }

    void __fastcall Hook_LeaveLobby(void* qnet, uint64_t lobby)
    {
        orig_LeaveLobby(qnet, lobby);
        g_Pending = {};
        g_HostLobby = 0;
        g_MarkedLobby = 0;
        g_FirstSeen.clear();
        g_Reported.clear();
        g_Vanilla.clear();
        g_VanillaNames.clear();
        g_Declined.clear();
        g_JoinedLobby = 0;
        g_Next = PromptStep::None;
        g_LeaveAt = 0;
        if (g_LastInfo && g_VanillaLines.contains(g_LastInfo))
            SetVisible(g_VanillaLines[g_LastInfo], false);
        BanList::UseGame();
        if (g_Format != 0)
            ApplyFormat(0);
    }

    // No: really leave the Steam lobby (ExitToMenu alone only changes screen, so the host still had the player), then go back to the menu.
    void LeaveNow(char* screen)
    {
        if (g_JoinQnet && g_JoinedLobby)
            Hook_LeaveLobby(g_JoinQnet, g_JoinedLobby);
        LobbyExit(screen);
    }
}

namespace LiveSetting
{
    const char* FormatKey() { return kFormatKeys[g_Format]; }

    void SetFormatKey(const char* key)
    {
        const int format = FormatFromKey(key);
        if (format == g_Format)
            return;
        ApplyFormat(format);
        Logger::WriteLog(std::format("Lobby duel format: {}", kFormatKeys[g_Format]), MODULE_NAME, 0);
    }

    // LiveSetting_RowsInput only sends left/right to ChangeOption on rows 0-4 ("cmp dword [r9+r8*4], 5; jb" at 0x1408542D6): our row 5
    // never got them. Raise the bound to 6 (6+ are the Create/Join buttons) so left/right on the row reach Hook_ChangeOption, which opens
    // the More Options page.
    void PatchKeyboardBound()
    {
        // cmp dword [r9+r8*4], 5 -> 6
        YGO::Memory::PatchBytes(0x1408542D6, { 0x43, 0x83, 0x3C, 0x81, 0x05 }, { 0x43, 0x83, 0x3C, 0x81, 0x06 }, "More Options row left/right keys");
    }

    void Attach()
    {
        PatchKeyboardBound();
        DetourAttach(&reinterpret_cast<PVOID&>(orig_BuildOptions), Hook_BuildOptions);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_BuildRows), Hook_BuildRows);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_RowsInput), Hook_RowsInput);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_ChangeOption), Hook_ChangeOption);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_LayoutRows), Hook_LayoutRows);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_RefreshRows), Hook_RefreshRows);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_Update), Hook_Update);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_Back), Hook_Back);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_PublishHostLobbyData), Hook_PublishHostLobbyData);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_OnLobbyEnter), Hook_OnLobbyEnter);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_LeaveLobby), Hook_LeaveLobby);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_SessionInfoShow), Hook_SessionInfoShow);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_LobbyUpdate), Hook_LobbyUpdate);
    }
}
