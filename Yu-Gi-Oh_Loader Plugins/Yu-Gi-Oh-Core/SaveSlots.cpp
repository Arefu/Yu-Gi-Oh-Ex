#include <Windows.h>
#include <algorithm>
#include <cstring>
#include <format>
#include <fstream>
#include <mutex>
#include <vector>

#include <json.hpp>

#include "Host.h"
#include "Logger.h"
#include "Save.h"
#include "SaveSlots.h"
#include "Yu-Gi-Oh-Mods.h"
#include "YuGiOh/YuGiOh-Addresses.h"

using json = nlohmann::json;

namespace
{
    constexpr int kDefaultSlots = 3;
    constexpr const char* kIniSection = "Yu-Gi-Oh-Core";

    // Portraits the slots get when saves.json does not name one (sprites in pdui/chars, without "_neutral").
    constexpr const char* kDefaultAvatars[] = { "yugimuto", "jadenyuki", "yuseifudo", "yumatsukumo", "yuya" };
    const char* DefaultAvatar(int index) { return kDefaultAvatars[index % std::size(kDefaultAvatars)]; }

    // The save file (see the savegame library): magic at 0, stats at 0x24 (100 x u64), player section at 0xFD8 (wallet u64 at +0x10),
    // card table at 0x5DC8 (20000 bytes, copies in bits 0-2).
    constexpr size_t kSaveSize = 44008;
    constexpr uint32_t kSaveMagic = 0x54CE29F9;
    constexpr size_t kStats = 0x24;
    constexpr size_t kWallet = 0xFD8 + 0x10;
    constexpr size_t kCardTable = 0x5DC8;
    constexpr size_t kCardTableSize = 20000;
    // SaveStat: games 0 campaign, 3 challenge, 4 multiplayer, 9 battle pack; wins 13, 16, 17, 22 (the same four).
    constexpr int kDuelStats[] = { 0, 3, 4, 9 };
    constexpr int kWinStats[] = { 13, 16, 17, 22 };

    // The game's character table (chardata): 240 records of 104 bytes, +0x20 = the ASCII key the portrait is named after.
    constexpr uintptr_t kCharacterRecords = YGO::Addr::g_CharacterRecords;
    constexpr int kCharacterCount = 240;

    struct Slot
    {
        std::wstring Name;
        std::string Avatar;   // sprite key
        std::string File;     // the save file's name, next to the GameSaveName one
        std::vector<Host::SaveTag> Tags;   // made with these plugins' "newSave" tags, sorted
    };

    std::vector<Host::SaveTag> g_RunTags;   // the loaded plugins' save tags (Host::SaveTags at Install: what loads this run)

    json TagsJson(const std::vector<Host::SaveTag>& tags)
    {
        json list = json::array();
        for (const Host::SaveTag& tag : tags)
            list.push_back({ { "tag", tag.Tag }, { "title", tag.Title } });
        return list;
    }

    std::vector<Host::SaveTag> TagsFrom(const json& entry)
    {
        std::vector<Host::SaveTag> tags;
        if (!entry.contains("tags") || !entry["tags"].is_array())
            return tags;
        for (const json& item : entry["tags"])
        {
            Host::SaveTag tag;
            if (item.is_string())
                tag.Tag = item.get<std::string>();
            else if (item.is_object() && item.contains("tag") && item["tag"].is_string())
            {
                tag.Tag = item["tag"].get<std::string>();
                if (item.contains("title") && item["title"].is_string())
                    tag.Title = item["title"].get<std::string>();
            }
            for (char& c : tag.Tag)
                c = static_cast<char>(tolower(static_cast<unsigned char>(c)));
            if (tag.Title.empty())
                tag.Title = tag.Tag;
            if (!tag.Tag.empty() && std::none_of(tags.begin(), tags.end(), [&](const Host::SaveTag& known) { return known.Tag == tag.Tag; }))
                tags.push_back(std::move(tag));
        }
        std::sort(tags.begin(), tags.end(), [](const Host::SaveTag& a, const Host::SaveTag& b) { return a.Tag < b.Tag; });
        return tags;
    }

