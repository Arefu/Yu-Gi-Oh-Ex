#include <format>
#include <string>

#include "Yu-Gi-Oh-RIX.h"

#include "Config.h"
#include "Live.h"
#include "Logger.h"
#include "ServerMenu.h"

namespace
{
    uint32_t g_ShownVersion = ~0u;
    Live::State g_ShownState = Live::State::Offline;

    // Labels/descriptions live here so the pointers handed to RIX stay valid while it copies them.
    std::wstring g_ToggleLabel, g_ToggleDescription, g_StatusLabel, g_StatusDescription, g_BanLabel, g_BanDescription;

    void __cdecl OnToggle(int, void*)
    {
        const Live::State state = Live::GetState();
        if (state == Live::State::Connected || state == Live::State::Connecting)
            Live::Disconnect();
        else
            Live::Connect();
    }

    void __cdecl OnStatus(int, void*)
    {
        g_ShownVersion = ~0u;   // force a redraw (the details may have been cut off)
    }

    void BuildButtons(RIX_PageButton (&buttons)[3])
    {
        const Live::State state = Live::GetState();
        const std::string url = Config::Get_ServerUrl();

        g_ToggleLabel = state == Live::State::Connected ? L"Disconnect" : state == Live::State::Connecting ? L"Cancel" : L"Connect";
        g_ToggleDescription = url.empty()
            ? std::wstring(L"No server set: add ServerUrl under [Yu-Gi-Oh-MP] in Config.ini (e.g. http://localhost:5080).")
            : std::format(L"Server: {}", std::wstring(url.begin(), url.end()));

        g_StatusLabel = Live::StatusText();
        g_StatusDescription = Live::DetailText();
        if (g_StatusDescription.empty())
            g_StatusDescription = state == Live::State::Connected ? L"Your duels are sent to the server: LP, cards played, results." : L"Not connected.";

        g_BanLabel = Live::BanListText();
        g_BanDescription = state == Live::State::Connected
            ? L"The server's ban list. It applies while you're connected; Disconnect puts the game's list back."
            : L"Connect to use the server's ban list.";

        buttons[0] = { g_ToggleLabel.c_str(), g_ToggleDescription.c_str(), &OnToggle, nullptr };
        buttons[1] = { g_StatusLabel.c_str(), g_StatusDescription.c_str(), &OnStatus, nullptr };
        buttons[2] = { g_BanLabel.c_str(), g_BanDescription.c_str(), &OnStatus, nullptr };
    }

    void __cdecl OnFrame(void*, int, int, float, void*)
    {
        const uint32_t version = Live::Version();
        const Live::State state = Live::GetState();
        if (version == g_ShownVersion && state == g_ShownState)
            return;
        g_ShownVersion = version;
        g_ShownState = state;

        RIX_PageButton buttons[3];
        BuildButtons(buttons);
        for (int i = 0; i < 3; ++i)
            RIX::Functions().UpdatePageButton(i, &buttons[i]);
    }

    void __cdecl OnServerButton(int, void*)
    {
        RIX_PageDesc page{};
        page.Size = sizeof(page);
        page.Header = L"Online Server";
        page.ButtonCount = 3;
        RIX_PageButton buttons[3];
        BuildButtons(buttons);
        for (int i = 0; i < 3; ++i)
            page.Buttons[i] = buttons[i];
        page.OnFrame = &OnFrame;

        g_ShownVersion = Live::Version();
        g_ShownState = Live::GetState();
        const int opened = RIX::Functions().OpenPage(&page);
        Logger::WriteLog(std::format("Online Server page {}", opened ? "opened" : "FAILED to open"), MODULE_NAME, opened ? 0 : 2);
    }
}

void ServerMenu::Setup()
{
    if (!RIX::Load())
    {
        Logger::WriteLog("Yu-Gi-Oh-RIX not loaded - no Connect to Server button", MODULE_NAME, 1);
        return;
    }
    RIX_ButtonDesc button = RIX::Describe(L"Connect to Server", L"Sign in to a Yu-Gi-Oh-Ex server: its ban list, duel tracking and leaderboards.",
        RIX_PAGE_MULTIPLAYER, &OnServerButton, nullptr, RIX_ITEM_MULTIPLAYER_A);
    const int id = RIX::AddMainMenuButton(&button);
    Logger::WriteLog(std::format("Connect to Server button added (id {})", id), MODULE_NAME, 0);
}
