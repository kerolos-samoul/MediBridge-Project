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

public sealed class RefreshTokenRotationTests
{
    [Fact]
    public async Task Refresh_RotatesRefreshTokenAndRejectsPreviousToken()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var (email, originalRefreshToken) = await LoginApprovedDoctorAsync(factory, client);

        using var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = originalRefreshToken });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        using var refreshDocument = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
        var replacementRefreshToken = refreshDocument.RootElement.GetProperty("Data").GetProperty("RefreshToken").GetString();

        Assert.False(string.IsNullOrWhiteSpace(replacementRefreshToken));
        Assert.NotEqual(originalRefreshToken, replacementRefreshToken);

        using var reuseResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = originalRefreshToken });

        Assert.Equal(HttpStatusCode.Conflict, reuseResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var credentials = await db.RefreshCredentials
            .Where(token => token.UserId == user.Id)
            .OrderBy(token => token.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(2, credentials.Count);
        Assert.Equal("Rotated", credentials[0].RevocationReason);
        Assert.NotNull(credentials[0].ReplacedByTokenHash);
        Assert.Equal("ReuseDetected", credentials[1].RevocationReason);
    }

    [Fact]
    public async Task Refresh_ConcurrentRequestsOnlyRotateOnce()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var (email, originalRefreshToken) = await LoginApprovedDoctorAsync(factory, client);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8)
            .Select(async _ =>
            {
                await ready.Task;
                using var response = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = originalRefreshToken });
                return response.StatusCode;
            })
            .ToArray();

        ready.SetResult();
        var statuses = await Task.WhenAll(tasks);

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(7, statuses.Count(status => status == HttpStatusCode.Conflict));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var credentials = await db.RefreshCredentials
            .Where(token => token.UserId == user.Id)
            .ToListAsync();

        Assert.Equal(2, credentials.Count);
        Assert.DoesNotContain(credentials, token => token.RevokedAtUtc == null);
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
