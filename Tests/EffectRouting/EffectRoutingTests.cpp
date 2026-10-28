// Exercise the production hooks with mocked vanilla lookups, without launching a duel.
#ifdef EFFECT_ROUTING_BASELINE
#include "../../../Toolkit-Setup/Pendulum-Routing-Baseline/EffectClone.cpp"
namespace { Clone* RouteByTrigger(Clone* clone, std::span<const char* const> triggers, int) { return RouteByTrigger(clone, triggers); } }
#else
#include "../../Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Effects/EffectClone.cpp"
#endif
#include "EffectSourceLending.h"
#include <iostream>
#include <stdexcept>

std::string Config::Get_WorkingDirectory() { return "."; }

namespace
{
    Row drawRow{4844}, summonRow{6784}, graveRow{7892}, genericRow{0};
    int lastSource = 0;
    bool provideGeneric = false;
    void Check(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
    uint64_t __fastcall Resolve(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        lastSource = effect[0];
        Check(g_Active && g_Active->From == lastSource, "Slot runs with the wrong part's parameters");
        return 0;
    }
    void* __fastcall Lookup(uint16_t* effect)
    {
        if (effect[4] == 0) return effect[0] == 4844 ? &drawRow : provideGeneric ? &genericRow : nullptr;
        if (effect[4] == 1 && effect[0] == 6784) return &summonRow;
        if (effect[4] == 2 && effect[0] == 7892) return &graveRow;
        return nullptr;
    }
    unsigned short __cdecl NoBorrow(unsigned short) { return 0; }
    int64_t __fastcall Insert(int kind, uint32_t ref, int16_t instance, uint32_t)
    {
        auto* records = reinterpret_cast<uint16_t*>(kind ? 0x14349BC80 : 0x14349B580);
        auto* count = reinterpret_cast<uint32_t*>(kind ? 0x14349C000 : 0x14349C020);
        for (uint32_t i = 0; i < *count; ++i)
            if (records[i * 28] == static_cast<uint16_t>(ref) && records[i * 28 + 11] == instance) return -1;
        uint16_t* record = records + (*count)++ * 28;
        memset(record, 0, 0x38);
        record[0] = static_cast<uint16_t>(ref);
        record[11] = instance;
        return 1;
    }
    int64_t __fastcall Offer(uint32_t ref, int16_t instance, uint32_t param)
    { return Hook_InsertOffer(0, ref, instance, param); }
    int64_t __fastcall MenuOffer(uint32_t ref, int16_t instance, void*, uint32_t param)
    { return Hook_InsertOffer(1, ref, instance, param); }
    Clone Make(int from, const char* trigger = "", int draw = -1)
    {
        Clone part;
        part.From = from;
        part.Trigger = trigger;
        part.Draw = draw;
        return part;
    }
    // The engine's use-limit machinery, transcribed from the IDB (2026-10-25) so the plugin's rows can be played through it without a duel:
    //   Check_CanActivate_Driver 0x1400E0C30 -> Timing_Check_Class1..4 (0x1400F78E0 ...): zone > 12 passes, else
    //     Count_FlagMarkersInZone(player, zone, 1000 + class) 0x140052450 < Get_UseLimitPerCopyCount 0x1401F64E0 (1 for a custom id);
    //   Timing_Check_Class5_9 / 6_10 / 7 / 8: !Has_FlagMarker(player, 13, 1000 + class - 4, card id) 0x140052670;
    //   Check_Activation_Cost_And_Register 0x1400E0DF0 -> Register_UseLimit_Marker 0x1400E0E50: class 1-4 adds (zone, key, 0) while the zone holds the
    //     card (UseLimit_Class1..4_RegisterZoneMarker*), 5-8 adds (13, key, card id) (UseLimit_Class5..8_RegisterMarker*);
    //   DuelMsg_EndOfTurn -> FlagMarkers_EndOfTurnSweep(1, 0) 0x14004C320: lifetime 1/2 markers with counter 0 (all of these) are removed;
    //   classes 9 / 10 (once per Duel): Timing_Check_Class5_9 / 6_10 test the same keys 1001 / 1002, UseLimit_Class9/10_RegisterDuelMarker*
    //   (0x1401F88B0 / 0x1401F8900) add them with lifetime 5, which the sweep never removes.
    struct UseLimitEngine
    {
        struct Marker { int Player, Zone, Key, Value, Lifetime; };
        std::vector<Marker> Markers;
        struct Card { uint16_t Id; int Player, Zone; };   // zone 0-12 field, 13 hand, 16 GY

        int Count(int player, int zone, int key) const
        {
            return static_cast<int>(std::count_if(Markers.begin(), Markers.end(), [&](const Marker& m) { return m.Player == player && m.Zone == zone && m.Key == key; }));
        }
        bool Has(int player, int zone, int key, int value) const
        {
            return std::any_of(Markers.begin(), Markers.end(), [&](const Marker& m) { return m.Player == player && m.Zone == zone && m.Key == key && m.Value == value; });
        }
        static int ClassOf(const Row* row) { return row->Extra[0] & 0xF; }
        // Get_UseLimitPerCopyCount, through the plugin's hook (1 unless the card has "usesPerTurn")
        static int PerCopyCount(const Card& card)
        {
            uint16_t record[24]{};
            record[0] = card.Id;
            return Hook_UseCount(record);
        }
        bool CanActivate(const Card& card, const Row* row) const
        {
            const int c = ClassOf(row);
            if (c >= 1 && c <= 4) return card.Zone > 12 || Count(card.Player, card.Zone, 1000 + c) < PerCopyCount(card);
            if (c >= 5 && c <= 8) return !Has(card.Player, 13, 1000 + c - 4, card.Id);
            if (c == 9 || c == 10) return !Has(card.Player, 13, 1000 + c - 8, card.Id);
            return true;
        }
        void Register(const Card& card, const Row* row)
        {
            const int c = ClassOf(row);
            if (c >= 1 && c <= 4 && card.Zone <= 12) Markers.push_back({ card.Player, card.Zone, 1000 + c, 0, 1 });
            if (c >= 5 && c <= 8) Markers.push_back({ card.Player, 13, 1000 + c - 4, card.Id, 1 });
            if (c == 9 || c == 10) Markers.push_back({ card.Player, 13, 1000 + c - 8, card.Id, 5 });
        }
        // Activate = offer check, then (cost, then) registration, as Chain_ActivateEffect_StateMachine 0x1400AD230 steps 2 / 3 do.
        bool Activate(const Card& card, const Row* row)
        {
            if (!CanActivate(card, row)) return false;
            Register(card, row);
            return true;
        }
        void EndOfTurn() { std::erase_if(Markers, [](const Marker& m) { return m.Lifetime <= 2; }); }
    };

    // The rows Yu-Gi-Oh-Effects builds for each effect of a card from its effectClone JSON (ParseEffect -> AssignLimitClasses -> WrappedRow).
    std::vector<Row*> RowsFor(const nlohmann::json& json, uint16_t id, Clone& clone)
    {
        Check(ParseEffect(json, clone, "test"), "Test effectClone did not parse");
        std::vector<Row*> rows;
        for (Clone* part : AllParts(clone))
            rows.push_back(WrappedRow(&drawRow, id, *part));
        return rows;
    }

    void TestUseLimitStateMachine()
    {
        using nlohmann::json;
        // the game's Get_UseLimitPerCopyCount answers 1 for every custom id (its 2s are vanilla ids); the hook's fall-through calls this
        orig_UseCount = [](const uint16_t*) { return 1; };
        // "Once per turn: draw 1" (per copy): each copy on the field once; off the field the engine does not count it; the next turn resets.
        {
            Clone clone;
            auto rows = RowsFor(json{{"from", 4844}, {"oncePerTurnPerCopy", true}}, 15310, clone);
            Check(UseLimitEngine::ClassOf(rows[0]) == 1, "Per-copy limit is not class 1");
            UseLimitEngine engine;
            UseLimitEngine::Card a{15310, 0, 1}, b{15310, 0, 2}, grave{15310, 0, 16};
            Check(engine.Activate(a, rows[0]), "Per copy: first copy refused");
            Check(!engine.Activate(a, rows[0]), "Per copy: the same copy was allowed twice");
            Check(engine.Activate(b, rows[0]), "Per copy: a second copy was refused");
            Check(engine.Activate(grave, rows[0]) && engine.Activate(grave, rows[0]), "Per copy: the engine counts a card off the field (it should not)");
            engine.EndOfTurn();
            Check(engine.Activate(a, rows[0]), "Per copy: not reset at the end of the turn");
        }
        // "You can only use this effect of X once per turn" (per name): one use for all copies, the opponent's copies apart.
        {
            Clone clone;
            auto rows = RowsFor(json{{"from", 4844}, {"oncePerTurn", true}}, 15311, clone);
            Check(UseLimitEngine::ClassOf(rows[0]) == 5, "Per-name limit is not class 5");
            UseLimitEngine engine;
            UseLimitEngine::Card a{15311, 0, 1}, b{15311, 0, 2}, theirs{15311, 1, 1};
            Check(engine.Activate(a, rows[0]), "Per name: first use refused");
            Check(!engine.Activate(b, rows[0]), "Per name: a second copy was allowed");
            Check(engine.Activate(theirs, rows[0]), "Per name: the opponent's copy was refused");
            engine.EndOfTurn();
            Check(engine.Activate(b, rows[0]), "Per name: not reset at the end of the turn");
        }
        // "Once per turn: activate 1 of these effects" + a separate per-name effect + a separate per-copy effect on one card.
        {
            Clone clone;
            const json bullet1{{"from", 4844}, {"oncePerTurnPerCopy", true}, {"limitGroup", 0}};
            json card = bullet1;
            card["parts"] = json::array({
                json{{"from", 4844}, {"oncePerTurnPerCopy", true}, {"limitGroup", 0}},   // bullet 2: same choice
                json{{"from", 4844}, {"oncePerTurn", true}},                            // its own per-name limit
                json{{"from", 4844}, {"oncePerTurnPerCopy", true}},                     // its own per-copy limit
                json{{"from", 4844}} });                                                // no limit
            auto rows = RowsFor(card, 15312, clone);
            Check(UseLimitEngine::ClassOf(rows[0]) == 1 && UseLimitEngine::ClassOf(rows[1]) == 1 && UseLimitEngine::ClassOf(rows[2]) == 5
                && UseLimitEngine::ClassOf(rows[3]) == 2 && UseLimitEngine::ClassOf(rows[4]) == 0, "Mixed card got the wrong classes");
            UseLimitEngine engine;
            UseLimitEngine::Card a{15312, 0, 3};
            Check(engine.Activate(a, rows[1]), "Bullet 2 refused");
            Check(!engine.Activate(a, rows[0]), "Bullet 1 allowed after bullet 2: the choice does not share its limit");
            Check(engine.Activate(a, rows[2]), "Per-name effect blocked by the per-copy choice (key 1001 in another list)");
            Check(engine.Activate(a, rows[3]), "Second per-copy effect blocked by the first");
            Check(!engine.Activate(a, rows[3]) && !engine.Activate(a, rows[2]), "A limited effect was allowed twice");
            Check(engine.Activate(a, rows[4]) && engine.Activate(a, rows[4]), "Unlimited effect was limited");
        }
        // "once per Duel" (class 9) beside a "once per turn" effect (moves to class 6: class 9 holds key 1001): the Duel marker survives the turn.
        {
            Clone clone;
            json card{{"from", 4844}, {"oncePerDuel", true}};
            card["parts"] = json::array({ json{{"from", 4844}, {"oncePerTurn", true}} });
            auto rows = RowsFor(card, 15313, clone);
            Check(UseLimitEngine::ClassOf(rows[0]) == 9 && UseLimitEngine::ClassOf(rows[1]) == 6, "Once per Duel / once per turn got the wrong classes");
            UseLimitEngine engine;
            UseLimitEngine::Card a{15313, 0, 1};
            Check(engine.Activate(a, rows[0]) && !engine.Activate(a, rows[0]), "Once per Duel: used twice in a turn");
            Check(engine.Activate(a, rows[1]), "Once per Duel blocked the once-per-turn effect");
            engine.EndOfTurn();
            Check(!engine.Activate(a, rows[0]), "Once per Duel: allowed again next turn");
            Check(engine.Activate(a, rows[1]), "Once per turn: not reset beside a once-per-Duel effect");
        }
        // "twice per turn" (usesPerTurn 2, per copy): Get_UseLimitPerCopyCount answers 2 through Hook_UseCount.
        {
            g_Clones.clear();
            Clone parsed;
            Check(ParseEffect(json{{"from", 4844}, {"usesPerTurn", 2}}, parsed, "test"), "usesPerTurn did not parse");
            AssignLimitClasses(parsed, "test");
            g_Clones.emplace(static_cast<uint16_t>(15314), parsed);
            Row* row = WrappedRow(&drawRow, 15314, g_Clones[15314]);
            Check(UseLimitEngine::ClassOf(row) == 1, "Twice per turn is not a per-copy class");
            UseLimitEngine engine;
            UseLimitEngine::Card a{15314, 0, 1};
            Check(engine.Activate(a, row) && engine.Activate(a, row), "Twice per turn: second use refused");
            Check(!engine.Activate(a, row), "Twice per turn: third use allowed");
            g_Clones.clear();
        }
    }

    // The Standby / End Phase drivers, transcribed from the IDB (Duel__Phase__EnterStandbyPhase 0x14014B110 case 10, Phase_EndPhase_RunHandlers
    // 0x1401558E0 cases 10-12): four passes; pass p sets the parity HIDWORD(field_37C2) = p & 1 and calls every row's handler(turn player, card id);
    // a handler that offers returns 0 and the step runs again next tick (handlers remember what they offered). The handler families gate on
    // a1 == turn ^ parity and look for the card on a1's field: 0x140148EB0 / 0x14014D9A0 (a1: "your" phase), 0x140148FA0 / 0x14014DAB0 (1 - a1:
    // "your opponent's"), 0x140148FB0 / 0x14014DAC0 (both: "each").
    struct PhaseWorld
    {
        int Turn = 0, Parity = 0;
        std::map<uint16_t, int> Controller;            // card id on the field -> controlling player
        std::set<std::pair<uint16_t, int>> Offered;    // (card id, turn) offered this phase
        int Family = 0;                                // 0 own, 1 opponent's, 2 each
    } g_World;
    int64_t OwnFamily(int a1, uint16_t id)
    {
        if (a1 != (g_World.Turn ^ g_World.Parity)) return 1;
        auto it = g_World.Controller.find(id);
        if (it == g_World.Controller.end() || it->second != a1 || g_World.Offered.contains({ id, g_World.Turn })) return 1;
        g_World.Offered.insert({ id, g_World.Turn });
        return 0;
    }
    int64_t __fastcall MockPhaseHandler(uint64_t a1, uint64_t id, uint64_t, uint64_t)
    {
        const int p = static_cast<int>(a1), q = 1 - p;
        const uint16_t card = static_cast<uint16_t>(id);
        switch (g_World.Family)
        {
        case 0: return OwnFamily(p, card);
        case 1: return OwnFamily(q, card);
        default: return OwnFamily(p, card) != 0 && OwnFamily(q, card) != 0;
        }
    }
    // One phase for the turn player: the four passes over the donor's row, re-run after every offer (bounded).
    void RunPhase(int turn, uint16_t donor)
    {
        g_World.Turn = turn;
        for (int guard = 0; guard < 16; ++guard)
        {
            bool offered = false;
            for (int pass = 0; pass < 4 && !offered; ++pass)
            {
                g_World.Parity = pass & 1;
                offered = PhaseThunk<0>(static_cast<uint64_t>(turn), donor, 0, 0) == 0;
            }
            if (!offered) return;
        }
        throw std::runtime_error("Phase never settled");
    }

    void TestPhaseDriverStateMachine()
    {
        constexpr uint16_t donor = 6608, custom = 15320;   // a custom monster controlled by player 0; the donor itself is not on the field
        g_PhaseHookCount = 1;
        g_PhaseHooks[0].Orig = &MockPhaseHandler;
        g_PhaseHooks[0].CustomBySource = { { donor, { custom } } };
        const char* names[] = { "your", "your opponent's", "each" };
        const bool expect[3][2] = { { true, false }, { false, true }, { true, true } };   // [family][turn player]
        for (int family = 0; family < 3; ++family)
        {
            for (int turn = 0; turn < 2; ++turn)
            {
                g_World = {};
                g_World.Family = family;
                g_World.Controller[custom] = 0;
                RunPhase(turn, donor);
                const bool got = g_World.Offered.contains({ custom, turn });
                if (got != expect[family][turn])
                    throw std::runtime_error(std::format("{} phase family: custom card {} offered on player {}'s turn", names[family], got ? "was" : "was not", turn));
            }
        }
        // the trigger names the compiler writes for each family fit the phase events (2 Standby, 6 End Phase) and nothing else
        for (const char* t : { "standby_phase", "opponent_standby_phase", "each_standby_phase" })
            Check(TriggerFitsEvent(t, 2) && !TriggerFitsEvent(t, 6) && !TriggerFitsEvent(t, 7), "Standby trigger fits the wrong events");
        for (const char* t : { "end_phase", "opponent_end_phase", "each_end_phase" })
            Check(TriggerFitsEvent(t, 6) && !TriggerFitsEvent(t, 2), "End Phase trigger fits the wrong events");
        g_PhaseHookCount = 0;
        g_PhaseHooks[0] = {};
    }

    // Own-trigger cards (no game card behind the trigger): the plugin's own row answers only its trigger's lookup, the action is composed onto
    // it, and its phase offer is a virtual row after the table's last row calling the handler family - played through the same driver.
    int64_t __fastcall LastRowHandler(uint64_t, uint64_t, uint64_t, uint64_t) { return 1; }   // the table's last row: its card is not on the field
    void TestOwnTriggerStateMachine()
    {
        using nlohmann::json;
        constexpr uint16_t custom = 15330, lastRowId = 14620;
        g_Clones.clear();
        Clone parsed;
        Check(ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "each_end_phase"}, {"effectOnly", true}}, parsed, "test"), "Own trigger did not parse");
        Check(parsed.OwnTrigger && parsed.ActionFrom == 4844, "Own trigger did not take its action from \"from\"");
        g_Clones.emplace(custom, std::move(parsed));
        uint16_t endPhase[0x20]{}, standby[0x20]{}, spell[0x20]{};
        endPhase[0] = standby[0] = spell[0] = custom;
        endPhase[4] = standby[4] = 1;
        endPhase[3] = 6; standby[3] = 2;
        Row* row = static_cast<Row*>(Hook_GetEntry(endPhase));
        Check(row && row->Id == custom && (row->Extra[0] & 0xF) == 0, "Own row missing for its own trigger's lookup");
        Check(row->Slot[0] && !row->Slot[3], "Own row did not compose the action's slots (or kept a cost)");
        reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(row->Slot[0])(endPhase, 0, 0, 0);
        Check(lastSource == 4844, "Own row did not resolve as its action card");
        Check(!Hook_GetEntry(standby) && !Hook_GetEntry(spell), "Own row answered a lookup its trigger does not make");
        uint16_t resolving[0x20]{};   // a table-1 lookup with no event (made while the activation / resolution runs): answered, as a donor row is
        resolving[0] = custom; resolving[4] = 1;
        Check(Hook_GetEntry(resolving) == row, "Own row refused an event-less lookup of its table");
        Check(YGO::Effects::SingleEffectSource(json{{"from", 4844}, {"ownTrigger", true}}, 3900, 14969) == 0, "Own-trigger card was lent its action card's id");

        const bool expect[3][2] = { { true, false }, { false, true }, { true, true } };
        for (int family = 0; family < 3; ++family)
            for (int turn = 0; turn < 2; ++turn)
            {
                g_PhaseHookCount = 1;
                g_PhaseHooks[0] = {};
                g_PhaseHooks[0].Orig = &LastRowHandler;
                g_PhaseHooks[0].AppendAfter = lastRowId;
                g_PhaseHooks[0].Virtual = { { reinterpret_cast<uintptr_t>(&MockPhaseHandler), custom } };
                g_World = {};
                g_World.Family = family;
                g_World.Controller[custom] = 0;
                RunPhase(turn, lastRowId);
                if (g_World.Offered.contains({ custom, turn }) != expect[family][turn])
                    throw std::runtime_error(std::format("Own phase row, family {}: wrong offer on player {}'s turn", family, turn));
                g_World = {};
                g_World.Family = family;
                g_World.Controller[custom] = 0;
                RunPhase(turn, 5406);   // another row id: the virtual rows run only after the last row
                Check(g_World.Offered.empty(), "Own phase rows ran after a row that is not the table's last");
            }
        g_PhaseHookCount = 0;
        g_PhaseHooks[0] = {};
        g_Clones.clear();
    }

    // Own ignition (section 96): YGO::Effects::Check_CanActivateKind3AtZone (0x140107930) sets word4 3 for a card on the field and asks
    // Has_Kind3EffectEntry (0x1400DFD70: the lookup with word2 = the zone, no event); the row it finds is offered by the game's own menu.
    void TestOwnIgnitionRow()
    {
        using nlohmann::json;
        constexpr uint16_t custom = 15331;
        g_Clones.clear();
        Clone parsed;
        Check(ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "ignition"}, {"effectOnly", true}}, parsed, "test"), "Own ignition did not parse");
        g_Clones.emplace(custom, std::move(parsed));
        uint16_t kind3[0x20]{}, monster[0x20]{}, hand[0x20]{}, summoned[0x20]{};
        kind3[0] = monster[0] = hand[0] = summoned[0] = custom;
        kind3[2] = 2; kind3[4] = 3;          // Has_Kind3EffectEntry(id, zone 2)
        monster[4] = 1; hand[4] = 2;
        summoned[4] = 3; summoned[3] = 7;    // a known event is never an ignition's
        Row* row = static_cast<Row*>(Hook_GetEntry(kind3));
        Check(row && row->Id == custom, "Own ignition row missing for the field ignition table");
        // the shape of a plain no-cost ignition row (Aquaactress Tetra 11916: {id, class, slot 0, -, slot 2, -, -}): no donor's slot-3 cost
        Check(row->Slot[0] && !row->Slot[3], "Own ignition row did not compose its action (or kept a cost)");
        reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(row->Slot[0])(kind3, 0, 0, 0);
        Check(lastSource == 4844, "Own ignition did not resolve as its action card");
        Check(!Hook_GetEntry(monster) && !Hook_GetEntry(hand) && !Hook_GetEntry(summoned), "Own ignition answered another table or an event");
        g_Clones.clear();

        // three ignitions joined with 'or' (issue #39's card): one select row in table 3, no slot 3 from any donor, one limit group
        Clone root, second, third;
        Check(ParseEffect(json{{"from", 4659}, {"ownTrigger", true}, {"trigger", "ignition"}, {"effectOnly", true}, {"limitGroup", 0}, {"oncePerTurn", true},
            {"parts", json::array({ json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "ignition"}, {"effectOnly", true}, {"limitGroup", 0}, {"oncePerTurn", true}},
                                    json{{"from", 4345}, {"ownTrigger", true}, {"trigger", "ignition"}, {"effectOnly", true}, {"limitGroup", 0}, {"oncePerTurn", true}} })}},
            root, "test"), "Own ignition group did not parse");
        g_Clones.emplace(custom, std::move(root));
        Clone* card = &g_Clones.at(custom);
        Check(Candidates(card, kind3).size() == 3, "The three own ignitions are not all candidates of the table-3 lookup");
        Row* choice = static_cast<Row*>(Hook_GetEntry(kind3));
        Check(choice && choice->Id == custom && choice->Slot[3] == reinterpret_cast<void*>(&ChoiceSlot3), "Own ignition group is not offered once with the select row");
        g_Clones.clear();
    }

    // Own move triggers: Hook_MoveEvaluator offers the card itself the way Eval_CardMovedTriggers does (0x1400A3765), after checking the move
    // reference (bits 0-8 instance, 10-14 from zone, 16 from field, 18 battle, 21-25 to zone) against the trigger and its moveCondition.
    std::vector<std::pair<uint32_t, int16_t>> g_Offers;
    int g_Evaluated = 0;
    uint32_t g_InstanceSeen = 0;
    int64_t __fastcall MockOffer(uint32_t ref, int16_t slot, uint32_t) { g_Offers.emplace_back(ref, slot); return 1; }
    void __fastcall MockEvaluator(int, uint32_t* ref, uint32_t, int)
    {
        ++g_Evaluated;
        g_InstanceSeen = *reinterpret_cast<uint32_t*>(kCardInstances + 8 * static_cast<uintptr_t>(*ref & 0x1FF));
    }
    void TestOwnMoveTriggers()
    {
        using nlohmann::json;
        constexpr uint16_t banishedCard = 15340, graveCard = 15341;
        g_Clones.clear();
        Clone a, b;
        Check(ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "banished"}, {"moveCondition", {{"from", json::array({"grave"})}}}}, a, "test"), "banished parse");
        Check(ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "sent_to_grave"}, {"moveCondition", {{"from", json::array({"field"})}, {"by", "battle"}}}}, b, "test"), "grave parse");
        g_Clones.emplace(banishedCard, std::move(a));
        g_Clones.emplace(graveCard, std::move(b));
        orig_OfferByRef = &MockOffer;
        orig_MoveEvaluator = &MockEvaluator;
        auto* instances = reinterpret_cast<uint32_t*>(kCardInstances);
        auto move = [&](uint16_t card, uint32_t index, uint32_t from, uint32_t to, bool field, bool battle, int event) {
            instances[2 * index] = card;
            g_Offers.clear(); g_Evaluated = 0;
            uint32_t ref = index | (from << 10) | (field ? 1u << 16 : 0) | (battle ? 1u << 18 : 0) | (to << 21);
            Hook_MoveEvaluator(event, &ref, 0, 0);
            Check(g_Evaluated == 1 && (g_InstanceSeen & 0x3FFF) == card, "Own move trigger card was shown as another card to the evaluator");
            return g_Offers;
        };
        auto offers = move(banishedCard, 5, 16, 17, false, false, 31);   // banished from the GY
        Check(offers.size() == 1 && offers[0].second == 5, "Banished from the GY: no offer");
        const uint32_t expected = banishedCard | ((17u | 0x40u | (((5u & 1) << 6 | 31u) << 9)) << 16);
        Check(offers[0].first == expected, "Own move offer reference differs from the evaluator's layout");
        Check(move(banishedCard, 5, 13, 17, false, false, 31).empty(), "Banished from the hand fired a from-GY trigger");
        Check(move(banishedCard, 5, 16, 16, false, false, 31).empty(), "Sent to the GY fired a banished trigger");
        Check(move(graveCard, 6, 2, 16, true, true, 33).size() == 1, "Destroyed by battle on the field: no offer");
        Check(move(graveCard, 6, 2, 16, true, false, 33).empty(), "Not by battle fired a by-battle trigger");
        Check(move(graveCard, 6, 13, 16, false, true, 32).empty(), "From the hand fired a from-field trigger");
        uint16_t lookup[0x20]{};
        lookup[0] = banishedCard; lookup[4] = 2; lookup[3] = 31;
        Check(Hook_GetEntry(lookup) != nullptr, "Own move row missing in table 2");
        lookup[4] = 1;
        Check(Hook_GetEntry(lookup) == nullptr, "Own move row answered table 1");
        g_Clones.clear();
    }

    // Own attack triggers (Hook_AttackEval): after the game's attack evaluator, offer event 18 for the attacker / the attacked player's monsters
    // / the attack target, with the evaluator's own param.
    std::vector<std::tuple<int, int, int, uint32_t>> g_ZoneOffers;
    std::map<std::pair<int, int>, uint16_t> g_FaceUp;   // (player, zone) -> card id
    int64_t __fastcall MockAttackEval(int) { return 7; }
    int64_t __fastcall MockOfferAtZone(int player, int zone, int event, uint32_t param) { g_ZoneOffers.emplace_back(player, zone, event, param); return 1; }
    BOOL __fastcall MockFaceUp(unsigned int player, int zone, int id) { auto it = g_FaceUp.find({ static_cast<int>(player), zone }); return it != g_FaceUp.end() && it->second == id; }
    void TestOwnAttackTriggers()
    {
        using nlohmann::json;
        g_Clones.clear();
        Clone attacker, guard, target;
        Check(ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "attack_declared"}}, attacker, "t") &&
              ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "opponent_attack_declared"}}, guard, "t") &&
              ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "attacked"}}, target, "t"), "attack parse");
        g_Clones.emplace(15350, std::move(attacker));
        g_Clones.emplace(15351, std::move(guard));
        g_Clones.emplace(15352, std::move(target));
        g_OwnAttackCards = { 15350, 15351, 15352 };
        orig_AttackEval = &MockAttackEval;
        Offer_EffectOfCardAtZone = &MockOfferAtZone;
        Zone_HoldsFaceUpCard = &MockFaceUp;
        auto* attack = reinterpret_cast<uint8_t*>(0x14349C480);
        memset(attack, 0, 16);
        attack[0] = 0; attack[1] = 1; attack[2] = 0;                           // player 0 attacks player 1's monster
        *reinterpret_cast<int16_t*>(attack + 8) = 2; *reinterpret_cast<int16_t*>(attack + 0xA) = 4;
        g_FaceUp = { { {0, 2}, 15350 }, { {1, 1}, 15351 }, { {1, 4}, 15352 } };
        const uint32_t param = AttackParam(attack, 0);
        Check(param == ((2u * ((2u & 0x1F) | (32u * ((1u & 1) | (2u * (4u & 0x1F)))))) ), "Attack param differs from the evaluator's packing");
        g_ZoneOffers.clear();
        Check(Hook_AttackEval(0) == 7, "Attack evaluator result changed");
        Check(g_ZoneOffers.size() == 3, "Own attack triggers: expected the attacker, the attacked player's monster and the target");
        Check(std::count(g_ZoneOffers.begin(), g_ZoneOffers.end(), std::make_tuple(0, 2, 18, param)) == 1, "Attacker not offered");
        Check(std::count(g_ZoneOffers.begin(), g_ZoneOffers.end(), std::make_tuple(1, 1, 18, param)) == 1, "Attacked player's monster not offered");
        Check(std::count(g_ZoneOffers.begin(), g_ZoneOffers.end(), std::make_tuple(1, 4, 18, param)) == 1, "Attack target not offered");
        attack[2] = 1;   // a direct attack: no target
        g_ZoneOffers.clear();
        Hook_AttackEval(0);
        Check(std::none_of(g_ZoneOffers.begin(), g_ZoneOffers.end(), [](auto& o) { return std::get<1>(o) == 4; }), "Direct attack offered the 'attacked' card");
        g_ZoneOffers.clear();
        Hook_AttackEval(1);
        Check(g_ZoneOffers.empty(), "Own attack triggers offered on a replay pass");
        g_OwnAttackCards.clear();
        g_Clones.clear();
    }

    // Own "this card is Normal / Special Summoned" triggers (section 99): offered from Hook_EventEvaluator with the evaluator's own ref layout
    // (Eval_EventTriggersCardEffect_ByCardId 0x1400E3951-0x1400E3986), gated on the zone's status byte, never evaluated as the action card.
    std::vector<std::tuple<uint32_t, int16_t, uint32_t>> g_RefOffers;
    int64_t __fastcall MockRefOffer(uint32_t ref, int16_t slot, uint32_t param) { g_RefOffers.emplace_back(ref, slot, param); return 1; }
    int g_EvaluatorRuns = 0;
    void __fastcall MockEventEvaluator(int) { ++g_EvaluatorRuns; }
    uint32_t __fastcall MockEventParam() { return 0x20005; }
    void TestOwnSummonTriggers()
    {
        using nlohmann::json;
        constexpr uint16_t custom = 15360;
        g_Clones.clear();
        Clone normal, special, both;
        Check(ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"effectOnly", true}, {"trigger", "normal_summoned"}}, normal, "t")
            && ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"effectOnly", true}, {"trigger", "special_summoned"},
                                {"summonCondition", json{{"from", json::array({"grave"})}}}}, special, "t"), "summon trigger parse");
        g_Clones.emplace(custom, std::move(normal));
        g_Clones.emplace(custom + 1, std::move(special));
        orig_OfferByRef = &MockRefOffer;
        orig_EventEvaluator = &MockEventEvaluator;
        g_SummonEventParam = &MockEventParam;
        auto* record = reinterpret_cast<uint8_t*>(0x14349C560);
        auto summon = [&](uint16_t id, int player, int zone, uint16_t fromZone, bool live) {
            memset(record, 0, 0x40);
            *reinterpret_cast<uint16_t*>(record + 2) = static_cast<uint16_t>(player);
            *reinterpret_cast<uint16_t*>(record + 4) = static_cast<uint16_t>(zone);
            *reinterpret_cast<uint16_t*>(record + 0x0A) = fromZone;
            *reinterpret_cast<uint32_t*>(record + 0x26) = 3;     // the record carries the id (+0xE)
            *reinterpret_cast<uint16_t*>(record + 0xE) = id;
            *reinterpret_cast<uint8_t*>(0x143497C40 + 0x53 + player * 0xD94 + 24 * zone) = live ? 1 : 0;
        };
        g_RefOffers.clear(); g_EvaluatorRuns = 0;
        summon(custom, 1, 2, 13, true);
        Hook_EventEvaluator(7);
        Check(g_RefOffers.size() == 1, "Normal Summon trigger not offered once");
        const uint32_t expected = custom | ((((1u << 6 | 7u) << 9) | 2u | 0x20u) << 16);
        Check(std::get<0>(g_RefOffers[0]) == expected && std::get<1>(g_RefOffers[0]) == 5 && std::get<2>(g_RefOffers[0]) == 0x20005, "Offer ref / slot / param differ from the evaluator's");
        Check(g_EvaluatorRuns == 0, "An own trigger was evaluated by the game's id branches");
        g_RefOffers.clear();
        Hook_EventEvaluator(9);
        Check(g_RefOffers.empty(), "Normal Summon trigger offered for a Special Summon");
        summon(custom, 1, 2, 13, false);
        Hook_EventEvaluator(7);
        Check(g_RefOffers.empty(), "Offered although the zone's status byte is clear (the evaluator's entry gate)");
        summon(custom + 1, 0, 3, 16, true);
        Hook_EventEvaluator(9);
        Check(g_RefOffers.size() == 1, "Special Summon from the GY not offered");
        g_RefOffers.clear();
        summon(custom + 1, 0, 3, 13, true);
        Hook_EventEvaluator(9);
        Check(g_RefOffers.empty(), "summonCondition (from the GY) ignored");
        // the offered lookup (table 1, the event) finds the own row with the action composed onto it
        uint16_t lookup[0x20]{};
        lookup[0] = custom; lookup[2] = 2; lookup[3] = 7; lookup[4] = 1;
        Row* row = static_cast<Row*>(Hook_GetEntry(lookup));
        Check(row && row->Id == custom && row->Slot[0] && !row->Slot[3], "Own summon trigger row missing or kept a cost");
        lookup[3] = 9;
        Check(!Hook_GetEntry(lookup), "Own Normal Summon row answered a Special Summon lookup");
        g_Clones.clear();
    }

    // Own summon-reaction triggers (Hook_SummonReaction): after the game's pass, offered through the named-card helper to the right player(s).
    std::vector<std::tuple<int, int, int, uint32_t>> g_NamedOffers;
    void __fastcall MockSummonReaction(int) {}
    uint32_t __fastcall MockSummonParam() { return 0x1234; }
    int64_t __fastcall MockOfferNamed(int player, int id, int event, uint32_t param, int, int) { g_NamedOffers.emplace_back(player, id, event, param); return 1; }
    int MockIsMonster(uint16_t) { return 1; }
    void TestOwnSummonReactions()
    {
        using nlohmann::json;
        g_Clones.clear();
        Clone special, any, both;
        Check(ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "opponent_special_summoned"}}, special, "t") &&
              ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "opponent_summoned"}}, any, "t") &&
              ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "monster_special_summoned"}}, both, "t"), "reaction parse");
        g_Clones.emplace(15360, std::move(special));
        g_Clones.emplace(15361, std::move(any));
        g_Clones.emplace(15362, std::move(both));
        g_OwnSummonReactionCards = { 15360, 15361, 15362 };
        orig_SummonReaction = &MockSummonReaction;
        Get_SummonEventParam = &MockSummonParam;
        Offer_EffectOfNamedCardOnField = &MockOfferNamed;
        IsMonsterCategory = &MockIsMonster;
        *reinterpret_cast<uint16_t*>(0x14349C560) = 0;   // EventRecord.word0: player 0 summoned
        auto count = [](int player, int id, int event) {
            return std::count(g_NamedOffers.begin(), g_NamedOffers.end(), std::make_tuple(player, id, event, 0x1234u)); };
        g_NamedOffers.clear();
        Hook_SummonReaction(9);
        Check(count(1, 15360, 9) == 1 && count(1, 15361, 9) == 1, "Special Summon: the summoner's opponent was not offered");
        Check(count(0, 15362, 9) == 1 && count(1, 15362, 9) == 1, "Any Special Summon: not offered to both players");
        Check(g_NamedOffers.size() == 4, "Special Summon: an extra offer");
        g_NamedOffers.clear();
        Hook_SummonReaction(7);
        Check(g_NamedOffers.size() == 1 && count(1, 15361, 7) == 1, "Normal Summon: only 'Normal or Special Summons' reacts");
        g_NamedOffers.clear();
        Hook_SummonReaction(10);
        Check(g_NamedOffers.empty(), "A Set made a summon-reaction offer");
        g_OwnSummonReactionCards.clear();
        g_Clones.clear();
    }

    // Own watcher triggers (docs/EffectSystem.md section 91): after Eval_LeaveFieldTriggers (leave reference, event 22 destroyed / 30 sent by
    // Card_Destroy / 34 other leave) and Eval_CardMovedTriggers (move reference), each watcher is offered by name - from the field, or from the GY
    // with inGrave - to the player(s) whose side / zone / battle / cause conditions the moved card meets.
    // Target verbs (docs/EffectSystem.md section 92): "targetDo" puts the game's own target machine in slot 0,
    // the targeting slots of a target move, and the filter row carries the verb's own tests (Filter_CardAtZoneMatchesFlags bits).
    void TestTargetVerbs()
    {
        using nlohmann::json;
        auto parsed = [](const char* verb, json filter) {
            Clone c;
            filter["target"] = true;
            Check(ParseEffect(json{{"rawRow", true}, {"filter", filter}, {"targetDo", verb}}, c, "t"), "targetDo parse");
            Check(c.RawRow && c.SlotOverride[1] == 0x1401B0FF0 && c.SlotOverride[4] == 0x14008CCC0, "targetDo: targeting slots");
            return c;
        };
        Clone position = parsed("position", json{{"side", "opponent"}, {"position", "attack"}, {"canChangePosition", true}});
        const uint32_t positionFlags = *reinterpret_cast<const uint32_t*>(&position.FilterRow[4]);
        Check(position.SlotOverride[0] == 0x14016B430, "position: not the game's change-position machine");
        Check(positionFlags == (0x1410u | 2u | 0x80u | 0x80000u), "position: filter flags (opponent, targetable, Attack Position, can change)");
        Clone defense = parsed("position", json{{"position", "defense"}, {"canChangePosition", true}});
        Check((*reinterpret_cast<const uint32_t*>(&defense.FilterRow[4]) & 0x180) == 0x100, "position: Defense Position test");
        Clone faceDown = parsed("faceDown", json{{"canChangePosition", true}});
        Check(faceDown.SlotOverride[0] == 0x14049BE90, "faceDown: not the game's face-down machine");
        Clone control = parsed("control", json{{"side", "opponent"}, {"canChangeControl", true}});
        const uint32_t controlFlags = *reinterpret_cast<const uint32_t*>(&control.FilterRow[4]);
        Check(control.SlotOverride[0] == 0x140177090 && (controlFlags & 0x20000) && !(controlFlags & 0x80000), "control: machine / filter");
        Clone atk;
        Check(ParseEffect(json{{"rawRow", true}, {"filter", {{"target", true}, {"faceUp", true}}}, {"targetDo", "atk"}, {"amount", -1500}}, atk, "t"), "atk parse");
        Check(atk.SlotOverride[0] == reinterpret_cast<uint64_t>(&Slot0_OwnTargetResult) && atk.ResultKey == 1040 && atk.ResultValue == -1500, "atk: result key 1040 / amount");
        Clone negate = parsed("negate", json{{"faceUp", true}});
        Check(negate.SlotOverride[0] == reinterpret_cast<uint64_t>(&Slot0_OwnTargetResult) && negate.ResultKey == 1027 && negate.ResultValue == 0, "negate: result key 1027");
        Clone bad;
        Check(!ParseEffect(json{{"rawRow", true}, {"targetDo", "atk"}}, bad, "t"), "targetDo: atk without an amount parsed");
        Check(!ParseEffect(json{{"rawRow", true}, {"targetDo", "swap"}}, bad, "t"), "targetDo: an unknown verb parsed");
        Check(!ParseEffect(json{{"rawRow", true}, {"filter", {{"position", "sideways"}}}, {"targetDo", "position"}}, bad, "t"), "targetDo: an unknown position parsed");
    }

    // Battle Phase triggers and phase windows (docs/EffectSystem.md section 93): the events they fit, the end-of-Battle-Phase handler families,
    // and Check_Phase against the duel's phase / turn player.
    void TestBattlePhaseTriggers()
    {
        Check(TriggerFitsEvent("battle_phase_start", 4) && TriggerFitsEvent("opponent_battle_phase_start", 4) && !TriggerFitsEvent("battle_phase_start", 24),
              "start of the Battle Phase: event 4");
        Check(TriggerFitsEvent("battle_phase_end", 24) && TriggerFitsEvent("your_battle_phase_end", 24) && IsKnownTriggerEvent(24) && IsKnownTriggerEvent(4),
              "end of the Battle Phase: event 24");
        Check(PhaseFamilyFor("battle_phase_end") == 0x14021D140 && PhaseFamilyFor("your_battle_phase_end") == 0x14021D050, "end-of-Battle-Phase handler families");
        auto* phase = reinterpret_cast<int*>(YGO::Addr::Duel_CurrentPhase);
        auto* turn = reinterpret_cast<int*>(YGO::Addr::Duel_TurnPlayer);
        uint16_t record[32]{};
        record[1] = 0;   // controller: player 0
        const int savedPhase = *phase, savedTurn = *turn;
        *phase = 2; *turn = 1;   // Main Phase 1 of the opponent's turn
        Check(Check_Phase<kPhaseMain, 0>(record, 0, 0, 0) && Check_Phase<kPhaseMain, 2>(record, 0, 0, 0) && !Check_Phase<kPhaseMain, 1>(record, 0, 0, 0)
              && !Check_Phase<kPhaseBattle, 0>(record, 0, 0, 0) && Check_Phase<kPhaseMain | kPhaseBattle, 2>(record, 0, 0, 0)
              && Check_Phase<kPhaseAny, 2>(record, 0, 0, 0) && !Check_Phase<kPhaseAny, 1>(record, 0, 0, 0), "Check_Phase: opponent's Main Phase 1");
        *phase = 4; *turn = 0;   // your Main Phase 2
        Check(Check_Phase<kPhaseMain, 1>(record, 0, 0, 0) && !Check_Phase<kPhaseMain, 2>(record, 0, 0, 0), "Check_Phase: your Main Phase 2");
        *phase = 5;              // End Phase
        Check(!Check_Phase<kPhaseMain | kPhaseBattle, 0>(record, 0, 0, 0) && Check_Phase<kPhaseAny, 1>(record, 0, 0, 0), "Check_Phase: End Phase");
        *phase = savedPhase; *turn = savedTurn;
    }

    std::vector<std::tuple<int, int, int, uint32_t>> g_GraveOffers;
    int64_t __fastcall MockOfferGrave(int player, int id, int event, uint32_t param) { g_GraveOffers.emplace_back(player, id, event, param); return 1; }
    void __fastcall MockLeaveEval(uint32_t, int, int, uint32_t, int, int16_t, int) {}
    void TestOwnWatchers()
    {
        using nlohmann::json;
        constexpr uint16_t guard = 15370, mourner = 15371, moved = 4007;
        g_Clones.clear();
        Clone a, b;
        Check(ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "card_destroyed"}, {"watch", {{"side", "you"}, {"by", "battle"}, {"other", true}}}}, a, "t") &&
              ParseEffect(json{{"from", 4844}, {"ownTrigger", true}, {"trigger", "card_sent_to_grave"}, {"watch", {{"side", "opponent"}, {"from", json::array({"hand", "deck"})}, {"inGrave", true}}}}, b, "t"),
              "watch parse");
        g_Clones.emplace(guard, std::move(a));
        g_Clones.emplace(mourner, std::move(b));
        g_OwnWatchCards = { guard, mourner };
        orig_LeaveEval = &MockLeaveEval;
        orig_MoveEvaluator = &MockEvaluator;
        orig_OfferByRef = &MockOffer;
        Offer_EffectOfNamedCardOnField = &MockOfferNamed;
        Offer_EffectOfNamedCardInGrave = &MockOfferGrave;
        IsMonsterCategory = &MockIsMonster;
        auto* instances = reinterpret_cast<uint32_t*>(kCardInstances);
        auto* sides = reinterpret_cast<uint8_t*>(0x14349C494);   // g_BattleSides
        memset(sides, 0, 80);
        instances[2 * 9] = moved;
        auto leaveRef = [](uint32_t player, uint32_t zone, bool destroyed) { return 9u | (player << 9) | (zone << 10) | (destroyed ? 1u << 17 : 0) | (16u << 21); };
        // player 0's monster in zone 2 destroyed by battle: the guard (player 0's side) reacts, event 22 with the reference as the param
        sides[1] = 2; sides[0x23] = 1;
        g_NamedOffers.clear(); g_GraveOffers.clear();
        Hook_LeaveEval(leaveRef(0, 2, true), 0, 0, 0, -1, 0, 0);
        Check(g_NamedOffers.size() == 1 && g_NamedOffers[0] == std::make_tuple(0, int(guard), 22, leaveRef(0, 2, true)), "Destroyed by battle: the guard was not offered to its controller");
        Check(g_GraveOffers.empty(), "Destroyed: the GY watcher of a GY move reacted");
        // not by battle (no battle side in that zone), an opponent's monster, a leave that is not a destruction: no offer
        sides[0x23] = 0;
        g_NamedOffers.clear();
        Hook_LeaveEval(leaveRef(0, 2, true), 0, 0, 0, -1, 0, 0);
        Check(g_NamedOffers.empty(), "Destroyed by an effect fired a by-battle watcher");
        sides[40 + 1] = 3; sides[40 + 0x23] = 1;
        Hook_LeaveEval(leaveRef(1, 3, true), 0, 0, 0, -1, 0, 0);
        Check(g_NamedOffers.size() == 1 && std::get<0>(g_NamedOffers[0]) == 1, "An opponent's monster: only its controller's copy may react ('you control')");
        g_NamedOffers.clear();
        Hook_LeaveEval(leaveRef(1, 3, false), 1, 0, 0, -1, 0, 0);
        Check(g_NamedOffers.empty(), "A leave that is not a destruction fired card_destroyed");
        // the GY watcher: player 1's card sent from the hand (zone 13) to the GY - player 0's copy in the GY reacts (event 32)
        g_GraveOffers.clear(); g_NamedOffers.clear();
        uint32_t move = 9u | (1u << 9) | (13u << 10) | (16u << 21);
        Hook_MoveEvaluator(32, &move, 0, 0);
        Check(g_GraveOffers.size() == 1 && g_GraveOffers[0] == std::make_tuple(0, int(mourner), 32, move), "Sent from the opponent's hand: the GY watcher was not offered");
        Check(g_NamedOffers.empty(), "A GY move fired a field watcher");
        g_GraveOffers.clear();
        move = 9u | (1u << 9) | (16u << 10) | (17u << 21);
        Hook_MoveEvaluator(31, &move, 0, 0);
        Check(g_GraveOffers.empty(), "Banished fired a sent-to-the-GY watcher");
        // own rows: the field watcher in table 1, the GY watcher in table 2
        uint16_t lookup[0x20]{};
        lookup[0] = guard; lookup[4] = 1; lookup[3] = 22;
        Check(Hook_GetEntry(lookup) != nullptr, "Field watcher row missing in table 1");
        lookup[0] = mourner; lookup[4] = 2; lookup[3] = 32;
        Check(Hook_GetEntry(lookup) != nullptr, "GY watcher row missing in table 2");
        lookup[4] = 1;
        Check(Hook_GetEntry(lookup) == nullptr, "GY watcher answered table 1");
        g_OwnWatchCards.clear();
        g_Clones.clear();
    }

    // Raw pick costs (docs/EffectSystem.md section 90): each verb gets the game's cost machine traced in IDA (no donor id), the hand verbs the
    // per-index hand scan their picker asks, Tribute / hand-or-field your own field; the count hooks answer for the card's own engine id, inside
    // the machine (g_Active) and between its steps (g_Paying), and nothing else.
    void TestRawPickCosts()
    {
        using nlohmann::json;
        auto parse = [](json pick) {
            Clone c;
            Check(ParseEffect(json{{"from", 4844}, {"costPick", pick}}, c, "t") && c.CostPick, "raw cost parse");
            return c;
        };
        Clone discard = parse(json{{"verb", "discard"}, {"count", 2}, {"deck", {{"type", "spell"}}}});
        Clone& d = *discard.CostPick;
        Check(d.RawSlots && d.From == 0 && d.Verb == Clone::PickVerb::Discard && d.CostPickCount == 2, "discard: not a raw 2-card cost");
        Check(discard.SlotOverride[3] == 0x1401F8010, "discard: not Cost_DiscardPickedHandCards");
        Check(*reinterpret_cast<const uint64_t*>(d.DeckRow + 8) == 0x14052F350, "discard: the picker's per-index hand scan is not the row's");
        Clone tributeOne = parse(json{{"verb", "tribute"}, {"count", 1}});
        Clone tributeTwo = parse(json{{"verb", "tribute"}, {"count", 2}, {"deck", {{"race", "Dragon"}}}});
        Check(tributeOne.SlotOverride[3] == 0x1401FDDF0 && tributeTwo.SlotOverride[3] == 0x1401FE930, "tribute: wrong machine for 1 / N");
        Check(tributeTwo.CostPick->FieldPick && tributeTwo.CostPick->FieldSide == 0, "tribute: not your own field");
        Clone send = parse(json{{"verb", "send"}, {"count", 3}});
        Clone reveal = parse(json{{"verb", "reveal"}, {"count", 3}});
        Clone handField = parse(json{{"verb", "sendHandOrField"}});
        Check(send.SlotOverride[3] == 0x1401FF090 && send.CostPick->CostPickCount == 3, "send: machine / count");
        Check(reveal.SlotOverride[3] == 0x140202770 && reveal.CostPick->CostPickCount == 1, "reveal: the machine reveals 1 for an unknown id");
        Check(handField.SlotOverride[3] == 0x140208E00 && handField.CostPick->FieldSide == 0 && handField.CostPick->CostPickCount == 1, "hand or field: machine / side");
        Clone donor = parse(json{{"count", 1}, {"deck", {{"scan", "grave"}}}});
        Clone raw = parse(json{{"verb", "banish"}, {"raw", true}, {"self", true}, {"count", 2}, {"deck", {{"scan", "grave"}}}});
        Check(!donor.CostPick->RawSlots && donor.CostPick->From == 7452, "banish without raw changed (old data)");
        Check(raw.CostPick->RawSlots && raw.CostPick->From == 0 && raw.CostPick->CostSelf && raw.SlotOverride[3] == 0x1401F99D0, "raw banish");

        uint16_t record[0x20]{};
        record[0] = 15310;
        d.EngineId = 15310;
        g_Active = &d;
        Check(PayingClone(15310) == &d && Hook_DiscardCount(record) == 2, "discard count inside the machine");
        Check(PayingClone(4844) == nullptr, "another id answered as the cost");
        g_Active = nullptr;
        g_Paying = &d;
        Check(PayingClone(15310) == &d && DeckFilterClone(15310) == &d, "between the steps the picker's test is not the cost's");
        Clone& t = *tributeTwo.CostPick;
        t.EngineId = 15310;
        g_Paying = &t;
        int max = 0;
        Check(Hook_TributeCount(record, &max) == 2 && max == 2, "tribute count / maximum");
        Clone& s = *send.CostPick;
        s.EngineId = 15310;
        g_Paying = &s;
        Check(Hook_SendHandCount(record) == 3, "send count");
        g_Paying = nullptr;
        Check(PayingClone(15310) == nullptr, "a finished cost still answered");
    }

    void TestOrders(bool explicitTriggers)
    {
        for (bool reversed : {false, true})
        {
            Clone draw = Make(4844, "", 2);
            Clone summon = Make(6784, explicitTriggers ? "normal_or_special_summoned" : "");
            Clone card = reversed ? draw : summon;
            card.Parts.push_back(reversed ? summon : draw);
            Check(RouteByTrigger(&card, kSummonTriggers, 1)->From == 6784, "Summon evaluator selected the first effect instead of the monster source");
            g_Clones.clear();
            g_Clones.emplace(15301, std::move(card));
            for (const auto [table, event, expected] : {std::tuple{0, 0, 4844}, std::tuple{1, 7, 6784}, std::tuple{1, 9, 6784}})
            {
                uint16_t record[0x20]{};
                record[0] = 15301; record[2] = table == 0 ? 7 : 0;
                record[3] = event; record[4] = table;
                const auto saved = std::to_array(record);
                Row* wrapped = static_cast<Row*>(Hook_GetEntry(record));
                Check(wrapped && wrapped->Id == 15301, "Missing wrapped effect row");
                Check(wrapped->Slot[0] != nullptr, "Missing resolve slot");
                reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(wrapped->Slot[0])(record, 0, 0, 0);
                Check(lastSource == expected, "Resolution used the wrong source");
                Check(std::equal(saved.begin(), saved.end(), record), "Routing changed the engine record");
                if (table == 0) Check(Hook_GetDraw(record) == 2, "Draw count routed to the wrong part");
            }
        }
    }
}

