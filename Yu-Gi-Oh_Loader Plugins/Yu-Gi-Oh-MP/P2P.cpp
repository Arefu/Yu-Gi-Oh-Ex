#include <Windows.h>
#include <winhttp.h>

#include <atomic>
#include <format>
#include <mutex>
#include <thread>
#include <type_traits>
#include <vector>

#include "P2P.h"
#include "Auth.h"
#include "Config.h"
#include "Http.h"
#include "Logger.h"

#pragma comment(lib, "winhttp.lib")

namespace
{
    HINTERNET g_Session = nullptr;
    HINTERNET g_Connection = nullptr;
    HINTERNET g_Socket = nullptr;   // the upgraded WebSocket handle; only this one is used once Connect() returns
    std::thread g_ReceiveThread;
    std::atomic<bool> g_Running = false;
    std::mutex g_HandlerLock;
    P2P::ActionHandler g_Handler;
    std::mutex g_SendLock;   // WinHttpWebSocketSend isn't documented safe for concurrent callers; Send/ReportEvent can both fire from hook threads

    // Our own wire format (P2P.h): one WebSocket binary message = one frame, no length-prefix framing needed beyond
    // what WinHTTP already gives us (each WinHttpWebSocketSend/Receive call is exactly one message). All integers
    // little-endian (x86/x64 native - both ends of this protocol are ours, no portability need beyond that).
    //
    //   byte    Kind             1 = Action, 2 = EventReport
    //   uint32  SequenceNumber
    //   uint32  Seat             PlayerIndex for Action, Seat for EventReport
    //   uint16  EventType        0 for Action (meaningless there); the game's own event-type word for EventReport
    //   uint32  PayloadLength
    //   byte[]  Payload
    enum class FrameKind : uint8_t { Action = 1, EventReport = 2 };

    template <typename T>
    void AppendLE(std::vector<uint8_t>& out, T value)
    {
        static_assert(std::is_integral_v<T>, "AppendLE is for fixed-width integers only");
        for (size_t i = 0; i < sizeof(T); ++i)
            out.push_back(static_cast<uint8_t>(value >> (8 * i)));
    }

    std::vector<uint8_t> EncodeFrame(FrameKind kind, uint32_t sequenceNumber, uint32_t seat, uint16_t eventType, const std::vector<uint8_t>& payload)
    {
        std::vector<uint8_t> frame;
        frame.reserve(15 + payload.size());
        frame.push_back(static_cast<uint8_t>(kind));
        AppendLE(frame, sequenceNumber);
        AppendLE(frame, seat);
        AppendLE(frame, eventType);
        AppendLE(frame, static_cast<uint32_t>(payload.size()));
        frame.insert(frame.end(), payload.begin(), payload.end());
        return frame;
    }

    bool SendFrame(const std::vector<uint8_t>& frame)
    {
        std::lock_guard<std::mutex> lock(g_SendLock);
        const DWORD result = WinHttpWebSocketSend(g_Socket, WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE,
            const_cast<uint8_t*>(frame.data()), static_cast<DWORD>(frame.size()));
        if (result != NO_ERROR)
            Logger::WriteLog(std::format("P2P: WinHttpWebSocketSend failed ({})", result), MODULE_NAME, 2);
        return result == NO_ERROR;
    }

    // Blocks on WinHttpWebSocketReceive until the socket closes or errors; runs on its own thread (started by
    // Connect). Only Action frames come back down this path (EventReport is one-way, client to server - see P2P.h).
    // Dispatches to g_Handler as-is, on THIS thread - not the game's; whatever calls SetActionHandler must not touch
    // engine/duel state directly without marshalling onto the game's own thread first (still open, needs Hook Point C).
    void ReceiveLoop()
    {
        std::vector<uint8_t> buffer(64 * 1024);
        while (g_Running)
        {
            DWORD received = 0;
            WINHTTP_WEB_SOCKET_BUFFER_TYPE type{};
            const DWORD result = WinHttpWebSocketReceive(g_Socket, buffer.data(), static_cast<DWORD>(buffer.size()), &received, &type);
            if (result != NO_ERROR)
            {
                Logger::WriteLog(std::format("P2P: WinHttpWebSocketReceive failed ({}) - relay connection lost", result), MODULE_NAME, 2);
                break;
            }
            if (type == WINHTTP_WEB_SOCKET_CLOSE_BUFFER_TYPE)
            {
                Logger::WriteLog("P2P: relay closed the connection", MODULE_NAME, 1);
                break;
            }
            if (type != WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE && type != WINHTTP_WEB_SOCKET_BINARY_FRAGMENT_BUFFER_TYPE)
                continue;   // text frames aren't a thing in this protocol

            if (received < 15 || buffer[0] != static_cast<uint8_t>(FrameKind::Action))
            {
                Logger::WriteLog(std::format("P2P: received a {}-byte message that isn't a well-formed Action frame - ignored", received), MODULE_NAME, 2);
                continue;
            }

            P2P::Action action;
            action.SequenceNumber = buffer[1] | (buffer[2] << 8) | (buffer[3] << 16) | (static_cast<uint32_t>(buffer[4]) << 24);
            action.PlayerIndex = buffer[5] | (buffer[6] << 8) | (buffer[7] << 16) | (static_cast<uint32_t>(buffer[8]) << 24);
            const uint32_t payloadLength = buffer[11] | (buffer[12] << 8) | (buffer[13] << 16) | (static_cast<uint32_t>(buffer[14]) << 24);
            if (15u + payloadLength > received)
            {
                Logger::WriteLog("P2P: Action frame's PayloadLength doesn't match the message size - ignored", MODULE_NAME, 2);
                continue;
            }
            action.Payload.assign(buffer.begin() + 15, buffer.begin() + 15 + payloadLength);
            Logger::WriteLog(std::format("P2P: received Action (seq={}, seat={}, {} bytes)", action.SequenceNumber, action.PlayerIndex, action.Payload.size()), MODULE_NAME, 69);

            std::lock_guard<std::mutex> lock(g_HandlerLock);
            if (g_Handler)
                g_Handler(action);
        }
        g_Running = false;
    }
}

