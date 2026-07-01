using System.Net.Http.Headers;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.IntegrationTests;

public static class WalletCampaignWorkflowTestHelpers
{
    public static async Task<WalletCampaignFixtureIds> CreateApprovedWorkflowActorsAsync(ConfiguredWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var adminUser = CreateUser($"workflow-admin-{suffix}@medibridge.local", UserRole.Admin);
        var companyUser = CreateUser($"workflow-company-{suffix}@medibridge.local", UserRole.Company);
        var doctorUser = CreateUser($"workflow-doctor-{suffix}@medibridge.local", UserRole.Doctor);

        var company = new CompanyProfile
        {
            UserId = companyUser.Id,
            CompanyName = "Workflow Pharma",
            LicenseNumber = $"workflow-license-{suffix}",
            ContactName = "Workflow Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "company-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 2048,
            VerificationReference = $"workflow/{suffix}/company"
        };

        var doctor = new DoctorProfile
        {
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 8,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"workflow/{suffix}/doctor"
        };

        await context.Users.AddRangeAsync(adminUser, companyUser, doctorUser);
        await context.CompanyProfiles.AddAsync(company);
        await context.DoctorProfiles.AddAsync(doctor);
        await context.SaveChangesAsync();

        return new WalletCampaignFixtureIds(adminUser.Id, companyUser.Id, company.Id, doctorUser.Id, doctor.Id);
    }

    /// <remarks>Creates a wallet row only when <paramref name="createWalletRow"/> is true; it never mutates an existing wallet.</remarks>
    public static Task<WalletCampaignFixtureCampaignIds> CreateDraftCampaignAsync(
        ConfiguredWebAppFactory factory,
        string companyId,
        bool createWalletRow = false,
        decimal availableBalance = 0m)
        => CreateCampaignAsync(
            factory,
            companyId,
            CampaignStatus.Draft,
            submittedAtUtc: null,
            createWalletRow,
            availableBalance);

    /// <remarks>Creates a wallet row only when <paramref name="createWalletRow"/> is true; it never mutates an existing wallet.</remarks>
    public static Task<WalletCampaignFixtureCampaignIds> CreateRevisionRequiredCampaignAsync(
        ConfiguredWebAppFactory factory,
        string companyId,
        DateTime submittedAtUtc,
        bool createWalletRow = false,
        decimal availableBalance = 0m)
        => CreateCampaignAsync(
            factory,
            companyId,
            GetRevisionRequiredStatus(),
            submittedAtUtc,
            createWalletRow,
            availableBalance);

    /// <remarks>Creates a wallet row only when <paramref name="createWalletRow"/> is true; it never mutates an existing wallet.</remarks>
    public static Task<WalletCampaignFixtureCampaignIds> CreatePendingReviewCampaignAsync(
        ConfiguredWebAppFactory factory,
        string companyId,
        DateTime submittedAtUtc,
        bool createWalletRow = false,
        decimal availableBalance = 0m)
        => CreateCampaignAsync(
            factory,
            companyId,
            CampaignStatus.PendingReview,
            submittedAtUtc,
            createWalletRow,
            availableBalance);

