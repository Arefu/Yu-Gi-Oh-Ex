# Multiplayer service (YgoMp)

The server side of Yu-Gi-Oh-MP: login, lobbies, the duel relay/arbiter, duel records, stats and leaderboards.
All C# (.NET 10), in `Server/` (`Server/YgoMp.sln`). Started 2026-10-14.

**Self-host first.** One console exe + one SQLite file runs on Windows or Linux: VPS, LAN party, home server.
The Azure "near free" deployment comes later as a second host over the same code (only storage changes).

## Projects

| Project | What it is |
|---|---|
| `YgoMp.Protocol` | Wire format shared with the plugin: `Frame` (byte-identical to `EncodeFrame` in `Yu-Gi-Oh-MP/P2P.cpp`), HTTP DTOs |
| `YgoMp.Core` | No I/O: `DuelArbiter`, `LobbyService`, `AccountService` + `TokenMinter` (auth), stats (`DuelLog`, `StatExtractor`, `DuelService`), `IStore`/`IReplayStore` |
| `YgoMp.Server` | ASP.NET Core endpoints + WebSocket relay (`RelayRoom`), `AddYgoMp`/`UseYgoMp`. A library, so any host can use it |
| `YgoMp.SelfHost` | The console exe: `SqliteStore`, `FileReplayStore`, admin commands |
| `YgoMp.Tests` | xUnit: frame layout vs P2P.cpp, arbiter rules, stats extraction |

`Server/nuget.config` clears package sources down to nuget.org (a private feed on the dev machine 401s restores).

## Running a server

```
YgoMp.SelfHost                       run (default http://0.0.0.0:5080, see appsettings.json)
YgoMp.SelfHost mint [count] [name]   print claim codes (XXXX-XXXX-XXXX-XXXX)
YgoMp.SelfHost players               list players
YgoMp.SelfHost ban|unban <playerId>
```

Settings: `appsettings.json` → `YGOMP_` env vars → `--YgoMp:Key=value`. The data directory (`YgoMp:DataDirectory`, default
`data` next to the exe) holds `ygomp.db`, `replays/`, `signing.pem` (JWT key, made on first run, **back it up**) and
three hand-editable JSON files made on first run: `boards.json`, `stats.json`, `card-catalog.json`.
TLS: put it behind Caddy/nginx or set Kestrel certs. The plugin's `ServerUrl` points at it.

## Auth

The server always mints its **own** ES256 JWT (`sub` = player id, `name`, `platform`), 30 days by default. Tokens are
per server (each has its own key). Bans block new logins; an existing token keeps working until it expires
(short-lived token + refresh is a to-do).

- **Steam** (`POST /auth/steam`, what `Auth.cpp` sends): `Steam:Mode`
  - `Trust` (default): accept the steamId as-is. LAN/friends only: anyone can claim any SteamID.
  - `WebApi`: `ISteamUserAuth/AuthenticateUserTicket`. Needs the **publisher** key for AppID 1150640, i.e. Konami's. Not realistic.
  - `Off`: claim codes only.
  - To-do for public servers: **Steam OpenID** (browser login → localhost callback/device code). Free, no publisher key.
- **Claim codes** (`POST /auth/claim {code, displayName}`): the Epic/GOG/LAN path. An admin mints codes, the player
  redeems one, and that code is their login from then on (same code = same account). To-do: Epic Account Services OAuth,
  Discord OAuth, device key binding so a leaked code can't be reused elsewhere.

## Lobby + relay

