using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class DoctorRegistrationIntegrationTests
{
    [Fact]
    public async Task RegisterDoctor_CreatesPendingUserAndDoctorProfile()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        var phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}";
        var request = CreateDoctorRequest(email, phoneNumber);

        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var profile = await db.DoctorProfiles.SingleAsync(candidate => candidate.UserId == user.Id);

        Assert.Equal("Pending", user.AccountStatus.ToString());
        Assert.Equal("Doctor", user.Role.ToString());
        Assert.False(user.IsDeleted);
        Assert.NotNull(user.PasswordHash);

        Assert.Equal("Cardiology", profile.Specialization);
        Assert.Equal(5, profile.ExperienceYears);
        Assert.Equal("Lagos", profile.Location);
        Assert.Equal("License", profile.VerificationDocumentType);
        Assert.Equal("license.pdf", profile.VerificationOriginalFileName);
        Assert.Equal("application/pdf", profile.VerificationContentType);
        Assert.Equal(1024, profile.VerificationSizeBytes);
        Assert.StartsWith("ref-", profile.VerificationReference, StringComparison.Ordinal);

        Assert.Empty(await db.RefreshCredentials.Where(token => token.UserId == user.Id).ToListAsync());
        Assert.NotEmpty(await db.AuthenticationAuditEvents.Where(eventItem => eventItem.TargetUserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task RegisterDoctor_WithActiveDuplicateEmail_ReturnsConflict()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        var firstRequest = CreateDoctorRequest(email, $"555{Random.Shared.Next(1000000, 9999999)}");
        var duplicateRequest = CreateDoctorRequest(email, $"555{Random.Shared.Next(1000000, 9999999)}");

        using var firstResponse = await client.PostAsJsonAsync("/api/auth/register-doctor", firstRequest);
        using var duplicateResponse = await client.PostAsJsonAsync("/api/auth/register-doctor", duplicateRequest);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task RegisterDoctor_WithSoftDeletedDuplicateEmail_CreatesNewUser()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        var firstRequest = CreateDoctorRequest(email, $"555{Random.Shared.Next(1000000, 9999999)}");
        var replacementRequest = CreateDoctorRequest(email, $"555{Random.Shared.Next(1000000, 9999999)}");

        using var firstResponse = await client.PostAsJsonAsync("/api/auth/register-doctor", firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var originalUser = await db.Users.SingleAsync(candidate => candidate.Email == email);
            originalUser.IsDeleted = true;
            originalUser.DeletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        using var replacementResponse = await client.PostAsJsonAsync("/api/auth/register-doctor", replacementRequest);

        Assert.Equal(HttpStatusCode.Created, replacementResponse.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var users = await verificationDb.Users
            .Where(candidate => candidate.Email == email)
            .OrderBy(candidate => candidate.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(2, users.Count);
        Assert.True(users[0].IsDeleted);
        Assert.False(users[1].IsDeleted);
        Assert.NotEqual(users[0].Id, users[1].Id);
        Assert.NotNull(await verificationDb.DoctorProfiles.SingleOrDefaultAsync(candidate => candidate.UserId == users[1].Id));
    }

    [Fact]
    public async Task RegisterDoctor_WithSoftDeletedDuplicatePhone_CreatesNewUser()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}";
        var firstRequest = CreateDoctorRequest($"doctor-{Guid.NewGuid():N}@example.com", phoneNumber);
        var replacementEmail = $"doctor-{Guid.NewGuid():N}@example.com";
        var replacementRequest = CreateDoctorRequest(replacementEmail, phoneNumber);

        using var firstResponse = await client.PostAsJsonAsync("/api/auth/register-doctor", firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var originalUser = await db.Users.SingleAsync(candidate => candidate.PhoneNumber == phoneNumber);
            originalUser.IsDeleted = true;
            originalUser.DeletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        using var replacementResponse = await client.PostAsJsonAsync("/api/auth/register-doctor", replacementRequest);

        Assert.Equal(HttpStatusCode.Created, replacementResponse.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var replacementUser = await verificationDb.Users.SingleAsync(candidate => candidate.Email == replacementEmail);
        Assert.False(replacementUser.IsDeleted);
        Assert.Equal(phoneNumber, replacementUser.PhoneNumber);
    }

    private static object CreateDoctorRequest(string email, string phoneNumber)
    {
        return new
        {
            Email = email,
            Password = "Password1!",
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
}
