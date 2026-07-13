using System.Net;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CompanyCampaignReportingContractTests
{
    [Fact]
    public async Task GetCompanyCampaigns_ReturnsReportingFieldsEnvelopeAndPaginationMetadata()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedReportingCampaignAsync(factory, consistentEvidence: true);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        using var response = await client.GetAsync("/api/company/campaigns?fromDateEgypt=2026-07-01&toDateEgypt=2026-07-13&PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        var page = data.GetProperty("Page");
        Assert.Equal(1, page.GetProperty("PageNumber").GetInt32());
        Assert.Equal(20, page.GetProperty("PageSize").GetInt32());
        Assert.Equal(1, page.GetProperty("TotalCount").GetInt32());
        var item = data.GetProperty("Items")[0];
        Assert.Equal(seed.CampaignId, item.GetProperty("CampaignId").GetString());
        Assert.Equal(4, item.GetProperty("DeliveredCount").GetInt32());
        Assert.Equal(1, item.GetProperty("ActiveUnansweredCount").GetInt32());
        Assert.Equal(1, item.GetProperty("AcceptedCount").GetInt32());
        Assert.Equal(1, item.GetProperty("RejectedCount").GetInt32());
        Assert.Equal(1, item.GetProperty("ExpiredCount").GetInt32());
        Assert.Equal(1, item.GetProperty("FeedbackCount").GetInt32());
        Assert.Equal(100m, item.GetProperty("ReservedAmount").GetDecimal());
        Assert.Equal(200m, item.GetProperty("ChargedSpend").GetDecimal());
        Assert.Equal(160m, item.GetProperty("DoctorEarnings").GetDecimal());
        Assert.Equal(40m, item.GetProperty("PlatformFee").GetDecimal());
    }

    [Fact]
    public async Task GetCompanyCampaigns_RejectsDateRangesLongerThanNinetyDays()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedReportingCampaignAsync(factory, consistentEvidence: true);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        using var response = await client.GetAsync("/api/company/campaigns?fromDateEgypt=2026-04-14&toDateEgypt=2026-07-13");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);
    }

    [Fact]
    public async Task GetCompanyCampaigns_WithOmittedDates_UsesDefaultScopeAndReturnsEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedReportingCampaignAsync(factory, consistentEvidence: true);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        using var response = await client.GetAsync("/api/company/campaigns");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.True(data.GetProperty("Items").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task GetCompanyCampaigns_WithReportingDiscrepancy_Returns409Envelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedReportingCampaignAsync(factory, consistentEvidence: false);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        using var response = await client.GetAsync("/api/company/campaigns?fromDateEgypt=2026-07-01&toDateEgypt=2026-07-13");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 409);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private static async Task<ReportingSeed> SeedReportingCampaignAsync(ContractWebAppFactory factory, bool consistentEvidence)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var companyUser = CreateUser($"phase10-company-{suffix}@example.test", UserRole.Company, AccountStatus.Approved);
        var doctorUser = CreateUser($"phase10-doctor-{suffix}@example.test", UserRole.Doctor, AccountStatus.Approved);
        var company = new CompanyProfile
        {
            Id = $"company-{suffix}",
            UserId = companyUser.Id,
            CompanyName = "Phase 10 Pharma",
            LicenseNumber = $"license-{suffix}",
            ContactName = "Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}"
        };
        var doctor = new DoctorProfile
        {
            Id = $"doctor-{suffix}",
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 7,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}/doctor",
            DailyMessageLimit = 10,
            PricePerMessage = 100m
        };
        var campaign = new Campaign
        {
            Id = $"campaign-{suffix}",
            CompanyId = company.Id,
            Title = "Reporting campaign",
            Description = "Reporting campaign",
            Status = CampaignStatus.Active,
            SubmittedAtUtc = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 7, 1, 7, 0, 0, DateTimeKind.Utc)
        };
        var companyWallet = Wallet($"company-wallet-{suffix}", WalletOwnerType.Company, company.Id, companyUser.Id);
        var doctorWallet = Wallet($"doctor-wallet-{suffix}", WalletOwnerType.Doctor, doctor.Id, doctorUser.Id);
        await db.Users.AddRangeAsync(companyUser, doctorUser);
        await db.CompanyProfiles.AddAsync(company);
        await db.DoctorProfiles.AddAsync(doctor);
        await db.Campaigns.AddAsync(campaign);
        await db.CampaignTargets.AddAsync(new CampaignTarget { Id = $"target-{suffix}", CampaignId = campaign.Id, DoctorId = doctor.Id, SpecializationSnapshot = doctor.Specialization, ExperienceYearsSnapshot = doctor.ExperienceYears, LocationSnapshot = doctor.Location, ActivityScoreSnapshot = 90m, PricePerMessageSnapshot = 100m });
        await db.Wallets.AddRangeAsync(companyWallet, doctorWallet);

        var statuses = new[] { DeliveryStatus.Active, DeliveryStatus.Accepted, DeliveryStatus.Rejected, DeliveryStatus.Expired };
        for (var index = 0; index < statuses.Length; index++)
        {
            var deliveryId = $"delivery-{index}-{suffix}";
            var delivery = Delivery(deliveryId, campaign.Id, company.Id, doctor.Id, statuses[index], new DateOnly(2026, 7, 10 + index));
            await db.DoctorAdDeliveries.AddAsync(delivery);
            if (statuses[index] == DeliveryStatus.Active)
            {
                await db.WalletTransactions.AddAsync(Transaction(companyWallet.Id, WalletTransactionType.Reserve, deliveryId, 100m));
            }
            else if (statuses[index] is DeliveryStatus.Accepted or DeliveryStatus.Rejected && consistentEvidence)
            {
                await db.WalletTransactions.AddRangeAsync(
                    Transaction(companyWallet.Id, WalletTransactionType.Charge, deliveryId, 100m),
                    Transaction(doctorWallet.Id, WalletTransactionType.Earn, deliveryId, 80m));
            }
        }

        await db.SaveChangesAsync();
        return new ReportingSeed(companyUser.Id, company.Id, campaign.Id);
    }

    private static DoctorAdDelivery Delivery(string id, string campaignId, string companyId, string doctorId, DeliveryStatus status, DateOnly date)
        => new()
        {
            Id = id,
            CampaignId = campaignId,
            CompanyId = companyId,
            DoctorId = doctorId,
            DeliveryDateEgypt = date,
            DeliveredAtUtc = date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), DateTimeKind.Utc),
            Status = status,
            ReservationStatus = status == DeliveryStatus.Active ? ReservationStatus.Reserved : status == DeliveryStatus.Expired ? ReservationStatus.Released : ReservationStatus.Charged,
            InteractedAtUtc = status is DeliveryStatus.Accepted or DeliveryStatus.Rejected ? date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9)), DateTimeKind.Utc) : null,
            FeedbackText = status == DeliveryStatus.Accepted ? "Useful feedback" : null,
            FeedbackCreatedAtUtc = status == DeliveryStatus.Accepted ? date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9)), DateTimeKind.Utc) : null,
            FeedbackQualityStatus = status == DeliveryStatus.Accepted ? FeedbackQualityStatus.Accepted : null,
            PricePerMessageSnapshot = 100m,
            PlatformFeePercentSnapshot = 20m,
            PlatformFeeAmount = 20m,
            DoctorEarnings = 80m,
            ReservedAmount = 100m,
            CreatedAtUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
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

    private sealed record ReportingSeed(string CompanyUserId, string CompanyId, string CampaignId);
}
