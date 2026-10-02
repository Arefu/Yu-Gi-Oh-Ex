#pragma once
#include <cstdint>

// What a RELAY (not peer-to-peer) architecture actually buys for anti-cheat: the server sees every action either
// side sends, so it can referee obvious protocol violations (illegal moves, once the relay knows the rules - it
// doesn't yet). What it does NOT get for free is protection against a client that just lies about its own local game
// state (reveals face-down cards to the player, auto-picks optimal moves, etc.) - that needs the CLIENT to attest to
// something the server can cross-check between both sides. This file is that attestation, plus basic tamper detection.
//
// STATUS 2026-09-30: scaffolding only, nothing sent anywhere yet (no server to send it to). ComputeStateHash in
// particular is UNVERIFIED - it hashes the Duel::PlayerState region mapped in ygo-playerstate-struct-map-2026-09-30
// (0x143497C40, one player's block 0xD94 bytes), which is real and confirmed for the ZONE ARRAYS, but whether both
// clients' copies of that memory are actually byte-identical for the same logical game state (vs. each client storing
// the opponent's hidden info differently/not at all) has NOT been checked. Do not trust this hash for anything until
// that's confirmed against two real clients.
namespace AntiCheat
{
    // Enumerates loaded modules once and remembers them as the "known" set. Call again to re-baseline after loading
    // something legitimate late; anything present at a later CheckEnvironment() that wasn't in the last Setup() call
    // is reported as newly-loaded.
    void Setup();

    // Debugger presence (IsDebuggerPresent/CheckRemoteDebuggerPresent) and modules loaded after Setup() that aren't
    // this project's own plugin DLLs or ordinary Windows/Steam system DLLs. False positives are expected (this is a
    // modded game with a whole plugin loader already legitimately injecting DLLs) - this reports, it doesn't block
    // anything; deciding what to DO about a positive is a product/policy question, not this function's job.
    bool IsEnvironmentSuspicious();

    // Hashes the current Duel::PlayerState snapshot (both players' blocks). See the UNVERIFIED warning above.
    // Returns 0 if the duel isn't active / the region reads as all-zero (nothing to hash yet).
    uint64_t ComputeStateHash();

    // Would POST { stateHash, suspicious, sequenceNumber } to <ServerUrl>/anticheat/heartbeat. Currently just logs at
    // debug level and returns - there is nothing at ServerUrl to receive this yet, and the wire format isn't final.
    void ReportHeartbeat(uint32_t sequenceNumber);
}
