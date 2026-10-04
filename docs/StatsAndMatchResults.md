# How the game tracks stats, match results and Steam data (RE, 2026-09-30)

What the game itself counts, when, and how it reaches Steam. Written as the reference for building our own stat system: every number the game tracks is listed with the exact hook point. All names, types (`SaveStatId`, `DuelWinReason`, `DuelOutcome`, `MatchResult`, `LiveForfeitReason`, `DuelAnimId`, `DuelWinReasonText`) and comments are in the IDB (`Yu-Gi-Oh-Ex\YuGiOh.exe.i64`).

## 1. The stat store and the path to Steam

`Steam::AchievementProgress::UpdateSaveStat(statsSection, SaveStatId, delta)` (0x1407F9420) is the only writer:

1. Adds `delta` to a u64 counter in the save's stats section (`YGO::SAVE::Get_StatsSection`, 8 bytes per stat, index = `SaveStatId`).
2. Calls `ISteamUserStats::SetStat(name, newTotal)`. Steam receives **absolute totals** from the local save, not deltas.
3. Walks a 16-entry achievement table (0x140A66710, 24-byte entries: stat, threshold, achievement id) and unlocks or reports progress.

Tutorial duels are skipped entirely. There are 43 named stats (0-42, see the `SaveStatId` enum). **Stat slot 44 is used for Link summons but has no Steam name**, so it's only ever counted locally.

Leaderboards (`RIX::CSteamLeaderboard`): index 0 "Leaderboard" gets the **ranked wins total**; index 1 "Cards Earned" gets the cards-earned total. Both upload the save's value as-is.

## 2. Match lifecycle

### Modes (flags at 0x140C8D1D0 onward)

| flag | set by | meaning |
|---|---|---|
| `g_DuelMode_IsBattlePack` | main menu item 7 | Battle Pack |
| `g_DuelMode_IsCampaign` | item 1 | Campaign |
| `g_DuelMode_IsChallenge` | item 2 | Duelist Challenge |
| `YGO::DUEL::g_bIsDuelMultiplayer` | items 4/5, `ScreenLiveLobby::OnEnter` | online |
| `g_bIsLiveSession` | items 4/5 | online session active |
| `g_LiveMatchType` | item 4 -> **1 = Ranked**, item 5 -> **0 = Friendly** | confirmed by which stat `FinishAndUpdateSave` counts |
| `g_bIsRoundBasedDuel` | match settings | best of 3 |

### Result state (0x140C8D374)

- `g_MatchRoundIndex` (0..2) and round results `int[3]` at 0x140C8D378: 0 none, 1 parity-0 side won, 2 parity-1 side won, 3 draw. Written by `YGO__DUEL__Set_RoundResult`.
- `g_bOpponentLeft` (0x140C8D390): set on live error 12 (opponent left) unless we already forfeited. Counts as **our win**.
- `g_LocalForfeitReason` (0x140C8D394): 1 = quit through the pause menu (surrender), 2 = our connection was lost (live error 13). Counts as **our loss**.
- `YGO__DUEL__IsMatchOver`, `YGO__DUEL__DidSideWinMatch(parity)`, `YGO__DUEL__Get_DuelMatchResult(parity)` (1 win / 2 loss / 3 draw / 0 not over), `YGO__DUEL__ResetMatchResults`.

### End of a duel

1. Engine: `YGO__DUEL__ResolveDuelEnd` (0x140112650) writes the winner (`PlayerState.field_3792`, 3 = draw) and each side's **win reason** (`PlayerState.field_BC`, stride 0xD94) plus `g_LastDuelWinReason`, then plays the finish animation. `YGO__DUEL__CheckTurnOrTimeLimit` handles the time/turn cap (higher LP wins; equal LP or the 50-turn cap, 90 in tag, is a draw).
2. Presentation: `Draw_DuelAnimationFromId` anim 2 / `YGO__DUEL__OnDuelEnd` records the round result and shows the banner (`YGO_FRONT__ShowDuelResultBanner`).
3. Save/stats: `YGO::DUEL::FinishAndUpdateSave` (0x14087F250), see section 3.

### Win reasons (`DuelWinReason`; text from `strings/strings_steam_E.bnd`)

