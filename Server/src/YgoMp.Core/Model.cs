using YgoMp.Protocol;

namespace YgoMp.Core;

public static class Platforms
{
    public const string Steam = "steam";
    public const string Claim = "claim";   // Epic / GOG / LAN: identity is the redeemed code
}

public record Player(string Id, string DisplayName, string Platform, string PlatformId, DateTimeOffset CreatedAt, bool Banned = false);

// A one-use code an admin minted. Redeeming it creates (or, with BoundPlayerId, logs back into) a player.
public record ClaimCode(string Code, string? DisplayName, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? RedeemedAt = null, string? BoundPlayerId = null);

public enum DuelStatus
{
    Agreed,     // every seat reported the same result and no desync: counts for stats and leaderboards
    Disputed,   // seats reported different results
    Desynced,   // state hashes diverged at some point
    Abandoned,  // someone left / not every seat reported a result
    Local,      // a duel one client played on its own (vs the AI) and streamed over /live; nothing to cross-check
}

public record DuelSeatRecord(int Seat, string PlayerId, DuelResultPayload? ReportedResult);

public record DuelRecord(
    string Id,
    int SeatCount,
    ulong Seed,
    string? CardSetHash,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    DuelStatus Status,
    DuelOutcome? Outcome,
    int? WinnerSeat,
    int? Reason,
    IReadOnlyList<DuelSeatRecord> Seats,
    string? Notes,
    StatsState Stats = StatsState.Pending);

public enum StatsState
{
    Pending,   // not decided yet (not Agreed, or no seat has uploaded its log)
    Counted,
    Skipped,   // decided not to count (anti-farming); reason in Notes
}

public enum StatAggregate
{
    Sum,
    Max,
    Min,
}

public record StatIncrement(string PlayerId, string Stat, string Period, long Value, StatAggregate Aggregate);
