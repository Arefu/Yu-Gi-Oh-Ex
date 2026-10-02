#pragma once
#include <cstdint>
#include <string>

// A lobby/duel session: 2 seats for a normal duel, more for a tag duel (the user wants tag duels supported - the seat
// count is just a number to the relay/lobby service, the duel-engine side of "more than 2 players" is a separate,
// IDA-dependent question, same as everything else marked open in P2P.h).
namespace Session
{
    enum class Mode
    {
        Duel1v1 = 2,
        Tag2v2 = 4,
    };

    // POSTs to <ServerUrl>/lobby/create, gets back a session id + this client's seat index, then calls P2P::Connect.
    // Returns "" on failure (logs why) - no ServerUrl, no token, service down, etc.
    std::string Create(Mode mode);

    // POSTs to <ServerUrl>/lobby/join with a code (however the lobby service ends up handing those out - not designed
    // yet), then P2P::Connect on success. Returns false on failure.
    bool Join(const std::string& lobbyCode);

    void Leave();

    bool IsActive();
    uint32_t Get_SeatIndex();   // which seat this client is - valid only while IsActive()
    uint32_t Get_SeatCount();   // 2 for a normal duel, 4 for Tag2v2
}
