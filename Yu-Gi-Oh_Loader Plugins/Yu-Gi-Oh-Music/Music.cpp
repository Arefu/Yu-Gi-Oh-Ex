#define NOMINMAX
#include <Windows.h>
#include <detours.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <charconv>
#include <format>
#include <fstream>
#include <map>
#include <mutex>
#include <optional>
#include <random>
#include <string>
#include <vector>

#include <json.hpp>

#include "Common.h"
#include "Logger.h"
#include "Music.h"
#include "Player.h"
#include "Voice.h"
#include "Yu-Gi-Oh-Mods.h"

namespace Music
{
    namespace
    {
        // The music slots by their Wwise event name (Play_<name>). Slot 13 is a sound effect slot the game plays as music (duel end/results).
        constexpr std::array<const char*, 14> kMusicSlotNames = {
            "duel_1_r", "duel_1_y", "duel_2_r", "duel_2_y", "duel_normal_2_t", "mus_arc_v", "mus_duel_01",
            "mus_duel_03", "mus_title", "mus_tutorial", "mus_vrains", "mus_vs01", "system_deck", "system_result",
        };

        // Keys that stand for several slots: "duel" = every base duel track Duel_StartMusic picks from, "pinch" = the LP pinch pairs.
        const std::map<std::string, std::vector<int>> kGroups = {
            { "duel", { 4, 5, 6, 7, 10 } },
            { "pinch", { 0, 1, 2, 3 } },
        };

        constexpr int kTitleSlot = 8;   // menus: never taken from a duel scope

        struct Choice
        {
            Player::Track Track;        // a file the plugin plays...
            int GameSlot = -1;          // ...or another of the game's own music slots
        };

        using Scope = std::array<std::vector<Choice>, kSlotCount>;   // per slot: the choices (one is picked at random)

        struct Config
        {
            Scope Global;
            std::map<int, Scope> Arenas, Opponents, StoryDuels;
        };

        std::mutex g_ConfigLock;
        Config g_Config;
        std::mt19937 g_Random{ std::random_device{}() };

        // The duel being set up, from the game's three arena setters (the game never clears its own story duel id).
        std::atomic<int> g_StoryDuel{ -1 };
        std::atomic<bool> g_OpponentKnown{ false };

        // A slot re-routed to another game slot: the game believes `requested` plays (g_CurrentMusicSlot), Wwise plays this one.
        int g_RemapSlot = -1;

        PlayMusicSlot_t orig_PlayMusicSlot = reinterpret_cast<PlayMusicSlot_t>(0x14086C300);               // YGO::SOUND::PlayMusicSlot
        ShutdownSoundSlots_t orig_ShutdownSoundSlots = reinterpret_cast<ShutdownSoundSlots_t>(0x14086C0C0); // YGO::SOUND::ShutdownSoundSlots
        SoundManagerSuspend_t orig_Suspend = reinterpret_cast<SoundManagerSuspend_t>(0x14087A3E0);         // YGO::SOUND::SoundManager_Suspend
        SoundManagerResume_t orig_Resume = reinterpret_cast<SoundManagerResume_t>(0x14087A270);            // YGO::SOUND::SoundManager_Resume
        SetVolumeLevel_t orig_SetVolumeLevel = reinterpret_cast<SetVolumeLevel_t>(0x14086BE60);            // YGO::SOUND::SetVolumeLevel
        SetArenaId_t orig_SetCurrentArenaId = reinterpret_cast<SetArenaId_t>(0x140769540);                 // free duel, tutorials
        SetArenaId_t orig_SetArenaIdFromDuel = reinterpret_cast<SetArenaId_t>(0x1407695B0);                // campaign duels (story duel id)
        SetArenaFromMatchType_t orig_SetArenaIdFromMatchType = reinterpret_cast<SetArenaFromMatchType_t>(0x140769560); // battle packs, online...

