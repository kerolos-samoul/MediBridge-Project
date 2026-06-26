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

public sealed class AdminAccountDecisionContractTests
{
    [Theory]
    [InlineData("Approve", "Approved")]
    [InlineData("Reject", "Rejected")]
    [InlineData("Suspend", "Suspended")]
    [InlineData("Inactivate", "Inactive")]
    [InlineData("Reactivate", "Approved")]
    public async Task DecideAccount_WithAdminToken_ReturnsDecisionEnvelopeAnd200(string decision, string expectedStatus)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (adminId, targetId) = await CreateUsersAsync(factory, InitialStatusForValidDecision(decision));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(adminId, "Admin"));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{targetId}/decision", new
        {
            Decision = decision,
            Reason = decision is "Reject" or "Suspend" ? "Verification failed." : null,
            Notes = "Reviewed."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(targetId, data.GetProperty("UserId").GetString());
        Assert.Equal(expectedStatus, data.GetProperty("ResultingAccountStatus").GetString());
    }

    [Fact]
    public async Task DecideAccount_WhenTokenRoleIsAdminButStoredCallerIsNotAdmin_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (callerId, targetId) = await CreateCallerAndTargetAsync(factory, UserRole.Doctor, AccountStatus.Approved, AccountStatus.Pending);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(callerId, "Admin"));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{targetId}/decision", new
        {
            Decision = "Approve"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task DecideAccount_RejectWithoutReason_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (adminId, targetId) = await CreateUsersAsync(factory, AccountStatus.Pending);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(adminId, "Admin"));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{targetId}/decision", new
        {
            Decision = "Reject"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Theory]
    [InlineData(AccountStatus.Pending, "Suspend")]
    [InlineData(AccountStatus.Approved, "Reject")]
    [InlineData(AccountStatus.Rejected, "Reactivate")]
    [InlineData(AccountStatus.Suspended, "Reject")]
    [InlineData(AccountStatus.Inactive, "Suspend")]
    public async Task DecideAccount_WithInvalidStatusTransition_ReturnsValidationEnvelope(AccountStatus currentStatus, string decision)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (adminId, targetId) = await CreateUsersAsync(factory, currentStatus);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(adminId, "Admin"));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{targetId}/decision", new
        {
            Decision = decision,
            Reason = decision is "Reject" or "Suspend" ? "Invalid transition check." : null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private static AccountStatus InitialStatusForValidDecision(string decision)
    {
        return decision switch
        {
            "Approve" or "Reject" => AccountStatus.Pending,
            "Suspend" or "Inactivate" => AccountStatus.Approved,
            "Reactivate" => AccountStatus.Suspended,
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unexpected decision.")
        };
    }

    private static async Task<(string AdminId, string TargetId)> CreateUsersAsync(ContractWebAppFactory factory, AccountStatus targetStatus)
    {
        return await CreateCallerAndTargetAsync(factory, UserRole.Admin, AccountStatus.Approved, targetStatus);
    }

    private static async Task<(string CallerId, string TargetId)> CreateCallerAndTargetAsync(
        ContractWebAppFactory factory,
        UserRole callerRole,
        AccountStatus callerStatus,
        AccountStatus targetStatus)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var admin = CreateUser("caller", callerRole, callerStatus, now);
        var target = CreateUser("doctor", UserRole.Doctor, targetStatus, now);

        db.Users.AddRange(admin, target);
        await db.SaveChangesAsync();
        return (admin.Id, target.Id);
    }

    private static MediBridgeIdentityUser CreateUser(string prefix, UserRole role, AccountStatus status, DateTime now)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        return new MediBridgeIdentityUser
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            Role = role,
            AccountStatus = status,
            CreatedAtUtc = now,
            ApprovedAtUtc = status == AccountStatus.Approved ? now : null,
            LastStatusChangedAtUtc = now
        };
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
