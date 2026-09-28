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

    template <int SlotIndex>
    uint64_t __fastcall SlotThunk(uint16_t* effect, uint64_t a2, uint64_t a3, uint64_t a4)
    {
        bool borrowedWithoutClone;
        Clone* clone = Find(effect[0], borrowedWithoutClone);
        if (!clone)
            return 0;   // cannot happen for a row we built; a defensive default

        // The same source row the wrapper was built from (the record's other fields are unchanged, so the lookup picks the same row).
        const uint16_t saved = effect[0];
        effect[0] = static_cast<uint16_t>(clone->From);
        const Row* row = static_cast<const Row*>(orig_GetEntry(effect));
        void* fn = row ? row->Slot[SlotIndex] : nullptr;

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
                effect[0] = static_cast<uint16_t>(clone->From);
            }
        }

        g_Active = clone;
        uint64_t result = 0;
        if (fn)
            result = reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(fn)(effect, a2, a3, a4);
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
                if (Clone* clone = Find(effect[0], ignore))
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
                    return Orig(clone->From);
                if (IsPlainCustomId(static_cast<uint16_t>(id)))
                    return 0;
            }
            return Orig(id);
        }
    };

    // 743FA0 has a hand effect; 743F40 / 744080 the two id lists it is built from; 7440E0 / 744140 / 743120 other id lists the offer and action mask use.
    constexpr uintptr_t kIdTestAddresses[] = { 0x140743FA0, 0x140743F40, 0x140744080, 0x1407440E0, 0x140744140, 0x140743120 };

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
        Clone* clone = raw ? Find(raw, ignore) : nullptr;

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
            wrapped->Slot[i] = !source->Slot[i] ? nullptr : identityTagged ? source->Slot[i] : kThunks[i];
        }
        g_Wrapped[key] = wrapped;
        return wrapped;
    }

    void* __fastcall Hook_GetEntry(uint16_t* effect)
    {
        if (g_Clones.empty())
            return orig_GetEntry(effect);

        bool borrowedWithoutClone;
        Clone* clone = Find(effect[0], borrowedWithoutClone);
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
        Clone* clone = Find(effect[0], borrowedWithoutClone);
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
        else if (side == "opp") flags |= 2;
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
        return (g_Active && g_Active->HasDeckFilter && id == g_Active->From) ? g_Active : nullptr;
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
        else if (scan != "deck") { error = "deck.scan must be deck, grave, graveSummon, opponentGrave or bothGravesSummon"; return false; }

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
            g_Clones[static_cast<uint16_t>(id)] = c;
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

    DetourAttach(&(PVOID&)orig_GetEntry, Hook_GetEntry);
    DetourAttach(&(PVOID&)orig_GetDraw, Hook_GetDraw);
    DetourAttach(&(PVOID&)orig_GetRow, Hook_GetRow);
    DetourAttach(&(PVOID&)orig_LpChanges, Hook_LpChanges);
    DetourAttach(&(PVOID&)orig_Collect, Hook_Collect);
    DetourAttach(&(PVOID&)orig_Count, Hook_Count);
    AttachLadders(std::make_integer_sequence<int, 7>());
    AttachIdTests(std::make_integer_sequence<int, 6>());
    DetourAttach(&(PVOID&)orig_CardIdAt, Hook_CardIdAt);
    DetourAttach(&(PVOID&)orig_OfferByRef, Hook_OfferByRef);
    DetourAttach(&(PVOID&)orig_EventEvaluator, Hook_EventEvaluator);
}
