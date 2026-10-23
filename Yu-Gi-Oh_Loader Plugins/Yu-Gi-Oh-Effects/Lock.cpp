#include <Windows.h>
#include <detours.h>

#include <cstdint>
#include <format>
#include <string>
#include <unordered_map>

#include <json.hpp>

#include "Lock.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    enum class Side { Both, Opponent, You };
    // Where the card must be face-up. Auto (no "zone" given): from the card itself - a Spell or Trap in a Spell & Trap Zone, a monster in a
    // Monster Zone - so a script never has to say it.
    enum class Zone { Auto, Monster, SpellTrap, Any };
    const auto CardType = reinterpret_cast<int(__fastcall*)(int)>(0x14081A650);   // Get_CardTypeFromFullCardPropsByKonamiId: 13 Spell, 14 Trap

    // Is a card with this rule counted in this zone (0-6 Monster Zones, 7-11 Spell & Trap Zones)?
    bool InItsZone(Zone where, uint16_t engineId, int zone)
    {
        const bool inMonster = zone <= 6;
        if (where == Zone::Auto)
        {
            const int type = CardType(engineId);
            where = type == 13 || type == 14 ? Zone::SpellTrap : Zone::Monster;
        }
        return where == Zone::Any || (where == Zone::Monster) == inMonster;
    }

    // The narrow monster locks: one lock-state int each, read by Duel_IsMonsterEffectNegatedByLocks (0x1401103A0) for every monster on the
    // field, whoever controls it. The value is the lock card's activation number (INTLID), as the game's own cards write it.
    struct MonsterFilter { const char* Name; int Slots[2]; const char* What; };
    constexpr MonsterFilter kFilters[] = {
        { "summoned_this_turn",      { 9, 10 }, "monsters Special Summoned this turn (Lose 1 Turn)" },   // UNK_03 & 4
        { "a_counter",               { 13, -1 }, "monsters with an A-Counter (Alien Mars)" },            // counter 6800
        { "light",                   { 14, -1 }, "LIGHT monsters (Brain Golem)" },
        { "except_koaki_meiru",      { 15, -1 }, "monsters except Koa'ki Meiru (Hydro Barrier)" },       // archetype 70
        { "from_main_deck",          { 16, -1 }, "monsters Special Summoned from the Main Deck (Corridor of Agony)" },   // UNK_02 & 0x80000
        { "from_extra_deck",         { 17, -1 }, "monsters Special Summoned from the Extra Deck (Nekroz of Unicore)" },  // UNK_02 & 0x200000
        { "fusion_synchro_xyz",      { 19, -1 }, "Fusion, Synchro and Xyz monsters (Amorphactor Pain)" },
        { "extra_monster_zone",      { 20, -1 }, "monsters in the Extra Monster Zones (Beatraptor)" },   // zone > 4
        { "except_tribute_summoned", { 21, -1 }, "monsters except Tribute Summoned ones (The Monarchs Erupt)" },   // UNK_02 & 8
        { "defense_position",        { 26, -1 }, "Defense Position monsters (Gorgon's Eye)" },           // Pos != 0
    };

    struct LockRule
    {
        bool Traps = true;
        bool Spells = false;
        bool Monsters = false;          // monster effects on the field negated (Skill Drain's slots)
        bool ContinuousTraps = false;   // these four: can't be activated, by either player (one slot each, no side)
        bool ContinuousSpells = false;
        bool FieldSpells = false;
        bool EquipSpells = false;
        Side Who = Side::Both;
        Zone Where = Zone::Auto;
        const MonsterFilter* Filter = nullptr;   // "monsters" only for these (kFilters); null = every monster of the side
    };

    std::unordered_map<uint16_t, LockRule> g_Locks;   // real card id (custom, or an overridden game card) -> its lock

    // Duel_BuildContinuousLockState (0x14010EAA0): (lock state). sub_14010E0E0 (lock, player, zone, 1) = the card's effects are negated.
    using Build_t = int64_t(__fastcall*)(int32_t*);
    Build_t orig_Build = reinterpret_cast<Build_t>(0x14010EAA0);
    const auto IsNegated = reinterpret_cast<int(__fastcall*)(int32_t*, int, int, int)>(0x14010E0E0);
    const auto CardIdAtZone = reinterpret_cast<uint32_t(__fastcall*)(int, int, int)>(0x140047FF0);   // YGO::Duel::Get_CardIdAtZone

    // Duel::PlayerState (0x143497C40), 3476 bytes per player; Duel::InPlay_Card[12] from +0x4C (zones 0-6 monster, 7-11 Spell/Trap), 24 bytes:
    // +0 CARDID, +4 INTLID, +0xC Pos (2 / 8 face-down), +0x10 UNK_02, +0x14 UNK_03.
    constexpr uintptr_t kPlayerState = 0x143497C40, kPlayerStride = 3476, kZones = 0x4C, kZoneSize = 24;

    const uint8_t* ZoneRecord(int player, int zone)
    {
        return reinterpret_cast<const uint8_t*>(kPlayerState + kPlayerStride * (player & 1) + kZones + kZoneSize * zone);
    }

    // In a duel a custom card may play under a borrowed vanilla id (Yu-Gi-Oh-MoreCards): this is the custom card it stands for, 0 = none.
    uint16_t RealId(uint16_t id)
    {
        using RealId_t = unsigned short(__cdecl*)(unsigned short);
        static RealId_t real = [] {
            HMODULE cards = GetModuleHandleA("Yu-Gi-Oh-MoreCards.dll");
            return cards ? reinterpret_cast<RealId_t>(GetProcAddress(cards, "Card_GetRealIdForBorrowed")) : nullptr;
        }();
        const uint16_t mapped = real ? real(id) : 0;
        return mapped ? mapped : id;
    }

    // The same conditions the game checks before its id switch: a card, face-up, not marked disabled (UNK_02 | UNK_03 >> 4 bit 2), effects
    // not negated.
    bool Active(int32_t* state, int player, int zone)
    {
        const uint8_t* record = ZoneRecord(player, zone);
        const uint32_t cardId = *reinterpret_cast<const uint32_t*>(record) & 0x3FFF;
        const uint32_t position = *reinterpret_cast<const uint32_t*>(record + 0xC);
        const uint32_t flags2 = *reinterpret_cast<const uint32_t*>(record + 0x10), flags3 = *reinterpret_cast<const uint32_t*>(record + 0x14);
        if (!cardId || position == 2 || position == 8 || ((flags2 | (flags3 >> 4)) & 2) || (flags3 & 0x400))
            return false;
        return IsNegated(state, player, zone, 1) == 0;
    }

    void Apply(int32_t* state, int controller, int activation, const LockRule& rule)
    {
        if (rule.Filter)
            for (int slot : rule.Filter->Slots)
                if (slot >= 0 && (state[slot] == 0 || state[slot] > activation))
                    state[slot] = activation;
        for (int player = 0; player < 2; ++player)
        {
            const bool hit = rule.Who == Side::Both || (rule.Who == Side::Opponent) == (player != controller);
            if (!hit)
                continue;
            if (rule.Traps)
                state[player] = 1;          // +0 / +4: all Traps negated, can't be activated
            if (rule.Spells)
                state[3 + player] = 1;      // +12 / +16: all Spells
            if (rule.Monsters && !rule.Filter)
            {
                state[5 + 2 * player] = 1;  // +20 / +28 and +24 / +32: the player's monster effects (sub_1401103A0 reads both)
                state[6 + 2 * player] = 1;
            }
        }
        if (rule.ContinuousTraps) state[22] = 1;    // +88   (sub_140111690, the can't-activate check)
        if (rule.ContinuousSpells) state[23] = 1;   // +92
        if (rule.FieldSpells) state[24] = 1;        // +96
        if (rule.EquipSpells) state[25] = 1;        // +100
    }

    int64_t __fastcall Hook_Build(int32_t* state)
    {
        const int64_t result = orig_Build(state);
        for (int player = 0; player < 2; ++player)
            for (int zone = 0; zone < 12; ++zone)
            {
                const uint16_t id = static_cast<uint16_t>(CardIdAtZone(player, zone, 1) & 0x3FFF);
                if (!id)
                    continue;
                auto it = g_Locks.find(RealId(id));
                if (it == g_Locks.end())
                    continue;
                const LockRule& rule = it->second;
                if (!InItsZone(rule.Where, id, zone))
                    continue;
                if (Active(state, player, zone))
                {
                    const int32_t activation = *reinterpret_cast<const int32_t*>(ZoneRecord(player, zone) + 4);   // INTLID
                    Apply(state, player, activation > 0 ? activation : 1, rule);
                }
            }
        return result;
    }

    bool Parse(const nlohmann::json& j, LockRule& rule, std::string& error)
    {
        if (!j.is_object())
        {
            error = "\"lock\" must be an object";
            return false;
        }
        rule.Traps = j.value("traps", true);
        rule.Spells = j.value("spells", false);
        rule.Monsters = j.value("monsters", false);
        rule.ContinuousTraps = j.value("continuousTraps", false);
        rule.ContinuousSpells = j.value("continuousSpells", false);
        rule.FieldSpells = j.value("fieldSpells", false);
        rule.EquipSpells = j.value("equipSpells", false);
        if (j.contains("monsterFilter"))
        {
            const std::string name = j["monsterFilter"].is_string() ? j["monsterFilter"].get<std::string>() : "";
            for (const MonsterFilter& filter : kFilters)
                if (name == filter.Name)
                    rule.Filter = &filter;
            if (!rule.Filter)
            {
                error = std::format("unknown monsterFilter \"{}\"", name);
                return false;
            }
            rule.Monsters = true;
        }
        const std::string side = j.value("side", std::string("both"));
        const std::string zone = j.value("zone", std::string("auto"));
        if (side == "both") rule.Who = Side::Both;
        else if (side == "opponent") rule.Who = Side::Opponent;
        else if (side == "you") rule.Who = Side::You;
        else { error = std::format("unknown side \"{}\" (both, opponent, you)", side); return false; }
        if (zone == "auto") rule.Where = Zone::Auto;
        else if (zone == "monster") rule.Where = Zone::Monster;
        else if (zone == "spelltrap") rule.Where = Zone::SpellTrap;
        else if (zone == "any") rule.Where = Zone::Any;
        else { error = std::format("unknown zone \"{}\" (monster, spelltrap, any)", zone); return false; }
        if (rule.Filter && rule.Who != Side::Both)
        {
            error = "a monsterFilter lock affects both players' monsters (the game has one slot for it), so side must be both";
            return false;
        }
        if (!rule.Traps && !rule.Spells && !rule.Monsters && !rule.ContinuousTraps && !rule.ContinuousSpells && !rule.FieldSpells && !rule.EquipSpells)
        {
            error = "locks nothing";
            return false;
        }
        return true;
    }

    // ---- hand size limit ("handLimit": {"limit": N, "side": "opponent" | "you" | "both", "zone": "monster" | "spelltrap" | "any"}) ----
    // Duel_GetHandLimit (0x14005D120, player) is the End Phase limit EndPhase_DiscardToHandLimit (0x140155830) discards down to: 6, none
    // with Infinite Cards, 5 with the opponent's Enervating Mist, 3 with Finite Cards. A custom card face-up in its zone, its effects not
    // negated, lowers it for the side it names (the lowest limit wins). Default side: the opponent ("your opponent's hand size limit is 3").
    struct HandRule { int Limit = 6; Side Who = Side::Opponent; Zone Where = Zone::Auto; };
    std::unordered_map<uint16_t, HandRule> g_HandLimits;

    using HandLimit_t = uint32_t(__fastcall*)(uint32_t);
    HandLimit_t orig_HandLimit = reinterpret_cast<HandLimit_t>(0x14005D120);

    uint32_t __fastcall Hook_HandLimit(uint32_t player)
    {
        uint32_t limit = orig_HandLimit(player);
        int32_t state[64]{};   // the lock state, so a card whose effects are negated (Skill Drain ...) doesn't count
        bool built = false;
        for (int controller = 0; controller < 2; ++controller)
            for (int zone = 0; zone < 12; ++zone)
            {
                const uint16_t id = static_cast<uint16_t>(CardIdAtZone(controller, zone, 1) & 0x3FFF);
                if (!id)
                    continue;
                auto it = g_HandLimits.find(RealId(id));
                if (it == g_HandLimits.end())
                    continue;
                const HandRule& rule = it->second;
                if (!InItsZone(rule.Where, id, zone))
                    continue;
                const bool own = (player & 1) == static_cast<uint32_t>(controller);
                if ((rule.Who == Side::Opponent && own) || (rule.Who == Side::You && !own))
                    continue;
                if (!built)
                {
                    Hook_Build(state);   // the game's locks and ours
                    built = true;
                }
                if (Active(state, controller, zone) && static_cast<uint32_t>(rule.Limit) < limit)
                    limit = static_cast<uint32_t>(rule.Limit);
            }
        return limit;
    }

    bool ParseHandLimit(const nlohmann::json& j, HandRule& rule, std::string& error)
    {
        if (!j.is_object() || !j.contains("limit") || !j["limit"].is_number_integer())
        {
            error = "\"handLimit\" needs an integer \"limit\"";
            return false;
        }
        rule.Limit = j["limit"].get<int>();
        if (rule.Limit < 0 || rule.Limit > 60)
        {
            error = "\"handLimit\".limit must be 0..60";
            return false;
        }
        const std::string side = j.value("side", std::string("opponent"));
        const std::string zone = j.value("zone", std::string("auto"));
        if (side == "both") rule.Who = Side::Both;
        else if (side == "opponent") rule.Who = Side::Opponent;
        else if (side == "you") rule.Who = Side::You;
        else { error = std::format("unknown side \"{}\" (both, opponent, you)", side); return false; }
        if (zone == "auto") rule.Where = Zone::Auto;
        else if (zone == "monster") rule.Where = Zone::Monster;
        else if (zone == "spelltrap") rule.Where = Zone::SpellTrap;
        else if (zone == "any") rule.Where = Zone::Any;
        else { error = std::format("unknown zone \"{}\" (monster, spelltrap, any)", zone); return false; }
        return true;
    }

    // "key" on the card, or inside its compiled effect (the WolfX script's statement compiles there).
    const nlohmann::json* Continuous(const nlohmann::json& entry, const char* key)
    {
        if (entry.contains(key))
            return &entry[key];
        if (entry.contains("effectClone") && entry["effectClone"].is_object() && entry["effectClone"].contains(key))
            return &entry["effectClone"][key];
        return nullptr;
    }

    void LoadHandLimit(const char* file, const nlohmann::json& entry, bool needsOverridden)
    {
        const nlohmann::json* json = Continuous(entry, "handLimit");
        if (!json || (needsOverridden && !entry.contains("handLimit") && !entry.value("overridden", false)))
            return;
        HandRule rule;
        std::string error;
        const int id = entry["id"].get<int>();
        if (!ParseHandLimit(*json, rule, error))
        {
            Logger::WriteLog(std::format("{} card {}: \"handLimit\" skipped: {}", file, id, error), MODULE_NAME, 2);
            return;
        }
        g_HandLimits[static_cast<uint16_t>(id)] = rule;
        Logger::WriteLog(std::format("{} card {}: hand size limit {} for {} while in {}", file, id, rule.Limit,
            rule.Who == Side::Both ? "both players" : rule.Who == Side::Opponent ? "the opponent" : "its controller",
            rule.Where == Zone::Auto ? "its own zone" : rule.Where == Zone::Monster ? "a Monster Zone" : rule.Where == Zone::SpellTrap ? "a Spell/Trap Zone" : "any zone"), MODULE_NAME, 0);
    }

    void Load(const char* file, bool needsOverridden)
    {
        const auto shared = YGO::Mods::ReadMergedShared(file, "cards");   // the same parse EffectClone / SummonJson use
        const nlohmann::json& root = *shared;
        if (!root.is_object() || !root.contains("cards") || !root["cards"].is_array())
            return;
        for (const auto& entry : root["cards"])
        {
            if (!entry.is_object() || !entry.contains("id") || !entry["id"].is_number_integer())
                continue;
            LoadHandLimit(file, entry, needsOverridden);
            // "lock" on the card, or inside its compiled effect (the WolfX script's lock(...) statement compiles there)
            const nlohmann::json* lockJson = entry.contains("lock") ? &entry["lock"]
                : entry.contains("effectClone") && entry["effectClone"].is_object() && entry["effectClone"].contains("lock") ? &entry["effectClone"]["lock"] : nullptr;
            if (!lockJson)
                continue;
            // effects.json: a lock in the compiled effect counts once the override is on; a "lock" key of its own counts on its own
            if (needsOverridden && !entry.contains("lock") && !entry.value("overridden", false))
                continue;
            LockRule rule;
            std::string error;
            const int id = entry["id"].get<int>();
            if (!Parse(*lockJson, rule, error))
            {
                Logger::WriteLog(std::format("{} card {}: \"lock\" skipped: {}", file, id, error), MODULE_NAME, 2);
                continue;
            }
            g_Locks[static_cast<uint16_t>(id)] = rule;
            std::string what;
            for (auto [on, name] : { std::pair{ rule.Traps, "Traps" }, { rule.Spells, "Spells" }, { rule.Monsters && !rule.Filter, "monster effects" }, { rule.Filter != nullptr, rule.Filter ? rule.Filter->What : "" },
                                     { rule.ContinuousTraps, "Continuous Traps" }, { rule.ContinuousSpells, "Continuous Spells" },
                                     { rule.FieldSpells, "Field Spells" }, { rule.EquipSpells, "Equip Spells" } })
                if (on)
                    what += (what.empty() ? "" : ", ") + std::string(name);
            Logger::WriteLog(std::format("{} card {}: lock {} for {} while in {}", file, id, what,
                rule.Who == Side::Both ? "both players" : rule.Who == Side::Opponent ? "the opponent" : "its controller",
                rule.Where == Zone::Auto ? "its own zone" : rule.Where == Zone::Monster ? "a Monster Zone" : rule.Where == Zone::SpellTrap ? "a Spell/Trap Zone" : "any zone"), MODULE_NAME, 0);
        }
    }
}

void Lock::Setup()
{
    Load("cards.json", false);
    Load("effects.json", true);
    if (!g_HandLimits.empty())
    {
        DetourAttach(&(PVOID&)orig_HandLimit, Hook_HandLimit);
        Logger::WriteLog(std::format("{} card(s) with a hand size limit: Duel_GetHandLimit hooked", g_HandLimits.size()), MODULE_NAME, 0);
    }
    if (g_Locks.empty())
        return;
    DetourAttach(&(PVOID&)orig_Build, Hook_Build);
    Logger::WriteLog(std::format("{} card(s) with a continuous lock: Duel_BuildContinuousLockState hooked", g_Locks.size()), MODULE_NAME, 0);
}
