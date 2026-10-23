# Duel format: Speed Duel and Rush Duel

Yu-Gi-Oh-Core (`DuelFormat.cpp`, 2026-10-22). `[Yu-Gi-Oh-Core] DuelFormat=master | speed | rush`, also on WolfX's Config page.
Pair it with the deck limits ([DeckLimits.md](DeckLimits.md)) for Speed Duel decks (20 to 30 main, 5 extra).

## The switch

The duel engine has Konami's Speed and Rush rules built in, keyed on one value: `Duel::PlayerState` +0x37A4
(0x14349B3E4, IDB type `DuelFormat`: 0 Master, 1 Speed, 2 Rush). `DuelSetup_ClearState` (0x14005FC90) zeroes the whole
PlayerState at the start of every duel and **nothing in the exe writes the value** (every `.text` reference is a read), so on PC the
rules never run. Core hooks ClearState and writes the format straight after it, before `DuelSetup_InitEngine` reads it. Both
duel start paths go through ClearState: `DuelSetup_ClearAndInit` (one deck per side) and `DuelSetup_LoadBothDecks` (tag duels).

Online duels use a separate value, `Core_SetOnlineDuelFormat` (Yu-Gi-Oh-Core.h), because both players' engines run in lockstep and a
different format on one side would desync them. Yu-Gi-Oh-MP sets it from the lobby:

* The host's match settings screen (screen 34) has a **Duel format** row (Master / Speed Duel / Rush Duel) under Ban list, when
  hosting (not Ranked). It borrows the Join button's slot 7 (no free slot is left) and is handed to the game as slot 5 while the cursor
  is on it, because slot 7 is an action (confirm = Join) and gets no left/right keys (MP `LiveSetting.cpp`, IDB comments on
  `LiveSetting_RowsInput` / `LiveSetting_ChangeOption`).
* Lobby data `exformat` = `master` / `speed` / `rush`; every Yu-Gi-Oh-MP player sets the format when entering the lobby. The lobby's
  settings panel shows a "Duel format" line when it isn't Master. A player without Yu-Gi-Oh-MP plays Master and the duel desyncs:
  the host sees them on the "No Yu-Gi-Oh-MP" line and should kick them.
* Deck sizes follow (`Core_SetDeckLimits`): Speed 20-30 main / 5 extra, Rush 40-60 / 15, Master = the player's own Config.ini
  limits. The host's deck list is filled again when the row changes. A joiner's deck is checked on their screen before they read the
  lobby, so a joiner's own limits decide which decks they can pick.
* The online starting LP stays the lobby's LP setting (Speed Duel: pick 4000). Back on the screen or leaving the lobby puts Master back.

## What the engine does with it (every reader traced)

| Where | Speed (1) | Rush (2) |
|---|---|---|
| `DuelSetup_InitEngine` 0x14005FD90 | 4000 LP, 4-card hand | 8000 LP, 4-card hand |
| `Get_UsableZoneMask` 0x140019430 | zones 1-3, S/T 1-3, field (0x176E176E) | same, and no Extra Monster Zones |
| `Get_DrawCountForTurn` 0x14014CB80 | 1 | draw until you hold 5 (at least 1) |
| `Duel__Phase__EnterDrawPhase` 0x14014CD00 | no draw on the first turn | draws on the first turn too |
| `Duel__Phase__EnterMainPhase` 0x140148140, `Get_PhaseMenuFlags` 0x140063C70 | no Main Phase 2 | no Main Phase 2 |
| `Get_NormalSummonsAllowed` 0x140053ED0 | normal | 50 (unlimited) |
| `Chain_MovePendingToChain` 0x1400B5130 | 16 links | 1 link (no chains) |
| `Duel_TurnStep5_CheckTurnOrTimeLimit` 0x140147DD0 | turn 50 (90 in tag) is a draw | same |
| `Duel__Msg__Handle_04_NextTurn` 0x1401129E0 | tag partner LP reset to 4000 | 8000 |
| `Duel__IdleCommand__StateMachine` 0x1401035A0 | | effect-started idle commands end at once |
| `Get_EffectiveLevel`, `Get_EffectiveRace`, `Calc_EffectiveAtkDef_Modifiers`, card code | card-specific Speed rulings | |
| AI (0x14023CE80, 0x14024E790, 0x14024EAA0, 0x1406A2C20, 0x1404FA980, 0x1406A3C20 ...) | 3-zone "field full", deck-out margin 8 not 10, copy limit 2, Skill 7220 checks | same |

## The starting LP

The duel UI copies `g_iStartLifePoints` (0x140C8D370; `Setup_RuleAndLP` sets it to 8000 before a duel) over both players' LP
when the duel starts (`Draw_DuelAnimationFromId` at 0x1407C1A97). For Speed, Core sets it to 4000 when it is still 8000, so a
different starting LP from another plugin (PatchMeOut `StartingLP`) is kept.

## Not in the engine

* The duel field is drawn by the UI, which never reads the format: it still shows 5 monster and 5 spell/trap zones. Only the
  middle 3 can be picked.
* Speed Duel Skills (only AI checks for card 7220 exist) and Rush Duel Maximum monsters.
* Rush cards themselves: add them as custom cards (cards.json / WolfX / a mod).
