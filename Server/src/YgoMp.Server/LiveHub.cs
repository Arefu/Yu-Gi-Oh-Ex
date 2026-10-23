using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YgoMp.Core;
using YgoMp.Core.Stats;

namespace YgoMp.Server;

// /live: one WebSocket per signed-in game client (the "Connect to Server" button in Yu-Gi-Oh-MP). Text messages only:
//   client -> server   a JSON object ({"type":"hello", ...}) or one Duels.log line (DuelRecorder.h format), as written
//   server -> client   JSON: {"type":"welcome"|"notice"|"duelSaved", ...}
// The server keeps who is online and, per client, the duel in progress (live LP); at DUEL_END the duel is saved as a
// DuelStatus.Local record + replay and counted (StatsRules.CountLocalDuels).
public sealed class LiveClient
{
    private readonly SemaphoreSlim m_SendLock = new(1, 1);

    internal LiveClient(Player player, WebSocket socket)
    {
        Player = player;
        Socket = socket;
    }

    public Player Player { get; }
    internal WebSocket Socket { get; }
    public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;
    public string? ClientVersion { get; internal set; }
    public string? Mode { get; internal set; }            // DUEL_BEGIN mode= of the duel in progress, null when not in a duel
    public string Lp { get; internal set; } = "";         // last "lp=" seen (side0/side1)
    public int Events { get; internal set; }

    internal StringBuilder? DuelLog { get; set; }
    internal Dictionary<int, int> SlotToCard { get; } = new();   // from the duel's CARDS lines
    internal DateTimeOffset DuelStartedAt { get; set; }

    internal async Task SendAsync(object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);
        await m_SendLock.WaitAsync(ct);
        try
        {
            if (Socket.State == WebSocketState.Open)
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            m_SendLock.Release();
        }
    }
}

public sealed record OnlinePlayer(string PlayerId, string DisplayName, string Platform, DateTimeOffset ConnectedAt, string? ClientVersion,
    bool InDuel, string? Mode, string Lp, int Events);

public sealed class LiveHub(DuelService duels, ServerOptions options, CardCatalog catalog, StatsRules rules, ILogger<LiveHub> logger)
{
    private const int MaxDuelLogBytes = 8 * 1024 * 1024;
    private readonly ConcurrentDictionary<LiveClient, byte> m_Clients = new();

    public IReadOnlyList<OnlinePlayer> Online() => m_Clients.Keys
        .Select(c => new OnlinePlayer(c.Player.Id, c.Player.DisplayName, c.Player.Platform, c.ConnectedAt, c.ClientVersion,
            c.Mode is not null, c.Mode, c.Lp, c.Events))
        .OrderBy(p => p.ConnectedAt)
        .ToList();

