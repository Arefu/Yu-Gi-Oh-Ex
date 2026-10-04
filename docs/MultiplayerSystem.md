# The game's own online-duel system (found 2026-09-30, IDA)

Legacy of the Duelist: Link Evolution's online duel system is **live on PC**: it is the game's own Steam multiplayer mode (Main Menu, Multiplayer page). **Correction (2026-09-30, later the same day):** an earlier version of this doc said "no UI path ever sets `g_bIsDuelMultiplayer`". That was wrong. `RIX::ScreenMainMenu::ActivateItem` items 4/5 and `RIX::ScreenLiveLobby::OnEnter` set it, and the transport underneath is Steam lobbies + Steam P2P (`QNet`). See "Steam transport (QNet) and LiveManager" below. This is the system `Yu-Gi-Oh-MP` (see `ygo-mp-lobby-plan` memory) plans to drive directly instead of building duel-sync from scratch, and **any hook on it can be tested through the real Multiplayer menu with two Steam accounts**.

## Steam transport (QNet) and LiveManager - fully mapped 2026-09-30

All names, types (`QNet`, `RIX_LiveManager`, `LiveSeat`, `LiveSeatBlock`, `LiveDeckBlob`, `LiveHostContext`, `QNetLobbyEntry`, `QNetLobbyKey`, enum `NetDataType`) and comments are in the IDB.

### Layers, bottom to top

```
Steam (steam_api64)  ISteamNetworking005 (classic P2P, channel 0)  +  ISteamMatchmaking009 (lobbies)
  QNet  (embedded in LiveManager at +0x60; RTTI .?AVQNet@@ / base .?AVQNetBase@@)
    QNet__SendP2PToPeer        SendP2PPacket to ONE peer (QNet.PeerSteamID) - 1v1 only over P2P
    QNet__PumpP2PReceive       RunCallbacks + ReadP2PPacket loop -> QNetBase__EnqueueReceivedPacket(seat from SteamID)
    lobby: CreateLobby / JoinLobby / LeaveLobbyAndCloseP2P / RequestLobbyList / GetLobbyListResults / GetLobbyMemberList
  RIX_LiveManager (g_pLiveManager 0x142924138, size 0x7F0)
    network thread: RIX::App::Initialize starts NetThreadEntry -> NetThreadTick loop (Sleep 1ms), ALWAYS running
      ProcessPendingCommand -> HeartbeatTick -> TransmitDeckInfo -> LiveIndexInfo -> FlushOutgoingAndRouteIncoming -> PumpP2PReceive
    WriteChannel(ch)  -> outgoing list (cap 1024)      ReadChannel(ch) <- per-channel inbound lists (cap 1024)
  Game callers: Duel__LiveManager_TransportSend/Receive (ch 2 lobby), Duel__NetEvent__Send/Receive (ch 3 duel events)
  UI: ScreenMainMenu (MP page) -> ScreenLiveMenu / LiveSetting / LiveLobby / LiveSession / LiveLoading / LiveLeaderboard / InviteLanding
```

### Wire format (one Steam P2P packet = one of these)

6-byte header then payload, built by `WriteChannel` / the heartbeat and deck senders, validated in `FlushOutgoingAndRouteIncoming`:

| byte | meaning |
|---|---|
| 0 | `NetDataType` kind |
| 1 | `(u8)ComputeInternalCRC32(payload)` (packet dropped if it mismatches) |
| 2-3 | `u16` payload length (must equal received size - 6) |
| 4 | seat route mask (15 = everyone; ignored by `SendP2PToPeer`, which only has one peer) |
| 5 | `g_LiveManagerPacketSeq++` (u8, not checked on receive) |

`NetDataType` (official names from `NetDataType_ToString`, 0x14080B880):

| value | name | payload | handling |
|---|---|---|---|
| 0x10-0x17 | `Bin0`-`Bin7` | opaque | queued per channel (`Bin2` = lobby/coin-toss, `Bin3` = duel event bus) |
| 0x80 | `NetHostContext` | 24B `LiveHostContext` {State, AvatarId, ?, MatchSettings[12]} | host -> clients every 1.5 s, unreliable; applied on clients only |
| 0x81 | `NetClientContext` | 8B `LiveNetContext` {State, AvatarId} | client -> host every 1.5 s, unreliable |
| 0x82 | `LiveIndexInfo` | 4 x 8B seat SteamIDs | host -> clients, reliable; seat assignment |
| 0xC0-0xC3 | `Deck0`-`Deck3` | 260B `LiveDeckBlob` {u32 0x201, u32 DuelistId, 252B DeckListItem} | each client sends its OWN deck, reliable |
| 0xC4 | `Chat` | - | **no receive handler: chat is dead on PC** |
| 0xC5 | `RequestLiveIndexInfo` | - | host re-sends 0x82 |

### Lobby (Steam matchmaking)

- Lobby data keys (`g_QNetLobbyKeys`): `type` (match type, `g_LiveMatchType`), `match`, `time`, `lp`, each an int, used for publishing, search filters (numerical ==) and reading. The host also writes `hostxuid` (its SteamID; Xbox-era name), `hostavatar` and `owner` (persona name). Each member sets member data `avatar`.
- `CreateLobby` uses max members **2** and a lobby type from settings (invisible / private / friends-only / public). `JoinLobby` first sends the host a 1-byte P2P packet to open the session, then calls `JoinLobby`. The host locks the lobby (`SetLobbyJoinable(false)` + private) when the match starts.
- `LiveCommandSession` kinds (`QNetBase__RunSessionCommand`): 1 CreateGame, 2 **RandomGame = stub that returns success (quick-match is not implemented on PC)**, 3 Join, 4 member list, 5 Leave. `LiveCommandFindSession` = lobby browser.
- Seats: `g_LiveSeats[4]` {wstring Name, u64 SteamID, int AvatarId}; host broadcasts the SteamID order with `LiveIndexInfo`; `g_bIsLiveHost` = (my SteamID == lobby owner).

### Duel setup over the network

