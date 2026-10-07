#pragma once
/*
    Mods: content packs that are loaded on top of each other (docs/Mods.md).

    Every mod is a folder <game>\Mods\<id>\ (the Mod Manager unpacks a mod's .zip there):

        mod.json                 who made it and what it needs (below)
        Yu-Gi-Oh-Ex\             new content, the same files WolfX writes to <game>\Yu-Gi-Oh-Ex (cards.json, packs.json, art, music, ...)
        YGO_2020-Ex.toc / .dat   changed game files (WolfX's patch archive, Core Patch.h)
        YGO_2020\                changed game files as loose files (<path inside the archive>, e.g. YGO_2020\bin\pd_limits.bin)
        Plugins\                 plugin DLLs the Mod Manager installs with the mod (not read from here by the game)

    mod.json (every field optional):

        {
          "name": "Yugi-Kaiba Format",                   default: the folder name
          "version": "1.0", "author": "...", "website": "https://...",
          "description": "One line.",
          "details": "Longer text, shown by the Mod Manager.",
          "requires": [ "Yu-Gi-Oh-MoreCards",             plugin DLL names (no .dll) the mod needs ...
                        { "plugin": "Yu-Gi-Oh-Foo", "url": "https://..." } ]   ... with where to get one that isn't part of Yu-Gi-Oh-Ex
        }

    <game>\Mods\modlist.json (written by the Mod Manager) is the load order and what is switched on:

        { "mods": [ { "id": "anime-frames", "enabled": true }, { "id": "yugi-kaiba-format", "enabled": false } ] }

    Later mods win. A mod folder that isn't listed loads after the listed ones (by name) and is on, so copying a folder in works.
    <game>\Yu-Gi-Oh-Ex and <game>\YGO_2020-Ex (WolfX's own workspace) always load last, on top of every mod.

    How content plugins read their files (instead of <game>\Yu-Gi-Oh-Ex\<file>):
      - ReadMerged("packs.json"): every copy merged into one document, lowest priority first: arrays are joined, objects merged key by key,
        anything else is replaced by the later copy. Every object in a top-level array gets "$folder": the content folder it came from
        (UTF-8, ends in a backslash), so relative paths ("image": "art\\x.png") resolve against the right mod.
      - Files("music.json"): every copy, lowest priority first, for readers that read each one with its own folder.
      - Find("pages\\menu.json"): the copy that wins, or an empty path.
    The list is worked out once per process; changing mods needs a restart (like plugins).

    Header only; the loader, Yu-Gi-Oh-Core and the content plugins all include it, so they agree on the order. C++17, nlohmann json.hpp.
*/
#include <Windows.h>

#include <algorithm>
#include <cctype>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

#include <json.hpp>

namespace YGO
{
    namespace Mods
    {
        // A plugin a mod needs, and where to get it when it isn't part of Yu-Gi-Oh-Ex (empty = no link given).
        struct Requirement
        {
            std::string Plugin;
            std::string Url;
        };

        struct Mod
        {
            std::string Id;                  // the folder name in <game>\Mods
            std::filesystem::path Folder;    // <game>\Mods\<id>
            std::string Name, Version, Author, Website, Description, Details;
            std::vector<Requirement> Requires;
            bool Enabled = true;
            bool Listed = false;             // modlist.json names it
        };

