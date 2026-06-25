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
    private readonly byte[] oneTimeSecretHashingKey;
    private readonly int accessTokenMinutes;
    private readonly int refreshTokenDays;

    public AuthTokenService(
        string issuer,
        string audience,
        string signingKey,
        string oneTimeSecretHashingKey,
        int accessTokenMinutes,
        int refreshTokenDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(oneTimeSecretHashingKey);

        if (accessTokenMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accessTokenMinutes), "Access token lifetime must be positive.");
        }

        if (refreshTokenDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshTokenDays), "Refresh token lifetime must be positive.");
        }

        this.issuer = issuer;
        this.audience = audience;
        this.signingKey = Encoding.UTF8.GetBytes(signingKey);
        this.oneTimeSecretHashingKey = Encoding.UTF8.GetBytes(oneTimeSecretHashingKey);
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

    public string CreateNumericCode(int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Code length must be positive.");
        }

        var builder = new StringBuilder(length);
        for (var index = 0; index < length; index++)
        {
            builder.Append(RandomNumberGenerator.GetInt32(0, 10));
        }

        return builder.ToString();
    }

    public string HashOneTimeSecret(string normalizedDestination, string plaintextSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedDestination);
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextSecret);

        using var hmac = new HMACSHA256(oneTimeSecretHashingKey);
        var payload = Encoding.UTF8.GetBytes($"{normalizedDestination}\u001F{plaintextSecret}");
        return Base64UrlEncode(hmac.ComputeHash(payload));
    }

    public bool VerifyOneTimeSecret(string normalizedDestination, string plaintextSecret, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(normalizedDestination)
            || string.IsNullOrWhiteSpace(plaintextSecret)
            || string.IsNullOrWhiteSpace(expectedHash))
        {
            return false;
        }

        byte[] expectedBytes;
        try
        {
            expectedBytes = Base64UrlDecode(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualBytes = Base64UrlDecode(HashOneTimeSecret(normalizedDestination, plaintextSecret));
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
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

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value
            .Replace('-', '+')
            .Replace('_', '/');

        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        return Convert.FromBase64String(base64);
    }
}
