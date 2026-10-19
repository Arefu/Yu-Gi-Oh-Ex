using YgoMp.Core;
using YgoMp.Core.Auth;
using YgoMp.SelfHost;
using YgoMp.Server;

// Self-hosted Yu-Gi-Oh-MP server: one process, one SQLite file, replays on disk. Runs on Windows or Linux
// (VPS, LAN party box, home server). Settings come from appsettings.json, YGOMP_ environment variables, or --Key=value.
//
//   YgoMp.SelfHost                       run the server
//   YgoMp.SelfHost mint [count] [name]   mint claim codes (Epic/GOG/LAN players redeem them at /auth/claim)
//   YgoMp.SelfHost players               list players
//   YgoMp.SelfHost ban <playerId>        ban / unban a player
//   YgoMp.SelfHost unban <playerId>
//   YgoMp.SelfHost rebuild-stats         recount every saved duel (after changing stats.json or card-catalog.json); server stopped

string command = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "run";
var rest = command == "run" ? args : args[1..];

var builder = WebApplication.CreateBuilder(rest.Where(a => a.StartsWith("--")).ToArray());
builder.Configuration.AddEnvironmentVariables("YGOMP_");
var options = builder.Configuration.GetSection("YgoMp").Get<ServerOptions>() ?? new ServerOptions();
options.DataDirectory = Path.GetFullPath(options.DataDirectory, AppContext.BaseDirectory);
Directory.CreateDirectory(options.DataDirectory);

var store = new SqliteStore(Path.Combine(options.DataDirectory, "ygomp.db"));
var positional = rest.Where(a => !a.StartsWith("--")).ToArray();

switch (command)
{
    case "run":
        builder.Services.AddSingleton<IStore>(store);
        builder.Services.AddSingleton<IReplayStore>(new FileReplayStore(Path.Combine(options.DataDirectory, "replays")));
        builder.Services.AddYgoMp(options);

        var app = builder.Build();
        app.UseYgoMp();
        app.Logger.LogInformation("Data directory: {Dir}", options.DataDirectory);
        app.Logger.LogInformation("Steam login mode: {Mode}", options.Steam.Mode);
        if (options.Steam.Mode == SteamAuthMode.Trust)
            app.Logger.LogWarning("Steam Trust mode: anyone can log in as any SteamID. Fine for LAN/friends, not for a public server.");
        await app.RunAsync();
        return 0;

    case "mint":
    {
        int count = positional.Length > 0 && int.TryParse(positional[0], out int n) ? Math.Clamp(n, 1, 1000) : 1;
        string? name = positional.Length > 1 ? positional[1] : null;
        var accounts = new AccountService(store, options.Steam, new HttpClient());
        foreach (var code in await accounts.MintClaimCodesAsync(count, name, $"cli:{Environment.UserName}", CancellationToken.None))
            Console.WriteLine(AccountService.FormatCode(code.Code));
        return 0;
    }

    case "players":
        foreach (var p in await store.ListPlayersAsync())
            Console.WriteLine($"{p.Id}  {p.Platform,-6} {p.DisplayName}{(p.Banned ? "  [BANNED]" : "")}");
        return 0;

    case "ban":
    case "unban":
    {
        if (positional.Length == 0 || await store.GetPlayerAsync(positional[0]) is not { } player)
        {
            Console.Error.WriteLine("Unknown player id (see `players`).");
            return 1;
        }
        await store.SavePlayerAsync(player with { Banned = command == "ban" });
        Console.WriteLine($"{player.DisplayName}: {(command == "ban" ? "banned" : "unbanned")} (existing tokens stay valid until they expire)");
        return 0;
    }

    case "rebuild-stats":
    {
        builder.Services.AddSingleton<IStore>(store);
        builder.Services.AddSingleton<IReplayStore>(new FileReplayStore(Path.Combine(options.DataDirectory, "replays")));
        builder.Services.AddYgoMp(options);
        using var services = builder.Services.BuildServiceProvider();
        var (counted, skipped) = await services.GetRequiredService<YgoMp.Core.Stats.DuelService>().RebuildStatsAsync(CancellationToken.None);
        Console.WriteLine($"Stats rebuilt: {counted} duels counted, {skipped} skipped.");
        return 0;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Commands: run, mint, players, ban, unban, rebuild-stats.");
        return 1;
}