        inline std::string Utf8(const std::wstring& text)
        {
            if (text.empty())
                return {};
            const int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
            std::string out(size, '\0');
            WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), size, nullptr, nullptr);
            return out;
        }

        inline std::wstring Wide(const std::string& text)
        {
            if (text.empty())
                return {};
            const int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
            std::wstring out(size, L'\0');
            MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), size);
            return out;
        }

        inline bool SameId(const std::string& a, const std::string& b)
        {
            return a.size() == b.size() && std::equal(a.begin(), a.end(), b.begin(), [](char x, char y)
            {
                return std::tolower(static_cast<unsigned char>(x)) == std::tolower(static_cast<unsigned char>(y));
            });
        }

        // A JSON file, or a discarded value when it is missing or not JSON (comments are allowed).
        inline nlohmann::json ReadJson(const std::filesystem::path& path)
        {
            std::ifstream file(path, std::ios::binary);
            if (!file)
                return nlohmann::json(nlohmann::json::value_t::discarded);
            return nlohmann::json::parse(file, nullptr, false, true);
        }

        inline Mod ReadMod(const std::filesystem::path& folder)
        {
            Mod mod;
            mod.Folder = folder;
            mod.Id = Utf8(folder.filename().wstring());
            mod.Name = mod.Id;
            const nlohmann::json root = ReadJson(folder / L"mod.json");
            if (!root.is_object())
                return mod;
            auto text = [&](const char* key) -> std::string
            {
                auto it = root.find(key);
                return it != root.end() && it->is_string() ? it->get<std::string>() : std::string();
            };
            if (!text("name").empty())
                mod.Name = text("name");
            mod.Version = text("version");
            mod.Author = text("author");
            mod.Website = text("website");
            mod.Description = text("description");
            mod.Details = text("details");
            if (auto it = root.find("requires"); it != root.end() && it->is_array())
            {
                for (const auto& item : *it)
                {
                    Requirement need;
                    if (item.is_string())
                        need.Plugin = item.get<std::string>();
                    else if (item.is_object() && item.contains("plugin") && item["plugin"].is_string())
                    {
                        need.Plugin = item["plugin"].get<std::string>();
                        if (item.contains("url") && item["url"].is_string())
                            need.Url = item["url"].get<std::string>();
                    }
                    if (need.Plugin.size() > 4 && SameId(need.Plugin.substr(need.Plugin.size() - 4), ".dll"))
                        need.Plugin.resize(need.Plugin.size() - 4);
                    if (!need.Plugin.empty())
                        mod.Requires.push_back(std::move(need));
                }
            }
            return mod;
        }

        // True when a folder in Mods is a mod: it has a mod.json. Anything else is left alone: people keep other things there (copies of
        // YGO_2020.toc/.dat, extracted files). Same rule as the Mod Manager (ModLibrary.IsModFolder).
        inline bool IsModFolder(const std::filesystem::path& folder)
        {
            const std::wstring name = folder.filename().wstring();
            if (name.size() >= 11 && _wcsicmp(name.c_str() + name.size() - 11, L".installing") == 0)
                return false;   // the Mod Manager's half-unpacked copy
            std::error_code error;
            return std::filesystem::is_regular_file(folder / L"mod.json", error);
        }

        // Every mod in <game>\Mods, in load order (modlist.json's order, then unlisted folders by name), switched on or off as listed.
        inline std::vector<Mod> Installed(const std::filesystem::path& gameFolder)
        {
            std::vector<Mod> found;
            const std::filesystem::path root = gameFolder / L"Mods";
            std::error_code error;
            for (std::filesystem::directory_iterator it(root, error), end; !error && it != end; it.increment(error))
            {
                if (it->is_directory(error) && IsModFolder(it->path()))
                    found.push_back(ReadMod(it->path()));
            }
            std::sort(found.begin(), found.end(), [](const Mod& a, const Mod& b)
            {
                return _wcsicmp(a.Folder.filename().c_str(), b.Folder.filename().c_str()) < 0;
            });

            std::vector<Mod> ordered;
            const nlohmann::json list = ReadJson(root / L"modlist.json");
            if (list.is_object() && list.contains("mods") && list["mods"].is_array())
            {
                for (const auto& entry : list["mods"])
                {
                    if (!entry.is_object() || !entry.contains("id") || !entry["id"].is_string())
                        continue;
                    const std::string id = entry["id"].get<std::string>();
                    auto it = std::find_if(found.begin(), found.end(), [&](const Mod& mod) { return !mod.Listed && SameId(mod.Id, id); });
                    if (it == found.end())
                        continue;   // listed but removed
                    it->Listed = true;
                    it->Enabled = !entry.contains("enabled") || !entry["enabled"].is_boolean() || entry["enabled"].get<bool>();
                    ordered.push_back(*it);
                }
            }
            for (const Mod& mod : found)
            {
                if (!mod.Listed)
                    ordered.push_back(mod);
            }
            return ordered;
        }

        // The mods that load, lowest priority first.
        inline std::vector<Mod> Active(const std::filesystem::path& gameFolder)
        {
            std::vector<Mod> active;
            for (Mod& mod : Installed(gameFolder))
            {
                if (mod.Enabled)
                    active.push_back(std::move(mod));
            }
            return active;
        }

        // ---- inside the game (the game folder is the folder YuGiOh.exe is in) ----

        inline const std::filesystem::path& GameFolder()
        {
            static const std::filesystem::path folder = []
            {
                wchar_t exe[MAX_PATH]{};
                GetModuleFileNameW(nullptr, exe, MAX_PATH);
                return std::filesystem::path(exe).parent_path();
            }();
            return folder;
        }

        // The content folders, lowest priority first: each active mod's Yu-Gi-Oh-Ex folder that exists, then <game>\Yu-Gi-Oh-Ex (always
        // listed, even before it exists: it is where new files get written).
        inline const std::vector<std::filesystem::path>& ContentFolders()
        {
            static const std::vector<std::filesystem::path> folders = []
            {
                std::vector<std::filesystem::path> list;
                std::error_code error;
                for (const Mod& mod : Active(GameFolder()))
                {
                    const auto content = mod.Folder / L"Yu-Gi-Oh-Ex";
                    if (std::filesystem::is_directory(content, error))
                        list.push_back(content);
                }
                list.push_back(GameFolder() / L"Yu-Gi-Oh-Ex");
                return list;
            }();
            return folders;
        }

        // <game>\Yu-Gi-Oh-Ex: WolfX's workspace and the place for files the game writes (exported decks, ...).
        inline const std::filesystem::path& LocalContentFolder()
        {
            return ContentFolders().back();
        }

        // A content folder as a UTF-8 string ending in a backslash (what "$folder" holds).
        inline std::string FolderText(const std::filesystem::path& folder)
        {
            std::string text = Utf8(folder.wstring());
            if (!text.empty() && text.back() != '\\')
                text += '\\';
            return text;
        }

        // Every copy of a content file (relative to the content folders, e.g. "music.json" or "pages\\menu.json"), lowest priority first.
        inline std::vector<std::filesystem::path> Files(const std::string& relative)
        {
            std::vector<std::filesystem::path> files;
            std::error_code error;
            const std::filesystem::path name(Wide(relative));
            for (const auto& folder : ContentFolders())
            {
                auto path = folder / name;
                if (std::filesystem::is_regular_file(path, error))
                    files.push_back(std::move(path));
            }
            return files;
        }

        // The copy that wins (the last of Files), or an empty path.
        inline std::filesystem::path Find(const std::string& relative)
        {
            auto files = Files(relative);
            return files.empty() ? std::filesystem::path() : files.back();
        }

        // Every file in a content subfolder (e.g. "menus") matching an extension (".json"), lowest priority first; a file name in a later
        // folder replaces the same name in an earlier one (keeping the earlier one's place).
        inline std::vector<std::filesystem::path> FilesIn(const std::string& subfolder, const std::wstring& extension)
        {
            std::vector<std::filesystem::path> files;
            std::error_code error;
            for (const auto& folder : ContentFolders())
            {
                const auto dir = folder / Wide(subfolder);
                for (std::filesystem::directory_iterator it(dir, error), end; !error && it != end; it.increment(error))
                {
                    if (!it->is_regular_file(error) || _wcsicmp(it->path().extension().c_str(), extension.c_str()) != 0)
                        continue;
                    auto same = std::find_if(files.begin(), files.end(), [&](const std::filesystem::path& p)
                    {
                        return _wcsicmp(p.filename().c_str(), it->path().filename().c_str()) == 0;
                    });
                    if (same != files.end())
                        *same = it->path();
                    else
                        files.push_back(it->path());
                }
                error.clear();
            }
            return files;
        }

        namespace Detail
        {
            inline void Tag(nlohmann::json& array, const std::string& folder)
            {
                for (auto& item : array)
                {
                    if (item.is_object() && !item.contains("$folder"))
                        item["$folder"] = folder;
                }
            }

            // Later copies win: arrays are joined, objects merged key by key, anything else replaced.
            inline void Merge(nlohmann::json& into, nlohmann::json&& from)
            {
                if (into.is_object() && from.is_object())
                {
                    for (auto& [key, value] : from.items())
                    {
                        auto it = into.find(key);
                        if (it == into.end())
                            into[key] = std::move(value);
                        else
                            Merge(*it, std::move(value));
                    }
                }
                else if (into.is_array() && from.is_array())
                {
                    for (auto& value : from)
                        into.push_back(std::move(value));
                }
                else
                    into = std::move(from);
            }
        }

        // Every copy of a content file merged into one document (see the top of this file), or null when there is none. arrayKey: a file
        // that may be a bare array (cards.json) is read as { arrayKey: [...] }. Copies that aren't JSON are skipped and named in problems.
        inline nlohmann::json ReadMerged(const std::string& relative, const char* arrayKey = nullptr, std::vector<std::string>* problems = nullptr)
        {
            nlohmann::json merged;   // null
            for (const auto& path : Files(relative))
            {
                nlohmann::json root = ReadJson(path);
                if (root.is_discarded())
                {
                    if (problems)
                        problems->push_back(Utf8(path.wstring()) + " is not valid JSON");
                    continue;
                }
                if (arrayKey && root.is_array())
                    root = nlohmann::json{ { arrayKey, std::move(root) } };
                const std::string folder = FolderText(path.parent_path());
                if (root.is_array())
                    Detail::Tag(root, folder);
                else if (root.is_object())
                {
                    for (auto& [key, value] : root.items())
                    {
                        if (value.is_array())
                            Detail::Tag(value, folder);
                    }
                }
                if (merged.is_null())
                    merged = std::move(root);
                else
                    Detail::Merge(merged, std::move(root));
            }
            return merged;
        }

        // An entry's "$folder" (from ReadMerged), else <game>\Yu-Gi-Oh-Ex\.
        inline std::string FolderOf(const nlohmann::json& entry)
        {
            if (entry.is_object())
            {
                auto it = entry.find("$folder");
                if (it != entry.end() && it->is_string())
                    return it->get<std::string>();
            }
            return FolderText(LocalContentFolder());
        }
    }
}