        std::optional<int> SlotFromName(const std::string& key)
        {
            for (size_t i = 0; i < kMusicSlotNames.size(); ++i)
                if (_stricmp(key.c_str(), kMusicSlotNames[i]) == 0)
                    return static_cast<int>(i);
            int number = 0;
            auto [end, error] = std::from_chars(key.data(), key.data() + key.size(), number);
            if (error == std::errc() && end == key.data() + key.size() && number >= 0 && number < kSlotCount)
                return number;
            return std::nullopt;
        }

        std::optional<int> IntKey(const std::string& key)
        {
            int number = 0;
            auto [end, error] = std::from_chars(key.data(), key.data() + key.size(), number);
            return error == std::errc() && end == key.data() + key.size() ? std::optional<int>(number) : std::nullopt;
        }

        float Float(const nlohmann::json& entry, const char* key, float fallback)
        {
            auto it = entry.find(key);
            return it != entry.end() && it->is_number() ? it->get<float>() : fallback;
        }

        uint32_t Ms(const nlohmann::json& entry, const char* key, uint32_t fallback)
        {
            return static_cast<uint32_t>(std::max(0.0f, Float(entry, key, static_cast<float>(fallback))));
        }

        struct Loader
        {
            std::wstring Folder;
            Player::Track Defaults;
            int Count = 0;

            // A choice is "file.mp3", {"file": ..., options} or {"slot": "mus_vrains"} (one of the game's tracks).
            std::optional<Choice> ReadChoice(const nlohmann::json& entry, const std::string& where)
            {
                Choice choice{ Defaults };
                if (entry.is_string())
                    choice.Track.Path = ResolvePath(Folder, entry.get<std::string>());
                else if (entry.is_object())
                {
                    if (auto slot = entry.find("slot"); slot != entry.end())
                    {
                        auto game = slot->is_string() ? SlotFromName(slot->get<std::string>())
                            : slot->is_number_integer() ? SlotFromName(std::to_string(slot->get<int>())) : std::nullopt;
                        if (!game)
                        {
                            Logger::Log(std::format("music.json: {}: unknown game slot {}", where, slot->dump()), MODULE_NAME, 1);
                            return std::nullopt;
                        }
                        choice.GameSlot = *game;
                        return choice;
                    }
                    auto file = entry.find("file");
                    if (file == entry.end() || !file->is_string())
                    {
                        Logger::Log(std::format("music.json: {} has an entry with no file or slot", where), MODULE_NAME, 1);
                        return std::nullopt;
                    }
                    choice.Track.Path = ResolvePath(Folder, file->get<std::string>());
                    choice.Track.Volume = Float(entry, "volume", choice.Track.Volume);
                    if (auto loop = entry.find("loop"); loop != entry.end() && loop->is_boolean())
                        choice.Track.Loop = loop->get<bool>();
                    choice.Track.LoopStart = Float(entry, "loopStart", 0.0f);
                    choice.Track.LoopEnd = Float(entry, "loopEnd", 0.0f);
                    choice.Track.FadeInMs = Ms(entry, "fadeIn", choice.Track.FadeInMs);
                    choice.Track.FadeOutMs = Ms(entry, "fadeOut", choice.Track.FadeOutMs);
                }
                else
                    return std::nullopt;

                if (GetFileAttributesW(choice.Track.Path.c_str()) == INVALID_FILE_ATTRIBUTES)
                {
                    Logger::Log(std::format("music.json: {} -> {} is missing", where, Narrow(choice.Track.Path)), MODULE_NAME, 1);
                    return std::nullopt;
                }
                return choice;
            }

