#include <Windows.h>
#include <detours.h>
#include <algorithm>
#include <cstdint>
#include <format>
#include <fstream>
#include <map>
#include <cctype>
#include <string>
#include <vector>

#include <json.hpp>

#include "Fusion.h"
#include "Logger.h"

namespace
{
    // In a duel a custom card above 16383 (or one lending its effect source's id, docs/EffectSystem.md section 32) plays under a BORROWED
    // vanilla id (Yu-Gi-Oh-MoreCards, ygo-duel-id-remap-plan). The hooks get engine ids: a fusion id is turned back into the custom card
    // (Card_GetRealIdForBorrowed) before g_Recipes is asked, and a custom material id is handed out as the id it plays under
    // (Card_GetActiveDuelSessionId), so the game's same-name check compares like with like.
    constexpr int kFirstExtraCardId = 0x3BC4;   // 15300, as in Yu-Gi-Oh-Cards/Card.h
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

    // Fusion_CardMatchesMaterialCode (0x140004B10, code, card instance index, override id): the duel's material check. Every recipe slot
    // goes through it (Fusion_SelectMaterials 0x140006BD0), so a recipe may hold a generic code instead of a card id:
    //   >= 3000   the card is (named like) that card id
    //   1..25     race (CARD_Type: 1 Dragon .. 24 Divine-Beast)
    //   26..31    attribute (26 LIGHT, 27 DARK, 28 WATER, 29 FIRE, 30 EARTH, 31 WIND)
    //   32..43    Level == code-31,   44..55 Level >= code-43,   56..67 Level <= code-55
    //   72 Gemini, 73 Normal, 74 Effect, 75 Synchro, 81 Synchro or Xyz, 82 Xyz, 83 Pendulum, 89 Fusion, 90 Tuner, 95 Link
    //   98..516   archetype code-98 (the game's 1..418)
    // Codes 517..2999 mean nothing to the game (it returns 0, and the UI code that reads recipes skips them); Hook_MatchesMaterialCode
    // answers 517..2496 as custom archetypes (code-98 >= 419, which the Cards plugin's Is_CardInNamedArchetype hook knows), 2497 as "Ritual
    // Monster", 2498 as Illusion (custom race 26, Yu-Gi-Oh-Cards Card::Illusion), 2499 as "any monster" and 2500..2999 as COMBINED materials ("1 LIGHT Warrior monster", "1 non-Tuner Synchro Monster"): every part must match, a negative part
    // must not.
    using MatchesMaterialCode_t = int64_t(__fastcall*)(int, int, int);
    MatchesMaterialCode_t orig_MatchesMaterialCode = reinterpret_cast<MatchesMaterialCode_t>(0x140004B10);
    using Kind_IsMonster_t = bool(__fastcall*)(int);
    const Kind_IsMonster_t Kind_IsMonster = reinterpret_cast<Kind_IsMonster_t>(0x140742DF0);
    using Get_CardProp_t = int(__fastcall*)(int);
    const Get_CardProp_t Get_MonsterType = reinterpret_cast<Get_CardProp_t>(0x14081A7B0);
    const Get_CardProp_t Get_CardKind = reinterpret_cast<Get_CardProp_t>(0x14081A650);
    using Is_CardInNamedArchetype_t = int64_t(__fastcall*)(uint16_t, int);
    const Is_CardInNamedArchetype_t Is_CardInNamedArchetype = reinterpret_cast<Is_CardInNamedArchetype_t>(0x14076CFF0);
    constexpr uintptr_t kCardInstances = 0x143499798; // 8 bytes per duel card instance, id = low 14 bits
    constexpr int kArchetypeCodeBase = 98;
    constexpr int kLastGameMaterialCode = 516;
    constexpr int kRitualCode = 2497;
    constexpr int kIllusionCode = 2498;
    constexpr int kAnyMonsterCode = 2499;
    constexpr int kIllusionRace = 0x1A;
    constexpr int kRitualKinds[] = { 4, 5, 38 }; // Ritual, Ritual Effect, Ritual Spirit Effect (WolfX CardsPanel kinds)
    constexpr int kFirstCombinedCode = 2500;
    constexpr int kFirstCardIdCode = 3000;
    bool g_NeedsMatcherHook = false;

    // Combined material code - kFirstCombinedCode -> its parts (negative = "non-"); all must hold, or with any = true one of them.
    struct Combined
    {
        bool any = false;
        std::vector<int> parts;
        bool operator==(const Combined&) const = default;
    };
    std::vector<Combined> g_Combined;

    int64_t __fastcall Hook_MatchesMaterialCode(int code, int instance, int overrideId);