`YGO__DuelSetup__AssignSeatDecksAndDuelists` (0x14081E530, runs before `SetupDuelSettings`) is the per-seat duelist+deck assignment for **every** duel mode. In MP, the local seat uses its own save deck and the remote seat uses the received `LiveDeckBlob`; the only check is `Magic == 0x201`, otherwise `RIX__LiveSession__AbortWithError`.

### Anti-cheat / tournament relevance (all confirmed in code)

1. **P2P session accepted from anyone.** `QNet__OnP2PSessionRequest_AcceptAnyone` calls `AcceptP2PSessionWithUser` with no lobby check. `PumpP2PReceive` reads packets from any sender, and `QNetBase__DequeueReceivedPacket` does not drop sender seat -1. A stranger who knows your SteamID can inject `Bin3` duel events or `Deck`/context packets mid-match. A tournament client must filter these by lobby membership / `PeerSteamID`.
2. **Decks are self-reported and unvalidated.** The server must hold the registered deck and compare it (hook `TransmitDeckInfo` / the `Deck` receive route, or have both clients report the blob to our server).
3. The per-packet check is only a 1-byte CRC. Integrity against tampering has to come from our own layer (EventReport stream + server-side replay).
4. Custom cards: `LiveDeckBlob` carries real ids (possibly above 0x3FFF). Both clients need the same Cards.json, and the Yu-Gi-Oh-Cards `Duel_LoadDeck` id borrowing must produce identical engine ids on both sides, or the lockstep will desync.

## The chain

```
RIX::ScreenExGameDuel::OnDispatch   (per-frame duel-screen tick)
  -> Duel__LiveManager_TransportReceive   -> g_NetIncomingQueueBuf/Size
  -> Duel__NetSession__Update
       - Duel__NetQueue__DequeueIncoming  (drains the incoming queue)
       - walks messages: byte[2]=type, byte[3]=length
           type 1/2/3-6 -> sub_1407B4F50 (NOT YET DECODED)
           type 7        -> Duel__NetMsg_Type7_MarkSeatReady
       - session state machine: 1=ready-check, 2=YGO::DUEL::Engine_Init(), 3=running duel
  -> (if outgoing bytes queued) Duel__LiveManager_TransportSend
```

- **`Duel__NetQueue__EnqueueOutgoing`** (`sub_1407BE5A0`) is the send-side API the engine calls. If `g_bIsDuelMultiplayer`, it queues to the outgoing buffer (flushed by `TransportSend`). If NOT multiplayer, it appends straight into the SAME incoming queue instead - single-player duels loop a "sent" message back to "received" and use the identical message-dispatch/session-state-machine to set themselves up. This is why the local and future-network paths should be low-risk to bridge: they already share almost all their code.
- **`Duel__LiveManager_TransportReceive`/`Send`** (`sub_14081E010`/`sub_14081E060`) are the actual platform transport, reached via `YGOInstance::Get_LiveManager()`. A `databuffer_LiveManager_SteamInterface` type exists in this binary - unconfirmed whether it's reachable/functional. **This is the recommended hook point**: two small, narrow functions to detour, below which the entire original Konami online-duel protocol (message framing, ready-check, `Engine_Init`, gameplay) runs unmodified.

## Per-seat controller type (separate finding, same session)

`YGO::DUEL::Engine_Init` assigns every duel seat (`Get_NumberOfPlayers()`: 2 normal, 4 for `Get_IsTagDuel()`) a **ControllerType** stored in `Duel_PlayerRecords[seat]` (offset +4): `0` = local human (`seat == g_LocalPlayerSeat`), `1` = AI, `Get_IsDuelMultiplayer()+1` = `2` for a non-local seat when multiplayer is on. `g_LocalPlayerSeat` is set by `YGO::DUEL::Set_LocalPlayerSeat`, called from `YGO::DuelSetup::SetupDuelSettings`'s multiplayer branch (reads a live-match-settings struct - seat, tag/round-based, starting LP, time limit) or randomly for local play.

## The blob decoded (2026-09-30, continued) - it's the LOBBY protocol, not gameplay sync

Traced message types 1-6's handler (`Duel__NetLobby__HandleMessage_Type1to6`, was `sub_1407B4F50`) and their senders. Header per message: `byte[0]`=seat/index, `byte[2]`=type, `byte[3]`=length, `byte[4]`=payload value.

- **Type 1**: "my seat is now in lobby phase N" (value stored per seat at lobbyCtx+3660+12*seat). Sent by `Duel__NetLobby__SendType1_SeatPhase` (was misnamed `SendType5_SimpleValue`).
- **Type 2**: per-seat "arrived" flag (lobbyCtx+3664+12*seat). Together with type 1 this is a **phase barrier**: `Duel__NetLobby__PhaseBarrier_SendType2(ctx, phase)` (was misnamed `SendType4_ReadyRetry`) returns 0 until every seat reports `phase`, resends type 2 every 30 frames, and returns 1 once every seat has arrived.
- **Type 3/4**: a **rock-paper-scissors / coin-toss CALL exchange** - each side's pick, compared against local seat parity to tell own vs opponent's call; type 4 also sets a "confirmed" flag. `Duel__NetLobby__CoinToss_Update` (was `sub_1407B4D20`) cycles the pick `%3` (three choices - genuinely RPS, not a coin flip) and sends a `rand()%2` fallback. **Correction (2026-09-30 param pass):** what it actually sends is type 6 (header word 0x06FF) on timeout and type 5 (0x05FF) every 10 ticks; the type 3/4 senders have not been located.
- **Type 5/6**: broadcasts/finalizes the toss result (who goes first).
- **Type 7**: (found earlier) marks a seat "ready."

**Conclusion: this whole type 1-7 system is the pre-duel lobby/ready-check/coin-toss negotiation protocol, confirmed by two full waiting-room state machines built around it** (`Duel__NetLobby__CoinToss_Update`, `Duel__NetLobby__WaitForAllReady_Update` - both have real timeouts in milliseconds via a tick-counter function, retry sends, and a final "now launch the duel" callback that leads into `Engine_Init`).

