#include <algorithm>
#include <array>
#include <bit>
#include <optional>
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
#include <memory>
#include <span>
#include <unordered_map>
#include <vector>

#include <json.hpp>

#include "EffectClone.h"
#include "Logger.h"
#include "MasterDuelFunctionMap.h"
#include "Yu-Gi-Oh-Core.h"
#include "Yu-Gi-Oh-Memory.h"
#include "Yu-Gi-Oh-Mods.h"
#include "YuGiOh/YuGiOh-Addresses.h"
#include "YuGiOh/YuGiOh-DUELSTATE.h"

namespace
{
    constexpr int kFirstExtraCardId = 0x3BC4;   // 15300, as in Yu-Gi-Oh-Cards/Card.h
    constexpr int kLastExtraCardId = 0xFFFE;    // 65534, as in Yu-Gi-Oh-Cards/Card.h
    // A game card whose effect is overridden with none ("overridden": true and no "effectClone" in effects.json) clones this card:
    // Blue-Eyes White Dragon is a Normal Monster, in no effect table, id list, ladder or phase table, so every lookup answers "nothing".
    constexpr int kNoEffectSource = 4007;

    struct Row   // the game's 48-byte effect table row
    {
        uint16_t Id;
        uint16_t Extra[3];
        void* Slot[5];
    };
    static_assert(sizeof(Row) == 48, "effect table rows are 48 bytes");

    struct Clone
    {
        int From = 0;          // the vanilla card whose effect handlers are borrowed
        int Draw = -1;         // Get_NumberOfCardsToDraw's answer; -1 = whatever the source card draws
        bool HasFilter = false;   // replaces the source's generic filter row (table 0x140B16220, row k = 0), see FilterRow
        uint16_t FilterRow[6]{};  // the 12-byte row: id, param, flagsExtra (u32), flags (u32)
        std::vector<Clone> Pre;       // "before": steps that run first, in one call, ahead of this one (chaining, see SlotThunk<0>)
        bool OncePerTurn = false;     // "You can only use this effect once per turn" (per card name): the row's limit class becomes 5, see WrappedRow
        // "oncePerTurnPerCopy": "Once per turn: ..." - each copy on the field once per turn: limit class 1-4, a marker on the card's own zone
        // (UseLimit_Class1_RegisterZoneMarker1001 0x1401F8390; Timing_Check_Class1 0x1400F78E0 lets a card off the field through). OncePerTurn wins.
        bool OncePerCopy = false;
        // "oncePerDuel": classes 9 / 10 - the per-name keys 1001 / 1002 (Timing_Check_Class5_9 / 6_10), registered with marker lifetime 5
        // (UseLimit_Class9_RegisterDuelMarker1001 0x1401F88B0: msg 53 arg1 0x50D), which FlagMarkers_EndOfTurnSweep never removes.
        bool OncePerDuel = false;
        // "usesPerTurn": N uses per copy on the field per turn (per-copy classes 1-4; Hook_UseCount answers Get_UseLimitPerCopyCount 0x1401F64E0
        // with N, which Timing_Check_Class1..4 compare with the zone's marker count). 0 = not set.
        int UsesPerTurn = 0;
        // "summonLock": "You cannot Special Summon (from the Extra Deck) the turn you activate this, except ..." - activating it adds a
        // marker (player, 13, the card's id) for the turn (Hook_RegisterLimit); Hook_SummonRestrictions refuses the forbidden summons while it
        // is there, as the game's Summon_CheckPlayerRestrictions 0x140058440 does for its own cards. Kind: 0 any, 1 Fusion, 2 Synchro, 3 Xyz,
        // 4 Link, 5 Ritual. "when": "resolve" ("For the rest of this turn after this card resolves, ...") adds the marker when slot 0 has
        // resolved instead (EffectResolved), so a negated activation locks nothing.
        bool HasSummonLock = false, SummonLockExtraOnly = false, SummonLockOnResolve = false;
        int SummonLockKind = 0, SummonLockRace = 0, SummonLockAttribute = 0, SummonLockArchetype = 0;
        uint16_t ControlsName = 0;   // "controlsName": the card id the "controlsName" requirement looks for (Check_ControlsName)
        int LpAtMost = 0;            // "lpAtMost": the "lpAtMost" requirement's N ("If your LP are N or less", Check_LifePoints<0>)
        // "costPick": "banish N <filtered> cards from your GY (and this card)" - slot 3 is the game's Cost_BanishPickedCards (0x1401F99D0) run as
        // this sub-clone: its From is the id the machine runs under (The Beginning of the End 7452 = picks only, Laval Lakeside Lady 9585 = also
        // banishes this card from the GY), its deck filter answers the pick list, CostPickCount answers Get_CostPickCount_ById (docs s85).
        std::shared_ptr<Clone> CostPick;
        int CostPickCount = 0;
        // A raw pick cost (RawSlots on the CostPick part, docs/EffectSystem.md section 90): no donor id - the game's cost machine for the verb
        // runs under this card's own engine id, its count from the count hooks and its list / zone test from this part's row.
        //   Banish   Cost_BanishPickedCards 0x1401F99D0 (count Get_CostPickCount_ById), the part's list (GY / hand / banished ...)
        //   Discard  Cost_DiscardPickedHandCards 0x1401F8010 (count Get_DiscardCount_ById), hand picker asking the row per hand index
        //   SendHand Cost_SendPickedHandCardsToGrave 0x1401FF090 (count Get_SendHandCount_ById), the same hand picker
        //   Tribute  Cost_TributeOne 0x1401FDDF0 / Cost_TributePickedMonsters 0x1401FE930 (Get_TributeCount_ById), zone test in Hook_PickFilter
        //   Reveal   Cost_RevealPickedHandCards 0x140202770 (reveals 1 for an unknown id), the same hand picker
        //   SendHandOrField  Cost_SendPickedHandOrFieldCardToGrave 0x140208E00 (always 1): Pick_HandOrFieldFilter = the hand test per index for
        //            zone 13, Pick_ZoneFilter_BySlot2 (Hook_PickFilter: FieldZoneMatches on your side) for zones 0-12
        enum class PickVerb : uint8_t { Banish, Discard, SendHand, Tribute, Reveal, SendHandOrField };
        PickVerb Verb = PickVerb::Banish;
        bool CostSelf = false;   // a raw banish cost also banishes this card from the GY (the game's Laval Lakeside Lady extra, done by the plugin)
        // "quick": a Quick Effect (spell speed 2: usable in either player's turn and in a chain). Speed is by card id in the game
        // (Get_DefaultSpellSpeedForCard 0x140743C30: IdList_QuickEffectMonsters_783), so a composed effect would get its donor's speed;
        // the speed ladder (IdLadder 3, Get_EffectSpellSpeed_ByEffectId 0x14012FC50) answers 2 for it instead.
        bool Quick = false;
        // The limit class used for OncePerTurn / OncePerCopy. The engine has four independent once-per-turn keys per card name: classes 5, 6, 7, 8
        // = flag markers 1001-1004 in the player's list, value = card id (Timing_Check_Class5_9 / 6_10 / 7 / 8, UseLimit_Class5..8_Register*), and
        // four per copy: classes 1, 2, 3, 4 = the same keys in the card's own zone list (Timing_Check_Class1..4, UseLimit_Class1..4_RegisterZoneMarker*).
        // Each limited effect of a card, or each limit group, gets the next one of its kind (AssignLimitClasses), so "(1) ... once per turn;
        // (2) ... once per turn" are counted apart.
        uint16_t LimitClass = 5;
        // "limitGroup": effects with the same number share ONE once-per-turn limit: the bullets of "Once per turn: activate 1 of these
        // effects" (EffectScript bodies joined with 'or'). -1 = its own limit.
        int LimitGroup = -1;
        bool HasLp = false;           // replaces the life point changes of a Life Point effect (see Hook_LpChanges)
        int LpOwn = 0, LpOpp = 0;     // signed change for the effect's controller / the opponent (negative = damage)
        bool HasDeckFilter = false;   // replaces which Deck cards a "search the Deck" effect can find (see BuildDeckFilter)
        uint8_t DeckRow[24]{};        // synthetic row of the game's deck filter table (0x140AD2A80): id, param, -1, scan fn, flags
        // "except [this card's name]" ("deck": {"exceptSelf": true}, on by default from the compiler): the row's name test (param) becomes
        // minus the id the card plays under this duel, set just before the row is used (PrepareDeckRow). Only when the row has no other name
        // test (an archetype / card filter already uses the one param), then it is left out.
        bool ExceptSelf = false;
        bool ExceptSelfAfter = false;   // the name test is taken (archetype / card filter): this card's name is dropped from the matches instead
        uint16_t Owner = 0;           // the card this effect belongs to (custom id, or the overridden game card's id)
        std::wstring Label;           // "label": this effect's text in the choose-an-effect prompt (its name, or the compiler's summary)
        // "steps": immediate actions the plugin runs itself, in order, with the game's own functions (Slot0_RunSteps): draw / gain / damage
        // for own / opponent / both players. The row (how the card is activated) still comes from From.
        struct StepOp { int Op = 0; int N = 0; int Who = 0; };   // Op: 0 draw, 1 gain LP, 2 damage; Who: 0 own, 1 opponent, 2 both
        std::vector<StepOp> Steps;
        // "own": the plugin's own pick-and-act machine instead of the source card's slot 0 (Slot0_OwnPickAndAct): the list is built from the
        // "deck" filter when the step runs, the player picks, then 1 = each pick to the hand, 2 = each pick Special Summoned, 3 = sent to the
        // GY, 4 = banished (3 / 4: from the Deck, Extra Deck or hand; 4 also from the GY), 5 = destroyed (a card on the field, picked with the
        // game's field prompt instead of a list: Slot0_OwnFieldDestroy).
        int Own = 0;
        int OwnCount = 1;   // "ownCount": up to N picks, one after another (not for "summon"; section 95)
        // "move" (docs/EffectSystem.md section 86): the plugin's own move step Slot0_OwnMove. MoveScope 1 = the targets chosen at activation
        // (the donor row's slot 1, Target_Generic_FromFilterRow with this clone's "filter"), 2 = this card. MoveReason = the destination zone
        // the DestroyQueue takes (13 hand, 14 Extra Deck, 15 Deck, 16 GY, 17 banished), MoveFlags = its flags (2 | 0x2000000 destroy by an
        // effect, 0x800 Deck bottom, 0x4000000 shuffle the Deck afterwards, 0x80 face-down) - as the game's own target machines pass them.
        int MoveScope = 0;
        // "count" > 1: up to N targets with the game's multi-target picker (slot 4 = Twin Twisters' Slot4_MultiTargetPicker_StateMachine
        // 0x140094DC0: its maximum comes from Get_MultiTargetMaxCount_ById 0x140094490 = Hook_TargetCount, its zone test
        // Pick_ZoneFilter_MultiTarget 0x1400937A0 calls Pick_ZoneFilter_BySlot2 = Hook_PickFilter -> the target filter row)
        int MoveCount = 1;
        int MoveReason = 0;
        uint32_t MoveFlags = 0;
        // "targetDo": "atk" / "negate" (section 92): the result marker Slot0_OwnTargetResult puts on each target - key 1040 with the ATK change
        // ("amount", negative = loses), key 1027 = its effects negated, both until the end of the turn
        uint16_t ResultKey = 0;
        int ResultValue = 0;
        bool HandList = false;   // "deck.scan": "handSummon": an "own" step builds the hand list itself (OwnHandList), see Hook_Collect
        bool FieldPick = false;  // "deck.scan": "field": the cards on the field ("deck.side": own / opponent / any), tested by FieldZoneMatches
        int FieldSide = 2;       // 0 own, 1 opponent, 2 either side
        // "deck": {..., "or": [{...}, ...]}: more alternatives, one synthetic row each (each its own scan, so one list can span zones: a Special
        // Summon from the hand, Deck or GY); Collect / Count run them all and keep each card once. The game's own row-index callers
        // (Get_DeckFilterIndexForEffect, "is there a match" checks) only see the first. HandList: as Clone::HandList, for this alternative.
        struct AltRow { uint8_t Row[24]{}; bool ExceptSelf = false; bool ExceptSelfAfter = false; bool HandList = false; };
        std::vector<AltRow> OrRows;
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
        // "ownTrigger": no game card gives the trigger. The row is the plugin's own (this card's id, no use-limit / event words, no slots:
        // OwnTriggerRow) with the action composed onto it (ActionFrom = From when not given), and the plugin offers the card itself - for a
        // phase trigger through a virtual row appended to the game's phase table (AttachPhaseHandlers, docs/EffectSystem.md section 61).
        bool OwnTrigger = false;
        // No donor card at all (docs/EffectSystem.md section 88). "rawSlots": slots 0, 1, 2 and 4 are exactly the engine functions in
        // SlotOverride (an empty one stays empty) and run with the record's OWN id - no source row is asked, no id is swapped in, so no donor's
        // id-keyed ladder or list applies; the activation gate's ladders answer from the custom card's own kind (Get_DefaultSpellSpeedForCard,
        // Kind_IsMonster). "rawRow": the row is the plugin's own too (RawRowFits; with ownTrigger the own trigger's row) and there is no "from".
        bool RawSlots = false;
        bool RawRow = false;
        // The id the engine used for this card when one of its slots last ran (a custom id above 16383 plays under a scratch id): the hooks
        // that recognise "inside this clone's slot" by the record's id (MatchesFrom) compare a raw clone against it instead of a donor id.
        mutable uint16_t EngineId = 0;
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
        // "summonCondition": how the card must have been summoned for a summon trigger to fire (docs/EffectSystem.md section 46), checked
        // against the event record in Hook_EventEvaluator.
        struct SummonCondition
        {
            bool Any = false;          // a condition was given
            uint32_t FromZones = 0;    // bit per zone it came from (13 hand, 14 Extra Deck, 15 Deck, 16 GY, 17 banished, 7-11 Spell & Trap Zones); 0 = any
            enum class By { Any, Effect, NotEffect, Own, Spell, Trap, Monster, Card, Archetype } ByWhat = By::Any;
            int ByValue = 0;           // the card id (Card) or archetype code (Archetype)
            bool Proper = false;       // Fusion / Synchro / Xyz / Ritual / Link Summoned (the record's flag bit 0; the card's own kind says which)
            bool Pendulum = false;     // Pendulum Summoned (flag 0x40)
            int MinTributes = 0;       // Tribute Summoned with at least this many Tributes
            // the summoned monster itself, for the summon reactions ("If a Zombie monster(s) is Special Summoned to your field", section 87):
            // its Type / Attribute / archetype / kind (1 Fusion, 2 Synchro, 3 Xyz, 4 Link, 5 Ritual; 0 = any), and whose field it went to
            // (ToSide 0 = this card's controller's, 1 = the opponent's, -1 either)
            int CardRace = 0, CardAttribute = 0, CardArchetype = 0, CardKind = 0;
            int ToSide = -1;
        } Summon;
        // "moveCondition" (own move triggers, docs/EffectSystem.md section 65): where the card came from and how, checked against the move
        // reference Eval_CardMovedTriggers (0x1400A2F70) gets: bits 0-8 instance, 10-14 from zone, 16 from the field, 18 by battle, 21-25 to zone.
        struct MoveCondition
        {
            uint32_t FromZones = 0;    // bit per zone it left (0-6 Monster Zones, 7-12 Spell & Trap / Field, 13 hand, 15 Deck, 16 GY); 0 = any
            int Battle = -1;           // 1 = by battle, 0 = not by battle, -1 = either
        } Move;
        // "watch" (own watcher triggers, docs/EffectSystem.md section 91): ANOTHER card is destroyed / leaves the field / is sent to the GY / is
        // banished - card_destroyed / card_left_field (Eval_LeaveFieldTriggers' leave reference) and card_sent_to_grave / card_banished
        // (Eval_CardMovedTriggers' move reference). This card is offered by name from the field or, with InGrave, from the GY.
        struct WatchCondition
        {
            int Kind = 0;              // the moved card: 0 any, 1 monster, 2 Spell, 3 Trap
            int Race = 0, Attribute = 0, Archetype = 0, CardKind = 0;   // CardKind 1 Fusion, 2 Synchro, 3 Xyz, 4 Link, 5 Ritual
            uint16_t Name = 0;
            int Side = -1;             // whose card it was: 0 this card's controller's, 1 the opponent's, -1 either
            uint32_t FromZones = 0;    // bit per zone it left (0-6 Monster Zones, 7-12 Spell & Trap / Field, 13 hand, 15 Deck, 16 GY); 0 = any
            int By = 0;                // 0 any, 1 battle, 2 card effect, 3 an opponent's card effect
            bool Other = false;        // not a card with this card's id
            bool InGrave = false;      // this card watches from the GY ("..., while this card is in your GY")
        } Watch;
        // A card with several effects ("If Normal Summoned: ... If sent to the GY: ..."): each further effect borrows its own vanilla card.
        // The engine asks for a card's effects table by table (summon / FLIP rows, leave-field rows, ignition rows) and per event, so every
        // lookup is routed to the part whose source answers it (RouteByRow / RouteByTrigger). Parts have no parts of their own.
        std::vector<Clone> Parts;
        bool Announced = false;
    };

    std::unordered_map<uint16_t, Clone> g_Clones;
    // Game cards (ids below 15300) whose own effect is replaced: Yu-Gi-Oh-Ex\effects.json, entries with "overridden": true. They are clones
    // keyed by their own id, so every hook that serves a custom clone serves them too (docs/EffectSystem.md section 43).
    std::set<uint16_t> g_Overridden;

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

    Clone* g_Active = nullptr;     // the clone whose slot function is running (the draw hook needs its parameters)
    // The clone whose resolution (slot 0) is between steps: an interactive step (a "pick a card" screen) returns a next step and the game
    // runs the screen's per-card test OUTSIDE the slot, asking by the id the slot showed (the source / action card). Cleared when slot 0
    // returns 0 (resolved).
    Clone* g_Resolving = nullptr;
    // The raw pick cost whose machine is between its steps (slot 3 returned 0): the game's picker asks its per-card test (Pick_HandFilter_ByDeckRow,
    // Pick_ZoneFilter_BySlot2) and its counts by the card's own id outside the slot, and that id would otherwise find the effect's own list.
    // Cleared when the machine finishes (slot 3 returns 1) or the effect resolves.
    Clone* g_Paying = nullptr;
    constexpr uintptr_t kCostStep = 0x14349C148;   // g_CostStep (stru_14349B580 + 0xBC8): the running cost machine's state
    uint16_t g_ShowingSource = 0;  // the source id a ladder / evaluator hook is showing the engine for a clone, while it runs

    // While a clone runs as its source card (a slot, ladder or evaluator shows the source id), a lookup of that source is the GAME's card:
    // when the source is itself an overridden game card its override must not answer, or a clone of it would run the override's source.
    bool ShownAsSource(uint16_t id)
    {
        if (!g_Overridden.contains(id))
            return false;
        return id == g_ShowingSource || (g_Active && (id == g_Active->From || id == g_Active->ActionFrom || id == g_Active->CostFrom));
    }

