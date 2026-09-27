#include "PluginMenu.h"

#include <Windows.h>
#include <algorithm>
#include <format>
#include <functional>
#include <map>
#include <string>
#include <vector>

#include "Logger.h"
#include "Yu-Gi-Oh-Core.h"

#include "MainMenu.h"
#include "YuGiOh/YuGiOh-RIX.h"

namespace
{
    constexpr const char* kSection = "Yu-Gi-Oh-RIX";
    constexpr const char* kGuiPrefix = "YGO-Ex/";
    constexpr const char* kPerPageKey = "PluginsPerPage";   // a setting that lives in the same section, not a plugin
    constexpr const char* kConfig = ".\\Config.ini";
    constexpr int kMaxSlots = 5;       // the page (slots, Next, Previous, Back) has to fit on the screen
    constexpr int kDefaultPerPage = 5;
    constexpr int kMinPerPage = 3;     // PluginsPerPage is kMinPerPage..kMaxSlots

    // One row of the list, as Yu-Gi-Oh-Core reports it (Core reads the plugin folders, the manifests and Config.ini, and works out which plugins can load).
    struct Plugin
    {
        std::string Key;          // the line in Config.ini: the DLL's name, or "YGO-Ex/<name>" for the ones Core starts
        bool On = false;
        bool Blocked = false;     // on, but something it requires is off or missing
        std::wstring Title;
        std::wstring Description; // the manifest's description, or why it is blocked
    };

    std::vector<Plugin> g_Plugins;
    std::map<std::string, bool> g_AtStart;   // what was switched on when the game started, i.e. what is actually loaded
    int g_PerPage = kDefaultPerPage;
    int g_Page = 0;
    int g_OptionsButton = -1;
    int g_SlotIds[kMaxSlots];
    int g_PrevId = -1;
    int g_NextId = -1;
    int g_BackId = -1;
    bool g_Open = false;
    bool g_HelpDirty = false;                // the help bar at the bottom needs the Back prompt put on it
    bool g_PluginsStarted = false;           // Core was told to start the YGO-Ex plugins

    std::wstring FromUtf8(const std::string& text)
    {
        int size = MultiByteToWideChar(CP_UTF8, 0, text.c_str(), -1, nullptr, 0);
        if (size <= 1)
            return {};
        std::wstring result(static_cast<size_t>(size - 1), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.c_str(), -1, result.data(), size);
        return result;
    }

    int PageCount()
    {
        const int pages = static_cast<int>((g_Plugins.size() + g_PerPage - 1) / g_PerPage);
        return pages > 1 ? pages : 1;
    }

    void LoadPlugins()
    {
        g_Plugins.clear();
        if (!Core::Load())
            return;

        Core::Functions().Refresh();
        const int count = Core::Functions().GetPluginCount();
        for (int i = 0; i < count; ++i)
        {
            CorePluginInfo info = {};
            info.Size = sizeof(info);
            if (!Core::Functions().GetPluginInfo(i, &info))
                continue;

            // Enforced plugins can never be switched off, so there is nothing to choose here - leave them off
            // the list entirely instead of showing a row nobody can act on.
            if (info.Enforced)
                continue;

            Plugin plugin;
            plugin.Key = info.Key;
            plugin.On = info.Enabled != 0;
            plugin.Blocked = info.Enabled && !info.Active;
            plugin.Title = FromUtf8(info.Title);
            plugin.Description = FromUtf8(info.Description);
            if (plugin.Blocked)
                plugin.Description = L"Not loaded: it " + FromUtf8(info.Problem) + L".";
            g_Plugins.push_back(std::move(plugin));
        }
    }

    void ReadList()
    {
        g_PerPage = std::clamp(static_cast<int>(GetPrivateProfileIntA(kSection, kPerPageKey, kDefaultPerPage, kConfig)), kMinPerPage, kMaxSlots);

        LoadPlugins();
        for (const Plugin& plugin : g_Plugins)
            g_AtStart[plugin.Key] = plugin.On;
        if (g_Plugins.empty())
            Logger::WriteLog("No plugin list: Yu-Gi-Oh-Core is not loaded or found no plugins", MODULE_NAME, 1);
    }

