#include <Windows.h>
#include <string>

#include "Steam.h"
#include "Logger.h"

namespace
{
    // Steamworks' flat C exports (steam_api64.dll), declared by hand so this plugin needs no Steamworks SDK (see Steam.h).
    // The game ships an OLD steam_api64.dll (checked 2026-10-07): it has no SteamAPI_SteamUser / SteamAPI_SteamMatchmaking_v009 accessors,
    // only SteamClient() + SteamAPI_GetHSteamUser/Pipe + SteamAPI_ISteamClient_GetISteam*(client, user, pipe, version). The versions are the
    // ones the game itself asks for (strings in YuGiOh.exe). The newer accessors are tried first so a newer DLL works too.
    using Accessor_t = intptr_t(__cdecl*)();
    using GetHandle_t = int32_t(__cdecl*)();
    using GetInterface_t = intptr_t(__cdecl*)(intptr_t client, int32_t user, int32_t pipe, const char* version);
    using GetSteamID_t = uint64_t(__cdecl*)(intptr_t self);
    using GetAuthSessionTicket_t = uint32_t(__cdecl*)(intptr_t self, void* ticket, int maxTicket, uint32_t* written);
    using GetLobbyData_t = const char*(__cdecl*)(intptr_t self, uint64_t lobby, const char* key);
    using SetLobbyData_t = bool(__cdecl*)(intptr_t self, uint64_t lobby, const char* key, const char* value);
    using SetMemberData_t = void(__cdecl*)(intptr_t self, uint64_t lobby, const char* key, const char* value);
    using GetMemberData_t = const char*(__cdecl*)(intptr_t self, uint64_t lobby, uint64_t member, const char* key);
    using MemberCount_t = int(__cdecl*)(intptr_t self, uint64_t lobby);
    using MemberAt_t = uint64_t(__cdecl*)(intptr_t self, uint64_t lobby, int index);
    using PersonaName_t = const char*(__cdecl*)(intptr_t self, uint64_t player);

    constexpr const char* kUserVersion = "SteamUser020";
    constexpr const char* kMatchmakingVersion = "SteamMatchMaking009";
    constexpr const char* kFriendsVersion = "SteamFriends017";

    Accessor_t NewUser = nullptr, NewMatchmaking = nullptr;                 // SteamAPI_SteamUser(_vNNN), SteamAPI_SteamMatchmaking(_vNNN)
    Accessor_t SteamClient = nullptr;                                       // old style
    GetHandle_t GetHSteamUser = nullptr, GetHSteamPipe = nullptr;
    GetInterface_t ClientGetUser = nullptr, ClientGetMatchmaking = nullptr;
    GetSteamID_t UserGetSteamID = nullptr;
    GetAuthSessionTicket_t UserGetAuthSessionTicket = nullptr;
    GetLobbyData_t MatchmakingGetLobbyData = nullptr;
    SetLobbyData_t MatchmakingSetLobbyData = nullptr;
    SetMemberData_t MatchmakingSetMemberData = nullptr;
    GetMemberData_t MatchmakingGetMemberData = nullptr;
    MemberCount_t MatchmakingMemberCount = nullptr;
    MemberAt_t MatchmakingMemberAt = nullptr;
    Accessor_t NewFriends = nullptr;
    GetInterface_t ClientGetFriends = nullptr;
    PersonaName_t FriendsPersonaName = nullptr;

    bool g_Resolved = false;

    template <typename T> T Find(HMODULE module, std::initializer_list<const char*> names)
    {
        for (const char* name : names)
            if (FARPROC proc = GetProcAddress(module, name))
                return reinterpret_cast<T>(proc);
        return nullptr;
    }

    // Resolves the exports the first time steam_api64.dll is there (the plugin can load before the game's SteamAPI_Init).
    bool Resolve()
    {
        if (g_Resolved)
            return true;
        HMODULE steam = GetModuleHandleA("steam_api64.dll");
        if (!steam)
            return false;
        NewUser = Find<Accessor_t>(steam, { "SteamAPI_SteamUser_v023", "SteamAPI_SteamUser_v021", "SteamAPI_SteamUser_v020", "SteamAPI_SteamUser" });
        NewMatchmaking = Find<Accessor_t>(steam, { "SteamAPI_SteamMatchmaking_v009", "SteamAPI_SteamMatchmaking" });
        SteamClient = Find<Accessor_t>(steam, { "SteamClient" });
        GetHSteamUser = Find<GetHandle_t>(steam, { "SteamAPI_GetHSteamUser" });
        GetHSteamPipe = Find<GetHandle_t>(steam, { "SteamAPI_GetHSteamPipe" });
        ClientGetUser = Find<GetInterface_t>(steam, { "SteamAPI_ISteamClient_GetISteamUser" });
        ClientGetMatchmaking = Find<GetInterface_t>(steam, { "SteamAPI_ISteamClient_GetISteamMatchmaking" });
        UserGetSteamID = Find<GetSteamID_t>(steam, { "SteamAPI_ISteamUser_GetSteamID" });
        UserGetAuthSessionTicket = Find<GetAuthSessionTicket_t>(steam, { "SteamAPI_ISteamUser_GetAuthSessionTicket" });
        MatchmakingGetLobbyData = Find<GetLobbyData_t>(steam, { "SteamAPI_ISteamMatchmaking_GetLobbyData" });
        MatchmakingSetLobbyData = Find<SetLobbyData_t>(steam, { "SteamAPI_ISteamMatchmaking_SetLobbyData" });
        MatchmakingSetMemberData = Find<SetMemberData_t>(steam, { "SteamAPI_ISteamMatchmaking_SetLobbyMemberData" });
        MatchmakingGetMemberData = Find<GetMemberData_t>(steam, { "SteamAPI_ISteamMatchmaking_GetLobbyMemberData" });
        MatchmakingMemberCount = Find<MemberCount_t>(steam, { "SteamAPI_ISteamMatchmaking_GetNumLobbyMembers" });
        MatchmakingMemberAt = Find<MemberAt_t>(steam, { "SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex" });
        NewFriends = Find<Accessor_t>(steam, { "SteamAPI_SteamFriends_v017", "SteamAPI_SteamFriends" });
        ClientGetFriends = Find<GetInterface_t>(steam, { "SteamAPI_ISteamClient_GetISteamFriends" });
        FriendsPersonaName = Find<PersonaName_t>(steam, { "SteamAPI_ISteamFriends_GetFriendPersonaName" });

        const bool oldStyle = SteamClient && GetHSteamUser && GetHSteamPipe;
        g_Resolved = UserGetSteamID && ((NewUser && NewMatchmaking) || (oldStyle && ClientGetUser && ClientGetMatchmaking));
        if (!g_Resolved)
            Logger::WriteLog("steam_api64.dll is loaded but is missing the flat exports this plugin needs - no Steam identity available", MODULE_NAME, 2);
        return g_Resolved;
    }

