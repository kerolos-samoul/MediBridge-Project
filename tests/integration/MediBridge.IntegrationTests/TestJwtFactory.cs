using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MediBridge.IntegrationTests;

internal static class TestJwtFactory
{
    private const string Issuer = "MediBridge.IntegrationTests";
    private const string Audience = "MediBridge.IntegrationTests.ApiClients";
    private const string SigningKey = "IntegrationTestSigningKey-ReplaceBeforeProduction-32Chars";

    public static string CreateToken(string role, string? userId = null)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId ?? $"user-{Guid.NewGuid():N}",
            ["email"] = $"{role.ToLowerInvariant()}@example.com",
            ["name"] = $"{role.ToLowerInvariant()}@example.com",
            ["role"] = role,
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.AddMinutes(15).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        return CreateJwt(payload);
    }

    private static string CreateJwt(Dictionary<string, object> payload)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        })));
        var body = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        var signature = Base64UrlEncode(CreateSignature($"{header}.{body}"));

        return $"{header}.{body}.{signature}";
    }

    private static byte[] CreateSignature(string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
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