    bool CombinedMatches(int code, int instance, int overrideId)
    {
        const size_t index = static_cast<size_t>(code - kFirstCombinedCode);
        if (index >= g_Combined.size())
            return false;
        const Combined& combined = g_Combined[index];
        for (int part : combined.parts)
        {
            const bool matches = (Hook_MatchesMaterialCode(part < 0 ? -part : part, instance, overrideId) != 0) == (part > 0);
            if (combined.any && matches)
                return true;
            if (!combined.any && !matches)
                return false;
        }
        return !combined.any;
    }

    int64_t __fastcall Hook_MatchesMaterialCode(int code, int instance, int overrideId)
    {
        if (code <= kLastGameMaterialCode || code >= kFirstCardIdCode)
            return orig_MatchesMaterialCode(code, instance, overrideId);

        // Our codes: the game's own gate (a material code below 3000 only matches monsters) first.
        const int id = overrideId != 0 ? overrideId
            : static_cast<int>(*reinterpret_cast<const uint32_t*>(kCardInstances + 8 * static_cast<uintptr_t>(instance & 0x1FFF)) & 0x3FFF);
        if (id == 0 || !Kind_IsMonster(id))
            return 0;
        if (code == kAnyMonsterCode)
            return 1;
        if (code == kIllusionCode)
            return Get_MonsterType(id) == kIllusionRace ? 1 : 0;
        if (code == kRitualCode)
            return std::find(std::begin(kRitualKinds), std::end(kRitualKinds), Get_CardKind(id)) != std::end(kRitualKinds) ? 1 : 0;
        if (code >= kFirstCombinedCode)
            return CombinedMatches(code, instance, overrideId) ? 1 : 0;
        return Is_CardInNamedArchetype(static_cast<uint16_t>(id), code - kArchetypeCodeBase) != 0 ? 1 : 0;
    }

    std::string Squash(const std::string& text)
    {
        std::string out;
        for (char c : text)
        {
            if (c != ' ' && c != '-' && c != '_')
                out.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
        }
        return out;
    }

    int ParseNumber(const std::string& text, size_t from)
    {
        if (from >= text.size())
            return -1;
        int value = 0;
        for (size_t i = from; i < text.size(); ++i)
        {
            if (!std::isdigit(static_cast<unsigned char>(text[i])) || value > 100000)
                return -1;
            value = value * 10 + (text[i] - '0');
        }
        return value;
    }

    // Readable material -> game code. Accepts "Dragon", "race:Dragon", "DARK", "attribute:DARK", "level:4", "level>=5", "level<=4",
    // "archetype:12", "monster" (any), "Illusion", "ritual", "normal", "effect", "tuner", "fusion", "synchro", "xyz", "pendulum", "link", "gemini", "synchroorxyz". 0 = unknown.
    int MaterialCode(const std::string& raw)
    {
        static const char* kRaces[] = { "dragon", "zombie", "fiend", "pyro", "seaserpent", "rock", "machine", "fish", "dinosaur", "insect",
            "beast", "beastwarrior", "plant", "aqua", "warrior", "wingedbeast", "fairy", "spellcaster", "thunder", "reptile", "psychic", "wyrm",
            "cyberse", "divinebeast" };
        static const char* kAttributes[] = { "light", "dark", "water", "fire", "earth", "wind" };
        static const std::pair<const char*, int> kKinds[] = { { "gemini", 72 }, { "normal", 73 }, { "effect", 74 }, { "synchro", 75 },
            { "synchroorxyz", 81 }, { "xyz", 82 }, { "pendulum", 83 }, { "fusion", 89 }, { "tuner", 90 }, { "link", 95 } };

        std::string text = Squash(raw);
        std::string kind;
        const size_t colon = text.find(':');
        if (colon != std::string::npos)
        {
            kind = text.substr(0, colon);
            text = text.substr(colon + 1);
        }

        if (kind.empty() && text.rfind("level", 0) == 0)
        {
            kind = "level";
            text = text.substr(5);
        }
        if (kind == "level")
        {
            int base = 31;
            size_t at = 0;
            if (text.rfind(">=", 0) == 0) { base = 43; at = 2; }
            else if (text.rfind("<=", 0) == 0) { base = 55; at = 2; }
            else if (text.rfind("=", 0) == 0) { at = 1; }
            const int level = ParseNumber(text, at);
            return level >= 1 && level <= 12 ? base + level : 0;
        }
        if (kind == "archetype")
        {
            const int archetype = ParseNumber(text, 0);
            return archetype >= 1 && archetype + kArchetypeCodeBase < kRitualCode ? archetype + kArchetypeCodeBase : 0;
        }
        if (kind.empty() || kind == "race" || kind == "type")
        {
            for (int i = 0; i < static_cast<int>(std::size(kRaces)); ++i)
            {
                if (text == kRaces[i])
                    return i + 1;
            }
        }
        if (kind.empty() || kind == "attribute")
        {
            for (int i = 0; i < static_cast<int>(std::size(kAttributes)); ++i)
            {
                if (text == kAttributes[i])
                    return 26 + i;
            }
        }
        if (kind.empty() && text == "monster")
            return kAnyMonsterCode;
        if ((kind.empty() || kind == "race" || kind == "type") && text == "illusion")
            return kIllusionCode;
        if ((kind.empty() || kind == "kind") && text == "ritual")
            return kRitualCode;
        if (kind.empty() || kind == "kind")
        {
            for (const auto& [name, code] : kKinds)
            {
                if (text == name)
                    return code;
            }
        }
        return 0;
    }

