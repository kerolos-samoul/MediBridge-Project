using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3CoreEntityPersistenceTests
{
    [Fact]
    public async Task DomainUnitOfWork_CreatesAndReads_Phase3SampleGraph()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        var ids = await SeedProfilesAsync(factory.Services);

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var campaignId = $"campaign-{Guid.NewGuid():N}";
        var targetId = $"target-{Guid.NewGuid():N}";
        var reviewId = $"review-{Guid.NewGuid():N}";
        var queueId = $"queue-{Guid.NewGuid():N}";
        var deliveryId = $"delivery-{Guid.NewGuid():N}";
        var companyWalletId = $"wallet-company-{Guid.NewGuid():N}";
        var transactionId = $"transaction-{Guid.NewGuid():N}";
        var ledgerEntryId = $"ledger-{Guid.NewGuid():N}";
        var withdrawalId = $"withdrawal-{Guid.NewGuid():N}";
        var storedFileId = $"file-{Guid.NewGuid():N}";
        var doctorPriceHistoryId = $"doctor-price-{Guid.NewGuid():N}";
        var platformFeeHistoryId = $"platform-fee-{Guid.NewGuid():N}";
        var activityHistoryId = $"activity-{Guid.NewGuid():N}";
        var auditEventId = $"audit-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        await unitOfWork.StoredFiles.AddStoredFileAsync(storedFileId, StoredFileOwnerType.Company, ids.CompanyProfileId, StoredFilePurpose.CampaignMedia);
        await unitOfWork.Campaigns.AddCampaignAsync(campaignId, ids.CompanyProfileId, CampaignStatus.Approved);
        await unitOfWork.Campaigns.AddCampaignTargetAsync(targetId, campaignId, ids.DoctorProfileId);
        await unitOfWork.Campaigns.AddCampaignReviewHistoryAsync(reviewId, campaignId, ids.AdminUserId, CampaignReviewDecision.Approved);
        await unitOfWork.MessageQueues.AddQueueItemAsync(queueId, ids.DoctorProfileId, campaignId, now.AddHours(-1), now);
        await unitOfWork.Deliveries.AddDeliveryAsync(deliveryId, ids.DoctorProfileId, campaignId, ids.CompanyProfileId, DateOnly.FromDateTime(now), 50m, 10m, 5m, 45m, 50m, now);
        await unitOfWork.Wallets.AddWalletAsync(companyWalletId, WalletOwnerType.Company, ids.CompanyProfileId, ids.CompanyUserId);
        await unitOfWork.WalletTransactions.AddTransactionAsync(transactionId, companyWalletId, WalletTransactionType.TopUp, $"top-up-{Guid.NewGuid():N}", 100m);
        await unitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(ledgerEntryId, transactionId, companyWalletId, WalletLedgerEntryDirection.Credit, WalletBalanceType.Available, 100m);
        await unitOfWork.PolicyHistory.AddDoctorPriceHistoryAsync(doctorPriceHistoryId, ids.DoctorProfileId, null, 50m, ids.AdminUserId);
        await unitOfWork.PolicyHistory.AddPlatformFeePolicyHistoryAsync(platformFeeHistoryId, 10m, now, ids.AdminUserId);
        await unitOfWork.PolicyHistory.AddActivityScoreHistoryAsync(activityHistoryId, ids.DoctorProfileId, 95m, DateOnly.FromDateTime(now.AddDays(-7)), DateOnly.FromDateTime(now));
        await unitOfWork.AuditEvents.AddAuditEventAsync(auditEventId, "phase3.sample", AuditOutcome.Info, now);
        await context.WithdrawalRequests.AddAsync(new WithdrawalRequest
        {
            Id = withdrawalId,
            DoctorId = ids.DoctorProfileId,
            Amount = 25m
        });

        await unitOfWork.SaveChangesAsync();

        Assert.Equal(campaignId, await unitOfWork.Campaigns.FindActiveCampaignIdAsync(campaignId));
        Assert.Contains(targetId, await unitOfWork.Campaigns.ListCampaignTargetIdsAsync(campaignId));
        Assert.Contains(reviewId, await unitOfWork.Campaigns.ListCampaignReviewHistoryIdsAsync(campaignId));
        Assert.Contains(queueId, await unitOfWork.MessageQueues.ListActiveQueueItemIdsForDoctorAsync(ids.DoctorProfileId, QueueItemStatus.Queued));
        Assert.Equal(deliveryId, await unitOfWork.Deliveries.FindDeliveryIdAsync(ids.DoctorProfileId, DateOnly.FromDateTime(now), campaignId));
        var persistedDelivery = await context.DoctorAdDeliveries.SingleAsync(delivery => delivery.Id == deliveryId);
        Assert.Equal(50m, persistedDelivery.PricePerMessageSnapshot);
        Assert.Equal(10m, persistedDelivery.PlatformFeePercentSnapshot);
        Assert.Equal(5m, persistedDelivery.PlatformFeeAmount);
        Assert.Equal(45m, persistedDelivery.DoctorEarnings);
        Assert.Equal(50m, persistedDelivery.ReservedAmount);
        Assert.Equal(companyWalletId, await unitOfWork.Wallets.FindActiveWalletIdByOwnerAsync(WalletOwnerType.Company, ids.CompanyProfileId));
        Assert.Equal(transactionId, await unitOfWork.WalletTransactions.FindTransactionIdByIdempotencyAsync(WalletTransactionType.TopUp, await context.WalletTransactions.Where(transaction => transaction.Id == transactionId).Select(transaction => transaction.IdempotencyKey).SingleAsync()));
        Assert.Contains(ledgerEntryId, await unitOfWork.WalletLedgerEntries.ListLedgerEntryIdsByWalletTransactionAsync(transactionId));
        Assert.Equal(storedFileId, await unitOfWork.StoredFiles.FindActiveStoredFileIdAsync(storedFileId));
        Assert.Contains(doctorPriceHistoryId, await unitOfWork.PolicyHistory.ListDoctorPriceHistoryIdsAsync(ids.DoctorProfileId));
        Assert.Contains(platformFeeHistoryId, await unitOfWork.PolicyHistory.ListPlatformFeePolicyHistoryIdsAsync(now));
        Assert.Contains(activityHistoryId, await unitOfWork.PolicyHistory.ListActivityScoreHistoryIdsAsync(ids.DoctorProfileId));
        Assert.Equal(auditEventId, await unitOfWork.AuditEvents.FindAuditEventIdAsync(auditEventId));
        Assert.True(await context.WithdrawalRequests.AnyAsync(request => request.Id == withdrawalId));
    }

    private static async Task<Phase3ProfileIds> SeedProfilesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var adminUser = CreateUser($"admin-{suffix}@medibridge.local", UserRole.Admin);
        var doctorUser = CreateUser($"doctor-{suffix}@medibridge.local", UserRole.Doctor);
        var companyUser = CreateUser($"company-{suffix}@medibridge.local", UserRole.Company);

        await context.Users.AddRangeAsync(adminUser, doctorUser, companyUser);

        var doctorProfile = new DoctorProfile
        {
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 7,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}/doctor",
            PricePerMessage = 50m
        };

        var companyProfile = new CompanyProfile
        {
            UserId = companyUser.Id,
            CompanyName = "Phase 3 Pharma",
            LicenseNumber = $"license-{suffix}",
            ContactName = "Phase 3 Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "company-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 2048,
            VerificationReference = $"verification/{suffix}/company"
        };

        await context.DoctorProfiles.AddAsync(doctorProfile);
        await context.CompanyProfiles.AddAsync(companyProfile);
        await context.SaveChangesAsync();

        return new Phase3ProfileIds(adminUser.Id, doctorUser.Id, doctorProfile.Id, companyUser.Id, companyProfile.Id);
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role)
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
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private sealed record Phase3ProfileIds(string AdminUserId, string DoctorUserId, string DoctorProfileId, string CompanyUserId, string CompanyProfileId);
}
