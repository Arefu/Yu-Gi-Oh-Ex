#include <algorithm>
#include <array>
#include <utility>
#include <filesystem>
#include "Config.h"
#include <intrin.h>
#include <set>
#include <tuple>
#include <Windows.h>
#include <detours.h>
#include <cstdint>
#include <cstring>
#include <format>
#include <fstream>
#include <string>
#include <map>
#include <span>
#include <unordered_map>
#include <vector>

#include <json.hpp>

#include "EffectClone.h"
#include "Logger.h"

namespace
{
    constexpr int kFirstExtraCardId = 0x3BC4;   // 15300, as in Yu-Gi-Oh-Cards/Card.h
    constexpr int kLastExtraCardId = 0x4E1F;

    struct Clone
    {
        int From = 0;          // the vanilla card whose effect handlers are borrowed
        int Draw = -1;         // Get_NumberOfCardsToDraw's answer; -1 = whatever the source card draws
        bool HasFilter = false;   // replaces the source's generic filter row (table 0x140B16220, row k = 0), see FilterRow
        uint16_t FilterRow[6]{};  // the 12-byte row: id, param, flagsExtra (u32), flags (u32)
        std::vector<Clone> Pre;       // "before": steps that run first, in one call, ahead of this one (chaining, see SlotThunk<0>)
        bool OncePerTurn = false;     // "You can only use this effect once per turn" (per card name): the row's limit class becomes 5, see WrappedRow
        bool HasLp = false;           // replaces the life point changes of a Life Point effect (see Hook_LpChanges)
        int LpOwn = 0, LpOpp = 0;     // signed change for the effect's controller / the opponent (negative = damage)
        bool HasDeckFilter = false;   // replaces which Deck cards a "search the Deck" effect can find (see BuildDeckFilter)
        uint8_t DeckRow[24]{};        // synthetic row of the game's deck filter table (0x140AD2A80): id, param, -1, scan fn, flags
        bool HasStats = false;        // "stats": { "atk": N, "def": N } - replaces the ATK/DEF a stat table gives (equip / union / boost), see Hook_RowSearch
        int16_t StatRow[4]{};         // synthetic row handed back: source id, ATK, DEF, 0
        // Slot functions that replace the source row's own (0 = keep): a new effect composed from the game's pieces. So far slot 2, the
        // can-activate condition: "condition": "always" (Slot_ReturnConst2) or "listHasMatch" (Cond_ListHasEnoughMatches), e.g. Unexpected Dai's
        // "Special Summon 1 X from the Deck" without its "if you control no monsters" (docs/EffectSystem.md section 33).
        uint64_t SlotOverride[5]{};
        // Trigger x action composition: the row (its table, event wiring, limit class, slot 3 monster-effect hooks) comes from From - a monster
        // with the wanted trigger - and slots 0, 1, 2 and 4 (resolve, target, condition, prompt) from ActionFrom's row, each run impersonating
        // ActionFrom. "When this card is Normal Summoned: Special Summon 1 X from your hand" has no vanilla card; this composes it.
        int ActionFrom = 0;
        // A cost composed from a game card whose slot 3 is that cost ("cost": { "from": N, "amount": K }): slot 3 runs N's cost function as N,
        // slot 2 also has to pass N's condition (e.g. "enough cards in hand"), and K replaces the amount N's id would give (discard count
        // sub_1401F6F90, life points sub_1401F65D0). Lightning Vortex 5217 = discard, Delinquent Duo 4901 = pay LP (docs section 34).
        int CostFrom = 0;
        int CostAmount = -1;
        uint64_t CostCheck = 0;   // "cost": { "check": name } - this check instead of the cost card's own condition (which may be card-specific)
        // "require": [names] - the game's own small checks, run (as From) before the condition: where the card is for a composed hand / GY /
        // field effect ("canBanishSelfFromGrave" when From's cost banishes this card from the GY ...), "you control no monsters" ... A composed
        // effect takes slot 2 from its action card, which knows nothing about the zone the cost needs (docs/EffectSystem.md section 37).
        std::vector<uint64_t> Require;
        // "detach": N - Xyz Materials the composed detach cost takes (the cost / its check ask sub_1401F7AF0 by the row card's id).
        int DetachCount = 0;
        // "negate": what a negation may negate (docs/EffectSystem.md section 39). The negate check Cond_NegateTargetMatches (0x1400FC410) reads
        // the effect card's row {id, what, flags} of NegateTable (0x140B0DAB0); while this clone's check runs the source's row holds these.
        bool HasNegate = false;
        uint16_t NegateWhat = 0;      // 1 Spell, 2 Trap, 4 monster effect (or'ed; 7 any), >= 3000 one named card
        uint16_t NegateFlags = 0;     // 0x1 a card's activation only, 0x2 the opponent's only, 0x10 / 0x20 your / the opponent's turn, 0x40 Battle Phase, 0x400 targets exactly 1 card
        int NegateProperty = -1;      // Spell/Trap property the negated card must have (0 Normal, 1 Counter, 2 Field, 3 Equip, 4 Continuous, 5 Quick-Play, 6 Ritual); -1 any
        std::string Trigger;          // the EffectScript trigger this effect was compiled for ("normal_summoned", "sent_to_grave" ...), "" = none / unknown
        // A card with several effects ("If Normal Summoned: ... If sent to the GY: ..."): each further effect borrows its own vanilla card.
        // The engine asks for a card's effects table by table (summon / FLIP rows, leave-field rows, ignition rows) and per event, so every
        // lookup is routed to the part whose source answers it (RouteByRow / RouteByTrigger). Parts have no parts of their own.
        std::vector<Clone> Parts;
        bool Announced = false;
    };

    std::unordered_map<uint16_t, Clone> g_Clones;

    using GetEntry_t = void*(__fastcall*)(uint16_t*);
    using GetDraw_t = int64_t(__fastcall*)(uint16_t*);
    GetEntry_t orig_GetEntry = reinterpret_cast<GetEntry_t>(0x1400DFBC0);   // YGO::Effects::Get_EffectTableEntryForCard
    GetDraw_t orig_GetDraw = reinterpret_cast<GetDraw_t>(0x14015EA90);      // YGO::Effects::Get_NumberOfCardsToDraw
    using Collect_t = int64_t(__fastcall*)(int, uint16_t, uint32_t);
    Collect_t orig_Collect = reinterpret_cast<Collect_t>(0x1400C0290);      // YGO::Effects::Deck_CollectMatchingCards
    Collect_t orig_Count = reinterpret_cast<Collect_t>(0x1400C0320);        // YGO::Effects::Deck_CountMatchingCards
    using LpChanges_t = int16_t(__fastcall*)(uint16_t*, int32_t*);
    LpChanges_t orig_LpChanges = reinterpret_cast<LpChanges_t>(0x14015AC90);   // fills int[2]: life point change per player (absolute player index)
    using GetRow_t = uint16_t*(__fastcall*)(uint16_t, int);
    GetRow_t orig_GetRow = reinterpret_cast<GetRow_t>(0x1401B0F40);         // YGO::Effects::Get_SecondaryRow_1401B0F40 (filter rows, 0x140B16220)

    // In a duel a custom card above 16383 plays under a vanilla id borrowed for the duel (Yu-Gi-Oh-Cards, ygo-duel-id-remap-plan);
    // this asks Cards which custom card the engine's id stands for. 0 = not a borrowed id.
    using RealId_t = unsigned short(__cdecl*)(unsigned short);
    RealId_t g_RealId = nullptr;

    unsigned short BorrowedRealId(uint16_t id)
    {
        if (!g_RealId)
        {
            HMODULE cards = GetModuleHandleA("Yu-Gi-Oh-MoreCards.dll");
            if (!cards)
                return 0;
            g_RealId = reinterpret_cast<RealId_t>(GetProcAddress(cards, "Card_GetRealIdForBorrowed"));
            if (!g_RealId)
                return 0;
        }
        return g_RealId(id);
    }

    // What the engine's id means: the custom card it is (a plain custom id, or a borrowed vanilla id), or nothing.
    // `borrowedWithoutClone` is set for a borrowed id whose custom card has no clone: it must NOT fall through to the
    // vanilla card that owns that id (that would run someone else's effect), it simply has no effect.
    // The game ships effect code and id lists for ids 14969 - 15234 (198 rows in the effect tables, ids in word_140BF8BC0 ...) although it has no
    // card data for them (cut or later cards). Custom cards used to be numbered from 14969 (they start at 15300 now, past every ghost id, so this is a safety net), so a custom card WITHOUT its own effect inherits one of
    // those ghost effects (and their hand/trigger list entries) by accident. A custom id that is not a clone therefore has no effect at all.
    bool IsPlainCustomId(uint16_t id)
    {
        return id >= kFirstExtraCardId && id <= 0x3FFF && g_Clones.find(id) == g_Clones.end();
    }

    // Debug tracing (call stacks of lookups, source rows, type classes) is off unless a file named trace.txt exists in the plugin's Effects folder.
    bool g_Trace = false;

    Clone* Find(uint16_t id, bool& borrowedWithoutClone)
    {
        borrowedWithoutClone = false;
        const unsigned short real = BorrowedRealId(id);
        const uint16_t key = real ? real : id;
        auto it = g_Clones.find(key);
        if (it != g_Clones.end())
            return &it->second;
        borrowedWithoutClone = real != 0;
        return nullptr;
    }

    // Every effect of a card, the main one first.
    std::vector<Clone*> AllParts(Clone& clone)
    {
        std::vector<Clone*> all{ &clone };
        for (Clone& part : clone.Parts)
            all.push_back(&part);
        return all;
    }

    // Does an effect compiled for `trigger` belong to the event in an effect record's word3 (docs/EffectSystem.md sections 29-30)?
    bool TriggerFitsEvent(const std::string& trigger, uint16_t event)
    {
        switch (event)
        {
        case 7: case 9: return trigger == "normal_summoned" || trigger == "special_summoned" || trigger == "summoned" || trigger == "normal_or_special_summoned";
        case 8: return trigger == "flip" || trigger == "summoned";
        case 13: case 21: return trigger == "flip";
        case 31: case 32: case 33: return trigger == "sent_to_grave" || trigger == "sent_from_field_to_grave" || trigger == "destroyed_by_battle";
        case 2: return trigger == "standby_phase";
        case 6: return trigger == "end_phase";
        case 15: return trigger == "battle_damage";
        case 18: case 19: return trigger == "attack_declared";
        case 22: return trigger == "destroys_by_battle";
        default: return false;
        }
    }

    // The part of a multi-effect card whose source has a row for this lookup (same table selector word4, index word2, event word3). When several
    // do (two effects in the same table, e.g. a FLIP and a Normal Summon effect), the one whose trigger fits the record's event wins; then the first.
    Clone* RouteByRow(Clone* clone, uint16_t* effect)
    {
        if (!clone || clone->Parts.empty() || !effect)
            return clone;
        const uint16_t saved = effect[0];
        Clone* first = nullptr;
        Clone* fitting = nullptr;
        for (Clone* part : AllParts(*clone))
        {
            effect[0] = static_cast<uint16_t>(part->From);
            if (orig_GetEntry(effect))
            {
                if (!first)
                    first = part;
                if (!fitting && TriggerFitsEvent(part->Trigger, effect[3]))
                    fitting = part;
            }
        }
        effect[0] = saved;
        return fitting ? fitting : first ? first : clone;
    }

    constexpr const char* kSummonTriggers[] = { "normal_summoned", "special_summoned", "summoned", "normal_or_special_summoned", "flip" };
    constexpr const char* kLeaveTriggers[] = { "sent_to_grave", "sent_from_field_to_grave", "destroyed_by_battle" };
    constexpr const char* kBattleListTriggers[] = { "attack_declared", "battle_damage" };

    // The part whose trigger is one of `triggers` (the main effect when none is).
    Clone* RouteByTrigger(Clone* clone, std::span<const char* const> triggers)
    {
        if (!clone || clone->Parts.empty())
            return clone;
        for (Clone* part : AllParts(*clone))
            for (const char* t : triggers)
                if (part->Trigger == t)
                    return part;
        return clone;
    }

