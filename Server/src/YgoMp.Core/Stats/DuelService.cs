using YgoMp.Core.Arbiter;
using YgoMp.Core.Lobby;

namespace YgoMp.Core.Stats;

// Owns a duel after its relay room closes: writes the DuelRecord, stores each seat's DuelRecorder log, and applies
// stats exactly once - only for Agreed duels that pass the anti-farming rules. Log uploads can arrive before or after
// the room closes, so both paths try to apply.
public sealed class DuelService(IStore store, IReplayStore replays, StatExtractor extractor, StatsRules rules)
{
    private readonly SemaphoreSlim m_Gate = new(1, 1);

    public async Task<DuelRecord> ConcludeAsync(Lobby.Lobby lobby, DuelArbiter arbiter, DateTimeOffset startedAt, CancellationToken ct)
    {
        var (status, result) = arbiter.Conclude();
        var seats = lobby.Seats
            .Select((playerId, seat) => (playerId, seat))
            .Where(s => s.playerId is not null)
            .Select(s => new DuelSeatRecord(s.seat, s.playerId!, arbiter.Results[s.seat]))
            .ToList();

        var notes = new List<string>();
        if (arbiter.FirstDesyncEvent is uint desync)
            notes.Add($"desync at event {desync}");
        if (arbiter.ViolationCount > 0)
            notes.Add($"{arbiter.ViolationCount} protocol violations");

        var duel = new DuelRecord(lobby.SessionId, lobby.SeatCount, lobby.Seed, lobby.CardSetHash, startedAt, DateTimeOffset.UtcNow,
            status, result?.Outcome, result is { WinnerSeat: not 0xFF } r ? r.WinnerSeat : null, result?.Reason, seats,
            notes.Count > 0 ? string.Join("; ", notes) : null);

        await m_Gate.WaitAsync(ct);
        try
        {
            await store.SaveDuelAsync(duel, ct);
            return await TryApplyStatsAsync(duel, ct);
        }
        finally
        {
            m_Gate.Release();
        }
    }

    // POST /duels/{id}/record. The caller must have sat in the duel (checked against the lobby or the saved record).
    public async Task AttachLogAsync(string duelId, int seat, string text, CancellationToken ct)
    {
        await replays.WriteAsync(duelId, seat, text, ct);

        await m_Gate.WaitAsync(ct);
        try
        {
            if (await store.GetDuelAsync(duelId, ct) is { } duel)
                await TryApplyStatsAsync(duel, ct);
        }
        finally
        {
            m_Gate.Release();
        }
    }

    // A duel one client played alone (vs the AI), streamed line by line over /live. Seat = the client's own seat.
    // finished = false: the client quit, started another duel or disconnected first; kept as Abandoned (replay saved, not counted).
    public async Task<DuelRecord> RecordLocalAsync(string playerId, string logText, DateTimeOffset startedAt, CancellationToken ct, bool finished = true)
    {
        var log = DuelLog.Parse(logText);
        int seat = log.LocalSeat;
        var end = log.End ?? new Dictionary<string, string>();
        int? outcome = DuelLog.Int(end, "outcome");
        // DUEL_END winner is an engine side (0/1); map it to a seat of that side's parity.
        int? winnerSide = DuelLog.Int(end, "winner");
        int? winnerSeat = winnerSide is int w && w is 0 or 1 ? (w == log.LocalSide ? seat : seat ^ 1) : null;

        var duel = new DuelRecord(Guid.CreateVersion7().ToString("N"), 2, 0, null, startedAt, DateTimeOffset.UtcNow,
            finished ? DuelStatus.Local : DuelStatus.Abandoned, winnerSeat is null ? Protocol.DuelOutcome.Draw : Protocol.DuelOutcome.Win, winnerSeat, DuelLog.Int(end, "reason"),
            [new DuelSeatRecord(seat, playerId, null)],
            $"mode={log.Begin.GetValueOrDefault("mode", "?")} outcome={outcome?.ToString() ?? "?"}");

        await replays.WriteAsync(duel.Id, seat, logText, ct);
        await m_Gate.WaitAsync(ct);
        try
        {
            await store.SaveDuelAsync(duel, ct);
            return await TryApplyStatsAsync(duel, ct);
        }
        finally
        {
            m_Gate.Release();
        }
    }

    // Recounts every saved duel from its replay with the current rules + card catalog (after either changes).
    // Returns (counted, skipped). Run with the server stopped.
    public async Task<(int Counted, int Skipped)> RebuildStatsAsync(CancellationToken ct)
    {
        await store.ClearStatsAsync(ct);
        int counted = 0, skipped = 0;
        foreach (var saved in await store.ListDuelsAsync(ct))
        {
            // Back to undecided, minus any earlier "stats skipped" note, then decide again.
            var notes = saved.Notes?.Split("; ").Where(n => !n.StartsWith("stats skipped")).ToArray();
            var duel = saved with { Stats = StatsState.Pending, Notes = notes is { Length: > 0 } ? string.Join("; ", notes) : null };
            duel = await TryApplyStatsAsync(duel, ct);
            if (duel.Stats == StatsState.Counted) counted++;
            else if (duel.Stats == StatsState.Skipped) skipped++;
            if (duel.Stats == StatsState.Pending)
                await store.SaveDuelAsync(duel, ct);
        }
        return (counted, skipped);
    }

    private async Task<DuelRecord> TryApplyStatsAsync(DuelRecord duel, CancellationToken ct)
    {
        if (duel.Stats != StatsState.Pending || !(duel.Status == DuelStatus.Agreed || (duel.Status == DuelStatus.Local && rules.CountLocalDuels)))
            return duel;

        // Any one seat's log will do: lockstep means every seat logged the same duel.
        DuelLog? log = null;
        foreach (var seat in duel.Seats)
            if (await replays.ReadAsync(duel.Id, seat.Seat, ct) is { } text)
            {
                log = DuelLog.Parse(text);
                break;
            }
        if (log is null)
            return duel;

        string? skip = null;
        if (log.Lines.Count < rules.MinEvents)
            skip = $"too short ({log.Lines.Count} events)";
        else if (duel.Seats.Count == 2)
        {
            var since = duel.EndedAt.UtcDateTime.Date;
            int already = await store.CountCountedDuelsBetweenAsync(duel.Seats[0].PlayerId, duel.Seats[1].PlayerId, since, ct);
            if (already >= rules.MaxDailyDuelsVsSameOpponent)
                skip = "daily cap vs this opponent reached";
        }

        if (skip is null)
            await store.ApplyStatsAsync(extractor.Extract(duel, log), ct);

        duel = duel with
        {
            Stats = skip is null ? StatsState.Counted : StatsState.Skipped,
            Notes = skip is null ? duel.Notes : string.Join("; ", new[] { duel.Notes, $"stats skipped: {skip}" }.Where(n => n is not null)),
        };
        await store.SaveDuelAsync(duel, ct);
        return duel;
    }
}
