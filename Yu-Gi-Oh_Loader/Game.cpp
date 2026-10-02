#include <fstream>
#include <iostream>
#include <Shlwapi.h>
#include <detours.h>
#include <string>
#include <vector>
#include <sstream>
#include <Windows.h>

#include "Game.h"
#include "Yu-Gi-Oh-Manifest.h"

TCHAR Game::gGamePath[MAX_PATH];
TCHAR Game::gGameLocation[MAX_PATH];

std::vector<std::string> Game::gDlls;
std::vector <std::string> Game::gModsToLoad;
std::vector<LPCSTR> Game::gPlugins;

BOOL Game::Locate()
{
    HKEY hKey;
    char lGamePath[MAX_PATH];
    DWORD dwSize = sizeof(lGamePath);
    if (RegOpenKeyExA(HKEY_LOCAL_MACHINE, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1150640", 0, KEY_READ, &hKey) == ERROR_SUCCESS)
    {
        if (RegQueryValueExA(hKey, "InstallLocation", NULL, NULL, (LPBYTE)lGamePath, &dwSize) == ERROR_SUCCESS)
        {
            strncpy(Game::gGamePath, lGamePath, MAX_PATH);
            strncat(Game::gGameLocation, lGamePath, strlen(lGamePath));
            strncat(Game::gGameLocation, "\\YuGiOh.exe", sizeof("\\YuGiOh.exe"));
        }
        else
            return FALSE;

        RegCloseKey(hKey);
        return TRUE;
    }
    else
        return FALSE;
}

// The game restarts itself (the plugin list asks "restart now?") by running this loader again, so it is told where the loader is.
static void ExportLoaderPath()
{
    char exe[MAX_PATH] = {};
    GetModuleFileNameA(NULL, exe, MAX_PATH);
    char dir[MAX_PATH] = {};
    GetCurrentDirectoryA(MAX_PATH, dir);
    SetEnvironmentVariableA("YGOEX_LOADER", exe);
    SetEnvironmentVariableA("YGOEX_LOADER_DIR", dir);
}

// The loader is not a mod, so it can run the game without Yu-Gi-Oh-Core, but Core is what keeps the game's save away from Steam Cloud (and what the
// other plugins stand on): without it the base game's save can be changed. So it is a strong warning, and the choice is the player's.
static bool CoreIsInjected(const std::vector<std::string>& dlls)
{
    for (const std::string& dll : dlls)
    {
        const size_t slash = dll.find_last_of("\\/");
        if (_stricmp(dll.c_str() + (slash == std::string::npos ? 0 : slash + 1), "Yu-Gi-Oh-Core.dll") == 0)
            return true;
    }
    return MessageBoxA(NULL,
        "Yu-Gi-Oh-Core.dll is not in the Plugins folder.\n\n"
        "Core is what keeps your real Steam save from being changed. Without it the game (and any plugin) can write to your actual save, "
        "and you could lose progress.\n\n"
        "Start the game anyway?", "Yu-Gi-Oh_Loader - Core is missing", MB_ICONWARNING | MB_YESNO | MB_DEFBUTTON2) == IDYES;
}

BOOL Game::Start()
{
    if (Game::Check(gGamePath) == FALSE)
        return FALSE;
    if (!CoreIsInjected(gDlls))
        return FALSE;

    ExportLoaderPath();

    STARTUPINFO info = { sizeof(info) };
    PROCESS_INFORMATION processInfo;

    return DetourCreateProcessWithDllsA(gGameLocation, NULL, NULL, NULL, FALSE, 0, NULL, gGamePath, &info, &processInfo, gDlls.size(), gPlugins.data(), NULL);
}

BOOL Game::Start(LPWSTR CustomPath)
{
    STARTUPINFO info = { sizeof(info) };
    PROCESS_INFORMATION processInfo;

    Set_GamePath(CustomPath);

    Game::LookForPlugins();
    if (!CoreIsInjected(gDlls))
        return FALSE;
    ExportLoaderPath();

    return DetourCreateProcessWithDllsA(gGameLocation, NULL, NULL, NULL, TRUE, NULL, NULL, NULL, &info, &processInfo, gDlls.size(), gPlugins.data(), NULL);
}

// The DLLs in a folder, by name without ".dll".
static std::vector<std::string> DllsIn(const std::string& folder)
{
    std::vector<std::string> names;
    WIN32_FIND_DATAA data;
    HANDLE find = FindFirstFileA((folder + "*.dll").c_str(), &data);
    if (find == INVALID_HANDLE_VALUE)
        return names;
    do
    {
        std::string file = data.cFileName;
        names.push_back(file.substr(0, file.find_last_of('.')));
    } while (FindNextFileA(find, &data) != 0);
    FindClose(find);
    return names;
}

void Game::LookForPlugins()
{
    char current[MAX_PATH];
    GetCurrentDirectoryA(MAX_PATH, current);
    const std::string pluginsFolder = std::string(current) + "\\Plugins\\";
    const std::string guiFolder = pluginsFolder + "YGO-Ex\\";

    // [Yu-Gi-Oh-RIX] in the game's Config.ini is the plugin list, one line per plugin: Name=1 (on) or Name=0 (off). It is what the in-game
    // Plugins menu (Yu-Gi-Oh-RIX) and WolfX edit.
    //   Yu-Gi-Oh-Cards=1        a plugin in Plugins\: injected here when it is on.
    //   YGO-Ex/Yu-Gi-Oh-Funky=0 a plugin in Plugins\YGO-Ex\: not injected, Yu-Gi-Oh-Core starts the ones that are on.
    // A plugin that is not listed yet is added as 0 (everything is off until it is switched on), except the enforced ones. A plugin's
    // <name>.json (see Yu-Gi-Oh-Manifest.h) can make it enforced (always on) and list the plugins it requires; one whose requirements are not
    // met is not loaded.
    CHAR ConfigPath[MAX_PATH];
    strncpy(ConfigPath, gGamePath, MAX_PATH - 1);
    ConfigPath[MAX_PATH - 1] = 0;
    strncat(ConfigPath, "\\Config.ini", MAX_PATH - strlen(ConfigPath) - 1);

    const char* Section = "Yu-Gi-Oh-RIX";
    // 0 or 1 as written, or 2 when the plugin is not listed. An older build kept the list in [Yu-Gi-Oh-Plugins]; that is read as a fallback.
    auto ReadFlag = [&](const std::string& key)
    {
        UINT value = GetPrivateProfileIntA(Section, key.c_str(), 2, ConfigPath);
        return value != 2 ? value : GetPrivateProfileIntA("Yu-Gi-Oh-Plugins", key.c_str(), 2, ConfigPath);
    };

    std::vector<YGO::Manifest::Plugin> plugins;
    for (bool gui : { false, true })
    {
        const std::string& folder = gui ? guiFolder : pluginsFolder;
        for (const std::string& name : DllsIn(folder))
        {
            YGO::Manifest::Plugin plugin;
            plugin.Name = name;
            plugin.Gui = gui;
            plugin.Key = gui ? "YGO-Ex/" + name : name;
            plugin.Details = YGO::Manifest::Read(folder + name + ".json");
            // These two are the base everything else stands on; they are enforced even if their manifest is missing.
            if (!gui && (_stricmp(name.c_str(), "Yu-Gi-Oh-RIX") == 0 || _stricmp(name.c_str(), "Yu-Gi-Oh-Core") == 0))
                plugin.Details.Enforced = true;
            plugin.Enabled = plugin.Details.Enforced || ReadFlag(plugin.Key) == 1;
            plugins.push_back(std::move(plugin));
        }
    }

    YGO::Manifest::ClaimOwned(plugins);

    // Content made with WolfX (Yu-Gi-Oh-Ex\content.json) switches on the plugins it needs, and says which are not installed.
    const std::vector<std::string> missing = YGO::Manifest::ApplyContent(plugins, YGO::Manifest::ReadContent(gGamePath));
    if (!missing.empty())
    {
        std::string text = "The content in the Yu-Gi-Oh-Ex folder needs plugins that are not installed, so it won't show up in the game:\n\n";
        for (const std::string& line : missing)
            text += "  " + line + "\n";
        text += "\nPut those plugins in the Plugins folder (or remove that content).";
        MessageBoxA(nullptr, text.c_str(), "Yu-Gi-Oh-Ex", MB_OK | MB_ICONWARNING);
    }
    YGO::Manifest::Resolve(plugins);

    for (const YGO::Manifest::Plugin& plugin : plugins)
    {
        // The list shows what is on, including the plugins that are enforced; an old value is never lost for one that is only switched off.
        WritePrivateProfileStringA(Section, plugin.Key.c_str(), plugin.Enabled ? "1" : "0", ConfigPath);
        if (plugin.Enabled && !plugin.Active)
            OutputDebugStringA(("Yu-Gi-Oh_Loader: " + plugin.Name + " is not loaded, it " + plugin.Problem + "\n").c_str());
    }

    gDlls.clear();
    gPlugins.clear();
    for (size_t index : YGO::Manifest::LoadOrder(plugins))
    {
        if (plugins[index].Gui)
            continue;
        for (const std::string& owned : plugins[index].Owned)   // the plugin's own extra DLLs load just before it
            gDlls.push_back(pluginsFolder + owned + ".dll");
        gDlls.push_back(pluginsFolder + plugins[index].Name + ".dll");
    }
    for (const std::string& dll : gDlls)
        gPlugins.push_back(dll.c_str());

    // Everything from the old section now lives in [Yu-Gi-Oh-RIX].
    WritePrivateProfileStringA("Yu-Gi-Oh-Plugins", NULL, NULL, ConfigPath);
}

void Game::Set_GamePath(LPWSTR Path)
{
    char charPath[MAX_PATH];
    wcstombs(charPath, Path, MAX_PATH);

    strncpy(Game::gGamePath, charPath, MAX_PATH);
    strncat(Game::gGameLocation, charPath, strlen(charPath));
    strncat(Game::gGameLocation, "\\YuGiOh.exe", sizeof("\\YuGiOh.exe"));

    HKEY hKey;
    if (RegOpenKeyExA(HKEY_LOCAL_MACHINE, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1150640", 0, KEY_WRITE, &hKey) == ERROR_SUCCESS)
    {
        RegSetValueExA(hKey, "InstallLocation", 0, REG_SZ, (LPBYTE)charPath, strlen(charPath));
        RegCloseKey(hKey);
    }
}

void Game::CreateConfig(LPCSTR ConfigName)
{
    CHAR ConfigPath[MAX_PATH];
    strncpy(ConfigPath, gGamePath, MAX_PATH);
    strncat(ConfigPath, "\\", sizeof("\\"));
    strncat(ConfigPath, ConfigName, strlen(ConfigName));

    if (PathFileExistsA(ConfigPath) == FALSE)
    {
        std::ofstream ConfigFile(ConfigPath);

        char CurrentDir[MAX_PATH];
        GetCurrentDirectoryA(MAX_PATH, CurrentDir);
        ConfigFile << "[Yu-Gi-Oh-Core]" << std::endl;
        ConfigFile << "PluginsPath=" << CurrentDir << "\\Plugins\\" << std::endl;
        ConfigFile.close();
    }
}

void Game::CheckForLoadOrder()
{
    char configPath[MAX_PATH] = { 0 };
    strncpy(configPath, Game::gGamePath, MAX_PATH - 1);

    size_t len = strlen(configPath);
    if (len > 0 && configPath[len - 1] != '\\' && configPath[len - 1] != '/') {
        strncat(configPath, "\\", MAX_PATH - len - 1);
    }
    strncat(configPath, "Config.ini", MAX_PATH - strlen(configPath) - 1);

    char buffer[1024] = { 0 };
    DWORD charsRead = GetPrivateProfileStringA("Yu-Gi-Oh-Loader", "LoadOrder", "", buffer, 1024, configPath
    );

    if (charsRead == 0) {
        return;
    }

    std::string loadOrder(buffer);

    std::stringstream stream(loadOrder);
    std::string token;

    while (std::getline(stream, token, ' ')) {
        Game::gModsToLoad.push_back(token);
    }
}

BOOL Game::Check(LPSTR Path)
{
    return PathFileExistsA(Path);
}