    bool Changed()
    {
        return std::any_of(g_Plugins.begin(), g_Plugins.end(), [](const Plugin& p) { return g_AtStart[p.Key] != p.On; });
    }

    // The text is only pointed at, so the caller keeps it alive until the button is added or updated.
    RIX_ButtonDesc Describe(const wchar_t* label, const wchar_t* description, RIX_ButtonCallback callback, void* user, int skin = RIX_ITEM_DECK_EDITOR, int menu = RIX_MENU_MAIN)
    {
        RIX_ButtonDesc desc{};
        desc.Size = sizeof(desc);
        desc.Label = label;
        desc.Description = description;
        desc.Menu = menu;
        desc.Page = RIX_PAGE_MAIN;
        desc.Skin = skin;
        desc.OnPress = callback;
        desc.User = user;
        return desc;
    }

    void __cdecl OnToggle(int id, void* user);
    void __cdecl OnNext(int id, void* user);
    void __cdecl OnPrevious(int id, void* user);
    void __cdecl OnBack(int id, void* user);

    // Shows the buttons of the current page (open) or takes them all off (closed).
    void Refresh(bool open)
    {
        for (int slot = 0; slot < kMaxSlots; ++slot)
        {
            const size_t index = static_cast<size_t>(g_Page * g_PerPage + slot);
            if (!open || slot >= g_PerPage || index >= g_Plugins.size())
            {
                Menu::Remove(g_SlotIds[slot]);
                continue;
            }

            const Plugin& plugin = g_Plugins[index];
            std::wstring label = plugin.Title + (plugin.Blocked ? L": Blocked" : plugin.On ? L": On" : L": Off");
            const std::wstring& description = plugin.Description;   // empty when the plugin has no manifest
            RIX_ButtonDesc desc = Describe(label.c_str(), description.c_str(), &OnToggle, reinterpret_cast<void*>(static_cast<intptr_t>(slot)));
            Menu::Update(g_SlotIds[slot], desc);
        }

        // Next is above Previous so Enter keeps going forward without the cursor moving. Each only exists where there is a page to go
        // to and shows the page you are on. Back is on the first page; Esc / Backspace goes back from any page.
        if (open && g_Page < PageCount() - 1)
        {
            std::wstring label = std::format(L"Next page ({}/{})", g_Page + 1, PageCount());
            RIX_ButtonDesc desc = Describe(label.c_str(), L"", &OnNext, nullptr);
            Menu::Update(g_NextId, desc);
        }
        else
            Menu::Remove(g_NextId);

        if (open && g_Page > 0)
        {
            std::wstring label = std::format(L"Previous page ({}/{})", g_Page + 1, PageCount());
            RIX_ButtonDesc desc = Describe(label.c_str(), L"", &OnPrevious, nullptr);
            Menu::Update(g_PrevId, desc);
        }
        else
            Menu::Remove(g_PrevId);

        if (open && g_Page == 0)
        {
            RIX_ButtonDesc back = Describe(L"Back", L"", &OnBack, nullptr);
            Menu::Update(g_BackId, back);
        }
        else
            Menu::Remove(g_BackId);

        g_HelpDirty = open;
    }
    // Presses the button in Help & Options: the list itself is a page of the main menu, so the list is set up first (it shows the moment the
    // main menu does, with no flash of the normal menu) and then the screen goes back to the main menu.
    void __cdecl OnOpenFromOptions(int, void*)
    {
        g_Open = true;
        g_Page = 0;
        Menu::SetExclusive(true);
        Refresh(true);
        if (!RIX_GotoScreen(RIX_SCREEN_MAIN_MENU))
        {
            Refresh(false);
            Menu::SetExclusive(false);
            g_Open = false;
        }
    }

