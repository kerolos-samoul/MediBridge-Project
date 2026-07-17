using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class AdminToolsWorkQueueContractTests
{
    [Fact]
    public async Task WorkQueue_WithAdminToken_ReturnsStandardEnvelopeAndSchema()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken("admin-user", "Admin"));

        using var response = await client.GetAsync($"{AdminToolsRoutes.WorkQueue}?PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(200, root.GetProperty("Code").GetInt32());
        Assert.Equal("Success", root.GetProperty("Message").GetString());
        var data = root.GetProperty("Data");
        var page = data.GetProperty("page");
        Assert.Equal(1, page.GetProperty("pageNumber").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(JsonValueKind.Object, data.GetProperty("categoryCounts").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("items").ValueKind);
    }

    [Theory]
    [InlineData("?PageNumber=0&PageSize=20")]
    [InlineData("?PageNumber=1&PageSize=101")]
    [InlineData("?category=NotAQueue")]
    public async Task WorkQueue_WithInvalidQuery_ReturnsBadRequestEnvelope(string query)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken("admin-user", "Admin"));

        using var response = await client.GetAsync(AdminToolsRoutes.WorkQueue + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task WorkQueue_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(AdminToolsRoutes.WorkQueue);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task WorkQueue_WithDoctorToken_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken("doctor-user", "Doctor"));

        using var response = await client.GetAsync(AdminToolsRoutes.WorkQueue);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private static class ContractJwtFactory
    {
        private const string Issuer = "MediBridge.ContractTests";
        private const string Audience = "MediBridge.ContractTests.ApiClients";
        private const string SigningKey = "ContractTestSigningKey-ReplaceBeforeProduction-32Chars";

        public static string CreateToken(string userId, string role)
        {
            var issuedAt = DateTimeOffset.UtcNow;
            var payload = new Dictionary<string, object>
            {
                ["sub"] = userId,
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
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
