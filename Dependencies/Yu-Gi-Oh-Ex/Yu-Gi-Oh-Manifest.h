#pragma once
/*
    Plugin manifests, and working out which plugins can load.

    A plugin may have a <DLL name>.json next to its DLL (Plugins\Yu-Gi-Oh-Cards.json, Plugins\YGO-Ex\Yu-Gi-Oh-Funky.json):

        {
          "title": "Speed Hacks",                    what the plugin list calls it (default: the DLL's name)
          "author": "You",                           who made it (default: nothing)
          "version": "1.0",                          its version (default: nothing)
          "description": "Faster game.",             one short line shown under it (default: nothing)
          "enforced": true,                          it can not be turned off (default: false)
          "settings": [ ... ],                       the Config.ini settings it reads (WolfX's Config editor shows them); see ConfigCatalog.cs
          "requires": [ "Yu-Gi-Oh-Core", "Yu-Gi-Oh-GUI" ],  DLL names (no .dll) that have to be on for this one to load;
                                                     "Plugin/Setting" also needs that plugin's Config.ini setting on, "Plugin/Setting=value"
                                                     set to that value (see "Required settings" below)
          "dlls": [ "Yu-Gi-Oh-FooHelper" ],          more DLLs (same folder) that belong to this plugin: they are not listed on their own,
                                                     and they load with it, just before it
          "content": [ "cards.json", "pages\\" ]       the files in <game>\Yu-Gi-Oh-Ex this plugin applies to the game ("x\\" = a folder);
                                                     WolfX reads it to write Yu-Gi-Oh-Ex\content.json
          "replaces": [ "Yu-Gi-Oh-Music",            older plugins this one took over (renamed, merged in, or retired with this one
                        { "name": "Yu-Gi-Oh-Old",    answering for them), each a name or { "name", "note" }: see "Replaced plugins" below
                          "note": "Now a BOB page." } ],
          "newSave": "forbidden-memories"            this plugin needs a save made with it (true = the DLL's name as the tag): Yu-Gi-Oh-Core
                                                     only loads save slots whose tags are exactly the tags of the loaded plugins
        }

    Replaced plugins: someone who copies a new build over an old folder keeps the old DLLs (Yu-Gi-Oh-Music.dll next to Yu-Gi-Oh-Media.dll).
    A plugin that another installed plugin's manifest "replaces" never loads (Resolve: Problem = "replaced by ..."), and anything that
    asks for it by name - another manifest's "requires", content.json - gets the plugin that replaced it (Current). Yu-Gi-Oh-Core moves
    the old Config.ini switch to the new plugin and tells the player once which leftover files can go; WolfX and ModManager offer to move
    them to Plugins\_retired.

    <game>\Yu-Gi-Oh-Ex\content.json (written by WolfX whenever the content changes) lists the content there and the plugins it needs:

        { "content": [ { "file": "genres.json", "plugins": [ "Yu-Gi-Oh-MoreCards" ] }, ... ] }

    The loader switches those plugins on (and what they require) before it injects anything (ApplyContent below), and says which
    are not installed, so content made on one PC tells another what it needs.

    Required settings: "Yu-Gi-Oh-Core/MasterDuelEngine" in "requires" means Yu-Gi-Oh-Core has to be on AND [Yu-Gi-Oh-Core]
    MasterDuelEngine has to be on (anything but empty, 0, false, off, no); "Yu-Gi-Oh-Core/DuelFormat=speed" needs that exact value
    (case-insensitive). The section is the one the required plugin's manifest "settings" gives the key (default: the plugin's DLL name);
    a key missing from Config.ini counts as that setting's "default". A plugin whose setting is not met is blocked like one whose plugin
    is off ("needs Master Duel engine (Yu-Gi-Oh-Core) on"). Switching the plugin on (Core_SetPluginEnabled) also sets the setting; a mod's
    mod.json "requires" takes the same form, and the loader sets the setting in Config.ini when the mod is on (EnableSettings).

    The loader, Yu-Gi-Oh-Core and Yu-Gi-Oh-RIX all include this file, so they agree on what "can load" means:
      - an enforced plugin is always on;
      - a plugin that is on is only ACTIVE when everything it requires is installed and active, and the settings it requires are on;
      - active plugins load requirements first.
    Header only; needs nlohmann's json.hpp on the include path.
*/
#include <Windows.h>

#include <algorithm>
#include <cctype>
#include <fstream>
#include <functional>
#include <string>
#include <vector>

#include <json.hpp>

namespace YGO
{
    namespace Manifest
    {
        // "Plugin/Setting" or "Plugin/Setting=value" in a "requires" list: a setting of that plugin that has to be on (Value empty) or
        // have that value.
        struct SettingNeed
        {
            std::string Plugin;
            std::string Key;
            std::string Value;
        };

