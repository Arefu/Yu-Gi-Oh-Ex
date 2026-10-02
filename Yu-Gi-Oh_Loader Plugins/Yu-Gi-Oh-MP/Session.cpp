#include <format>

#include <json.hpp>

#include "Session.h"
#include "Auth.h"
#include "Config.h"
#include "Http.h"
#include "Logger.h"
#include "P2P.h"

namespace
{
    bool g_Active = false;
    uint32_t g_SeatIndex = 0;
    uint32_t g_SeatCount = 0;

    // Shared by Create/Join: both end with "here is a session id and your seat, now connect the relay".
    bool EnterSession(const nlohmann::json& response)
    {
        if (!response.is_object() || !response.contains("sessionId") || !response["sessionId"].is_string()
            || !response.contains("seatIndex") || !response["seatIndex"].is_number_integer()
            || !response.contains("seatCount") || !response["seatCount"].is_number_integer())
        {
            Logger::WriteLog("Session: lobby response missing sessionId/seatIndex/seatCount - is the service running this version of the protocol?", MODULE_NAME, 2);
            return false;
        }

        const std::string sessionId = response["sessionId"].get<std::string>();
        if (!P2P::Connect(sessionId))
            return false;

        g_SeatIndex = response["seatIndex"].get<uint32_t>();
        g_SeatCount = response["seatCount"].get<uint32_t>();
        g_Active = true;
        Logger::WriteLog(std::format("Session: active, seat {} of {}", g_SeatIndex, g_SeatCount), MODULE_NAME, 0);
        return true;
    }
}

std::string Session::Create(Mode mode)
{
    Logger::WriteLog(std::format("Session::Create(mode={})", static_cast<int>(mode)), MODULE_NAME, 69);

    if (IsActive())
    {
        Logger::WriteLog("Session::Create: already in a session - call Leave() first", MODULE_NAME, 2);
        return "";
    }
    if (!Auth::HasToken())
    {
        Logger::WriteLog("Session::Create: no auth token", MODULE_NAME, 2);
        return "";
    }

    nlohmann::json request;
    request["seatCount"] = static_cast<int>(mode);

    const nlohmann::json response = Http::PostJson(Config::Get_ServerUrl(), "/lobby/create", request, Auth::Get_Token());
    if (!response.is_object() || !response.contains("lobbyCode") || !response["lobbyCode"].is_string() || !EnterSession(response))
    {
        Logger::WriteLog("Session::Create: failed", MODULE_NAME, 2);
        return "";
    }
    return response["lobbyCode"].get<std::string>();
}

bool Session::Join(const std::string& lobbyCode)
{
    Logger::WriteLog(std::format("Session::Join(code={})", lobbyCode), MODULE_NAME, 69);

    if (IsActive())
    {
        Logger::WriteLog("Session::Join: already in a session - call Leave() first", MODULE_NAME, 2);
        return false;
    }
    if (!Auth::HasToken())
    {
        Logger::WriteLog("Session::Join: no auth token", MODULE_NAME, 2);
        return false;
    }

    nlohmann::json request;
    request["lobbyCode"] = lobbyCode;

    const nlohmann::json response = Http::PostJson(Config::Get_ServerUrl(), "/lobby/join", request, Auth::Get_Token());
    return EnterSession(response);
}

void Session::Leave()
{
    Logger::WriteLog("Session::Leave()", MODULE_NAME, 69);
    P2P::Disconnect();
    g_Active = false;
    g_SeatIndex = 0;
    g_SeatCount = 0;
}

bool Session::IsActive()
{
    return g_Active;
}

uint32_t Session::Get_SeatIndex()
{
    return g_SeatIndex;
}

uint32_t Session::Get_SeatCount()
{
    return g_SeatCount;
}
