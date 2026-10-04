#pragma once
/*
    Plugin manifests, and working out which plugins can load.

    A plugin may have a <DLL name>.json next to its DLL (Plugins\Yu-Gi-Oh-Cards.json, Plugins\YGO-Ex\Yu-Gi-Oh-Funky.json):

        {
          "title": "Speed Hacks",                    what the plugin list calls it (default: the DLL's name)
          "description": "Faster game.",             one short line shown under it (default: nothing)
          "enforced": true,                          it can not be turned off (default: false)
          "requires": [ "Yu-Gi-Oh-Core", "Yu-Gi-Oh-GUI" ],  DLL names (no .dll) that have to be on for this one to load
          "dlls": [ "Yu-Gi-Oh-FooHelper" ],          more DLLs (same folder) that belong to this plugin: they are not listed on their own,
                                                     and they load with it, just before it
          "content": [ "cards.json", "pages\\" ]       the files in <game>\Yu-Gi-Oh-Ex this plugin applies to the game ("x\\" = a folder);
                                                     WolfX reads it to write Yu-Gi-Oh-Ex\content.json
        }

    <game>\Yu-Gi-Oh-Ex\content.json (written by WolfX whenever the content changes) lists the content there and the plugins it needs:

        { "content": [ { "file": "genres.json", "plugins": [ "Yu-Gi-Oh-MoreCards" ] }, ... ] }

    The loader switches those plugins on (and what they require) before it injects anything (ApplyContent below), and says which
    are not installed, so content made on one PC tells another what it needs.

    The loader, Yu-Gi-Oh-Core and Yu-Gi-Oh-RIX all include this file, so they agree on what "can load" means:
      - an enforced plugin is always on;
      - a plugin that is on is only ACTIVE when everything it requires is installed and active;
      - active plugins load requirements first.
    Header only; needs nlohmann's json.hpp on the include path.
*/
#include <Windows.h>

#include <algorithm>
#include <cctype>
#include <fstream>
#include <string>
#include <vector>

#include <json.hpp>

namespace YGO
{
    namespace Manifest
    {
        struct Info
        {
            std::string Title;
            std::string Description;
            bool Enforced = false;
            std::vector<std::string> Requires;   // DLL names without ".dll"
            std::vector<std::string> Dlls;       // more DLLs that belong to this plugin (names without ".dll")
            std::vector<std::string> Content;    // the Yu-Gi-Oh-Ex files it applies ("cards.json", "pages\\")
            bool Found = false;                  // a manifest file was read
        };

        // What the list and the loader know about one plugin.
        struct Plugin
        {
            std::string Key;      // the line in Config.ini: "Yu-Gi-Oh-Cards", or "YGO-Ex/Yu-Gi-Oh-Funky" for the ones Yu-Gi-Oh-GUI starts
            std::string Name;     // the DLL's name without .dll
            bool Gui = false;     // lives in Plugins\YGO-Ex and is started by Yu-Gi-Oh-Core, not injected by the loader
            Info Details;
            std::vector<std::string> Owned;   // DLLs that belong to it (its manifest's "dlls" that exist in the same folder); they have no entry of their own
            bool Enabled = false; // switched on (enforced plugins always are)
            bool Active = false;  // switched on AND everything it requires is active: this is what loads
            std::string Problem;  // why an enabled plugin is not active
        };

        inline bool SameName(const std::string& a, const std::string& b)
        {
            if (a.size() != b.size())
                return false;
            for (size_t i = 0; i < a.size(); ++i)
            {
                if (std::tolower(static_cast<unsigned char>(a[i])) != std::tolower(static_cast<unsigned char>(b[i])))
                    return false;
            }
            return true;
        }

        inline std::string StripDll(std::string name)
        {
            if (name.size() > 4 && SameName(name.substr(name.size() - 4), ".dll"))
                name.resize(name.size() - 4);
            return name;
        }

