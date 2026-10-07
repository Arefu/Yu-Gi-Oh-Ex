#include <Windows.h>
#include <cstring>
#include <format>
#include <string>

#include "AntiCheat.h"
#include "Auth.h"
#include "Config.h"
#include "EngineHooks.h"
#include "Logger.h"
#include "LiveSetting.h"
#include "BanList.h"
#include "Detours.h"
#include "P2P.h"
#include "Session.h"
#include "Steam.h"

// Yu-Gi-Oh-MP: the multiplayer/tournament companion plugin. Scope (user, 2026-09-30): relay-based lobbies/duels
// (including tag duels) with basic anti-cheat, NOT a from-scratch duel engine - see ygo-mp-lobby-plan memory for the
// full decision history and, importantly, everything below marked as UNVERIFIED/blocked on IDA (the actual
// duel-engine input hook this all eventually needs to call). Every export logs its own call at debug level (69) on
// top of whatever the module it calls into already logs, so the plugin boundary itself is observable in
// console.log/trace even before anything is wired up to a real UI or a real server.
namespace
{
    void LogCall(const std::string& what)
    {
        Logger::WriteLog(std::format("[export] {}", what), MODULE_NAME, 69);
    }
}

extern "C" __declspec(dllexport) void __cdecl MP_Setup()
{
    LogCall("MP_Setup()");
    AntiCheat::Setup();   // baseline loaded modules; call again later to re-baseline after anything legitimate loads late
}

extern "C" __declspec(dllexport) bool __cdecl MP_TryAuthenticate()
{
    LogCall("MP_TryAuthenticate()");
    return Auth::AuthenticateWithSteam();
}

extern "C" __declspec(dllexport) bool __cdecl MP_HasToken()
{
    LogCall("MP_HasToken()");
    return Auth::HasToken();
}

extern "C" __declspec(dllexport) unsigned long long __cdecl MP_GetSteamId64()
{
    LogCall("MP_GetSteamId64()");
    return Steam::Get_SteamId64();
}

// seatCount: 2 for a normal duel, 4 for a tag duel (Session::Mode). Returns the lobby code to share, or "" on failure
// (check console.log/trace for why - no ServerUrl, no token, service unreachable are all logged separately).
extern "C" __declspec(dllexport) void __cdecl MP_CreateLobby(int seatCount, char* lobbyCodeOut, int lobbyCodeOutSize)
{
    LogCall(std::format("MP_CreateLobby(seatCount={})", seatCount));
    const std::string code = Session::Create(seatCount == 4 ? Session::Mode::Tag2v2 : Session::Mode::Duel1v1);
    if (lobbyCodeOut && lobbyCodeOutSize > 0)
        strncpy_s(lobbyCodeOut, lobbyCodeOutSize, code.c_str(), _TRUNCATE);
}

extern "C" __declspec(dllexport) bool __cdecl MP_JoinLobby(const char* lobbyCode)
{
    LogCall(std::format("MP_JoinLobby({})", lobbyCode ? lobbyCode : "(null)"));
    return lobbyCode && Session::Join(lobbyCode);
}

extern "C" __declspec(dllexport) void __cdecl MP_LeaveLobby()
{
    LogCall("MP_LeaveLobby()");
    Session::Leave();
}

extern "C" __declspec(dllexport) bool __cdecl MP_IsSessionActive()
{
    LogCall("MP_IsSessionActive()");
    return Session::IsActive();
}

extern "C" __declspec(dllexport) unsigned int __cdecl MP_GetSeatIndex()
{
    LogCall("MP_GetSeatIndex()");
    return Session::Get_SeatIndex();
}

// Placeholder action send - the real payload shape doesn't exist yet (needs the duel-engine input hook, see P2P.h).
// Exists so the export surface is there to wire a real caller into once that hook is found.
extern "C" __declspec(dllexport) void __cdecl MP_SendRawAction(const unsigned char* data, int length)
{
    LogCall(std::format("MP_SendRawAction({} bytes)", length));
    P2P::Action action;
    action.PlayerIndex = Session::Get_SeatIndex();
    if (data && length > 0)
        action.Payload.assign(data, data + length);
    P2P::SendAction(action);
}

extern "C" __declspec(dllexport) bool __cdecl MP_IsEnvironmentSuspicious()
{
    LogCall("MP_IsEnvironmentSuspicious()");
    return AntiCheat::IsEnvironmentSuspicious();
}

extern "C" __declspec(dllexport) unsigned long long __cdecl MP_ComputeStateHash()
{
    LogCall("MP_ComputeStateHash()");
    return AntiCheat::ComputeStateHash();
}

// No ban list (every card at 3 copies): the host's "Ban list" lobby setting (LiveSetting.cpp; this export only does Off - custom lists are chosen there). Not saved: the game's list at every start.
extern "C" __declspec(dllexport) void __cdecl MP_SetNoBanList(bool on)
{
    LogCall(std::format("MP_SetNoBanList({})", on));
    on ? BanList::UseOff() : BanList::UseGame();
}

extern "C" __declspec(dllexport) bool __cdecl MP_GetNoBanList()
{
    return BanList::CurrentMode() != BanList::Mode::Game;
}

// Debug-only: flips the game's own g_bIsDuelMultiplayer so Duel__LiveManager_TransportReceive/Send actually fire
// (see EngineHooks.h) - nothing in the shipped UI ever sets this, so there's no other way to exercise those two hooks
// short of the real lobby/transport wiring. Not part of the real launch flow.
extern "C" __declspec(dllexport) void __cdecl MP_DEBUG_SetIsDuelMultiplayer(bool on)
{
    LogCall(std::format("MP_DEBUG_SetIsDuelMultiplayer({})", on));
    EngineHooks::DEBUG_SetIsDuelMultiplayer(on);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        Logger::SetupLogger();
        Logger::WriteLog(std::format("Running from: {}", Config::Get_WorkingDirectory()), MODULE_NAME, 0);

        // Only the cheap, DllMain-safe setup happens here (Steam::Setup is GetProcAddress only, no network, no new
        // module loads). AntiCheat::Setup() enumerates modules and is left to MP_Setup() to call once the game is
        // further along, same reasoning as MP_TryAuthenticate not running here.
        if (Steam::Setup())
            Logger::WriteLog(std::format("Steam identity ready (SteamID64 {})", Steam::Get_SteamId64()), MODULE_NAME, 0);
        else
            Logger::WriteLog("Steam is not started yet (the plugin loads before the game's SteamAPI_Init); it is looked up when first needed", MODULE_NAME, 0);

        // Hooks game addresses directly, same as Yu-Gi-Oh-Effects' DllMain does - the loader only injects this DLL
        // once the game process (and its .text) already exists, so this is safe here (see EngineHooks.cpp).
        EngineHooks::Setup();

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        BanList::Attach();
        LiveSetting::Attach();
        DetourTransactionCommit();
        break;
    }
    return TRUE;
}
