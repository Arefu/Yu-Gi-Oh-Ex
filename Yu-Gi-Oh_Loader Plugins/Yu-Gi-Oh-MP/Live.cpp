#include <Windows.h>
#include <winhttp.h>

#include <atomic>
#include <condition_variable>
#include <deque>
#include <format>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#include <json.hpp>

#include "Auth.h"
#include "BanList.h"
#include "Config.h"
#include "Http.h"
#include "Live.h"
#include "Logger.h"
#include "Steam.h"

namespace
{
    constexpr const char* kClientVersion = "Yu-Gi-Oh-MP 2026-10-14";
    constexpr size_t kMaxQueued = 20000;   // ~a few duels of lines; past this the server is too slow and lines are dropped

    std::atomic<Live::State> g_State = Live::State::Offline;
    std::atomic<uint32_t> g_Version = 0;
    std::atomic<bool> g_Stop = false;

    std::mutex g_TextLock;
    std::wstring g_Status = L"Not connected";
    std::wstring g_Detail;
    std::wstring g_BanList = L"Ban list: the game's";

    std::thread g_Worker;
    std::thread g_Sender;

    std::mutex g_SocketLock;   // guards g_Socket's lifetime and serializes sends (WinHttpWebSocketSend isn't documented thread safe)
    HINTERNET g_Session = nullptr, g_Connection = nullptr, g_Socket = nullptr;

    std::mutex g_QueueLock;
    std::condition_variable g_QueueSignal;
    std::deque<std::string> g_Queue;

    bool g_BanListApplied = false;   // worker thread only (and Disconnect after joining it)

