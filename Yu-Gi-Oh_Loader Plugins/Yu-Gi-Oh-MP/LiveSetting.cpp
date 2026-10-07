#include "LiveSetting.h"

#include <Windows.h>
#include <cstdint>

#include "Detours.h"
#include "Logger.h"
#include "BanList.h"
#include "Steam.h"
#include "YuGiOh/YuGiOh-RIX.h"

#include <cstdlib>
#include <functional>
#include <malloc.h>
#include <map>
#include <string>
#include <vector>

namespace
{
    constexpr int kSlot = 5;                 // the free setting slot
    constexpr int kCreate = 6;               // the action row when hosting
    constexpr size_t kSlots = 3848, kSlotSize = 48;          // +0 label, +8 description, +16 options (begin, end, capacity)
    constexpr size_t kChosen = 4328;         // int per slot
    constexpr size_t kRows = 3824;           // std::vector<int> of slot numbers
    constexpr size_t kCursor = 4368;         // ListState: +0 cursor
    constexpr size_t kMode = 3736;           // 1 = the settings rows have the input
    constexpr int kLeft = 4, kRight = 8;
    constexpr int kOptionGame = 0, kOptionOff = 1, kFirstCustom = 2;   // then one option per saved list

    struct Vector { char* Begin; char* End; char* Capacity; };
    struct Option { int64_t Text; int64_t Unused; };

    constexpr int kRankedMatch = 1;          // g_LiveMatchType 1: fixed rules, the game shows it no settings, so no ban list row either
    // Lobby data (only Yu-Gi-Oh-MP reads it): "exbanmode" = game / off / custom; the custom list (BanList::Encode) in "exbanlist0",
    // "exbanlist1"... with "exbanparts" = how many, so a joiner plays the host's list without having the file. Steam keeps up to 8 KB per
    // value, so the list is split; any list fits (the game's own is about 620 characters).
    // Member data "exmp" = "1": the player runs Yu-Gi-Oh-MP. The host is told about players without it ("Vanilla Client").
    constexpr const char* kLobbyMode = "exbanmode";
    constexpr const char* kLobbyParts = "exbanparts";
    constexpr const char* kLobbyListPrefix = "exbanlist";
    constexpr size_t kLobbyChunk = 7000;
    constexpr const char* kMemberMark = "exmp";

    using Screen_t = void(__fastcall*)(char* screen);
    using Input_t = void(__fastcall*)(char* screen, int pressed);
    using Change_t = char(__fastcall*)(char* screen, int64_t pressed);

    Screen_t orig_BuildOptions = reinterpret_cast<Screen_t>(0x1408546A0);   // LiveSetting_BuildOptions
    Screen_t orig_BuildRows = reinterpret_cast<Screen_t>(0x140855B00);      // LiveSetting_BuildRows
    Input_t orig_RowsInput = reinterpret_cast<Input_t>(0x140853D70);        // LiveSetting_RowsInput
    Change_t orig_ChangeOption = reinterpret_cast<Change_t>(0x140855DD0);   // LiveSetting_ChangeOption
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
    void SetLobbyData(uint64_t lobby, const char* key, const char* value)
    {
        if (!Steam::SetLobbyData(lobby, key, value))
            Logger::WriteLog("Could not write the ban list setting to the lobby (Steam not ready)", MODULE_NAME, 1);
    }

    // The game's own vector growth (its allocator): std::vector<Option>::_Emplace_reallocate and std::vector<int>::_Emplace_reallocate.
    const auto GrowOptions = reinterpret_cast<void*(__fastcall*)(Vector* vec, char* where, const Option* value)>(0x140852C20);
    const auto GrowInts = reinterpret_cast<void*(__fastcall*)(Vector* vec, char* where, const int* value)>(0x140746A20);

    const wchar_t* const kLabel = L"Ban list";
    const wchar_t* const kDescription = L"Game: the game's own Forbidden/Limited list. Off: every card at 3 copies. Others: your lists (WolfX: Forbidden & Limited > Save as MP list).";
    const wchar_t* const kGame = L"Game";
    const wchar_t* const kOff = L"Off";

