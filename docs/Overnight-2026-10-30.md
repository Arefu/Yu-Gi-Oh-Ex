# Overnight work 2026-10-30 / 31

Read this first. Everything here was built and unit-tested where a test exists; **nothing was run in the game** (the game was not launched).
Nothing is committed.

## 1. BOB / BAB can use ANY card, the specially handled ones included (done, built, tests pass)

**Decision: lending, not a port of the donor machinery.** Konami's per-card behaviour lives in branches keyed on the card's id (hundreds of
id lists and ladders in duel.dll as in the game). The only way to get every one of them right for a custom card is for the custom card to
*be* that id inside the engine. The game path already did this for vanilla sources ("source-id lending", docs/EffectSystem.md section 32);
under duel.dll it was lost (the clone kept its own engine id, which has no rows). Now:

- **Core 1.3.6** - new API `Core_EngineLend(displayId, engineId, propsGameId)` (Yu-Gi-Oh-Core.h): for this duel, duel.dll's card record for
  `engineId` gets the custom card's props (stats, kind, level, attribute, type) and the screen maps it to the custom card. duel.dll reads the
  record table in place (`DLL_SetCardProperty` keeps Core's pointer - checked in IDA), so the change is live at once; `ClearDisplayIds` (the
  next duel's start) puts the records back; a card refresh drops them. Logged: "card N (shown as D) plays as engine card E this duel".
- **MoreCards 1.3.4** - `ExtraCard::EngineSource`: a single-effect clone's source, **any** id (not only the game's 3900-14968). Under duel.dll
  (`Core_EngineProc("DLL_DuelSysAct")`) `ResolveDuelSessionId` shows the clone as before (its vanilla source when free, else a borrowed id)
  and lends it the source's engine id - unless a real copy of the source is in either deck (`g_EngineIdsInUseThisDuel`, filled by the deck
  pre-scan) or another clone took it this duel; then it plays with no effect and the log says why.
- **WolfX** - the Effect library lists **13,571 cards** (was 1,176): every card duel.dll has a record for (the game's cards + the Cards mod's
  cards with a Konami id), minus Normal Monsters / Tokens / Skill Cards, as `as(<Konami id>);` with a note. The 1,176 composed entries are kept
  as they were. New tree root "Every card (Master Duel engine)", by kind then first letter; they are kept out of "Script action" (they would
  all be "as (clone)"); a filter only auto-opens categories with <= 300 matches. Made by **Tools/Scripts/make_full_effect_library.py**
  (YGOPRODeck cardinfo.json + the Cards mod's cards.json); re-run it after build_effect_reference.py.
- **as("name")** also finds the 4,281 post-game cards (new **engine_card_names.json**, shipped next to WolfX); card_names.json is untouched
  (the Card Manager / Summoning / Fusion readers use it to get *game* ids). The Effects page's "Game cards" ignores the new entries.
- Tests: Test-Compiler 339 / 339 (new case `as(15245)` = Fallen of Albaz), EffectRouting all pass.

**Not verified (needs the game):** a clone of a post-game card actually playing as it in a duel (deck with a card whose script is e.g.
`as("Fallen of Albaz");` and no real Albaz). Expected log lines: MoreCards "Duel: card N ..." / Core "card N (shown as D) plays as engine
card 15245 this duel".

**Notes.** A clone keeps the source's archetype / same-name / genre membership in the engine (it *is* the source there). Two clones of the
same source in one duel: the second has no effect (one record per id). The old `Tools\WolfX\Binaries` folder (a build without SolutionDir;
84 files from before tonight) was left alone. Backups of the two WolfX data files before the change: scratchpad `backup-2026-10-30\wolfx`.

## 2. Card-name declaration under duel.dll (done, built; needs the game)

**What was wrong.** duel.dll asks for a name with dialog kind 160-170 (= 20 + its list kind 0x8C-0x96; Crossout Designator = 0x91, "a card in
your Main Deck") and expects its own client to show a card search and answer with `DLL_DuelDlgSetResult(card id)`. The game has no such
dialog, so nothing answered; duel.dll's wait (`DuelDlg_WaitDialog` 0x1805932C0, named) saw no busy dialog, checked the empty answer, showed
message 145 and asked again, many times a second.

**What Core's bridge does now (MasterDuelBridge.cpp, "card-name declaration").** After a step, if duel.dll's dialog kind is 160-170 it opens
the game's own list picker the way the game does for a human player (`UI_BeginCardListSelect` 0x14005DE00: list block side / mode 6 /
entries, 0x3800 = 25, 0x3804 = player, animation 0x38) with the names the player may legally declare: the duel's cards first, then every other
card the engine has, up to the list's 300. Legality is duel.dll's own check, `DuelDlg_IsDeclarableCardName` (0x180598AF0, named and
commented in duel.dll.i64); it is not exported, so Core calls it only when its first 56 bytes are those of the traced build (else: the
duel's cards, unchecked, and duel.dll's own check after the answer still applies). While the picker is open the bridge answers busy to
duel.dll (it waits), keeps the game's list block / dialog fields its own (the list getters read the game's block), takes the picked entry from
ListSetIndex and the close from SetPromptAnswer, and sends `DLL_DuelDlgSetResult(engine id)` before the next step.

