using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class IdentityRegistrationContractTests
{
    [Fact]
    public async Task RegisterDoctor_ReturnsPendingEnvelopeAnd201()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        var request = CreateDoctorRequest(email, $"555{Random.Shared.Next(1000000, 9999999)}");

        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 201, "Success");

        var data = document.RootElement.GetProperty("Data");
        Assert.Equal("Doctor", data.GetProperty("Role").GetString());
        Assert.Equal("Pending", data.GetProperty("AccountStatus").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("UserId").GetString()));
        Assert.True(data.GetProperty("VerificationRequired").GetBoolean());
        Assert.Equal("Email", data.GetProperty("VerificationChannel").GetString());
        Assert.Contains("***", data.GetProperty("MaskedVerificationDestination").GetString());
        Assert.True(data.GetProperty("VerificationExpiresAtUtc").GetDateTime() > DateTime.UtcNow);
        Assert.Equal("Sent", data.GetProperty("VerificationDeliveryStatus").GetString());
    }

    [Fact]
    public async Task RegisterDoctor_WithEightCharacterPassword_ReturnsPendingEnvelopeAnd201()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        var request = CreateDoctorRequest(email, $"555{Random.Shared.Next(1000000, 9999999)}", "abcdefgh");

        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 201, "Success");
    }

    [Fact]
    public async Task RegisterDoctor_WithInvalidRequest_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task RegisterDoctor_WithDuplicateEmail_ReturnsConflictEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var request = CreateDoctorRequest($"duplicate-{Guid.NewGuid():N}@example.com", $"555{Random.Shared.Next(1000000, 9999999)}");

        using var firstResponse = await client.PostAsJsonAsync("/api/auth/register-doctor", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/auth/register-doctor", request);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        using var document = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 409, "Duplicate email, phone, or license.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private static object CreateDoctorRequest(string email, string phoneNumber, string password = "Password1!")
    {
        return new
        {
            Email = email,
            Password = password,
            PhoneNumber = phoneNumber,
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
    }

    private static void AssertEnvelope(JsonElement root, int expectedCode, string expectedMessage)
    {
        Assert.Equal(expectedCode, root.GetProperty("Code").GetInt32());
        Assert.Equal(expectedMessage, root.GetProperty("Message").GetString());
    }
}
