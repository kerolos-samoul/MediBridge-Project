using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class LoginApprovalGateTests
{
    [Theory]
    [InlineData(AccountStatus.Pending)]
    [InlineData(AccountStatus.Rejected)]
    [InlineData(AccountStatus.Suspended)]
    [InlineData(AccountStatus.Inactive)]
    public async Task Login_DeniesNonApprovedStatuses(AccountStatus status)
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        await RegisterDoctorAsync(client, email);
        await UpdateUserStatusAsync(factory, email, status, isDeleted: false);

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);

        Assert.Empty(await db.RefreshCredentials.Where(token => token.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task Login_DeniesSoftDeletedUser()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        await RegisterDoctorAsync(client, email);
        await UpdateUserStatusAsync(factory, email, AccountStatus.Approved, isDeleted: true);

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);

        Assert.Empty(await db.RefreshCredentials.Where(token => token.UserId == user.Id).ToListAsync());
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

    private static async Task UpdateUserStatusAsync(WebAppFactory factory, string email, AccountStatus status, bool isDeleted)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var now = DateTime.UtcNow;

        user.AccountStatus = status;
        user.ApprovedAtUtc = status == AccountStatus.Approved ? now : null;
        user.LastStatusChangedAtUtc = now;
        user.IsDeleted = isDeleted;
        user.DeletedAtUtc = isDeleted ? now : null;

        await db.SaveChangesAsync();
    }
}
