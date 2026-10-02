#pragma once
#include <cstdint>
#include <functional>
#include <string>
#include <vector>

// The relay connection: one persistent WebSocket to <ServerUrl>, authenticated with Auth::Get_Token(). The server
// relays duel actions between the players in a session (and observes them - see AntiCheat.h) rather than the clients
// talking peer-to-peer; "P2P" in the file name is what the user called it, the actual transport is client-server relay
// (simpler NAT story, and the server can referee/log for anti-cheat, ygo-mp-lobby-plan memory).
//
// STATUS 2026-09-30: the engine side is now known (docs/MultiplayerSystem.md) - the game already has TWO complete,
// intact message systems: channel 2 (pre-duel lobby/coin-toss/ready-check) and channel 3 (the real in-duel action
// bus, gates every tick of duel progression, decoded down to individual event types). We do NOT need to invent a
// duel-action protocol; what we DO own is OUR OWN wire format between this plugin and the relay/server, because the
// game's bytes alone give a server nothing to validate or track (no player identity, no ordering, no event type
// visible without unpacking Konami's framing). That's what this file defines - two frame kinds on one WebSocket:
//   Action        - opaque engine bytes to relay to another seat (gameplay sync - NOT wired to the engine yet, that's
//                   the next step once channel-3 hooking is built; see Hook Point C in docs/MultiplayerSystem.md)
//   EventReport   - one decoded-enough duel event (type + raw payload) mirrored to the server for TRACKING (LP
//                   changes etc, per the user's explicit ask) - fire-and-forget telemetry, never blocks the duel,
//                   and works even in single-player (the event bus runs locally-looped regardless of multiplayer -
//                   docs/MultiplayerSystem.md), so this is also a way to record/verify solo duels for tournaments.
namespace P2P
{
    // Our envelope around one opaque blob of the game's own LOBBY transport bytes (Duel__LiveManager_TransportSend/
    // Receive, channel 2 - docs/MultiplayerSystem.md). Payload is exactly what one such call passed/received,
    // unparsed. Everything else is ours: Seat (server cross-checks against the session's authed seat, never trusted
    // from the client alone) and SequenceNumber (strictly increasing per seat per session - the server's anti-replay
    // check). No checksum: the WebSocket transport (TCP+TLS) already gives integrity; trust comes from Seat+
    // SequenceNumber, not from anything inside Payload. NOT YET WIRED to the engine (still needs Hook Point C).
    struct Action
    {
        uint32_t SequenceNumber = 0;
        uint32_t PlayerIndex = 0;      // which seat sent it (0/1, or up to 3 for a tag duel)
        std::vector<uint8_t> Payload;  // opaque: exactly one Duel__LiveManager_TransportSend/Receive call's bytes
    };

    // One duel event observed via the game's own in-duel event bus (Duel__NetEvent__Send/PumpOne, channel 3).
    // Fire-and-forget telemetry for the server to TRACK (LP damage, etc.) - never blocks or gates the duel, a lost
    // report just means the server missed one data point, nothing more. EventType is the game's own event-type word
    // (0-0x21 known range so far); Payload is everything after that word, still in the game's own encoding (most
    // individual types aren't decoded yet - see docs/MultiplayerSystem.md's table - so the server/we decode Payload
    // per-EventType as more types get understood, same as EnginePayload in Action).
    struct EventReport
    {
        uint32_t SequenceNumber = 0;
        uint32_t Seat = 0;              // local seat parity at the moment of the hook - see EngineHooks.cpp
        uint16_t EventType = 0;
        std::vector<uint8_t> Payload;
    };

    using ActionHandler = std::function<void(const Action&)>;

    // Opens the relay WebSocket for the given session (Session.h calls this, not the game directly). Requires
    // Auth::HasToken(). Returns false (logs why) on any failure - no ServerUrl, no token, connect failure.
    bool Connect(const std::string& sessionId);
    void Disconnect();
    bool IsConnected();

    // Queues an action for the relay to forward to the other seat(s) in the session. Fire-and-forget from the
    // caller's point of view. No-op (logged) if not connected.
    void SendAction(const Action& action);

    // Mirrors one observed duel event to the server. Fire-and-forget, best-effort - silently dropped (debug-level
    // log only) if not connected, since telemetry must never affect the duel itself.
    void ReportEvent(const EventReport& event);

    // Called (on whatever thread the receive loop ends up on - not decided yet, likely needs marshalling onto the
    // game's own thread before touching engine state) for every Action the relay delivers from another seat.
    // EventReports never come back down this path - they're one-way, client to server.
    void SetActionHandler(ActionHandler handler);
}
