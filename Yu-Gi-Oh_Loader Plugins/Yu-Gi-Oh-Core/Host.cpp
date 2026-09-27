#include "Host.h"

#include <algorithm>
#include <format>
#include <mutex>
#include <set>
#include <string>
#include <vector>

#include "Logger.h"
#include "Save.h"
#include "Yu-Gi-Oh-Manifest.h"

namespace
{
    constexpr const char* kSection = "Yu-Gi-Oh-RIX";   // the plugin list lives in this section of Config.ini (the loader writes it)
    constexpr const char* kGuiPrefix = "YGO-Ex/";

    using YGO::Manifest::Plugin;

    struct Runtime
    {
        Plugin P;
        HMODULE Module = nullptr;
    };

    std::recursive_mutex g_Lock;
    HMODULE g_CoreModule = nullptr;
    std::vector<Runtime> g_List;
    std::set<std::string> g_Started;   // YGO-Ex plugins Core has started (keys, lower case)

    std::string Lower(std::string text)
    {
        for (char& c : text)
            c = static_cast<char>(tolower(static_cast<unsigned char>(c)));
        return text;
    }

    std::string PluginsFolder()
    {
        char path[MAX_PATH] = {};
        GetModuleFileNameA(g_CoreModule, path, MAX_PATH);
        std::string folder = path;
        return folder.substr(0, folder.find_last_of("\\/") + 1);
    }

    std::string IniPath()
    {
        return Save::GameFolder() + "Config.ini";
    }

