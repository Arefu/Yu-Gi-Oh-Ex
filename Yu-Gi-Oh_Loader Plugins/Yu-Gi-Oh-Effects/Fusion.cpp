#include <Windows.h>
#include <detours.h>
#include <algorithm>
#include <cstdint>
#include <format>
#include <fstream>
#include <map>
#include <string>
#include <vector>

#include <json.hpp>

#include "Fusion.h"
#include "Logger.h"

namespace
{
    // TODO once Card::ResolveDuelSessionId (Yu-Gi-Oh-Cards, Card.h) is implemented (see the
    // "ygo-duel-id-remap-plan" memory): a recipe or material id above 16383 is accepted here today
    // (up to kLastExtraCardId) but is NEVER USABLE in an actual duel yet - the hooks below are called
    // with whatever id the duel engine currently holds, which for a card above 16383 is either the
    // aliased wrong card's id (before the remap exists) or, once it exists, the card's BORROWED
    // scratch-pool id, not this file's own g_Recipes key. Hook_Get_FusionMaterialCount/Material/
    // UsingMaterial must resolve every id they receive (and are keyed by) through the real<->borrowed
    // mapping once it exists, not compare against g_Recipes' ids directly.
    constexpr int kFirstExtraCardId = 0x3A79;
    constexpr int kLastExtraCardId = 0x4E1F;
    constexpr size_t kMinMaterials = 2;
    constexpr size_t kMaxMaterials = 5; // the game's 4+ table has five slots

    // Get_FusionsUsingMaterial writes into a caller's stack buffer that has room for about 34 ids, and the vanilla tables already fill part of it.
    constexpr int kMaxUsers = 30;

    // Fusion monster id -> material ids, in order. Ordered so the detours give the same answer every run.
    std::map<int, std::vector<int>> g_Recipes;

    using Get_FusionMaterialCount_t = int64_t(__fastcall*)(int);
    using Get_FusionMaterial_t = int64_t(__fastcall*)(int, int);
    using Get_FusionsUsingMaterial_t = int64_t(__fastcall*)(int, int*);

    Get_FusionMaterialCount_t orig_Get_FusionMaterialCount = reinterpret_cast<Get_FusionMaterialCount_t>(0x140006250);
    Get_FusionMaterial_t orig_Get_FusionMaterial = reinterpret_cast<Get_FusionMaterial_t>(0x1400063E0);
    Get_FusionsUsingMaterial_t orig_Get_FusionsUsingMaterial = reinterpret_cast<Get_FusionsUsingMaterial_t>(0x140006610);

    int64_t __fastcall Hook_Get_FusionMaterialCount(int fusionId)
    {
        auto it = g_Recipes.find(fusionId);
        if (it != g_Recipes.end())
            return static_cast<int64_t>(it->second.size());
        return orig_Get_FusionMaterialCount(fusionId);
    }

    int64_t __fastcall Hook_Get_FusionMaterial(int fusionId, int index)
    {
        auto it = g_Recipes.find(fusionId);
        if (it != g_Recipes.end())
            return index >= 0 && static_cast<size_t>(index) < it->second.size() ? it->second[index] : 0;
        return orig_Get_FusionMaterial(fusionId, index);
    }

    int64_t __fastcall Hook_Get_FusionsUsingMaterial(int materialId, int* out)
    {
        int count = static_cast<int>(orig_Get_FusionsUsingMaterial(materialId, out));

        // A custom recipe replaces the game's for the same id (cards.json can change a vanilla fusion).
        int kept = 0;
        for (int i = 0; i < count; ++i)
        {
            if (!g_Recipes.contains(out[i]))
                out[kept++] = out[i];
        }
        count = kept;

        for (const auto& [fusionId, materials] : g_Recipes)
        {
            if (std::find(materials.begin(), materials.end(), materialId) == materials.end())
                continue;
            if (count >= kMaxUsers)
            {
                Logger::WriteLog(std::format("Material {} is used by more fusions than the game's buffer holds, {} is not listed for it", materialId, fusionId), MODULE_NAME, 2);
                break;
            }
            out[count++] = fusionId;
        }
        return count;
    }

    std::string CardsJsonPath()
    {
        char exe[MAX_PATH]{};
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        std::string folder = exe;
        folder.resize(folder.find_last_of("\\/") + 1);
        return folder + "Yu-Gi-Oh-Ex\\cards.json";
    }

    void LoadRecipes()
    {
        const std::string path = CardsJsonPath();
        std::ifstream file(path);
        if (!file)
        {
            Logger::WriteLog(std::format("No {}, no custom fusion recipes", path), MODULE_NAME, 0);
            return;
        }

        nlohmann::json root = nlohmann::json::parse(file, nullptr, false, true);
        if (root.is_discarded())
        {
            Logger::WriteLog(std::format("{} is not valid JSON, no custom fusion recipes", path), MODULE_NAME, 2);
            return;
        }

        const nlohmann::json& list = root.is_array() ? root : root["cards"];
        if (!list.is_array())
            return;

        for (size_t i = 0; i < list.size(); ++i)
        {
            const nlohmann::json& entry = list[i];
            if (!entry.is_object() || !entry.contains("fusion"))
                continue;

            const std::string label = std::format("cards.json entry {} (\"{}\")", i, entry.value("name", std::string()));
            if (!entry.contains("id") || !entry["id"].is_number_integer())
            {
                Logger::WriteLog(std::format("{}: has \"fusion\" but no integer \"id\", skipped", label), MODULE_NAME, 2);
                continue;
            }

            // Ids below 14969 are the game's cards: a recipe for one replaces the game's, so that is allowed too.
            const int id = entry["id"].get<int>();
            if (id < 1 || id > kLastExtraCardId)
            {
                Logger::WriteLog(std::format("{}: fusion id {} is out of range, skipped", label, id), MODULE_NAME, 2);
                continue;
            }

            const nlohmann::json& materials = entry["fusion"];
            if (!materials.is_array() || materials.size() < kMinMaterials || materials.size() > kMaxMaterials)
            {
                Logger::WriteLog(std::format("{}: \"fusion\" must list {} to {} card ids, skipped", label, kMinMaterials, kMaxMaterials), MODULE_NAME, 2);
                continue;
            }

            std::vector<int> ids;
            bool valid = true;
            for (const auto& m : materials)
            {
                if (!m.is_number_integer() || m.get<int>() < 1 || m.get<int>() > kLastExtraCardId)
                {
                    valid = false;
                    break;
                }
                ids.push_back(m.get<int>());
            }
            if (!valid)
            {
                Logger::WriteLog(std::format("{}: \"fusion\" holds something that is not a card id, skipped", label), MODULE_NAME, 2);
                continue;
            }

            g_Recipes[id] = std::move(ids);
        }
    }
}

size_t Fusion::Count()
{
    return g_Recipes.size();
}

void Fusion::Setup()
{
    LoadRecipes();
    Logger::WriteLog(std::format("Loaded {} custom fusion recipe(s)", g_Recipes.size()), MODULE_NAME, 0);
    if (g_Recipes.empty())
        return;

    DetourAttach(&(PVOID&)orig_Get_FusionMaterialCount, Hook_Get_FusionMaterialCount);
    DetourAttach(&(PVOID&)orig_Get_FusionMaterial, Hook_Get_FusionMaterial);
    DetourAttach(&(PVOID&)orig_Get_FusionsUsingMaterial, Hook_Get_FusionsUsingMaterial);
}