            // A value is one choice, a list of choices, or {"files": [...], options} (the options apply to each file).
            std::vector<Choice> ReadChoices(const nlohmann::json& value, const std::string& where)
            {
                std::vector<Choice> choices;
                auto add = [&](const nlohmann::json& entry)
                {
                    if (auto choice = ReadChoice(entry, where))
                        choices.push_back(std::move(*choice));
                };
                if (value.is_array())
                    for (auto& entry : value)
                        add(entry);
                else if (value.is_object() && value.contains("files") && value["files"].is_array())
                {
                    nlohmann::json shared = value;
                    shared.erase("files");
                    for (auto& entry : value["files"])
                    {
                        nlohmann::json one = shared;
                        if (entry.is_string())
                            one["file"] = entry;
                        else if (entry.is_object())
                            one.update(entry);
                        add(one);
                    }
                }
                else
                    add(value);
                return choices;
            }

            // {"<slot name | number | duel | pinch>": choices}. A group fills each of its slots; a slot's own key wins over its group.
            Scope ReadScope(const nlohmann::json& object, const std::string& where)
            {
                Scope scope;
                if (!object.is_object())
                    return scope;
                for (int pass = 0; pass < 2; ++pass)
                {
                    for (auto& [key, value] : object.items())
                    {
                        auto group = kGroups.find(key);
                        const bool isGroup = group != kGroups.end();
                        if (isGroup != (pass == 0))
                            continue;
                        std::vector<int> slots;
                        if (isGroup)
                            slots = group->second;
                        else if (auto slot = SlotFromName(key))
                            slots = { *slot };
                        else
                        {
                            Logger::Log(std::format("music.json: {}: unknown slot \"{}\" skipped", where, key), MODULE_NAME, 1);
                            continue;
                        }
                        auto choices = ReadChoices(value, std::format("{} {}", where, key));
                        if (choices.empty())
                            continue;
                        Count += static_cast<int>(choices.size());
                        for (int slot : slots)
                            scope[slot] = choices;
                    }
                }
                return scope;
            }

            void ReadScopes(const nlohmann::json& root, const char* key, std::map<int, Scope>& out)
            {
                auto it = root.find(key);
                if (it == root.end() || !it->is_object())
                    return;
                for (auto& [id, value] : it->items())
                {
                    if (auto number = IntKey(id))
                        out[*number] = ReadScope(value, std::format("{} {}", key, id));
                    else
                        Logger::Log(std::format("music.json: {}: \"{}\" is not a number", key, id), MODULE_NAME, 1);
                }
            }
        };

        // A later music.json (a later mod, or the game folder over the mods) replaces the choices of the slots it sets; the rest stay.
        void Overlay(Scope& into, const Scope& from)
        {
            for (size_t slot = 0; slot < from.size(); ++slot)
            {
                if (!from[slot].empty())
                    into[slot] = from[slot];
            }
        }

        void Overlay(std::map<int, Scope>& into, const std::map<int, Scope>& from)
        {
            for (const auto& [id, scope] : from)
                Overlay(into[id], scope);
        }

        void Load()
        {
            Config config;
            const auto files = YGO::Mods::Files("music.json");   // every mod's and the game folder's, lowest priority first
            if (files.empty())
                Logger::Log("No music.json in Yu-Gi-Oh-Ex or a mod: the game's own music plays", MODULE_NAME, 0);
            for (const auto& path : files)
            {
                std::ifstream file(path);
                // the content folder this copy is in: its "folder" and track names are relative to it
                const std::wstring content = path.parent_path().wstring() + L"\\";
                try
                {
                    auto root = nlohmann::json::parse(file, nullptr, true, true);
                    Config read;
                    Loader loader;
                    loader.Folder = content + L"music\\";
                    if (auto it = root.find("folder"); it != root.end() && it->is_string())
                    {
                        loader.Folder = ResolvePath(content, it->get<std::string>());
                        if (!loader.Folder.empty() && loader.Folder.back() != L'\\')
                            loader.Folder += L'\\';
                    }
                    loader.Defaults.Volume = Float(root, "volume", 1.0f);
                    loader.Defaults.FadeInMs = Ms(root, "fadeIn", 0);
                    loader.Defaults.FadeOutMs = Ms(root, "fadeOut", 0);

                    if (auto it = root.find("slots"); it != root.end())
                        read.Global = loader.ReadScope(*it, "slots");
                    loader.ReadScopes(root, "arenas", read.Arenas);
                    loader.ReadScopes(root, "opponents", read.Opponents);
                    loader.ReadScopes(root, "storyDuels", read.StoryDuels);
                    Logger::Log(std::format("{}: {} track(s); {} arena, {} opponent and {} story duel scope(s)", YGO::Mods::Utf8(path.wstring()),
                        loader.Count, read.Arenas.size(), read.Opponents.size(), read.StoryDuels.size()), MODULE_NAME, 0);
                    Overlay(config.Global, read.Global);
                    Overlay(config.Arenas, read.Arenas);
                    Overlay(config.Opponents, read.Opponents);
                    Overlay(config.StoryDuels, read.StoryDuels);
                }
                catch (const std::exception& e)
                {
                    Logger::Log(std::format("{} is not valid JSON ({}): it is left out", YGO::Mods::Utf8(path.wstring()), e.what()), MODULE_NAME, 2);
                }
            }

            std::lock_guard lock(g_ConfigLock);
            g_Config = std::move(config);
        }

