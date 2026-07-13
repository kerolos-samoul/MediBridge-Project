using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.IntegrationTests.Campaigns;

internal static class CompanyReportingIntegrationTestHelpers
{
    public static readonly CompanyReportingDeliverySpec[] MixedDeliverySpecs =
    [
        new("active", DeliveryStatus.Active, new DateOnly(2026, 7, 10), HasFeedback: false),
        new("accepted", DeliveryStatus.Accepted, new DateOnly(2026, 7, 11), HasFeedback: true),
        new("rejected", DeliveryStatus.Rejected, new DateOnly(2026, 7, 12), HasFeedback: false),
        new("expired", DeliveryStatus.Expired, new DateOnly(2026, 7, 13), HasFeedback: false)
    ];

    public static async Task<CompanyReportingSeed> SeedCampaignAsync(
        IServiceProvider services,
        CompanyReportingDeliverySpec[]? deliverySpecs = null,
        bool consistentEvidence = true,
        CampaignStatus status = CampaignStatus.Active,
        string doctorSpecialization = "Cardiology",
        string doctorLocation = "Cairo")
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var companyUser = CreateUser($"phase10-company-{suffix}@medibridge.local", UserRole.Company, AccountStatus.Approved);
        var doctorUser = CreateUser($"phase10-doctor-{suffix}@medibridge.local", UserRole.Doctor, AccountStatus.Approved);
        var company = new CompanyProfile
        {
            Id = $"company-{suffix}",
            UserId = companyUser.Id,
            CompanyName = "Phase 10 Pharma",
            LicenseNumber = $"phase10-license-{suffix}",
            ContactName = "Phase 10 Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "company-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 2048,
            VerificationReference = $"verification/{suffix}/company"
        };
        var doctor = new DoctorProfile
        {
            Id = $"doctor-{suffix}",
            UserId = doctorUser.Id,
            Specialization = doctorSpecialization,
            ExperienceYears = 7,
            Location = doctorLocation,
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}/doctor",
            ActivityScore = 90m,
            Status = DoctorMarketplaceStatus.Active,
            PricePerMessage = 100m,
            DailyMessageLimit = 10
        };
        var campaign = new Campaign
        {
            Id = $"campaign-{suffix}",
            CompanyId = company.Id,
            Title = "Phase 10 reporting campaign",
            Description = "Phase 10 reporting campaign",
            Status = status,
            SubmittedAtUtc = status == CampaignStatus.Draft ? null : new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 7, 1, 7, 0, 0, DateTimeKind.Utc)
        };
        var companyWallet = Wallet($"company-wallet-{suffix}", WalletOwnerType.Company, company.Id, companyUser.Id);
        var doctorWallet = Wallet($"doctor-wallet-{suffix}", WalletOwnerType.Doctor, doctor.Id, doctorUser.Id);

        await db.Users.AddRangeAsync(companyUser, doctorUser);
        await db.CompanyProfiles.AddAsync(company);
        await db.DoctorProfiles.AddAsync(doctor);
        await db.Campaigns.AddAsync(campaign);
        await db.CampaignTargets.AddAsync(new CampaignTarget
        {
            Id = $"target-{suffix}",
            CampaignId = campaign.Id,
            DoctorId = doctor.Id,
            SpecializationSnapshot = doctor.Specialization,
            ExperienceYearsSnapshot = doctor.ExperienceYears,
            LocationSnapshot = doctor.Location,
            ActivityScoreSnapshot = doctor.ActivityScore,
            PricePerMessageSnapshot = 100m
        });
        await db.Wallets.AddRangeAsync(companyWallet, doctorWallet);

        foreach (var spec in deliverySpecs ?? MixedDeliverySpecs)
        {
            var deliveryId = $"delivery-{spec.Key}-{suffix}";
            await db.DoctorAdDeliveries.AddAsync(Delivery(deliveryId, campaign.Id, company.Id, doctor.Id, spec));
            if (spec.Status == DeliveryStatus.Active)
            {
                await db.WalletTransactions.AddAsync(Transaction(companyWallet.Id, WalletTransactionType.Reserve, deliveryId, 100m));
            }
            else if (spec.Status is DeliveryStatus.Accepted or DeliveryStatus.Rejected && consistentEvidence)
            {
                await db.WalletTransactions.AddRangeAsync(
                    Transaction(companyWallet.Id, WalletTransactionType.Charge, deliveryId, 100m),
                    Transaction(doctorWallet.Id, WalletTransactionType.Earn, deliveryId, 80m));
            }
            else if (spec.Status == DeliveryStatus.Expired)
            {
                await db.WalletTransactions.AddAsync(Transaction(companyWallet.Id, WalletTransactionType.Release, deliveryId, 100m));
            }
        }

        await db.SaveChangesAsync();
        return new CompanyReportingSeed(companyUser.Id, company.Id, campaign.Id);
    }

    public static async Task<CompanyReportingSeed> SeedAdditionalCampaignForCompanyAsync(
        IServiceProvider services,
        CompanyReportingSeed owner,
        CompanyReportingDeliverySpec[] deliverySpecs,
        bool consistentEvidence = true,
        CampaignStatus status = CampaignStatus.Active)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var doctorUser = CreateUser($"phase10-extra-doctor-{suffix}@medibridge.local", UserRole.Doctor, AccountStatus.Approved);
        var doctor = new DoctorProfile
        {
            Id = $"doctor-extra-{suffix}",
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 7,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}/doctor",
            ActivityScore = 90m,
            Status = DoctorMarketplaceStatus.Active,
            PricePerMessage = 100m,
            DailyMessageLimit = 10
        };
        var campaign = new Campaign
        {
            Id = $"campaign-extra-{suffix}",
            CompanyId = owner.CompanyId,
            Title = "Phase 10 additional reporting campaign",
            Description = "Phase 10 additional reporting campaign",
            Status = status,
            SubmittedAtUtc = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 7, 1, 7, 0, 0, DateTimeKind.Utc)
        };
        var companyWalletId = db.Wallets
            .Where(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == owner.CompanyId)
            .Select(wallet => wallet.Id)
            .Single();
        var doctorWallet = Wallet($"doctor-wallet-extra-{suffix}", WalletOwnerType.Doctor, doctor.Id, doctorUser.Id);

        await db.Users.AddAsync(doctorUser);
        await db.DoctorProfiles.AddAsync(doctor);
        await db.Campaigns.AddAsync(campaign);
        await db.CampaignTargets.AddAsync(new CampaignTarget
        {
            Id = $"target-extra-{suffix}",
            CampaignId = campaign.Id,
            DoctorId = doctor.Id,
            SpecializationSnapshot = doctor.Specialization,
            ExperienceYearsSnapshot = doctor.ExperienceYears,
            LocationSnapshot = doctor.Location,
            ActivityScoreSnapshot = doctor.ActivityScore,
            PricePerMessageSnapshot = 100m
        });
        await db.Wallets.AddAsync(doctorWallet);

        foreach (var spec in deliverySpecs)
        {
            var deliveryId = $"delivery-extra-{spec.Key}-{suffix}";
            await db.DoctorAdDeliveries.AddAsync(Delivery(deliveryId, campaign.Id, owner.CompanyId, doctor.Id, spec));
            if (spec.Status == DeliveryStatus.Active)
            {
                await db.WalletTransactions.AddAsync(Transaction(companyWalletId, WalletTransactionType.Reserve, deliveryId, 100m));
            }
            else if (spec.Status is DeliveryStatus.Accepted or DeliveryStatus.Rejected && consistentEvidence)
            {
                await db.WalletTransactions.AddRangeAsync(
                    Transaction(companyWalletId, WalletTransactionType.Charge, deliveryId, 100m),
                    Transaction(doctorWallet.Id, WalletTransactionType.Earn, deliveryId, 80m));
            }
        }

        await db.SaveChangesAsync();
        return owner with { CampaignId = campaign.Id };
    }

    private static DoctorAdDelivery Delivery(string id, string campaignId, string companyId, string doctorId, CompanyReportingDeliverySpec spec)
        => new()
        {
            Id = id,
            CampaignId = campaignId,
            CompanyId = companyId,
            DoctorId = doctorId,
            DeliveryDateEgypt = spec.DeliveryDateEgypt,
            DeliveredAtUtc = spec.DeliveryDateEgypt.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), DateTimeKind.Utc),
            ReadAtUtc = spec.Status is DeliveryStatus.Accepted or DeliveryStatus.Rejected ? spec.DeliveryDateEgypt.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(8.5)), DateTimeKind.Utc) : null,
            Status = spec.Status,
            ReservationStatus = spec.Status == DeliveryStatus.Active ? ReservationStatus.Reserved : spec.Status == DeliveryStatus.Expired ? ReservationStatus.Released : ReservationStatus.Charged,
            InteractedAtUtc = spec.Status is DeliveryStatus.Accepted or DeliveryStatus.Rejected ? spec.DeliveryDateEgypt.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9)), DateTimeKind.Utc) : null,
            FeedbackText = spec.HasFeedback ? spec.FeedbackText ?? "Useful campaign feedback." : null,
            FeedbackCreatedAtUtc = spec.HasFeedback ? spec.DeliveryDateEgypt.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9)), DateTimeKind.Utc) : null,
            FeedbackQualityStatus = spec.HasFeedback ? spec.FeedbackQualityStatus ?? FeedbackQualityStatus.Accepted : null,
            PricePerMessageSnapshot = 100m,
            PlatformFeePercentSnapshot = 20m,
            PlatformFeeAmount = 20m,
            DoctorEarnings = 80m,
            ReservedAmount = 100m,
            CreatedAtUtc = spec.DeliveryDateEgypt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        };

    private static Wallet Wallet(string id, WalletOwnerType ownerType, string ownerId, string ownerUserId)
        => new() { Id = id, OwnerType = ownerType, OwnerId = ownerId, OwnerUserId = ownerUserId, Currency = "EGP", CreatedAtUtc = DateTime.UtcNow };

    private static WalletTransaction Transaction(string walletId, WalletTransactionType type, string deliveryId, decimal amount)
        => new() { Id = Guid.NewGuid().ToString("N"), WalletId = walletId, OperationType = type, RelatedDeliveryId = deliveryId, IdempotencyKey = $"{type}:{deliveryId}", Amount = amount, CreatedAtUtc = DateTime.UtcNow };

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role, AccountStatus status)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = role,
            AccountStatus = status,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
}

internal sealed record CompanyReportingSeed(string CompanyUserId, string CompanyId, string CampaignId);

internal sealed record CompanyReportingDeliverySpec(
    string Key,
    DeliveryStatus Status,
    DateOnly DeliveryDateEgypt,
    bool HasFeedback,
    string? FeedbackText = null,
    FeedbackQualityStatus? FeedbackQualityStatus = null);
