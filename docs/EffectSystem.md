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
* The old table-relocation code in Yu-Gi-Oh-Effects (`Effects_FunctionTable`, `Table_140B15B30`, `CardsThatMakeYouDraw`, with its `Memory`/`File` helpers) was **removed** (2026-09-28; it is in git history): it copied and zeroed the game's tables and patched instruction bytes, and had a wrong size. The abandoned C++ ANTLR parser (`Script/EffectScript.*`, generated files) went too; `Script/EffectScript.g4` stays (WolfX generates its C# parser from it).
* `EffectDispatch.cpp`: passthrough logger on `EffectCondition_CheckCardUsableAtTiming`.
* `Fusion.cpp`: custom fusion recipes.
* WolfX's "Effects" page compiles EffectScript (`Script/EffectScript.g4`) to `effectClone` on the card (at the time of this note the v0 compiler only wrote a JSON `effect` the runtime didn't read; `effectClone` replaced it).

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
| 0 | RESOLVE - a per-call state machine. The current step is a global (0x14349C13C / 0x14349C148+4); the function does one step and returns the next (128 start/check, 127/126 prompt, 125 pick, 124 chain/animation, 120-122 finish, 100 apply). | `Slot0_AddFromDeckToHand_StateMachine` 0x14016D5A0 (622 rows), `Slot0_Generic_TargetedEffect_StateMachine` 0x140163830 (402), `Slot0_AlterLifePoints` 0x14015E570 (481), `Effect_DrawCards_Resolve`, `Effect_RunSlot1Action_Driver` 0x140156E70 (destroy-all family), `Slot0_DestroyFirstTarget` 0x140156D40 |
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

**"Target 1 X; destroy it"** (Remove Trap 4838, Shield Crush 6003, Anti-Fusion Device 7011, Night Beam, Typhoon...): slot0 `Slot0_DestroyFirstTarget` 0x140156D40, slot1 `Target_Generic_FromFilterRow` (row driven), slot4 prompt. So the existing `filter` override works; `"target": true` (adds the targetable bit 0x400: base 0x1414 for monsters) and `"kind": "spelltrap"` (base 0x41440, MST's flags) select the "choose one card" form. Vanilla with hard-coded flags instead of a row (Mystical Space Typhoon 4909 slot1 = constant `0x41440`) would need a hook on `Filter_CardAtZoneMatchesFlags` limited to slot1 calls (not needed yet). Language: `destroy(target, [side,] [monster|spell|trap where ...])`. Test file `tests\8_targeted_destroy.json`.

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

The game ships effect code and id lists for cards it has no card data for: 198 ids between 14969 and 15234 have rows in the four effect tables (and `word_140BF8BC0`, a "has a hand effect" list, holds 14996-14999 ...). Custom cards used to be numbered from 14969, so each landed on a ghost: a custom Abyss Shark (14996) was offered "activate from the hand" because 14996 is in the hand-effect list. Custom ids now start at **15300** (`kFirstExtraCardId` in Yu-Gi-Oh-Cards/Card.h, Yu-Gi-Oh-Effects, WolfEx `CardsPanel.MinId`, `-StartId` of Delta.ps1 / Cards.ps1); the cap was 19999 (the save's 20,000-byte card table, 4,700 custom cards) until 2026-10-19 and is now 65534: Yu-Gi-Oh-Core's CardTable.cpp hands the game a 65,536-byte card table (ids past 19999 kept in the save extension, tag "cards.high") and Yu-Gi-Oh-MoreCards widens the game's 14 `mov reg,4E20h` table bounds to 0xFFFF. Ids above 16383 borrow a vanilla id per duel as before; the save holds real ids only.

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

**Fourth duel test (2026-10-01):** CONFIRMED - Superheavy Samurai Drum-style leave-field trigger (Flint Cragger revive, `Hook_RowSearch`), Tributing a borrowed-id monster (safe borrow ids), Tribute Summon -> "When Normal Summoned" trigger, sent from the field to the GY (Botanical Girl source, `TriggerIdList_SentFromFieldToGrave`), ignition with once per turn (Aquaactress Tetra source, table 3). One fault: a clone of **Poki Draco (9648)** added a card without showing the list (slot 0 went 128 -> 125 -> 100 in 3 ms instead of 128 -> 126 -> 125 -> 100). Poki Draco's list-filter row has param -1 ("a card with this card's own name"); the search machine reads that row itself, outside the `deck` override, and auto-picks. Rule: **never borrow from a card whose list row param is -1.** `build_effect_reference.py` now skips them, so WolfEx's `normal_summoned` search source is Gem-Armadillo 8897 (trigger_sources.json regenerated into `Tools\WolfX\Content` and `Binaries\Debug\Tools`; the script's default OUT path is `Tools\WolfX\Content` now).

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
| 12 | **flipped face-up** too: `Duel_ChangePosition_OfferFlip_Ev12or13` (0x140084E50) offers event 12 + flag, FLIP monsters only (seen in-duel 2026-10-14: Book of Taiyou on Bunny Ear Enthusiast) | position change | `Kind_IsFlipMonster` | fits "flip" since 2026-10-14 (it was unknown, so a FLIP + Standby card got the choose-an-effect prompt) |
| - | **one-effect clones are offered only for their own trigger** (2026-10-14): `Hook_OfferByRef` drops an offer whose event (ref bits 25-30) is known and does not fit the clone's trigger. A lent source id brings every id-keyed trigger of the source along: Black Mamba (as Edge Imp Chain 11688) dealt its 300 when Dark Hole sent it to the GY. | | | fixed, untested |
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

**Opting out (2026-10-25):** `"effectOnly": true` on the clone (EffectScript `as(...) effect_only`) turns lending off (`SingleEffectSource` in
`Dependencies/Yu-Gi-Oh-Ex/EffectSourceLending.h` returns 0), so the card keeps its own kind, stats and identity and runs on the hook path. Made for a
monster that copies a Spell's or Trap's effect (Ojama Trio): lent the Trap's id, every id-keyed rule treated it as that Trap. Such a monster also needs
a monster trigger to use it: `on <trigger>: as(N)` compiles to a composed clone (`from` = a monster with the trigger, `actionFrom` = N, section 33).
Built and covered by Tests/EffectRouting (lending policy, compiler, blocks round trip); not played in a duel yet.

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
Source-id lending (section 32) applies only to single-effect clones. A card with non-empty `parts` keeps its custom id (or a scratch id above
the duel engine's 14-bit limit), so the first source's properties and hard-coded identity cannot decide how the other effects behave.

**Pendulum order fix (2026-10-12):** raw clones without `trigger` metadata choose a source with a real row in the summon or leave-field table.
Row routing prefers an exact source row over the engine's generic Spell/Trap fallback. Wrapped rows are cached per part so their overrides
cannot collide. The Stratos (6784) / Pot of Greed (4844) combination is covered in both orders by the production-hook regression tests in
`Tests/EffectRouting`, including slot resolution and draw counts. Those tests mock vanilla engine lookups; they do not replace live duel testing.

WolfX supports `as(6784); also as(4844);`, its reverse, multiple named `effect { as(...); }` blocks, and mixtures of cloned and scripted effects.
Clone blocks now stack with other effect blocks and all are preserved when converting between script and blocks. These are separate effects,
not a resolution chain: `then` still describes actions within one effect. Multiple sources in different tables or with distinct supported
events can be routed independently. Trigger and activation offers also enumerate compatible effects sharing a table and event. A per-part
tag in the queued record preserves the selected effect through lookup and resolution, while native calls see their original sub-effect number.
Duplicate suppression distinguishes sibling effects but still rejects repeated offers of the same effect, and respects native queue capacities.
Up to 127 independently selectable parts are supported. Independent per-effect use limits are not added; native card-wide limits still apply.
Composed actions omit Stratos's unrelated native HERO choice menu unless an explicit cost or slot override requires it; raw clones retain it.

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

A material is absent/"any", a word Fusion.cpp reads (race, attribute, `archetype:N`, normal, gemini, pendulum, synchro), a raw code, or (Synchro) a card id. Custom archetypes (`archetype:419` up, codes 517..2496): the Synchro, Xyz and Link checks test `code-98 <= 0x1A2` before Is_CardInNamedArchetype, and a code past that falls through (the Synchro check then accepts any monster). When a requirement names one, `WidenArchetypeChecks` raises the four bounds (0x14007D8C9, 0x14003F761, 0x1405B295F, 0x1405B35CC) to 2398, and the Cards plugin's Is_CardInNamedArchetype hook answers the custom codes. Two Tuners: `"tuners": 2` (1-2, fewer than `materials`, which counts every Tuner). Synchro_GetTunerCount (0x14007CAF0) is the game's own switch (2 for Red Nova Dragon, Vylon Omega, Hot Red Dragon Archfiend King Calamity, Tyrant Red Dragon Archfiend, Crystron Quariongandrax); Synchro_CanSelectMaterial makes the first that many picks Tuners (row Tuner code) and the rest non-Tuners. Effects detours it for every custom Synchro (1 by default, so a borrowed 2-Tuner id isn't inherited) and for game cards with "tuners". The AI reads the same function; an inlined copy at 0x1401E010C only serves card 12540's effect. Shared code moved out of Ritual.cpp: `CodePatch.cpp` (near-image memory, operand writes, entry stubs) and `SummonJson.cpp` (both files' entries, id maps).

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

## 44. Continuous locks: Jinzo, Royal Decree, Spell Canceller, Skill Drain (2026-10-11)

The effect library includes curated lock templates from `docs/effect-scripts/lock_reference.py`, even when the original card has no effect-table row.
Spell Canceller is `lock(spell);`. Jinzo, Royal Decree, Skill Drain, and Silent Swordsman LV7 are marked partial with notes for exceptions,
activation-order behavior, costs, or additional restrictions the template does not reproduce. These templates are not activated-effect trigger sources.
The generator merges them on every refresh. Run `python docs/effect-scripts/test_lock_reference.py` to check the shipped entries and refresh behavior.

Jinzo (4758) "Trap Cards, and their effects on the field, cannot be activated. Negate all Trap effects on the field." has no row in any effect table. The
engine rebuilds a **lock state** (an int struct on the caller's stack) whenever it needs it, in `Duel_BuildContinuousLockState` (0x14010EAA0), from
**hard-coded card ids**. Six callers use it, among them:

* `Duel_IsActivationLocked` (0x140111690, player, zone, card): the card can't be activated.
* `Duel_ApplyLockNegatedBits` (0x140111440): sets or clears each zone's "negated" bit (UNK_02 0x80). Monsters go through
  `Duel_IsMonsterEffectNegatedByLocks` (0x1401103A0) and Spell/Trap zones through `Duel_IsSpellTrapNegatedByLocks` (0x140110B50).

| Slot | Meaning | Vanilla cards that set it |
|---|---|---|
| +0 / +4 | player 0 / 1's **Traps** can't be activated and are negated. 1 = all; a larger value = only cards activated after that activation number | Jinzo 4758 face-up in a Monster Zone (both players; with Amplifier 4819 equipped only the opponent), Royal Decree 4866 (threshold), 7580, 7114 face-up, 5984, markers 8223 / 9592 |
| +12 / +16 | player 0 / 1's **Spells**, the same way | Spell Canceller 5594, Silent Swordsman LV7 6166, S/T 4960 / 5989 |
| +20/+24, +28/+32 | player 0 / 1's **monster effects** negated (compared with each card's activation number) | Skill Drain 5740 |
| +88 / +92 / +96 / +100 | Continuous Traps / Continuous Spells / Field Spells / Equip Spells can't be activated (no side) | 6598, markers 4945..4942 |
| +108 | everything except Normal / Ritual Spells, and monster effects in some zones | 10298 |
| +112 / +116 | player 0 / 1 may activate anyway (exemption) | 11274 |
| +52..+84 | narrower monster locks: counters (6801), DARK (8179), archetype 70 (8313), Fusion/Synchro/Xyz (12316), Extra Monster Zone (14476), The Monarchs Erupt | |

A lock card counts only while it is face-up, isn't marked disabled, and `Duel_IsCardExemptFromLocks` (0x14010E0E0) is 0. That check lists the
per-card immunities.

**Built (Yu-Gi-Oh-Effects `Lock.cpp`, untested in a duel):** a hook on the builder runs the game's version, then adds every card with a lock that
is face-up in its zone and not exempt.

* **Where a lock comes from:**
  * `"lock"` on a cards.json entry, or inside its compiled `"effectClone"` (the script statement);
  * effects.json: a `"lock"` of its own (the game card keeps its effect), or inside an overridden card's `"effectClone"`.
* **Keys:** `{ "traps", "spells", "monsters", "continuousTraps", "continuousSpells", "fieldSpells", "equipSpells", "side": "both|opponent|you", "zone": "monster|spelltrap|any" }`.
  * "traps" defaults to true.
  * side is relative to the card's controller.

**Language:** `lock(trap);` (Jinzo), `lock(spell);` (Spell Canceller), `lock(monster, spelltrap_zone);` (Skill Drain), `lock(trap, opponent);`,
`lock(continuous_trap, field_spell);`.
* Words: trap, spell, monster, continuous_trap, continuous_spell, field_spell, equip_spell; both, opponent, own; monster_zone, spelltrap_zone, any_zone.
* One lock per card. It goes before or after any effect, and a script may be only a lock.

**Every card the builder names (re-read 2026-10-14)** is in `Tools/WolfX/Content/LockCatalog.cs` and shows in the Effect library under its own **Continuous locks** root, by kind. **Narrow monster locks are lock words now** (`Duel_IsMonsterEffectNegatedByLocks` 0x1401103A0 reads one int each; Lock.cpp `kFilters` writes the lock card's activation number into it): `light` (+56), `a_counter` (+52, counter 6800), `except_koaki_meiru` (+60, archetype 70), `from_main_deck` (+64, UNK_02 0x80000), `from_extra_deck` (+68, UNK_02 0x200000), `fusion_synchro_xyz` (+76), `extra_monster_zone` (+80), `except_tribute_summoned` (+84, UNK_02 & 8), `summoned_this_turn` (+36/+40, UNK_03 & 4), `defense_position` (+104). One per lock, both players only; JSON `"monsterFilter"`. Blocks: the Lock block's "monster effects of only"; the Lock dialog's picker. The this-turn locks (flag markers: Trap Stun 8223, Gishki Emilia 9592, 6208, 12013, 13551, 5753, 7275, 8313, 4799, 11096, 14122, 11994, 10298, 4942-4945), Powersink Stone 9155, Moon Dance Ritual 10821 and the exemptions (11274, 4819, 9156) are keyed on the card id, so the library template is `as("Card")` (source-id lending). 15148 is a not-in-game id. All untested in a duel.

**WolfX:** a "Lock" block category, and a **Lock...** button on the Effects page. The button's dialog has presets (Jinzo, Spell Canceller,
Skill Drain) and writes the script line. For a game card that keeps its own effect, the lock goes into effects.json as `"lock"` instead.

## 45. Several effects of the same kind: the player chooses (2026-10-11)

**How the game does it.** Cards such as Rainbow Dragon (7171, two ignition effects) or Cyber Raider (4760) have several CONSECUTIVE rows with the same id in one table. `Effect_GetRowIndexForRecord` (0x1400DE070, called by `Get_EffectTableEntryForCard`) picks the row: for the 237 ids of `EffectChoice_IdList` (0x140AF6A70) it is record word19 (+0x26) - 1 (word20/21 for 14940/15132/14755); `EffectRowByEvent_List` {id, event} picks row 1 when word3 is the event; `EffectRowByZone_List` {id, zone} by word2; then a per-id ladder. The rows of such a card share slot 2 `Slot2_EffectChoice_AnyAvailable` (0x1400FB8D0) and slot 3 `Slot3_EffectChoice_SelectThenCost` (0x1401FCB00):
* `EffectChoice_GetAvailableMask` (0x1401FA890): bit n = effect n can be used now (`EffectChoice_ConditionTable` 0x140B24E00 {id, cond1..3}, run with word19 = n+1, plus per-id ladders).
* slot 3, stage counter at 0x14349B580+754: 0 -> mask; a single option is auto-picked for some ids, else `EffectChoice_ShowPrompt` (0x14005F630, text 259) lists `EffectChoice_GetCount` (0x1401FA6D0: 2 unless listed) options labelled by `EffectChoice_GetLabel` (0x1401FA760: `EffectChoice_LabelTable`, default id + (n << 14)); returns 0 (call again). 1 -> the answer (DuelState +0x3802, 0-based) -> word19 = answer + 1. 2 and on -> the chosen effect's cost (`EffectChoice_CostTable` 0x140B223D0).
* Once per turn per effect: class 5 (key 1001) and class 6 (key 1002) are two independent limits (section 20); some cards key 1001 by the effect number.

**Custom cards (Yu-Gi-Oh-Effects EffectClone.cpp).** When more than one part answers a lookup (`Candidates`: the parts whose source has a row, narrowed to the ones whose trigger fits the event), the lookup returns a select row (`ChoiceRow`): the first candidate's row with no limit class, slot 1 empty, slot 2 `ChoiceSlot2` (any effect usable: `ComputeChoiceMask` runs the full `Check_CanActivate_Driver` with each effect chosen in turn - limit class, condition, targets), slot 3 `ChoiceSlot3` (one usable effect -> chosen; else the game's prompt with the mask, `Hook_ChoiceCount` answering the number of effects; the next call reads the answer) and then the chosen effect's own slot 3 (cost). The choice is kept by the plugin per (custom id, card instance word11) in `g_Choices`, not in word19 (a borrowed source's machines may use that word), and dropped when the chosen effect's slot 0 finishes. After the choice every lookup (`RouteByRow`, ladders, slots) means the chosen effect. The second once-per-turn effect of a card gets limit class 6. Built 2026-10-11, untested.

**Open:** the prompt's option labels are the game's default (custom id + (n << 14)), not checked in a duel - if they show nothing useful, hook `EffectChoice_GetLabel` (done, section 47). A choice cancelled before it resolves keeps the card on that effect until it resolves once. ~~"Once per turn: activate 1 of these effects" gives each bullet its own limit~~ - fixed by limit groups, section 58.

**WolfX:** `CardTextTranslator` no longer drops a second effect of the same kind, and keeps every bullet of "activate 1 of these effects".

## 46. Summon conditions: how the card was summoned (2026-10-11)

**The summon event record** (0x14349C560, the one `Eval_EventTriggersCardEffect_ByCardId` reads; decoded from its per-card checks and the cards they belong to):
| offset | meaning | evidence |
|---|---|---|
| +0x0A (u16) | zone the monster came from: 13 hand, 14 Extra Deck, 15 Deck, 16 GY, 17 banished, 7-11 Spell & Trap Zones | Gilasaurus / Archfiend Commander test 13; Molten Zombie, Felgrand, Tengu 16; Esper Girl, Mental Seeker 17; Impcantations 15; Nebula / Cosmo Neos 14; Vision HEROs 7-11 |
| +0x10 (u16) flags | bit 0 = Summoned properly as its kind (Fusion / Synchro / Xyz / Ritual / Link Summon); 0x40 = Pendulum Summoned | Neo Galaxy-Eyes, Five-Headed Link Dragon, Saffira (bit 0); Aether, D/D/D Supreme King Kaiser, Magician of Hope (0x40) |
| +0x12 (u16) | id of the card whose effect Special Summoned it (0 = not by an effect); compared with the card itself ("by its own effect": the Aesir), archetypes, Spell (`Check_LinkIsSpellEffect`) | |
| +0x30 (u16) | number of Tributes of a Tribute Summon (event 7) | Moisture Creature / Gilford / Barbaros (>= 3); the Monarchs list needs it non-zero |

**Runtime (EffectClone.cpp).** `"summonCondition": { "from": [zones], "by": "effect" | "not_effect" | "own" | "spell" | "trap" | "monster" | {"card": id} | {"archetype": code}, "proper": true, "pendulum": true, "tributes": N }` on an effect (or part). `Hook_EventEvaluator`, for events 7 and 9, picks the first summon effect whose trigger fits the event and whose condition the record meets (`SummonMatches`); when the card has conditions and none is met, the evaluator does not run for it (no offer). "own": the record holds the id the clone's effect ran under (a part's source / action card) or the card's own id. Spell / Trap = card type 13 / 14; monster = `Kind_IsMonster`; archetype = `Is_CardInNamedArchetype`. The source the compiler borrows for the trigger must fire on every summon of that kind (Sacred Crane, Genex Blastfan ...), the condition narrows it.

**Language:** `on special_summoned(from grave, by effect): ...`, `on normal_summoned(tribute 2): ...`, `on special_summoned(xyz): ...`, `on special_summoned(by "Dark World", from hand): ...`, `pendulum`, `by own`. Only with normal_summoned / special_summoned / summoned / normal_or_special_summoned. Blocks: the Effect block's "if summoned: from / by / as" row. `CardTextTranslator` reads "If this card is Special Summoned from the GY by a card effect", "If this card is Xyz / Pendulum / Tribute Summoned" ... exactly (it used to read them as any Special Summon).

**Built 2026-10-11, untested in a duel.** Open: a choice between two summon effects (section 45) does not look at the conditions. Next family: the leave-the-field conditions (the move ref already carries from-zone, from-field, by-battle, section 29).

## 47. Hand size limit, choose-an-effect labels, except-this-card with a name test (2026-10-14)

**Hand size limit.** The turn runs a table of step functions (0x140ACD6D8, index `field_37BA`): 0 `sub_140147A00`, 1 Draw Phase, 2 Standby Phase,
3 Main Phase (Battle inside), 4 `Phase_EndPhase_RunHandlers` (0x1401558E0), 5 `Duel_TurnStep5_CheckTurnOrTimeLimit` (0x140147DD0). The End Phase runner
walks the 552-row handler table 0x140B12310; the row `EndPhase_DiscardToHandLimit` (0x140155830) moves the step to 11 and, when the hand
(`PlayerState.iNumberOfCardsInHand`) is over `Duel_GetHandLimit(player)` (0x14005D120), sets up the discard-N prompt. `Duel_GetHandLimit`: 6;
none (0xFFFF) while Infinite Cards is face-up; 5 while the OPPONENT has Enervating Mist (or flag marker 5535 on the player); 3 while Finite Cards
(12341) is face-up unless the player has Silent Wobby. Callers: the End Phase row, `sub_140284300`, `sub_14050FEC0` (AI / UI) - one hook covers all.
**Built (Lock.cpp, untested):** `"handLimit": {"limit": N, "side": "opponent" (default) | "you" | "both", "zone": "monster" | "spelltrap" | "any"}` on
a card or in its effectClone; `Hook_HandLimit` lowers the limit while the card is face-up in its zone and not negated (the lock state is built
with our locks, so a custom Skill Drain stops it). Language `hand_limit(3);` `hand_limit(4, both, spelltrap_zone);`; BAB "While face-up" category.

**Choose-an-effect labels.** `EffectChoice_GetLabel` (0x1401FA760, id, n): sorted {card, label} rows at 0x140B241B0 (labels < 3000 = DLG text
indices, the duel prompt table, 689 entries); else id + (n << 14). `EffectChoice_ShowPrompt` keeps them as u16. `Duel_PromptText` (0x14077AE30):
< 3000 = `Get_DlgText`; else the text after the (n+1)-th U+25CF bullet of card (id & 0x3FFF)'s description - which is why a custom card showed
blank CHOICE 1 / 2. **Built (EffectClone.cpp, untested):** `Hook_ChoiceLabel` answers card | n << 14 for our prompt (a lent source's table labels
never leak in), `ChoiceSlot3` records the candidates' labels per card, `Hook_PromptText` answers them (game std::wstring filled with the game's own
assign 0x140751310). The compiler gives every effect of a several-effect card a `"label"`: the `effect "Name"` name, else a summary
("Draw 1 card", "Inflict 500 damage, then ..."). At most 4 options (2 bits of n).

**Except this card with an archetype / card filter.** A match-list entry is the card's dword (`sub_14003D120(player, zone, index)`), id = low 14
bits. When the row's name test is taken, `RunDeckScan` drops the matches named like this card (`j_Card_IsSameName` 0x1407EBA00) after each scan
(`ExceptSelfAfter`). The ~58 direct row-index callers (Get_DeckFilterIndexForEffect) still see the plain row.

## 48. "For the rest of this turn" locks: lock_turn(...) (2026-10-14)

`Action_AddTurnFlagMarker` (0x140177EE0) is slot 0 of the this-turn cards: it pushes MsgQueue 0x35 FlagMarker_Add with arg2 = the effect
RECORD's card id and arg3 = the current turn (Trap Stun 8223, Gorgon's Eye 4799, Magic Deflector 10298; Koa'ki Meiru Hydro Barrier 8313 kind 2
= until the next Standby Phase; Dark Contract with Errors 12013 does its own result first). `Duel_BuildContinuousLockState` reads the markers by
id (section 44: 8223 -> both players' Traps, 10298 -> +108, 4799 -> +104). A composed custom effect runs its action as the action card
(impersonation), so the marker it adds IS that card's: no hook needed.

**Built (compiler + BAB, untested in a duel):** `lock_turn(trap | spell | defense_position);` = `{"from": 10298 (Magic Deflector's Normal Spell
row), "condition": "always", "actionFrom": <8223 | 4799>}`; with a trigger the usual trigger x action composition (any monster with the trigger,
actionFrom the marker card). BAB: "for the rest of this turn, negate ..." in Actions. The Effect library's Trap Stun / Gorgon's Eye / Magic
Deflector now use it (complete). Others of the family (Armored Glass 4942 ... Metal Detector 4945, White Howling, Triangle Ecstasy Spark) use
other slot functions with conditions - not mapped yet.

## 49. Engine primitives for our own resolver (research, 2026-10-14)

The long-term BAB goal (fully custom effects from parts) needs a resolve step of our own that calls the engine's actions directly. Found so far:

| primitive | address | call | notes |
|---|---|---|---|
| draw N | `Draw_ExecuteDraw` 0x1400A1C10 | (record, player, count, flags) | whole draw in one call: draw-blocking cards (Protector of the Sanctuary, markers 9279 / 12005 / 13547 / 14144), deck -> hand, UI messages, on-draw triggers; returns -1 when the deck runs out, 1 = drew |
| effect damage | `Damage_Player` 0x14009DE50 | (player, amount) | MsgQueue 0x24 (arg2 3 = effect damage) + animation 45; the LP handler applies it |
| turn marker | `Action_AddTurnFlagMarker` 0x140177EE0 | slot 0 | MsgQueue 0x35 FlagMarker_Add, keyed on the record id (section 48) |
| to GY / banish | `Move_NonFieldCardToGraveOrBanish_Ev31` 0x1400A6F40, `Move_HandCardToGraveOrBanish_Ev32` 0x140088D90, `Move_FieldCardToGraveOrBanish_Ev33` 0x1400A7750 | (player, zone, ..., index, ..., by card) | fire the leave / sent-to-GY events (section 29) |
| destroy zones | `Action_DestroyZoneMask` 0x1400D5150 | | |
| hand limit | `Duel_GetHandLimit` 0x14005D120 | (player) | section 47 |

Immediate steps (draw, LP, markers) can run back to back inside one slot 0 call, the way chaining stage 1 already runs draw / LP "before" steps.
Interactive steps (choose a card, target) are step machines (section 12) and still need the game's own machines - the sequencer of section
"Chaining" in EffectLanguage.md. Next: add-to-hand from the GY / Deck without a selection, Special Summon by id, change ATK.

## 50. The plugin's own resolver: "steps" (2026-10-14)

First piece of "custom effects from parts": an effect's slot 0 can be the plugin's `Slot0_RunSteps` instead of a game card's machine. It runs
`"steps": [{"op": "draw" | "gain" | "damage", "n": N, "who": "own" | "opponent" | "both"}, ...]` in order with the game's functions -
`Draw_ExecuteDraw` (0x1400A1C10, record, player, count, 0), `LP_ApplyGain_Internal` (0x1400A15F0, record, side, amount), `LP_ApplyDamage_Internal`
(0x14009E4C0) - for the record's player `record[1]` (as `LP_ApplyChangeFromParamTable` reads it) and/or the other one, then returns 0 (finished:
all of them are immediate). The row (activation: Spell / trigger) still comes from `from` (Pot of Greed 4844 for a Spell, the trigger composition
otherwise) with `"condition": "always"`.
Compiler: a chain made only of draw / gain_lp / burn / damage, or one that names a player (`draw(1, opponent)`, `gain_lp(500, any)`, new
`damage(N[, own | opponent | any])`), compiles to steps; a single plain draw / gain_lp / burn keeps its game card (unchanged, tested). Mixing a
player-named step with an interactive action (search ...) is an error for now. BAB: draw / gain LP / damage blocks have a "whose" dropdown.
Built 2026-10-14, untested in a duel (test 8). Not yet: Resolution_BeginScope around a both-players LP change (the game does it for two-sided
changes; watch the LP animation), card moves (field cards: `Action_DestroyZoneMask` queues ONE pending move, so only as a last step; GY/Deck -> hand
mover not found yet), choices/targets (step machines).