        // A missing or unreadable file is just "no manifest": every field has a default.
        inline Info Read(const std::string& jsonPath)
        {
            Info info;
            std::ifstream file(jsonPath);
            if (!file)
                return info;

            nlohmann::json root = nlohmann::json::parse(file, nullptr, false, true);
            if (root.is_discarded() || !root.is_object())
                return info;

            info.Found = true;
            auto text = [&](std::initializer_list<const char*> keys) -> std::string
            {
                for (const char* key : keys)
                {
                    auto it = root.find(key);
                    if (it != root.end() && it->is_string())
                        return it->get<std::string>();
                }
                return {};
            };
            info.Title = text({ "title", "Title", "name" });
            info.Description = text({ "description", "Description", "desc", "Desc" });

            for (const char* key : { "enforced", "Enforced" })
            {
                auto it = root.find(key);
                if (it != root.end() && it->is_boolean())
                    info.Enforced = it->get<bool>();
            }
            for (const char* key : { "requires", "Requires" })
            {
                auto it = root.find(key);
                if (it != root.end() && it->is_array())
                {
                    for (const auto& item : *it)
                    {
                        if (item.is_string())
                            info.Requires.push_back(StripDll(item.get<std::string>()));
                    }
                }
            }
            for (const char* key : { "content", "Content" })
            {
                auto it = root.find(key);
                if (it != root.end() && it->is_array())
                {
                    for (const auto& item : *it)
                    {
                        if (item.is_string())
                            info.Content.push_back(item.get<std::string>());
                    }
                }
            }
            for (const char* key : { "dlls", "Dlls" })
            {
                auto it = root.find(key);
                if (it != root.end() && it->is_array())
                {
                    for (const auto& item : *it)
                    {
                        if (item.is_string())
                            info.Dlls.push_back(StripDll(item.get<std::string>()));
                    }
                }
            }
            return info;
        }

        inline const Plugin* Find(const std::vector<Plugin>& plugins, const std::string& name)
        {
            for (const Plugin& plugin : plugins)
            {
                if (SameName(plugin.Name, name))
                    return &plugin;
            }
            return nullptr;
        }

        // A DLL named in another plugin's "dlls" (same folder) belongs to that plugin: it loses its own entry and goes into the owner's Owned list.
        inline void ClaimOwned(std::vector<Plugin>& plugins)
        {
            std::vector<bool> claimed(plugins.size(), false);
            for (size_t owner = 0; owner < plugins.size(); ++owner)
            {
                for (const std::string& name : plugins[owner].Details.Dlls)
                {
                    if (SameName(name, plugins[owner].Name))
                        continue;
                    for (size_t other = 0; other < plugins.size(); ++other)
                    {
                        if (other != owner && !claimed[other] && plugins[other].Gui == plugins[owner].Gui && SameName(plugins[other].Name, name))
                        {
                            plugins[owner].Owned.push_back(plugins[other].Name);
                            claimed[other] = true;
                        }
                    }
                }
            }
            std::vector<Plugin> kept;
            for (size_t i = 0; i < plugins.size(); ++i)
            {
                if (!claimed[i])
                    kept.push_back(std::move(plugins[i]));
            }
            plugins = std::move(kept);
        }

        // One line of Yu-Gi-Oh-Ex\content.json: a content file and the plugins (DLL names) it needs.
        struct ContentNeed
        {
            std::string File;
            std::vector<std::string> Plugins;
        };

        // A content.json (<game>\Yu-Gi-Oh-Ex\content.json, or a mod's Mods\<id>\Yu-Gi-Oh-Ex\content.json), or nothing when there is none
        // (or it can't be read).
        inline std::vector<ContentNeed> ReadContentFile(const std::wstring& path)
        {
            std::vector<ContentNeed> needs;
            std::ifstream file(path);
            if (!file)
                return needs;
            nlohmann::json root = nlohmann::json::parse(file, nullptr, false, true);
            if (root.is_discarded() || !root.is_object() || !root.contains("content") || !root["content"].is_array())
                return needs;
            for (const auto& entry : root["content"])
            {
                if (!entry.is_object() || !entry.contains("file") || !entry["file"].is_string())
                    continue;
                ContentNeed need;
                need.File = entry["file"].get<std::string>();
                if (entry.contains("plugins") && entry["plugins"].is_array())
                {
                    for (const auto& plugin : entry["plugins"])
                    {
                        if (plugin.is_string())
                            need.Plugins.push_back(StripDll(plugin.get<std::string>()));
                    }
                }
                needs.push_back(std::move(need));
            }
            return needs;
        }