    std::wstring Wide(const std::string& utf8)
    {
        if (utf8.empty())
            return {};
        std::wstring wide(MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), wide.data(), static_cast<int>(wide.size()));
        return wide;
    }

    std::string Narrow(const std::wstring& wide)
    {
        if (wide.empty())
            return {};
        std::string utf8(WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), nullptr, 0, nullptr, nullptr), '\0');
        WideCharToMultiByte(CP_UTF8, 0, wide.data(), static_cast<int>(wide.size()), utf8.data(), static_cast<int>(utf8.size()), nullptr, nullptr);
        return utf8;
    }

    void SetText(std::wstring* status, std::wstring* detail, std::wstring* banList)
    {
        {
            std::lock_guard lock(g_TextLock);
            if (status) g_Status = std::move(*status);
            if (detail) g_Detail = std::move(*detail);
            if (banList) g_BanList = std::move(*banList);
        }
        ++g_Version;
    }

    void SetState(Live::State state, std::wstring status, std::wstring detail)
    {
        g_State = state;
        SetText(&status, &detail, nullptr);
    }

    void Fail(const std::wstring& status, const std::wstring& detail)
    {
        Logger::WriteLog(std::format("Live: {} - {}", Narrow(status), Narrow(detail)), MODULE_NAME, 2);
        SetState(Live::State::Failed, status, detail);
    }

    bool SendText(const std::string& text)
    {
        std::lock_guard lock(g_SocketLock);
        if (!g_Socket)
            return false;
        return WinHttpWebSocketSend(g_Socket, WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE, const_cast<char*>(text.data()),
            static_cast<DWORD>(text.size())) == NO_ERROR;
    }

    void CloseSocket()
    {
        std::lock_guard lock(g_SocketLock);
        if (g_Socket)
        {
            WinHttpWebSocketClose(g_Socket, WINHTTP_WEB_SOCKET_SUCCESS_CLOSE_STATUS, nullptr, 0);
            WinHttpCloseHandle(g_Socket);
            g_Socket = nullptr;
        }
        if (g_Connection) { WinHttpCloseHandle(g_Connection); g_Connection = nullptr; }
        if (g_Session) { WinHttpCloseHandle(g_Session); g_Session = nullptr; }
    }

    // The server's ban list (GET /server/info "banList", ServerBanList.cs): mode game / off / custom + Konami ids.
    void ApplyBanList(const nlohmann::json& list)
    {
        if (!list.is_object())
            return;
        const std::string mode = list.value("mode", std::string("game"));
        const std::string name = list.value("name", std::string("Server list"));
        std::wstring text;
        if (mode == "off")
        {
            BanList::UseOff();
            g_BanListApplied = true;
            text = L"Ban list: none (every card at 3)";
        }
        else if (mode == "custom")
        {
            BanList::List custom;
            custom.Name = name;
            auto ids = [&](const char* key, std::vector<uint16_t>& out)
            {
                if (list.contains(key) && list[key].is_array())
                    for (const auto& id : list[key])
                        if (id.is_number_unsigned() && id.get<uint32_t>() <= 0xFFFF)
                            out.push_back(static_cast<uint16_t>(id.get<uint32_t>()));
            };
            ids("forbidden", custom.Forbidden);
            ids("limited", custom.Limited);
            ids("semiLimited", custom.SemiLimited);
            BanList::UseCustom(custom);
            g_BanListApplied = true;
            text = std::format(L"Ban list: {} ({} forbidden, {} limited, {} semi-limited)", Wide(name), custom.Forbidden.size(), custom.Limited.size(),
                custom.SemiLimited.size());
        }
        else
        {
            text = L"Ban list: the game's";
        }
        Logger::WriteLog(std::format("Live: server ban list mode={} name={}", mode, name), MODULE_NAME, 0);
        SetText(nullptr, nullptr, &text);
    }

    bool OpenSocket(const std::string& serverUrl)
    {
        bool https;
        std::wstring host, path;
        unsigned short port;
        if (!Http::ParseUrl(serverUrl, https, host, port, path))
            return false;
        path += L"/live";

        std::lock_guard lock(g_SocketLock);
        g_Session = WinHttpOpen(L"Yu-Gi-Oh-MP/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
        g_Connection = g_Session ? WinHttpConnect(g_Session, host.c_str(), port, 0) : nullptr;
        HINTERNET request = g_Connection ? WinHttpOpenRequest(g_Connection, L"GET", path.c_str(), nullptr, WINHTTP_NO_REFERER,
            WINHTTP_DEFAULT_ACCEPT_TYPES, https ? WINHTTP_FLAG_SECURE : 0) : nullptr;
        if (!request)
            return false;

        const std::wstring authHeader = L"Authorization: Bearer " + Wide(Auth::Get_Token());
        WinHttpAddRequestHeaders(request, authHeader.c_str(), static_cast<DWORD>(authHeader.size()), WINHTTP_ADDREQ_FLAG_ADD);
        WinHttpSetOption(request, WINHTTP_OPTION_UPGRADE_TO_WEB_SOCKET, nullptr, 0);
        if (WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) && WinHttpReceiveResponse(request, nullptr))
            g_Socket = WinHttpWebSocketCompleteUpgrade(request, 0);
        WinHttpCloseHandle(request);
        return g_Socket != nullptr;
    }

    void SenderLoop()
    {
        while (true)
        {
            std::string batch;
            {
                std::unique_lock lock(g_QueueLock);
                g_QueueSignal.wait(lock, [] { return g_Stop || !g_Queue.empty(); });
                if (g_Stop)
                    return;
                while (!g_Queue.empty() && batch.size() < 32 * 1024)
                {
                    batch += g_Queue.front();
                    batch += '\n';
                    g_Queue.pop_front();
                }
            }
            if (!SendText(batch))
                return;   // the receive loop sees the socket die and reports it
        }
    }

    void OnServerMessage(const std::string& text)
    {
        const nlohmann::json message = nlohmann::json::parse(text, nullptr, false);
        if (!message.is_object())
            return;
        const std::string type = message.value("type", std::string());
        if (type == "welcome")
        {
            const std::string server = message.value("server", std::string("server"));
            const std::string name = message.value("displayName", Auth::Get_DisplayName());
            SetState(Live::State::Connected, std::format(L"Connected to {} as {}", Wide(server), Wide(name)),
                Wide(message.value("motd", std::string())) + std::format(L"  ({} online)", message.value("online", 1)));
            Logger::WriteLog(std::format("Live: connected to {} as {}", server, name), MODULE_NAME, 0);
        }
        else if (type == "notice")
        {
            const std::string notice = message.value("text", std::string());
            Logger::WriteLog(std::format("Live: server says: {}", notice), MODULE_NAME, 0);
            std::wstring detail = Wide(notice);
            SetText(nullptr, &detail, nullptr);
        }
        else if (type == "duelSaved")
        {
            Logger::WriteLog(std::format("Live: server saved the duel as {} (stats {})", message.value("duelId", std::string("?")),
                message.value("stats", std::string("?"))), MODULE_NAME, 0);
        }
    }

    void Run()
    {
        const std::string serverUrl = Config::Get_ServerUrl();
        if (serverUrl.empty())
            return Fail(L"No server set", L"Set [Yu-Gi-Oh-MP] ServerUrl in Config.ini, e.g. http://localhost:5080");

        SetState(Live::State::Connecting, std::format(L"Connecting to {}...", Wide(serverUrl)), L"");

        const nlohmann::json info = Http::GetJson(serverUrl, "/server/info");
        if (!info.is_object())
            return Fail(std::format(L"Can't reach {}", Wide(serverUrl)), L"Is the server running? (YgoMp.SelfHost) Check ServerUrl in Config.ini.");

        const std::string claim = Config::Get_ClaimCode();
        const bool signedIn = !claim.empty() ? Auth::AuthenticateWithClaim(claim, Config::Get_DisplayName()) : Auth::AuthenticateWithSteam();
        if (!signedIn)
            return Fail(L"Sign-in refused", !claim.empty()
                ? L"The server didn't accept ClaimCode from Config.ini (see console.log)."
                : L"Steam sign-in failed (see console.log). Without Steam, ask the host for a claim code and set ClaimCode in Config.ini.");

        ApplyBanList(info.contains("banList") ? info["banList"] : nlohmann::json());

        if (!OpenSocket(serverUrl))
        {
            CloseSocket();
            return Fail(L"Signed in, but the live connection failed", std::format(L"WinHTTP error {}", GetLastError()));
        }

        g_Sender = std::thread(SenderLoop);

        nlohmann::json hello;
        hello["type"] = "hello";
        hello["version"] = kClientVersion;
        hello["steamId"] = std::to_string(Steam::Get_SteamId64());
        hello["banList"] = BanList::CurrentMode() == BanList::Mode::Custom ? BanList::CurrentList().Name : BanList::CurrentMode() == BanList::Mode::Off ? "off" : "game";
        SendText(hello.dump());
        // Connected from here (lines start flowing); the server's welcome fills in the name and message.
        SetState(Live::State::Connected, std::format(L"Connected to {}", Wide(serverUrl)), L"");

        // Receive loop. The socket handle is only closed by Disconnect (which sets g_Stop first) or below, so reading it unlocked is fine here.
        std::vector<char> buffer(64 * 1024);
        std::string message;
        while (!g_Stop)
        {
            DWORD received = 0;
            WINHTTP_WEB_SOCKET_BUFFER_TYPE type;
            HINTERNET socket;
            {
                std::lock_guard lock(g_SocketLock);
                socket = g_Socket;
            }
            if (!socket || WinHttpWebSocketReceive(socket, buffer.data(), static_cast<DWORD>(buffer.size()), &received, &type) != NO_ERROR)
                break;
            if (type == WINHTTP_WEB_SOCKET_CLOSE_BUFFER_TYPE)
                break;
            message.append(buffer.data(), received);
            if (type == WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE)
            {
                OnServerMessage(message);
                message.clear();
            }
            else if (type != WINHTTP_WEB_SOCKET_UTF8_FRAGMENT_BUFFER_TYPE)
            {
                message.clear();   // binary: not used on /live
            }
        }

        const bool wanted = g_Stop;
        g_Stop = true;
        g_QueueSignal.notify_all();
        if (g_Sender.joinable())
            g_Sender.join();
        CloseSocket();
        if (wanted)
            SetState(Live::State::Offline, L"Not connected", L"");
        else
            Fail(L"Connection to the server lost", L"Press Connect to try again.");
    }
}