    // Diagnostics for the log (level 69 = debug): every distinct kind of lookup the engine makes for a custom card, and the first calls of each
    // slot function. This is how a trigger that "does nothing" is told apart from one the engine never asks about.
    void LogLookup(const uint16_t* effect)
    {
        if (!g_Trace)
            return;
        static std::unordered_map<uint64_t, int> seen;
        const uint64_t key = static_cast<uint64_t>(effect[0]) | (static_cast<uint64_t>(effect[2]) << 16) | (static_cast<uint64_t>(effect[3]) << 32) | (static_cast<uint64_t>(effect[4]) << 48);
        if (++seen[key] > 2 || seen.size() > 300)
            return;
        Logger::WriteLog(std::format("Effect lookup for custom id {}: controller {} zone/index {} type {} subtype {} (call {})", effect[0], effect[1], effect[2], effect[3], effect[4], seen[key]), MODULE_NAME, 0);
    }

    void Announce(Clone& clone, uint16_t id)
    {
        if (clone.Announced)
            return;
        clone.Announced = true;
        Logger::WriteLog(std::format("Custom card {} borrows the effect of vanilla card {}", id, clone.From), MODULE_NAME, 0);
        if (g_Trace)
        {
            // Card type class of the custom card vs its source: many engine gates index small per-type tables with it (word_140BF7820 + 12 * type).
            for (const unsigned cardId : { static_cast<unsigned>(BorrowedRealId(id) ? BorrowedRealId(id) : id), static_cast<unsigned>(clone.From) })
            {
                const int type = reinterpret_cast<int(__fastcall*)(int)>(0x14081A650)(static_cast<int>(cardId));
                const uint16_t* w = reinterpret_cast<const uint16_t*>(0x140BF7820 + 12 * static_cast<uintptr_t>(type));
                Logger::WriteLog(std::format("  card {} type class {}: table words {} {} {} {} {} {}", cardId, type, w[0], w[1], w[2], w[3], w[4], w[5]), MODULE_NAME, 0);
                // The engine's per-id yes/no tests that decide what the hand offers for a monster (Get_... 0x140108610 uses them): is it a monster type,
                // has a hand effect (743FA0 = 743F40 or 744080 or a 349-entry list). A custom id should answer no to the hand ones.
                using IdTest = int64_t(__fastcall*)(int);
                const auto test = [cardId](uintptr_t address) { return static_cast<int>(reinterpret_cast<IdTest>(address)(static_cast<int>(cardId)) & 0xFF); };
                Logger::WriteLog(std::format("  card {} id tests: monsterType {} handEffect {} (list1 {} list2 {}) 1407440E0 {} 744140 {}", cardId, test(0x140742DF0), test(0x140743FA0),
                    test(0x140743F40), test(0x140744080), test(0x1407440E0), test(0x140744140)), MODULE_NAME, 0);
            }
        }
    }

    // ---- impersonation -------------------------------------------------------------------------------------------------------
    // Presenting the source id only for the table lookup is not enough: many slot functions are big per-card ladders keyed by the effect
    // record's own id (e.g. Dark Hole's target predicate 0x1401952D0 switches on it), so with the custom id they fall to their default
    // branch ("no targets", never playable). So the row handed back holds wrappers, one per slot the source row uses, that swap the source
    // id into the record for the duration of that slot call and restore it after: the effect runs exactly as the source card's would.

    struct Row   // the game's 48-byte effect table row
    {
        uint16_t Id;
        uint16_t Extra[3];
        void* Slot[5];
    };
    static_assert(sizeof(Row) == 48, "effect table rows are 48 bytes");

    Clone* g_Active = nullptr;   // the clone whose slot function is running (the draw hook needs its parameters)

    // Draw and life point effects resolve completely within a single call (no player interaction), so they can be run back to back.
    bool IsImmediate(const Clone& c)
    {
        return c.HasLp || c.Draw >= 0 || c.From == 4844 /* Pot of Greed */ || c.From == 4345 /* Red Medicine */ || c.From == 4350 /* Hinotama */;
    }

    // The effect row of an action source card (Clone::ActionFrom): its first row in any of the four tables, cached.
    const Row* ActionRow(int actionFrom)
    {
        static std::unordered_map<int, const Row*> cache;
        auto it = cache.find(actionFrom);
        if (it != cache.end())
            return it->second;
        uint16_t record[32]{};
        record[0] = static_cast<uint16_t>(actionFrom);
        const Row* row = nullptr;
        for (uint16_t selector = 0; selector < 4 && !row; ++selector)
        {
            record[4] = selector;
            row = static_cast<const Row*>(orig_GetEntry(record));
        }
        cache[actionFrom] = row;
        if (!row)
            Logger::WriteLog(std::format("Action source {} has no effect row in any table", actionFrom), MODULE_NAME, 2);
        return row;
    }

    template <int SlotIndex>
    uint64_t __fastcall SlotThunk(uint16_t* effect, uint64_t a2, uint64_t a3, uint64_t a4)
    {
        bool borrowedWithoutClone;
        Clone* clone = RouteByRow(Find(effect[0], borrowedWithoutClone), effect);
        if (!clone)
            return 0;   // cannot happen for a row we built; a defensive default

        // The same source row the wrapper was built from (the record's other fields are unchanged, so the lookup picks the same row).
        const uint16_t saved = effect[0];
        effect[0] = static_cast<uint16_t>(clone->From);
        const Row* row = static_cast<const Row*>(orig_GetEntry(effect));
        void* fn = clone->SlotOverride[SlotIndex] ? reinterpret_cast<void*>(clone->SlotOverride[SlotIndex]) : row ? row->Slot[SlotIndex] : nullptr;
        uint16_t impersonate = static_cast<uint16_t>(clone->From);
        if (clone->CostFrom && (SlotIndex == 3 || SlotIndex == 2))
        {
            const Row* cost = ActionRow(clone->CostFrom);
            if constexpr (SlotIndex == 3)
            {
                // The cost itself, run as the cost card.
                fn = cost ? cost->Slot[3] : nullptr;
                impersonate = static_cast<uint16_t>(clone->CostFrom);
                effect[0] = impersonate;
            }
            else if (clone->CostCheck || (cost && cost->Slot[2]))
            {
                // The cost has to be payable: the cost card's own condition (or the named check) first, run as the cost card, then the effect's.
                effect[0] = static_cast<uint16_t>(clone->CostFrom);
                Clone* previousActive = g_Active;
                g_Active = clone;
                void* check = clone->CostCheck ? reinterpret_cast<void*>(clone->CostCheck) : cost->Slot[2];
                const uint64_t payable = reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(check)(effect, a2, a3, a4) & 0xFF;
                g_Active = previousActive;
                effect[0] = static_cast<uint16_t>(clone->From);
                if (!payable)
                {
                    effect[0] = saved;
                    return 0;
                }
            }
        }
        if (clone->ActionFrom && SlotIndex != 3)
        {
            // Composed effect: the action card's slot, run as the action card (its id-keyed parameters and ladders apply).
            const Row* action = ActionRow(clone->ActionFrom);
            fn = clone->SlotOverride[SlotIndex] ? reinterpret_cast<void*>(clone->SlotOverride[SlotIndex]) : action ? action->Slot[SlotIndex] : nullptr;
            impersonate = static_cast<uint16_t>(clone->ActionFrom);
            effect[0] = impersonate;
        }

        if constexpr (SlotIndex == 2)
        {
            // The required checks, each run as the row's card (they read the record's player / zone / card instance only).
            for (const uint64_t check : clone->Require)
            {
                effect[0] = static_cast<uint16_t>(clone->From);
                Clone* previousActive = g_Active;
                g_Active = clone;
                const uint64_t ok = reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(check)(effect, a2, a3, a4);
                g_Active = previousActive;
                if (!(ok & 0xFF))
                {
                    effect[0] = saved;
                    return 0;
                }
            }
            effect[0] = impersonate;
        }

        Clone* previous = g_Active;

        // Chaining: "before" steps are immediate effects (draw, life points) that finish in ONE call of their slot 0. They run ahead of
        // this card's own step: on every call when the own step is immediate too, else only on the first call of the resolution (the
        // engine's step variable 0x14349C13C is 128 there; an interactive step - search, revive, ... - is called again for each later step).
        if constexpr (SlotIndex == 0)
        {
            if (!clone->Pre.empty() && (IsImmediate(*clone) || *reinterpret_cast<const uint32_t*>(0x14349C13C) == 128))
            {
                for (Clone& step : clone->Pre)
                {
                    effect[0] = static_cast<uint16_t>(step.From);
                    const Row* stepRow = static_cast<const Row*>(orig_GetEntry(effect));
                    void* stepFn = stepRow ? stepRow->Slot[0] : nullptr;
                    g_Active = &step;
                    if (stepFn)
                        reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(stepFn)(effect, a2, a3, a4);
                }
                effect[0] = impersonate;
            }
        }

        g_Active = clone;
        uint64_t result = 0;
        if (fn)
            result = reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(fn)(effect, a2, a3, a4);
        else if constexpr (SlotIndex == 2)
        {
            if (clone->CostFrom || !clone->Require.empty())
                result = 2;   // the slot only exists for the cost / required checks, which passed; no condition of its own = allowed (as Slot_ReturnConst2)
        }
        g_Active = previous;
        effect[0] = saved;
        static std::unordered_map<uint64_t, int> calls;
        if (++calls[(static_cast<uint64_t>(saved) << 8) | SlotIndex] <= 3)
            Logger::WriteLog(std::format("Slot {} of custom id {} (source {}) called: step variable {}, result {}", SlotIndex, saved, clone->From,
                *reinterpret_cast<const uint32_t*>(0x14349C13C), static_cast<int64_t>(result)), MODULE_NAME, 0);
        return result;
    }

    void* const kThunks[5] = { reinterpret_cast<void*>(&SlotThunk<0>), reinterpret_cast<void*>(&SlotThunk<1>),
        reinterpret_cast<void*>(&SlotThunk<2>), reinterpret_cast<void*>(&SlotThunk<3>), reinterpret_cast<void*>(&SlotThunk<4>) };

    // ---- per-id ladders outside the slots ------------------------------------------------------------------------------------
    // Besides the table rows the engine keeps id-keyed ladders that it asks about an effect record when deciding whether an effect may be
    // offered / activated (spell speed, "is optional", timing class ...). Those see the custom id and answer "unknown card" (0), which makes the
    // gate refuse the effect. Each is wrapped so the source id is in the record while it runs (not needed inside a slot thunk, which already swapped).
    template <int N>
    struct IdLadder
    {
        static inline uint64_t(__fastcall* Orig)(uint16_t*, uint64_t, uint64_t, uint64_t) = nullptr;
        static uint64_t __fastcall Hook(uint16_t* effect, uint64_t a2, uint64_t a3, uint64_t a4)
        {
            if (!g_Active && effect)
            {
                bool ignore;
                if (Clone* clone = RouteByRow(Find(effect[0], ignore), effect))
                {
                    const uint16_t saved = effect[0];
                    effect[0] = static_cast<uint16_t>(clone->From);
                    const uint64_t result = Orig(effect, a2, a3, a4);
                    effect[0] = saved;
                    return result;
                }
            }
            return Orig(effect, a2, a3, a4);
        }
    };

    constexpr uintptr_t kLadderAddresses[] = { 0x1400A8F50, 0x1400A9790, 0x140108910, 0x14012FC50, 0x1400A8480, 0x1401088D0, 0x140131D30 };

    template <int... I>
    void AttachLadders(std::integer_sequence<int, I...>)
    {
        ((IdLadder<I>::Orig = reinterpret_cast<decltype(IdLadder<I>::Orig)>(kLadderAddresses[I]),
          DetourAttach(&(PVOID&)IdLadder<I>::Orig, IdLadder<I>::Hook)), ...);
    }

    // ---- plain id tests -----------------------------------------------------------------------------------------------------
    // Small pure functions of a card id (binary searches over sorted id lists: "has a hand effect", "is in the ... list"). The hand menu, the
    // action mask and the offer path call them with the raw id of the card, outside any slot or ladder that would already show the source id.
    // A custom id gets an arbitrary answer from them - Abyss Shark (id 14996) even landed in the hand-effect list of sub_140743FA0, because the
    // game's lists reach ids up to 15000 (word_140BF8BC0 holds 14996 - 14999) and so offered "activate" from the hand for a monster whose
    // effect is a summon trigger. So for a clone they answer for the source card, exactly as its row would.
    template <int N>
    struct IdTest
    {
        static inline int64_t(__fastcall* Orig)(int) = nullptr;
        static int64_t __fastcall Hook(int id)
        {
            if (!g_Active && id > 0 && id <= 0xFFFF)
            {
                bool ignore;
                if (Clone* clone = Find(static_cast<uint16_t>(id), ignore))
                    { int64_t best = 0; for (Clone* part : AllParts(*clone)) best = (std::max)(best, Orig(part->From)); return best; }   // any effect of the card answers yes
                if (IsPlainCustomId(static_cast<uint16_t>(id)))
                    return 0;
            }
            return Orig(id);
        }
    };