**It is NOT confirmed to carry in-duel gameplay actions** (Normal Summon, attack, chain activation, etc.) - `Duel__NetSession__Update`'s dispatch loop only accepts types 1-7; anything else stops parsing the buffer entirely. If real card-by-card action sync exists in this build, it either uses a different `LiveManager` channel (`TransportReceive`/`Send` hardcode channel index `2` for this lobby traffic - other channels aren't traced) or a different mechanism (e.g. full authoritative-state snapshotting each turn rather than discrete actions) that hasn't been located. This is now the open question, not the lobby protocol.

## The REAL in-duel action sync, found (2026-09-30, continued) - channel 3, not channel 2

Channel 2 (`Duel__LiveManager_TransportReceive`/`Send`) is lobby-only. Channel 3 is a completely separate, much richer system, and it's the real one:

```
Duel__MasterTick_GatedByNetEvent          (the master per-tick duel PHASE driver)
  calls Duel_PhaseStepFunctionTable[phase]  - draw/standby/main/battle/end etc (table not enumerated)
  GATED: only runs this tick's phase step if field_37B0==0 (not network-waiting)
         OR Duel__NetEvent__PumpOne() returns nonzero ("got an event, proceed")
       -> Duel__NetEvent__PumpOne
            - Duel__NetEvent__Receive (LiveManager channel 3) into Duel_NetEventBuffer
            - dispatches on event TYPE (word, 34 values: 0-0x21), each one directly writes into
              EventRecord (0x14349C560) / ResolutionStepState (0x14349C580) / Duel_DuelEngine /
              stru_143497C40 - i.e. this IS the action-injection point, using the exact structs
              docs/EffectSystem.md's earlier sessions already mapped in full.
            - if nothing received: returns "proceed" when not multiplayer or already done, else
              "keep waiting" - same local-loopback pattern as the lobby channel.

Duel__NetEvent__Send(payload, length)       (LiveManager channel 3, with ROUTING: broadcast/
  <- Duel__NetEvent__SendSimple(type, ...)     targeted-seat/all-but-sender, a per-sender sequence
  <- 30+ other call sites across the whole      counter byte_143327BBA, local seat)
     engine (0x1400Exxxx - 0x14049xxxx)
```

**This is a real lockstep architecture: every tick of actual duel progression is gated on consuming one event from this channel, local or networked.** Single-player duels use the identical mechanism, just looped back to themselves (same pattern channel 2 uses). A sample of the 34 event types, decoded from `Duel__NetEvent__PumpOne`'s switch:

| type | effect |
|---|---|
| 0x13/0x14 | writes straight into `EventRecord` + resets `ResolutionStepState` - direct action injection |
| 0xC/0xF | calls `sub_140063840` - supplies an answer to a pending player prompt (sibling of `Slot4_ActivationPrompt_Driver`, docs/EffectSystem.md §12) |
| 8 | payload word 0/1/2 = generic resolution-scope end/begin/nest marker (`YGO::Effects::Resolution_BeginScope` on 1); **not** an LP or value carrier despite being sent from inside `LP_ApplyChangeFromParamTable` (0x1400A1A40) - that send is just the scope-end for the LP-change resolution step, unconditional of the actual delta |
| **7** | **the LP-change/damage animation cue (closest thing to "LP changed" in the whole table) - see dedicated section below** |
| 0x16 | builds an action record (`sub_1400AA440`) then checks `YGO::Effects::Get_Slot0Handler_Resolved` |
| 2/3 | a "duel format" animation trigger (`Draw_DuelAnimationFromId`) |
| the rest | mostly small per-field state updates (chain/scope counters, deck-position sync) - not individually decoded |

### All 34 event types (decoded 2026-09-30, enum `DuelNetEventType` in the IDB)

Found by scripting every send site (about 250 `Duel__NetEvent__SendSimple`/`Send4` calls; the type is the first argument) and reading each type's single sender plus the receive handler (`Duel__NetEvent__PumpOne`).

**The model:** one client **simulates** the duel (the rules engine runs there), and the other **mirrors** it. The simulating client streams every engine message (`g_DuelMsgQueue`, see below) as type 5, and the mirror executes it and acknowledges with type 6. When the simulator needs a decision or hidden information from the other player, it sends a *request* type, and the other client, running `Duel__Net__ServeRemoteTurnRequests`, answers with the matching *result* type. Most of the 200+ effect state machines only send type 8 (scope markers) and type 9 (selection lists).

| type | name | payload / effect |
|---|---|---|
| 0, 0x21 | noop | |
| 1 | abort | unknown type / desync, marks the stream done |
| 2 / 3 | duel over notify / ack | from `Duel__EndHandshake`; receiver replies 3 and plays anim 0x37 |
| 4 | turn player sync | receiver copies the turn player into the "active" slot |
| **5 / 6** | **engine message / ack** | 8-byte `DuelMsg` from `Duel__MsgQueue__PumpAndMirror`, replayed by the mirror |
| 7 | animation cue | LP change animation (section below) |
| 8 | resolution scope | 0 end / 1 begin (`Resolution_BeginScope`) / 2 nest |
| 9 / 0xA | selection list | `{u8, u32}[n]` chosen cards; 0xA also resets action state |
| 0xB / 0xE | prompt pass | |
| 0xC / 0xF | prompt answer | no chain / in chain; three args to `sub_140063840` |
| 0xD | command chosen | index of the idle command picked (`Duel__IdleCommand__StateMachine`) |
| 0x10 / 0x11 | set action record / selected index | 28-byte action records at `stru_14349B580+1792` |
| 0x12 | register action candidate | 12 bytes, fed to `Duel__Action__RegisterCandidate` |
| 0x13 / 0x14 / 0x15 | EventRecord start / update / handler done | full `EventRecord` copy (0x13 also resets `ResolutionStepState`). **This is where card effects start on the mirror.** |
| 0x16 / 0x17 | target select request / result | result = 1-2 action records |
| 0x18 / 0x19 | effect check request / result | effect table `FunctionFive` |
| 0x1A / 0x1B | cost request / result | `Check_Activation_Cost_And_Register` |
| 0x1C / 0x1D | remote prompt request / answer | 6 bytes / 4 bytes |
| 0x1E / 0x1F | list prompt request / answer | 10 bytes / 16 bytes |
| 0x20 | surrender / forced result | `Duel__DeclareLoss` (engine message 34, reason 5) |