int main()
{
    try
    {
        // SlotThunk diagnostics read the engine's step variable even with tracing off.
        void* page = VirtualAlloc(reinterpret_cast<void*>(0x143490000), 0x10000, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        Check(page != nullptr, "Could not reserve mock engine memory");
        orig_GetEntry = &Lookup;
        g_RealId = &NoBorrow;
        drawRow.Slot[0] = summonRow.Slot[0] = graveRow.Slot[0] = reinterpret_cast<void*>(&Resolve);
        TestOrders(false);
        std::cout << "PASS raw Pendulum/monster clones in both source orders, lookup and resolution\n";
        TestOrders(true);
        std::cout << "PASS compiled triggers in both source orders, lookup and resolution\n";
        provideGeneric = true;
        TestOrders(false);
        provideGeneric = false;
        std::cout << "PASS exact source rows win over generic Spell/Trap fallbacks\n";
        Clone ordinary = Make(4844), limited = Make(4844);
        limited.OncePerTurn = true;
        Row* ordinaryWrapped = WrappedRow(&drawRow, 15302, ordinary);
        Row* limitedWrapped = WrappedRow(&drawRow, 15302, limited);
        Check(ordinaryWrapped != limitedWrapped && (ordinaryWrapped->Extra[0] & 15) == 0 && (limitedWrapped->Extra[0] & 15) == 5,
            "Parts borrowing one source row shared overrides");
        std::cout << "PASS parts sharing a source keep separate overrides\n";
        {
            // "activate 1 of these effects" bullets (limitGroup) share one class; other once-per-turn effects take the next of 5, 6, 7, 8
            Clone card = Make(4844);
            card.OncePerTurn = true;
            card.LimitGroup = 0;
            card.Parts = {Make(4844), Make(6784), Make(7892), Make(4844), Make(6784), Make(4844)};
            card.Parts[0].OncePerTurn = true; card.Parts[0].LimitGroup = 0;   // same choice as the main effect
            card.Parts[1].OncePerTurn = true;                                 // its own limit
            // Parts[2] has no limit
            card.Parts[3].OncePerTurn = true; card.Parts[3].LimitGroup = 4;
            card.Parts[4].OncePerTurn = true;
            card.Parts[5].OncePerTurn = true;                                 // a fifth limit: shares class 5
            AssignLimitClasses(card, "test");
            Check(card.LimitClass == 5 && card.Parts[0].LimitClass == 5, "Choice group does not share one limit class");
            Check(card.Parts[1].LimitClass == 6 && card.Parts[3].LimitClass == 7 && card.Parts[4].LimitClass == 8,
                "Independent once-per-turn effects did not get classes 6, 7, 8");
            Check(card.Parts[5].LimitClass == 5, "A fifth limit did not fall back to class 5");
            Row* grouped = WrappedRow(&drawRow, 15304, card.Parts[3]);
            Check((grouped->Extra[0] & 15) == 7, "Wrapped row lost limit class 7");
            Row seven = drawRow;
            seven.Extra[0] = 7;
            Check((WrappedRow(&seven, 15305, card.Parts[2])->Extra[0] & 15) == 0, "A donor's class 7 limit leaked into a clone");
        }
        std::cout << "PASS once-per-turn classes 5-8 and shared choice-group limits\n";
        TestUseLimitStateMachine();
        std::cout << "PASS use-limit state machine (IDA transcription): per copy, per name, shared choice, end of turn\n";
        TestPhaseDriverStateMachine();
        std::cout << "PASS phase driver state machine (IDA transcription): your / opponent's / each Standby and End Phase\n";
        TestOwnTriggerStateMachine();
        std::cout << "PASS own trigger (no donor): own row, composed action, virtual phase row after the table's last row\n";
        TestOwnIgnitionRow();
        std::cout << "PASS own ignition: table-3 row with no slot-3 cost, resolves as its action, three 'or' ignitions offered once (issue #39)\n";
        TestOwnMoveTriggers();
        std::cout << "PASS own move triggers: move reference checks, the evaluator's offer layout, no donor swap, table 2 row\n";
        TestOwnAttackTriggers();
        std::cout << "PASS own attack triggers: attacker / attacked player's monster / target, evaluator param, direct attack, replay\n";
        TestOwnSummonTriggers();
        std::cout << "PASS own summon triggers: the evaluator's ref / slot / param, zone gate, event, summonCondition, own row (section 99)\n";
        TestOwnSummonReactions();
        std::cout << "PASS own summon-reaction triggers: opponent's Special / Normal-or-Special Summon, any Special Summon, Set ignored\n";
        TestRawPickCosts();
        std::cout << "PASS raw pick costs: machine per verb, picker scans, counts for the card's own id inside and between the machine's steps\n";
        TestOwnWatchers();
        std::cout << "PASS own watchers: destroyed by battle / effect, whose card, GY watcher on a hand-to-GY move, banish ignored, own rows per table\n";
        TestBattlePhaseTriggers();
        std::cout << "PASS Battle Phase triggers and phase windows: events 4 / 24, end-of-Battle-Phase families, Check_Phase per phase and turn\n";
        TestTargetVerbs();
        std::cout << "PASS target verbs: change position / face-down / take control machines, targeting slots, position and change filter bits\n";
        g_Clones.clear();
        Clone choices = Make(4844, "", 2);
        choices.Parts = {Make(4844, "", 3)};
        g_Clones.emplace(15301, std::move(choices));
        Clone& root = g_Clones.at(15301);
        orig_InsertOffer = Insert;
        for (int kind : {0, 1})
        {
            auto* count = reinterpret_cast<uint32_t*>(kind ? 0x14349C000 : 0x14349C020);
            *count = 0;
            for (Clone* part : AllParts(root))
            {
                g_MenuPart = part;
                Check(Hook_InsertOffer(kind, 15301, 12, 0) == 1, "Sibling offer was merged");
                Check(Hook_InsertOffer(kind, 15301, 12, 0) == -1, "Identical offer was duplicated");
            }
            g_MenuPart = nullptr;
            Check(*count == 2, "Menu does not contain both effects");
            const uint32_t capacity = kind ? 16 : 32;
            *count = capacity;
            g_MenuPart = &root;
            Check(Hook_InsertOffer(kind, 15301, 12, 0) == 0 && *count == capacity,
                "Full native offer queue was modified");
            g_MenuPart = nullptr;
            *count = 0;
        }
        std::cout << "PASS separate trigger and activation offers, with duplicate suppression\n";
        for (Clone* part : AllParts(root))
        {
            uint16_t record[0x20]{};
            record[0] = 15301;
            record[20] = ChoiceTag(root, part) | 7;
            const auto saved = std::to_array(record);
            Check(RouteByRow(&root, record) == part, "Selected same-table effect lost its identity");
            Row* row = static_cast<Row*>(Hook_GetEntry(record));
            reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(row->Slot[0])(record, 0, 0, 0);
            Check(Hook_GetDraw(record) == part->Draw, "Selected effect used sibling draw parameters");
            Check(std::equal(saved.begin(), saved.end(), record), "Choice tag or native sub-effect number was lost");
        }
        std::cout << "PASS independently selected effects sharing table, event and source row\n";
        {
            // issue #38: a Spell with two effects; the player picks effect 2 at the select row's prompt, and the record that resolves is a copy
            // whose card instance (word11) differs - the choice must travel in the record (word20 tag), not only in the (card, instance) key
            uint16_t activation[0x20]{};
            activation[0] = 15301; activation[2] = 9; activation[11] = 5;
            Row* select = static_cast<Row*>(Hook_GetEntry(activation));
            Check(select && select->Slot[3] == reinterpret_cast<void*>(&ChoiceSlot3), "Two-effect Spell is not offered with the select row");
            g_ChoicePrompted.insert(ChoiceKey(activation));       // the prompt is up ...
            *reinterpret_cast<uint16_t*>(kPromptAnswer) = 1;      // ... and the player picked the second effect
            Check(ChoiceSlot3(activation, 0, 0, 0) == 1, "Chosen effect with no cost did not finish slot 3");
            uint16_t resolving[0x20];
            std::copy(std::begin(activation), std::end(activation), resolving);
            resolving[2] = 7; resolving[11] = 9;
            Row* chosen = static_cast<Row*>(Hook_GetEntry(resolving));
            Check(chosen && chosen->Slot[3] != reinterpret_cast<void*>(&ChoiceSlot3), "Resolution was offered the select row again");
            Check(RouteByRow(&root, resolving) == root.Parts.data(), "Resolution lost the chosen effect");
            Check(Hook_GetDraw(resolving) == 3, "Resolution used the first effect's parameters");
            reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(chosen->Slot[0])(resolving, 0, 0, 0);
            Check(g_Choices.empty(), "Resolved choice was kept");
            uint16_t next[0x20]{};
            next[0] = 15301; next[2] = 9; next[11] = 5;
            Check(static_cast<Row*>(Hook_GetEntry(next))->Slot[3] == reinterpret_cast<void*>(&ChoiceSlot3), "The next activation did not choose again");
        }
        std::cout << "PASS a chosen effect survives into a copied record with another card instance (issue #38)\n";
        orig_OfferByRef = Offer;
        orig_MenuOffer = MenuOffer;
        *reinterpret_cast<uint32_t*>(0x14349C020) = 0;
        *reinterpret_cast<uint32_t*>(0x14349C000) = 0;
        Hook_OfferByRef(15301, 12, 0);
        Hook_MenuOffer(15301, 12, nullptr, 0);
        Check(*reinterpret_cast<uint32_t*>(0x14349C020) == 2 && *reinterpret_cast<uint32_t*>(0x14349C000) == 2,
            "Unpinned offer did not enumerate all compatible parts");
        Check(g_MenuPart == nullptr, "Offer enumeration leaked the selected part");
        Clone composed = Make(6784);
        composed.ActionFrom = 4844;
        summonRow.Slot[3] = reinterpret_cast<void*>(0x140201950);
        Check(WrappedRow(&summonRow, 15303, composed)->Slot[3] == nullptr,
            "Composed action inherited its donor's unrelated choices");
        Clone rawChoices = Make(6784);
        Check(WrappedRow(&summonRow, 15303, rawChoices)->Slot[3] != nullptr,
            "Raw clone lost its native choices");
        summonRow.Slot[3] = nullptr;
        std::cout << "PASS offer enumeration and composed versus raw donor choices\n";
        Clone card = Make(4844);
        card.Parts = {Make(6784), Make(7892)};
        Check(RouteByTrigger(&card, kLeaveTriggers, 2)->From == 7892, "Third effect was not selected for a graveyard event");
        Check(RouteByTrigger(&card, kSummonTriggers, 1)->From == 6784, "Third effect changed summon routing");
        std::cout << "PASS three independent effect tables\n";
        Clone incompatible = Make(6784, "sent_to_grave");
        incompatible.Parts = {Make(4844)};
        Check(RouteByTrigger(&incompatible, kSummonTriggers, 1) == nullptr, "Explicit trigger was ignored");
        Clone single = Make(4844);
        Check(RouteByTrigger(&single, kSummonTriggers, 1) == &single, "Single-source behavior changed");
        std::cout << "PASS explicit trigger restrictions and single-effect compatibility\n";
        using nlohmann::json;
        for (const auto& clone : {json{{"from",6784},{"parts",json::array({json{{"from",4844}}})}},
                                 json{{"from",4844},{"parts",json::array({json{{"from",6784}}})}}})
            Check(YGO::Effects::SingleEffectSource(clone,3900,14969) == 0, "Multi-effect card borrowed its first source's identity");
        Check(YGO::Effects::SingleEffectSource(json{{"from",6784}},3900,14969) == 6784, "Single-source lending changed");
        Check(YGO::Effects::SingleEffectSource(json{{"from",6784},{"parts",json::array()}},3900,14969) == 6784, "Empty parts changed lending");
        Check(YGO::Effects::SingleEffectSource(json{{"from",15301}},3900,14969) == 0, "Invalid source was lent");
        Check(YGO::Effects::SingleEffectSource(json{{"from",5738},{"effectOnly",true}},3900,14969) == 0, "Effect-only clone was lent its source's identity");
        Check(YGO::Effects::SingleEffectSource(json{{"from",5738},{"effectOnly",false}},3900,14969) == 5738, "effectOnly false stopped lending");
        std::cout << "PASS source lending policy in both orders\n";
        VirtualFree(page, 0, MEM_RELEASE);
        return 0;
    }
    catch (const std::exception& e) { std::cerr << "FAIL " << e.what() << '\n'; return 1; }
}