## 51. Effects activated from the GY: on in_grave (2026-10-21)

**How the game offers them.** `Check_CanActivateKind3AtZone` (0x140107930): a card past the field (zone > 12: hand 13, GY 16, banished 17) gets record word4 = 2, so its effect is looked up in `EffectTable_Kind2` (0x140B85A50, the GY / leave-field table), and goes straight to the chain-speed and offer gates. There is **no id list for the GY**; only the hand has one (`Id_HasHandEffect` 0x140743FA0: three lists, read by `HandCard_GetCommandFlags` and the offer gate). A field card gets word4 = 3 after `Has_Kind3EffectEntry`.

**The rows.** Every game card activated from the GY or hand has a Kind2 row with event (word at +2) **0** and slot 2 `Slot_Delegate_ToSecondaryTable_FE560` (0x1400FE620); slot 3 is what it pays:

| Card | From | Slot 3 (cost) |
|---|---|---|
| Quillbolt Hedgehog 7701, Glow-Up Bulb 9095 (event 9), Spore 8795 (event 9) | GY | none / Spore 0x1401F99D0 |
| Mezuki 7359, Bujingi Turtle 10621, Breakthrough Skill 10437, Skill Prisoner 10964 | GY | `Cost_BanishSelfFromGrave` 0x1401FA670 |
| Swift Scarecrow 9078 | hand | discard self 0x1401FCEB0 |
| Effect Veiler 8933, Ghost Ogre 11708 | hand | send self from hand 0x1401FF020 |

**Language.** `on in_grave: <action>` ("If this card is in your GY: You can ..."): `from` = Quillbolt Hedgehog 7701 (its Kind2 row has no cost), the action composed onto it (`actionFrom`), `"trigger": "in_grave"`, `"require": ["inGrave"]`. `inGrave` is Yu-Gi-Oh-Effects' own `Check_InGrave` (the card's position zone 16, as `Check_InHand` reads 13): the same table answers for the banished cards, so without it the effect could be activated from there too. Costs still work with it (`cost pay_lp(N)`, `discard(N)`, `tribute(1)`); `cost banish_self:` already places an effect in the GY (Rose Lover's row) and needs no trigger. Spells/Traps: not yet (the donor is a monster row). Hand effects (`Id_HasHandEffect`'s lists) are not covered: a custom card is on none of them.

**Translator.** `CardTextTranslator` reads "If this card is in your GY", "During your Main Phase, if this card is in your GY" and "..., except the turn it was sent there" (noted in `effectLooser`) as `on in_grave:` for a monster's action; "You can banish it;" after such a head is `cost banish_self`. Before this, the head was accepted but the effect was written as a field ignition effect (fixed). Over the Cards mod: 51 part-translated cards get their GY effect; whole cards still wait on their other sentences. Built, not run in a duel.

## 52. Quick Effects: quick (2026-10-21)

**Spell speed is by card id.** `Check_CanChainAtSpeed_ByEffectId` (0x140108910) lets an effect respond in a chain (or be used outside its own open window) when `Get_EffectSpellSpeed_ByEffectId` (0x14012FC50) says 2 or more. That function is a per-card ladder that ends in `Get_DefaultSpellSpeedForCard` (0x140743C30, named 2026-10-21): Trap 2 (Counter Trap 3), Quick-Play Spell 2, a monster 2 only when its id is in `IdList_QuickEffectMonsters_783` (0x140BF8470), else 1. So a composed effect would get its donor card's speed.

**Yu-Gi-Oh-Effects.** `"quick": true` on an effect (`Clone::Quick`): the speed ladder (`IdLadder<3>`) answers 2 for it before the donor is asked. **Language:** `quick;` after the action, before `once_per_turn` (`on in_grave: draw(1); quick; once_per_turn;`); BAB: the "Quick Effect" checkbox on every effect hat. **Translator:** "(Quick Effect)" on a monster's effect writes `quick` (it used to read it as an ignition effect, noted in `effectLooser`). Timing heads ("During the Main Phase", "During your opponent's turn") are still read as any time the game offers it. Built, not run in a duel.

## 53. The general resolver: any action then any action (2026-10-21)

**Protocol (from Slot0_AddFromDeckToHand_StateMachine 0x14016D5A0).** A slot 0 is a step machine: it reads the step variable 0x14349C13C (128 = start), does one step and returns the next one (126 prompt, 125 pick, 100 apply, ...); 0 = done.

**Yu-Gi-Oh-Effects.** `"before"` steps are no longer limited to draw / life points. `SlotThunk<0>` runs them in order, each as its own source card (`from`, `actionFrom`, own filters via `g_Active`), then the card's own step. A step still running (non-zero) is returned as is; an interactive step that finishes sets the step variable back to 128 and returns 128 so the engine calls again and the next machine starts fresh; immediate steps run back to back in one call. The running step is kept per card instance in `g_Sequence` (same key as `g_Choices`) and cleared when the effect resolves. Only the last (own) step has the activation's target, so the compiler refuses a targeting action before `then`. Not confirmed in IDA that the resolve driver itself stores the return value in the step variable (the thunk writes it either way). **In-duel 2026-10-21:** search then draw, send then draw, summon then burn, draw then search all work. Search then hand summon: the first version skipped the search (it only ran the steps when it read step value 128 at that moment). Now a resolution with no `g_Sequence` entry is fresh, and every later machine starts at the value the resolution started with (`g_SequenceStart`, logged once per resolution; it was 128). Retest: the search ran, but the summon offered only cards already in hand. Magnet Circle LV2's machine (`Slot0_SummonSelected_StateMachine` 0x140160080) summons the card picked at **activation**. So a chained `special_summon(hand, ...)` now compiles to Unexpected Dai (11740, `Slot0_Generic_TargetedEffect_StateMachine` 0x140163830), which collects at step 128 (resolution) through the hooked `Deck_CollectMatchingCards`, with the hand scan and `"condition": "always"` (the match may only exist after the earlier steps). Superseded by section 54 for the action itself (the row stays 11740).

## 54. Own resolver steps: pick and act (2026-10-21)

The first step away from borrowing whole card routines: `"own": "toHand" | "summon"` on an effect or a `before` step replaces the source card's slot 0 with the plugin's `Slot0_OwnPickAndAct`. The source card still gives the row (how it is activated) and the record id that routes the collect hook to the step's `deck` filter. The machine is the game's search machine taken apart (`Slot0_AddFromDeckToHand_StateMachine` 0x14016D5A0), with the act swapped:

| Step | Does |
|---|---|
| 128 | `Deck_CollectMatchingCards(player, id, Get_DeckFilterParamForEffect_Ladder)` (hooked, our filter). None: `UI_ShowNoneMessage(player, 0x15)` (0x14005E940), done. Else `UI_ShowPromptText(player, 0x33)` → 126 |
| 126 | `UI_BeginCardListSelect(player, 6, id, param)` (collects again for a human; the AI picks via sub_1400BC130) → 125 |
| 125 | the pick: a pick-1 list (mode 6) leaves the picked **index** at 0x14349C5C8 (word), the card is `list[index]` at 0x14349C5D8 (the search machine's one-card path). `Get_SelectedListCard(n)` / the count at 0x143497C40+0x3802 are for multi-pick lists only: the first build used them and moved nothing (in-duel 2026-10-21). **toHand:** `Resolution_BeginScope(1)`, `Card_MoveListCardToHand(record, player, card, 1)` (0x14008A3F0), scope counter 0x14349C180 minus 1, online: `Duel__NetEvent__SendSimple(8, -1, &0, 2)` → 100. **summon:** `Chain_AddEffectLink(player, card, 1, 2, id << 16, 0)` (the engine's summon queue, as Unexpected Dai's step 124) → 120 |
| 100 / 120 | `Msg_EndOfListEffect(player)` (0x1400A7DD0, message 86), done |

The list is built when the step runs, so "search then Special Summon" can pick the searched card. Between calls the pick screen tests cards by the step's id through `g_Resolving`, which the sequencer now sets to the running `before` step. **Compiler:** in a `then` chain, steps with a search / salvage source become `toHand` and steps with a `*Summon` scan become `summon` (`MarkOwnMachine`); single actions keep their proven donors. **Hand list (2026-10-21):** in-duel the summon step offered too little. The game's hand scan (`Scan_Hand_SpecialSummonable` 0x14052EE40 → `Scan_HandCard_Impl` 0x14052C8D0) adds `Check_CanSpecialSummonByEffect` keyed by the row's card id: under Unexpected Dai's id no hand monster passed, under Magnet Circle LV2's a Level 4 Normal Monster (The Dragon Dwelling in the Cave) did not. An own summon step with `"scan": "handSummon"` now builds the list itself (`OwnHandList`, answered from `Hook_Collect`): each hand card through `Scan_CardMatchesRowFlags` (0x14052B540: type, race, attribute, Level in hand with le/ge, ATK) and the row's name test, written as `MatchList_AddZoneCard` (0x14052B310) writes it (packed card; flags = player | 13 << 1). Whether the summon is legal is left to the engine's summon queue. In-duel 2026-10-21: search then summon the searched card works (card 9725 searched and summoned).

**Rules (2026-10-21):** *Activation:* a chain whose first step is an own step can only be activated while its list has a card - the game's list-effect rule (`Slot2_Condition_ListHasMatch` 0x1400FB620: `Deck_CountMatchingCards` >= 1), run in `SlotThunk<2>` with the first step's filter; `Hook_Count` answers an own hand step with `OwnHandList(write = false)` (counts without touching the game's list). Later steps are not checked (an earlier step may make them possible). *"X, then Y":* an own step that finds nothing sets `g_OwnStepFoundNothing`; the sequencer then ends the effect without the rest (summon then burn burned with nothing summoned before). *Prompt:* 0x33 (search's "add to your hand") for toHand, 0x30 (the summon list's, Unexpected Dai step 127) for summon. **Slot 2 must exist:** `Check_CanActivate_Driver` (0x1400E0C30) treats a missing slot 2 as allowed, so a borrowed row without a condition (Hinotama, summon then burn) never ran the list check (in-duel: playable with no monster; at resolution the chain did stop, no burn). `WrappedRow` now gives slot 2 to a row whose clone `FirstStepIsOwn`; the thunk answers 2 (allowed, no targets) after the check. Slot 2 contract: 0 refuse, 1 allowed + target mask from slot 1, >1 allowed. **AI / online:** `ListSelect_PostRequest` (0x1400BC130) files the pick; the answer is collected between calls through the hooked collect (served via `g_Resolving`) into the same pick index. In-duel: 19601 search then summon the searched card works end to end. Next own steps: send to GY, destroy, banish (the act at 125 is the only difference). Send to GY and banish: section 55.

## 55. Own steps: send to the GY, banish (2026-10-22)

**The movers (from Foolish Burial's machine).** `Slot0_SelectFromListAndMove_StateMachine` (0x140178460, Foolish Burial family, 263+122 rows) moves each pick at step 125 by the list entry's zone (entry flags `& 0x3E`: 30 = Deck, 26 = hand, else Extra Deck):

| From | Function | Arguments |
|---|---|---|
| Deck 15 / Extra Deck 14 | `Move_NonFieldCardToGraveOrBanish_Ev31` 0x1400A6F40 | (player, from zone, **0 = GY / 1 = banish**, card instance, flags 0, effect id). Banish falls back to the GY when banishing is not allowed (`sub_14005C630`). Raises event 31. |
| hand 13 | `Move_HandCardToGraveOrBanish_Ev32` 0x140088D90 (via `Hand_SendListCardToGrave` 0x140089410) | (player, card instance, flags: 0 = "sent" event 33 / 0x4000 = discard event 32, **16 = GY / 17 = banish**, effect id > 0 = by an effect) |

Card instance from a packed list entry: `((packed & 0x4000) != 0) + 2 * (packed >> 23)` (as the machine unpacks it). Its multi-pick path walks picks with `ListSelect_SetCurrentByPickNumber` (0x14005E7B0); the own machine stays on the pick-1 path of section 54.

**Yu-Gi-Oh-Effects.** `"own": "toGrave" | "banish"` (Clone::Own 3 / 4): `Slot0_OwnPickAndAct` step 125 reads the picked card's zone from `Card_GetPositionByInstance` and calls the mover above in one resolution scope (as toHand). The prompt is Foolish Burial's 0x3A for both (no game card banishes from a Deck list, so there is no banish prompt to take). Picks from any other zone are logged and not moved.

**Language / compiler.** `send_to_grave(deck | hand, selector)`, new `banish(deck | hand, selector)` (rule `banishFromAction`; `banish(all | target, ...)` is unchanged). A single `send_to_grave(deck, ...)` keeps Foolish Burial's machine (proven); in a `then` chain it becomes own `toGrave` (`MarkOwnMachine`). From the hand, and every banish from a list, compile to Foolish Burial's row + `"condition": "listHasMatch"` + `own` (`OwnMove`). With a trigger, the action key is `send_to_grave` (the usual composition). BAB: "send from [your Deck | your hand] to the GY", new "banish from [your Deck | your hand]" block (`ys_banish_from`; ACTIONS entries can carry `rule` / `block` when two grammar rules share a word). Translator: "send 1 X from your hand to the GY", "banish 1 X from your Deck / hand".

**Not yet:** the Extra Deck as a list (the generic scanner `ScanGeneric_Deck` 0x14052B8C0 is Deck only; the mover already takes zone 14), banish from the GY (`Cost_BanishSelfFromGrave` 0x1401FA670 is self only), destroy as an own step. Test cards 19605 (send then draw, own), 19606 (banish from Deck), 19607 (hand to GY then draw) in the game's Yu-Gi-Oh-Ex\cards.json; built, not run in a duel.

**Routing fix (same day).** A card with a trigger part and a GY part in the same table (Kind2: "sent from the field to the GY" via Botanical Girl 7892, and "banish this card from your GY" via Rose Lover 9409) never offered the GY effect: the GY activation check comes with event 0, both parts have Kind2 rows, and `Candidates` kept only the parts sharing the FIRST part's trigger. Now an event-0 lookup drops event-trigger parts (`IsEventTrigger`) when a part without one is left.

## 57. Special Summon from several zones, the banished cards, the Extra Deck (2026-10-25)

**IDA (2026-10-25).** The summon queue the own step uses, `Chain_AddEffectLink` (0x1400F2450), takes the summoned card's zone from `Card_GetPositionByInstance`, not from the list entry. About 100 game machines call it, and only the hand (zone 13) gets extra handling, so one call serves every zone. The game has its own "Special Summon from" list scans:

| Scan (name in IDA) | Scanner, behaviour flags | Game rows |
|---|---|---|
| `Scan_Banished_FaceUp_Summonable` 0x1405344A0 | `ScanGeneric_Banished` 0x14052D410, 0x44: 0x40 face-up only, 0x04 `Check_CanSpecialSummonByEffect` from zone 17 to your field (mode 1) | Return from the Different Dimension, Dimension Distortion, Dimension Fusion, D.D.M. (20+ rows) |
| `Scan_ExtraDeck_SummonableByEffect` 0x14052EF40 | `ScanGeneric_ExtraDeck` 0x14052C090, 0x10: `Check_CanSpecialSummonByEffect` from zone 14 (mode 1) | Starlight Road, Shooting Quasar Dragon (11 rows) |

`Check_CanSpecialSummonByEffect`'s source kind (`Get_SummonSourceKind` 0x14052B3E0) only asks whether the row's card is a monster or a Spell/Trap. The rest are the general summon rules, so the GY / banished / Extra Deck scans give the same answers under any row id (the HAND scan is the exception, section 54: it is listed by the plugin).

**Yu-Gi-Oh-Effects.** New `deck.scan` values `banishedSummon` / `extraSummon`. Each `"or"` alternative already had its own scan, so one filter can now span zones. `AltRow::HandList`: a `handSummon` alternative of an own step is listed by `OwnHandRows` (the old `OwnHandList`, now per row, appending) inside `RunDeckScan`. Collect / Count only take the hand-only shortcut when there are no alternatives; before, a hand filter's `"or"` rows were ignored.

**Language / compiler.** `special_summon(hand or deck or grave or banished or extra, selector)` (rule `summonZones`). One zone of hand / deck / grave compiles as before (Magnet Circle LV2 / Unexpected Dai / Monster Reborn). Several zones, `banished` or `extra` compile to Unexpected Dai's row + `"condition": "listHasMatch"` + `"own": "summon"`, with one alternative per zone x selector (`ZonesFilter`). With a trigger, the composition is the usual one (`actionFrom` 11740). `self` / `self_grave` cannot be joined. BAB: "Special Summon from any of" (`ys_summon_zones`, a box per zone, hand / Deck / GY ticked by default). The card-text translator now keeps every zone of "from your hand, Deck, or GY" (it used to read only the first).

**Not yet:** banished cards face-down and the opponent's zones are not listed. An Extra Deck summon by effect is not a proper summon (the engine's rules apply). Covered by Tests/EffectRouting (compiler, blocks round trip, routing); not run in a duel.

## 58. "Activate 1 of these effects": choice bullets and four once-per-turn limits (2026-10-25)

**IDA (traced 2026-10-25).** The engine has FOUR independent once-per-turn keys per card, not two: use-limit classes 5, 6, 7, 8 (row Extra[0] low nibble) are refused by `Timing_Check_Class5_9` / `Class6_10` / `Class7` (0x1400F7AC0) / `Class8` (0x1400F7B10) while flag marker (player, 13, 1001 / 1002 / 1003 / 1004, record card id) exists, and registered by `UseLimit_Class5_RegisterMarker1001` ... `UseLimit_Class8_RegisterMarker1004` (0x1401F8810 / 0x1401F8860, MsgQueue 0x35 (269, key, card id)). Class 3 also counts key 1003 markers (against a per-zone count). Activation order, `Chain_ActivateEffect_StateMachine` (0x1400AD230, step at 0x14349B580+0xBAC):

| Step | Does |
|---|---|
| (offer) | `Check_CanActivate_Driver`: the row's class check, slot 2 (condition), slot 1 target mask |
| 2 / 3 | `Check_Activation_Cost_And_Register`: slot 3 (for an effect-choice card: the select-an-effect prompt FIRST, then the chosen effect's cost), then `Register_UseLimit_Marker` with the row the lookup gives NOW - the chosen effect's, so its class |
| 4 | `Call_Slot4_DefaultTrue` / `Get_Slot4_FollowingChoice`: slot 4, the activation-time picks (targets), again the chosen row |

Online, `Is_RecordOwnerRemoteOnline` (0x1400A9760) sends a step to the peer instead. The prompt is `YGO::Duel::ShowOptionPrompt` (0x14005F340): 5 u16 labels (0 = option hidden), enabled mask, answer in field_3802; the AI / remote side picks through sub_1400BAB00. Labels are u16, so the plugin's card | n << 14 labels allow 4 options.

**Yu-Gi-Oh-Effects.** `"limitGroup": n` on an effect / part: effects with the same number share one limit class. `AssignLimitClasses` gives each once-per-turn effect, or each group, the next of 5, 6, 7, 8 (a fifth shares 5). Since every effect's record carries the card's own id, a shared class is a shared limit: after one bullet is used, the others' class check refuses them. Several-effect cards are offered one menu entry per effect (record word20 tag, `Hook_InsertOffer`); the select row (section 45) is the fallback, now capped at 4 options. `WrappedRow` clears an inherited class 1 or 5-10 from the donor. Plugin 1.3.2.

**Language / compiler / blocks.** Bodies (or `effect { }` blocks) joined with `or` instead of `also` are one group: `"limitGroup"` = the index of the group's first effect, and `once_per_turn` on any of them sets `"oncePerTurn"` on all; more than 4 is an error. BAB: "or the next effect (activate 1 of these)" box on every effect hat (`OR_NEXT`, written as `or`, read back from the `or` token).

**Translator.** A header `[head:] [cost / target(, then | to)] (You can) activate | apply 1 of these effects[;]` (`ChoiceHead`) puts its head and cost in front of every bullet (`If X: ` + bullet, `cost; ` + bullet), so each bullet translates as a whole effect. The bullets are one `or` group named by their text (`effect "<bullet>" { ... } or effect ...`, so the prompt shows the bullet), unless the header says "(but you can only use each effect of this card once per turn)" (each its own limit, joined with `also`). Text after the choice phrase is noted as not run. "You can only use this effect of X once per turn" after the bullets limits the whole group. Headers starting with a rule word (While, Once per Chain ...) stay rules. Over the Cards mod (run on a copy): 702 bullets were skipped as "under a choice"; 332 are now read, 20 translate, 312 fail on their own head / cost / action (top: "If you control", "If your opponent", "Once per turn, ..."). The remaining 368 skipped bullets belong to other families ("gains these effects based on ...", "in sequence", "apply the following effect"). Not run in a duel. Tests: Tests/EffectRouting (classes 5-8 and shared groups; compiler `or` groups; blocks round trip).

## 59. "Once per turn" per copy vs per card name (2026-10-25)

**IDA.** The eight once-per-turn classes are two key spaces. Classes 5-8 (section 58) put marker (1001-1004, value = card id) in the PLAYER's list (zone 13): one use per card name. Classes 1-4 put (1001-1004, value 0, lifetime 2) in the CARD'S OWN ZONE list (`UseLimit_Class1..4_RegisterZoneMarker*` 0x1401F8390 / 0x1401F8420 / 0x1401F84B0 / 0x1401F8540, only while that zone still holds the activating instance), and `Timing_Check_Class1..4` refuse while `Count_FlagMarkersInZone(player, zone, key)` (0x140052450) reaches `Get_UseLimitPerCopyCount` (0x1401F64E0: 1, 2 for a few "twice per turn" ids): one use per copy on the field. A card off the field (zone > 12: hand, GY, banished) passes the per-copy check. Marker records: 12 bytes at 0x143497C40+0x2B52 (key, flags = lifetime nibble | turns-left counter << 4, u32 value, serial `field_376C`, next), allocated by `FlagMarker_AllocAndLink` (0x14004A2B0), removed by `FlagMarker_Remove` (0x140049DE0). All of them expire in `FlagMarkers_EndOfTurnSweep` (0x14004C320), run by `DuelMsg_EndOfTurn` (MsgQueue case 3; with Arg1 = 0 the use-limit keys are kept). Not checked: whether a zone's markers are cleared when its card leaves (a new card in the same zone the same turn may inherit the use).

**Yu-Gi-Oh-Effects.** `"oncePerTurnPerCopy": true` (Clone::OncePerCopy; `"oncePerTurn"` wins when both are set). `AssignLimitClasses` now runs for every card (a one-effect card too) and hands per-copy limits classes 1, 2, 3, 4 and per-name limits 5, 6, 7, 8, each group / effect in order. `WrappedRow` clears any donor class 1-10.

**Language / compiler / blocks.** `once_per_turn(copy);` ("Once per turn: ...") next to `once_per_turn;` ("You can only use this effect of X once per turn"); any other word is an error. An `or` group takes per name when any member says so, else per copy. BAB: "only once per turn (each copy)" box next to "only once per turn (per card name)".

**Translator.** "Once per turn" and "Once per turn, <head>" (any head the translator knows, read by recursion) give `once_per_turn(copy)`; "You can only use this / each effect ... once per turn" stays per name and wins. A per-copy limit on an effect used off the field (from the GY or hand, a GY trigger, a self cost from the hand / GY, a self summon) becomes per name, noted in `effectLooser`, because the engine would not count it there. Before this, "Once per turn: ..." was written per card name, and "Once per turn, during your Standby Phase" lost its limit.

**Test (Tests/EffectRouting, IDA transcription).** `UseLimitEngine` re-implements the class checks, the registrars and the end-of-turn sweep from the decompiled code above, and plays the rows the plugin builds from effectClone JSON (`ParseEffect` -> `WrappedRow`) through it: per copy (two copies, the GY, next turn), per name (two copies, the opponent's copy), a choice group shared with another per-copy effect, a per-name effect and an unlimited one on one card.

## 60. Whose phase: your / your opponent's / each Standby and End Phase (2026-10-25)

**IDA.** `Duel__Phase__EnterStandbyPhase` (case 10) and `Phase_EndPhase_RunHandlers` (cases 10-12) run four passes; pass p sets the parity HIDWORD(field_37C2) = p & 1 and calls every row's handler(turn player, card id). Each handler gates on player == turn ^ parity and searches player's field, so the HANDLER says whose phase it is:

| Phase | Your | Your opponent's | Each |
|---|---|---|---|
| Standby, monster (zones 0-6) | `PhaseHandler_Standby_MonsterYours` 0x140148EB0 (Bowganian) | `..._MonsterOpponents` 0x140148FA0 (Malice Ascendant 6608) | `..._MonsterEach` 0x140148FB0 (D.D. Sprite 9295) |
| Standby, Spell/Trap / Pendulum (7-12) | `PhaseHandler_OfferAtStandby_Ev2` 0x1401491C0 | `..._SpellTrapOpponents` 0x1401491D0 | `..._SpellTrapEach` 0x1401491F0 |
| End Phase, monster | `PhaseHandler_End_MonsterYours` 0x14014D9A0 (Solar Flare Dragon) | `..._MonsterOpponents` 0x14014DAB0 (Diana the Light Spirit 13914) | `..._MonsterEach` 0x14014DAC0 (Simorgh, Bird of Divinity 6720) |

Others named: delayed-marker handlers (0x140149D10 / 0x140149E60, "during the next Standby Phase after ..."), a GY handler (0x140149710, key 1012 per instance). A handler that offers returns 0 and the step re-runs (it remembers its offer, sub_1400A83E0).

**Yu-Gi-Oh-Effects / language.** New triggers `opponent_standby_phase`, `each_standby_phase`, `opponent_end_phase`, `each_end_phase` (events 2 / 6 in `TriggerFitsEvent`). *Superseded the same day by section 61: these triggers now need no game card at all.* (First version:) The donors above have ONE plain row in every effect table (checked against all five tables, so no other effect can leak), and the compiler marks these compositions `"effectOnly": true`: the card keeps its own id, and `PhaseThunk` runs the donor's handler for it. trigger_sources.json / build_effect_reference.py `MANUAL_SOURCES` carry them; the generator's `end_phase` pattern is now "your End Phase" only. BAB: the "During" hat lists all six.

**Translator.** "During your opponent's Standby / End Phase", "During the / each / either player's Standby / End Phase" -> the new triggers (monsters only). Before this, "during the End Phase" was written as YOUR End Phase.

**Test (IDA transcription).** `PhaseWorld` + `RunPhase` re-implement the four-pass driver and the three monster handler families and drive the plugin's real `PhaseThunk` (a mocked donor handler) with a custom monster controlled by player 0: yours fires on turn 0 only, your opponent's on turn 1 only, each on both.

**Coverage (Cards mod, on a copy).** Effects read 794 (start of the day) -> 821 (section 58) -> 822 (per copy) -> 826 (whose phase); 19 effects get `once_per_turn(copy)`, 6 the new phase triggers. The "Once per turn, ..." heads that still fail fail on the head after it (Battle Phase timing, "when your opponent would Special Summon", "if the previous effect was activated this turn" ...). Actions are the bigger blocker (4120 reasons).

**Next (donor-free triggers).** Done: section 61.

## 61. Own triggers: no game card behind the trigger (2026-10-25)

A borrowed trigger card only ever gave two things: the ROW (table, event wiring, use-limit word, slot 3) and the code that OFFERS the card at the right moment. Both are now the plugin's own for the phase triggers, so such an effect is made entirely of parts.

**IDA.** The phase handler families (section 60) take (turn player, card id) and belong to no card: gate player == turn ^ parity, the card in that player's Monster Zones (`sub_1400139D0` / `sub_140013A70`: id at the zone, face-up, not disabled), zone status bit 43 clear (`UNK_03 & 0x800`), `Offer_EffectOfCardAtZone(player, zone, 2 | 6, 0)` (0x1400AAF90: ref = id | zone | table 1 | event | player), and on an offer `Zone_SetStatusBit(player, zone, 43, 1)` (0x1400A83E0: the per-zone "offered this phase" bit, also a MsgQueue 41 for the UI) and return 0. The last table rows: Standby row 294 = {0x1401491F0, Borrel Supplier 14620}, End Phase row 551 = {0x1401540E0, 5406} (no flag bytes: every End Phase step reaches it).

**Yu-Gi-Oh-Effects.** `"ownTrigger": true` (Clone::OwnTrigger; `"from"` is then the ACTION card, ActionFrom = From):
* **Own row** - `OwnTriggerRow(id)`: this card's id, Extra 0, no slots; `WrappedRow` composes the action's slots 0 / 1 / 2 / 4 onto it (slot 3 stays empty unless a cost is composed). `OwnRowFits`: only the monster table (word4 1), for the events the trigger fits, and (as a donor row would) for events the plugin does not place (0: lookups made while the activation / resolution runs); never another known event. `Hook_GetEntry`, `Candidates` and the offer probes use it in place of a donor's row.
* **Own offer** - a virtual row {family handler, custom id} appended after the table's last row: `PhaseHook::AppendAfter` / `Virtual`, run by `PhaseThunk` after that row returns non-zero, called exactly as the driver calls a row (0 = offered: the step re-runs; bit 43 stops a second offer). Family by trigger: `g_PhaseFamilies`.
* No lending: `SingleEffectSource` (Dependencies/Yu-Gi-Oh-Ex/EffectSourceLending.h, used by Yu-Gi-Oh-Cards) returns 0 for `ownTrigger`; the compiler also writes `effectOnly`. Effects 1.3.2, Cards (MoreCards) 1.3.2.

**Compiler.** All six phase triggers compile to `ownTrigger` unless a game card does exactly that trigger + action (`TryTriggerSource`: e.g. `on end_phase: burn(300)` keeps Solar Flare Dragon, proven in a duel). `on <phase>: as(N)` too (the as card is the action). The section 60 donors were removed from trigger_sources.json / `MANUAL_SOURCES`.

**Test (Tests/EffectRouting).** `TestOwnTriggerStateMachine`: the own row answers its own lookup (and an event-less one), refuses other events / tables, resolves as its action card (draw 4844), is never lent; its virtual row, played through the IDA-transcribed driver, fires on the right turns for all three families and never after a row that is not the table's last.

**Next for own triggers.** The summon / GY / battle triggers still borrow the row of a monster with that trigger; the event side is already hooked (`Hook_EventEvaluator`, section 22), so the same own row + an own offer at the evaluator would make them part-built too.

## 62. Translator wording, 2026-10-25 (late)

* "target 1 X in your GY, except this card; Special Summon it / add it to your hand": the ", except this card" closing a target clause is read like the one at the end (the list never offers a card with this card's name).
* "target 1 of your banished X(s); Special Summon it" -> `special_summon(banished, X)`; "... that is banished or in your GY; Special Summon it" -> `special_summon(grave or banished, X)` (the own summon list, section 57; picked when it resolves, noted). `Singular` reads "1 of your "X" monsters".
* Never writes what the compiler refuses: "then Special Summon this card" as a later step (dropped, noted), `if controls(... level = N)` (the filter tests a Level with <= / >= only), `if controls(...)` with a destroy / banish / return anywhere in the chain, and a `then` chain in a second effect (the chained effect goes first; a further chain keeps its first action, noted). Every partial script over the Cards mod now compiles (823 / 823; 7 did not).
* Coverage (Cards mod, on a copy): effects read 826 -> 863. Next large families: summoning procedures ("Special Summon this card (from your hand) by sending / revealing / Tributing ..."), Fusion Summon actions (64), "Set 1 X Spell/Trap" (32).
* Later the same day: summoning procedures "Special Summon this card (from your hand) by discarding / sending / Tributing / paying ..." -> "<cost>; Special Summon this card from your hand" (Watch Cat's self summon + a composed cost; noted: here an effect that starts a Chain); "You can activate this effect;" (no cost); "(Quick Effect): You can ..." with no head; "<detach / discard N>, then target ..."; battle-position tails ("in Attack / face-down Defense Position") and ", with a different name ..." noted; `Choices`: "A or B" selectors and Type / Attribute lists ("Level 4 or lower Beast, Beast-Warrior, or Winged Beast monsters", "LIGHT or DARK monster") as EffectScript choice lists; `gain_atk` with a target filter / ATK/DEF / other durations (noted); a crash on "Level" with no number fixed. Effects read 863 -> 1009; every partial script compiles.

## 63. Set a Spell/Trap from the Deck: set(deck, ...) (2026-10-25)

**IDA.** `Slot0_SetSpellTrapFromDeck_StateMachine` (0x14022A0A0; 60+ rows: Fire Fist, Altergeist Marionetter, Watch Cat, Set Rotation ...): 128 free Spell & Trap Zone + `Deck_CollectMatchingCards` through the card's deck-filter row (none: message 0xD), prompt 0x36 -> 127 card list (mode 6) -> 126 pick, zone prompt (`sub_1400F37B0`; a Field Spell goes to zone 12) -> 125 Set (`sub_14008AC80`) -> 124 `Eval_CardSetTriggers_Ev11` + per-id markers -> 123 / 100 end. Per-id branches (25 ids) and two archetype branches (Fire Fist 148: no zone check; Metalfoes 275: scope variant); every Spell/Trap row on it is one of those ids.

**Language / compiler.** `set(deck, selector)` -> Pot of Greed's Normal Spell row (activation) + `actionFrom` Altergeist Marionetter 13228 (no special branch) + the `deck` filter, which the hooked collect / count serve as for a search. With a trigger: the usual composition (or an own phase trigger). BAB: "Set from your Deck". Translator: "Set 1 X Spell/Trap (Card) (directly) from your Deck". 49 cards over the Cards mod.

## 64. Fusion Summon: fusion_summon(...) (2026-10-25)

**IDA.** Every fusion Spell (Polymerization 4837, Fusion Gate, Super Polymerization, Fusion Substitute, Frightfur Fusion) has slot 0 `Slot0_FusionSummon_StateMachine` (0x14000A710) and slot 2 `Slot2_Fusion_CanSummon` (0x14000A650: materials, then `Deck_CountMatchingCards` through the card's deck-filter row). Polymerization's row (table 0x140AD2A80 row 91) = `Scan_ExtraDeck_FusionSummonable` (0x140531D30), flags 0x802; **Frightfur Fusion is the same row with param 209 (the Frightfur archetype)** - so a filter on that scan is "Fusion Summon 1 "X" Fusion Monster". Materials come from row k = 1 and per-id branches (hand or field for Polymerization).

**Yu-Gi-Oh-Effects.** `deck.scan: "fusion"` -> scan 0x140531D30, flags | 0x802 (race / attribute / Level bits as usual, param = archetype / name). **Language / compiler:** `fusion_summon(selector?)` -> Polymerization's row + `deck` (scan fusion); with a trigger, composed (`actionFrom` 4837). BAB "Fusion Summon a Fusion Monster". Translator: "Fusion Summon 1 (X) Fusion Monster from your Extra Deck(, using monsters from your hand or field as material)(, including ...)" - other material zones and "including" requirements are noted (Polymerization's materials are used). Not yet: banish / shuffle-materials-from-the-GY fusions (other machines). 22+ cards.

## 65. Own move triggers: sent to the GY / banished, with conditions (2026-10-25)

**IDA.** `Eval_CardMovedTriggers` (0x1400A2F70, events 31 / 32 / 33 for both the GY and banishment) takes the move reference: bits 0-8 card instance (bit 0 = owner), 9 player, 10-14 from zone, 16 from the field, 18 by battle, 20 controller differs, 21-25 to zone (16 GY, 17 banished). Its offer (0x1400A3765): `Offer_EffectByCardRef(id | (to zone | 0x40 | (owner << 6 | event) << 9) << 16, slot = instance, param)` - table 2.

**Yu-Gi-Oh-Effects.** New trigger `banished` (events 31-33). An own-trigger effect with a move trigger (`sent_to_grave`, `sent_from_field_to_grave`, `destroyed_by_battle`, `banished`): its row is in table 2 (`OwnRowFits`), and `Hook_MoveEvaluator` offers it itself with that exact reference when `MoveMatches`: the destination the trigger names (banished 17, else 16), from-field / by-battle for the field / battle triggers, and `"moveCondition": {"from": hand | deck | grave | field | monster | spelltrap ..., "by": battle | effect}`. A card with only own triggers is evaluated without a donor swap; a multi-effect card's donor parts keep the swap path. Before this, "sent to the GY" clones also fired when banished, unless their donor's own ladder refused.

**Language / compiler / blocks.** `on banished:`, `on banished(from grave):`, `on sent_to_grave(from hand, from field, by effect):` (the summonConditions list; `field` and kind words added to its zones). Move triggers with no exact game match, or with conditions, compile to `ownTrigger` + `moveCondition`. BAB: the event hat's "moved from / by" boxes and "this card is banished". **Translator:** "If this card is banished (from the hand / GY / field)", "If this card is sent from the hand / Deck / field / Monster Zone / Spell & Trap Zone (or ...) to the GY (by card effect)", "sent to the GY, except from the field", "sent to the GY by card effect".

## 66. Own hand effects: on in_hand (2026-10-25)

**IDA.** A monster in the hand is offered from the menu / chain windows only if `Id_HasHandEffect` (0x140743FA0: three id lists) says yes; `sub_140108610` then sets record word4 = 2 (table 2) and checks event 0 or each queued event through `Check_CanChainAtSpeed_ByEffectId` and `Offer_GateCanOfferEffect`. Every gate (menu flags `HandCard_GetCommandFlags`, the offer gate, the AI) asks `Id_HasHandEffect`.

**Yu-Gi-Oh-Effects.** The existing id-test hook (`IdTest<0>`, the 743FA0 slot of `kIdTestAddresses`) answers yes for a card with an own `in_hand` effect; own rows for `in_hand` live in table 2; own-trigger parts are never asked as their action card in the id tests. **Compiler:** `on in_hand:` -> `ownTrigger` + `require: ["inHand"]` (table 2 also answers for the GY). **Translator:** "If this card is in your hand: You can <action>" with no self cost; "If this card is in your hand or GY" -> `on in_hand: X or on in_grave: X` (one choice group: one shared limit; split into two effect blocks in effect-block output). BAB event "you use it while this card is in your hand".

## 67. Own attack triggers (2026-10-25)

**IDA.** `Eval_AttackDeclaredTriggers_Ev18` (0x140086C00, arg = replay count) reads `g_AttackRecord` (0x14349C480, struct `AttackRecord`: attacker player, other player, direct attack, attacker zone word +8, target zone word +0xA) and offers event 18 with a packed param (comment at the function) to the attacker, the attacked player's monsters, the target and named cards.

**Yu-Gi-Oh-Effects.** `Hook_AttackEval` runs the game's evaluator, then on the first pass offers own triggers with the same param through `Offer_EffectOfCardAtZone`: `attack_declared` (this card is the attacker), `opponent_attack_declared` (a face-up monster of the attacked player), `attacked` (this card is the target; not on a direct attack). The inline attacker-list stub no longer maps own-trigger cards to their action card. **Language / translator:** `on opponent_attack_declared:`, `on attacked:` ("When an opponent's monster declares an attack", "When this card is targeted for an attack"); `attack_declared` now compiles to an own trigger too. Spell/Trap and hand versions are not covered (they need an activation condition / a hand offer). Test: `TestOwnAttackTriggers` (param packing, the three cases, direct attack, replay pass). Effects read 1014.

## 68. Own summon-reaction triggers (2026-10-25)

**IDA.** `Eval_SummonReactionTriggers` (0x1400E48F0, renamed; arg = event: 7 Normal Summon, 10 when it is a Set (EventRecord+6 == 0), 8 Flip Summon, 9 Special Summon) offers the cards that react to ANOTHER card's summon, by id, through `Offer_EffectOfNamedCardOnField(player, id, event, param, -1, KIND_TABLE category != 0)` (0x1400AB360) with `param = Get_SummonEventParam()` (0x1400E19B0); "your opponent summons" cards are offered for `1 - EventRecord.word0`.

**Yu-Gi-Oh-Effects.** `Hook_SummonReaction` runs it, then offers own reaction triggers the same way: `opponent_special_summoned` (event 9, the summoner's opponent), `opponent_summoned` (7 or 9), `monster_special_summoned` (9, both players). **Language / translator:** "If / When your opponent Special Summons a monster(s)", "... Normal or Special Summons ...", "If a monster(s) is Special Summoned". Narrower forms (from the Extra Deck, to your opponent's field ...) are not read yet. Test: `TestOwnSummonReactions`. Effects read 1018.

## 69. Continuous ATK / DEF changes: aura(...) (2026-10-25)

**IDA.** `Calc_EffectiveAtkDef_Modifiers` (0x1400307C0, player, zone, out, 0, 0; 7322 instructions) fills a 40-byte block: **+4 ATK, +8 DEF (int), +0xC original ATK** (read by "double / halve"); per-id ladders and the stat tables (section 31) are applied inside, then the epilogue (0x1400382E0..) swaps ATK/DEF for switch effects, clamps both to 99,999,999 and floors negatives at 0. Callers: `Get_EffectiveAtk` (0x140039B30, +4), `Check_EffectiveAtkAtLeast` (0x140013FC0), `Calc_EffectiveAtkDef` (0x1400307A0, the 0, 0 wrapper), battle, fusion-material and list code. Attribute tests: `Get_AttributeMask` (0x14003CAC0) = 1 << attribute (+ `Get_EffectiveAttribute` 0x14003BF70, `Get_ExtraAttributeMask` 0x14003C9A0); Type: `Get_EffectiveRace(player, zone, -2)`.

**Yu-Gi-Oh-Effects (Lock.cpp).** `"aura": [{"atk": N, "def": N, "side": "you" | "opponent" | "both" | "self", "race", "attribute", "archetype"}]` on a card (or in its effectClone). `Hook_Calc` runs the game's calculation, then for a face-up monster adds every aura of every face-up custom card in its own zone (Spell/Trap Zone for a Spell/Trap, Monster Zone for a monster) whose effects are not negated (the lock state, as the hand limit: a custom or game Skill Drain stops it) and which covers it (side, Type, Attribute, archetype), then clamps the same way. A re-entry guard keeps the lock-state build from recursing.

**Language / compiler / blocks / translator.** `aura(atk -500, opponent);`, `aura(atk 300, def 300, own, monster where race = Dragon);`, `aura(atk 1000, self);` (several per card, beside effects). BAB: "While face-up" category, "While this card is face-up, your monsters [selector] change ATK by .. and DEF by ..". Translator: "Monsters your opponent controls lose N ATK", "<X> monsters you control gain N ATK/DEF", "All <X> monsters on the field gain N ATK", "(This card) gains N ATK" (Level / ATK filters refused). Effects read 1052 (27 cards). Not yet: "for each ..." amounts, conditions ("while ...", "during your turn"), equip-only ("the equipped monster gains", already `equip(...)` for Equip Spells). No native routing test yet for `Hook_Calc` (Lock.cpp is its own translation unit; its game calls need a test seam).

## 70. Research notes, not built yet (2026-10-25)

* **Summoning conditions** "Must be Special Summoned (from your Extra Deck / hand / GY) by / with ..." and "Must (first) be either Fusion Summoned, or Special Summoned ..." are now read as covered-but-not-enforced (`Covered`, noted), like the existing "(from your hand)" form: 62 sentences no longer block their card.
* **Attack legality** (named): `Check_CanAttackTarget` (0x140141DE0, attacker player / zone, target player / zone, mode, BOOL* forced flag; per-id / per-race ladder incl. Marshmallon), `Count_AttackableOpponentMonsters` (0x140144660), `Has_AttackableOpponentMonster` (0x14013E6A0). The place for own "cannot be targeted for attacks" / "can attack directly" rules.
* **"Cannot be destroyed by battle / by card effects"**: traced and built, see section 71.

**Tests.** Routing: `TestOwnMoveTriggers` (banished from GY offers with the evaluator's exact reference; wrong zone / destination / battle do not; the card is never shown as another card; table 2 row only). Compiler + blocks round trip: set, fusion_summon, banished, sent_to_grave conditions. Coverage: effects read 1009.

## 71. Cannot be destroyed: indestructible(...) (2026-10-25)

**IDA (all named, typed, commented).**
* **Battle.** `Battle_DamageStepMachine` (0x1402114C0, cases 0..0xA0) runs `Battle_CalcDamage` (0x140070850) and `Battle_ResolveResult` (0x1400795C0). The participants are `g_BattleSides` (0x14349C494, `BattleSide[2]` by player, 40 bytes: +0 Player, +1 Zone, +2 InDefense, +4 Present, +6 CardCode, +8 CardId, +0x14 Atk, +0x18 Def, +0x23 DestroyedByBattle); damage entries follow at 0x14349C4E4. `Battle_ResolveResult` compares ATK with ATK / DEF, adds damage (`Battle_AddBattleDamage` 0x140073A00), sets the loser's `DestroyedByBattle`, then clears it when **`Battle_CanDestroyByBattle`** (0x140074430, isAttacker, apply, a3, side, other) returns 0. That function checks: cards attached to the monster against `g_BattleIndestructibleAttachIds` (0x140AD0AC0, 77 ids, binary search), field markers (8292, 9298 ...), then - only when the monster is face-up active (`Zone_IsFaceUpActive` 0x140046ED0) and not negated (UNK_03 & 0x400) - its own id against `g_BattleIndestructibleIds` (0x140AD09E0, 73 ids: Marshmallon 6000, Spirit Reaper 5526 ...), then a per-id ladder (ATK comparisons, Defense Position, archetype protectors). Hex-Rays cuts the tail (the chunk runs to 0x14007865C); the caller fixes the meaning: 0 = not destroyed.
* **Card effects.** `Card_Destroy` (0x1400D0F50, player, zone, reason 16 effect / 17 battle-related, flags bit 2 = destroy, bit 1 = opponent's effect, srcCardId, srcZone): kind immunity first (`Zone_IsUnaffectedByMonsters` 0x140022F40 / `Zone_IsUnaffectedByTraps` 0x140022E90 / `Zone_IsUnaffectedBySpells` 0x140022DE0 by the source's kind, section 72), then **`Destroy_CanEffectDestroy`** (0x1400295A0: builds a 56-byte record for the source card, returns `Effect_CanAffectCard` 0x140026440 with mode 0) - 0 = not destroyed - then `Destroy_TryReplacement` (0x1400CF810, "... instead"). The queue the destroy actions fill is `Duel::PlayerState.DestroyQueue_ZoneMask / _Record / _Reason / _Flags / _Pending` (+0x1B2C, +0x389C, +0x38D4, +0x38DC, +0x3894; `Action_DestroyZoneMask`, `Action_DestroyCardAtZone` 0x1400D51D0).
* `ContEffect_CoversZone` (0x1400484F0, player, zone, srcPlayer, srcZone, srcCardId): the per-source-id "does this continuous effect apply to that monster" ladder (80+ callers).

**Yu-Gi-Oh-Effects (Lock.cpp).** `"protect": [{"from": "battle" | "effect" | "both", "by": "any" | "opponent", "side": "self" (default) | "you" | "opponent" | "both", "race", "attribute", "archetype"}]`. `Hook_BattleDestroy` / `Hook_EffectDestroy` return 0 (before the game's checks) when the face-up card is covered by a protect rule of a face-up custom card whose effects are not negated (the lock state, as the aura; the filter is the aura's). `"by": "opponent"` limits only the card-effect half. Clones holding only a protect rule are continuous, not effects.

**Language / compiler / blocks / translator.** `indestructible(battle);`, `indestructible(battle, effects);`, `indestructible(opponent_effects);`, `indestructible(battle, own, monster where race = Warrior);`. BAB: "While face-up" > "While this card is face-up, [this card / your ...] monsters cannot be destroyed by [...]". Translator: "(This card) cannot be destroyed by battle / card effects / your opponent's card effects / battle or (by) card effects", "<X> monsters you control cannot be destroyed by ..." (other / position / Level / ATK filters refused). Effects read 1092 (from 1052), 25 whole cards.

**Not yet.** Conditions ("while you control ...", "once per turn" count-based protection, "during your turn"), equip-only protection ("the equipped monster cannot be destroyed" - `g_BattleIndestructibleAttachIds` is the game's path), ("cannot be targeted" and "unaffected by": section 72). No native routing test for the Lock.cpp hooks yet (same seam problem as the aura).

**Tooling.** `Tools\Scripts\ida_struct_members.py` names struct members offline (idalib, analysis off; the MCP has no member rename).

## 72. Cannot be targeted, unaffected by: untargetable(...), unaffected(...) (2026-10-25)

**IDA (all named, typed, commented).**
* **Card-effect targets.** `Effect_CanTargetCard` (0x140023AF0, EffectRecord*, player, zone; 1 = can be targeted): empty zone / UNK_03 & 0x20 / 0x400 -> 0, a per-id ladder of untargetable cards (Number 49: Fortune Tune 10703, Number C88: Gimmick Puppet Disaster Leo 11016 ...; "by your opponent's" ones test source player = (Player ^ SideXor) & 1 == 1 - player), field protectors (Galaxy Stealth Dragon, SPYRAL Resort, The Weather Painter Aurora), marker 13958, FA Turbo Charger. Callers: `Filter_TargetableCardAtZone` (0x14017E4E0 = `Filter_CardAtZoneMatchesFlags` 264212 && `TargetFilter_ByEffectId` (0x14017CAF0, `g_TargetFilterTable` 0x140B1B500, 197 rows {effect id, kind, value}) == 1 && (word7 || Effect_CanTargetCard)) and ~17 target pickers.
* **Attack targets.** `Check_CanAttackTarget` (0x140141DE0, attacker player / zone, target player / zone, mode, BOOL* outFlag; 1 = may attack it); `Attack_MustTarget` (0x140141580) = a target that forces attacks onto itself; Ring of Magnetism via `Count_RingOfMagnetismMonsters` (0x1400516B0) / `Count_ActiveAttachedCards` (0x140050650). `Count_AttackableOpponentMonsters` and `Has_AttackableOpponentMonster` ask it per zone.
* **Unaffected by.** `Zone_GetImmunityMasks` (0x140022C70, player, zone 0-6, int* monsterMask, int* spellMask, int* trapMask, skipOwn) = `Immunity_FromAttachedCards` (0x140021160) | `Immunity_FromFieldCards` (0x1400215F0: Non-Spellcasting Area, Dust Barrier, Eternal Soul, Tenyi ...) | `Immunity_FromOwnId` (0x140021E40, unless skipOwn == 1). Spell / Trap masks: bit 1 = unaffected by its controller's, bit 2 = by the opponent's (`Zone_IsUnaffectedBySpells` 0x140022DE0 / `ByTraps` 0x140022E90 test `mask & (srcIsOpponent + 1)`); monster mask bits base + side, base 2 / 4 / 6 by the source monster's frame (`Zone_IsUnaffectedByMonsters` 0x140022F40). Read by Card_Destroy, Effect_CanAffectCard, the attack checks and every per-kind immunity test.

**Yu-Gi-Oh-Effects (Lock.cpp).** `"protect"` "from" is a word or a list: battle, effect, both (section 71), **target** (`Hook_CanTarget` returns 0; "by": "opponent" = only the opponent's effects), **attack** (`Hook_CanAttack` returns 0 and clears outFlag), **spells / traps / monsters** (`Hook_Immunity` ORs bit 2 - and bit 1 unless "by": "opponent" - into the masks; monsters 0xA8 / 0x54). Each hook installs only when some card uses that kind; a re-entry guard covers the lock-state build.

**Language / compiler / blocks / translator.** `untargetable(effects | opponent_effects | attacks, side, selector);`, `unaffected(spells | traps | monster_effects | opponent_spells | opponent_traps | opponent_monster_effects, side, selector);` (mixing opponent_ and plain words needs two statements). BAB: the protection block's "cannot ..." list (be destroyed / be targeted / be affected by ...). Translator: "This card cannot be targeted by (your opponent's) card effects / for attacks (, but does not prevent ...)", "(This card is) unaffected by (your opponent's) Spell / Trap / Spell and Trap / monster effects". Effects read 1097.

**Not yet / open (s72).** Whether a player may attack directly when every opponent's monster is untargetable for attacks (the game's direct-attack rule is not traced yet); "unaffected by other cards' effects" (needs the source card != this card, not a kind mask); conditional forms ("while you control ...", "while this card has Xyz Material").

## 73. This card's name becomes ...: name_as(...) (2026-10-25)

**IDA (named, commented).** `Get_CardCodeAtZone` (0x140047060) is the code every name / archetype test of a field card uses. For a face-up card that is not disabled (UNK_02 | UNK_03 >> 4 bit 2 clear, UNK_02 >= 0) it maps the id through **`Get_FieldNameAsCode`** (0x140046FD0) = binary search of **`g_FieldNameAsTable`** (0x140ACFE80, 54 {id, code}: Proto-Cyber Dragon -> Cyber Dragon, Elemental HERO Neos Alius -> Neos, Forgotten Temple of the Deep -> Umi, Decode Talker Extended -> Decode Talker ...); attached-card overrides (Elf's Light 8027 style) and a few ids come first. Wherever a card is looked up by id instead (GY / Deck scans, fusion materials, filters: 100+ callers) the game asks **`Get_ProxyCardId`** (0x1400DB180), an id ladder - Harpie Queen 7111 and the Harpie Ladies -> Harpie Lady 4068, Cyber Dragon variants -> 6390 ... Harpie Queen's text says "while on the field or in the GY", so the game itself counts those cards everywhere.

**Yu-Gi-Oh-Effects (Lock.cpp).** `"nameAs": {"id": N, "where": "field" | "grave"}`: `Hook_CardCode` answers N for the face-up, not disabled custom card (zones 0-12, the game's own test); `"grave"` (the default: field and GY) also answers `Get_ProxyCardId` (`Hook_ProxyId`), the game's path for Harpie Queen.

**Language / blocks / translator.** `name_as("Harpie Lady");`, `name_as(6390, field);` (one per card; card name or id). BAB: "This card's name becomes [..] while [on the field or in the GY | face-up on the field]". Translator: "This card's name becomes "X" while on the field / face-up on the field / in the Field Zone" -> field; "... on the field or in the GY / in the GY / in the hand or on the field / in the hand, Deck, GY, or on the field" -> grave (the game's approximation). The script rule moved the continuous statements into `continuous` (`script : continuous* ... continuous*`). Effects read 1144 (from 1097).

## 74. Once per Duel, twice / thrice per turn (2026-10-25)

**IDA (named, commented).** Use-limit classes 9 / 10 are checked by `Timing_Check_Class5_9` / `Class6_10` with the same keys as 5 / 6 (marker 1001 / 1002 in the player's list, value = card id), but registered by **`UseLimit_Class9_RegisterDuelMarker1001`** (0x1401F88B0) / **`Class10_RegisterDuelMarker1002`** (0x1401F8900) with message 53 arg1 0x50D = zone 13 | lifetime **5**; `FlagMarkers_EndOfTurnSweep` removes only lifetimes 1-3 and 6-9, so a class 9 / 10 marker lasts the Duel: **once per Duel**. The per-copy classes 1-4 allow `Get_UseLimitPerCopyCount` (0x1401F64E0) uses per turn - 1, or 2 for a few game ids (Timing_Check_Class1: zone marker count < it).

**Yu-Gi-Oh-Effects (EffectClone.cpp).** `"oncePerDuel": true` -> class 9, a second one (or 'or' group) 10; the per-name once-per-turn classes then start after the keys once per Duel took (9 = key 1001 = class 5's). `"usesPerTurn": N` (2-9) -> a per-copy class with `Hook_UseCount` answering N (the card's largest). N uses per turn are counted PER COPY - the text means per card name; the per-name check is a single "marker exists" test (noted approximation).

**Language / blocks / translator.** `once_per_duel;`, `uses_per_turn(N);` after an effect (`uses_per_turn(1)` = `once_per_turn(copy)`); an 'or' group shares once_per_duel, uses_per_turn needs an effect of its own. BAB: the hat's "other use limit" list (once per Duel, twice / three / four times per turn). Translator: "You can only use this effect of this card once per Duel / (up to) twice per turn / thrice per turn / up to four times per turn (, and its previous effect only once per turn)". Test: `TestUseLimitStateMachine` (IDA-transcribed engine now with lifetimes and Get_UseLimitPerCopyCount through the hook): once per Duel survives the end of the turn and moves the once-per-turn effect to class 6; twice per turn allows 2 uses, refuses the third.

## 75. You can only control 1: only_one(...) (2026-10-25)

**IDA (named, commented).** **`Card_GetOnlyOneKind`** (0x1407448C0, id) is the game's uniqueness rule by sorted id lists: 1 = `g_OnlyOneControlIds` (0x140BF76A0, 104 ids: "You can only control 1 X" - Gravekeeper's Chief, Noble Arms - Caliburn, Virgil ...), 2 = `g_OnlyOneOnFieldIds` (0x140BD1498, 10 ids: only one face-up on the whole field - Dragon Ice, Rai-Jin ...), 3 = `g_OnlyOneArchetypeIds` (0x140BCA998, 25 ids: Earthbound Immortals, Malefic, Kaiju). **`Card_MayPlay_UniqueAndDeclared`** (0x14005AAE0, player, id) refuses a summon / activation for kind 1 while the player already has one face-up (**`Count_FaceUpSameNameOnField`** 0x1400167E0: zones 0-12, Get_CardCodeAtZone + Card_IsSameName - FLIRT had labelled it a CRT strtox helper), kind 2 while either player has one, kind 3 per archetype (Malefic with Malefic Territory ...); it also applies Prohibition's declared name and marker 9283.

**Yu-Gi-Oh-Effects (Lock.cpp).** `"onlyOne": "you" | "field"`: `Hook_OnlyOne` answers kind 1 / 2 for the custom card, the game does the rest.

**Language / blocks / translator.** `only_one;` / `only_one(field);` (one per card). BAB: "There can only be 1 face-up copy of this card [on your field | on the whole field]". Translator: "You can only control 1 (face-up) "<this card>"" -> only_one, "There can only be 1 face-up "<this card>" on the field" -> only_one(field) (another card's name is left as a rule). The Cards mod writes a card's own name as "this card", which the translator accepts too. Effects read 1172.

## 76. This card can attack directly: attack_directly (2026-10-25)

**IDA (named, commented).** **`Attack_GetDirectAttackRule`** (0x140144DF0, player, zone, ignoreId; 57 callers: the attack menu, the AI, battle) answers 0 = only when the opponent has no monsters (also when **`Attack_DirectAttackNotPrevented`** 0x1401446E0 fails: "cannot attack" markers 12477 / 14286, per-id monsters that cannot attack directly, attached cards in `g_NoDirectAttackAttachIds` 0x140B10D88 (21), a target that forces attacks (`Attack_MustTarget`), field locks 5737 / 6922 / 10092 ... Poison Rose, Triamid Sphinx, FA Turbo Charger), 2 = cannot attack directly (markers 4820 / 6141, Duck Dummy ...), 1 = can attack directly: attached cards in `g_DirectAttackAttachIds` (0x140B10DF0, 36), markers 5035 / 6988 / 9756 ..., or - face-up, not negated (UNK_03 & 0x400), id != ignoreId - its own id in **`g_DirectAttackIds`** (0x140B10F80, 40: Jinzo - Returner, Wattcobra, Altergeist Meluseek, Sky Striker Ace - Hayate, T.G. Drill Fish ...) or archetype 69, then a per-id ladder.

**Yu-Gi-Oh-Effects (Lock.cpp).** `"attackDirectly": true`: `Hook_DirectRule` turns the game's 0 into 1 for the face-up, not disabled / negated custom card when `Attack_DirectAttackNotPrevented` allows it (a 2 or a prevented attack stays).

**Language / blocks / translator.** `attack_directly;`. BAB: "This card can attack directly". Translator: "This card can attack (your opponent) directly".

## 77. You cannot Special Summon the turn you activate this: no_special_summon(...) (2026-10-25)

**IDA (named, typed, commented).** The game's cards do it with markers keyed by their own id in the player's list (zone 13): a resolution step such as **`Slot0_AddOwnIdPlayerMarker`** (0x1401776B0: msg 53, arg1 = ((turns << 4 | lifetime) << 8) | 13, arg2 = card id) adds it, and **`Summon_CheckPlayerRestrictions`** (0x140058440, player, cardId, instance, fromZone 13 hand / 14 Extra Deck / 15 Deck / 16 GY / 17 banished, sourceKind) refuses a Special Summon while the marker exists and the monster is not of the allowed kind (`Has_FlagMarkerKey(player, 13, id)` or **`FlagMarkerList_FindKeyLifetime`** 0x140052130 on the player's list head: 10500 Battlin' Boxer, 10895 Chronomaly, 12695 Red Rising Dragon DARK Dragon Synchro, 12742 Windwitch, 13786 Link Turret DARK Link ...). It is one of the tests of `Check_CanSpecialSummonByEffect` (0x14005AC50) with **`Player_CanSpecialSummonAtAll`** (0x140055FD0), **`Summon_CheckSourceZoneRules`** (0x14005AE40), **`Summon_CheckProcedureRules`** (0x14005A650) and **`Summon_CheckReceiverRules`** (0x140056D00). `Register_UseLimit_Marker` (0x1400E0E50) runs once per activation after the cost; its remaining classes are now named too: 11 = zone marker lifetime 3, 12 = lifetime 4 (lasts while the card stays in the zone), 14 = zone status bit 43.

**Yu-Gi-Oh-Effects (EffectClone.cpp).** `"summonLock": {"from": "any" | "extra", "kind": "fusion" | "synchro" | "xyz" | "link" | "ritual", "race", "attribute", "archetype"}` on an effect: `Hook_RegisterLimit` adds marker (controller, 13, the custom id, lifetime 1) when ANY effect of the card is activated (the record does not say which part; noted), `Hook_SummonRestrictions` refuses the summons the rule forbids while it is there ("extra": only summons from zone 14 are tested; an "except" with no filter forbids all). Activating means the restriction starts then: "you cannot have Special Summoned before activating it" is not enforced (noted).

**Language / blocks / translator.** `no_special_summon;`, `no_special_summon(extra, synchro, except monster where attribute = Dark);` after an effect (after its limit). BAB: the hat's "the turn you activate it, you cannot Special Summon [monsters | from the Extra Deck] except [kind] [selector]". Translator: "You cannot Special Summon (any) (monsters) (from the Extra Deck) (, except X,) (during) the turn you activate this card / effect / either of this card's effects (, except X)" - a Type / Attribute / archetype / summon-kind "except"; a sentence before the effects goes to the first one. Most of these cards' effects are not translated yet, so 29 sentences still wait for their effect.

## 78. If you control "X": controls(card where name = N) (2026-10-25)

The filter-row `controls(...)` (Quillbolt Hedgehog's field scan) cannot test a card name. A name-only selector now compiles to `"controlsName": N` + require `"controlsName"`: **`Check_ControlsName`** (EffectClone.cpp) asks the game's **`Count_FaceUpSameNameOnField`** (0x1400167E0, section 75) for the effect's controller - face-up, by name (so a custom card with `name_as` counts too). It needs no filter row, so it combines with destroy / banish. Translator: "If you control (a face-up) "X":" heads and "while you control "X"" asides. Effects read 1183.

## 79. During the End Phase, if this card is in the GY because it was sent there this turn: grave_end_phase (2026-10-25)

**IDA (named, typed, commented).** End Phase rows {0x14014E000, Ruffian Railcar 10863 / Scarm 11317 / Harpie Harpist 11657} call **`PhaseHandler_End_ArrivedThisTurn`** (0x14014DDC0, turn player, card id, zone; wrappers **`..._ArrivedInGraveThisTurn`** 0x14014E000 zone 16, **`..._ArrivedBanishedThisTurn`** 0x14014E360 zone 17, 0x14014E370 zone 18): for player = turn ^ parity it walks that player's marker list for (key = card id, lifetime 1-2, counter 0, value = card instance); if the instance is still in that zone with that id it offers it with `Offer_EffectByCardRef(id | (zone | player << 15 | 0xC40) << 16)` - table 2, event 6 - and returns 0. The marker is added by `Eval_CardMovedTriggers` when such a card arrives (case 10863 ...: msg 53, arg1 0x10D = zone 13 lifetime 1, arg2 id, arg3 instance). Both passes run, so it is either player's End Phase. Also named: `Get_RestrictionMarkerKey_ByEffectId` (0x1401F8950).

**Yu-Gi-Oh-Effects (EffectClone.cpp).** Own triggers `grave_end_phase` / `banished_end_phase` (event 6, table 2 rows): `Hook_MoveEvaluator` adds the arrival marker (owner, 13, engine id, instance, lifetime 1) when the custom card reaches the GY / is banished; a virtual End Phase row runs the game's handler (`g_PhaseFamilies`), which offers the card from that zone. No game card is borrowed.

**Language / blocks / translator / editor.** `on grave_end_phase: ...;`, `on banished_end_phase: ...;`. BAB: the "During" hat lists both. Translator: "(Once per turn,) during the End Phase, if this card is in the GY because it was sent there this turn" (and the banished form). The script editor's keyword / help lists now carry every word added today (aura ... no_special_summon, controls). Not yet: "... because it was destroyed by battle or card effect and sent there this turn", "if this card was Special Summoned this turn". Effects read 1185.

## 80. At the start of the Damage Step, if this card battles / attacks (2026-10-25)

**IDA (named, typed, commented).** **`Battle_StartOfDamageStepMachine`** (0x140210C10, cases 0-5) runs **`Eval_StartOfDamageStepTriggers_Ev20`** (0x140210580, isAttackerSide) for the attacker (1) and the defender (0): for that side's battling monster (`g_BattleSides`) a per-id ladder offers `Offer_EffectOfCardAtZone(player, zone, 20, param)`, param = attacker player | attacker zone << 8 | (defender player | target zone << 8) << 16 - "if this card battles" ids on either side unless it is a direct attack, "if this card attacks" ids only for the attacker; then attached cards, archetype-keyed named cards, GY / Pendulum ones. The end of the Damage Step has no evaluator of its own (its cards are gated in `Cond_GenericCanActivate_Ladder`), so it is not built.

**Yu-Gi-Oh-Effects (EffectClone.cpp).** Own triggers `damage_step_battles` / `damage_step_attacks` (event 20, table 1): `Hook_DamageStepStart` offers the face-up custom monster on that side the same way after the game's pass (never on a direct attack; attacks = attacker side only). Event 20 joins the known trigger events.

**Language / blocks / translator / editor.** `on damage_step_battles: ...;`, `on damage_step_attacks: ...;`. BAB: event hats. Translator: "At the start of the Damage Step, if this card battles / attacks (an opponent's monster / a monster)". Actions that refer to "that monster" (the one it battles) are not read yet. Effects read 1186.

## 81. During the (your / your opponent's) Battle Phase (2026-10-25)

**IDA.** The duel's current phase is the int at PlayerState +0x3788 (0x14349B3C8; `ContEffect_CoversZone` compares it with 3 = BATTLE and 5 = END) and the turn player the int at +0x3774 (0x14349B3B4). Both are now `YGO::Addr::Duel_CurrentPhase` / `Duel_TurnPlayer` (YuGiOh-Addresses.h).

**Yu-Gi-Oh-Effects (EffectClone.cpp).** Requirements `"battlePhase"`, `"yourBattlePhase"`, `"opponentBattlePhase"`: `Check_BattlePhase<0|1|2>` - phase == 3, and the turn player against the effect's controller.

**Language / blocks / translator / editor.** `if battle_phase: ...; quick;` (also `your_battle_phase`, `opponent_battle_phase`). BAB: "Only if" > "it is [either player's | your | your opponent's] Battle Phase". Translator: "During the / either player's / your / your opponent's Battle Phase" heads; a monster's such effect must be a Quick Effect (an ignition effect only runs in the Main Phase), otherwise it is refused. Effects read 1191, 28 whole cards.

## 82. For the rest of this turn after this card resolves; If your LP are N or less (2026-10-26)

**Resolve-time summon locks.** "..., also you cannot Special Summon (from the Extra Deck) for the rest of this turn, except X" and "For the rest of this turn after this card resolves, you cannot Special Summon ..." start when the effect has RESOLVED (a negated activation locks nothing; one whose search found nothing still locks). Yu-Gi-Oh-Effects: `"summonLock": {"when": "resolve", ...}` - `EffectResolved` adds the controller's marker (13, custom id, lifetime 1) when the part's slot 0 returns 0 (also when a step chain stops because a step found nothing); `Hook_RegisterLimit` skips such locks; `Hook_SummonRestrictions` enforces both kinds. EffectScript `no_special_summon(resolved, ...)`; BAB hat row "after it resolves this turn,"; the translator strips the tail from the action (`ResolveLockTail`) and keeps one lock per card.

**LP conditions.** The engine reads LP inline everywhere (no getter): side block +0 XOR `Duel_DuelEngine.LpXorKey` (e.g. **`Duel_IsAnyPlayerOutOfLP`** 0x14003E020; `YGO::DUELSTATE::LifePoints`). Requirements `"lpAtMost"` (with `"lpAtMost": N`) and `"lpLower"` = `Check_LifePoints<0|1>`. EffectScript `if lp_at_most(N): ...`, `if lp_lower: ...`; BAB "Only if" > "your LP are N or less" / "your LP are lower than your opponent's"; translator heads "If your LP are N or less", "If your LP are lower than your opponent's" (optionally after "During your Main Phase,").

Requirement `"bothExtraDecks"` (2026-10-31, the first word from Master Duel's own step functions): both players have a card in their Extra
Deck. On the game's engine `Check_BothExtraDecks` reads each side's Extra Deck count (`Duel::PlayerState::Player` +0x18); under duel.dll the
plugin runs duel.dll's own `Cond_BothPlayersHaveExtraDeckCard` (0x180286840, the Dogmatika cards' slot 2; `MdCheckTwin`). EffectScript
`if both_extra_decks: ...`; BAB "Only if" > "both players have a card in their Extra Deck".

## 83. You can reveal this card in your hand (2026-10-26)

**IDA.** The game has no reveal step: Impcantation Chalislime (14109) and Danger! Thunderbird! (14067) are hand-effects table rows (EffectTable_Kind2 2441 / 2398) with an EMPTY slot 3; Chalislime's slot 0 **`Slot0_HandDiscardPickThen_ById`** (0x14058B680) opens its "discard 1 card" pick, then runs the card's part by id. Revealing = activating from the hand.

**Translator.** "reveal this card in your hand; X" (also "..., then target ...") on a monster with no trigger in the head = an own hand effect (`on in_hand: X`). With a trigger in the head (activated from the hand in response to an event) it is not read yet.

## 84. Send this card from your hand / the field to the GY: send_self, send_self_field (2026-10-26)

**IDA.** **`Cost_SendSelfFromHandToGrave`** (0x1401FF020, ~75 hand rows: Fabled Grimro 8093, Garbage Ogre 10377) = `Move_HandCardToGraveOrBanish_Ev32(player, instance, 0, 16, 0)` - a move, not a discard (`Cost_DiscardSelf` uses `Hand_DiscardAt`), generic except 14258. **`Cost_SendSelfFromFieldToGrave`** (0x1401FCF30, ~70 field rows: Wightprincess 12768, World Chalice Guardragon 13060) = `Action_DestroyZoneMask` of the own zone with reason 16 and the record's id blanked (a cost, no immunity); id-keyed extras save state for 25 ids first, every other id takes the plain path. No vanilla monster has a plain field ignition with it (only Quick Effects from hand or field).

**Yu-Gi-Oh-Effects.** `"costSlot": "sendSelfFromHand" | "sendSelfFromField"` puts that cost in slot 3 (`SlotOverride[3]`) of the donor row: Hecatrice's hand row (7572) / Planet Pathfinder's field row (10232). The compiler sets `"effectOnly"` (the donor lends only its row's place, never its id; `IdTest` still answers the hand-effect list from the donor).

**Language / blocks / translator / editor.** `cost send_self: ...` (requires canDiscardSelf = in the hand), `cost send_self_field: ...`. BAB self-cost dropdown. Translator: "send this card from your hand to the GY", "send this (face-up) card (from the field / you control) to the GY" (in the hand after "If this card is in your hand" = send_self). "From your hand or field" (two rows in the game) is not read yet. Effects read 1216, 39 whole cards.

**Also traced (no build yet): destroy replacement.** `Destroy_TryReplacement` (0x1400CF810) runs only for effect destruction and is all id-hardcoded (self-protecting ids with marker 1001, field protectors gated by **`Zone_IsUnaffectedByCardAtZone`**, counter removal via **`Zone_ChangeCounter`**). There is no generic or GY-card path: "If a X you control would be destroyed, you can banish this card from your GY instead" (about 100 sentences) needs a hook there plus battle destruction's own path.

## 85. Banish / Tribute this card from the hand or field; "from your hand or field"; banish N X from your GY (2026-10-26)

**IDA (named, typed, commented).** The game's self costs, each fully generic:
- **`Cost_BanishSelfFromHand`** (0x140209040): Move_HandCardToGraveOrBanish_Ev32 dest 17.
- **`Cost_TributeSelfFromHand`** (0x14020A140): the same mover to the GY with flag 0x1000 = Tributed (Artifact Lancea, Crystal Keeper).
- **`Cost_BanishSelfFromField`** (0x1401FD800): **`Queue_BanishZone_Reason17`** - DestroyQueue reason 17 is BANISH (Card_Destroy's old "battle" note was wrong); Kozmo Farmgirl, The Weather Snowy Canvas.

A card usable from two zones has one row per zone (Battlin' Boxer Counterpunch: Cost_BanishSelfFromGrave + Cost_BanishSelfFromHand; Artifact Lancea: Cost_TributeSelfFromHand + Cost_TributeSelf).

**`Cost_BanishPickedCards`** (0x1401F99D0, 180 rows) is the game's "banish N <filtered> cards" cost: count = **`Get_CostPickCount_ById`** (0x1401F7450, default 1), the list = the id's deck filter row (`UI_BeginCardListSelect`, `Get_DeckFilterParamForEffect_Ladder`), and ids such as Laval Lakeside Lady 9585 / Destiny HERO - Celestial 12707 also banish this card from the GY first. Plain ids (The Beginning of the End 7452) only pick.

**Yu-Gi-Oh-Effects.**
- `"costSlot": "banishSelfFromHand" | "banishSelfFromField" | "tributeSelfFromHand"` (as section 84: Hecatrice's hand row / Planet Pathfinder's field row, effectOnly).
- `"costPick": {"count": N, "self": bool, "deck": {filter}}`: a sub-clone (From = 7452, or 9585 with self) with its own deck filter. Slot 3 = Cost_BanishPickedCards, run with effect[0] = the sub-clone's id and `g_Active` = the sub-clone, so `DeckFilterClone` / `Hook_DeckFilterIndex` / `Hook_CostPickCount` (0x1401F7450) answer its list and count. Slot 2 checks there are N candidates (Deck_CountMatchingCards through the sub-clone). The list leaves this card out (exceptSelf, `SetOwner` reaches the sub-clone). With self the effect is activated from the GY (Rose Lover's row + canBanishSelfFromGrave).
- Not tested in a duel; the risk is a per-card check the list screen runs outside the slot (it would see the vanilla 7452 / 9585 row).

**Language / blocks / translator / editor.** `cost banish_self_hand:`, `cost banish_self_field:`, `cost tribute_self_hand:`, `cost banish_grave(N, selector):`, `cost banish_self_and(N, selector):`. BAB: self-cost dropdown + "banish [this card and] N [selector] from your GY". Translator: "banish this card from your hand / you control / from the field", "Tribute this card from your hand", "banish this card" placed by the head, "<send|banish|Tribute> this card from your hand or field / hand or GY / field or GY" = the effect twice joined with `or` (one limit), "banish (this card and) N X from your GY", and "<any readable cost>, then target ...". Effects read 1253, 109 whole cards (44 this run + 65 attached earlier).

**Coverage workbook.** `docs/Card Lists/Effects Coverage.xlsx` (replaces Effects To Write.csv): tables Fully read / Partially read / Not read with a plain-words "why" column, made by `Tools/Scripts/effects_coverage.py <cards.json> <work.csv> <out.xlsx>` from a WolfX `--attach-effects` run.

## 86. Moving cards: move(...) (2026-10-26)

**Why.** The biggest group of unread sentences moves cards between zones: shuffle into the Deck, place on top / bottom, return to the hand / Extra Deck, banish, send, destroy, targeted or picked. One general step replaces a donor per wording.

**IDA (2026-10-26).** The game's target machines all end the same way (Slot0_DestroyFirstTarget, Slot0_ReturnTargetToHand, Slot0_BanishTarget): clear the "moved" masks (DuelState +0x1B3A / +0x1B42), `Has_SelectedTarget(record, i, &zoneWord)` (0x14008C040; low byte side, high byte zone), `Check_TargetStillValid` (0x1400DFEE0), then **`Action_DestroyZoneMask`** (0x1400D5150: record, zone bits, REASON = destination zone, flags). DestroyQueue_Process moves the cards after the slot returns: reason 13 hand, 14 Extra Deck, 15 Deck (flag 0x800 bottom, 0x4000000 shuffle afterwards), 16 GY (with 0x2000002 = destroyed by an effect), 17 banished (0x80 face-down). GY cards go to the Deck with **`Move_GraveCardToDeck`** (0x1400889A0, Pot of Avarice's machine 0x140228930), hand cards with **`Hand_ReturnCardToDeck`** (0x1400888F0), then **`Deck_PushShuffleMessages`** (0x1400A7E00). GY / banished cards go to the hand with **`Effect_AddGraveCardToHand`** (0x140089C40) / **`Effect_AddBanishedCardToHand`** (0x140089CB0); `Card_MoveListCardToHand` only takes Deck cards (its old comment was wrong). Several targets: Twin Twisters' picker **0x140094DC0** (its maximum from 0x140094490, its zone test from `Pick_ZoneFilter_BySlot2`).

**Yu-Gi-Oh-Effects.** `"move": {"scope": "target" | "self", "to": "hand" | "grave" | "destroy" | "banished" | "banishedFaceDown" | "deck" | "deckTop" | "deckBottom" | "extra", "count": N}` = **`Slot0_OwnMove`**. Scope target keeps Remove Trap's targeting row (slot 1 = `Target_Generic_FromFilterRow` with this clone's "filter") and queues every selected target with the destination's reason / flags. `count` > 1 puts the multi-target picker in slot 4; `Hook_TargetCount` answers N and `Hook_PickFilter` tests each zone against the target filter row. Scope self: on the field the same queue; in the GY / banished to the hand with the movers above; in the GY to the Deck with Move_GraveCardToDeck. Hand / GY picks to the Deck use the own pick-and-act step (`"own": "toDeck" | "toDeckTop" | "toDeckBottom" | "toExtra"`). Also fixed: own to-hand picks from the GY / banished cards now use the right movers. A first `move(target, ...)` may be followed by other steps (`"targetSlots"`: the card's row takes over the targeting).

**Language / blocks / translator.** `move(target[, N][, side][, selector], dest)`, `move(self, dest)`, `move(hand | grave [or ...][, choices], to_deck | to_deck_top | to_deck_bottom | to_extra)` (EffectLanguage.md). BAB: one "move" block (what / up to N / side / cards / destination). Translator: "target 1 X (you control / your opponent controls / on the field); shuffle / place / return / send it ...", "target (up to) N X; ... them", "target 1 X in your GY; shuffle it into the Deck", "shuffle / place / return 1 X from your GY / hand (or ...) into / on / to ...", and "<move> this card ..." (add / return / shuffle / place / send / banish). Effects read 1317, 120 whole cards (was 1253 / 109). Not tested in a duel.

## 87. Summon reactions with a filter: on monster_summoned(is X, own) (2026-10-26)

`on monster_summoned:` = "If a monster(s) is Normal or Special Summoned" (events 7 or 9, both players), beside section 68's three. The summon reactions take two more conditions: **`is <selector>`** (the summoned monster's Type / Attribute / archetype / kind, tested by `SummonMatches` on the summon event record as the summon lock's tests) and **`own` / `opponent`** (whose field it went to: `Summon.ToSide`; `Hook_SummonReaction` offers the effect only to that player, read from the record's +2 word). EffectScript `on monster_summoned(is monster where race = Insect, own): ...`; compiler `"summon": {"race" / "attribute" / "archetype" / "kind", "to": "own" | "opponent"}`. BAB: the event hat's "summoned monster [side] [cards]" slots. Not tested in a duel.

## 88. Donor-free rows: rawRow / rawSlots (2026-10-26)

**Why.** A clone copied a game card's row and ran its slots with that card's id swapped into the record, and lent the card that id for the duel - so every id-keyed ladder and list in the engine treated it as the donor. The engine functions themselves are generic; only the id was borrowed.

**IDA (all named / commented, IDB saved).**
- **Rows.** Extra words 1 and 2 are 0 in every row of every table (padding); Extra[0] = use-limit class + 0x10 / 0x20 bits. Places: table 0 Spell/Trap, 1 monster triggers, 2 hand / GY effects (Hecatrice, Rose Lover, Quillbolt), 3 field ignitions (Planet Pathfinder, Thunder End Dragon, Maryokutai). Remove Trap's row = {0, Slot0_DestroyFirstTarget, Target_Generic_FromFilterRow, -, -, Slot4_ActivationPrompt_Text281}: no card-specific code.
- **Slots for an unknown id.** `Target_Generic_FromFilterRow` = Get_SecondaryRow_1401B0F40(id, 0) + the filter (null row = nothing targetable, so Hook_GetRow answers). `Slot4_ActivationPrompt_Driver` (0x14008C9D0): id branches only for 9 auto-pick ids and 10668, else prompt 281 then `Pick_ZoneFilter_BySlot2` (the row's slot 1). `Slot4_MultiTargetPicker_StateMachine` (0x140094DC0): prompt text by id, default 277 / 93; zone test `Pick_ZoneFilter_MultiTarget` (0x1400937A0) = Pick_ZoneFilter_BySlot2 + not picked + per-id side rules. `Cond_ListHasEnoughMatches` (0x1400FA680): needs 1 for an unknown id, counts through Deck_CountMatchingCards.
- **The activation gate's ladders** (the plugin's IdLadder hooks), for an id they do not list: `Get_EffectSpellSpeed_ByEffectId` ends in **`Get_DefaultSpellSpeedForCard`** (0x140743C30: Trap 2, Counter Trap 3, Quick-Play 2, monster 1 unless in IdList_QuickEffectMonsters_783); `Check_EffectSourceIsMonsterId` = Kind_IsMonster of the record's id; `Ladder_IsOptionalTrigger_ByEffectId` 0 for table 0; `Ladder_ChainSpeedOverride_ByEffectId` 0; `Check_IsNegateSummonListCard` (37 ids) no; `sub_140131520` (Damage Step allowed) Counter Traps + lists only; `Check_IsOwnMainPhaseSummonWindow` no id. So the card's OWN kind gives the right answers - no donor needed (only Quick Effect keeps the override).
- **Deck lists.** Callers take a row INDEX into YGO_Effects_DeckFilterTable and read the row; `Scan_Deck_Own` passes row+0 to ScanGeneric_Deck as the effect's own card ("same name as this card", "except this card" flag 0x10 in sub_14052B440, Get_SummonSourceKind). Row 5919 = ghost id 15234 (no game card), between 15232 and the 65532 sentinels.

**Yu-Gi-Oh-Effects.** `"rawSlots": true`: slots 0/1/2/4 are exactly SlotOverride (empty stays empty), run with the record's own id (no orig lookup, no swap); `Clone::EngineId` = the id the engine used, and `MatchesFrom` replaces `id == From` in the cost-count / detach-count / deck-filter hooks. `"rawRow": true` (implies rawSlots, no "from"): the row is `OwnTriggerRow` (own id, Extra 0 + the script's limit class) answering the Spell/Trap table (`RawRowFits`; with ownTrigger the own trigger's lookups, `OwnRowAnswers`); IdLadder hooks do not swap, IdTest hooks skip it, no id lending. A raw list uses the scratch deck row 5919 with the card's engine id (`PatchRawDeckRow`, put back before any other lookup). Move's target slots are named explicitly (0x1401B0FF0, 0x14008CCC0). Still donors, by design for now: negate (rewrites the donor's NegateTable row), costs from a cost card (CostFrom / costPick sub-clones), and the trigger rows of non-own triggers.

**Compiler.** `move(...)` compiles to `rawRow`. `ComposeOnto` replaces the five "actionFrom = from; from = donor" sites: a raw action keeps `rawSlots` and only takes the donor row's PLACE (effectOnly), a donor action composes as before; a raw action never takes a "direct" trigger card. Tests: Test-Compiler raw='row' / 'slots' checks (no from, no actionFrom), `on end_phase: move(...)` = own trigger + raw row. Not tested in a duel.

**Next for donor-free.** Own rows for tables 1-3 (monster triggers, hand / GY, field ignition): trace each place's slot-3 wrappers (Slot3_MonsterEffect_WithHooks family, compared by address) and slot-2 `Slot_Delegate_ToSecondaryTable` (per-card condition by id), then move every remaining action (search, special_summon, revive, send, destroy ...) onto raw slots.

**Next (ranked by cards made fully read, 2026-10-26 run).** Pick-N costs (Tribute / discard / send / banish / reveal N filtered cards from any zone): 110 cards on their own, 540 touched; then continuous While / Each time rules, equip, battle timing, plain conditions (you control / have in GY / all are / no monsters), phase windows.

## 89. Continuous rules with a condition or a count: while ... / per ... (2026-10-26)

**What the "continuous rules" bucket is.** 1,035 unread `rule:` sentences (2026-10-26 run), but 14 different mechanics: While-conditions (201), protection variants (131), ATK "for each / while" (94), "your opponent / neither player cannot ..." (91), use limits (72), summon conditions (67), activation-turn restrictions (64), attack rules (61), Each time (48), once per Chain (38), material restrictions (35), chain locks (23), damage rules (17), and a tail. Built all, they would finish only ~77 cards on their own (they share cards with unread effects). This section builds the general piece under the two biggest: a condition gate and a count on the continuous rules already built (sections 69-76).

**IDA (named, commented, IDB saved).**
- **`Count_XyzMaterialsAtZone`** (0x140041190, was Count_ZoneRecordsKey1019Kind5): the zone's linked records (head at InPlay_Card.UNK_01+2) with key 1019 and kind 5 = Xyz materials. `Cond_CanDetachXyzMaterials` (0x1400FBFF0) counts the card's own materials with it; **`Detach_ZoneCanGiveMaterial`** (0x1401A8350) refuses a zone with none; **`Count_ZonesPassingFilter`** (0x1400E02C0) counts zones 0-12 of both players passing a per-zone test (generic target filters through Filter_CardAtZoneMatchesFlags_TwoPass on the secondary row).
- **Printed data by id** (FULL_CARD_PROPS, what MoreCards fills for custom cards): `Get_MonsterType` (0x14081A7B0, race), `Get_AttributeFromFullCardProps` (0x14081A5D0), `Get_CardTypeFromFullCardPropsByKonamiId` (0x14081A650, kind: 13 Spell, 14 Trap). Field cards keep using the effective readers (`Get_EffectiveRace` - with -2 it ORs 0x100 when a field card changed the race, now masked in the aura filter too - `Get_AttributeMask`, `Get_CardCodeAtZone`), names `Card_IsSameName`, archetypes `Is_CardInNamedArchetype_Thunk`.
- **Piles.** `Duel::PlayerState::Player` at block +0x0C: hand, Deck, GY, Extra Deck, banished counts (+0x0C / +0x10 / +0x14 / +0x18 / +0x1C); packed card ids at +0x19C hand, +0x7B4 GY, +0xA0C banished (section 28).
- `Count_ActiveAttachedCards` (0x140050650) is NOT materials: it counts equip-style attached cards by id / archetype.

**Yu-Gi-Oh-Effects (Lock.cpp).** A count test `{"count": "field" | "grave" | "hand" | "banished" | "materials" | "lp" | "defense", "side": "you" | "opponent" | "both", "min", "max", "other", "kind": "monster" | "spell" | "trap", "race", "attribute", "archetype", "name"}` holds while min (default 1; 0 for lp) <= count <= max. `"while": [tests]` on an aura, a protect rule or `"attackDirectly": {"while": [...]}`: the rule applies only while all hold (checked after the card's own face-up / not negated test, in Hook_Calc, ScanProtectors and Hook_DirectRule). `"per": test` on an aura multiplies its amounts by the count. Field counts are face-up cards in zones 0-12 ("other" leaves the rule card out); "materials" and "defense" are the rule card's own. No native routing test: Lock.cpp still has no seam for its absolute game reads (as sections 69 / 71).

**Language / compiler / blocks / translator.**

    while controls(monster where archetype = "Ancient Warriors", other): untargetable(opponent_effects);
    while in_grave(25): aura(atk 2500, def 2500, self);        while not in_hand(card): aura(atk -800, opponent);
    while not controls(monster, other) and lp_at_most(2000): indestructible(battle);
    while has_material: attack_directly;                        while defense_position: indestructible(battle, own);
    aura(atk 400, self, per controls(other, monster where race = Plant));   aura(atk 500, self, per materials);

Count tests: `controls` / `in_grave` / `in_hand` / `banished` `(N, own | opponent | any, other, selector)` (kind, race, attribute, archetype, name; level / ATK refused), `materials`; `not` = fewer than N. BAB: the aura / protection / attack-directly blocks gained "only while" (and the aura "for each") inputs taking the new `ys_count` ("at least / fewer than N cards of yours / ... on the field / in the GY / in the hand / banished, other than this card"), `ys_count_self` (has material / Defense Position / LP at most) and `ys_count_and` blocks ("While face-up" category). Translator: "While <condition>, <rule>[, also <rule>]" with conditions you control (a / another / N or more / no (other)) X, your opponent controls ..., X is on the field / in your GY, you have N or more / no cards in your GY / hand, N or more of your cards are banished, this card has material, is in Defense Position, your LP are N or less ("this card is face-up on the field" needs no test); "(This card) gains N ATK(/DEF) for each X you control / in your GY / card in your hand / material attached to it (, except this card)"; and the player-worded protections "Your opponent cannot target this card with card effects", "Your opponent('s monsters) cannot target this card for attacks", "Neither player can target ...", "unaffected by (your opponent's) card effects", "an opponent's card effects". Selector approximations are refused (a count must be exact).

**Coverage.** Effects read 1356 (from 1317), fully read cards 125 (from 120), not read 2956 (from 2995); all 1245 partial scripts compile. Tests: Test-Compiler while / per cases (field + other + archetype, GY count, not in hand, two tests with LP, materials on attack_directly, per materials / per GY race, a while on one aura only) and refusals (while before name_as, a level filter in a count, two per), block round trips. Not tested in a duel.

**Still open in the bucket.** "While this card is an Equip Card / equipped ..." (equip-only rules: `g_BattleIndestructibleAttachIds` path), Token restrictions ("the player who Summoned it cannot ..."), "this card points to", "this <kind> Summoned card" (how it was summoned is not tested), Attack Position / Level filters in counts, "also DARK-Attribute", multi-attack, "you choose the attack targets", the player locks ("your opponent cannot activate ..."), use limits whose effect is not read yet.
## 90. Pick costs with no donor: discard / Tribute / send / reveal / banish N X (2026-10-26)

**Why.** "Discard 1 Spell; ...", "Tribute 1 Dragon monster; ...", "send 1 WATER monster from your hand to the GY; ..." were the largest single blocker (the 2026-10-26 ranking: 110 cards finished on their own). The old costs borrowed a card for its cost slot (Lightning Vortex 5217 discard, Share the Pain 4889 Tribute, The Beginning of the End 7452 / Laval Lakeside Lady 9585 banish) and could not filter.

**IDA (all named, typed, commented, IDB saved).** Every cost machine asks **`Cost_IsWaived`** (0x1400A9B60) first and steps on **g_CostStep** (dword 0x14349C148 = stru_14349B580 + 0xBC8; +0xBC0 its pick counter). For an id they do not list:
- **`Cost_DiscardPickedHandCards`** (0x1401F8010, 101 rows): count **`Get_DiscardCount_ById`** (0x1401F6F90: IdList_DiscardCostOne_336 / IdList_HandEffectA_120 = 1, a few 2-5, else **0**); prompt 0x15B (`UI_ShowCostPrompt`), hand picker mode 8 whose per-card test is **`Pick_HandFilter_ByDeckRow`** (0x14015FA70): own hand, `Deck_CountMatchingCards(player, record id, handIndex)` - the hand index rides in r8 into the row's scan function (checked in the disassembly), and the discard rows' scan is **`Scan_HandCardAt_RowFlags`** (0x14052F350: that one hand card against the row's name test and flags); `Hand_DiscardAt` each pick.
- **`Cost_SendPickedHandCardsToGrave`** (0x1401FF090, 68): the same picker, count **`Get_SendHandCount_ById`** (0x1401F7120: IdList_SendHandCostOne_137 = 1, else 0), `Move_HandCardToGraveOrBanish_Ev32` dest 0x10.
- **`Cost_TributeOne`** (0x1401FDDF0) / **`Cost_TributePickedMonsters`** (0x1401FE930, 44): min / max from **`Get_TributeCount_ById`** (0x1401F76A0, 1 / 1 by default); state 0 needs **`Count_ZonesPassingRowSlot2`** > 0; zone picker mode 9 = `Pick_ZoneFilter_TributeWrap` over **`Pick_ZoneFilter_NotPickedYet`** -> `Pick_ZoneFilter_BySlot2` (the plugin's Hook_PickFilter); **`Queue_TributeZoneMask`** (0x1400DA9F0). The id ladders only store the picked card's ATK / Level for listed ids.
- **`Cost_RevealPickedHandCards`** (0x140202770, 37): reveals ONE pick for an unknown id (`RevealList_AddInstance` / `RevealList_GetInstance`, message 83 each).
- **`Cost_SendPickedHandOrFieldCardToGrave`** (0x140208E00, 33): one pick through **`Pick_HandOrFieldFilter`** (0x14015FBD0: zone 13 = the hand test, zones 0-12 = `Pick_ZoneFilter_BySlot2`, or `Check_TargetStillValid` when the record has a target); hand -> GY, field -> `Queue_SendZoneToGrave_Cost`.
- `Cost_BanishPickedCards` (0x1401F99D0): prompt 0x56, count `Get_CostPickCount_ById`, banish the picks; only "also this card" is id-keyed (9585 and others call `Cost_BanishSelfFromGrave` in state 2, before the picks).
- Also named: `Cost_PayLifePoints` (0x1401F7CC0, amount `Get_CostLpAmount_ById`), `Slot3_ConfirmThenCostAndRegister` (0x1401FF340), `Slot3_TwoStageCost_ById` (0x140200180 over `g_TwoStageCostTable` 0x140B29990), `Cost_DiscardCount_PlayerChooses` (0x1401F7FD0 -> `Request_HandDiscard`), `Get_DeckFilterRowOffset_ByParam`, `UI_EndPickPrompt`, `Pick_IsChosenCardStillValid`.

**Yu-Gi-Oh-Effects.** `"costPick": {"verb": "banish" | "discard" | "send" | "tribute" | "reveal" | "sendHandOrField", "count": N, "deck": {filter}, "self", "raw"}` - every verb but a donor banish is RAW: the CostPick part has no From, `SlotOverride[3]` = the verb's machine, and the machine runs under the card's own engine id (`EngineId`). Counts: Hook_DiscardCount / **Hook_SendHandCount** / **Hook_TributeCount** (new) / Hook_CostPickCount answer the part's count for that id. The hand verbs get the row scan `handAt` (Scan_HandCardAt_RowFlags); Tribute and hand-or-field test your own field with FieldZoneMatches (Hook_PickFilter; Tribute also needs sub_14001D700). **`PayingClone(id)`**: the raw cost inside its machine (g_Active) or between its steps (**g_Paying**, set while slot 3 returns 0, cleared when it returns 1 or the effect resolves) - Hook_DeckFilterIndex (the scratch row 5919 for ANY k, since the hand picker passes the index there), DeckFilterClone and Hook_PickFilter ask it first, so the effect's own list never answers the cost's picker. The activation check (`CostCandidates`) counts what the machine could take (hand indexes one by one, Tributable matching monsters, hand + field, or the list) against N. A raw banish with "self" calls Cost_BanishSelfFromGrave when g_CostStep reaches 2, where the game does it. Effects 1.3.5.

**Language / compiler / blocks / translator.**

    cost discard(1, spell): draw(1);                       cost discard(2): ...         // discard N (matching) cards
    cost tribute(1, monster where race = Dragon): ...      cost tribute(2): ...         // Tribute N (matching) monsters you control
    cost send_hand(1, monster where attribute = Water): ...                             // send N matching cards from your hand to the GY
    cost reveal(monster where archetype = "Fiend"): ...                                 // reveal 1 matching card in your hand
    cost send_hand_field(card): ...                                                     // send 1 matching card from your hand or field
    cost banish_grave(2, monster where race = Dragon): ...                              // now raw too (no 7452 / 9585)

`discard(N)` / `tribute(N)` no longer use Lightning Vortex / Share the Pain; pay LP still borrows Delinquent Duo (next). BAB: the discard / Tribute cost blocks take an optional "which cards" input; the pick-cost block's list gained "send from your hand to the GY", "reveal from your hand", "send from your hand or field". Translator: "discard N X", "Tribute N X (you control)", "send N X from your hand to the GY", "send 1 X from your hand or (face-up) field to the GY", "reveal 1 X in your hand", and "send 1 card from your hand to the GY" is now a send, not a discard.

**Tests.** Native `TestRawPickCosts` (parse -> machine per verb, the hand picker's per-index scan, own field for Tribute / hand-or-field, donor banish unchanged for old data, the count hooks and PayingClone inside / between the machine's steps / after it). Test-Compiler: 8 cases + 2 refusals. Coverage: effects read 1426 (from 1356), fully read cards 145 (from 125), not read 2889; all 1292 partial scripts compile. Not tested in a duel.

**Open.** "Tribute ... from your hand (or field)", "send N X from your Deck / Extra Deck to the GY" (a list machine to trace), "N or more" / "any number of", "discard this card and 1 X" (two-part), pay half your LP / pay LP raw (Cost_PayLifePoints + its amount hook), the target check of Pick_HandOrFieldFilter for an effect that also targets.

## 91. Watchers: another card destroyed / left the field / sent to the GY / banished (2026-10-26)

**IDA (named, typed, commented, saved).**
* `Eval_LeaveFieldTriggers` (0x1400C2CA0, was `Eval_LeaveFieldTriggers_Battle`; `(leaveRef, notDestroyed, srcCardId, flags, a5, a6, srcZone)`) runs for EVERY field card leaving: from `Card_Destroy` (notDestroyed 0, flags = Card_Destroy's: bit 0 the source is the card's controller's opponent, bit 2 destroy) and from `Card_LeaveFieldToZone` (0x1400D3CD0, was sub_1400D3CD0; notDestroyed 1: to hand / Deck / Extra Deck). leaveRef: bits 0-8 instance, 9 player, 10-14 from zone, 17 destroyed, 18 by an effect, 20 controller parity, 21-25 to zone. Its event: 34 for a plain leave, else 22 (destroyed) or 30 (sent / banished by Card_Destroy).
* The game's watchers ("If a monster you control is destroyed ...") are per-id ladder branches in it and in `Eval_CardMovedTriggers` that call `Offer_EffectOfNamedCardOnField` (table 1, the Monster Zones or, for a non-monster, the Spell & Trap Zones) / `Offer_EffectOfNamedCardInGrave` (0x1400ABF60: every copy in that player's GY, table 2) / `Offer_EffectOfNamedCardInSpellTrapZones` (0x1400ABE00) with a literal card id, the evaluator's event and its reference as the param.

**Yu-Gi-Oh-Effects.** Triggers `card_destroyed`, `card_left_field` (from `Hook_LeaveEval`, after the game's evaluator) and `card_sent_to_grave`, `card_banished` (from `Hook_MoveEvaluator`, after the moved card's own pass; the old body is `MoveEvaluatorBody`). `"watch": {"kind", "race", "attribute", "archetype", "cardKind", "name", "side": "you" | "opponent", "from": [field | monster | spelltrap | hand | deck | grave], "by": "battle" | "effect" | "opponent", "other", "inGrave"}` (`ParseWatch`, `WatchMatches`): the moved card's id from the card instance table and its printed data, whose card it was (ref bit 9 against each watcher player), the zone it left, battle (the battling monster of that side in `g_BattleSides` was destroyed by battle), an opponent's effect (Card_Destroy flags bit 0), not this card's id. Each matching watcher is offered with `g_MenuPart` set, by name from the field (table 1) or, with inGrave, from the GY (table 2; `OwnRowFits`). Event 22 fits card_destroyed / card_left_field, 31-33 card_sent_to_grave / card_banished; 30 / 34 are not known events (own rows answer them). A battle-destroyed card does not pass Eval_CardMovedTriggers (section 29), so `card_sent_to_grave(by battle)` is refused: use `card_destroyed(..., by battle)`.

**Language / compiler / blocks / translator.** `on card_destroyed(is monster where race = Insect, own, by battle):`, `on card_left_field(own, other, by opponent):`, `on card_sent_to_grave(is monster, opponent, from hand, from deck, in_grave):`, `on card_banished:` - always an own trigger, "watch" always written; `is` takes race / attribute / archetype / name (no Level: the runtime reads printed data). BAB: the "When a card ... is destroyed / leaves the field / sent to the GY / banished" hat (which cards, whose, from, by, not this card, while this card is in your GY). Translator (`WatchHead`): "If a (face-up) <X> monster(s) / card(s) you control / your opponent controls / on the field is destroyed (by battle / card effect / an opponent's card effect) / leaves the field (by ...) / is sent (from <zones>) to the GY / is banished", "your <X> monster", "another ...", ", except this card", ", while this card is in your GY" (GY watcher; its once-per-turn counted per name), "while this card is in the Monster Zone" (dropped: a field watcher is on the field). Also on Continuous / Field Spells and Traps (the named offer scans their zone; their activation row is empty, which the game treats as an activation that does nothing). "face-up" is noted, not checked; "whose GY" is read as whose card it was (noted).

**Tests.** Native `TestOwnWatchers` (destroyed by battle / by effect, whose card, a non-destroying leave, a GY watcher on a hand-to-GY move, banish ignored, own rows per table). Test-Compiler: 5 cases + 3 refusals; blocks round trip. Coverage: effects read 1440 (from 1426), fully read 153. Not tested in a duel.

**Open.** A Normal Trap whose activation needs the event ("If a monster you control is destroyed: Target ...", Scrap Garage) needs event windows for Traps (next); the moved card as the effect's target ("Special Summon that monster"); Level / ATK filters on the moved card; "by a <X> card's effect".

## 92. Target verbs: change battle position, face-down Defense, take control (2026-10-26)

**Why.** "Target 1 X; <do something to it>" was the biggest single last blocker of partly read cards (99 cards, 2026-10-26 ranking), with one verb per family. The first three verbs the game already has as generic target machines.

**IDA (named, typed, commented, IDB saved).**
- **`Slot0_ChangeTargetsBattlePosition`** (0x14016B430, Stop Defense 4326 / Enemy Controller 5505): for an id it does not list, mode 1 and count = record word 14; per valid target (Has_SelectedTarget, Check_TargetStillValid) the flag "face-up Defense" goes to **`Duel_ChangePosition_OfferFlip_Ev12or13`** (0x140084E50) unless **`Effect_IsTargetImmune`** (0x140023110: Zone_GetImmunityMasks against the source's kind and owner): the position is toggled, a face-down monster is flipped face-up (FLIP offered).
- **`Slot0_TakeControlOfTarget`** (0x140177090, Mind Control 4817 / Change of Heart 4860): target 0; default duration byte 1 = until the End Phase (msg 53, then sub_140106960 moves it); needs **`Zone_CanSwitchControl`** (0x140021100).
- **`Slot0_SetTargetsFaceDown`** (0x14049BE90, Book of Moon 5432) runs **`Change_ToFaceDownDefense`** (0x140085F60: can be set, not immune, the flip-down functions) for each of the record's word-16 targets: **`Record_AddSelectedTarget`** (0x14008BA20) calls **`Record_StoreTarget`** (0x14008B830), which stores the packed target at word 17 + n and increments word 16, so after the picks word 14 = word 16 = the number of targets (`Emit_ResultEvent` loops word 16 the same way).
- **Results on targets.** `Slot0_EmitResultParams_ByCardId` (0x1401748C0, 392 rows: Rush Recklessly, Forbidden Lance, Breakthrough Skill, Effect Veiler) picks a (key, value) per id and calls **`Emit_ResultEvent`** (0x14016BAF0: effect, key, lifetime << 4 | 2, value, ignore immunity), which puts a zone marker {key, kind 2 = until the end of the turn, int value} on each of the record's word-16 targets that is still valid and not immune (msg 53). Most keys are the card's own id (read by id ladders), but some are generic: **`Get_NegateResultKey_ById`** (0x140139FA0) turns 54 ids (Effect Veiler, Breakthrough Skill, Infinite Impermanence) into key **1027** = effects negated until the end of the turn (27 more into 1028, which `Duel_IsCardExemptFromLocks` - really "is negated" - also tests), and Cyberse Accelerator / Decode Destruction (via **`Effect_AddZoneMarkerUnlessImmune`** 0x140083A10) use key **1040** = ATK + value. `Get_StatMarkerMode` (0x14002F2B0, table `StatMarkerModeTable_379` 0x140ACDA10) maps other stat keys (1036-1039, 1047-1050: ATK / DEF become / Level).
- Slot 4 wrappers are all `Slot4_ActivationPrompt_Driver` with a prompt text (`Text357` Stop Defense, `Text282` Book of Moon, `Text119` Mind Control, `Text281` Remove Trap).
- **`Filter_CardAtZoneMatchesFlags`** bits (decoded): 0x80 Attack Position (zone byte +2 == 0), 0x100 Defense Position, 0x4 face-up, 0x20000 control can switch, 0x40000 not this card, **0x80000 battle position can change** (`Zone_CanChangeBattlePosition` 0x14001FD00, not a Link Monster), 0x1000000 has Xyz material, 0x2000000 has a Level. Stop Defense's row 0x80512, Remove Trap's 0x1434.

**Yu-Gi-Oh-Effects.** `"targetDo": "position" | "faceDown" | "control" | "atk" | "negate"` with `"rawRow": true` and a target "filter": slot 0 = Slot0_ChangeTargetsBattlePosition / Slot0_SetTargetsFaceDown / Slot0_TakeControlOfTarget, all run with the card's own id, or the plugin's **`Slot0_OwnTargetResult`** = Emit_ResultEvent with key 1040 and `"amount"` (negative = loses; the value is an int, the game's own cards pass negatives the same way, but a loss is not yet seen in a duel) / key 1027; slots 1 / 4 = Target_Generic_FromFilterRow / Slot4_ActivationPrompt_Text281 (as a target move). The filter takes `"position": "attack" | "defense"`, `"canChangePosition"`, `"canChangeControl"` (the bits above). Effects 1.3.7.

**Language / compiler / blocks / translator.** `change_position(target[, side][, selector][, attack | defense])`, `set_face_down(target, ...)`, `take_control(target, ...)`, `negate_effects(target, ...)`, `change_atk(target, ..., [-]N)` (monsters only); the compiler adds canChangePosition / canChangeControl, and face-up for change_position without a position, change_atk and negate_effects. The old `gain_atk` (Rush Recklessly's donor) stays for old scripts; the translator now writes `change_atk` with the exact selector ("it gains / loses N ATK (until the end of this turn)"; "/DEF" noted) and `negate_effects` ("negate its effects (until the end of this turn)"), and drops "(even if this card leaves the field)" (the marker stays on the target). Like a target move, one may come first in a `then` chain (`targetSlots`). BAB: three action blocks (side, which monsters, position). Translator (`TargetVerb`): "target 1 X (you control / your opponent controls / on the field); change it to (face-up) Defense / Attack Position" (the target is one in the other position), "change its battle position", "change it to face-down Defense Position", "take control of it until the End Phase / end of this turn"; "Attack Position monster" in the selector becomes the position test. Followed by ", but ..." riders they are noted, not run. Also: `if controls(...)` with any target action is refused by the translator (one filter row).

**Tests.** Native `TestTargetVerbs` (machine per verb, result keys / amount, targeting slots, filter bits, refusals). Test-Compiler: 8 cases + 4 refusals; blocks round trip (signed number field). Coverage: fully read 161 (from 153), 1311 partial, all partial scripts compile. Not tested in a duel.

**Open (target verbs still unread).** DEF changes (key not identified), "ATK becomes" (1047 family), "it cannot be destroyed ...", "increase / reduce its Level" (1039 / 1050 family), "equip this card to it", "attach ... to it as material", "take control of it" (permanent), two-target clauses ("1 X you control and 1 card your opponent controls"), GY / banished targets with Special Summon / Set follow-ups, "target 1 of those monsters" (the event's card).

## 93. Phase windows and Battle Phase triggers (2026-10-26)

**Why.** "During the Main Phase / your opponent's Main Phase / the Main or Battle Phase / your opponent's turn, (if ...)" and "At the start / end of the Battle Phase" were the biggest head family among partly read cards (2026-10-26 ranking: 113 cards touched).

**IDA (named, typed, commented, IDB saved).**
- **Phases.** PlayerState +0x3788: 2 / 4 = Main Phase 1 / 2 (the game's own "(phase - 2) & ~2 == 0" Main Phase test, Effect_IsTargetImmune / Duel__Phase__EnterMainPhase), 3 = Battle, 5 = End; +0x3774 the turn player.
- **Start of the Battle Phase.** `Battle_RunPhaseStep` (0x14021FA70) runs `BattlePhase_StepTable` (0x140B2B1E0) by PlayerState +0x37CA; step 0 = **`Battle_StartOfBattlePhaseStep`** (0x14020DBE0, turn player). In sub-state 2 it offers the turn player's face-up monsters with zone bit 43 clear through `Offer_EffectOfCardAtZone(player, zone, 4, 0)` - but only one id (11466) - sets bit 43 and returns 0 (re-run); bit 43 is cleared when the phase changes. (Nurse / Parlor Dragonmaid's "At the start of the Battle Phase" are table-3 Quick Effects, not this offer.)
- **End of the Battle Phase.** `Phase_EndOfBattlePhase_RunHandlers` (0x14021F7A0, step 10) runs table 0x140B2B2C0 (101 rows, four passes, the Standby / End Phase contract of section 60): **`PhaseHandler_EndBattle_Each`** (0x14021D140, 68 cards: player = turn ^ parity, so either player's Battle Phase) and **`PhaseHandler_EndBattle_TurnPlayer`** (0x14021D050), both event 24 + bit 43.
- **"If this card battled".** Zone marker key **1024** (`Cond_GladiatorBeast_BattledEndOfBattle` 0x14023BF30 tests `Has_FlagMarkerKey(player, zone, 1024)` at the end of the Battle Phase).

**Yu-Gi-Oh-Effects.** `Check_Phase<phases, whose>` (was Check_BattlePhase) answers the requirements `battlePhase` / `yourBattlePhase` / `opponentBattlePhase` and the new `mainPhase`, `yourMainPhase`, `opponentMainPhase`, `mainOrBattlePhase`, `yourMainOrBattlePhase`, `opponentMainOrBattlePhase`, `yourTurn`, `opponentTurn`; `battledThisTurn` = `Check_BattledThisTurn` (marker 1024 on this card's zone). Own triggers `battle_phase_start` / `your_battle_phase_start` / `opponent_battle_phase_start` (event 4: **`Hook_BattlePhaseStart`** offers them in sub-state 2 before the game's pass, turn player's side first, bit 43 set on an offer and 0 returned) and `battle_phase_end` / `your_battle_phase_end` (event 24: a virtual row after the end-of-Battle-Phase table's last row, Extinction on Schedule 12032, calling the game's handler family). Effects 1.3.8.

**Language / compiler / blocks / translator.** `if` takes several conditions joined with `and` (`if opponent_main_phase and no_monsters: ...`); new conditions `main_phase`, `your_main_phase`, `opponent_main_phase`, `main_or_battle_phase` (+ your_ / opponent_), `your_turn`, `opponent_turn`, `battled`; triggers `on battle_phase_start:` (+ your_ / opponent_), `on battle_phase_end:`, `on your_battle_phase_end:` (own triggers). BAB: "it is [whose] [Main Phase | Main or Battle Phase | Battle Phase | turn]", "this card battled this turn", "[a] and [b]" condition blocks; the hats list the five Battle Phase triggers. Translator: "During (the / your / your opponent's) Main Phase / Main or Battle Phase / Battle Phase / turn(, you can | , if <condition>)" -> the phase condition and the inner condition with `and`; a monster's window outside your own Main Phase must be a Quick Effect (else refused), a wider window on an ignition effect is noted; "At the start / end of the (your / your opponent's) Battle Phase(, if this card battled (an opponent's monster))" for monsters.

**Tests.** Native `TestBattlePhaseTriggers` (events 4 / 24, the handler families, Check_Phase over phases and turns). Test-Compiler: 6 cases; blocks round trip. Coverage: fully read 166 (from 161), 1322 partial (cards moving in from "not read" raise it), all partial scripts compile. Not tested in a duel.

**Open (phases).** "At the start of your Main Phase 1" (7), "At the end of the Damage Step, if this card battled" (10, no evaluator), "During the Standby Phase of the next turn after this card was banished", turn-history conditions ("if this card was activated / Normal or Special Summoned this turn", "if X was sent to your GY this turn"), "if X is in a Field Zone", "if this card is a Continuous Trap".

## 94. Extra Deck summons: Fusion reading, banish-the-materials Fusion, alternative Xyz Summon (2026-10-26)

**Fusion reading.** The translator required "as material" after "using monsters from your hand or field"; most texts end without it. Fixed (7 more cards finished).

**Fusion by banishing the materials (IDA).** Every fusion Spell's row is `Slot0_FusionSummon_StateMachine` (0x14000A710) + `Slot2_Fusion_CanSummon` (0x14000A650 -> `Fusion_CanActivateCommon` 0x14000A550 -> `Fusion_GetSummonModeCode` 0x140008E40, the summon-type code). The machine picks the material route by the card's id (hand / field, field / GY with `Effect_BanishGraveCard`, Deck shuffles with `Move_GraveCardToDeck` / `Hand_ReturnCardToDeck`): Polymerization 4837 = hand or field, **Dragon's Mirror 6433 = banish the materials from your field or GY** (Miracle Fusion 6432 / Overload Fusion 6783 the same route with their own lists). As plain Fusion composes on Polymerization's row with its own list (section 64), `fusion_summon(selector, banish_materials)` composes on Dragon's Mirror's (compiler `FusionBanishSource`). Translator: "Fusion Summon 1 (X) Fusion Monster from your Extra Deck, by banishing (the) Fusion Materials listed / mentioned on it (its materials) from your field (and/or | or) GY" (only the GY / only the field: noted). BAB: the Fusion block's material choice. Still a donor row (the id-keyed route), like plain Fusion.

**Alternative Xyz Summon (IDA, named, saved).** **`Xyz_CanUseAsAltMaterial`** (0x14003FA70: player, Xyz id, side, zone) is the one test behind "You can also Xyz Summon this card by using 1 X you control": a per-id ladder over the monster (Rank from `Get_ZoneRank` 0x14003B2C0, archetype, name, Xyz kind, materials, once-per-turn marker 1001 for a few), then `Xyz_CanSummonWithMaterial` (0x14003ED60) and the free-zone check; an id it does not list gets no. Its callers are the material picker (`Xyz_MaterialPick_ZoneFilter` 0x140040C20: the first pick may be that one monster), the summon lists and the AI. **Yu-Gi-Oh-Effects Lock.cpp** `Hook_XyzAlt`: a custom card's `"xyzAlso": {"kind", "race", "attribute", "archetype", "name", "xyz", "rankMin", "rankMax"}` answers it (the count test's field match + Xyz kind + Rank range), then the game's two checks. Language: `xyz_also(selector[, xyz_monster][, rank N | rank_at_least N | rank_at_most N]);` (a card-level statement like only_one). BAB: "You can also Xyz Summon this card by using 1 [cards] you control [Xyz Monster] Rank [from N to M]". Translator: "(Once per turn,) you can also Xyz Summon this card by using 1 / a [Rank N (or lower | higher | or N+1)] X (Xyz) Monster you control (as material) (transfer its materials ...)". Not counted: the once per turn (noted), costs ("by discarding 1 Spell and using ..."), conditions ("if an Xyz Monster battled this turn"). Effects 1.3.8.

**Tests.** Test-Compiler: 6 cases + 2 refusals; blocks round trip (`ys_xyz_also` counts as a card-level block). Coverage: fully read 174 (from 166). Not tested in a duel.

**Open (Extra Deck).** Ritual Summon actions ("Ritual Summon 1 X Ritual Monster from your hand, by Tributing ...": `Ritual_CanSummonMonsterWithSpell` 0x1400692E0 accepts a spell by table row or by hard-coded id, materials per id), "Immediately after this effect resolves, Synchro / Xyz / Link Summon ...", Fusions that shuffle the materials into the Deck or use the Deck / GY, "(This is treated as an Xyz / Synchro Summon.)" Special Summons.

## 95. Up to N cards from the GY / banished; GY-target reading fixes (2026-10-26)

**Yu-Gi-Oh-Effects.** `"ownCount": N` on an own pick-and-act step (`Slot0_OwnPickAndAct`): after a pick it goes round again (step 128, the list collected anew so the moved card is gone) until N cards were picked or none is left ("up to N"); not for summon / destroy. `g_OwnPicked` counts per record (id | instance). Effects 1.3.9.

**Language / compiler / blocks / translator.** `add_to_hand(grave | banished, N, selector)` (N > 1 = the own step with the GY / banished movers instead of the salvage donor) and `move(grave, N, selector, to_deck | ...)`. BAB: a count on the add-to-hand block and on the move block for hand / GY moves. Translator: "target (up to) N X in your GY / of your banished X; add them to your hand / shuffle them into the Deck / place them on the bottom of the Deck" (picked when it resolves, "up to" noted). Fixed: "target 1 of your X that is banished or in your GY; Special Summon it / add it to your hand" was taken by the GY-only reading and failed on its selector.

**Tests.** Test-Compiler: 3 cases + 1 refusal. Coverage: fully read 183, 118 attached whole. Not tested in a duel.

## 96. Own ignition row: field ignitions without a donor (2026-10-28, issue #39)

**Bug.** A composed `on ignition:` effect borrowed the row of the first monster `trigger_sources.json` listed for any ignition action: Caam, Serenity of Gusto 9168 ("shuffle 2 "Gusto" monsters from your Graveyard into the Main Deck; draw 1 card"). `WrappedRow` keeps the donor's slot 3 on a composed effect, and Caam's slot 3 (0x140206C90) is that shuffle cost, so every such ignition opened an empty "Select a card to be returned to the owner's Deck" list. `build_effect_reference.py` had read the unknown slot 3 as "no cost" (`cost_of` returned '' for any slot 3 not in `COST_SLOTS`, and `source_ok` tested `not cost`, so an unread cost passed too).

**IDA.** `YGO::Effects::Check_CanActivateKind3AtZone` (0x140107930), the field-ignition gate: for a card on the field (zone <= 12) it sets record word4 = 3 and needs the zone's +3 byte (or the 2-id list `sub_1407440E0`), `Has_Kind3EffectEntry` (0x1400DFD70: a plain `Get_EffectTableEntryForCard` with word2 = the zone, word4 3, no event), with word5 set the 6-id list `sub_140744140`, the face-up / disabled bits and marker 8475, then `Check_CanChainAtSpeed_ByEffectId` and `Offer_GateCanOfferEffect`. No id list decides it: a row in the lookup is what offers the card. Rows (table 3 body 0x140B39B20): Aquaactress Tetra 11916 = {Extra 1, slot 0 0x14016D5A0, -, slot 2 0x1400FB620, -, -}: a plain no-cost ignition row has an empty slot 3 (no address-compared wrapper); Caam 9168 = {Extra 1, 0x14015FBC0, -, 0x1400FE620, 0x140206C90, -}.

**Yu-Gi-Oh-Effects.** `OwnRowFits`: an own `ignition` answers table 3 only (word4 3, no known event). The own row plus the composed action's slots 0 / 1 / 2 / 4 is Tetra's shape. Nothing else changes: `Hook_GetEntry`, `Candidates`, the effect menu and the select row already go through `OwnRowAnswers`, and `PhaseFamilyFor("ignition")` is 0 (no virtual phase row). Effects 1.3.10.

**Compiler.** `on ignition:` is always `ownTrigger` (`OwnTriggerKind`; never "direct", since a game card with the same action could still bring its own cost). `"from"` stays the action's card (e.g. Pot of Greed 4844, Warrior Elimination 4659, Red Medicine 4345). Ignition costs (`cost detach / tribute_self / ...:`) are unchanged: they keep the cost card's row. `build_effect_reference.py`: an unknown slot 3 is a cost (`None`), never a clean source; the monster-effect wrappers WrappedRow compares by address are not costs. trigger_sources.json is not regenerated yet (the script's inputs were not on the machine).

**Tests.** Test-Compiler: `on ignition: draw(1)` is own with "from" 4844, and the issue's three `or` ignitions are all own with no 9168; the zones summon now checks own ignition on Unexpected Dai 11740. EffectRouting `TestOwnIgnitionRow`: the table-3 lookup (word2 = zone) gets the own row with no slot 3, it resolves as its action card, tables 1 / 2 and a known event get nothing, and three `or` ignitions are one select row. Not tested in a duel.

**Still open from #39.** "All three activate at once" was not reproduced: the log shows one chosen effect per activation; waiting on the reporter's console.log.

## 97. The chosen effect travels in the record; WolfX compile notes (2026-10-28, issue #38)

**Bug.** A Spell with two effects (`as("Pot of Greed"); also as("Graceful Charity");`, or `burn(500); also draw(1);`) showed the select prompt, but picking effect 2 resolved effect 1. The select row (section 45) kept the choice only in `g_Choices` keyed by (card, word11 CardInstance). If the record that resolves does not carry the activation's instance, the key misses, `Hook_GetEntry` hands out the select row again and `RouteByRow` falls back to the first part. A second fault: `Hook_GetEntry` asked `ChosenIndex` after `NativeChoiceScope` had hidden word20, so a record tagged by the effect menu (section 58) also got the select row.

**IDA.** The game's own chooser, `Slot3_EffectChoice_SelectThenCost` (0x1401FCB00), stores the answer IN the record: word19 (word20 / 21 for 14940 / 15132 / 14755) = `field_3802` + 1, and queues MsgQueue code 8 (2, card, answer). Stage counter `stru_14349B580 + 754` (int index): 0 = mask + `EffectChoice_ShowPrompt` (or auto-pick for the listed ids), 1 = store answer, 2+ = the chosen effect's cost from `EffectChoice_CostTable` (0x140B223D0) with the stage lowered by 2 during the call. `Check_Activation_Cost_And_Register` (0x1400E0DF0) runs slot 3 and registers the use limit when it returns 1. Graceful Charity 4812 = SpellTrap row {slot 0 `Slot0_DrawThenDiscard_StateMachine` 0x1404BED60, slot 2 0x1400FAC40}: a step machine (0x80 draw `Get_NumberOfCardsToDraw`, 0x7F discard drawn - 1, 0x7C-0x7E return-to-Deck variants by id), so it runs fine as a part once routed.

**Yu-Gi-Oh-Effects.** `ChoiceSlot3` writes the choice into word20 as the menu does (`ChoiceTag`: 0x8000 | index << 8, low byte kept). `ChosenIndex` reads that tag first (after a probe). `Hook_GetEntry` takes `ChosenIndex` before the source lookup hides the tag. `ForgetChoices` drops every remembered choice of the card when an effect resolves, since the activation's key need not be the resolving one. The log now says `Card N activates its effect K (source S), instance I` and `Card N resolved its effect with source S, instance I, tag T`: if the two instances differ, that was the cause. Effects 1.3.11. Test: EffectRouting "a chosen effect survives into a copied record with another card instance".

**WolfX (EffectNotes.cs).** Compile on the Effects page notes, in orange, what the compiler can't know without the card: a monster trigger (summoned, flip, battle) on a Spell / Trap never runs; several Activate effects on a Spell / Trap are a choice (to do them all, put the actions in one effect with `then`); an `as(...)` of a Field / Continuous / Equip card works only as the card's only effect, on a card of that kind and icon (CARD_Prop icon 2 / 3 / 4). Savage Colosseum 8482 is a Field Spell whose effect is id-keyed and lasting, and An Unfortunate Report 7329 is a Trap: with several parts the card keeps its own id, so the Field Spell part has nothing to run.

**Not changed.** Effects joined with `also` on a Spell / Trap stay a choice; "both" is one effect (`burn(500) then draw(1)`), which already works.

## 98. Search, salvage, Special Summon and send without a donor (2026-10-28)

**Why.** The user's goal: every new card's effect runs the engine's own functions under the card's own id, as a real card would, not a game card's row with the id swapped. A tally of the compiled Cards mod (scratchpad `donortally.ps1` + `donors2.py`: every compiled effect step, donor-free = `rawRow`, or an own trigger with raw slots) gave 399 of 1,703 steps donor-free; the biggest donors were Unexpected Dai 11740 / Magnet Circle LV2 6572 (Special Summon from a zone), Foolish Burial 5236 (send / banish from a zone), Reinforcement of the Army 5328 / The Warrior Returning Alive 5330 (search / salvage).

**IDA.** `Slot0_AddFromDeckToHand_StateMachine` (0x14016D5A0) for an id it does not list: step 128 needs a non-empty Deck and **`Can_PlayerAddFromDeckToHand`** (0x14005CCD0, named: Thunder King Rai-Oh, Deck Lockdown, Mistake, Super Quantal Mech King Great Magnus, Amorphage Sloth, Thunder Dragon Colossus, markers 9279 / 11849 / 13335 / 13547), collects, prompt 0x33, 126 `UI_BeginCardListSelect` mode 6, 125 `Resolution_BeginScope` + `Card_MoveListCardToHand` (list index at 0x14349C5C8, list 0x14349C5D8), 100 message 86. Its condition `Slot2_Condition_ListHasMatch` (0x1400FB620) is the same rule + one match. The plugin's `Slot0_OwnPickAndAct` already was that machine (plus the GY / banished movers); only the Deck rule was missing.

**Yu-Gi-Oh-Effects.** `DeckRowClosed`: an own to-hand step skips a Deck-scan row (`kDeckScanFn`) while `Can_PlayerAddFromDeckToHand` refuses, in `RunDeckScan` (each "or" zone) and in the single-row `Hook_Collect` / `Hook_Count`, so the activation check (`listHasMatch`, counted through `Hook_Count`) follows too. Under Rai-Oh, "from your Deck or GY" still offers the GY.

**Language / compiler.** `search(deck [or grave or banished][, N], selector)` (grammar `searchZones`): a raw row, `"own": "toHand"`, one alternative per zone (`ZonesFilter` with the plain scans), `ownCount` for "up to N". `add_to_hand(grave | banished, ...)`, `special_summon(<zones>)` (every zone, one or many) and `send_to_grave` / `banish(deck | hand ...)` are raw rows too (no Reinforcement / Warrior Returning Alive / Magnet Circle / Unexpected Dai / Foolish Burial). `revive(...)` keeps Monster Reborn: it targets at activation, the own step picks at resolution. Translator: "add (up to) N X from your Deck / GY / banishment [or ...] to your hand", "add 1 of your X that is banished or in your Deck / GY ...", and a non-targeting "Special Summon 1 X from your GY" is `special_summon(grave, ...)`. BAB: the search block has Deck / GY / banished boxes and "up to N".

**Tests.** Test-Compiler: search single / multi-zone / up to N / with a trigger / after a draw, salvage raw, every special_summon zone raw. Coverage (Cards mod): effects read 1571 (from 1517), fully read 187 (from 183). Not run in a duel.

## 99. Own "this card is Normal / Special Summoned" triggers (2026-10-28)

**IDA.** `Eval_EventTriggersCardEffect_ByCardId` (0x1400E26A0, the hooked summon evaluator): an entry gate - the event zone's status byte (DuelState + 0x53 + side * 0xD94 + zone * 24) must be set - then id lists (`EventEval_IdList_Event7_A`, `_Event7or9`, `_1`, ...) and per-id branches set "this card has a trigger for this event" (esi); everything meets at 0x1400E3904, and the generic offer (0x1400E3951 - 0x1400E3986) is `Offer_EffectByCardRef(ref, slot, param)` with ref = id | ((((record+2 player) << 6 | event & 0x3F) << 9 | zone & 0x1F | 0x20 (table 1)) << 16), slot = param & 0x1FF, param = `Get_SummonEventParam()` (0x1400E19B0); then message 0x63 for esi == 2. Events: 7 Normal Summon, 8 Flip Summon, 9 Special Summon. Nothing but the lists decides, which is why a custom card needed a donor's id.

**Yu-Gi-Oh-Effects.** `OfferOwnSummonTriggers` (called first in `Hook_EventEvaluator`): for each own part with a summon trigger, the zone gate, `OwnSummonFits` (normal_summoned 7, special_summoned 9, normal_or_special_summoned 7 / 9, summoned 7-9: the split the lists made) and the part's `summonCondition` (`SummonMatches`), then the offer above with `g_MenuPart`. A card whose parts are all own is never run through the game's branches; an own part is never evaluated as its action card (also fixed for other own triggers in a multi-effect card). `OwnRowFits` answers table 1 for those events with the same split. `g_SummonEventParam` is a pointer for the tests.

**Compiler.** `normal_summoned`, `special_summoned`, `summoned`, `normal_or_special_summoned` are `OwnTriggerKind`, and never "direct" (a game monster with the same trigger and action is a donor too). FLIP stays on a donor: the position-change and battle paths (events 12 / 13 / 21) offer it too.

**Tests.** EffectRouting `TestOwnSummonTriggers`: ref / slot / param exactly as the evaluator's, the zone gate, Normal vs Special, a summonCondition, no run of the game's branches, the own row's lookups. Test-Compiler: own summon triggers with donor and raw actions. Donor-free steps 915 of 1703 (from 617). Not run in a duel.

## 100. "Special Summon this card from your hand" without Watch Cat (2026-10-28)

**IDA.** Watch Cat 13582's hand row (table 2) = {Extra 5, slot 0 0x140161E20, slot 2 0x1400FE620}. **`YGO::Effects::Slot0_SpecialSummonSelfFromHand`** (0x140161E20) for an id it does not list: step 128 needs flag marker (player, 13, **4064**, card instance) and the card in the hand (zone 13) - 4064 is engine state set while a hand card's effect is activated and removed by Msg 0x12 (`Duel__Msg__Handle_12`), not id-keyed - then `sub_140054EA0` / `sub_14005A570` (summon rules), a free Monster Zone (none: the card is sent to the GY) and `Chain_AddEffectLink(player, the card's slot, mode 1, count 2, id << 16, 0)`, returning 127; the next call (step 128) returns 0. Slot 2 `Slot_Delegate_ToSecondaryTable_FE560` is a per-id secondary-row condition (not usable without the donor). `Cond_CanSpecialSummonSelf` (0x1400FB9A0, the "canSummonSelf" require) for an unknown id ends in `Cond_GenericCanActivate_Ladder` (0x1400F8690), whose unknown-id tail is one free Monster Zone + the player's summon rule - the same branch Watch Cat's id took.

**Yu-Gi-Oh-Effects / compiler.** `"selfDo": "summonFromHand"` = SlotOverride[0] 0x140161E20. `special_summon(self)` compiles to a raw row + own `in_hand` trigger (table 2, offered by the hand-effect id test, section 66) + requires inHand / canSummonSelf + selfDo. Tests: Test-Compiler (shape, with phase conditions and quick). Donor-free steps 1030 of 1703 (60%; 23% at the start of the day). Not run in a duel.

**Donors left (2026-10-28 tally, steps):** Monster Reborn 4842 (revive, targets) 79 + 12, Rose Lover 9409 (banish-self cost row) 74 + 31, Polymerization 4837 61, Altergeist Marionetter 13228 (set) 58, Remove Trap 4838 (target destroy) 51 + 21, Planet Pathfinder 10232 (Tribute-self row) 46, Hecatrice 7572 (discard-self row) 41, Pot of Greed 4844 (draw row) 34 + 29, Trap Jammer 5921 (negate) 30, Thunder End Dragon 9762 (detach row) 27, Quillbolt Hedgehog 7701 (summon self from the GY) 24, Spiritualism 5246 (return) 16, Legendary Knight Hermos 11887 (banish target) 12.

## 101. revive(...) without Monster Reborn (2026-10-30)

**IDA.** Monster Reborn 4842's row (table 0) = {Extra 0, slot 0 `Slot0_SpecialSummonTarget_Driver` 0x140169590, -, slot 2 `Slot2_Condition_Generic_And_FA680` 0x1400F9940, -, slot 4 `Slot4_SelectFromList_Driver` 0x140092910}. 4842 is not in any of their ladders, so an id they do not list takes exactly Monster Reborn's path: slot 4 collects the list (`Deck_CollectMatchingCards` with `Get_DeckFilterParamForEffect_Ladder`), prompts 94, opens `UI_BeginCardListSelect` mode 6 and stores the pick with `Record_AddSelectedTarget` (a target, chosen at activation); slot 2 = `Cond_GenericCanActivate_Ladder` (unknown-id tail: a free Monster Zone + the summon rule) and `Cond_ListHasEnoughMatches`; slot 0 calls `Action_QueueTargetedZoneEffect(effect, 1, 2, 0)`, which checks the target again (`sub_14005ADC0`), takes the field from `YGO::Effects::Get_EffectTargetPlayer_Ladder` (0x1400F8370, named: unknown id = the activating side) and queues the summon with `Chain_AddEffectLink`. The other 4842 immediates are not on this path: 0x14010C52C (an activation rule for Monster Reborn itself, `sub_140014D50(4859)`), and the "revived by Monster Reborn" marker scans `sub_1401E9350` / `YGO::Effects::Has_MonsterRebornMarker` (0x14071BC50, named). Commented, IDB saved.

**Yu-Gi-Oh-Effects.** `"targetDo": "revive"` = SlotOverride 0 / 2 / 4 as above (an explicit "condition" keeps its slot 2), no slot 1 and no Remove Trap targeting slots; needs a "deck" list, which a raw row serves from the scratch row 5919 (`PatchRawDeckRow`, section 88). Effects 1.3.13.

**Compiler.** `revive(grave | opponent_grave | either_grave, selector)` = `{"rawRow": true, "targetDo": "revive", "deck": ...}` (scans graveSummon / opponentGrave / bothGravesSummon as before), no "from". Tests: EffectRouting target verbs (slots, list needed), Test-Compiler (three shapes, with an own trigger), block round trips regenerated. Not run in a duel.

**In a chain.** A revive after another step (`draw(1) then revive(...)`, a `before` step) picks when it resolves: `MarkOwnMachine` turns it into the own summon step on the same raw row (`"own": "summon"`, condition listHasMatch, no targetDo), as it did with Monster Reborn's row. `MarkOwnMachine` also no longer needs a "from" (a raw step crashed the compiler with a null reference: `cost banish_self: revive(...) then send_to_grave(...)`).

## 102. Self costs and GY effects without a donor (2026-10-30)

**IDA.** `YGO::Effects::Check_CanActivateKind3AtZone` (0x140107930): a card past the field (zone > 12) gets word4 2 and goes straight to `Check_CanChainAtSpeed_ByEffectId` / `Offer_GateCanOfferEffect`; no id list for the GY (the hand has `Id_HasHandEffect`, section 66). The costs and their checks, each generic for an id it does not list (typed `EffectRecord*`, commented):
- `Cost_BanishSelfFromGrave` 0x1401FA670 (Rose Lover 9409): `Grave_FindIndexOfInstance(player, word11)` + `Move_GraveCardToBanished`, no id test. Check `Cond_CanBanishSelfFromGrave` 0x1400FB960 = in the GY + **`YGO::Duel::Can_PlayerBanishFromGrave`** (0x14005C700, named: the banish rule, marker 5248, Kycoo, Necrovalley unless Gravekeeper's Chief).
- `Cost_DiscardSelf` 0x1401FCEB0 (Hecatrice 7572): `Hand_FindIndexOfInstance` + `Hand_DiscardAt`; only 14449 adds a message. Check `Cond_CanDiscardSelfFromHand` 0x1400F7D00 = F.A. Whip Crosser + `sub_14005B9A0(instance, 13)`.
- `Cost_TributeSelf` 0x1401F8BA0 (Planet Pathfinder 10232): **`YGO::Duel::Queue_TributeAtZone`** (0x1400DA940, named: destroy queue, reason 16, record event 39); the archetype-193 rule (banish card 13000 from the GY instead) is a game rule any member gets; id branches 5583 / 6160 / 6797 / 7880 / 9913 only. Check `Cond_CanTributeSelf` 0x1400FAC80 = the same archetype rule + `sub_14001D700`.

**Yu-Gi-Oh-Effects.** `OwnRowFits`: an own `in_grave` answers table 2 (as `in_hand`). `costSlot` gains `banishSelfFromGrave`, `discardSelf`, `tributeSelf`; on an own row slot 3 is exactly that function (`WrappedRow` over the empty own row: raw row = SlotOverride only; a donor action gets the thunk running the named cost). Effects 1.3.13.

**Compiler.** `OwnPlace(compiled, place)`: own trigger + effectOnly + trigger + inGrave / inHand. Every self cost is the own row of its place with its cost named: banish_self -> in_grave + banishSelfFromGrave + canBanishSelfFromGrave; discard_self / send_self / banish_self_hand / tribute_self_hand -> in_hand + canDiscardSelf + their cost; tribute_self / send_self_field / banish_self_field -> ignition (+ canTributeSelf for tribute_self). `banish_self_and(...)` and `on in_grave:` -> own in_grave (Quillbolt Hedgehog's row is no longer used for them). The action is untouched: a raw action stays fully raw, a donor action (draw, Fusion ...) keeps "from" for its slots. Removed BanishSelfSource / DiscardSelfSource / TributeSelfSource.

**Tests.** EffectRouting `TestOwnSelfCosts`: the GY lookup (word2 16, word4 2) gets the own row with slots 0 / 2 / 3, not the field / monster tables; each costSlot's address; an unknown costSlot fails. Test-Compiler: the three costs on raw actions, banish_self on a donor draw, banish_self_and, on in_grave, and the chained revive. Donor-free steps (Cards mod tally) 1290 of 1702 (from 1115 after section 101, 1030 before). Not run in a duel.

**Donors left (2026-10-30 tally, steps):** Polymerization 4837 68, Altergeist Marionetter 13228 (set) 58, Remove Trap 4838 (target destroy) 55 + 4, Pot of Greed 4844 (draw) 44 + 16, Trap Jammer 5921 (negate) 30, Thunder End Dragon 9762 (detach row) 29 + 10, Quillbolt Hedgehog 7701 (special_summon(self_grave)) 24, Spiritualism 5246 (return) 20, Legendary Knight Hermos 11887 (banish target) 12, Warrior Elimination 4659 10, 6433 9, 5413 (row place) 8, Red Medicine 4350 / 4345 8 + 6, 4310 6.

## 103. Draw and the one-target Spells without a donor (2026-10-30)

**Approach (user, 2026-10-30).** The donors are the worklist: read each donor's row in the exe, check every function on it for id branches, then run those functions under the card's own id (a raw row) instead of cloning the donor.

**IDA (named, typed `EffectRecord*`, commented, IDB saved).**
- Pot of Greed 4844 = {slot 0 `Effect_DrawCards_Resolve` 0x14015F9C0, slot 2 `Effect_DrawCards_CanActivate` 0x1400FAC40}. Resolve: `Get_NumberOfCardsToDraw` (the hooked count), the Mystical Refpanel swap, `Draw_ExecuteDraw`; only 13977 is id-keyed. Check: the count + **`YGO::Duel::Can_PlayerDrawCards`** (0x14005CF80, named: Protector of the Sanctuary, marker 6277, Great Magnus, markers 9279 / 12005 / 13547 / 14144, then Deck size).
- Remove Trap 4838 = {**`Slot0_DestroyFirstTarget`** 0x140156D40 (renamed from Slot0_SelectFirstTargetAndApply) -> **`Action_DestroyFirstTarget`** 0x1401562A0 (renamed): `Has_SelectedTarget(0)` + `Check_TargetStillValid` + `Action_DestroyZoneMask(mask, 16, 0x2000002)`, no id test; slot 1 `Target_Generic_FromFilterRow`; slot 4 prompt 281}.
- Spiritualism 5246 = {`Slot0_ReturnTargetToHand` 0x140157350 (the same, reason 13), slot 1 the same, slot 4 **`Slot4_ActivationPrompt_Text348`** 0x14008CCF0 (named)}.
- Legendary Knight Hermos 11887 = {`Slot0_BanishTarget` 0x140156ED0 (reason 17; face-down only for 12076 / 12767, a message for 12435), slot 1 the same, slot 4 **`Slot4_ActivationPrompt_Text293`** 0x14008CD40 (named)}.
- `Slot4_ActivationPrompt_Driver` 0x14008C9D0: auto-pick for 9 listed ids, id texts only when the wrapper passes no text; otherwise prompt a3, the pick through the row's slot 1 (`Pick_ZoneFilter_BySlot2`) and `Record_AddSelectedTarget`. `Check_TargetStillValid` looks the card's own row up again, which a raw row answers.

**Yu-Gi-Oh-Effects.** `"targetDo"`: `destroy` (0x140156D40, prompt 281), `return` (0x140157350, prompt 348), `banish` (0x140156ED0, prompt 293), all with slot 1 `Target_Generic_FromFilterRow` and the clone's "filter". `"do": "draw"` = slot 0 0x14015F9C0, slot 2 0x1400FAC40, needs a non-zero "draw" (`Hook_GetDraw` answers it for the card's own id; `IsImmediate` as before). Effects 1.3.13.

**Compiler.** `draw(N)` = `{"rawRow", "do": "draw", "draw": N}`; draw / gain / damage chains ("steps") sit on a raw row (no Pot of Greed row); `destroy(target, ...)` / `return_to_hand(target, ...)` / `banish(target, ...)` = raw row + targetDo destroy / return / banish. Removed TargetDestroySource / ReturnSource / BanishTargetSource; DrawSource stays only as the Spell row of `set(deck, ...)` (Altergeist Marionetter's machine, next).

**Tests.** EffectRouting target verbs (the three rows' slots and prompts, do draw + count, no count fails). Test-Compiler: source-order cases list a raw part by its verb ("draw"); the three target verbs and a step chain raw; the draw checks of sections 96-102 now expect raw. Block round trips regenerated. Donor-free steps 1421 of 1703 (83%; 1290 after section 102). Not run in a duel.

**Donors left (2026-10-30 tally, steps):** Polymerization 4837 68, Altergeist Marionetter 13228 (set, on Pot of Greed's Spell row) 58 + 16, Thunder End Dragon 9762 (detach row) 37, Trap Jammer 5921 (negate) 30, Quillbolt Hedgehog 7701 (special_summon(self_grave)) 24, 5413 (row place) 10, Warrior Elimination 4659 (destroy all) 10, Dragon's Mirror 6433 9, Hinotama 4350 8, Axe of Despair 4310 6, Red Medicine 4345 6.