    bool HasTag(const std::vector<Host::SaveTag>& tags, const std::string& tag)
    {
        return std::any_of(tags.begin(), tags.end(), [&](const Host::SaveTag& known) { return known.Tag == tag; });
    }

    // The name slot `index` (1-based) had before slots carried their own file: the GameSaveName file, or "-N" before its extension.
    std::string NumberedFile(int number)
    {
        const std::filesystem::path& primary = Save::PrimarySavePath();
        if (number <= 1)
            return primary.filename().string();
        return primary.stem().string() + std::format("-{}", number) + primary.extension().string();
    }

    std::mutex g_Lock;
    json g_File = json::object();   // saves.json as read, so unknown fields survive a rewrite
    std::vector<Slot> g_Slots;
    int g_LastSlot = 1;
    int g_ForcedSlot = 0;           // SaveSlot in Config.ini

    std::filesystem::path JsonPath()
    {
        return std::filesystem::path(Save::GameFolder()) / "Yu-Gi-Oh-Ex" / "saves.json";
    }

    std::wstring Widen(const std::string& text)
    {
        if (text.empty())
            return {};
        int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring wide(size, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), size);
        return wide;
    }

    // A character id -> the key its portrait is named after (empty when the id has no record yet).
    std::string CharacterKey(int id)
    {
        if (id <= 0 || id >= kCharacterCount)
            return {};
        const char* key = *reinterpret_cast<const char* const*>(kCharacterRecords + 104ull * id + 0x20);
        return key && key[0] ? key : std::string();
    }

    void WriteJson()
    {
        g_File["lastSlot"] = g_LastSlot;

        std::error_code error;
        std::filesystem::create_directories(JsonPath().parent_path(), error);
        std::ofstream file(JsonPath(), std::ios::binary | std::ios::trunc);
        if (file)
            file << g_File.dump(2);
        else
            Logger::WriteLog(std::format("Could not write {}", JsonPath().string()), MODULE_NAME, 2);
    }

    void ReadJson()
    {
        g_Slots.clear();

        std::ifstream file(JsonPath(), std::ios::binary);
        if (file)
        {
            json parsed = json::parse(file, nullptr, false);
            if (parsed.is_object())
                g_File = std::move(parsed);
            else
                Logger::WriteLog(std::format("{} is not valid JSON, using the default save slots", JsonPath().string()), MODULE_NAME, 1);
        }

        json* slots = g_File.contains("slots") && g_File["slots"].is_array() ? &g_File["slots"] : nullptr;
        const int count = slots ? std::clamp(static_cast<int>(slots->size()), 1, SaveSlots::MaxSlots) : kDefaultSlots;

        for (int i = 0; i < count; ++i)
        {
            Slot slot;
            slot.Name = std::format(L"Duelist {}", i + 1);
            slot.Avatar = DefaultAvatar(i);
            slot.File = NumberedFile(i + 1);

            if (slots && (*slots)[i].is_object())
            {
                json& entry = (*slots)[i];
                if (entry.contains("file") && entry["file"].is_string() && !entry["file"].get<std::string>().empty())
                    slot.File = std::filesystem::path(entry["file"].get<std::string>()).filename().string();   // a name, never a path
                else
                    entry["file"] = slot.File;   // pinned now, so a later delete cannot move another slot onto it
                if (entry.contains("name") && entry["name"].is_string() && !entry["name"].get<std::string>().empty())
                    slot.Name = Widen(entry["name"].get<std::string>());
                if (entry.contains("avatar") && entry["avatar"].is_string() && !entry["avatar"].get<std::string>().empty())
                    slot.Avatar = entry["avatar"].get<std::string>();
                else if (entry.contains("avatar") && entry["avatar"].is_number_integer())
                    slot.Avatar = "#" + std::to_string(entry["avatar"].get<int>());   // a character id, resolved when shown (chardata loads later)
                slot.Tags = TagsFrom(entry);
            }
            g_Slots.push_back(std::move(slot));
        }
        g_File.erase("nextFile");   // legacy counter: Add() now reuses the lowest free file number

        if (!slots)
        {
            // The defaults go into the file the first time a slot is picked, so there is one to edit.
            json list = json::array();
            for (int i = 0; i < count; ++i)
                list.push_back({ { "name", std::format("Duelist {}", i + 1) }, { "avatar", DefaultAvatar(i) }, { "file", g_Slots[i].File } });
            g_File["slots"] = std::move(list);
        }
        else if (static_cast<int>(slots->size()) > count)
            g_File["slots"].erase(g_File["slots"].begin() + count, g_File["slots"].end());

        const bool hasLast = g_File.contains("lastSlot") && g_File["lastSlot"].is_number_integer();
        g_LastSlot = std::clamp(hasLast ? g_File["lastSlot"].get<int>() : 1, 1, count);
    }

    std::wstring FileDate(const std::filesystem::path& path)
    {
        WIN32_FILE_ATTRIBUTE_DATA data{};
        FILETIME local{};
        SYSTEMTIME time{};
        if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data) || !FileTimeToLocalFileTime(&data.ftLastWriteTime, &local) ||
            !FileTimeToSystemTime(&local, &time))
            return {};
        return std::format(L"{:04}-{:02}-{:02} {:02}:{:02}", time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute);
    }
}

