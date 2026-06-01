using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Identity;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AuthAuditSafetyTests
{
    private static readonly string[] ForbiddenAuditFieldMarkers =
    [
        "Password",
        "Plaintext",
        "RefreshToken",
        "AccessToken",
        "RequestBody",
        "ResponseBody",
        "Payload",
        "TokenValue"
    ];

    [Fact]
    public void AuthenticationAuditEvent_Should_NotExposeSensitivePayloadFields()
    {
        var propertyNames = typeof(AuthenticationAuditEvent)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        foreach (var marker in ForbiddenAuditFieldMarkers)
        {
            Assert.DoesNotContain(propertyNames, property => property.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task RegistrationAuditEvent_Should_NotStorePasswordsTokensOrBodies()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"audit-{Guid.NewGuid():N}@example.com";
        const string password = "Password1!";
        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", new
        {
            Email = email,
            Password = password,
            PhoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            Specialization = "Cardiology",
            ExperienceYears = 5,
            Location = "Lagos",
            VerificationMetadata = Phase6IdentityTestHelpers.CreateVerificationMetadata()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var auditEvent = await db.AuthenticationAuditEvents.SingleAsync(candidate => candidate.TargetUserId == user.Id);
        var serializedAuditEvent = System.Text.Json.JsonSerializer.Serialize(auditEvent);

        Assert.DoesNotContain(password, serializedAuditEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshToken", serializedAuditEvent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccessToken", serializedAuditEvent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequestBody", serializedAuditEvent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResponseBody", serializedAuditEvent, StringComparison.OrdinalIgnoreCase);
    }
}