**Decisions.** "Any card" declarations (Prohibition style) offer the duel's cards first, then the lowest ids; the list holds 300, so a
declaration of a card nowhere in the duel may not be offered (a search page would be the full answer - not built). Cancelling answers "none";
duel.dll then asks again (the picker re-opens). With no legal name at all it answers "none" at once (duel.dll then decides; it may ask again).

**Not verified (needs the game):** the picker opening and the answer accepted (Crossout Designator: log "declare a card name (list kind 0x91):
N legal name(s) offered" then "card name declared: <id>"), and the list showing the names correctly (entries are display ids with no card
instance).

## 3. Fusion recipes under duel.dll (checked: nothing to fix)

duel.dll carries its own recipe tables, the game's format extended: `FUSION_RECIPES_2_3` (0x180F2DB00, **847** rows of {u16 Fusion id, 3 x u16
material code}; the game's has 320) and `FUSION_RECIPES_4_5` (0x180F2F580, 21 rows), read by `Fusion_GetMaterialCount` (0x1804577F0) and two
siblings - all named / commented in duel.dll.i64. Titaniklad 15272 = {15272, 15245 Fallen of Albaz, code 104}. Core gives duel.dll only the
archetype / same-name / link / genre data, and needs to give nothing else for Fusions: every post-game Fusion duel.dll knows has its recipe.

**So why did Albaz fizzle?** Most likely the game rule: Albaz's Fusion may use only the OPPONENT's monsters besides itself, and its effect
finds no Fusion if they have none that fits (Titaniklad: 2500+ ATK; Albion: LIGHT; Mirrorjade: a Fusion / Synchro / Xyz / Link). The log
showed the activation, then two messages (ids 169 / 252) and no material choice. Not verified: what the opponent had on the field. Next test:
summon Albaz when the opponent controls e.g. a 2500+ ATK monster.

## 4. Catalogue of duel.dll's step functions with no game twin (done)

**docs/MasterDuelStepFunctions.md** (made by **Tools/Scripts/md_function_catalogue.py** from duel.dll + md_rows.json): all **521**, each with
the post-game cards that use it (table and slot), its size (exact, from the unwind table) and the card ids its own code compares.
**48 generic** (2+ cards, no card id in its own code), **89 card-specific**, **384 used by one card**. Heuristic: a function's own bytes
only (callees can still branch on ids). One entry (0x180477B50, used by 60 cards in slot 4) has no unwind entry, so its size shows 0 and its
code was not scanned - check it in IDA.

## 5. New BOB words / BAB blocks from the generic ones (first word built, 2026-10-31 tenth pass)