void SaveSlots::Install()
{
    std::lock_guard<std::mutex> guard(g_Lock);
    ReadJson();
    g_RunTags = Host::SaveTags();
    for (const Host::SaveTag& tag : g_RunTags)
        Logger::WriteLog(std::format("{} needs a save made with it (save tag \"{}\")", tag.Title, tag.Tag), MODULE_NAME, 0);

    const std::string ini = Save::GameFolder() + "Config.ini";
    g_ForcedSlot = static_cast<int>(GetPrivateProfileIntA(kIniSection, "SaveSlot", 0, ini.c_str()));
    if (g_ForcedSlot < 0 || g_ForcedSlot > Count())
        g_ForcedSlot = 0;
    if (g_ForcedSlot && !CanPlay(g_ForcedSlot))
    {
        Logger::WriteLog(std::format("SaveSlot={} was made with other plugins' save tags: showing the save-select screen", g_ForcedSlot), MODULE_NAME, 1);
        g_ForcedSlot = 0;
    }

    // Start on the slot that will be played: the forced one, else the last one played. A last slot whose file is gone falls back to slot 1,
    // so the title screen's early read does not make a new profile file in it.
    int start = g_ForcedSlot ? g_ForcedSlot : g_LastSlot;
    std::error_code error;
    if (!g_ForcedSlot && start != 1 && !std::filesystem::exists(PathOf(start), error))
        start = 1;

    Save::SetSavePath(PathOf(start));
    Logger::WriteLog(std::format("{} save slot(s), starting on slot {}{}", Count(), start,
                                 g_ForcedSlot ? " (SaveSlot in Config.ini, no save-select screen)" : ""), MODULE_NAME, 0);
}

int SaveSlots::Count()
{
    return static_cast<int>(g_Slots.size());
}

int SaveSlots::LastSlot()
{
    return g_LastSlot;
}

bool SaveSlots::ShouldAsk()
{
    return g_ForcedSlot == 0;   // also with one slot: the screen is where slots are added
}

bool SaveSlots::IsModded()
{
    if (!g_RunTags.empty())   // a loaded plugin declared a "newSave" tag (e.g. Forbidden Memories)
        return true;
    return !YGO::Mods::Active(std::filesystem::path(Save::GameFolder())).empty();
}

std::filesystem::path SaveSlots::PathOf(int slot)
{
    const std::filesystem::path& primary = Save::PrimarySavePath();
    if (slot < 1 || slot > Count())
        return primary;
    return primary.parent_path() / g_Slots[slot - 1].File;
}

std::wstring SaveSlots::NameOf(int slot)
{
    return slot >= 1 && slot <= Count() ? g_Slots[slot - 1].Name : std::wstring();
}

std::string SaveSlots::AvatarSpriteOf(int slot)
{
    if (slot < 1 || slot > Count())
        return {};
    std::string key = g_Slots[slot - 1].Avatar;
    if (!key.empty() && key[0] == '#')
    {
        key = CharacterKey(std::atoi(key.c_str() + 1));
        if (key.empty())
            key = DefaultAvatar(slot - 1);
    }
    return key + "_neutral";
}

