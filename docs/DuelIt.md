# Duel recording and DuelIt

`Yu-Gi-Oh-MP` records every duel (single player and online) to **`Duels.log`** in the game folder, next to `console.log`. **DuelIt** (`Tools/DuelIt`, built to `Binaries\Debug\Tools\DuelIt.exe`) reads that file and replays a duel on a board: LP, turn, phase, and where every card is, step by step or played back.

## Split logs (Yu-Gi-Oh-Console)

`Yu-Gi-Oh-Console` exports `WriteLogTo(file, message, module, level)` and `FlushLogTo(file)`. A plugin that wants its own log names it (`"Duels"` becomes `Duels.log`), and those lines never go to the console window or `console.log`. Split logs are appended to (history is kept across runs), and each run starts with a `session <date>` line. Use `YGO::LogTo` / `YGO::FlushLog` from `Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Log.h`, or resolve the exports yourself (`Yu-Gi-Oh-MP/Logger.cpp` does).

The MP plugin still logs its basic lines to the console (stub hits, "Duel 1 started (Campaign)", "Duel ended ..."). The per-event duel stream goes only to `Duels.log`.

## Duels.log format

Normal console line format; the message after `] ` is `TAG key=value ...` (values with spaces are quoted). Written by `DuelRecorder.cpp` from stub taps in `EngineHooks.cpp`:

| tag | from | fields |
|---|---|---|
| `DUEL_BEGIN` | after `YGO::DUEL::Engine_Init` | id, date, mode (Campaign/Challenge/BattlePack/Online/Free), online, tag, match (Ranked/Friendly), rounds, localSeat, localSide, startSide, rng (PlayerState.field_3768) |
| `SEAT` | `LiveSeats__SetName` (online) | seat, name |
| `CARDS` | PlayerState at duel start | side, zone (deck/extra/hand), cards = `slot:id,...` (real ids, custom cards resolved via Yu-Gi-Oh-MoreCards) |
| `MSG` | `Duel__MsgQueue__PumpAndMirror` pops it (execution order) | n, code, name (`DuelMsgCode`), side, a1-a3 (full 32 bits: `HighWordPrefix` 0x6D entries are folded into the next message), lp (both sides, as the message starts - before its handler changes LP) |
| `MOVE` | `YGO__UI__OnCardMove` | slot, kind, from, to (location = zone<<1 \| side \| index<<6) |
| `LP` | after `LP_ApplyDamage_Internal` / `LP_ApplyGain_Internal` | side, kind, amount, lp (informational; DuelIt applies LP from MSG 0x23/0x24/0x25) |
| `CHAIN` | `YGO__UI__OnChainResolve` | side, len |
| `START` | `Set_StartingPlayer` during a duel | side |
| `NET_SEND` / `NET_RECV` | `Duel__NetEvent__Send` / `Receive` (online only) | seq, type, payload / data (hex) |
| `ROUND`, `STAT`, `DUEL_END`, `MATCH_END` | `Set_RoundResult`, `UpdateSaveStat`, `OnDuelEnd`, `FinishAndUpdateSave` | round/outcome, stat/delta, outcome/winner/reason/lp/events, dp |

`Duel__MsgQueue__Push` is deliberately not hooked: the exe is link-time optimised and Push's ~2500 callers keep live values in rdx/r8/r9 across the call (Push never writes them), so a C++ detour on it broke the End Phase (the AI's turn never ended). Every other stub was checked for that (docs/MultiplayerSystem.md, "Header correction and logging stubs").

LP is stored XORed with a **16-bit** key (`Duel_Engine.LpXorKey`, 0x143330280): LP = PlayerState u32 ^ key16. Early recordings (before 2026-09-30 evening) read the key as 32 bits, so their `lp=` values are garbage like 364650304.

The engine doesn't send `TurnStart` (0x02) in practice: a turn is `EndOfTurn` (0x03) -> `NextTurn` (0x04) -> `Phase_Draw` (0x0A), so DuelIt counts turns at Draw Phases.

Zones: 0-4 monster, 5-6 extra monster, 7-11 spell/trap, 12 field (0-12 provisional), 13 hand, 14 extra deck, 15 deck, 16 graveyard, 17 banished. Winner: 1 = local side, 2 = other side, 3 = draw.

## DuelIt

- Opens `<game folder>\Duels.log` on start (the game folder is shared with WolfX's setting), or `DuelIt <file>`, or File > Open. F5 reloads, so it can be refreshed while the game is running (the log is flushed at each duel end).
- Pick a duel (newest selected). Left/Right step, Home/End, Space play/pause, speed 0.5x-10x.
- "Step key events" (on by default) steps and plays only through moves, LP, turns, phases, chains and results; the list still shows every line, with the rest greyed.
- Cards are drawn like Yu-Gi-Oh-AnimeCards draws them in game (CardPainter.cs, positions from docs/CardRendering.md): the game's frame for the card's kind (`duel\frame\card_*.png`; the Mods\Anime Frames versions when they're installed loose, as in game), the illustration filling the art window, the attribute icon (`pdui\STEAM_icons` ICON_ID_ATTR_*) in the frame's circle, level/rank stars centred in the band, ATK/DEF in the boxes. Illustrations come straight out of YGO_2020.dat, one picture at a time: `2020.full.illust_j.jpg.zib` (every card, the censored art the game shows); custom cards use their own art from `Yu-Gi-Oh-Ex\cards.json` (624x624 illustrations, framed the same way). The card back is `card\0000.png`. **View > Uncensored card art** prefers `2020.full.illust_a.jpg.zib` (only the cards whose art differs) and falls back to the censored set; it's remembered.
- Hover any card on the board (or a MOVE / activation line in the event list) for the hover card: big art, name, type/level/attribute, ATK/DEF, card text, where it is. Hover a pile (graveyard, deck, extra deck, banished) to list its cards, top first. Card data comes from the game's `bin\CARD_Prop.bin` / `CARD_Indx_E` / `CARD_Name_E` / `CARD_Desc_E` (GameData.cs) and cards.json.
- The board highlights the card that just moved. If a `MOVE` says a card came from somewhere the replay doesn't have it, that's counted and shown: it means something moved cards without an `OnCardMove`, i.e. a gap in what's recorded, worth tracing.

It's a presentation of what was recorded, not a rules simulation. A full re-simulation would need the engine itself: the record already holds what that needs (both decks, the RNG seed, the ordered message stream with shuffle seeds).