    std::wstring Wide(const std::string& text)
    {
        if (text.empty())
            return {};
        const int n = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring wide(n, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), n);
        return wide;
    }

    // The saved lists' names as the option texts (kept for the game's life: the options point at them).
    const std::vector<std::wstring>& CustomNames()
    {
        static std::vector<std::wstring> names;
        static bool built = false;
        if (!built)
        {
            built = true;
            for (const auto& list : BanList::Saved())
                names.push_back(Wide(list.Name));
        }
        return names;
    }

    int OptionForCurrent()
    {
        switch (BanList::CurrentMode())
        {
        case BanList::Mode::Off:
            return kOptionOff;
        case BanList::Mode::Custom:
        {
            const auto& saved = BanList::Saved();
            for (size_t i = 0; i < saved.size(); ++i)
                if (saved[i].Name == BanList::CurrentList().Name)
                    return kFirstCustom + static_cast<int>(i);
            return kOptionGame;
        }
        default:
            return kOptionGame;
        }
    }

    void UseOption(int option)
    {
        const auto& saved = BanList::Saved();
        if (option == kOptionOff)
            BanList::UseOff();
        else if (option >= kFirstCustom && option - kFirstCustom < static_cast<int>(saved.size()))
            BanList::UseCustom(saved[option - kFirstCustom]);
        else
            BanList::UseGame();
    }

    char* Slot(char* screen, int slot) { return screen + kSlots + kSlotSize * slot; }
    int& Chosen(char* screen, int slot) { return *reinterpret_cast<int*>(screen + kChosen + 4 * slot); }
    Vector* Rows(char* screen) { return reinterpret_cast<Vector*>(screen + kRows); }

    int CursorSlot(char* screen)
    {
        const Vector* rows = Rows(screen);
        const int64_t cursor = *reinterpret_cast<int64_t*>(screen + kCursor);
        const int64_t count = (rows->End - rows->Begin) / 4;
        return cursor >= 0 && cursor < count ? reinterpret_cast<int*>(rows->Begin)[cursor] : -1;
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
        *reinterpret_cast<int64_t*>(slot + 8) = reinterpret_cast<int64_t>(kDescription);
        Vector* options = reinterpret_cast<Vector*>(slot + 16);
        options->End = options->Begin;
        PushOption(options, reinterpret_cast<int64_t>(kGame));
        PushOption(options, reinterpret_cast<int64_t>(kOff));
        for (const auto& name : CustomNames())
            PushOption(options, reinterpret_cast<int64_t>(name.c_str()));
        Chosen(screen, kSlot) = OptionForCurrent();
    }

    // Hosting (the last row is Create): the ban list row goes in just before it.
    void __fastcall Hook_BuildRows(char* screen)
    {
        orig_BuildRows(screen);
        Vector* rows = Rows(screen);
        const int64_t count = (rows->End - rows->Begin) / 4;
        if (count == 0 || reinterpret_cast<int*>(rows->Begin)[count - 1] != kCreate || LiveMatchType() == kRankedMatch)
            return;
        const int create = kCreate;
        reinterpret_cast<int*>(rows->Begin)[count - 1] = kSlot;
        if (rows->End == rows->Capacity)
            GrowInts(rows, rows->End, &create);
        else
        {
            *reinterpret_cast<int*>(rows->End) = create;
            rows->End += 4;
        }
    }

    void __fastcall Hook_RowsInput(char* screen, int pressed)
    {
        if (*reinterpret_cast<int*>(screen + kMode) == 1 && CursorSlot(screen) == kSlot && (pressed & (kLeft | kRight)))
        {
            orig_ChangeOption(screen, pressed & (kLeft | kRight));
            pressed &= ~(kLeft | kRight);
        }
        orig_RowsInput(screen, pressed);
    }

    // Applies the choice as soon as it changes, so the deck list and the deck check on this screen follow it.
    char __fastcall Hook_ChangeOption(char* screen, int64_t pressed)
    {
        const char result = orig_ChangeOption(screen, pressed);
        if (CursorSlot(screen) == kSlot)
            UseOption(Chosen(screen, kSlot));
        return result;
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
    std::map<char*, char*> g_BanLines;   // SessionInfo widget -> our row
    std::wstring g_LineValue;            // the row's value text (kept while shown)

    char* BanLine(char* info)
    {
        auto found = g_BanLines.find(info);
        if (found != g_BanLines.end())
            return found->second;
        auto* line = static_cast<char*>(_aligned_malloc(kLineSize, 16));
        if (!line)
            return nullptr;
        LineConstruct(nullptr, line, 1);
        YGO::RIX::SharedNode parent = YGO::RIX::ParentRef(reinterpret_cast<YGO::RIX::SharedNode*>(info + 16));   // consumed by CreatePart2
        LineCreate(line, &parent, 7, *reinterpret_cast<void**>(info + 32), 259.0f, 133.0f, 0, 0.0f, 1139736576);   // as SessionInfo makes its rows
        LineSetLabel(line, reinterpret_cast<int64_t>(L"Ban list"));
        g_BanLines[info] = line;
        return line;
    }

    void SetVisible(char* widget, bool visible)
    {
        using SetVisible_t = void(__fastcall*)(char* self, bool visible);
        (*reinterpret_cast<SetVisible_t**>(widget))[3](widget, visible);   // vftable slot 3 = SetVisible
    }

    void __fastcall Hook_SessionInfoShow(char* info, void* settings)
    {
        orig_SessionInfoShow(info, settings);
        const bool show = BanList::CurrentMode() != BanList::Mode::Game;
        if (!show && g_BanLines.find(info) == g_BanLines.end())
            return;
        char* line = BanLine(info);
        if (!line)
            return;
        SetVisible(line, show);
        if (!show)
            return;
        g_LineValue = BanList::CurrentMode() == BanList::Mode::Off ? L"Off" : Wide(BanList::CurrentList().Name);
        LineSetValue(line, g_LineValue.c_str());
        int visible = 0;
        char* rows = *reinterpret_cast<char**>(info + kInfoLines);
        for (size_t i = 0; rows && i < kInfoLineCount; ++i)
            if (void* node = *reinterpret_cast<void**>(rows + i * kLineSize + 16); node && NodeIsVisible(node))
                ++visible;
        YGO::RIX::NodeSetY(*reinterpret_cast<void**>(line + 16), visible * 130.0f + 133.0f);
    }

    // Leaving the screen without hosting: the ban list is back (it is only ever off for a lobby).
    void __fastcall Hook_Back(char* screen)
    {
        BanList::UseGame();
        orig_Back(screen);
    }

    // ---- a joiner is asked before playing the host's list (Yes = play it, No = leave the lobby). The box is the lobby screen's own dialog
    // (screen+432), built like RIX::Screen::ShowYesNo but with a No that does something (YuGiOh-RIX.h Dialog*), on the lobby screen's
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

    void OnAccept()
    {
        if (g_Asked.Mode == BanList::Mode::Off)
            BanList::UseOff();
        else if (g_Asked.Mode == BanList::Mode::Custom)
            BanList::UseCustom(g_Asked.List);
        Logger::WriteLog("Accepted the host's ban list", MODULE_NAME, 0);
    }

    void OnDecline()
    {
        Logger::WriteLog("Declined the host's ban list: leaving the lobby", MODULE_NAME, 0);
        BanList::UseGame();
        if (g_PromptScreen)
            LobbyExit(g_PromptScreen);
    }

    void AskForBanList(char* screen)
    {
        g_Asked = g_Pending;
        g_Pending.Ask = false;
        g_PromptScreen = screen;
        if (g_Asked.Mode == BanList::Mode::Off)
            g_PromptText = L"The host plays with the ban list off: every card can be used at 3 copies.\n\nPlay with it?";
        else
            g_PromptText = L"The host plays with the ban list \"" + Wide(g_Asked.List.Name) + L"\" (" + std::to_wstring(g_Asked.List.Forbidden.size()) +
                L" forbidden, " + std::to_wstring(g_Asked.List.Limited.size()) + L" limited, " + std::to_wstring(g_Asked.List.SemiLimited.size()) +
                L" semi-limited).\n\nPlay with it?";
        char* dialog = screen + 432;
        YGO::RIX::DialogClear(dialog);
        YGO::RIX::DialogSetMode(dialog, 1);
        YGO::RIX::DialogSetText(dialog, g_PromptText.c_str());
        std::function<void()> yes = &OnAccept, no = &OnDecline;   // plain function pointers fit the small buffer; the game takes them over
        YGO::RIX::DialogAddItem(dialog, YGO::RIX::DialogLabelYes, &yes, -1);
        YGO::RIX::DialogAddItem(dialog, YGO::RIX::DialogLabelNo, &no, -1);
        using SetVisible_t = void(__fastcall*)(char* self, bool visible);
        (*reinterpret_cast<SetVisible_t**>(dialog))[3](dialog, true);
        *reinterpret_cast<int*>(screen + 48) = 1;   // the screen sends its input to the dialog
    }

    // ---- the host is told when a player without Yu-Gi-Oh-MP joins ("Vanilla Client"): they can't see this lobby's ban list and play with
    // the game's own, so the host can kick them. A member without the "exmp" mark 4 seconds after we first saw them is reported once.
    uint64_t g_HostLobby = 0;
    std::map<uint64_t, ULONGLONG> g_FirstSeen;
    std::map<uint64_t, bool> g_Reported;
    ULONGLONG g_NextCheck = 0;
    std::wstring g_VanillaText;
    YGO::RIX::EmptyFunction g_NoCallback;

    std::wstring BanListDescription()
    {
        switch (BanList::CurrentMode())
        {
        case BanList::Mode::Off: return L"the ban list off";
        case BanList::Mode::Custom: return L"the ban list \"" + Wide(BanList::CurrentList().Name) + L"\"";
        default: return L"the game's ban list";
        }
    }

    void CheckForVanillaClients(char* screen)
    {
        const ULONGLONG now = GetTickCount64();
        if (!g_HostLobby || now < g_NextCheck || *reinterpret_cast<int*>(screen + 48) != 0)   // +48: a box is already open
            return;
        g_NextCheck = now + 1000;
        const uint64_t self = Steam::Get_SteamId64();
        for (uint64_t member : Steam::LobbyMembers(g_HostLobby))
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
            if (now - seen->second < 4000)
                continue;
            g_Reported[member] = true;
            const std::wstring name = Wide(Steam::PersonaName(member));
            Logger::WriteLog("Vanilla Client: " + Steam::PersonaName(member) + " joined without Yu-Gi-Oh-MP", MODULE_NAME, 1);
            g_VanillaText = L"Vanilla Client: " + name + L" joined without Yu-Gi-Oh-MP.\n\n";
            g_VanillaText += BanList::CurrentMode() == BanList::Mode::Game
                ? L"They play with the game's ban list, like you."
                : L"This lobby plays with " + BanListDescription() + L", but they can't see it and play with the game's own list. Kick them if that matters.";
            g_NoCallback = {};
            YGO::RIX::ShowMessageText(screen, -1, g_VanillaText.c_str(), &g_NoCallback);
            return;   // one box at a time
        }
    }

    void __fastcall Hook_LobbyUpdate(char* screen, void* ui, float seconds)
    {
        orig_LobbyUpdate(screen, ui, seconds);
        if (g_Pending.Ask && !IsLiveHost())
            AskForBanList(screen);
        else if (IsLiveHost())
            CheckForVanillaClients(screen);
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
        Steam::SetLobbyMemberData(lobby, kMemberMark, "1");
        g_HostLobby = lobby;
    }

    void __fastcall Hook_OnLobbyEnter(void* qnet, void* lobbyEnter, char ioFailure)
    {
        orig_OnLobbyEnter(qnet, lobbyEnter, ioFailure);
        if (ioFailure || !lobbyEnter || IsLiveHost())
            return;
        const uint64_t lobby = *static_cast<uint64_t*>(lobbyEnter);
        Steam::SetLobbyMemberData(lobby, kMemberMark, "1");   // tells the host this player runs Yu-Gi-Oh-MP
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
                if (g_Pending.List.Forbidden.empty() && g_Pending.List.Limited.empty() && g_Pending.List.SemiLimited.empty())   // sent by name only
                    for (const auto& saved : BanList::Saved())
                        if (saved.Name == g_Pending.List.Name)
                            g_Pending.List = saved;
                g_Pending.Mode = BanList::Mode::Custom;
                g_Pending.Ask = true;
            }
        }
        Logger::WriteLog("Joined a lobby (ban list: " + (value.empty() ? std::string("the game's") : value) + ")", MODULE_NAME, 0);
    }

    void __fastcall Hook_LeaveLobby(void* qnet, uint64_t lobby)
    {
        orig_LeaveLobby(qnet, lobby);
        g_Pending = {};
        g_HostLobby = 0;
        g_FirstSeen.clear();
        g_Reported.clear();
        BanList::UseGame();
    }
}

namespace LiveSetting
{
    void Attach()
    {
        DetourAttach(&reinterpret_cast<PVOID&>(orig_BuildOptions), Hook_BuildOptions);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_BuildRows), Hook_BuildRows);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_RowsInput), Hook_RowsInput);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_ChangeOption), Hook_ChangeOption);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_Back), Hook_Back);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_PublishHostLobbyData), Hook_PublishHostLobbyData);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_OnLobbyEnter), Hook_OnLobbyEnter);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_LeaveLobby), Hook_LeaveLobby);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_SessionInfoShow), Hook_SessionInfoShow);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_LobbyUpdate), Hook_LobbyUpdate);
    }
}