    int AddCombined(Combined combined)
    {
        g_NeedsMatcherHook = true;
        // The same combination twice ("2 LIGHT Warrior monsters") shares one code.
        for (size_t i = 0; i < g_Combined.size(); ++i)
        {
            if (g_Combined[i] == combined)
                return kFirstCombinedCode + static_cast<int>(i);
        }
        if (g_Combined.size() >= static_cast<size_t>(kFirstCardIdCode - kFirstCombinedCode))
            return 0;
        g_Combined.push_back(std::move(combined));
        return kFirstCombinedCode + static_cast<int>(g_Combined.size() - 1);
    }

    // A material -> its code, negative for "non-X" / "!X" (only meaningful inside a combination), 0 = not understood.
    //   number            card id or raw game code
    //   "word"            see MaterialCode
    //   [a, b, ...]       all of them ("LIGHT Warrior" = ["LIGHT", "Warrior"])
    //   {"any": [a, b]}   one of them ("LIGHT or DARK" = {"any": ["LIGHT", "DARK"]})
    int ParseNode(const nlohmann::json& m)
    {
        if (m.is_number_integer())
        {
            const int value = m.get<int>();
            return value >= 1 && value <= kLastExtraCardId ? value : 0;
        }
        if (m.is_string())
        {
            std::string text = m.get<std::string>();
            bool negate = false;
            if (!text.empty() && text[0] == '!')
            {
                negate = true;
                text = text.substr(1);
            }
            else if (Squash(text).rfind("non", 0) == 0)
            {
                negate = true;
                text = text.substr(text.find_first_of("nN") + 3);
            }
            const int code = MaterialCode(text);
            if (code > kLastGameMaterialCode && code < kFirstCardIdCode)
                g_NeedsMatcherHook = true;
            return negate ? -code : code;
        }

        Combined combined;
        const nlohmann::json* items = &m;
        if (m.is_object())
        {
            if (!m.contains("any") || !m["any"].is_array())
                return 0;
            combined.any = true;
            items = &m["any"];
        }
        if (!items->is_array() || items->empty())
            return 0;
        for (const auto& item : *items)
        {
            const int code = ParseNode(item);
            if (code == 0)
                return 0;
            combined.parts.push_back(code);
        }
        if (combined.parts.size() == 1 && combined.parts[0] > 0)
            return combined.parts[0];
        return AddCombined(std::move(combined));
    }

    // A recipe slot: as ParseNode, a lone "non-X" becomes a combination of one.
    int ParseMaterial(const nlohmann::json& m)
    {
        const int code = ParseNode(m);
        if (code >= 0)
            return code;
        return AddCombined(Combined{ false, { code } });
    }

    using IdMap_t = unsigned short(__cdecl*)(unsigned short);
    IdMap_t g_RealIdForBorrowed = nullptr;
    IdMap_t g_ActiveDuelId = nullptr;

    void BindIdMaps()
    {
        if (g_RealIdForBorrowed && g_ActiveDuelId)
            return;
        if (HMODULE cards = GetModuleHandleA("Yu-Gi-Oh-MoreCards.dll"))
        {
            g_RealIdForBorrowed = reinterpret_cast<IdMap_t>(GetProcAddress(cards, "Card_GetRealIdForBorrowed"));
            g_ActiveDuelId = reinterpret_cast<IdMap_t>(GetProcAddress(cards, "Card_GetActiveDuelSessionId"));
        }
    }

