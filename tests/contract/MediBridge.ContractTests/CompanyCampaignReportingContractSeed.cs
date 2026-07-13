using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.ContractTests;

internal static class CompanyCampaignReportingContractSeed
{
    public static async Task<SeedResult> SeedAsync(ContractWebAppFactory factory, bool includeDeliveries, bool includeFeedback = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var companyUser = CreateUser($"phase10-contract-company-{suffix}@example.test", UserRole.Company, AccountStatus.Approved);
        var doctorUser = CreateUser($"phase10-contract-doctor-{suffix}@example.test", UserRole.Doctor, AccountStatus.Approved);
        var company = new CompanyProfile { Id = $"company-{suffix}", UserId = companyUser.Id, CompanyName = "Phase 10 Pharma", LicenseNumber = $"license-{suffix}", ContactName = "Contact", VerificationDocumentType = "License", VerificationOriginalFileName = "license.pdf", VerificationContentType = "application/pdf", VerificationSizeBytes = 1024, VerificationReference = $"verification/{suffix}" };
        var doctor = new DoctorProfile { Id = $"doctor-{suffix}", UserId = doctorUser.Id, Specialization = "Cardiology", ExperienceYears = 7, Location = "Cairo", VerificationDocumentType = "License", VerificationOriginalFileName = "doctor.pdf", VerificationContentType = "application/pdf", VerificationSizeBytes = 1024, VerificationReference = $"verification/{suffix}/doctor", DailyMessageLimit = 10, PricePerMessage = 100m };
        var campaign = new Campaign { Id = $"campaign-{suffix}", CompanyId = company.Id, Title = "Reporting campaign", Description = "Reporting campaign", Status = CampaignStatus.Active, SubmittedAtUtc = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc), CreatedAtUtc = new DateTime(2026, 7, 1, 7, 0, 0, DateTimeKind.Utc) };
        var companyWallet = new Wallet { Id = $"company-wallet-{suffix}", OwnerType = WalletOwnerType.Company, OwnerId = company.Id, OwnerUserId = companyUser.Id, Currency = "EGP", CreatedAtUtc = DateTime.UtcNow };
        await db.Users.AddRangeAsync(companyUser, doctorUser);
        await db.CompanyProfiles.AddAsync(company);
        await db.DoctorProfiles.AddAsync(doctor);
        await db.Campaigns.AddAsync(campaign);
        await db.CampaignTargets.AddAsync(new CampaignTarget { Id = $"target-{suffix}", CampaignId = campaign.Id, DoctorId = doctor.Id, SpecializationSnapshot = doctor.Specialization, ExperienceYearsSnapshot = doctor.ExperienceYears, LocationSnapshot = doctor.Location, ActivityScoreSnapshot = 90m, PricePerMessageSnapshot = 100m });
        await db.Wallets.AddAsync(companyWallet);
        if (includeDeliveries)
        {
            var deliveryId = $"delivery-{suffix}";
            await db.DoctorAdDeliveries.AddAsync(new DoctorAdDelivery
            {
                Id = deliveryId,
                CampaignId = campaign.Id,
                CompanyId = company.Id,
                DoctorId = doctor.Id,
                DeliveryDateEgypt = new DateOnly(2026, 7, 13),
                DeliveredAtUtc = new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc),
                Status = includeFeedback ? DeliveryStatus.Accepted : DeliveryStatus.Active,
                ReservationStatus = includeFeedback ? ReservationStatus.Charged : ReservationStatus.Reserved,
                InteractedAtUtc = includeFeedback ? new DateTime(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc) : null,
                FeedbackText = includeFeedback ? "Useful feedback" : null,
                FeedbackCreatedAtUtc = includeFeedback ? new DateTime(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc) : null,
                FeedbackQualityStatus = includeFeedback ? FeedbackQualityStatus.Accepted : null,
                PricePerMessageSnapshot = 100m,
                PlatformFeePercentSnapshot = 20m,
                PlatformFeeAmount = 20m,
                DoctorEarnings = 80m,
                ReservedAmount = 100m,
                CreatedAtUtc = new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc)
            });
            await db.WalletTransactions.AddAsync(new WalletTransaction { Id = Guid.NewGuid().ToString("N"), WalletId = companyWallet.Id, OperationType = WalletTransactionType.Reserve, RelatedDeliveryId = deliveryId, IdempotencyKey = $"Reserve:{deliveryId}", Amount = 100m, CreatedAtUtc = DateTime.UtcNow });
        }

        await db.SaveChangesAsync();
        return new SeedResult(companyUser.Id, company.Id, campaign.Id);
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role, AccountStatus status)
        => new() { Id = Guid.NewGuid().ToString("N"), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), Role = role, AccountStatus = status, EmailVerified = true, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
}

internal sealed record SeedResult(string CompanyUserId, string CompanyId, string CampaignId);