    /// <remarks>Does not create or mutate wallet rows.</remarks>
    public static async Task<string> AddTargetSnapshotAsync(
        ConfiguredWebAppFactory factory,
        string campaignId,
        string doctorId,
        DateTime? createdAtUtc = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var target = new CampaignTarget
        {
            CampaignId = campaignId,
            DoctorId = doctorId,
            SpecializationSnapshot = "Cardiology",
            ExperienceYearsSnapshot = 8,
            LocationSnapshot = "Cairo",
            ActivityScoreSnapshot = 90m,
            PricePerMessageSnapshot = 50m,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        await context.CampaignTargets.AddAsync(target);
        await context.SaveChangesAsync();
        return target.Id;
    }

    /// <remarks>Does not create or mutate wallet rows.</remarks>
    public static Task<string> AddPendingCampaignMediaAsync(
        ConfiguredWebAppFactory factory,
        string campaignId)
        => AddCampaignMediaAsync(factory, campaignId, StoredFileReviewStatus.Pending);

    /// <remarks>Does not create or mutate wallet rows.</remarks>
    public static Task<string> AddApprovedCampaignMediaAsync(
        ConfiguredWebAppFactory factory,
        string campaignId)
        => AddCampaignMediaAsync(factory, campaignId, StoredFileReviewStatus.Approved);

    /// <remarks>Does not create or mutate wallet rows.</remarks>
    public static Task<string> AddRejectedCampaignMediaAsync(
        ConfiguredWebAppFactory factory,
        string campaignId)
        => AddCampaignMediaAsync(factory, campaignId, StoredFileReviewStatus.Rejected);

    /// <remarks>Does not create or mutate wallet rows.</remarks>
    public static Task<string> AddDeletedCampaignMediaAsync(
        ConfiguredWebAppFactory factory,
        string campaignId)
        => AddCampaignMediaAsync(
            factory,
            campaignId,
            StoredFileReviewStatus.Pending,
            StorageObjectState.Deleted,
            deletedAtUtc: DateTime.UtcNow);

    /// <remarks>Does not create or mutate wallet rows.</remarks>
    public static Task<string> AddSupersededCampaignMediaAsync(
        ConfiguredWebAppFactory factory,
        string campaignId)
        => AddCampaignMediaAsync(
            factory,
            campaignId,
            StoredFileReviewStatus.Pending,
            supersededByFileId: Guid.NewGuid().ToString("N"));

    public static HttpClient CreateAdminClient(ConfiguredWebAppFactory factory, string adminUserId)
        => CreateAuthenticatedClient(factory, nameof(UserRole.Admin), adminUserId);

    public static HttpClient CreateCompanyClient(ConfiguredWebAppFactory factory, string companyUserId)
        => CreateAuthenticatedClient(factory, nameof(UserRole.Company), companyUserId);

    public static HttpClient CreateDoctorClient(ConfiguredWebAppFactory factory, string doctorUserId)
        => CreateAuthenticatedClient(factory, nameof(UserRole.Doctor), doctorUserId);

    public static async Task<Wallet?> FindWalletAsync(ConfiguredWebAppFactory factory, string walletId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await context.Wallets.AsNoTracking().FirstOrDefaultAsync(wallet => wallet.Id == walletId);
    }

    public static async Task<MockPaymentTransaction?> FindPaymentAsync(ConfiguredWebAppFactory factory, string paymentId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await context.MockPaymentTransactions.AsNoTracking().FirstOrDefaultAsync(payment => payment.PaymentId == paymentId);
    }

    public static async Task<Campaign?> FindCampaignAsync(ConfiguredWebAppFactory factory, string campaignId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await context.Campaigns.AsNoTracking().FirstOrDefaultAsync(campaign => campaign.Id == campaignId);
    }

    public static async Task<StoredFile?> FindAssetAsync(ConfiguredWebAppFactory factory, string assetId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await context.StoredFiles.AsNoTracking().FirstOrDefaultAsync(asset => asset.Id == assetId);
    }

    public static async Task<IReadOnlyList<CampaignTarget>> ListTargetsAsync(ConfiguredWebAppFactory factory, string campaignId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await context.CampaignTargets.AsNoTracking().Where(target => target.CampaignId == campaignId).ToListAsync();
    }

    public static async Task<IReadOnlyList<string>> ListQueueIdsAsync(ConfiguredWebAppFactory factory, string campaignId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await context.DoctorMessageQueues
            .AsNoTracking()
            .Where(queue => queue.CampaignId == campaignId)
            .OrderBy(queue => queue.QueuedAtUtc)
            .ThenBy(queue => queue.Id)
            .Select(queue => queue.Id)
            .ToListAsync();
    }

    private static HttpClient CreateAuthenticatedClient(ConfiguredWebAppFactory factory, string role, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(role, userId));
        return client;
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role)
    {
        var normalized = email.ToUpperInvariant();
        var now = DateTime.UtcNow;
        return new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = normalized,
            Email = email,
            NormalizedEmail = normalized,
            Role = role,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            LastStatusChangedAtUtc = now
        };
    }

    private static async Task<WalletCampaignFixtureCampaignIds> CreateCampaignAsync(
        ConfiguredWebAppFactory factory,
        string companyId,
        CampaignStatus status,
        DateTime? submittedAtUtc,
        bool createWalletRow,
        decimal availableBalance)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var campaign = new Campaign
        {
            CompanyId = companyId,
            Title = "Campaign moderation fixture",
            Description = "Campaign content created explicitly for moderation workflow coverage.",
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        SetSubmittedAtUtc(campaign, submittedAtUtc);
        await context.Campaigns.AddAsync(campaign);

        Wallet? wallet = null;
        if (createWalletRow)
        {
            wallet = new Wallet
            {
                OwnerType = WalletOwnerType.Company,
                OwnerId = companyId,
                AvailableBalance = availableBalance,
                ReservedBalance = 0m
            };
            await context.Wallets.AddAsync(wallet);
        }

        await context.SaveChangesAsync();
        return new WalletCampaignFixtureCampaignIds(campaign.Id, wallet?.Id, createWalletRow);
    }

    private static async Task<string> AddCampaignMediaAsync(
        ConfiguredWebAppFactory factory,
        string campaignId,
        StoredFileReviewStatus reviewStatus,
        StorageObjectState storageState = StorageObjectState.Active,
        DateTime? deletedAtUtc = null,
        string? supersededByFileId = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var asset = new StoredFile
        {
            OwnerType = StoredFileOwnerType.Campaign,
            OwnerId = campaignId,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = $"campaign-{Guid.NewGuid():N}.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"integration/{campaignId}/{Guid.NewGuid():N}.png",
            StorageState = storageState,
            DeletedAtUtc = deletedAtUtc,
            SupersededByFileId = supersededByFileId,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = reviewStatus,
            CreatedAtUtc = DateTime.UtcNow
        };

        await context.StoredFiles.AddAsync(asset);
        await context.SaveChangesAsync();
        return asset.Id;
    }

    private static CampaignStatus GetRevisionRequiredStatus()
        => Enum.TryParse<CampaignStatus>("RevisionRequired", out var status)
            ? status
            : throw new InvalidOperationException(
                "CampaignStatus.RevisionRequired must be added by Phase 2 before this fixture is used.");

    private static void SetSubmittedAtUtc(Campaign campaign, DateTime? submittedAtUtc)
    {
        if (submittedAtUtc is null)
        {
            return;
        }

        var property = typeof(Campaign).GetProperty("SubmittedAtUtc");
        if (property is null)
        {
            throw new InvalidOperationException(
                "Campaign.SubmittedAtUtc must be added by Phase 2 before this fixture is used.");
        }

        property.SetValue(campaign, submittedAtUtc);
    }
}

public sealed record WalletCampaignFixtureIds(
    string AdminUserId,
    string CompanyUserId,
    string CompanyProfileId,
    string DoctorUserId,
    string DoctorProfileId);

public sealed record WalletCampaignFixtureCampaignIds(
    string CampaignId,
    string? WalletId,
    bool CreatesWalletRow);
