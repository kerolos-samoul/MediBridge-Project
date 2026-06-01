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

public sealed class AccountRecoveryContractTests
{
    [Fact]
    public async Task ForgotPassword_ReturnsAcceptedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Contact = "known@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 202, "Accepted");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_ReturnsOkEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var token = await CreatePasswordResetFlowAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/reset-password", new { ResetToken = token, NewPassword = "NewPassword1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task ResetPassword_WithInvalidRequest_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/reset-password", new { ResetToken = "", NewPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task VerifyContact_WithValidToken_ReturnsOkEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var token = await CreateContactVerificationFlowAsync(factory, ContactVerificationChannel.Email);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", VerificationToken = token });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task VerifyContact_WithInvalidRequest_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", VerificationToken = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private static async Task<string> CreatePasswordResetFlowAsync(ContractWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IAuthTokenService>();
        var user = CreateUser();
        var (plaintext, hash) = tokenService.CreateOneTimeToken();

        db.Users.Add(user);
        db.PasswordResetFlows.Add(new PasswordResetFlow
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            CreatedAtUtc = DateTime.UtcNow,
            RequestCorrelationId = Guid.NewGuid().ToString("N")
        });

        await db.SaveChangesAsync();
        return plaintext;
    }

    private static async Task<string> CreateContactVerificationFlowAsync(ContractWebAppFactory factory, ContactVerificationChannel channel)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IAuthTokenService>();
        var user = CreateUser();
        var (plaintext, hash) = tokenService.CreateOneTimeToken();

        db.Users.Add(user);
        db.ContactVerificationFlows.Add(new ContactVerificationFlow
        {
            UserId = user.Id,
            Channel = channel,
            DestinationHash = tokenService.HashToken(user.Email!),
            TokenHash = hash,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return plaintext;
    }

    private static MediBridgeIdentityUser CreateUser()
    {
        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        var now = DateTime.UtcNow;
        return new MediBridgeIdentityUser
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            PhoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            LastStatusChangedAtUtc = now
        };
    }

    private static void AssertEnvelope(JsonElement root, int expectedCode, string expectedMessage)
    {
        Assert.Equal(expectedCode, root.GetProperty("Code").GetInt32());
        Assert.Equal(expectedMessage, root.GetProperty("Message").GetString());
    }
}
