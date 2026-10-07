# Duel animations (`DuelAnimId`)

Everything the duel screen shows for the rules engine goes through one function: `YGO::UI::Draw_DuelAnimationFromId(anim, a2, a3, a4)` at 0x1407C1450 (thunk 0x1407EB7E0). `Duel__MsgQueue__ExecuteFront` turns each engine message (`DuelMsgCode`, see MultiplayerSystem.md) into one or more of these calls. Mapped 2026-10-04.

Code: `Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-UI.h` has the enum and a catalog (`YGO::UI::DuelAnims[]`, `FindDuelAnim`) with argument labels and whether the animation can be played by hand. Funky's **UI** tab is built from it. IDB: enum `DuelAnimId` (all members), the dispatcher's first argument is typed with it, and every handler is named (`DuelAnim__*`, `EffectHandle_*__Spawn`, `EffActField_*__Spawn/__ctor`).

## How it works

- On entry: `g_iPreviousDuelAnimation = g_iCurrentDuelAnimation; g_iCurrentDuelAnimation = anim`. In a tutorial duel, `DuelAnim__TutorialAllows` (0x1407C0800) can swallow the call.
- `YGO::DuelGraphics::g_ActiveAnimation` (0x1427D0C08) = anim for the duration of the call, then 0. Helpers read it to tag what they create.
- A visual case allocates a presentation object (RTTI names `YGO_FRONT::EffectHandle_*` and `YGO_FRONT::EffActField_*`), stores the anim id at object +40 and sets a completion bit in `YGOFront_UNK.BaseWidget.field_AC` / `dwordA8`. The message queue waits on those bits before it runs the next engine message.
- Ids with no case do nothing: 3, 17, 34, 37, 54, 65 (`Finish_DestinyBoard` in the IDB: the engine still sends it), 66-68, 71, 73-79, 85, 86, 91+. Some of these are sent by the engine (37 attack declare, 54 declare loss, 77 end of turn); their presentation was cut from this build.
- The return value is always 1.

## Who can play what

Funky only fires the **Play** and **Play with care** rows, and only while a duel is running (side 0 has cards in deck or hand). The other rows are listed greyed out when "List the ids that can't be played from here" is ticked:

- **Needs cards**: the arguments are packed card refs (`side | zone << 1 | index << 6 | 0x4000`), zones or card instances that must exist. The engine is the right sender.
- **Waits for input**: opens a prompt the engine expects to answer; fired alone it is left hanging.
- **No animation**: changes duel or UI state only.
- **Writes the save**: bumps save stats or unlocks a Steam achievement.

## The ids

