using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.IntegrationTests;

internal static class Phase9TestHelpers
{
    public static async Task<Phase9DoctorSeed> SeedApprovedDoctorAsync(
        IServiceProvider services,
        string? specialization = null,
        int dailyMessageLimit = 10,
        int minimumWeeklyRequirement = 5,
        decimal activityScore = 95m,
        CancellationToken cancellationToken = default)
    {
        return await SeedDoctorAsync(
            services,
            AccountStatus.Approved,
            isDeleted: false,
            DoctorMarketplaceStatus.Active,
            suspendedAtUtc: null,
            suspendedUntilUtc: null,
            specialization,
            dailyMessageLimit,
            minimumWeeklyRequirement,
            activityScore,
            cancellationToken);
    }

    public static async Task<Phase9DoctorSeed> SeedUnapprovedDoctorAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        return await SeedDoctorAsync(
            services,
            AccountStatus.Pending,
            isDeleted: false,
            DoctorMarketplaceStatus.Active,
            suspendedAtUtc: null,
            suspendedUntilUtc: null,
            specialization: null,
            dailyMessageLimit: 10,
            minimumWeeklyRequirement: 5,
            activityScore: 95m,
            cancellationToken);
    }

    public static async Task<Phase9DoctorSeed> SeedDeletedDoctorAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        return await SeedDoctorAsync(
            services,
            AccountStatus.Approved,
            isDeleted: true,
            DoctorMarketplaceStatus.Active,
            suspendedAtUtc: null,
            suspendedUntilUtc: null,
            specialization: null,
            dailyMessageLimit: 10,
            minimumWeeklyRequirement: 5,
            activityScore: 95m,
            cancellationToken);
    }

    public static async Task<Phase9DoctorSeed> SeedSuspendedDoctorAsync(
        IServiceProvider services,
        DateTime suspendedAtUtc,
        DateTime suspendedUntilUtc,
        CancellationToken cancellationToken = default)
    {
        return await SeedDoctorAsync(
            services,
            AccountStatus.Approved,
            isDeleted: false,
            DoctorMarketplaceStatus.Suspended,
            suspendedAtUtc,
            suspendedUntilUtc,
            specialization: null,
            dailyMessageLimit: 10,
            minimumWeeklyRequirement: 5,
            activityScore: 95m,
            cancellationToken);
    }

    public static async Task<string> SeedDeliveryAsync(
        IServiceProvider services,
        string doctorId,
        DateOnly deliveryDateEgypt,
        DateTime deliveredAtUtc,
        DeliveryStatus status,
        DateTime? interactedAtUtc = null,
        string? feedbackText = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var company = await SeedDeliveryCompanyAndCampaignAsync(context, cancellationToken);
        var delivery = new DoctorAdDelivery
        {
            Id = $"phase9-delivery-{Guid.NewGuid():N}",
            DoctorId = doctorId,
            CampaignId = company.CampaignId,
            CompanyId = company.CompanyId,
            DeliveryDateEgypt = deliveryDateEgypt,
            DeliveredAtUtc = deliveredAtUtc,
            Status = status,
            InteractedAtUtc = interactedAtUtc,
            FeedbackText = feedbackText,
            FeedbackCreatedAtUtc = feedbackText is null ? null : interactedAtUtc,
            FeedbackQualityStatus = feedbackText is null ? null : FeedbackQualityStatus.Accepted,
            PricePerMessageSnapshot = 10m,
            PlatformFeePercentSnapshot = 10m,
            PlatformFeeAmount = 1m,
            DoctorEarnings = 9m,
            ReservedAmount = 10m,
            ReservationStatus = status is DeliveryStatus.Accepted or DeliveryStatus.Rejected
                ? ReservationStatus.Charged
                : ReservationStatus.Reserved,
            CreatedAtUtc = deliveredAtUtc
        };

        await context.DoctorAdDeliveries.AddAsync(delivery, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return delivery.Id;
    }

    private static async Task<Phase9DeliveryCompanySeed> SeedDeliveryCompanyAndCampaignAsync(
        MediBridgeDbContext context,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"phase9-company-{suffix}@medibridge.local";
        var user = new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = UserRole.Company,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var company = new CompanyProfile
        {
            Id = $"phase9-company-{suffix}",
            UserId = user.Id,
            CompanyName = "Phase 9 Delivery Company",
            LicenseNumber = $"phase9-license-{suffix}",
            ContactName = "Phase 9 Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"phase9/company/{suffix}"
        };
        var campaign = new Campaign
        {
            Id = $"phase9-campaign-{suffix}",
            CompanyId = company.Id,
            Title = "Phase 9 campaign",
            Description = "Phase 9 test campaign.",
            Status = CampaignStatus.Approved,
            SubmittedAtUtc = DateTime.UtcNow
        };

        await context.Users.AddAsync(user, cancellationToken);
        await context.CompanyProfiles.AddAsync(company, cancellationToken);
        await context.Campaigns.AddAsync(campaign, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return new Phase9DeliveryCompanySeed(company.Id, campaign.Id);
    }

    public static async Task<Phase9MutationSnapshot> CaptureNoMutationSnapshotAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        return new Phase9MutationSnapshot(
            await context.DoctorMessageQueues.CountAsync(cancellationToken),
            await context.DoctorAdDeliveries.CountAsync(cancellationToken),
            await context.Wallets.CountAsync(cancellationToken),
            await context.WalletTransactions.CountAsync(cancellationToken),
            await context.WalletLedgerEntries.CountAsync(cancellationToken));
    }

    public static Phase9ActivitySnapshotDraft ActivityScoreSnapshot(
        string doctorId,
        DateOnly scoreDateEgypt,
        decimal finalScore) => new(doctorId, scoreDateEgypt, scoreDateEgypt.AddDays(-30), scoreDateEgypt.AddDays(-1), finalScore);

    public static Phase9WeeklyDecisionDraft WeeklyDecision(
        string doctorId,
        DateOnly weekStartDateEgypt,
        int minimumWeeklyRequirement,
        int interactionCount) => new(doctorId, weekStartDateEgypt, weekStartDateEgypt.AddDays(7), minimumWeeklyRequirement, interactionCount);

    public static Phase9ViolationDraft WeeklyViolation(
        string doctorId,
        DateOnly weekStartDateEgypt,
        int rollingViolationCount) => new(doctorId, weekStartDateEgypt, weekStartDateEgypt.AddDays(7), rollingViolationCount);

    public static async Task<string> SeedWeeklyViolationAsync(
        IServiceProvider services,
        string doctorId,
        DateOnly weekStartDateEgypt,
        int rollingViolationCount,
        int minimumWeeklyRequirement = 5,
        int interactionCount = 0,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var decisionId = $"phase9-weekly-decision-{Guid.NewGuid():N}";
        var violationId = $"phase9-weekly-violation-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        var decision = new WeeklyEnforcementDecision
        {
            Id = decisionId,
            DoctorId = doctorId,
            WeekStartDateEgypt = weekStartDateEgypt,
            WeekEndDateEgypt = weekStartDateEgypt.AddDays(7),
            MinimumWeeklyRequirement = minimumWeeklyRequirement,
            InteractionCount = interactionCount,
            Decision = WeeklyEnforcementDecisionType.Violation,
            SuspensionOverlapped = false,
            RollingViolationCountAfterDecision = rollingViolationCount,
            CreatedAtUtc = now
        };
        var violation = new DoctorWeeklyViolation
        {
            Id = violationId,
            DoctorId = doctorId,
            WeeklyEnforcementDecisionId = decisionId,
            WeekStartDateEgypt = weekStartDateEgypt,
            WeekEndDateEgypt = weekStartDateEgypt.AddDays(7),
            MinimumWeeklyRequirement = minimumWeeklyRequirement,
            InteractionCount = interactionCount,
            RollingViolationCount = rollingViolationCount,
            CreatedAtUtc = now
        };

        await context.WeeklyEnforcementDecisions.AddAsync(decision, cancellationToken);
        await context.DoctorWeeklyViolations.AddAsync(violation, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return violationId;
    }

    public static Phase9EnforcementActionDraft EnforcementAction(
        string doctorId,
        string actorAdminUserId,
        string actionType,
        string reason) => new(doctorId, actorAdminUserId, actionType, reason);

    private static async Task<Phase9DoctorSeed> SeedDoctorAsync(
        IServiceProvider services,
        AccountStatus accountStatus,
        bool isDeleted,
        DoctorMarketplaceStatus status,
        DateTime? suspendedAtUtc,
        DateTime? suspendedUntilUtc,
        string? specialization,
        int dailyMessageLimit,
        int minimumWeeklyRequirement,
        decimal activityScore,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"phase9-doctor-{suffix}@medibridge.local";
        var user = new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = UserRole.Doctor,
            AccountStatus = accountStatus,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var profile = new DoctorProfile
        {
            Id = $"phase9-doctor-{suffix}",
            UserId = user.Id,
            Specialization = specialization ?? "Cardiology",
            ExperienceYears = 9,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"phase9/verification/{suffix}",
            DailyMessageLimit = dailyMessageLimit,
            MinimumWeeklyRequirement = minimumWeeklyRequirement,
            ActivityScore = activityScore,
            Status = status,
            SuspendedAtUtc = suspendedAtUtc,
            SuspendedUntilUtc = suspendedUntilUtc,
            LastStatusChangedAtUtc = suspendedAtUtc,
            PricePerMessage = 50m,
            IsDeleted = isDeleted,
            DeletedAtUtc = isDeleted ? DateTime.UtcNow : null
        };

        await context.Users.AddAsync(user, cancellationToken);
        await context.DoctorProfiles.AddAsync(profile, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return new Phase9DoctorSeed(user.Id, profile.Id, suspendedAtUtc, suspendedUntilUtc);
    }
}

internal sealed record Phase9DoctorSeed(
    string UserId,
    string DoctorId,
    DateTime? SuspendedAtUtc = null,
    DateTime? SuspendedUntilUtc = null);

internal sealed record Phase9DeliveryCompanySeed(string CompanyId, string CampaignId);

internal sealed record Phase9MutationSnapshot(
    int DoctorMessageQueueCount,
    int DoctorAdDeliveryCount,
    int WalletCount,
    int WalletTransactionCount,
    int WalletLedgerEntryCount);

internal sealed record Phase9ActivitySnapshotDraft(
    string DoctorId,
    DateOnly ScoreDateEgypt,
    DateOnly WindowStartDateEgypt,
    DateOnly WindowEndDateEgypt,
    decimal FinalScore);

internal sealed record Phase9WeeklyDecisionDraft(
    string DoctorId,
    DateOnly WeekStartDateEgypt,
    DateOnly WeekEndDateEgypt,
    int MinimumWeeklyRequirement,
    int InteractionCount);

internal sealed record Phase9ViolationDraft(
    string DoctorId,
    DateOnly WeekStartDateEgypt,
    DateOnly WeekEndDateEgypt,
    int RollingViolationCount);

internal sealed record Phase9EnforcementActionDraft(
    string DoctorId,
    string ActorAdminUserId,
    string ActionType,
    string Reason);
