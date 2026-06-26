using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MediBridge.ContractTests;

public static class Phase5ContractTestHelpers
{
    public static void AuthorizeAsCompany(HttpClient client, string? userId = null)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Company", userId));
    }

    public static string CreateToken(string role, string? userId = null)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId ?? $"user-{Guid.NewGuid():N}",
            ["email"] = $"{role.ToLowerInvariant()}@example.com",
            ["name"] = $"{role.ToLowerInvariant()}@example.com",
            ["role"] = role,
            ["iss"] = "MediBridge.ContractTests",
            ["aud"] = "MediBridge.ContractTests.ApiClients",
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.AddMinutes(15).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        return CreateJwt(payload);
    }

    public static StringContent CreateJsonContent(object request)
    {
        return new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
    }

    public static object CreateCampaignRequest(IEnumerable<string> assetIds, IEnumerable<string> targetDoctorIds)
    {
        return new
        {
            Title = "Phase 5 campaign",
            Description = "Phase 5 campaign description",
            ClinicalResearchInfo = "Phase 5 clinical research information",
            AssetIds = assetIds.ToArray(),
            TargetDoctorIds = targetDoctorIds.ToArray()
        };
    }

    public static object CreateTopUpRequest(decimal amount, string? description = "Phase 5 top-up")
    {
        return new
        {
            Amount = amount,
            Description = description
        };
    }

    public static void AssertEnvelope(JsonElement root, int expectedCode)
    {
        Assert.Equal(expectedCode, root.GetProperty("Code").GetInt32());
        Assert.True(root.TryGetProperty("Message", out _));
        Assert.True(root.TryGetProperty("Data", out _));
    }

    public static JsonElement AssertDataEnvelope(JsonDocument document, int expectedCode)
    {
        AssertEnvelope(document.RootElement, expectedCode);
        return document.RootElement.GetProperty("Data");
    }

    public static void AddIdempotencyKey(HttpRequestMessage request, string? key = null)
    {
        request.Headers.Add("Idempotency-Key", key ?? $"idem-{Guid.NewGuid():N}");
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
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("ContractTestSigningKey-ReplaceBeforeProduction-32Chars"));
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
