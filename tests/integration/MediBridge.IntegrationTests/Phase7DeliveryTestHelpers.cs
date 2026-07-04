using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.IntegrationTests;

public static class Phase7DeliveryTestHelpers
{
    public static Task<Phase7DoctorSeed> SeedApprovedDoctorAsync(
        IServiceProvider services,
        string userId,
        string doctorId,
        string email,
        decimal pricePerMessage,
        int dailyMessageLimit,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        return SeedDoctorAsync(
            services,
            userId,
            doctorId,
            email,
            AccountStatus.Approved,
            DoctorMarketplaceStatus.Active,
            isDeleted: false,
            pricePerMessage,
            dailyMessageLimit,
            createdAtUtc,
            deletedAtUtc: null,
            cancellationToken);
    }

    public static Task<Phase7DoctorSeed> SeedSuspendedDoctorAsync(
        IServiceProvider services,
        string userId,
        string doctorId,
        string email,
        decimal pricePerMessage,
        int dailyMessageLimit,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        return SeedDoctorAsync(
            services,
            userId,
            doctorId,
            email,
            AccountStatus.Suspended,
            DoctorMarketplaceStatus.Suspended,
            isDeleted: false,
            pricePerMessage,
            dailyMessageLimit,
            createdAtUtc,
            deletedAtUtc: null,
            cancellationToken);
    }

    public static Task<Phase7DoctorSeed> SeedDeletedDoctorAsync(
        IServiceProvider services,
        string userId,
        string doctorId,
        string email,
        decimal pricePerMessage,
        int dailyMessageLimit,
        DateTime createdAtUtc,
        DateTime deletedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return SeedDoctorAsync(
            services,
            userId,
            doctorId,
            email,
            AccountStatus.Inactive,
            DoctorMarketplaceStatus.Active,
            isDeleted: true,
            pricePerMessage,
            dailyMessageLimit,
            createdAtUtc,
            deletedAtUtc,
            cancellationToken);
    }

    public static Task<Phase7CompanySeed> SeedApprovedCompanyAsync(
        IServiceProvider services,
        string userId,
        string companyId,
        string email,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        return SeedCompanyAsync(
            services,
            userId,
            companyId,
            email,
            AccountStatus.Approved,
            isDeleted: false,
            createdAtUtc,
            deletedAtUtc: null,
            cancellationToken);
    }

    public static Task<Phase7CompanySeed> SeedSuspendedCompanyAsync(
        IServiceProvider services,
        string userId,
        string companyId,
        string email,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        return SeedCompanyAsync(
            services,
            userId,
            companyId,
            email,
            AccountStatus.Suspended,
            isDeleted: false,
            createdAtUtc,
            deletedAtUtc: null,
            cancellationToken);
    }

    public static Task<Phase7CompanySeed> SeedDeletedCompanyAsync(
        IServiceProvider services,
        string userId,
        string companyId,
        string email,
        DateTime createdAtUtc,
        DateTime deletedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return SeedCompanyAsync(
            services,
            userId,
            companyId,
            email,
            AccountStatus.Inactive,
            isDeleted: true,
            createdAtUtc,
            deletedAtUtc,
            cancellationToken);
    }

