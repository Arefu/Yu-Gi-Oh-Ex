#include <Windows.h>
#include <format>
#include <fstream>

#include "SummonJson.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    nlohmann::json g_Cards;
    nlohmann::json g_Summoning;
    std::vector<SummonJson::Entry> g_Entries;
    bool g_Loaded = false;

    // Every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h); null when there is none.
    nlohmann::json Read(const std::string& name)
    {
        std::vector<std::string> problems;
        nlohmann::json root = YGO::Mods::ReadMerged(name, "cards", &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", its summoning requirements are left out", MODULE_NAME, 2);
        return root;
    }

    void Add(const nlohmann::json& root, const char* file, bool custom)
    {
        const nlohmann::json* list = root.is_array() ? &root : root.is_object() && root.contains("cards") ? &root["cards"] : nullptr;
        if (!list || !list->is_array())
            return;
        for (size_t i = 0; i < list->size(); ++i)
        {
            const nlohmann::json& entry = (*list)[i];
            if (!entry.is_object() || !entry.contains("id") || !entry["id"].is_number_integer())
                continue;
            const int id = entry["id"].get<int>();
            if (id < 1 || id > SummonJson::kLastExtraCardId)
                continue;
            g_Entries.push_back({ &entry, id, custom, std::format("{} entry {} (\"{}\")", file, i, entry.value("name", std::to_string(id))) });
        }
    }

    using IdMap_t = unsigned short(__cdecl*)(unsigned short);
    IdMap_t g_ActiveDuelId = nullptr;
    IdMap_t g_RealIdForBorrowed = nullptr;

    void BindIdMaps()
    {
        if (g_ActiveDuelId && g_RealIdForBorrowed)
            return;
        if (HMODULE cards = GetModuleHandleA("Yu-Gi-Oh-MoreCards.dll"))
        {
            g_ActiveDuelId = reinterpret_cast<IdMap_t>(GetProcAddress(cards, "Card_GetActiveDuelSessionId"));
            g_RealIdForBorrowed = reinterpret_cast<IdMap_t>(GetProcAddress(cards, "Card_GetRealIdForBorrowed"));
        }
    }
}

const std::vector<SummonJson::Entry>& SummonJson::Entries()
{
    if (!g_Loaded)
    {
        g_Loaded = true;
        g_Cards = Read("cards.json");
        g_Summoning = Read("summoning.json");
        Add(g_Cards, "cards.json", true);
        Add(g_Summoning, "summoning.json", false);
    }
    return g_Entries;
}

uint16_t SummonJson::EngineId(uint16_t id)
{
    if (id < kFirstExtraCardId || id > kLastExtraCardId)
        return id;
    BindIdMaps();
    return g_ActiveDuelId ? g_ActiveDuelId(id) : id;
}

uint16_t SummonJson::RealIdForBorrowed(uint16_t id)
{
    BindIdMaps();
    return g_RealIdForBorrowed ? g_RealIdForBorrowed(id) : 0;
}
