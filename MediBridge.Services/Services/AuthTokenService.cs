using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediBridge.Core.Interfaces.Identity;

namespace MediBridge.Services.Services;

public sealed class AuthTokenService : IAuthTokenService
{
    private readonly string issuer;
    private readonly string audience;
    private readonly byte[] signingKey;
    private readonly int accessTokenMinutes;
    private readonly int refreshTokenDays;

    public AuthTokenService(string issuer, string audience, string signingKey, int accessTokenMinutes, int refreshTokenDays)
    {
        this.issuer = issuer;
        this.audience = audience;
        this.signingKey = Encoding.UTF8.GetBytes(signingKey);
        this.accessTokenMinutes = accessTokenMinutes;
        this.refreshTokenDays = refreshTokenDays;
    }

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(accessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(refreshTokenDays);

    public string CreateAccessToken(string userId, string email, string role)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId,
            ["email"] = email,
            ["name"] = email,
            ["role"] = role,
            ["iss"] = issuer,
            ["aud"] = audience,
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.AddMinutes(accessTokenMinutes).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        return CreateJwt(payload);
    }

    public (string PlaintextToken, string TokenHash) CreateRefreshToken()
    {
        return CreateRandomTokenPair();
    }

    public (string PlaintextToken, string TokenHash) CreateOneTimeToken()
    {
        return CreateRandomTokenPair();
    }

    public string HashToken(string plaintextToken)
    {
        return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextToken)));
    }

    private (string PlaintextToken, string TokenHash) CreateRandomTokenPair()
    {
        var plaintextToken = Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        return (plaintextToken, HashToken(plaintextToken));
    }

    private string CreateJwt(Dictionary<string, object> payload)
    {
        var headerJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        });

        var payloadJson = JsonSerializer.Serialize(payload);
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var body = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signature = Base64UrlEncode(CreateSignature($"{header}.{body}"));

        return $"{header}.{body}.{signature}";
    }

    private byte[] CreateSignature(string value)
    {
        using var hmac = new HMACSHA256(signingKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}