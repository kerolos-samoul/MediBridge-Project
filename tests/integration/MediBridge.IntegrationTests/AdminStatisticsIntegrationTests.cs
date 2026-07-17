using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminStatisticsIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminStatisticsIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task StatisticsReturnCommittedSourceCountsForBoundedPeriod()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedStatisticsSourcesAsync(factory.Services, DateTime.UtcNow);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.GetAsync($"/api/admin/statistics?fromDateEgypt={seed.BusinessDate:yyyy-MM-dd}&toDateEgypt={seed.BusinessDate:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(1, data.GetProperty("accountCounts").GetProperty("Admin:Approved").GetInt32());
        Assert.Equal(1, data.GetProperty("accountCounts").GetProperty("Doctor:Approved").GetInt32());
        Assert.Equal(1, data.GetProperty("reviewCounts").GetProperty("PendingWithdrawals").GetInt32());
        Assert.Equal(1, data.GetProperty("campaignCounts").GetProperty("PendingReview").GetInt32());
        Assert.Equal(1, data.GetProperty("deliveryOutcomeCounts").GetProperty("Accepted").GetInt32());
        Assert.Equal(1, data.GetProperty("interactionOutcomeCounts").GetProperty("Accept").GetInt32());
        Assert.Equal(1, data.GetProperty("withdrawalStatusCounts").GetProperty("Requested").GetInt32());
        Assert.Equal(1, data.GetProperty("pricingPolicySummary").GetProperty("PricingDeactivations").GetInt32());
        Assert.Equal(1, data.GetProperty("pricingPolicySummary").GetProperty("PlatformFeePolicyChanges").GetInt32());
        Assert.Equal(1, data.GetProperty("enforcementActionCounts").GetProperty("Warn").GetInt32());
        Assert.Equal(100m, data.GetProperty("walletMovementSummary").GetProperty("WithdrawalHold").GetDecimal());
        Assert.Empty(data.GetProperty("withheldFinancialScopes").EnumerateArray());
    }

    [Fact]
    public async Task EmptyValidDateRangeReturnsZeroTotalsAndClearBoundaries()
    {
        await factory.InitializeDatabaseAsync();
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.GetAsync("/api/admin/statistics?fromDateEgypt=2026-01-01&toDateEgypt=2026-01-02");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal("2026-01-01", data.GetProperty("fromDateEgypt").GetString());
        Assert.Equal("2026-01-02", data.GetProperty("toDateEgypt").GetString());
        Assert.Empty(data.GetProperty("accountCounts").EnumerateObject());
        Assert.All(data.GetProperty("reviewCounts").EnumerateObject(), property => Assert.Equal(0, property.Value.GetInt32()));
        Assert.Empty(data.GetProperty("campaignCounts").EnumerateObject());
        Assert.Empty(data.GetProperty("deliveryOutcomeCounts").EnumerateObject());
        Assert.Empty(data.GetProperty("interactionOutcomeCounts").EnumerateObject());
        Assert.Empty(data.GetProperty("withdrawalStatusCounts").EnumerateObject());
    }

    internal static async Task<StatisticsSeed> SeedStatisticsSourcesAsync(IServiceProvider services, DateTime nowUtc)
    {
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(services);
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(services);
        var businessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ResolveEgyptTimeZone()));
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = new Wallet
        {
            Id = $"wallet-{Guid.NewGuid():N}",
            OwnerType = WalletOwnerType.Doctor,
            OwnerId = doctor.DoctorId,
            OwnerUserId = doctor.UserId,
            AvailableBalance = 150m,
            ReservedBalance = 100m,
            Currency = "EGP",
            CreatedAtUtc = nowUtc
        };
        var campaign = new Campaign
        {
            Id = $"campaign-{Guid.NewGuid():N}",
            CompanyId = company.CompanyId,
            Title = "Stats campaign",
            Description = "Stats campaign",
            Status = CampaignStatus.PendingReview,
            CreatedAtUtc = nowUtc,
            SubmittedAtUtc = nowUtc
        };
        var delivery = new DoctorAdDelivery
        {
            Id = $"delivery-{Guid.NewGuid():N}",
            DoctorId = doctor.DoctorId,
            CampaignId = campaign.Id,
            CompanyId = campaign.CompanyId,
            DeliveryDateEgypt = businessDate,
            DeliveredAtUtc = nowUtc,
            Status = DeliveryStatus.Accepted,
            ReservationStatus = ReservationStatus.Charged,
            PricePerMessageSnapshot = 100m,
            PlatformFeePercentSnapshot = 10m,
            PlatformFeeAmount = 10m,
            DoctorEarnings = 90m,
            ReservedAmount = 100m,
            CreatedAtUtc = nowUtc
        };
        var chargeTransactionId = $"charge-{Guid.NewGuid():N}";
        var earnTransactionId = $"earn-{Guid.NewGuid():N}";
        var interaction = new DeliveryInteraction
        {
            Id = $"interaction-{Guid.NewGuid():N}",
            DeliveryId = delivery.Id,
            DoctorId = doctor.DoctorId,
            ActorUserId = doctor.UserId,
            Outcome = DeliveryInteractionOutcome.Accept,
            IdempotencyKeyHash = $"hash-{Guid.NewGuid():N}",
            RequestFingerprint = $"fingerprint-{Guid.NewGuid():N}",
            FeedbackQualifiesForScore = true,
            ChargeTransactionId = chargeTransactionId,
            EarnTransactionId = earnTransactionId,
            CreatedAtUtc = nowUtc
        };
        var withdrawal = new WithdrawalRequest
        {
            Id = $"withdrawal-{Guid.NewGuid():N}",
            DoctorId = doctor.DoctorId,
            Amount = 100m,
            Status = WithdrawalRequestStatus.Requested,
            RequestedAtUtc = nowUtc
        };
        var transaction = new WalletTransaction
        {
            Id = $"tx-{Guid.NewGuid():N}",
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.WithdrawalHold,
            IdempotencyKey = $"withdrawal:{withdrawal.Id}:hold",
            Amount = 100m,
            WithdrawalRequestId = withdrawal.Id,
            CreatedAtUtc = nowUtc
        };
        context.Wallets.Add(wallet);
        context.Campaigns.Add(campaign);
        context.DoctorAdDeliveries.Add(delivery);
        context.DeliveryInteractions.Add(interaction);
        context.WithdrawalRequests.Add(withdrawal);
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = chargeTransactionId,
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.Charge,
            IdempotencyKey = $"delivery:{delivery.Id}:charge",
            Amount = 100m,
            RelatedDeliveryId = delivery.Id,
            CreatedAtUtc = nowUtc
        });
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = earnTransactionId,
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.Earn,
            IdempotencyKey = $"delivery:{delivery.Id}:earn",
            Amount = 90m,
            RelatedDeliveryId = delivery.Id,
            CreatedAtUtc = nowUtc
        });
        context.WalletTransactions.Add(transaction);
        context.WalletLedgerEntries.AddRange(
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = chargeTransactionId, Direction = WalletLedgerEntryDirection.Debit, BalanceType = WalletBalanceType.Reserved, Amount = 100m, MessageDeliveryId = delivery.Id, CreatedAtUtc = nowUtc },
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = earnTransactionId, Direction = WalletLedgerEntryDirection.Credit, BalanceType = WalletBalanceType.Available, Amount = 90m, MessageDeliveryId = delivery.Id, DoctorId = doctor.DoctorId, CreatedAtUtc = nowUtc },
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = transaction.Id, WithdrawalRequestId = withdrawal.Id, Direction = WalletLedgerEntryDirection.Debit, BalanceType = WalletBalanceType.Available, Amount = 100m, CreatedAtUtc = nowUtc },
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = transaction.Id, WithdrawalRequestId = withdrawal.Id, Direction = WalletLedgerEntryDirection.Credit, BalanceType = WalletBalanceType.Reserved, Amount = 100m, CreatedAtUtc = nowUtc });
        context.DoctorPriceHistories.Add(new DoctorPriceHistory { Id = $"price-{Guid.NewGuid():N}", DoctorId = doctor.DoctorId, PreviousPricePerMessage = 50m, NewPricePerMessage = null, PricingIsActive = false, ChangedByAdminUserId = admin.Id, Reason = "Stats", CreatedAtUtc = nowUtc });
        context.PlatformFeePolicyHistories.Add(new PlatformFeePolicyHistory { Id = $"fee-{Guid.NewGuid():N}", FeePercent = 15m, EffectiveFromUtc = nowUtc, ChangedByAdminUserId = admin.Id, Reason = "Stats", CreatedAtUtc = nowUtc });
        context.DoctorEnforcementActions.Add(new DoctorEnforcementAction { Id = $"enforce-{Guid.NewGuid():N}", DoctorId = doctor.DoctorId, ActorAdminUserId = admin.Id, ActionType = DoctorEnforcementActionType.Warn, Reason = "Stats", PreviousStatus = DoctorMarketplaceStatus.Active, NewStatus = DoctorMarketplaceStatus.Warned, PreviousDailyMessageLimit = 10, EffectiveAtUtc = nowUtc, CreatedAtUtc = nowUtc });
        await context.SaveChangesAsync();
        return new StatisticsSeed(admin.Id, doctor.UserId, doctor.DoctorId, businessDate);
    }

    private static TimeZoneInfo ResolveEgyptTimeZone()
    {
        foreach (var id in new[] { "Egypt Standard Time", "Africa/Cairo" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}

internal sealed record StatisticsSeed(string AdminUserId, string DoctorUserId, string DoctorId, DateOnly BusinessDate);
