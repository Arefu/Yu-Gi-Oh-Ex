#include <Windows.h>
#include <algorithm>
#include <cstring>
#include <format>
#include <fstream>
#include <mutex>
#include <vector>

#include <json.hpp>

#include "Logger.h"
#include "Save.h"
#include "SaveSlots.h"

using json = nlohmann::json;

namespace
{
    constexpr int kDefaultSlots = 3;
    constexpr const char* kIniSection = "Yu-Gi-Oh-Core";

    // Portraits the slots get when saves.json does not name one (sprites in pdui/chars, without "_neutral").
    constexpr const char* kDefaultAvatars[SaveSlots::MaxSlots] = { "yugimuto", "jadenyuki", "yuseifudo", "yumatsukumo", "yuya" };

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
    constexpr uintptr_t kCharacterRecords = 0x142913470;
    constexpr int kCharacterCount = 240;

    struct Slot
    {
        std::wstring Name;
        std::string Avatar;   // sprite key
    };

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

        const json* slots = g_File.contains("slots") && g_File["slots"].is_array() ? &g_File["slots"] : nullptr;
        const int count = slots ? std::clamp(static_cast<int>(slots->size()), 1, SaveSlots::MaxSlots) : kDefaultSlots;

        for (int i = 0; i < count; ++i)
        {
            Slot slot;
            slot.Name = std::format(L"Duelist {}", i + 1);
            slot.Avatar = kDefaultAvatars[i];

            if (slots && (*slots)[i].is_object())
            {
                const json& entry = (*slots)[i];
                if (entry.contains("name") && entry["name"].is_string() && !entry["name"].get<std::string>().empty())
                    slot.Name = Widen(entry["name"].get<std::string>());
                if (entry.contains("avatar") && entry["avatar"].is_string() && !entry["avatar"].get<std::string>().empty())
                    slot.Avatar = entry["avatar"].get<std::string>();
                else if (entry.contains("avatar") && entry["avatar"].is_number_integer())
                    slot.Avatar = "#" + std::to_string(entry["avatar"].get<int>());   // a character id, resolved when shown (chardata loads later)
            }
            g_Slots.push_back(std::move(slot));
        }

        if (!slots)
        {
            // The defaults go into the file the first time a slot is picked, so there is one to edit.
            json list = json::array();
            for (int i = 0; i < count; ++i)
                list.push_back({ { "name", std::format("Duelist {}", i + 1) }, { "avatar", kDefaultAvatars[i] } });
            g_File["slots"] = std::move(list);
        }

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

    const std::string ini = Save::GameFolder() + "Config.ini";
    g_ForcedSlot = static_cast<int>(GetPrivateProfileIntA(kIniSection, "SaveSlot", 0, ini.c_str()));
    if (g_ForcedSlot < 0 || g_ForcedSlot > Count())
        g_ForcedSlot = 0;

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
    return g_ForcedSlot == 0 && Count() > 1;
}

std::filesystem::path SaveSlots::PathOf(int slot)
{
    const std::filesystem::path& primary = Save::PrimarySavePath();
    if (slot <= 1)
        return primary;
    std::filesystem::path name = primary.stem();
    name += std::format("-{}", slot);
    name += primary.extension();
    return primary.parent_path() / name;
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
            key = kDefaultAvatars[slot - 1];
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

    if (static_cast<size_t>(file.tellg()) != kSaveSize)
        return summary;
    std::vector<unsigned char> data(kSaveSize);
    file.seekg(0);
    if (!file.read(reinterpret_cast<char*>(data.data()), kSaveSize))
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
    return summary;
}

void SaveSlots::Select(int slot)
{
    std::lock_guard<std::mutex> guard(g_Lock);
    slot = std::clamp(slot, 1, Count());
    Save::SetSavePath(PathOf(slot));
    g_LastSlot = slot;
    WriteJson();
    Logger::WriteLog(std::format("Save slot {} picked ({})", slot, PathOf(slot).string()), MODULE_NAME, 0);
}