        // One entry of a manifest's "settings" (only what working out requirements needs).
        struct SettingInfo
        {
            std::string Key;
            std::string Section;   // empty = the plugin's DLL name
            std::string Title;
            std::string Default;
        };

        struct Info
        {
            std::string Title;
            std::string Author;
            std::string Version;
            std::string Description;
            bool Enforced = false;
            std::vector<std::string> Requires;   // DLL names without ".dll" (a "Plugin/Setting" entry adds its plugin here too)
            std::vector<SettingNeed> RequiredSettings;   // the "Plugin/Setting[=value]" entries of "requires"
            std::vector<SettingInfo> Settings;   // its "settings"
            std::vector<std::string> Dlls;       // more DLLs that belong to this plugin (names without ".dll")
            std::vector<std::string> Content;    // the Yu-Gi-Oh-Ex files it applies ("cards.json", "pages\\")
            struct Replaced { std::string Name, Note; };
            std::vector<Replaced> Replaces;      // older plugins this one took over (DLL names without ".dll")
            bool NewSave = false;                // needs a save made with it ("newSave")
            std::string SaveTag;                 // the tag that save carries; empty = the DLL's name (see SaveTagOf)
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
            std::string ReplacedBy;    // another installed plugin's manifest "replaces" this one: it never loads (a leftover file)
            std::string ReplacedNote;  // that manifest's note about it
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

        inline std::string Trim(std::string text)
        {
            while (!text.empty() && std::isspace(static_cast<unsigned char>(text.back())))
                text.pop_back();
            size_t start = 0;
            while (start < text.size() && std::isspace(static_cast<unsigned char>(text[start])))
                ++start;
            return text.substr(start);
        }

        // Splits a "requires" entry: "Plugin", "Plugin/Setting" or "Plugin/Setting=value" ("\" works as the separator too). Returns the
        // plugin's DLL name (no .dll); `setting` gets the setting part (Key empty = none).
        inline std::string ParseRequirement(const std::string& entry, SettingNeed& setting)
        {
            setting = {};
            const size_t slash = entry.find_first_of("/\\");
            if (slash == std::string::npos)
                return StripDll(Trim(entry));
            std::string plugin = StripDll(Trim(entry.substr(0, slash)));
            std::string rest = entry.substr(slash + 1);
            const size_t equals = rest.find('=');
            setting.Plugin = plugin;
            setting.Key = Trim(equals == std::string::npos ? rest : rest.substr(0, equals));
            setting.Value = equals == std::string::npos ? std::string() : Trim(rest.substr(equals + 1));
            return plugin;
        }

