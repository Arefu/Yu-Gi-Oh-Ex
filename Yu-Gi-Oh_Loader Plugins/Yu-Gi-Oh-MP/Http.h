#pragma once
#include <string>
#include <json.hpp>

// The tiny WinHTTP helper Auth.cpp and Session.cpp both need (P2P.cpp needs ParseUrl only, for the WebSocket upgrade
// request it builds itself). No async, no retry - one blocking call, called rarely (auth, server info, lobby create/join), never
// per-duel-action (that's P2P's WebSocket, not this).
namespace Http
{
    // Splits "https://host:port/base/path" into what WinHttpConnect/WinHttpOpenRequest need. False if it doesn't parse.
    bool ParseUrl(const std::string& url, bool& https, std::wstring& host, unsigned short& port, std::wstring& path);

    // One POST of a JSON body to <baseUrl><route>, with an optional bearer token. Returns a discarded json on any
    // failure (bad URL, connect failure, non-2xx, non-JSON body) - callers check .is_object() the way Auth.cpp does.
    nlohmann::json PostJson(const std::string& baseUrl, const std::string& route, const nlohmann::json& body, const std::string& bearerToken = "");

    // One GET, same rules as PostJson.
    nlohmann::json GetJson(const std::string& baseUrl, const std::string& route, const std::string& bearerToken = "");

    // What both use. A non-2xx reply is logged; its body is still parsed (the server's {"error": ...}).
    nlohmann::json Request(const wchar_t* method, const std::string& baseUrl, const std::string& route, const nlohmann::json* body, const std::string& bearerToken);
}
