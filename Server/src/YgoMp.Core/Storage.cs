namespace YgoMp.Core;

// Everything durable goes through these two interfaces. SelfHost = SQLite + files; Azure = Table/Blob later.
public interface IStore
{
    Task<Player?> GetPlayerAsync(string id, CancellationToken ct = default);
    Task<Player?> FindPlayerAsync(string platform, string platformId, CancellationToken ct = default);
    Task SavePlayerAsync(Player player, CancellationToken ct = default);
    Task<IReadOnlyList<Player>> ListPlayersAsync(CancellationToken ct = default);

    Task SaveClaimCodeAsync(ClaimCode code, CancellationToken ct = default);
    Task<ClaimCode?> GetClaimCodeAsync(string code, CancellationToken ct = default);
    // Atomically marks the code redeemed by playerId. False if it was already redeemed by someone else.
    Task<bool> RedeemClaimCodeAsync(string code, string playerId, DateTimeOffset at, CancellationToken ct = default);

    Task SaveDuelAsync(DuelRecord duel, CancellationToken ct = default);
    Task<DuelRecord?> GetDuelAsync(string id, CancellationToken ct = default);
    // Duels with Stats == Counted that both players sat in, ended at or after since (anti-farming cap).
    Task<int> CountCountedDuelsBetweenAsync(string playerA, string playerB, DateTimeOffset since, CancellationToken ct = default);

    Task<IReadOnlyList<DuelRecord>> ListDuelsAsync(CancellationToken ct = default);   // oldest first

    Task ApplyStatsAsync(IEnumerable<StatIncrement> increments, CancellationToken ct = default);
    Task ClearStatsAsync(CancellationToken ct = default);
    // descending = higher value ranks first
    Task<IReadOnlyList<(string PlayerId, long Value)>> GetBoardAsync(string stat, string period, bool descending, int take, CancellationToken ct = default);
    Task<IReadOnlyList<(string Stat, string Period, long Value)>> GetPlayerStatsAsync(string playerId, CancellationToken ct = default);
}

// Replays = DuelRecorder-format text (see Yu-Gi-Oh-MP/DuelRecorder.h), one file per (duel, seat).
public interface IReplayStore
{
    Task WriteAsync(string duelId, int seat, string text, CancellationToken ct = default);
    Task<string?> ReadAsync(string duelId, int seat, CancellationToken ct = default);
}
