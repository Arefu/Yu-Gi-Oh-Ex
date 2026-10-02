#pragma once
#include <string>

// Exchanges the player's Steam identity (Steam.h) for a session token from OUR OWN service (Config's ServerUrl) - see
// ygo-mp-lobby-plan memory for why: Steam's own stats/leaderboards belong to Konami's App ID, not us, so match results,
// lobbies and rankings are our data, on our service. A non-Steam player (no steam_api64.dll, or Steam.Setup() failed)
// gets a token some other way later (docs/MultiplayerService.md, not built yet) - this plugin only does the Steam path.
namespace Auth
{
    // POSTs { steamId, ticket } (both from Steam.h) to <ServerUrl>/auth/steam and stores the returned token in memory.
    // Returns false (and logs why) if there is no ServerUrl configured, Steam identity isn't available, or the request
    // fails - all of that is expected until the service in ygo-mp-lobby-plan actually exists.
    bool AuthenticateWithSteam();

    bool HasToken();
    const std::string& Get_Token();   // empty if not authenticated
}
