using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class ApprovedLoginIntegrationTests
{
    [Fact]
    public async Task Login_WithApprovedAccount_ReturnsTokensAndStoresRefreshCredential()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        await RegisterDoctorAsync(client, email);
        await ApproveUserAsync(factory, email);

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");

        var accessToken = data.GetProperty("AccessToken").GetString();
        var refreshToken = data.GetProperty("RefreshToken").GetString();
        var role = data.GetProperty("Role").GetString();

        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
        Assert.Equal("Doctor", role);

        var payload = DecodeJwtPayload(accessToken!);
        Assert.Equal("Doctor", payload.GetProperty("role").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var credential = await db.RefreshCredentials.SingleAsync(token => token.UserId == user.Id);

        Assert.NotEqual(refreshToken, credential.TokenHash);
        Assert.Null(credential.RevokedAtUtc);
        Assert.True(credential.ExpiresAtUtc > DateTime.UtcNow);
    }

    private static JsonElement DecodeJwtPayload(string token)
    {
        var parts = token.Split('.');
        var payloadBytes = Base64UrlDecode(parts[1]);
        return JsonDocument.Parse(payloadBytes).RootElement.Clone();
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        var remainder = padded.Length % 4;
        if (remainder == 2)
        {
            padded += "==";
        }
        else if (remainder == 3)
        {
            padded += "=";
        }

        return Convert.FromBase64String(padded);
    }

    private static async Task RegisterDoctorAsync(HttpClient client, string email)
    {
        var request = new
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
        };

        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task ApproveUserAsync(WebAppFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var now = DateTime.UtcNow;
        user.AccountStatus = Core.Enums.AccountStatus.Approved;
        user.ApprovedAtUtc = now;
        user.LastStatusChangedAtUtc = now;
        user.EmailVerified = true;

        await db.SaveChangesAsync();
    }
}
