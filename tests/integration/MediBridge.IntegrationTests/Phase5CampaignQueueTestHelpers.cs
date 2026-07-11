using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.IntegrationTests;

public static class Phase5CampaignQueueTestHelpers
{
    public static async Task<Phase5CompanySeed> SeedApprovedCompanyAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = CreateUser($"phase5-company-{suffix}@medibridge.local", UserRole.Company, AccountStatus.Approved);
        var profile = new CompanyProfile
        {
            Id = $"company-{suffix}",
            UserId = user.Id,
            CompanyName = "Phase 5 Pharma",
            LicenseNumber = $"phase5-license-{suffix}",
            ContactName = "Phase 5 Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "company-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 2048,
            VerificationReference = $"verification/{suffix}/company"
        };

        await context.Users.AddAsync(user);
        await context.CompanyProfiles.AddAsync(profile);
        await context.SaveChangesAsync();
        return new Phase5CompanySeed(user.Id, profile.Id);
    }

    public static async Task<Phase5DoctorSeed> SeedApprovedDoctorAsync(
        IServiceProvider services,
        decimal pricePerMessage = 50m,
        decimal activityScore = 95m,
        int dailyMessageLimit = 10)
    {
        return await SeedDoctorAsync(services, AccountStatus.Approved, DoctorMarketplaceStatus.Active, isDeleted: false, pricePerMessage, activityScore, dailyMessageLimit);
    }

    public static async Task<Phase5DoctorSeed> SeedSuspendedDoctorAsync(IServiceProvider services)
    {
        return await SeedDoctorAsync(services, AccountStatus.Approved, DoctorMarketplaceStatus.Suspended, isDeleted: false, 50m, 20m, 10);
    }

    public static async Task<Phase5DoctorSeed> SeedSoftDeletedDoctorAsync(IServiceProvider services)
    {
        return await SeedDoctorAsync(services, AccountStatus.Approved, DoctorMarketplaceStatus.Active, isDeleted: true, 50m, 20m, 10);
    }

    public static async Task<Phase5DoctorSeed> SeedZeroPriceDoctorAsync(IServiceProvider services)
    {
        return await SeedDoctorAsync(services, AccountStatus.Approved, DoctorMarketplaceStatus.Active, isDeleted: false, 0m, 20m, 10);
    }

    public static async Task<Phase5DoctorSeed> SeedZeroDailyLimitDoctorAsync(IServiceProvider services)
    {
        return await SeedDoctorAsync(services, AccountStatus.Approved, DoctorMarketplaceStatus.Active, isDeleted: false, 50m, 20m, 0);
    }

    public static async Task<string> SeedApprovedCampaignAssetAsync(IServiceProvider services, string companyId, string? campaignId = null)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var file = new StoredFile
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = companyId,
            RelatedCampaignId = campaignId,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "phase5-asset.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"campaigns/{Guid.NewGuid():N}/phase5-asset.png",
            StorageProvider = "TestStorage",
            StorageResourceType = StoredFileStorageResourceType.Image,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Approved,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            ReviewedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        await context.StoredFiles.AddAsync(file);
        await context.SaveChangesAsync();
        return file.Id;
    }

    public static async Task<string> SeedCompanyWalletAsync(IServiceProvider services, string companyId, string companyUserId, decimal availableBalance = 0m)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = new Wallet
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerType = WalletOwnerType.Company,
            OwnerId = companyId,
            OwnerUserId = companyUserId,
            AvailableBalance = availableBalance,
            ReservedBalance = 0m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow
        };

        await context.Wallets.AddAsync(wallet);
        await context.SaveChangesAsync();
        return wallet.Id;
    }

    public static async Task<string> SeedCampaignAsync(
        IServiceProvider services,
        string companyId,
        CampaignStatus status = CampaignStatus.PendingReview,
        DateTime? submittedAtUtc = null)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var createdAtUtc = DateTime.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid().ToString("N"),
            CompanyId = companyId,
            Title = "Phase 5 campaign",
            Description = "Phase 5 seeded campaign",
            ClinicalResearchInfo = "Phase 5 research context",
            Status = status,
            SubmittedAtUtc = status == CampaignStatus.Draft ? null : submittedAtUtc ?? createdAtUtc,
            CreatedAtUtc = createdAtUtc
        };

        await context.Campaigns.AddAsync(campaign);
        await context.SaveChangesAsync();
        return campaign.Id;
    }

    public static async Task<string> SeedQueueItemAsync(IServiceProvider services, string campaignId, string doctorId, DateTime? queuedAtUtc = null)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaignSubmittedAtUtc = await context.Campaigns
            .Where(campaign => campaign.Id == campaignId)
            .Select(campaign => campaign.SubmittedAtUtc)
            .SingleAsync()
            ?? throw new InvalidOperationException("The queue fixture requires an authentic campaign submission timestamp.");
        var queueItem = new DoctorMessageQueue
        {
            Id = Guid.NewGuid().ToString("N"),
            CampaignId = campaignId,
            DoctorId = doctorId,
            CampaignSubmittedAtUtc = campaignSubmittedAtUtc,
            QueuedAtUtc = queuedAtUtc ?? DateTime.UtcNow,
            Status = QueueItemStatus.Queued,
            CreatedAtUtc = DateTime.UtcNow
        };

        await context.DoctorMessageQueues.AddAsync(queueItem);
        await context.SaveChangesAsync();
        return queueItem.Id;
    }

    public static async Task<DoctorMessageQueue?> FindQueueItemAsync(IServiceProvider services, string campaignId, string doctorId)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await context.DoctorMessageQueues.FirstOrDefaultAsync(queue => queue.CampaignId == campaignId && queue.DoctorId == doctorId);
    }

    public static async Task<CampaignQueueCreationResultDto> TriggerApprovedCampaignQueueCreationAsync(
        IServiceProvider services,
        string campaignId,
        DateTime queuedAtUtc,
        string? actorUserId = null)
    {
        using var scope = services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ICampaignWorkflowService>();
        return await workflow.CreateQueueForApprovedCampaignAsync(campaignId, queuedAtUtc, actorUserId, CancellationToken.None);
    }

    private static async Task<Phase5DoctorSeed> SeedDoctorAsync(
        IServiceProvider services,
        AccountStatus accountStatus,
        DoctorMarketplaceStatus marketplaceStatus,
        bool isDeleted,
        decimal pricePerMessage,
        decimal activityScore,
        int dailyMessageLimit)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = CreateUser($"phase5-doctor-{suffix}@medibridge.local", UserRole.Doctor, accountStatus);
        var profile = new DoctorProfile
        {
            Id = $"doctor-{suffix}",
            UserId = user.Id,
            Specialization = "Cardiology",
            ExperienceYears = 7,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}/doctor",
            ActivityScore = activityScore,
            Status = marketplaceStatus,
            PricePerMessage = pricePerMessage,
            DailyMessageLimit = dailyMessageLimit,
            IsDeleted = isDeleted,
            DeletedAtUtc = isDeleted ? DateTime.UtcNow : null
        };

        await context.Users.AddAsync(user);
        await context.DoctorProfiles.AddAsync(profile);
        await context.SaveChangesAsync();
        return new Phase5DoctorSeed(user.Id, profile.Id);
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role, AccountStatus status)
    {
        var normalized = email.ToUpperInvariant();
        return new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = normalized,
            Email = email,
            NormalizedEmail = normalized,
            Role = role,
            AccountStatus = status,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}

public sealed record Phase5CompanySeed(string UserId, string CompanyId);

public sealed record Phase5DoctorSeed(string UserId, string DoctorId);