        const std::vector<Choice>* Find(const std::map<int, Scope>& scopes, int id, int slot)
        {
            auto it = scopes.find(id);
            return it != scopes.end() && !it->second[slot].empty() ? &it->second[slot] : nullptr;
        }

        // Most specific first: story duel, opponent, arena, then the global slots. The menus' title slot only uses the global slots.
        std::optional<Choice> Pick(int slot)
        {
            if (slot < 0 || slot >= kSlotCount)
                return std::nullopt;
            std::lock_guard lock(g_ConfigLock);
            const std::vector<Choice>* choices = nullptr;
            if (slot != kTitleSlot)
            {
                if (int duel = g_StoryDuel; duel >= 0)
                    choices = Find(g_Config.StoryDuels, duel, slot);
                if (!choices && g_OpponentKnown)
                    choices = Find(g_Config.Opponents, g_DuelSideCharacters[1], slot);
                if (!choices)
                    choices = Find(g_Config.Arenas, *g_CurrentArenaId, slot);
            }
            if (!choices && !g_Config.Global[slot].empty())
                choices = &g_Config.Global[slot];
            if (!choices)
                return std::nullopt;
            return (*choices)[std::uniform_int_distribution<size_t>(0, choices->size() - 1)(g_Random)];
        }

        void StopWwiseMusic()
        {
            if (g_RemapSlot >= 0)
            {
                if (g_SoundSlots[g_RemapSlot] != 0)
                    Sound_Stop(g_SoundSlots[g_RemapSlot], 0);
                g_RemapSlot = -1;
            }
            const int current = *g_CurrentMusicSlot;
            if (current >= 0 && current < kSlotCount && g_SoundSlots[current] != 0)
                Sound_Stop(g_SoundSlots[current], 0);
        }

        // Same contract as the original: 0 when sound isn't ready or the slot is already current, otherwise switch and return 1.
        // g_CurrentMusicSlot always holds the slot the game asked for (overridden or re-routed), so the LP pinch checks and the same-slot
        // test keep working.
        char __fastcall Hook_PlayMusicSlot(int slot)
        {
            if (!*g_bSoundSlotsReady || slot == *g_CurrentMusicSlot)
                return 0;
            auto choice = Pick(slot);
            if (choice && choice->GameSlot < 0 && Player::Play(choice->Track))
            {
                StopWwiseMusic();
                *g_CurrentMusicSlot = slot;
                return 1;
            }
            Player::Stop();
            if (choice && choice->GameSlot >= 0 && choice->GameSlot != slot && g_SoundSlots[choice->GameSlot] != 0)
            {
                StopWwiseMusic();
                const int64_t sound = g_SoundSlots[choice->GameSlot];
                Sound_SetVolume(sound, *g_MusicVolume);
                Sound_Play(sound);
                g_RemapSlot = choice->GameSlot;
                *g_CurrentMusicSlot = slot;
                return 1;
            }
            if (g_RemapSlot >= 0)
                StopWwiseMusic();
            return orig_PlayMusicSlot(slot);
        }