**First word: `if both_extra_decks:`** (BAB "Only if" > "both players have a card in their Extra Deck"; require `"bothExtraDecks"`).
Traced: duel.dll 0x180286840, now `Cond_BothPlayersHaveExtraDeckCard` - no arguments used, no card ids: DuelState +24 and +3572 (the two
players' Extra Deck counts, stride 3548) both non-zero; the slot 2 of White Knight of Dogmatika 15993, Dogmatika Nation 15285, Dogmatika
Lawbringer 22086. On the game's engine the plugin's `Check_BothExtraDecks` reads the same counts (PlayerState +0x18); under duel.dll its
lookup hook (6b) runs duel.dll's own function through `MdSlot2_Requires` / `MdCheckTwin`. Grammar (EffectScript.g4 + regenerated parser),
compiler, BAB block (definition, toolbox, text, script -> blocks), docs/EffectLanguage.md, docs/EffectSystem.md; Test-Compiler cases
`if both_extra_decks: draw(1);` and `if your_turn and both_extra_decks: draw(1);` pass. Under duel.dll the row runs when the rest of the
effect is portable (6b): both examples are (raw draw row + Hook_MdGetDraw; `yourTurn` reads the mirrored turn player, eleventh pass).

Traced, **no word (decisions):**
- `0x180477B50` (60 cards' slot 4) = `Slot4_PickOneTarget_WithPrompt` (0x180477130) with prompt string 210: the game's one-target picker
  family (0x14008CCC0 = 0x180477B20 ...) with a Master Duel prompt id the game's screen does not have. Nothing new to say in BOB.
- `0x1801ED6F0` (slot 3 of Baronne de Fleur 16386, Ogdoabyss 16079, First of the Dragonlords 21447, Charmer Quartet in Bloom 22559, Ars Magna
  "Citrinitas" 23383) = `UseLimit_RegisterZoneMarker` (0x1801ED570) with marker 1001, **lifetime 3** (the game uses 1 = turn and 5 = duel).
  Very likely "once while face-up on the field", but the check side is not traced: those rows' slot 2 (0x18023EAB0) is a per-card composite
  looked up by id. A `once_while_face_up` word waits for that trace.

Older note (before 6b existed): a word that puts a duel.dll function in a custom card's own row needs the Effects plugin to serve own rows
from duel.dll's lookup (step 6), and the plugin's own step machines call game functions that must point at duel.dll's twins too. Meanwhile **every one of the 521 already runs for a custom card through `as(<card>)`**
(step 1: the card plays as its source), which is what the Effect library now offers for all 13,571 cards. Order for the next session: step
6's lookup hook, then the 48 generic functions as words (each traced in IDA first, as the rule says).

## 6. Port of the Effects plugin's own rows / hooks to duel.dll (started: measured and first twins)

Measured (**docs/effects-duel-dll-port.json**, the worklist): the plugin uses 322 code addresses and 47 data addresses of YuGiOh.exe, and
hooks 47 functions. The 2,492-pair row map covers only **66** of the code addresses (mostly slot functions) and **1** hook target: the hook
targets are engine internals no shared row passes through, so each needs tracing in IDA, and the data addresses are game state that under
duel.dll lives in its work memory (Core's mirror copies it one way only, so the plugin's writes would have to go to duel.dll's copy).

Twins traced so far (named / commented in duel.dll.i64):

| game | duel.dll | what |
|---|---|---|
| 0x1400DFBC0 Get_EffectTableEntryForCard | 0x18056C0C0 `Get_EffectTableEntryForCard` | the one effect lookup; same record words, same five tables (`EffectTable_*`) - the hook point for own rows |
| 0x1400063E0 Get_FusionMaterial | 0x180457A90 `Get_FusionMaterial` | recipe codes (tables of 847 + 21 rows) |
| - | 0x1804577F0 `Fusion_GetMaterialCount` | material count |
| - | 0x180598AF0 `DuelDlg_IsDeclarableCardName`, 0x180591B30 `DuelDlg_ShowMessage`, 0x1805932C0 `DuelDlg_WaitDialog` | dialogs (Core's bridge) |
| 0x1400C0290 Deck_CollectMatchingCards | 0x1805460E0 `Deck_CollectMatchingCards` | **extra first arg** (a context pointer, kept at qword_1811F9C58+2712); filter table `DeckFilterTable` 0x180F34B00 has **32-byte** entries (the game's 24) |
| 0x1400C0320 Deck_CountMatchingCards | 0x180546360 `Deck_HasMatchingCards` | same extra arg; returns count >= 1 (a bool), not the count |
| 0x1400C01B0 Get_DeckFilterIndexForEffect | 0x180545F00 | (a1, player, id) |
| 0x14008CE70 Get_DeckFilterParamForEffect_Ladder | 0x180477CF0 | by call position in Slot4_SelectFromList_Driver |
| 0x14015EA90 Get_NumberOfCardsToDraw | 0x1802AB390 | the draw-count hook |
| 0x1401F64E0 Get_UseLimitPerCopyCount | 0x1801E6F40 | by call position only (Timing_Check_Class1 = 0x18022CA70); check line by line |
| 0x140006250 Get_FusionMaterialCount | 0x1804577F0 `Fusion_GetMaterialCount` | |
| 0x140006610 Get_FusionsUsingMaterial | 0x180457EA0 | same (material, int* out) -> count |
| 0x140004B10 Fusion_CardMatchesMaterialCode | 0x180452C40 | same (code, instance index, override); 3x the size (more codes) |
| 0x140006BD0 Fusion_SelectMaterials | 0x180458B10 (probable) | calls the matcher like the game's; not checked line by line |
| 0x1400DB180 Get_ProxyCardId | 0x1800300F0 | **table-driven** (`ProxyCardIdTable` 0x180EC4360, 119 {id, counts-as} pairs; the game's is a ladder) and returns **0** for "none" (the game returns the id): the nameAs hook must keep that. (0x180450950 is a separate instance-based "counts as for materials".) |

| 0x1401FA6D0 EffectChoice_GetCount | 0x1801F4DF0 | same ladder, more ids (124-row list for 3 choices; up to 6) |
| 0x1401FA760 EffectChoice_GetLabel | 0x1801F4F00 | DuelDlg_ShowMessage case 2 calls it as the game's message handler does |

| 0x1400E0FE0 Get_CostSlotOrLimitRegistrar (not hooked, but on the use-limit path) | 0x18056DF60 | returns the cost slot or the class registrar; Master Duel adds use-limit class 15 |
| 0x1400E0E50 Register_UseLimit_Marker | no single twin | Master Duel runs the registrar 0x18056DF60 returns (classes 1-15 at 0x1801ED670..0x1801ED7F0) |

| 0x14005D120 Duel_GetHandLimit | 0x1800DE280 | same card checks; marker 5535 gives 7 (the game: 6) and a Speed Duel branch |
| 0x140014450 (face-up copy activation number) | 0x180060640 `Get_FaceUpCopyActivationNumber` | helper, not hooked |

| 0x1401F7120 Get_SendHandCount_ById | 0x1801E8F90 | confirmed; 449-id list (the game's 137) |
| 0x1401F7450 Get_CostPickCount_ById | 0x1801E9620 (probable) | position + a shared id (8597); not line by line |
| 0x1401F76A0 Get_TributeCount_ById | 0x1801E9A60 (probable) | position only |

| 0x14007CAF0 Synchro_GetTunerCount | 0x18055F110 | same compares (also exported as DLL_FusionHowManyTunerThisSyncNeed) |
| 0x140094490 Get_MultiTargetMaxCount_ById | 0x180486EA0 | first call of the picker in both (Slot4_MultiTargetPicker_StateMachine = 0x180488B20); same opening |

| 0x1400DFE30 Pick_ZoneFilter_BySlot2 | 0x18056C490 | first call of a row-map pair (0x14008CD90 = 0x180477C10) |
| 0x1401B0F40 Get_SecondaryRow | 0x1801E6D30 | first call of Target_Generic_FromFilterRow (0x1401B0FF0 = 0x1801E6DE0) |
| 0x14015AC90 Get_LifePointChanges | 0x1802A4020 | only call of a row-map pair (0x1400FABE0 = 0x1802323C0) |

| 0x1400FC410 Cond_NegateTargetMatches | 0x180236700 | only call of a row-map pair (0x140251370 = 0x1802483B0) |
| 0x1407448C0 Card_GetOnlyOneKind | 0x18015DCC0 | found by the game's g_OnlyOneOnFieldIds table bytes (0x180ECF9A0); same size |
| 0x14077AE30 Duel_PromptText | - (UI side) | the game's screen code, not the engine: stays on the game, no twin needed |

| 0x14003F3C0 Xyz_GetMaterialLevelIfUsable | 0x180572100 | same compare run (11413, 11932 ...); 2x the size |
| 0x14003FA70 Xyz_CanUseAsAltMaterial | 0x180573600 | the next function, as in the game; shares the 11133 compare |

| 0x1405B2500 Link_CardIsValidMaterial | 0x18046E120 | found through its rule table (game 0x140BCA0D0 = duel.dll `LinkMaterialRuleTable` 0x180F2F680). **Material codes are renumbered** (0x49 -> 0x50, 0x4A -> 0x51 ...): the plugin's Link rules need a code translation when ported, and the same may hold for Fusion codes - check before porting Fusion.cpp |

**Material codes, game -> duel.dll (checked 2026-10-31 over the 316 Fusions both recipe tables share; 157 rows identical, 159 differ only
by this):** codes 1-25 (races) unchanged; **26-97 (attributes, Levels, kind letters) +7** (26 LIGHT -> 33, 97 -> 104); **98 and up
(archetype = code - 98) +10** (so duel.dll's archetype codes are code - 108); card ids (3000+) unchanged. Master Duel added 7 codes after the
races and 3 more before the archetypes. The same shift shows in the Link rule table (0x49 -> 0x50). Fusion.cpp / SynchroXyz.cpp / the Link
rules must translate their codes with this when they run under duel.dll: **built** as `YGO::Effects::ToMasterDuelMaterialCode`
(Dependencies/Yu-Gi-Oh-Ex/MasterDuelMaterialCodes.h, compile-time checks + EffectRouting test "game -> Master Duel material codes" over 15
pairs read from both engines; passes). **Caveat recorded in the header:** the plugin's own codes (517-2999) collide with Master Duel's real
archetype codes (527 and up) - that range must move before Fusion.cpp runs under duel.dll. Script: scratchpad comparison, reproducible
with PE() from Tools/Scripts/md_effect_rows.py over FUSION_RECIPES_2_3 in both binaries.

**Found by shared card ids (2026-10-31, tenth pass; `Tools/Scripts/md_twin_by_ids.py <duel.dll> <YuGiOh.exe> <addr>...`).** Function bounds
from both .pdata tables; every 32-bit value 3900-30000 in a function's bytes counts as an id; the duel.dll function sharing the most is the
twin. Named + commented in duel.dll.i64 (ids shared / ids in the game function; next best in brackets). **Unverified beyond the id overlap**
(no side-by-side decompile yet) - the margins are wide, but signatures may differ as they did for the deck filters:

| Game | duel.dll | shared ids |
|---|---|---|
| 0x1400C2CA0 Eval_LeaveFieldTriggers | 0x180122BC0 | 291 / 401 (86) |
| 0x1400E48F0 Eval_SummonReactionTriggers | 0x180622DD0 | 307 / 342 (84) |
| 0x140086C00 Eval_AttackDeclaredTriggers_Ev18 | 0x180116800 | 93 / 106 (28) |
| 0x140074430 Battle_CanDestroyByBattle | 0x1805817D0 | 181 / 202 (52) |
| 0x140141DE0 Check_CanAttackTarget | 0x18001C3F0 | 113 / 129 (22) |
| 0x140144DF0 Attack_GetDirectAttackRule | 0x180022FD0 | 56 / 69 (15) |
| 0x140058440 Summon_CheckPlayerRestrictions | 0x1800D24E0 | 134 / 152 (41) |
| 0x140023AF0 Effect_CanTargetCard | 0x18007E7E0 | 156 / 177 (47) |
| 0x14010EAA0 Duel_BuildContinuousLockState | 0x180226010 | 43 / 48 (24 = 0x180229A70, the earlier wrong lead) |
| 0x1400679F0 EffectCondition_CheckCardUsableAtTiming | 0x1804A0790 (weak, named `_probable`) | 13 / 25 (9) |

(Rerunning the script gives slightly higher counts than the table - its id window is wider than the first inline run - with the same winners.)
Size check (eleventh pass, .pdata bounds): the id-laden twins are 1.3x-3.3x the game's size (duel.dll carries ~3,000 more cards' branches:
Eval_LeaveFieldTriggers 1.51, Eval_SummonReactionTriggers 1.58, Eval_AttackDeclared 1.76, Battle_CanDestroyByBattle 1.29,
Check_CanAttackTarget 1.98, Attack_GetDirectAttackRule 2.14, Summon_CheckPlayerRestrictions 2.93, Effect_CanTargetCard 3.31,
Duel_BuildContinuousLockState 1.97); EffectCondition_CheckCardUsableAtTiming 0.85. Consistent with the pairing; still not a side-by-side
decompile. **Correction (thirteenth pass):** the Calc_EffectiveAtkDef_Modifiers figure (0.79) was wrong - .pdata gave only its first chunk;
IDA's bounds are 0x8A44 (game) / 0x764B (duel.dll). That pair is now **confirmed**: both recursive, both call the face-up copy activation
helper (game 0x140014450 = duel.dll 0x180060640), plus the 26 caller votes. (The .pdata sizes above are first chunks too where a function
is split; treat them as a rough check only.)
No card ids, so that route cannot pair them: Eval_StartOfDamageStepTriggers_Ev20, Battle_StartOfBattlePhaseStep,
Calc_EffectiveAtkDef_Modifiers, Destroy_CanEffectDestroy, Zone_GetImmunityMasks, Get_CardCodeAtZone, Chain_TryOfferEffect_ForCardRef,
Duel__Action__RegisterCandidate (weak result).

**Found by call position in mapped callers** (scratchpad twin_by_callers.py: every `call` in each binary bucketed by .pdata function; for each
game caller already in the row map, drop the callees both sides know and take the target's position in what is left; the method gives back
Pick_ZoneFilter_BySlot2, Get_NumberOfCardsToDraw and Get_UseLimitPerCopyCount unprompted, so it works):

| Game | duel.dll | evidence |
|---|---|---|
| 0x140047060 Get_CardCodeAtZone | 0x1800302C0 | 177 votes over 152 mapped callers; **decompiled: confirmed** (proxy id via Get_ProxyCardId, name-changing effects); Master Duel adds a 3rd arg `mode` |
| 0x1400307C0 Calc_EffectiveAtkDef_Modifiers | 0x1800B3B40 | 26 votes over 44 mapped callers (next 5); **confirmed** (thirteenth pass: recursion + shared helper, sizes) |

Propagating the row map with the same rule (4+ votes, twice the runner-up) adds 180 pairs but still reaches none of: Zone_GetImmunityMasks
0x140022C70 (65 callers, 4 mapped, no agreement), Battle_StartOfBattlePhaseStep 0x14020DBE0 and Battle_StartOfDamageStepMachine 0x140210C10
(only reached through the Battle Phase step tables, whose neighbours are unmapped too), Eval_StartOfDamageStepTriggers_Ev20 0x140210580 (its one
caller is that machine), and the targets at 0x1400295A0 / 0x1400AA860 / 0x1400AB050 / 0x1400DB180 (Destroy_CanEffectDestroy,
Chain_TryOfferEffect_ForCardRef, Duel__Action__RegisterCandidate among them). Next for those: find duel.dll's Battle Phase step table from its
phase driver (DLL_DuelComMovePhase side) and pair step by step in IDA.

**Fourteenth pass - by shared callees:** the game's Zone_GetImmunityMasks 0x140022C70 calls exactly Immunity_FromAttachedCards
0x140021160, Immunity_FromFieldCards 0x1400215F0, Immunity_FromOwnId 0x140021E40; the duel.dll candidates of Check_CanAttackTarget and
Attack_GetDirectAttackRule both call three consecutive functions 0x180078E00 / 0x1800793F0 / 0x18007A2F0 (as the game's two call the
trio), and among those functions' callers **0x18007B460** is the twin: decompiled side by side - same signature (player, zone, 3 mask
pointers, skipOwn), the same zone <= 6 + face-up gate, the same three calls in order. Master Duel adds a duel-mode gate (DuelState +15416
& ~1 == 0) and hands skipOwn to the third helper. All four named / typed / commented, added to make_md_function_map.py (map: 2,462 pairs).
This also backs two of the id-matched pairs: Check_CanAttackTarget and Attack_GetDirectAttackRule call the immunity trio and
Get_CardCodeAtZone in both engines. Effect_CanTargetCard, Eval_AttackDeclaredTriggers_Ev18 and Battle_CanDestroyByBattle call the
confirmed Get_CardCodeAtZone twin in duel.dll (as their game sides do).

**Fifteenth pass:** Effect_CanAffectCard 0x140026440 = duel.dll **0x180087200** by shared card ids (197 of 262; next best 60), and among its
small callers **Destroy_CanEffectDestroy** 0x1400295A0 = duel.dll **0x18008E230**, decompiled side by side: the same 7 arguments and the
same 56-byte record (card, player = srcIsOpponent ? 1 - player : player, zone, event 39) handed to Effect_CanAffectCard; Master Duel adds
record word [8] = (srcZone > 7) and clamps a negative mode to 0 in normal duels. (Searching duel.dll for the game's instruction bytes found
nothing: its record write differs.) Both named / typed / commented and in the map (2,464 pairs). Card_Destroy 0x1400D0F50 -> 0x180137950
by ids (74 of 140, next 48: weaker; it calls Effect_CanAffectCard 3 times as the game's does twice + through Destroy_CanEffectDestroy).

**40 of 46 engine hook targets paired** (fifteenth pass). Left: Battle_StartOfBattlePhaseStep, Eval_StartOfDamageStepTriggers_Ev20,
Chain_TryOfferEffect_ForCardRef, Duel__Action__RegisterCandidate, the 0x1400AA860 / 0x1400AB050 / 0x1400DB180 targets.

**39 of 46 engine hook targets paired (9 by ids - 2 of them backed by shared callees), 3 probable** (fourteenth pass). Zone_GetImmunityMasks
is ported to the map but not hooked under duel.dll (its game hook is Lock.cpp's "unaffected by" auras).

**38 of 46 engine hook targets paired (9 by ids, unverified beyond the overlap and sizes), 3 probable** (2026-10-31, tenth pass). The older list
below predates it.

**27 of 46 engine hook targets paired, 2 more probable** (2026-10-31, ninth pass). Left (19): the event evaluators (Eval_LeaveFieldTriggers,
Eval_SummonReactionTriggers, Eval_AttackDeclaredTriggers_Ev18, Eval_StartOfDamageStepTriggers_Ev20, Battle_StartOfBattlePhaseStep), the rule
checks (Battle_CanDestroyByBattle, Check_CanAttackTarget, Attack_GetDirectAttackRule, Summon_CheckPlayerRestrictions, Effect_CanTargetCard,
Calc_EffectiveAtkDef_Modifiers, Destroy_CanEffectDestroy, Zone_GetImmunityMasks), Duel_BuildContinuousLockState, Get_CardCodeAtZone,
Duel__Action__RegisterCandidate, Chain_TryOfferEffect_ForCardRef, EffectCondition_CheckCardUsableAtTiming, Get_DeckFilterIndex (done) -
plus Register_UseLimit_Marker (no single twin). A game id-table's bytes found in duel.dll are a third
route (for table-driven functions). Small row-map callers with one or two calls are the fastest
route (script: callers of each target -> functionMap -> callee lists). Distinctive card-id immediates (`find_bytes` on the id's 4 bytes) worked well for
id-laden functions like the hand limit. Left: the event evaluators (Eval_LeaveFieldTriggers, Eval_SummonReactionTriggers,
attack / damage-step / battle-phase triggers), the battle / summon / target rule checks (Battle_CanDestroyByBattle, Check_CanAttackTarget,
Summon_CheckPlayerRestrictions, Effect_CanTargetCard, Calc_EffectiveAtkDef_Modifiers, Destroy_CanEffectDestroy, Zone_GetImmunityMasks),
Xyz / Synchro / Link material checks, Duel_BuildContinuousLockState, Cond_NegateTargetMatches, the count-by-id ladders (send / cost /
tribute / multi-target / draw), Get_LifePointChanges, Register_UseLimit_Marker, Pick_ZoneFilter_BySlot2, Get_SecondaryRow, Get_ProxyCardId,
Card_GetOnlyOneKind, Duel_PromptText, Duel_GetHandLimit, Get_CardCodeAtZone, Duel__Action__RegisterCandidate, Chain_TryOfferEffect_ForCardRef,
EffectCondition_CheckCardUsableAtTiming. Method for each: a mapped caller (docs/effects-duel-dll-port.json "twins") or a distinctive
constant, then both decompiled side by side.

How these were found: a caller that is already a pair in the row map (Slot4_SelectFromList_Driver = 0x1804833B0, Slot2_Fusion_CanSummon =
0x180462650, Effect_DrawCards_CanActivate = 0x1802323F0, Timing_Check_Class1 = 0x18022CA70), then the same call in duel.dll's copy, checked by
decompiling both. **Port note:** signatures changed (extra context argument, table stride, a bool for a count), so the plugin's hooks need a
duel.dll variant each, not just new addresses.

**Estimate:** the remaining 45 hook twins and the state accesses are several sessions of IDA work; the effect lookup hook alone (own rows
answered from duel.dll's lookup) is the first useful piece, then the slot machines the compiler emits most (search / summon / send / draw /
destroy / revive and the self costs, docs/EffectSystem.md sections 96-103) - their game addresses are in the worklist with twins where known.

### 6b. The effect lookup hook on duel.dll (BUILT 2026-10-31, tenth pass; not run in game)

- **Core 1.3.7: `Core_EngineToDisplay(engineId)`** (Yu-Gi-Oh-Core.h, client binding `EngineToDisplay`): the id the screen shows for an
  engine id, which is what Effects keys its clones on. duel.dll's records carry engine ids (Konami / 40000+).
- **Dependencies/Yu-Gi-Oh-Ex/MasterDuelFunctionMap.h** (generated by `Tools/Scripts/make_md_function_map.py <md_effect_rows output>
  docs/effects-duel-dll-port.json <out>`): 2,458 game function -> duel.dll RVA pairs (row map + worklist twins + the hook-target twins above);
  `YGO::MasterDuelMap::ToDuelRva(gameAddress)`, 0 for no twin. Where duel.dll has several copies of one game function the lowest wins.
  **All 32 slot functions the compiler's words put in rows have a twin** (EffectRouting test "game -> duel.dll function map").
- **Effects 1.3.15 (EffectClone.cpp `AttachMasterDuel` / `Hook_MdGetEntry`):** detours duel.dll's Get_EffectTableEntryForCard (RVA 0x56C0C0,
  2 args: record + `int*` row index) after checking the dll's PE stamp 0x6A41C989 / image size 0x1290000 (build 80460; another build = log
  error, no hook). For a custom card it serves **a raw row (no donor) whose slots all have duel.dll twins and that needs no other game hook**:
  the plugin's own row with the twins (limit class in Extra[0] as the game path). Everything else gets no row and one log line per card
  saying why (`MdNotPortedWhy`: donor -> plays through Core's lend; own trigger other than ignition; draw / filter / deck / LP / stats /
  negate answers; composed cost / action; steps / own machines; summon lock / uses per turn / quick). The log at start says "N of M custom
  effects run on it as they are".
- **Decision:** not a slot-by-slot port of the thunk path. The slot thunks and 65 detours touch 187 hard-wired game addresses (state like
  the step variable 0x14349C13C) that under duel.dll live in its work memory; porting them is the multi-session job above. This hook is the
  piece the generic BOB words (section 5) need: a word naming a duel.dll function becomes a raw-row slot.
- **Draw count ported** (same pass): `Hook_MdGetDraw` on duel.dll's Get_NumberOfCardsToDraw (RVA 0x2AB390, same 1-arg signature as the
  game's) answers a raw row's `"draw"`. A compiled `draw(N)` (`"do": "draw"`: slots 0x14015F9C0 / 0x1400FAC40 -> duel.dll 0x1802ACFC0 /
  0x1802323F0, exactly Pot of Greed's duel.dll row) is therefore the first BOB action that runs under duel.dll as it is.
- **LP changes ported** (twelfth pass): `Hook_MdLpChanges` on duel.dll's Get_LifePointChanges (RVA 0x2A4020, same signature as the game's
  0x14015AC90; traced: Slot0_AlterLifePoints 0x1802AAD70 -> Effect_AlterLifePoints_Apply 0x18014E7E0 asks it, then deals damage / gains
  per side - both named in duel.dll.i64). And a **donor-free raw LP row** on both engines: `{"rawRow": true, "lp": {"gain": N} | {"damage":
  N} ...}` gets slot 0 = Slot0_AlterLifePoints (0x14015E570, Red Medicine / Hinotama's only slot; duel.dll 0x1802AAD70), the amounts from
  Hook_LpChanges (game, via g_Active in SlotThunk) / Hook_MdLpChanges (duel.dll). **Decision:** the compiler still writes `gain_lp` /
  `burn` with Red Medicine / Hinotama as donors (`"from": 4345 / 4350`) - that output feeds lending and the trigger composition
  (Test-Compiler case `on end_phase: burn(300);` is lent to 5974), so switching it changes game-engine behaviour no one can test tonight.
  Switch it after a raw row (`draw(2);`) is seen working in a duel.dll duel. Not tested in game.
- **"require" checks under duel.dll:** allowed when each has a duel.dll twin (`MdCheckTwin`: a game check through the map, or the plugin's
  `Check_BothExtraDecks` -> duel.dll's own 0x180286840); the row's slot 2 is then `MdSlot2_Requires` (each twin, then the row's condition).
  **Eleventh pass:** the plugin's phase / turn / LP checks (battlePhase ... opponentMainOrBattlePhase, yourTurn, opponentTurn, lpAtMost,
  lpLower) also run under duel.dll as they are: they read only what Core's bridge mirrors from duel.dll after every step (turn player
  0x14349B3B4 and phase 0x14349B3C8 are in its global run 0x3770-0x3834; LP in the player blocks, same XOR key per the bridge's comment).
  `MdSlot2_Requires` gives them the record's display id (Check_LifePoints looks the clone up), duel.dll twins the engine id. So
  `if your_turn and both_extra_decks: draw(1);` is portable now. **Unverified:** a check made in the middle of a duel.dll step sees the
  state of the last mirror (phase / turn do not change mid-step; LP can, within one chain link). Still not ported: in hand / in GY /
  controls / battled / canX (card instances and zone markers are not verified to match).
- **Unverified:** (1) duel.dll slot functions that read per-id parameters (counts, filter rows) get the custom engine id and answer their
  "unknown card" default - the game path's count / filter hooks (but the draw count) are not ported; (2) the use-limit classes 1-10 are
  assumed the same numbers in duel.dll (its registrar 0x18056DF60 lists 1-15); (3) RouteByRow is given the display id in the record while
  it routes; (4) **Extra[0]'s high byte**: duel.dll's own rows carry 0x58 / 0x60 / 0x68 there (Pot of Greed 0x5800), the plugin's row
  leaves it 0 as on the game's engine - unknown meaning; first thing to check if a ported row is never offered.
- **First in-game test:** a custom Spell with `draw(2);` (or `if both_extra_decks: draw(1);`) in a duel.dll duel: log "effect row lookup
  hooked; N of M ...", then activating it draws.
- Built Debug (Core, Effects); EffectRouting 0 FAIL (new test above); Test-Compiler 0 FAIL.

## Decisions and open points

- Effects 1.3.14 refuses to load without duel.dll (your rule, 22:50). Until step 6 is done, a custom card's **composed** BOB / BAB effect
  (draw / search / revive ... parts) does nothing in a duel.dll duel; a single `as(<card>)` effect works fully (step 1). The WolfX library
  says so in each added entry's note.
- Versions bumped tonight: Core 1.3.7, MoreCards 1.3.4, Effects 1.3.15, Funky 1.3.1, SpeedHacks 1.3.1.
- Built (Debug + SolutionDir): Core, MoreCards, Effects, SpeedHacks, Funky, WolfX; EffectRouting (Release, its only configuration).
- Not committed. GLOSSARY.md and docs/MasterDuelEngine.md updated for lending, the name picker and the Fusion tables.

**To test first in the morning:** (1) a custom card with `as("Fallen of Albaz");` or any post-game card: log "plays as engine card ...";
(2) Crossout Designator: the picker opens with your deck's names; (3) Albaz against a 2500+ ATK monster: the Titaniklad Fusion;
(4) a custom Spell with only `draw(2);` in a duel.dll duel (section 6b: Effects' own row on duel.dll's lookup; the log line at start says how
many custom effects run on it).