SaveSlots::Summary SaveSlots::Read(int slot)
{
    Summary summary;
    const std::filesystem::path path = PathOf(slot);

    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file)
        return summary;
    summary.Exists = true;
    summary.LastPlayed = FileDate(path);

    const size_t fileSize = static_cast<size_t>(file.tellg());
    if (fileSize < kSaveSize)   // more = plugin data after the blob (Core_GetSaveExtension)
        return summary;
    std::vector<unsigned char> data(fileSize);
    file.seekg(0);
    if (!file.read(reinterpret_cast<char*>(data.data()), static_cast<std::streamsize>(fileSize)))
        return summary;

    auto u32 = [&](size_t offset) { uint32_t value; std::memcpy(&value, &data[offset], sizeof(value)); return value; };
    auto u64 = [&](size_t offset) { uint64_t value; std::memcpy(&value, &data[offset], sizeof(value)); return value; };
    if (u32(0) != kSaveMagic)
        return summary;

    summary.Valid = true;
    summary.Wallet = u64(kWallet);
    for (int stat : kDuelStats)
        summary.Duels += u64(kStats + 8ull * stat);
    for (int stat : kWinStats)
        summary.Wins += u64(kStats + 8ull * stat);
    for (size_t id = 0; id < kCardTableSize; ++id)
        if (data[kCardTable + id] & 7)
            ++summary.CardsOwned;

    // Cards past id 19999 are in the save extension (CardTable.cpp: "YGEX", version, count, then 32-byte tag + u32 size + data;
    // "cards.high" = u16 id + u8 byte each).
    size_t at = kSaveSize;
    if (data.size() >= at + 12 && std::memcmp(&data[at], "YGEX", 4) == 0 && u32(at + 4) == 1)
    {
        const uint32_t count = u32(at + 8);
        at += 12;
        for (uint32_t i = 0; i < count && data.size() - at >= 36; ++i)
        {
            const std::string tag(reinterpret_cast<const char*>(&data[at]), strnlen(reinterpret_cast<const char*>(&data[at]), 32));
            const uint32_t size = u32(at + 32);
            at += 36;
            if (data.size() - at < size)
                break;
            if (tag == "cards.high")
                for (size_t e = 0; e + 3 <= size; e += 3)
                    if (data[at + e + 2] & 7)
                        ++summary.CardsOwned;
            at += size;
        }
    }
    return summary;
}

bool SaveSlots::CanPlay(int slot)
{
    if (slot < 1 || slot > Count())
        return false;
    std::error_code error;
    if (!std::filesystem::exists(PathOf(slot), error))
        return true;   // empty: a new profile starts in it and takes the tags
    const std::vector<Host::SaveTag>& tags = g_Slots[slot - 1].Tags;
    return tags.size() == g_RunTags.size() &&
           std::equal(tags.begin(), tags.end(), g_RunTags.begin(), [](const Host::SaveTag& a, const Host::SaveTag& b) { return a.Tag == b.Tag; });
}

std::vector<std::wstring> SaveSlots::WhyNot(int slot)
{
    std::vector<std::wstring> lines;
    if (CanPlay(slot))
        return lines;
    const std::vector<Host::SaveTag>& tags = g_Slots[slot - 1].Tags;
    for (const Host::SaveTag& tag : g_RunTags)
        if (!HasTag(tags, tag.Tag))
            lines.push_back(L"Needs a new save: " + Widen(tag.Title));
    for (const Host::SaveTag& tag : tags)
        if (!HasTag(g_RunTags, tag.Tag))
            lines.push_back(L"Made with " + Widen(tag.Title) + L" (off)");
    return lines;
}

void SaveSlots::Select(int slot, bool newGame)
{
    std::lock_guard<std::mutex> guard(g_Lock);
    slot = std::clamp(slot, 1, Count());
    Save::SetSavePath(PathOf(slot));
    g_LastSlot = slot;
    if (newGame)
    {
        g_Slots[slot - 1].Tags = g_RunTags;
        if (g_RunTags.empty())
            g_File["slots"][slot - 1].erase("tags");
        else
            g_File["slots"][slot - 1]["tags"] = TagsJson(g_RunTags);
    }
    WriteJson();
    Logger::WriteLog(std::format("Save slot {} picked ({})", slot, PathOf(slot).string()), MODULE_NAME, 0);
}

