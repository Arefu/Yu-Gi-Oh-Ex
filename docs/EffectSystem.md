# The game's effect system, and how custom cards plug into it

Status: 2026-09-28. Written so a later session (or a person) can pick up card-effect work without re-deriving any of this.
Everything here was read from `YuGiOh.exe` (IDA database `YuGiOh.exe.i64`, names under `YGO::Effects::`) and from the game's data files.
Nothing about *running* a custom effect in a duel has been tested yet - the mechanism is built (see "What exists") but untried.

## 1. The one thing to remember

**Card effects in this exe are table driven, and every table is read through one function.**
An earlier note said "there is no per-card handler, it's one giant id ladder". That was wrong: that ladder
(`EffectCondition_CheckCardUsableAtTiming`, 0x1400679F0) is only the *summon/timing availability* check used by the AI and special-summon
searches. The effects themselves live in tables.

`YGO::Effects::Get_EffectTableEntryForCard` (0x1400DFBC0) - ~100 callers, the only reader of the four main tables.

* Argument: an **effect record**. word0 = card Konami id, word2 = effect index (a card with several effects has several rows with
  the same id; `sub_1400DE070` picks which), word4 = **table selector**.
* Selector -> table (rows are 48 bytes, sorted by id, binary searched):

| selector | table | address | rows | holds |
|---|---|---|---|---|
| 0 | `EffectTable_SpellTrap` | 0x140BA8730 (first 3 rows = id-3000 defaults by spell/trap property) | 2831 | Spell/Trap card effects |
| 1 | `EffectTable_MonsterEffect` | 0x140B5CC20 | 3489 | effect monsters |
| 2 | `EffectTable_Kind2` | 0x140B85A50 | 2970 | role not decoded (2290 monsters) |
| 3 | `EffectTable_Kind3_Head` + body | 0x140B38290 (131 rows) + 0x140B39B20 (2992 rows) | 3123 | triggers / continuous; head vs body chosen by card flags |

(If selector 2 and word3 == 40 it uses the spell/trap table.) Sizes are immediates in the code, so the tables cannot simply be grown.

