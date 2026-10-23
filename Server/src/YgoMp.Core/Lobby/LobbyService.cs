using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace YgoMp.Core.Lobby;

public sealed class LobbyException(string message) : Exception(message);

// In-memory lobbies. A lobby = one duel session: a short code people share, a session id the relay uses, seats filled
// in join order, and the server-issued RNG seed (so neither client picks its own shuffle).
public sealed class Lobby
{
    private readonly object m_Lock = new();
    private readonly string?[] m_Seats;

    internal Lobby(string code, int seatCount, string? cardSetHash)
    {
        Code = code;
        SessionId = Guid.CreateVersion7().ToString("N");
        Seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        CardSetHash = cardSetHash;
        m_Seats = new string?[seatCount];
    }

    public string Code { get; }
    public string SessionId { get; }
    public ulong Seed { get; }
    public string? CardSetHash { get; }
    public int SeatCount => m_Seats.Length;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public bool Closed { get; private set; }

    public bool IsFull { get { lock (m_Lock) return m_Seats.All(s => s is not null); } }

    public IReadOnlyList<string?> Seats { get { lock (m_Lock) return m_Seats.ToArray(); } }

    // Rejoining returns the same seat (reconnect after a dropped socket).
    internal int TakeSeat(string playerId)
    {
        lock (m_Lock)
        {
            if (Closed)
                throw new LobbyException("Lobby is closed");
            int existing = Array.IndexOf(m_Seats, playerId);
            if (existing >= 0)
                return existing;
            int free = Array.IndexOf(m_Seats, null);
            if (free < 0)
                throw new LobbyException("Lobby is full");
            m_Seats[free] = playerId;
            return free;
        }
    }

    public int SeatOf(string playerId)
    {
        lock (m_Lock)
            return Array.IndexOf(m_Seats, playerId);
    }

    internal void Close()
    {
        lock (m_Lock)
            Closed = true;
    }
}

public sealed class LobbyService
{
    private readonly ConcurrentDictionary<string, Lobby> m_ByCode = new();
    private readonly ConcurrentDictionary<string, Lobby> m_BySession = new();

    public Lobby Create(string playerId, int seatCount, string? cardSetHash)
    {
        if (seatCount is not (2 or 4))
            throw new LobbyException("seatCount must be 2 (duel) or 4 (tag duel)");

        while (true)
        {
            var lobby = new Lobby(RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 6), seatCount, cardSetHash);
            if (!m_ByCode.TryAdd(lobby.Code, lobby))
                continue;
            m_BySession[lobby.SessionId] = lobby;
            lobby.TakeSeat(playerId);
            return lobby;
        }
    }

    public (Lobby Lobby, int Seat) Join(string playerId, string code, string? cardSetHash)
    {
        if (!m_ByCode.TryGetValue(code.Trim().ToUpperInvariant(), out var lobby))
            throw new LobbyException("No lobby with that code");
        // Both clients must run the same cards (custom ids borrow engine ids identically or lockstep desyncs).
        if (lobby.CardSetHash is not null && cardSetHash is not null && lobby.CardSetHash != cardSetHash)
            throw new LobbyException("Your cards/mods don't match the host's");
        return (lobby, lobby.TakeSeat(playerId));
    }

    public Lobby? FindSession(string sessionId) => m_BySession.GetValueOrDefault(sessionId);

    public void Close(Lobby lobby)
    {
        lobby.Close();
        m_ByCode.TryRemove(lobby.Code, out _);
        m_BySession.TryRemove(lobby.SessionId, out _);
    }

    // Drops lobbies nobody finished joining. Called by the server's sweeper.
    public int SweepStale(TimeSpan maxAge)
    {
        int removed = 0;
        foreach (var lobby in m_ByCode.Values)
        {
            if (!lobby.IsFull && DateTimeOffset.UtcNow - lobby.CreatedAt > maxAge)
            {
                Close(lobby);
                removed++;
            }
        }
        return removed;
    }
}