    void __cdecl OnBack(int id, void* user);

    // Core starts the YGO-Ex plugins that are on (they work without Yu-Gi-Oh-GUI). Done just before the main menu is first built, so the
    // buttons those plugins add are in it; Tick is the fallback.
    void StartPlugins()
    {
        if (g_PluginsStarted)
            return;
        g_PluginsStarted = true;
        if (Core::Load())
            Core::Functions().StartPlugins();
    }

    // Runs on every main menu frame.
    void Tick()
    {
        StartPlugins();

        if (!g_Open)
            return;

        // The help bar at the bottom lists Back (Esc / Backspace); the menu rebuilds it when the page changes, so it is set after that.
        if (g_HelpDirty)
        {
            g_HelpDirty = false;
            void* screen = Menu::MainScreen();
            if (screen)
            {
                void* help = static_cast<char*>(screen) + 264;
                static const YGO::RIX::HelpEntry back = { 0x2000, 1, 880 };   // the game's own "Back" prompt (cancel button)
                YGO::RIX::HelpClear(help);
                YGO::RIX::HelpAdd(help, &back, 1);
                YGO::RIX::HelpLayout(help);
            }
        }

        // Esc / Backspace (the game's cancel input) leaves the list from any page.
        if (YGO::RIX::InputCancelPressed(YGO::RIX::InputState, 0x2000))
            OnBack(0, nullptr);
    }

    void __cdecl OnToggle(int, void* user)
    {
        const size_t index = static_cast<size_t>(g_Page * g_PerPage + static_cast<int>(reinterpret_cast<intptr_t>(user)));
        if (index >= g_Plugins.size())
            return;

        // Core switches it (and what it needs, or what needs it) in Config.ini.
        const Plugin plugin = g_Plugins[index];
        if (Core::Load())
            Core::Functions().SetPluginEnabled(plugin.Key.c_str(), plugin.On ? 0 : 1);
        LoadPlugins();
        Refresh(true);
    }

    void __cdecl OnNext(int, void*)
    {
        if (g_Page < PageCount() - 1)
            ++g_Page;
        Refresh(true);
    }

    void __cdecl OnPrevious(int, void*)
    {
        if (g_Page > 0)
            --g_Page;
        Refresh(true);
    }

    // The game closes itself if it has not within a few seconds of being asked, so a restart never hangs on something else.
    DWORD WINAPI ForceExit(LPVOID)
    {
        Sleep(4000);
        ExitProcess(0);
    }

    // Runs the loader again (it waits for this process to end first), then closes the game.
    void RestartGame()
    {
        char loader[MAX_PATH] = {};
        char directory[MAX_PATH] = {};
        if (!GetEnvironmentVariableA("YGOEX_LOADER", loader, MAX_PATH) || !GetEnvironmentVariableA("YGOEX_LOADER_DIR", directory, MAX_PATH))
        {
            Logger::WriteLog("Cannot restart: the game was not started by Yu-Gi-Oh_Loader.exe, so close and start it again yourself", MODULE_NAME, 1);
            return;
        }

        std::string command = std::format("\"{}\" --wait {}", loader, GetCurrentProcessId());
        STARTUPINFOA startup = { sizeof(startup) };
        PROCESS_INFORMATION info = {};
        if (!CreateProcessA(loader, command.data(), nullptr, nullptr, FALSE, 0, nullptr, directory, &startup, &info))
        {
            Logger::WriteLog(std::format("Cannot restart: starting the loader failed (error {})", GetLastError()), MODULE_NAME, 2);
            return;
        }
        CloseHandle(info.hProcess);
        CloseHandle(info.hThread);

        Logger::WriteLog("Restarting the game for the plugin changes", MODULE_NAME, 0);
        YGO::RIX::RequestQuit(YGO::RIX::GetGlobalInstance());
        CloseHandle(CreateThread(nullptr, 0, ForceExit, nullptr, 0, nullptr));
    }

    void* g_MainScreen = nullptr;   // the main menu the list was left from

