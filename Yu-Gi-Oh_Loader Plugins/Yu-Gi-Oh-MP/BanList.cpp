#include "BanList.h"

#include <Windows.h>
#include <algorithm>
#include <cstring>
#include <cstdint>
#include <format>
#include <fstream>
#include <sstream>
#include <vector>

#include <json.hpp>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    constexpr uintptr_t kKonamiProps = 0x142847E50;   // YGO::CARDS::KONAMI_ID_CARD_PROPS
    constexpr size_t kKonamiStride = 0x30, kKonamiCount = 0x3A79, kKonamiLimit = 0x20;
    constexpr uintptr_t kFullProps = 0x142927600;     // YGO::CARDS::FULL_CARD_PROPS
    constexpr size_t kFullStride = 0xA0, kFullCount = 65536, kFullLimit = 0x64;
    constexpr int32_t kForbidden = 0, kLimited = 1, kSemiLimited = 2, kUnlimited = 3;

    using Setup_t = int64_t(__fastcall*)();
    Setup_t orig_SetupFullCardProps = reinterpret_cast<Setup_t>(0x14081A080);   // YGO::CARDS::Setup_FullCardProps

    BanList::Mode g_Mode = BanList::Mode::Game;
    BanList::List g_List;
    std::vector<int32_t> g_GameLimits;   // the Konami table's limits as the game loaded them
    bool g_Applied = false;              // the tables currently hold our values

    int32_t& KonamiLimit(size_t id) { return *reinterpret_cast<int32_t*>(kKonamiProps + id * kKonamiStride + kKonamiLimit); }
    int32_t& FullLimit(size_t id) { return *reinterpret_cast<int32_t*>(kFullProps + id * kFullStride + kFullLimit); }

    bool Loaded()
    {
        for (size_t id = 0; id < kKonamiCount; ++id)
            if (KonamiLimit(id) != 0)
                return true;   // Setup_CardPropTable has run (it writes 3 for every card first)
        return false;
    }

    void SetLimit(size_t id, int32_t limit)
    {
        if (id < kKonamiCount)
            KonamiLimit(id) = limit;
        if (id < kFullCount)
            FullLimit(id) = limit;
    }

    void Apply()
    {
        if (!Loaded())
            return;
        if (!g_Applied)   // the tables hold the game's list: keep it
        {
            g_GameLimits.resize(kKonamiCount);
            for (size_t id = 0; id < kKonamiCount; ++id)
                g_GameLimits[id] = KonamiLimit(id);
        }
        if (g_Mode == BanList::Mode::Game)
        {
            if (g_Applied)
                for (size_t id = 0; id < kKonamiCount; ++id)
                    SetLimit(id, g_GameLimits[id]);   // FULL_CARD_PROPS +0x64 is Get_CardLimitedStatus(id), the Konami table's value
            g_Applied = false;
            return;
        }
        for (size_t id = 0; id < kKonamiCount; ++id)
            SetLimit(id, kUnlimited);
        for (size_t id = kKonamiCount; id < kFullCount; ++id)
            if (FullLimit(id) < kUnlimited)
                FullLimit(id) = kUnlimited;
        if (g_Mode == BanList::Mode::Custom)
        {
            for (uint16_t id : g_List.SemiLimited) SetLimit(id, kSemiLimited);
            for (uint16_t id : g_List.Limited) SetLimit(id, kLimited);
            for (uint16_t id : g_List.Forbidden) SetLimit(id, kForbidden);
        }
        g_Applied = true;
    }

    // What Apply wrote for a card in the Konami table.
    int32_t Expected(size_t id)
    {
        if (g_Mode == BanList::Mode::Custom)
        {
            auto has = [id](const std::vector<uint16_t>& ids) { return std::find(ids.begin(), ids.end(), static_cast<uint16_t>(id)) != ids.end(); };
            if (has(g_List.Forbidden)) return kForbidden;
            if (has(g_List.Limited)) return kLimited;
            if (has(g_List.SemiLimited)) return kSemiLimited;
        }
        return kUnlimited;
    }

    // Setup_FullCardProps copies the Konami table into FULL_CARD_PROPS. After a language change that table was read again from
    // pd_limits.bin (the game's list: keep it, then apply again); if it still holds what we wrote, only FULL_CARD_PROPS needs it again.
    int64_t __fastcall Hook_SetupFullCardProps()
    {
        const int64_t result = orig_SetupFullCardProps();
        if (g_Applied)
            for (size_t id = 0; id < kKonamiCount; ++id)
                if (KonamiLimit(id) != Expected(id))
                {
                    g_Applied = false;   // freshly loaded
                    break;
                }
        if (g_Mode != BanList::Mode::Game)
            Apply();
        return result;
    }

    void Use(BanList::Mode mode, const BanList::List* list)
    {
        g_Mode = mode;
        g_List = list ? *list : BanList::List{};
        Apply();
        Logger::WriteLog(mode == BanList::Mode::Game ? std::string("Ban list: the game's") : mode == BanList::Mode::Off ? std::string("Ban list off: every card at 3")
            : std::format("Ban list: {} ({} forbidden, {} limited, {} semi-limited)", g_List.Name, g_List.Forbidden.size(), g_List.Limited.size(), g_List.SemiLimited.size()),
            MODULE_NAME, 0);
    }

    std::vector<uint16_t> Ids(const nlohmann::json& json, const char* key)
    {
        std::vector<uint16_t> ids;
        if (auto it = json.find(key); it != json.end() && it->is_array())
            for (const auto& value : *it)
                if (value.is_number_integer() && value.get<int>() > 0 && value.get<int>() < 0x10000)
                    ids.push_back(static_cast<uint16_t>(value.get<int>()));
        return ids;
    }

    constexpr char kBase64[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    std::string ToBase64(const std::vector<uint8_t>& bytes)
    {
        std::string text;
        for (size_t i = 0; i < bytes.size(); i += 3)
        {
            const uint32_t chunk = (bytes[i] << 16) | ((i + 1 < bytes.size() ? bytes[i + 1] : 0) << 8) | (i + 2 < bytes.size() ? bytes[i + 2] : 0);
            text += kBase64[(chunk >> 18) & 63];
            text += kBase64[(chunk >> 12) & 63];
            text += i + 1 < bytes.size() ? kBase64[(chunk >> 6) & 63] : '=';
            text += i + 2 < bytes.size() ? kBase64[chunk & 63] : '=';
        }
        return text;
    }

    std::vector<uint8_t> FromBase64(const std::string& text)
    {
        std::vector<uint8_t> bytes;
        uint32_t chunk = 0;
        int bits = 0;
        for (char c : text)
        {
            const char* at = std::strchr(kBase64, c);
            if (!at || c == 0)
                continue;   // padding, newlines
            chunk = (chunk << 6) | static_cast<uint32_t>(at - kBase64);
            bits += 6;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.push_back(static_cast<uint8_t>((chunk >> bits) & 0xFF));
            }
        }
        return bytes;
    }
}

