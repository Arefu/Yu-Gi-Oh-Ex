#include "StoryScripts.h"
#include "Common.h"

#include <Windows.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <format>
#include <fstream>
#include <map>
#include <string>
#include <vector>

#include <json.hpp>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    using namespace Campaign;

    // ---- the game (names and layouts in the IDB) ----

    struct ScriptLine             // StoryScriptLine (0x20): all UTF-8
    {
        const char* Speaker;      // character key, "command" or "infn8" (narrator)
        const char* Position;     // LEFT / CENTER / RIGHT / NONE (+ FADEIN / FADEOUT), BG / PROP_ON / PROP_OFF for a command
        const char* Expression;   // expression, or the background / prop picture for a command
        const char* Text;
    };
    static_assert(sizeof(ScriptLine) == 0x20, "StoryScriptLine layout drifted");

    struct ScriptEntry            // StoryScriptEntry (0x10)
    {
        uint32_t FirstLine;
        uint32_t LastLine;        // inclusive
        const char* Name;
    };
    static_assert(sizeof(ScriptEntry) == 0x10, "StoryScriptEntry layout drifted");

    struct ScriptFile             // StoryScriptFile: the header, then ScriptCount entries
    {
        ScriptLine* Lines;
        uint32_t ScriptCount;
        uint32_t LineCount;
        ScriptEntry Scripts[1];
    };

    constexpr uintptr_t kStoryScriptFile = 0x140D4DEF0;    // g_pStoryScriptFile
    ScriptFile*& StoryScriptFile() { return *reinterpret_cast<ScriptFile**>(kStoryScriptFile); }

    using LoadStoryScriptData_t = void(__fastcall*)();
    uintptr_t orig_LoadStoryScriptData = 0x14074A290;     // LoadStoryScriptData

    // The game's CRT heap (it frees the block with ucrtbase's free).
    using Malloc_t = void*(__cdecl*)(size_t);
    using Free_t = void(__cdecl*)(void*);
    Malloc_t CrtMalloc() { return reinterpret_cast<Malloc_t>(GetProcAddress(GetModuleHandleW(L"ucrtbase.dll"), "malloc")); }
    Free_t CrtFree() { return reinterpret_cast<Free_t>(GetProcAddress(GetModuleHandleW(L"ucrtbase.dll"), "free")); }

    // ---- storyscripts.json ----

    struct Line
    {
        std::string Speaker, Position, Expression;
        std::map<char, std::string> Texts;   // UTF-8, as the game keeps them
    };

    struct Script
    {
        std::string Name;
        std::vector<Line> Lines;   // empty = remove the game's scene
    };

    std::vector<Script> g_scripts;

    bool SameName(const char* a, const std::string& b) { return a && _stricmp(a, b.c_str()) == 0; }

    void Load()
    {
        // every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h): later mods win for the same id
        std::vector<std::string> problems;
        auto root = YGO::Mods::ReadMerged("storyscripts.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", it is left out", MODULE_NAME, 2);
        if (root.is_null())
            return; // storyscripts.json is optional
        try
        {
            auto list = root.find("scripts");
            if (list == root.end() || !list->is_array())
                return;
            for (auto& json : *list)
            {
                auto name = json.is_object() ? Str(json, "name") : std::nullopt;
                if (!name || name->empty())
                {
                    Logger::WriteLog("storyscripts.json: a scene without a name was skipped", MODULE_NAME, 2);
                    continue;
                }
                Script script{ *name, {} };
                if (auto lines = json.find("lines"); lines != json.end() && lines->is_array())
                {
                    for (auto& l : *lines)
                    {
                        if (!l.is_object())
                            continue;
                        Line line{ Str(l, "who").value_or(""), Str(l, "position").value_or(""), Str(l, "expression").value_or(""), {} };
                        if (auto text = l.find("text"); text != l.end())
                        {
                            if (text->is_string())
                                line.Texts['E'] = text->get<std::string>();
                            else if (text->is_object())
                                for (auto& [letter, value] : text->items())
                                    if (!letter.empty() && value.is_string())
                                        line.Texts[static_cast<char>(std::toupper(static_cast<unsigned char>(letter[0])))] = value.get<std::string>();
                        }
                        script.Lines.push_back(std::move(line));
                    }
                }
                g_scripts.push_back(std::move(script));
            }
            Logger::WriteLog(std::format("storyscripts.json: {} scene(s)", g_scripts.size()), MODULE_NAME, 0);
        }
        catch (const std::exception& ex)
        {
            g_scripts.clear();
            Logger::WriteLog(std::string("storyscripts.json couldn't be read: ") + ex.what(), MODULE_NAME, 3);
        }
    }

    const std::string& PickText(const Line& line)
    {
        static const std::string empty;
        for (char language : { CurrentLanguage(), 'E' })
            if (auto it = line.Texts.find(language); it != line.Texts.end())
                return it->second;
        return line.Texts.empty() ? empty : line.Texts.begin()->second;
    }

    // A scene in the merged table: either the game's (pointers into its block) or one of ours.
    struct Merged
    {
        const char* Name;
        std::vector<std::array<const char*, 4>> Lines;
    };

    // Runs after every LoadStoryScriptData: replaces the game's block with the merged one.
    void Apply()
    {
        ScriptFile* game = StoryScriptFile();
        if (!game)
            return;
        Malloc_t malloc_ = CrtMalloc();
        Free_t free_ = CrtFree();
        if (!malloc_ || !free_)
        {
            Logger::WriteLog("Story scenes: ucrtbase malloc/free not found, storyscripts.json not applied", MODULE_NAME, 3);
            return;
        }

        std::vector<Merged> merged;
        std::vector<bool> used(g_scripts.size());
        size_t replaced = 0, removed = 0, added = 0;
        auto ours = [&](size_t i)
        {
            Merged m{ g_scripts[i].Name.c_str(), {} };
            for (const Line& line : g_scripts[i].Lines)
                m.Lines.push_back({ line.Speaker.c_str(), line.Position.c_str(), line.Expression.c_str(), PickText(line).c_str() });
            return m;
        };
        for (uint32_t s = 0; s < game->ScriptCount; ++s)
        {
            const ScriptEntry& entry = game->Scripts[s];
            size_t i = 0;
            while (i < g_scripts.size() && !SameName(entry.Name, g_scripts[i].Name))
                ++i;
            if (i < g_scripts.size())
            {
                used[i] = true;
                if (g_scripts[i].Lines.empty())
                    ++removed;
                else
                {
                    merged.push_back(ours(i));
                    ++replaced;
                }
                continue;
            }
            Merged m{ entry.Name, {} };
            for (uint32_t l = entry.FirstLine; l <= entry.LastLine && l < game->LineCount; ++l)
            {
                const ScriptLine& line = game->Lines[l];
                m.Lines.push_back({ line.Speaker, line.Position, line.Expression, line.Text });
            }
            merged.push_back(std::move(m));
        }
        for (size_t i = 0; i < g_scripts.size(); ++i)
            if (!used[i] && !g_scripts[i].Lines.empty())
            {
                merged.push_back(ours(i));
                ++added;
            }

        // Size it: header + entries, then the lines, then every string (copied: the game's block is freed below).
        size_t scriptCount = 0, lineCount = 0, strings = 0;
        for (const Merged& m : merged)
        {
            if (m.Lines.empty())
                continue;   // the game can't store an empty scene (LastLine = FirstLine - 1 underflows for the first)
            ++scriptCount;
            lineCount += m.Lines.size();
            strings += std::strlen(m.Name) + 1;
            for (const auto& line : m.Lines)
                for (const char* text : line)
                    strings += std::strlen(text ? text : "") + 1;
        }
        const size_t headerSize = 16 + scriptCount * sizeof(ScriptEntry);
        const size_t total = headerSize + lineCount * sizeof(ScriptLine) + strings;
        auto* block = static_cast<uint8_t*>(malloc_(total));
        if (!block)
        {
            Logger::WriteLog(std::format("Story scenes: couldn't allocate {} bytes, storyscripts.json not applied", total), MODULE_NAME, 3);
            return;
        }
        auto* file = reinterpret_cast<ScriptFile*>(block);
        auto* lines = reinterpret_cast<ScriptLine*>(block + headerSize);
        char* pool = reinterpret_cast<char*>(lines + lineCount);
        auto copy = [&](const char* text)
        {
            text = text ? text : "";
            const size_t n = std::strlen(text) + 1;
            std::memcpy(pool, text, n);
            const char* at = pool;
            pool += n;
            return at;
        };
        file->Lines = lines;
        file->ScriptCount = static_cast<uint32_t>(scriptCount);
        file->LineCount = static_cast<uint32_t>(lineCount);
        uint32_t s = 0, next = 0;
        for (const Merged& m : merged)
        {
            if (m.Lines.empty())
                continue;
            ScriptEntry& entry = file->Scripts[s++];
            entry.FirstLine = next;
            entry.LastLine = next + static_cast<uint32_t>(m.Lines.size()) - 1;
            entry.Name = copy(m.Name);
            for (const auto& line : m.Lines)
            {
                ScriptLine& out = lines[next++];
                out.Speaker = copy(line[0]);
                out.Position = copy(line[1]);
                out.Expression = copy(line[2]);
                out.Text = copy(line[3]);
            }
        }

        StoryScriptFile() = file;
        free_(game);
        Logger::WriteLog(std::format("Story scenes: {} replaced, {} removed, {} new ({} scenes, {} lines)", replaced, removed, added, scriptCount, lineCount),
            MODULE_NAME, 0);
    }

    void __fastcall Hook_LoadStoryScriptData()
    {
        reinterpret_cast<LoadStoryScriptData_t>(orig_LoadStoryScriptData)();
        Apply();
    }
}

namespace StoryScripts
{
    void Attach()
    {
        Load();
        if (g_scripts.empty())
            return;
        DetourAttach(&(PVOID&)orig_LoadStoryScriptData, Hook_LoadStoryScriptData);
    }

    void ApplyIfLoaded()
    {
        if (!g_scripts.empty() && StoryScriptFile())
            Apply();   // the game loaded its scenes before this plugin was injected
    }
}