`POST /lobby/create {seatCount: 2|4, cardSetHash?}` / `POST /lobby/join {lobbyCode, cardSetHash?}` return
`{sessionId, lobbyCode, seatIndex, seatCount, seed}` (Session.cpp's fields + the server-issued RNG `seed`).
Join is refused when the `cardSetHash` differs from the host's (both sides must borrow custom ids identically or lockstep desyncs).

`GET /relay/{sessionId}` (WebSocket, Bearer token) = P2P.cpp's connection. Frame kinds:

| Kind | Dir | Meaning |
|---|---|---|
| 1 Action | both | opaque engine bytes, relayed to the other seats |
| 2 EventReport | c→s | channel-3 event mirror, recorded, never relayed |
| 3 StateHash | c→s | `u32 engineEventIndex, u64 AntiCheat::ComputeStateHash` |
| 4 DuelResult | c→s | `u8 outcome (1 win/2 draw/3 aborted), u8 winnerSeat (0xFF none), u8 reason` |
| 5 ServerNotice | s→c | UTF-8 JSON (`seatConnected`, `seatDisconnected`, ...) |

### What the arbiter can and can't do

The rules engine lives in the game exe, and both clients simulate the whole duel in lockstep (MultiplayerSystem.md).
So the server **cannot** judge whether a move is legal or hide information. What it does own:

1. **Identity:** a frame's seat = the seat the token sits in; the client's seat field is overwritten.
2. **Ordering:** `Sequence` must strictly increase per seat; the relay is the single ordering point (closes the
   "anyone can inject P2P packets" hole in the shipping QNet).
3. **Inputs:** the seed (to-do: the registered deck + ban list check).
4. **Agreement:** state hashes at the same engine event index must match on every seat; the result counts only when
   every seat reports the same one. `DuelStatus`: `Agreed` / `Disputed` / `Desynced` / `Abandoned`.

It can't stop a modified client from revealing hidden cards to its own player. That would take a server-side engine.

## Records, stats, leaderboards

After the duel each seat `POST /duels/{id}/record` its DuelRecorder log (DuelRecorder.h format). `DuelService`
applies stats **once**, only for `Agreed` duels, and skips (marks `Skipped` + reason) duels with fewer than `MinEvents`
lines or past `MaxDailyDuelsVsSameOpponent` counted duels per day vs the same opponent (anti-farming).

Stat keys are `family:dimension`, kept per period `all` / `2026-10` / `2026-W42`:

- `duels:played|won|lost|drawn`, `damage:total`, `damage:max` (max), `chain:links`, `chain:longest` (max)
- per play category from MOVE kinds (`summon`, `special_summon`, `set`, `activate`, + `played` for all of them):
  `<cat>:any`, `<cat>:card=<id>`, `<cat>:kind=`, `:frame=`, `:attribute=`, `:race=`, `:level=`, `:archetype=`

MOVE kinds, decoded 2026-10-14 from 763 MOVE lines of a real Duels.log (built in: `StatsRules.DefaultMoveKinds`, an empty
`moveKinds` in stats.json uses them): 2 Hand->Monster = `summon` (Normal), 3 Hand/Extra/Deck/GY->Monster = `special_summon`,
4 Hand->S/T = `activate`, 5 Hand->Monster/S/T = `set`, 6/7/8 Monster->GY = `field_to_grave`, 9 Deck->Hand = `draw`,
10 Hand->GY = `discard`, 12 S/T->GY = `spelltrap_to_grave`, 0 = other. Summons also count as `summoned:*` and
`<cat>:from=hand|extra|deck|grave|banished`. **Gap:** activating a card that was Set flips it in place (no MOVE), so those
activations aren't counted yet.
`card-catalog.json` gives the dimensions (Konami ids only, no names: names are resolved later, outside the server) (kind, frame, attribute, race, level). Make it with
`Server/tools/Export-CardCatalog.ps1 -Out <data>\card-catalog.json -GameDir <game folder>` (CARD_Props.dll for game cards,
`Yu-Gi-Oh-Ex\cards.json` for custom ones: 10,242 cards). Archetypes/genres not exported yet.
After changing stats.json or the catalog: stop the server, `YgoMp.SelfHost rebuild-stats` recounts every saved duel.
Delete `boards.json` to get new default boards (it's only written when missing).
`boards.json` = which stat each board shows and its sort order. Add a board = add a line, no code.

`GET /boards`, `GET /boards/{id}?period=all|month|week|<key>&take=`, `GET /players/{id}/stats`, `GET /duels/{id}`, `GET /me`.

## MVP: Connect to Server (2026-10-14)

**In game:** Main menu > Multiplayer > **Connect to Server** (Yu-Gi-Oh-MP `ServerMenu.cpp`, a RIX page). Page buttons:
Connect/Disconnect, status (description = server message / last notice / error), ban list. `Live.cpp` does the work on a
worker thread, so the game never waits:

1. `GET /server/info` (reachability + name, motd, ban list)
2. sign in: `[Yu-Gi-Oh-MP] ClaimCode` if set (`/auth/claim`, with `DisplayName`), else Steam (`/auth/steam`)
3. apply the server's ban list (`BanList::UseCustom/UseOff`) until Disconnect, which puts the game's list back
4. open `GET /live` (WebSocket, Bearer), send `{"type":"hello",...}`, then **every Duels.log line** (DuelRecorder `Write` calls
   `Live::Forward`), batched on a sender thread

**Server:** `LiveHub` logs `CONNECT` / `HELLO` / `DUEL_BEGIN` / every `LP` change / `DUEL_END` / `DISCONNECT`, keeps who is
online and their live LP (`GET /online`), logs every summon/set/activation by Konami id (`SPECIAL_SUMMON Yugi: card 15542 (fusion warrior) Extra Deck -> Monster 3 [side 0]`),
and at `DUEL_END` saves the duel as `DuelStatus.Local` + replay and counts its stats. A duel left unfinished (new DUEL_BEGIN or disconnect) is saved as `Abandoned`, not counted
(`stats.json` `countLocalDuels`, default **true** for testing: turn it off on a public server, AI duels are farmable).
Server → client messages: `welcome`, `notice` (someone connected/left), `duelSaved`.

**Ban list:** `<data>/banlist.json`, the same format as `Yu-Gi-Oh-Ex\banlists\*.json` (WolfX Save as MP list). It's re-read
when the file changes; clients get it on their next connect. No file = the game's list; `{"mode":"off"}` = every card at 3.

```json
{ "name": "Server list", "forbidden": [4007], "limited": [5000], "semiLimited": [] }
```

**Testing on localhost:** run `YgoMp.SelfHost` (port 5080), set `ServerUrl=http://localhost:5080` in Config.ini (WolfX Config >
Yu-Gi-Oh-MP), and for a non-Steam account `YgoMp.SelfHost mint 1 Yugi` → `ClaimCode=`. Verified without the game: the same
calls the plugin makes (info with a custom list, claim login, /live, a streamed duel → LP logs, saved duel, boards). **Not
yet run in game.**

Known MVP limits: Cancel/Disconnect while connecting waits for the HTTP call to finish (fast on localhost); the ban list is
written from the worker thread (do it outside a duel); online duels still go through the game's own Steam P2P, not the relay.

## Roadmap

1. ✅ **Self-host core** (2026-10-14): auth (Steam Trust/WebApi, claim codes), lobbies, relay + arbiter, records,
   stats, boards, admin CLI. 11 unit tests + a local HTTP/WebSocket smoke test pass. Nothing tested with the game yet.
2. **Plugin side** (`Yu-Gi-Oh-MP`): wire channel-3 traffic through `P2P::SendAction` (Hook Point C); send StateHash
   + DuelResult frames; upload Duels.log; use the server `seed`; send `cardSetHash`; claim-code login
   (`Auth::AuthenticateWithClaim`); handle ServerNotice (P2P.cpp currently logs non-Action frames as malformed).
   **First:** confirm `ComputeStateHash` is identical on both clients (AntiCheat.h marks it UNVERIFIED). The whole desync check depends on it.
3. **Stats data:** decode MOVE kinds / summon methods in IDA → `stats.json`; WolfX card-catalog export.
4. **Hardening:** deck registration + ban-list validation, Steam OpenID, refresh tokens + ban takes effect immediately,
   rate limits, win streak / fastest-win stats (need a turn counter in DuelRecorder), Glicko-2 rating.
5. **Azure host** (`YgoMp.Hosting.Azure`): Container Apps consumption (scale to zero, sticky routing by session id),
   Table Storage store + precomputed top-N board snapshots in Blob (Table can't sort by value), Blob replays (cool tier),
   Key Vault for `signing.pem`, Bicep/azd. Optional federation: self-hosted servers trusting the main platform's keys.
6. Website (FAF-style): boards, profiles, replays in DuelIt, tournaments.