        // <game>\Yu-Gi-Oh-Ex\content.json.
        inline std::vector<ContentNeed> ReadContent(const std::string& gameFolder)
        {
            const std::string path = gameFolder + "\\Yu-Gi-Oh-Ex\\content.json";   // ANSI, as the loader has it
            std::wstring wide(MultiByteToWideChar(CP_ACP, 0, path.c_str(), -1, nullptr, 0), L'\0');
            MultiByteToWideChar(CP_ACP, 0, path.c_str(), -1, wide.data(), static_cast<int>(wide.size()));
            if (!wide.empty())
                wide.pop_back();   // the terminator
            return ReadContentFile(wide);
        }

        // Switches on every plugin the content needs, and everything those require (call before Resolve). Returns the lines
        // "<file> needs <plugin>" for plugins that are not installed.
        inline std::vector<std::string> ApplyContent(std::vector<Plugin>& plugins, const std::vector<ContentNeed>& needs)
        {
            std::vector<std::string> missing;
            std::vector<std::string> pending;
            for (const ContentNeed& need : needs)
            {
                for (const std::string& name : need.Plugins)
                {
                    if (Find(plugins, name))
                        pending.push_back(name);
                    else
                        missing.push_back(need.File + " needs " + name);
                }
            }
            while (!pending.empty())
            {
                std::string name = pending.back();
                pending.pop_back();
                for (Plugin& plugin : plugins)
                {
                    if (!SameName(plugin.Name, name) || plugin.Enabled)
                        continue;
                    plugin.Enabled = true;
                    for (const std::string& required : plugin.Details.Requires)
                        pending.push_back(required);
                }
            }
            return missing;
        }

        // Fills Active and Problem from Enabled (and Enforced, which forces a plugin on).
        inline void Resolve(std::vector<Plugin>& plugins)
        {
            for (Plugin& plugin : plugins)
            {
                if (plugin.Details.Enforced)
                    plugin.Enabled = true;
                plugin.Active = plugin.Enabled;
                plugin.Problem.clear();
            }

            // A plugin drops out when something it requires is missing or not active; that can drop others, so repeat until nothing changes.
            for (bool changed = true; changed;)
            {
                changed = false;
                for (Plugin& plugin : plugins)
                {
                    if (!plugin.Active)
                        continue;
                    for (const std::string& required : plugin.Details.Requires)
                    {
                        const Plugin* other = Find(plugins, required);
                        if (!other)
                            plugin.Problem = "needs " + required + ", which is not installed";
                        else if (!other->Active)
                            plugin.Problem = "needs " + required + ", which is off";
                        else
                            continue;
                        plugin.Active = false;
                        changed = true;
                        break;
                    }
                }
            }
        }

        // The active plugins in the order they should load: everything a plugin requires comes before it; otherwise enforced ones first, then by name.
        inline std::vector<size_t> LoadOrder(const std::vector<Plugin>& plugins)
        {
            std::vector<size_t> pending;
            for (size_t i = 0; i < plugins.size(); ++i)
            {
                if (plugins[i].Active)
                    pending.push_back(i);
            }
            std::sort(pending.begin(), pending.end(), [&](size_t a, size_t b)
            {
                if (plugins[a].Details.Enforced != plugins[b].Details.Enforced)
                    return plugins[a].Details.Enforced;
                std::string x = plugins[a].Name, y = plugins[b].Name;
                for (char& c : x) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
                for (char& c : y) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
                return x < y;
            });

            std::vector<size_t> order;
            std::vector<bool> placed(plugins.size(), false);
            while (!pending.empty())
            {
                bool progress = false;
                for (auto it = pending.begin(); it != pending.end();)
                {
                    bool ready = true;
                    for (const std::string& required : plugins[*it].Details.Requires)
                    {
                        for (size_t j = 0; j < plugins.size(); ++j)
                        {
                            if (SameName(plugins[j].Name, required) && plugins[j].Active && !placed[j])
                                ready = false;
                        }
                    }
                    if (ready)
                    {
                        order.push_back(*it);
                        placed[*it] = true;
                        it = pending.erase(it);
                        progress = true;
                    }
                    else
                        ++it;
                }
                if (!progress)   // a requirement cycle: load the rest as they are
                {
                    for (size_t i : pending)
                        order.push_back(i);
                    break;
                }
            }
            return order;
        }
    }
}