        // A setting's value counts as on unless it is empty, 0, false, off or no.
        inline bool IsOn(const std::string& value)
        {
            const std::string v = Trim(value);
            return !(v.empty() || v == "0" || SameName(v, "false") || SameName(v, "off") || SameName(v, "no"));
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
            info.Author = text({ "author", "Author" });
            info.Version = text({ "version", "Version" });
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
                        if (!item.is_string())
                            continue;
                        SettingNeed setting;
                        const std::string plugin = ParseRequirement(item.get<std::string>(), setting);
                        if (plugin.empty())
                            continue;
                        if (std::none_of(info.Requires.begin(), info.Requires.end(), [&](const std::string& known) { return SameName(known, plugin); }))
                            info.Requires.push_back(plugin);
                        if (!setting.Key.empty())
                            info.RequiredSettings.push_back(std::move(setting));
                    }
                }
            }
            if (auto it = root.find("settings"); it != root.end() && it->is_array())
            {
                for (const auto& item : *it)
                {
                    if (!item.is_object() || !item.contains("key") || !item["key"].is_string())
                        continue;
                    auto field = [&](const char* key) { return item.contains(key) && item[key].is_string() ? item[key].get<std::string>() : std::string(); };
                    info.Settings.push_back({ field("key"), field("section"), field("title"), field("default") });
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
            for (const char* key : { "replaces", "Replaces" })
            {
                auto it = root.find(key);
                if (it != root.end() && it->is_array())
                {
                    for (const auto& item : *it)
                    {
                        if (item.is_string())
                            info.Replaces.push_back({ StripDll(item.get<std::string>()), {} });
                        else if (item.is_object() && item.contains("name") && item["name"].is_string())
                            info.Replaces.push_back({ StripDll(item["name"].get<std::string>()),
                                                      item.contains("note") && item["note"].is_string() ? item["note"].get<std::string>() : std::string() });
                    }
                }
            }
            for (const char* key : { "newSave", "NewSave" })
            {
                auto it = root.find(key);
                if (it != root.end() && it->is_boolean())
                    info.NewSave = it->get<bool>();
                else if (it != root.end() && it->is_string() && !it->get<std::string>().empty())
                {
                    info.NewSave = true;
                    info.SaveTag = it->get<std::string>();
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

        // The tag a save made with this plugin carries (lower case), or "" when it doesn't need its own save.
        inline std::string SaveTagOf(const Plugin& plugin)
        {
            if (!plugin.Details.NewSave)
                return {};
            std::string tag = plugin.Details.SaveTag.empty() ? plugin.Name : plugin.Details.SaveTag;
            for (char& c : tag)
                c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
            return tag;
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

        // Fills ReplacedBy / ReplacedNote: a plugin some other installed plugin's manifest "replaces" (call before Resolve; Resolve does).
        inline void MarkReplaced(std::vector<Plugin>& plugins)
        {
            for (Plugin& plugin : plugins)
            {
                plugin.ReplacedBy.clear();
                plugin.ReplacedNote.clear();
            }
            for (const Plugin& successor : plugins)
            {
                for (const Info::Replaced& old : successor.Details.Replaces)
                {
                    for (Plugin& plugin : plugins)
                    {
                        if (&plugin != &successor && SameName(plugin.Name, old.Name) && !SameName(plugin.Name, successor.Name))
                        {
                            plugin.ReplacedBy = successor.Name;
                            plugin.ReplacedNote = old.Note;
                        }
                    }
                }
            }
        }

        // The plugin that answers for a name: the plugin itself, unless it is missing or replaced, then the one whose "replaces" names it.
        inline const Plugin* Current(const std::vector<Plugin>& plugins, const std::string& name)
        {
            const Plugin* plugin = Find(plugins, name);
            if (plugin && plugin->ReplacedBy.empty())
                return plugin;
            for (const Plugin& successor : plugins)
            {
                for (const Info::Replaced& old : successor.Details.Replaces)
                {
                    if (SameName(old.Name, name) && successor.ReplacedBy.empty())
                        return &successor;
                }
            }
            return plugin;
        }

        // Reads a Config.ini value: (section, key, fallback) -> value. The default reads the Config.ini next to this process's exe (the game's,
        // in the game; Yu-Gi-Oh-Core's mod settings answer through the same call there). The loader passes its own (its Config.ini path, and
        // the Config.ini of the mods that are on).
        using SettingReader = std::function<std::string(const std::string& section, const std::string& key, const std::string& fallback)>;

        inline std::string ReadIni(const std::string& iniPath, const std::string& section, const std::string& key, const std::string& fallback)
        {
            char buffer[512] = {};
            GetPrivateProfileStringA(section.c_str(), key.c_str(), fallback.c_str(), buffer, sizeof(buffer), iniPath.c_str());
            return buffer;
        }

        inline std::string ProcessConfigPath()
        {
            char path[MAX_PATH] = {};
            GetModuleFileNameA(nullptr, path, MAX_PATH);
            std::string folder = path;
            folder = folder.substr(0, folder.find_last_of("\\/") + 1);
            return folder + "Config.ini";
        }

        inline SettingReader DefaultReader()
        {
            const std::string ini = ProcessConfigPath();
            return [ini](const std::string& section, const std::string& key, const std::string& fallback) { return ReadIni(ini, section, key, fallback); };
        }

        // The required plugin's manifest entry for a setting, if it declares one.
        inline const SettingInfo* FindSetting(const Plugin* plugin, const std::string& key)
        {
            if (!plugin)
                return nullptr;
            for (const SettingInfo& setting : plugin->Details.Settings)
            {
                if (SameName(setting.Key, key))
                    return &setting;
            }
            return nullptr;
        }

        // Where a required setting lives: the section its plugin's manifest gives it, else the plugin's DLL name.
        inline std::string SectionOf(const std::vector<Plugin>& plugins, const SettingNeed& need)
        {
            const Plugin* plugin = Current(plugins, need.Plugin);
            const SettingInfo* setting = FindSetting(plugin, need.Key);
            if (setting && !setting->Section.empty())
                return setting->Section;
            return plugin ? plugin->Name : need.Plugin;
        }

        // The value Config.ini (or the manifest default, when the key is not there) gives a required setting.
        inline std::string SettingValue(const std::vector<Plugin>& plugins, const SettingNeed& need, const SettingReader& reader)
        {
            const SettingInfo* setting = FindSetting(Current(plugins, need.Plugin), need.Key);
            return reader(SectionOf(plugins, need), need.Key, setting ? setting->Default : std::string());
        }

        inline bool SettingMet(const std::vector<Plugin>& plugins, const SettingNeed& need, const SettingReader& reader)
        {
            const std::string value = SettingValue(plugins, need, reader);
            return need.Value.empty() ? IsOn(value) : SameName(Trim(value), need.Value);
        }

        // What switching a required setting on writes: its value, or 1.
        inline std::string WantedValue(const SettingNeed& need)
        {
            return need.Value.empty() ? "1" : need.Value;
        }

        // "Master Duel engine (Yu-Gi-Oh-Core) on" / "DuelFormat (Yu-Gi-Oh-Core) set to speed", for the plugin list and message boxes.
        inline std::string DescribeSetting(const std::vector<Plugin>& plugins, const SettingNeed& need)
        {
            const Plugin* plugin = Current(plugins, need.Plugin);
            const SettingInfo* setting = FindSetting(plugin, need.Key);
            const std::string name = setting && !setting->Title.empty() ? setting->Title : need.Key;
            const std::string owner = plugin && !plugin->Details.Title.empty() ? plugin->Details.Title : need.Plugin;
            return name + " (" + owner + ") " + (need.Value.empty() ? "on" : "set to " + need.Value);
        }

        // Writes every required setting of the given needs that is not met into a Config.ini. Returns "<Section> <Key>=<value>" lines for
        // what was written.
        inline std::vector<std::string> EnableSettings(const std::vector<Plugin>& plugins, const std::vector<SettingNeed>& needs,
                                                       const SettingReader& reader, const std::string& iniPath)
        {
            std::vector<std::string> written;
            for (const SettingNeed& need : needs)
            {
                if (need.Key.empty() || SettingMet(plugins, need, reader))
                    continue;
                const std::string section = SectionOf(plugins, need), value = WantedValue(need);
                WritePrivateProfileStringA(section.c_str(), need.Key.c_str(), value.c_str(), iniPath.c_str());
                written.push_back("[" + section + "] " + need.Key + "=" + value);
            }
            return written;
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
            std::vector<SettingNeed> Settings;   // "Plugin/Setting[=value]" entries (their plugin is in Plugins too)
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
                        if (!plugin.is_string())
                            continue;
                        SettingNeed setting;
                        need.Plugins.push_back(ParseRequirement(plugin.get<std::string>(), setting));
                        if (!setting.Key.empty())
                            need.Settings.push_back(std::move(setting));
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
        // "<file> needs <plugin>" for plugins that are not installed. `settings`, when given, collects the settings the content and the
        // plugins it switched on require (for EnableSettings).
        inline std::vector<std::string> ApplyContent(std::vector<Plugin>& plugins, const std::vector<ContentNeed>& needs,
                                                     std::vector<SettingNeed>* settings = nullptr)
        {
            std::vector<std::string> missing;
            std::vector<std::string> pending;
            for (const ContentNeed& need : needs)
            {
                if (settings)
                    settings->insert(settings->end(), need.Settings.begin(), need.Settings.end());
                for (const std::string& name : need.Plugins)
                {
                    if (const Plugin* current = Current(plugins, name))
                        pending.push_back(current->Name);
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
                    if (settings)
                        settings->insert(settings->end(), plugin.Details.RequiredSettings.begin(), plugin.Details.RequiredSettings.end());
                    for (const std::string& required : plugin.Details.Requires)
                        if (const Plugin* current = Current(plugins, required))
                            pending.push_back(current->Name);
                }
            }
            return missing;
        }

        // Fills Active and Problem from Enabled (and Enforced, which forces a plugin on). `reader` answers the required settings (empty = the
        // Config.ini next to this process's exe).
        inline void Resolve(std::vector<Plugin>& plugins, SettingReader reader = {})
        {
            if (!reader)
                reader = DefaultReader();
            MarkReplaced(plugins);
            for (Plugin& plugin : plugins)
            {
                if (plugin.Details.Enforced)
                    plugin.Enabled = true;
                plugin.Active = plugin.Enabled && plugin.ReplacedBy.empty();
                plugin.Problem.clear();
                if (!plugin.ReplacedBy.empty())
                    plugin.Problem = "replaced by " + plugin.ReplacedBy + (plugin.ReplacedNote.empty() ? std::string() : " (" + plugin.ReplacedNote + ")") +
                                     ": a leftover file, it is not loaded";
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
                        const Plugin* other = Current(plugins, required);
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
                    if (!plugin.Active)
                        continue;
                    for (const SettingNeed& need : plugin.Details.RequiredSettings)
                    {
                        if (SettingMet(plugins, need, reader))
                            continue;
                        plugin.Problem = "needs " + DescribeSetting(plugins, need);
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
                        const Plugin* current = Current(plugins, required);
                        for (size_t j = 0; j < plugins.size(); ++j)
                        {
                            if (current && &plugins[j] == current && plugins[j].Active && !placed[j])
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
