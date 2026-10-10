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
    enum class Zone { Monster, SpellTrap, Any };

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
        Zone Where = Zone::Monster;
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

    void Apply(int32_t* state, int controller, const LockRule& rule)
    {
        for (int player = 0; player < 2; ++player)
        {
            const bool hit = rule.Who == Side::Both || (rule.Who == Side::Opponent) == (player != controller);
            if (!hit)
                continue;
            if (rule.Traps)
                state[player] = 1;          // +0 / +4: all Traps negated, can't be activated
            if (rule.Spells)
                state[3 + player] = 1;      // +12 / +16: all Spells
            if (rule.Monsters)
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
                const bool inMonster = zone <= 6;
                if ((rule.Where == Zone::Monster && !inMonster) || (rule.Where == Zone::SpellTrap && inMonster))
                    continue;
                if (Active(state, player, zone))
                    Apply(state, player, rule);
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
        const std::string side = j.value("side", std::string("both"));
        const std::string zone = j.value("zone", std::string("monster"));
        if (side == "both") rule.Who = Side::Both;
        else if (side == "opponent") rule.Who = Side::Opponent;
        else if (side == "you") rule.Who = Side::You;
        else { error = std::format("unknown side \"{}\" (both, opponent, you)", side); return false; }
        if (zone == "monster") rule.Where = Zone::Monster;
        else if (zone == "spelltrap") rule.Where = Zone::SpellTrap;
        else if (zone == "any") rule.Where = Zone::Any;
        else { error = std::format("unknown zone \"{}\" (monster, spelltrap, any)", zone); return false; }
        if (!rule.Traps && !rule.Spells && !rule.Monsters && !rule.ContinuousTraps && !rule.ContinuousSpells && !rule.FieldSpells && !rule.EquipSpells)
        {
            error = "locks nothing";
            return false;
        }
        return true;
    }

    void Load(const char* file, bool needsOverridden)
    {
        nlohmann::json root = YGO::Mods::ReadMerged(file, "cards");
        if (!root.is_object() || !root.contains("cards") || !root["cards"].is_array())
            return;
        for (const auto& entry : root["cards"])
        {
            if (!entry.is_object() || !entry.contains("id") || !entry["id"].is_number_integer())
                continue;
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
            for (auto [on, name] : { std::pair{ rule.Traps, "Traps" }, { rule.Spells, "Spells" }, { rule.Monsters, "monster effects" },
                                     { rule.ContinuousTraps, "Continuous Traps" }, { rule.ContinuousSpells, "Continuous Spells" },
                                     { rule.FieldSpells, "Field Spells" }, { rule.EquipSpells, "Equip Spells" } })
                if (on)
                    what += (what.empty() ? "" : ", ") + std::string(name);
            Logger::WriteLog(std::format("{} card {}: lock {} for {} while in {}", file, id, what,
                rule.Who == Side::Both ? "both players" : rule.Who == Side::Opponent ? "the opponent" : "its controller",
                rule.Where == Zone::Monster ? "a Monster Zone" : rule.Where == Zone::SpellTrap ? "a Spell/Trap Zone" : "any zone"), MODULE_NAME, 0);
        }
    }
}

void Lock::Setup()
{
    Load("cards.json", false);
    Load("effects.json", true);
    if (g_Locks.empty())
        return;
    DetourAttach(&(PVOID&)orig_Build, Hook_Build);
    Logger::WriteLog(std::format("{} card(s) with a continuous lock: Duel_BuildContinuousLockState hooked", g_Locks.size()), MODULE_NAME, 0);
}