    std::vector<std::string> DllsIn(const std::string& folder)
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
        std::sort(names.begin(), names.end(), [](const std::string& a, const std::string& b) { return Lower(a) < Lower(b); });
        return names;
    }

    // Runs one of a plugin's entry points; a fault in it is caught here instead of closing the game.
    bool CallGuarded(FARPROC entry)
    {
        __try
        {
            reinterpret_cast<void(__stdcall*)()>(entry)();
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    Runtime* FindByName(const std::string& name)
    {
        for (Runtime& runtime : g_List)
        {
            if (YGO::Manifest::SameName(runtime.P.Name, name))
                return &runtime;
        }
        return nullptr;
    }

    Runtime* FindByKey(const std::string& key)
    {
        for (Runtime& runtime : g_List)
        {
            if (YGO::Manifest::SameName(runtime.P.Key, key))
                return &runtime;
        }
        return nullptr;
    }

    void RefreshLocked()
    {
        const std::string plugins = PluginsFolder();
        const std::string gui = plugins + "YGO-Ex\\";
        const std::string ini = IniPath();

        std::vector<Runtime> fresh;
        for (bool isGui : { false, true })
        {
            const std::string& folder = isGui ? gui : plugins;
            for (const std::string& name : DllsIn(folder))
            {
                Runtime runtime;
                runtime.P.Name = name;
                runtime.P.Gui = isGui;
                runtime.P.Key = isGui ? kGuiPrefix + name : name;
                runtime.P.Details = YGO::Manifest::Read(folder + name + ".json");
                if (!isGui && (YGO::Manifest::SameName(name, "Yu-Gi-Oh-Core") || YGO::Manifest::SameName(name, "Yu-Gi-Oh-RIX")))
                    runtime.P.Details.Enforced = true;
                runtime.P.Enabled = runtime.P.Details.Enforced || GetPrivateProfileIntA(kSection, runtime.P.Key.c_str(), 0, ini.c_str()) == 1;
                runtime.Module = GetModuleHandleA((name + ".dll").c_str());
                fresh.push_back(std::move(runtime));
            }
        }

        std::vector<Plugin> plain;
        for (const Runtime& runtime : fresh)
            plain.push_back(runtime.P);
        YGO::Manifest::ClaimOwned(plain);   // extra DLLs of a plugin have no entry of their own
        YGO::Manifest::Resolve(plain);

        std::vector<Runtime> listed;
        for (Plugin& plugin : plain)
        {
            for (Runtime& runtime : fresh)
            {
                if (YGO::Manifest::SameName(runtime.P.Key, plugin.Key))
                {
                    runtime.P = std::move(plugin);
                    listed.push_back(std::move(runtime));
                    break;
                }
            }
        }
        g_List = std::move(listed);
    }

    // Everything a plugin requires is loaded right now (a YGO-Ex plugin can only start once the plugins it needs are in memory).
    bool RequirementsLoaded(const Runtime& runtime, std::string& why)
    {
        for (const std::string& required : runtime.P.Details.Requires)
        {
            const Runtime* other = FindByName(required);
            if (!other)
            {
                why = "needs " + required + ", which is not installed";
                return false;
            }
            const bool loaded = other->P.Gui ? g_Started.count(Lower(other->P.Key)) != 0 : GetModuleHandleA((other->P.Name + ".dll").c_str()) != nullptr;
            if (!loaded)
            {
                why = "needs " + required + ", which is not loaded (it was switched on after the game started, or it failed)";
                return false;
            }
        }
        return true;
    }

    void Copy(char* destination, size_t size, const std::string& text)
    {
        strncpy_s(destination, size, text.c_str(), _TRUNCATE);
    }

    // Turning one on turns on what it requires; turning one off turns off what requires it. `changed` collects the plugins that flipped.
    void Cascade(Runtime& root, bool enabled, std::set<std::string>& visited, std::vector<Runtime*>& changed)
    {
        if (!visited.insert(Lower(root.P.Key)).second)
            return;

        if (root.P.Enabled != enabled && !(root.P.Details.Enforced && !enabled))
        {
            root.P.Enabled = enabled;
            changed.push_back(&root);
        }

        if (enabled)
        {
            for (const std::string& required : root.P.Details.Requires)
            {
                if (Runtime* other = FindByName(required))
                    Cascade(*other, true, visited, changed);
            }
        }
        else
        {
            for (Runtime& other : g_List)
            {
                for (const std::string& required : other.P.Details.Requires)
                {
                    if (YGO::Manifest::SameName(required, root.P.Name))
                        Cascade(other, false, visited, changed);
                }
            }
        }
    }
}

namespace Host
{
    void SetModule(HMODULE core)
    {
        g_CoreModule = core;
    }

    void Refresh()
    {
        std::lock_guard<std::recursive_mutex> guard(g_Lock);
        RefreshLocked();
    }

    int Count()
    {
        std::lock_guard<std::recursive_mutex> guard(g_Lock);
        if (g_List.empty())
            RefreshLocked();
        return static_cast<int>(g_List.size());
    }

    bool Info(int index, CorePluginInfo& out)
    {
        std::lock_guard<std::recursive_mutex> guard(g_Lock);
        if (g_List.empty())
            RefreshLocked();
        if (index < 0 || static_cast<size_t>(index) >= g_List.size())
            return false;

        const Runtime& runtime = g_List[static_cast<size_t>(index)];
        out = {};
        out.Size = sizeof(out);
        Copy(out.Key, sizeof(out.Key), runtime.P.Key);
        Copy(out.Name, sizeof(out.Name), runtime.P.Name);
        Copy(out.Title, sizeof(out.Title), runtime.P.Details.Title.empty() ? runtime.P.Name : runtime.P.Details.Title);
        Copy(out.Description, sizeof(out.Description), runtime.P.Details.Description);
        Copy(out.Problem, sizeof(out.Problem), runtime.P.Enabled && !runtime.P.Active ? runtime.P.Problem : std::string());
        out.Gui = runtime.P.Gui;
        out.Enabled = runtime.P.Enabled;
        out.Active = runtime.P.Active;
        out.Enforced = runtime.P.Details.Enforced;
        out.Loaded = runtime.Module != nullptr;
        out.Module = runtime.Module;
        return true;
    }

    int SetEnabled(const char* key, bool enabled)
    {
        std::lock_guard<std::recursive_mutex> guard(g_Lock);
        RefreshLocked();

        Runtime* root = key ? FindByKey(key) : nullptr;
        if (!root)
            return 0;

        std::set<std::string> visited;
        std::vector<Runtime*> changed;
        Cascade(*root, enabled, visited, changed);

        const std::string ini = IniPath();
        for (Runtime* runtime : changed)
        {
            WritePrivateProfileStringA(kSection, runtime->P.Key.c_str(), runtime->P.Enabled ? "1" : "0", ini.c_str());
            Logger::WriteLog(std::format("{} switched {}", runtime->P.Name, runtime->P.Enabled ? "on" : "off"), MODULE_NAME, 0);
        }
        const int count = static_cast<int>(changed.size());
        RefreshLocked();
        return count;
    }

    int StartPlugins()
    {
        std::lock_guard<std::recursive_mutex> guard(g_Lock);
        RefreshLocked();

        std::vector<Plugin> plain;
        for (const Runtime& runtime : g_List)
            plain.push_back(runtime.P);

        int started = 0;
        for (size_t index : YGO::Manifest::LoadOrder(plain))
        {
            Runtime& runtime = g_List[index];
            if (!runtime.P.Gui || g_Started.count(Lower(runtime.P.Key)))
                continue;

            std::string why;
            if (!RequirementsLoaded(runtime, why))
            {
                Logger::WriteLog(std::format("{} was not started, it {}", runtime.P.Name, why), MODULE_NAME, 1);
                continue;
            }

            Logger::WriteLog("Loading Plugin: " + runtime.P.Name, MODULE_NAME, 0);
            for (const std::string& owned : runtime.P.Owned)   // its extra DLLs first
                LoadLibraryA((PluginsFolder() + "YGO-Ex\\" + owned + ".dll").c_str());
            HMODULE module = LoadLibraryA((PluginsFolder() + "YGO-Ex\\" + runtime.P.Name + ".dll").c_str());
            if (!module)
            {
                Logger::WriteLog(std::format("Could not load {} (error {})", runtime.P.Name, GetLastError()), MODULE_NAME, 2);
                continue;
            }
            runtime.Module = module;

            bool ok = true;
            if (FARPROC config = GetProcAddress(module, "ProcessConfig"))
                ok = CallGuarded(config);
            if (ok)
            {
                if (FARPROC detours = GetProcAddress(module, "ProcessDetours"))
                    ok = CallGuarded(detours);
            }

            if (!ok)
            {
                Logger::WriteLog(runtime.P.Name + " crashed while starting and was switched off", MODULE_NAME, 2);
                g_Started.insert(Lower(runtime.P.Key));   // it is in memory; it is not started again this run
                WritePrivateProfileStringA(kSection, runtime.P.Key.c_str(), "0", IniPath().c_str());
                continue;
            }

            g_Started.insert(Lower(runtime.P.Key));
            Logger::WriteLog("Started " + runtime.P.Name, MODULE_NAME, 0);
            ++started;
        }
        RefreshLocked();
        return started;
    }
}