int SaveSlots::Add()
{
    std::lock_guard<std::mutex> guard(g_Lock);
    if (Count() >= MaxSlots)
        return 0;

    // The lowest free file number (2 and up; number 1 is the primary GameSaveName file, kept for the original
    // slot 1). Reusing freed numbers keeps the names tidy instead of climbing forever as slots are added and
    // deleted - a deleted "savegame-ex-2.dat" is handed to the next new slot, not left as a permanent gap.
    std::error_code error;
    auto taken = [&](int n)
    {
        const std::string file = NumberedFile(n);
        for (const Slot& existing : g_Slots)
            if (_stricmp(existing.File.c_str(), file.c_str()) == 0)
                return true;
        return std::filesystem::exists(Save::PrimarySavePath().parent_path() / file, error);
    };
    int number = 2;
    while (taken(number))
        ++number;

    Slot slot;
    slot.Name = std::format(L"Duelist {}", Count() + 1);
    slot.Avatar = DefaultAvatar(Count());
    slot.File = NumberedFile(number);
    g_File["slots"].push_back({ { "name", std::format("Duelist {}", Count() + 1) }, { "avatar", slot.Avatar }, { "file", slot.File } });
    g_Slots.push_back(std::move(slot));
    WriteJson();
    Logger::WriteLog(std::format("Save slot {} added ({})", Count(), g_Slots.back().File), MODULE_NAME, 0);
    return Count();
}

bool SaveSlots::Delete(int slot)
{
    std::lock_guard<std::mutex> guard(g_Lock);
    if (slot < 1 || slot > Count())
        return false;

    const std::filesystem::path save = PathOf(slot);
    std::error_code error;
    for (const std::filesystem::path& file : { save, std::filesystem::path(save.string() + ".series.bin") })
    {
        std::filesystem::remove(file, error);   // false without an error = there was no file
        if (error)
        {
            Logger::WriteLog(std::format("Could not erase {}: {}", file.string(), error.message()), MODULE_NAME, 2);
            return false;
        }
    }

    if (Count() > 1)
    {
        g_Slots.erase(g_Slots.begin() + (slot - 1));
        g_File["slots"].erase(g_File["slots"].begin() + (slot - 1));
        if (g_LastSlot > slot || g_LastSlot > Count())
            --g_LastSlot;
        g_LastSlot = std::clamp(g_LastSlot, 1, Count());
    }
    Save::SetSavePath(PathOf(g_LastSlot));   // the game's path never points at the moved file
    WriteJson();
    Logger::WriteLog(std::format("Save slot {} deleted ({} erased)", slot, save.filename().string()), MODULE_NAME, 0);
    return true;
}

void SaveSlots::SetAvatar(int slot, const std::string& key)
{
    std::lock_guard<std::mutex> guard(g_Lock);
    if (slot < 1 || slot > Count() || key.empty())
        return;
    g_Slots[slot - 1].Avatar = key;
    g_File["slots"][slot - 1]["avatar"] = key;
    WriteJson();
}

std::string SaveSlots::AvatarKeyOf(int slot)
{
    const std::string sprite = AvatarSpriteOf(slot);
    return sprite.size() > 8 ? sprite.substr(0, sprite.size() - 8) : sprite;   // without "_neutral"
}

std::vector<std::string> SaveSlots::AvatarKeys()
{
    std::vector<std::string> keys;
    for (int id = 1; id < kCharacterCount; ++id)
    {
        const std::string key = CharacterKey(id);
        if (!key.empty() && std::find(keys.begin(), keys.end(), key) == keys.end())
            keys.push_back(key);
    }
    for (const char* key : kDefaultAvatars)   // before chardata has loaded there are at least these
        if (std::find(keys.begin(), keys.end(), key) == keys.end())
            keys.push_back(key);
    return keys;
}