        // The original re-applies the volume to g_SoundSlots[g_CurrentMusicSlot]; a re-routed slot needs it too.
        void __fastcall Hook_SetVolumeLevel(int channel, int level)
        {
            orig_SetVolumeLevel(channel, level);
            if (channel == 0 && g_RemapSlot >= 0 && g_SoundSlots[g_RemapSlot] != 0)
                Sound_SetVolume(g_SoundSlots[g_RemapSlot], *g_MusicVolume);
        }

        void __fastcall Hook_ShutdownSoundSlots()
        {
            Player::Shutdown();
            g_RemapSlot = -1;
            orig_ShutdownSoundSlots();
        }

        int64_t __fastcall Hook_Suspend(int64_t manager, uint8_t renderAnyway)
        {
            Player::Suspend();
            return orig_Suspend(manager, renderAnyway);
        }

        int64_t __fastcall Hook_Resume(int64_t manager)
        {
            Player::Resume();
            return orig_Resume(manager);
        }

        int64_t __fastcall Hook_SetCurrentArenaId(unsigned int arena)
        {
            g_StoryDuel = -1;
            g_OpponentKnown = true;
            return orig_SetCurrentArenaId(arena);
        }

        int64_t __fastcall Hook_SetArenaIdFromDuel(unsigned int duel)
        {
            g_StoryDuel = static_cast<int>(duel);
            g_OpponentKnown = true;
            return orig_SetArenaIdFromDuel(duel);
        }

        int64_t __fastcall Hook_SetArenaIdFromMatchType()
        {
            g_StoryDuel = -1;
            g_OpponentKnown = false;
            return orig_SetArenaIdFromMatchType();
        }

        // The game writes g_MusicVolume from several places (settings page, sign-in, defaults, reset), so follow the value itself.
        DWORD WINAPI Watcher(LPVOID)
        {
            float applied = -1.0f;
            for (;;)
            {
                const float volume = std::clamp(*g_MusicVolume, 0.0f, 1.0f);
                if (volume != applied)
                {
                    Player::SetMasterVolume(volume);
                    applied = volume;
                }
                Player::SetVoiceVolume(std::clamp(*g_SfxVolume, 0.0f, 1.0f) * Voice::Volume());
                Player::Reap();
                Voice::Tick();
                Sleep(50);
            }
        }
    }

    void Setup()
    {
        Load();
        Voice::Setup();

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_PlayMusicSlot, Hook_PlayMusicSlot);
        DetourAttach(&(PVOID&)orig_ShutdownSoundSlots, Hook_ShutdownSoundSlots);
        DetourAttach(&(PVOID&)orig_Suspend, Hook_Suspend);
        DetourAttach(&(PVOID&)orig_Resume, Hook_Resume);
        DetourAttach(&(PVOID&)orig_SetVolumeLevel, Hook_SetVolumeLevel);
        DetourAttach(&(PVOID&)orig_SetCurrentArenaId, Hook_SetCurrentArenaId);
        DetourAttach(&(PVOID&)orig_SetArenaIdFromDuel, Hook_SetArenaIdFromDuel);
        DetourAttach(&(PVOID&)orig_SetArenaIdFromMatchType, Hook_SetArenaIdFromMatchType);
        const LONG error = DetourTransactionCommit();
        Logger::Log(std::format("Music hooks {}", error == NO_ERROR ? "attached" : std::format("FAILED (Detours error {})", error)),
            MODULE_NAME, error == NO_ERROR ? 0 : 2);

        if (HANDLE thread = CreateThread(nullptr, 0, Watcher, nullptr, 0, nullptr))
            CloseHandle(thread);
    }

    void Reload()
    {
        Load();
        Voice::Reload();
    }
}