| id | name | use | arguments (a2, a3, a4) | what it does |
|---|---|---|---|---|
| 1 | DuelStart | play | - | "DUEL" splash (`EffectHandle_DuelStart`) |
| 2 | DuelEnd | care | outcome 1 local won / 2 other won / 3 draw | round result (`YGO__DUEL__Set_RoundResult`), `YGO_FRONT__ShowDuelResultBanner`, result music (not for Exodia). The duel finishes after it |
| 4 | FlowStep | state | step, a3, a4 | `DuelAnim__FlowStep`: prompt/cursor step of the duel flow, tutorial aware |
| 5 | PhaseChange | care | side, phase 0-5 (Draw, Standby, Main 1, Battle, Main 2, End) | `EffectHandle_PhaseChange`; also moves the on-screen phase marker |
| 6 | TurnStart | care | side | `EffectHandle_TurnChange`; also sets the on-screen turn owner and clears the per-turn part of `g_DuelStatBlock` (`DuelStatBlock__ClearTurn`) |
| 7 | FieldChange | state | a2, a3 | field background change + `SND_FIELD_CHANGE` (arguments not decoded) |
| 8 | CursorToCard | cards | side, zone, index | card cursor + highlight (skipped in tutorial 26) |
| 9 | UpdateMusicForLP | play | - | `YGO::DUEL::Duel_UpdateMusicForLP` |
| 10 | AttackReset | state | - | clears the attack state (also called at engine init) |
| 11 | AttackConfirm | state | - | attack state = confirmed, cursor to the attacker |
| 12 | AttackDeclare | cards | attacker side \| zone << 8, target side, target zone | a4 < 0 cancels, a4 >= 7 is a direct attack (`EffectHandle_DirectAttack`) |
| 13 | Battle | cards | a2, a3, a4 | `EffectHandle_Battle` between the attacker and target from the attack state |
| 14 | BattleEnd | state | - | attack state = 3 |
| 15 | LP_Set | state | side, LP | sets the shown LP; before the duel starts it also writes the engine LP (from `Get_StartingLifePoints`) |
| 16 | LP_Change | save | side, delta, byte0 must be 0 / byte1 cause | `EffectHandle_DamagePoint` popup + `YGO__Stats__OnLifePointsChanged` |
| 18 | HandShuffle | play | side | hand shuffle (needs 2+ cards in hand) |
| 19 | HandCardReveal | cards | side, hand index | reveals one hand card |
| 20 | ExodiaHand | cards | side, face up 0/1, instance (0 = whole hand) | turns hand cards face up; the whole-hand form is the Exodia reveal |
| 21 | DeckShuffle | play | side | `EffectHandle_DeckShuffle` + deck redraw |
| 22 | PileRefresh | state | side, location 0-17 | redraws a zone or pile (13 hand, 14 extra, 15 deck, 16 grave, 17 banished) |
| 23 | DrawFromPile | cards | a2, a3 | card object leaves a pile, shown in the card info panel |
| 24 | PileUpdate | cards | a2, a3, card | pile card update |
| 25 | CursorSelect | cards | side, zone \| index << 8 | cursor to a card and select it |
| 26 | CardMove | cards | card \| kind << 16, from, to | `YGO__UI__OnCardMove` |
| 27 | CardSwap | cards | card A, card B | two cards trade places (`DuelAnim__CardSwap`) |
| 28 | CardReturn | cards | card, 0 = to the Extra Deck / 1 = to the hand | card flies back (`EffectHandle_MoveSoul` path) |
| 29 | CardChangeId | cards | packed, card id, instance | a card turns into another card; counts the card as used |
| 30 | CardSet | cards | a2, a3, a4 | `EffActField_SetCard` + `EffectHandle_CardFlash` |
| 31-33 | CardDestroy(2/3) | cards | a2, a3, a4 | `EffActField_DestroyCard`; 31/32 refresh the zone first, 33 doesn't; 32 uses another completion bit |
| 35 | CardActivate | cards | card, a3, card id | `EffectHandle_CardHappen`; counts the card as used |
| 36 | CardNegate | cards | a2, a3, instance | `EffectHandle_CardDisable` |
| 38 | CounterChange | cards | side \| zone << 8, count, instance | `EffectHandle_TurnCounter` (card 11888 counts the other way) |
| 39 | ActionEnd | state | - | end of an action: clears cursor/selection |
| 40 | SpellCounter | cards | side \| zone << 8, a3, a4 | `EffectHandle_MagicCounter` |
| 41 | CounterSet | cards | a2, a3, a4 | `EffectHandle_TurnCounter` |
| 42 | MonsterShuffle | cards | a2, a3 | `EffectHandle_MonstShuffle` |
| 43/44/45 | Tribute Mark/Clear/Play | cards | a4 instance / - / side, zone, instance | material kind 1; Play = `EffectHandle_CardSacrifice` |
| 46/47/48 | Fusion Mark/Clear/Play | cards | same | material kind 3; Play = `EffActField_Fusion` ("CardFusion" sound) |
| 49/50/51 | Synchro Mark/Clear/Play | cards | same | material kind 4; Play only clears (the scene is 59) |
| 52 | ZoneHighlight | input | side, zone, 1 new list / 2 last | selectable-zone list |
| 53 | ChainResolve | cards | side, a3, chain length | `YGO__UI__OnChainResolve` (`EffectHandle_Chain`) |
| 55 | Message | input | kind 0-18, string, a4 | duel dialog (`DuelAnim__Message`) |
| 56 | SelectOption | input | a2, a3 | option/number select (`DuelAnim__SelectOption`) |
| 57 | SummonNormal | cards | packed, card id | `EffActField_SummonMonster`; counts the card as used |
| 58 | SummonSpecial | cards | packed, instance | `EffActField_SummonMonster`; token telemetry; counts the card as used |
| 59 | ExtraSummonScene | cards | instance, a3, kind | 0/1 `EffectHandle_SceneFusion`, 2 `EffectHandle_Synchro`, 4 `EffectHandle_NewXYZ`, 5 `EffectHandle_Pendulum`, 6 `EffectHandle_LinkSummon`. Nothing if the card has no materials |
| 60 | ShowCardInfo | play | card id | `YGO::DUELUI::CardInfo_ShowCard` |
| 61 | Coin | play | side \| zone << 1 \| index << 6, card id, bit0 result | `EffectHandle_Coin` |
| 62 | Dice | play | side, roll, a4 | `EffectHandle_Dice` |
| 63 | Yujyo | play | side, non-zero | `EffectHandle_Yujyo` ("friendship") |
| 64 | Finish_Generic | save | side, win reason | no visual; reason 6 (Exodia) by the local player unlocks achievement 23 |
| 69 | TributeOrRitualSummon | save | side, card, a4 1-3 tribute / 6 ritual | save stats only |
| 70 | ExtraDeckSummon | save | side, card, a4 0/1 Fusion, 2 Synchro, 4 Xyz, 5 Pendulum, 6 Link | save stats only (Link goes to slot 44, no Steam name) |
| 72 | StatSpellTrap | state | side, card, kind | compiled-out telemetry; does nothing |
| 80 | LPPanelFlash | play | side, 0 on / 1 off | LP panel highlight for 60 frames |
| 81 | HandRandom | play | side | `EffectHandle_HandRandom` |
| 82/83/84 | Xyz Mark/Clear/Play | cards | as 43-45 | material kind 5 |
| 87/88/89 | Link Mark/Clear/Play | cards | as 43-45 | material kind 6; Mark also spawns `EffectHandle_MaterialMark` |
| 90 | Janken | input | a2, a3 | `EffectHandle_Janken` (rock-paper-scissors); arguments not decoded |

### Material kinds 4/5/6

Kinds 1 (Tribute, `CardSacrifice`) and 3 (Fusion, `EffActField_Fusion`) are confirmed by the object each Play creates. Synchro/Xyz/Link for 4/5/6 is inferred: `DuelMsg_MaterialAnimByFrame` (0x4D) plays 51 / 84 / 89 for a card's BaseFrame 10 / 12 / 18, and only kind 6 spawns `EffectHandle_MaterialMark` (the Link arrows mark). Confirm in a live duel before relying on it.

## Not done

- The ids marked "arguments not decoded" (7, 42, 90 and the a3/a4 of several card ones).
- Whether 5/6 confuse the engine when fired between its own messages (they only touch the presentation's phase/turn fields, and the next real message overwrites them). Untested in game: the Funky build couldn't link while the game was running.
