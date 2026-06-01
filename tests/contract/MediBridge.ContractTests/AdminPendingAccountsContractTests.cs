using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class AdminPendingAccountsContractTests
{
    [Fact]
    public async Task PendingAccounts_WithAdminToken_ReturnsEnvelopePaginationFieldsAnd200()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminId = await CreateAdminAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(adminId, "Admin"));

        using var response = await client.GetAsync("/api/admin/pending-accounts?PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(JsonValueKind.Array, data.GetProperty("Items").ValueKind);
        Assert.Equal(1, data.GetProperty("PageNumber").GetInt32());
        Assert.Equal(20, data.GetProperty("PageSize").GetInt32());
        Assert.True(data.GetProperty("TotalCount").GetInt32() >= 0);
    }

    [Fact]
    public async Task PendingAccounts_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/admin/pending-accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Unauthorized.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task PendingAccounts_WithDoctorToken_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken("doctor-user", "Doctor"));

        using var response = await client.GetAsync("/api/admin/pending-accounts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private static async Task<string> CreateAdminAsync(ContractWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var admin = new MediBridgeIdentityUser
        {
            Email = $"admin-{Guid.NewGuid():N}@example.com",
            UserName = $"admin-{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"ADMIN-{Guid.NewGuid():N}@EXAMPLE.COM",
            NormalizedUserName = $"ADMIN-{Guid.NewGuid():N}@EXAMPLE.COM",
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            LastStatusChangedAtUtc = DateTime.UtcNow
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin.Id;
    }

    private static void AssertEnvelope(JsonElement root, int expectedCode, string expectedMessage)
    {
        Assert.Equal(expectedCode, root.GetProperty("Code").GetInt32());
        Assert.Equal(expectedMessage, root.GetProperty("Message").GetString());
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
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