void Live::Connect()
{
    const State state = g_State;
    if (state == State::Connecting || state == State::Connected)
        return;
    if (g_Worker.joinable())
        g_Worker.join();   // a finished (failed) previous attempt

    {
        std::lock_guard lock(g_QueueLock);
        g_Queue.clear();
    }
    g_Stop = false;
    g_State = State::Connecting;
    g_Worker = std::thread(Run);
}

void Live::Disconnect()
{
    g_Stop = true;
    g_QueueSignal.notify_all();
    {
        // Closing the socket unblocks the worker's WinHttpWebSocketReceive.
        std::lock_guard lock(g_SocketLock);
        if (g_Socket)
            WinHttpWebSocketClose(g_Socket, WINHTTP_WEB_SOCKET_SUCCESS_CLOSE_STATUS, nullptr, 0);
    }
    if (g_Worker.joinable())
        g_Worker.join();
    CloseSocket();
    Auth::Clear();

    if (g_BanListApplied)
    {
        BanList::UseGame();
        g_BanListApplied = false;
        std::wstring text = L"Ban list: the game's";
        SetText(nullptr, nullptr, &text);
    }
    SetState(State::Offline, L"Not connected", L"");
    Logger::WriteLog("Live: disconnected", MODULE_NAME, 0);
}

Live::State Live::GetState()
{
    return g_State;
}

std::wstring Live::StatusText()
{
    std::lock_guard lock(g_TextLock);
    return g_Status;
}

std::wstring Live::DetailText()
{
    std::lock_guard lock(g_TextLock);
    return g_Detail;
}

std::wstring Live::BanListText()
{
    std::lock_guard lock(g_TextLock);
    return g_BanList;
}

uint32_t Live::Version()
{
    return g_Version;
}

void Live::Forward(const std::string& line)
{
    if (g_State != State::Connected)
        return;
    {
        std::lock_guard lock(g_QueueLock);
        if (g_Queue.size() >= kMaxQueued)
            return;
        g_Queue.push_back(line);
    }
    g_QueueSignal.notify_one();
}
