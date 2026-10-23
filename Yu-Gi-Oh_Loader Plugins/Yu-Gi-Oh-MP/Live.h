#pragma once
#include <cstdint>
#include <string>

// The always-on connection to a YgoMp server (Server/, docs/MultiplayerService.md), started by the "Connect to Server" button
// (ServerMenu.cpp). Connecting, on a worker thread:
//   1. sign in: ClaimCode from Config.ini if set, else Steam (Auth.h)
//   2. GET /server/info: the server's name, message of the day and ban list; the ban list is applied (BanList.h) until Disconnect
//   3. open the /live WebSocket and say hello; from then on every Duels.log line (DuelRecorder.h: DUEL_BEGIN, CARDS, MOVE, LP,
//      CHAIN, DUEL_END, ...) is streamed to the server, which tracks live LP and saves + scores each finished duel.
// Lines are queued and sent on a sender thread, so the duel hooks never wait on the network.
namespace Live
{
    enum class State { Offline, Connecting, Connected, Failed };

    void Connect();      // no-op unless Offline / Failed
    void Disconnect();   // also puts the game's ban list back

    State GetState();

    // Text for the server page, UTF-16 for RIX. Read any time; Version() changes whenever any of them does.
    std::wstring StatusText();     // "Connected to ygo-mp as Yugi", "Can't reach http://localhost:5080", ...
    std::wstring DetailText();     // the server's message of the day / the last notice / the error in full
    std::wstring BanListText();    // "Ban list: Game list" etc
    uint32_t Version();

    // One Duels.log line (DuelRecorder calls this for every line it writes). Dropped unless Connected.
    void Forward(const std::string& line);
}
