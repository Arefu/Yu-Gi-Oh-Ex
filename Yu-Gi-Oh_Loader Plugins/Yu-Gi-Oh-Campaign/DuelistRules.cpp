#include "DuelistRules.h"
#include "Common.h"

#include <Windows.h>
#include <format>
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

    using GetInt_t = int(__fastcall*)();
    using SideValue_t = int(__fastcall*)(int side);

    uintptr_t orig_GetStartingLifePoints = 0x140768DC0;   // YGO::DUEL::Get_StartingLifePoints
    const auto SideCharacter = reinterpret_cast<SideValue_t>(0x140768D90);        // the duel side's character (dword_140C8D1F8[side])
    const auto IsMultiplayer = reinterpret_cast<GetInt_t>(YGO::Addr::Duel_Get_IsDuelMultiplayer);   // YGO::DUEL::Get_IsDuelMultiplayer

    struct Rules
    {
        int Lp = 0;
    };
    std::map<int, Rules> g_rules;   // character id -> its rules

    int __fastcall Hook_GetStartingLifePoints()
    {
        const int lp = reinterpret_cast<GetInt_t>(orig_GetStartingLifePoints)();
        if (IsMultiplayer())
            return lp;
        if (auto it = g_rules.find(SideCharacter(1)); it != g_rules.end() && it->second.Lp > 0)
            return it->second.Lp;
        return lp;
    }
}

namespace DuelistRules
{
    void Attach()
    {
        std::vector<std::string> problems;
        const nlohmann::json root = YGO::Mods::ReadMerged("duelistrules.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", it is left out", MODULE_NAME, 2);
        if (!root.is_object() || !root.contains("duelists") || !root["duelists"].is_array())
            return;
        for (const auto& entry : root["duelists"])
        {
            if (!entry.is_object())
                continue;
            const int id = Int(entry, "id").value_or(-1);
            Rules rules;
            rules.Lp = Int(entry, "lp").value_or(0);
            if (id < 0 || rules.Lp < 0 || rules.Lp > 999999)
            {
                Logger::WriteLog(std::format("duelistrules.json: duelist {} skipped (id 0-239, lp 1-999999)", id), MODULE_NAME, 2);
                continue;
            }
            if (rules.Lp > 0)
                g_rules[id] = rules;
        }
        if (g_rules.empty())
            return;
        DetourAttach(&(PVOID&)orig_GetStartingLifePoints, Hook_GetStartingLifePoints);
        Logger::WriteLog(std::format("Duelist rules: {} duelist(s) with their own starting LP", g_rules.size()), MODULE_NAME, 0);
    }
}