    // 743FA0 has a hand effect; 743F40 / 744080 the two id lists it is built from; 7440E0 / 744140 / 743120 other id lists the offer and action mask use.
    // 743030 = Kind_IsFlipMonster (KIND_TABLE category 4, or the 50-id list word_140BF7B90): the change-position routine (0x140084780) offers the card's
    // effect with event 13 only when it says yes, so a clone of a FLIP monster must answer yes whatever kind the custom card has (docs section 29).
    constexpr uintptr_t kIdTestAddresses[] = { 0x140743FA0, 0x140743F40, 0x140744080, 0x1407440E0, 0x140744140, 0x140743120, 0x140743030 };

    template <int... I>
    void AttachIdTests(std::integer_sequence<int, I...>)
    {
        ((IdTest<I>::Orig = reinterpret_cast<decltype(IdTest<I>::Orig)>(kIdTestAddresses[I]),
          DetourAttach(&(PVOID&)IdTest<I>::Orig, IdTest<I>::Hook)), ...);
    }

    // ---- the event trigger evaluator ----------------------------------------------------------------------------------------
    // sub_1400E26A0(event type) decides whether the card the CURRENT EVENT is about (a summon, a flip, a card sent to the grave ...) triggers an
    // effect: it takes that card's id (from the event record 0x14349C560, or the id of the card in the event's zone via sub_140047FF0) and tests
    // it against per-event id lists (word_140AD1FA8, word_140AF7510, word_140AF7D70, ... and a big switch). A custom id is in none of them, so a
    // custom monster never triggers however good its table row is. Its last act is to offer the effect through sub_1400AAE50 with a reference
    // whose low word is that same id.
    // So for the duration of the call the source card's id is shown wherever the evaluator reads the event's card (47FF0's result and the event
    // record's own id word), and 1400AAE50 gets the custom id back in its reference so the offer, the row lookup and the resolve all use the
    // custom card (and our wrapped row).
    struct EventSwap
    {
        bool Active = false;
        uint16_t Raw = 0;    // the id as the engine has it (the custom id, or the vanilla id it borrowed for this duel)
        uint16_t From = 0;   // the source card's id
        int Swaps = 0;       // how many times 47FF0 showed the source id (diagnostics)
        int Offers = 0;      // how many offers were made with the custom id restored
        int64_t OfferResult = 0;
    } g_Swap;

    uint32_t(__fastcall* orig_CardIdAt)(int, int, int) = reinterpret_cast<uint32_t(__fastcall*)(int, int, int)>(0x140047FF0);
    int64_t(__fastcall* orig_OfferByRef)(uint32_t, int16_t, uint32_t) = reinterpret_cast<int64_t(__fastcall*)(uint32_t, int16_t, uint32_t)>(0x1400AAE50);
    void(__fastcall* orig_EventEvaluator)(int) = reinterpret_cast<void(__fastcall*)(int)>(0x1400E26A0);

    uint32_t __fastcall Hook_CardIdAt(int player, int zone, int flag)
    {
        const uint32_t result = orig_CardIdAt(player, zone, flag);
        if (g_Swap.Active && static_cast<uint16_t>(result) == g_Swap.Raw)
        {
            ++g_Swap.Swaps;
            return (result & ~0xFFFFu) | g_Swap.From;
        }
        return result;
    }

    int64_t __fastcall Hook_OfferByRef(uint32_t ref, int16_t a2, uint32_t a3)
    {
        if (g_Swap.Active && static_cast<uint16_t>(ref) == g_Swap.From)
        {
            ref = (ref & ~0xFFFFu) | g_Swap.Raw;
            const int64_t result = orig_OfferByRef(ref, a2, a3);
            ++g_Swap.Offers;
            g_Swap.OfferResult = result;
            return result;
        }
        return orig_OfferByRef(ref, a2, a3);
    }

    // The first 0x40 bytes of the event record as 16-bit words, for the trace.
    std::string DumpEventRecord(const uint8_t* record)
    {
        std::string text;
        for (int i = 0; i < 0x40; i += 2)
            text += std::format("{}{:X}", i ? " " : "", *reinterpret_cast<const uint16_t*>(record + i));
        return text;
    }

    void __fastcall Hook_EventEvaluator(int eventType)
    {
        if (g_Swap.Active)
            return orig_EventEvaluator(eventType);

        // The event's card: the id word of the record when the record carries one (kind 3), else the card standing in the event's zone.
        uint8_t* record = reinterpret_cast<uint8_t*>(0x14349C560);
        const bool recordCarriesId = *reinterpret_cast<uint32_t*>(record + 0x26) == 3;
        const uint16_t raw = recordCarriesId ? *reinterpret_cast<uint16_t*>(record + 0xE) : static_cast<uint16_t>(orig_CardIdAt(*reinterpret_cast<uint16_t*>(record + 2), *reinterpret_cast<uint16_t*>(record + 4), 1));

        bool ignore;
        Clone* clone = raw ? RouteByTrigger(Find(raw, ignore), kSummonTriggers) : nullptr;

        // Trace: the event record of every custom card AND of every vanilla card a clone borrows from, side by side, to see which words differ.
        if (g_Trace && raw)
        {
            bool isSource = false;
            for (const auto& entry : g_Clones)
                isSource = isSource || entry.second.From == raw;
            static int dumps = 0;
            if ((clone || isSource) && ++dumps <= 40)
                Logger::WriteLog(std::format("  event {} card {}{} record: {}", eventType, raw, clone ? " (custom)" : " (source)", DumpEventRecord(record)), MODULE_NAME, 0);
        }
        if (!clone)
        {
            if (raw && IsPlainCustomId(raw))
                return;   // a custom card without an effect must not trigger a ghost card's effect
            return orig_EventEvaluator(eventType);
        }

        static int logged = 0;
        if (++logged <= 20)
            Logger::WriteLog(std::format("Event {} for custom card {} (id {}): evaluated as source card {}", eventType, raw, raw, clone->From), MODULE_NAME, 0);

        g_Swap = { true, raw, static_cast<uint16_t>(clone->From) };
        // Every word of the record that holds the card's id shows the source id (some evaluator branches compare the id with a second copy of it).
        int replaced[0x20], replacedCount = 0;
        for (int i = 0; i < 0x40; i += 2)
        {
            uint16_t& word = *reinterpret_cast<uint16_t*>(record + i);
            if (word == raw)
            {
                word = g_Swap.From;
                replaced[replacedCount++] = i;
            }
        }
        orig_EventEvaluator(eventType);
        for (int k = 0; k < replacedCount; ++k)
            *reinterpret_cast<uint16_t*>(record + replaced[k]) = raw;
        g_Swap.Active = false;
        if (logged <= 20)
            Logger::WriteLog(std::format("  event {}: id read from the {}, {} record word(s) and {} zone lookup(s) shown as source {}, {} offer(s) made (last result {})", eventType,
                recordCarriesId ? "event record" : "card in the event's zone", replacedCount, g_Swap.Swaps, g_Swap.From, g_Swap.Offers, g_Swap.OfferResult), MODULE_NAME, 0);
    }

    // ---- the "card moved" trigger evaluator -----------------------------------------------------------------------------
    // sub_1400A2F70(event type, &ref, ...) is the second per-card evaluator: it runs when a card is sent to the Graveyard or banished, with event 31
    // (from the hand / Deck, sub_1400A6F40) or 33 (from the field: destroyed, tributed ..., sub_1400A7750; a flag bit in ref marks "by battle").
    // ref: bits 0-8 card instance index, 9 player, 10-14 zone it came from, 21-25 zone it went to (16 GY, 17 banished). It reads the card's id from
    // the card instance table (0x143499798 + 8 * index, id = low 14 bits), checks a "sent to the GY" id list (word_140AD1ED0) and a big per-id ladder,
    // then offers the card's Kind 2 row (table bit 0x40) through Offer_EffectByCardRef. So, as for the summon evaluator, the instance shows the
    // source id while it runs and Hook_OfferByRef hands the offer the custom id back: the source's own ladder decides battle vs effect, GY vs banish.
    constexpr uintptr_t kCardInstances = 0x143499798;
    void(__fastcall* orig_MoveEvaluator)(int, uint32_t*, uint32_t, int) = reinterpret_cast<void(__fastcall*)(int, uint32_t*, uint32_t, int)>(0x1400A2F70);

    void __fastcall Hook_MoveEvaluator(int eventType, uint32_t* ref, uint32_t a3, int a4)
    {
        if (g_Swap.Active || !ref)
            return orig_MoveEvaluator(eventType, ref, a3, a4);

        uint32_t& instance = *reinterpret_cast<uint32_t*>(kCardInstances + 8 * static_cast<uintptr_t>(*ref & 0x1FF));
        const uint16_t raw = static_cast<uint16_t>(instance & 0x3FFF);
        bool ignore;
        Clone* clone = raw ? RouteByTrigger(Find(raw, ignore), kLeaveTriggers) : nullptr;
        // A custom card without a clone is NOT skipped here (unlike the summon evaluator): this evaluator may also let other cards react to the move.
        if (!clone)
            return orig_MoveEvaluator(eventType, ref, a3, a4);

        static int logged = 0;
        if (++logged <= 20)
            Logger::WriteLog(std::format("Move event {} for custom card {} (to zone {}): evaluated as source card {}", eventType, raw, (*ref >> 21) & 0x1F, clone->From), MODULE_NAME, 0);

        g_Swap = { true, raw, static_cast<uint16_t>(clone->From) };
        const uint32_t saved = instance;
        instance = (instance & ~0x3FFFu) | (static_cast<uint32_t>(clone->From) & 0x3FFF);
        orig_MoveEvaluator(eventType, ref, a3, a4);
        instance = saved;
        g_Swap.Active = false;
        if (logged <= 20)
            Logger::WriteLog(std::format("  move event {}: {} zone lookup(s) shown as source {}, {} offer(s) made (last result {})", eventType, g_Swap.Swaps, g_Swap.From, g_Swap.Offers, g_Swap.OfferResult), MODULE_NAME, 0);
    }

    // ---- trigger id lists searched through the generic helper ---------------------------------------------------------
    // When a monster leaves the field (destroyed by battle or by an effect, sent to the GY) sub_1400C2CA0 asks List_BinarySearchKonamiId
    // (0x140742D20: id, sorted u16 list, count) whether the card is in the "destroyed by battle and sent to the GY" list (0x140AF67E0, 133 ids:
    // Birdface, Giant Rat ...) or the "sent from the field to the GY" list (0x140AF5C60, 111 ids: Witch of the Black Forest ...). A custom id is in
    // neither, so the trigger is never offered. The helper is also used by Is_CardInNamedArchetype and UI code, so only these lists are answered
    // for the source card (a clone must keep its OWN archetypes).
    constexpr uintptr_t kTriggerLists[] = { 0x140AF67E0, 0x140AF5C60 };
    int64_t(__fastcall* orig_ListSearch)(int, const uint16_t*, int) = reinterpret_cast<int64_t(__fastcall*)(int, const uint16_t*, int)>(0x140742D20);

    int64_t __fastcall Hook_ListSearch(int id, const uint16_t* list, int count)
    {
        if (id > 0 && id <= 0xFFFF)
        {
            for (const uintptr_t trigger : kTriggerLists)
            {
                if (reinterpret_cast<uintptr_t>(list) != trigger)
                    continue;
                bool ignore;
                if (Clone* clone = Find(static_cast<uint16_t>(id), ignore))
                {
                    // Any effect of the card whose source is in the list (a multi-effect card's leave-field part).
                    static int logged = 0;
                    int64_t result = 0;
                    int answeredBy = clone->From;
                    for (Clone* part : AllParts(*clone))
                    {
                        result = orig_ListSearch(part->From, list, count);
                        answeredBy = part->From;
                        if (result & 0xFF)
                            break;
                    }
                    if (++logged <= 20)
                        Logger::WriteLog(std::format("Trigger list {:X} asked about custom card {}: answered as source {} ({})", trigger, id, answeredBy, result & 0xFF), MODULE_NAME, 0);
                    return result;
                }
                break;
            }
        }
        return orig_ListSearch(id, list, count);
    }