`Duel_NetEventBuffer+142` counts outstanding requests (incremented on send of the request/stream types, decremented on receive).

### The engine message queue (`g_DuelMsgQueue`, 0x14332FA40): best replay source

The rules engine talks to the presentation layer through a queue of 8-byte messages `{u16 code | side<<15, u16 arg1, arg2, arg3}` (up to 256 entries, similar to YGOPro's `MSG_*`), pushed by `Duel__MsgQueue__Push` and executed by `Duel__MsgQueue__ExecuteFront`. Code 109 is a prefix carrying the high 16 bits of the next message's args. Seen so far: 5 = duel result (winner + per-side `DuelWinReason`), 9 = turn change, 34 = declare loss, 3/4/100. **This runs in every duel, offline included.** It's the natural "chess notation" for the replay/debug log, and it's literally what the game already sends to the opponent.

**Replay needs:** both decks (the `LiveDeckBlob`s), match settings, starting player, the ordered message stream (or the NetEvent stream), and the **RNG seed**. The engine advances `PlayerState.field_3768` every tick with the MSVC `rand()` LCG (`x*214013+2531011`), so the initial value must be captured at duel start.

### LP change / damage: type 7, confirmed 2026-09-30

**There is no event type that carries LP as authoritative state.** Both clients independently and deterministically recompute the same LP delta from the synced action/duel-state stream (other event types) - LP itself is never put on the wire as a number to be trusted.

The closest thing is **event type 7**, sent by `Duel__NetEvent__QueueAnimationTrigger` (0x140100B10) - but it is a **display-only animation cue**, not a state-sync mechanism:

- **Sender**: `YGO__Effects__LP_ApplyDamage_Internal` (0x14009E4C0) calls `QueueAnimationTrigger(code=16, param)`; `YGO__Effects__LP_ApplyGain_Internal` (0x1400A15F0) calls `QueueAnimationTrigger(code=17, param)` - both *after* already mutating the local authoritative LP total (`stru_143497C40.field_0`, i.e. `Duel::PlayerState`, XOR-obfuscated with `Duel_DuelEngine.field_0`).
- `QueueAnimationTrigger` only forwards over the wire (`Duel__NetEvent__SendSimple(type=7, ...)`) when the *current active player's seat* has `ControllerType==2` (a remote/network-controlled seat) - i.e. the client that's actually simulating this turn tells the other side (which is just watching) what animation/number to play.
- **Payload (6 bytes)**: `byte[0..1]` = animation code `u16` (16=damage, 17=gain), `byte[2..5]` = param `u32` where **low 16 bits = the LP delta, saturated/clamped to 0xFFFF** (confirmed via disasm at 0x14009f0b0: `cmp r15d,0xFFFF` / `cmovg eax,ecx` / `movzx eax,ax` before OR'ing into the low word), **high 16 bits = packed zone/parity flags** for the animation (not itself an LP value).
- **Receive side** (`Duel__NetEvent__PumpOne`, 0x14000EA80, case 7): just calls `QueueAnimationTrigger` again locally to re-queue the same animation for display. It does **not** call `LP_ApplyDamage_Internal`/`LP_ApplyGain_Internal` - the receiving client's own LP total is untouched by this event. LP state consistency across clients relies entirely on deterministic replay of the synced action stream, not on this event.

**Practical implication for server-side LP tracking**: type 7 will usually appear on the wire close to an LP change and its low-16 param bits are a decent *hint* of the delta magnitude (saturated at 65535, and only sent one-directionally toward whichever seat is the passive/remote observer for that turn) - but it is not a reliable, complete, or bidirectional LP-change feed. To track LP authoritatively server-side, the right approach is to replay/derive it from the actual synced actions (same as the real clients do), not to sniff type 7.

**What this means for `Yu-Gi-Oh-MP`:** the real hook point (Hook Point C, supersedes the raw-transport Hook Point A) is `Duel__NetEvent__Send`/`Duel__NetEvent__Receive` (or `Duel__NetEvent__PumpOne` one level up) - not the channel-2 transport functions. The sequence-number field we already designed into our own `P2P::Action` envelope maps directly onto the game's own `byte_143327BBA` counter here.

## Bonus lead: Steam Matchmaking/Networking interfaces exist in this binary

Found while looking for something else: `STEAM::Internal::CreateSteamMatchmakingInterface` (0x14080C010) and `CreateSteamNetworkingInterface` (0x1408D9250) both exist, alongside the earlier-found `databuffer_LiveManager_SteamInterface` type. Not confirmed reachable/live, but this is now three independent signals that `LiveManager`'s channel 2/3 transport may already have a working Steam backend in this PC build - worth checking before assuming a custom relay needs to carry the duel bytes too (it may only be needed for lobby discovery/matchmaking, with Steam doing the actual transport).

## Tag duel (investigated 2026-09-30, agent pass)

**Confirmed: a tag duel's two teams are NOT 4 independent duelists with independent state. They are 2 shared team game-states, paired by seat parity, wearing 4 seats of UI dressing.**

- `Get_NumberOfPlayers()` (0x140768DF0) returns 4 when `g_bIsTagDuel`, else 2 - but this size only ever governs `Duel_PlayerRecords` (per-seat `ControllerType`/ready-flags/UI slots, stride 0x4820) in `Engine_Init`'s per-seat loop. It does **not** mean 4 independent `Duel::PlayerState` blocks exist.
- **`Duel::PlayerState`** (`stru_143497C40`) is indexed by **`seat & 1` (team parity) everywhere checked**, never by a full 0-3 seat index or any `+0xD94*2`/`*3`-style third/fourth block:
  - `Engine_Init` (0x1407BC4C0): the per-player card-count init loop uses `869 * (StartingPlayer & 1)`.
  - `YGO::DUELUI::FrontDeckBlock_Load` (0x140082580): every loaded card is written into `stru_143497C40.field_1B52 + 2*v14`, where `v14` derives from `a1 & 1` only.
  - The duel-start overlay builder (0x1407AA470) loops exactly twice, toggling `StartingPlayer &= 1` between iterations (2 iterations = 2 team banks, not 4 seats).
  - **Conclusion**: team pairing is **{seat0, seat2} share the parity-0 `PlayerState` block, {seat1, seat3} share the parity-1 block.** A tag team's two duelists literally share one hand/field/deck-pool in this engine's model - not two independent hands merged only at the shared-zone level, as real tabletop tag rules would imply. Not exhaustively proven across all ~49 functions touching the struct, but every function checked agrees; flagged as high-confidence, not 100%.
- **`YGO::DUEL::DuelSetup_LoadBothDecks`** (0x14005FED0) loads exactly 2 front-end deck blocks (`StartingPlayer`, `1-StartingPlayer`) - now explained: because state is shared per team, only 2 deck blocks are needed, not 4. A `StartingPlayer >= 4` branch (also present in `FrontDeckBlock_Load`, selecting a different `Duel_UNK` counter bank) turned out to be **dead code in the normal call path**: `FrontDeckBlock_Load`'s only real caller is `DuelSetup_LoadBothDecks`, which never passes `>=4`, and `YGO::DUEL::Set_StartingPlayer` (0x140769740) is only ever called from the coin-toss "resolve starting player" UI state (`Duel__Lobby__ResolveStartingPlayer_State0xB`, was `sub_1407B4100`), which computes the value as `LocalPlayerSeat & 1` or its complement - always 0 or 1. The `>=4` branch (and the matching `Duel_UNK` bank it selects, a cosmetic/display-only hand/deck/extra-count tracker, not the real card pool) looks like vestigial support for a genuine 4-duelist/2-decks-per-team variant (console build?) that isn't reachable on PC.
- **Not chased**: exactly how within-team turn order alternates (`Duel_DuelEngine.field_4`, the "local seat parity" field, is read in hundreds of functions across the whole duel engine - too broad to isolate a tag-specific turn-advance rule in this pass); whether the 4 `ControllerType` seats are ever read back individually during actual gameplay input polling (still an open question from the main netcode investigation too). Light `entity_query` string search found `bin/tagdata.bin`/`bin/taginfo_#.bin` (asset files, unexplored) and confirmed Steam stat names `SAVESTAT_GAMES_MULTIPLAYER_TAG`/`SAVESTAT_WINS_MULTIPLAYER_TAG` exist, consistent with tag duel being a real, tracked mode - no dedicated "switch to partner" UI string was found.

### Partner decks and the turn-change swap (2026-09-30, later)

The "shared hand" reading above is only half the story. Each team's **active** duelist uses the side's PlayerState block, and the
**partner's** hand, deck and extra deck are parked in a front block (`Duel_UNK + 4 + 302 * side`: u16 hand, deck, extra counts, then
card slots). From turn 2, `Duel__Msg__Handle_04_NextTurn` calls `Duel_LoadEngineFromFrontBlock(side)` (0x140082960), which swaps the
parked partner in and parks the one who just played. So the engine does support separate hands per partner.

- `DuelSetup_LoadBothDecks` (tag only, from `Engine_Init`) fills the front blocks from the partner seats' decks: seat start+2 for the
  starting side, seat 3-start for the other (`Duel_DuelEngine + 0x2A + 192 * seat`), each partner starting with the same hand size.
- Seat decks come from the player records (`Duel_PlayerRecords + 0x4820 * seat + 0x40`, u16: [33] main count, [34] extra, [35] side,
  [36..95] main, [96..110] extra, [111..125] side) via `Engine_CopySeatDeckFromRecord` (0x14076AA30).
- The records are filled by `YGO__DuelSetup__AssignSeatDecksAndDuelists` for `Get_NumberOfPlayers()` seats, **before** `Engine_Init`.
  Yu-Gi-Oh-TagDuel turns tag mode on inside `Engine_Init`, so seats 2 and 3 had no deck: the first swap loaded an empty block and the
  hand and deck vanished (the duels ended in Deck-out).
- Fix (Yu-Gi-Oh-TagDuel): before `Engine_Init` it fills seats 2 and 3 (`[Yu-Gi-Oh-TagDuel] PartnerDeck` / `OpponentPartnerDeck` .ydc,
  else a copy of the teammate's deck). `DeckMode=shared` detours the swap to a no-op so partners play one hand and deck.

## Header correction and logging stubs (2026-09-30, param pass)

- Lobby message header, confirmed from the senders' stack layouts: `byte0 = seat`, `byte1 = 0xFF`, `byte2 = type`, `byte3 = length`, `byte4 = value`. IDB struct `NetLobbyMsg`. The senders build it as one `u16` (0x01FF = type 1, 0x02FF = type 2, ...), which is how the old names got the type wrong. `Duel__NetLobby__WaitForAllReady_Update`'s retry is type 7 (0x07FF), not type 4.
- All MP functions now have named/typed parameters in the IDB (session command struct `LiveSessionCommand`: +28 Status, +32 Kind).
- `Yu-Gi-Oh-MP` hooks every function in this doc and in StatsAndMatchResults.md with a passthrough logging stub (`EngineHooks.cpp`, `Stubs.h`). Each line is `[stub] Name(param=value, ...) from <caller address>`. Buffers are hex-dumped. Per-frame functions log only their first few calls, at debug level (console.log).

- **Register assumptions (hook hazard).** YuGiOh.exe is link-time optimised, so a caller may keep live values in "volatile" registers across a call when it knows the callee doesn't touch them. A C++ detour does touch them. `Duel__MsgQueue__Push` only writes rax/rcx/r10/r11 and its callers rely on rdx/r8/r9 surviving (`YGO__DUEL__CheckTurnOrTimeLimit` stores edx right after `call Push`, 0x140148034, as the End Phase sub-step). Hooking Push left that step at garbage and the End Phase never finished (the AI's turn hung). A scan of every call site of every stubbed function (reads of rcx/rdx/r8-r11 after the call before a write) found **only Push** relies on this; it is not stubbed, and DuelRecorder reads messages as `Duel__MsgQueue__PumpAndMirror` pops them. Re-check any new hot/leaf function before detouring it.
- Every stub logs its first 3 calls, then stops ("further calls not logged").
- `Duel_Engine.LpXorKey` (0x143330280) is a **u16**: LP = PlayerState u32 ^ key16.
- Turns: the engine doesn't send `TurnStart` (0x02) in a Campaign duel; a turn is `EndOfTurn` (0x03, from CheckTurnOrTimeLimit) -> `NextTurn` (0x04) -> `Phase_Draw`.

## Engine message codes (`DuelMsgCode`, 2026-09-30)

Every code handled by `Duel__MsgQueue__ExecuteFront`, as enum `DuelMsgCode` in the IDB (each member has a comment, each switch case target is labelled, handlers are named `Duel__Msg__Handle_XX_*`). Queue struct: `DuelMsgQueue` {Front, FrontArgHigh[3] @+0xA, Entries[256] @+0x10, Count @+0x810, FrontBusy, FrontStep, FrontSaved}. A message is `DuelMsg` {CodeAndSide (code | side<<15), Arg1, Arg2, Arg3}. Code 0x6D carries the high 16 bits of the next message's args.

Method: the receive switch (what each case does and which DuelAnimId it plays) cross-checked against every sender (~2500 `Push` calls, constant codes recovered with Hex-Rays) and a live offline Campaign duel. "yes" = confirmed; "?" = provisional name from the handler's behaviour only.

**Offline duels never use the channel-3 event bus** (`Duel__NetEvent__Send` never fired in a Campaign duel); this queue is the only in-duel stream there. Shuffles carry their RNG seed (0x50 hand, 0x56 deck), which a replay needs.

| code | name | | notes |
|---|---|---|---|
| 0x01 | `DuelMsg_DuelStart` | yes | plays DuelAnim_DuelStart once (front step 0) |
| 0x02 | `DuelMsg_TurnStart` | yes | clears per-turn zone flags for both sides, active player = turn player; waits for DuelAnim_TurnStart(6) |
| 0x03 | `DuelMsg_EndOfTurn` | ? | only sender YGO__DUEL__CheckTurnOrTimeLimit (arg1=1); plays anim 77, tags every graveyard/banished/extra card with flag 0x100000, then field refresh (0x27) |
| 0x04 | `DuelMsg_NextTurn` | ? | sender YGO__DUEL__CheckTurnOrTimeLimit when the duel continues; handler reloads the engine from the front block and plays anim 6 (TurnStart) |
| 0x05 | `DuelMsg_DuelResult` | yes | arg1 winner, arg2/arg3 per-side DuelWinReason; runs YGO__DUEL__ResolveDuelEnd. Sender Duel__EndHandshake |
| 0x06 | `DuelMsg_SetDuelFlagBit` | yes | sets/clears bit arg2 of PlayerState.field_1B22 (duel-global flag word) |
| 0x07 | `DuelMsg_Anim03Cue` | ? | plays anim 3 with arg1 (senders pass 15 or 30) |
| 0x08 | `DuelMsg_ShowMessage` | ? | handler plays anim 0x37 (the message/dialog anim Duel__EndHandshake also uses); 91 senders, arg1 small id (2, 10, ...) |
| 0x09 | `DuelMsg_ShowBanner` | ? | anim 0x42 with string id 396+arg1; arg1 0/1 also snapshots the turn counter (field_3784) |
| 0x0A | `DuelMsg_Phase_Draw` | yes | phase change -> Duel__Msg__Handle_PhaseChange(0). Seen live turn 1 |
| 0x0B | `DuelMsg_Phase_Standby` | yes | phase change (1). Seen live |
| 0x0C | `DuelMsg_Phase_Main1` | yes | phase change (2). Seen live |
| 0x0D | `DuelMsg_Phase_Battle` | ? | phase change (3) - order of the six phase codes |
| 0x0E | `DuelMsg_Phase_Main2` | ? | phase change (4); same sender as Main1 |
| 0x0F | `DuelMsg_Phase_End` | ? | phase change (5) |
| 0x10 | `DuelMsg_Anim34_SideValue` | ? | anim 0x34(side, arg2, arg3/arg2<<16); keeps a per-side running max at stru_14349C280+172 |
| 0x11 | `DuelMsg_ChainLinkResolve` | ? | anim 0x35 (DuelAnim_ChainResolve); arg6 indexes a 56-byte action record, arg7 packs two nibbles into it |
| 0x12 | `DuelMsg_Handler12` | ? | large attribute/type-checking handler, anims 0x14/0x56 |
| 0x13 | `DuelMsg_SetField38` | ? | stores arg1 -> PlayerState.field_38, arg2 -> field_DC2, plays anim 7 |
| 0x14 | `DuelMsg_BattleStep2` | ? | field_378C=2, field_37CA=1, anim 0xA(side) |
| 0x15 | `DuelMsg_RefreshAll15` | ? | recomputes both sides (sub_140070850/sub_1400795C0), sets flag 0x10, field_378A=3 |
| 0x16 | `DuelMsg_Handler16` | ? | big handler (1219 lines), anim 0xD, link arrows / linked zones |
| 0x17 | `DuelMsg_Anim0C_Summon` | ? | anim 0xC(side/arg2<<8, arg4, arg5); arg3==2 bumps a per-side counter at +274 (summon count?) |
| 0x18 | `DuelMsg_Anim0C_Summon2` | ? | anim 0xC(side/arg2<<8, 1-side, 7) - sibling of 0x17 |
| 0x19 | `DuelMsg_ZoneFlag19` | ? | arg2!=0: runs a card-8215 check on zone arg1; else clears monster flag 0x80 |
| 0x1A | `DuelMsg_Handler1A` | ? | anims 0xE, 0x27 |
| 0x1B | `DuelMsg_Handler1B` | ? | anim 0x27 |
| 0x1C | `DuelMsg_Handler1C` | ? | anim 0x27 |
| 0x1D | `DuelMsg_BattleStep4` | ? | field_37CA=10, field_378C=4, anim 0xE(turn player) |
| 0x1E | `DuelMsg_BattleStep7` | ? | field_37CA=7 |
| 0x1F | `DuelMsg_Handler1F` | ? | anim 0x27 |
| 0x20 | `DuelMsg_ZoneFlag20` | ? | sets monster flag 0x400 (and 0x200000 on the card id) on side/zone arg1 |
| 0x21 | `DuelMsg_SetZoneBit21` | ? | sets/clears a bit in per-side PlayerState.field_190 bitset (arg1 index, arg2 bit) |
| 0x22 | `DuelMsg_DeclareLoss` | yes | arg1 = DuelWinReason (5 = rules). Senders Duel__DeclareLoss, PumpOne (net surrender 0x20), CheckTurnOrTimeLimit. Plays anim 0x36(reason)+0xF, sets g_LastDuelWinReason and the winner |
| 0x23 | `DuelMsg_LP_Gain` | yes | sender YGO__Effects__LP_ApplyGain_Internal(amount, 4, 0); handler Duel__Msg__Handle_LPChange(1) |
| 0x24 | `DuelMsg_LP_Damage` | yes | senders YGO::DUEL::Damage_Player, LP_ApplyDamage_Internal (amount, cause, 0); handler Duel__Msg__Handle_LPChange(0) |
| 0x25 | `DuelMsg_LP_Set` | yes | seen live at duel start: (8000, 8000, 1); handler plays DuelAnim_LP_Set |
| 0x27 | `DuelMsg_CardToPileAnim` | ? | arg1 16 = graveyard / 17 = banished (pile update anim 0x18), then anim 8(side, arg1, card) |
| 0x28 | `DuelMsg_Handler28` | ? | 128 senders incl. YGO::Effects::Draw_ExecuteDraw; variable anim |
| 0x29 | `DuelMsg_SetCardBit29` | ? | sets/clears bit arg4 in the card word at side/zone arg1/arg2; archetype-32 counter |
| 0x2B | `DuelMsg_Handler2B` | ? | variable anim |
| 0x2C | `DuelMsg_Handler2C` | ? | anims 0x1A, 0x27, 0x37, 0x3A |
| 0x2D | `DuelMsg_Handler2D` | ? | anims 0x14, 0x18, 0x23, 0x37, 0x48 |
| 0x2E | `DuelMsg_ShowCardActivation` | ? | arg1 = card id (senders pass 5788, 11738...); anim 0x48 then 0x23 then the 0x37 prompt |
| 0x2F | `DuelMsg_ShowCardEffect` | ? | arg1 = card id (5231, 6538...); anims 0x23, 0x48 |
| 0x30 | `DuelMsg_Handler30` | ? | anims 0x16, 0x1A, 0x4F |
| 0x31 | `DuelMsg_Handler31` | ? | anims 0x16, 0x21 |
| 0x32 | `DuelMsg_Handler32` | ? | variable anim |
| 0x33 | `DuelMsg_Handler33` | ? | anims 0x27, 0x4B |
| 0x34 | `DuelMsg_Handler34` | ? | anims 0x27, 0x48, 0x4C |
| 0x35 | `DuelMsg_FlagMarker_Add` | ? | 509 senders; adds flag marker arg2 (1001-1004 special-cased) to side/zone arg1, value arg3/hi<<16 |
| 0x36 | `DuelMsg_FlagMarker_Remove` | ? | removes a flag marker (sub_14004A940/A7D0/A9D0); seen live during the draw phase |
| 0x37 | `DuelMsg_FlagMarker_Bulk` | ? | arg1 != 0xFFFF: sub_14004FC90 on one card; else walks all 256 card slots |
| 0x38 | `DuelMsg_RenumberCard` | ? | gives the card at side/zone arg2 a new unique id (field_376C++) and clears flag 2 |
| 0x39 | `DuelMsg_ZoneValueSetOrAdd` | ? | sets (arg3==0) or adds arg1 to the zone value at side/zone arg2 |
| 0x3A | `DuelMsg_Handler3A` | ? | anims 0x26, 0x27 |
| 0x3B | `DuelMsg_Handler3B` | ? | anims 0x27, 0x28 |
| 0x3C | `DuelMsg_Handler3C` | ? | anim 0x1A (card move), Xyz checks |
| 0x3D | `DuelMsg_Handler3D` | ? | anim 0x1B |
| 0x3E | `DuelMsg_Handler3E` | ? | anims 0x16, 0x18, 0x1A |
| 0x3F | `DuelMsg_Handler3F` | ? | anims 0x16, 0x1A |
| 0x40 | `DuelMsg_Handler40` | ? | anims 0x18, 0x1A, 0x1E |
| 0x41 | `DuelMsg_AttackDeclare` | ? | anim 0x25(attacker side/zone<<8, target side/zone<<8, pos); bumps a per-side counter at stru_14349C280+330 |
| 0x42 | `DuelMsg_ChangePosition` | ? | anim 0x24(side, zone, pos); arg3!=0 applies it via sub_14004E5E0 |
| 0x43 | `DuelMsg_Handler43` | ? | anims 0x1D, 0x27; Mark_CardUsedInDuel |
| 0x44 | `DuelMsg_Handler44` | ? | big (1031 lines), anims 0x27, 0x55 |
| 0x45 | `DuelMsg_Handler45` | ? | anims 0x16, 0x1A, 0x2D |
| 0x47 | `DuelMsg_Handler47` | ? | Mark_CardUsedInDuel, variable anim |
| 0x48 | `DuelMsg_Handler48` | ? | anim 0x2A |
| 0x49 | `DuelMsg_Anim2B_Zone` | ? | anim 0x2B(side, zone, pos) |
| 0x4A | `DuelMsg_Anim2E_Zone` | ? | anim 0x2E(side, zone, arg3/arg2<<16) |
| 0x4B | `DuelMsg_Handler4B` | ? | anims 0x18, 0x2F, 0x30 |
| 0x4C | `DuelMsg_Handler4C` | ? | anim 0x31, Link rating / level |
| 0x4D | `DuelMsg_MaterialAnimByFrame` | ? | anim 0x33 / 0x54 / 0x59 by the card's BaseFrame (10 / 12 / 18) |
| 0x4E | `DuelMsg_ChainHistoryWrite` | ? | writes two entries into a 128-slot ring at stru_14349B580+1364 |
| 0x4F | `DuelMsg_Handler4F` | ? | variable anim |
| 0x50 | `DuelMsg_HandShuffle` | yes | arg2 = RNG seed -> PlayerState.field_3768, Fisher-Yates of side's hand with the MSVC LCG, anim 0x12 |
| 0x51 | `DuelMsg_Handler51` | ? | anim 0x1E |
| 0x52 | `DuelMsg_Handler52` | ? | anims 0x1E, 0x49 |
| 0x53 | `DuelMsg_Handler53` | ? | anims 0x14, 0x19, 0x37 |
| 0x54 | `DuelMsg_Card7609Flags` | ? | card 7609/11087/11671/14222 flag handling, anim 0x14 |
| 0x55 | `DuelMsg_Anim51_Hand` | ? | anim 0x51 when side has 2+ cards in hand |
| 0x56 | `DuelMsg_DeckShuffle` | yes | arg2 = RNG seed -> field_3768, YGO::DUEL::Duel_ShuffleDeck(side), anim 0x15 |
| 0x57 | `DuelMsg_Handler57` | ? | sender YGO::Effects::Draw_ExecuteDraw(0xFFFF..); anims 0xF, 0x1A, 0x44 |
| 0x59 | `DuelMsg_CardToPile` | ? | moves a card to arg1 pile: 14 extra / 15 deck / 16 grave / 17 banished, anim 0x16 |
| 0x5A | `DuelMsg_DrawFromPile` | ? | sender YGO::Effects::Draw_ExecuteDraw(15=deck...); anim 0x17(side, pile, count) |
| 0x5B | `DuelMsg_DeckFlag4Toggle` | ? | sets/clears field_1B22 bit 4, refreshes both decks (anim 0x16 x2) |
| 0x5C | `DuelMsg_Noop5C` | yes | no-op |
| 0x5D | `DuelMsg_RefreshDecksAndGraves` | ? | sub_1400DBAE0 both sides, anim 0x16 deck+grave |
| 0x5E | `DuelMsg_Anim3D` | ? | anim 0x3D(arg1, arg2, arg3) |
| 0x5F | `DuelMsg_Anim3E` | ? | anim 0x3E(side, arg1, arg2) |
| 0x60 | `DuelMsg_Anim3F` | ? | anim 0x3F(side, arg1); arg1!=0 applies flag 84 to both sides |
| 0x61 | `DuelMsg_Anim41_DestinyBoard` | ? | anim 0x41 (DuelAnim_Finish_DestinyBoard) |
| 0x62 | `DuelMsg_Handler62` | ? | anim 0x45 (DuelAnim_TributeOrRitualSummon) |
| 0x63 | `DuelMsg_Anim4A` | ? | anim 0x4A; sender YGO::Effects::Eval_EventTriggersCardEffect_ByCardId |
| 0x64 | `DuelMsg_Anim47_Idle` | ? | anim 0x47(side, arg1); sender Duel__IdleCommand__StateMachine |
| 0x68 | `DuelMsg_Prompt68` | ? | arg2 = prompt id (140, 169-203): builds a player prompt (sub_14005E870/sub_14005F240) |
| 0x69 | `DuelMsg_Anim43` | ? | anim 0x43(side, arg1) |
| 0x6A | `DuelMsg_ActionRecordFlags` | ? | sets flags on 56-byte action record arg1 |
| 0x6B | `DuelMsg_Anim5A` | ? | anim 0x5A(arg1, arg2) |
| 0x6C | `DuelMsg_Anim5B` | ? | anim 0x5B; clears flag 0x20 on card-11289 monsters |
| 0x6D | `DuelMsg_HighWordPrefix` | yes | Duel__MsgQueue__Push inserts it before a message whose args need more than 16 bits; its args are the next message's high words |

## What's still unknown

- ~~Whether `databuffer_LiveManager_SteamInterface` is live~~ - **answered: yes, it's `QNet`, the shipping Steam transport** (section above).
- ~~Deck-list sync~~ - **answered: `NetDataType_Deck0-3`** (section above). Message types 1-6 are decoded in "The blob decoded" below.
- Whether seat 3's (and 4-seat tag's) P2P works at all: `QNet` holds a single `PeerSteamID` and `CreateLobby` caps at 2 members, so online tag duel looks unsupported by the shipping transport. Not verified against the tag UI.
- `LiveCommandSession` kinds 0 and 6 (both stubs on `QNet`), and what `ScreenLiveSession`/`ScreenLiveLoading` do state by state.
- The exact shape of the live-match-settings struct `SetupDuelSettings` reads (mutex-guarded via `sub_140806D60`/`sub_140806CA0`, a widely-shared "current match settings" object, not multiplayer-specific on its own).

## Session

IDA session `de7ff80b` on `YuGiOh.exe.i64`, all renames/comments saved. Full narrative in memory `ygo-mp-lobby-plan`. 2026-09-30 continued in session `1abcc9f6`: confirmed the LP-change event finding (type 7, see above) and completed three comments that had been cut off mid-sentence by a rate limit. Later on 2026-09-30: QNet/LiveManager/lobby/wire format mapped (section "Steam transport (QNet) and LiveManager"). **Note:** the live IDB is `Yu-Gi-Oh-Ex\YuGiOh.exe.i64` (repo root). The old Steam-folder copy was archived as stale (`C:\Users\Johnathon\IDA-Archive\YuGiOh-stale-2026-09-26\`) and must not be used.