bool P2P::Connect(const std::string& sessionId)
{
    Logger::WriteLog(std::format("P2P::Connect(session={})", sessionId), MODULE_NAME, 69);

    if (IsConnected())
    {
        Logger::WriteLog("P2P::Connect: already connected - call Disconnect() first", MODULE_NAME, 2);
        return false;
    }
    if (!Auth::HasToken())
    {
        Logger::WriteLog("P2P::Connect: no auth token (Auth::AuthenticateWithSteam hasn't succeeded) - refusing to connect anonymously", MODULE_NAME, 2);
        return false;
    }
    const std::string serverUrl = Config::Get_ServerUrl();
    bool https;
    std::wstring host, path;
    unsigned short port;
    if (serverUrl.empty() || !Http::ParseUrl(serverUrl, https, host, port, path))
    {
        Logger::WriteLog("P2P::Connect: no usable [Yu-Gi-Oh-MP] ServerUrl", MODULE_NAME, 2);
        return false;
    }
    path += std::wstring(L"/relay/") + std::wstring(sessionId.begin(), sessionId.end());

    g_Session = WinHttpOpen(L"Yu-Gi-Oh-MP/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    g_Connection = g_Session ? WinHttpConnect(g_Session, host.c_str(), port, 0) : nullptr;
    HINTERNET request = g_Connection ? WinHttpOpenRequest(g_Connection, L"GET", path.c_str(), nullptr, WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES, https ? WINHTTP_FLAG_SECURE : 0) : nullptr;
    if (!request)
    {
        Logger::WriteLog("P2P::Connect: WinHttpOpen/Connect/OpenRequest failed", MODULE_NAME, 2);
        Disconnect();
        return false;
    }

    const std::wstring token(Auth::Get_Token().begin(), Auth::Get_Token().end());
    const std::wstring authHeader = L"Authorization: Bearer " + token;
    WinHttpAddRequestHeaders(request, authHeader.c_str(), static_cast<DWORD>(authHeader.size()), WINHTTP_ADDREQ_FLAG_ADD);
    WinHttpSetOption(request, WINHTTP_OPTION_UPGRADE_TO_WEB_SOCKET, nullptr, 0);

    bool ok = WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0)
        && WinHttpReceiveResponse(request, nullptr);
    if (ok)
    {
        g_Socket = WinHttpWebSocketCompleteUpgrade(request, 0);
        ok = g_Socket != nullptr;
    }
    WinHttpCloseHandle(request);   // the request handle is spent once the upgrade completes (or failed) - only g_Socket matters from here

    if (!ok)
    {
        Logger::WriteLog(std::format("P2P::Connect: WebSocket upgrade failed (WinHTTP error {})", GetLastError()), MODULE_NAME, 2);
        Disconnect();
        return false;
    }

    g_Running = true;
    g_ReceiveThread = std::thread(ReceiveLoop);
    Logger::WriteLog("P2P: relay connected", MODULE_NAME, 0);
    return true;
}

void P2P::Disconnect()
{
    Logger::WriteLog("P2P::Disconnect()", MODULE_NAME, 69);
    g_Running = false;
    if (g_Socket)
    {
        WinHttpWebSocketClose(g_Socket, WINHTTP_WEB_SOCKET_SUCCESS_CLOSE_STATUS, nullptr, 0);
        WinHttpCloseHandle(g_Socket);
        g_Socket = nullptr;
    }
    if (g_ReceiveThread.joinable())
        g_ReceiveThread.join();
    if (g_Connection) { WinHttpCloseHandle(g_Connection); g_Connection = nullptr; }
    if (g_Session) { WinHttpCloseHandle(g_Session); g_Session = nullptr; }
}

bool P2P::IsConnected()
{
    return g_Socket != nullptr && g_Running;
}

void P2P::SendAction(const Action& action)
{
    Logger::WriteLog(std::format("P2P::SendAction(seq={}, seat={}, {} bytes)", action.SequenceNumber, action.PlayerIndex, action.Payload.size()), MODULE_NAME, 69);
    if (!IsConnected())
    {
        Logger::WriteLog("P2P::SendAction: not connected, dropped", MODULE_NAME, 2);
        return;
    }
    SendFrame(EncodeFrame(FrameKind::Action, action.SequenceNumber, action.PlayerIndex, 0, action.Payload));
}

void P2P::ReportEvent(const EventReport& event)
{
    Logger::WriteLog(std::format("P2P::ReportEvent(seq={}, seat={}, type=0x{:x}, {} bytes)", event.SequenceNumber, event.Seat, event.EventType, event.Payload.size()), MODULE_NAME, 69);
    if (!IsConnected())
    {
        Logger::WriteLog("P2P::ReportEvent: not connected, dropped (telemetry only - never blocks the duel)", MODULE_NAME, 69);
        return;
    }
    SendFrame(EncodeFrame(FrameKind::EventReport, event.SequenceNumber, event.Seat, event.EventType, event.Payload));
}

void P2P::SetActionHandler(ActionHandler handler)
{
    std::lock_guard<std::mutex> lock(g_HandlerLock);
    g_Handler = std::move(handler);
}