    // The recipe for an engine id: the custom card a borrowed id stands for (nullptr when that card has no recipe - the vanilla
    // recipe of the borrowed id is not its own), else the id's own custom recipe. `borrowed` tells the caller not to fall back.
    const std::vector<int>* RecipeFor(int engineId, bool& borrowed)
    {
        BindIdMaps();
        borrowed = false;
        if (g_RealIdForBorrowed && engineId > 0 && engineId <= 0xFFFF)
        {
            if (const unsigned short real = g_RealIdForBorrowed(static_cast<unsigned short>(engineId)); real != 0)
            {
                borrowed = true;
                auto it = g_Recipes.find(real);
                return it != g_Recipes.end() ? &it->second : nullptr;
            }
        }
        auto it = g_Recipes.find(engineId);
        return it != g_Recipes.end() ? &it->second : nullptr;
    }

    // A recipe entry as the engine sees it: custom card ids become the id they play under this duel.
    int EngineMaterial(int material)
    {
        if (material < kFirstExtraCardId || material > kLastExtraCardId)
            return material;
        BindIdMaps();
        return g_ActiveDuelId ? g_ActiveDuelId(static_cast<unsigned short>(material)) : material;
    }

    int64_t __fastcall Hook_Get_FusionMaterialCount(int fusionId)
    {
        bool borrowed = false;
        if (const auto* recipe = RecipeFor(fusionId, borrowed))
            return static_cast<int64_t>(recipe->size());
        return borrowed ? 0 : orig_Get_FusionMaterialCount(fusionId);
    }

    int64_t __fastcall Hook_Get_FusionMaterial(int fusionId, int index)
    {
        bool borrowed = false;
        if (const auto* recipe = RecipeFor(fusionId, borrowed))
            return index >= 0 && static_cast<size_t>(index) < recipe->size() ? EngineMaterial((*recipe)[index]) : 0;
        return borrowed ? 0 : orig_Get_FusionMaterial(fusionId, index);
    }

    int64_t __fastcall Hook_Get_FusionsUsingMaterial(int materialId, int* out)
    {
        int count = static_cast<int>(orig_Get_FusionsUsingMaterial(materialId, out));

        // A custom recipe replaces the game's for the same id (cards.json can change a vanilla fusion), and a borrowed id's vanilla
        // recipe is not the custom card's.
        int kept = 0;
        for (int i = 0; i < count; ++i)
        {
            bool borrowed = false;
            if (RecipeFor(out[i], borrowed) == nullptr && !borrowed)
                out[kept++] = out[i];
        }
        count = kept;

        for (const auto& [fusionId, materials] : g_Recipes)
        {
            if (std::none_of(materials.begin(), materials.end(), [&](int m) { return EngineMaterial(m) == materialId; }))
                continue;
            if (count >= kMaxUsers)
            {
                Logger::WriteLog(std::format("Material {} is used by more fusions than the game's buffer holds, {} is not listed for it", materialId, fusionId), MODULE_NAME, 2);
                break;
            }
            out[count++] = EngineMaterial(fusionId);
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
                Logger::WriteLog(std::format("{}: \"fusion\" must list {} to {} materials, skipped", label, kMinMaterials, kMaxMaterials), MODULE_NAME, 2);
                continue;
            }

            // A material is a card id (>= 3000), a raw game material code (1..2999, see Hook_MatchesMaterialCode), a readable one ("Dragon",
            // "attribute:DARK", "level>=5", "archetype:12", "tuner", "non-Tuner"...), an array of those that must ALL hold
            // (["LIGHT", "Warrior"]) or {"any": [...]} (one of them); the last two become combined codes.
            std::vector<int> ids;
            std::string bad;
            for (const auto& m : materials)
            {
                const int code = ParseMaterial(m);
                if (code == 0)
                {
                    bad = m.dump();
                    break;
                }
                ids.push_back(code);
            }
            if (!bad.empty())
            {
                Logger::WriteLog(std::format("{}: \"fusion\" material {} is not a card id or a known material (race, attribute, level, archetype, kind), skipped", label, bad), MODULE_NAME, 2);
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
    Logger::WriteLog(std::format("Loaded {} custom fusion recipe(s), {} combined material(s)", g_Recipes.size(), g_Combined.size()), MODULE_NAME, 0);
    if (g_Recipes.empty())
        return;

    DetourAttach(&(PVOID&)orig_Get_FusionMaterialCount, Hook_Get_FusionMaterialCount);
    DetourAttach(&(PVOID&)orig_Get_FusionMaterial, Hook_Get_FusionMaterial);
    DetourAttach(&(PVOID&)orig_Get_FusionsUsingMaterial, Hook_Get_FusionsUsingMaterial);
    if (g_NeedsMatcherHook)
        DetourAttach(&(PVOID&)orig_MatchesMaterialCode, Hook_MatchesMaterialCode);
}
