#pragma once
#include <string>

// Exchanges the player's Steam identity (Steam.h) for a session token from OUR OWN service (Config's ServerUrl) - see
// ygo-mp-lobby-plan memory for why: Steam's own stats/leaderboards belong to Konami's App ID, not us, so match results,
// lobbies and rankings are our data, on our service. A non-Steam player (no steam_api64.dll, or Steam.Setup() failed)
// signs in with a claim code instead (AuthenticateWithClaim, docs/MultiplayerService.md).
namespace Auth
{
    // POSTs { steamId, ticket } (both from Steam.h) to <ServerUrl>/auth/steam and stores the returned token in memory.
    // Returns false (and logs why) if there is no ServerUrl configured, Steam identity isn't available, or the request
    // fails - all of that is expected until the service in ygo-mp-lobby-plan actually exists.
    bool AuthenticateWithSteam();

    // POSTs { code, displayName } to <ServerUrl>/auth/claim: the login for copies without Steam (a code the server's host minted
    // with `YgoMp.SelfHost mint`). The same code logs into the same account every time.
    bool AuthenticateWithClaim(const std::string& code, const std::string& displayName);

    // What the server called us (from the last successful login), UTF-8.
    const std::string& Get_DisplayName();

    // Forgets the token (Disconnect).
    void Clear();

    bool HasToken();
    const std::string& Get_Token();   // empty if not authenticated
}