    // Cards in neither list are looked up next (same function, 0x1400C300F) in a {u16 id, u16 flags} table of "leaves the field" conditions
    // (0x140AF5D40, 676 rows: Superheavy Samurai Drum 11950 = 0x180C, Archfiend Heiress 10632 = 0x1200 ...) through List_BinarySearchRowByKonamiId
    // (0x140742D80: id, table, count, stride -> row or NULL). Only the tables below are answered with the source card's row; the helper has 51 callers.
    // The effective ATK/DEF calculation (sub_1400307C0) uses the same helper for four {card id -> ATK, DEF} tables keyed by the card that GAVE the
    // boost: Equip Spells (0x140ACEF20, 138 rows: Axe of Despair +1000), Union monsters (0x140ACF260, 12 rows, stride 8), "gains N ATK" effects
    // (0x140ACE2B0, 210 rows) and more boosts (0x140ACE7A0, 310 rows). Without these a custom Equip Spell cloning Axe of Despair gives +0.
    constexpr std::pair<uintptr_t, const char*> kRowTables[] = {
        { 0x140AF5D40, "leave-field" }, { 0x140ACEF20, "equip ATK/DEF" }, { 0x140ACF260, "union ATK/DEF" },
        { 0x140ACE2B0, "ATK/DEF boost A" }, { 0x140ACE7A0, "ATK/DEF boost B" } };
    const uint16_t*(__fastcall* orig_RowSearch)(int, const uint8_t*, int, int) = reinterpret_cast<const uint16_t*(__fastcall*)(int, const uint8_t*, int, int)>(0x140742D80);
    std::unordered_map<uint16_t, const Clone*> g_StatsByAction;   // action card id -> the first composed clone with its own "stats" (filled after loading)

    const uint16_t* __fastcall Hook_RowSearch(int id, const uint8_t* table, int count, int stride)
    {
        if (id > 0 && id <= 0xFFFF && !g_Active)
        {
            for (const auto& [address, what] : kRowTables)
            {
                if (reinterpret_cast<uintptr_t>(table) != address)
                    continue;
                bool ignore;
                if (Clone* found = Find(static_cast<uint16_t>(id), ignore))
                {
                    // The first effect of the card whose source has a row in this table.
                    static int logged = 0;
                    Clone* clone = found;
                    const uint16_t* row = nullptr;
                    for (Clone* part : AllParts(*found))
                    {
                        if ((row = orig_RowSearch(part->From, table, count, stride)) != nullptr)
                        {
                            clone = part;
                            break;
                        }
                    }
                    // The clone's own amounts for a stat table (every table here except the leave-field one is {id, ATK, DEF[, 0]}).
                    if (clone->HasStats && address != 0x140AF5D40 && row)
                        row = reinterpret_cast<const uint16_t*>(clone->StatRow);
                    if (++logged <= 30)
                        Logger::WriteLog(std::format("Table {} asked about custom card {}: answered as source {} (row {})", what, id, clone->From, row ? "found" : "none"), MODULE_NAME, 0);
                    return row;
                }
                // A composed boost ("actionFrom": Rush Recklessly) registers the boost under the ACTION card's id (its slot 0 runs as that card), so
                // the stat table is asked about the action card. The custom card's own amount answers for it (the first clone with "stats" using
                // that action card; a vanilla copy of the action card in the same duel would get that amount too - documented in section 37).
                if (address != 0x140AF5D40)
                {
                    auto own = g_StatsByAction.find(static_cast<uint16_t>(id));
                    if (own != g_StatsByAction.end() && orig_RowSearch(id, table, count, stride))
                        return reinterpret_cast<const uint16_t*>(own->second->StatRow);
                }
                break;
            }
        }
        return orig_RowSearch(id, table, count, stride);
    }

    // ---- per-card phase handlers ----------------------------------------------------------------------------------------
    // "During your Standby Phase", "during the End Phase", "at the end of the Battle Phase" effects are not found through an event at all: the phase
    // code walks fixed tables of 16-byte rows {u64 handler, u32 card id} and calls handler(player, cardId) for every row, four passes (player x 2).
    // The handler looks for that card on the field and offers its effect (Darklord Marie / Bowganian -> 0x1401491C0 -> event 2). A handler that
    // offers returns 0 and the phase step is re-run from the first row next tick (handlers remember what they already offered), so this hook keeps
    // that contract: after the source card's row it runs the same handler for every custom card cloning that source, and stops if one returns 0.
    // Tables: Duel__Phase__EnterStandbyPhase 0x140B110A0 (295 rows) + 0x140B11020 (8 rows); end of the Battle Phase (sub_14021F7A0) 0x140B2B2A0
    // (2 rows) + 0x140B2B2C0 (101 rows); End Phase (sub_1401558E0) 0x140B12310 (552 rows; the id is a u16 at +8, +0xA/+0xB are flag bytes)
    // + 0x140B14590 (19 rows). Rows are read as u32 at +8 and truncated to the u16 id.
    struct PhaseTable { uintptr_t Rows; int Count; };
    constexpr PhaseTable kPhaseTables[] = { { 0x140B110A0, 295 }, { 0x140B11020, 8 }, { 0x140B2B2A0, 2 }, { 0x140B2B2C0, 101 },
                                            { 0x140B12310, 552 }, { 0x140B14590, 19 } };

    using PhaseHandler_t = int64_t(__fastcall*)(uint64_t, uint64_t, uint64_t, uint64_t);
    using ActiveId_t = unsigned short(__cdecl*)(unsigned short);
    ActiveId_t g_ActiveId = nullptr;

    // The id a custom card plays under in the current duel (its own id, or the vanilla id it borrowed).
    uint16_t EngineIdFor(uint16_t customId)
    {
        if (!g_ActiveId)
        {
            if (HMODULE cards = GetModuleHandleA("Yu-Gi-Oh-MoreCards.dll"))
                g_ActiveId = reinterpret_cast<ActiveId_t>(GetProcAddress(cards, "Card_GetActiveDuelSessionId"));
            if (!g_ActiveId)
                return customId;
        }
        return g_ActiveId(customId);
    }

    struct PhaseHook
    {
        PhaseHandler_t Orig = nullptr;
        std::unordered_map<uint16_t, std::vector<uint16_t>> CustomBySource;   // source card id -> custom cards cloning it
    };
    constexpr int kMaxPhaseHooks = 48;
    PhaseHook g_PhaseHooks[kMaxPhaseHooks];
    int g_PhaseHookCount = 0;
    bool g_InPhaseExtra = false;

    template <int N>
    int64_t __fastcall PhaseThunk(uint64_t player, uint64_t id, uint64_t a3, uint64_t a4)
    {
        PhaseHook& hook = g_PhaseHooks[N];
        const int64_t result = hook.Orig(player, id, a3, a4);
        if (result == 0 || g_InPhaseExtra)
            return result;
        auto it = hook.CustomBySource.find(static_cast<uint16_t>(id));
        if (it == hook.CustomBySource.end())
            return result;
        for (const uint16_t custom : it->second)
        {
            const uint16_t engineId = EngineIdFor(custom);
            // A clone playing under its source's own id (Cards: BorrowSourceId) was already handled by the call above.
            if (engineId == 0 || engineId > 0x3FFF || engineId == static_cast<uint16_t>(id))
                continue;
            g_InPhaseExtra = true;
            const int64_t extra = hook.Orig(player, engineId, a3, a4);
            g_InPhaseExtra = false;
            static int logged = 0;
            if (extra == 0 && ++logged <= 20)
                Logger::WriteLog(std::format("Phase handler {:X} for custom card {} (as source {}) offered an effect", reinterpret_cast<uintptr_t>(hook.Orig), engineId, id & 0xFFFF), MODULE_NAME, 0);
            if (extra == 0)
                return 0;
        }
        return result;
    }

    template <int... I>
    constexpr std::array<void*, sizeof...(I)> MakePhaseThunks(std::integer_sequence<int, I...>)
    {
        return { reinterpret_cast<void*>(&PhaseThunk<I>)... };
    }
    const auto kPhaseThunks = MakePhaseThunks(std::make_integer_sequence<int, kMaxPhaseHooks>());

    // Detours every handler whose rows name a clone's source card. Called once the clones are loaded.
    void AttachPhaseHandlers()
    {
        std::map<uintptr_t, int> slotOf;
        for (const PhaseTable& table : kPhaseTables)
        {
            for (int r = 0; r < table.Count; ++r)
            {
                const uintptr_t fn = *reinterpret_cast<const uint64_t*>(table.Rows + 16 * static_cast<uintptr_t>(r));
                const uint16_t sourceId = static_cast<uint16_t>(*reinterpret_cast<const uint32_t*>(table.Rows + 16 * static_cast<uintptr_t>(r) + 8));
                for (auto& [customId, cardClone] : g_Clones)
                for (Clone* part : AllParts(cardClone))   // every effect of the card (multi-effect cards)
                {
                    if (part->From != sourceId)
                        continue;
                    auto slot = slotOf.find(fn);
                    if (slot == slotOf.end())
                    {
                        if (g_PhaseHookCount >= kMaxPhaseHooks)
                        {
                            Logger::WriteLog(std::format("Too many phase handlers to hook; custom card {} gets no phase trigger", customId), MODULE_NAME, 2);
                            continue;
                        }
                        slot = slotOf.emplace(fn, g_PhaseHookCount++).first;
                        g_PhaseHooks[slot->second].Orig = reinterpret_cast<PhaseHandler_t>(fn);
                    }
                    g_PhaseHooks[slot->second].CustomBySource[sourceId].push_back(customId);
                }
            }
        }
        for (int i = 0; i < g_PhaseHookCount; ++i)
            DetourAttach(&(PVOID&)g_PhaseHooks[i].Orig, kPhaseThunks[i]);
        if (g_PhaseHookCount)
            Logger::WriteLog(std::format("Hooked {} phase handler(s) for custom cards with Standby / End / Battle Phase effects", g_PhaseHookCount), MODULE_NAME, 0);
    }

    // ---- "named card on the field" offers -------------------------------------------------------------------------------------
    // Many per-event ladders offer a specific card by its id: "if Worm Illidan is on the field, offer it" (event 11, a card is Set), drawn
    // (28), added to the hand (29), Standby Phase extras and ~56 calls in the summon code. They call one of these helpers with a literal
    // card id; the helper looks for every copy of that card (field / S&T zones / Graveyard / linked zones) and offers it. A custom card is
    // never named, so after the call for a source card the hook calls the helper again for every custom card cloning it (its duel id).
    struct NamedHelper { uintptr_t Address; int IdArg; };   // IdArg: 1 = second argument (rdx), 2 = third (r8)
    constexpr NamedHelper kNamedHelpers[] = {
        { 0x1400AB360, 1 },   // Offer_EffectOfNamedCardOnField(player, id, event, param, excludeSlot, monstersOnly)
        { 0x1400AB5E0, 1 },   // Pendulum zones (player, id, event, param)
        { 0x1400AB750, 2 },   // monsters a Link points to (player, zone, id, event, param)
        { 0x1400ABA70, 2 },   // archetype 335 variant (player, zone, id, event, param)
        { 0x1400ABC30, 1 },   // field, sets the zone's "used" bit (player, id, event, param)
        { 0x1400ABE00, 1 },   // Spell & Trap zones (player, id, event, param)
        { 0x1400ABF60, 1 },   // Graveyard (player, id, event, param)
    };
    using Named_t = int64_t(__fastcall*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t);
    Named_t g_NamedOrig[std::size(kNamedHelpers)];
    std::unordered_map<uint16_t, std::vector<uint16_t>> g_CustomBySource;   // source card id -> custom cards cloning it
    bool g_InNamedExtra = false;

