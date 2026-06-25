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

    [Fact]
    public async Task DecisionAgainstAdminTarget_ReturnsValidationAndDoesNotPersistSideEffects()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var targetAdmin = await CreateTargetAsync(factory.Services, AccountStatus.Approved, role: UserRole.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{targetAdmin.Id}/decision", new
        {
            Decision = "Suspend",
            Reason = "Admin target guard."
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var persistedTarget = await db.Users.SingleAsync(candidate => candidate.Id == targetAdmin.Id);

        Assert.Equal(AccountStatus.Approved, persistedTarget.AccountStatus);
        Assert.Equal(UserRole.Admin, persistedTarget.Role);
        Assert.Empty(await db.AdminAccountDecisions.Where(candidate => candidate.TargetUserId == targetAdmin.Id).ToListAsync());
        Assert.Empty(await db.AuthenticationAuditEvents.Where(candidate => candidate.TargetUserId == targetAdmin.Id).ToListAsync());
        Assert.Empty(await db.RefreshCredentials.Where(candidate => candidate.UserId == targetAdmin.Id && candidate.RevokedAtUtc != null).ToListAsync());
    }

    [Fact]
    public async Task ApprovingPendingCompany_CreatesExactlyOneActiveWallet()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var companyUser = await CreateTargetAsync(factory.Services, AccountStatus.Pending, role: UserRole.Company, emailVerified: true);
        string companyProfileId;
        using (var setupScope = factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var profile = new MediBridge.Core.Entities.Profiles.CompanyProfile
            {
                UserId = companyUser.Id,
                CompanyName = "Approval Wallet Pharma",
                LicenseNumber = $"approval-wallet-{Guid.NewGuid():N}",
                ContactName = "Approval Contact",
                VerificationDocumentType = "License",
                VerificationOriginalFileName = "approval-license.pdf",
                VerificationContentType = "application/pdf",
                VerificationSizeBytes = 1024,
                VerificationReference = $"approval/{Guid.NewGuid():N}"
            };
            setupDb.CompanyProfiles.Add(profile);
            await setupDb.SaveChangesAsync();
            companyProfileId = profile.Id;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{companyUser.Id}/decision", new
        {
            Decision = "Approve"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallets = await db.Wallets
            .AsNoTracking()
            .Where(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == companyProfileId && !wallet.IsDeleted)
            .ToListAsync();
        var wallet = Assert.Single(wallets);
        Assert.Equal(companyUser.Id, wallet.OwnerUserId);
        Assert.Equal(0m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);
    }

    [Fact]
    public async Task ConcurrentCompanyApprovals_PersistOneTransitionAuditAndWallet()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var companyUser = await CreateTargetAsync(factory.Services, AccountStatus.Pending, role: UserRole.Company, emailVerified: true);
        string companyProfileId;
        using (var setupScope = factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var profile = new MediBridge.Core.Entities.Profiles.CompanyProfile
            {
                UserId = companyUser.Id,
                CompanyName = "Concurrent Approval Pharma",
                LicenseNumber = $"concurrent-approval-{Guid.NewGuid():N}",
                ContactName = "Approval Contact",
                VerificationDocumentType = "License",
                VerificationOriginalFileName = "approval-license.pdf",
                VerificationContentType = "application/pdf",
                VerificationSizeBytes = 1024,
                VerificationReference = $"approval/{Guid.NewGuid():N}"
            };
            setupDb.CompanyProfiles.Add(profile);
            await setupDb.SaveChangesAsync();
            companyProfileId = profile.Id;
        }

        var token = TestJwtFactory.CreateToken("Admin", admin.Id);
        firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var responses = await Task.WhenAll(
            firstClient.PutAsJsonAsync($"/api/admin/accounts/{companyUser.Id}/decision", new { Decision = "Approve" }),
            secondClient.PutAsJsonAsync($"/api/admin/accounts/{companyUser.Id}/decision", new { Decision = "Approve" }));

        try
        {
            Assert.Equal(
                new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }.OrderBy(status => status),
                responses.Select(response => response.StatusCode).OrderBy(status => status));

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            Assert.Equal(AccountStatus.Approved, (await db.Users.SingleAsync(user => user.Id == companyUser.Id)).AccountStatus);
            Assert.Equal(1, await db.AdminAccountDecisions.CountAsync(decision => decision.TargetUserId == companyUser.Id));
            Assert.Equal(1, await db.AuthenticationAuditEvents.CountAsync(audit => audit.TargetUserId == companyUser.Id));
            Assert.Equal(1, await db.Wallets.CountAsync(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == companyProfileId));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static async Task<MediBridgeIdentityUser> CreateTargetAsync(
        IServiceProvider services,
        AccountStatus status,
        bool isDeleted = false,
        UserRole role = UserRole.Doctor,
        bool emailVerified = false)
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
            Role = role,
            AccountStatus = status,
            EmailVerified = emailVerified || status == AccountStatus.Approved,
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
