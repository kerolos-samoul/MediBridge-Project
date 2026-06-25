using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class ContactVerificationIssuanceTests
{
    [Theory]
    [InlineData("doctor")]
    [InlineData("company")]
    public async Task Registration_IssuesOneEmailOtpAndStoresOnlyHashes(string role)
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = $"{role}-{Guid.NewGuid():N}@example.com";

        using var response = role == "doctor"
            ? await client.PostAsJsonAsync("/api/auth/register-doctor", CreateDoctorRequest(email))
            : await client.PostAsJsonAsync("/api/auth/register-company", CreateCompanyRequest(email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.True(data.GetProperty("VerificationRequired").GetBoolean());
        Assert.Equal("Email", data.GetProperty("VerificationChannel").GetString());
        Assert.Contains("***", data.GetProperty("MaskedVerificationDestination").GetString());
        Assert.Equal("Sent", data.GetProperty("VerificationDeliveryStatus").GetString());
        Assert.True(data.GetProperty("VerificationExpiresAtUtc").GetDateTime() > DateTime.UtcNow);

        var otp = factory.GetLatestContactVerificationCode(email);
        Assert.Matches("^[0-9]{6}$", otp);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var flows = await db.ContactVerificationFlows.Where(flow => flow.UserId == user.Id).ToListAsync();
        Assert.Single(flows);
        var flow = flows[0];
        Assert.Equal(ContactVerificationChannel.Email, flow.Channel);
        Assert.NotEqual(email, flow.DestinationHash);
        Assert.NotEqual(otp, flow.TokenHash);
        Assert.DoesNotContain(otp, AllStoredStringValues(user, flow));
    }

    private static object CreateDoctorRequest(string email)
        => new
        {
            Email = email,
            Password = "Password1!",
            PhoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            Specialization = "Cardiology",
            ExperienceYears = 5,
            Location = "Lagos",
            VerificationMetadata = Phase6IdentityTestHelpers.CreateVerificationMetadata()
        };

    private static object CreateCompanyRequest(string email)
        => new
        {
            Email = email,
            Password = "Password1!",
            PhoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            CompanyName = "Acme Pharma",
            LicenseNumber = $"LIC-{Guid.NewGuid():N}",
            ContactName = "Casey Admin",
            VerificationMetadata = Phase6IdentityTestHelpers.CreateVerificationMetadata()
        };

    private static string AllStoredStringValues(params object[] entities)
    {
        var values = new List<string>();
        foreach (var entity in entities)
        {
            foreach (var property in entity.GetType().GetProperties().Where(property => property.PropertyType == typeof(string)))
            {
                if (property.GetValue(entity) is string value)
                {
                    values.Add(value);
                }
            }
        }

        return string.Join('\n', values);
    }
}
