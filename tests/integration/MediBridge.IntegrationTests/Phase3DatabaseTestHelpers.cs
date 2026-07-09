using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.IntegrationTests;

internal static class Phase3DatabaseTestHelpers
{
    public static async Task<Phase3ProfileIds> SeedProfilesAsync(IServiceProvider services)
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

    public static async Task<string> AddCampaignAsync(IServiceProvider services, string companyProfileId, CampaignStatus status = CampaignStatus.Approved, DateTime? submittedAtUtc = null)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaignId = $"campaign-{Guid.NewGuid():N}";
        await context.Campaigns.AddAsync(new Campaign
        {
            Id = campaignId,
            CompanyId = companyProfileId,
            Title = "Phase 3 campaign",
            Description = "Phase 3 integration test campaign.",
            Status = status,
            SubmittedAtUtc = submittedAtUtc
        });
        await context.SaveChangesAsync();
        return campaignId;
    }

    public static Task<decimal> GetAvailableBalanceAsync(IServiceProvider services, string walletId)
    {
        return GetWalletBalanceAsync(services, walletId, available: true);
    }

    public static Task<decimal> GetReservedBalanceAsync(IServiceProvider services, string walletId)
    {
        return GetWalletBalanceAsync(services, walletId, available: false);
    }

    private static async Task<decimal> GetWalletBalanceAsync(IServiceProvider services, string walletId, bool available)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return available
            ? await context.Wallets.Where(wallet => wallet.Id == walletId).Select(wallet => wallet.AvailableBalance).SingleAsync()
            : await context.Wallets.Where(wallet => wallet.Id == walletId).Select(wallet => wallet.ReservedBalance).SingleAsync();
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
}

internal sealed record Phase3ProfileIds(string AdminUserId, string DoctorUserId, string DoctorProfileId, string CompanyUserId, string CompanyProfileId);
