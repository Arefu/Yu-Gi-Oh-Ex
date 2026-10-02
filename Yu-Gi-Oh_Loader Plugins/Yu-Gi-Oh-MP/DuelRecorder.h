#pragma once

#include <cstddef>
#include <cstdint>

// Writes Duels.log (a Yu-Gi-Oh-Console split log, next to console.log): one line per duel event, for DuelIt to replay.
// Fed by stub taps in EngineHooks.cpp; works for every duel, single player and online. Format (the text after "] "):
//
//   DUEL_BEGIN id=20260930-123007-1 date=2026-09-30T12:30:07 mode=Campaign online=0 tag=0 match=- rounds=0 localSeat=0 localSide=0 startSide=0 rng=0x1234
//   SEAT seat=0 name="Player"                          (online: the lobby names)
//   CARDS side=0 zone=deck cards=slot:id,slot:id,...   (zones deck/extra/hand at duel start; id = real card id, customs resolved)
//   MSG n=12 code=0x25 name=LP_Set side=0 a1=8000 a2=8000 a3=1 lp=8000/8000
//   MOVE slot=82 kind=9 from=0x1E to=0x1A              (location = zone<<1 | side | index<<6; zones as DuelIt.Zones)
//   LP side=1 kind=damage amount=1000 lp=8000/7000
//   CHAIN side=0 len=2
//   NET_SEND seq=5 type=EngineMsg payload=...          NET_RECV data=...   (online only)
//   ROUND round=0 outcome=1                            STAT stat=17 delta=1
//   DUEL_END outcome=1 winner=1 reason=1 events=345    MATCH_END
//
// Everything a replay needs that the game doesn't redo on its own is captured: both decks, the RNG seed, and the ordered
// engine message stream (shuffles carry their own seeds). docs/MultiplayerSystem.md "Engine message codes".
namespace DuelRecorder
{
    using u64 = uint64_t;

    // Stub taps (Stubs::TapFn).
    void Tap_EngineInit(const u64* args, size_t n, bool after, u64 result);
    void Tap_PumpAndMirror(const u64* args, size_t n, bool after, u64 result);
    void Tap_OnCardMove(const u64* args, size_t n, bool after, u64 result);
    void Tap_OnChainResolve(const u64* args, size_t n, bool after, u64 result);
    void Tap_LPDamage(const u64* args, size_t n, bool after, u64 result);
    void Tap_LPGain(const u64* args, size_t n, bool after, u64 result);
    void Tap_OnDuelEnd(const u64* args, size_t n, bool after, u64 result);
    void Tap_SetRoundResult(const u64* args, size_t n, bool after, u64 result);
    void Tap_FinishAndUpdateSave(const u64* args, size_t n, bool after, u64 result);
    void Tap_UpdateSaveStat(const u64* args, size_t n, bool after, u64 result);
    void Tap_SetStartingPlayer(const u64* args, size_t n, bool after, u64 result);
    void Tap_LiveSeatsSetName(const u64* args, size_t n, bool after, u64 result);
    void Tap_NetEventReceive(const u64* args, size_t n, bool after, u64 result);

    // Called by the hand-written Duel__NetEvent__Send hook.
    void OnNetSend(uint32_t sequence, uint16_t type, const uint8_t* payload, size_t size);
}