    template <int N>
    int64_t __fastcall NamedThunk(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6)
    {
        int64_t result = g_NamedOrig[N](a1, a2, a3, a4, a5, a6);
        if (g_InNamedExtra || g_Active)
            return result;
        const uint16_t sourceId = static_cast<uint16_t>(kNamedHelpers[N].IdArg == 1 ? a2 : a3);
        auto it = g_CustomBySource.find(sourceId);
        if (it == g_CustomBySource.end())
            return result;
        for (const uint16_t custom : it->second)
        {
            const uint16_t engineId = EngineIdFor(custom);
            if (engineId == 0 || engineId > 0x3FFF || engineId == sourceId)   // playing under the source's own id: already offered above
                continue;
            g_InNamedExtra = true;
            const int64_t extra = kNamedHelpers[N].IdArg == 1 ? g_NamedOrig[N](a1, engineId, a3, a4, a5, a6) : g_NamedOrig[N](a1, a2, engineId, a4, a5, a6);
            g_InNamedExtra = false;
            if (extra && (extra & 0xFF))
            {
                static int logged = 0;
                if (++logged <= 20)
                    Logger::WriteLog(std::format("Named offer {:X} for custom card {} (as source {}) offered an effect", kNamedHelpers[N].Address, engineId, sourceId), MODULE_NAME, 0);
                if (!result)
                    result = extra;
            }
        }
        return result;
    }

    template <int... I>
    void AttachNamedHelpers(std::integer_sequence<int, I...>)
    {
        for (auto& [customId, clone] : g_Clones)
            for (Clone* part : AllParts(clone))   // every effect of the card (multi-effect cards)
                g_CustomBySource[static_cast<uint16_t>(part->From)].push_back(customId);
        ((g_NamedOrig[I] = reinterpret_cast<Named_t>(kNamedHelpers[I].Address), DetourAttach(&(PVOID&)g_NamedOrig[I], reinterpret_cast<void*>(&NamedThunk<I>))), ...);
    }

    // ---- id lists searched inline -------------------------------------------------------------------------------------------
    // "When this card declares an attack" (event 18, sub_140086C00, 50 ids at 0x140AD11B0) and "if this card inflicts battle damage" (event 15,
    // sub_14009FB40, 27 ids at 0x140ACFF58) are binary searches written inline, with the card's id in r10d, followed (on a hit) by
    // Offer_EffectOfCardAtZone for the card in the zone - which already is the custom card. So only the id being searched for has to be the
    // source's: a generated stub at the start of each search maps r10d (clone -> source id), runs the 9 bytes it replaced and jumps back.
    // Installed with DetourAttach on the site (the trampoline goes unused).
    struct InlineListSite { uintptr_t Site; int Length; const char* What; };
    constexpr InlineListSite kInlineListSites[] = {
        { 0x140086CF8, 9, "attack declared" },   // mov r9d, ebp; mov r8d, 0x31   (list EventIdList_AttackDeclared_Ev18)
        { 0x14009FDC0, 9, "battle damage" },     // xor r9d, r9d; mov r8d, 0x1A   (list EventIdList_BattleDamage_Ev15)
    };

    uint32_t __cdecl MapInlineListId(uint32_t id)
    {
        const uint16_t raw = static_cast<uint16_t>(id);
        if (g_Active || !raw)
            return id;
        bool ignore;
        if (Clone* clone = RouteByTrigger(Find(raw, ignore), kBattleListTriggers))
        {
            static int logged = 0;
            if (++logged <= 20)
                Logger::WriteLog(std::format("Inline trigger list asked about custom card {}: searched as source {}", raw, clone->From), MODULE_NAME, 0);
            return static_cast<uint32_t>(clone->From);
        }
        return id;
    }

