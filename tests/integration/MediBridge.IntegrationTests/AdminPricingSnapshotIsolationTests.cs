using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminPricingSnapshotIsolationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminPricingSnapshotIsolationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task PricingAndPlatformFeeChanges_DoNotRewriteExistingDeliveryOrFinancialEvidence()
    {
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedApprovedAdminAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 100m);
        var walletId = await Phase5CampaignQueueTestHelpers.SeedCompanyWalletAsync(factory.Services, company.CompanyId, company.UserId, 1000m);
        var seed = await SeedHistoricalEvidenceAsync(adminUserId, company.CompanyId, doctor.DoctorId, walletId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", adminUserId));

        using var deactivate = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/price/deactivate",
            new { Reason = "Pause future paid delivery activation" });
        using var setPrice = await client.PutAsJsonAsync(
            $"/api/admin/doctors/{doctor.DoctorId}/price",
            new { PricePerMessage = 150m, Reason = "Future price only" });
        using var setFee = await client.PutAsJsonAsync(
            "/api/admin/platform-fee-policy",
            new { FeePercent = 30m, Reason = "Future fee only" });

        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, setPrice.StatusCode);
        Assert.Equal(HttpStatusCode.OK, setFee.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await context.DoctorAdDeliveries.AsNoTracking().SingleAsync(item => item.Id == seed.DeliveryId);
        var transaction = await context.WalletTransactions.AsNoTracking().SingleAsync(item => item.Id == seed.TransactionId);
        var ledger = await context.WalletLedgerEntries.AsNoTracking().SingleAsync(item => item.Id == seed.LedgerEntryId);

        Assert.Equal(100m, delivery.PricePerMessageSnapshot);
        Assert.Equal(20m, delivery.PlatformFeePercentSnapshot);
        Assert.Equal(20m, delivery.PlatformFeeAmount);
        Assert.Equal(80m, delivery.DoctorEarnings);
        Assert.Equal(100m, delivery.ReservedAmount);
        Assert.Equal(100m, transaction.Amount);
        Assert.Equal(100m, ledger.Amount);
        Assert.Equal(WalletTransactionType.Reserve, transaction.OperationType);
        Assert.Equal(WalletLedgerEntryDirection.Debit, ledger.Direction);
    }

    private async Task<SnapshotSeed> SeedHistoricalEvidenceAsync(
        string adminUserId,
        string companyId,
        string doctorId,
        string walletId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow.AddDays(-1);
        var campaign = new Campaign
        {
            Id = $"campaign-{Guid.NewGuid():N}",
            CompanyId = companyId,
            Title = "Historical campaign",
            Description = "Already delivered campaign.",
            Status = CampaignStatus.Active,
            SubmittedAtUtc = now,
            CreatedAtUtc = now
        };
        var delivery = new DoctorAdDelivery
        {
            Id = $"delivery-{Guid.NewGuid():N}",
            DoctorId = doctorId,
            CampaignId = campaign.Id,
            CompanyId = companyId,
            DeliveryDateEgypt = DateOnly.FromDateTime(now),
            DeliveredAtUtc = now,
            Status = DeliveryStatus.Active,
            ReservationStatus = ReservationStatus.Reserved,
            PricePerMessageSnapshot = 100m,
            PlatformFeePercentSnapshot = 20m,
            PlatformFeeAmount = 20m,
            DoctorEarnings = 80m,
            ReservedAmount = 100m,
            CreatedAtUtc = now
        };
        var transaction = new WalletTransaction
        {
            Id = $"tx-{Guid.NewGuid():N}",
            WalletId = walletId,
            OperationType = WalletTransactionType.Reserve,
            IdempotencyKey = $"reserve-{delivery.Id}",
            Amount = 100m,
            RelatedDeliveryId = delivery.Id,
            CreatedAtUtc = now
        };
        var ledger = new WalletLedgerEntry
        {
            Id = $"ledger-{Guid.NewGuid():N}",
            WalletTransactionId = transaction.Id,
            WalletId = walletId,
            Direction = WalletLedgerEntryDirection.Debit,
            Amount = 100m,
            BalanceType = WalletBalanceType.Available,
            CampaignId = campaign.Id,
            MessageDeliveryId = delivery.Id,
            CompanyId = companyId,
            CreatedAtUtc = now
        };
        var policy = new PlatformFeePolicyHistory
        {
            Id = $"policy-{Guid.NewGuid():N}",
            FeePercent = 20m,
            EffectiveFromUtc = now.AddDays(-1),
            ChangedByAdminUserId = adminUserId,
            Reason = "Historical fee"
        };

        await context.Campaigns.AddAsync(campaign);
        await context.DoctorAdDeliveries.AddAsync(delivery);
        await context.WalletTransactions.AddAsync(transaction);
        await context.WalletLedgerEntries.AddAsync(ledger);
        await context.PlatformFeePolicyHistories.AddAsync(policy);
        await context.SaveChangesAsync();
        return new SnapshotSeed(delivery.Id, transaction.Id, ledger.Id);
    }

    private async Task<string> SeedApprovedAdminAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"pricing-snapshot-admin-{suffix}@medibridge.local";
        var user = new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private sealed record SnapshotSeed(string DeliveryId, string TransactionId, string LedgerEntryId);
}
