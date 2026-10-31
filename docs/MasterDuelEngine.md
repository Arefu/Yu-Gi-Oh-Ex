# Master Duel's duel engine (duel.dll)

Decision 2026-10-30: the duel runs on Master Duel's `duel.dll` instead of the game's built-in engine, so every card Master Duel has plays with
Konami's own effects, with no per-card effect porting. `duel.dll` is the game's own duel engine, extended: the same 48-byte effect rows and
machines, the same message queue, the same card ids (Konami's database ids: Pot of Greed 4844 in both), the same player-state struct with fields
inserted. It is loaded from `Plugins\Yu-Gi-Oh-Core\duel.dll` (distribution handled separately; it is not in git). IDB: `duel.dll.i64`.

Plugin roles: **Core** (always on) loads the engine, feeds it the game's card data and redirects the duel to it; it knows nothing about other
plugins. **Yu-Gi-Oh-MoreCards** and **Yu-Gi-Oh-Effects** are optional: they only call Core's API (`Core_EngineReady`, `Core_EngineProc`,
`Core_EngineRefreshCards`, Yu-Gi-Oh-Core.h), never each other.

## Status

| Stage | What | State |
|---|---|---|
| 1 | Core loads duel.dll, work memory, card data from the game's lookups (MoreCards' cards included via `Core_EngineRefreshCards`), self-test CPU duel to console.log | built 2026-10-30 (Core 1.3.4, MoreCards 1.3.3), not run in the game; `[Yu-Gi-Oh-Core] MasterDuelEngine=1` |
| 2 | Redirect the duel screen to it (Core `MasterDuelBridge.cpp`) | built 2026-10-30, not run in the game: offline duels only (not tag / online / tutorial) |
| 3a | Card ids: MoreCards gives each card the engine its Konami id (`"konamiId"` in cards.json, 4,281 Cards mod cards; `make_cards_mod.py` writes it), else id + 20000; borrowed duel ids shown on screen (`Core_EngineMapCard` / `Core_EngineSetDisplayId`) | built 2026-10-30, not run |
| 3b | MoreCards: custom cards' archetypes / same-name / genres into the engine's Named / Same / Genre data | not built |
| 4 | Effects: effects.json / cards.json effect rows into duel.dll's own tables | not built (rows are the same 48-byte format) |

Standalone proof (2026-10-30, scratchpad `mdspike/spike.py`): Python ctypes loaded duel.dll (imports only KERNEL32), fed it the game's own
bin\CARD_* files from YGO_2020.dat, and ran a CPU-vs-CPU duel to the end (11 turns, LP to 0, under a second).

## API (read in duel.dll.i64, build "80460 : 20260629")

Setup, in order:
- `DLL_SetWorkMemory(void*)`: NULL returns the size (2,908,148 bytes); then the buffer. Blocks: +0 DuelState (player stride 0xDDC = 3548; the
  game's is 0xD94), +15824 list block, +18248 message queue (count at +2064, 8-byte entries from +16), +2907324 config (= the game's
  `Duel_DuelEngine`: +0 seed, +8 player types, +24 CPU params, +40 first player, +44 decks 192 bytes per player, +812 rules, +820 finish).