    void AttachInlineListStubs()
    {
        for (const InlineListSite& site : kInlineListSites)
        {
            std::vector<uint8_t> s = {
                0x9C, 0x50, 0x51, 0x52, 0x41, 0x50, 0x41, 0x51, 0x41, 0x53,   // pushfq; push rax, rcx, rdx, r8, r9, r11
                0x55, 0x48, 0x89, 0xE5,                                       // push rbp; mov rbp, rsp
                0x48, 0x83, 0xE4, 0xF0,                                       // and rsp, -16
                0x48, 0x81, 0xEC, 0x80, 0x00, 0x00, 0x00 };                   // sub rsp, 0x80 (imm32: shadow space + xmm0-5)
            for (uint8_t x = 0; x < 6; ++x)                                   // movdqu [rsp + 0x20 + 16x], xmmx (volatile, the C++ callee may use them)
                s.insert(s.end(), { 0xF3, 0x0F, 0x7F, static_cast<uint8_t>(0x44 | (x << 3)), 0x24, static_cast<uint8_t>(0x20 + 16 * x) });
            s.insert(s.end(), { 0x44, 0x89, 0xD1, 0x48, 0xB8 });              // mov ecx, r10d; mov rax, imm64
            const uint64_t fn = reinterpret_cast<uint64_t>(&MapInlineListId);
            s.insert(s.end(), reinterpret_cast<const uint8_t*>(&fn), reinterpret_cast<const uint8_t*>(&fn) + 8);
            s.insert(s.end(), { 0xFF, 0xD0, 0x41, 0x89, 0xC2 });              // call rax; mov r10d, eax
            for (uint8_t x = 0; x < 6; ++x)                                   // movdqu xmmx, [rsp + 0x20 + 16x]
                s.insert(s.end(), { 0xF3, 0x0F, 0x6F, static_cast<uint8_t>(0x44 | (x << 3)), 0x24, static_cast<uint8_t>(0x20 + 16 * x) });
            const uint8_t tail[] = {
                0x48, 0x89, 0xEC, 0x5D,                                       // mov rsp, rbp; pop rbp
                0x41, 0x5B, 0x41, 0x59, 0x41, 0x58, 0x5A, 0x59, 0x58, 0x9D }; // pop r11, r9, r8, rdx, rcx, rax; popfq
            s.insert(s.end(), std::begin(tail), std::end(tail));
            s.insert(s.end(), reinterpret_cast<const uint8_t*>(site.Site), reinterpret_cast<const uint8_t*>(site.Site) + site.Length);  // the replaced instructions
            const uint64_t back = site.Site + site.Length;
            const uint8_t jmp[] = { 0xFF, 0x25, 0, 0, 0, 0 };                // jmp [rip+0]
            s.insert(s.end(), std::begin(jmp), std::end(jmp));
            s.insert(s.end(), reinterpret_cast<const uint8_t*>(&back), reinterpret_cast<const uint8_t*>(&back) + 8);

            void* stub = VirtualAlloc(nullptr, s.size(), MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
            if (!stub)
                continue;
            memcpy(stub, s.data(), s.size());
            static PVOID targets[std::size(kInlineListSites)];
            PVOID& target = targets[&site - kInlineListSites];
            target = reinterpret_cast<PVOID>(site.Site);
            DetourAttach(&target, stub);
            Logger::WriteLog(std::format("Inline trigger list stub for {} at {:X}", site.What, site.Site), MODULE_NAME, 0);
        }
    }

    // ---- cost amounts -----------------------------------------------------------------------------------------------------
    // The cost functions ask the effect card for their amount by id: sub_1401F6F90 = how many cards to discard (Lightning Vortex: 1),
    // sub_1401F65D0 = how many life points to pay. While a composed cost runs (the record shows the cost card), the clone's own amount answers.
    int64_t(__fastcall* orig_DiscardCount)(uint16_t*) = reinterpret_cast<int64_t(__fastcall*)(uint16_t*)>(0x1401F6F90);
    int64_t(__fastcall* orig_LpCost)(uint16_t*) = reinterpret_cast<int64_t(__fastcall*)(uint16_t*)>(0x1401F65D0);

    int64_t __fastcall Hook_DiscardCount(uint16_t* effect)
    {
        if (g_Active && g_Active->CostFrom && g_Active->CostAmount >= 0 && effect && effect[0] == g_Active->CostFrom)
            return g_Active->CostAmount;
        return orig_DiscardCount(effect);
    }

    // How many Xyz Materials a detach cost (Cost 0x1401FF4D0, check 0x1400FBFF0) takes, asked by the effect card's id: the clone's own number
    // while its composed effect runs as the row card (Thunder End Dragon).
    int64_t(__fastcall* orig_DetachCount)(uint16_t*) = reinterpret_cast<int64_t(__fastcall*)(uint16_t*)>(0x1401F7AF0);

    int64_t __fastcall Hook_DetachCount(uint16_t* effect)
    {
        if (g_Active && g_Active->DetachCount > 0 && effect && effect[0] == g_Active->From)
            return g_Active->DetachCount;
        return orig_DetachCount(effect);
    }

    int64_t __fastcall Hook_LpCost(uint16_t* effect)
    {
        if (g_Active && g_Active->CostFrom && g_Active->CostAmount >= 0 && effect && effect[0] == g_Active->CostFrom)
            return g_Active->CostAmount;
        return orig_LpCost(effect);
    }

    // ---- negation ---------------------------------------------------------------------------------------------------------
    // Cond_NegateTargetMatches (0x1400FC410, slot 2 of Trap Jammer / Seven Tools, the effect check of Magic Jammer's delegate row) answers "can
    // this card negate that chain link" (effect, link). It first binary-searches NegateTable (0x140B0DAB0, 375 rows {u16 id, u16 what, u16 flags},
    // the only reader is this function) by the effect card's id, then runs per-id extras that return 1 for ids they do not name. A clone's own
    // "negate" is put into its source's row for the length of the call (the duel engine is single threaded) and its Spell/Trap property test
    // is run after the game's (the game has that one as a per-id ladder: Armor Break = Equip, World Suppression = Field ...).
    using NegateCheck_t = uint64_t(__fastcall*)(uint16_t*, uint16_t*);
    NegateCheck_t orig_NegateCheck = reinterpret_cast<NegateCheck_t>(0x1400FC410);
    uint16_t* const kNegateTable = reinterpret_cast<uint16_t*>(0x140B0DAB0);
    constexpr int kNegateRows = 375;
    auto const Kind_IsMonster = reinterpret_cast<int(__fastcall*)(int)>(0x140742DF0);                          // YGO::CARDS::Kind_IsMonster(konami id)
    auto const Get_SpellTrapProperty = reinterpret_cast<int(__fastcall*)(int16_t)>(0x14081A630);               // FULL_CARD_PROPS[id].SpellTrapProperty

    uint16_t* NegateRow(uint16_t id)
    {
        int lo = 0, hi = kNegateRows;
        while (lo < hi)
        {
            const int mid = (lo + hi) / 2;
            if (kNegateTable[3 * mid] < id) lo = mid + 1;
            else hi = mid;
        }
        return lo < kNegateRows && kNegateTable[3 * lo] == id ? kNegateTable + 3 * lo : nullptr;
    }

    uint64_t __fastcall Hook_NegateCheck(uint16_t* effect, uint16_t* link)
    {
        Clone* clone = g_Active;
        uint16_t* row = clone && clone->HasNegate && effect && effect[0] == clone->From ? NegateRow(effect[0]) : nullptr;
        if (!row)
            return orig_NegateCheck(effect, link);
        const uint16_t savedWhat = row[1], savedFlags = row[2];
        row[1] = clone->NegateWhat;
        row[2] = clone->NegateFlags;
        uint64_t result = orig_NegateCheck(effect, link);
        row[1] = savedWhat;
        row[2] = savedFlags;
        if ((result & 0xFF) && clone->NegateProperty >= 0 && link)
        {
            const int16_t id = static_cast<int16_t>(link[0]);
            if (Kind_IsMonster(id) || Get_SpellTrapProperty(id) != clone->NegateProperty)
                result = 0;
        }
        return result;
    }

    // "negate": { "what": "spell" | "trap" | "monster" | "spelltrap" | "any" | <card id>, "property": "normal" | "counter" | "field" | "equip" |
    // "continuous" | "quickplay" | "ritual", "activation": true, "opponentOnly": bool, "yourTurn" / "opponentTurn" / "battlePhase" /
    // "targetsOne": bool, "flags": raw (or'ed in) }
    bool BuildNegate(const nlohmann::json& j, Clone& c, std::string& error)
    {
        if (j.contains("what") && j["what"].is_number_integer())
            c.NegateWhat = static_cast<uint16_t>(j["what"].get<int>());
        else
        {
            static const std::pair<const char*, uint16_t> kWhat[] = { { "spell", 1 }, { "trap", 2 }, { "monster", 4 }, { "spelltrap", 3 }, { "any", 7 } };
            const std::string what = j.value("what", std::string("any"));
            for (const auto& [name, value] : kWhat)
                if (what == name) c.NegateWhat = value;
            if (!c.NegateWhat)
            {
                error = "\"what\" must be spell, trap, monster, spelltrap, any or a card id";
                return false;
            }
        }
        if (c.NegateWhat >= 8 && c.NegateWhat < 3000)
        {
            error = "\"what\" as a number must be a card id (3000 or more) or 1..7";
            return false;
        }
        if (j.contains("property"))
        {
            static const char* kProperty[] = { "normal", "counter", "field", "equip", "continuous", "quickplay", "ritual" };
            const std::string property = j.value("property", std::string());
            for (int i = 0; i < 7; ++i)
                if (property == kProperty[i]) c.NegateProperty = i;
            if (c.NegateProperty < 0)
            {
                error = "\"property\" must be normal, counter, field, equip, continuous, quickplay or ritual";
                return false;
            }
        }
        uint16_t flags = j.value("activation", true) ? 0x1 : 0;
        if (j.value("opponentOnly", false)) flags |= 0x2;
        if (j.value("yourTurn", false)) flags |= 0x10;
        if (j.value("opponentTurn", false)) flags |= 0x20;
        if (j.value("battlePhase", false)) flags |= 0x40;
        if (j.value("targetsOne", false)) flags |= 0x400;
        flags |= static_cast<uint16_t>(j.value("flags", 0));
        c.NegateFlags = flags;
        c.HasNegate = true;
        return true;
    }

    // One wrapped row per (custom card, source row); rows are never freed (the engine may hold the pointer).
    std::map<std::pair<const void*, uint16_t>, Row*> g_Wrapped;

    Row* WrappedRow(const Row* source, uint16_t customId, const Clone& clone)
    {
        auto key = std::make_pair(static_cast<const void*>(source), customId);
        auto it = g_Wrapped.find(key);
        if (it != g_Wrapped.end())
            return it->second;

        Row* wrapped = new Row(*source);
        wrapped->Id = customId;

        // Once per turn. The low nibble of the row's first Extra word is a USE-LIMIT class (docs/EffectSystem.md section 20): after an effect is
        // activated the engine runs the class's registration (Check_CanActivate_Driver's sibling 0x1400E0E50 -> 0x1401F8770 for class 5 adds a
        // (1001, card id) marker for the turn) and before offering it, Timing_Check_Class5_9 refuses while that marker exists. Both read the row through
        // the normal lookup, so a wrapped row with class 5 IS "once per turn, per card name". Limit classes the source happens to have (1, 5, 6, 9, 10)
        // are cleared first so a clone never inherits a restriction the script did not ask for.
        uint16_t& limit = wrapped->Extra[0];
        const uint16_t nibble = limit & 0xF;
        if (nibble == 1 || nibble == 5 || nibble == 6 || nibble == 9 || nibble == 10)
            limit &= ~static_cast<uint16_t>(0xF);
        if (clone.OncePerTurn)
            limit = (limit & ~static_cast<uint16_t>(0xF)) | 5;
        for (int i = 0; i < 5; ++i)
        {
            // An empty slot stays empty: the engine treats it as "none". A few slot functions are compared BY ADDRESS elsewhere in the engine (a row whose
            // slot 3 is Slot3_MonsterEffect_WithHooks is treated as a monster effect with hooks, sub_140100410 / sub_1400E0FE0 ...) and do not look at the
            // card id themselves, so they are handed over as they are; every other slot gets its impersonating wrapper.
            const uint64_t fn = reinterpret_cast<uint64_t>(source->Slot[i]);
            const bool identityTagged = i == 3 && (fn == 0x1401F94C0 || fn == 0x1401F9470 || fn == 0x1401F9630 || fn == 0x1401F9920 || fn == 0x1402007F0);
            // Composed effect: slots 0, 1, 2, 4 exist when the action card has them (SlotThunk runs them as the action card).
            const Row* action = clone.ActionFrom && i != 3 ? ActionRow(clone.ActionFrom) : nullptr;
            if ((clone.CostFrom && (i == 2 || i == 3)) || (i == 2 && !clone.Require.empty()))
                wrapped->Slot[i] = kThunks[i];   // the composed cost (slot 3) and its payability / required checks (slot 2)
            else if (clone.ActionFrom && i != 3)
                wrapped->Slot[i] = clone.SlotOverride[i] || (action && action->Slot[i]) ? kThunks[i] : nullptr;
            else
                wrapped->Slot[i] = clone.SlotOverride[i] ? kThunks[i] : !source->Slot[i] ? nullptr : identityTagged ? source->Slot[i] : kThunks[i];
        }
        g_Wrapped[key] = wrapped;
        return wrapped;
    }

    void* __fastcall Hook_GetEntry(uint16_t* effect)
    {
        if (g_Clones.empty())
            return orig_GetEntry(effect);

        bool borrowedWithoutClone;
        Clone* clone = RouteByRow(Find(effect[0], borrowedWithoutClone), effect);
        {
            // Trace: full call stack of the lookups that matter (a monster's event record, word3 7 / word4 1, and every custom-card lookup), once per card and caller.
            static std::set<std::tuple<uint16_t, uint16_t, uint16_t, uintptr_t>> traced;
            if (g_Trace && ((effect[3] == 7 && effect[4] == 1) || clone) && traced.size() < 200)
            {
                void* frames[10] = {};
                const USHORT n = CaptureStackBackTrace(0, 10, frames, nullptr);
                const uintptr_t caller = reinterpret_cast<uintptr_t>(frames[1]);
                if (traced.emplace(effect[0], effect[3], effect[4], caller).second)
                {
                    std::string stack;
                    for (USHORT k = 1; k < n; ++k)
                        stack += std::format(" {:X}", reinterpret_cast<uintptr_t>(frames[k]));
                    Logger::WriteLog(std::format("  trace id {} w2 {} w3 {} w4 {}{} stack:{}", effect[0], effect[2], effect[3], effect[4], clone ? " (custom)" : "", stack), MODULE_NAME, 0);
                }
            }
        }
        if (borrowedWithoutClone)
            return nullptr;
        if (!clone)
            return IsPlainCustomId(effect[0]) ? nullptr : orig_GetEntry(effect);

        Announce(*clone, effect[0]);
        LogLookup(effect);
        const uint16_t saved = effect[0];
        effect[0] = static_cast<uint16_t>(clone->From);
        const Row* source = static_cast<const Row*>(orig_GetEntry(effect));
        effect[0] = saved;
        {
            static int logged = 0;
            if (g_Trace && ++logged <= 12)
                Logger::WriteLog(source ? std::format("  source {} row found: extra {:04X} {:04X} {:04X}, slots {:X} {:X} {:X} {:X} {:X}", clone->From, source->Extra[0], source->Extra[1], source->Extra[2],
                    reinterpret_cast<uintptr_t>(source->Slot[0]), reinterpret_cast<uintptr_t>(source->Slot[1]), reinterpret_cast<uintptr_t>(source->Slot[2]),
                    reinterpret_cast<uintptr_t>(source->Slot[3]), reinterpret_cast<uintptr_t>(source->Slot[4]))
                    : std::format("  source {} has NO row for word4 {} word2 {}", clone->From, effect[4], effect[2]), MODULE_NAME, 0);
        }
        if (!source)
            return nullptr;

        const unsigned short real = BorrowedRealId(saved);
        return WrappedRow(source, real ? real : saved, *clone);
    }

    int64_t __fastcall Hook_GetDraw(uint16_t* effect)
    {
        if (g_Clones.empty())
            return orig_GetDraw(effect);

        // Inside a slot function of a clone the record already carries the source id: the clone's own draw count still applies.
        if (g_Active && g_Active->Draw >= 0)
            return g_Active->Draw;

        bool borrowedWithoutClone;
        Clone* clone = RouteByRow(Find(effect[0], borrowedWithoutClone), effect);
        if (borrowedWithoutClone)
            return 0;
        if (!clone)
            return orig_GetDraw(effect);

        if (clone->Draw >= 0)
            return clone->Draw;

        const uint16_t saved = effect[0];
        effect[0] = static_cast<uint16_t>(clone->From);
        const int64_t count = orig_GetDraw(effect);
        effect[0] = saved;
        return count;
    }

    // Inside a clone's slot function the record carries the source id, so the source's filter row would be found; hand back the clone's own.
    uint16_t* __fastcall Hook_GetRow(uint16_t id, int k)
    {
        if (g_Active && g_Active->HasFilter && k == 0)
            return g_Active->FilterRow;
        return orig_GetRow(id, k);
    }

    // "filter": { "side": "own|opp|any", "race": 1 | "attribute": 2 | "level_max": 4 | "level_min": 5 | "archetype": 425 | "card": 4335,
    //             "flags": <raw>, "param": <raw> }  - see docs/EffectSystem.md section 11. Returns false if nothing usable was given.
    bool BuildFilter(const nlohmann::json& f, Clone& c, std::string& error)
    {
        uint32_t flags = 0x1014;   // occupied, monster-zone class, face-up style check: what the vanilla "destroy all X" rows use
        // "target": true = the player picks ONE card ("Target 1 X; destroy it" rows such as Shield Crush / Remove Trap): adds the targetable
        // check (0x400); "kind": "spelltrap" uses the Spell & Trap zones (Mystical Space Typhoon's flags 0x41440) instead of the monsters'.
        if (f.value("target", false))
            flags = f.value("kind", std::string("monster")) == "spelltrap" ? 0x41440 : 0x1414;
        uint32_t param = 0;
        const std::string side = f.value("side", std::string("any"));
        if (side == "own") flags |= 1;
        else if (side == "opp" || side == "opponent") flags |= 2;   // "opponent" is what the WolfX compiler writes (EffectScript's side word)
        else if (side != "any") { error = "side must be own, opp or any"; return false; }

        // race / attribute may be given by name ("Dragon", "Winged Beast", "Earth"); the ids are the game's (Card.h in Yu-Gi-Oh-Cards)
        auto named = [](const char* key, const nlohmann::json& v, int& out) {
            static const std::pair<const char*, int> races[] = { {"dragon",1},{"zombie",2},{"fiend",3},{"pyro",4},{"seaserpent",5},{"rock",6},
                {"machine",7},{"fish",8},{"dinosaur",9},{"insect",10},{"beast",11},{"beastwarrior",12},{"plant",13},{"aqua",14},{"warrior",15},
                {"wingedbeast",16},{"fairy",17},{"spellcaster",18},{"thunder",19},{"reptile",20},{"psychic",21},{"wyrm",22},{"cyberse",23},
                {"divinebeast",24},{"creatorgod",25} };
            static const std::pair<const char*, int> attrs[] = { {"special",0},{"light",1},{"dark",2},{"water",3},{"fire",4},{"earth",5},
                {"wind",6},{"divine",7},{"spell",8},{"trap",9} };
            if (!v.is_string())
                return false;
            std::string n;
            for (char ch : v.get<std::string>())
                if (ch != ' ' && ch != '-' && ch != '_')
                    n += static_cast<char>(std::tolower(static_cast<unsigned char>(ch)));
            if (n.size() > 4 && n.compare(n.size() - 4, 4, "type") == 0)
                n.resize(n.size() - 4);
            const bool race = std::string(key) == "race";
            if (race)
            {
                for (const auto& e : races)
                    if (n == e.first) { out = e.second; return true; }
            }
            else
            {
                for (const auto& e : attrs)
                    if (n == e.first) { out = e.second; return true; }
            }
            return false;
        };
        auto kind = [&](const char* key, uint32_t top) {
            if (!f.contains(key))
                return false;
            int value = 0;
            if (f[key].is_number_integer())
                value = f[key].get<int>();
            else if (!named(key, f[key], value))
            {
                error = std::format("\"{}\" is not a known name or number", key);
                return false;
            }
            flags |= top;
            param = static_cast<uint32_t>(value);
            return true;
        };
        // one match kind per filter (top nibble of the flags)
        int chosen = 0;
        chosen += kind("archetype", 0x10000000);
        chosen += kind("card", 0x10000000);
        chosen += kind("kindCategory", 0x20000000);
        chosen += kind("race", 0x30000000);
        chosen += kind("attribute", 0x40000000);
        chosen += kind("level_max", 0x50000000);
        chosen += kind("level_min", 0x60000000);
        if (chosen > 1) { error = "give only one of archetype/card/kindCategory/race/attribute/level_max/level_min"; return false; }
        if (f.contains("flags") && f["flags"].is_number_integer())
            flags = f["flags"].get<uint32_t>();
        if (f.contains("param") && f["param"].is_number_integer())
            param = f["param"].get<uint32_t>();

        c.HasFilter = true;
        c.FilterRow[0] = static_cast<uint16_t>(c.From);
        c.FilterRow[1] = static_cast<uint16_t>(param);
        *reinterpret_cast<uint32_t*>(&c.FilterRow[2]) = 0;       // extra flags: none
        *reinterpret_cast<uint32_t*>(&c.FilterRow[4]) = flags;
        return true;
    }

    // ---- life point effects -------------------------------------------------------------------------------------------------
    // Slot0_AlterLifePoints (Red Medicine, Hinotama...) asks sub_14015AC90 for the change of each player (int[2], indexed by absolute
    // player; positive = gain, negative = damage) and applies it. For a clone with an "lp" block those numbers are replaced.
    int16_t __fastcall Hook_LpChanges(uint16_t* effect, int32_t* out)
    {
        const int16_t result = orig_LpChanges(effect, out);
        if (g_Active && g_Active->HasLp)
        {
            const int owner = effect[1] & 1;   // effect[1] is the controller
            out[0] = 0;
            out[1] = 0;
            out[owner] = g_Active->LpOwn;
            out[1 - owner] = g_Active->LpOpp;
        }
        return result;
    }

    // ---- deck search filters ------------------------------------------------------------------------------------------------
    // "Add 1 X from your Deck to your hand" effects list their candidates through Deck_CollectMatchingCards / Deck_CountMatchingCards,
    // which find a row in the game's deck filter table by card id and call its scan function (docs/EffectSystem.md section 12).
    // For a clone with a "deck" filter these two run the SAME scan function on a synthetic row instead.
    constexpr uintptr_t kMatchHeader = 0x14349C5C8;   // dword cleared per collect; the word at +2 is the number of matches
    constexpr uintptr_t kMatchList = 0x14349C5D8;     // 300 x u32 packed cards
    constexpr uintptr_t kMatchFlags = 0x14349CA88;    // 300 x u16
    constexpr uint64_t kDeckScanFn = 0x14052F2A0;     // the scan used by "search the Deck" rows (Reinforcement of the Army)

    Clone* DeckFilterClone(uint16_t id)
    {
        return (g_Active && g_Active->HasDeckFilter && (id == g_Active->From || (g_Active->ActionFrom && id == g_Active->ActionFrom))) ? g_Active : nullptr;
    }

    void RunDeckScan(const Clone& clone, int player)
    {
        using Scan_t = int64_t(__fastcall*)(const void*, uint32_t);
        reinterpret_cast<Scan_t>(*reinterpret_cast<const uint64_t*>(clone.DeckRow + 8))(clone.DeckRow, static_cast<uint32_t>(player));
    }

    int64_t __fastcall Hook_Collect(int player, uint16_t id, uint32_t param)
    {
        Clone* clone = DeckFilterClone(id);
        if (!clone)
            return orig_Collect(player, id, param);
        *reinterpret_cast<uint32_t*>(kMatchHeader) = 0;
        memset(reinterpret_cast<void*>(kMatchList), 0, 1200);
        memset(reinterpret_cast<void*>(kMatchFlags), 0, 600);
        RunDeckScan(*clone, player);
        return *reinterpret_cast<int16_t*>(kMatchHeader + 2);
    }

    int64_t __fastcall Hook_Count(int player, uint16_t id, uint32_t param)
    {
        Clone* clone = DeckFilterClone(id);
        if (!clone)
            return orig_Count(player, id, param);
        const int16_t before = *reinterpret_cast<int16_t*>(kMatchHeader + 2);
        RunDeckScan(*clone, player);
        const int16_t after = *reinterpret_cast<int16_t*>(kMatchHeader + 2);
        *reinterpret_cast<int16_t*>(kMatchHeader + 2) = before;
        return after - before;
    }

    // "deck": { "scan": "deck|grave|graveSummon|opponentGrave|bothGravesSummon", "type": "monster|spell|trap|any", "race": "Dragon", "attribute": "Dark", "level": 4, "levelOp": "le|ge|eq",
    //           "atk1500": true, "flags": <raw> }  - the row flags of the deck filter table (bit table in docs/EffectSystem.md section 12).
    bool BuildDeckFilter(const nlohmann::json& f, Clone& c, std::string& error)
    {
        static const std::pair<const char*, int> races[] = { {"dragon",1},{"zombie",2},{"fiend",3},{"pyro",4},{"seaserpent",5},{"rock",6},
            {"machine",7},{"fish",8},{"dinosaur",9},{"insect",10},{"beast",11},{"beastwarrior",12},{"plant",13},{"aqua",14},{"warrior",15},
            {"wingedbeast",16},{"fairy",17},{"spellcaster",18},{"thunder",19},{"reptile",20},{"psychic",21},{"wyrm",22},{"cyberse",23},
            {"divinebeast",24},{"creatorgod",25} };
        static const std::pair<const char*, int> attrs[] = { {"light",1},{"dark",2},{"water",3},{"fire",4},{"earth",5},{"wind",6},{"divine",7} };
        auto lookup = [](const nlohmann::json& v, const auto& table, int& out) {
            if (v.is_number_integer()) { out = v.get<int>(); return true; }
            if (!v.is_string()) return false;
            std::string n;
            for (char ch : v.get<std::string>())
                if (ch != ' ' && ch != '-' && ch != '_')
                    n += static_cast<char>(std::tolower(static_cast<unsigned char>(ch)));
            if (n.size() > 4 && n.compare(n.size() - 4, 4, "type") == 0)
                n.resize(n.size() - 4);
            for (const auto& e : table)
                if (n == e.first) { out = e.second; return true; }
            return false;
        };

        uint32_t flags = 0;
        const std::string type = f.value("type", std::string("any"));
        if (type == "monster") flags |= 0x2;
        else if (type == "spell") flags |= 0x4;
        else if (type == "trap") flags |= 0x8;
        else if (type != "any") { error = "deck.type must be monster, spell, trap or any"; return false; }

        int value = 0;
        if (f.contains("race"))
        {
            if (!lookup(f["race"], races, value) || value < 1 || value > 31) { error = "deck.race is not a known race"; return false; }
            flags |= 0x2u | (static_cast<uint32_t>(value) << 27);
        }
        if (f.contains("attribute"))
        {
            if (!lookup(f["attribute"], attrs, value) || value < 1 || value > 7) { error = "deck.attribute is not a known attribute"; return false; }
            flags |= 0x2u | (static_cast<uint32_t>(value) << 24);
        }
        if (f.contains("level"))
        {
            if (!f["level"].is_number_integer() || f["level"].get<int>() < 0 || f["level"].get<int>() > 15) { error = "deck.level must be 0..15"; return false; }
            const std::string op = f.value("levelOp", std::string("eq"));
            flags |= 0x2u | (static_cast<uint32_t>(f["level"].get<int>()) << 18);
            if (op == "le") flags |= 0x800000;
            else if (op == "ge") flags |= 0x400000;
            else if (op != "eq") { error = "deck.levelOp must be le, ge or eq"; return false; }
        }
        if (f.value("atk1500", false))
            flags |= 0x2000 | 0x2;

        // The row's "param" (word +2) is a name test on every candidate (docs/EffectSystem.md section 17): 1..2999 = the card belongs to that
        // archetype code, >= 3000 = the card is named like that game card id, a negative number = the card must NOT match. It is what makes
        // "Add 1 \"Dark World\" monster" (archetype 397...) or "Add 1 \"Polymerization\"" work.
        int16_t param = 0;
        if (f.contains("archetype") && f["archetype"].is_number_integer())
            param = static_cast<int16_t>(f["archetype"].get<int>());
        else if (f.contains("card") && f["card"].is_number_integer())
            param = static_cast<int16_t>(f["card"].get<int>());
        else if (f.contains("notArchetype") && f["notArchetype"].is_number_integer())
            param = static_cast<int16_t>(-f["notArchetype"].get<int>());
        if (f.contains("flags") && f["flags"].is_number_integer())
            flags = f["flags"].get<uint32_t>();

        // where the candidates come from: the scan function of the list filter row (names in IDA: YGO::Effects::Scan_*)
        uint64_t scanFn = kDeckScanFn;
        const std::string scan = f.value("scan", std::string("deck"));
        if (scan == "grave") scanFn = 0x14052F4D0;                  // Scan_Grave_Own
        else if (scan == "graveSummon") scanFn = 0x14052EC80;       // Scan_Grave_Own_SummonableOnly (revive)
        else if (scan == "opponentGrave") scanFn = 0x14052EE00;     // Scan_Grave_Opponent
        else if (scan == "bothGravesSummon") scanFn = 0x140531D70;  // Scan_Grave_Both_SummonableOnly (Monster Reborn)
        // 2026-10-01, the wrappers read in IDA (docs/EffectSystem.md section 33): zone + behaviour flag passed to the generic zone scanner.
        else if (scan == "deckSummon") scanFn = 0x14052EE80;        // Deck, flag 4: monsters that can be Special Summoned from the Deck
        else if (scan == "handSummon") scanFn = 0x14052EE40;        // hand, flag 2: monsters that can be Special Summoned from the hand
        else if (scan == "hand") scanFn = 0x14052F490;              // hand, flag 1: any card in the hand
        else if (scan == "banished") scanFn = 0x140532BF0;          // banished cards, flag 0x40
        else if (scan != "deck") { error = "deck.scan must be deck, deckSummon, hand, handSummon, grave, graveSummon, opponentGrave, bothGravesSummon or banished"; return false; }

        c.HasDeckFilter = true;
        uint8_t* row = c.DeckRow;
        *reinterpret_cast<uint16_t*>(row + 0) = static_cast<uint16_t>(c.From);
        *reinterpret_cast<int16_t*>(row + 2) = param;
        *reinterpret_cast<uint32_t*>(row + 4) = 0xFFFF;
        *reinterpret_cast<uint64_t*>(row + 8) = scanFn;
        *reinterpret_cast<uint32_t*>(row + 16) = flags;
        *reinterpret_cast<uint32_t*>(row + 20) = 0;
        return true;
    }

    std::string CardsJsonPath()
    {
        char exe[MAX_PATH]{};
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        std::string folder = exe;
        folder.resize(folder.find_last_of("\\/") + 1);
        return folder + "Yu-Gi-Oh-Ex\\cards.json";
    }

    // One step of an effect: "from" plus the optional draw / filter / lp / deck parameters.
    // "This card is in the hand": the zone byte of the card's position (sub_140044480(card instance) >> 8, as Special Summon-this-card 0x140161E20
    // reads it; 13 = hand). The hand-only summon donor (Watch Cat) would otherwise also be offered from the GY, where its machine does nothing.
    uint64_t __fastcall Check_InHand(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const uint32_t position = reinterpret_cast<uint32_t(__fastcall*)(int)>(0x140044480)(effect[11]);
        return ((position >> 8) & 0xFF) == 13 ? 1 : 0;
    }

    // "You can Tribute 1 monster": you control a monster that can be Tributed (the game's per-zone test sub_14001D700, as Cond_CanTributeSelf uses
    // it). The game's Tribute-1 cards each have their own conditions (Tribute Doll needs a Level 7 in the hand), so none is borrowed for this.
    uint64_t __fastcall Check_CanTributeOne(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const int player = effect[1] & 1;
        using CanTribute_t = int64_t(__fastcall*)(int, int, int, int);
        for (int zone = 0; zone <= 6; ++zone)
            if ((orig_CardIdAt(player, zone, 1) & 0x3FFF) != 0 && (reinterpret_cast<CanTribute_t>(0x14001D700)(player, player, zone, 1) & 0xFF))
                return 1;
        return 0;
    }

    // The game's checks by name (docs/EffectSystem.md section 37); each reads only the record (player, zone, card instance). 0 = unknown.
    uint64_t CheckByName(const std::string& name)
    {
        static const std::pair<const char*, uint64_t> checks[] = {
            { "canBanishSelfFromGrave", 0x1400FB960 },   // this card is in your GY and can be banished (Rose Lover, Destiny HERO - Malicious)
            { "canDiscardSelf", 0x1400F7D00 },           // this card is in your hand (F.A. Whip Crosser rule; Hecatrice, Thunder Dragon)
            { "canTributeSelf", 0x1400FAC80 },           // this card on the field can be Tributed (Planet Pathfinder)
            { "canSummonSelf", 0x1400FB9A0 },            // this card can be Special Summoned from where it is (Watch Cat, Cyber Dinosaur)
            { "noMonsters", 0x140242A80 },               // you control no monsters (Watch Cat, Summon Cloud)
            { "canSummonSelfFromGrave", 0x1400FA380 },   // this card in the GY can be Special Summoned (Quillbolt Hedgehog, Spore)
            // A card matching this effect's filter row ("filter") is on the field (Quillbolt Hedgehog's "you control a Tuner": the generic
            // target scan through Target_Generic_FromFilterRow, which reads the clone's own row through Hook_GetRow).
            { "controlsMatch", 0x1400FE510 },
            { "inHand", reinterpret_cast<uint64_t>(&Check_InHand) },   // this card is in the hand (the plugin's own check, see Check_InHand)
            { "canDetach", 0x1400FBFF0 },                // this Xyz Monster has enough materials to detach ("detach" of them; Thunder End Dragon)
            { "canTributeOne", reinterpret_cast<uint64_t>(&Check_CanTributeOne) },   // you control a monster that can be Tributed
        };
        for (const auto& [n, address] : checks)
            if (name == n)
                return address;
        return 0;
    }

    bool ParseStep(const nlohmann::json& clone, Clone& c, const std::string& label)
    {
        if (!clone.is_object() || !clone.contains("from") || !clone["from"].is_number_integer())
        {
            Logger::WriteLog(std::format("{}: an effect step needs {{ \"from\": <vanilla id> }}, skipped", label), MODULE_NAME, 2);
            return false;
        }
        const int from = clone["from"].get<int>();
        if (from < 1 || from >= kFirstExtraCardId)
        {
            Logger::WriteLog(std::format("{}: effect step from {} is not a game card, skipped", label, from), MODULE_NAME, 2);
            return false;
        }
        c.From = from;
        c.OncePerTurn = clone.value("oncePerTurn", false);
        if (clone.contains("trigger") && clone["trigger"].is_string())
            c.Trigger = clone["trigger"].get<std::string>();
        if (clone.contains("cost") && clone["cost"].is_object() && clone["cost"].contains("from") && clone["cost"]["from"].is_number_integer())
        {
            c.CostFrom = clone["cost"]["from"].get<int>();
            c.CostAmount = clone["cost"].value("amount", -1);
            if (clone["cost"].contains("check"))
            {
                c.CostCheck = CheckByName(clone["cost"].value("check", std::string()));
                if (!c.CostCheck)
                {
                    Logger::WriteLog(std::format("{}: unknown cost \"check\", skipped", label), MODULE_NAME, 2);
                    return false;
                }
            }
        }
        if (clone.contains("actionFrom") && clone["actionFrom"].is_number_integer())
            c.ActionFrom = clone["actionFrom"].get<int>();
        if (clone.contains("condition") && clone["condition"].is_string())
        {
            const std::string cond = clone["condition"].get<std::string>();
            if (cond == "always") c.SlotOverride[2] = 0x1400DE060;              // Slot_ReturnConst2
            else if (cond == "listHasMatch") c.SlotOverride[2] = 0x1400FA680;   // Cond_ListHasEnoughMatches (1 candidate by default)
            else
            {
                Logger::WriteLog(std::format("{}: unknown \"condition\" \"{}\" (always, listHasMatch), skipped", label, cond), MODULE_NAME, 2);
                return false;
            }
        }
        if (clone.contains("require") && clone["require"].is_array())
        {
            for (const nlohmann::json& name : clone["require"])
            {
                const std::string n = name.is_string() ? name.get<std::string>() : std::string();
                const uint64_t check = CheckByName(n);
                if (!check)
                {
                    Logger::WriteLog(std::format("{}: unknown \"require\" check \"{}\" (canBanishSelfFromGrave, canDiscardSelf, canTributeSelf, canSummonSelf, canSummonSelfFromGrave, noMonsters, controlsMatch, inHand, canDetach, canTributeOne), skipped", label, n), MODULE_NAME, 2);
                    return false;
                }
                c.Require.push_back(check);
            }
        }
        c.DetachCount = clone.value("detach", 0);
        if (clone.contains("negate") && clone["negate"].is_object())
        {
            std::string error;
            if (!BuildNegate(clone["negate"], c, error) || !NegateRow(static_cast<uint16_t>(from)))
            {
                Logger::WriteLog(std::format("{}: effectClone negate: {}, skipped", label,
                    error.empty() ? std::format("source {} is not a negation card (no NegateTable row; use Trap Jammer 5921 or Magic Jammer 4862)", from) : error), MODULE_NAME, 2);
                return false;
            }
        }
        if (clone.contains("stats") && clone["stats"].is_object())
        {
            c.HasStats = true;
            c.StatRow[0] = static_cast<int16_t>(from);
            c.StatRow[1] = static_cast<int16_t>(clone["stats"].value("atk", 0));
            c.StatRow[2] = static_cast<int16_t>(clone["stats"].value("def", 0));
        }
        if (clone.contains("draw") && clone["draw"].is_number_integer())
            c.Draw = clone["draw"].get<int>();
        if (clone.contains("filter") && clone["filter"].is_object())
        {
            std::string error;
            if (!BuildFilter(clone["filter"], c, error))
            {
                Logger::WriteLog(std::format("{}: effectClone filter: {}, skipped", label, error), MODULE_NAME, 2);
                return false;
            }
        }
        if (clone.contains("lp") && clone["lp"].is_object())
        {
            const nlohmann::json& lp = clone["lp"];
            c.HasLp = true;
            c.LpOwn = lp.value("gain", 0) - lp.value("selfDamage", 0);
            c.LpOpp = lp.value("opponentGain", 0) - lp.value("damage", 0);
        }
        if (clone.contains("deck") && clone["deck"].is_object())
        {
            std::string error;
            if (!BuildDeckFilter(clone["deck"], c, error))
            {
                Logger::WriteLog(std::format("{}: effectClone deck: {}, skipped", label, error), MODULE_NAME, 2);
                return false;
            }
        }
        return true;
    }

    void LoadClones()
    {
        const std::string path = CardsJsonPath();
        std::ifstream file(path);
        if (!file)
            return;

        nlohmann::json root = nlohmann::json::parse(file, nullptr, false, true);
        if (root.is_discarded())
            return;

        const nlohmann::json& list = root.is_array() ? root : root["cards"];
        if (!list.is_array())
            return;

        for (size_t i = 0; i < list.size(); ++i)
        {
            const nlohmann::json& entry = list[i];
            if (!entry.is_object() || !entry.contains("effectClone"))
                continue;

            const std::string label = std::format("cards.json entry {} (\"{}\")", i, entry.value("name", std::string()));
            const nlohmann::json& clone = entry["effectClone"];
            if (!entry.contains("id") || !entry["id"].is_number_integer() || !clone.is_object()
                || !clone.contains("from") || !clone["from"].is_number_integer())
            {
                Logger::WriteLog(std::format("{}: \"effectClone\" needs an integer \"id\" on the card and {{ \"from\": <vanilla id> }}, skipped", label), MODULE_NAME, 2);
                continue;
            }

            const int id = entry["id"].get<int>();
            const int from = clone["from"].get<int>();
            if (id < kFirstExtraCardId || id > kLastExtraCardId || from < 1 || from >= kFirstExtraCardId)
            {
                Logger::WriteLog(std::format("{}: effectClone from {} onto id {} is out of range (from must be a game card), skipped", label, from, id), MODULE_NAME, 2);
                continue;
            }

            Clone c;
            if (!ParseStep(clone, c, label))
                continue;
            bool chainOk = true;
            if (clone.contains("before") && clone["before"].is_array())
            {
                for (const nlohmann::json& stepJson : clone["before"])
                {
                    Clone step;
                    if (!ParseStep(stepJson, step, label) || !IsImmediate(step))
                    {
                        Logger::WriteLog(std::format("{}: \"before\" steps must be draw / life point effects, skipped", label), MODULE_NAME, 2);
                        chainOk = false;
                        break;
                    }
                    c.Pre.push_back(step);
                }
            }
            if (!chainOk)
                continue;
            // A card's further effects: "parts": [ { "from": ..., "trigger": ..., ... }, ... ] (each one step, its own source; see Clone::Parts).
            if (clone.contains("parts") && clone["parts"].is_array())
            {
                for (const nlohmann::json& partJson : clone["parts"])
                {
                    Clone part;
                    if (ParseStep(partJson, part, label))
                        c.Parts.push_back(std::move(part));
                }
                Logger::WriteLog(std::format("{}: {} effect(s) ({} borrowed from {})", label, 1 + c.Parts.size(), c.From,
                    [&] { std::string s; for (const Clone& p : c.Parts) s += std::format(", {}", p.From); return s; }()), MODULE_NAME, 0);
            }
            g_Clones[static_cast<uint16_t>(id)] = std::move(c);
        }
    }
}

size_t EffectClone::Count()
{
    return g_Clones.size();
}

void EffectClone::Setup()
{
    g_Trace = std::filesystem::exists(std::filesystem::path(Config::Get_WorkingDirectory()) / "trace.txt");
    if (g_Trace)
        Logger::WriteLog("trace.txt found: effect lookup tracing is on", MODULE_NAME, 0);
    LoadClones();
    Logger::WriteLog(std::format("Loaded {} custom effect clone(s)", g_Clones.size()), MODULE_NAME, 0);
    if (g_Clones.empty())
        return;
    for (auto& [id, clone] : g_Clones)
        for (Clone* part : AllParts(clone))
            if (part->HasStats && part->ActionFrom)
                g_StatsByAction.emplace(static_cast<uint16_t>(part->ActionFrom), part);

    DetourAttach(&(PVOID&)orig_GetEntry, Hook_GetEntry);
    DetourAttach(&(PVOID&)orig_GetDraw, Hook_GetDraw);
    DetourAttach(&(PVOID&)orig_GetRow, Hook_GetRow);
    DetourAttach(&(PVOID&)orig_LpChanges, Hook_LpChanges);
    DetourAttach(&(PVOID&)orig_Collect, Hook_Collect);
    DetourAttach(&(PVOID&)orig_Count, Hook_Count);
    AttachLadders(std::make_integer_sequence<int, 7>());
    AttachIdTests(std::make_integer_sequence<int, 7>());
    DetourAttach(&(PVOID&)orig_CardIdAt, Hook_CardIdAt);
    DetourAttach(&(PVOID&)orig_OfferByRef, Hook_OfferByRef);
    DetourAttach(&(PVOID&)orig_EventEvaluator, Hook_EventEvaluator);
    DetourAttach(&(PVOID&)orig_MoveEvaluator, Hook_MoveEvaluator);
    DetourAttach(&(PVOID&)orig_ListSearch, Hook_ListSearch);
    DetourAttach(&(PVOID&)orig_RowSearch, Hook_RowSearch);
    DetourAttach(&(PVOID&)orig_DiscardCount, Hook_DiscardCount);
    DetourAttach(&(PVOID&)orig_LpCost, Hook_LpCost);
    DetourAttach(&(PVOID&)orig_DetachCount, Hook_DetachCount);
    bool anyNegate = false;
    for (auto& [id, clone] : g_Clones)
        for (Clone* part : AllParts(clone))
            anyNegate |= part->HasNegate;
    if (anyNegate)
    {
        // NegateTable lives in .rdata; Hook_NegateCheck writes a source's row for the length of a call.
        DWORD old;
        if (VirtualProtect(kNegateTable, kNegateRows * 6, PAGE_READWRITE, &old))
            DetourAttach(&(PVOID&)orig_NegateCheck, Hook_NegateCheck);
        else
            Logger::WriteLog("NegateTable could not be made writable: \"negate\" is not applied", MODULE_NAME, 2);
    }
    AttachPhaseHandlers();
    AttachInlineListStubs();
    AttachNamedHelpers(std::make_integer_sequence<int, static_cast<int>(std::size(kNamedHelpers))>());
}
