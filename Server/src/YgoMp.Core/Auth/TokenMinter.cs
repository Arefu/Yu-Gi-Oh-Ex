using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace YgoMp.Core.Auth;

// Every login path (Steam, claim code, later Epic/Discord) ends here: the server signs its own ES256 JWT, and that
// token is the only thing the relay and the API trust. Each server has its own key, so tokens don't cross servers.
public sealed class TokenMinter
{
    public const string PlatformClaim = "platform";

    private readonly SigningCredentials m_Credentials;
    private readonly JsonWebTokenHandler m_Handler = new();

    public TokenMinter(ECDsa key, string issuer, TimeSpan lifetime)
    {
        SigningKey = new ECDsaSecurityKey(key) { KeyId = issuer };
        m_Credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.EcdsaSha256);
        Issuer = issuer;
        Lifetime = lifetime;
    }

    public ECDsaSecurityKey SigningKey { get; }
    public string Issuer { get; }
    public TimeSpan Lifetime { get; }

    public (string Token, DateTimeOffset ExpiresAt) Mint(Player player)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now + Lifetime;
        var token = m_Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = m_Credentials,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, player.Id),
                new Claim(JwtRegisteredClaimNames.Name, player.DisplayName),
                new Claim(PlatformClaim, player.Platform),
            ]),
        });
        return (token, expires);
    }

    // Loads the PEM key at path, or creates one (P-256) on first run.
    public static ECDsa LoadOrCreateKey(string path)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(path))
        {
            key.ImportFromPem(File.ReadAllText(path));
            return key;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
        return key;
    }
}
