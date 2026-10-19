using System.Text.Json;
using Microsoft.Data.Sqlite;
using YgoMp.Core;
using YgoMp.Core.Stats;

namespace YgoMp.SelfHost;

// One SQLite file. Fine for a LAN party or a small VPS community; the Azure host swaps this for Table Storage.
public sealed class SqliteStore : IStore
{
    private readonly string m_ConnectionString;

    public SqliteStore(string path)
    {
        m_ConnectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
        using var db = Open();
        Execute(db, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS players (
                id TEXT PRIMARY KEY, display_name TEXT NOT NULL, platform TEXT NOT NULL, platform_id TEXT NOT NULL,
                created_at TEXT NOT NULL, banned INTEGER NOT NULL DEFAULT 0, UNIQUE (platform, platform_id));
            CREATE TABLE IF NOT EXISTS claim_codes (
                code TEXT PRIMARY KEY, display_name TEXT, created_by TEXT NOT NULL, created_at TEXT NOT NULL,
                redeemed_at TEXT, bound_player_id TEXT);
            CREATE TABLE IF NOT EXISTS duels (
                id TEXT PRIMARY KEY, ended_at TEXT NOT NULL, status TEXT NOT NULL, stats TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS duel_seats (
                duel_id TEXT NOT NULL, seat INTEGER NOT NULL, player_id TEXT NOT NULL, PRIMARY KEY (duel_id, seat));
            CREATE INDEX IF NOT EXISTS duel_seats_player ON duel_seats (player_id);
            CREATE TABLE IF NOT EXISTS stats (
                player_id TEXT NOT NULL, stat TEXT NOT NULL, period TEXT NOT NULL, value INTEGER NOT NULL,
                PRIMARY KEY (player_id, stat, period));
            CREATE INDEX IF NOT EXISTS stats_board ON stats (stat, period, value);
            """);
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(m_ConnectionString);
        db.Open();
        return db;
    }

    private static void Execute(SqliteConnection db, string sql, params (string, object?)[] args)
    {
        using var cmd = Command(db, sql, args);
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static string Time(DateTimeOffset t) => t.UtcDateTime.ToString("O");
    private static DateTimeOffset Time(string s) => DateTimeOffset.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);

    // ---- players ----
    private static Player ReadPlayer(SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), Time(r.GetString(4)), r.GetInt64(5) != 0);

    private const string PlayerColumns = "id, display_name, platform, platform_id, created_at, banned";

    private Task<Player?> QueryPlayer(string where, params (string, object?)[] args)
    {
        using var db = Open();
        using var cmd = Command(db, $"SELECT {PlayerColumns} FROM players WHERE {where}", args);
        using var r = cmd.ExecuteReader();
        return Task.FromResult(r.Read() ? ReadPlayer(r) : null);
    }

    public Task<Player?> GetPlayerAsync(string id, CancellationToken ct = default) => QueryPlayer("id = $id", ("$id", id));

    public Task<Player?> FindPlayerAsync(string platform, string platformId, CancellationToken ct = default) =>
        QueryPlayer("platform = $p AND platform_id = $pid", ("$p", platform), ("$pid", platformId));

    public Task SavePlayerAsync(Player p, CancellationToken ct = default)
    {
        using var db = Open();
        Execute(db, $"INSERT OR REPLACE INTO players ({PlayerColumns}) VALUES ($id, $n, $p, $pid, $c, $b)",
            ("$id", p.Id), ("$n", p.DisplayName), ("$p", p.Platform), ("$pid", p.PlatformId), ("$c", Time(p.CreatedAt)), ("$b", p.Banned ? 1 : 0));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Player>> ListPlayersAsync(CancellationToken ct = default)
    {
        using var db = Open();
        using var cmd = Command(db, $"SELECT {PlayerColumns} FROM players ORDER BY created_at");
        using var r = cmd.ExecuteReader();
        var list = new List<Player>();
        while (r.Read())
            list.Add(ReadPlayer(r));
        return Task.FromResult<IReadOnlyList<Player>>(list);
    }

    // ---- claim codes ----
    public Task SaveClaimCodeAsync(ClaimCode c, CancellationToken ct = default)
    {
        using var db = Open();
        Execute(db, "INSERT OR REPLACE INTO claim_codes VALUES ($code, $n, $by, $at, $r, $pid)",
            ("$code", c.Code), ("$n", c.DisplayName), ("$by", c.CreatedBy), ("$at", Time(c.CreatedAt)),
            ("$r", c.RedeemedAt is { } r ? Time(r) : null), ("$pid", c.BoundPlayerId));
        return Task.CompletedTask;
    }

    public Task<ClaimCode?> GetClaimCodeAsync(string code, CancellationToken ct = default)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT code, display_name, created_by, created_at, redeemed_at, bound_player_id FROM claim_codes WHERE code = $c", ("$c", code));
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return Task.FromResult<ClaimCode?>(null);
        return Task.FromResult<ClaimCode?>(new ClaimCode(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), Time(r.GetString(3)),
            r.IsDBNull(4) ? null : Time(r.GetString(4)), r.IsDBNull(5) ? null : r.GetString(5)));
    }

    public Task<bool> RedeemClaimCodeAsync(string code, string playerId, DateTimeOffset at, CancellationToken ct = default)
    {
        using var db = Open();
        using var cmd = Command(db, "UPDATE claim_codes SET redeemed_at = $at, bound_player_id = $pid WHERE code = $c AND bound_player_id IS NULL",
            ("$at", Time(at)), ("$pid", playerId), ("$c", code));
        return Task.FromResult(cmd.ExecuteNonQuery() == 1);
    }

    // ---- duels ----
    public Task SaveDuelAsync(DuelRecord duel, CancellationToken ct = default)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        Execute(db, "INSERT OR REPLACE INTO duels VALUES ($id, $end, $status, $stats, $json)",
            ("$id", duel.Id), ("$end", Time(duel.EndedAt)), ("$status", duel.Status.ToString()), ("$stats", duel.Stats.ToString()),
            ("$json", JsonSerializer.Serialize(duel, StatsJson.Options)));
        foreach (var seat in duel.Seats)
            Execute(db, "INSERT OR REPLACE INTO duel_seats VALUES ($id, $seat, $pid)", ("$id", duel.Id), ("$seat", seat.Seat), ("$pid", seat.PlayerId));
        tx.Commit();
        return Task.CompletedTask;
    }

    public Task<DuelRecord?> GetDuelAsync(string id, CancellationToken ct = default)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT json FROM duels WHERE id = $id", ("$id", id));
        return Task.FromResult(cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<DuelRecord>(json, StatsJson.Options) : null);
    }

    public Task<int> CountCountedDuelsBetweenAsync(string playerA, string playerB, DateTimeOffset since, CancellationToken ct = default)
    {
        using var db = Open();
        using var cmd = Command(db, """
            SELECT COUNT(*) FROM duels d
            WHERE d.stats = 'Counted' AND d.ended_at >= $since
              AND EXISTS (SELECT 1 FROM duel_seats s WHERE s.duel_id = d.id AND s.player_id = $a)
              AND EXISTS (SELECT 1 FROM duel_seats s WHERE s.duel_id = d.id AND s.player_id = $b)
            """, ("$since", Time(since)), ("$a", playerA), ("$b", playerB));
        return Task.FromResult(Convert.ToInt32(cmd.ExecuteScalar()));
    }

    public Task<IReadOnlyList<DuelRecord>> ListDuelsAsync(CancellationToken ct = default)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT json FROM duels ORDER BY ended_at");
        using var r = cmd.ExecuteReader();
        var list = new List<DuelRecord>();
        while (r.Read())
            if (JsonSerializer.Deserialize<DuelRecord>(r.GetString(0), StatsJson.Options) is { } duel)
                list.Add(duel);
        return Task.FromResult<IReadOnlyList<DuelRecord>>(list);
    }

    // ---- stats ----
    public Task ClearStatsAsync(CancellationToken ct = default)
    {
        using var db = Open();
        Execute(db, "DELETE FROM stats");
        return Task.CompletedTask;
    }

    public Task ApplyStatsAsync(IEnumerable<StatIncrement> increments, CancellationToken ct = default)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        foreach (var inc in increments)
        {
            var merge = inc.Aggregate switch
            {
                StatAggregate.Max => "max(value, excluded.value)",
                StatAggregate.Min => "min(value, excluded.value)",
                _ => "value + excluded.value",
            };
            Execute(db, $"INSERT INTO stats VALUES ($p, $s, $per, $v) ON CONFLICT (player_id, stat, period) DO UPDATE SET value = {merge}",
                ("$p", inc.PlayerId), ("$s", inc.Stat), ("$per", inc.Period), ("$v", inc.Value));
        }
        tx.Commit();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string PlayerId, long Value)>> GetBoardAsync(string stat, string period, bool descending, int take, CancellationToken ct = default)
    {
        using var db = Open();
        using var cmd = Command(db, $"""
            SELECT s.player_id, s.value FROM stats s JOIN players p ON p.id = s.player_id
            WHERE s.stat = $s AND s.period = $per AND p.banned = 0
            ORDER BY s.value {(descending ? "DESC" : "ASC")} LIMIT $take
            """, ("$s", stat), ("$per", period), ("$take", take));
        using var r = cmd.ExecuteReader();
        var rows = new List<(string, long)>();
        while (r.Read())
            rows.Add((r.GetString(0), r.GetInt64(1)));
        return Task.FromResult<IReadOnlyList<(string, long)>>(rows);
    }

    public Task<IReadOnlyList<(string Stat, string Period, long Value)>> GetPlayerStatsAsync(string playerId, CancellationToken ct = default)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT stat, period, value FROM stats WHERE player_id = $p ORDER BY stat, period", ("$p", playerId));
        using var r = cmd.ExecuteReader();
        var rows = new List<(string, string, long)>();
        while (r.Read())
            rows.Add((r.GetString(0), r.GetString(1), r.GetInt64(2)));
        return Task.FromResult<IReadOnlyList<(string, string, long)>>(rows);
    }
}

public sealed class FileReplayStore(string directory) : IReplayStore
{
    private string PathFor(string duelId, int seat)
    {
        if (duelId.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("bad duel id", nameof(duelId));
        return Path.Combine(directory, duelId[..2], $"{duelId}.seat{seat}.log");
    }

    public async Task WriteAsync(string duelId, int seat, string text, CancellationToken ct = default)
    {
        var path = PathFor(duelId, seat);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, ct);
    }

    public async Task<string?> ReadAsync(string duelId, int seat, CancellationToken ct = default)
    {
        var path = PathFor(duelId, seat);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
    }
}
