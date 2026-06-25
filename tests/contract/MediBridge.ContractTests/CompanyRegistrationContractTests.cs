using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CompanyRegistrationContractTests
{
    [Fact]
    public async Task RegisterCompany_ReturnsPendingEnvelopeAnd201()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var licenseNumber = $"LIC-{Guid.NewGuid():N}";
        var request = CreateCompanyRequest($"company-{Guid.NewGuid():N}@example.com", $"555{Random.Shared.Next(1000000, 9999999)}", licenseNumber);

        using var response = await client.PostAsJsonAsync("/api/auth/register-company", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 201, "Success");

        var data = document.RootElement.GetProperty("Data");
        Assert.Equal("Company", data.GetProperty("Role").GetString());
        Assert.Equal("Pending", data.GetProperty("AccountStatus").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("UserId").GetString()));
        Assert.True(data.GetProperty("VerificationRequired").GetBoolean());
        Assert.Equal("Email", data.GetProperty("VerificationChannel").GetString());
        Assert.Contains("***", data.GetProperty("MaskedVerificationDestination").GetString());
        Assert.True(data.GetProperty("VerificationExpiresAtUtc").GetDateTime() > DateTime.UtcNow);
        Assert.Equal("Sent", data.GetProperty("VerificationDeliveryStatus").GetString());
    }

    [Fact]
    public async Task RegisterCompany_WithInvalidRequest_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/register-company", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task RegisterCompany_WithDuplicateLicense_ReturnsConflictEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var request = CreateCompanyRequest($"company-{Guid.NewGuid():N}@example.com", $"555{Random.Shared.Next(1000000, 9999999)}", $"LIC-{Guid.NewGuid():N}");

        using var firstResponse = await client.PostAsJsonAsync("/api/auth/register-company", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/auth/register-company", request);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        using var document = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 409, "Duplicate email, phone, or license.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private static object CreateCompanyRequest(string email, string phoneNumber, string licenseNumber)
    {
        return new
        {
            Email = email,
            Password = "Password1!",
            PhoneNumber = phoneNumber,
            CompanyName = "Acme Pharma",
            LicenseNumber = licenseNumber,
            ContactName = "Jane Doe",
            VerificationMetadata = new
            {
                DocumentType = "License",
                OriginalFileName = "company-license.pdf",
                ContentType = "application/pdf",
                SizeBytes = 2048,
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
