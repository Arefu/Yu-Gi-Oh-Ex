namespace YgoMp.Protocol;

// HTTP bodies. ASP.NET serializes camelCase, which is what Auth.cpp / Session.cpp read ("token", "sessionId", ...).
// Extra response fields are fine: the plugin ignores keys it doesn't know.

// POST /auth/steam - what Auth.cpp sends today. Ticket = hex of GetAuthSessionTicket.
public record SteamAuthRequest(string SteamId, string Ticket);

// POST /auth/claim - Epic/GOG/LAN players: redeem a code an admin minted (`YgoMp.SelfHost mint`).
public record ClaimAuthRequest(string Code, string? DisplayName);

public record TokenResponse(string Token, string PlayerId, string DisplayName, DateTimeOffset ExpiresAt);

// POST /lobby/create - Session.cpp sends seatCount (2 = duel, 4 = tag).
public record LobbyCreateRequest(int SeatCount, string? CardSetHash = null);

// POST /lobby/join
public record LobbyJoinRequest(string LobbyCode, string? CardSetHash = null);

// Session.cpp requires sessionId, seatIndex, seatCount (+ lobbyCode on create). Seed = the server-issued duel RNG seed.
public record LobbySessionResponse(string SessionId, string LobbyCode, int SeatIndex, int SeatCount, ulong Seed);

public record BoardInfo(string Id, string Title, string Stat, string Order, string[] Periods);

public record BoardEntry(int Rank, string PlayerId, string DisplayName, long Value);

public record BoardResponse(string Id, string Title, string Period, IReadOnlyList<BoardEntry> Entries);

public record PlayerStat(string Stat, string Period, long Value);
