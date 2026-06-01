using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class RefreshTokenReuseDetectionTests
{
    [Fact]
    public async Task Refresh_ReusedRotatedTokenRevokesActiveFamily()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var (email, originalRefreshToken) = await LoginApprovedDoctorAsync(factory, client);

        using var firstRefreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = originalRefreshToken });
        Assert.Equal(HttpStatusCode.OK, firstRefreshResponse.StatusCode);

        using var firstRefreshDocument = JsonDocument.Parse(await firstRefreshResponse.Content.ReadAsStringAsync());
        var activeRefreshToken = firstRefreshDocument.RootElement.GetProperty("Data").GetProperty("RefreshToken").GetString();

        using var reuseResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = originalRefreshToken });

        Assert.Equal(HttpStatusCode.Conflict, reuseResponse.StatusCode);

        using var activeTokenResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = activeRefreshToken });

        Assert.Equal(HttpStatusCode.Conflict, activeTokenResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var activeCredentials = await db.RefreshCredentials
            .Where(token => token.UserId == user.Id && token.RevokedAtUtc == null)
            .ToListAsync();
        var auditEvents = await db.AuthenticationAuditEvents
            .Where(audit => audit.TargetUserId == user.Id && audit.EventType == AuthAuditEventType.RefreshReuseDetected)
            .ToListAsync();

        Assert.Empty(activeCredentials);
        Assert.NotEmpty(auditEvents);
    }

    [Fact]
    public async Task Refresh_ExpiredRotatedTokenReuseStillRevokesActiveFamily()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var (email, originalRefreshToken) = await LoginApprovedDoctorAsync(factory, client);

        using var firstRefreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = originalRefreshToken });
        Assert.Equal(HttpStatusCode.OK, firstRefreshResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
            var originalCredential = await db.RefreshCredentials
                .Where(token => token.UserId == user.Id && token.RevokedAtUtc != null)
                .SingleAsync();

            originalCredential.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var reuseResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = originalRefreshToken });

        Assert.Equal(HttpStatusCode.Conflict, reuseResponse.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var verificationUser = await verificationDb.Users.SingleAsync(candidate => candidate.Email == email);
        var activeCredentials = await verificationDb.RefreshCredentials
            .Where(token => token.UserId == verificationUser.Id && token.RevokedAtUtc == null)
            .ToListAsync();

        Assert.Empty(activeCredentials);
    }

    private static async Task<(string Email, string RefreshToken)> LoginApprovedDoctorAsync(WebAppFactory factory, HttpClient client)
    {
        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        await RegisterDoctorAsync(client, email);
        await SetStatusAsync(factory, email, AccountStatus.Approved);

        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var document = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var refreshToken = document.RootElement.GetProperty("Data").GetProperty("RefreshToken").GetString();

        return (email, refreshToken ?? string.Empty);
    }

    private static async Task RegisterDoctorAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", new
        {
            Email = email,
            Password = "Password1!",
            PhoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            Specialization = "Cardiology",
            ExperienceYears = 5,
            Location = "Lagos",
            VerificationMetadata = new
            {
                DocumentType = "License",
                OriginalFileName = "license.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                Reference = $"ref-{Guid.NewGuid():N}"
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task SetStatusAsync(WebAppFactory factory, string email, AccountStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var now = DateTime.UtcNow;

        user.AccountStatus = status;
        user.ApprovedAtUtc = status == AccountStatus.Approved ? now : null;
        user.LastStatusChangedAtUtc = now;

        await db.SaveChangesAsync();
    }
}