    public async Task RunAsync(Player player, WebSocket socket, CancellationToken ct)
    {
        var client = new LiveClient(player, socket);
        m_Clients[client] = 0;
        logger.LogInformation("CONNECT {Name} ({Platform}, {Id}) - {Count} online", player.DisplayName, player.Platform, player.Id, m_Clients.Count);
        await client.SendAsync(new { type = "welcome", server = options.Name, motd = options.Motd, displayName = player.DisplayName, online = m_Clients.Count }, ct);
        await BroadcastAsync(client, new { type = "notice", text = $"{player.DisplayName} connected" }, ct);

        var buffer = new byte[64 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > 1024 * 1024)
                        throw new InvalidDataException("message too large");
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                    break;
                }
                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                // One message may carry several lines (the client batches).
                foreach (var line in Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length).Split('\n'))
                    if (line.Length > 0)
                        await OnLineAsync(client, line.TrimEnd('\r'), ct);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException)
        {
            logger.LogInformation("{Name}: socket ended ({Message})", player.DisplayName, ex.Message);
        }
        finally
        {
            m_Clients.TryRemove(client, out _);
        }

        if (client.DuelLog is not null)
            await SaveUnfinishedAsync(client, "disconnected mid-duel", CancellationToken.None);
        logger.LogInformation("DISCONNECT {Name} - {Count} online", player.DisplayName, m_Clients.Count);
        await BroadcastAsync(client, new { type = "notice", text = $"{player.DisplayName} disconnected" }, CancellationToken.None);
    }

    private async Task OnLineAsync(LiveClient client, string line, CancellationToken ct)
    {
        if (line.StartsWith('{'))
        {
            OnHello(client, line);
            return;
        }

        int space = line.IndexOf(' ');
        var keyword = space < 0 ? line : line[..space];
        var name = client.Player.DisplayName;

        if (keyword == "DUEL_BEGIN")
        {
            if (client.DuelLog is not null)
                await SaveUnfinishedAsync(client, "a new duel started", ct);
            client.DuelLog = new StringBuilder();
            client.DuelStartedAt = DateTimeOffset.UtcNow;
            client.Events = 0;
            client.Lp = "";
            client.SlotToCard.Clear();
            var begin = Core.Stats.DuelLog.Parse(line).Begin;
            client.Mode = begin.GetValueOrDefault("mode", "?");
            logger.LogInformation("DUEL_BEGIN {Name}: mode={Mode} online={Online} tag={Tag}", name, client.Mode,
                begin.GetValueOrDefault("online", "0"), begin.GetValueOrDefault("tag", "0"));
        }

        if (client.DuelLog is null)
            return;   // lines outside a duel (e.g. connected mid-duel): nothing to attach them to

        if (client.DuelLog.Length + line.Length < MaxDuelLogBytes)
            client.DuelLog.Append(line).Append('\n');
        client.Events++;

        switch (keyword)
        {
            case "CARDS":
                foreach (var (slot, card) in Core.Stats.DuelLog.Parse(line).SlotToCard)
                    client.SlotToCard[slot] = card;
                break;
            case "MOVE":
            {
                var f = Core.Stats.DuelLog.Parse(line).Lines[0].Fields;
                if (!f.TryGetValue("kind", out var kind) || rules.CategoryOf(kind) is not { } category || category == "draw")
                    break;   // draws are counted, just not logged one by one
                int? slot = Core.Stats.DuelLog.Int(f, "slot"), from = Core.Stats.DuelLog.Int(f, "from"), to = Core.Stats.DuelLog.Int(f, "to");
                int card = slot is int s && client.SlotToCard.TryGetValue(s, out int id) ? id : 0;
                var info = catalog.Get(card);
                // Konami ids only; names get resolved later (client/site side), not by the server.
                logger.LogInformation("{Category} {Name}: card {Id}{Detail} {From} -> {To} [side {Side}]",
                    category.ToUpperInvariant(), name, card,
                    info is null ? "" : $" ({info.Frame}{(info.Race is null ? "" : " " + info.Race)})",
                    from is int fr ? StatExtractor.ZoneName(fr) : "?", to is int t ? StatExtractor.ZoneName(t) : "?", to is int ts ? ts & 1 : -1);
                break;
            }
            case "LP":
            {
                var f = Core.Stats.DuelLog.Parse(line).Lines[0].Fields;
                client.Lp = f.GetValueOrDefault("lp", client.Lp);
                logger.LogInformation("LP {Name}: side {Side} {Kind} {Amount} -> {Lp}", name, f.GetValueOrDefault("side"), f.GetValueOrDefault("kind"),
                    f.GetValueOrDefault("amount"), client.Lp);
                break;
            }
            case "CHAIN":
                logger.LogDebug("{Name}: {Line}", name, line);
                break;
            case "DUEL_END":
            {
                var f = Core.Stats.DuelLog.Parse(line).Lines[0].Fields;
                client.Lp = f.GetValueOrDefault("lp", client.Lp);
                var text = client.DuelLog.ToString();
                client.DuelLog = null;
                client.Mode = null;
                var duel = await duels.RecordLocalAsync(client.Player.Id, text, client.DuelStartedAt, ct);
                logger.LogInformation("DUEL_END {Name}: winner side {Winner}, reason {Reason}, lp {Lp}, {Events} events -> duel {Duel} ({Stats})",
                    name, f.GetValueOrDefault("winner"), f.GetValueOrDefault("reason"), client.Lp, client.Events, duel.Id, duel.Stats);
                await client.SendAsync(new { type = "duelSaved", duelId = duel.Id, stats = duel.Stats.ToString(), notes = duel.Notes }, ct);
                break;
            }
        }
    }

    private async Task SaveUnfinishedAsync(LiveClient client, string why, CancellationToken ct)
    {
        var text = client.DuelLog!.ToString();
        client.DuelLog = null;
        client.Mode = null;
        var duel = await duels.RecordLocalAsync(client.Player.Id, text, client.DuelStartedAt, ct, finished: false);
        logger.LogInformation("DUEL_ABANDONED {Name}: {Why} after {Events} events -> duel {Duel} (kept, not counted)", client.Player.DisplayName, why, client.Events, duel.Id);
    }

    private void OnHello(LiveClient client, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("version", out var version))
                client.ClientVersion = version.GetString();
            logger.LogInformation("HELLO {Name}: {Json}", client.Player.DisplayName, json.Length > 300 ? json[..300] : json);
        }
        catch (JsonException)
        {
            logger.LogWarning("{Name}: bad JSON message", client.Player.DisplayName);
        }
    }

    private async Task BroadcastAsync(LiveClient except, object message, CancellationToken ct)
    {
        foreach (var other in m_Clients.Keys)
            if (other != except)
                await other.SendAsync(message, ct);
    }
}
