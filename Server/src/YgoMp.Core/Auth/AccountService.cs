using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace YgoMp.Core.Auth;

public enum SteamAuthMode
{
    // /auth/steam is refused; players use claim codes.
    Off,
    // Take the client's steamId at its word. LAN parties / friends-only servers ONLY: anyone can claim any SteamID.
    Trust,
    // Validate the ticket with ISteamUserAuth/AuthenticateUserTicket. Needs a PUBLISHER Web API key for the AppID,
    // which only the AppID's owner (Konami) has. Left in for completeness; expect to use Trust or OpenID instead.
    WebApi,
}

public sealed class SteamOptions
{
    public SteamAuthMode Mode { get; set; } = SteamAuthMode.Trust;
    public string? WebApiKey { get; set; }
    public uint AppId { get; set; } = 1150640;   // Legacy of the Duelist: Link Evolution
}

public sealed class AuthException(string message) : Exception(message);

// Turns a platform identity into one of our players. Steam players get one automatically; claim codes are the minting
// path for Epic/GOG/LAN copies (an admin mints them with `YgoMp.SelfHost mint`).
public sealed class AccountService(IStore store, SteamOptions steam, HttpClient http)
{
    public async Task<Player> LoginSteamAsync(string steamId, string ticketHex, CancellationToken ct)
    {
        if (!ulong.TryParse(steamId, out _))
            throw new AuthException("steamId is not a SteamID64");

        switch (steam.Mode)
        {
            case SteamAuthMode.Off:
                throw new AuthException("Steam login is disabled on this server - ask the host for a claim code");
            case SteamAuthMode.WebApi:
                await VerifySteamTicketAsync(steamId, ticketHex, ct);
                break;
            case SteamAuthMode.Trust:
                break;
        }

        var player = await store.FindPlayerAsync(Platforms.Steam, steamId, ct);
        if (player is null)
        {
            player = new Player(NewPlayerId(), $"Duelist-{steamId[^4..]}", Platforms.Steam, steamId, DateTimeOffset.UtcNow);
            await store.SavePlayerAsync(player, ct);
        }
        return EnsureNotBanned(player);
    }

    public async Task<Player> LoginClaimAsync(string code, string? displayName, CancellationToken ct)
    {
        code = NormalizeCode(code);
        var claim = await store.GetClaimCodeAsync(code, ct) ?? throw new AuthException("Unknown claim code");

        // A redeemed code is the player's login from then on (same code = same account).
        if (claim.BoundPlayerId is not null)
        {
            var existing = await store.GetPlayerAsync(claim.BoundPlayerId, ct) ?? throw new AuthException("Claim code's player is gone");
            return EnsureNotBanned(existing);
        }

        var name = Clean(displayName) ?? Clean(claim.DisplayName) ?? $"Duelist-{code[..4]}";
        var player = new Player(NewPlayerId(), name, Platforms.Claim, code, DateTimeOffset.UtcNow);
        if (!await store.RedeemClaimCodeAsync(code, player.Id, DateTimeOffset.UtcNow, ct))
            return await LoginClaimAsync(code, displayName, ct);   // lost a race: someone redeemed it first, log in as them
        await store.SavePlayerAsync(player, ct);
        return player;
    }

    public async Task<IReadOnlyList<ClaimCode>> MintClaimCodesAsync(int count, string? displayName, string createdBy, CancellationToken ct)
    {
        var codes = new List<ClaimCode>(count);
        for (int i = 0; i < count; i++)
        {
            var code = new ClaimCode(NewClaimCode(), displayName, createdBy, DateTimeOffset.UtcNow);
            await store.SaveClaimCodeAsync(code, ct);
            codes.Add(code);
        }
        return codes;
    }

    private async Task VerifySteamTicketAsync(string steamId, string ticketHex, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(steam.WebApiKey))
            throw new AuthException("Steam WebApi mode needs Steam:WebApiKey");

        var url = $"https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/?key={Uri.EscapeDataString(steam.WebApiKey)}&appid={steam.AppId}&ticket={Uri.EscapeDataString(ticketHex)}";
        using var doc = await http.GetFromJsonAsync<JsonDocument>(url, ct) ?? throw new AuthException("Steam returned nothing");
        if (!doc.RootElement.TryGetProperty("response", out var response)
            || !response.TryGetProperty("params", out var p)
            || p.GetProperty("result").GetString() != "OK"
            || p.GetProperty("steamid").GetString() != steamId)
            throw new AuthException("Steam rejected the ticket");
    }

    private static Player EnsureNotBanned(Player player) =>
        player.Banned ? throw new AuthException("This account is banned on this server") : player;

    private static string? Clean(string? name)
    {
        name = name?.Trim();
        return string.IsNullOrEmpty(name) ? null : name.Length > 24 ? name[..24] : name;
    }

    public static string NormalizeCode(string code) => code.Replace("-", "").Trim().ToUpperInvariant();

    private static string NewPlayerId() => Guid.CreateVersion7().ToString("N");

    // 16 chars of Crockford-ish base32 (no 0/O/1/I), shown as XXXX-XXXX-XXXX-XXXX. ~80 bits.
    private static string NewClaimCode() => RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 16);

    public static string FormatCode(string code) => string.Join('-', code.Chunk(4).Select(c => new string(c)));
}
