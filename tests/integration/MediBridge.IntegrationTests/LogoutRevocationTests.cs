using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class LogoutRevocationTests
{
    [Fact]
    public async Task Logout_RevokesSubmittedRefreshToken()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var (email, refreshToken, accessToken) = await LoginApprovedDoctorAsync(factory, client);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { RefreshToken = refreshToken })
        };
        logoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var logoutResponse = await client.SendAsync(logoutRequest);

        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        using var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });

        Assert.Equal(HttpStatusCode.Conflict, refreshResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var credential = await db.RefreshCredentials.SingleAsync(token => token.UserId == user.Id);

        Assert.NotNull(credential.RevokedAtUtc);
        Assert.Equal("Logout", credential.RevocationReason);
    }

    [Fact]
    public async Task Logout_WithRotatedToken_RevokesRefreshFamily()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var (_, originalRefreshToken, accessToken) = await LoginApprovedDoctorAsync(factory, client);

        using var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = originalRefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        using var refreshDocument = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
        var replacementRefreshToken = refreshDocument.RootElement.GetProperty("Data").GetProperty("RefreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(replacementRefreshToken));

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { RefreshToken = originalRefreshToken })
        };
        logoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var logoutResponse = await client.SendAsync(logoutRequest);

        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        using var replacementRefreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = replacementRefreshToken });
        Assert.Equal(HttpStatusCode.Conflict, replacementRefreshResponse.StatusCode);
    }

    private static async Task<(string Email, string RefreshToken, string AccessToken)> LoginApprovedDoctorAsync(WebAppFactory factory, HttpClient client)
    {
        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        await RegisterDoctorAsync(client, email);
        await SetStatusAsync(factory, email, AccountStatus.Approved);

        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var document = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");

        return (
            email,
            data.GetProperty("RefreshToken").GetString() ?? string.Empty,
            data.GetProperty("AccessToken").GetString() ?? string.Empty);
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