* Row: `{u16 id, u16 extra[3], void* slot0..slot4}`. `extra` is not decoded (about half the rows have it non-zero).
* Slots (from the callers):
  * **slot0** resolve / execute the effect.
  * **slot1** *target predicate* `fn(effect, player, zone) -> bool`. `Build_TargetMask_Generic` (0x1400E0060) calls it for both players x 13 zones to build the affected-cards mask. (Raigeki's slot1 `Target_Raigeki_OpponentMonsters` selects the opponent's monsters; Dark Hole's selects all.)
  * **slot2** condition check `fn(effect, x) -> bool`; empty means allowed (`Call_Slot2_Condition_DefaultTrue`).
  * slot3 / slot4 not decoded (slot4 is often a tiny wrapper, e.g. `sub_14008CCC0 -> sub_14008C9D0(..., 281)`).
* **Effect type = rows with an identical set of five pointers.** Raigeki and Dark Hole share slot0 (`Effect_RunSlot1Action_Driver`) and differ only in slot1. Many rows across the game are exact duplicates: that is the reusable vocabulary. `effect_tables.xlsx` (Desktop\New folder) lists every type with its size, example cards and text.

## 2. Two layers: generic thunks + secondary tables

Most slots hold *generic thunks* that look the card up again in a **secondary id-keyed table** for the card-specific functions/parameters
(e.g. `Slot_Delegate_ToSecondaryTable_FE560` -> `Get_SecondaryRow_Table_140AFD7E0`: 2593 rows of 24 bytes, cond at +8, action at +16).
So a custom card needs either (a) a clone of a vanilla card by id (everything follows), or (b) rows in every secondary table its handlers consult.

Secondary handler tables found (function-pointer rows; accessor = the only/few readers):

| table | row | rows | accessor(s) |
|---|---|---|---|
| 0x140AFD7E0 | 24 | 2593 | `Get_SecondaryRow_Table_140AFD7E0` (0x1400FE560) |
| 0x140AF8180 | 32 | 685 | `Get_SecondaryRow_Table_140AF8180` (0x1400FE700) |
| 0x140B0CB10 | 40 | 100 | 0x1400FE8A0, 0x1401001D0 |
| 0x140AD2A80 | 24 | 5920 | 46 readers (a widely used per-card record table) |
| 0x140B1CB90 | 32 | 488 | 24 readers |
| 0x140B223D0 | 32 | 212 | 5 readers |
| 0x140B27260 | 24 | 349 | 4 readers |
| 0x140B1B9A0, 0x140B1C350, 0x140B20890, 0x140B24E00, 0x140B29990, 0x140B2BD40, 0x140B34D40, 0x140BCAC90 | 16-48 | 88-488 | one reader each |
| 0x1401B0F40 | (function) | | secondary row lookup used by target predicates like `sub_1401B0FF0` |

## 3. Parameter tables (a number an effect reads by card id)

Stride-4 `{u16 id, u16 value}` tables (about 60 found), e.g.:

* `CardID_WithNumberOfCardsToDraw` 0x140B15110 (470 rows) - read only by `Get_NumberOfCardsToDraw` (0x14015EA90); if the id is absent it falls to a big per-card ladder.
* `CardID_WithEffectDamage` 0x140B15B30 (240 rows) - LP amounts (see the Effects plugin's `Table_140B15B30`).
* Lists of member cards (AI hints and "cards that do X" sets, e.g. `RitualSummonRalated` 0x140ACD828 and the unnamed 0x140AD07C0 family) - AI heuristics, not effect execution.

The scan (`docs/effect-scripts/find_id_tables.py`) found **105** id-keyed tables (rows >= 80, u16 ids ascending, stride <= 64 bytes). There are certainly more: smaller tables, u32 ids, ids not in the first field, unsorted tables. Treat 105 as a floor.
Three kinds: (1) function-pointer rows = effect handlers (about 25 tables), (2) `{id, number}` = parameters like "draw N", (3) id lists = set membership.

## 4. Other id-keyed systems that affect a card

* **Archetypes**: `Is_CardInNamedArchetype` (0x14076CFF0) binary-searches `bin/CARD_Named.bin` lists (code = archetype index). Custom cards join via `"archetypes": [codes]` in cards.json (hook in Yu-Gi-Oh-Cards). `Archetypes.json` names them.
* **Summon conditions**: `EffectCondition_CheckCardUsableAtTiming` (0x1400679F0) - a giant per-id ladder of special-summon / activation-availability checks for the AI and searches. Custom ids never match, so they fall through to "no effect".
* **Fusion**: recipe tables + `Get_Fusion*` (Yu-Gi-Oh-Effects `Fusion.cpp`, done). Ritual: `RitualSummonRalated` is AI hints only; specific ritual spells hardcode their monster.
* **Game data files**: `taginfo_<lang>.bin` (filter vocabulary `{AD}/{FIND}TYPE: ATTR: KIND: LEVEL: RANK: LINK: ATK: DEF: ICON: DECK:MAIN SPECIALSUMMON:YES` plus localized archetype names), `tagdata.bin` (related-cards lists), `main/scriptdata_<lang>.bin` (story-mode dialogue, NOT card scripts).

## 5. What exists in the repo

* `Yu-Gi-Oh-Effects/EffectClone.cpp`: reads `"effectClone": { "from": <vanilla id>, "draw": N }` from cards.json and hooks
  `Get_EffectTableEntryForCard` (presents the source id to the original function, returns the source's row) and `Get_NumberOfCardsToDraw`
  (returns `draw`, or the source's count). A borrowed duel id (custom card above 16383, see `docs`/ygo-duel-id-remap-plan memory) is mapped back through `Card_GetRealIdForBorrowed`
  (exported by Yu-Gi-Oh-Cards); a borrowed id whose card has no clone gets NO effect instead of the vanilla owner's.
  **Built, not yet run in a duel.**
* The old table-relocation code in Yu-Gi-Oh-Effects (`Effects_FunctionTable`, `Table_140B15B30`, `CardsThatMakeYouDraw`, with its `Memory`/`File` helpers) was **removed** (2026-09-28; it is in git history): it copied and zeroed the game's tables and patched instruction bytes, and had a wrong size. The abandoned C++ ANTLR parser (`Script/EffectScript.*`, generated files) went too; `Script/EffectScript.g4` stays (WolfEx generates its C# parser from it).
* `EffectDispatch.cpp`: passthrough logger on `EffectCondition_CheckCardUsableAtTiming`.
* `Fusion.cpp`: custom fusion recipes.
* WolfEx "Effects" tab compiles EffectScript (`Script/EffectScript.g4`, v0 = `draw(N)`) to JSON `effect`; the runtime does not read it yet. The natural next step is for the compiler to emit `effectClone` (a draw-N script = Pot of Greed's handlers + count).

## 6. How to give a custom card an effect (current plan)

1. Find a vanilla card whose effect is the same *type* (effect_tables.xlsx: same handler set; read the example text). 
2. Put `"effectClone": {"from": <its id>, ...params}` on the custom card in cards.json. The lookup hook redirects; parameter tables need one hook each (draw count is done; add the LP table etc. as needed by decompiling the handler and finding which id-keyed table or ladder feeds its number).
3. For an effect no vanilla card has: compose new rows = pick slot functions from existing types (the reusable pieces), and only where nothing fits write a new function. That requires our own slot implementations calling the engine's primitives (draw, destroy, special summon...) - not started; find them by decompiling the shared slot functions.
4. Custom cards above 16383 play under a borrowed id; every id the engine hands us must be mapped back (`Card_GetRealIdForBorrowed`).
5. Test: Funky debug window (add cards by id/name to the opponent's hand) and the plugin log line "Custom card N borrows the effect of vanilla card M".

## 7. Names given in IDA so far

`YGO::Effects::` Get_EffectTableEntryForCard, EffectTable_SpellTrap / _MonsterEffect / _Kind2 / _Kind3_Head, Get_NumberOfCardsToDraw, CardID_WithNumberOfCardsToDraw, Effect_DrawCards_Resolve (0x14015F9C0, slot0 of draw-N spells) / _CanActivate (0x1400FAC40, slot2), Effect_RunSlot1Action_Driver (0x140156E70), Target_Raigeki_OpponentMonsters (0x140180F10), Target_DarkHole_AllMonsters (0x1401952D0), Build_TargetMask_Generic (0x1400E0060) + _FromSlot1_A/_B, Call_Slot2_Condition_DefaultTrue, Has_Kind3EffectEntry, Slot_ReturnConst2 (0x1400DE060), Slot_Delegate_ToSecondaryTable_FE560 (0x1400FE620), Get_SecondaryRow_* accessors, Slot0_DefaultMonsterResolve_1F94C0 (0x1401F94C0).
Others: `Is_CardInNamedArchetype` (+ `_Thunk`), `List_BinarySearchKonamiId`, `EffectCondition_CheckCardUsableAtTiming`.

## 8. Scripts (docs/effect-scripts, all read YuGiOh.exe with pefile; paths are the author's)

`effect_tables.py`/`effect_catalog.py` dump the four tables (-> effect_tables_raw.json), `find_id_tables.py` scans for id-keyed tables (-> id_tables.json), `effect_workbook.py` builds `effect_tables.xlsx`, `DecodeRelated.py` decodes tagdata.bin.

## 9. Parameterised reuse (the real route for modern cards) - found 2026-09-28, not decoded yet

Cloning a vanilla card only helps when a custom card's text is exactly a vanilla card's. Measured: **0 of the 4333 delta cards** have text identical to a vanilla card (normalised), and only 9 are >= 88% similar, none the same effect (the game has the old simple cards; the delta is modern, parameterised text). So the useful reuse is *same handler, different parameters*.

Seen so far:
* `Get_SecondaryRow_1401B0F40(id, k)` (0x1401B0F40) binary-searches **0x140B16220**: 1637 rows of 12 bytes (6 words: id + 5 params), several consecutive rows per card, k picks the row. It feeds generic target predicates such as `sub_1401B0FF0` (used by 494 kind-3 rows) -> `sub_14017C810` -> `sub_14017B600(effect, player, zone, flagsA, flagsB)`, a **generic card filter driven by bit flags** (Raigeki's slot1 is `sub_14017B600(..., 4114 = 0x1012, 0)`).
* This is the game's parameterised "which cards does this affect / find" mechanism - the same idea as the `TYPE:/ATTR:/LEVEL:/KIND:` filter vocabulary in taginfo. Decoding the flag bits (owner/side, zone, face-up, type/attribute/level bits) lets a custom card get its own filter row without new code.
* Next: decode the flag bits of `sub_14017B600` by correlating rows in 0x140B16220 with the texts of the cards they belong to (e.g. "Level 4 or lower Pyro"), then write an encoder (`filter -> row`) and extend EffectClone with `"filter"` rows; then the same for the other secondary tables (0x140AFD7E0, 0x140AF8180, ...).
* Candidate first families among the delta (from effects_backlog_grouped.xlsx): search-from-Deck (765 cards), draw (255), destroy (337), Special Summon from GY, burn (205).

## 10. First in-duel test of EffectClone (2026-09-28) - results and the impersonation fix

Six Normal Spells given a borrowed effect (`cards.effecttest.json`): three with plain ids (14981-14983), three with recycled ids above 16383 (16385, 16387, 16392).

| card | borrows | plain id | recycled id |
|---|---|---|---|
| A.I. Connect / Fish and Bids | Pot of Greed, draw 2 | works | works |
| A.I. Contact / Fish Sonar | Raigeki | works (correctly not playable until the opponent has monsters) | works |
| A.I. Meet You / Five Star Twilight | Dark Hole | settable only, never playable | same |

So the lookup hook and the borrowed-id mapping are proven. **Why Dark Hole failed:** its slot1 target predicate (`Target_DarkHole_AllMonsters`, 0x1401952D0) is a large per-id ladder shared by many cards - it first runs the flag filter, then switches on the effect record's own id and returns 0 for any id it does not know. Raigeki's slot1 is a plain constant-flag filter, so it did not care. First version only swapped the id during the *lookup*.
**Fix (built, CONFIRMED WORKING in-duel 2026-09-28, Dark Hole clones 14983/16392 now playable):** the row returned for a custom card now holds per-slot wrapper functions that swap the source card's id into the record while that slot runs, then restore it (impersonation), and remember which clone is active so the draw-count override still applies. Empty slots stay empty. Rows are cached per (custom id, source row) and never freed.
**Rule this teaches:** slot functions may key on `effect[0]`; anything in a handler that is id-keyed (ladders, secondary tables, parameter tables) sees the SOURCE card under a clone. A custom card's own parameters must therefore be applied by hooks that consult the active clone (as `draw` does), not by id.

## 11. The generic filter decoded (2026-09-28) - "destroy all X" family is row driven

**Row table 0x140B16220** (`Get_SecondaryRow_1401B0F40(id, k)`), 12 bytes: `u16 id, u16 param, u32 extraFlags, u32 flags`. Dump/decode with `docs/effect-scripts/filter_rows.py [ids]`.
Vanilla "Destroy all Warrior / Machine / Rock / Fish / Insect monsters" (4659, 4666, 4669, 4670, 4668...) all have slot0 = `Effect_RunSlot1Action_Driver` (0x140156E70) and slot1 = `Target_Generic_FromFilterRow` (0x1401B0FF0); they differ only by their row, so a custom card can reuse them with its own row.

`Filter_CardAtZoneMatchesFlags(effect, player, zone, flags, param)` (0x14017B600), flags:
| bits | meaning |
|---|---|
| 0-1 | side: 1 owner's, 2 opponent's, 3 either (+zone check), 0 unrestricted |
| 0x4 / 0x8 | zone occupied / empty |
| 0x70 | zone class (0x10 zone<7 [field-monster range], 0x20/0x30/0x40/0x50/0x60 other classes) |
| 0x80 / 0x100 | slot byte +2 must be 0 / non-zero (position-ish) |
| 0x400 | must be targetable (`sub_140023AF0`) |
| 0x1000, 0x2000 | visibility / "can be affected" checks (both used by every destroy-all row) |
| 0x40000 | not the effect's own card |
| 0x400000 | there must be a free monster zone (summon effects) |
| 0x70000000 | **match kind**, tested against `param`: 1 archetype (param<3000, an archetype code / special 7,10,28,209,329,380) or card id (param>=3000); 2 KIND_TABLE category (negative = not); **3 race** (`Get_EffectiveRace`, 1 Dragon 2 Zombie 6 Rock 7 Machine 8 Fish 10 Insect 15 Warrior); **4 attribute** (`Get_AttributeMask`, bit test); **5 level <= param**; **6 level >= param**; 7 special case #param (switch 1..25: face-down, position, can-be-Normal-Summoned, ...) |

`extraFlags` (row +4) is a second pass run only when the effect is not yet chained (`effect[7]==0`). Rows come out of the game as `0x30001014` (race), `0x50001414` (level<=), etc.

**Built:** `"effectClone": { "from": 4659, "filter": { "side": "any|own|opp", "race": 1 } }` - keys `archetype`, `card`, `kindCategory`, `race`, `attribute`, `level_max`, `level_min` (one only), or raw `flags`/`param`. `EffectClone` hooks `Get_SecondaryRow_1401B0F40` and, while a clone's slot function runs, returns a synthetic row for k=0. Test cards (cards.effecttest.json): 14969 (all Dragons), 14977 (opp Machines), 16406 (attribute 5), 16419 (own Level<=4). **Untested in-duel.**
Also named in IDA: `Get_EffectiveLevel` 0x14003A060, `Get_EffectiveRace` 0x14003B460, `Get_AttributeMask` 0x14003CAC0, `Is_FieldCardInArchetypeOrSpecial` 0x140047DD0, `Is_KindCategory` 0x140742C80, `Draw_CardsForPlayer` 0x1400A1B90, `Draw_ExecuteDraw` 0x1400A1C10.

## 12. Slot semantics, resolve state machines and the deck-search family (2026-09-28)

**Slots of an effect row (48 bytes, five function pointers)** as far as read:
| slot | role | examples |
|---|---|---|
| 0 | RESOLVE - a per-call state machine. The current step is a global (0x14349C13C / 0x14349C148+4); the function does one step and returns the next (128 start/check, 127/126 prompt, 125 pick, 124 chain/animation, 120-122 finish, 100 apply). | `Slot0_AddFromDeckToHand_StateMachine` 0x14016D5A0 (622 rows), `Slot0_Generic_TargetedEffect_StateMachine` 0x140163830 (402), `Slot0_AlterLifePoints` 0x14015E570 (481), `Effect_DrawCards_Resolve`, `Effect_RunSlot1Action_Driver` 0x140156E70 (destroy-all family), `Slot0_SelectFirstTargetAndApply` 0x140156D40 |
| 1 | TARGET predicate / mask | `Target_Generic_FromFilterRow` 0x1401B0FF0 (row table 0x140B16220, section 11) |
| 2 | CAN-ACTIVATE condition / cost check (0 fail, 1/2 ok) | `Slot2_Condition_DeckHasMatchingCard` 0x1400F99F0, `Slot2_Delegate_ToSecondaryTable_*`, `Slot_ReturnConst2`, `Cond_GenericCanActivate_Ladder` 0x1400F8690 |
| 3 | monster-effect wrapper (before / default / after hooks from table 0x140B27260, 348 rows) | `Slot3_MonsterEffect_WithHooks_qword_140B27260` 0x1401F9630 |
| 4 | player interaction: confirm prompt / pick from list / apply to targets | `Slot4_ActivationPrompt_Driver` 0x14008C9D0 (+ Text100/101/111/124/281 wrappers), `Slot4_SelectFromDeckList_Driver` 0x140092910, `Slot4_ApplyToSlot1Targets` 0x140095330 |

**Deck filter table 0x140AD2A80** (`YGO_Effects_DeckFilterTable`, 5924 rows, sorted by id, 24 bytes): `u16 id, s16 param, u32 -1, u64 scan function, u32 flags, u32 0`. `Get_DeckFilterIndexForEffect` (0x1400C01B0, ~58 callers) binary-searches it; `Deck_CollectMatchingCards` (0x1400C0290) / `Deck_CountMatchingCards` (0x1400C0320) call the row's scan function, which fills the match list (0x14349C5D8 u32 cards, 0x14349CA88 u16 flags, count = word at 0x14349C5CA). Scan functions are tiny wrappers over generic scanners: `0x14052B8C0` (Deck, zone 15), `0x14052CD10` (zone 16), `0x14052C8D0`; the wrapper only fixes a behaviour-flag constant (0, 4, 8, 0x40000...). The row **flags** are the filter:
`0x2` monster, `0x4` spell, `0x8` trap (none = any), `0x80..0x380` spell/trap subtype (property+1 at bits 7-9), `0x400` normal-monster style check, `0x800` special-summon check, `0x1000` excluded set, `0x2000` ATK <= 1500, `0x4000`/`0x8000` kind-table columns, **`0x3C0000` level (4 bits, >>18) with `0x400000` = ">=" / `0x800000` = "<=" / neither "=="**, **`0x7000000` attribute (1 Light..7 Divine, >>24)**, **`0xF8000000` race (>>27)**. Check: Reinforcement of the Army = `0x78900002` = monster | Level<=4 | Warrior(15).
`filter_rows.py` dumps section-11 rows, `deck_filter_table.py` dumps this table.

**Built, CONFIRMED in-duel 2026-09-28 (plain id 14992 and recycled id 16427 both listed and added only matching cards):** `"effectClone": { "from": 5328, "deck": { "type": "monster", "race": "Dragon", "level": 4, "levelOp": "le" } }` - keys `type`, `race`, `attribute`, `level`+`levelOp` (le/ge/eq), `atk1500`, raw `flags`. `EffectClone` hooks Collect/Count and, while the clone's slot function is running, runs scan function 0x14052F2A0 on a synthetic row. Sources to clone: any "search the Deck" spell whose slot0 is the AddFromDeckToHand machine and whose slot2 is `0x1400FB620` (Reinforcement of the Army 5328, Terraforming 5537, Fusion Sage 4872...). Test cards: 14992 (Level<=4 Dragon), 16427 (DARK Spellcaster).
Caveat: the AddFromDeckToHand machine also switches on the effect id for special cases (see its ladder); clones of plain rows like 5328 hit none of them.

**Not table-driven (needs per-card code):** `Get_DeckFilterParamForEffect_Ladder` (0x14008CE70, 0x5A96 bytes), `Cond_GenericCanActivate_Ladder`, `Slot0_EmitResultParams_ByCardId` and most `Slot0` machines keep card-id ladders; parameters they read for a custom card must come from a hook that consults the active clone (as `draw`, `filter` and `deck` do).

## 13. Revive (Monster Reborn family) and the language (2026-09-28)

The candidate list of "target 1 monster in a Graveyard" effects comes from the SAME list filter table as the deck searches (section 12): the row's scan function just reads the Graveyard (`ScanGeneric_Grave` 0x14052CD10, zone 16) instead of the Deck. Scan wrappers: `Scan_Grave_Own` 0x14052F4D0, `Scan_Grave_Own_SummonableOnly` 0x14052EC80 (behaviour flag 8 = only cards that can be Special Summoned), `Scan_Grave_Opponent` 0x14052EE00, `Scan_Grave_Both_SummonableOnly` 0x140531D70 (Monster Reborn). Row flag bits are identical (level/race/attribute, per-zone lookups inside `Scan_CardMatchesRowFlags` 0x14052B540).
Monster Reborn slot0 = `Slot0_SpecialSummonTarget_Driver` 0x140169590 (541 rows, CONFIRMED in-duel with own-GY and either-GY filters) -> `Action_QueueTargetedZoneEffect` 0x140156470; slot2 `Slot2_Condition_Generic_And_FA680`; slot4 `Slot4_SelectFromList_Driver` 0x140092910 (renamed from ...DeckList: it lists whatever the scan collected).
`effectClone.deck.scan` = `deck | grave | graveSummon | opponentGrave | bothGravesSummon`. **Untested in-duel.**
The card scripts/compiler for all of this: docs/EffectLanguage.md. Test files: `Desktop\New folder\tests\1..5_*.json` + README.txt.
More slot0 machines named 2026-09-28: `Slot0_SelectFromListAndMove_StateMachine` 0x140178460 (Foolish Burial family: collect -> choose -> move to GY), `Slot0_ApplyToEachSelectedTarget_StateMachine` 0x14016E3B0 (302 rows), `Cond_ListHasEnoughMatches` 0x1400FA680 (slot2 candidate-count rule: 1 by default, 2 for 78 listed ids, specials 3-5), `Get_SelectedListCard` 0x14005E810, `Get_ProxyCardId` 0x1400DB180 (name-proxy ids).

## 14. Life point effects (2026-09-28)

`Slot0_AlterLifePoints` (0x14015E570, 481 rows: Red Medicine, Hinotama...) calls `LP_ApplyChangeFromParamTable` (0x1400A1A40), which asks `sub_14015AC90` (0x14015AC90, a 99 KB ladder + the table `Effects_Burner` at 0x140B15B30) to fill `int[2]` - the life point change per absolute player (positive = gain, negative = damage) - and applies gains/damage to both players. The table is 4-byte entries `s16 id, s16 amount` (Red Medicine 4345 = +500, Hinotama 4350 = -500).
`effectClone.lp = { "gain": N, "damage": N, "selfDamage": N, "opponentGain": N }` hooks `sub_14015AC90` and, inside the clone's slot function, overwrites the two numbers (controller / opponent). Sources: Red Medicine 4345, Hinotama 4350. Language: `gain_lp(N)`, `burn(N)`. Test file: `tests\6_life_points.json`. **Untested in-duel.** (Results of tests 1-4 - revive, search, destroy-all, draw - all worked in-duel.)

## 15. Send from the Deck to the Graveyard (2026-09-28)

`Slot0_SelectFromListAndMove_StateMachine` (0x140178460, Foolish Burial 5236) collects candidates with `Deck_CollectMatchingCards`, so the same `deck` filter override applies. Language: `send_to_grave(deck, selector)`. Test file `tests\7_send_to_grave.json`. **Untested in-duel.**

## 16. Targeted destroy and chaining (2026-09-28)

**"Target 1 X; destroy it"** (Remove Trap 4838, Shield Crush 6003, Anti-Fusion Device 7011, Night Beam, Typhoon...): slot0 `Slot0_SelectFirstTargetAndApply` 0x140156D40, slot1 `Target_Generic_FromFilterRow` (row driven), slot4 prompt. So the existing `filter` override works; `"target": true` (adds the targetable bit 0x400: base 0x1414 for monsters) and `"kind": "spelltrap"` (base 0x41440, MST's flags) select the "choose one card" form. Vanilla with hard-coded flags instead of a row (Mystical Space Typhoon 4909 slot1 = constant `0x41440`) would need a hook on `Filter_CardAtZoneMatchesFlags` limited to slot1 calls (not needed yet). Language: `destroy(target, [side,] [monster|spell|trap where ...])`. Test file `tests\8_targeted_destroy.json`.

**Chaining, stage 1 (built):** `"before": [ step, ... ]` on an effectClone. Draw and life point effects finish in one call of their slot 0, so `SlotThunk<0>` runs the "before" steps back to back (each with its own clone parameters and its source's slot 0) and then the card's own step: on every call if the own step is immediate too (draw/LP), otherwise only on the first call of the resolution (engine step variable 0x14349C13C == 128), since a search/revive machine is called again for each of its later steps. Language: `draw(2) then gain_lp(1000)`, `gain_lp(500) then search(deck, ...)`. Rule enforced by the compiler and the loader: only draw / gain_lp / burn can come before another action.
**Stage 2 (not built):** an interactive action followed by another action (`search(...) then draw(1)`). It needs a per-resolution step counter and a rewind of the step variable when a machine finishes (its final call, step 100, returns 0); the exact rewind rules of each machine have to be read from IDA first (docs section 12: 128/127/126/125/124/122/121/120/100). Test file `tests\9_chain.json` covers stage 1: CONFIRMED in-duel 2026-09-28 (draw+LP, draw+burn, LP+search all worked). Test 8 (targeted destroy) also worked.

## 17. Name and archetype tests in list rows (2026-09-28, built, untested)

The candidate check `sub_14052B440` (used by every Deck/Graveyard scan) tests the row's `param` (the s16 at row +2) against each card: `param >= 3000` = the card is named like game card id `param`; `1..2999` = the card belongs to archetype `param` (`Is_CardInNamedArchetype`; custom archetypes >= 419 work through the Cards plugin's hook); `param < 0` = the card must NOT match; `-1` = same name as the effect's own card. That is how "Add 1 \"Dark World\" monster" and "Add 1 \"Polymerization\"" rows are made, so `effectClone.deck` takes `"archetype": <code>`, `"notArchetype": <code>` and `"card": <konami id>`. Language: `search(deck, monster where archetype = "Dark World")`, `search(deck, name = 4867)`. This covers the majority of modern "add 1 X from your Deck" texts.
`Desktop\New folder\Attach-Effects.py` reads a full Cards.json, recognises Normal Spells whose text is exactly one supported effect (optionally plus "You can only activate 1 ... per turn", which is NOT enforced yet) and writes `effectClone` + `effectScript` for them. Run it after every Delta.ps1. Among the 352 Normal Spells of the delta only a handful are that plain; most modern cards are compound, which is why the language has to grow (conditions, costs, chains, once-per-turn) before large numbers of cards can be covered.

## 19. Monster effects: the same clones, sourced from vanilla MONSTERS (2026-09-28, built by design, untested)

Nothing in EffectClone is spell specific: `Get_EffectTableEntryForCard` looks the card up in four tables (section 1) and the wrapped row keeps the source's `Extra` words, so a custom monster that names a vanilla MONSTER as `from` gets that monster's trigger, its slots and, with the `deck` / `filter` / `lp` / `draw` parameters, its own numbers. **Correction (2026-09-28 night): the trigger is NOT in the row.** `Extra[0]` is not the trigger: table T2 with `Extra[0]=0` holds FLIP monsters (133) and Normal Summon monsters (181) alike, and so on for every value. Its low nibble is a once-per-turn class read by `Timing_Check_Class*` (5/9 and 6/10 test the per-turn use counters 1001 / 1002 by card id - this is how "You can only use this effect once per turn" is enforced; 1 compares the counter with a limit) and the other bits are unknown. Which event offers which card is decided by the dispatcher that builds the temporary effect record (its word +6 event code, `a1[4]` subtype) and by per-id ladders (slot 2 `Cond_GenericCanActivate_Ladder`, `sub_140108910` for spell speed/chain rules). A clone gets the SOURCE card's trigger only as far as those checks run inside slot calls (impersonation); the first duel test of `tests\10_monster_effects.json` decides whether that is enough. So sources are chosen by the trigger written in their CARD TEXT (`build_effect_reference.py` classifies "FLIP:", "When this card is Normal Summoned:" ... and writes `trigger_sources.json`). The old table below (Extra[0] per trigger) is kept for the slot layouts only; ignore its "Extra[0]" column as a trigger.

Slot layouts by handler x table x Extra[0] (Extra[0] is NOT the trigger, see above):
| trigger text | table, Extra[0] | clean sources (slot layout) |
|---|---|---|
| "When this card is Normal Summoned" | T2, 0 | search Poki Draco 9648; revive Guiding Light 11127; send-to-GY Armageddon Knight 7423 |
| "If this card is Normal or Special Summoned" | T2, 21 | search Lady Debug 13522; send-to-GY Darklord Ukoback 8384 |
| "If this card is Summoned" | T2, 5 | search Cyber Petit Angel 6848; send-to-GY Satellarknight Unukalhai 11231; burn Gravekeeper's Curse 5510 |
| "FLIP:" | T2, 16 | search Gishki Ariel 9162; destroy target Green Turtle Summoner 11145 (row driven); destroy all Magnetic Mosquito 7439; burn Poison Mummy 5413; draw Skelengel 4546 |
| "destroyed by battle and sent to the GY" | T4, 0 | search Birdface 5021; revive Evoltile Gephyro 9734; draw Blizzed 7527 |
| "sent to the GY" / left the field | T4, 5 | search Archfiend Heiress 10632; revive Superheavy Samurai Drum 11950 |
| "Once per turn: you can" (ignition) | T1, 1 | search Aquaactress Tetra 11916; revive Coach Soldier Wolfbark 10444 |
Source hygiene: the resolve machines have per-id special cases (the ladders in `Slot0_AddFromDeckToHand_StateMachine` etc.); pick a source whose id is not in them (Sangan 4054 and Witch of the Black Forest 4580 are, do not use them). Everything a clone needs from the source is its table, Extra[0] and slots.
Open questions for the first duel test (`tests\10_monster_effects.json`): (1) does the engine's event dispatch (summon / flip / battle-destroy) find a custom monster's trigger through the wrapped row, or does some other per-id registry decide which cards to ask? (2) does the timing class function (`Timing_Check_Class*`) behave for a custom id? If a trigger never offers, look at what enumerates the field's cards on a summon event (xrefs of `Check_CanActivate_Driver` 0x1400E0C30). Read so far: the OFFER path `Chain_TryOfferEffect_ForCardRef` (0x1400AB050) builds a temporary effect record from a card reference (id = the card's id) and calls `Check_CanActivate_Driver` - so any card on the field is asked through the same table lookup, which is the hook point; nothing found that restricts offers to a fixed id list (100+ callers of the driver remain unread).
Text coverage measured on the delta: only ~35 monster texts are exactly "trigger: one supported action" (20 "Normal or Special Summoned: add..."), so this is a proof of the mechanism rather than bulk coverage; the language needs conditions/costs/once-per-turn for more.

**Getter names corrected in IDA (2026-09-28):** `Get_EffectiveDefFromFullCardProps` (0x14081A5B0) is really the ATK (row flag 0x2000 = "ATK <= 1500", 29/30 texts agree; also Deck Devastation Virus), renamed `Get_EffectiveAtkFromFullCardProps`; `Get_RawDefFromFullCardProps` (0x14081A5D0) is the ATTRIBUTE (confirmed by the duel tests), renamed `Get_AttributeFromFullCardProps`; +0x44 = base ATK (`_0x44`), +0x54 = base DEF (`_0x54`). DEF-limited searches (Witch of the Black Forest) use a different scan, `0x140531630` (`ScanGeneric_Deck` + callback `0x14052E3C0` with the threshold in row word +4), not decoded. Language: `atk <= 1500` is supported (deck.atk1500).

## 18. Win conditions (Exodia, Final Countdown) - looked at, not built

They are not table parameters. Exodia's pieces are not in the effect tables at all, and Final Countdown's slot0 (`0x1404A8450`) only records a marker event - so the win is decided elsewhere, by card id, in the duel loop (turn/phase end checks). A custom "you win" card therefore needs a hook at that check (and the game's declare-winner routine, not yet located), not an effectClone. Parameterising an existing one (e.g. Final Countdown's 20 turns) needs the same hook to read the clone's number.

## 20. Once per turn = the row's use-limit class (2026-09-28 night, built, untested)

The low nibble of a row's first Extra word (`Extra[0] & 0xF`) is a USE-LIMIT class, not a trigger. Read from IDA:
* Offering an effect: `Check_CanActivate_Driver` (0x1400E0C30) runs `Timing_Check_Class*` for the class. Class 5/9 (`0x1400F7A20`): allowed only while the player has NO marker (key 1001, value = the effect's card id) in the per-player flag list (zone 13; lookup `sub_140052670(player, 13, key, value)`); class 6/10 does the same with key 1002 (a second, independent limit for cards with two effects); class 1 counts a marker kept on the card's own zone (per copy on the field, limit from `sub_1401F64E0`); class 14 checks a zone flag.
* Activating: `sub_1400E0DF0` (after the cost slot, Slot[3]) calls `sub_1400E0E50`, which switches on the same class and registers the marker: class 5 -> `0x1401F8770` (event 269, key 1001, `*a1` = card id), 6 -> `0x1401F87C0` (key 1002), 1/2 -> `0x1401F8390` / `0x1401F8420` (key on the zone), 9/10 -> `0x1401F88B0` / `0x1401F8900` (event 1293). Markers expire by themselves at the end of the turn (record type nibble < 6 in the list).
So a wrapped row whose class is 5 IS "You can only use this effect once per turn" per card name, with no code of our own. `EffectClone` sets the nibble to 5 for `"oncePerTurn": true` and CLEARS classes 1, 5, 6, 9, 10 a source row happens to carry, so a clone only has the limit its script asks for. 282 of the game's described cards have class 5/9 (Sangan among them), so the library shows `once_per_turn;` for them. Language: `once_per_turn;` after the action. Test file `tests\11_once_per_turn.json`. Names in IDA: `Check_Activation_Cost_And_Register`, `Register_UseLimit_Marker`.

## 21. Monster clones did not work on first test - what is known and what was changed (2026-09-28 night)

First duel test of `tests\10_monster_effects.json`: none of the 8 monsters worked; one could be "activated" from the hand and then did nothing. Read since:
* `Get_EffectTableEntryForCard` picks the table from the effect record's **word4** (0 Spell/Trap, 1 MonsterEffect, 2 Kind2 (Spell/Trap if word3 == 40), 3 Kind3), so the CALLER decides which table a card is asked in; the trigger is not id data. `Chain_TryOfferEffect_ForCardRef` (0x1400AB050) builds the record from a card reference for the offer path; a card offered from the hand as if it were a Spell (the observed "activate from hand") means some lookup returned a row for a monster in the spell-style table or the offer bypassed the trigger check.
* The engine compares row slot pointers BY ADDRESS in places (`sub_140100410`, `sub_14020C730`, `sub_1400E1160`: "is slot X this exact function"), so wrapping every slot in our thunks can hide a row's identity. `WrappedRow` now hands slot 3 over UNWRAPPED when it is one of the generic monster-effect functions (0x1401F94C0, 0x1401F9470, 0x1401F9630, 0x1401F9920, 0x1402007F0) - they do not read the card id themselves.
* Diagnostics added (log level 69): every distinct kind of lookup for a custom id ("Effect lookup for custom id N: controller, zone/index, type, subtype") and the first three calls of every slot function of a clone with the step variable and result. A trigger that the engine never asks about shows no lookup line; one it asks about but that fails shows the lookup and the slot calls. The log is written when the game exits.

## 22. Monster triggers: the event evaluator, and the debug trace

Findings from the first monster-clone tests (Abyssrhine cloning Armageddon Knight 7423):

- A summon trigger is NOT found through the effect tables. `Eval_EventTriggersCardEffect_ByCardId` (0x1400E26A0, arg = event type) takes the id of the card the current event (`0x14349C560`) is about and tests it against per-event id lists (`word_140AD1FA8`, `word_140AF7510`, `word_140AF7D70`, `word_140AF76F0` ... and a big switch). Only on a match does it call `Offer_EffectByCardRef` (0x1400AAE50) with a reference whose low word is that id, which is when the table row is looked up (word3 7 / word4 1 for a monster). A custom id is in no list, so the row was never asked for.
- EffectClone hooks the evaluator (`Hook_EventEvaluator`): while it runs, the event's card shows the source id in EVERY place it is held - each 16-bit word of the event record's first 0x40 bytes equal to the custom id (the id is at word 7, offset 0xE, on a summon) and the result of `Get_CardIdAtZone` 0x140047FF0 - and `Offer_EffectByCardRef` gets the custom id back. Swapping only the zone lookup was not enough: the evaluator also compares against the record's copy of the id and refused the offer (0 offers) until that word was swapped too. Confirmed with Abyssrhine (15000 borrowing Armageddon Knight 7423): offer made, slot 2 true, slot 0 stepped 128 to 125. Also wrapped so they see the source id: `Get_EffectSpellSpeed_ByEffectId`, `Check_CanChainAtSpeed_ByEffectId`, `Ladder_IsOptionalTrigger_ByEffectId`, `Ladder_ChainSpeedOverride_ByEffectId`, `Check_EffectSourceIsMonsterId`, `Check_IsOwnMainPhaseSummonWindow`, `Check_IsNegateSummonListCard` (`IdLadder`).
- The Kind 3 table (word4 3) is asked for a monster on the field (`Has_Kind3EffectEntry`, ignition-style effects); a trigger monster correctly has no row there.

**Debug trace:** create an empty `trace.txt` in the plugin's `Effects` folder (`Binaries\Debug\Plugins\Effects`). The plugin then logs, once per card and caller, the call stack (10 return addresses, exe addresses) of every custom-card row lookup and of every monster event lookup (word3 7 / word4 1), the source row (or "NO row") found for each custom lookup, and the type class table words of the custom card and its source. Delete the file to turn it off. Reading a stack: compare the vanilla card's chain with the custom card's and find where the custom one drops out.

## 23. Custom ids start at 15300 (ghost ids 14969 - 15234)

The game ships effect code and id lists for cards it has no card data for: 198 ids between 14969 and 15234 have rows in the four effect tables (and `word_140BF8BC0`, a "has a hand effect" list, holds 14996-14999 ...). Custom cards used to be numbered from 14969, so each landed on a ghost: a custom Abyss Shark (14996) was offered "activate from the hand" because 14996 is in the hand-effect list. Custom ids now start at **15300** (`kFirstExtraCardId` in Yu-Gi-Oh-Cards/Card.h, Yu-Gi-Oh-Effects, WolfEx `CardsPanel.MinId`, `-StartId` of Delta.ps1 / Cards.ps1); the cap stays 19999 (the save's 20,000-byte card table), i.e. 4,700 custom cards. Ids above 16383 borrow a vanilla id per duel as before; the save holds real ids only.

`card_ids.json` (the "never renumber" registry) was renumbered once on 2026-09-28: the 331 cards that had ids below 15300 moved to the free ids from 19302 up (mapping in `id_remap_15300.json`, backup `card_ids.pre15300.bak`). Existing saves and decks that hold custom ids must be deleted or re-made (the -ex save was deleted). Safety net in EffectClone: a custom id in 15300-16383 with no clone gets no effect (`IsPlainCustomId`).

## 24. The event-queue pump (2026-09-29 IDA pass) — what was and wasn't found

Traced `sub_14003E530`, the duel's per-tick event-queue pump: it reads `stru_14349C560.field_26` ("event kind", a 4-byte field) and dispatches to one of six handlers. Confirmed by reading the disassembly at each call site (not just the decompiler's guess):

| kind | handler | what it passes to the evaluator |
|---|---|---|
| 1 | `sub_1400E9D50` | `Eval_EventTriggersCardEffect_ByCardId(7)` — the summon trigger, confirmed working |
| 2 | `sub_1400EBE10` | reaches `Eval_EventTriggersCardEffect_ByCardId(8)` from its own case 0x16, after a Level<=2-tribute special case or `sub_140082E70` failing; real-world trigger unconfirmed |
| 3 | `sub_1400EC290` | tail-calls `sub_1400E9D50` directly outside its own steps 1/10-14, i.e. also produces event type 7 |
| 4 | `sub_1400EC530` | `Eval_EventTriggersCardEffect_ByCardId(9)` — also what `Chain_AddEffectLink` (effect activation) always sets `field_26` to |
| 5 | `sub_1400F0EE0` | does NOT call the evaluator; it's a huge id-keyed table computing chain-resolution amounts (damage/LP math per vanilla card id), falls through to kind 4 by default |
| 6 | `sub_1400F1F40` | not read this pass |

**Important consequence:** `Hook_EventEvaluator` in EffectClone.cpp hooks `Eval_EventTriggersCardEffect_ByCardId` (0x1400E26A0) itself via Detours, not a specific call site. So the id-swap fix already covers event types 7, 8 and 9 (and any other caller of that one function) automatically — no extra hook was needed for those.

**What was NOT found this pass, despite a real attempt:** the producer that enqueues a FLIP-summon or "destroyed by battle" trigger. `sub_1401E6B50` (called from the pump when `stru_14349C560.field_16` is set — a "destroy / replacement effect check", name unconfirmed) does set `WORD3(MEMORY[0x14349C580])=1`, which re-enters the pump as if kind==1 next tick — i.e. it plausibly re-uses event type 7 for post-destroy re-evaluation, but this was read from the decompiler only and NOT confirmed against a real duel. Found "FLIPSUMMON" / "Flip Summoned #" strings at 0x140A50748/0x140A50760, but their only xref (`sub_140767BF0`) looks like a generic UI/flag-name-dump function, not the trigger site.

**Recommendation for next time:** don't keep static-tracing this — the evaluator hook is global, so the fastest way to know whether FLIP/destroyed-by-battle already work is to test them in a duel with `trace.txt` on and read the `Event N for custom card …` line (or its absence) in the log, the same way summon (event 7) was confirmed on 2026-09-28.

## 25. Extra-deck summon CRITERIA (Xyz/Synchro material matching) — good news, confirmed 2026-09-29

Distinct from monster trigger EFFECTS (sections 19-24): this is whether a custom Fusion/Synchro/Xyz/Link monster can be summoned via its normal method at all.

- **Xyz**: `Get_XyzSummonRequirementCode_ByCardId` (0x14003EB90) binary-searches `XyzSummonRequirements` (0x140ACF2E0, 219 entries, `{u16 id, u16 param, u16 requirementCode}`) by Konami id and returns the matching row's code, or **2 for any id not in the table** - the generic "N monsters whose Level matches this card's Rank" rule. Called from `Can_XyzSummonWithFieldMaterials` (0x140040360, one of 19 callers of the lookup) which then walks the 14 field zone-pairs checking materials. Since the table only lists ~219 vanilla cards with a card-specific material rule (named materials, "2 Tuners", etc.), **every custom card id gets code 2 automatically** - a custom Xyz monster with a plain "2+ Level N monsters" requirement should already be summonable with no extra work.
- **Synchro**: the same pattern, `Get_SynchroSummonRequirementCode_ByCardId` (0x14007C9A0) over `SynchroSummonRequirements` (0x140AD0BA0, 194 entries), same default-2 fallback for any unlisted id.
- **Fusion**: NOT the same - Fusion has no generic "sum of Levels" rule the engine can fall back to; it always needs an explicit material list (this is why `Yu-Gi-Oh-Effects/Fusion.cpp` exists as a separate recipe system - see `ygo-effects-fusion-plan` memory). Ritual/Link not checked this pass.
- **A separate, narrower gate**: `EffectCondition_CheckCardUsableAtTiming` (0x1400679F0) is a single ~150-id hardcoded `if/else` ladder for CARD-SPECIFIC special-summon conditions and activation-timing checks (e.g. "cannot be Special Summoned except by its own effect"), not material matching. `Yu-Gi-Oh-Effects/EffectDispatch.cpp` already detours it as a passthrough OBSERVER only (logs every call for a custom id, changes nothing) - a custom card always falls through this ladder, which is harmless (no special restriction applies) rather than blocking. Confirmed: this hook currently sees the card's BORROWED id for anything above 0x3FFF, not its real id (the reverse lookup `g_BorrowedToHigh` in Yu-Gi-Oh-Cards is not exposed cross-DLL yet - see `ygo-duel-id-remap-plan` memory) - a real problem if/when this hook needs to react rather than just log, but not for cards below 0x3FFF (15300-16383, most of the current delta after the section-23 renumber).

**Net effect:** Xyz/Synchro summoning mechanics for a plain custom monster are very likely already working (same conclusion `Yu-Gi-Oh-GUI Plugins/Yu-Gi-Oh-Funky/DuelTest.cpp`'s comment already guessed at, now confirmed from the tables rather than assumed) - what's still missing is (a) Fusion recipes for anything not in `Fusion.cpp`'s hand-built list, and (b) "when this card is Xyz/Synchro Summoned: ..." trigger EFFECTS, which is the section 19-24 work (EffectClone's monster-trigger hook) and should already apply the same way it does to Normal/Special Summon, since nothing found this pass suggests event type 7 distinguishes the summon method - only the vanilla card's own slot-2 condition text does that. Untested empirically.

## 26. FLIP-summon trigger hunt — two more dead ends (2026-09-29, second pass)

Continuing section 24's search with named `EventRecord`/`ResolutionStepState` fields in hand:

- `sub_140767BF0` (owner of the "FLIPSUMMON"/"Flip Summoned #" strings) was fully decompiled, not just glanced at: it is a ~85-entry debug/UI stat-name lookup table (`DAMAGEEFX`, `NORMALSUMMON`, `FLIPSUMMON`, `FUSION`, `SYNC`, `XYZ`, `PENDULUM`, `LINK`, ...), returning a display name for an internal stat index. Confirmed dead end, not a trigger site.
- The save file's own `SaveStat` enum (`File Type Libraries/savegame/savegame.cs`, ~28 values) tracks `SummonsNormal/Tribute/Ritual/Fusion/Xyz/Synchro/Pendulum` but has **no Flip entry at all** and is a different, coarser counter system from the in-memory one above. No lead there either.

**Net result:** the FLIP-summon (and destroyed-by-battle) producer is still not found. Static tracing of this specific question has now hit two independent dead ends in a row across two sessions; further progress likely needs either (a) an empirical duel test with `trace.txt` on to see if it already works via a path not yet identified (recommended next step - cheaper than more ASM reading), or (b) tracing the face-down/face-up POSITION BIT write site in the per-zone card record (not yet located with a confirmed offset - the `0x20` bit seen tested in several places looks like an unrelated "already used this turn" flag, not face-up state; do not assume it is without checking).

## 27. Data renamed 2026-09-29 (in addition to sections 22-25's function renames)

| was | now | confidence |
|---|---|---|
| `stru_717` type applied to `0x14349C560` | `EventRecord` struct, fields `CardRef` (0xC, id at +2) and `Kind` (0x26) named | confirmed by usage |
| unnamed/untyped `0x14349C580` | `ResolutionStepState` struct, fields `HandlerActive` (0x2), `HandlerFamily` (0x6), `Step` (0x8) | confirmed by usage across 5 handler functions |
| `word_140BF7822/24/2A` | `YGO::CARDS::TypeCategoryTable_A/B/C` | A confirmed (10=Synchro,12=Xyz,18=Link); B/C codes not decoded |
| `word_140AF7DC0` | `YGO::Effects::EventEval_IdList_Event7_A` | confirmed (`cmp r13d,7`) |
| `word_140AD1FA8` | `YGO::Effects::EventEval_IdList_Event8` | confirmed (`cmp r13d,8`) |
| `word_140AD01E8` | `YGO::Effects::EventEval_IdList_Event9` | confirmed (`cmp r13d,9`) |
| `word_140AF7B60` | `YGO::Effects::EventEval_IdList_Event7or9` | confirmed (shared guard `(r13d-7)&~2==0`) |
| `word_140AF7670/78B0/7510/7D70/76F0` | `YGO::Effects::EventEval_IdList_1/3/5/6/7` | NOT mapped to an event type yet - numbered placeholders only |
| `YGO::Effects::XYZSummonRequirements` (old cap) | `YGO::Effects::XyzSummonRequirements` (renamed for casing) + `Get_XyzSummonRequirementCode_ByCardId` (0x14003EB90) | confirmed, see section 25 |
| `stru_140AD0BA0` | `YGO::Effects::SynchroSummonRequirements` + `Get_SynchroSummonRequirementCode_ByCardId` (0x14007C9A0) | confirmed, see section 25 |
| `sub_140040360` | `YGO::DUEL::Can_XyzSummonWithFieldMaterials` | confirmed |

`stru_143497C40` was found to already be a large (14,640-byte) partially-named type (`Duel::PlayerState`) from earlier, unrelated work - left untouched rather than risk a wrong redeclaration; not relevant to effects work per the user, deliberately not pursued further.

**IDA session:** `YuGiOh.exe.i64`, all of the above saved. Last working session id `034b41f3` (session ids change on reconnect; use `idb_open` fresh each time, the file itself is what persists).

## 28. `Duel::PlayerState` mapped (2026-09-30) — the live duel-state struct, corrected from section 27

Section 27 said `stru_143497C40` (`Duel::PlayerState`, 14640 bytes) was "not relevant to effects" and left it alone. That was wrong to generalize from - the user asked to keep mapping it, and it turned out to hold exactly the card-zone data the deck/grave/hand filter system needs. One player's block is `0xD94` (3476) bytes; player 2 is at `+0xD94` (no 3rd/4th player block found - the struct is sized for exactly 2 at this layer, contrary to an earlier guess).

Confirmed (all traced to real code, not inferred from field names) - offsets relative to a player's block start:
| offset | field | zone# | notes |
|---|---|---|---|
| 0x0C | `PlayerOne` (`Duel::PlayerState::Player`) | - | card counts: `iNumberOfCardsInHand/Deck/Graveyard/ExtraPile/DiscardPile` - already named pre-session |
| 0x4C | `MonsterZone[5]` (`Duel::InPlay_Card[5]`) | - | Main Monster Zones only; the 2 Extra Monster Zones are NOT in this array (shared-zone special-casing elsewhere, e.g. `sub_1400435E0`/`sub_140043670` called from `Filter_CardAtZoneMatchesFlags`'s zone-class switch) |
| 0xF0 | `SpellTrapZone[5]` | - | |
| 0x168 | `FieldSpell` (single `Duel::InPlay_Card`) | - | |
| 0x19C | `HandCards[120]` (global `HandCards_P1`) | 13 | packed dword, same bit layout as `Duel::InPlay_Card.CARDID` (`id&0x3FFF`, `0x4000` flag, `(val>>23)` 2-bit field) |
| 0x37C | `DeckCards[120]` (`DeckCards_P1`) | 15 | |
| 0x55C | `ExtraPileCards[150]` (`ExtraPileCards_P1`) | 14 | "Extra Pile" = Extra Deck |
| 0x7B4 | `GraveyardCards[149]` (`GraveyardCards_P1`) | 16 | |
| 0xA0C | `DiscardPileCards[226]` (`DiscardPileCards_P1`) | 17 | "Discard Pile" = banished |

Found by decompiling the zone scanners the deck-search filter already calls (`docs/EffectSystem.md` section 12): `YGO::Effects::ScanGeneric_Deck` (0x14052B8C0), `ScanGeneric_Grave` (0x14052CD10), and their siblings `sub_14052C090` (zone 14), `sub_14052C8D0` (zone 13), `sub_14052D410` (zone 17) - each reads its own `iNumberOfCardsInX` count and its own packed-dword array, so the zone numbers and array bounds above are read directly off real code, not guessed. IDA globals created at the player-1 addresses (`make_data`, exact addresses and sizes in the tool's summary comment on `stru_143497C40` itself - read that comment first before re-deriving any of this). Player 2 = same field offsets `+0xD94`.

**Still unmapped:** the 0xC4-0xF0 gap (44 bytes, after MonsterZone before SpellTrapZone) and the 0x180-0x19C gap (before HandCards) - likely Extra Monster Zone / Pendulum Zone / a "field spell negated" flag (`field_180 & 0x20` is tested alongside `POne_FieldSpell` checks in several functions, e.g. `sub_1402344E0`), not yet confirmed.

**`Duel::InPlay_Card.Pos`, typed `Duel::CardPosition`, is the real battle-position bitmask** (confirmed via 3 dedicated accessor functions - `sub_140013C00` EQ, `sub_140013C30` GE, `sub_140013C60` single-bit test - all reading the same dword). Enum filled in this session: `POS_FACEDOWN_ATTACK=2`, `POS_FACEUP_DEFENSE=4`, `POS_FACEDOWN_DEFENSE=8`. **Value `1` was already named `FDD` by earlier (unknown-session) work, NOT overwritten** - so do not assume `1 = POS_FACEUP_ATTACK` (the usual EDOPro-style convention) until confirmed empirically; it may be Konami's own bit order.

**Design finding for a `position` selector (blocked on the above):** the generic row-filter (`Filter_CardAtZoneMatchesFlags`, section 11) does NOT have an existing "match kind" case for battle position in its `0x70000000`/case-1..25 switch - case 24 (the closest-looking one) is an unrelated sanity check (`Pos != 0`), not a position filter. So `position = attack` etc. can't be added the same cheap way `race`/`level`/`attribute` were (as a new `param`/`flags` combination on the existing row). It needs a genuinely new check: hook `Filter_CardAtZoneMatchesFlags` (or add a post-filter in `EffectClone`'s target predicate) to read the candidate's `Duel::InPlay_Card.Pos` directly against a value stored on the active `Clone`. Not built yet - correct next step once bit `1`'s meaning is confirmed, to avoid shipping an inverted "attack"/"defense" selector.

**Also noted, not chased further this session:** section 26 speculated the FLIP-summon trigger site might be near "the face-down/face-up position bit write site (not yet located)". Now that `Pos`'s read site is fully identified, the write site (whatever sets `Duel::InPlay_Card.Pos` on a Flip Summon) is a promising next lead for that hunt - `xrefs_to_field` on `Duel::InPlay_Card.Pos` returns 150+ functions, so this needs narrowing (e.g. searching for the ones that also touch `EventRecord`) rather than reading them all.

## 29. The trigger map: FLIP, sent to the GY, destroyed, battle (2026-10-01)

The question sections 24 and 26 could not answer (where FLIP / destroyed-by-battle triggers come from) is answered. The key was the packed effect reference every offer takes (`Offer_EffectByCardRef` 0x1400AAE50): **bits 0-15 card id, 16-20 zone, 21-23 table selector (word4), 24 flag, 25-30 event type (word3), 31 player**. So every offer call site gives its event number away as a constant, and an effect record's word3 IS the event that offered it.

| event (word3) | what | offered by | table | gate | custom clones |
|---|---|---|---|---|---|
| 7 | Normal / Special Summoned | `Eval_EventTriggersCardEffect_ByCardId` (pump kinds 1, 3) | 1 | id lists + ladder | work (Hook_EventEvaluator, confirmed) |
| 8 | **Flip Summoned** | same evaluator; pump kind 2 = `EventKind2_FlipSummon_Handler` 0x1400EBE10 (flips the card, Goblin Fan check, then event 8) | 1 | `EventEval_IdList_FlipSummoned_Ev8` ("when this card is Flip Summoned") or `Kind_IsFlipMonster` | covered by Hook_EventEvaluator (the source id is shown) |
| 9 | Special Summoned / material | same evaluator (pump kind 4); `Eval_MaterialGrantedEffects_Ev9` 0x1400E3D20 = effects a Fusion/Synchro/Xyz/Link monster gets from its materials | 1 | id lists | summon side covered |
| 13 / 21 | **flipped face-up**: 21 = a set monster attacked and flipped in battle (CONFIRMED in-duel 2026-10-01: Dragite cloning Green Turtle Summoner destroyed the attacker, twice), 13 = flipped by an effect (not a Flip Summon) | `ChangeBattlePosition_FlipTrigger` 0x140084780 -> `Offer_EffectOfCardAtZone(player, zone, 13)` | 1 | only `Kind_IsFlipMonster(card)` | **new: IdTest hook on 0x140743030**, a clone answers like its source |
| 15 | inflicts battle damage | battle / LP code (`sub_14009FB40`, `LP_ApplyDamage_Internal`) -> `Offer_EffectOfCardAtZone(..., 15)` | 1 | inline binary search of `EventIdList_BattleDamage_Ev15` (0x140ACFF58, 27 ids) | **not built** (needs a mid-function hook) |
| 18 / 19 | attack declared | `sub_140086C00` -> `Offer_EffectOfCardAtZone(..., 18)` | 1 | inline `EventIdList_AttackDeclared_Ev18` (0x140AD11B0, 50 ids) | **not built** |
| 31 | sent to the GY / banished from the hand or Deck (discard, Foolish Burial) | `Move_NonFieldCardToGraveOrBanish_Ev31` 0x1400A6F40 -> `Eval_CardMovedTriggers(31)` | 2 | `MoveEval_IdList_SentToGrave` (105 ids) + per-id ladder | **new: Hook_MoveEvaluator** |
| 33 | a field card sent to the GY / banished (destroyed, tributed; bit 18 of the move ref = by battle) | `Move_FieldCardToGraveOrBanish_Ev33` 0x1400A7750 -> `Eval_CardMovedTriggers(33)` | 2 | same | **new: Hook_MoveEvaluator** |
| others seen | 0, 1, 2, 4, 6, 14, 20, 21, 22, 24, 26, 28, 30, 39, 40, 41, 42 | phase functions (0x140149000 - 0x140152DF0: Standby / End phase families), activation menu (`sub_1400600E0`) ... | | | not mapped |

**FLIP is a card KIND, not a row.** FLIP and summon monsters share table 1 (MonsterEffect) and their rows look alike. What makes a row fire on a flip is `Kind_IsFlipMonster` (0x140743030, was misnamed `Kind_IsToonOrSpecial`). `KIND_TABLE` (0x140BF7820) is 6 x s16 per kind: BaseFrame, ?, **Category**, DeckSection, Tuner, Pendulum. Category 4 = FLIP (kinds 24 FlipEffect, 32 TunerFlipEffect, 35 PendulumFlipEffect; 2 Normal, 3 Effect, 5 Toon, 6 Spirit, 7 Union, 8 Gemini). It also answers yes (2) for the 50 ids in `FlipBehaviour_ExtraIdList` (cards of other kinds with a "when flipped face-up" effect). The FLIP test monsters of the old test 10 had kind `Effect`, so they could never fire on a flip; with the IdTest hook a clone answers as its source, whatever the custom kind.

**Leave-the-field triggers are table 2 (Kind2).** Destroyed-by-battle (Birdface 5021) and sent-to-GY (Archfiend Heiress 10632, Superheavy Samurai Drum 11950) rows all live in `EffectTable_Kind2`; table 3 is ignition (Aquaactress Tetra 11916). `Eval_CardMovedTriggers` (0x1400A2F70) is their per-card evaluator. Its move ref: bits 0-8 card INSTANCE index, 9 player, 10-14 from zone, 16 from-field, 18 by battle, 20 controller differs, 21-25 to zone (16 GY, 17 banished). It reads the card id from the card instance table (**0x143499798 + 8 * index**, id = low 14 bits; IDA shows it as `stru_143497C40.field_1B52`), not through `Get_CardIdAtZone`. So `Hook_MoveEvaluator` writes the source id into that instance for the call, and `Hook_OfferByRef` gives the custom id back. A custom card with no clone is NOT skipped there (the evaluator may let other cards react to the move).

**Test:** `Desktop\New folder\tests\12_flip_and_grave.json` (README lists the cards): FLIP search with kind Effect (15303), FLIP destroy (19341, works), FLIP burn (15329), destroyed by battle (15335); all Level 4 or lower (the first version used Level 5/8 cards that need Tributes), sent to GY by effect or battle (19333; use spell 19328 to send it from the Deck = event 31), destroyed on the field -> revive (16403). Log lines to look for: "Move event 31/33 for custom card ..."; for FLIP by attack, the slot calls of the clone. **Built 2026-10-01, not run in a duel.**

**Still open:** attack-declared (18) and battle-damage (15) triggers need a hook inside `sub_140086C00` / `sub_14009FB40` at the list search (the list size is an immediate). Ignition (table 3) and quick effects are offered through the activation menu (`sub_1400600E0` -> `Chain_TryOfferEffect_ForCardRef`) with no id list: expected to work through the row hook, untested. Standby / End phase triggers (events 2 and 6) not read.

**Battle destruction (second duel test, 2026-10-01):** a monster destroyed by battle did NOT go through `Eval_CardMovedTriggers`: no "Move event" line, nothing offered (15335 cloning Birdface). The trigger is decided in `sub_1400C2CA0`, which asks `List_BinarySearchKonamiId` (0x140742D20) whether the card is in `TriggerIdList_DestroyedByBattleToGrave` (0x140AF67E0, 133 ids) or `TriggerIdList_SentFromFieldToGrave` (0x140AF5C60, 111 ids). `Hook_ListSearch` answers those two lists (and only those - the same helper serves `Is_CardInNamedArchetype`) for a clone's source. Log line: "Trigger list ... asked about custom card ...". Untested. Also confirmed in that duel: FLIP search (15303, kind Effect) and FLIP burn (15329).

**Borrowed duel ids must be harmless cards.** The engine applies every hard-coded id rule to the id a custom card plays under. The Cards plugin used to lend the first free id from 3900: 3901 is Kuriboh Token ("cannot be used as a Tribute"), so a custom Dragite lent 3901 could not be Tributed. 3900 - 4006 are Tokens and 4007 is Blue-Eyes White Dragon. `BorrowScratchId` now lends first from 51 Normal monsters that appear nowhere in the exe (no effect row, id list or table, deck/filter param or code immediate), then from 4008 up skipping Tokens.

**Third duel test (2026-10-01):** CONFIRMED - destroyed by battle -> search (15335 cloning Birdface: list answered, offered, search machine ran 128 -> 100); sent to the GY from the Deck by an effect = event 31 (19333 cloning Archfiend Heiress, via `Hook_MoveEvaluator`); borrowed ids now come from the safe list (16403 -> 4085). NOT working then: Flint Cragger cloning Superheavy Samurai Drum (11950) - Drum is in neither trigger list but in a third structure the same function reads next, `TriggerTable_LeaveFieldConditions` (0x140AF5D40, 676 rows {u16 id, u16 condition flags}, Drum = 0x180C), searched with `List_BinarySearchRowByKonamiId` (0x140742D80). `Hook_RowSearch` answers that table only with the source's row (log: "Leave-field table asked about custom card ..."). Built, untested. `sub_1400C2CA0` renamed `Eval_LeaveFieldTriggers_Battle`.

**Fourth duel test (2026-10-01):** CONFIRMED - Superheavy Samurai Drum-style leave-field trigger (Flint Cragger revive, `Hook_RowSearch`), Tributing a borrowed-id monster (safe borrow ids), Tribute Summon -> "When Normal Summoned" trigger, sent from the field to the GY (Botanical Girl source, `TriggerIdList_SentFromFieldToGrave`), ignition with once per turn (Aquaactress Tetra source, table 3). One fault: a clone of **Poki Draco (9648)** added a card without showing the list (slot 0 went 128 -> 125 -> 100 in 3 ms instead of 128 -> 126 -> 125 -> 100). Poki Draco's list-filter row has param -1 ("a card with this card's own name"); the search machine reads that row itself, outside the `deck` override, and auto-picks. Rule: **never borrow from a card whose list row param is -1.** `build_effect_reference.py` now skips them, so WolfEx's `normal_summoned` search source is Gem-Armadillo 8897 (trigger_sources.json regenerated into `Tools\WolfX\Content` and `Binaries\Debug\Tools`; the script's default OUT path still points at the old `Tools\WolfEx`).

## 30. The full event map (2026-10-01, second pass)

Made by scanning every call to the offer functions (`Offer_EffectByCardRef`, `Offer_EffectOfCardAtZone`, `Chain_TryOfferEffect_ForCardRef` and the per-card helpers 0x1400AB360..0x1400ABF60) with capstone, decoding the event constant at each site, and naming the event from the function it is in and the cards that function tests (`docs/effect-scripts/event_map.py`: pefile + capstone + `.pdata` for function bounds). Values above 42 printed by such a scan are card ids passed to the per-card helpers, not events.

| event | meaning (from the function) | where | gate for a custom card | status |
|---|---|---|---|---|
| 1 | Draw Phase | `Duel__Phase__EnterDrawPhase` | per-id ladder | not hooked |
| 2 | Standby Phase ("during your Standby Phase") | `PhaseHandler_OfferAtStandby_Ev2` and friends, rows of `PhaseHandlerTable_Standby` | **table of {handler, card id} rows** | **hooked** (AttachPhaseHandlers), test 14 |
| 4 | single hard-coded cards in the leave-field switch (Different Dimension Gate) and Union Attack | `Eval_LeaveFieldTriggers_Battle`, 0x14020DC0C | per-card | not a family |
| 6 | End Phase ("during the End Phase") | `Phase_EndPhase_RunHandlers` 0x1401558E0, rows at 0x140B12310 (552, u16 id at +8, flag bytes +0xA/+0xB) + 0x140B14590 (19) | table of {handler, id} | **hooked** (same AttachPhaseHandlers), test 15 |
| 7 / 8 / 9 | summon / Flip Summon / special + material | section 29 | | working |
| 11 | a card is Set (Worm Illidan) | `Eval_CardSetTriggers_Ev11` | named-card helper | **hooked** (named helpers) |
| 13 / 21 | flipped by an effect / after damage calculation and battle flips (D.D. Warrior, FLIP in battle) | `ChangeBattlePosition_FlipTrigger`, `Eval_AfterDamageCalcTriggers_Ev21` | | FLIP working; other 21 uses untested |
| 14 | damage calculation (Cipher Soldier "during damage calculation") | `sub_1402114C0` | named-card helpers + a few per-id compares | **hooked** (named helpers) |
| 15 | inflicts battle damage | `Eval_BattleDamageTriggers_Ev15` | inline list `EventIdList_BattleDamage_Ev15` | **hooked** (inline stub), test 14 |
| 16 / 17 | effect damage taken / LP gained (Dark Room of Nightmare, Aromage Jasmine) | `LP_ApplyDamage_Internal`, `LP_ApplyGain_Internal` | named-card helpers | **hooked** (named helpers), test 15 (Jasmine) |
| 18 / 19 | attack declared | `Eval_AttackDeclaredTriggers_Ev18` | inline list `EventIdList_AttackDeclared_Ev18` | **hooked** (inline stub); no clean vanilla source to test with (Edge Imp Chain searches its own name) |
| 20 | start of the Damage Step (Sasuke Samurai) | 0x140210647, 0x140210D0A, 0x1402109F1 | named-card helpers + per-id compares | **hooked** where named |
| 22 | a monster destroys an opponent's monster by battle (Howl of the Wild, Goyo Emperor; generic offer at 0x1400C0D1D reads the destroyer's id from the battle record) | `sub_1400C0850` (called from `Eval_LeaveFieldTriggers_Battle`) | none for the destroyer's own effect; named helpers for others | generic, test 15 (Guardian Angel Joan) |
| 24 | end of the Battle Phase | `Phase_EndOfBattlePhase_RunHandlers`, rows at 0x140B2B2A0 / 0x140B2B2C0 | table of {handler, id} | **hooked**; no clean vanilla source (all have extra conditions) |
| 26 | an Equip Card is equipped (Morale Boost, Gearfried) | `Eval_EquipTriggers_Ev26`, 0x1401ECE80 | archetype check (Noble Knight 147) / zone offer | expected generic, untested |
| 28 | a card is drawn (Appropriate, Naturia Ragweed) | `Draw_ExecuteDraw` | named-card helpers | **hooked** (named helpers) |
| 29 | added from the Deck to the hand (Watapon, R-Genex) | 0x140089530, 0x14008A3F0 | named-card helpers | **hooked** (named helpers) |
| 30 | in Super Robolady / Magma Neos code (returned to the Extra Deck?) | 0x14022E4C0 (a slot function) | | not identified |
| 31 / 32 / 33 | sent to GY / banished (hand-Deck / hand / field) | section 29 | | working |
| 39 | Standby skip / Draw (Solomon's Lawbook, Draw_ExecuteDraw) | | | not identified |
| 40, 41, 42 | Last Will / Majestic Star Dragon / paying LP as a cost (Masterking Archfiend, Chain Energy) | | | not identified |

**Per-card handler tables** are a third gating style besides id lists and per-id ladders: the phase code walks `{u64 handler, u32 card id}` rows (unsorted) and calls `handler(player, cardId)`; a handler that offers returns 0 and the phase step restarts from the first row next tick. `AttachPhaseHandlers` detours each handler whose rows name a clone's source (pool of 48 thunks) and, after the source's row, calls it again with the custom card's duel id (`Card_GetActiveDuelSessionId` from Yu-Gi-Oh-MoreCards), returning 0 if that call does.

**Inline list stubs:** `AttachInlineListStubs` writes a 0x93-byte stub per site (save flags, rax/rcx/rdx/r8/r9/r11 and xmm0-5, align the stack, `r10d = MapInlineListId(r10d)`, restore, run the 9 replaced bytes, `jmp` back) and installs it with DetourAttach on the site. Verified by disassembling the generated bytes. Log: "Inline trigger list asked about custom card ...".

**Test 14** (`tests\14_standby_battledamage.json`): Standby Phase burn (15362, plain id) and gain (19333, borrowed id) cloning Bowganian 5687; direct-attack battle damage search (15364) cloning Wattcobra 9746.

**Named-card offers (third pass):** most per-event ladders offer a specific card by passing its literal id to one of the `Offer_EffectOfNamedCard*` helpers (0x1400AB360 field, AB5E0 Pendulum zones, ABC30 field + used bit, ABE00 Spell/Trap zones, ABF60 Graveyard: id in arg 2; AB750 linked, ABA70: id in arg 3; AB8C0 is Voltester-only). `AttachNamedHelpers` re-calls the helper with each clone's duel id after the call for its source, which covers events 11, 14, 16, 17, 20, 28, 29, 42 and ~56 summon-reaction calls at once. Draw Phase (1) and events 30 / 39 / 40 / 41 offer single hard-coded cards and are not families.

**"When this card destroys a monster by battle"** (200 game monsters) is offered generically: their rows are plain table-1 rows (Guardian Angel Joan: LP machine only) and the card ids appear nowhere in engine code; the id lists that name them (0x140B32E60 ...) are read only by the AI (`sub_1403D4E30`). So clones should get it with no hook (test 15, 15309).

**Test 15** (`tests\15_phases_battle_named.json`, replaces 14): Standby burn / gain (Bowganian), End Phase burn (Solar Flare Dragon 5974), direct-attack damage search (Wattcobra), destroys-by-battle LP gain (Guardian Angel Joan 5899), "if you gain LP: draw 1" (Aromage Jasmine 11818, named helper, event 17).

## 31. Continuous effects: first findings (2026-10-01)

Continuous stat effects (Gaia Power, Command Knight, Equip Spells) have **no rows in the four effect tables**, and their ids barely appear in engine code. What was found:

| table | layout | rows | what | reader |
|---|---|---|---|---|
| 0x140ACEF20 `StatTable_EquipAtkDef` | {u16 id, s16 ATK, s16 DEF} | 138 | Equip Spells (Axe of Despair +1000, Legendary Sword +300/+300) | `Calc_EffectiveAtkDef_Modifiers` (0x1400307C0) via `List_BinarySearchRowByKonamiId`, keyed by the EQUIP card's id |
| 0x140ACF260 `StatTable_UnionAtkDef` | {u16 id, s16 ATK, s16 DEF, 0} | 12 | Union monsters equipped (Y-Dragon Head, Kiryu) | same function |
| 0x140ACE2B0 `StatTable_BoostA` | {u16 id, s16, s16} | 210 | "gains N ATK" keyed by the card that gave it (Junk Blader +400); ids 1029-1044 = generic +1/-1 markers | same function |
| 0x140ACE7A0 `StatTable_BoostB` | {u16 id, s16, s16} | 310 | more boosts (Madolche Lesson +800/+800, Gravity Blaster +400) | same function |
| 0x140BF519C | {u16 id, u16, u32 affectsMask, u32} | 663 | which monsters a continuous effect touches: bits 0-5 attribute (1 LIGHT 2 DARK 4 WATER 8 FIRE 0x10 EARTH 0x20 WIND), bit (race+6) Type (0x80 Dragon, 0x200000 Warrior) | **not found** (no direct reference) |
| 0x140B36350 | {u16 id, u16 scope, s16 ATK, s16 DEF} | 116 | continuous boosts with amounts (Gaia Power 3/+500/-400, Command Knight 1/+400) | AI only |
| 0x140B35FC0 | copy of the equip table | 130 | | AI only |

**Built:** `Hook_RowSearch` now answers the four ATK/DEF tables (as well as the leave-field table) for a clone's source, so a custom Equip Spell / Union / "gains N ATK" card gives its source's numbers. Test card in test 15: 15728 (Equip Spell cloning Axe of Despair 4310). A clone's OWN amount (e.g. "+700") is not supported yet - it would be a synthetic row like `deck`/`filter`.

**Still open:** where Field Spells / "all X monsters you control gain N" (Gaia Power, Command Knight) are applied - the amounts are not in any engine-read table found so far, so they are probably a per-id ladder inside `Calc_EffectiveAtkDef_Modifiers` (35 KB; decompile is truncated by the tool - read it in pieces with `docs/effect-scripts`-style capstone windows) plus the 663-row affects-mask table.

## 32. Source-id lending: a clone plays under its source's id (2026-10-01)

The duel engine reads about **290 id lists** (catalogued by a scan of every sorted u16 list referenced from 0x140001000-0x140300000; `listcat.py` idea in the session notes) plus per-card switches, phase tables, continuous effect tables and the AI's own tables. Hooking them one by one does not scale. Instead **Yu-Gi-Oh-MoreCards lends a clone its effect source's own id for the duel** when it can:

* `ExtraCard.CloneFrom` = `effectClone.from` from cards.json.
* `Hook_Duel_LoadDeck` (player 0's call) first reserves every vanilla id in BOTH deck structs (`Duel_DuelEngine + 0x2A` and `+ 0x236`, both filled before the first `Duel_LoadDeck`; `ReserveVanillaIds`).
* `ResolveDuelSessionId` tries `BorrowSourceId` first: the source id is used when no deck holds the vanilla card and no other custom card took it. The source's FULL_CARD_PROPS entry is overwritten with the custom card's name, art and stats (`BorrowInto`, same as the scratch borrow) and restored after the duel. Otherwise the old path runs (own id, or a safe scratch id above 16383).
* Log: `Duel: card N borrows vanilla id M for this duel (its effect source)`.

Under its source's id the card IS the source to every id-keyed rule: lists ("can attack directly", "cannot be destroyed by battle" ...), per-card switches, continuous effects (Gaia Power, Command Knight), win conditions, equip restrictions, the AI's knowledge - while showing its own name, art and stats. Its own archetypes still apply (the Cards plugin's archetype hook maps the lent id back). Yu-Gi-Oh-Effects maps the id back too (`Card_GetRealIdForBorrowed`), so `draw` / `lp` / `deck` / `filter` / `stats` / `oncePerTurn` keep working; its phase and named-card thunks skip the second call when the clone's duel id IS the source id.

**Fallback:** the source card is in a deck, or two custom cards clone the same source -> the hooks of sections 22-31 (partial coverage: everything hooked works, the 290 lists do not).

**Language (EffectScript, WolfX):**
* `as(<id> | "Card name") [with atk N, def N];` -> `{ "from": id, "stats": {...} }`. Names come from `card_names.json` (every game card, written by `build_effect_reference.py`, shipped next to WolfX).
* `equip(atk N[, def N])` -> Axe of Despair 4310 + `stats` (an Equip Spell for any monster; its only other effect is optional).
* Triggers added: `standby_phase`, `end_phase` (hand-checked source Solar Flare Dragon 5974), `destroys_by_battle`, `battle_damage`, `attack_declared` (no clean source yet), `ignition`.
* `stats` (runtime): a clone's own amounts for the equip / union / boost tables (`Hook_RowSearch` returns a synthetic {id, ATK, DEF, 0} row).

**Test 16** (`tests\16_source_lending.json`, includes test 15): Atlantis as Gaia Power, Battlin' Boxer Uppercutter as Command Knight, Battleguard Cadet as the beneficiary, Cursed Copycat Noble Arms as `equip(atk 700)`.

## 33. Multi-effect cards, new zones, condition override, trigger x action composition (2026-10-02)

**What the custom cards need** (sentence shapes over the 4,307 delta texts, `shapes.py` in the session notes): "once per turn" 3,446 cards (supported), destroy 1,160, banish 752, materials 715, search 643, negate 498, "Special Summon this card from your hand" 475, "banish this card from your GY" cost 444, Special Summon from hand 382 / Deck 280, ATK changes 275, detach 184, draw 181, Tribute cost 164. Almost every modern card has two or three separate effects.

**Multi-effect cards** - `"effectClone": { <main effect>, "parts": [ { "from": ..., "trigger": ..., ... }, ... ] }`. Every lookup is routed to one effect (`EffectClone.cpp`):
* row lookups (`Hook_GetEntry`, `SlotThunk`, id ladders, draw count): `RouteByRow` - the parts whose source has a row for the record's table (word4); several -> the one whose `trigger` fits the record's event (word3: 7/9 summon, 8/13/21 flip, 31-33 GY, 2 Standby, 6 End Phase, 15 battle damage, 18 attack, 22 destroys by battle; `TriggerFitsEvent`).
* summon / FLIP evaluator: `RouteByTrigger(kSummonTriggers)`; move evaluator: `kLeaveTriggers`; inline battle lists: `kBattleListTriggers`.
* trigger lists / leave-field and stat tables: the first part whose source is in the list / has a row.
* phase handlers and named-card helpers: every part is registered.
* plain id tests: the highest answer over the parts (e.g. "is a FLIP monster" if any part is).
Source-id lending (section 32) lends the MAIN effect's source.

**New list scans** (the list-filter wrappers decompiled: zone scanner + behaviour flag): `deckSummon` 0x14052EE80 (Deck, 4 = can be Special Summoned), `handSummon` 0x14052EE40 (hand, 2), `hand` 0x14052F490 (hand, 1 = any), `banished` 0x140532BF0 (0x40). Known before: `deck` 0x14052F2A0 (0), `grave` 0x14052F4D0 (0), `graveSummon` 0x14052EC80 (8), `opponentGrave` 0x14052EE00 (0x40001), Extra Deck 0x140531D30 (0x842). Magnet Circle LV2's machine 0x140160080 is HAND-ONLY (checks zone 13, reads the hand array).

**Condition override** - `"condition": "always" | "listHasMatch"` replaces slot 2 of the borrowed row with `Slot_ReturnConst2` / `Cond_ListHasEnoughMatches` (which counts through the clone's own list filter). Used to drop Unexpected Dai's "if you control no monsters".

**Trigger x action composition** - `"actionFrom": N`: the row (table, event wiring, limit class, slot 3) comes from `from` (a monster with the trigger), slots 0/1/2/4 from N's first row (`ActionRow`), each run impersonating N. The WolfX compiler composes automatically when no game monster has the exact trigger + action; trigger-only sources may be cards whose own action is unusable ('compose' entries in trigger_sources.json, e.g. Edge Imp Chain for `attack_declared`).

**Language:** `special_summon(hand | deck | grave, selector)` (Magnet Circle LV2 6572 / Unexpected Dai 11740 + `listHasMatch` / Monster Reborn), `add_to_hand(grave, selector)` (The Warrior Returning Alive 5330), several effects with `also` or several `effect { }` blocks.

**Test 17** (`tests\17_actions_composition.json`, includes 16): 15341 Special Summon from Deck, 15342 from hand, 15376 add from GY, 15381 Normal Summoned -> Special Summon from hand (composed), 15382 sent to GY -> draw (composed), 15405 attack declared -> burn 300 (composed). Built, not run in a duel.

## 34. Costs (2026-10-02)

**Costs are slot 3 of an effect row**, one shared function per cost type (counted over the four tables): discard `0x1401F7FD0` (120 rows), pay LP `0x1401F7CC0` (147), Tribute 1 `0x1401FDDF0` (285), banish this card from the GY `0x1401FA670` (261), send this card from the hand to the GY `0x1401FF020` (74). The "monster effect wrapper" slot 3 functions of section 12 are the no-cost defaults. Amounts are id-keyed: `sub_1401F6F90(effect)` = cards to discard (the discard condition `0x1400F7BA0` checks the hand has that many, plus Goblin of Greed / F.A. Whip Crosser rules), `sub_1401F65D0(effect)` = life points to pay (paid by `sub_14009DDC0`).

**Built:** `"cost": { "from": N, "amount": K }` on an effect step. Slot 3 runs N's cost function as N; slot 2 first runs N's condition as N (a cost that cannot be paid blocks the activation), then the effect's own (none = allowed, 2). `Hook_DiscardCount` / `Hook_LpCost` answer K while the record shows N. Sources: Lightning Vortex 5217 (discard), Delinquent Duo 4901 (pay LP; its own condition is card-specific, so LP payability is not checked yet). Language: `cost discard(N): <action>` / `cost pay_lp(N): <action>`. Test cards 15379 (discard 1, draw 2) and 15342 (pay 800, Special Summon from hand) in test 17.

**Not built:** Tribute, banish-self and send-self costs - they belong to effects activated from the GY / hand (their rows sit in the Kind2 table with GY/hand activation wiring), which need a GY/hand-activated source per action first. Detach Xyz material (184 delta cards) not looked at.

## 35. Banish, banished zone, negate (2026-10-02)

* **Banish machines** (slot 0), both driven by the section-11 filter row through slot 1 `Target_Generic_FromFilterRow`: `0x140156ED0` "target 1 card; banish it" (49 rows; Legendary Knight Hermos 11887 - no id reference anywhere in code) and `0x140157050` "banish all ..." (24 rows; Armoroid 7057 - its engine references are its own Tribute Summon trigger, not the machine). They are MONSTER rows, so a Spell keeps a Spell row (Remove Trap 4838 / Warrior Elimination 4659) and borrows the banish slots by composition (`actionFrom`). Language: `banish(target | all, side, selector)` (same filters as destroy).
* **From the banished cards:** Dragoncarnation 10561 ("Target 1 of your banished Dragon monsters; add it to your hand", machine `0x14016E3B0`, list scan `banished` 0x140532BF0). Language: `add_to_hand(banished, selector)`.
* **Negate:** "Negate the activation, and if you do, destroy it" is one machine (`0x14015EA50`, 18 Spell/Trap rows) but WHAT each card may negate is card-specific; "negate the Summon" likewise (`0x1404C1E00`). Custom negation cards are written `as("Magic Jammer")` etc.: with source-id lending (section 32) the engine's own per-card rules apply. **Superseded by section 39: the rules are a table, `negate(...)` gives a card its own.**
* **Bug fixed:** the WolfX compiler writes `"side": "opponent"` for destroy/banish filters, the runtime only knew `"opp"` -> every scripted opponent-side destroy was rejected at load. `BuildFilter` now accepts both.

Test 17 additions: 15389 banish target, 15391 banish all opponent's monsters, 15399 add a banished monster to the hand.

## 36. Fusion materials: generic codes, combined materials, recipes from card text (2026-10-02)

**How the game checks materials.** `Fusion_SelectMaterials` (0x140006BD0) reads every recipe slot through `Get_FusionMaterial` and tests each candidate with `Fusion_CardMatchesMaterialCode` (0x140004B10, `(code, card instance index, override id)`; instance table 0x143499798 + 8 * index). So a recipe slot is a CODE, not only a card id:

| code | meaning |
|---|---|
| >= 3000 | card id (same-name check, `sub_1407EBA00`) |
| 1..25 | race bit (CARD_Type: 1 Dragon ... 24 Divine-Beast) |
| 26..31 | attribute (26 LIGHT, 27 DARK, 28 WATER, 29 FIRE, 30 EARTH, 31 WIND) |
| 32..43 / 44..55 / 56..67 | Level == code-31 / Level >= code-43 / Level <= code-55 |
| 68..97 | special letters: 72 Gemini, 73 Normal, 74 Effect, 75 Synchro, 76-80 Synchro + race/attribute, 81 Synchro or Xyz, 82 Xyz, 83 Pendulum, 89 Fusion, 90 Tuner, 95 Link, 97 per-fusion ladder |
| 98..516 | archetype code-98 (special cases 108/126/149/307/337/423) |
| 517..2999 | nothing (returns 0; the UI hint code `sub_140359960` skips them) |

A code below 3000 only matches monsters (the matcher's own gate).

**Yu-Gi-Oh-Effects `Fusion.cpp`** - `"fusion"` in cards.json takes 2 to 5 materials, each:
* a number: card id or raw code;
* a word: `"Dragon"`/`"race:Dragon"`, `"DARK"`/`"attribute:DARK"`, `"level:4"`, `"level>=5"`, `"level<=4"`, `"archetype:12"`, `"normal"`, `"effect"`, `"tuner"`, `"ritual"`, `"fusion"`, `"synchro"`, `"xyz"`, `"pendulum"`, `"link"`, `"gemini"`, `"synchroorxyz"`, `"Illusion"`, `"monster"` (any);
* `"non-X"` / `"!X"`: not X;
* an array: ALL of them (`["LIGHT", "Warrior"]` = "1 LIGHT Warrior monster");
* `{"any": [...]}`: one of them (`{"any": ["LIGHT", "DARK"]}`), nestable.

Codes the game does not have are answered by `Hook_MatchesMaterialCode` (detoured only when a recipe uses one): 517..2496 custom archetypes (code-98 >= 419, through the Cards plugin's `Is_CardInNamedArchetype` hook), 2497 Ritual Monster (kinds 4/5/38), 2498 Illusion (custom race 0x1A), 2499 any monster, 2500..2999 combined materials (all / any, negatives = "non-"). Each first checks `Kind_IsMonster` like the game's gate.

**Borrowed ids.** In a duel the hooks get ENGINE ids: `RecipeFor` turns a borrowed id back into the custom card (`Card_GetRealIdForBorrowed`; a borrowed id whose custom card has no recipe has none - the vanilla recipe of that id is not its own), and `EngineMaterial` hands a custom material id out as the id it plays under (`Card_GetActiveDuelSessionId`). Same in `Get_FusionsUsingMaterial`.

**Recipes from text (WolfX).** `FusionMaterialText.cs` reads a Fusion Monster's material line (first line; after `[ Monster Effect ]` for Pendulums; skipping "(This card is always treated as ...)") into that form. Quoted names -> card ids (`card_names.json` + custom cards; old names mapped, e.g. Red-Eyes Black Dragon -> Red-Eyes B. Dragon); `"X" monster` -> archetype codes: the catalog's exact name, the game archetype most of whose members (`archetype_members.json`, dumped from CARD_Named.bin) are named with X, and the custom code most custom cards named with X carry (custom cards often have a custom code for a game archetype: Gem-Knight = 84 and 547), else the few game cards named with X ("Forbidden One"). Conditions the check cannot see ("with different names", "in your GY", "on the field", "face-up") are dropped and saved as `"fusionLooser"`. Effects tab -> "Attach from card text..." now also writes `"fusion"` for every Fusion without one. Over the 250 custom Fusions: 246 recipes (68 looser than the text); the other 4 are not Polymerization Fusions (contact/"Must be Special Summoned with", 7 or 8 materials).

Test 18 (`tests\18_fusion_materials.json`, includes 17): race, attribute, Level, kind, combined, any-of and archetype recipes.

## 37. Costs paid with the card itself, Special Summon this card, conditions, ATK gain, whole-card text (2026-10-02)

**Where hand / GY effects live.** `EffectTable_Kind2` (word4 = 2, 0x140B85A50) holds every effect activated in the hand or GY as well as the leave-field triggers. `Check_CanActivateKind3AtZone` sends any card in a zone > 12 straight to table 2 (no id list); a MONSTER in the hand is only offered when `Id_HasHandEffect` (0x140743FA0: `IdList_HandEffectA_120` 0x140BF8A90, `IdList_HandEffectB_195` 0x140BF81C0, `IdList_HandEffect_349` 0x140BF8BC0) says so - an IdTest hook answers it for the clone's sources.

**Slot 2 of these rows is a delegate** (`Slot_Delegate_ToSecondaryTable_FE560` 0x1400FE620) into `EffectCondTable_CostAndEffect` (0x140AFD7E0, 2593 rows {u16 id, fn costCheck, fn effectCheck}, both must pass). The cost checks are small generic functions, so a composed effect can keep them:

| cost (slot 3) | rows | check | donor row used |
|---|---|---|---|
| `Cost_BanishSelfFromGrave` 0x1401FA670 | 261 (Kind2) | `Cond_CanBanishSelfFromGrave` 0x1400FB960 | Rose Lover 9409 |
| `Cost_DiscardSelf` 0x1401FCEB0 | 53 (Kind2) | `Cond_CanDiscardSelfFromHand` 0x1400F7D00 | Hecatrice 7572 (on the hand list) |
| `Cost_TributeSelf` 0x1401F8BA0 | 39 (Kind3 ignition) | `Cond_CanTributeSelf` 0x1400FAC80 | Planet Pathfinder 10232 |

Other generic checks found: `Cond_CanSpecialSummonSelf` 0x1400FB9A0 (this card can be Special Summoned from where it is), `Cond_CanSpecialSummonSelfFromGrave` 0x1400FA380, `Cond_ControlNoMonsters` 0x140242A80, `Cond_FieldHasFilterRowMatch` 0x1400FE510 (a card matching the effect's generic filter row is on the field - Quillbolt Hedgehog's "you control a Tuner"; with the clone's own filter row it is any "If you control a ... monster").

**Special Summon this card.** From the hand: `Slot0_SpecialSummonSelfFromHand` 0x140161E20 (generic path for unknown ids; donor Watch Cat 13582, on the hand list). From the GY: 0x140169F70 (donor Quillbolt Hedgehog 7701). Watch Cat's own condition is "no monsters"; it is replaced by `condition: always` + the checks below.

**Runtime (EffectClone.cpp).** `"require": [names]` - checks run as the row's card before slot 2's condition (SlotThunk<2>), all must pass: `canBanishSelfFromGrave`, `canDiscardSelf`, `canTributeSelf`, `canSummonSelf`, `canSummonSelfFromGrave`, `noMonsters`, `controlsMatch` (with the clone's `filter`), `inHand` (the plugin's own `Check_InHand`: zone byte of `Card_GetPositionByInstance` 0x140044480 == 13 - without it Watch Cat's row was also offered from the GY, where the machine does nothing). A composed effect's ATK amount: Rush Recklessly's slot 0 registers the boost under the ACTION card's id, so `Hook_RowSearch` answers `StatTable_BoostB` for that id with the first composed clone that has `stats` (`g_StatsByAction`; a vanilla copy of the action card in the same duel would get that amount too).

**Language.** `cost banish_self: / discard_self: / tribute_self:` (row = the donor above, action composed with `actionFrom`, its check in `require`; no trigger allowed), `if no_monsters:`, `if controls(selector):` (not with destroy / banish, which need the one filter row), `special_summon(self)` (hand), `special_summon(self_grave)`, `gain_atk([own,] N)` (Rush Recklessly 4905 / Inspiration 11427 + `stats`). `trigger_sources.json` gained `sent_from_field_to_grave` (Botanical Girl 7892, Superheavy Samurai Drum 11950; also in `build_effect_reference.py` MANUAL_SOURCES).

**Whole-card text (WolfX `CardTextTranslator.cs`).** Splits a card's text into sentences (bullets kept with their header), reads each as `[trigger][condition][cost] action`, writes the ones the game can run joined with `also`, and keeps the rest: `effectNotImplemented` (sentences not run) and `effectLooser` (dropped details: "and if you do, ...", "except this card", Synchro/Link/... Summon read as any Special Summon, Quick Effects read as ignition, alternatives "A or B" read as A ...). Rules that come from the runtime: one effect per kind (a trigger, ignition, hand/GY, Spell activation - `RouteByRow` picks the first part with a row in the asked table, so a second effect of the same kind would never be found); an own-side destroy whose follow-up is dropped is not written; Equip Spells only take `equip(...)`; Continuous / Field cards' effects are noted as running once on activation. The Effects tab "Attach from card text..." runs it on every card without a script (and writes Fusion recipes).

Over the 4,333 custom cards (harness `texttest.ps1` in the session notes): **737 cards get 763 effects, 31 cards completely**, 0 compile errors. Before this section: 199 cards with at least one runnable effect. The full file with them attached: `Desktop\New folder\Cards.with-effects.json` (plus the 246 Fusion recipes). What is left is a long tail (each shape < 25 sentences): card-specific conditions ("If your LP are lower", "If a Quick-Play Spell is activated"), negation, excavation, Set from Deck, Fusion/Ritual Summon by effect, attach/detach, return to hand, position changes, "add this card from the GY to your hand" (no clean donor found yet), summon procedures ("You can Special Summon this card (from your hand) by ...").

Test 19 (`tests\19_self_costs_and_self_summons.json`, includes 18): Rose Shaman (Tribute self -> draw), Rookie Warrior Lady (GY banish -> add EARTH Warrior from GY), Raise Moon Blade Shayna (discard self -> draw), Crimson Resonator (no monsters -> Special Summon self), Fire Flint Lady (control a Warrior -> Special Summon self), Chronomaly Acambaro Figures (pay 1000 -> Special Summon self from GY), WAKE CUP! Mocha (FLIP -> +1000 ATK). Built, not run in a duel.

## 38. Detach, Tribute 1, return to hand; choice headers (2026-10-02, second pass)

* **Detach** - `Cost_DetachXyzMaterials` 0x1401FF4D0 (182 rows, Kind3 ignition), check `Cond_CanDetachXyzMaterials` 0x1400FBFF0, count `Get_DetachCountForEffect_Ladder` 0x1401F7AF0 (per card id). Donor row Thunder End Dragon 9762; `"detach": N` + `Hook_DetachCount` give the card's own count; `require: canDetach`. Language `cost detach(N):` (Xyz Monsters only in the text reader).
* **Tribute 1** - `Cost_TributeOne` 0x1401FDDF0 (285 rows; was named `YGO::CardEffect::TributeMonster`). Every donor's own condition is card-specific (Tribute Doll needs a Level 7 in the hand), so `"cost": { "from": 4889 /* Share the Pain */, "check": "canTributeOne" }` - a cost may now name its check instead of running the cost card's condition; `canTributeOne` is the plugin's `Check_CanTributeOne` (a monster in zones 0-6 with the game's can-Tribute test `sub_14001D700(player, player, zone, 1)`). Language `cost tribute(1):`. "send 1 card from your hand to the GY" is read as `discard(1)`.
* **Return to the hand** - `Slot0_ReturnTargetToHand` 0x140157350; with slot 1 `Target_Generic_FromFilterRow` (Spiritualism 5246) the target is the clone's own filter: `return_to_hand(target, side, selector)`.
* Check names now live in `CheckByName` (EffectClone.cpp), shared by `require` and cost `check`.
* **Text reader:** a plain "(Once per turn:) Activate 1 of these effects" header lets its bullets through (the first one is written - one effect per kind - noted as looser; "once per turn" in it applies); any other header naming "these effects" (e.g. "Gains these effects while in the Extra Monster Zone") blocks its bullets - before, Cielo's conditional bullets were written as plain effects.

Coverage now: **770 cards, 799 effects, 34 complete**. Test 19 gained Xyz Armor Torpedo (detach 2 -> draw), Starring Knight (detach 1 -> Special Summon LIGHT from hand), Vivid Tail (return own monster), Swordsoul Auspice Chunjun (Tribute 1 -> Special Summon self).

Not done: negation (done in section 39), "add this card from the GY to the hand", summon procedures, "for each" ATK gains (continuous).

## 39. Negation is table driven too (2026-10-02)

Section 35 said what a negation may negate is per card id. It is not: it is a table.

* **`NegateTable`** (0x140B0DAB0, 375 rows `{u16 id, u16 what, u16 flags}`, sorted, IDA type `NEGATE_ROW`) is read by one function only, **`Cond_NegateTargetMatches`** (0x1400FC410, `(effect, link)` = this card's record, the chain link it would negate). It is slot 2 of Trap Jammer / Seven Tools and the effect check of Magic Jammer's delegate row (`EffectCondTable_CostAndEffect`), and the AI calls it too.
* `what`: 1 Spell, 2 Trap, 4 monster effect, or'ed (3 Spell/Trap, 7 any); `>= 3000` = one named card (Call of the Grave = Monster Reborn 4842).
* `flags`: 0x1 a Spell/Trap counts only when the card itself is activated (`Check_LinkIsSpellTrapActivationOrMonsterEffect` 0x1400A9910; monster effects always pass), 0x2 the opponent's only, 0x4 may negate its own name, 0x10 / 0x20 your / the opponent's turn, 0x40 Battle Phase, 0x400 targets exactly 1 card, 0x800 targets this card, 0x1000 / 0x2000 a target matches (per-id ladder; 0x2000 = in a Monster Zone), 0x4000 that target is yours.
* After the row, per-id extras: the Spell/Trap property of the negated card (Armor Break 3 Equip, World Suppression 2 Field, Spell-Stopping Statute / Royal Surrender 4 Continuous, Counter Counter 1 Counter, 0 Normal) and a ladder that returns 1 for any id it does not name. Trap Jammer 5921, Magic Jammer 4862, Seven Tools 4863 and Maryokutai 5260 fall straight through: for them the row IS the whole rule.
* The machine: slot 0 `Slot0_NegateActivationAndDestroy` (0x14015EA50, ~40 rows) = negate and destroy. "Negate the Summon" (0x1404C1E00) and "negate the attack" (0x1404BE980 / 0x140495780) are other machines, not done.

**Built (Yu-Gi-Oh-Effects, untested in a duel):** `"negate": { "what": "spell|trap|monster|spelltrap|any|<card id>", "opponentOnly", "yourTurn", "opponentTurn", "battlePhase", "targetsOne": bool, "activation": true (false = S/T effects too), "property": "normal|counter|field|equip|continuous|quickplay|ritual", "flags": raw }` on a clone. `Hook_NegateCheck` writes the clone's `{what, flags}` into its source's row of `NegateTable` while the clone's slot 2 runs (g_Active, effect id == From; the table is made writable once at setup, only if some clone uses `negate`) and restores it; the property is checked after the game's answer. A source must have a `NegateTable` row (checked at load). The AI still sees the source's rule (it calls the check outside a slot).

**Language:** `negate(what[, opponent][, your_turn | opponent_turn][, battle_phase][, targets_one][, effect][, property])` -> Trap Jammer 5921 (no cost of its own, so `cost discard(N)` / `pay_lp(N)` / `tribute(1)` compose onto it, and `if controls(...)` works). `cost tribute_self: negate(...)` -> Maryokutai 5260, a monster Quick Effect whose own row Tributes itself (the generic tribute_self donor is an ignition row, wrong for a chain response). The negated card is always destroyed. Block editor: a negate block (what, by the opponent, property).

**Text reader:** heads "When (your opponent activates) a <Spell Card | Trap Card | Spell/Trap Card | monster effect | card or effect | Equip Spell Card ...> (is activated)" + "negate the activation (, and if you do, destroy it)"; "while you control a X" becomes `if controls(X)`, any other while / if condition makes the sentence not read (it would be stronger than printed); "during the Battle Phase" and "that targets exactly 1 ..." are read. Over the 4,333 custom cards: **798 cards / 828 effects / 39 complete** (from 770 / 799 / 34), 0 compile errors, 29 negations. Still not read (sentences): 225 negate a face-up card's effects (continuous), 158 chain negations with a head or cost not read, 154 "but negate its effects" follow-ups, 44 Quick Effects with another cost, 15 negate an attack, 14 negate a Summon.

Test 20 (`tests\20_negation.json`, includes 19).

**Trigger reference (same day):** `build_effect_reference.py` now reads every sentence of a game card (old "When X, do Y" wording, Graveyard / Life Points / -Type), the WolfX text reader's wordings, and slot 3 costs (`COST_SLOTS`): unclassified game effects 779 -> 382; the rest are event triggers the language has no word for ("when a Zombie is Special Summoned to your field", "when this card is selected as an attack target", "if a monster you control is destroyed", Spell/Trap chain responses). Loose readings and rows with a cost never become trigger sources. Cyber Gymnast (6665) is no longer the `ignition` / `destroy_target` source: its row has a discard cost in slot 3, so every clone borrowing it made the player discard; that pair now composes. New sources: 11 (e.g. `battle_damage` draw 4226, `destroyed_by_battle` draw 7527 / burn 6327, `sent_to_grave` gain_lp 4770, `end_phase` revive 8726).

## 40. Ritual Monster <-> Ritual Spell table; WolfX "Required cards" tab (2026-10-04)

**The table.** `RitualMonsterSpellTable` (0x140AD07C0, named + typed in the IDB): 96 rows of `{u16 Ritual Monster id, u16 its Ritual Spell id}`; spell 0 = only the generic spells. `Ritual_CanSummonMonsterWithSpell` (0x1400692E0, ctx a1: a1[0] spell id, a1[1] player) needs the monster's kind frame to be Ritual AND a row for the monster, then accepts the row's spell or a generic spell hardcoded by id (Advanced Ritual Art 6996, Chaos Form 12491 = archetype 279, Machine Angel Ritual 6850 = archetype 280, 5784/5910 by attribute, ...). **A Ritual Monster without a row cannot be Ritual Summoned by anything**, so every custom Ritual Monster needs one. With a4 it stores the row index in a1[17] (read back by 0x140308E3A) and the Level total. `Ritual_GetNthMonsterForSpell` (0x14006CB20) lists the monsters a spell can summon.

Readers: 0x140069393 / 0x1400693CD / 0x14006A493 / 0x140308E3A / 0x14069C466 (`movzx/cmp [base+idx*4+rva]`, image base in a register), 0x14006CB3F (`lea r14, [rip+...]` = spell column). Row-count bounds: `cmp reg, 60h` (imm8) at 0x1400693A2, 0x1400693A7, 0x14006A4A2, 0x14006CE27; 0x14069C466 has no bound (the monster is always there).

**Yu-Gi-Oh-Effects `Ritual.cpp`.** Copies the table into memory allocated within +2 GB of the image (127 rows max: the imm8 bounds), rewrites the six disp32 operands and four bounds (operand writes, checked against the expected bytes first), and appends custom rows from cards.json: `"ritualSpell": id` on a Ritual Monster, `"ritualMonsters": [ids]` on a Ritual Spell, plus a spell-0 row for every other custom Ritual-kind card. A pair for a monster that already has a game row changes that row (a monster has one spell; the readers stop at its first row). Rows hold the ids the cards play under in the duel (`Card_GetActiveDuelSessionId`): refreshed by entry stubs on 0x1400692E0 / 0x14006CB20 / 0x140069EC0 (Detours; the stub saves flags, rax/rcx/rdx/r8-r11, xmm0-5, calls RefreshRows, jumps to the trampoline - caller-invisible). 31 custom rows at most; extra ones are logged and left out.

A custom Ritual Spell needs an effect: `as("Black Luster Ritual");` (4676 is not one of the hardcoded generic spells, so the borrowed id matches by the table row). Untested in a duel.

**WolfX.** New cards page, tab "Required cards" (`CardsPanel.Summon.cs`, `MaterialConditionDialog.cs`): Fusion kinds edit `"fusion"` (Add cards / Add condition / Edit / Duplicate / Remove / Move / Read card text; conditions = type, attribute, Level =/>=/<=, kind, archetype, any monster, each optionally "non-", combined as all-of or `{"any": ...}`); Ritual kinds pick `"ritualSpell"`; a Spell with the Ritual icon lists `"ritualMonsters"` and has a "Use Black Luster Ritual's effect" button. Xyz/Synchro need nothing: custom ids fall back to requirement code 2 (section 25).

(2026-10-04, later: the tab is now "Summoning" and is the shared `SummonEditor` - see section 41. Section 25's "Xyz/Synchro need nothing" was wrong about Xyz: the default is 2 materials, not "the card's own count".)

## 41. Synchro and Xyz requirement tables; summoning.json for game cards; one Summoning editor (2026-10-04)

**What section 25 got wrong.** The value `Get_XyzSummonRequirementCode_ByCardId` returns (default 2) is the NUMBER OF MATERIALS, not a rule code. The tables (named, typed and commented in the IDB):

| table | row | meaning |
|---|---|---|
| `XyzSummonRequirements` 0x140ACF2E0, 219 rows | `{i16 Xyz id, i16 MaterialCode, i16 MaterialCount}` | each material must match the code; Level = the Xyz's Rank (from the card); no row = 2 materials, no condition |
| `SynchroSummonRequirements` 0x140AD0BA0, 194 rows | `{i16 Synchro id, i16 TunerCode, i16 NonTunerCode, i16 MaterialCount}` | MaterialCount = the Tuner + the non-Tuners; > 0 that many or more, < 0 exactly that many; no row = 1 Tuner + 1 or more |

Codes are the Fusion code space (section 36), checked inline (not through `Fusion_CardMatchesMaterialCode`): 0 any, 1-25 race bit, 26-31 attribute, 98-516 archetype (286 = Pendulum + archetype 188), 72 Gemini (`Kind_IsCategory8`), 73 Normal, 75 Synchro (Synchro only), 83 Pendulum, 94 DARK Pendulum, 97 = a per-card `if` ladder keyed on the summoned card's id, >= 3000 a card by name (Synchro only; Xyz ignores it). Anything else is accepted. Checked against real rows: Trishula = 0/0/3 ("1 Tuner + 2+ non-Tuners"), Shooting Star Dragon = 75 / Stardust Dragon / -2 (exactly), Flower Cardian Lightflare = -5, Utopia Ray = LIGHT x3, Black Ray Lancer = WATER x2.

Readers (all binary searches; the bound is `mov r32, imm32` = rows - 1): Xyz `Get_XyzSummonRequirementCode_ByCardId` 0x14003EB90, `Xyz_GetMaterialLevelIfUsable` 0x14003F3C0, 0x1404FBE00; Synchro `Get_SynchroSummonRequirementCode_ByCardId` 0x14007C9A0, `Synchro_GetMaterialCode` 0x14007CA40, `Synchro_TunerSlotIsSynchronCard` 0x14007CB40, `Synchro_CardMatchesMaterialSlot` 0x14007D4B0, `Synchro_CanSummonWithSelection` 0x14007EF20 (the exact-count check). A whole-.text scan found no other operand into either table.

**Yu-Gi-Oh-Effects `SynchroXyz.cpp`.** Copies both tables near the image with room for every override, rewrites the 22 operands (rip-relative leas, image-base disp32s, bounds; checked first) and keeps the rows sorted. Unused rows hold id 0x7FFF (sorts last, never matches), so the bounds are written once. Rows are rebuilt with duel ids (custom card -> the id it plays under, custom card ids inside a row too) by register-preserving entry stubs on the eight reader functions, only when those ids changed. A custom Synchro/Xyz with no requirements that borrows a vanilla id removes that id's row, so it gets the generic rule instead of the vanilla card's. cards.json / summoning.json:

    "xyz":     {"material": "LIGHT", "materials": 3}
    "synchro": {"tuner": 7687, "nonTuner": "Dragon", "materials": 2, "exactly": true}

A material is absent/"any", a word Fusion.cpp reads (race, attribute, `archetype:N` up to 418, normal, gemini, pendulum, synchro), a raw code, or (Synchro) a card id. Shared code moved out of Ritual.cpp: `CodePatch.cpp` (near-image memory, operand writes, entry stubs) and `SummonJson.cpp` (both files' entries, id maps).

**summoning.json (game cards).** `Yu-Gi-Oh-Ex\summoning.json` = `{"cards": [{"id": <game id>, "name", <the cards.json keys>}]}`, read after cards.json by Fusion.cpp, Ritual.cpp and SynchroXyz.cpp (later wins). So a game card's Fusion recipe, Ritual pairing, Synchro or Xyz requirements change the same way a new card's are set. Listed in content.json under Yu-Gi-Oh-Effects.

**WolfX.** `Content/SummonEditor.cs`: one "Summoning" tab used by the New cards page (`CardsPanel.Summon.cs`, the card's Extra) and the Card Manager (game cards: starts from the game's requirements, `summon_tables.json` made by `docs/effect-scripts/build_summon_tables.py`; the first change copies them into summoning.json; "Use the game's" drops the entry). Synchro: Tuner / non-Tuner pickers (any, Type, Attribute, archetype, kind, card), total materials, "exactly"; Xyz: material picker and count; both "Read card text" (`ExtraDeckMaterialText.cs`) and "Use the generic rule". A game code the pickers cannot name (97) is kept and shown as "Game rule". Link Monsters: see section 42 (they ARE table driven).

Not tested in a duel. Tests to run: a custom Xyz with `"xyz": {"materials": 3}` (2 materials must no longer be offered); a custom Synchro with `"tuner": "DARK"`; a game Synchro changed in the Card Manager. Console lines: "Xyz: table moved", "Synchro: table moved".

## 42. Link material requirements table (2026-10-04)

The user found it: file offset 0xBC94D0 = `g_LinkMaterialRequirements` 0x140BCA0D0, 281 sorted rows of `{i16 TargetLinkId, i16 Requirement[3]}`
(type `LinkMaterialRequirement` in the IDB). No row = any monsters; the number of materials is the Link Rating's business (not in the table).

| slot | meaning (Link_CardIsValidMaterial 0x1405B2500) | examples |
|---|---|---|
| Requirement[0] "condition" | every material must match | Decode Talker 74 (Effect) = "2+ Effect Monsters"; 96 = "except Tokens"; Link Disciple 59 = Level 4 or lower |
| Requirement[1] "material" | every material must match too (same test) | Day-Breaker 14445 = 18 ("2 Spellcaster monsters"); Honeybot 23 Cyberse; Link Spider 73 Normal |
| Requirement[2] "including" | when the last material is picked, at least one of the selection must match | "including a Link Monster" (95), an archetype, a card name |

Konami's placement (from the user, matches every row checked): what an "N+" line names goes in slot 0 ("2+ Effect Monsters" = Decode Talker 74),
what an exact "N" line names in slot 1 ("1 Normal Monster" = Link Spider 73, "2 Spellcaster monsters" = Day-Breaker 18); "except Tokens" and
Levels in slot 0. The check itself tests both slots the same way on every material - the "+" / exact count is not in the table (Link Rating
rule). WolfX's captions and "Read card text" follow that placement.

Codes are the Fusion space: 1-24 Type, 26-31 Attribute, 32-43 Level =, 44-55 Level >=, 56-67 Level <=, 73 Normal, 74 Effect, 82 Xyz, 83
Pendulum, 95 Link, 96 not a Token, 97 per-card `if` ladder, 98-516 archetype; in "including" also 75 Synchro, 90 Tuner, >= 3000 a card by name.
Any other code in "including" can never be met (the card becomes unsummonable), so the plugin refuses it.

**Why IDA missed it at first:** the player's check reads the table through the image base (`lea r11, image base` then
`movsx edx, word [rcx+r11+0BCA0D0h]`), which IDA does not list as a cross-reference; only the AI's reader `Link_GetMaterialRequirement`
0x1405B1E60 (rip-relative lea) showed. A capstone scan of every instruction for operands into the table found both. AI users:
`AI_ExtraDeck_GetMaterialTargetBits` 0x1404FBE00 and the Extra Deck planner sub_140485830 via `MaterialCode_ToTargetBits` 0x140294360.

**Yu-Gi-Oh-Effects `SynchroXyz.cpp`** moves it like the Synchro/Xyz tables (third `Table`, sites: rip lea 0x1405B1E78, bounds 0x1405B1E81 and
0x1405B259E, image-base disp32 at 0x1405B25C6 (+5) and 0x1405B25EB (+3); entry stubs on both readers). cards.json / summoning.json:

    "link": {"condition": "effect" | "notToken" | "level<=4" | "xyz" ..., "material": "Spellcaster" | "LIGHT" | "archetype:12" ...,
             "including": "link" | "synchro" | "tuner" | "Cyberse" | "archetype:N" | <card id>}

An empty object (or all three "any") on a game card takes its row away (any monsters); a custom Link without "link" gets no row.

**WolfX** Summoning tab, Link section: "Every material is" / "Every material is also" / "Including at least one" pickers (`MaterialCodePicker`
modes LinkEach / LinkIncluding: Level choices, Effect / not a Token / Xyz / Link kinds, a card for "including"), "Read card text"
(`ExtraDeckMaterialText.Link`), "Any monsters", and "Use the game's" for game cards (summon_tables.json now has "link", from
build_summon_tables.py). Not tested in a duel: Day-Breaker with 2 non-Spellcasters must not be offered; a game Link changed in the Card Manager.

## 43. Overriding a game card's effect (2026-10-04)

`Yu-Gi-Oh-Ex\effects.json` (merged over the mods like every other file), read by Yu-Gi-Oh-Effects `EffectClone.cpp` after cards.json:

    {"cards": [{"id": 4041, "name": "...", "overridden": true, "effectScript": "...", "effectClone": {"from": 4844, "draw": 3}}]}

- `"overridden": true` is what makes an entry live; an entry without it is ignored by the game.
- `"effectClone"` is exactly a new card's (from / draw / filter / parts / before / cost ... everything in sections 22-39). No `effectClone`
  (or null) = the card has **no effect**: it clones Blue-Eyes White Dragon 4007 (a Normal Monster, in no table, list or ladder).
- The card is a clone keyed by its **own** id (`g_Clones`, plus `g_Overridden`), so every hook that serves a custom clone serves it: the four
  effect tables (Hook_GetEntry wraps the source's row), the hooked ladders and id tests, the trigger lists, the summon / move evaluators,
  phase handlers and named-card offers (`EngineIdFor` returns a game id as it is). There is no source-id lending: the card keeps its id.
- **Guard:** while a clone runs as its source (slot thunk `g_Active`, ladder / evaluator `g_ShowingSource`), a lookup of that source is the
  game's card even when the source is itself overridden (`ShownAsSource` in `Find`); `Hook_OfferByRef` clears it so an override whose source
  is the card itself still offers its own wrapped row.
- **Limits:** lists and ladders that name the card itself and that no hook covers (EffectCondition_CheckCardUsableAtTiming's inline branches,
  continuous effect tables, AI tables) still answer for the card's own effect; a known trigger event that does not fit the override's trigger
  is refused (as for clones). Not tested in a duel.

**WolfX** Effects page, "Show": new cards (cards.json, as before) / game cards / game cards I changed. A game card shows its own effect as
the Effect library reads it (effect_reference.json); Compile overrides it (an empty script asks, then means no effect); "Use the game's
effect" drops the entry. The Effect library's "Use" fills the selected game card too. The page is now an IContentPanel saving effects.json
(`GameEffectsFile`, `GameEffectCard` in Content\GameEffects.cs); content.json / mods map effects.json to Yu-Gi-Oh-Effects.