    public static async Task SeedEffectiveFeePolicyAsync(
        IServiceProvider services,
        string policyId,
        string changedByAdminUserId,
        decimal feePercent,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.PlatformFeePolicyHistories.AddAsync(new PlatformFeePolicyHistory
        {
            Id = policyId,
            FeePercent = feePercent,
            EffectiveFromUtc = effectiveFromUtc,
            EffectiveToUtc = effectiveToUtc,
            ChangedByAdminUserId = changedByAdminUserId,
            CreatedAtUtc = createdAtUtc
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedCampaignAsync(
        IServiceProvider services,
        string campaignId,
        string companyId,
        CampaignStatus status,
        DateTime submittedAtUtc,
        DateTime createdAtUtc,
        bool isDeleted,
        DateTime? deletedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Campaigns.AddAsync(new Campaign
        {
            Id = campaignId,
            CompanyId = companyId,
            Title = $"Campaign {campaignId}",
            Description = "Phase 7 delivery test campaign",
            ClinicalResearchInfo = "Phase 7 test research",
            Status = status,
            SubmittedAtUtc = submittedAtUtc,
            CreatedAtUtc = createdAtUtc,
            IsDeleted = isDeleted,
            DeletedAtUtc = deletedAtUtc
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedApprovedCampaignAssetAsync(
        IServiceProvider services,
        string fileId,
        string campaignId,
        string companyId,
        StoredFilePurpose purpose,
        string storageKey,
        DateTime createdAtUtc,
        DateTime reviewedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = await context.Campaigns.FindAsync([campaignId], cancellationToken)
            ?? throw new InvalidOperationException($"Campaign '{campaignId}' was not seeded.");

        var file = new StoredFile
        {
            Id = fileId,
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = companyId,
            RelatedCampaignId = campaignId,
            Purpose = purpose,
            OriginalFileName = $"{fileId}.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            StorageKey = storageKey,
            StorageProvider = "TestStorage",
            StorageResourceType = StoredFileStorageResourceType.Raw,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Approved,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Passed,
            CreatedAtUtc = createdAtUtc,
            ReviewedAtUtc = reviewedAtUtc
        };

        if (purpose == StoredFilePurpose.CampaignMedia)
        {
            campaign.MediaFileId = fileId;
        }
        else if (purpose == StoredFilePurpose.VoiceNote)
        {
            campaign.VoiceNoteFileId = fileId;
        }

        await context.StoredFiles.AddAsync(file, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedFifoQueueRowAsync(
        IServiceProvider services,
        string queueId,
        string campaignId,
        string doctorId,
        DateTime campaignSubmittedAtUtc,
        DateTime queuedAtUtc,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.DoctorMessageQueues.AddAsync(new DoctorMessageQueue
        {
            Id = queueId,
            CampaignId = campaignId,
            DoctorId = doctorId,
            CampaignSubmittedAtUtc = campaignSubmittedAtUtc,
            QueuedAtUtc = queuedAtUtc,
            Status = QueueItemStatus.Queued,
            CreatedAtUtc = createdAtUtc
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedCompanyWalletAsync(
        IServiceProvider services,
        string walletId,
        string companyId,
        string companyUserId,
        decimal availableBalance,
        decimal reservedBalance,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Wallets.AddAsync(new Wallet
        {
            Id = walletId,
            OwnerType = WalletOwnerType.Company,
            OwnerId = companyId,
            OwnerUserId = companyUserId,
            AvailableBalance = availableBalance,
            ReservedBalance = reservedBalance,
            Currency = "EGP",
            CreatedAtUtc = createdAtUtc
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedDeliveryAsync(
        IServiceProvider services,
        string deliveryId,
        string doctorId,
        string campaignId,
        string companyId,
        DateOnly deliveryDateEgypt,
        DateTime deliveredAtUtc,
        DeliveryStatus status,
        ReservationStatus reservationStatus,
        decimal pricePerMessageSnapshot,
        decimal platformFeePercentSnapshot,
        decimal platformFeeAmount,
        decimal doctorEarnings,
        decimal reservedAmount,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.DoctorAdDeliveries.AddAsync(new DoctorAdDelivery
        {
            Id = deliveryId,
            DoctorId = doctorId,
            CampaignId = campaignId,
            CompanyId = companyId,
            DeliveryDateEgypt = deliveryDateEgypt,
            DeliveredAtUtc = deliveredAtUtc,
            Status = status,
            ReservationStatus = reservationStatus,
            PricePerMessageSnapshot = pricePerMessageSnapshot,
            PlatformFeePercentSnapshot = platformFeePercentSnapshot,
            PlatformFeeAmount = platformFeeAmount,
            DoctorEarnings = doctorEarnings,
            ReservedAmount = reservedAmount,
            CreatedAtUtc = createdAtUtc
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Phase7DoctorSeed> SeedDoctorAsync(
        IServiceProvider services,
        string userId,
        string doctorId,
        string email,
        AccountStatus accountStatus,
        DoctorMarketplaceStatus marketplaceStatus,
        bool isDeleted,
        decimal pricePerMessage,
        int dailyMessageLimit,
        DateTime createdAtUtc,
        DateTime? deletedAtUtc,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = CreateUser(userId, email, UserRole.Doctor, accountStatus, isDeleted, createdAtUtc, deletedAtUtc);
        var profile = new DoctorProfile
        {
            Id = doctorId,
            UserId = userId,
            Specialization = "Cardiology",
            ExperienceYears = 10,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = $"{doctorId}-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"phase7/{doctorId}/license",
            DailyMessageLimit = dailyMessageLimit,
            ActivityScore = 95m,
            Status = marketplaceStatus,
            PricePerMessage = pricePerMessage,
            CreatedAtUtc = createdAtUtc,
            IsDeleted = isDeleted,
            DeletedAtUtc = deletedAtUtc
        };

        await context.Users.AddAsync(user, cancellationToken);
        await context.DoctorProfiles.AddAsync(profile, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return new Phase7DoctorSeed(userId, doctorId);
    }

    private static async Task<Phase7CompanySeed> SeedCompanyAsync(
        IServiceProvider services,
        string userId,
        string companyId,
        string email,
        AccountStatus accountStatus,
        bool isDeleted,
        DateTime createdAtUtc,
        DateTime? deletedAtUtc,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = CreateUser(userId, email, UserRole.Company, accountStatus, isDeleted, createdAtUtc, deletedAtUtc);
        var profile = new CompanyProfile
        {
            Id = companyId,
            UserId = userId,
            CompanyName = $"Company {companyId}",
            LicenseNumber = $"license-{companyId}",
            ContactName = "Phase 7 Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = $"{companyId}-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 2048,
            VerificationReference = $"phase7/{companyId}/license",
            CreatedAtUtc = createdAtUtc,
            IsDeleted = isDeleted,
            DeletedAtUtc = deletedAtUtc
        };

        await context.Users.AddAsync(user, cancellationToken);
        await context.CompanyProfiles.AddAsync(profile, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return new Phase7CompanySeed(userId, companyId);
    }

    private static MediBridgeIdentityUser CreateUser(
        string userId,
        string email,
        UserRole role,
        AccountStatus accountStatus,
        bool isDeleted,
        DateTime createdAtUtc,
        DateTime? deletedAtUtc)
    {
        var normalizedEmail = email.ToUpperInvariant();
        return new MediBridgeIdentityUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = normalizedEmail,
            Email = email,
            NormalizedEmail = normalizedEmail,
            Role = role,
            AccountStatus = accountStatus,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = createdAtUtc,
            IsDeleted = isDeleted,
            DeletedAtUtc = deletedAtUtc
        };
    }
}

public sealed record Phase7CompanySeed(string UserId, string CompanyId);

public sealed record Phase7DoctorSeed(string UserId, string DoctorId);
