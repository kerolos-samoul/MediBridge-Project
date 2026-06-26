using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminDecisionTransitionIntegrationTests
{
    [Theory]
    [InlineData(AccountStatus.Pending, "Suspend")]
    [InlineData(AccountStatus.Approved, "Reject")]
    [InlineData(AccountStatus.Rejected, "Reactivate")]
    [InlineData(AccountStatus.Suspended, "Reject")]
    [InlineData(AccountStatus.Inactive, "Suspend")]
    public async Task InvalidDecisionTransition_DoesNotMutateStatusOrPersistSideEffects(AccountStatus currentStatus, string decision)
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var target = await CreateTargetAsync(factory.Services, currentStatus);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new
        {
            Decision = decision,
            Reason = decision is "Reject" or "Suspend" ? "Invalid transition check." : null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var persistedTarget = await db.Users.SingleAsync(candidate => candidate.Id == target.Id);

        Assert.Equal(currentStatus, persistedTarget.AccountStatus);
        Assert.Empty(await db.AdminAccountDecisions.Where(candidate => candidate.TargetUserId == target.Id).ToListAsync());
        Assert.Empty(await db.AuthenticationAuditEvents.Where(candidate => candidate.TargetUserId == target.Id).ToListAsync());
        Assert.Empty(await db.AccountResubmissionTokens.Where(candidate => candidate.UserId == target.Id).ToListAsync());
        Assert.Empty(await db.RefreshCredentials.Where(candidate => candidate.UserId == target.Id && candidate.RevokedAtUtc != null).ToListAsync());
    }

    [Fact]
    public async Task DecisionAgainstSoftDeletedAccount_ReturnsValidationAndDoesNotPersistSideEffects()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var target = await CreateTargetAsync(factory.Services, AccountStatus.Pending, isDeleted: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new
        {
            Decision = "Approve"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var persistedTarget = await db.Users.SingleAsync(candidate => candidate.Id == target.Id);

        Assert.True(persistedTarget.IsDeleted);
        Assert.Equal(AccountStatus.Pending, persistedTarget.AccountStatus);
        Assert.Empty(await db.AdminAccountDecisions.Where(candidate => candidate.TargetUserId == target.Id).ToListAsync());
        Assert.Empty(await db.AuthenticationAuditEvents.Where(candidate => candidate.TargetUserId == target.Id).ToListAsync());
    }

    private static async Task<MediBridgeIdentityUser> CreateTargetAsync(IServiceProvider services, AccountStatus status, bool isDeleted = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        var user = new MediBridgeIdentityUser
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            Role = UserRole.Doctor,
            AccountStatus = status,
            CreatedAtUtc = now,
            ApprovedAtUtc = status == AccountStatus.Approved ? now : null,
            LastStatusChangedAtUtc = now,
            IsDeleted = isDeleted,
            DeletedAtUtc = isDeleted ? now : null
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
