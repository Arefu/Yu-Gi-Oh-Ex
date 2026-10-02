#include <string>

#include <json.hpp>

#include "Auth.h"
#include "Config.h"
#include "Http.h"
#include "Logger.h"
#include "Steam.h"

namespace
{
    std::string g_Token;

    std::string ToHex(const std::vector<uint8_t>& bytes)
    {
        static const char* digits = "0123456789abcdef";
        std::string text;
        text.reserve(bytes.size() * 2);
        for (uint8_t b : bytes)
        {
            text += digits[b >> 4];
            text += digits[b & 0xF];
        }
        return text;
    }
}

bool Auth::AuthenticateWithSteam()
{
    const std::string serverUrl = Config::Get_ServerUrl();
    if (serverUrl.empty())
    {
        Logger::WriteLog("No [Yu-Gi-Oh-MP] ServerUrl in Config.ini - multiplayer features are off", MODULE_NAME, 1);
        return false;
    }
    if (!Steam::Setup() || !Steam::IsAvailable())
    {
        Logger::WriteLog("No Steam identity available - non-Steam login is not built yet (see ygo-mp-lobby-plan)", MODULE_NAME, 2);
        return false;
    }

    const uint64_t steamId = Steam::Get_SteamId64();
    const std::vector<uint8_t> ticket = Steam::Get_AuthSessionTicket();
    if (steamId == 0 || ticket.empty())
    {
        Logger::WriteLog("Steam::Get_SteamId64/Get_AuthSessionTicket returned nothing - is the user actually signed into Steam?", MODULE_NAME, 2);
        return false;
    }

    // The server verifies this ticket against Steam's own ISteamUserAuth Web API before trusting steamId - it must
    // NEVER just take steamId's word for it (anyone can send any number). Not our job here, that's the service's.
    nlohmann::json request;
    request["steamId"] = std::to_string(steamId);
    request["ticket"] = ToHex(ticket);

    const nlohmann::json response = Http::PostJson(serverUrl, "/auth/steam", request);
    if (!response.is_object() || !response.contains("token") || !response["token"].is_string())
    {
        Logger::WriteLog("MP /auth/steam did not return a token - is the service running and is ServerUrl right?", MODULE_NAME, 2);
        return false;
    }

    g_Token = response["token"].get<std::string>();
    Logger::WriteLog("Authenticated with the MP service", MODULE_NAME, 0);
    return true;
}

bool Auth::HasToken()
{
    return !g_Token.empty();
}

const std::string& Auth::Get_Token()
{
    return g_Token;
}