| # | reason | # | reason |
|---|---|---|---|
| 1 | LP reduced to 0 | 13 | Exodius the Ultimate Forbidden Lord |
| 2 | Deck-out | 14 | match-winning effect |
| 3 | Out of time / turn limit | 15 | Number 88: Gimmick Puppet of Leo |
| 4 | Surrender | 16 | Number C88: Gimmick Puppet Disaster Leo |
| 5 | Broke the rules | 17 | Jackpot 7 |
| 6 | Exodia the Forbidden One | 18 | match-winning effect |
| 7 | Destiny Board | 19 | Relay Soul |
| 8 | Yata-Garasu lock | 20 | Ghostrick Angel of Mischief |
| 9 | Last Turn | 21 | Phantasm Spiral Assault |
| 10 | Final Countdown | 22 | F.A. Winners |
| 11 | match-winning effect | 23 | Flying Elephant |
| 12 | Vennominaga | 24 | Exodia, the Legendary Defender |

Banner text ids: 765 YOU WIN, 766 YOU LOSE, 767 DRAW. The per-reason subtitle ids are in `g_DuelWinReasonText` (0x140A5EFB0). Display quirks: reason 5 is shown as 3, and reason 11 is shown as 1 when the duel isn't round-based.

## 3. What gets counted, and where

### End of duel (`FinishAndUpdateSave`, online branch)

- Wallet += DP reward.
- `GAMES_MULTIPLAYER`, then `GAMES_1V1` or `GAMES_TAG`, then one of `GAMES_BATTLEPACK_ANY` (battle pack) / `GAMES_RANKED` / `GAMES_FRIENDLY`.
- On a win (`DidSideWinMatch(local)`, or opponent left without a local forfeit): `WINS_MULTIPLAYER`, `WINS_1V1`/`WINS_TAG`, then `WINS_BATTLEPACK_ANY` / `WINS_RANKED` (+ leaderboard 0 upload) / `WINS_FRIENDLY`.
- Any mode: `WINS_MATCH` (round-based) or `WINS_NONMATCH` on a match win.
- Cards-earned total -> leaderboard 1.
- **`*_BATTLEPACK_1/2/3` are never incremented** by this function.
- Offline modes use their own helpers: campaign `sub_140880240`, challenge `sub_140881170`, battle pack `sub_14087FC60`.

### During the duel (local player only)

| stat | where |
|---|---|
| SUMMONS_NORMAL | `YGO__UI__OnCardMove` (anim 26), move kind 2 (and 5 for monsters) |
| SUMMONS_TRIBUTE / RITUAL | `Draw_DuelAnimationFromId` anim 69 |
| SUMMONS_FUSION / SYNCHRO / XYZ / PENDULUM / (Link -> slot 44) | anim 70, kind 0-1 / 2 / 4 / 5 / 6 |
| DAMAGE_EFFECT / BATTLE / REFLECT / DIRECT / ANY | `YGO__Stats__OnLifePointsChanged(side, delta, cause)`: cause 0 effect, 1 battle, 4 gain; damage *dealt* by the local player |
| CHAINS | `YGO__UI__OnChainResolve`, chain length >= 2 built by the local player |
| DECKS_CREATED | deck editor save (`RIX__ScreenDeckEditor__OnSaveDialogResult`) |
| (achievement 23) | Exodia win, unlocked directly in anim 64 |

### Per-duel detail block (not sent to Steam)

`g_DuelStatBlock` (0x140D4FF20): 3 scopes of {total[9], per-side [2][9] x3} counters (damage dealt is counter 0), plus per-seat "cards used" lists (`YGO__Stats__RecordCardUsed`). Two scopes reset per duel, and all three on entering a mode from the main menu. Probably what Score Review shows.

### Compiled-out telemetry = free hook points

Dozens of calls to empty stubs (`YGO__Stats__StrippedTelemetryStub`, `nullsub`) sit at every stat-worthy event: each summon type, card moves (sent, destroyed, banished), LP comparisons, chain resolution, turn start. They look like a detailed stat/telemetry API removed from the release build. For our stat system these are ideal hooks: the game already calls them with the context in registers at exactly the right moment.

## 4. Implications for our stat system

- Mirror the 43 counters, but key them by SteamID64 on our server. Don't trust the client's save totals; derive them from the event stream where possible.
- Record win method (`DuelWinReason`), ranked/friendly/battle-pack, round results, and disconnect vs surrender separately. The game folds disconnects into wins/losses and never records the reason.
- Link summons (slot 44) and the Battle Pack 1/2/3 splits are missing from Konami's Steam stats, so ours can do better.
- Hook points: `YGO__DUEL__ResolveDuelEnd` (result + reason), `YGO::DUEL::FinishAndUpdateSave` (final tally), `UpdateSaveStat` (every counted stat, with id and delta), and the stripped telemetry stubs (fine-grained events).