- `DLL_SetCardProperty(u32 records[2n], bytes)`: record 0 dummy, then one per card. u32 a = id 16 | kind 6 << 16 | attribute 4 << 22 |
  level 4 << 26 | star type 2 << 30 (1 Level, 2 Rank, 3 Link = the game's `StarTypeValue`); u32 b = ATK/10 9 | DEF/10 or Link arrows 9 << 9 |
  icon 3 << 18 | type 6 << 21 | Pendulum scale 4 << 27. The id range is record 1's id .. last record's id. Kinds 0-45 are the game's kind table
  exactly (Master Duel adds 46-56).
- `DLL_SetInternalID(u16[id - first id])` -> record index. `DLL_SetCardGenre(u64 per record)`, `DLL_SetCardNamed(CARD_Named)`,
  `DLL_SetCardSame(12-byte records, bytes)`, `DLL_SetCardLink(4-byte records, bytes)`: the game's own files.

A duel: `DLL_DuelSysClearWork()`; `DLL_DuelSysSetDeck2(player, main u32[], n, side u32[], n, extra u32[], n)` (id in the low word);
`DLL_DuelSetPlayerType(player, 0 human | 1 CPU | 3 replay)`; `DLL_DuelSetCpuParam(player, n)`; `DLL_DuelSetRandomSeed(n)`;
`DLL_DuelSetFirstPlayer(p)`; `DLL_DuelSysInitCustom(type, flag, LP0, LP1, hand0, hand1, noShuffle)`; then `DLL_DuelSysAct()` per step until
`DLL_DuelGetDuelFinish()`. `DLL_DuelGetLP(p)` (stored XOR the seed's low word), `DLL_DuelGetTurnNum()`, `DLL_DuelGetCurrentPhase()`.

Messages: the engine's `sub_18010D830` is the game's `Duel__MsgQueue__Push` (0x14000F310) in behaviour (same codes, same code-109 high-word
prefix, nothing pushed in replay mode) and also calls the `DLL_SetAddRecordDelegate` callback with each 8-byte message.
`DLL_SetEffectDelegate(fn, fn2)`: notification hook (`qword_1811F9B98`, called e.g. with (13, ...) from the summon evaluator).

Input: `DLL_DuelComGetCommandMask(player, zone 0-18, index)` switches on the input mode (state +15568; the game's `state+0x3838`,
0x14349B478; modes 1-9: 7 = idle command, 8 = select, 9 = pick a zone with the callback at +15640); `DLL_DuelComDoCommand(player, zone, index,
command)` writes the command record (state fields 3892-3908); `DLL_DuelDlgSetResult(n)` (state +15516); `DLL_DuelListSetIndex(n)` (list block +8,
as the game's 0x14349C5C8).

## What the bridge does (built 2026-10-30, `MasterDuelBridge.cpp`)

- **Duel start** (`DuelSetup_InitEngine` 0x14005FD90 detoured): Core forgets the last duel's display ids, the game's own setup runs (it loads
  the decks; MoreCards borrows its duel ids and reports them), then `Duel_DuelEngine` (824 bytes) is copied into duel.dll's config - it IS that
  block (seed / LP key at +0, player types +8, first player +40, deck blocks +44 + 192 x player, rules +812) - with the deck ids turned into
  engine ids, `DLL_SetEffectDelegate(animation, busy)` is set and `DLL_DuelSysInitCustom(DuelFormat, 0, 0, 0, -1, -1, 0)` starts the duel.
- **Animations**: duel.dll runs its own message queue and executor (`sub_18010FAC0` = the game's `Duel__MsgQueue__PumpAndMirror`,
  `sub_18010D990` = `Duel__MsgQueue__ExecuteFront`) and reaches the screen only through its effect delegate: the two callbacks are the game's
  `Draw_DuelAnimationFromId` (0x1407C1450) and `Duel__UI__IsAnimBusy` (0x1407EB7B0) - the game's engine's only two UI calls (its UI thunks
  0x1407EB7E0 / 0x1407EB7B0; the other thunks it calls are card data: archetype 0x1407EB9E0, same name 0x1407EBA00, genre 0x1407EB9F0,
  multiplayer flag 0x1407EB9C0). The state is mirrored before each animation.
- **Tick** (`Duel__MasterTick_GatedByNetEvent` detoured): `DLL_DuelSysAct`, mirror; when `DLL_DuelGetDuelFinish` is set, the result words
  (config +816 / +820) go to `Duel_DuelEngine` and the tick returns 1 (the game ends the duel).
- **Mirror**: per player LP (raw: same key), the five counts, 13 zones (first 24 of duel.dll's 28 bytes, entry transcoded), the five lists
  (transcoded), the 512-entry instance table, `Duel_UNK` (1340 bytes), and the globals in three runs (0x3770-0x3834 = +1172, 0x3838 = +1176,
  0x3840-0x3870 = +1180). Entry transcode: id 16 -> 14 (display id), instance bit 16 + bits 24-31 -> bit 14 + bits 23-31, flags 17 -> 15
  (0x8000), 18-23 -> 17-22.
- **Input**: `Pick_GetCardFlags` -> `DLL_DuelComGetCommandMask`, `SubmitPick` -> `DLL_DuelComDoCommand` (prompt answers come this way too:
  net events 0xC / 0xF feed it), `Com_DefaultLocation`, `Get_PhaseMenuFlags` -> `DLL_DuelComGetMovablePhase`, `List_GetItemAttribute` /
  `List_GetItemId` / `List_GetItemInstance` -> `DLL_DuelList*`. The duel screen writes no engine state itself (one write: the tag-duel flag at
  duel start).
- **Faults**: duel start or a step that faults turns the bridge off (logged); the game's engine plays the next duel.

After the first in-game run (2026-10-30: duel ran, draw / hand commands reached duel.dll, phases and Polymerization's list did not): the
screen's real API is the `j_` thunk block 0x1407EB320-0x1407EBA20 (the scan of direct calls missed it). Added: `0x140063D00(phase)` =
`DLL_DuelComMovePhase` (thunk 0x1407EB560), `0x140063AE0()` = `DLL_DuelComCancelCommand`, `0x140063B90(flag)` = `DLL_DuelComCancelCommand2`,
the prompt-answer setter thunk 0x1407EB9B0 (`mov [state+0x3808], ecx`) also calls `DLL_DuelDlgSetResult`, the list index setter 0x14005E010
also calls `DLL_DuelListSetIndex`; the mirror copies the input sub-mode (0x383C = +15572) and the list block (header 24 bytes, entries
transcoded, attribute words +1224 -> +2448). `[Yu-Gi-Oh-Core] MasterDuelLog` 0 / 1 / 2 logs every duel.dll call Core makes (arguments and
result), every animation and each turn / phase / input change.

Not covered yet: the command-flag table the screen reads through thunk 0x1407EB680 (`0x14005D620` -> stru_14349C280, 40 sites); the remaining unpaired engine calls from the screen
(0x1400662F0, 0x1400452A0, 0x140043840, ...) still run the game's engine code on the mirrored state; card ids inside animation arguments are not
translated; the custom cards' archetypes / same-name / genres (stage 3b); Effects' own JSON (stage 4).

## Redirect design (stage 2, traced 2026-10-30)

The game's duel screen talks to its engine in three ways; each becomes a redirect:

1. **The tick.** `Duel__MasterTick_GatedByNetEvent` (0x14003EAC0) pumps the message queue (`Duel__MsgQueue__PumpAndMirror` 0x1400117E0,
   which blocks while messages play) and otherwise runs one step of `Duel_PhaseStepFunctionTable[field_37BC]`, unless the player type byte
   (`Duel_DuelEngine.__Byte_0008`) is 3 (replay: messages only). The step becomes `DLL_DuelSysAct`.
2. **Messages.** Every Master Duel message (AddRecord delegate) is appended to `g_DuelMsgQueue` (0x14332FA40) directly (the game's Push is a
   no-op in replay mode). The game's executor (`Duel__MsgQueue__ExecuteFront`) and its handlers animate them as now.
3. **State the screen reads directly.** The duel screen's code (0x140760000-0x140800000, 136 functions) touches only 61 engine addresses,
   22 of them array bases: `Duel_DuelEngine` (seats, player types: the game's own setup still fills them), `PlayerState` 0x143497C40 per player
   (zones 0x143497C8C, hand / lists, LP), turn player 0x14349B3B4, phase 0x14349B3C8, input mode 0x14349B478, the pick block 0x14349B444-450,
   the list block 0x14349C5C0. These are mirrored from Master Duel's state after each step (by meaning, through its `DLL_DuelGet*` getters, or by
   the offset map); the game's own engine also runs ahead and the screen plays its messages against the finished state, so the timing is the same.
4. **Engine calls from the screen.** Only 33 engine functions are called from the duel screen (each from one site): setup
   (`DuelSetup_ClearAndInit` 0x14005FEB0, `DuelSetup_LoadBothDecks` 0x14005FED0), the tick, menus (`YGO::Duel::Pick_GetCardFlags` 0x1400632D0
   = `DLL_DuelComGetCommandMask`, called by `YGO::DUELUI::Get_CardCommandFlags` 0x1407AD660; `YGO::DUEL::Get_PhaseMenuFlags` 0x140063C70), input
   (`YGO::Duel::SubmitPick` 0x140063840, 0x140063C40, 0x1400662F0, 0x14005DCF0, 0x14005E080-0x14005E390), queries (`Card_GetPositionByInstance`
   0x140044480, `Calc_EffectiveAtkDef` 0x1400307A0, 0x140043840, 0x140044C50, 0x140044F90, 0x140045170, 0x1400452A0, 0x140046950, 0x140046B30,
   0x14003CDB0, 0x14003D3F0, 0x14003D7F0, 0x14002ED50, 0x140020B30, 0x140017250, 0x140019D90, 0x14005CE90, 0x140067650, 0x1400800D0). Each is
   detoured to the matching `DLL_*` export. Next: name each of the unnamed ones and pair it with its export.

Paired so far (same code or same logic; renamed in YuGiOh.exe.i64):

| Game function | duel.dll export | How it was matched |
|---|---|---|
| `YGO::Duel::SubmitPick` 0x140063840 (player, zone, index, command) | `DLL_DuelComDoCommand` | the next pair calls it the same way |
| `YGO::Duel::Com_DefaultLocation` 0x140063C40 | `DLL_DuelComDefaultLocation` | identical code (default zone, then command 12) |
| `YGO::Duel::Pick_GetCardFlags` 0x1400632D0 | `DLL_DuelComGetCommandMask` | switch on the input mode, modes 1-9 |
| `YGO::DUEL::Get_PhaseMenuFlags` 0x140063C70 | `DLL_DuelComGetMovablePhase` | same logic (modes 2 / 3, flags 8 / 0x10 / 0x20) |
| `YGO::Duel::List_GetItemId` 0x14005E180 | `DLL_DuelListGetItemID` | same list read + validity check (`List_IsItemValid` 0x14005E080) |
| `YGO::Duel::List_GetItemInstance` 0x14005E1B0 | `DLL_DuelListGetItemUniqueID` | the entry's card instance |
| `YGO::DUEL::Is_ZoneAvailable` 0x140019D90 | `DLL_DuelIsThisZoneAvailable(2)` | bit of the usable-zone mask |
| 0x14005DCF0 | `DLL_DuelListInitString` (likely) | builds the list prompt text; same place in the function order |

The export internals are in the same order in both binaries (lists, card queries, command mask, command, movable phase), which is how the
rest are being matched. Not an export: 0x140067650 (puts a card in a zone and pushes message 67), 0x14005CE90 (is the screen waiting on this
player: reads the game's own queue / net state, stays the game's).

State fields paired by meaning (mirror): input active MD +15580 = IDA `field_383A`; input mode +15568 = `field_3832`; asked player +15616 =
`field_385A` high dword; phase +15388 = `field_3788`; duel type +15416 = `field_37A4`. (IDA names; see the note below for real offsets.)

### Player state layout (mirror)

From `DLL_DuelGetCardNum_0` / `sub_180599910` / `DLL_DuelGetCardUniqueID_0` (Master Duel) and `YGO::Duel::Get_CardIdAtZoneIndex` 0x14003D3F0 (game):

| | Master Duel, per player (stride 3548 = 887 dwords) | game, per player (stride 3476 = 869 dwords) |
|---|---|---|
| counts | +12 hand, +16 Deck, +20 GY, +24 Extra Deck, +28 banished | in the player header |
| hand list (u32 entries) | +484 | +0x19C (`field_198` + 4) |
| 2nd list, 480 bytes | +964 **Extra Deck** (zone 14) | +0x37C **Deck** (zone 15, `field_378`) |
| 3rd list, 600 bytes | +1444 **Deck** (zone 15) | +0x55C **Extra Deck** (zone 14, `field_558`) |
| GY list | +2044 | +0x7B4 (`field_7B0`) |
| banished list | +2644 | +0xA0C (`field_A08`) |
| field zones 0-12 | +92, 28 bytes each | +0x4C, 24 bytes each (`POne_MonsterZone`) |
| card instance table (8 bytes each) | +7148 | `field_1B52` (absolute, after both players) |

List entries are transcoded, not copied: Master Duel's u32 = card id 16 | instance in the high half (bit 16 + 2 x bits 24-31); the game's = id 14 |
instance bit 14 + 2 x bits 23-31 (`(v & 0x4000) != 0) + 2 * (v >> 23)`).

The player header matches: LP at +0 (Master Duel stores it XOR the seed's low word), the five pile counts at +0x0C hand, +0x10 Deck, +0x14 GY,
+0x18 Extra Deck, +0x1C banished in both (`Duel::PlayerState::Player`). Master Duel inserts 16 bytes before the zones (+92 vs +0x4C).
Each pile list is Master Duel's at the game's offset + 72 (13 zones x 4 bytes + those 16): hand 0x19C / 484, Deck 0x37C / 964, Extra Deck
0x55C / 1444, GY 0x7B4 / 2044, banished 0xA0C / 2644 (duel.dll's deck loader `sub_180037590` writes Deck and Extra there). The bridge first had
Deck and Extra paired the wrong way round, which left the field's deck pile empty while duel.dll still drew from its own.

Dialog kinds (`state +0x3800` / duel.dll +15508, and animation 55's first argument) are numbered differently: Master Duel added a kind 5, so
its 6 and up are the game's kind - 1 (6 option list = 5, 7 phase select = 6, 8 type declaration = 7, ...). Untranslated, the phase menu
opened the game's type declaration (Fiend / Sea Serpent / Zombie). `ToGameDialogKind` in MasterDuelBridge.cpp.

Lending (`Core_EngineLend`, 2026-10-31): for one duel the card the screen shows as display id D plays as engine id E, with E's record in
duel.dll's card table rewritten from a game card's props (duel.dll reads Core's table in place, `DLL_SetCardProperty` keeps the pointer), and
put back at the next duel start. Yu-Gi-Oh-MoreCards lends a single-effect clone (`effectClone.from`, any id) its source when no real copy of it
is in either deck: the clone then IS the source to the engine (every row and id-keyed branch) with its own stats. WolfX's Effect library
offers every card that way (`as(<id>)`).

Card-name declaration (2026-10-31): duel.dll's dialog kinds 160-170 (20 + list kind 0x8C-0x96) want a card name from its own client; the
bridge opens the game's list picker (as `UI_BeginCardListSelect`) with the names `DuelDlg_IsDeclarableCardName` (0x180598AF0, called only
when its code matches the traced build) allows - the duel's cards first, up to 300 - answers busy while it is open and sends
`DLL_DuelDlgSetResult(card)`.

Fusion recipes are duel.dll's own (`FUSION_RECIPES_2_3` 0x180F2DB00, 847 rows; `FUSION_RECIPES_4_5` 0x180F2F580, 21 rows;
`Get_FusionMaterial` 0x180457A90): nothing to feed for the post-game Fusions.

Deck order: duel.dll gets each Main Deck in the order the game's own setup left it (already shuffled by `Duel_ShuffleDeck`, plus
anything a plugin did there, such as Funky's stacked test hands) and is told not to shuffle (`DLL_DuelSysInitCustom`'s last argument).
A deck over the block's 60 ids is passed as before and shuffled by duel.dll.

Message text: both engines' ShowMessage (game `Duel__Msg__Handle_08_ShowMessage`, duel.dll `DuelDlg_ShowMessage`, d_dueldlg.c) write the
message as words at 0x3810 (count 0x380C) and the screen builds the text from them (`Duel_BuildPromptMessage`; type 5 words are card ids,
so they are mapped to display ids). The globals are copied on change only, but the screen clears its copy after a message, so on
animation 55 the bridge copies duel.dll's whole prompt block (0x3800-0x3834); before that, messages with the same word count as the last
one came up as an empty box with only OK (the hand-size discard message, Heart of the Underdog's reveal).
A zone (`Duel::InPlay_Card`, 24 bytes: CARDID, INTLID, UNK_01, Pos, UNK_02, UNK_03) is Master Duel's 28-byte zone with 4 bytes added at the end:
+0 id / instance (`DLL_DuelGetCardUniqueID` reads +2), +7 the face byte (`DLL_DuelGetCardFace`; the game reads INTLID's top byte the same way),
+12 the card's parameter / counter (`DLL_DuelGetThisCardParameter`; the game's 0x140046950 reads `Pos`).

IDA note: in the 0x3800+ part of `stru_143497C40`, IDA's field names are 6 bytes below the real offsets (`field_3832` is at 0x14349B478).

Offset map attempts (scratchpad tools statemap.py / statemap2.py, aligning state accesses of the 2,492 matched effect-function pairs) were too
thin (73-133 offsets); seen shifts -72 / -144 (player blocks) and about -1172 to -1180 past them. Mirroring by meaning is the plan.