    intptr_t Interface(Accessor_t newer, GetInterface_t fromClient, const char* version)
    {
        if (!Resolve())
            return 0;
        if (newer)
            return newer();
        const intptr_t client = SteamClient();
        const int32_t user = GetHSteamUser(), pipe = GetHSteamPipe();
        return client && user && pipe ? fromClient(client, user, pipe, version) : 0;   // 0 until the game's SteamAPI_Init has run
    }

    intptr_t User()
    {
        const intptr_t user = Interface(NewUser, ClientGetUser, kUserVersion);
        static bool announced = false;
        if (user && !announced)
        {
            announced = true;
            Logger::WriteLog("Steam identity ready (SteamID64 " + std::to_string(UserGetSteamID(user)) + ")", MODULE_NAME, 0);
        }
        return user;
    }
    intptr_t Matchmaking() { return Interface(NewMatchmaking, ClientGetMatchmaking, kMatchmakingVersion); }
    intptr_t Friends() { return ClientGetFriends || NewFriends ? Interface(NewFriends, ClientGetFriends, kFriendsVersion) : 0; }
}

bool Steam::Setup()
{
    return Resolve() && User() != 0;
}

bool Steam::IsAvailable()
{
    return User() != 0;
}

uint64_t Steam::Get_SteamId64()
{
    const intptr_t user = User();
    return user ? UserGetSteamID(user) : 0;
}

std::vector<uint8_t> Steam::Get_AuthSessionTicket()
{
    const intptr_t user = User();
    if (!user || !UserGetAuthSessionTicket)
        return {};

    std::vector<uint8_t> ticket(1024);   // Steamworks docs: 1024 bytes is enough for any ticket
    uint32_t written = 0;
    const uint32_t handle = UserGetAuthSessionTicket(user, ticket.data(), static_cast<int>(ticket.size()), &written);
    if (handle == 0 || written == 0)   // k_HAuthTicketInvalid = 0
        return {};

    ticket.resize(written);
    return ticket;
}

const char* Steam::GetLobbyData(uint64_t lobby, const char* key)
{
    const intptr_t matchmaking = Matchmaking();
    return matchmaking && MatchmakingGetLobbyData ? MatchmakingGetLobbyData(matchmaking, lobby, key) : nullptr;
}

bool Steam::SetLobbyData(uint64_t lobby, const char* key, const char* value)
{
    const intptr_t matchmaking = Matchmaking();
    return matchmaking && MatchmakingSetLobbyData && MatchmakingSetLobbyData(matchmaking, lobby, key, value);
}

void Steam::SetLobbyMemberData(uint64_t lobby, const char* key, const char* value)
{
    if (const intptr_t matchmaking = Matchmaking(); matchmaking && MatchmakingSetMemberData)
        MatchmakingSetMemberData(matchmaking, lobby, key, value);
}

const char* Steam::GetLobbyMemberData(uint64_t lobby, uint64_t member, const char* key)
{
    const intptr_t matchmaking = Matchmaking();
    return matchmaking && MatchmakingGetMemberData ? MatchmakingGetMemberData(matchmaking, lobby, member, key) : nullptr;
}

std::vector<uint64_t> Steam::LobbyMembers(uint64_t lobby)
{
    std::vector<uint64_t> members;
    const intptr_t matchmaking = Matchmaking();
    if (!matchmaking || !MatchmakingMemberCount || !MatchmakingMemberAt)
        return members;
    const int count = MatchmakingMemberCount(matchmaking, lobby);
    for (int i = 0; i < count; ++i)
        members.push_back(MatchmakingMemberAt(matchmaking, lobby, i));
    return members;
}

std::string Steam::PersonaName(uint64_t player)
{
    const intptr_t friends = Friends();
    const char* name = friends && FriendsPersonaName ? FriendsPersonaName(friends, player) : nullptr;
    return name && *name ? name : std::to_string(player);
}