    // Back: to Help & Options, where the list was opened from.
    void GoToOptions()
    {
        if (g_MainScreen)
            YGO::RIX::GotoScreenFrom(g_MainScreen, RIX_SCREEN_HELP_AND_OPTIONS);
    }

    // The main menu is shown again (after Help & Options): the list buttons are taken off just before the normal pages come back.
    void OnReentry()
    {
        Refresh(false);
        g_Open = false;
    }

    void __cdecl OnBack(int, void*)
    {
        g_Open = false;

        g_MainScreen = Menu::CallbackSource();
        if (!g_MainScreen)
            g_MainScreen = Menu::MainScreen();   // Esc / Backspace: not inside a button press
        if (!g_MainScreen)
        {
            Refresh(false);
            Menu::SetExclusive(false);
            return;
        }

        // The list stays up while the screen changes (so the normal main menu never flashes) and is undone when the main menu is next shown.
        Menu::SetExclusive(false, true);
        if (!Changed())
        {
            GoToOptions();
            return;
        }

        // The game's own Yes/No box, put together by hand (see ShowYesNo in YuGiOh-RIX.h) so that No goes on to Help & Options too.
        static const wchar_t* text = L"You have changed which plugins are on. The game has to restart for that to take effect. Restart now?";
        std::function<void()> yes = &RestartGame;
        std::function<void()> no = &GoToOptions;
        void* dialog = static_cast<char*>(g_MainScreen) + 432;
        YGO::RIX::PlayUISound(52);
        YGO::RIX::DialogClear(dialog);
        YGO::RIX::DialogSetMode(dialog, 1);
        YGO::RIX::DialogSetText(dialog, text);
        YGO::RIX::DialogAddItem(dialog, YGO::RIX::DialogLabelYes, &yes, -1);
        YGO::RIX::DialogAddItem(dialog, YGO::RIX::DialogLabelNo, &no, -1);
        (*reinterpret_cast<void(__fastcall***)(void*, char)>(dialog))[3](dialog, 1);   // show
        *(static_cast<char*>(g_MainScreen) + 48) = 1;                                  // the screen now sends its input to the box
    }
}
namespace PluginMenu
{
    int PerPage()
    {
        return g_PerPage;
    }

    void SetPerPage(int count)
    {
        g_PerPage = std::clamp(count, kMinPerPage, kMaxSlots);
        g_Page = 0;
        WritePrivateProfileStringA(kSection, kPerPageKey, std::to_string(g_PerPage).c_str(), kConfig);
    }

    void Install()
    {
        Menu::SetFrameCallback(&Tick);
        Menu::SetBuildCallback(&StartPlugins);
        Menu::SetReentryCallback(&OnReentry);

        ReadList();
        if (g_Plugins.empty())
            return;

        // The buttons must exist before the menu is first built, so they are all added now and switched off; Refresh brings them back.
        RIX_ButtonDesc blank = Describe(L"-", L"", nullptr, nullptr);
        for (int slot = 0; slot < kMaxSlots; ++slot)
        {
            g_SlotIds[slot] = Menu::Add(blank);
            Menu::Pin(g_SlotIds[slot]);
        }
        g_NextId = Menu::Add(blank);
        g_PrevId = Menu::Add(blank);
        g_BackId = Menu::Add(blank);
        Menu::Pin(g_NextId);
        Menu::Pin(g_PrevId);
        Menu::Pin(g_BackId);
        Refresh(false);

        // The entry point is a button in Help & Options.
        RIX_ButtonDesc open = Describe(L"Plugins", L"Choose which plugins load. The game restarts to apply the change.", &OnOpenFromOptions, nullptr, RIX_OPTION_SETTINGS, RIX_MENU_OPTIONS);
        g_OptionsButton = Menu::Add(open);

        Logger::WriteLog(std::format("Plugin list: {} plugin(s), {} per page", g_Plugins.size(), g_PerPage), MODULE_NAME, 0);
    }
}