    Clone* Find(uint16_t id, bool& borrowedWithoutClone)
    {
        borrowedWithoutClone = false;
        const unsigned short real = BorrowedRealId(id);
        if (!real && ShownAsSource(id))
            return nullptr;
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

    // The native record copier preserves word 20 verbatim, including in compact
    // chain records. Keep the native sub-effect number in its low byte.
    Clone* g_MenuPart = nullptr;
    uint16_t ChoiceTag(Clone& root, const Clone* part)
    {
        const auto parts = AllParts(root);
        for (size_t i = 0; i < parts.size() && i < 127; ++i)
            if (parts[i] == part) return static_cast<uint16_t>(0x8000 | ((i + 1) << 8));
        return 0;
    }

    struct NativeChoiceScope
    {
        uint16_t* Record;
        uint16_t Tag;
        explicit NativeChoiceScope(uint16_t* record) : Record(record), Tag(record[20] & 0xFF00)
        { if (Tag & 0x8000) record[20] &= 0xFF; else Tag = 0; }
        ~NativeChoiceScope() { if (Tag) Record[20] = Tag | (Record[20] & 0xFF); }
    };

    // Does an effect compiled for `trigger` belong to the event in an effect record's word3 (docs/EffectSystem.md sections 29-30)?
    bool TriggerFitsEvent(const std::string& trigger, uint16_t event)
    {
        switch (event)
        {
        case 7: case 9: return trigger == "normal_summoned" || trigger == "special_summoned" || trigger == "summoned" || trigger == "normal_or_special_summoned"
            || trigger == "opponent_summoned" || trigger == "monster_summoned"
            || (event == 9 && (trigger == "opponent_special_summoned" || trigger == "monster_special_summoned"));
        case 8: return trigger == "flip" || trigger == "summoned";
        case 12: case 13: case 21: return trigger == "flip";   // 12 / 13: Duel_ChangePosition_OfferFlip_Ev12or13 (0x140084E50), 21: battle
        // 31 / 32 / 33: a card moved to the GY OR banished (the move reference's to zone tells which; own move triggers check it, MoveMatches)
        case 31: case 32: case 33: return trigger == "sent_to_grave" || trigger == "sent_from_field_to_grave" || trigger == "destroyed_by_battle" || trigger == "banished"
            || trigger == "card_sent_to_grave" || trigger == "card_banished";   // another card moved (watchers, section 91)
        // whose phase is decided by the donor's phase handler (Standby / End Phase handler families, docs/EffectSystem.md section 60)
        case 2: return trigger == "standby_phase" || trigger == "opponent_standby_phase" || trigger == "each_standby_phase";
        case 6: return trigger == "end_phase" || trigger == "opponent_end_phase" || trigger == "each_end_phase" || trigger == "grave_end_phase" || trigger == "banished_end_phase";
        case 15: return trigger == "battle_damage";
        case 18: case 19: return trigger == "attack_declared" || trigger == "opponent_attack_declared" || trigger == "attacked";
        // 22 is also Eval_LeaveFieldTriggers' "destroyed" event (30 sent / banished by Card_Destroy, 34 any other leave: not known events)
        case 22: return trigger == "destroys_by_battle" || trigger == "card_destroyed" || trigger == "card_left_field";
        case 20: return trigger == "damage_step_battles" || trigger == "damage_step_attacks";   // start of the Damage Step (section 80)
        // start / end of the Battle Phase (section 93): Battle_StartOfBattlePhaseStep's event 4, the end-of-Battle-Phase handlers' event 24
        case 4: return trigger == "battle_phase_start" || trigger == "your_battle_phase_start" || trigger == "opponent_battle_phase_start";
        case 24: return trigger == "battle_phase_end" || trigger == "your_battle_phase_end";
        default: return false;
        }
    }

    // The events TriggerFitsEvent knows. A clone compiled for one trigger has no effect on the OTHER known events, even when its source card
    // has one there (Black Mamba borrows Edge Imp Chain for "declares an attack", and Edge Imp Chain also searches when sent to the GY:
    // in-duel 2026-10-02 the clone searched from the GY too). Other events (0 = no event: activation checks) are left alone.
    bool IsKnownTriggerEvent(uint16_t event)
    {
        switch (event)
        {
        case 2: case 4: case 6: case 7: case 8: case 9: case 12: case 13: case 15: case 18: case 19: case 20: case 21: case 22: case 24: case 31: case 32: case 33: return true;
        default: return false;
        }
    }

    // Is `trigger` one of the event triggers above (summoned, sent to the GY, FLIP, ...)? "ignition", "in_grave" and no trigger are not: they are
    // activated by the player, with no event in the record.
    bool IsEventTrigger(const std::string& trigger)
    {
        for (uint16_t event : { 2, 4, 6, 7, 8, 9, 12, 13, 15, 18, 19, 20, 21, 22, 24, 31, 32, 33 })
            if (TriggerFitsEvent(trigger, event))
                return true;
        return false;
    }

    // ---- choosing between effects of the same kind ----------------------------------------------------------------------------
    // Two effects a lookup cannot tell apart (two ignition effects, two effects for the same event) are chosen by the PLAYER, the way the game's
    // own "(1) ... (2) ..." cards work (Rainbow Dragon 7171; docs/EffectSystem.md section 45): the card is offered once, its slot 3
    // (Slot3_EffectChoice_SelectThenCost 0x1401FCB00 for game cards) shows the "select an effect" prompt with the usable effects, and from then on
    // every lookup of that activation means the chosen effect. The game keeps the choice in the effect record (word19 - 1 = the row,
    // Effect_GetRowIndexForRecord 0x1400DE070); a borrowed source's machines may use that word for themselves, so the plugin keeps it apart,
    // per card instance (word11), until the effect resolves.
    std::map<std::pair<uint16_t, uint16_t>, int> g_Choices;      // (custom id, card instance) -> index in AllParts
    std::map<std::pair<uint16_t, uint16_t>, size_t> g_Sequence;   // (custom id, card instance) -> the "before" step running (Pre.size() = its own step)
    std::map<std::pair<uint16_t, uint16_t>, uint32_t> g_SequenceStart;   // ... and the step value its resolution started with
    std::set<std::pair<uint16_t, uint16_t>> g_ChoicePrompted;    // the prompt is up; the answer is read on the next slot 3 call
    struct { bool Active = false; std::pair<uint16_t, uint16_t> Key; int Index = 0; } g_Probe;   // ComputeChoiceMask trying one effect
    uint16_t g_PromptId = 0;    // while the prompt is built: the card it is for ...
    int g_PromptCount = 0;      // ... and how many effects it lists (EffectChoice_GetCount's answer)
    // The option texts of the last prompt per card (engine id): option n = the n-th candidate, drawn by the UI after the prompt is built.
    std::map<uint16_t, std::vector<std::wstring>> g_PromptLabels;

    std::pair<uint16_t, uint16_t> ChoiceKey(const uint16_t* effect)
    {
        const unsigned short real = BorrowedRealId(effect[0]);
        return { static_cast<uint16_t>(real ? real : effect[0]), effect[11] };
    }

    // The effect chosen for this record's card (or being tried), as an index in AllParts; -1 = none yet. The record's own tag (word20, written by
    // the effect menu or by ChoiceSlot3) comes first: it travels with the record into the chain, as the game's word19 does for its own cards,
    // while the (card, instance) key alone lost the choice of a Spell activated from the hand (issue #38: choice 2 resolved as choice 1).
    int ChosenIndex(const uint16_t* effect)
    {
        const auto key = ChoiceKey(effect);
        if (g_Probe.Active && g_Probe.Key == key)
            return g_Probe.Index;
        if (effect[20] & 0x8000)
            if (const int tagged = ((effect[20] >> 8) & 0x7F) - 1; tagged >= 0)
                return tagged;
        auto it = g_Choices.find(key);
        return it == g_Choices.end() ? -1 : it->second;
    }

    // An effect of this card resolved: forget its choices under every instance, since the activation's key (a Spell in the hand) need not be the
    // resolving record's. A copy still waiting in the chain keeps its choice in its own tag.
    void ForgetChoices(const uint16_t* effect)
    {
        const uint16_t card = ChoiceKey(effect).first;
        std::erase_if(g_Choices, [card](const auto& entry) { return entry.first.first == card; });
    }

    // An own-trigger effect (Clone::OwnTrigger) has a row only in the monster effect table (word4 1): for the events its trigger fits (the
    // lookup Offer_EffectOfCardAtZone's offer makes: table 1 + the event), and, as a donor's row would, for events the plugin does not place
    // (0 and the like, lookups made while the activation / resolution runs). Never for another known event.
    // A move trigger's own row lives in table 2 (the leave-the-field / GY table, as Eval_CardMovedTriggers' offers ask: 0x40 << 16 = word4 2).
    bool IsMoveTrigger(const std::string& trigger)
    {
        return trigger == "sent_to_grave" || trigger == "sent_from_field_to_grave" || trigger == "destroyed_by_battle" || trigger == "banished";
    }

    // Watchers: another card was destroyed / left the field / was sent to the GY / was banished (docs/EffectSystem.md section 91).
    bool IsWatchTrigger(const std::string& trigger)
    {
        return trigger == "card_destroyed" || trigger == "card_left_field" || trigger == "card_sent_to_grave" || trigger == "card_banished";
    }

    // A hand effect ("If this card is in your hand: You can ...") is looked up in table 2 too: sub_140108610 sets word4 = 2 for a hand monster
    // that Id_HasHandEffect (0x140743FA0) accepts (IdTest<0> answers yes for an own "in_hand" effect).
    // An own summon trigger and the summon evaluator's event (section 99): 7 Normal Summon, 8 Flip Summon, 9 Special Summon. For game cards
    // its id lists (EventEval_IdList_Event7_A / _Event9 / _Event7or9) split them; TriggerFitsEvent does not, a donor's lists used to.
    bool OwnSummonFits(const std::string& trigger, int event)
    {
        if (trigger == "normal_summoned") return event == 7;
        if (trigger == "special_summoned") return event == 9;
        if (trigger == "normal_or_special_summoned") return event == 7 || event == 9;
        return event >= 7 && event <= 9;   // summoned
    }
    bool IsOwnSummonTrigger(const std::string& trigger)
    {
        return trigger == "normal_summoned" || trigger == "special_summoned" || trigger == "summoned" || trigger == "normal_or_special_summoned";
    }

    bool OwnRowFits(const Clone& part, const uint16_t* effect)
    {
        if (part.OwnTrigger && IsOwnSummonTrigger(part.Trigger) && effect && effect[4] == 1 && effect[3] >= 7 && effect[3] <= 9)
            return OwnSummonFits(part.Trigger, effect[3]);
        // A field ignition / Quick Effect is table 3 (EffectTable_Kind3): YGO::Effects::Check_CanActivateKind3AtZone (0x140107930) sets word4 3
        // for a card on the field and asks Has_Kind3EffectEntry (0x1400DFD70, a plain lookup with word2 = the zone), then the speed / offer
        // gates - no id list, so the own row is offered by the game's own activation menu. A plain no-cost ignition row is {id, limit class,
        // slot 0, -, slot 2, -, -} (Aquaactress Tetra 11916), which is what the own row plus the composed action is. No event: the record has none.
        if (part.Trigger == "ignition")
            return part.OwnTrigger && effect && effect[4] == 3 && !IsKnownTriggerEvent(effect[3]);
        // (the GY / banished End Phase triggers are offered from those zones: table 2 too)
        // (a watcher in the GY is offered by Offer_EffectOfNamedCardInGrave: table 2 too)
        // (in_grave, section 102: Check_CanActivateKind3AtZone sets word4 2 for a card past the field and goes straight to the speed / offer
        // gates - no id list for the GY, so the own row is offered by the game's own GY activation; "inGrave" keeps it out of the banished zone)
        const bool table2 = IsMoveTrigger(part.Trigger) || part.Trigger == "in_hand" || part.Trigger == "in_grave" || part.Trigger == "grave_end_phase" || part.Trigger == "banished_end_phase"
            || (IsWatchTrigger(part.Trigger) && part.Watch.InGrave);
        return part.OwnTrigger && effect && effect[4] == (table2 ? 2 : 1)
            && (TriggerFitsEvent(part.Trigger, effect[3]) || !IsKnownTriggerEvent(effect[3]));
    }

    // A raw row without a trigger (Clone::RawRow, section 88) is a Spell / Trap card's effect: the Spell/Trap table (word4 0), which the
    // lookup also uses for selector 2 with word3 40 (Get_EffectTableEntryForCard) - the lookups a Spell donor's row answered.
    bool RawRowFits(const Clone& part, const uint16_t* effect)
    {
        return part.RawRow && !part.OwnTrigger && effect && (effect[4] == 0 || (effect[4] == 2 && effect[3] == 40));
    }

    // The plugin's own row (own trigger or raw row) instead of a donor's; OwnRowAnswers = it answers this lookup.
    bool UsesOwnRow(const Clone& part) { return part.OwnTrigger || part.RawRow; }
    bool OwnRowAnswers(const Clone& part, const uint16_t* effect) { return part.OwnTrigger ? OwnRowFits(part, effect) : RawRowFits(part, effect); }

    // Is `id` the record id this clone's slots run under? The donor id for a donor clone; the card's own engine id for a raw one (section 88).
    bool MatchesFrom(const Clone* c, int id)
    {
        return c && ((c->From && id == c->From) || (c->RawSlots && c->EngineId && id == c->EngineId));
    }

    // Does a move (Eval_CardMovedTriggers' reference) fire this own move trigger? The trigger names the destination (banished: zone 17, the
    // others: the GY, 16), sent_from_field_to_grave / destroyed_by_battle need the from-field bit (16) and the latter the by-battle bit (18);
    // "moveCondition" narrows the zone it left and battle / effect.
    bool MoveMatches(const Clone& part, uint32_t ref)
    {
        const uint32_t to = (ref >> 21) & 0x1F, from = (ref >> 10) & 0x1F;
        const bool fromField = (ref >> 16) & 1, battle = (ref >> 18) & 1;
        if (to != (part.Trigger == "banished" ? 17u : 16u))
            return false;
        if ((part.Trigger == "sent_from_field_to_grave" || part.Trigger == "destroyed_by_battle") && !fromField)
            return false;
        if (part.Trigger == "destroyed_by_battle" && !battle)
            return false;
        if (part.Move.FromZones && !(part.Move.FromZones & (1u << from)))
            return false;
        if (part.Move.Battle >= 0 && battle != (part.Move.Battle == 1))
            return false;
        return true;
    }

    // The plugin's own row for a custom card: its id, no use-limit / event words, no slots (WrappedRow composes the action's slots onto it).
    const Row* OwnTriggerRow(uint16_t id)
    {
        static std::map<uint16_t, Row*> rows;
        Row*& row = rows[id];
        if (!row)
        {
            row = new Row{};
            row->Id = id;
        }
        return row;
    }

    // The parts of a multi-effect card that answer this lookup (same table selector word4, index word2, event word3): those whose source has a
    // real row for it, narrowed to the ones whose trigger fits the record's event when any does (a FLIP and a Normal Summon effect share a table).
    // Table 0 returns a generic Spell/Trap row even for a source without one; such a part is only used when no part has a real row.
    std::vector<Clone*> Candidates(Clone* clone, uint16_t* effect)
    {
        std::vector<Clone*> rows, fitting, generic;
        if (!clone || clone->Parts.empty() || !effect)
            return rows;
        const uint16_t saved = effect[0];
        for (Clone* part : AllParts(*clone))
        {
            if (UsesOwnRow(*part))
            {
                // the plugin's own row answers exactly its trigger's lookups (OwnRowFits), a raw row the Spell/Trap table's (RawRowFits)
                if (OwnRowAnswers(*part, effect))
                {
                    rows.push_back(part);
                    fitting.push_back(part);
                }
                continue;
            }
            effect[0] = static_cast<uint16_t>(part->From);
            if (const Row* row = static_cast<const Row*>(orig_GetEntry(effect)))
            {
                if (row->Id != part->From)
                {
                    generic.push_back(part);
                    continue;
                }
                if (!part->Trigger.empty() && part->Trigger != "ignition" && IsKnownTriggerEvent(effect[3])
                    && !TriggerFitsEvent(part->Trigger, effect[3]))
                    continue;
                rows.push_back(part);
                if (TriggerFitsEvent(part->Trigger, effect[3]))
                    fitting.push_back(part);
            }
        }
        effect[0] = saved;
        if (!fitting.empty())
            return fitting;
        // Event 0 = an activation check (ignition, from the GY / hand): no event offered it, so an event-trigger part can't be meant. Without this a
        // "sent from the field to the GY" part (Botanical Girl's Kind2 row) listed before a banish-from-GY part (Rose Lover's Kind2 row) won the
        // lookup below and the GY effect was never offered. Only when another part is left (a record that lost its event still finds its effect).
        if (effect[3] == 0 && std::any_of(rows.begin(), rows.end(), [](const Clone* part) { return !IsEventTrigger(part->Trigger); }))
            std::erase_if(rows, [](const Clone* part) { return IsEventTrigger(part->Trigger); });
        // An event the plugin can't place: never ask the player to choose between effects of DIFFERENT triggers (a FLIP and a Standby effect
        // both have table 1 rows); the effects sharing the first one's trigger stay.
        if (rows.size() > 1)
            std::erase_if(rows, [&](const Clone* part) { return part->Trigger != rows.front()->Trigger; });
        return !rows.empty() ? rows : generic;
    }

    // The part of a multi-effect card this lookup means: the part picked from the effect menu, or the one tagged in the record (word20 bit 15,
    // index in bits 8-14); else the only candidate, else the one the player chose at the "select an effect" prompt, else the first.
    Clone* RouteByRow(Clone* clone, uint16_t* effect)
    {
        if (!clone || clone->Parts.empty() || !effect)
            return clone;
        const std::vector<Clone*> all = AllParts(*clone);
        if (g_MenuPart && std::find(all.begin(), all.end(), g_MenuPart) != all.end())
            return g_MenuPart;
        if (effect[20] & 0x8000)
        {
            const size_t index = ((effect[20] >> 8) & 0x7F);
            if (index && index <= all.size()) return all[index - 1];
            return nullptr;
        }
        const std::vector<Clone*> pool = Candidates(clone, effect);
        if (pool.empty())
            return clone;
        if (pool.size() > 1)
        {
            const int chosen = ChosenIndex(effect);
            if (chosen >= 0 && chosen < static_cast<int>(all.size()) && std::find(pool.begin(), pool.end(), all[chosen]) != pool.end())
                return all[chosen];
        }
        return pool.front();
    }

    constexpr const char* kSummonTriggers[] = { "normal_summoned", "special_summoned", "summoned", "normal_or_special_summoned", "flip" };    constexpr const char* kLeaveTriggers[] = { "sent_to_grave", "sent_from_field_to_grave", "destroyed_by_battle" };
    constexpr const char* kBattleListTriggers[] = { "attack_declared", "battle_damage" };

    // Explicit triggers take precedence. Raw {from, parts} clones have no trigger metadata:
    // choose a source with an actual row in the event's table, rather than always the main source.
    // A generic Spell/Trap fallback is not evidence of a monster event effect.
    Clone* RouteByTrigger(Clone* clone, std::span<const char* const> triggers, int table = -1)
    {
        if (!clone || clone->Parts.empty())
            return clone;
        for (Clone* part : AllParts(*clone))
            for (const char* t : triggers)
                if (part->Trigger == t)
                    return part;
        if (table >= 0)
        {
            for (Clone* part : AllParts(*clone))
            {
                if (!part->Trigger.empty())
                    continue;
                if (part->RawRow)
                {
                    if (table == 0)
                        return part;   // a raw row is a Spell/Trap table row (RawRowFits)
                    continue;
                }
                uint16_t record[0x20]{};
                record[0] = static_cast<uint16_t>(part->From);
                record[4] = static_cast<uint16_t>(table);
                const Row* row = static_cast<const Row*>(orig_GetEntry(record));
                if (row && row->Id == part->From)
                    return part;
            }
            return nullptr;
        }
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
        Logger::WriteLog(g_Overridden.contains(id) ? std::format("Game card {} plays with its overridden effect (from card {})", id, clone.From)
                                                   : std::format("Custom card {} borrows the effect of vanilla card {}", id, clone.From), MODULE_NAME, 0);
        if (g_Trace)
        {
            // Card type class of the custom card vs its source: many engine gates index small per-type tables with it (word_140BF7820 + 12 * type).
            for (const unsigned cardId : { static_cast<unsigned>(BorrowedRealId(id) ? BorrowedRealId(id) : id), static_cast<unsigned>(clone.From) })
            {
                const int type = reinterpret_cast<int(__fastcall*)(int)>(YGO::Addr::Card_GetTypeFromFullCardPropsByKonamiId)(static_cast<int>(cardId));
                const uint16_t* w = reinterpret_cast<const uint16_t*>(YGO::Addr::CardFrameTable + 12 * static_cast<uintptr_t>(type));
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

    // Draw and life point effects resolve completely within a single call (no player interaction), so they can be run back to back.
    bool IsImmediate(const Clone& c)
    {
        return !c.Steps.empty() || c.HasLp || c.Draw >= 0 || c.From == 4844 /* Pot of Greed */ || c.From == 4345 /* Red Medicine */ || c.From == 4350 /* Hinotama */;
    }

    // ---- the plugin's own resolver ("steps") --------------------------------------------------------------------------------
    // The game's action functions, called directly (docs/EffectSystem.md sections 49 / 50). Each finishes in one call, so a list of them is one
    // resolve step: the slot returns 0 (done) after running them all.
    const auto Draw_ExecuteDraw = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, int, int, uint32_t)>(0x1400A1C10);         // (record, player, count, flags)
    const auto LP_ApplyGain = reinterpret_cast<uint32_t(__fastcall*)(uint16_t*, int, int)>(0x1400A15F0);                      // LP_ApplyGain_Internal (record, side, amount)
    const auto LP_ApplyDamage = reinterpret_cast<uint32_t(__fastcall*)(uint16_t*, int, int)>(0x14009E4C0);                    // LP_ApplyDamage_Internal (record, side, amount)

    uint64_t __fastcall Slot0_RunSteps(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const Clone* clone = g_Active;
        if (!clone || !effect)
            return 0;
        const int controller = effect[1] & 1;   // the record's player (as LP_ApplyChangeFromParamTable reads it)
        for (const Clone::StepOp& step : clone->Steps)
        {
            for (int player = 0; player < 2; ++player)
            {
                const bool own = player == controller;
                if ((step.Who == 0 && !own) || (step.Who == 1 && own))
                    continue;
                switch (step.Op)
                {
                case 0: Draw_ExecuteDraw(effect, player, step.N, 0); break;
                case 1: LP_ApplyGain(effect, player, step.N); break;
                case 2: LP_ApplyDamage(effect, player, step.N); break;
                }
            }
        }
        return 0;
    }

    // ---- the plugin's own pick-and-act machine ("own") -----------------------------------------------------------------------
    // No card's slot 0: the steps of the game's search machine (Slot0_AddFromDeckToHand_StateMachine 0x14016D5A0) with the act swapped per
    // Clone::Own. The list is collected when the step runs (not at activation), so a card an earlier step put in the hand can be picked.
    // The record's id (effect[0]) is the step's source, which routes the collect hook to this step's "deck" filter (DeckFilterClone).
    const auto Get_DeckFilterParam = reinterpret_cast<uint32_t(__fastcall*)(uint16_t*)>(0x14008CE70);         // Get_DeckFilterParamForEffect_Ladder
    const auto Deck_Collect = reinterpret_cast<int64_t(__fastcall*)(int, int, uint32_t)>(0x1400C0290);         // Deck_CollectMatchingCards (hooked: our filter)
    const auto UI_ShowNoneMessage = reinterpret_cast<void(__fastcall*)(int, uint32_t)>(0x14005E940);          // (player, 0x15): "no card to choose"
    const auto UI_ShowPrompt = reinterpret_cast<void(__fastcall*)(int, uint32_t)>(0x14005E8D0);               // UI_ShowPromptText (player, 0x33)
    const auto UI_BeginListSelect = reinterpret_cast<int64_t(__fastcall*)(int, int, int, uint32_t)>(0x14005DE00);   // (player, 6 = pick 1, id, param)
    const auto Resolution_BeginScope = reinterpret_cast<void(__fastcall*)(int)>(0x1400A81A0);
    const auto Card_MoveListCardToHand = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, int, uint32_t*, int)>(0x14008A3F0);   // (record, player, card, 1)
    const auto Chain_AddEffectLink = reinterpret_cast<int64_t(__fastcall*)(int16_t, uint32_t*, int16_t, int16_t, uint32_t, int16_t)>(0x1400F2450);
    const auto Msg_EndOfListEffect = reinterpret_cast<void(__fastcall*)(int)>(0x1400A7DD0);                    // message 86 (sub_1400A7DD0)
    const auto Net_SendSimple = reinterpret_cast<int64_t(__fastcall*)(int, int, void*, int)>(0x14000E990);     // Duel__NetEvent__SendSimple
    // The movers of Foolish Burial's machine (Slot0_SelectFromListAndMove_StateMachine 0x140178460, step 125), both raise the moved events:
    // Deck (15) / Extra Deck (14): (player, from zone, 0 = GY / 1 = banish, card instance, flags 0, effect id); the hand: (player, card instance,
    // flags 0 = "sent" (event 33, 0x4000 = discard, event 32), 16 = GY / 17 = banish, effect id).
    const auto Move_NonFieldCardToGraveOrBanish = reinterpret_cast<int64_t(__fastcall*)(int, int, int, int, uint32_t, int)>(0x1400A6F40);   // _Ev31
    const auto Move_HandCardToGraveOrBanish = reinterpret_cast<int64_t(__fastcall*)(int, int, uint32_t, uint32_t, int)>(0x140088D90);       // _Ev32
    const auto Card_GetPositionByInstance = reinterpret_cast<uint32_t(__fastcall*)(int)>(0x140044480);   // zone in bits 8-15 (13 hand, 14 Extra, 15 Deck, 16 GY)
    // Banish rules. Can_PlayerBanish (0x14005C630, player): not under Imperial Iron Wall / the opponent's Chaos Hunter / flag 11243; the Deck /
    // hand movers fall back to the GY without it, so an own banish step checks it first. Can_BanishFromGrave (0x14005C700, player, the GY's
    // owner): that, plus flag 5248, the opponent's Kycoo the Ghost Destroyer, Necrovalley (unless the owner has Gravekeeper's Chief).
    const auto Can_PlayerBanish = reinterpret_cast<int64_t(__fastcall*)(int)>(0x14005C630);
    const auto Can_BanishFromGrave = reinterpret_cast<int64_t(__fastcall*)(int, uint32_t)>(0x14005C700);
    // A GY card by an effect, as the "banish the targeted GY cards" machine (0x140178110) does it: the card's slot in the duel state
    // (Card_GetSlotPtrByInstance 0x140044C10), then Effect_BanishGraveCard (0x140089D30: record, banishing player, slot, flags; it adds
    // 0x400 "by an effect" and calls Move_GraveCardToBanished 0x140089E70, which raises the moved event).
    const auto Card_GetSlotPtrByInstance = reinterpret_cast<uint32_t*(__fastcall*)(int)>(0x140044C10);
    const auto Effect_BanishGraveCard = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, int, uint32_t*, int)>(0x140089D30);
    // To the Deck / Extra Deck (docs/EffectSystem.md section 86), as Pot of Avarice's machine (Slot0_ShuffleTargetsIntoDeckThen_ById 0x140228930)
    // and Celestial Observatory's cost (Cost_PlaceHandOrFieldCardOnDeck 0x1402087F0) do it: Move_GraveCardToDeck (0x1400889A0: actor, GY owner,
    // instance, effect id, top 1 / bottom 0, force Extra Deck; an Extra Deck monster goes to the Extra Deck by itself), Hand_ReturnCardToDeck
    // (0x1400888F0: player, hand index, top), then Deck_PushShuffleMessages (0x1400A7E00, message 86) when the Deck is shuffled.
    const auto Move_GraveCardToDeck = reinterpret_cast<int64_t(__fastcall*)(int, uint32_t, int, int, int, int)>(0x1400889A0);
    const auto Hand_ReturnCardToDeck = reinterpret_cast<void(__fastcall*)(int, int, int)>(0x1400888F0);
    const auto Hand_FindIndexOfInstance = reinterpret_cast<int(__fastcall*)(int, int)>(0x140044BB0);
    const auto Deck_PushShuffleMessages = reinterpret_cast<int64_t(__fastcall*)()>(0x1400A7E00);
    // To the hand from the GY / banished cards (the salvage machine Slot0_ApplyToEachSelectedTarget_StateMachine's movers): (record, owner, the
    // card's slot). Card_MoveListCardToHand only takes Deck cards (Deck_FindIndexOfInstance; it returns 0 for any other zone).
    const auto Effect_AddGraveCardToHand = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, uint32_t, uint32_t*)>(0x140089C40);
    const auto Effect_AddBanishedCardToHand = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, uint32_t, uint32_t*)>(0x140089CB0);

    // A chain ("before" steps) whose first step is an own pick-and-act step: activation needs its list to have a card (SlotThunk<2>).
    bool FirstStepIsOwn(const Clone& clone)
    {
        return !clone.Pre.empty() && clone.Pre[0].Own;
    }

    bool g_OwnStepFoundNothing = false;   // set by Slot0_OwnPickAndAct when its list is empty; read and cleared by the sequencer
    int g_OwnPicked = 0;                  // picks made so far by a multi-pick step ("ownCount" > 1): the machine goes round again until done
    uint32_t g_OwnPickedFor = 0;          // the record (id | card instance << 16) those picks belong to: another record starts at 0

    uint64_t __fastcall Slot0_OwnPickAndAct(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const Clone* clone = g_Active;
        if (!clone || !effect)
            return 0;
        const int player = (effect[1] ^ effect[5]) & 1;
        const int id = effect[0];
        uint32_t& step = *reinterpret_cast<uint32_t*>(0x14349C13C);
        if (const uint32_t owner = static_cast<uint32_t>(id) | (static_cast<uint32_t>(effect[11]) << 16); owner != g_OwnPickedFor)
        {
            g_OwnPickedFor = owner;
            g_OwnPicked = 0;
        }
        switch (step)
        {
        case 128:
            if ((clone->Own == 4 && !(Can_PlayerBanish(player) & 0xFF)) || Deck_Collect(player, id, Get_DeckFilterParam(effect)) <= 0)
            {
                if (g_OwnPicked > 0)
                {
                    // a multi-pick step that ran out of cards after its first pick: done ("up to N")
                    g_OwnPicked = 0;
                    Msg_EndOfListEffect(player);
                    return 0;
                }
                Logger::WriteLog(std::format("Own step (source {}): nothing matches{}", id, clone->Own == 4 ? " (or banishing is not allowed)" : ""), MODULE_NAME, 1);
                UI_ShowNoneMessage(player, 0x15);
                g_OwnStepFoundNothing = true;   // "X, then Y": the sequencer stops here (Y needs X)
                return 0;
            }
            // the prompt the game shows before such a list: 0x33 the search's "add to your hand", 0x30 the summon's
            // (Slot0_Generic_TargetedEffect_StateMachine step 127, Unexpected Dai), 0x3A Foolish Burial's (its machine's step 128; also used
            // for banish, the game has no banish-from-Deck list machine to take one from)
            UI_ShowPrompt(player, clone->Own == 1 ? 0x33 : clone->Own == 2 ? 0x30 : 0x3A);
            return 126;
        case 126:
            UI_BeginListSelect(player, 6, id, Get_DeckFilterParam(effect));
            return 125;
        case 125:
        {
            // A pick-1 list (mode 6) leaves the picked INDEX at 0x14349C5C8 and the list at 0x14349C5D8 (the search machine's one-card path,
            // 0x14016DE3D); Get_SelectedListCard and the count at +0x3802 are for multi-pick lists only (they moved nothing, in-duel 2026-10-21).
            uint32_t* const picked = reinterpret_cast<uint32_t*>(0x14349C5D8) + *reinterpret_cast<const int16_t*>(0x14349C5C8);
            static constexpr const char* kActs[] = { "", "to hand", "summon", "to GY", "banish", "destroy", "shuffle into Deck", "Deck top", "Deck bottom", "Extra Deck" };
            Logger::WriteLog(std::format("Own step (source {}, {}): picked list entry {} = card {}", id, kActs[clone->Own],
                *reinterpret_cast<const int16_t*>(0x14349C5C8), *picked & 0x3FFF), MODULE_NAME, 1);
            if (clone->Own != 2)
            {
                // as the search / Foolish Burial machines: one resolution scope around the move, then tell the other side (online duel)
                Resolution_BeginScope(1);
                if (clone->Own == 1)
                {
                    // the Deck: the search machine's mover; the GY / banished cards: the salvage machine's
                    const int instance = ((*picked & 0x4000) != 0) + 2 * static_cast<int>(*picked >> 23);
                    const int zone = (Card_GetPositionByInstance(instance) >> 8) & 0xFF;
                    uint32_t* const slot = zone == 16 || zone == 17 ? Card_GetSlotPtrByInstance(instance) : nullptr;
                    if (zone == 16 && slot)
                        Effect_AddGraveCardToHand(effect, static_cast<uint32_t>(instance & 1), slot);
                    else if (zone == 17 && slot)
                        Effect_AddBanishedCardToHand(effect, static_cast<uint32_t>(instance & 1), slot);
                    else
                        Card_MoveListCardToHand(effect, player, picked, 1);
                }
                else
                {
                    // the list entry's card instance, as Foolish Burial's machine unpacks it
                    const int instance = ((*picked & 0x4000) != 0) + 2 * static_cast<int>(*picked >> 23);
                    const int zone = (Card_GetPositionByInstance(instance) >> 8) & 0xFF;
                    const bool banish = clone->Own == 4;
                    if (clone->Own >= 6)
                    {
                        // 6 shuffle into the Deck (placed on top, then shuffled), 7 Deck top, 8 Deck bottom, 9 Extra Deck
                        const int top = clone->Own == 8 ? 0 : 1;
                        if (zone == 16)
                            Move_GraveCardToDeck(player, static_cast<uint32_t>(instance & 1), instance, id, top, clone->Own == 9 ? 1 : 0);
                        else if (zone == 13 && clone->Own != 9 && Hand_FindIndexOfInstance(player, instance) >= 0)
                            Hand_ReturnCardToDeck(player, Hand_FindIndexOfInstance(player, instance), top);
                        else
                            Logger::WriteLog(std::format("Own step (source {}): card {} is in zone {}, not moved to the Deck (GY, hand; Extra Deck from the GY)",
                                id, *picked & 0x3FFF, zone), MODULE_NAME, 2);
                        if (clone->Own == 6)
                            Deck_PushShuffleMessages();
                    }
                    else if (zone == 13)
                        Move_HandCardToGraveOrBanish(player, instance, 0, banish ? 17 : 16, id);
                    else if (zone == 14 || zone == 15)
                        Move_NonFieldCardToGraveOrBanish(player, zone, banish ? 1 : 0, instance, 0, id);
                    else if (zone == 16 && banish)
                    {
                        uint32_t* const slot = Card_GetSlotPtrByInstance(instance);
                        if (slot && (Can_BanishFromGrave(player, static_cast<uint32_t>(instance & 1)) & 0xFF))
                            Effect_BanishGraveCard(effect, player, slot, 0);
                        else
                            Logger::WriteLog(std::format("Own step (source {}): card {} cannot be banished from the GY now", id, *picked & 0x3FFF), MODULE_NAME, 1);
                    }
                    else
                        Logger::WriteLog(std::format("Own step (source {}): card {} is in zone {}, not moved (Deck, Extra Deck, hand; banish also the GY)",
                            id, *picked & 0x3FFF, zone), MODULE_NAME, 2);
                }
                --*reinterpret_cast<int32_t*>(0x14349C180);
                if (*reinterpret_cast<const int32_t*>(0x143497C40 + 0x37B0))
                {
                    uint32_t payload = 0;
                    Net_SendSimple(8, -1, &payload, 2);   // NetEv_ResolutionScope
                }
                // "ownCount" N: round again (the list is collected anew, so the moved card is no longer in it) until N cards were picked
                if (++g_OwnPicked < clone->OwnCount)
                    return 128;
                g_OwnPicked = 0;
                return 100;
            }
            // Special Summon: the engine's summon link, as Unexpected Dai's machine queues it (0x140163830 step 124: mode 1, count 2)
            Chain_AddEffectLink(static_cast<int16_t>(player), picked, 1, 2, static_cast<uint32_t>(id) << 16, 0);
            return 120;
        }
        case 120:
        case 100:
            Msg_EndOfListEffect(player);
            return 0;
        default:
            return 0;
        }
    }

    // ---- the plugin's own field destroy ("own": "destroy") ------------------------------------------------------------------
    // "Destroy 1 card on the field" without a target: the card is picked at resolution with the game's field prompt, as its own field-pick
    // destroy machine does it (sub_14017AFD0, read in IDA 2026-10-22):
    //   126: UI_ShowPromptText(player, 0x62); prompt owner +0x385E = player; prompt kind +0x3832 (qword) = 9; zone test +0x387A = callback;
    //        +0x384A = the record's id; answered flag +0x3842 (qword) = 0 -> 125. The engine tick shows the prompt while +0x3832 is set (AI:
    //        sub_1400BAA70) and resolves nothing until it is answered.
    //   125: answered (+0x3842), the answer kind +0x3856 == 12 and sub_140063120(+0x383E) (the picked zone is still selectable) - else back
    //        to 126. Side +0x3862, zone +0x3866 + index +0x386A. Action_DestroyZoneMask(record, 1 << (zone + 16 * side), 16, 2) queues the
    //        destruction (Effect_RunSlot1Action_Driver's flags), which Duel__Engine__Tick runs (sub_1400D53E0) BEFORE it calls the chain
    //        resolver (sub_1400AF730) again - so a step after it sees the card gone.
    // The kind-9 zone test (Pick_GetCardFlags 0x1400632D0) is called as (resolving record, side, zone) for zones 0..12; non-zero = selectable.
    constexpr uintptr_t kDuelState = 0x143497C40;
    Clone* g_FieldPick = nullptr;   // the own destroy step whose prompt is up (the zone test runs between slot calls, without g_Active)
    int g_SlotInstance = -1;        // the card instance (record word 11) of the slot running now: Hook_Count's "except this card" for a field step

    // Does the card in (side, zone) pass the step's "deck" row? The zone's InPlay_Card (24 bytes from +0x4C: CARDID, ..., UNK_03 at +0x14
    // with 0x20 = "not counted", as Check_TargetStillValid 0x1400DFEE0 skips it); kind bits 2 / 4 / 8 of the row (none = any); a monster
    // through Scan_CardMatchesRowFlags (0x14052B540: race / attribute / Level by the zone's effective values, ATK); the row's name test.
    bool FieldZoneMatches(const Clone& clone, int player, int side, int zone, int selfInstance)
    {
        if (zone < 0 || zone > 12)
            return false;
        if ((clone.FieldSide == 0 && side != player) || (clone.FieldSide == 1 && side == player))
            return false;
        const uintptr_t entry = kDuelState + static_cast<uintptr_t>(side & 1) * 0xD94 + 0x4C + 24 * static_cast<uintptr_t>(zone);
        uint32_t* const card = reinterpret_cast<uint32_t*>(entry);
        const int cardId = static_cast<int>(*card & 0x3FFF);
        if (cardId == 0 || (*reinterpret_cast<const uint32_t*>(entry + 0x14) & 0x20))
            return false;
        const int instance = ((*card & 0x4000) != 0) + 2 * static_cast<int>(*card >> 23);
        if ((clone.ExceptSelf || clone.ExceptSelfAfter) && instance == selfInstance)
            return false;   // "except this card": this card itself, not its name (a field card is one card)
        const uint32_t rowFlags = *reinterpret_cast<const uint32_t*>(clone.DeckRow + 16);
        const uint32_t kinds = (rowFlags & 0xE) ? (rowFlags & 0xE) : 0xE;
        const int type = reinterpret_cast<int(__fastcall*)(int)>(YGO::Addr::Card_GetTypeFromFullCardPropsByKonamiId)(cardId);
        bool ok;
        if (type == 13) ok = (kinds & 4) != 0;        // Spell
        else if (type == 14) ok = (kinds & 8) != 0;   // Trap
        else
        {
            using Matches_t = int64_t(__fastcall*)(int64_t, uint32_t, int, int, int, uint32_t*, uint32_t);
            ok = (kinds & 2) && (reinterpret_cast<Matches_t>(0x14052B540)(0, static_cast<uint32_t>(side & 1), zone, 0,
                *reinterpret_cast<const uint16_t*>(clone.DeckRow), card, rowFlags | 2) & 0xFF);
        }
        if (!ok)
            return false;
        const auto IsSameName = reinterpret_cast<int64_t(__fastcall*)(int, int)>(0x1407EBA00);    // j_Card_IsSameName
        const auto InArchetype = reinterpret_cast<int64_t(__fastcall*)(int, int)>(0x1407EB9E0);   // Is_CardInNamedArchetype
        const int nameTest = *reinterpret_cast<const int16_t*>(clone.DeckRow + 2);
        return !((nameTest >= 3000 && !IsSameName(cardId, nameTest)) || (nameTest > 0 && nameTest < 3000 && !InArchetype(cardId, nameTest))
            || (nameTest <= -3000 && IsSameName(cardId, -nameTest)) || (nameTest < -1 && nameTest > -3000 && InArchetype(cardId, -nameTest)));
    }

    // A raw Tribute cost's zone test (section 90): a monster of the record's controller (zones 0-6) the part's row matches (FieldZoneMatches)
    // that can be Tributed (sub_14001D700, the test Cond_CanTributeSelf / Check_CanTributeOne use).
    bool TributeCandidate(const Clone& pick, const uint16_t* effect, uint32_t player, int zone)
    {
        const int own = (effect[1] ^ effect[5]) & 1;
        if (static_cast<int>(player & 1) != own || zone < 0 || zone > 6 || !FieldZoneMatches(pick, own, own, zone, effect[11]))
            return false;
        return reinterpret_cast<int64_t(__fastcall*)(int, int, int, int)>(0x14001D700)(own, own, zone, 1) & 0xFF;
    }

    // How many cards a pick cost could take now (its activation check, section 90). The hand verbs ask the part's row once per hand index, as
    // the game's hand picker does (Pick_HandFilter_ByDeckRow -> Deck_CountMatchingCards(player, id, handIndex) -> Scan_HandCardAt_RowFlags);
    // Tribute counts the monsters TributeCandidate allows; a banish counts its list. Runs with the part active (g_Active) and the record showing
    // the id the machine runs under.
    int CostCandidates(Clone& pick, uint16_t* effect)
    {
        const int player = (effect[1] ^ effect[5]) & 1;
        const auto Deck_Count = reinterpret_cast<int64_t(__fastcall*)(int, int, uint32_t)>(0x1400C0320);   // Deck_CountMatchingCards (hooked)
        switch (pick.Verb)
        {
        case Clone::PickVerb::Discard:
        case Clone::PickVerb::SendHand:
        case Clone::PickVerb::Reveal:
        case Clone::PickVerb::SendHandOrField:
        {
            const int inHand = *reinterpret_cast<const int*>(kDuelState + static_cast<uintptr_t>(player) * 0xD94 + 0x0C);   // PlayerOne.iNumberOfCardsInHand
            int found = 0;
            for (int i = 0; i < inHand && i < 120; ++i)
                if (Deck_Count(player, effect[0], static_cast<uint32_t>(i)) > 0)
                    ++found;
            if (pick.Verb == Clone::PickVerb::SendHandOrField)
                for (int zone = 0; zone <= 12; ++zone)
                    if (FieldZoneMatches(pick, player, player, zone, effect[11]))
                        ++found;
            return found;
        }
        case Clone::PickVerb::Tribute:
        {
            int found = 0;
            for (int zone = 0; zone <= 6; ++zone)
                if (TributeCandidate(pick, effect, static_cast<uint32_t>(player), zone))
                    ++found;
            return found;
        }
        default:
            return static_cast<int>(Deck_Count(player, effect[0], Get_DeckFilterParam(effect)));
        }
    }

    int OwnFieldCount(const Clone& clone, int player, int selfInstance)
    {
        int count = 0;
        for (int side = 0; side < 2; ++side)
            for (int zone = 0; zone <= 12; ++zone)
                count += FieldZoneMatches(clone, player, side, zone, selfInstance) ? 1 : 0;
        return count;
    }

    // The kind-9 prompt's zone test.
    int64_t __fastcall FieldPick_ZoneTest(uint16_t* record, uint32_t side, uint32_t zone)
    {
        if (!g_FieldPick || !record)
            return 0;
        return FieldZoneMatches(*g_FieldPick, (record[1] ^ record[5]) & 1, static_cast<int>(side & 1), static_cast<int>(zone), record[11]) ? 1 : 0;
    }

    uint64_t __fastcall Slot0_OwnFieldDestroy(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        Clone* clone = g_Active;
        if (!clone || !effect)
            return 0;
        const int player = (effect[1] ^ effect[5]) & 1;   // whose side is "own" (as Cond_ListHasEnoughMatches counts); the prompt is record[1]'s
        uint32_t& step = *reinterpret_cast<uint32_t*>(0x14349C13C);
        const auto PickStillSelectable = reinterpret_cast<int64_t(__fastcall*)(int)>(0x140063120);
        const auto Action_DestroyZoneMask = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, int, int, int)>(0x1400D5150);
        switch (step)
        {
        case 128:
            if (OwnFieldCount(*clone, player, effect[11]) == 0)
            {
                Logger::WriteLog(std::format("Own destroy (source {}): no card on the field matches", effect[0]), MODULE_NAME, 1);
                g_OwnStepFoundNothing = true;   // "X, then Y": the sequencer stops here
                return 0;
            }
            [[fallthrough]];
        case 126:
            g_FieldPick = clone;
            UI_ShowPrompt(effect[1], 0x62);
            *reinterpret_cast<uint32_t*>(kDuelState + 0x385E) = effect[1];
            *reinterpret_cast<uint64_t*>(kDuelState + 0x3832) = 9;
            *reinterpret_cast<uint64_t*>(kDuelState + 0x387A) = reinterpret_cast<uint64_t>(&FieldPick_ZoneTest);
            *reinterpret_cast<uint32_t*>(kDuelState + 0x384A) = effect[0];
            *reinterpret_cast<uint64_t*>(kDuelState + 0x3842) = 0;
            return 125;
        case 125:
        {
            if (*reinterpret_cast<const uint32_t*>(kDuelState + 0x3842) == 0 || *reinterpret_cast<const uint32_t*>(kDuelState + 0x3856) != 12
                || !(PickStillSelectable(*reinterpret_cast<const int32_t*>(kDuelState + 0x383E)) & 0xFF))
                return 126;
            const int side = *reinterpret_cast<const int32_t*>(kDuelState + 0x3862);
            const int zone = *reinterpret_cast<const int32_t*>(kDuelState + 0x3866) + *reinterpret_cast<const int32_t*>(kDuelState + 0x386A);
            g_FieldPick = nullptr;
            Logger::WriteLog(std::format("Own destroy (source {}): side {} zone {}", effect[0], side & 1, zone), MODULE_NAME, 1);
            Action_DestroyZoneMask(effect, 1 << (zone + 16 * (side & 1)), 16, 2);
            return 0;
        }
        default:
            g_FieldPick = nullptr;
            return 0;
        }
    }

    // ---- the plugin's own move step ("move", docs/EffectSystem.md section 86) ---------------------------------------------------
    // The game's target machines all end the same way (Slot0_DestroyFirstTarget / Slot0_ReturnTargetToHand / Slot0_BanishTarget, read
    // in IDA 2026-10-26): clear the "moved" masks (+0x1B3A, +0x1B42), Has_SelectedTarget(record, i, &zone) (low byte side, high byte zone),
    // Check_TargetStillValid(record, zone word, zone), then Action_DestroyZoneMask(record, zone bits, REASON = the destination zone, flags).
    // DestroyQueue_Process moves the cards when the slot returns (16 + 0x2000002 destroy, 13 hand, 17 banish, 15 Deck with 0x800 bottom /
    // 0x4000000 shuffle, 14 Extra Deck). This step does that for every selected target (scope 1), or for this card (scope 2: on the field
    // the same queue; in the GY the Deck movers).
    uint64_t __fastcall Slot0_OwnMove(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const Clone* clone = g_Active;
        if (!clone || !effect || !clone->MoveReason)
            return 0;
        const auto Has_SelectedTarget = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, int, int*)>(0x14008C040);
        const auto Check_TargetStillValid = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, int, int)>(0x1400DFEE0);
        const auto Action_DestroyZoneMask = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, int, int, uint32_t)>(0x1400D5150);
        *reinterpret_cast<uint64_t*>(kDuelState + 0x1B42) = 0;
        *reinterpret_cast<uint64_t*>(kDuelState + 0x1B3A) = 0;
        int mask = 0;
        if (clone->MoveScope == 1)
        {
            for (int i = 0; i < 5; ++i)
            {
                int zoneWord = 0;
                if (!(Has_SelectedTarget(effect, i, &zoneWord) & 0xFF))
                    break;
                const int side = zoneWord & 1, zone = (zoneWord >> 8) & 0xFF;
                if (zone <= 12 && (Check_TargetStillValid(effect, zoneWord, zone) & 0xFF))
                    mask |= 1 << (zone + 16 * side);
            }
        }
        else if (clone->MoveScope == 2)
        {
            const int instance = effect[11];
            const uint32_t position = Card_GetPositionByInstance(instance);
            const int side = position & 1, zone = (position >> 8) & 0xFF;
            if (zone <= 12)
                mask = 1 << (zone + 16 * side);
            else if ((zone == 16 || zone == 17) && clone->MoveReason == 13)
            {
                uint32_t* const slot = Card_GetSlotPtrByInstance(instance);
                if (slot)
                    (zone == 16 ? Effect_AddGraveCardToHand : Effect_AddBanishedCardToHand)(effect, static_cast<uint32_t>(instance & 1), slot);
            }
            else if (zone == 16 && clone->MoveReason == 15)
            {
                Move_GraveCardToDeck(side, static_cast<uint32_t>(instance & 1), instance, effect[0], (clone->MoveFlags & 0x800) ? 0 : 1, 0);
                if (clone->MoveFlags & 0x4000000)
                    Deck_PushShuffleMessages();
            }
            else
                Logger::WriteLog(std::format("Own move (card {}): this card is in zone {}, not moved", effect[0], zone), MODULE_NAME, 1);
        }
        if (mask)
            Action_DestroyZoneMask(effect, mask, clone->MoveReason, clone->MoveFlags);
        return 0;
    }

    // ---- a result on the targets ("targetDo": "atk" | "negate", docs/EffectSystem.md section 92) --------------------------------------
    // The game's own result step: Emit_ResultEvent (0x14016BAF0: effect, key, lifetime << 4 | 2, value, ignore immunity) puts a marker with
    // that key and value on each of the record's still-valid, not immune targets (msg 53). Generic keys (Slot0_EmitResultParams_ByCardId /
    // sub_140139FA0): 1027 = effects negated until the end of the turn (Effect Veiler, Breakthrough Skill, Infinite Impermanence), 1040 =
    // gains <value> ATK until the end of the turn (Cyberse Accelerator, Decode Destruction); lifetime 0 -> marker kind 2, cleared at the end of
    // the turn.
    uint64_t __fastcall Slot0_OwnTargetResult(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const Clone* clone = g_Active;
        if (!clone || !effect || !clone->ResultKey)
            return 0;
        const auto Emit_ResultEvent = reinterpret_cast<int64_t(__fastcall*)(uint16_t*, uint16_t, uint8_t, int, int)>(0x14016BAF0);
        Emit_ResultEvent(effect, clone->ResultKey, 2, clone->ResultValue, 0);
        return 0;
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

    void EffectResolved(const Clone* part, const uint16_t* effect, uint16_t customId);   // the summon lock section

    template <int SlotIndex>
    uint64_t __fastcall SlotThunk(uint16_t* effect, uint64_t a2, uint64_t a3, uint64_t a4)
    {
        bool borrowedWithoutClone;
        Clone* clone = RouteByRow(Find(effect[0], borrowedWithoutClone), effect);
        if (!clone)
            return 0;   // cannot happen for a row we built; a defensive default
        struct InstanceScope { int Saved; explicit InstanceScope(int now) : Saved(g_SlotInstance) { g_SlotInstance = now; } ~InstanceScope() { g_SlotInstance = Saved; } }
            instanceScope(effect[11]);

        // The same source row the wrapper was built from (the record's other fields are unchanged, so the lookup picks the same row).
        const uint16_t saved = effect[0];
        // Raw slots (section 88): the engine function the clone names runs with the record's own id; slot 3 of a donor row is still the row's.
        const bool raw = clone->RawSlots && (SlotIndex != 3 || clone->RawRow);
        if (clone->RawSlots)
            clone->EngineId = saved;
        if (!raw || clone->From)
            effect[0] = static_cast<uint16_t>(clone->From);
        NativeChoiceScope nativeChoice(effect);
        const Row* row = clone->From ? static_cast<const Row*>(orig_GetEntry(effect)) : nullptr;
        void* fn = clone->SlotOverride[SlotIndex] ? reinterpret_cast<void*>(clone->SlotOverride[SlotIndex]) : raw ? nullptr : row ? row->Slot[SlotIndex] : nullptr;
        uint16_t impersonate = raw ? saved : static_cast<uint16_t>(clone->From);
        effect[0] = impersonate;
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
                effect[0] = impersonate;
                if (!payable)
                {
                    effect[0] = saved;
                    return 0;
                }
            }
        }
        if (clone->ActionFrom && SlotIndex != 3 && !raw)
        {
            // Composed effect: the action card's slot, run as the action card (its id-keyed parameters and ladders apply).
            const Row* action = ActionRow(clone->ActionFrom);
            fn = clone->SlotOverride[SlotIndex] ? reinterpret_cast<void*>(clone->SlotOverride[SlotIndex]) : action ? action->Slot[SlotIndex] : nullptr;
            impersonate = static_cast<uint16_t>(clone->ActionFrom);
            effect[0] = impersonate;
        }

        if constexpr (SlotIndex == 2)
        {
            // A chain whose FIRST step picks from a list ("own") can only be activated while that list has a card - the game's rule for a
            // list effect (Slot2_Condition_ListHasMatch 0x1400FB620: Deck_CountMatchingCards >= 1, answered by Hook_Count with the step's
            // filter). Later steps are not checked: an earlier step may be what makes them possible (search, then summon it).
            if (FirstStepIsOwn(*clone))
            {
                Clone& first = clone->Pre[0];
                if (first.RawSlots)
                    first.EngineId = saved;
                effect[0] = first.RawSlots ? saved : static_cast<uint16_t>(first.ActionFrom ? first.ActionFrom : first.From);
                Clone* previousActive = g_Active;
                g_Active = &first;
                const auto Deck_Count = reinterpret_cast<int64_t(__fastcall*)(int, int, uint32_t)>(0x1400C0320);   // Deck_CountMatchingCards (hooked)
                const int64_t found = Deck_Count((effect[1] ^ effect[5]) & 1, effect[0], Get_DeckFilterParam(effect));
                g_Active = previousActive;
                if (found < 1)
                {
                    effect[0] = saved;
                    return 0;
                }
                effect[0] = impersonate;
            }
            // "costPick": the cost can be paid - at least N cards in its pick list (Deck_CountMatchingCards through the sub-clone's filter; the
            // list leaves this card out, "exceptSelf")
            if (clone->CostPick)
            {
                Clone& pick = *clone->CostPick;
                if (pick.RawSlots)
                    pick.EngineId = saved;
                effect[0] = pick.RawSlots ? saved : static_cast<uint16_t>(pick.From);
                Clone* previousActive = g_Active;
                g_Active = &pick;
                const int found = CostCandidates(pick, effect);
                g_Active = previousActive;
                if (found < pick.CostPickCount)
                {
                    effect[0] = saved;
                    return 0;
                }
                effect[0] = impersonate;
            }
            // The required checks, each run as the row's card (they read the record's player / zone / card instance only).
            for (const uint64_t check : clone->Require)
            {
                effect[0] = raw ? saved : static_cast<uint16_t>(clone->From);
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

        // Chaining - the general resolver (docs/EffectSystem.md section 53): "before" steps run ahead of this card's own step, in order, each
        // as its own source card. Every slot 0 is a step machine: one call = one step, it returns the next step (stored in the step variable
        // 0x14349C13C, 128 = start) and 0 when done. An immediate step (draw, life points) finishes in one call, so a run of them is done
        // back to back; an interactive one (search, revive, ...) is called again until it returns 0, then the step variable is set back to 128
        // and 128 is returned, so the engine calls slot 0 again and the next step starts fresh. Which step is running is kept per card
        // instance (g_Sequence) until the effect resolves. Their targets: only the card's own (last) step has the activation's target.
        if constexpr (SlotIndex == 0)
        {
            uint32_t* const stepVariable = reinterpret_cast<uint32_t*>(0x14349C13C);
            const auto key = ChoiceKey(effect);
            // No entry = a fresh resolution. Its first step value is kept and given to every later machine as its start: it is not always 128
            // (in-duel 2026-10-21: a hand-summon main step, source 6572, did not start at 128, and a check for 128 skipped the search before it).
            if (!g_Sequence.contains(key))
            {
                g_SequenceStart[key] = *stepVariable;
                if (!clone->Pre.empty())
                    Logger::WriteLog(std::format("Card {} resolves {} step(s) then its own (source {}), starting at step value {}", key.first, clone->Pre.size(),
                        clone->From, *stepVariable), MODULE_NAME, 1);
            }
            size_t index = g_Sequence.contains(key) ? g_Sequence[key] : 0;
            while (index < clone->Pre.size())
            {
                Clone& step = clone->Pre[index];
                // a raw step (section 88) runs its own slot 0 under the card's own id; a donor step as its source card
                if (step.RawSlots)
                    step.EngineId = saved;
                effect[0] = step.RawSlots ? saved : static_cast<uint16_t>(step.From);
                const Row* stepRow = step.RawSlots ? nullptr : static_cast<const Row*>(orig_GetEntry(effect));
                void* stepFn = stepRow ? stepRow->Slot[0] : nullptr;
                if (step.ActionFrom && !step.RawSlots)
                {
                    const Row* action = ActionRow(step.ActionFrom);
                    stepFn = action ? action->Slot[0] : nullptr;
                    effect[0] = static_cast<uint16_t>(step.ActionFrom);
                }
                if (step.SlotOverride[0])
                    stepFn = reinterpret_cast<void*>(step.SlotOverride[0]);
                g_Active = &step;
                g_OwnStepFoundNothing = false;
                const uint64_t stepResult = stepFn ? reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(stepFn)(effect, a2, a3, a4) : 0;
                g_Active = previous;
                effect[0] = saved;
                if (g_OwnStepFoundNothing)
                {
                    // "X, then Y": X did nothing, so Y does not happen; the effect has resolved
                    g_OwnStepFoundNothing = false;
                    Logger::WriteLog(std::format("Card {}: step {} found nothing, the rest of the chain is not run", key.first, index + 1), MODULE_NAME, 1);
                    g_Sequence.erase(key);
                    ForgetChoices(effect);
                    if (g_Resolving == &step || g_Resolving == clone)
                        g_Resolving = nullptr;
                    EffectResolved(clone, effect, saved);
                    return 0;
                }
                if (stepResult & 0xFF)
                {
                    // still running: called again for its next step. Between the calls the pick screen tests cards by the step's id
                    // (DeckFilterClone through g_Resolving), so the step is the one resolving.
                    g_Sequence[key] = index;
                    g_Resolving = &step;
                    return stepResult;
                }
                ++index;
                if (!IsImmediate(step))
                {
                    // an interactive step finished: the next one starts on the engine's next call
                    g_Sequence[key] = index;
                    g_Resolving = clone;
                    *stepVariable = g_SequenceStart[key];
                    return g_SequenceStart[key];
                }
            }
            g_Sequence[key] = index;
            effect[0] = impersonate;
        }

        g_Active = clone;
        // "costPick": Cost_BanishPickedCards runs under the sub-clone's id with the sub-clone active, so its list, count and "this card too"
        // come from it (DeckFilterClone / Hook_DeckFilterIndex / Hook_CostPickCount match g_Active by that id)
        if constexpr (SlotIndex == 3)
            if (clone->CostPick)
            {
                Clone& pick = *clone->CostPick;
                g_Active = &pick;
                // a raw pick cost (section 90) runs under this card's own id; a donor one as its machine's id card (7452 / 9585)
                if (pick.RawSlots)
                    pick.EngineId = saved;
                effect[0] = pick.RawSlots ? saved : static_cast<uint16_t>(pick.From);
                // "banish this card and N X from your GY": the game does the "this card" half by id (Laval Lakeside Lady 9585) in the machine's
                // last state, right before the picks; a raw cost does it here at the same point
                if (pick.RawSlots && pick.CostSelf && pick.Verb == Clone::PickVerb::Banish && *reinterpret_cast<const int*>(kCostStep) == 2)
                    reinterpret_cast<int64_t(__fastcall*)(uint16_t*)>(0x1401FA670)(effect);   // Cost_BanishSelfFromGrave
            }
        uint64_t result = 0;
        if (fn)
            result = reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(fn)(effect, a2, a3, a4);
        if constexpr (SlotIndex == 3)
            if (clone->CostPick && clone->CostPick->RawSlots)
                g_Paying = (result & 0xFF) ? nullptr : clone->CostPick.get();   // between its steps the picker asks by this card's id
        else if constexpr (SlotIndex == 2)
        {
            if (clone->CostFrom || clone->CostPick || !clone->Require.empty() || FirstStepIsOwn(*clone))
                result = 2;   // the slot only exists for the cost / required checks, which passed; no condition of its own = allowed (as Slot_ReturnConst2)
        }
        g_Active = previous;
        effect[0] = saved;
        if constexpr (SlotIndex == 0)
        {
            if (g_Paying == clone->CostPick.get())
                g_Paying = nullptr;   // resolving: its cost is paid (or was given up)
            g_Resolving = (result & 0xFF) ? clone : (g_Resolving == clone ? nullptr : g_Resolving);
            if (!(result & 0xFF))
            {
                if (const Clone* root = Find(saved, borrowedWithoutClone); root && !root->Parts.empty())
                    Logger::WriteLog(std::format("Card {} resolved its effect with source {}, instance {}, tag {:04X}", saved, clone->From, effect[11], effect[20]), MODULE_NAME, 0);
                ForgetChoices(effect);   // resolved: the card's next activation chooses again
                g_Sequence.erase(ChoiceKey(effect));  // and starts its steps from the first
                EffectResolved(clone, effect, saved);
            }
        }
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
                    // the spell speed ladder: a Quick Effect is speed 2 whatever its donor card is (Clone::Quick)
                    if constexpr (N == 3)
                        if (clone->Quick)
                            return 2;
                    // a raw row (section 88) has no source card: every one of these ladders answers from the card's own kind for an id it
                    // does not list (Get_DefaultSpellSpeedForCard, Kind_IsMonster ...), which is this card's true answer
                    if (clone->RawRow || !clone->From)
                        return Orig(effect, a2, a3, a4);
                    const uint16_t saved = effect[0];
                    const uint16_t previousShown = g_ShowingSource;
                    effect[0] = static_cast<uint16_t>(clone->From);
                    g_ShowingSource = effect[0];
                    NativeChoiceScope nativeChoice(effect);
                    const uint64_t result = Orig(effect, a2, a3, a4);
                    g_ShowingSource = previousShown;
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
                {
                    // Kind_IsFlipMonster (743030) reads the card's kind from FULL_CARD_PROPS, and under source-id lending the source's entry
                    // holds the CUSTOM card's kind for the duel: a FLIP clone of kind "Effect" was answered "not a FLIP monster" and its FLIP was
                    // never offered (in-duel 2026-10-02: Baby Spider, Bunny Ear Enthusiast). A card with a "flip" effect is a FLIP monster.
                    if constexpr (N == 6)
                        for (Clone* part : AllParts(*clone))
                            if (part->Trigger == "flip")
                                return 1;
                    // Id_HasHandEffect (743FA0): an own "in_hand" effect (no game card behind it) is a hand effect (docs/EffectSystem.md s66)
                    if constexpr (N == 0)
                        for (Clone* part : AllParts(*clone))
                            if (part->OwnTrigger && part->Trigger == "in_hand")
                                return 1;
                    // any effect of the card answers yes; an own-trigger effect's "from" is only its action card, never asked, and a raw row
                    // has no source (section 88: it is in none of these lists, as a card of its own)
                    int64_t best = 0;
                    for (Clone* part : AllParts(*clone))
                        if (!UsesOwnRow(*part) && part->From)
                            best = (std::max)(best, Orig(part->From));
                    return best;
                }
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
            // The offer is for the card itself (its wrapped row), not the source being shown: matters when a game card's override
            // takes its own card as the source (same id), see ShownAsSource.
            const uint16_t previousShown = g_ShowingSource;
            g_ShowingSource = 0;
            const int64_t result = orig_OfferByRef(ref, a2, a3);
            g_ShowingSource = previousShown;
            ++g_Swap.Offers;
            g_Swap.OfferResult = result;
            return result;
        }
        bool ignore;
        Clone* root = Find(static_cast<uint16_t>(ref), ignore);
        // A one-effect clone is offered only for its own trigger: a lent source id brings ALL of the source's id-keyed triggers along (Black
        // Mamba plays as Edge Imp Chain 11688 and was offered Edge Imp Chain's sent-to-the-GY effect when Dark Hole destroyed it, in-duel
        // 2026-10-14). The event is bits 25-30 of the reference (section 29).
        if (root && root->Parts.empty() && !g_MenuPart && !root->Trigger.empty() && root->Trigger != "ignition")
        {
            const uint16_t event = static_cast<uint16_t>((ref >> 25) & 0x3F);
            if (IsKnownTriggerEvent(event) && !TriggerFitsEvent(root->Trigger, event))
                return 0;
        }
        if (!root || root->Parts.empty() || g_MenuPart)
            return orig_OfferByRef(ref, a2, a3);
        int64_t result = 0;
        for (Clone* part : AllParts(*root))
        {
            uint16_t probe[0x20]{};
            probe[0] = static_cast<uint16_t>(part->From);
            probe[1] = ref >> 31;
            probe[2] = (ref >> 16) & 0x1F;
            probe[3] = (ref >> 25) & 0x3F;
            probe[4] = (ref >> 21) & 7;
            if (!part->Trigger.empty() && IsKnownTriggerEvent(probe[3]) && !TriggerFitsEvent(part->Trigger, probe[3])) continue;
            if (UsesOwnRow(*part) ? !OwnRowAnswers(*part, probe) : [&] { const Row* row = static_cast<const Row*>(orig_GetEntry(probe)); return !row || row->Id != part->From; }())
                continue;   // no row for this offer (an own-trigger / raw effect: the plugin's own row, OwnRowAnswers)
            g_MenuPart = part;
            const int64_t offered = orig_OfferByRef(ref, a2, a3);
            if (offered) result = offered;
        }
        g_MenuPart = nullptr;
        return result;
    }

    using MenuOffer_t = int64_t(__fastcall*)(uint32_t, int16_t, void*, uint32_t);
    MenuOffer_t orig_MenuOffer = reinterpret_cast<MenuOffer_t>(0x1400AB050);
    using InsertOffer_t = int64_t(__fastcall*)(int, uint32_t, int16_t, uint32_t);
    InsertOffer_t orig_InsertOffer = reinterpret_cast<InsertOffer_t>(0x1400AA860);

    int64_t __fastcall Hook_MenuOffer(uint32_t ref, int16_t instance, void* chain, uint32_t param)
    {
        bool ignore;
        Clone* root = Find(static_cast<uint16_t>(ref), ignore);
        if (!root || root->Parts.empty() || g_MenuPart)
            return orig_MenuOffer(ref, instance, chain, param);
        int64_t result = 0;
        for (Clone* part : AllParts(*root))
        {
            uint16_t probe[0x20]{};
            probe[0] = static_cast<uint16_t>(part->From);
            probe[1] = ref >> 31;
            probe[2] = (ref >> 16) & 0x1F;
            probe[3] = (ref >> 25) & 0x3F;
            probe[4] = (ref >> 21) & 7;
            if (!part->Trigger.empty() && IsKnownTriggerEvent(probe[3]) && !TriggerFitsEvent(part->Trigger, probe[3])) continue;
            if (UsesOwnRow(*part) ? !OwnRowAnswers(*part, probe) : [&] { const Row* row = static_cast<const Row*>(orig_GetEntry(probe)); return !row || row->Id != part->From; }())
                continue;   // no row for this offer (an own-trigger / raw effect: the plugin's own row, OwnRowAnswers)
            g_MenuPart = part;
            const int64_t offered = orig_MenuOffer(ref, instance, chain, param);
            if (offered) result = offered;
        }
        g_MenuPart = nullptr;
        return result;
    }

    int64_t __fastcall Hook_InsertOffer(int kind, uint32_t ref, int16_t instance, uint32_t param)
    {
        bool ignore;
        Clone* root = Find(static_cast<uint16_t>(ref), ignore);
        if (!root || root->Parts.empty() || !g_MenuPart)
            return orig_InsertOffer(kind, ref, instance, param);
        const uint16_t tag = ChoiceTag(*root, g_MenuPart);
        auto* records = reinterpret_cast<uint16_t*>(kind ? 0x14349BC80 : 0x14349B580);
        auto* count = reinterpret_cast<uint32_t*>(kind ? 0x14349C000 : 0x14349C020);
        const uint32_t before = *count;
        // The two queues occupy 16 and 32 records respectively. Expanding a
        // card's choices must never write past the native buffers.
        if (before >= (kind ? 16u : 32u)) return 0;
        std::vector<std::pair<uint16_t*, uint16_t>> hidden;
        // Native duplicate detection compares card instance and table, but not
        // the selected effect. Hide sibling choices for this one insertion.
        for (uint32_t i = 0; i < before; ++i)
        {
            uint16_t* record = records + i * (0x38 / 2);
            if (record[0] == static_cast<uint16_t>(ref) && (record[20] & 0xFF00) != tag)
            { hidden.emplace_back(record, record[0]); record[0] = 0; }
        }
        const int64_t result = orig_InsertOffer(kind, ref, instance, param);
        for (const auto& [record, id] : hidden) record[0] = id;
        for (uint32_t i = before; i < *count; ++i)
        {
            uint16_t* record = records + i * (0x38 / 2);
            record[20] = tag | (record[20] & 0xFF);
        }
        return result;
    }

    // The first 0x40 bytes of the event record as 16-bit words, for the trace.
    std::string DumpEventRecord(const uint8_t* record)
    {
        std::string text;
        for (int i = 0; i < 0x40; i += 2)
            text += std::format("{}{:X}", i ? " " : "", *reinterpret_cast<const uint16_t*>(record + i));
        return text;
    }

    // Does the summon event in the record (0x14349C560) meet a part's "summonCondition"? Record words, decoded from the evaluator's own per-card
    // checks (docs/EffectSystem.md section 46): +0x0A the zone the monster came from, +0x10 flags (bit 0 = properly Summoned as its kind:
    // Fusion/Synchro/Xyz/Ritual/Link; 0x40 = Pendulum Summoned), +0x12 the card whose effect Special Summoned it (0 = not by an effect),
    // +0x30 the number of Tributes of a Tribute Summon.
    bool SummonMatches(const Clone& root, const Clone& part, const uint8_t* record, uint16_t raw)
    {
        const auto& s = part.Summon;
        if (!s.Any)
            return true;
        const uint16_t from = *reinterpret_cast<const uint16_t*>(record + 0x0A);
        const uint16_t flags = *reinterpret_cast<const uint16_t*>(record + 0x10);
        const uint16_t by = *reinterpret_cast<const uint16_t*>(record + 0x12);
        const uint16_t tributes = *reinterpret_cast<const uint16_t*>(record + 0x30);
        if (s.FromZones && (from >= 32 || !(s.FromZones & (1u << from))))
            return false;
        if (s.Proper && !(flags & 1))
            return false;
        if (s.Pendulum && !(flags & 0x40))
            return false;
        if (s.MinTributes && tributes < s.MinTributes)
            return false;
        if (s.CardRace || s.CardAttribute || s.CardArchetype || s.CardKind)
        {
            // the summoned monster: the card in the record's (side +2, zone +4), as Hook_EventEvaluator reads the event's card
            const int id = static_cast<int>(orig_CardIdAt(*reinterpret_cast<const uint16_t*>(record + 2) & 1, *reinterpret_cast<const uint16_t*>(record + 4), 1) & 0x3FFF);
            using IdTest_t = int(__fastcall*)(int);
            static const IdTest_t kindTests[] = { nullptr, reinterpret_cast<IdTest_t>(0x140742EF0), reinterpret_cast<IdTest_t>(0x140742F20),
                reinterpret_cast<IdTest_t>(0x140742F80), reinterpret_cast<IdTest_t>(0x1407432A0), reinterpret_cast<IdTest_t>(0x140742FE0) };   // Kind_IsFusion / Synchro / Xyz / Link / Ritual
            if (!id
                || (s.CardRace && reinterpret_cast<int(__fastcall*)(int)>(0x14081A7B0)(id) != s.CardRace)                 // Get_MonsterType
                || (s.CardAttribute && reinterpret_cast<int(__fastcall*)(int)>(0x14081A5D0)(id) != s.CardAttribute)       // Get_AttributeFromFullCardProps
                || (s.CardArchetype && !reinterpret_cast<int(__fastcall*)(int, int)>(0x1407EB9E0)(id, s.CardArchetype))   // Is_CardInNamedArchetype
                || (s.CardKind && !kindTests[s.CardKind](id)))
                return false;
        }
        using By = Clone::SummonCondition::By;
        const auto typeOf = [](int id) { return reinterpret_cast<int(__fastcall*)(int)>(YGO::Addr::Card_GetTypeFromFullCardPropsByKonamiId)(id); };
        switch (s.ByWhat)
        {
        case By::Any: return true;
        case By::Effect: return by != 0;
        case By::NotEffect: return by == 0;
        case By::Own:
        {
            // Its own effect: while a clone's effect resolves the engine records the id it runs under (a part's source / action card).
            if (by == raw || by == BorrowedRealId(raw))
                return true;
            for (const Clone* p : AllParts(const_cast<Clone&>(root)))
                if (by == p->From || (p->ActionFrom && by == p->ActionFrom))
                    return true;
            return false;
        }
        case By::Spell: return by != 0 && typeOf(by) == 13;
        case By::Trap: return by != 0 && typeOf(by) == 14;
        case By::Monster: return by != 0 && reinterpret_cast<int64_t(__fastcall*)(int)>(0x140742DF0)(by) != 0;   // Kind_IsMonster
        case By::Card: return by == s.ByValue;
        case By::Archetype: return by != 0 && reinterpret_cast<int64_t(__fastcall*)(int, int)>(0x1407EB9E0)(by, s.ByValue) != 0;   // Is_CardInNamedArchetype
        }
        return true;
    }

    // Own summon triggers ("When this card is Normal / Special / Flip Summoned", no game card behind them; docs/EffectSystem.md section 99),
    // offered the way Eval_EventTriggersCardEffect_ByCardId (0x1400E26A0) offers a card its id lists name (0x1400E3951 - 0x1400E3986):
    //   entry gate: the event zone's status byte (DuelState + 0x53 + side * 0xD94 + zone * 24) is set, else the evaluator does nothing;
    //   ref = id | ((((record+2) << 6 | event & 0x3F) << 9 | zone & 0x1F | 0x20 (table 1)) << 16), slot = param & 0x1FF,
    //   param = Get_SummonEventParam() (0x1400E19B0). Only the id lists decide whether the card "has" the trigger, so the plugin answers that
    //   itself, with the part's summonCondition (SummonMatches). Returns: own parts found / donor parts found.
    uint32_t(__fastcall* g_SummonEventParam)() = reinterpret_cast<uint32_t(__fastcall*)()>(0x1400E19B0);   // Get_SummonEventParam

    std::pair<bool, bool> OfferOwnSummonTriggers(Clone& root, int eventType, const uint8_t* record, uint16_t raw)
    {
        bool anyOwn = false, anyDonor = false;
        const uint16_t player = *reinterpret_cast<const uint16_t*>(record + 2);
        const uint32_t zone = *reinterpret_cast<const uint32_t*>(record + 4) & 0x1F;
        const bool zoneLive = *reinterpret_cast<const uint8_t*>(0x143497C40 + 0x53 + static_cast<uintptr_t>(player & 1) * 0xD94 + 24 * static_cast<uintptr_t>(*reinterpret_cast<const uint16_t*>(record + 4))) != 0;
        for (Clone* part : AllParts(root))
        {
            if (!part->OwnTrigger || !IsOwnSummonTrigger(part->Trigger))
            {
                anyDonor = anyDonor || (!part->OwnTrigger && TriggerFitsEvent(part->Trigger.empty() ? "summoned" : part->Trigger, static_cast<uint16_t>(eventType)));
                continue;
            }
            anyOwn = true;
            // the evaluator's events: 7 Normal Summon, 8 Flip Summon, 9 Special Summon (its id lists EventEval_IdList_Event7_A / _Event9 /
            // _Event7or9 split them for game cards; TriggerFitsEvent does not, a donor's lists used to)
            if (!zoneLive || !OwnSummonFits(part->Trigger, eventType) || !SummonMatches(root, *part, record, raw))
                continue;
            const uint32_t param = g_SummonEventParam();
            const uint32_t packed = raw | ((((((static_cast<uint32_t>(player) << 6) | (static_cast<uint32_t>(eventType) & 0x3F)) << 9) | zone | 0x20)) << 16);
            g_MenuPart = part;   // every lookup of this offer means this effect (RouteByRow / Hook_InsertOffer)
            const int64_t offered = orig_OfferByRef(packed, static_cast<int16_t>(param & 0x1FF), param);
            g_MenuPart = nullptr;
            static int logged = 0;
            if (++logged <= 20)
                Logger::WriteLog(std::format("Own summon trigger {} of custom card {} (event {}, zone {}): offer {}", part->Trigger, raw, eventType, zone, offered), MODULE_NAME, 0);
        }
        return { anyOwn, anyDonor };
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
        Clone* root = raw ? Find(raw, ignore) : nullptr;
        if (root && !g_MenuPart)
        {
            const auto [anyOwn, anyDonor] = OfferOwnSummonTriggers(*root, eventType, record, raw);
            if (anyOwn && !anyDonor)
                return;   // nothing for a game card's evaluator branch to do (the custom id is in none of its lists)
        }
        if (root && !root->Parts.empty() && !g_MenuPart)
        {
            for (Clone* part : AllParts(*root))
            {
                if (part->OwnTrigger) continue;   // offered by OfferOwnSummonTriggers (or by its own evaluator), never shown as its action card
                if (!part->Trigger.empty() && !TriggerFitsEvent(part->Trigger, static_cast<uint16_t>(eventType))) continue;
                g_MenuPart = part;
                Hook_EventEvaluator(eventType);
            }
            g_MenuPart = nullptr;
            return;
        }
        if (root && root->Parts.empty() && root->OwnTrigger && !g_MenuPart)
            return;   // a one-effect own trigger: offered above when it fits, never evaluated as its action card
        Clone* clone = g_MenuPart ? g_MenuPart : RouteByTrigger(root, kSummonTriggers, 1);

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
        const uint16_t previousShown = g_ShowingSource;
        g_ShowingSource = g_Swap.From;
        orig_EventEvaluator(eventType);
        g_ShowingSource = previousShown;
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

    void OfferWatchers(int eventType, uint32_t ref, bool leave, uint32_t flags);

    void MoveEvaluatorBody(int eventType, uint32_t* ref, uint32_t a3, int a4);

    void __fastcall Hook_MoveEvaluator(int eventType, uint32_t* ref, uint32_t a3, int a4)
    {
        // the moved card's own triggers first (and the game's evaluator), then the cards watching that move (section 91)
        const bool outer = ref && !g_Swap.Active && !g_MenuPart;
        const uint32_t move = ref ? *ref : 0;
        MoveEvaluatorBody(eventType, ref, a3, a4);
        if (outer)
            OfferWatchers(eventType, move, false, 0);
    }

    void MoveEvaluatorBody(int eventType, uint32_t* ref, uint32_t a3, int a4)
    {
        if (g_Swap.Active || !ref)
            return orig_MoveEvaluator(eventType, ref, a3, a4);

        uint32_t& instance = *reinterpret_cast<uint32_t*>(kCardInstances + 8 * static_cast<uintptr_t>(*ref & 0x1FF));
        const uint16_t raw = static_cast<uint16_t>(instance & 0x3FFF);
        bool ignore;
        Clone* root = raw ? Find(raw, ignore) : nullptr;
        if (root && !g_MenuPart)
        {
            // Own move triggers (no game card behind them): offered here the way the evaluator offers a card (0x1400A3765): ref = id |
            // (to zone | table 2 (0x40) | (owner << 6 | event) << 9) << 16, slot = the card instance (move ref bits 0-8; bit 0 = owner).
            bool anyOwn = false, anyDonor = false;
            const uint32_t move = *ref;
            for (Clone* part : AllParts(*root))
            {
                // "During the End Phase, if this card is in the GY / banished because it was sent there this turn": the arrival marker the
                // game's End Phase handler looks for (Eval_CardMovedTriggers adds (owner, 13, id, instance) for its own such cards, msg 53 0x10D)
                if (part->OwnTrigger && (part->Trigger == "grave_end_phase" || part->Trigger == "banished_end_phase"))
                {
                    anyOwn = true;
                    const uint32_t to = (move >> 21) & 0x1F;
                    using AddMarker_t = int64_t(__fastcall*)(int, int, uint16_t, unsigned int, int);   // Add_FlagMarker(player, zone, key, lifetime, value)
                    if (to == (part->Trigger == "grave_end_phase" ? 16u : 17u))
                        reinterpret_cast<AddMarker_t>(0x140083A90)(static_cast<int>(move & 1), 13, raw, 1, static_cast<int>(move & 0x1FF));
                    continue;
                }
                if (!part->OwnTrigger || !IsMoveTrigger(part->Trigger))
                {
                    anyDonor = anyDonor || !part->OwnTrigger;
                    continue;
                }
                anyOwn = true;
                if (!TriggerFitsEvent(part->Trigger, static_cast<uint16_t>(eventType)) || !MoveMatches(*part, move))
                    continue;
                const uint32_t index = move & 0x1FF;
                const uint32_t packed = raw | ((((move >> 21) & 0x1F) | 0x40 | ((((index & 1) << 6) | (static_cast<uint32_t>(eventType) & 0x3F)) << 9)) << 16);
                g_MenuPart = part;   // every lookup of this offer means this effect (RouteByRow / Hook_InsertOffer)
                const int64_t offered = orig_OfferByRef(packed, static_cast<int16_t>(index), static_cast<uint32_t>(a4));
                g_MenuPart = nullptr;
                static int logged = 0;
                if (++logged <= 20)
                    Logger::WriteLog(std::format("Own move trigger {} of custom card {} (event {}, to zone {}): offer {}", part->Trigger, raw, eventType, (move >> 21) & 0x1F, offered), MODULE_NAME, 0);
            }
            if (anyOwn && !anyDonor)
                return orig_MoveEvaluator(eventType, ref, a3, a4);   // nothing to show a source for: other cards may still react to the move
        }
        if (root && !root->Parts.empty() && !g_MenuPart)
        {
            for (Clone* part : AllParts(*root))
            {
                if (part->OwnTrigger) continue;   // offered above, never shown as its action card
                if (!part->Trigger.empty() && !TriggerFitsEvent(part->Trigger, static_cast<uint16_t>(eventType))) continue;
                g_MenuPart = part;
                MoveEvaluatorBody(eventType, ref, a3, a4);
            }
            g_MenuPart = nullptr;
            return;
        }
        Clone* clone = g_MenuPart ? g_MenuPart : RouteByTrigger(root, kLeaveTriggers, 2);
        // A custom card without a clone is NOT skipped here (unlike the summon evaluator): this evaluator may also let other cards react to the move.
        if (!clone)
            return orig_MoveEvaluator(eventType, ref, a3, a4);

        static int logged = 0;
        if (++logged <= 20)
            Logger::WriteLog(std::format("Move event {} for custom card {} (to zone {}): evaluated as source card {}", eventType, raw, (*ref >> 21) & 0x1F, clone->From), MODULE_NAME, 0);

        g_Swap = { true, raw, static_cast<uint16_t>(clone->From) };
        const uint32_t saved = instance;
        instance = (instance & ~0x3FFFu) | (static_cast<uint32_t>(clone->From) & 0x3FFF);
        const uint16_t previousShown = g_ShowingSource;
        g_ShowingSource = g_Swap.From;
        orig_MoveEvaluator(eventType, ref, a3, a4);
        g_ShowingSource = previousShown;
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
        if (customId < kFirstExtraCardId)
            return customId;   // an overridden game card plays under its own id
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
        // Own-trigger cards (Clone::OwnTrigger): rows {family handler, custom id} appended after the table's last row (AppendAfter = that row's
        // card id): the driver reaches them in every pass exactly as it would a row of its own (docs/EffectSystem.md section 61).
        uint16_t AppendAfter = 0;
        std::vector<std::pair<uintptr_t, uint16_t>> Virtual;
    };

    // The phase handler families (traced 2026-10-25, docs/EffectSystem.md section 60): handler(turn player, card id) gates on turn ^ parity and
    // offers the card in that player's Monster Zones with event 2 / 6, then sets the zone's status bit 43 ("offered this phase",
    // Zone_SetStatusBit 0x1400A83E0). Mutable so the routing test can stand in for the game's functions.
    std::pair<const char*, uintptr_t> g_PhaseFamilies[] = {
        { "standby_phase", 0x140148EB0 }, { "opponent_standby_phase", 0x140148FA0 }, { "each_standby_phase", 0x140148FB0 },
        { "end_phase", 0x14014D9A0 }, { "opponent_end_phase", 0x14014DAB0 }, { "each_end_phase", 0x14014DAC0 },
        // "During the End Phase, if this card is in the GY / banished because it was sent there this turn": PhaseHandler_End_ArrivedThisTurn
        // (0x14014DDC0, zone 16 via 0x14014E000 / 17 via 0x14014E360) offers the card from that zone while marker (player, 13, its id, its
        // instance) exists - the marker Eval_CardMovedTriggers adds when it arrives (Hook_MoveEvaluator adds it for own cards; section 79)
        { "grave_end_phase", 0x14014E000 }, { "banished_end_phase", 0x14014E360 },
        // "At the end of the Battle Phase" (section 93): Phase_EndOfBattlePhase_RunHandlers' table 0x140B2B2C0 - PhaseHandler_EndBattle_Each
        // (0x14021D140: player = turn ^ parity, so either player's Battle Phase; 68 game cards) / PhaseHandler_EndBattle_TurnPlayer (0x14021D050:
        // the turn player's only), both offering event 24 and setting zone bit 43
        { "battle_phase_end", 0x14021D140 }, { "your_battle_phase_end", 0x14021D050 } };
    uintptr_t PhaseFamilyFor(const std::string& trigger)
    {
        for (const auto& [name, fn] : g_PhaseFamilies)
            if (trigger == name)
                return fn;
        return 0;
    }
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
        if (hook.AppendAfter && static_cast<uint16_t>(id) == hook.AppendAfter)
        {
            // the plugin's own rows after the table's last one, called the way the driver calls a row (a 0 = offered: the step re-runs)
            for (const auto& [family, custom] : hook.Virtual)
            {
                const uint16_t engineId = EngineIdFor(custom);
                if (engineId == 0 || engineId > 0x3FFF)
                    continue;
                g_InPhaseExtra = true;
                const int64_t own = reinterpret_cast<PhaseHandler_t>(family)(player, engineId, a3, a4);
                g_InPhaseExtra = false;
                if (own == 0)
                {
                    static int logged = 0;
                    if (++logged <= 20)
                        Logger::WriteLog(std::format("Own phase row {:X} offered custom card {}", family, engineId), MODULE_NAME, 0);
                    return 0;
                }
            }
        }
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

    // The hook slot for a phase handler function (one per function), allocated on first use.
    std::map<uintptr_t, int> g_PhaseSlotOf;
    PhaseHook* HookFor(uintptr_t fn, uint16_t customId)
    {
        auto slot = g_PhaseSlotOf.find(fn);
        if (slot == g_PhaseSlotOf.end())
        {
            if (g_PhaseHookCount >= kMaxPhaseHooks)
            {
                Logger::WriteLog(std::format("Too many phase handlers to hook; custom card {} gets no phase trigger", customId), MODULE_NAME, 2);
                return nullptr;
            }
            slot = g_PhaseSlotOf.emplace(fn, g_PhaseHookCount++).first;
            g_PhaseHooks[slot->second].Orig = reinterpret_cast<PhaseHandler_t>(fn);
        }
        return &g_PhaseHooks[slot->second];
    }

    // Detours every handler whose rows name a clone's source card, and the last-row handlers own-trigger cards append to. Called once the
    // clones are loaded.
    void AttachPhaseHandlers()
    {
        for (const PhaseTable& table : kPhaseTables)
        {
            for (int r = 0; r < table.Count; ++r)
            {
                const uintptr_t fn = *reinterpret_cast<const uint64_t*>(table.Rows + 16 * static_cast<uintptr_t>(r));
                const uint16_t sourceId = static_cast<uint16_t>(*reinterpret_cast<const uint32_t*>(table.Rows + 16 * static_cast<uintptr_t>(r) + 8));
                for (auto& [customId, cardClone] : g_Clones)
                for (Clone* part : AllParts(cardClone))   // every effect of the card (multi-effect cards)
                {
                    if (part->OwnTrigger || part->From != sourceId)
                        continue;   // an own-trigger effect's "from" is its action card, not a phase row
                    PhaseHook* hook = HookFor(fn, customId);
                    if (!hook)
                        continue;
                    hook->CustomBySource[sourceId].push_back(customId);
                }
            }
        }
        // Own-trigger phase effects: a virtual row {family handler, custom id} after the last row of the Standby table (Duel__Phase__EnterStandbyPhase
        // calls rows 0..294 per pass; row 294 = Borrel Supplier 14620) / the End Phase table (row 551 = 5406, no flag bytes, so every End Phase
        // step reaches it). The family handler is the game's generic one; no game card's row or effect is borrowed.
        for (auto& [customId, cardClone] : g_Clones)
            for (Clone* part : AllParts(cardClone))
            {
                const uintptr_t family = part->OwnTrigger ? PhaseFamilyFor(part->Trigger) : 0;
                if (!family)
                    continue;
                // the Standby table, the end-of-Battle-Phase table (row 100 = Extinction on Schedule 12032) or the End Phase table
                const PhaseTable& table = kPhaseTables[part->Trigger.find("standby") != std::string::npos ? 0
                    : part->Trigger.find("battle_phase_end") != std::string::npos ? 3 : 4];
                const uintptr_t lastRow = table.Rows + 16 * static_cast<uintptr_t>(table.Count - 1);
                PhaseHook* hook = HookFor(*reinterpret_cast<const uint64_t*>(lastRow), customId);
                if (!hook)
                    continue;
                hook->AppendAfter = static_cast<uint16_t>(*reinterpret_cast<const uint32_t*>(lastRow + 8));
                if (std::find(hook->Virtual.begin(), hook->Virtual.end(), std::make_pair(family, customId)) == hook->Virtual.end())
                    hook->Virtual.emplace_back(family, customId);
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
            for (Clone* part : AllParts(clone))   // every effect of the card (multi-effect cards); a raw row has no source
                if (part->From)
                    g_CustomBySource[static_cast<uint16_t>(part->From)].push_back(customId);
        ((g_NamedOrig[I] = reinterpret_cast<Named_t>(kNamedHelpers[I].Address), DetourAttach(&(PVOID&)g_NamedOrig[I], reinterpret_cast<void*>(&NamedThunk<I>))), ...);
    }

    // ---- own attack triggers ----------------------------------------------------------------------------------------------
    // Eval_AttackDeclaredTriggers_Ev18 (0x140086C00, arg = replay count) reads the attack record 0x14349C480: +0 attacker player, +1 the other
    // player, +2 direct attack, word +8 attacker zone, word +0xA attack target zone; it offers event 18 through Offer_EffectOfCardAtZone with a
    // packed param (below) to the attacker (id list), the other player's monsters (per-id ladder), the target (id list word_140AD1220) and named
    // cards. An own attack trigger (docs/EffectSystem.md section 67) is offered the same way after the game's own offers, on the first pass only:
    // attack_declared = this card is the attacker, opponent_attack_declared = this card is a face-up monster of the player being attacked,
    // attacked = this card is the attack target.
    // "usesPerTurn": Get_UseLimitPerCopyCount (0x1401F64E0, EffectRecord*: word 0 = card id) is how many per-copy uses (classes 1-4)
    // Timing_Check_Class1..4 allow per turn - 1, or 2 for a few game ids. A custom card answers its largest "usesPerTurn".
    using UseCount_t = int(__fastcall*)(const uint16_t*);
    UseCount_t orig_UseCount = reinterpret_cast<UseCount_t>(0x1401F64E0);

    int __fastcall Hook_UseCount(const uint16_t* effect)
    {
        bool borrowedWithoutClone = false;
        if (const Clone* c = effect ? Find(effect[0], borrowedWithoutClone) : nullptr)
        {
            int uses = 0;
            for (const Clone* part : AllParts(*const_cast<Clone*>(c)))
                uses = (std::max)(uses, part->UsesPerTurn);
            if (uses > 1)
                return uses;
        }
        return orig_UseCount(effect);
    }

    // "costPick": Get_CostPickCount_ById (0x1401F7450, EffectRecord*: word 0 = id) is how many cards Cost_BanishPickedCards picks for the id
    // (1 by default, The Beginning of the End's 7452 = 5). While a costPick sub-clone runs the cost under its id, it answers its own count.
    using CostPickCount_t = int(__fastcall*)(const uint16_t*);
    CostPickCount_t orig_CostPickCount = reinterpret_cast<CostPickCount_t>(0x1401F7450);

    // A multi-target move's maximum: the game's multi-target picker (0x140094DC0) asks sub_140094490 (record) how many targets an id may
    // choose (2 unless the id says otherwise). The clone answers its "count".
    using TargetCount_t = int64_t(__fastcall*)(uint16_t*);
    TargetCount_t orig_TargetCount = reinterpret_cast<TargetCount_t>(0x140094490);

    int64_t __fastcall Hook_TargetCount(uint16_t* effect)
    {
        const Clone* clone = g_Active;
        if (!clone && effect)
        {
            bool borrowedWithoutClone;
            clone = RouteByRow(Find(effect[0], borrowedWithoutClone), effect);
        }
        if (clone && clone->MoveScope == 1 && clone->MoveCount > 1)
            return clone->MoveCount;
        return orig_TargetCount(effect);
    }

    // The raw pick cost asked about by this id (section 90): the part running its machine (g_Active), or - between the machine's steps - the
    // one whose picker is up (g_Paying).
    Clone* PayingClone(uint16_t id)
    {
        if (g_Active)
            return g_Active->CostPickCount > 0 && g_Active->RawSlots && MatchesFrom(g_Active, id) ? g_Active : nullptr;
        return g_Paying && MatchesFrom(g_Paying, id) ? g_Paying : nullptr;
    }

    // Get_SendHandCount_ById (0x1401F7120) / Get_TributeCount_ById (0x1401F76A0, returns the minimum, *outMax the maximum): 0 / 1 for an id the
    // game does not list; a raw send / Tribute cost answers its own count.
    using SendHandCount_t = int64_t(__fastcall*)(uint16_t*);
    SendHandCount_t orig_SendHandCount = reinterpret_cast<SendHandCount_t>(0x1401F7120);
    using TributeCount_t = int64_t(__fastcall*)(uint16_t*, int*);
    TributeCount_t orig_TributeCount = reinterpret_cast<TributeCount_t>(0x1401F76A0);

    int64_t __fastcall Hook_SendHandCount(uint16_t* effect)
    {
        if (Clone* pick = effect ? PayingClone(effect[0]) : nullptr; pick && pick->Verb == Clone::PickVerb::SendHand)
            return pick->CostPickCount;
        return orig_SendHandCount(effect);
    }

    int64_t __fastcall Hook_TributeCount(uint16_t* effect, int* outMax)
    {
        if (Clone* pick = effect ? PayingClone(effect[0]) : nullptr; pick && pick->Verb == Clone::PickVerb::Tribute)
        {
            if (outMax)
                *outMax = pick->CostPickCount;
            return pick->CostPickCount;
        }
        return orig_TributeCount(effect, outMax);
    }

    int __fastcall Hook_CostPickCount(const uint16_t* effect)
    {
        if (effect && g_Active && g_Active->CostPickCount > 0 && MatchesFrom(g_Active, effect[0]))
            return g_Active->CostPickCount;
        return orig_CostPickCount(effect);
    }

    // ---- "You cannot Special Summon the turn you activate this" ("summonLock", docs/EffectSystem.md section 77) ----
    // Register_UseLimit_Marker (0x1400E0E50, EffectRecord*) runs once per activation, after the cost (Chain_ActivateEffect_StateMachine): a
    // custom card with a summon lock adds marker (controller, 13, its own id, lifetime 1 = this turn) there, the way the game's cards put their
    // id in the player's list (Slot0_AddOwnIdPlayerMarker 0x1401776B0). Summon_CheckPlayerRestrictions (0x140058440, player, cardId, instance,
    // fromZone 14 = Extra Deck, sourceKind) then refuses what the rule forbids while the marker exists.
    using RegisterLimit_t = int64_t(__fastcall*)(uint16_t*);
    RegisterLimit_t orig_RegisterLimit = reinterpret_cast<RegisterLimit_t>(0x1400E0E50);
    using SummonRestrictions_t = BOOL(__fastcall*)(unsigned int, int, int, int, int);
    SummonRestrictions_t orig_SummonRestrictions = reinterpret_cast<SummonRestrictions_t>(0x140058440);
    const auto AddFlagMarker = reinterpret_cast<int64_t(__fastcall*)(int, int, uint16_t, unsigned int, int)>(0x140083A90);   // player, zone, key, lifetime, value
    const auto HasFlagMarkerKey = reinterpret_cast<int(__fastcall*)(int, int, int)>(0x140052560);                           // player, zone, key
    std::vector<std::pair<uint16_t, const Clone*>> g_SummonLocks;   // (custom id = marker key, the part with the rule)

    int64_t __fastcall Hook_RegisterLimit(uint16_t* effect)
    {
        const int64_t result = orig_RegisterLimit(effect);
        if (!effect || g_SummonLocks.empty())
            return result;
        bool borrowedWithoutClone = false;
        if (Clone* c = Find(effect[0], borrowedWithoutClone))
            for (const auto& [customId, part] : g_SummonLocks)
                if (&g_Clones[customId] == c && !part->SummonLockOnResolve)
                {
                    AddFlagMarker((effect[1] ^ effect[5]) & 1, 13, customId, 1, 0);   // the effect's controller, this turn
                    break;
                }
        return result;
    }

    // A part's slot 0 returned 0 (the effect has resolved, or its chain stopped because a step found nothing): a "when": "resolve" lock now
    // adds the controller's marker for the rest of the turn. Slot 0 is not called for a negated activation.
    void EffectResolved(const Clone* part, const uint16_t* effect, uint16_t customId)
    {
        if (!part->HasSummonLock || !part->SummonLockOnResolve)
            return;
        AddFlagMarker((effect[1] ^ effect[5]) & 1, 13, customId, 1, 0);
    }

    bool SummonLockAllows(const Clone& rule, int cardId, int fromZone)
    {
        using IdTest_t = int(__fastcall*)(int);
        static const IdTest_t kindTests[] = { nullptr, reinterpret_cast<IdTest_t>(0x140742EF0), reinterpret_cast<IdTest_t>(0x140742F20),
            reinterpret_cast<IdTest_t>(0x140742F80), reinterpret_cast<IdTest_t>(0x1407432A0), reinterpret_cast<IdTest_t>(0x140742FE0) };   // Kind_IsFusion / Synchro / Xyz / Link / Ritual
        const auto MonsterType = reinterpret_cast<int(__fastcall*)(int)>(0x14081A7B0);          // YGO::CARDS::Get_MonsterType (race)
        const auto Attribute = reinterpret_cast<int(__fastcall*)(int)>(0x14081A5D0);            // YGO::CARDS::Get_AttributeFromFullCardProps
        const auto InArchetype = reinterpret_cast<int(__fastcall*)(int, int)>(0x1407EB9E0);     // Is_CardInNamedArchetype_Thunk
        if (rule.SummonLockExtraOnly && fromZone != 14)
            return true;
        const bool except = rule.SummonLockKind || rule.SummonLockRace || rule.SummonLockAttribute || rule.SummonLockArchetype;
        if (!except)
            return false;
        if (rule.SummonLockKind && !kindTests[rule.SummonLockKind](cardId)) return false;
        if (rule.SummonLockRace && MonsterType(cardId) != rule.SummonLockRace) return false;
        if (rule.SummonLockAttribute && Attribute(cardId) != rule.SummonLockAttribute) return false;
        if (rule.SummonLockArchetype && !InArchetype(cardId, rule.SummonLockArchetype)) return false;
        return true;
    }

    BOOL __fastcall Hook_SummonRestrictions(unsigned int player, int cardId, int instance, int fromZone, int sourceKind)
    {
        if (!orig_SummonRestrictions(player, cardId, instance, fromZone, sourceKind))
            return FALSE;
        for (const auto& [customId, part] : g_SummonLocks)
            if (HasFlagMarkerKey(static_cast<int>(player & 1), 13, customId) && !SummonLockAllows(*part, cardId & 0x3FFF, fromZone))
                return FALSE;
        return TRUE;
    }

    using AttackEval_t = int64_t(__fastcall*)(int);
    AttackEval_t orig_AttackEval = reinterpret_cast<AttackEval_t>(0x140086C00);
    using OfferAtZone_t = int64_t(__fastcall*)(int, int, int, uint32_t);
    OfferAtZone_t Offer_EffectOfCardAtZone = reinterpret_cast<OfferAtZone_t>(0x1400AAF90);
    using ZoneHolds_t = BOOL(__fastcall*)(unsigned int, int, int);
    ZoneHolds_t Zone_HoldsFaceUpCard = reinterpret_cast<ZoneHolds_t>(0x1400139D0);
    std::vector<uint16_t> g_OwnAttackCards;   // custom cards with an own attack trigger (filled at load)

    // The param the evaluator gives every event-18 offer (0x140086C6D..0x140086CA0), from the attack record.
    uint32_t AttackParam(const uint8_t* attack, int replay)
    {
        const uint32_t dword0 = *reinterpret_cast<const uint32_t*>(attack), dword8 = *reinterpret_cast<const uint32_t*>(attack + 8);
        const uint32_t zoneA = static_cast<uint32_t>(*reinterpret_cast<const int16_t*>(attack + 0xA));
        const uint32_t inner = (replay > 0 ? 2u : 0u) | (attack[2] & 1u);
        return (attack[0] & 1u) | (dword0 & 0xFFFFC000u) | (2u * ((dword8 & 0x1Fu) | (32u * ((attack[1] & 1u) | (2u * ((zoneA & 0x1Fu) | (32u * inner)))))));
    }

    bool HasOwnTrigger(uint16_t engineId, const char* trigger)
    {
        bool ignore;
        if (Clone* root = Find(engineId, ignore))
            for (Clone* part : AllParts(*root))
                if (part->OwnTrigger && part->Trigger == trigger)
                    return true;
        return false;
    }

    int64_t __fastcall Hook_AttackEval(int replay)
    {
        const int64_t result = orig_AttackEval(replay);
        if (replay != 0 || g_OwnAttackCards.empty())
            return result;
        const auto* attack = reinterpret_cast<const uint8_t*>(0x14349C480);
        const int attacker = static_cast<int8_t>(attack[0]) & 1, defender = static_cast<int8_t>(attack[1]) & 1;
        const int attackerZone = *reinterpret_cast<const int16_t*>(attack + 8), targetZone = *reinterpret_cast<const int16_t*>(attack + 0xA);
        const bool direct = attack[2] & 1;
        const uint32_t param = AttackParam(attack, replay);
        for (const uint16_t custom : g_OwnAttackCards)
        {
            const uint16_t id = EngineIdFor(custom);
            if (!id || id > 0x3FFF)
                continue;
            if (HasOwnTrigger(id, "attack_declared") && Zone_HoldsFaceUpCard(attacker, attackerZone, id))
                Offer_EffectOfCardAtZone(attacker, attackerZone, 18, param);
            if (HasOwnTrigger(id, "attacked") && !direct && Zone_HoldsFaceUpCard(defender, targetZone, id))
                Offer_EffectOfCardAtZone(defender, targetZone, 18, param);
            if (HasOwnTrigger(id, "opponent_attack_declared"))
                for (int zone = 0; zone <= 6; ++zone)
                    if (Zone_HoldsFaceUpCard(defender, zone, id))
                        Offer_EffectOfCardAtZone(defender, zone, 18, param);
        }
        return result;
    }

    // ---- own start-of-Damage-Step triggers (docs/EffectSystem.md section 80) -----------------------------------------------------
    // Eval_StartOfDamageStepTriggers_Ev20 (0x140210580, isAttackerSide; run for both sides by Battle_StartOfDamageStepMachine 0x140210C10
    // case 2) offers the battling monster of that side (g_BattleSides) through Offer_EffectOfCardAtZone(player, zone, 20, param), param =
    // attacker player | attacker zone << 8 | (defender player | target zone << 8) << 16: "if this card battles" ids on either side when it is
    // not a direct attack, "if this card attacks" ids on the attacker's side only. Own triggers damage_step_battles / damage_step_attacks are
    // offered the same way after it.
    using DamageStepStart_t = int64_t(__fastcall*)(int);
    DamageStepStart_t orig_DamageStepStart = reinterpret_cast<DamageStepStart_t>(0x140210580);
    std::vector<uint16_t> g_OwnDamageStepCards;

    int64_t __fastcall Hook_DamageStepStart(int isAttackerSide)
    {
        const int64_t result = orig_DamageStepStart(isAttackerSide);
        const auto* attack = reinterpret_cast<const uint8_t*>(0x14349C480);   // AttackRecord
        if (g_OwnDamageStepCards.empty() || (attack[2] & 1))                 // a direct attack: no battle
            return result;
        const int attacker = attack[0] & 1, defender = attack[1] & 1;
        const int attackerZone = *reinterpret_cast<const int16_t*>(attack + 8), targetZone = *reinterpret_cast<const int16_t*>(attack + 0xA);
        const int side = isAttackerSide ? attacker : defender;
        const auto* battleSide = reinterpret_cast<const uint8_t*>(0x14349C494 + 40 * side);   // g_BattleSides[side]: +0 player, +1 zone
        const int player = battleSide[0] & 1, zone = battleSide[1];
        const uint32_t param = static_cast<uint32_t>(attacker | ((attackerZone & 0xFF) << 8) | ((defender | ((targetZone & 0xFF) << 8)) << 16));
        for (const uint16_t custom : g_OwnDamageStepCards)
        {
            const uint16_t id = EngineIdFor(custom);
            if (!id || id > 0x3FFF || !Zone_HoldsFaceUpCard(player, zone, id))
                continue;
            if (HasOwnTrigger(id, "damage_step_battles") || (isAttackerSide && HasOwnTrigger(id, "damage_step_attacks")))
                Offer_EffectOfCardAtZone(player, zone, 20, param);
        }
        return result;
    }

    // ---- own start-of-Battle-Phase triggers (docs/EffectSystem.md section 93) -------------------------------------------------------
    // Battle_StartOfBattlePhaseStep (0x14020DBE0, step 0 of the Battle Phase driver table 0x140B2B1E0, turn player) in sub-state 2
    // (HIDWORD of PlayerState +0x37CA) offers the turn player's face-up monsters whose zone bit 43 ("offered this phase", UNK_03 & 0x800) is
    // clear with Offer_EffectOfCardAtZone(player, zone, 4, 0) - only one game card (11466) - sets bit 43 on an offer and returns 0 (the step
    // re-runs), and moves on when none is left. Own battle_phase_start (either player's), your_battle_phase_start, opponent_battle_phase_start
    // monsters are offered the same way first, turn player's side first; bit 43 is cleared when the phase changes.
    using BattleStep_t = int64_t(__fastcall*)(unsigned int);
    BattleStep_t orig_BattlePhaseStart = reinterpret_cast<BattleStep_t>(0x14020DBE0);
    std::vector<uint16_t> g_OwnBattleStartCards;
    using SetStatusBit_t = void(__fastcall*)(int, int, int, int);
    SetStatusBit_t Zone_SetStatusBit = reinterpret_cast<SetStatusBit_t>(0x1400A83E0);

    int64_t __fastcall Hook_BattlePhaseStart(unsigned int turnPlayer)
    {
        const uint32_t subState = *reinterpret_cast<const uint32_t*>(kDuelState + 0x37CA + 4);
        if (subState == 2 && !g_OwnBattleStartCards.empty())
        {
            const auto* offered = reinterpret_cast<const uint32_t*>(0x14349C020);   // the offer queue's count
            for (int pass = 0; pass < 2; ++pass)
            {
                const int player = static_cast<int>((turnPlayer ^ pass) & 1);
                const char* whose = pass == 0 ? "your_battle_phase_start" : "opponent_battle_phase_start";
                for (int zone = 0; zone <= 6; ++zone)
                {
                    const uint32_t flags3 = *reinterpret_cast<const uint32_t*>(kDuelState + 0x4C + 3476 * player + 24 * zone + 0x14);
                    if (flags3 & 0x800)
                        continue;
                    for (const uint16_t custom : g_OwnBattleStartCards)
                    {
                        const uint16_t id = EngineIdFor(custom);
                        if (!id || id > 0x3FFF || !Zone_HoldsFaceUpCard(player, zone, id) || !(HasOwnTrigger(id, "battle_phase_start") || HasOwnTrigger(id, whose)))
                            continue;
                        const uint32_t before = *offered;
                        Offer_EffectOfCardAtZone(player, zone, 4, 0);
                        if (*offered != before)
                        {
                            Zone_SetStatusBit(player, zone, 43, 1);
                            return 0;   // offered: the step re-runs (as the game's own loop does)
                        }
                    }
                }
            }
        }
        return orig_BattlePhaseStart(turnPlayer);
    }

    // ---- own summon-reaction triggers ---------------------------------------------------------------------------------------
    // Eval_SummonReactionTriggers (0x1400E48F0, event 7 Normal Summon / 10 Set / 8 Flip / 9 Special) offers the cards that react to ANOTHER card's
    // summon, by id, through Offer_EffectOfNamedCardOnField (0x1400AB360) with param = Get_SummonEventParam() (0x1400E19B0); a "your opponent
    // summons" card is offered for 1 - EventRecord.word0 (0x1400E53CA). Own reaction triggers (docs/EffectSystem.md section 68) are offered the
    // same way after it: opponent_special_summoned (event 9, the summoner's opponent), opponent_summoned (7 or 9, the summoner's opponent),
    // monster_special_summoned (event 9, both players), monster_summoned (7 or 9, both players); "to your field" / a card filter in the
    // summon condition narrows them (section 87).
    using SummonReaction_t = void(__fastcall*)(int);
    SummonReaction_t orig_SummonReaction = reinterpret_cast<SummonReaction_t>(0x1400E48F0);
    using SummonParam_t = uint32_t(__fastcall*)();
    SummonParam_t Get_SummonEventParam = reinterpret_cast<SummonParam_t>(0x1400E19B0);
    using OfferNamed_t = int64_t(__fastcall*)(int, int, int, uint32_t, int, int);
    OfferNamed_t Offer_EffectOfNamedCardOnField = reinterpret_cast<OfferNamed_t>(0x1400AB360);
    std::vector<uint16_t> g_OwnSummonReactionCards;   // custom cards with an own reaction trigger (filled at load)

    // KIND_TABLE (0x140BF7820, 6 x s16 per kind) Category != 0, as every Offer_EffectOfNamedCardOnField caller computes its last argument.
    int GameIsMonsterCategory(uint16_t id)
    {
        const auto kindOf = reinterpret_cast<int(__fastcall*)(int)>(0x14081A650);   // Get_CardTypeFromFullCardPropsByKonamiId
        return *reinterpret_cast<const int16_t*>(0x140BF7820 + 12 * static_cast<uintptr_t>(kindOf(id)) + 4) != 0;
    }
    int (*IsMonsterCategory)(uint16_t) = &GameIsMonsterCategory;   // replaceable for the routing test

    void __fastcall Hook_SummonReaction(int eventType)
    {
        orig_SummonReaction(eventType);
        if (g_OwnSummonReactionCards.empty() || (eventType != 7 && eventType != 9))
            return;
        const auto* record = reinterpret_cast<const uint8_t*>(0x14349C560);   // the summon event record (docs section 46)
        const int summoner = *reinterpret_cast<const uint16_t*>(record) & 1;   // EventRecord.word0
        const uint32_t param = Get_SummonEventParam();
        for (const uint16_t custom : g_OwnSummonReactionCards)
        {
            const uint16_t id = EngineIdFor(custom);
            bool ignore;
            Clone* root = id && id <= 0x3FFF ? Find(id, ignore) : nullptr;
            if (!root)
                continue;
            // which players this card reacts for: any reaction part whose trigger fits the event and whose summon conditions ("from extra" ...)
            // the summoned monster's record meets (SummonMatches, the same checks as a card's own summon triggers)
            // players (bit per side) whose copies of this card are offered: "your opponent summons" = the summoner's opponent; "a monster is
            // summoned" = both, or only the side whose field it went to / the other one ("to your field", Summon.ToSide; section 87)
            const int summonedSide = *reinterpret_cast<const uint16_t*>(record + 2) & 1;
            int players = 0;
            for (Clone* part : AllParts(*root))
            {
                if (!part->OwnTrigger || !TriggerFitsEvent(part->Trigger, static_cast<uint16_t>(eventType)) || !SummonMatches(*root, *part, record, id))
                    continue;
                if (part->Trigger == "opponent_special_summoned" || part->Trigger == "opponent_summoned")
                    players |= 1 << (1 - summoner);
                else if (part->Trigger == "monster_special_summoned" || part->Trigger == "monster_summoned")
                    players |= part->Summon.ToSide == 0 ? 1 << summonedSide : part->Summon.ToSide == 1 ? 1 << (1 - summonedSide) : 3;
            }
            const int monster = IsMonsterCategory(id);
            for (int player = 0; player < 2; ++player)
                if (players & (1 << player))
                    Offer_EffectOfNamedCardOnField(player, id, eventType, param, -1, monster);
        }
    }

    // ---- own watcher triggers: another card destroyed / left the field / sent to the GY / banished (docs/EffectSystem.md section 91) --
    // The game's watchers ("If a monster you control is destroyed ...") are named-card offers inside the per-move ladders: Eval_LeaveFieldTriggers
    // (0x1400C2CA0; every field card leaving, from Card_Destroy and Card_LeaveFieldToZone) and Eval_CardMovedTriggers (0x1400A2F70; sent to the
    // GY / banished) call Offer_EffectOfNamedCardOnField (table 1) / Offer_EffectOfNamedCardInGrave (0x1400ABF60, table 2) with a literal card
    // id, the evaluator's event and its reference as the param. Own watcher triggers are offered the same way after the evaluator.
    using OfferNamedGrave_t = int64_t(__fastcall*)(int, int, int, uint32_t);
    OfferNamedGrave_t Offer_EffectOfNamedCardInGrave = reinterpret_cast<OfferNamedGrave_t>(0x1400ABF60);
    using LeaveEval_t = void(__fastcall*)(uint32_t, int, int, uint32_t, int, int16_t, int);
    LeaveEval_t orig_LeaveEval = reinterpret_cast<LeaveEval_t>(0x1400C2CA0);
    std::vector<uint16_t> g_OwnWatchCards;   // custom cards with an own watcher trigger (filled at load)

    // What happened to the card, from either reference (leave: Eval_LeaveFieldTriggers' leaveRef; move: Eval_CardMovedTriggers' moveRef).
    struct WatchedMove
    {
        bool Leave = false;        // from Eval_LeaveFieldTriggers (destroyed / left the field), else from Eval_CardMovedTriggers
        uint16_t Id = 0;           // the card's id (card instance table)
        int Controller = 0;        // ref bit 9
        uint32_t From = 0, To = 0; // ref bits 10-14 / 21-25
        bool FromField = false, Destroyed = false, Battle = false, ByOpponent = false;
    };

    // Kind_IsFusion / Synchro / Xyz / Link / Ritual, by id (as SummonMatches)
    bool WatchedCardMatches(const Clone::WatchCondition& w, uint16_t id)
    {
        using IdTest_t = int(__fastcall*)(int);
        static const IdTest_t kindTests[] = { nullptr, reinterpret_cast<IdTest_t>(0x140742EF0), reinterpret_cast<IdTest_t>(0x140742F20),
            reinterpret_cast<IdTest_t>(0x140742F80), reinterpret_cast<IdTest_t>(0x1407432A0), reinterpret_cast<IdTest_t>(0x140742FE0) };
        if (!id)
            return false;
        if (w.Kind)
        {
            // card type 13 = Spell, 14 = Trap (Get_CardTypeFromFullCardPropsByKonamiId, as Lock.cpp KindMatches)
            const int type = reinterpret_cast<int(__fastcall*)(int)>(YGO::Addr::Card_GetTypeFromFullCardPropsByKonamiId)(id);
            if (w.Kind == 2 ? type != 13 : w.Kind == 3 ? type != 14 : (type == 13 || type == 14))
                return false;
        }
        return (!w.Race || reinterpret_cast<int(__fastcall*)(int)>(0x14081A7B0)(id) == w.Race)                    // Get_MonsterType
            && (!w.Attribute || reinterpret_cast<int(__fastcall*)(int)>(0x14081A5D0)(id) == w.Attribute)          // Get_AttributeFromFullCardProps
            && (!w.Archetype || reinterpret_cast<int(__fastcall*)(int, int)>(0x1407EB9E0)(id, w.Archetype))      // Is_CardInNamedArchetype
            && (!w.CardKind || kindTests[w.CardKind](id))
            && (!w.Name || reinterpret_cast<int64_t(__fastcall*)(int, int)>(0x1407EBA00)(id, w.Name));           // j_Card_IsSameName
    }

    // Does this watcher part (held by watcherPlayer, engine id watcherId) react to the move?
    bool WatchMatches(const Clone& part, const WatchedMove& m, int watcherPlayer, uint16_t watcherId)
    {
        const auto& w = part.Watch;
        if (part.Trigger == "card_destroyed" ? !(m.Leave && m.Destroyed)
            : part.Trigger == "card_left_field" ? !m.Leave
            : part.Trigger == "card_sent_to_grave" ? (m.Leave || m.To != 16)
            : part.Trigger == "card_banished" ? (m.Leave || m.To != 17) : true)
            return false;
        if (w.Side >= 0 && (m.Controller == watcherPlayer) != (w.Side == 0))
            return false;
        if (w.FromZones && (m.From >= 32 || !(w.FromZones & (1u << m.From))))
            return false;
        if ((w.By == 1 && !m.Battle) || (w.By == 2 && m.Battle) || (w.By == 3 && (m.Battle || !m.ByOpponent)))
            return false;
        if (w.Other && m.Id == watcherId)
            return false;
        return WatchedCardMatches(w, m.Id);
    }

    void OfferWatchers(int eventType, uint32_t ref, bool leave, uint32_t flags)
    {
        if (g_OwnWatchCards.empty())
            return;
        WatchedMove m;
        m.Leave = leave;
        m.Id = static_cast<uint16_t>(*reinterpret_cast<const uint32_t*>(kCardInstances + 8 * static_cast<uintptr_t>(ref & 0x1FF)) & 0x3FFF);
        m.Controller = (ref >> 9) & 1;
        m.From = (ref >> 10) & 0x1F;
        m.To = (ref >> 21) & 0x1F;
        if (leave)
        {
            m.FromField = true;
            m.Destroyed = eventType == 22;   // Card_Destroy flags bit 2 (leave ref bit 17); 30 = sent / banished by Card_Destroy, not destroyed
            // by battle: the battling monster of that side (g_BattleSides, 40 bytes per player: +1 zone, +0x23 destroyed by battle) is the card
            const auto* side = reinterpret_cast<const uint8_t*>(0x14349C494 + 40 * static_cast<uintptr_t>(m.Controller));
            m.Battle = m.Destroyed && side[1] == m.From && side[0x23] != 0;
            m.ByOpponent = eventType != 34 && (flags & 1);   // Card_Destroy flags bit 0: the source is the card's controller's opponent
        }
        else
        {
            m.FromField = ((ref >> 16) & 1) || m.From <= 12;
            m.Battle = (ref >> 18) & 1;
        }
        for (const uint16_t custom : g_OwnWatchCards)
        {
            const uint16_t id = EngineIdFor(custom);
            bool ignore;
            Clone* root = id && id <= 0x3FFF ? Find(id, ignore) : nullptr;
            if (!root)
                continue;
            for (Clone* part : AllParts(*root))
            {
                if (!part->OwnTrigger || !IsWatchTrigger(part->Trigger))
                    continue;
                for (int player = 0; player < 2; ++player)
                {
                    if (!WatchMatches(*part, m, player, id))
                        continue;
                    g_MenuPart = part;   // every lookup of this offer means this effect
                    const int64_t offered = part->Watch.InGrave ? Offer_EffectOfNamedCardInGrave(player, id, eventType, ref)
                                                                : Offer_EffectOfNamedCardOnField(player, id, eventType, ref, -1, IsMonsterCategory(id));
                    g_MenuPart = nullptr;
                    static int logged = 0;
                    if (++logged <= 20)
                        Logger::WriteLog(std::format("Own watcher {} of custom card {} (player {}, event {}, card {} from zone {} to {}): offer {}", part->Trigger, custom,
                            player, eventType, m.Id, m.From, m.To, offered), MODULE_NAME, 0);
                }
            }
        }
    }

    void __fastcall Hook_LeaveEval(uint32_t leaveRef, int notDestroyed, int srcCardId, uint32_t flags, int a5, int16_t a6, int srcZone)
    {
        orig_LeaveEval(leaveRef, notDestroyed, srcCardId, flags, a5, a6, srcZone);
        // the evaluator's own event: 34 any other leave, 22 destroyed (leave ref bit 17), 30 sent / banished by Card_Destroy
        OfferWatchers(notDestroyed ? 34 : ((leaveRef >> 17) & 1) ? 22 : 30, leaveRef, true, flags);
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
        if (Clone* clone = RouteByTrigger(Find(raw, ignore), kBattleListTriggers); clone && !clone->OwnTrigger)   // own triggers: Hook_AttackEval
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
        // a raw discard cost (Cost_DiscardPickedHandCards under this card's id, section 90)
        if (Clone* pick = effect ? PayingClone(effect[0]) : nullptr; pick && pick->Verb == Clone::PickVerb::Discard)
            return pick->CostPickCount;
        return orig_DiscardCount(effect);
    }

    // How many Xyz Materials a detach cost (Cost 0x1401FF4D0, check 0x1400FBFF0) takes, asked by the effect card's id: the clone's own number
    // while its composed effect runs as the row card (Thunder End Dragon).
    int64_t(__fastcall* orig_DetachCount)(uint16_t*) = reinterpret_cast<int64_t(__fastcall*)(uint16_t*)>(0x1401F7AF0);

    int64_t __fastcall Hook_DetachCount(uint16_t* effect)
    {
        if (g_Active && g_Active->DetachCount > 0 && effect && MatchesFrom(g_Active, effect[0]))
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

    // Each part has its own overrides, even when two parts borrow the same row.
    // Rows are never freed (the engine may hold the pointer).
    std::map<std::tuple<const void*, uint16_t, const Clone*>, Row*> g_Wrapped;

    Row* WrappedRow(const Row* source, uint16_t customId, const Clone& clone)
    {
        auto key = std::make_tuple(static_cast<const void*>(source), customId, &clone);
        auto it = g_Wrapped.find(key);
        if (it != g_Wrapped.end())
            return it->second;

        Row* wrapped = new Row(*source);
        wrapped->Id = customId;

        // Once per turn. The low nibble of the row's first Extra word is a USE-LIMIT class (docs/EffectSystem.md section 20): after an effect is
        // activated the engine runs the class's registration (Check_CanActivate_Driver's sibling 0x1400E0E50 -> 0x1401F8770 for class 5 adds a
        // (1001, card id) marker for the turn) and before offering it, Timing_Check_Class5_9 refuses while that marker exists. Both read the row through
        // the normal lookup, so a wrapped row with class 5 IS "once per turn, per card name" (6, 7, 8: the other three keys; 1-4: per copy,
        // AssignLimitClasses). Limit classes the source happens to have (1-10) are cleared first so a clone never inherits a restriction the
        // script did not ask for.
        uint16_t& limit = wrapped->Extra[0];
        const uint16_t nibble = limit & 0xF;
        if (nibble >= 1 && nibble <= 10)
            limit &= ~static_cast<uint16_t>(0xF);
        if (clone.OncePerTurn || clone.OncePerCopy || clone.OncePerDuel)
            limit = (limit & ~static_cast<uint16_t>(0xF)) | clone.LimitClass;
        for (int i = 0; i < 5; ++i)
        {
            // An empty slot stays empty: the engine treats it as "none". A few slot functions are compared BY ADDRESS elsewhere in the engine (a row whose
            // slot 3 is Slot3_MonsterEffect_WithHooks is treated as a monster effect with hooks, sub_140100410 / sub_1400E0FE0 ...) and do not look at the
            // card id themselves, so they are handed over as they are; every other slot gets its impersonating wrapper.
            const uint64_t fn = reinterpret_cast<uint64_t>(source->Slot[i]);
            const bool identityTagged = i == 3 && (fn == 0x1401F94C0 || fn == 0x1401F9470 || fn == 0x1401F9630 || fn == 0x1401F9920 || fn == 0x1402007F0);
            // Composed effect: slots 0, 1, 2, 4 exist when the action card has them (SlotThunk runs them as the action card).
            const Row* action = clone.ActionFrom && i != 3 ? ActionRow(clone.ActionFrom) : nullptr;
            // Slot 2 also exists for a chain whose first step picks from a list: its "the list has a card" check lives there (SlotThunk<2>),
            // and a row without a condition (Hinotama's) is never asked - summon then burn was playable with no monster (in-duel 2026-10-21).
            if ((clone.CostFrom && (i == 2 || i == 3)) || (i == 2 && (!clone.Require.empty() || FirstStepIsOwn(clone))))
                wrapped->Slot[i] = kThunks[i];   // the composed cost (slot 3) and its payability / required checks (slot 2)
            // This handler asks for the donor's native sub-effects (e.g. Stratos's
            // search/destroy menu). A composed action already defines the effect;
            // retaining that menu can cancel it when no donor option is legal.
            else if (i == 3 && clone.ActionFrom && !clone.SlotOverride[i] && fn == 0x140201950)
                wrapped->Slot[i] = nullptr;
            // raw slots (section 88): only the engine functions the clone names; a donor row's other slots are not used (an empty slot 2 =
            // allowed, as the engine treats a row without a condition)
            else if (clone.RawSlots && (i != 3 || clone.RawRow))
                wrapped->Slot[i] = clone.SlotOverride[i] ? kThunks[i] : nullptr;
            else if (clone.ActionFrom && i != 3)
                wrapped->Slot[i] = clone.SlotOverride[i] || (action && action->Slot[i]) ? kThunks[i] : nullptr;
            else
                wrapped->Slot[i] = clone.SlotOverride[i] ? kThunks[i] : !source->Slot[i] ? nullptr : identityTagged ? source->Slot[i] : kThunks[i];
        }
        g_Wrapped[key] = wrapped;
        return wrapped;
    }

    // ---- the "select an effect" row (see g_Choices) -------------------------------------------------------------------------------
    using Driver_t = uint64_t(__fastcall*)(uint16_t*, uint64_t);
    const Driver_t Check_CanActivate_Driver = reinterpret_cast<Driver_t>(0x1400E0C30);
    using ShowPrompt_t = int16_t(__fastcall*)(uint16_t*, uint32_t, uint32_t);
    const ShowPrompt_t EffectChoice_ShowPrompt = reinterpret_cast<ShowPrompt_t>(0x14005F630);   // the game's list, labels id + (n << 14)
    using ChoiceCount_t = int64_t(__fastcall*)(int);
    ChoiceCount_t orig_ChoiceCount = reinterpret_cast<ChoiceCount_t>(0x1401FA6D0);   // EffectChoice_GetCount: 2 for any card it does not know
    constexpr uint32_t kSelectEffectText = 259;            // the prompt text Slot3_EffectChoice_SelectThenCost uses for most cards
    constexpr uintptr_t kPromptAnswer = 0x14349B442;       // DuelState field_3802: the option the player picked (0-based)

    int64_t __fastcall Hook_ChoiceCount(int id)
    {
        if (g_PromptCount && id == g_PromptId)
            return g_PromptCount;
        return orig_ChoiceCount(id);
    }

    // The prompt's option labels. EffectChoice_GetLabel (0x1401FA760, id, n) gives a DLG text index (a table of game cards' labels) or
    // id + (n << 14); the text builder (Duel_PromptText 0x14077AE30) turns an id >= 3000 into the n-th "●" line of that card's text. A custom card
    // has no such lines (blank CHOICE 1 / 2 in-duel 2026-10-14), so for our prompt the label is always card | n << 14 (a lent source id must not
    // bring its own table labels), and the builder answers it with the part's "label".
    using ChoiceLabel_t = int64_t(__fastcall*)(int, int);
    ChoiceLabel_t orig_ChoiceLabel = reinterpret_cast<ChoiceLabel_t>(0x1401FA760);
    int64_t __fastcall Hook_ChoiceLabel(int id, int n)
    {
        if (g_PromptCount && id == g_PromptId && n >= 0 && n < g_PromptCount && n < 4 && id <= 0x3FFF)
            return id | (n << 14);
        return orig_ChoiceLabel(id, n);
    }

    // MSVC std::wstring as the game lays it out (Release STL: 16-byte buffer / pointer, size, capacity); filled through the game's own assign.
    struct GameWString { union { wchar_t Buffer[8]; wchar_t* Pointer; }; size_t Size; size_t Capacity; };
    using PromptText_t = GameWString*(__fastcall*)(GameWString*, int);
    PromptText_t orig_PromptText = reinterpret_cast<PromptText_t>(0x14077AE30);
    const auto GameWStringAssign = reinterpret_cast<void(__fastcall*)(GameWString*, const wchar_t*, size_t)>(0x140751310);   // std::wstring::assign

    GameWString* __fastcall Hook_PromptText(GameWString* out, int id)
    {
        if (id >= 3000)
        {
            auto it = g_PromptLabels.find(static_cast<uint16_t>(id & 0x3FFF));
            const size_t n = static_cast<size_t>(id >> 14);
            if (it != g_PromptLabels.end() && n < it->second.size() && !it->second[n].empty())
            {
                const std::wstring& text = it->second[n];
                out->Size = 0;
                out->Capacity = 7;
                out->Buffer[0] = 0;
                GameWStringAssign(out, text.c_str(), text.size());
                return out;
            }
        }
        return orig_PromptText(out, id);
    }

    // Bit n = effect n of the lookup's candidates can be activated now: the full check (limit class, condition, targets) run with that effect chosen.
    uint32_t ComputeChoiceMask(Clone* root, uint16_t* effect, uint64_t link, const std::vector<Clone*>& pool)
    {
        const std::vector<Clone*> all = AllParts(*root);
        const auto saved = g_Probe;
        uint32_t mask = 0;
        for (size_t n = 0; n < pool.size() && n < 4; ++n)
        {
            g_Probe = { true, ChoiceKey(effect), static_cast<int>(std::find(all.begin(), all.end(), pool[n]) - all.begin()) };
            if (Check_CanActivate_Driver(effect, link) & 0xFF)
                mask |= 1u << n;
        }
        g_Probe = saved;
        return mask;
    }

    uint64_t __fastcall ChoiceSlot2(uint16_t* effect, uint64_t link, uint64_t, uint64_t)
    {
        bool ignore;
        Clone* root = Find(effect[0], ignore);
        if (!root)
            return 0;
        return ComputeChoiceMask(root, effect, link, Candidates(root, effect)) ? 2 : 0;
    }

    // The chosen effect's own slot 3 (its cost), or done (1) when it has none.
    uint64_t RunChosenSlot3(uint16_t* effect, uint64_t a2, uint64_t a3, uint64_t a4);

    // Slot 3 of the select row: pick the effect (the only usable one, or the player's answer to the prompt), then pay that effect's cost.
    // 0 = call again (the prompt is up), as the game's own chooser returns.
    uint64_t __fastcall ChoiceSlot3(uint16_t* effect, uint64_t a2, uint64_t a3, uint64_t a4)
    {
        bool ignore;
        Clone* root = Find(effect[0], ignore);
        if (!root)
            return 1;
        const auto key = ChoiceKey(effect);
        if (ChosenIndex(effect) >= 0)
            return RunChosenSlot3(effect, a2, a3, a4);

        const std::vector<Clone*> pool = Candidates(root, effect);
        const std::vector<Clone*> all = AllParts(*root);
        auto choose = [&](size_t n) {
            n = (std::min)(n, pool.size() - 1);
            g_Choices[key] = static_cast<int>(std::find(all.begin(), all.end(), pool[n]) - all.begin());
            // the record carries the choice too (see ChosenIndex), so a copy made for the chain resolves the chosen effect
            effect[20] = static_cast<uint16_t>(ChoiceTag(*root, pool[n]) | (effect[20] & 0xFF));
            Logger::WriteLog(std::format("Card {} activates its effect {} (source {}), instance {}", key.first, g_Choices[key] + 1, pool[n]->From, key.second), MODULE_NAME, 0);
        };
        if (g_ChoicePrompted.erase(key))
        {
            choose(*reinterpret_cast<const uint16_t*>(kPromptAnswer));
            return RunChosenSlot3(effect, a2, a3, a4);
        }
        // At most 4 options: the prompt's labels are u16 (YGO::Duel::ShowOptionPrompt 0x14005F340), card | n << 14 leaves n two bits.
        const uint32_t mask = ComputeChoiceMask(root, effect, a2, pool) & 0xF;
        if (std::popcount(mask) <= 1)
        {
            choose(mask ? static_cast<size_t>(std::countr_zero(mask)) : 0);
            return RunChosenSlot3(effect, a2, a3, a4);
        }
        g_PromptId = effect[0];
        g_PromptCount = static_cast<int>((std::min)(pool.size(), size_t{ 4 }));
        auto& labels = g_PromptLabels[static_cast<uint16_t>(effect[0] & 0x3FFF)];
        labels.clear();
        for (const Clone* part : pool)
            labels.push_back(part->Label);
        EffectChoice_ShowPrompt(effect, kSelectEffectText, mask);
        g_PromptCount = 0;
        g_ChoicePrompted.insert(key);
        return 0;
    }

    // One select row per custom card: the first candidate's row (table, event wiring) with no limit class of its own (each effect's is checked
    // in the mask), slot 1 empty (targets are checked per effect too), slots 2 and 3 the chooser's; resolving (slot 0, 4) is the chosen effect's.
    Row* ChoiceRow(const Row* first, uint16_t customId)
    {
        static std::map<std::pair<const void*, uint16_t>, Row*> rows;
        auto& row = rows[{ first, customId }];
        if (!row)
        {
            row = new Row(*first);
            row->Id = customId;
            row->Extra[0] &= ~static_cast<uint16_t>(0xF);
            row->Slot[0] = kThunks[0];
            row->Slot[1] = nullptr;
            row->Slot[2] = reinterpret_cast<void*>(&ChoiceSlot2);
            row->Slot[3] = reinterpret_cast<void*>(&ChoiceSlot3);
            row->Slot[4] = first->Slot[4] ? kThunks[4] : nullptr;
        }
        return row;
    }

    void* __fastcall Hook_GetEntry(uint16_t* effect);

    uint64_t RunChosenSlot3(uint16_t* effect, uint64_t a2, uint64_t a3, uint64_t a4)
    {
        const Row* row = static_cast<const Row*>(Hook_GetEntry(effect));   // the chosen effect's own row now
        if (!row || !row->Slot[3] || row->Slot[3] == reinterpret_cast<void*>(&ChoiceSlot3))
            return 1;
        return reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(row->Slot[3])(effect, a2, a3, a4);
    }

    void* __fastcall Hook_GetEntry(uint16_t* effect)
    {
        if (g_Clones.empty())
            return orig_GetEntry(effect);

        // A composed effect runs its action card's slots impersonating that card, but the record still names the ROW's table (word4): a
        // machine that looks its own row up again (Armoroid's banish-all 0x140157050 for its target slot, Check_TargetStillValid 0x1400DFEE0)
        // found no row for the action card in that table - the banish-all then used mask -1 and banished every card on the field, Spell/Traps
        // included (in-duel 2026-10-02). The action card's (and cost card's) own row answers.
        if (g_Active && effect && ((g_Active->ActionFrom && effect[0] == g_Active->ActionFrom) || (g_Active->CostFrom && effect[0] == g_Active->CostFrom)))
            if (void* own = orig_GetEntry(effect))
                return own;
            else
                return const_cast<Row*>(ActionRow(effect[0]));

        bool borrowedWithoutClone;
        Clone* root = Find(effect[0], borrowedWithoutClone);
        Clone* clone = RouteByRow(root, effect);
        const bool chosen = effect && ChosenIndex(effect) >= 0;   // before NativeChoiceScope hides the record's tag from the source's lookup
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

        if (!clone->Trigger.empty() && clone->Trigger != "ignition" && IsKnownTriggerEvent(effect[3]) && !TriggerFitsEvent(clone->Trigger, effect[3]))
            return nullptr;   // the source's effect for another event (see IsKnownTriggerEvent)

        Announce(*clone, effect[0]);
        LogLookup(effect);
        const uint16_t saved = effect[0];
        const Row* source = nullptr;
        std::optional<NativeChoiceScope> nativeChoice;   // held to the end of the lookup, as before
        if (UsesOwnRow(*clone))
        {
            // no game card behind the trigger (or behind the row at all, a raw row): the plugin's own row, only for the lookups it answers
            if (!OwnRowAnswers(*clone, effect))
                return nullptr;
            const unsigned short ownReal = BorrowedRealId(saved);
            source = OwnTriggerRow(ownReal ? ownReal : saved);
        }
        else
        {
            effect[0] = static_cast<uint16_t>(clone->From);
            nativeChoice.emplace(effect);
            source = static_cast<const Row*>(orig_GetEntry(effect));
            effect[0] = saved;
        }
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
        // Several effects answer this lookup and none is chosen yet: the select row, which offers the card once and asks which effect.
        if (root && !root->Parts.empty() && !chosen && Candidates(root, effect).size() > 1)
            return ChoiceRow(source, real ? real : saved);
        return WrappedRow(source, real ? real : saved, *clone);
    }

    // ---- Master Duel's duel engine (Stage 1, docs/Overnight-2026-10-30.md) ------------------------------------------------------------
    // Duels run in duel.dll, whose own Get_EffectTableEntryForCard (RVA 0x56C0C0) takes (record, int* row index within the card's run) and
    // sees ENGINE ids (Konami's, or 40000+ for a card Konami does not have; Core_EngineToDisplay gives the id the clones are keyed on).
    // What it answers so far: a part with no donor (raw row, section 88) whose slots are game functions with a duel.dll twin
    // (MasterDuelFunctionMap.h) and that needs no other hook - the plugin's own row with the twins. A donor part already plays under
    // duel.dll through Core's lend (as(...): the engine runs the donor's own rows and id-keyed rules). Anything that needs the plugin's slot
    // thunks or another game hook (draw count, filters, steps, own machines, composed cost / action, own triggers but ignition ...) gets no
    // row until those hooks have duel.dll twins: logged once per card, so the card does nothing rather than something broken.
    using MdGetEntry_t = void*(__fastcall*)(uint16_t*, int*);
    MdGetEntry_t orig_MdGetEntry = nullptr;
    uintptr_t g_DuelBase = 0;
    int(__cdecl* g_EngineToDisplay)(int) = nullptr;
    constexpr uint32_t kDuelTimeStamp = 0x6A41C989, kDuelImageSize = 0x1290000;   // build 80460, the one the twins were traced in
    constexpr uint32_t kDuelRva_GetEffectTableEntryForCard = 0x56C0C0;

    // A "require" check's duel.dll twin (absolute address), 0 when it has none (defined with the checks, CheckByName).
    uintptr_t MdCheckTwin(uint64_t check);

    // Why this part cannot run under duel.dll yet (nullptr = it can).
    const char* MdNotPortedWhy(const Clone& c)
    {
        if (!c.RawRow) return c.From ? "has a donor (plays through Core's lend instead)" : "not a raw row";
        if (c.OwnTrigger && c.Trigger != "ignition") return "own trigger (needs the event evaluator hooks)";
        if (c.HasFilter || c.HasDeckFilter || !c.OrRows.empty() || c.HasStats || c.HasNegate) return "answers a game hook";   // (draw / LP: Hook_MdGetDraw / Hook_MdLpChanges)
        if (c.CostPick || c.CostFrom || c.ActionFrom || c.DetachCount || !c.Pre.empty()) return "composed cost / action";
        for (uint64_t check : c.Require)
            if (!MdCheckTwin(check))
                return "a \"require\" check with no duel.dll twin";
        if (!c.Steps.empty() || c.Own || c.MoveScope || c.ResultKey) return "the plugin's own machine";
        if (c.HasSummonLock || c.UsesPerTurn > 1 || c.Quick) return "a rule answered by a game hook";
        for (uint64_t slot : c.SlotOverride)
            if (slot && !YGO::MasterDuelMap::ToDuelRva(slot))
                return "a slot function with no duel.dll twin";
        return nullptr;
    }

    // The part a duel.dll record (engine id) belongs to: Find / RouteByRow see the display id while they route. nullptr for a card with no
    // clone or a borrowed id without one.
    Clone* MdClone(uint16_t* effect)
    {
        if (!effect || g_Clones.empty())
            return nullptr;
        const uint16_t engineId = effect[0];
        const uint16_t display = static_cast<uint16_t>(g_EngineToDisplay ? g_EngineToDisplay(engineId) : engineId);
        bool borrowedWithoutClone;
        effect[0] = display;
        Clone* clone = RouteByRow(Find(display, borrowedWithoutClone), effect);
        effect[0] = engineId;
        return borrowedWithoutClone ? nullptr : clone;
    }

    // Slot 2 of an own row with "require" checks under duel.dll: each check's twin, then the row's own condition (as SlotThunk<2> does on
    // the game's engine). The record's id is the engine id throughout (raw rows run under their own id).
    uint64_t __fastcall MdSlot2_Requires(uint16_t* effect, uint64_t a2, uint64_t a3, uint64_t a4)
    {
        using Slot_t = uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t);
        const Clone* clone = MdClone(effect);
        if (!clone)
            return 0;
        const uint16_t engineId = effect[0];
        const uint16_t display = static_cast<uint16_t>(g_EngineToDisplay ? g_EngineToDisplay(engineId) : engineId);
        for (uint64_t check : clone->Require)
        {
            const uintptr_t fn = MdCheckTwin(check);
            const bool inDuel = fn >= g_DuelBase && fn < g_DuelBase + kDuelImageSize;   // else the plugin's own (mirrored-state) check
            effect[0] = inDuel ? engineId : display;
            const uint64_t ok = reinterpret_cast<Slot_t>(fn)(effect, a2, a3, a4);
            effect[0] = engineId;
            if (!(ok & 0xFF))
                return 0;
        }
        const uint32_t rva = clone->SlotOverride[2] ? YGO::MasterDuelMap::ToDuelRva(clone->SlotOverride[2]) : 0;
        return rva ? reinterpret_cast<Slot_t>(g_DuelBase + rva)(effect, a2, a3, a4) : 1;
    }

    const Row* MdOwnRow(const Clone& c, uint16_t engineId)
    {
        static std::map<std::pair<const Clone*, uint16_t>, Row*> rows;   // never freed: the engine may hold the pointer
        Row*& row = rows[{ &c, engineId }];
        if (!row)
        {
            row = new Row{};
            row->Id = engineId;
            if (c.OncePerTurn || c.OncePerCopy || c.OncePerDuel)
                row->Extra[0] = c.LimitClass;   // the use-limit classes 1-10 are the game's (duel.dll adds 15)
            for (int i = 0; i < 5; ++i)
                if (c.SlotOverride[i])
                    row->Slot[i] = reinterpret_cast<void*>(g_DuelBase + YGO::MasterDuelMap::ToDuelRva(c.SlotOverride[i]));
            if (!c.Require.empty())
                row->Slot[2] = reinterpret_cast<void*>(&MdSlot2_Requires);
        }
        return row;
    }

    void* __fastcall Hook_MdGetEntry(uint16_t* effect, int* index)
    {
        Clone* clone = MdClone(effect);
        if (!clone)
            return orig_MdGetEntry(effect, index);
        if (const char* why = MdNotPortedWhy(*clone))
        {
            static std::set<std::pair<uint16_t, const Clone*>> logged;
            if (logged.emplace(effect[0], clone).second)
                Logger::WriteLog(std::format("duel.dll: card with engine id {}: this effect does not run under Master Duel's engine yet: {}", effect[0], why), MODULE_NAME, 1);
            return clone->From ? orig_MdGetEntry(effect, index) : nullptr;   // a lent donor's own row is the engine's
        }
        if (!OwnRowAnswers(*clone, effect))
            return nullptr;
        if (index)
            *index = 0;
        return const_cast<Row*>(MdOwnRow(*clone, effect[0]));
    }

    // duel.dll's Get_NumberOfCardsToDraw (RVA 0x2AB390, same signature as the game's 0x14015EA90): a raw "draw" row's count is its
    // "draw" (the duel.dll draw machine and its condition, the twins of 0x14015F9C0 / 0x1400FAC40, ask this for the record's id).
    using MdGetDraw_t = int64_t(__fastcall*)(uint16_t*);
    MdGetDraw_t orig_MdGetDraw = nullptr;
    constexpr uint32_t kDuelRva_GetNumberOfCardsToDraw = 0x2AB390;

    int64_t __fastcall Hook_MdGetDraw(uint16_t* effect)
    {
        if (const Clone* clone = MdClone(effect); clone && clone->RawRow && clone->Draw >= 0)
            return clone->Draw;
        return orig_MdGetDraw(effect);
    }

    // duel.dll's Get_LifePointChanges (RVA 0x2A4020, as the game's 0x14015AC90: record, int[2] by absolute player, + gain / - damage), asked
    // by Slot0_AlterLifePoints 0x1802AAD70 -> Effect_AlterLifePoints_Apply 0x18014E7E0: a raw LP row's "lp" amounts.
    using MdLpChanges_t = int16_t(__fastcall*)(uint16_t*, int32_t*);
    MdLpChanges_t orig_MdLpChanges = nullptr;
    constexpr uint32_t kDuelRva_GetLifePointChanges = 0x2A4020;

    int16_t __fastcall Hook_MdLpChanges(uint16_t* effect, int32_t* out)
    {
        const int16_t result = orig_MdLpChanges(effect, out);
        if (const Clone* clone = MdClone(effect); clone && clone->RawRow && clone->HasLp && out)
        {
            const int owner = effect[1] & 1;   // the controller
            out[owner] = clone->LpOwn;
            out[1 - owner] = clone->LpOpp;
        }
        return result;
    }

    bool AttachMasterDuel()
    {
        void* proc = Core::Functions().EngineProc ? Core::Functions().EngineProc("DLL_DuelSysAct") : nullptr;
        HMODULE duel = nullptr;
        if (!proc || !GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, static_cast<LPCSTR>(proc), &duel))
            return false;
        const auto base = reinterpret_cast<uintptr_t>(duel);
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + reinterpret_cast<const IMAGE_DOS_HEADER*>(base)->e_lfanew);
        if (nt->FileHeader.TimeDateStamp != kDuelTimeStamp || nt->OptionalHeader.SizeOfImage != kDuelImageSize)
        {
            Logger::WriteLog(std::format("duel.dll is not build 80460 (stamp {:08X}): custom effect rows are off in its duels", nt->FileHeader.TimeDateStamp), MODULE_NAME, 2);
            return false;
        }
        g_DuelBase = base;
        g_EngineToDisplay = Core::Functions().EngineToDisplay;
        orig_MdGetEntry = reinterpret_cast<MdGetEntry_t>(base + kDuelRva_GetEffectTableEntryForCard);
        DetourAttach(&(PVOID&)orig_MdGetEntry, Hook_MdGetEntry);
        orig_MdGetDraw = reinterpret_cast<MdGetDraw_t>(base + kDuelRva_GetNumberOfCardsToDraw);
        DetourAttach(&(PVOID&)orig_MdGetDraw, Hook_MdGetDraw);
        orig_MdLpChanges = reinterpret_cast<MdLpChanges_t>(base + kDuelRva_GetLifePointChanges);
        DetourAttach(&(PVOID&)orig_MdLpChanges, Hook_MdLpChanges);
        size_t ported = 0, total = 0;
        for (auto& [id, clone] : g_Clones)
            for (Clone* part : AllParts(clone))
            {
                ++total;
                ported += MdNotPortedWhy(*part) == nullptr;
            }
        Logger::WriteLog(std::format("duel.dll: effect row lookup hooked; {} of {} custom effects run on it as they are", ported, total), MODULE_NAME, 0);
        return true;
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
        // 0x4 is "face-up" (the zone's byte +3, not "occupied": the card is checked separately), 0x10 the Monster Zones. Vanilla "destroy all
        // <race>" rows carry 0x4 (a face-down card shows no race); "target 1 monster" rows do not (Green Turtle Summoner 0x1410). Without a
        // property test the clone's filter no longer asks for face-up: in-duel 2026-10-02 Adamancipator Crystal's FLIP found no target,
        // probably the opponent's Set monster. "faceUp": true asks for it.
        uint32_t flags = 0x1010;
        // "target": true = the player picks ONE card ("Target 1 X; destroy it" rows such as Shield Crush / Remove Trap): adds the targetable
        // check (0x400); "kind": "spelltrap" uses the Spell & Trap zones (Mystical Space Typhoon's flags 0x41440) instead of the monsters'.
        if (f.value("target", false))
            flags = f.value("kind", std::string("monster")) == "spelltrap" ? 0x41440 : 0x1410;
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
        if ((chosen && (flags & 0x70) == 0x10) || f.value("faceUp", false))
            flags |= 0x4;
        // battle position and change tests of Filter_CardAtZoneMatchesFlags (0x14017B600, section 92): 0x80 the zone's position byte (+2) is 0
        // = Attack Position, 0x100 it is set = Defense Position; 0x80000 its battle position can be changed (sub_14001FD00, not a Link
        // Monster: Stop Defense's row); 0x20000 its control can switch (sub_140021100)
        const std::string position = f.value("position", std::string());
        if (position == "attack") flags |= 0x80;
        else if (position == "defense") flags |= 0x100;
        else if (!position.empty()) { error = "position must be attack or defense"; return false; }
        if (f.value("canChangePosition", false)) flags |= 0x80000;
        if (f.value("canChangeControl", false)) flags |= 0x20000;
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

    // "deck.scan": "extra". The game's Extra Deck row scans (0x14052EF40 and siblings) all pass behaviour bits to ScanGeneric_ExtraDeck
    // (0x14052C090: zone 14, list flags 0x1C) - 16 = "can be Special Summoned", ... - so none lists "any matching card". This calls it with 0:
    // only the row's name test (sub_14052B440) and flag tests (monster / race / attribute / Level / ATK), as the row wrappers pass them
    // (player, row id, row param as a signed word, no callback, 0, the flags dword at +16, mode).
    int64_t __fastcall Scan_ExtraDeck_Any(const uint8_t* row, uint32_t player)
    {
        using ScanGeneric_t = int64_t(__fastcall*)(uint32_t, uint32_t, int, void*, int64_t, uint32_t, int16_t);
        const auto ScanGeneric_ExtraDeck = reinterpret_cast<ScanGeneric_t>(0x14052C090);
        return ScanGeneric_ExtraDeck(player, *reinterpret_cast<const uint16_t*>(row), *reinterpret_cast<const int16_t*>(row + 2), nullptr, 0,
            *reinterpret_cast<const uint32_t*>(row + 16), 0);
    }

    Clone* DeckFilterClone(uint16_t id)
    {
        // a raw pick cost's list / hand test (section 90): inside its machine, and between its steps while its picker is up
        if (Clone* pick = PayingClone(id); pick && pick->HasDeckFilter)
            return pick;
        if (g_Active)
            return (g_Active->HasDeckFilter && (MatchesFrom(g_Active, id) || (g_Active->ActionFrom && id == g_Active->ActionFrom))) ? g_Active : nullptr;
        // Between the steps of a clone's resolution: a selection screen's per-card test asks by the id the slot showed (Magnet Circle LV2's
        // hand pick sub_14015FA70 for Beetrooper Scale Bomber, which plays under the lent id 11127 but resolves as 6572; in-duel 2026-10-04).
        if (g_Resolving && g_Resolving->HasDeckFilter && (MatchesFrom(g_Resolving, id) || (g_Resolving->ActionFrom && id == g_Resolving->ActionFrom)))
            return g_Resolving;
        // Outside a slot, a raw clone (section 88) is asked by the card's own engine id: its raw part with a list answers.
        {
            bool ignore;
            if (Clone* clone = Find(id, ignore))
                for (Clone* part : AllParts(*clone))
                {
                    if (part->RawSlots && part->HasDeckFilter)
                        return part;
                    for (Clone& step : part->Pre)
                        if (step.RawSlots && step.HasDeckFilter)
                            return &step;
                }
        }
        // Outside a slot: a source id lent to a clone for this duel (no one else plays it) answers with the clone's filter - the game asks
        // before any slot runs (the leave-field offer counts revive candidates for Superheavy Samurai Drum's row; Flint Cragger was never offered).
        if (BorrowedRealId(id) == 0)
            return nullptr;
        bool ignore;
        if (Clone* clone = Find(id, ignore))
            for (Clone* part : AllParts(*clone))
                if (part->HasDeckFilter && part->From == id)
                    return part;
        return nullptr;
    }

    // The deck filter table (YGO_Effects_DeckFilterTable 0x140AD2A80, 5924 rows of 24 bytes) is not only read through Collect / Count: about
    // 58 callers take a row index from Get_DeckFilterIndexForEffect (0x1400C01B0) and run the row's scan function themselves - Magnet Circle
    // LV2's "a matching monster is in the hand" check sub_1400C0710 does, so Barian's Hope was only playable with a Level 2 Machine in hand
    // (in-duel 2026-10-02). So the row the index points at is overwritten with the clone's own row (its id kept) while the clone owns that
    // id - inside its slots, or for the duel when the id is lent to it - and the vanilla bytes are put back at the next lookup that is not
    // the clone's (the table is made writable at setup, only when some clone has a "deck" filter).
    void PrepareDeckRow(Clone& clone);
    constexpr uintptr_t kDeckFilterTable = 0x140AD2A80;
    constexpr int kDeckFilterRows = 5924;
    using DeckFilterIndex_t = int64_t(__fastcall*)(int, int, uint32_t);
    DeckFilterIndex_t orig_DeckFilterIndex = reinterpret_cast<DeckFilterIndex_t>(0x1400C01B0);
    std::unordered_map<int64_t, std::array<uint8_t, 24>> g_PatchedDeckRows;   // index -> the vanilla row

    // A raw clone (section 88) has no donor row to patch, but the game's list code needs a row INSIDE the table (callers index it, and the
    // scanners do not work on a copy) whose id field is the effect's own card: Scan_Deck_Own (0x14052F2A0) passes row+0 to ScanGeneric_Deck as
    // the card for "same name as this card" (param -1) / "except this card" (flag 0x10, sub_14052B440) and Get_SummonSourceKind (monster or
    // Spell summoner). Row 5919 belongs to ghost id 15234 (no game card uses it): its rows before are 15232, after it the sentinels 65532+,
    // so any custom id (15300 - 16383) written there keeps the table sorted for the binary search. Put back before any other lookup.
    constexpr int64_t kRawDeckRow = 5919;
    bool g_RawDeckRowPatched = false;
    std::array<uint8_t, 24> g_RawDeckRowVanilla{};

    int64_t PatchRawDeckRow(Clone& clone, uint16_t engineId)
    {
        auto* row = reinterpret_cast<uint8_t*>(kDeckFilterTable + static_cast<uintptr_t>(kRawDeckRow) * 24);
        if (!g_RawDeckRowPatched)
        {
            memcpy(g_RawDeckRowVanilla.data(), row, 24);
            g_RawDeckRowPatched = true;
        }
        PrepareDeckRow(clone);
        memcpy(row + 2, clone.DeckRow + 2, 22);
        *reinterpret_cast<uint16_t*>(row) = engineId;
        return kRawDeckRow;
    }

    void RestoreRawDeckRow()
    {
        if (!g_RawDeckRowPatched)
            return;
        memcpy(reinterpret_cast<uint8_t*>(kDeckFilterTable + static_cast<uintptr_t>(kRawDeckRow) * 24), g_RawDeckRowVanilla.data(), 24);
        g_RawDeckRowPatched = false;
    }

    // A clone's card asked by its OWN id outside its slots: a selection screen's per-card test runs after the slot returned, when the effect
    // record holds the custom id again - Magnet Circle LV2's "pick a monster from the hand" callback sub_14015FA70 -> sub_1400C0320 asks
    // with the record's id, and a custom id has no row, so no hand card was selectable (Barian's Hope, in-duel 2026-10-04). The row of the
    // card whose machine runs (ActionFrom for a composed effect, else From) is used, with the clone's filter.
    Clone* OwnIdDeckFilterClone(uint16_t id)
    {
        if (g_Active)
            return nullptr;
        bool ignore;
        Clone* clone = Find(id, ignore);
        if (!clone)
            return nullptr;
        for (Clone* part : AllParts(*clone))
            if (part->HasDeckFilter)
                return part;
        return nullptr;
    }

    int64_t __fastcall Hook_DeckFilterIndex(int player, int id, uint32_t k)
    {
        // a raw pick cost: the scratch row with its filter, whatever k is (the hand picker passes the hand index there, section 90)
        if (Clone* pick = id > 0 && id <= 0xFFFF ? PayingClone(static_cast<uint16_t>(id)) : nullptr; pick && pick->HasDeckFilter)
            return PatchRawDeckRow(*pick, static_cast<uint16_t>(id));
        Clone* own =id > 0 && id <= 0xFFFF ? OwnIdDeckFilterClone(static_cast<uint16_t>(id)) : nullptr;
        // a raw clone's list: the scratch row with this card's id (PatchRawDeckRow); anyone else first gets the scratch row's vanilla bytes back
        {
            Clone* raw = own && own->RawSlots ? own : nullptr;
            if (!raw && !own && id > 0 && id <= 0xFFFF)
                if (Clone* active = DeckFilterClone(static_cast<uint16_t>(id)); active && active->RawSlots)
                    raw = active;
            if (raw && k == 0)
                return PatchRawDeckRow(*raw, static_cast<uint16_t>(id));
            RestoreRawDeckRow();
        }
        const int64_t index = own ? orig_DeckFilterIndex(player, own->ActionFrom ? own->ActionFrom : own->From, k) : orig_DeckFilterIndex(player, id, k);
        if (index < 0 || index >= kDeckFilterRows)
            return index;
        auto* row = reinterpret_cast<uint8_t*>(kDeckFilterTable + static_cast<uintptr_t>(index) * 24);
        Clone* clone = own ? own : id > 0 && id <= 0xFFFF ? DeckFilterClone(static_cast<uint16_t>(id)) : nullptr;
        auto patched = g_PatchedDeckRows.find(index);
        if (clone)
        {
            if (patched == g_PatchedDeckRows.end())
            {
                std::array<uint8_t, 24> vanilla;
                memcpy(vanilla.data(), row, 24);
                g_PatchedDeckRows.emplace(index, vanilla);
            }
            PrepareDeckRow(*clone);
            memcpy(row + 2, clone->DeckRow + 2, 22);   // everything but the id the table is sorted by
            if (g_Trace)
                Logger::WriteLog(std::format("Deck filter row {} asked by id {}{}: answered with the filter of the clone of {}", index, id,
                    g_Active ? " in a slot" : " outside a slot", clone->From), MODULE_NAME, 0);
        }
        else if (patched != g_PatchedDeckRows.end())
        {
            memcpy(row, patched->second.data(), 24);
            g_PatchedDeckRows.erase(patched);
        }
        return index;
    }

    // The except-self name test needs the id the card plays under in THIS duel (it may be lent its source's id, or a scratch id).
    void ExceptSelfRow(uint8_t* row, bool exceptSelf, uint16_t owner)
    {
        if (exceptSelf && owner)
            *reinterpret_cast<int16_t*>(row + 2) = static_cast<int16_t>(-static_cast<int>(EngineIdFor(owner)));
    }

    void PrepareDeckRow(Clone& clone)
    {
        ExceptSelfRow(clone.DeckRow, clone.ExceptSelf, clone.Owner);
    }

    // Drops the matches named like the clone's card from [from, count) of the game's match list.
    void DropOwnName(const Clone& clone, int16_t from)
    {
        auto& count = *reinterpret_cast<int16_t*>(kMatchHeader + 2);
        auto* list = reinterpret_cast<uint32_t*>(kMatchList);
        auto* flags = reinterpret_cast<uint16_t*>(kMatchFlags);
        const auto IsSameName = reinterpret_cast<int64_t(__fastcall*)(int, int)>(0x1407EBA00);   // j_Card_IsSameName
        const int self = EngineIdFor(clone.Owner);
        int16_t kept = from;
        for (int16_t i = from; i < count && i < 300; ++i)
        {
            const int id = static_cast<int>(list[i] & 0x3FFF);
            if (id == self || IsSameName(id, self))
                continue;
            list[kept] = list[i];
            flags[kept] = flags[i];
            ++kept;
        }
        count = kept;
    }

    // The hand list of an "own" summon step, built by the plugin. The game's hand scan (0x14052EE40 -> 0x14052C8D0) adds "can this be Special
    // Summoned by the SOURCE card's effect" checks keyed by the row's card id: under Unexpected Dai's id no hand monster passed, under Magnet
    // Circle LV2's a Level 4 Normal Monster did not (in-duel 2026-10-21). Here only the card test is the game's (Scan_CardMatchesRowFlags
    // 0x14052B540: type, race, attribute, Level from the hand with le/ge, ATK) plus the row's name test; whether the summon is legal is left to
    // the engine's summon queue. Entries as the game writes them (sub_14052B310): the packed hand card, flags = player | zone 13 << 1.
    // Appends the matches of `row` at `count` (write = false: counts only, the game's list is not touched).
    int OwnHandRows(const Clone& clone, const uint8_t* row, bool exceptSelfAfter, int player, int16_t& count, bool write)
    {
        using Matches_t = int64_t(__fastcall*)(int64_t, uint32_t, int, int, int, uint32_t*, uint32_t);
        const auto CardMatchesRowFlags = reinterpret_cast<Matches_t>(0x14052B540);
        const auto IsSameName = reinterpret_cast<int64_t(__fastcall*)(int, int)>(0x1407EBA00);          // j_Card_IsSameName
        const auto InArchetype = reinterpret_cast<int64_t(__fastcall*)(int, int)>(0x1407EB9E0);         // Is_CardInNamedArchetype
        const uint32_t rowFlags = *reinterpret_cast<const uint32_t*>(row + 16);
        const int nameTest = *reinterpret_cast<const int16_t*>(row + 2);
        const int rowId = *reinterpret_cast<const uint16_t*>(row);
        auto* list = reinterpret_cast<uint32_t*>(kMatchList);
        auto* flags = reinterpret_cast<uint16_t*>(kMatchFlags);
        const int16_t before = count;
        const uintptr_t side = 0x143497C40 + static_cast<uintptr_t>(player & 1) * 0xD94;
        const uint32_t inHand = *reinterpret_cast<const uint32_t*>(side + 0x0C);
        for (uint32_t i = 0; i < inHand && i < 120 && count < 300; ++i)
        {
            uint32_t* card = reinterpret_cast<uint32_t*>(side + 0x19C + 4 * i);
            const int cardId = static_cast<int>(*card & 0x3FFF);
            if (!CardMatchesRowFlags(0, static_cast<uint32_t>(player), 13, static_cast<int>(i), rowId, card, rowFlags))
                continue;
            if ((nameTest >= 3000 && !IsSameName(cardId, nameTest)) || (nameTest > 0 && nameTest < 3000 && !InArchetype(cardId, nameTest))
                || (nameTest <= -3000 && IsSameName(cardId, -nameTest)) || (nameTest < -1 && nameTest > -3000 && InArchetype(cardId, -nameTest)))
                continue;
            if (exceptSelfAfter && clone.Owner && (cardId == EngineIdFor(clone.Owner) || IsSameName(cardId, EngineIdFor(clone.Owner))))
                continue;
            if (write)
            {
                list[count] = *card;
                flags[count] = static_cast<uint16_t>((player & 1) | (13 << 1));
            }
            ++count;
        }
        if (g_Trace)
            Logger::WriteLog(std::format("Own hand list (owner {}, {}): {} of {} hand card(s), row flags {:X} name test {}", clone.Owner,
                write ? "list" : "count", count - before, inHand, rowFlags, nameTest), MODULE_NAME, 0);
        return count - before;
    }

    // An own "add to the hand" step may not take cards from the Deck now: the game's search machine (Slot0_AddFromDeckToHand_StateMachine
    // 0x14016D5A0, step 128) and its condition (Slot2_Condition_ListHasMatch 0x1400FB620) ask Can_PlayerAddFromDeckToHand (0x14005CCD0: Thunder
    // King Rai-Oh, Deck Lockdown, Mistake, Great Magnus, Amorphage Sloth, Thunder Dragon Colossus, markers 9279 / 11849 / 13335 / 13547) first.
    // A row of another zone is not affected (Rai-Oh stops only the Deck).
    bool DeckRowClosed(const Clone& clone, const uint8_t* row, int player)
    {
        const auto CanAddFromDeck = reinterpret_cast<int64_t(__fastcall*)(int)>(0x14005CCD0);
        return clone.Own == 1 && *reinterpret_cast<const uint64_t*>(row + 8) == kDeckScanFn && !(CanAddFromDeck(player & 1) & 0xFF);
    }

    // Runs the clone's row and every "or" row; the matches of all of them land in the game's match list (kMatchList / kMatchFlags, count at
    // kMatchHeader + 2), each card once. Returns how many it added. A hand row of an own step is listed by OwnHandRows, not the game's hand scan.
    int RunDeckScan(Clone& clone, int player)
    {
        using Scan_t = int64_t(__fastcall*)(const void*, uint32_t);
        // The scan runs on the source card's own slot of the game's filter table, with our row copied in: the scanners do not work on a row
        // outside the table (Barian's Hope's hand pick counted 0 for every card with a copied row, in-duel 2026-10-14). The slot is put back
        // exactly as it was (vanilla, or our row when Hook_DeckFilterIndex has it patched).
        // a raw clone (section 88): the scratch row, carrying this card's own id
        const int64_t index = clone.RawSlots ? PatchRawDeckRow(clone, clone.EngineId)
                                             : (RestoreRawDeckRow(), orig_DeckFilterIndex(player, clone.ActionFrom ? clone.ActionFrom : clone.From, 0));
        uint8_t* slot = index >= 0 && index < kDeckFilterRows ? reinterpret_cast<uint8_t*>(kDeckFilterTable + static_cast<uintptr_t>(index) * 24) : nullptr;
        std::array<uint8_t, 24> kept{};
        if (slot)
            memcpy(kept.data(), slot, 24);
        auto scan = [player, slot](const uint8_t* row) {
            const uint8_t* run = row;
            if (slot)
            {
                memcpy(slot + 2, row + 2, 22);   // everything but the id the table is sorted by
                run = slot;
            }
            reinterpret_cast<Scan_t>(*reinterpret_cast<const uint64_t*>(run + 8))(run, static_cast<uint32_t>(player));
        };
        struct Restore { uint8_t* Slot; const std::array<uint8_t, 24>& Bytes; ~Restore() { if (Slot) memcpy(Slot, Bytes.data(), 24); } } restore{ slot, kept };
        auto& count = *reinterpret_cast<int16_t*>(kMatchHeader + 2);
        auto* list = reinterpret_cast<uint32_t*>(kMatchList);
        auto* flags = reinterpret_cast<uint16_t*>(kMatchFlags);
        const auto IsSameName = reinterpret_cast<int64_t(__fastcall*)(int, int)>(0x1407EBA00);   // j_Card_IsSameName
        // drop the matches named like this card from [from, count) (the "except this card" of a row whose name test is taken)
        auto exceptSelf = [&](int16_t from) {
            const int self = EngineIdFor(clone.Owner);
            int16_t kept = from;
            for (int16_t i = from; i < count && i < 300; ++i)
            {
                const int id = static_cast<int>(list[i] & 0x3FFF);
                if (id == self || IsSameName(id, self))
                    continue;
                list[kept] = list[i];
                flags[kept] = flags[i];
                ++kept;
            }
            count = kept;
        };
        // a hand row of an own step: the plugin's own hand list (see OwnHandRows), not the game's id-keyed hand scan
        auto scanRow = [&](const uint8_t* row, bool handList) {
            if (DeckRowClosed(clone, row, player))
                return;   // Thunder King Rai-Oh and the like: the Deck half of "from your Deck or GY" lists nothing
            if (clone.Own && handList)
                OwnHandRows(clone, row, false, player, count, true);
            else
                scan(row);
        };
        const int16_t before = count;
        PrepareDeckRow(clone);
        scanRow(clone.DeckRow, clone.HandList);
        if (clone.ExceptSelfAfter && clone.Owner)
            exceptSelf(before);
        for (Clone::AltRow& alt : clone.OrRows)
        {
            ExceptSelfRow(alt.Row, alt.ExceptSelf, clone.Owner);
            const int16_t start = count;
            scanRow(alt.Row, alt.HandList);
            if (alt.ExceptSelfAfter && clone.Owner)
                exceptSelf(start);
        }
        if (!clone.OrRows.empty())
        {
            int16_t kept = before;
            for (int16_t i = before; i < count && i < 300; ++i)
            {
                if (std::find(list + before, list + kept, list[i]) != list + kept)
                    continue;   // the same card matched an earlier alternative
                list[kept] = list[i];
                flags[kept] = flags[i];
                ++kept;
            }
            count = kept;
        }
        return count - before;
    }

    // The whole list of an own summon step from the hand alone (OwnHandRows on the clone's row). write = false (a count, Hook_Count): nothing
    // in the game's list changes.
    int OwnHandList(Clone& clone, int player, bool write = true)
    {
        PrepareDeckRow(clone);
        int16_t counted = 0;
        auto& count = write ? *reinterpret_cast<int16_t*>(kMatchHeader + 2) : counted;
        if (write)
            *reinterpret_cast<uint32_t*>(kMatchHeader) = 0;
        return OwnHandRows(clone, clone.DeckRow, clone.ExceptSelfAfter, player, count, write);
    }

    int64_t __fastcall Hook_Collect(int player, uint16_t id, uint32_t param)
    {
        Clone* clone = DeckFilterClone(id);
        if (!clone)
            return orig_Collect(player, id, param);
        if (clone->Own && clone->HandList && clone->OrRows.empty())
            return OwnHandList(*clone, player);   // with "or" rows (more zones), RunDeckScan below lists the hand row the same way
        if (clone->FieldPick)
            return OwnFieldCount(*clone, player & 1, g_SlotInstance);   // no list: the field prompt picks (Slot0_OwnFieldDestroy)
        if (clone->OrRows.empty() && DeckRowClosed(*clone, clone->DeckRow, player))
        {
            *reinterpret_cast<uint32_t*>(kMatchHeader) = 0;
            return 0;
        }
        if (clone->OrRows.empty())
        {
            // the game's own collect on the source's table row, which Hook_DeckFilterIndex answers with ours
            orig_Collect(player, id, param);
            if (clone->ExceptSelfAfter && clone->Owner)
                DropOwnName(*clone, 0);
            const int16_t found = *reinterpret_cast<int16_t*>(kMatchHeader + 2);
            if (g_Trace)
                Logger::WriteLog(std::format("Collect for id {} (clone of {}, owner {}, the game's scan): {} candidate(s)", id, clone->From, clone->Owner, found), MODULE_NAME, 0);
            return found;
        }
        *reinterpret_cast<uint32_t*>(kMatchHeader) = 0;
        memset(reinterpret_cast<void*>(kMatchList), 0, 1200);
        memset(reinterpret_cast<void*>(kMatchFlags), 0, 600);
        RunDeckScan(*clone, player);
        const int16_t found = *reinterpret_cast<int16_t*>(kMatchHeader + 2);
        if (g_Trace)
            Logger::WriteLog(std::format("Collect for id {} (clone of {}, owner {}): {} candidate(s), row flags {:X} param {}", id, clone->From, clone->Owner,
                found, *reinterpret_cast<const uint32_t*>(clone->DeckRow + 16), *reinterpret_cast<const int16_t*>(clone->DeckRow + 2)), MODULE_NAME, 0);
        return found;
    }

    int64_t __fastcall Hook_Count(int player, uint16_t id, uint32_t param)
    {
        Clone* clone = DeckFilterClone(id);
        if (!clone)
            return orig_Count(player, id, param);
        if (clone->Own && clone->HandList && clone->OrRows.empty())
            return OwnHandList(*clone, player, false);
        if (clone->FieldPick)
            return OwnFieldCount(*clone, player & 1, g_SlotInstance);
        if (clone->OrRows.empty() && DeckRowClosed(*clone, clone->DeckRow, player))
            return 0;
        if (clone->OrRows.empty())
        {
            // the game's own count: its parameter (a pick screen asks per card) keeps its meaning; the row is ours via Hook_DeckFilterIndex
            const int64_t n = orig_Count(player, id, param);
            if (g_Trace)
                Logger::WriteLog(std::format("Count for id {} param {:X} (clone of {}, the game's scan): {} match(es)", id, param, clone->From, n), MODULE_NAME, 0);
            return n;
        }
        const int16_t before = *reinterpret_cast<int16_t*>(kMatchHeader + 2);
        const int added = RunDeckScan(*clone, player);
        *reinterpret_cast<int16_t*>(kMatchHeader + 2) = before;
        if (g_Trace)
            Logger::WriteLog(std::format("Count for id {} param {:X} (clone of {}, {}): {} match(es)", id, param, clone->From,
                g_Active ? "in a slot" : "outside a slot", added), MODULE_NAME, 0);
        return added;
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
        c.HandList = scan == "handSummon";
        if (scan == "grave") scanFn = 0x14052F4D0;                  // Scan_Grave_Own
        else if (scan == "graveSummon") scanFn = 0x14052EC80;       // Scan_Grave_Own_SummonableOnly (revive)
        else if (scan == "opponentGrave") scanFn = 0x14052EE00;     // Scan_Grave_Opponent
        else if (scan == "bothGravesSummon") scanFn = 0x140531D70;  // Scan_Grave_Both_SummonableOnly (Monster Reborn)
        // 2026-10-01, the wrappers read in IDA (docs/EffectSystem.md section 33): zone + behaviour flag passed to the generic zone scanner.
        else if (scan == "deckSummon") scanFn = 0x14052EE80;        // Deck, flag 4: monsters that can be Special Summoned from the Deck
        else if (scan == "handSummon") scanFn = 0x14052EE40;        // hand, flag 2: monsters that can be Special Summoned from the hand
        else if (scan == "hand") scanFn = 0x14052F490;              // hand, flag 1: any card in the hand
        // 2026-10-26, read in IDA (section 90): ONE hand card, the index in the scan's third argument - what the hand-pick costs' picker
        // (Pick_HandFilter_ByDeckRow -> Deck_CountMatchingCards(player, id, handIndex)) asks per card
        else if (scan == "handAt") scanFn = 0x14052F350;            // Scan_HandCardAt_RowFlags
        else if (scan == "banished") scanFn = 0x140532BF0;          // banished cards, flag 0x40
        // 2026-10-25, read in IDA: the game's own "Special Summon from" list scans (both fill the list the Special Summon queue
        // Chain_AddEffectLink 0x1400F2450 takes; it reads the card's zone from its instance, so the same own summon step serves every zone).
        // Behaviour flags of the banished scanner 0x14052D410: 0x40 face-up only, 0x3C run Check_CanSpecialSummonByEffect from zone 17
        // (0x4 = to your own field, mode 1). The Extra Deck scanner 0x14052C090: 0x10 the same check from zone 14 (mode 1).
        else if (scan == "banishedSummon") scanFn = 0x1405344A0;    // banished, flag 0x44 (Return from the Different Dimension, Dimension Distortion)
        else if (scan == "extraSummon") scanFn = 0x14052EF40;       // Extra Deck, flag 0x10 (Starlight Road, Shooting Quasar Dragon)
        else if (scan == "extra") scanFn = reinterpret_cast<uint64_t>(&Scan_ExtraDeck_Any);   // Extra Deck, the row's own tests only
        // 2026-10-25, read in IDA: Polymerization's list row (deck filter table 0x140AD2A80 row 91: scan 0x140531D30, flags 0x802) = the Fusion
        // Monsters in the Extra Deck that can be Fusion Summoned with the available materials; Frightfur Fusion is the same row with param 209
        // (the Frightfur archetype). Slot2_Fusion_CanSummon 0x14000A650 and the fusion machine 0x14000A710 count / collect through it, so an
        // archetype / name / stat filter on this scan is "Fusion Summon 1 "X" Fusion Monster" (docs/EffectSystem.md section 64).
        else if (scan == "fusion") { scanFn = 0x140531D30; flags |= 0x802; }
        else if (scan == "field")
        {
            // no list: an own destroy step picks with the game's field prompt (Slot0_OwnFieldDestroy); the row only carries the tests
            c.FieldPick = true;
            const std::string side = f.value("side", std::string("any"));
            c.FieldSide = side == "own" ? 0 : side == "opponent" ? 1 : side == "any" ? 2 : -1;
            if (c.FieldSide < 0) { error = "deck.side must be own, opponent or any"; return false; }
        }
        else if (scan != "deck") { error = "deck.scan must be deck, deckSummon, hand, handAt, handSummon,extra, extraSummon, fusion, field, grave, graveSummon, opponentGrave, bothGravesSummon, banished or banishedSummon"; return false; }

        if (f.contains("or"))
        {
            if (!f["or"].is_array()) { error = "deck.or must be a list of filters"; return false; }
            for (const nlohmann::json& alt : f["or"])
            {
                Clone other;
                other.From = c.From;
                if (!alt.is_object() || alt.contains("or") || !BuildDeckFilter(alt, other, error))
                {
                    if (error.empty()) error = "deck.or: each alternative is a filter object (no nested or)";
                    return false;
                }
                Clone::AltRow row;
                memcpy(row.Row, other.DeckRow, sizeof(row.Row));
                row.ExceptSelf = other.ExceptSelf;
                row.ExceptSelfAfter = other.ExceptSelfAfter;
                row.HandList = other.HandList;
                c.OrRows.push_back(row);
            }
        }
        c.HasDeckFilter = true;
        // A "can be Special Summoned" scan passes the row's param to the summon-legality check as the summoning card's id
        // (ScanGeneric_Deck: sub_14052B3E0(param) -> sub_14005AC50), so a negative name test there fails EVERY card (Barian's Hope,
        // in-duel 2026-10-14). Those scans drop this card's name from the results afterwards instead.
        const bool summonScan = scan == "graveSummon" || scan == "bothGravesSummon" || scan == "deckSummon" || scan == "handSummon"
            || scan == "banishedSummon" || scan == "extraSummon";
        c.ExceptSelf = f.value("exceptSelf", false) && param == 0 && !summonScan;
        c.ExceptSelfAfter = f.value("exceptSelf", false) && (param != 0 || summonScan);
        uint8_t* row = c.DeckRow;
        *reinterpret_cast<uint16_t*>(row + 0) = static_cast<uint16_t>(c.From);
        *reinterpret_cast<int16_t*>(row + 2) = param;
        *reinterpret_cast<uint32_t*>(row + 4) = 0xFFFF;
        *reinterpret_cast<uint64_t*>(row + 8) = scanFn;
        *reinterpret_cast<uint32_t*>(row + 16) = flags;
        *reinterpret_cast<uint32_t*>(row + 20) = 0;
        return true;
    }

    // One step of an effect: "from" plus the optional draw / filter / lp / deck parameters.
    // "This card is in the hand": the zone byte of the card's position (sub_140044480(card instance) >> 8, as Special Summon-this-card 0x140161E20
    // reads it; 13 = hand). The hand-only summon donor (Watch Cat) would otherwise also be offered from the GY, where its machine does nothing.
    uint64_t __fastcall Check_InHand(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const uint32_t position = reinterpret_cast<uint32_t(__fastcall*)(int)>(0x140044480)(effect[11]);
        return ((position >> 8) & 0xFF) == 13 ? 1 : 0;
    }

    // "This card is in the GY" (zone 16), for "If this card is in your GY: You can ..." (EffectScript "on in_grave:"). The GY / hand effects
    // table (table 2) is asked for every card past the field (Check_CanActivateKind3AtZone 0x140107930: zone > 12 -> table 2), so the donor's
    // row would also be offered from the banished cards; this keeps the effect to the GY.
    uint64_t __fastcall Check_InGrave(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const uint32_t position = reinterpret_cast<uint32_t(__fastcall*)(int)>(0x140044480)(effect[11]);
        return ((position >> 8) & 0xFF) == 16 ? 1 : 0;
    }

    // "During the (your / your opponent's) Battle / Main / Main or Battle Phase", "During your (opponent's) turn": the duel's current phase
    // (PlayerState +0x3788: 2 / 4 = Main Phase 1 / 2 - the game's own "(phase - 2) & ~2 == 0" test in Effect_IsTargetImmune -, 3 = BATTLE,
    // 5 = END: ContEffect_CoversZone) and the turn player (+0x3774) against the effect's controller (docs/EffectSystem.md sections 81, 93).
    // End Phase = 5 in both engines (duel.dll's Life Shaver / Abyss of the Sacred Beasts condition 0x180262AC0 tests DuelState +15388 == 5
    // with the turn player +15368 = the opponent: the game's +0x3788 / +0x3774 moved by the bridge's 1172-byte shift).
    constexpr int kPhaseBattle = 1 << 3, kPhaseMain = (1 << 2) | (1 << 4), kPhaseEnd = 1 << 5, kPhaseAny = -1;
    template <int Phases, int Whose>   // Phases: a mask of phase numbers; Whose: 0 either player's, 1 yours, 2 your opponent's
    uint64_t __fastcall Check_Phase(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const int phase = *reinterpret_cast<const int*>(YGO::Addr::Duel_CurrentPhase);
        const int turn = *reinterpret_cast<const int*>(YGO::Addr::Duel_TurnPlayer) & 1;
        if (phase < 0 || phase > 30 || !((Phases >> phase) & 1))
            return 0;
        const int controller = effect[1] & 1;
        return Whose == 0 || (Whose == 1) == (turn == controller) ? 1 : 0;
    }

    // "if this card battled (this turn)": its zone carries marker key 1024 (the game's "battled this turn" marker - Gladiator Beasts' condition
    // 0x14023BF30 tests Has_FlagMarkerKey(player, zone, 1024) at the end of the Battle Phase; section 93).
    uint64_t __fastcall Check_BattledThisTurn(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const uint32_t position = reinterpret_cast<uint32_t(__fastcall*)(int)>(0x140044480)(effect[11]);
        const int side = position & 1, zone = (position >> 8) & 0xFF;
        if (zone > 6)
            return 0;
        const auto Has_FlagMarkerKey = reinterpret_cast<int64_t(__fastcall*)(int, int, int)>(0x140052560);
        return Has_FlagMarkerKey(side, zone, 1024) ? 1 : 0;
    }

    // "If you control "X"": the card's "controlsName" is face-up on its controller's field (zones 0-12), by name - the game's own
    // Count_FaceUpSameNameOnField (0x1400167E0, player, code; Get_CardCodeAtZone + Card_IsSameName), as the uniqueness rule counts.
    uint64_t __fastcall Check_ControlsName(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        bool borrowedWithoutClone = false;
        Clone* c = Find(effect[0], borrowedWithoutClone);
        if (!c)
            return 0;
        uint16_t name = 0;
        for (Clone* part : AllParts(*c))
            if (part->ControlsName)
            {
                name = part->ControlsName;
                break;
            }
        using Count_t = int(__fastcall*)(int, uint16_t);
        return name && reinterpret_cast<Count_t>(0x1400167E0)(effect[1] & 1, name) > 0 ? 1 : 0;
    }

    // "If your LP are N or less" / "If your LP are lower than your opponent's": the side's LP as the engine reads it inline everywhere
    // (PlayerState block +0 XOR Duel_DuelEngine.LpXorKey, e.g. Duel_IsAnyPlayerOutOfLP 0x14003E020; YGO::DUELSTATE::LifePoints).
    template <int Mode>   // 0 at most the part's "lpAtMost", 1 lower than the opponent's
    uint64_t __fastcall Check_LifePoints(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        const int player = effect[1] & 1;
        const int own = YGO::DUELSTATE::LifePoints(player);
        if constexpr (Mode == 1)
            return own < YGO::DUELSTATE::LifePoints(player ^ 1) ? 1 : 0;
        bool borrowedWithoutClone = false;
        Clone* c = Find(effect[0], borrowedWithoutClone);
        if (!c)
            return 0;
        for (Clone* part : AllParts(*c))
            if (part->LpAtMost)
                return own <= part->LpAtMost ? 1 : 0;
        return 0;
    }

    // "If both players have a card in their Extra Deck" (Dogmatika: "send 1 monster from each player's Extra Deck"): the Extra Deck count of
    // each side's Duel::PlayerState::Player (+0x18). Under Master Duel's engine its own check runs instead: 0x180286840 (DuelState +24 and
    // +3572, the two players' Extra Deck counts; White Knight of Dogmatika 15993, Dogmatika Nation 15285, Dogmatika Lawbringer 22086 - see
    // MdCheckTwin).
    uint64_t __fastcall Check_BothExtraDecks(uint16_t*, uint64_t, uint64_t, uint64_t)
    {
        const auto extra = [](int side) { return *reinterpret_cast<const uint32_t*>(kDuelState + static_cast<uintptr_t>(side) * 0xD94 + 0x18); };
        return extra(0) != 0 && extra(1) != 0 ? 1 : 0;
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

    // The cost pickers' per-zone filter (Pick_ZoneFilter_BySlot2 0x1400DFE30, (record, player, zone)): it asks the row's slot 2 whether a zone
    // can be picked (Cost_TributeOne's picker and its "any candidate" check). A clone's slot 2 is its activation condition (Slot_ReturnConst2
    // for "always"), which answers yes for every zone - empty ones too (Swordsoul Auspice Chunjun, in-duel 2026-10-14). A clone with the
    // "canTributeOne" cost answers with the Tribute test instead: a monster of the record's player in zones 0-6 that can be Tributed.
    using PickFilter_t = int64_t(__fastcall*)(uint16_t*, uint32_t, int);
    PickFilter_t orig_PickFilter = reinterpret_cast<PickFilter_t>(0x1400DFE30);

    int64_t __fastcall Hook_PickFilter(uint16_t* effect, uint32_t player, int zone)
    {
        // a raw Tribute cost (Cost_TributeOne / Cost_TributePickedMonsters under this card's id, section 90): its own zone test
        if (Clone* pick = effect ? PayingClone(effect[0]) : nullptr; pick && pick->Verb == Clone::PickVerb::Tribute)
            return TributeCandidate(*pick, effect, player, zone) ? 1 : 0;
        // a raw "send 1 X from your hand or field" cost: the field half of Pick_HandOrFieldFilter (a card of yours the row matches)
        if (Clone* pick = effect ? PayingClone(effect[0]) : nullptr; pick && pick->Verb == Clone::PickVerb::SendHandOrField)
        {
            const int own = (effect[1] ^ effect[5]) & 1;
            return static_cast<int>(player & 1) == own && FieldZoneMatches(*pick, own, own, zone, effect[11]) ? 1 : 0;
        }
        Clone* clone = nullptr;
        if (effect)
        {
            if (g_Active && g_Active->CostFrom && effect[0] == g_Active->CostFrom)
                clone = g_Active;   // inside the cost slot: the record shows the cost card
            else if (g_Active && g_Active->MoveCount > 1)
                clone = g_Active;   // inside the multi-target picker's slot 4: the record shows the donor's id
            else
            {
                bool borrowedWithoutClone;
                clone = RouteByRow(Find(effect[0], borrowedWithoutClone), effect);
            }
        }
        // a multi-target move (section 86): the picker's zone test is the target filter row (Target_Generic_FromFilterRow, as slot 1 tests it)
        if (clone && clone->MoveScope == 1 && clone->MoveCount > 1)
        {
            if (zone < 0 || zone > 12)
                return 0;
            using Target_t = int64_t(__fastcall*)(uint16_t*, uint32_t, int);
            return reinterpret_cast<Target_t>(0x1401B0FF0)(effect, player, zone) & 0xFF ? 1 : 0;
        }
        if (!clone || clone->CostCheck != reinterpret_cast<uint64_t>(&Check_CanTributeOne))
            return orig_PickFilter(effect, player, zone);
        const int own = effect[1] & 1;
        if (static_cast<int>(player & 1) != own || zone < 0 || zone > 6 || (orig_CardIdAt(own, zone, 1) & 0x3FFF) == 0)
            return 0;
        return reinterpret_cast<int64_t(__fastcall*)(int, int, int, int)>(0x14001D700)(own, own, zone, 1) & 0xFF ? 1 : 0;
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
            { "inGrave", reinterpret_cast<uint64_t>(&Check_InGrave) },  // this card is in the GY (the plugin's own check, see Check_InGrave)
            { "controlsName", reinterpret_cast<uint64_t>(&Check_ControlsName) },   // you control a face-up "X" ("controlsName": X, see Check_ControlsName)
            { "battlePhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseBattle, 0>) },          // during the Battle Phase (either player's)
            { "yourBattlePhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseBattle, 1>) },      // during your Battle Phase
            { "opponentBattlePhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseBattle, 2>) },  // during your opponent's Battle Phase
            { "mainPhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseMain, 0>) },              // during the Main Phase (section 93)
            { "yourMainPhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseMain, 1>) },
            { "opponentMainPhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseMain, 2>) },
            { "mainOrBattlePhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseMain | kPhaseBattle, 0>) },   // during the Main or Battle Phase
            { "yourMainOrBattlePhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseMain | kPhaseBattle, 1>) },
            { "opponentMainOrBattlePhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseMain | kPhaseBattle, 2>) },
            { "battledThisTurn", reinterpret_cast<uint64_t>(&Check_BattledThisTurn) },             // this card battled this turn (marker 1024)
            { "endPhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseEnd, 0>) },                // during the End Phase (either player's)
            { "yourEndPhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseEnd, 1>) },
            { "opponentEndPhase", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseEnd, 2>) },        // Life Shaver, Abyss of the Sacred Beasts
            { "yourTurn", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseAny, 1>) },                // during your turn
            { "opponentTurn", reinterpret_cast<uint64_t>(&Check_Phase<kPhaseAny, 2>) },            // during your opponent's turn
            { "lpAtMost", reinterpret_cast<uint64_t>(&Check_LifePoints<0>) },   // your LP are N or less ("lpAtMost": N)
            { "lpLower", reinterpret_cast<uint64_t>(&Check_LifePoints<1>) },    // your LP are lower than your opponent's
            { "canDetach", 0x1400FBFF0 },                // this Xyz Monster has enough materials to detach ("detach" of them; Thunder End Dragon)
            { "canTributeOne", reinterpret_cast<uint64_t>(&Check_CanTributeOne) },   // you control a monster that can be Tributed
            { "bothExtraDecks", reinterpret_cast<uint64_t>(&Check_BothExtraDecks) }, // both players have a card in their Extra Deck
            // No check (Slot_ReturnConst2). For "pay N LP": the LP cost card's own condition is card-specific (Delinquent Duo 0x140251110 =
            // "the opponent has a card in hand") and the LP total is not mapped yet, so the payment itself is not checked.
            { "always", 0x1400DE060 },
        };
        for (const auto& [n, address] : checks)
            if (name == n)
                return address;
        return 0;
    }

    uintptr_t MdCheckTwin(uint64_t check)
    {
        if (!g_DuelBase)
            return 0;
        if (check == reinterpret_cast<uint64_t>(&Check_BothExtraDecks))
            return g_DuelBase + 0x286840;   // both players' Extra Deck counts (traced 2026-10-31, docs/Overnight-2026-10-30.md section 5)
        // The plugin's checks that read only what Core's bridge mirrors from duel.dll into the game's state after every step (turn player
        // 0x14349B3B4 and phase 0x14349B3C8 are in its global run 0x3770-0x3834, LP in the player blocks): they run as they are, with the
        // record's display id (MdSlot2_Requires). Not the hand / GY / zone checks: their card instances are not verified to match.
        static const char* const mirrored[] = { "battlePhase", "yourBattlePhase", "opponentBattlePhase", "mainPhase", "yourMainPhase",
            "opponentMainPhase", "mainOrBattlePhase", "yourMainOrBattlePhase", "opponentMainOrBattlePhase", "endPhase", "yourEndPhase",
            "opponentEndPhase", "yourTurn", "opponentTurn",
            "lpAtMost", "lpLower" };
        for (const char* name : mirrored)
            if (check == CheckByName(name))
                return static_cast<uintptr_t>(check);
        // a game check: its duel.dll twin (the plugin's own checks read the game's state, which duel.dll does not use: none yet)
        const uint32_t rva = YGO::MasterDuelMap::ToDuelRva(check);
        return rva ? g_DuelBase + rva : 0;
    }

    // "summonCondition": { "from": ["hand", "deck", "extra", "grave", "banished", "spelltrap"], "by": "effect" | "not_effect" | "own" | "spell" |
    // "trap" | "monster" | { "card": id } | { "archetype": code }, "proper": true, "pendulum": true, "tributes": N }
    // "moveCondition": { "from": zone | [zones] (hand, deck, grave, field, monster, spelltrap), "by": "battle" | "effect" } (Clone::MoveCondition).
    bool ParseMoveCondition(const nlohmann::json& j, Clone::MoveCondition& m, const std::string& label)
    {
        auto fromZone = [&](const std::string& name) {
            static const std::pair<const char*, uint32_t> zones[] = { {"hand", 1u << 13}, {"deck", 1u << 15}, {"grave", 1u << 16}, {"gy", 1u << 16},
                {"field", 0x1FFFu}, {"monster", 0x7Fu}, {"spelltrap", 0x3Fu << 7} };
            for (const auto& z : zones)
                if (name == z.first) { m.FromZones |= z.second; return true; }
            Logger::WriteLog(std::format("{}: moveCondition \"from\": unknown zone \"{}\"", label, name), MODULE_NAME, 2);
            return false;
        };
        if (j.contains("from"))
        {
            if (j["from"].is_string() && !fromZone(j["from"].get<std::string>()))
                return false;
            if (j["from"].is_array())
                for (const auto& z : j["from"])
                    if (!z.is_string() || !fromZone(z.get<std::string>()))
                        return false;
        }
        if (j.contains("by"))
        {
            const std::string by = j.value("by", std::string());
            if (by == "battle") m.Battle = 1;
            else if (by == "effect" || by == "not_battle") m.Battle = 0;
            else { Logger::WriteLog(std::format("{}: moveCondition \"by\" must be battle or effect", label), MODULE_NAME, 2); return false; }
        }
        return true;
    }

    // "watch": {"kind": "card" | "monster" | "spell" | "trap", "race", "attribute", "archetype": code, "cardKind": "fusion" ..., "name": id,
    // "side": "you" | "opponent", "from": [zones], "by": "battle" | "effect" | "opponent", "other": true, "inGrave": true} (section 91)
    bool ParseWatch(const nlohmann::json& j, Clone::WatchCondition& w, const std::string& label)
    {
        static const std::pair<const char*, int> races[] = { {"Dragon",1},{"Zombie",2},{"Fiend",3},{"Pyro",4},{"SeaSerpent",5},{"Rock",6},{"Machine",7},
            {"Fish",8},{"Dinosaur",9},{"Insect",10},{"Beast",11},{"BeastWarrior",12},{"Plant",13},{"Aqua",14},{"Warrior",15},{"WingedBeast",16},
            {"Fairy",17},{"Spellcaster",18},{"Thunder",19},{"Reptile",20},{"Psychic",21},{"Wyrm",22},{"Cyberse",23},{"DivineBeast",24},{"CreatorGod",25} };
        static const std::pair<const char*, int> attributes[] = { {"Light",1},{"Dark",2},{"Water",3},{"Fire",4},{"Earth",5},{"Wind",6},{"Divine",7} };
        static const std::pair<const char*, int> cardKinds[] = { {"fusion",1},{"synchro",2},{"xyz",3},{"link",4},{"ritual",5} };
        auto lookup = [&](const char* key, const auto& table, int& out) {
            if (!j.contains(key))
                return true;
            const std::string v = j.value(key, std::string());
            for (const auto& [n, k] : table)
                if (_stricmp(n, v.c_str()) == 0) { out = k; return true; }
            Logger::WriteLog(std::format("{}: watch \"{}\": unknown \"{}\"", label, key, v), MODULE_NAME, 2);
            return false;
        };
        const std::string kind = j.value("kind", std::string("card"));
        w.Kind = kind == "monster" ? 1 : kind == "spell" ? 2 : kind == "trap" ? 3 : 0;
        if (!w.Kind && kind != "card")
        {
            Logger::WriteLog(std::format("{}: watch \"kind\" must be card, monster, spell or trap", label), MODULE_NAME, 2);
            return false;
        }
        if (!lookup("race", races, w.Race) || !lookup("attribute", attributes, w.Attribute) || !lookup("cardKind", cardKinds, w.CardKind))
            return false;
        w.Archetype = j.value("archetype", 0);
        w.Name = static_cast<uint16_t>(j.value("name", 0) & 0x3FFF);
        const std::string side = j.value("side", std::string("both"));
        w.Side = side == "you" ? 0 : side == "opponent" ? 1 : -1;
        if (j.contains("from") && j["from"].is_array())
            for (const auto& z : j["from"])
            {
                static const std::pair<const char*, uint32_t> zones[] = { {"field", 0x1FFFu}, {"monster", 0x7Fu}, {"spelltrap", 0x1F80u},
                    {"hand", 1u << 13}, {"deck", 1u << 15}, {"grave", 1u << 16} };
                const std::string name = z.is_string() ? z.get<std::string>() : std::string();
                auto it = std::find_if(std::begin(zones), std::end(zones), [&](const auto& p) { return name == p.first; });
                if (it == std::end(zones))
                {
                    Logger::WriteLog(std::format("{}: watch \"from\": unknown zone \"{}\"", label, name), MODULE_NAME, 2);
                    return false;
                }
                w.FromZones |= it->second;
            }
        const std::string by = j.value("by", std::string());
        w.By = by == "battle" ? 1 : by == "effect" ? 2 : by == "opponent" ? 3 : 0;
        if (!w.By && !by.empty())
        {
            Logger::WriteLog(std::format("{}: watch \"by\" must be battle, effect or opponent", label), MODULE_NAME, 2);
            return false;
        }
        w.Other = j.value("other", false);
        w.InGrave = j.value("inGrave", false);
        return true;
    }

    bool ParseSummonCondition(const nlohmann::json& j, Clone::SummonCondition& s, const std::string& label)
    {
        using By = Clone::SummonCondition::By;
        s.Any = true;
        auto fromZone = [&](const std::string& name) {
            static const std::pair<const char*, uint32_t> zones[] = { {"hand", 1u << 13}, {"extra", 1u << 14}, {"extra_deck", 1u << 14}, {"deck", 1u << 15},
                {"grave", 1u << 16}, {"gy", 1u << 16}, {"banished", 1u << 17}, {"spelltrap", 0x1Fu << 7} };
            for (const auto& z : zones)
                if (name == z.first) { s.FromZones |= z.second; return true; }
            Logger::WriteLog(std::format("{}: summonCondition \"from\": unknown zone \"{}\"", label, name), MODULE_NAME, 2);
            return false;
        };
        if (j.contains("from"))
        {
            if (j["from"].is_string() && !fromZone(j["from"].get<std::string>()))
                return false;
            if (j["from"].is_array())
                for (const auto& z : j["from"])
                    if (!z.is_string() || !fromZone(z.get<std::string>()))
                        return false;
        }
        if (j.contains("by"))
        {
            const auto& by = j["by"];
            if (by.is_object() && by.contains("card") && by["card"].is_number_integer())
                s.ByWhat = By::Card, s.ByValue = by["card"].get<int>();
            else if (by.is_object() && by.contains("archetype") && by["archetype"].is_number_integer())
                s.ByWhat = By::Archetype, s.ByValue = by["archetype"].get<int>();
            else if (by.is_string())
            {
                static const std::pair<const char*, By> words[] = { {"effect", By::Effect}, {"not_effect", By::NotEffect}, {"own", By::Own},
                    {"spell", By::Spell}, {"trap", By::Trap}, {"monster", By::Monster} };
                const std::string word = by.get<std::string>();
                auto it = std::find_if(std::begin(words), std::end(words), [&](const auto& w) { return word == w.first; });
                if (it == std::end(words))
                {
                    Logger::WriteLog(std::format("{}: summonCondition \"by\": unknown \"{}\"", label, word), MODULE_NAME, 2);
                    return false;
                }
                s.ByWhat = it->second;
            }
        }
        s.Proper = j.value("proper", false);
        s.Pendulum = j.value("pendulum", false);
        s.MinTributes = j.value("tributes", 0);
        if (j.contains("card") && j["card"].is_object())
        {
            // the summoned monster (summon reactions, section 87); names as the summon lock's (SummonLockAllows' tests)
            static const std::pair<const char*, int> races[] = { {"Dragon",1},{"Zombie",2},{"Fiend",3},{"Pyro",4},{"SeaSerpent",5},{"Rock",6},{"Machine",7},
                {"Fish",8},{"Dinosaur",9},{"Insect",10},{"Beast",11},{"BeastWarrior",12},{"Plant",13},{"Aqua",14},{"Warrior",15},{"WingedBeast",16},
                {"Fairy",17},{"Spellcaster",18},{"Thunder",19},{"Reptile",20},{"Psychic",21},{"Wyrm",22},{"Cyberse",23},{"DivineBeast",24},{"CreatorGod",25} };
            static const std::pair<const char*, int> attributes[] = { {"Light",1},{"Dark",2},{"Water",3},{"Fire",4},{"Earth",5},{"Wind",6},{"Divine",7} };
            static const std::pair<const char*, int> kinds[] = { {"fusion",1},{"synchro",2},{"xyz",3},{"link",4},{"ritual",5} };
            auto lookup = [](const nlohmann::json& o, const char* key, const auto& table) {
                const std::string v = o.value(key, std::string());
                for (const auto& [n, k] : table) if (_stricmp(n, v.c_str()) == 0) return k;
                return 0;
            };
            const nlohmann::json& card = j["card"];
            s.CardRace = lookup(card, "race", races);
            s.CardAttribute = lookup(card, "attribute", attributes);
            s.CardKind = lookup(card, "kind", kinds);
            s.CardArchetype = card.value("archetype", 0);
        }
        const std::string to = j.value("to", std::string());
        s.ToSide = to == "own" ? 0 : to == "opponent" ? 1 : -1;
        return true;
    }

    bool ParseStep(const nlohmann::json& clone, Clone& c, const std::string& label)
    {
        // "rawRow": no donor at all - the row and its slots are built from engine functions (section 88), so there is no "from"
        const bool rawRow = clone.is_object() && clone.value("rawRow", false);
        if (!clone.is_object() || (!rawRow && (!clone.contains("from") || !clone["from"].is_number_integer())))
        {
            Logger::WriteLog(std::format("{}: an effect step needs {{ \"from\": <vanilla id> }} or \"rawRow\": true, skipped", label), MODULE_NAME, 2);
            return false;
        }
        const int from = rawRow ? 0 : clone["from"].get<int>();
        if (!rawRow && (from < 1 || from >= kFirstExtraCardId))
        {
            Logger::WriteLog(std::format("{}: effect step from {} is not a game card, skipped", label, from), MODULE_NAME, 2);
            return false;
        }
        c.From = from;
        c.RawRow = rawRow;
        c.RawSlots = rawRow || clone.value("rawSlots", false);
        c.OncePerTurn = clone.value("oncePerTurn", false);
        c.OncePerDuel = !c.OncePerTurn && clone.value("oncePerDuel", false);
        c.UsesPerTurn = clone.value("usesPerTurn", 0);
        c.OncePerCopy = !c.OncePerTurn && !c.OncePerDuel && (clone.value("oncePerTurnPerCopy", false) || c.UsesPerTurn > 1);
        c.ControlsName = static_cast<uint16_t>(clone.value("controlsName", 0) & 0x3FFF);
        c.LpAtMost = clone.value("lpAtMost", 0);
        if (clone.contains("summonLock") && clone["summonLock"].is_object())
        {
            static const std::pair<const char*, int> races[] = { {"Dragon",1},{"Zombie",2},{"Fiend",3},{"Pyro",4},{"SeaSerpent",5},{"Rock",6},{"Machine",7},
                {"Fish",8},{"Dinosaur",9},{"Insect",10},{"Beast",11},{"BeastWarrior",12},{"Plant",13},{"Aqua",14},{"Warrior",15},{"WingedBeast",16},
                {"Fairy",17},{"Spellcaster",18},{"Thunder",19},{"Reptile",20},{"Psychic",21},{"Wyrm",22},{"Cyberse",23},{"DivineBeast",24},{"CreatorGod",25} };
            static const std::pair<const char*, int> attributes[] = { {"Light",1},{"Dark",2},{"Water",3},{"Fire",4},{"Earth",5},{"Wind",6},{"Divine",7} };
            static const std::pair<const char*, int> kinds[] = { {"fusion",1},{"synchro",2},{"xyz",3},{"link",4},{"ritual",5} };
            auto lookup = [](const nlohmann::json& j, const char* key, const auto& table) {
                const std::string v = j.value(key, std::string());
                for (const auto& [n, k] : table) if (_stricmp(n, v.c_str()) == 0) return k;
                return 0;
            };
            const nlohmann::json& lock = clone["summonLock"];
            c.HasSummonLock = true;
            c.SummonLockExtraOnly = lock.value("from", std::string("any")) == "extra";
            c.SummonLockOnResolve = lock.value("when", std::string("activate")) == "resolve";
            c.SummonLockKind = lookup(lock, "kind", kinds);
            c.SummonLockRace = lookup(lock, "race", races);
            c.SummonLockAttribute = lookup(lock, "attribute", attributes);
            c.SummonLockArchetype = lock.value("archetype", 0);
        }
        if (clone.contains("limitGroup") && clone["limitGroup"].is_number_integer())
            c.LimitGroup = clone["limitGroup"].get<int>();
        c.Quick = clone.value("quick", false);
        if (clone.contains("trigger") && clone["trigger"].is_string())
            c.Trigger = clone["trigger"].get<std::string>();
        if (clone.contains("summonCondition") && clone["summonCondition"].is_object() && !ParseSummonCondition(clone["summonCondition"], c.Summon, label))
            return false;
        if (clone.contains("moveCondition") && clone["moveCondition"].is_object() && !ParseMoveCondition(clone["moveCondition"], c.Move, label))
            return false;
        if (clone.contains("watch") && clone["watch"].is_object() && !ParseWatch(clone["watch"], c.Watch, label))
            return false;
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
        c.OwnTrigger = clone.value("ownTrigger", false);
        if (c.OwnTrigger && !c.ActionFrom)
            c.ActionFrom = c.From;   // "from" names the action card; the row is the plugin's own
        // "costSlot": the donor row's place (hand / field table) with another of the game's self costs in slot 3 (docs/EffectSystem.md
        // section 84): "sendSelfFromHand" = Cost_SendSelfFromHandToGrave 0x1401FF020 (a move to the GY, not a discard; Fabled Grimro, Garbage
        // Ogre), "sendSelfFromField" = Cost_SendSelfFromFieldToGrave 0x1401FCF30 (Action_DestroyZoneMask reason 16; Wightprincess's field row).
        if (clone.contains("costSlot") && clone["costSlot"].is_string())
        {
            const std::string slot = clone["costSlot"].get<std::string>();
            if (slot == "sendSelfFromHand") c.SlotOverride[3] = 0x1401FF020;
            else if (slot == "sendSelfFromField") c.SlotOverride[3] = 0x1401FCF30;
            else if (slot == "banishSelfFromHand") c.SlotOverride[3] = 0x140209040;    // Cost_BanishSelfFromHand (Battlin' Boxer Counterpunch's hand row)
            else if (slot == "banishSelfFromField") c.SlotOverride[3] = 0x1401FD800;   // Cost_BanishSelfFromField (Kozmo Farmgirl, Snowy Canvas)
            else if (slot == "tributeSelfFromHand") c.SlotOverride[3] = 0x14020A140;   // Cost_TributeSelfFromHand (Artifact Lancea's hand row)
            // section 102, no donor: Cost_BanishSelfFromGrave (Rose Lover, Mezuki: Grave_FindIndexOfInstance(player, word11) +
            // Move_GraveCardToBanished, no id test)
            else if (slot == "banishSelfFromGrave") c.SlotOverride[3] = 0x1401FA670;
            // Cost_DiscardSelf (Hecatrice: Hand_FindIndexOfInstance + Hand_DiscardAt; only 14449 is id-keyed)
            else if (slot == "discardSelf") c.SlotOverride[3] = 0x1401FCEB0;
            // Cost_TributeSelf (Planet Pathfinder: Queue_TributeAtZone(player, zone); the archetype-193 GY substitute is a game rule, the
            // other id branches are 5583 / 6160 / 6797 / 7880 / 9913)
            else if (slot == "tributeSelf") c.SlotOverride[3] = 0x1401F8BA0;
            else
            {
                Logger::WriteLog(std::format("{}: unknown \"costSlot\" \"{}\" (sendSelfFromHand, sendSelfFromField, banishSelfFromHand, banishSelfFromField, tributeSelfFromHand, banishSelfFromGrave, discardSelf, tributeSelf), skipped", label, slot), MODULE_NAME, 2);
                return false;
            }
        }
        if (clone.contains("costPick") && clone["costPick"].is_object())
        {
            // {"verb": "banish" | "discard" | "send" | "tribute" | "reveal", "count": N, "deck": {filter}, "self": true, "raw": true}: the game's
            // cost machine for the verb (Clone::PickVerb). Raw (every verb but a donor banish): no donor id, the machine runs as this card.
            const nlohmann::json& pick = clone["costPick"];
            auto part = std::make_shared<Clone>();
            const std::string verb = pick.value("verb", std::string("banish"));
            static const std::pair<const char*, Clone::PickVerb> verbs[] = { {"banish", Clone::PickVerb::Banish}, {"discard", Clone::PickVerb::Discard},
                {"send", Clone::PickVerb::SendHand}, {"tribute", Clone::PickVerb::Tribute}, {"reveal", Clone::PickVerb::Reveal},
                {"sendHandOrField", Clone::PickVerb::SendHandOrField} };
            bool knownVerb = false;
            for (const auto& [name, v] : verbs)
                if (verb == name)
                {
                    part->Verb = v;
                    knownVerb = true;
                }
            if (!knownVerb)
            {
                Logger::WriteLog(std::format("{}: \"costPick\" verb \"{}\" (banish, discard, send, tribute, reveal), skipped", label, verb), MODULE_NAME, 2);
                return false;
            }
            part->RawSlots = pick.value("raw", false) || part->Verb != Clone::PickVerb::Banish;
            if (part->RawSlots)
                part->CostSelf = pick.value("self", false);
            else
                part->From = pick.value("self", false) ? 9585 : 7452;   // Laval Lakeside Lady (also banishes this card) / The Beginning of the End
            // Cost_RevealPickedHandCards / Cost_SendPickedHandOrFieldCardToGrave take one card for an id they do not list
            part->CostPickCount = part->Verb == Clone::PickVerb::Reveal || part->Verb == Clone::PickVerb::SendHandOrField ? 1 : std::clamp(pick.value("count", 1), 1, 5);
            // the hand verbs' picker asks one hand index at a time (handAt); Tribute tests your own field (FieldZoneMatches)
            nlohmann::json deck = pick.contains("deck") && pick["deck"].is_object() ? pick["deck"] : nlohmann::json::object();
            const bool handVerb = part->Verb == Clone::PickVerb::Discard || part->Verb == Clone::PickVerb::SendHand || part->Verb == Clone::PickVerb::Reveal
                || part->Verb == Clone::PickVerb::SendHandOrField;
            if (handVerb)
                deck["scan"] = "handAt";
            else if (part->Verb == Clone::PickVerb::Tribute)
            {
                deck["scan"] = "field";
                deck["side"] = "own";
                if (!deck.contains("type"))
                    deck["type"] = "monster";
            }
            std::string error;
            if ((part->Verb == Clone::PickVerb::Banish && !pick.contains("deck")) || !BuildDeckFilter(deck, *part, error))
            {
                Logger::WriteLog(std::format("{}: \"costPick\" needs a \"deck\" filter ({}), skipped", label, error), MODULE_NAME, 2);
                return false;
            }
            if (part->Verb == Clone::PickVerb::SendHandOrField)
                part->FieldSide = 0;   // its field half: your own cards
            c.CostPick = part;
            switch (part->Verb)
            {
            case Clone::PickVerb::SendHandOrField: c.SlotOverride[3] = 0x140208E00; break;   // Cost_SendPickedHandOrFieldCardToGrave
            case Clone::PickVerb::Banish: c.SlotOverride[3] = 0x1401F99D0; break;     // Cost_BanishPickedCards
            case Clone::PickVerb::Discard: c.SlotOverride[3] = 0x1401F8010; break;    // Cost_DiscardPickedHandCards
            case Clone::PickVerb::SendHand: c.SlotOverride[3] = 0x1401FF090; break;   // Cost_SendPickedHandCardsToGrave
            case Clone::PickVerb::Reveal: c.SlotOverride[3] = 0x140202770; break;     // Cost_RevealPickedHandCards
            case Clone::PickVerb::Tribute:                                             // Cost_TributeOne / Cost_TributePickedMonsters
                c.SlotOverride[3] = part->CostPickCount == 1 ? 0x1401FDDF0 : 0x1401FE930;
                break;
            }
        }
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
                    Logger::WriteLog(std::format("{}: unknown \"require\" check \"{}\" (canBanishSelfFromGrave, canDiscardSelf, canTributeSelf, canSummonSelf, canSummonSelfFromGrave, noMonsters, controlsMatch, controlsName, battlePhase, yourBattlePhase, opponentBattlePhase, mainPhase, yourMainPhase, opponentMainPhase, mainOrBattlePhase, yourMainOrBattlePhase, opponentMainOrBattlePhase, yourTurn, opponentTurn, battledThisTurn, lpAtMost, lpLower, inHand, inGrave, canDetach, canTributeOne, bothExtraDecks, endPhase, yourEndPhase, opponentEndPhase), skipped", label, n), MODULE_NAME, 2);
                    return false;
                }
                c.Require.push_back(check);
            }
        }
        c.DetachCount = clone.value("detach", 0);
        if (clone.contains("steps") && clone["steps"].is_array())
        {
            for (const nlohmann::json& step : clone["steps"])
            {
                Clone::StepOp op;
                const std::string kind = step.value("op", std::string());
                const std::string who = step.value("who", std::string("own"));
                op.Op = kind == "draw" ? 0 : kind == "gain" ? 1 : kind == "damage" ? 2 : -1;
                op.Who = who == "own" ? 0 : who == "opponent" ? 1 : who == "both" ? 2 : -1;
                op.N = step.value("n", 0);
                if (op.Op < 0 || op.Who < 0 || op.N < 0)
                {
                    Logger::WriteLog(std::format("{}: effectClone steps: unknown step {}, skipped", label, step.dump()), MODULE_NAME, 2);
                    return false;
                }
                c.Steps.push_back(op);
            }
            c.SlotOverride[0] = reinterpret_cast<uint64_t>(&Slot0_RunSteps);
        }
        // "move": {"scope": "target" | "self", "to": "hand" | "grave" | "destroy" | "banished" | "banishedFaceDown" | "deck" | "deckTop" |
        // "deckBottom" | "extra"} - the plugin's own move step (Slot0_OwnMove, docs/EffectSystem.md section 86). A "target" move keeps the
        // donor row's slot 1 (Target_Generic_FromFilterRow with this clone's "filter").
        if (clone.contains("move") && clone["move"].is_object())
        {
            static const std::tuple<const char*, int, uint32_t> dests[] = { {"hand", 13, 0}, {"grave", 16, 0}, {"destroy", 16, 0x2000002},
                {"banished", 17, 0}, {"banishedFaceDown", 17, 0x80}, {"deck", 15, 0x4000000}, {"deckTop", 15, 0}, {"deckBottom", 15, 0x800},
                {"extra", 14, 0} };
            const nlohmann::json& move = clone["move"];
            const std::string to = move.value("to", std::string()), scope = move.value("scope", std::string("target"));
            for (const auto& [name, reason, flags] : dests)
                if (to == name)
                {
                    c.MoveReason = reason;
                    c.MoveFlags = flags;
                }
            c.MoveScope = scope == "target" ? 1 : scope == "self" ? 2 : 0;
            c.MoveCount = std::clamp(move.value("count", 1), 1, 5);
            if (c.MoveScope == 1 && c.MoveCount > 1)
                c.SlotOverride[4] = 0x140094DC0;   // Slot4_MultiTargetPicker_StateMachine (Twin Twisters, Double Cyclone)
            if (c.MoveScope == 1)
            {
                // the targeting slots by name (a raw row has no donor to give them, section 88): Target_Generic_FromFilterRow (this clone's
                // "filter" through Hook_GetRow) and Slot4_ActivationPrompt_Text281 (prompt 281, then the pick through Pick_ZoneFilter_BySlot2)
                if (!c.SlotOverride[1]) c.SlotOverride[1] = 0x1401B0FF0;
                if (!c.SlotOverride[4]) c.SlotOverride[4] = 0x14008CCC0;
            }
            if (!c.MoveReason || !c.MoveScope)
            {
                Logger::WriteLog(std::format("{}: \"move\" needs \"scope\" target|self and \"to\" hand|grave|destroy|banished|banishedFaceDown|deck|deckTop|deckBottom|extra; skipped", label), MODULE_NAME, 2);
                return false;
            }
            c.SlotOverride[0] = reinterpret_cast<uint64_t>(&Slot0_OwnMove);
        }
        // "targetDo": "position" | "faceDown" | "control" - act on the targets chosen at activation with the game's own target machines,
        // run raw (section 92: their id ladders have no branch for an id they do not list); the targeting slots as a target move's
        if (clone.contains("targetDo") && clone["targetDo"].is_string())
        {
            const std::string what = clone["targetDo"].get<std::string>();
            // "revive": Monster Reborn 4842's row run raw (section 101). Each of its functions takes its default path for an id it does not
            // list (4842 is in none of them): slot 4 Slot4_SelectFromList_Driver (0x140092910: Deck_CollectMatchingCards with the "deck" list,
            // prompt 94, UI_BeginCardListSelect mode 6, Record_AddSelectedTarget = the target is chosen at activation), slot 2
            // Slot2_Condition_Generic_And_FA680 (0x1400F9940: Cond_GenericCanActivate_Ladder's tail = a free Monster Zone + the summon
            // rule, and Cond_ListHasEnoughMatches), slot 0 Slot0_SpecialSummonTarget_Driver (0x140169590 -> Action_QueueTargetedZoneEffect
            // with (1, 2): the target checked again, summoned to the activating player's field through Chain_AddEffectLink). No slot 1.
            if (what == "revive")
            {
                c.SlotOverride[0] = 0x140169590;
                if (!c.SlotOverride[2]) c.SlotOverride[2] = 0x1400F9940;
                if (!c.SlotOverride[4]) c.SlotOverride[4] = 0x140092910;
                if (!clone.contains("deck"))
                {
                    Logger::WriteLog(std::format("{}: \"targetDo\": \"revive\" needs a \"deck\" list (the GY scan); skipped", label), MODULE_NAME, 2);
                    return false;
                }
            }
            // The plain one-target Spell rows run raw (section 103); each slot 0 is Has_SelectedTarget(0) + Check_TargetStillValid (which looks
            // this card's own row up again) + Action_DestroyZoneMask, slot 4 the prompt driver with its text, no id test on these paths:
            // Slot0_DestroyFirstTarget (Remove Trap 4838: reason 16, flags 0x2000002, prompt 281)
            else if (what == "destroy") c.SlotOverride[0] = 0x140156D40;
            // Slot0_ReturnTargetToHand (Spiritualism 5246: reason 13, prompt 348 = Slot4_ActivationPrompt_Text348)
            else if (what == "return")
            {
                c.SlotOverride[0] = 0x140157350;
                if (!c.SlotOverride[4]) c.SlotOverride[4] = 0x14008CCF0;
            }
            // Slot0_BanishTarget (Legendary Knight Hermos 11887: reason 17, face-down only for 12076 / 12767; prompt 293 = _Text293)
            else if (what == "banish")
            {
                c.SlotOverride[0] = 0x140156ED0;
                if (!c.SlotOverride[4]) c.SlotOverride[4] = 0x14008CD40;
            }
            // Slot0_ChangeTargetsBattlePosition (Stop Defense / Enemy Controller): toggles each target's position (Duel_ChangePosition_OfferFlip_Ev12or13)
            else if (what == "position") c.SlotOverride[0] = 0x14016B430;
            // Slot0_SetTargetsFaceDown (Book of Moon): Change_ToFaceDownDefense for each of the record's targets (word 16 = stored target count)
            else if (what == "faceDown") c.SlotOverride[0] = 0x14049BE90;
            // Slot0_TakeControlOfTarget (Mind Control / Change of Heart): target 0, until the End Phase
            else if (what == "control") c.SlotOverride[0] = 0x140177090;
            // the game's result markers on the targets (Slot0_OwnTargetResult): ATK change / effects negated until the end of the turn
            else if (what == "atk" || what == "negate")
            {
                c.ResultKey = what == "atk" ? 1040 : 1027;
                c.ResultValue = what == "atk" ? clone.value("amount", 0) : 0;
                if (what == "atk" && (c.ResultValue == 0 || c.ResultValue < -32768 || c.ResultValue > 32767))
                {
                    Logger::WriteLog(std::format("{}: \"targetDo\": \"atk\" needs an \"amount\" (-32768 to 32767, not 0); skipped", label), MODULE_NAME, 2);
                    return false;
                }
                c.SlotOverride[0] = reinterpret_cast<uint64_t>(&Slot0_OwnTargetResult);
            }
            else
            {
                Logger::WriteLog(std::format("{}: \"targetDo\" must be revive, destroy, return, banish, position, faceDown, control, atk or negate; skipped", label), MODULE_NAME, 2);
                return false;
            }
            if (what != "revive")
            {
                if (!c.SlotOverride[1]) c.SlotOverride[1] = 0x1401B0FF0;
                if (!c.SlotOverride[4]) c.SlotOverride[4] = 0x14008CCC0;
            }
        }
        // "selfDo": "summonFromHand" - "Special Summon this card from your hand" with no donor (section 100): YGO::Effects::Slot0_SpecialSummonSelfFromHand
        // (0x140161E20) run raw. For an id it does not list it needs marker (player, 13, 4064, card instance) - set by the hand-activation flow,
        // removed by Msg 0x12 - and the card in the hand, then sub_140054EA0 / sub_14005A570 (summon rules), a free Monster Zone (else the card
        // goes to the GY) and Chain_AddEffectLink of the card itself; its next step (128) returns 0. The checks are the "require"s inHand +
        // canSummonSelf (Cond_CanSpecialSummonSelf 0x1400FB9A0, whose unknown-id branch is Cond_GenericCanActivate_Ladder's generic tail).
        if (clone.contains("selfDo") && clone["selfDo"].is_string())
        {
            if (clone["selfDo"].get<std::string>() != "summonFromHand")
            {
                Logger::WriteLog(std::format("{}: \"selfDo\" must be summonFromHand; skipped", label), MODULE_NAME, 2);
                return false;
            }
            c.SlotOverride[0] = 0x140161E20;
        }
        // "do": "draw" - Pot of Greed 4844's row run raw (section 103): Effect_DrawCards_Resolve (0x14015F9C0: Get_NumberOfCardsToDraw, the
        // Mystical Refpanel swap, Draw_ExecuteDraw; only 13977 is id-keyed) and Effect_DrawCards_CanActivate (0x1400FAC40: the count +
        // Can_PlayerDrawCards). The count is this clone's "draw", which Hook_GetDraw answers for the card's own id.
        if (clone.contains("do") && clone["do"].is_string())
        {
            if (clone["do"].get<std::string>() != "draw" || !clone.contains("draw") || !clone["draw"].is_number_integer() || clone["draw"].get<int>() == 0)
            {
                Logger::WriteLog(std::format("{}: \"do\" must be draw, with a \"draw\" count (not 0); skipped", label), MODULE_NAME, 2);
                return false;
            }
            c.SlotOverride[0] = 0x14015F9C0;
            if (!c.SlotOverride[2]) c.SlotOverride[2] = 0x1400FAC40;
        }
        // "targetSlots": true - this effect chooses the target when it is activated although its own action does not target: the first step of
        // its chain is a "move" of the targets ("Target 1 X; shuffle it into the Deck, then draw 1 card"). Slots 1 / 4 = Remove Trap's
        // (Target_Generic_FromFilterRow 0x1401B0FF0 with this clone's "filter", Slot4_ActivationPrompt_Text281 0x14008CCC0); the step reads the
        // record's targets (Has_SelectedTarget) when it runs.
        if (clone.value("targetSlots", false))
        {
            c.SlotOverride[1] = 0x1401B0FF0;
            c.SlotOverride[4] = 0x14008CCC0;
        }
        if (clone.contains("own") && clone["own"].is_string())
        {
            // The plugin's own pick-and-act machine (Slot0_OwnPickAndAct); needs the "deck" filter for its list.
            const std::string own = clone["own"].get<std::string>();
            c.Own = own == "toHand" ? 1 : own == "summon" ? 2 : own == "toGrave" ? 3 : own == "banish" ? 4 : own == "destroy" ? 5
                : own == "toDeck" ? 6 : own == "toDeckTop" ? 7 : own == "toDeckBottom" ? 8 : own == "toExtra" ? 9 : 0;
            if (!c.Own || !clone.contains("deck"))   // the filter itself is parsed further down
            {
                Logger::WriteLog(std::format("{}: \"own\" must be toHand, summon, toGrave, banish, destroy, toDeck, toDeckTop, toDeckBottom or toExtra, with a \"deck\" filter; skipped", label), MODULE_NAME, 2);
                return false;
            }
            const bool fieldScan = clone["deck"].is_object() && clone["deck"].value("scan", std::string()) == "field";
            if ((c.Own == 5) != fieldScan)
            {
                Logger::WriteLog(std::format("{}: \"own\": \"destroy\" goes with \"deck.scan\": \"field\" (and only it); skipped", label), MODULE_NAME, 2);
                return false;
            }
            c.SlotOverride[0] = reinterpret_cast<uint64_t>(c.Own == 5 ? &Slot0_OwnFieldDestroy : &Slot0_OwnPickAndAct);
            c.OwnCount = std::clamp(clone.value("ownCount", 1), 1, 10);
            if (c.OwnCount > 1 && (c.Own == 2 || c.Own == 5))
            {
                Logger::WriteLog(std::format("{}: \"ownCount\" is for moves to the hand / GY / banishment / Deck (not summon or destroy); skipped", label), MODULE_NAME, 2);
                return false;
            }
        }
        if (clone.contains("label") && clone["label"].is_string())
        {
            const std::string utf8 = clone["label"].get<std::string>();
            const int n = MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
            c.Label.assign(static_cast<size_t>(n), L'\0');
            MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), c.Label.data(), n);
        }
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
            // A raw LP row (no donor): Red Medicine / Hinotama's own slot 0, Slot0_AlterLifePoints 0x14015E570 (duel.dll 0x1802AAD70), which
            // asks Get_LifePointChanges for the amounts - Hook_LpChanges / Hook_MdLpChanges answer with these.
            if (c.RawRow && !c.SlotOverride[0])
                c.SlotOverride[0] = 0x14015E570;
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

    void SetOwner(Clone& c, uint16_t owner)
    {
        c.Owner = owner;
        for (Clone& pre : c.Pre)
            SetOwner(pre, owner);
        for (Clone& part : c.Parts)
            SetOwner(part, owner);
        if (c.CostPick)
            SetOwner(*c.CostPick, owner);   // its pick list leaves this card out (ExceptSelf)
    }

    // Once-per-turn limits of a several-effect card: each once-per-turn effect, or each "limitGroup" (the bullets of "Once per turn: activate
    // 1 of these effects", which share one limit), takes the next of the engine's four independent keys, classes 5, 6, 7, 8 (markers
    // 1001-1004, traced 2026-10-25: Timing_Check_Class7 / 8 0x1400F7AC0 / 0x1400F7B10). The marker is keyed by the record's card id, and the
    // record of every effect of the card carries the card's own id, so a shared class IS a shared limit. A fifth limit shares class 5.
    // Per-copy limits ("Once per turn: ...", OncePerCopy) take classes 1, 2, 3, 4 the same way: the same keys in the card's own zone list
    // (UseLimit_Class1..4_RegisterZoneMarker*, traced 2026-10-25), a separate key space from the per-name ones.
    // Once-per-Duel limits take classes 9, 10 first: they use the per-name keys 1001 / 1002, so the once-per-turn ones start after them.
    void AssignLimitClasses(Clone& c, const std::string& label)
    {
        std::map<std::pair<bool, int>, uint16_t> groups;   // (per copy, limit group) -> class
        uint16_t duel = 9;
        std::map<int, uint16_t> duelGroups;   // limit group -> class: an 'or' group shares one once-per-Duel limit
        int ownDuel = 0;
        for (Clone* part : AllParts(c))
        {
            if (!part->OncePerDuel)
                continue;
            const int key = part->LimitGroup >= 0 ? part->LimitGroup : -1 - ownDuel++;
            auto it = duelGroups.find(key);
            if (it == duelGroups.end())
            {
                if (duel > 10)
                    Logger::WriteLog(std::format("{}: more than 2 once-per-Duel limits, the 3rd and later share the first one's", label), MODULE_NAME, 1);
                it = duelGroups.emplace(key, duel <= 10 ? duel++ : 9).first;
            }
            part->LimitClass = it->second;
        }
        uint16_t next[2] = { static_cast<uint16_t>(5 + (duel - 9)), 1 };   // per name (after the keys once per Duel took), per copy
        int own = 0;
        for (Clone* part : AllParts(c))
        {
            if (!part->OncePerTurn && !part->OncePerCopy)
                continue;
            const bool copy = !part->OncePerTurn;
            const uint16_t first = copy ? 1 : 5;
            const auto key = std::make_pair(copy, part->LimitGroup >= 0 ? part->LimitGroup : -1 - own++);
            auto it = groups.find(key);
            if (it == groups.end())
            {
                uint16_t& n = next[copy ? 1 : 0];
                if (n > first + 3)
                    Logger::WriteLog(std::format("{}: more than 4 once-per-turn limits {}, the 5th and later share the first one's",
                        label, copy ? "per copy" : "per card name"), MODULE_NAME, 1);
                it = groups.emplace(key, n <= first + 3 ? n++ : first).first;
            }
            part->LimitClass = it->second;
        }
    }

    // An "effectClone" object: its main step, "before" steps and "parts". False (logged) when it cannot be used.
    bool ParseEffect(const nlohmann::json& clone, Clone& c, const std::string& label)
    {
        if (!ParseStep(clone, c, label))
            return false;
        if (clone.contains("before") && clone["before"].is_array())
        {
            for (const nlohmann::json& stepJson : clone["before"])
            {
                Clone step;
                if (!ParseStep(stepJson, step, label))
                {
                    Logger::WriteLog(std::format("{}: a \"before\" step cannot be used, skipped", label), MODULE_NAME, 2);
                    return false;
                }
                c.Pre.push_back(step);
            }
        }
        // A card's further effects: "parts": [ { "from": ..., "trigger": ..., ... }, ... ] (each one step, its own source; see Clone::Parts).
        if (clone.contains("parts") && clone["parts"].is_array())
        {
            if (clone["parts"].size() >= 127)
            {
                Logger::WriteLog(std::format("{}: at most 127 independent effects are supported, skipped", label), MODULE_NAME, 2);
                return false;
            }
            for (const nlohmann::json& partJson : clone["parts"])
            {
                Clone part;
                if (ParseStep(partJson, part, label))
                    c.Parts.push_back(std::move(part));
            }
            Logger::WriteLog(std::format("{}: {} effect(s) ({} borrowed from {})", label, 1 + c.Parts.size(), c.From,
                [&] { std::string s; for (const Clone& p : c.Parts) s += std::format(", {}", p.From); return s; }()), MODULE_NAME, 0);
        }
        AssignLimitClasses(c, label);   // a one-effect card too: a per-copy limit is class 1, not the default 5
        return true;
    }

    void LoadClones()
    {
        // every mod's cards.json and the game folder's, merged (Yu-Gi-Oh-Mods.h)
        const auto shared = YGO::Mods::ReadMergedShared("cards.json", "cards");   // parsed once for the whole plugin
        const nlohmann::json& root = *shared;
        const nlohmann::json* list = root.is_object() && root.contains("cards") && root["cards"].is_array() ? &root["cards"] : nullptr;

        for (size_t i = 0; list && i < list->size(); ++i)
        {
            const nlohmann::json& entry = (*list)[i];
            if (!entry.is_object() || !entry.contains("effectClone"))
                continue;

            const std::string label = std::format("cards.json entry {} (\"{}\")", i, entry.value("name", std::string()));
            const nlohmann::json& clone = entry["effectClone"];
            // A script that is only lock(...) / hand_limit(...) compiles to {"lock": {...}} / {"handLimit": {...}}: no effect to clone
            // (Lock.cpp plays them).
            if (clone.is_object() && !clone.contains("from") && (clone.contains("lock") || clone.contains("handLimit") || clone.contains("aura") || clone.contains("protect") || clone.contains("nameAs") || clone.contains("onlyOne") || clone.contains("attackDirectly") || clone.contains("xyzAlso")))
                continue;
            // a raw row (section 88) has no "from": the row is built from engine functions
            const bool rawRow = clone.is_object() && clone.value("rawRow", false);
            if (!entry.contains("id") || !entry["id"].is_number_integer() || !clone.is_object()
                || (!rawRow && (!clone.contains("from") || !clone["from"].is_number_integer())))
            {
                Logger::WriteLog(std::format("{}: \"effectClone\" needs an integer \"id\" on the card and {{ \"from\": <vanilla id> }} or \"rawRow\": true, skipped", label), MODULE_NAME, 2);
                continue;
            }

            const int id = entry["id"].get<int>();
            const int from = rawRow ? 1 : clone["from"].get<int>();
            if (id < kFirstExtraCardId || id > kLastExtraCardId || from < 1 || from >= kFirstExtraCardId)
            {
                Logger::WriteLog(std::format("{}: effectClone from {} onto id {} is out of range (from must be a game card), skipped", label, from, id), MODULE_NAME, 2);
                continue;
            }

            Clone c;
            if (ParseEffect(clone, c, label))
            {
                SetOwner(c, static_cast<uint16_t>(id));
                g_Clones[static_cast<uint16_t>(id)] = std::move(c);
            }
        }

        // The game's own cards with a changed effect: Yu-Gi-Oh-Ex\effects.json (WolfX, Effects page, "Game cards"), merged over the mods.
        //   {"cards": [{"id": <game card id>, "name": "...", "overridden": true, "effectScript": "...", "effectClone": {...}}]}
        // "overridden": true is what makes an entry count (an entry without it is only kept for the editor). No "effectClone" (or null) = the
        // card has no effect at all. The card keeps its own id in duels, so lists that name the card itself (its own triggers in the engine's
        // per-id ladders that no hook covers) can still answer for it; the four effect tables, the hooked ladders, id tests, trigger lists and
        // evaluators answer with the override.
        const auto sharedOverrides = YGO::Mods::ReadMergedShared("effects.json", "cards");
        const nlohmann::json& overrides = *sharedOverrides;
        const nlohmann::json* games = overrides.is_object() && overrides.contains("cards") && overrides["cards"].is_array() ? &overrides["cards"] : nullptr;
        for (size_t i = 0; games && i < games->size(); ++i)
        {
            const nlohmann::json& entry = (*games)[i];
            if (!entry.is_object() || !entry.value("overridden", false))
                continue;
            const std::string label = std::format("effects.json entry {} (\"{}\")", i, entry.value("name", std::string()));
            if (!entry.contains("id") || !entry["id"].is_number_integer())
            {
                Logger::WriteLog(std::format("{}: needs an integer \"id\", skipped", label), MODULE_NAME, 2);
                continue;
            }
            const int id = entry["id"].get<int>();
            if (id < 1 || id >= kFirstExtraCardId)
            {
                Logger::WriteLog(std::format("{}: id {} is not a game card (new cards keep their effect in cards.json), skipped", label, id), MODULE_NAME, 2);
                continue;
            }
            Clone c;
            const nlohmann::json clone = entry.contains("effectClone") ? entry["effectClone"] : nlohmann::json();
            if (clone.is_null() || (clone.is_object() && !clone.contains("from") && clone.contains("lock")))
                c.From = kNoEffectSource;   // no effect at all (a lock-only override keeps just its lock, Lock.cpp)
            else if (!ParseEffect(clone, c, label))
                continue;
            SetOwner(c, static_cast<uint16_t>(id));
            g_Clones[static_cast<uint16_t>(id)] = std::move(c);
            g_Overridden.insert(static_cast<uint16_t>(id));
            Logger::WriteLog(clone.is_null() ? std::format("{}: game card {} has no effect now", label, id)
                                             : std::format("{}: game card {} now plays the effect of card {}", label, id, g_Clones[static_cast<uint16_t>(id)].From), MODULE_NAME, 0);
        }
    }
}

size_t EffectClone::Count()
{
    return g_Clones.size();
}

void EffectClone::Setup()
{
    // Config.ini [Yu-Gi-Oh-Effects] Trace=1: the diagnostic log lines (lookups, slot calls, deck filter rows, candidate counts).
    g_Trace = GetPrivateProfileIntA("Yu-Gi-Oh-Effects", "Trace", 0, ".\\Config.ini") != 0;
    if (g_Trace)
        Logger::WriteLog("trace.txt found: effect lookup tracing is on", MODULE_NAME, 0);
    LoadClones();
    Logger::WriteLog(std::format("Loaded {} custom effect clone(s) and {} overridden game card effect(s)", g_Clones.size() - g_Overridden.size(), g_Overridden.size()), MODULE_NAME, 0);
    if (g_Clones.empty())
        return;
    for (auto& [id, clone] : g_Clones)
        for (Clone* part : AllParts(clone))
            if (part->HasStats && part->ActionFrom)
                g_StatsByAction.emplace(static_cast<uint16_t>(part->ActionFrom), part);

    DetourAttach(&(PVOID&)orig_GetEntry, Hook_GetEntry);
    AttachMasterDuel();
    DetourAttach(&(PVOID&)orig_GetDraw, Hook_GetDraw);
    DetourAttach(&(PVOID&)orig_ChoiceCount, Hook_ChoiceCount);
    DetourAttach(&(PVOID&)orig_ChoiceLabel, Hook_ChoiceLabel);
    DetourAttach(&(PVOID&)orig_PromptText, Hook_PromptText);
    DetourAttach(&(PVOID&)orig_GetRow, Hook_GetRow);
    DetourAttach(&(PVOID&)orig_LpChanges, Hook_LpChanges);
    DetourAttach(&(PVOID&)orig_Collect, Hook_Collect);
    DetourAttach(&(PVOID&)orig_Count, Hook_Count);
    DetourAttach(&(PVOID&)orig_PickFilter, Hook_PickFilter);
    AttachLadders(std::make_integer_sequence<int, 7>());
    AttachIdTests(std::make_integer_sequence<int, 7>());
    DetourAttach(&(PVOID&)orig_CardIdAt, Hook_CardIdAt);
    DetourAttach(&(PVOID&)orig_OfferByRef, Hook_OfferByRef);
    DetourAttach(&(PVOID&)orig_MenuOffer, Hook_MenuOffer);
    DetourAttach(&(PVOID&)orig_InsertOffer, Hook_InsertOffer);
    DetourAttach(&(PVOID&)orig_EventEvaluator, Hook_EventEvaluator);
    DetourAttach(&(PVOID&)orig_MoveEvaluator, Hook_MoveEvaluator);
    // own attack triggers: the cards that have one, then the evaluator hook (docs/EffectSystem.md section 67)
    for (auto& [customId, cardClone] : g_Clones)
        for (Clone* part : AllParts(cardClone))
            if (part->OwnTrigger && (part->Trigger == "attack_declared" || part->Trigger == "opponent_attack_declared" || part->Trigger == "attacked"))
            {
                g_OwnAttackCards.push_back(customId);
                break;
            }
    DetourAttach(&(PVOID&)orig_AttackEval, Hook_AttackEval);
    // own start-of-Damage-Step triggers (docs/EffectSystem.md section 80)
    for (auto& [customId, cardClone] : g_Clones)
        for (Clone* part : AllParts(cardClone))
            if (part->OwnTrigger && (part->Trigger == "damage_step_battles" || part->Trigger == "damage_step_attacks"))
            {
                g_OwnDamageStepCards.push_back(customId);
                break;
            }
    if (!g_OwnDamageStepCards.empty())
        DetourAttach(&(PVOID&)orig_DamageStepStart, Hook_DamageStepStart);
    // own start-of-Battle-Phase triggers (docs/EffectSystem.md section 93)
    for (auto& [customId, cardClone] : g_Clones)
        for (Clone* part : AllParts(cardClone))
            if (part->OwnTrigger && part->Trigger.ends_with("battle_phase_start"))
            {
                g_OwnBattleStartCards.push_back(customId);
                break;
            }
    if (!g_OwnBattleStartCards.empty())
        DetourAttach(&(PVOID&)orig_BattlePhaseStart, Hook_BattlePhaseStart);
    // twice / thrice per turn (docs/EffectSystem.md section 74)
    bool anyUseCount = false;
    for (auto& [customId, cardClone] : g_Clones)
        for (Clone* part : AllParts(cardClone))
            anyUseCount |= part->UsesPerTurn > 1;
    if (anyUseCount)
        DetourAttach(&(PVOID&)orig_UseCount, Hook_UseCount);
    bool anyCostPick = false;
    for (auto& [customId, cardClone] : g_Clones)
        for (Clone* part : AllParts(cardClone))
            anyCostPick |= part->CostPick != nullptr;
    if (anyCostPick)
    {
        DetourAttach(&(PVOID&)orig_CostPickCount, Hook_CostPickCount);
        DetourAttach(&(PVOID&)orig_SendHandCount, Hook_SendHandCount);   // raw send / Tribute costs answer their counts (section 90)
        DetourAttach(&(PVOID&)orig_TributeCount, Hook_TributeCount);
    }
    bool anyMultiTarget = false;
    for (auto& [customId, cardClone] : g_Clones)
        for (Clone* part : AllParts(cardClone))
            anyMultiTarget |= part->MoveScope == 1 && part->MoveCount > 1;
    if (anyMultiTarget)
        DetourAttach(&(PVOID&)orig_TargetCount, Hook_TargetCount);
    // "You cannot Special Summon the turn you activate this" (docs/EffectSystem.md section 77)
    for (auto& [customId, cardClone] : g_Clones)
        for (Clone* part : AllParts(cardClone))
            if (part->HasSummonLock)
            {
                g_SummonLocks.emplace_back(customId, part);
                break;
            }
    if (!g_SummonLocks.empty())
    {
        DetourAttach(&(PVOID&)orig_RegisterLimit, Hook_RegisterLimit);
        DetourAttach(&(PVOID&)orig_SummonRestrictions, Hook_SummonRestrictions);
        Logger::WriteLog(std::format("{} card(s) with a turn summon lock: Register_UseLimit_Marker + Summon_CheckPlayerRestrictions hooked", g_SummonLocks.size()), MODULE_NAME, 0);
    }
    // own summon-reaction triggers (docs/EffectSystem.md section 68)
    for (auto& [customId, cardClone] : g_Clones)
        for (Clone* part : AllParts(cardClone))
            if (part->OwnTrigger && (part->Trigger == "opponent_special_summoned" || part->Trigger == "opponent_summoned" || part->Trigger == "monster_special_summoned"
                || part->Trigger == "monster_summoned"))
            {
                g_OwnSummonReactionCards.push_back(customId);
                break;
            }
    DetourAttach(&(PVOID&)orig_SummonReaction, Hook_SummonReaction);
    // own watcher triggers (docs/EffectSystem.md section 91): the move evaluator is hooked above; the leave-the-field evaluator only when needed
    for (auto& [customId, cardClone] : g_Clones)
        for (Clone* part : AllParts(cardClone))
            if (part->OwnTrigger && IsWatchTrigger(part->Trigger))
            {
                g_OwnWatchCards.push_back(customId);
                break;
            }
    if (!g_OwnWatchCards.empty())
        DetourAttach(&(PVOID&)orig_LeaveEval, Hook_LeaveEval);
    DetourAttach(&(PVOID&)orig_ListSearch, Hook_ListSearch);
    DetourAttach(&(PVOID&)orig_RowSearch, Hook_RowSearch);
    DetourAttach(&(PVOID&)orig_DiscardCount, Hook_DiscardCount);
    DetourAttach(&(PVOID&)orig_LpCost, Hook_LpCost);
    DetourAttach(&(PVOID&)orig_DetachCount, Hook_DetachCount);
    bool anyDeckFilter = false;
    for (auto& [id, clone] : g_Clones)
        for (Clone* part : AllParts(clone))
            anyDeckFilter |= part->HasDeckFilter || part->CostPick != nullptr;   // a costPick's pick list is a deck filter too
    if (anyDeckFilter)
    {
        if (YGO::Memory::MakeWritable(kDeckFilterTable, kDeckFilterRows * 24))
            DetourAttach(&(PVOID&)orig_DeckFilterIndex, Hook_DeckFilterIndex);
        else
            Logger::WriteLog("Deck filter table could not be made writable: \"deck\" filters only apply through Collect / Count", MODULE_NAME, 2);
    }
    bool anyNegate = false;
    for (auto& [id, clone] : g_Clones)
        for (Clone* part : AllParts(clone))
            anyNegate |= part->HasNegate;
    if (anyNegate)
    {
        // NegateTable lives in .rdata; Hook_NegateCheck writes a source's row for the length of a call.
        if (YGO::Memory::MakeWritable(reinterpret_cast<uintptr_t>(kNegateTable), kNegateRows * 6))
            DetourAttach(&(PVOID&)orig_NegateCheck, Hook_NegateCheck);
        else
            Logger::WriteLog("NegateTable could not be made writable: \"negate\" is not applied", MODULE_NAME, 2);
    }
    AttachPhaseHandlers();
    AttachInlineListStubs();
    AttachNamedHelpers(std::make_integer_sequence<int, static_cast<int>(std::size(kNamedHelpers))>());
}