namespace BanList
{
    void Attach()
    {
        DetourAttach(&reinterpret_cast<PVOID&>(orig_SetupFullCardProps), Hook_SetupFullCardProps);
    }

    void UseGame() { Use(Mode::Game, nullptr); }
    void UseOff() { Use(Mode::Off, nullptr); }
    void UseCustom(const List& list) { Use(Mode::Custom, &list); }

    Mode CurrentMode() { return g_Mode; }
    const List& CurrentList() { return g_List; }

    const std::vector<List>& Saved()
    {
        static std::vector<List> lists;
        static bool read = false;
        if (read)
            return lists;
        read = true;
        for (const auto& path : YGO::Mods::FilesIn("banlists", L".json"))
        {
            try
            {
                std::ifstream file(path);
                const nlohmann::json json = nlohmann::json::parse(file);
                List list;
                list.Name = json.value("name", path.stem().string());
                list.Forbidden = Ids(json, "forbidden");
                list.Limited = Ids(json, "limited");
                list.SemiLimited = Ids(json, "semiLimited");
                lists.push_back(std::move(list));
            }
            catch (const std::exception& e)
            {
                Logger::WriteLog(std::format("Ban list {} skipped: {}", path.string(), e.what()), MODULE_NAME, 1);
            }
        }
        Logger::WriteLog(std::format("{} custom ban list(s) in banlists\\", lists.size()), MODULE_NAME, 0);
        return lists;
    }

    std::string Encode(const List& list)
    {
        std::vector<uint8_t> bytes;
        for (const auto* ids : { &list.Forbidden, &list.Limited, &list.SemiLimited })
        {
            const uint16_t count = static_cast<uint16_t>(ids->size());
            bytes.push_back(static_cast<uint8_t>(count));
            bytes.push_back(static_cast<uint8_t>(count >> 8));
            for (uint16_t id : *ids)
            {
                bytes.push_back(static_cast<uint8_t>(id));
                bytes.push_back(static_cast<uint8_t>(id >> 8));
            }
        }
        return list.Name + "\n" + ToBase64(bytes);
    }

    bool Decode(const std::string& text, List& list)
    {
        list = {};
        const size_t newline = text.find('\n');
        list.Name = text.substr(0, newline);
        if (list.Name.empty())
            return false;
        if (newline == std::string::npos)
            return true;   // a name only
        const std::vector<uint8_t> bytes = FromBase64(text.substr(newline + 1));
        size_t at = 0;
        for (auto* ids : { &list.Forbidden, &list.Limited, &list.SemiLimited })
        {
            if (at + 2 > bytes.size())
                break;
            const size_t count = bytes[at] | (bytes[at + 1] << 8);
            at += 2;
            for (size_t i = 0; i < count && at + 2 <= bytes.size(); ++i, at += 2)
                if (const uint16_t id = static_cast<uint16_t>(bytes[at] | (bytes[at + 1] << 8)))
                    ids->push_back(id);
        }
        return true;
    }
}
