#include <Windows.h>
#include <detours.h>

#include <climits>
#include <cstdint>
#include <format>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include <json.hpp>

#include "Lock.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"
#include "YuGiOh/YuGiOh-DUELSTATE.h"

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

    // ---- continuous ATK / DEF changes ("aura": [{"atk": N, "def": N, "side": "you" | "opponent" | "both" | "self", "race", "attribute", "archetype"}]) ----
    // Calc_EffectiveAtkDef_Modifiers (0x1400307C0, player, zone, out, 0, 0) writes a monster's current ATK at out+4 and DEF at out+8 (int), after
    // the game's own modifiers, the ATK/DEF swap and the clamp to 0 .. 99,999,999 (epilogue 0x1400382FC). Every face-up custom card with an aura,
    // its effects not negated (the lock state, as the hand limit), adds its amounts to each face-up monster the aura covers; the result is clamped
    // the same way (docs/EffectSystem.md section 69).
    // ---- conditions on a continuous rule ("while": [tests], all must hold) and "for each" amounts ("per": test) ----
    // A test counts, for the rule card's controller ("side" you = its controller): face-up cards on the field (zones 0-12; "other" leaves the
    // rule card out), cards in the GY / hand / banished (the PlayerState piles), the rule card's Xyz materials, its controller's LP, or 1 while
    // the rule card is in Defense Position. It holds while min <= count <= max (docs/EffectSystem.md section 89).
    enum class Count { Field, Grave, Hand, Banished, Materials, LifePoints, Defense };
    struct CountTest
    {
        Count What = Count::Field;
        Side Who = Side::You;
        int Min = 1, Max = INT_MAX;
        bool Other = false;
        int Kind = 0;   // 0 any card, 1 monster, 2 Spell, 3 Trap
        int Race = 0, Attribute = 0, Archetype = 0;
        uint16_t Name = 0;
    };

    enum class AuraSide { You, Opponent, Both, Self };
    struct AuraRule
    {
        int Atk = 0, Def = 0;
        AuraSide Who = AuraSide::You;
        int Race = 0, Attribute = 0, Archetype = 0;
        std::vector<CountTest> While;   // "while": the rule applies only while every test holds
        bool HasPer = false;            // "per": the amounts are multiplied by this count ("gains 300 ATK for each ...")
        CountTest Per;
    };
    std::unordered_map<uint16_t, std::vector<AuraRule>> g_Auras;

    using Calc_t = uint64_t(__fastcall*)(unsigned int, int, int16_t*, int, int);
    Calc_t orig_Calc = reinterpret_cast<Calc_t>(0x1400307C0);
    const auto EffectiveRace = reinterpret_cast<int(__fastcall*)(unsigned int, int, int)>(0x14003B460);       // YGO::DUEL::Get_EffectiveRace (player, zone, -2)
    const auto AttributeMask = reinterpret_cast<unsigned int(__fastcall*)(unsigned int, int)>(0x14003CAC0);  // YGO::DUEL::Get_AttributeMask: 1 << attribute
    const auto CardCodeAtZone = reinterpret_cast<uint16_t(__fastcall*)(int, int)>(0x140047060);              // YGO::Duel::Get_CardCodeAtZone
    const auto InArchetype = reinterpret_cast<int(__fastcall*)(uint16_t, int)>(0x1407EB9E0);                 // Is_CardInNamedArchetype_Thunk
    bool g_InAura = false;   // the lock state / filters may ask ATK themselves

    bool FaceUpMonster(int player, int zone)
    {
        const uint8_t* record = ZoneRecord(player, zone);
        const uint32_t position = *reinterpret_cast<const uint32_t*>(record + 0xC);
        return (*reinterpret_cast<const uint32_t*>(record) & 0x3FFF) != 0 && position != 2 && position != 8;
    }

    // Field cards are tested as the game sees them now (Get_EffectiveRace, Get_AttributeMask, Get_CardCodeAtZone), pile cards by their printed
    // data (FULL_CARD_PROPS by id). Piles: Duel::PlayerState::Player counts at +0x0C hand / +0x14 GY / +0x1C banished, packed card ids at
    // +0x19C / +0x7B4 / +0xA0C (the zone scanners ScanGeneric_* read them so). Materials: Count_XyzMaterialsAtZone (0x140041190), the count
    // Cond_CanDetachXyzMaterials takes for the card's own materials.
    const auto MonsterType = reinterpret_cast<int(__fastcall*)(uint16_t)>(0x14081A7B0);         // YGO::CARDS::Get_MonsterType (printed race)
    const auto PrintedAttribute = reinterpret_cast<int(__fastcall*)(uint16_t)>(0x14081A5D0);    // YGO::CARDS::Get_AttributeFromFullCardProps
    const auto IsSameName = reinterpret_cast<int64_t(__fastcall*)(int, int)>(0x1407EBA00);      // j_Card_IsSameName
    const auto XyzMaterials = reinterpret_cast<int(__fastcall*)(int, int)>(0x140041190);        // Count_XyzMaterialsAtZone (player, zone)

    bool KindMatches(int kind, uint16_t id)
    {
        if (!kind)
            return true;
        const int type = CardType(id);
        return kind == 2 ? type == 13 : kind == 3 ? type == 14 : type != 13 && type != 14;
    }

    bool PileCardMatches(const CountTest& t, uint16_t id)
    {
        return id && KindMatches(t.Kind, id)
            && (!t.Race || MonsterType(id) == t.Race)
            && (!t.Attribute || PrintedAttribute(id) == t.Attribute)
            && (!t.Archetype || InArchetype(id, t.Archetype))
            && (!t.Name || IsSameName(id, t.Name));
    }

    bool FieldCardMatches(const CountTest& t, int player, int zone)
    {
        const uint16_t id = static_cast<uint16_t>(CardIdAtZone(player, zone, 1) & 0x3FFF);
        if (!id || !FaceUpMonster(player, zone) || !KindMatches(t.Kind, id))
            return false;
        const bool monsterZone = zone <= 6;
        if (t.Race && (monsterZone ? EffectiveRace(static_cast<unsigned int>(player), zone, -2) & 0xFF : MonsterType(id)) != t.Race) return false;
        if (t.Attribute && !(monsterZone ? AttributeMask(static_cast<unsigned int>(player), zone) & (1u << t.Attribute) : PrintedAttribute(id) == t.Attribute)) return false;
        if (t.Archetype && !InArchetype(CardCodeAtZone(player, zone), t.Archetype)) return false;
        if (t.Name && !IsSameName(CardCodeAtZone(player, zone), t.Name)) return false;
        return true;
    }

    int PileCount(const CountTest& t, int player)
    {
        const uintptr_t block = kPlayerState + kPlayerStride * (player & 1);
        const auto [countAt, cardsAt] = t.What == Count::Hand ? std::pair{ 0x0C, 0x19C } : t.What == Count::Grave ? std::pair{ 0x14, 0x7B4 } : std::pair{ 0x1C, 0xA0C };
        const int cards = *reinterpret_cast<const int*>(block + countAt);
        int hits = 0;
        for (int i = 0; i < cards && i < 226; ++i)
            if (PileCardMatches(t, static_cast<uint16_t>(*reinterpret_cast<const uint32_t*>(block + cardsAt + 4 * i) & 0x3FFF)))
                ++hits;
        return hits;
    }

    // The test's count for the rule card at controller / zone.
    int CountFor(const CountTest& t, int controller, int zone)
    {
        switch (t.What)
        {
        case Count::Materials: return XyzMaterials(controller, zone);
        case Count::LifePoints: return YGO::DUELSTATE::LifePoints(controller);
        case Count::Defense: return zone <= 6 && (*reinterpret_cast<const uint32_t*>(ZoneRecord(controller, zone) + 0xC) & 0xC) ? 1 : 0;   // 4 / 8 Defense Position
        default: break;
        }
        int hits = 0;
        for (int player = 0; player < 2; ++player)
        {
            if ((t.Who == Side::You && player != controller) || (t.Who == Side::Opponent && player == controller))
                continue;
            if (t.What != Count::Field)
            {
                hits += PileCount(t, player);
                continue;
            }
            for (int z = 0; z <= 12; ++z)
                if (!(t.Other && player == controller && z == zone) && FieldCardMatches(t, player, z))
                    ++hits;
        }
        return hits;
    }

    bool Holds(const std::vector<CountTest>& tests, int controller, int zone)
    {
        for (const CountTest& t : tests)
        {
            const int n = CountFor(t, controller, zone);
            if (n < t.Min || n > t.Max)
                return false;
        }
        return true;
    }

    constexpr std::pair<const char*, int> kRaces[] = { {"Dragon",1},{"Zombie",2},{"Fiend",3},{"Pyro",4},{"SeaSerpent",5},{"Rock",6},{"Machine",7},
        {"Fish",8},{"Dinosaur",9},{"Insect",10},{"Beast",11},{"BeastWarrior",12},{"Plant",13},{"Aqua",14},{"Warrior",15},{"WingedBeast",16},
        {"Fairy",17},{"Spellcaster",18},{"Thunder",19},{"Reptile",20},{"Psychic",21},{"Wyrm",22},{"Cyberse",23},{"DivineBeast",24},{"CreatorGod",25} };
    constexpr std::pair<const char*, int> kAttributes[] = { {"Light",1},{"Dark",2},{"Water",3},{"Fire",4},{"Earth",5},{"Wind",6},{"Divine",7} };

    template <size_t N>
    int Lookup(const std::string& v, const std::pair<const char*, int> (&table)[N])
    {
        for (const auto& [n, k] : table)
            if (_stricmp(n, v.c_str()) == 0)
                return k;
        return 0;
    }

    // {"count": "field" | "grave" | "hand" | "banished" | "materials" | "lp" | "defense", "side": "you" | "opponent" | "both", "min": N, "max": N,
    // "other": true, "kind": "card" | "monster" | "spell" | "trap", "race", "attribute", "archetype", "name": id}
    bool ParseCount(const nlohmann::json& j, CountTest& t, std::string& error)
    {
        if (!j.is_object()) { error = "a \"while\" / \"per\" test is an object"; return false; }
        static const std::pair<const char*, Count> whats[] = { {"field", Count::Field}, {"grave", Count::Grave}, {"hand", Count::Hand},
            {"banished", Count::Banished}, {"materials", Count::Materials}, {"lp", Count::LifePoints}, {"defense", Count::Defense} };
        const std::string what = j.value("count", std::string("field"));
        bool known = false;
        for (const auto& [n, w] : whats)
            if (what == n) { t.What = w; known = true; }
        if (!known) { error = std::format("unknown count \"{}\" (field, grave, hand, banished, materials, lp, defense)", what); return false; }
        const std::string side = j.value("side", std::string("you"));
        if (side == "you") t.Who = Side::You;
        else if (side == "opponent") t.Who = Side::Opponent;
        else if (side == "both") t.Who = Side::Both;
        else { error = std::format("unknown side \"{}\" (you, opponent, both)", side); return false; }
        t.Min = j.value("min", t.What == Count::LifePoints ? 0 : 1);
        t.Max = j.value("max", INT_MAX);
        t.Other = j.value("other", false);
        const std::string kind = j.value("kind", std::string("card"));
        t.Kind = kind == "monster" ? 1 : kind == "spell" ? 2 : kind == "trap" ? 3 : 0;
        if (!t.Kind && kind != "card") { error = std::format("unknown kind \"{}\" (card, monster, spell, trap)", kind); return false; }
        if (j.contains("race") && !(t.Race = Lookup(j.value("race", std::string()), kRaces))) { error = "unknown race"; return false; }
        if (j.contains("attribute") && !(t.Attribute = Lookup(j.value("attribute", std::string()), kAttributes))) { error = "unknown attribute"; return false; }
        t.Archetype = j.value("archetype", 0);
        t.Name = static_cast<uint16_t>(j.value("name", 0));
        return true;
    }

    bool ParseTests(const nlohmann::json& j, std::vector<CountTest>& tests, std::string& error)
    {
        for (const auto& w : j.is_array() ? j : nlohmann::json::array({ j }))
        {
            CountTest t;
            if (!ParseCount(w, t, error))
                return false;
            tests.push_back(t);
        }
        return true;
    }

    bool AuraCovers(const AuraRule& rule, int auraPlayer, int auraZone, int player, int zone)
    {
        switch (rule.Who)
        {
        case AuraSide::Self: if (auraPlayer != player || auraZone != zone) return false; break;
        case AuraSide::You: if (auraPlayer != player) return false; break;
        case AuraSide::Opponent: if (auraPlayer == player) return false; break;
        case AuraSide::Both: break;
        }
        // -2 sets 0x100 when a field card changed the race (Get_EffectiveRace)
        if (rule.Race && (EffectiveRace(static_cast<unsigned int>(player), zone, -2) & 0xFF) != rule.Race) return false;
        if (rule.Attribute && !(AttributeMask(static_cast<unsigned int>(player), zone) & (1u << rule.Attribute))) return false;
        if (rule.Archetype && !InArchetype(CardCodeAtZone(player, zone), rule.Archetype)) return false;
        return true;
    }

    uint64_t __fastcall Hook_Calc(unsigned int player, int zone, int16_t* out, int a4, int a5)
    {
        const uint64_t result = orig_Calc(player, zone, out, a4, a5);
        if (g_InAura || !out || zone < 0 || zone > 6 || !FaceUpMonster(static_cast<int>(player & 1), zone))
            return result;
        g_InAura = true;
        int32_t state[64]{};
        bool built = false;
        int atk = 0, def = 0;
        for (int controller = 0; controller < 2; ++controller)
            for (int z = 0; z < 12; ++z)
            {
                const uint16_t id = static_cast<uint16_t>(CardIdAtZone(controller, z, 1) & 0x3FFF);
                if (!id)
                    continue;
                auto it = g_Auras.find(RealId(id));
                if (it == g_Auras.end() || !InItsZone(Zone::Auto, id, z))
                    continue;
                for (const AuraRule& rule : it->second)
                {
                    if (!AuraCovers(rule, controller, z, static_cast<int>(player & 1), zone))
                        continue;
                    if (!built)
                    {
                        Hook_Build(state);   // the game's locks and ours: a negated aura card does not count
                        built = true;
                    }
                    if (!Active(state, controller, z))
                        break;
                    if (!Holds(rule.While, controller, z))
                        continue;
                    const int times = rule.HasPer ? CountFor(rule.Per, controller, z) : 1;
                    atk += rule.Atk * times;
                    def += rule.Def * times;
                }
            }
        g_InAura = false;
        if (atk || def)
        {
            auto clamp = [](int v) { return v < 0 ? 0 : v > 99999999 ? 99999999 : v; };
            int* stats = reinterpret_cast<int*>(reinterpret_cast<uint8_t*>(out) + 4);
            stats[0] = clamp(stats[0] + atk);
            stats[1] = clamp(stats[1] + def);
        }
        return result;
    }

    bool ParseAura(const nlohmann::json& j, AuraRule& rule, std::string& error)
    {
        if (!j.is_object()) { error = "an aura is an object"; return false; }
        rule.Atk = j.value("atk", 0);
        rule.Def = j.value("def", 0);
        const std::string side = j.value("side", std::string("you"));
        if (side == "you") rule.Who = AuraSide::You;
        else if (side == "opponent") rule.Who = AuraSide::Opponent;
        else if (side == "both") rule.Who = AuraSide::Both;
        else if (side == "self") rule.Who = AuraSide::Self;
        else { error = std::format("unknown side \"{}\" (you, opponent, both, self)", side); return false; }
        if (j.contains("race") && !(rule.Race = Lookup(j.value("race", std::string()), kRaces))) { error = "unknown race"; return false; }
        if (j.contains("attribute") && !(rule.Attribute = Lookup(j.value("attribute", std::string()), kAttributes))) { error = "unknown attribute"; return false; }
        rule.Archetype = j.value("archetype", 0);
        if (j.contains("while") && !ParseTests(j["while"], rule.While, error))
            return false;
        if (j.contains("per"))
        {
            if (!ParseCount(j["per"], rule.Per, error))
                return false;
            rule.HasPer = true;
        }
        return true;
    }

    // ---- protection ("protect": [{"from": "battle" | "effect" | "both" | "target" | "attack" | "spells" | "traps" | "monsters" (or a list),
    // "by": "any" | "opponent", "side", "race", "attribute", "archetype"}]) ----
    // Cannot be destroyed by battle: Battle_ResolveResult (0x1400795C0) flags the losing BattleSide (+0x23) and clears the flag when
    // Battle_CanDestroyByBattle (0x140074430, isAttacker, apply, a3, side, other) returns 0 - the game's own list (g_BattleIndestructibleIds:
    // Marshmallon, Spirit Reaper ...) answers there. By card effects: Card_Destroy (0x1400D0F50) skips a destruction when Destroy_CanEffectDestroy
    // (0x1400295A0, player, zone, srcIsOpponent, srcCardId, srcZone, flag, mode 0) returns 0. Cannot be targeted by card effects: every target
    // picker asks Effect_CanTargetCard (0x140023AF0, effect, player, zone; source player = (Player ^ SideXor) & 1). Cannot be targeted for attacks:
    // Check_CanAttackTarget (0x140141DE0, attacker player / zone, target player / zone, mode, outFlag), also behind Count_AttackableOpponentMonsters.
    // Unaffected by Spell / Trap / monster effects: Zone_GetImmunityMasks (0x140022C70, player, zone, monsterMask, spellMask, trapMask, skipOwn)
    // feeds Zone_IsUnaffectedBySpells / ByTraps / ByMonsters (bit side + 1; monster bits 2/4/6 + side by the source's frame). A face-up custom card
    // with a protect rule, its effects not negated (the lock state, as the aura), protects every face-up card the rule covers (side / Type /
    // Attribute / archetype, the aura's filter) - "self" is "this card ..." (docs/EffectSystem.md sections 71-72).
    enum ProtectKind { kBattle = 1, kEffect = 2, kTarget = 4, kAttack = 8, kSpells = 16, kTraps = 32, kMonsters = 64 };
    struct ProtectRule { int Kinds = 0; bool OpponentOnly = false; AuraRule Cover; };
    std::unordered_map<uint16_t, std::vector<ProtectRule>> g_Protects;
    bool g_InProtect = false;   // the lock-state build / filters may ask immunity themselves

    struct BattleSide { uint8_t Player, Zone; };   // g_BattleSides (0x14349C494), 40 bytes each; only the head is read here
    using BattleDestroy_t = BOOL(__fastcall*)(BOOL, BOOL, int, BattleSide*, BattleSide*);
    BattleDestroy_t orig_BattleDestroy = reinterpret_cast<BattleDestroy_t>(0x140074430);
    using EffectDestroy_t = BOOL(__fastcall*)(int16_t, int, int, int16_t, int16_t, int, int);
    EffectDestroy_t orig_EffectDestroy = reinterpret_cast<EffectDestroy_t>(0x1400295A0);
    using CanTarget_t = BOOL(__fastcall*)(const uint16_t*, unsigned int, int);
    CanTarget_t orig_CanTarget = reinterpret_cast<CanTarget_t>(0x140023AF0);
    using CanAttack_t = BOOL(__fastcall*)(unsigned int, int, unsigned int, int, int, BOOL*);
    CanAttack_t orig_CanAttack = reinterpret_cast<CanAttack_t>(0x140141DE0);
    using Immunity_t = char(__fastcall*)(unsigned int, int, int*, int*, int*, int);
    Immunity_t orig_Immunity = reinterpret_cast<Immunity_t>(0x140022C70);
    int g_ProtectKinds = 0;   // every kind some card protects from: a hook only installs for those

    // Is the face-up card at player/zone protected from this kind (opponent = the source is the other player's; "by": "opponent" limits the
    // effect kinds, not battle or attacks)?
    bool ScanProtectors(int player, int zone, int kind, bool opponent)
    {
        int32_t state[64]{};
        bool built = false;
        for (int controller = 0; controller < 2; ++controller)
            for (int z = 0; z < 12; ++z)
            {
                const uint16_t id = static_cast<uint16_t>(CardIdAtZone(controller, z, 1) & 0x3FFF);
                if (!id)
                    continue;
                auto it = g_Protects.find(RealId(id));
                if (it == g_Protects.end() || !InItsZone(Zone::Auto, id, z))
                    continue;
                for (const ProtectRule& rule : it->second)
                {
                    const bool byOpponentMatters = kind != kBattle && kind != kAttack;
                    if (!(rule.Kinds & kind) || (byOpponentMatters && rule.OpponentOnly && !opponent))
                        continue;
                    if (!AuraCovers(rule.Cover, controller, z, player, zone))
                        continue;
                    if (!built)
                    {
                        Hook_Build(state);   // a negated protector (Skill Drain ...) does not count
                        built = true;
                    }
                    if (Active(state, controller, z) && Holds(rule.Cover.While, controller, z))
                        return true;
                }
            }
        return false;
    }

    bool Protected(int player, int zone, int kind, bool opponent)
    {
        if (g_InProtect || !(g_ProtectKinds & kind) || zone < 0 || zone > 11 || !FaceUpMonster(player, zone))
            return false;
        g_InProtect = true;
        const bool hit = ScanProtectors(player, zone, kind, opponent);
        g_InProtect = false;
        return hit;
    }

    BOOL __fastcall Hook_BattleDestroy(BOOL isAttacker, BOOL apply, int a3, BattleSide* side, BattleSide* other)
    {
        if (side && Protected(side->Player & 1, side->Zone, kBattle, false))
            return FALSE;
        return orig_BattleDestroy(isAttacker, apply, a3, side, other);
    }

    BOOL __fastcall Hook_EffectDestroy(int16_t player, int zone, int srcIsOpponent, int16_t srcCardId, int16_t srcZone, int flag, int mode)
    {
        if (mode == 0 && Protected(player & 1, zone, kEffect, srcIsOpponent != 0))
            return FALSE;
        return orig_EffectDestroy(player, zone, srcIsOpponent, srcCardId, srcZone, flag, mode);
    }

    // effect: EffectRecord (word 1 Player, word 5 SideXor)
    BOOL __fastcall Hook_CanTarget(const uint16_t* effect, unsigned int player, int zone)
    {
        if (effect && Protected(static_cast<int>(player & 1), zone, kTarget, ((effect[1] ^ effect[5]) & 1) != (player & 1)))
            return FALSE;
        return orig_CanTarget(effect, player, zone);
    }

    BOOL __fastcall Hook_CanAttack(unsigned int attackerPlayer, int attackerZone, unsigned int targetPlayer, int targetZone, int mode, BOOL* outFlag)
    {
        if (Protected(static_cast<int>(targetPlayer & 1), targetZone, kAttack, true))
        {
            if (outFlag)
                *outFlag = FALSE;
            return FALSE;
        }
        return orig_CanAttack(attackerPlayer, attackerZone, targetPlayer, targetZone, mode, outFlag);
    }

    // Unaffected by Spell / Trap / monster effects: OR the side bits into the game's masks (bit 1 own, bit 2 the opponent's; monster bits
    // 2/4/6 + side for the three source frames).
    char __fastcall Hook_Immunity(unsigned int player, int zone, int* monsterMask, int* spellMask, int* trapMask, int skipOwn)
    {
        const char result = orig_Immunity(player, zone, monsterMask, spellMask, trapMask, skipOwn);
        const int p = static_cast<int>(player & 1);
        auto add = [&](int* mask, int kind, int own, int opponent) {
            if (!mask)
                return;
            if (Protected(p, zone, kind, true)) *mask |= opponent;
            if (Protected(p, zone, kind, false)) *mask |= own;
        };
        add(spellMask, kSpells, 1, 2);
        add(trapMask, kTraps, 1, 2);
        add(monsterMask, kMonsters, 0x54, 0xA8);
        return result;
    }

    bool ParseProtect(const nlohmann::json& j, ProtectRule& rule, std::string& error)
    {
        if (!j.is_object()) { error = "a protect rule is an object"; return false; }
        static const std::pair<const char*, int> kinds[] = { {"battle", kBattle}, {"effect", kEffect}, {"both", kBattle | kEffect}, {"target", kTarget},
            {"attack", kAttack}, {"spells", kSpells}, {"traps", kTraps}, {"monsters", kMonsters} };
        const nlohmann::json from = j.value("from", nlohmann::json("both"));
        for (const auto& f : from.is_array() ? from : nlohmann::json::array({ from }))
        {
            const std::string word = f.is_string() ? f.get<std::string>() : std::string();
            int kind = 0;
            for (const auto& [name, k] : kinds)
                if (word == name)
                    kind = k;
            if (!kind) { error = std::format("unknown from \"{}\" (battle, effect, both, target, attack, spells, traps, monsters)", word); return false; }
            rule.Kinds |= kind;
        }
        const std::string by = j.value("by", std::string("any"));
        if (by == "opponent") rule.OpponentOnly = true;
        else if (by != "any") { error = std::format("unknown by \"{}\" (any, opponent)", by); return false; }
        nlohmann::json cover = j;
        if (!cover.contains("side"))
            cover["side"] = "self";   // "this card cannot be destroyed ..."
        return ParseAura(cover, rule.Cover, error);
    }

    // ---- this card's name becomes another card's ("nameAs": {"id": N, "where": "field" | "grave"}) ----
    // Get_CardCodeAtZone (0x140047060, player, zone) is the code every name / archetype test of a field card uses; for a face-up card that is not
    // disabled (UNK_02 | UNK_03 >> 4 bit 2, UNK_02 >= 0) it maps the id through the game's table g_FieldNameAsTable (0x140ACFE80, 54 {id, code}:
    // Proto-Cyber Dragon -> Cyber Dragon ...). Get_ProxyCardId (0x1400DB180, id) is the id ladder the game uses wherever a card is looked up by
    // id (GY / Deck scans, fusion materials): its Harpie Queen and Dupe Frog say "while on the field or in the GY" and are listed there.
    // "field" answers the first only; "grave" (field and GY) both (docs/EffectSystem.md section 73).
    struct NameRule { uint16_t Code = 0; bool Everywhere = true; };
    std::unordered_map<uint16_t, NameRule> g_NameAs;

    using CardCode_t = uint64_t(__fastcall*)(int, int);
    CardCode_t orig_CardCode = reinterpret_cast<CardCode_t>(0x140047060);
    using ProxyId_t = int(__fastcall*)(int);
    ProxyId_t orig_ProxyId = reinterpret_cast<ProxyId_t>(0x1400DB180);

    uint64_t __fastcall Hook_CardCode(int player, int zone)
    {
        if (zone >= 0 && zone <= 12)   // 12 = the Field Zone (Duel::PlayerState POne_FieldSpell)
        {
            const uint16_t id = static_cast<uint16_t>(CardIdAtZone(player, zone, 1) & 0x3FFF);
            if (auto it = id ? g_NameAs.find(RealId(id)) : g_NameAs.end(); it != g_NameAs.end() && FaceUpMonster(player, zone))
            {
                const uint8_t* record = ZoneRecord(player, zone);
                const uint32_t flags2 = *reinterpret_cast<const uint32_t*>(record + 0x10), flags3 = *reinterpret_cast<const uint32_t*>(record + 0x14);
                if (!((flags2 | (flags3 >> 4)) & 2) && static_cast<int32_t>(flags2) >= 0)   // the game's own test for its table
                    return it->second.Code;
            }
        }
        return orig_CardCode(player, zone);
    }

    int __fastcall Hook_ProxyId(int id)
    {
        if (auto it = g_NameAs.find(RealId(static_cast<uint16_t>(id))); it != g_NameAs.end() && it->second.Everywhere)
            return it->second.Code;
        return orig_ProxyId(id);
    }

    // ---- "You can only control 1 <this card>" ("onlyOne": "you" | "field") ----
    // Card_GetOnlyOneKind (0x1407448C0, id) is the game's uniqueness rule: 1 = g_OnlyOneControlIds (the player may not summon / activate it while
    // they control one face-up), 2 = g_OnlyOneOnFieldIds (while either player does), 3 = archetype-wide, read by Card_MayPlay_UniqueAndDeclared
    // (0x14005AAE0) through Count_FaceUpSameNameOnField (docs/EffectSystem.md section 75).
    std::unordered_map<uint16_t, int> g_OnlyOne;
    using OnlyOne_t = int(__fastcall*)(int);
    OnlyOne_t orig_OnlyOne = reinterpret_cast<OnlyOne_t>(0x1407448C0);

    int __fastcall Hook_OnlyOne(int id)
    {
        if (auto it = g_OnlyOne.find(RealId(static_cast<uint16_t>(id))); it != g_OnlyOne.end())
            return it->second;
        return orig_OnlyOne(id);
    }

    // ---- "This card can attack directly" ("attackDirectly": true) ----
    // Attack_GetDirectAttackRule (0x140144DF0, player, zone, ignoreId): 0 = only when the opponent has no monsters (or
    // Attack_DirectAttackNotPrevented 0x1401446E0 failed), 2 = cannot attack directly, 1 = can attack directly - the game's g_DirectAttackIds
    // monsters, face-up and not negated (UNK_03 & 0x400). A custom card answers 1 the same way, only where the game said 0 and nothing prevents
    // a direct attack (docs/EffectSystem.md section 76).
    std::unordered_map<uint16_t, std::vector<CountTest>> g_AttackDirect;   // card -> its "while" tests (none = always)
    using DirectRule_t = int(__fastcall*)(unsigned int, int, int);
    DirectRule_t orig_DirectRule = reinterpret_cast<DirectRule_t>(0x140144DF0);
    const auto DirectNotPrevented = reinterpret_cast<BOOL(__fastcall*)(unsigned int, int)>(0x1401446E0);

    int __fastcall Hook_DirectRule(unsigned int player, int zone, int ignoreId)
    {
        const int rule = orig_DirectRule(player, zone, ignoreId);
        if (rule != 0 || zone < 0 || zone > 6)
            return rule;
        const int p = static_cast<int>(player & 1);
        const uint16_t id = static_cast<uint16_t>(CardIdAtZone(p, zone, 1) & 0x3FFF);
        auto it = id && id != ignoreId ? g_AttackDirect.find(RealId(id)) : g_AttackDirect.end();
        if (it == g_AttackDirect.end() || !FaceUpMonster(p, zone))
            return rule;
        const uint8_t* record = ZoneRecord(p, zone);
        const uint32_t flags2 = *reinterpret_cast<const uint32_t*>(record + 0x10), flags3 = *reinterpret_cast<const uint32_t*>(record + 0x14);
        if (((flags2 | (flags3 >> 4)) & 2) || (flags3 & 0x400) || !Holds(it->second, p, zone))
            return rule;
        return DirectNotPrevented(player, zone) ? 1 : rule;
    }

    // ---- "You can also Xyz Summon this card by using 1 X you control" ("xyzAlso", docs/EffectSystem.md section 94) ----
    // Xyz_CanUseAsAltMaterial (0x14003FA70: player, Xyz id, side, zone) is the game's test for the one monster an alternative Xyz Summon uses
    // (Number S39: Utopia the Lightning, Constellar Ptolemy M7 ...): a per-id ladder over the monster at (side, zone) - Rank (Get_ZoneRank
    // 0x14003B2C0), archetype, name, kind - then Xyz_CanSummonWithMaterial (0x14003ED60) and the free-zone check (sub_14001A530). Every caller
    // (the material picker sub_140040C20, the summon list, the AI) asks it, and an id it does not list gets no. A custom card's rule answers it
    // the same way: its own material test, then the game's two checks.
    struct XyzAlsoRule { CountTest Material; bool XyzOnly = false; int RankMin = 0, RankMax = 99; };
    std::unordered_map<uint16_t, XyzAlsoRule> g_XyzAlso;
    using XyzAlt_t = BOOL(__fastcall*)(int, int, unsigned int, int);
    XyzAlt_t orig_XyzAlt = reinterpret_cast<XyzAlt_t>(0x14003FA70);
    const auto ZoneRank = reinterpret_cast<int(__fastcall*)(unsigned int, int)>(0x14003B2C0);
    const auto KindIsXyz = reinterpret_cast<BOOL(__fastcall*)(uint32_t)>(0x140742F80);
    const auto XyzCanSummonWith = reinterpret_cast<int64_t(__fastcall*)(int, int, unsigned int, int, int, int)>(0x14003ED60);
    const auto ZonesUsable = reinterpret_cast<int64_t(__fastcall*)(int, int, int, int, int, int, int)>(0x14001A530);

    BOOL __fastcall Hook_XyzAlt(int player, int xyzId, unsigned int side, int zone)
    {
        const BOOL game = orig_XyzAlt(player, xyzId, side, zone);
        if (game)
            return game;
        auto it = g_XyzAlso.find(RealId(static_cast<uint16_t>(xyzId)));
        if (it == g_XyzAlso.end() || zone < 0 || zone > 6 || (side & 1) != (static_cast<unsigned int>(player) & 1))
            return game;
        const XyzAlsoRule& rule = it->second;
        const int p = static_cast<int>(side & 1);
        if (!FieldCardMatches(rule.Material, p, zone))
            return FALSE;
        if (rule.XyzOnly && !KindIsXyz(CardIdAtZone(p, zone, 1) & 0x3FFF))
            return FALSE;
        const int rank = ZoneRank(side, zone);
        if (rank < rule.RankMin || rank > rule.RankMax)
            return FALSE;
        return XyzCanSummonWith(player, xyzId, side, zone, 0, 1) != 0 && ZonesUsable(player, 0, xyzId, 1 << (zone + 16 * p), 1, 0, 0) != 0;
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
            if (const nlohmann::json* auras = Continuous(entry, "aura"); auras && (!needsOverridden || entry.contains("aura") || entry.value("overridden", false)))
            {
                const int id = entry["id"].get<int>();
                for (const auto& a : auras->is_array() ? *auras : nlohmann::json::array({ *auras }))
                {
                    AuraRule rule;
                    std::string error;
                    if (!ParseAura(a, rule, error))
                    {
                        Logger::WriteLog(std::format("{} card {}: \"aura\" skipped: {}", file, id, error), MODULE_NAME, 2);
                        continue;
                    }
                    g_Auras[static_cast<uint16_t>(id)].push_back(rule);
                }
            }
            if (const nlohmann::json* nameAs = Continuous(entry, "nameAs"); nameAs && (!needsOverridden || entry.contains("nameAs") || entry.value("overridden", false)))
            {
                const int id = entry["id"].get<int>();
                const int code = nameAs->is_object() ? nameAs->value("id", 0) : 0;
                if (code <= 0 || code > 0x3FFF)
                    Logger::WriteLog(std::format("{} card {}: \"nameAs\" skipped: needs a card id", file, id), MODULE_NAME, 2);
                else
                    g_NameAs[static_cast<uint16_t>(id)] = { static_cast<uint16_t>(code), nameAs->value("where", std::string("grave")) != "field" };
            }
            // "attackDirectly": true, or {"while": [tests]}
            if (const nlohmann::json* direct = Continuous(entry, "attackDirectly"); direct && ((direct->is_boolean() && direct->get<bool>()) || direct->is_object())
                && (!needsOverridden || entry.contains("attackDirectly") || entry.value("overridden", false)))
            {
                std::vector<CountTest> tests;
                std::string error;
                if (direct->is_object() && direct->contains("while") && !ParseTests((*direct)["while"], tests, error))
                    Logger::WriteLog(std::format("{} card {}: \"attackDirectly\" skipped: {}", file, entry["id"].get<int>(), error), MODULE_NAME, 2);
                else
                    g_AttackDirect[static_cast<uint16_t>(entry["id"].get<int>())] = std::move(tests);
            }
            // "xyzAlso": {"kind", "race", "attribute", "archetype", "name" (the count test's keys), "xyz": true, "rankMin", "rankMax"}
            if (const nlohmann::json* xyzAlso = Continuous(entry, "xyzAlso"); xyzAlso && xyzAlso->is_object() && (!needsOverridden || entry.contains("xyzAlso") || entry.value("overridden", false)))
            {
                XyzAlsoRule rule;
                std::string error;
                nlohmann::json test = *xyzAlso;
                test["count"] = "field";
                if (!test.contains("kind"))
                    test["kind"] = "monster";
                if (!ParseCount(test, rule.Material, error))
                    Logger::WriteLog(std::format("{} card {}: \"xyzAlso\" skipped: {}", file, entry["id"].get<int>(), error), MODULE_NAME, 2);
                else
                {
                    rule.XyzOnly = xyzAlso->value("xyz", false);
                    rule.RankMin = xyzAlso->value("rankMin", 0);
                    rule.RankMax = xyzAlso->value("rankMax", 99);
                    g_XyzAlso[static_cast<uint16_t>(entry["id"].get<int>())] = rule;
                }
            }
            if (const nlohmann::json* onlyOne = Continuous(entry, "onlyOne"); onlyOne && onlyOne->is_string() && (!needsOverridden || entry.contains("onlyOne") || entry.value("overridden", false)))
                g_OnlyOne[static_cast<uint16_t>(entry["id"].get<int>())] = onlyOne->get<std::string>() == "field" ? 2 : 1;
            if (const nlohmann::json* protects = Continuous(entry, "protect"); protects && (!needsOverridden || entry.contains("protect") || entry.value("overridden", false)))
            {
                const int id = entry["id"].get<int>();
                for (const auto& p : protects->is_array() ? *protects : nlohmann::json::array({ *protects }))
                {
                    ProtectRule rule;
                    std::string error;
                    if (!ParseProtect(p, rule, error))
                    {
                        Logger::WriteLog(std::format("{} card {}: \"protect\" skipped: {}", file, id, error), MODULE_NAME, 2);
                        continue;
                    }
                    g_Protects[static_cast<uint16_t>(id)].push_back(rule);
                    g_ProtectKinds |= rule.Kinds;
                }
            }
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
    if (!g_Auras.empty())
    {
        DetourAttach(&(PVOID&)orig_Calc, Hook_Calc);
        Logger::WriteLog(std::format("{} card(s) with a continuous ATK/DEF change: Calc_EffectiveAtkDef_Modifiers hooked", g_Auras.size()), MODULE_NAME, 0);
    }
    if (!g_AttackDirect.empty())
    {
        DetourAttach(&(PVOID&)orig_DirectRule, Hook_DirectRule);
        Logger::WriteLog(std::format("{} card(s) that can attack directly: Attack_GetDirectAttackRule hooked", g_AttackDirect.size()), MODULE_NAME, 0);
    }
    if (!g_XyzAlso.empty())
    {
        DetourAttach(&(PVOID&)orig_XyzAlt, Hook_XyzAlt);
        Logger::WriteLog(std::format("{} card(s) with an alternative Xyz Summon: Xyz_CanUseAsAltMaterial hooked", g_XyzAlso.size()), MODULE_NAME, 0);
    }
    if (!g_OnlyOne.empty())
    {
        DetourAttach(&(PVOID&)orig_OnlyOne, Hook_OnlyOne);
        Logger::WriteLog(std::format("{} card(s) you can only control 1 of: Card_GetOnlyOneKind hooked", g_OnlyOne.size()), MODULE_NAME, 0);
    }
    if (!g_NameAs.empty())
    {
        DetourAttach(&(PVOID&)orig_CardCode, Hook_CardCode);
        DetourAttach(&(PVOID&)orig_ProxyId, Hook_ProxyId);
        Logger::WriteLog(std::format("{} card(s) whose name becomes another's: Get_CardCodeAtZone + Get_ProxyCardId hooked", g_NameAs.size()), MODULE_NAME, 0);
    }
    if (!g_Protects.empty())
    {
        std::string hooked;
        auto attach = [&](int kind, PVOID* orig, PVOID hook, const char* name) {
            if (!(g_ProtectKinds & kind))
                return;
            DetourAttach(orig, hook);
            hooked += (hooked.empty() ? "" : ", ") + std::string(name);
        };
        attach(kBattle, &(PVOID&)orig_BattleDestroy, Hook_BattleDestroy, "Battle_CanDestroyByBattle");
        attach(kEffect, &(PVOID&)orig_EffectDestroy, Hook_EffectDestroy, "Destroy_CanEffectDestroy");
        attach(kTarget, &(PVOID&)orig_CanTarget, Hook_CanTarget, "Effect_CanTargetCard");
        attach(kAttack, &(PVOID&)orig_CanAttack, Hook_CanAttack, "Check_CanAttackTarget");
        attach(kSpells | kTraps | kMonsters, &(PVOID&)orig_Immunity, Hook_Immunity, "Zone_GetImmunityMasks");
        Logger::WriteLog(std::format("{} card(s) with protection: {} hooked", g_Protects.size(), hooked), MODULE_NAME, 0);
    }
    if (g_Locks.empty())
        return;
    DetourAttach(&(PVOID&)orig_Build, Hook_Build);
    Logger::WriteLog(std::format("{} card(s) with a continuous lock: Duel_BuildContinuousLockState hooked", g_Locks.size()), MODULE_NAME, 0);
}
