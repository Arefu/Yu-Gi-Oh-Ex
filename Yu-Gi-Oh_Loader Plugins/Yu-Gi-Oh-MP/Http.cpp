#include <Windows.h>
#include <winhttp.h>

#include <format>

#include "Http.h"
#include "Logger.h"

#pragma comment(lib, "winhttp.lib")

bool Http::ParseUrl(const std::string& url, bool& https, std::wstring& host, unsigned short& port, std::wstring& path)
{
    std::wstring wide(url.begin(), url.end());   // ServerUrl is expected to be plain ASCII (a hostname/IP)

    URL_COMPONENTS parts{};
    parts.dwStructSize = sizeof(parts);
    wchar_t hostBuffer[256]{};
    wchar_t pathBuffer[2048]{};
    parts.lpszHostName = hostBuffer;
    parts.dwHostNameLength = _countof(hostBuffer);
    parts.lpszUrlPath = pathBuffer;
    parts.dwUrlPathLength = _countof(pathBuffer);

    if (!WinHttpCrackUrl(wide.c_str(), static_cast<DWORD>(wide.size()), 0, &parts))
        return false;

    https = parts.nScheme == INTERNET_SCHEME_HTTPS;
    host = hostBuffer;
    port = parts.nPort;
    path = pathBuffer;
    return true;
}

nlohmann::json Http::PostJson(const std::string& baseUrl, const std::string& route, const nlohmann::json& body, const std::string& bearerToken)
{
    bool https;
    std::wstring host, path;
    unsigned short port;
    if (!ParseUrl(baseUrl, https, host, port, path))
    {
        Logger::WriteLog(std::format("Http::PostJson: ServerUrl '{}' is not a usable URL", baseUrl), MODULE_NAME, 2);
        return nlohmann::json();
    }
    path += std::wstring(route.begin(), route.end());

    HINTERNET session = WinHttpOpen(L"Yu-Gi-Oh-MP/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    HINTERNET connection = session ? WinHttpConnect(session, host.c_str(), port, 0) : nullptr;
    HINTERNET request = connection ? WinHttpOpenRequest(connection, L"POST", path.c_str(), nullptr, WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES, https ? WINHTTP_FLAG_SECURE : 0) : nullptr;

    nlohmann::json result;
    if (request)
    {
        std::wstring headers = L"Content-Type: application/json\r\n";
        if (!bearerToken.empty())
            headers += L"Authorization: Bearer " + std::wstring(bearerToken.begin(), bearerToken.end()) + L"\r\n";

        const std::string payload = body.dump();
        if (WinHttpSendRequest(request, headers.c_str(), static_cast<DWORD>(headers.size()),
                const_cast<char*>(payload.data()), static_cast<DWORD>(payload.size()), static_cast<DWORD>(payload.size()), 0)
            && WinHttpReceiveResponse(request, nullptr))
        {
            std::string response;
            DWORD available = 0;
            while (WinHttpQueryDataAvailable(request, &available) && available > 0)
            {
                std::string chunk(available, '\0');
                DWORD read = 0;
                if (!WinHttpReadData(request, chunk.data(), available, &read))
                    break;
                response.append(chunk, 0, read);
            }
            result = nlohmann::json::parse(response, nullptr, false, true);
            if (result.is_discarded())
                Logger::WriteLog(std::format("Http::PostJson {}: response was not JSON ({} bytes)", route, response.size()), MODULE_NAME, 2);
        }
        else
        {
            Logger::WriteLog(std::format("Http::PostJson {}: request failed (WinHTTP error {})", route, GetLastError()), MODULE_NAME, 2);
        }
        WinHttpCloseHandle(request);
    }
    if (connection) WinHttpCloseHandle(connection);
    if (session) WinHttpCloseHandle(session);
    return result;
}
