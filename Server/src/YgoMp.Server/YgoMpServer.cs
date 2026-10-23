using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using YgoMp.Core;
using YgoMp.Core.Auth;
using YgoMp.Core.Lobby;
using YgoMp.Core.Stats;
using YgoMp.Protocol;

namespace YgoMp.Server;

public sealed class ServerOptions
{
    // Also the JWT issuer/audience. Change it and every issued token stops working.
    public string Name { get; set; } = "ygo-mp";
    // Shown to players when they connect.
    public string Motd { get; set; } = "Welcome!";
    public int TokenLifetimeDays { get; set; } = 30;
    // Holds signing.pem, boards.json, stats.json, card-catalog.json (+ whatever the store keeps there).
    public string DataDirectory { get; set; } = "data";
    public SteamOptions Steam { get; set; } = new();
}

// The whole service minus storage. A host (SelfHost now, Azure later) registers IStore + IReplayStore, then calls
// AddYgoMp / UseYgoMp. Routes match what Yu-Gi-Oh-MP already calls (Auth.cpp, Session.cpp, P2P.cpp).
public static class YgoMpServer
{
    public static IServiceCollection AddYgoMp(this IServiceCollection services, ServerOptions options)
    {
        Directory.CreateDirectory(options.DataDirectory);
        string DataFile(string name) => Path.Combine(options.DataDirectory, name);

        var minter = new TokenMinter(TokenMinter.LoadOrCreateKey(DataFile("signing.pem")), options.Name, TimeSpan.FromDays(options.TokenLifetimeDays));
        var rules = StatsJson.LoadOrDefault(DataFile("stats.json"), () => new StatsRules());
        var catalog = StatsJson.LoadOrDefault(DataFile("card-catalog.json"), () => new CardCatalog());
        var boards = StatsJson.LoadOrDefault(DataFile("boards.json"), DefaultBoards.Create);

        services.AddSingleton(options);
        services.AddSingleton(options.Steam);
        services.AddSingleton(minter);
        services.AddSingleton(rules);
        services.AddSingleton(catalog);
        services.AddSingleton<IReadOnlyList<BoardDefinition>>(boards);
        services.AddSingleton<StatExtractor>();
        services.AddSingleton<DuelService>();
        services.AddSingleton<LobbyService>();
        services.AddSingleton<RelayRegistry>();
        services.AddSingleton<LiveHub>();
        services.AddSingleton(new ServerBanList(DataFile("banlist.json")));
        services.AddHttpClient<AccountService>();
        services.AddHostedService<LobbySweeper>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(jwt =>
        {
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = options.Name,
                ValidAudience = options.Name,
                IssuerSigningKey = minter.SigningKey,
                NameClaimType = "name",
            };
        });
        services.AddAuthorization();
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    public static WebApplication UseYgoMp(this WebApplication app)
    {
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { ok = true }));

        // ---- server info: what the client reads right after connecting (name, message, the server's ban list) ----
        app.MapGet("/server/info", (ServerOptions options, ServerBanList banList, LiveHub live) => Results.Ok(new
        {
            name = options.Name,
            motd = options.Motd,
            version = typeof(YgoMpServer).Assembly.GetName().Version?.ToString(),
            steamLogin = options.Steam.Mode.ToString(),
            online = live.Online().Count,
            banList = banList.Current(),
        }));

        app.MapGet("/online", (LiveHub live) => live.Online());

        // ---- live: the game client's always-on connection (presence + streamed Duels.log lines) ----
        app.Map("/live", async (HttpContext http, IStore store, LiveHub live) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
                return Results.BadRequest(new { error = "WebSocket only" });
            if (await store.GetPlayerAsync(PlayerId(http.User), http.RequestAborted) is not { Banned: false } player)
                return Results.Forbid();
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            await live.RunAsync(player, socket, http.RequestAborted);
            return Results.Empty;
        }).RequireAuthorization();

        // ---- auth ----
        app.MapPost("/auth/steam", async (SteamAuthRequest body, AccountService accounts, TokenMinter minter, CancellationToken ct) =>
            await Login(() => accounts.LoginSteamAsync(body.SteamId, body.Ticket, ct), minter));

        app.MapPost("/auth/claim", async (ClaimAuthRequest body, AccountService accounts, TokenMinter minter, CancellationToken ct) =>
            await Login(() => accounts.LoginClaimAsync(body.Code, body.DisplayName, ct), minter));

        app.MapGet("/me", async (ClaimsPrincipal user, IStore store, CancellationToken ct) =>
            await store.GetPlayerAsync(PlayerId(user), ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .RequireAuthorization();

        // ---- lobby ----
        app.MapPost("/lobby/create", (LobbyCreateRequest body, ClaimsPrincipal user, LobbyService lobbies) =>
        {
            try
            {
                var lobby = lobbies.Create(PlayerId(user), body.SeatCount, body.CardSetHash);
                return Results.Ok(new LobbySessionResponse(lobby.SessionId, lobby.Code, 0, lobby.SeatCount, lobby.Seed));
            }
            catch (LobbyException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/lobby/join", (LobbyJoinRequest body, ClaimsPrincipal user, LobbyService lobbies) =>
        {
            try
            {
                var (lobby, seat) = lobbies.Join(PlayerId(user), body.LobbyCode, body.CardSetHash);
                return Results.Ok(new LobbySessionResponse(lobby.SessionId, lobby.Code, seat, lobby.SeatCount, lobby.Seed));
            }
            catch (LobbyException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization();

        // ---- relay (P2P.cpp connects here with Authorization: Bearer) ----
        app.Map("/relay/{sessionId}", async (string sessionId, HttpContext http, LobbyService lobbies, RelayRegistry rooms) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
                return Results.BadRequest(new { error = "WebSocket only" });
            if (lobbies.FindSession(sessionId) is not { } lobby)
                return Results.NotFound(new { error = "No such session" });
            int seat = lobby.SeatOf(PlayerId(http.User));
            if (seat < 0)
                return Results.Forbid();

            var room = rooms.GetOrCreate(lobby);
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            await room.RunSeatAsync(seat, socket, http.RequestAborted);
            await rooms.OnSeatLeftAsync(room);
            return Results.Empty;
        }).RequireAuthorization();

        // ---- duel records (each seat uploads its DuelRecorder log after the duel) ----
        app.MapPost("/duels/{duelId}/record", async (string duelId, HttpRequest request, ClaimsPrincipal user, LobbyService lobbies,
            IStore store, DuelService duels, CancellationToken ct) =>
        {
            var playerId = PlayerId(user);
            int seat = lobbies.FindSession(duelId)?.SeatOf(playerId)
                ?? (await store.GetDuelAsync(duelId, ct))?.Seats.FirstOrDefault(s => s.PlayerId == playerId)?.Seat
                ?? -1;
            if (seat < 0)
                return Results.Forbid();
            if (request.ContentLength > 8 * 1024 * 1024)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            using var reader = new StreamReader(request.Body);
            await duels.AttachLogAsync(duelId, seat, await reader.ReadToEndAsync(ct), ct);
            return Results.Ok();
        }).RequireAuthorization();

        app.MapGet("/duels/{duelId}", async (string duelId, IStore store, CancellationToken ct) =>
            await store.GetDuelAsync(duelId, ct) is { } duel ? Results.Ok(duel) : Results.NotFound());

        // ---- leaderboards (public) ----
        app.MapGet("/boards", (IReadOnlyList<BoardDefinition> boards) =>
            boards.Select(b => new BoardInfo(b.Id, b.Title, b.Stat, b.Order, b.Periods)));

        app.MapGet("/boards/{id}", async (string id, string? period, int? take, IReadOnlyList<BoardDefinition> boards, IStore store, CancellationToken ct) =>
        {
            if (boards.FirstOrDefault(b => b.Id == id) is not { } board)
                return Results.NotFound();
            var key = Periods.Resolve(period, DateTimeOffset.UtcNow);
            var rows = await store.GetBoardAsync(board.Stat, key, board.Descending, Math.Clamp(take ?? 100, 1, 500), ct);
            var entries = new List<BoardEntry>(rows.Count);
            foreach (var (playerId, value) in rows)
                entries.Add(new BoardEntry(entries.Count + 1, playerId, (await store.GetPlayerAsync(playerId, ct))?.DisplayName ?? "?", value));
            return Results.Ok(new BoardResponse(board.Id, board.Title, key, entries));
        });

        app.MapGet("/players/{playerId}/stats", async (string playerId, IStore store, CancellationToken ct) =>
            (await store.GetPlayerStatsAsync(playerId, ct)).Select(s => new PlayerStat(s.Stat, s.Period, s.Value)));

        return app;
    }

    private static string PlayerId(ClaimsPrincipal user) => user.FindFirstValue("sub") ?? throw new InvalidOperationException("token has no sub");

    private static async Task<IResult> Login(Func<Task<Player>> login, TokenMinter minter)
    {
        try
        {
            var player = await login();
            var (token, expires) = minter.Mint(player);
            return Results.Ok(new TokenResponse(token, player.Id, player.DisplayName, expires));
        }
        catch (AuthException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
        }
    }
}

internal sealed class LobbySweeper(LobbyService lobbies, ILogger<LobbySweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(ct))
            if (lobbies.SweepStale(TimeSpan.FromMinutes(15)) is var n and > 0)
                logger.LogInformation("Swept {Count} stale lobbies", n);
    }
}
