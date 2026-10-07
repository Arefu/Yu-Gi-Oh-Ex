#pragma once
#include <cstdint>
#include <string>
#include <vector>

// Player identity for the MP service: the game already links and initialises steam_api64.dll (SteamAPI_Init, confirmed in
// IDA - see ygo-mp-lobby-plan memory), so this plugin never calls SteamAPI_Init itself and never vendors the Steamworks
// SDK. It resolves the handful of "flat" C exports steam_api64.dll ships (the same ones Unity/Unreal integrations use)
// by GetProcAddress on the module the host process already loaded, the same trick Logger.cpp uses on Yu-Gi-Oh-Console.dll.
//
// This gives player IDENTITY only (a SteamID64 and a session auth ticket the MP service can verify server-side via
// Steam's ISteamUserAuth Web API - a raw SteamID64 alone is just a number anyone could claim). All actual match/lobby/
// leaderboard DATA lives on our own service (Auth.h), never in Steam's stats/leaderboards - see ygo-mp-lobby-plan for why.
namespace Steam
{
    // Resolves the exports; safe to call more than once, cheap after the first. False if steam_api64.dll isn't loaded
    // yet or doesn't export what's needed, or the game's SteamAPI_Init hasn't run yet (everything below retries then).
    bool Setup();

    bool IsAvailable();
    uint64_t Get_SteamId64();   // 0 if unavailable

    // The session ticket to send to the MP service's /auth/steam endpoint for it to verify with Steam's Web API
    // (ISteamUserAuth::AuthenticateUserTicket). Empty if unavailable. The ticket is opaque bytes, not a string.
    std::vector<uint8_t> Get_AuthSessionTicket();

    // Steam lobby data (ISteamMatchmaking, the version the game uses). Null / false when Steam isn't ready.
    const char* GetLobbyData(uint64_t lobby, const char* key);
    bool SetLobbyData(uint64_t lobby, const char* key, const char* value);

    // Lobby members (ISteamMatchmaking) and a player's Steam name (ISteamFriends, the version the game uses). Empty / 0 when not ready.
    void SetLobbyMemberData(uint64_t lobby, const char* key, const char* value);
    const char* GetLobbyMemberData(uint64_t lobby, uint64_t member, const char* key);
    std::vector<uint64_t> LobbyMembers(uint64_t lobby);
    std::string PersonaName(uint64_t player);
}
