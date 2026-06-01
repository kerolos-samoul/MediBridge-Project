using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class AccountResubmissionContractTests
{
    [Fact]
    public async Task ResubmitRegistration_WithValidToken_ReturnsPendingEnvelopeAnd200()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (token, _) = await CreateRejectedDoctorWithTokenAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/resubmit-registration", CreateRequest(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal("Doctor", data.GetProperty("Role").GetString());
        Assert.Equal("Pending", data.GetProperty("AccountStatus").GetString());
    }

    [Fact]
    public async Task ResubmitRegistration_WithoutToken_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/resubmit-registration", CreateRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task ResubmitRegistration_WithInvalidToken_ReturnsAccountStatusDeniedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/resubmit-registration", CreateRequest("invalid-token"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Account status denied.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private static object CreateRequest(string token)
    {
        return new
        {
            ResubmissionToken = token,
            Specialization = "Neurology",
            ExperienceYears = 6,
            Location = "Cairo",
            VerificationMetadata = new
            {
                DocumentType = "License",
                OriginalFileName = "updated-license.pdf",
                ContentType = "application/pdf",
                SizeBytes = 2048,
                Reference = $"updated-ref-{Guid.NewGuid():N}"
            }
        };
    }

    private static async Task<(string PlaintextToken, string UserId)> CreateRejectedDoctorWithTokenAsync(ContractWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IAuthTokenService>();
        var now = DateTime.UtcNow;
        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        var user = new MediBridgeIdentityUser
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Rejected,
            CreatedAtUtc = now,
            LastStatusChangedAtUtc = now
        };
        var decision = new AdminAccountDecision
        {
            AdminUserId = user.Id,
            TargetUserId = user.Id,
            Decision = AdminAccountDecisionType.Reject,
            ResultingAccountStatus = AccountStatus.Rejected,
            Reason = "Missing document.",
            CreatedAtUtc = now
        };
        var (plaintext, hash) = tokenService.CreateOneTimeToken();

        db.Users.Add(user);
        db.DoctorProfiles.Add(new()
        {
            UserId = user.Id,
            Specialization = "Cardiology",
            ExperienceYears = 5,
            Location = "Lagos",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = "old-ref",
            CreatedAtUtc = now
        });
        db.AdminAccountDecisions.Add(decision);
        db.AccountResubmissionTokens.Add(new()
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAtUtc = now.AddDays(7),
            CreatedAtUtc = now,
            CreatedByAdminDecisionId = decision.Id
        });
        await db.SaveChangesAsync();
        return (plaintext, user.Id);
    }

    private static void AssertEnvelope(JsonElement root, int expectedCode, string expectedMessage)
    {
        Assert.Equal(expectedCode, root.GetProperty("Code").GetInt32());
        Assert.Equal(expectedMessage, root.GetProperty("Message").GetString());
    }
}
