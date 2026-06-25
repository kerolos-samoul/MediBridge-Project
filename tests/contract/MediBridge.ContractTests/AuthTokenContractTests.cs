using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class AuthTokenContractTests
{
    [Fact]
    public async Task Login_ReturnsTokenEnvelopeAnd200()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        await RegisterDoctorAsync(client, email);
        await UpdateAccountStatusAsync(factory, email, AccountStatus.Approved);

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");

        var data = document.RootElement.GetProperty("Data");
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("AccessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("RefreshToken").GetString()));
        Assert.Equal("Doctor", data.GetProperty("Role").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("ExpiresAtUtc").GetString()));
    }

    [Fact]
    public async Task Login_WithPendingAccount_ReturnsAccountStatusDeniedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        await RegisterDoctorAsync(client, email);

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Account status denied.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task Login_WithInvalidRequest_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task Refresh_ReturnsTokenEnvelopeAnd200()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var (email, refreshToken) = await LoginApprovedUserAsync(factory, client);

        using var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        using var document = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");

        var data = document.RootElement.GetProperty("Data");
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("AccessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("RefreshToken").GetString()));
        Assert.Equal("Doctor", data.GetProperty("Role").GetString());
    }

    [Fact]
    public async Task Refresh_WithInvalidRequest_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/refresh", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Authentication denied.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task Refresh_WithReusedToken_ReturnsConflictEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var (_, refreshToken) = await LoginApprovedUserAsync(factory, client);

        using var rotatedResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.OK, rotatedResponse.StatusCode);

        using var reuseResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });

        Assert.Equal(HttpStatusCode.Conflict, reuseResponse.StatusCode);

        using var document = JsonDocument.Parse(await reuseResponse.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 409, "Refresh reuse detected.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task Logout_WithoutBearer_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = "token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Unauthorized.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task Logout_WithValidToken_ReturnsOkEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var (_, refreshToken, accessToken) = await LoginApprovedUserWithAccessTokenAsync(factory, client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { RefreshToken = refreshToken })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
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

    private static async Task UpdateAccountStatusAsync(ContractWebAppFactory factory, string email, AccountStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        user.AccountStatus = status;
        user.ApprovedAtUtc = status == AccountStatus.Approved ? DateTime.UtcNow : null;
        user.LastStatusChangedAtUtc = DateTime.UtcNow;
        user.EmailVerified = status == AccountStatus.Approved;
        await db.SaveChangesAsync();
    }

    private static async Task<(string Email, string RefreshToken)> LoginApprovedUserAsync(ContractWebAppFactory factory, HttpClient client)
    {
        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        await RegisterDoctorAsync(client, email);
        await UpdateAccountStatusAsync(factory, email, AccountStatus.Approved);

        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var document = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var refreshToken = document.RootElement.GetProperty("Data").GetProperty("RefreshToken").GetString();

        return (email, refreshToken ?? string.Empty);
    }

    private static async Task<(string Email, string RefreshToken, string AccessToken)> LoginApprovedUserWithAccessTokenAsync(
        ContractWebAppFactory factory,
        HttpClient client)
    {
        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        await RegisterDoctorAsync(client, email);
        await UpdateAccountStatusAsync(factory, email, AccountStatus.Approved);

        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var document = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        var refreshToken = data.GetProperty("RefreshToken").GetString();
        var accessToken = data.GetProperty("AccessToken").GetString();

        return (email, refreshToken ?? string.Empty, accessToken ?? string.Empty);
    }

    private static void AssertEnvelope(JsonElement root, int expectedCode, string expectedMessage)
    {
        Assert.Equal(expectedCode, root.GetProperty("Code").GetInt32());
        Assert.Equal(expectedMessage, root.GetProperty("Message").GetString());
    }
}
