#include <Windows.h>

#include "Steam.h"
#include "Logger.h"

namespace
{
    // Steamworks' flat C exports (steam_api64.dll), same signatures as the SDK's steam_api_flat.h. Declared by hand so
    // this plugin needs no Steamworks SDK headers/libs - GetProcAddress on the module the host already loaded and
    // initialised is enough (see Steam.h).
    using SteamAPI_SteamUser_t = intptr_t(__cdecl*)();
    using SteamAPI_ISteamUser_GetSteamID_t = uint64_t(__cdecl*)(intptr_t instancePtr);
    using SteamAPI_ISteamUser_GetAuthSessionTicket_t = uint32_t(__cdecl*)(intptr_t instancePtr, void* pTicket, int cbMaxTicket, uint32_t* pcbTicket);

    SteamAPI_SteamUser_t SteamAPI_SteamUser = nullptr;
    SteamAPI_ISteamUser_GetSteamID_t SteamAPI_ISteamUser_GetSteamID = nullptr;
    SteamAPI_ISteamUser_GetAuthSessionTicket_t SteamAPI_ISteamUser_GetAuthSessionTicket = nullptr;

    bool g_Ready = false;
}

bool Steam::Setup()
{
    if (g_Ready)
        return true;

    HMODULE steam = GetModuleHandleA("steam_api64.dll");
    if (!steam)
        return false;

    SteamAPI_SteamUser = reinterpret_cast<SteamAPI_SteamUser_t>(GetProcAddress(steam, "SteamAPI_SteamUser"));
    SteamAPI_ISteamUser_GetSteamID = reinterpret_cast<SteamAPI_ISteamUser_GetSteamID_t>(GetProcAddress(steam, "SteamAPI_ISteamUser_GetSteamID"));
    SteamAPI_ISteamUser_GetAuthSessionTicket = reinterpret_cast<SteamAPI_ISteamUser_GetAuthSessionTicket_t>(GetProcAddress(steam, "SteamAPI_ISteamUser_GetAuthSessionTicket"));

    g_Ready = SteamAPI_SteamUser && SteamAPI_ISteamUser_GetSteamID && SteamAPI_ISteamUser_GetAuthSessionTicket;
    if (!g_Ready)
        Logger::WriteLog("steam_api64.dll is loaded but is missing the flat exports this plugin needs - no Steam identity available", MODULE_NAME, 2);
    return g_Ready;
}

bool Steam::IsAvailable()
{
    return g_Ready;
}

uint64_t Steam::Get_SteamId64()
{
    if (!g_Ready)
        return 0;
    const intptr_t user = SteamAPI_SteamUser();
    return user ? SteamAPI_ISteamUser_GetSteamID(user) : 0;
}

std::vector<uint8_t> Steam::Get_AuthSessionTicket()
{
    if (!g_Ready)
        return {};
    const intptr_t user = SteamAPI_SteamUser();
    if (!user)
        return {};

    std::vector<uint8_t> ticket(1024);   // Steamworks docs: 1024 bytes is enough for any ticket
    uint32_t written = 0;
    const uint32_t handle = SteamAPI_ISteamUser_GetAuthSessionTicket(user, ticket.data(), static_cast<int>(ticket.size()), &written);
    if (handle == 0 || written == 0)   // k_HAuthTicketInvalid = 0
        return {};

    ticket.resize(written);
    return ticket;
}
