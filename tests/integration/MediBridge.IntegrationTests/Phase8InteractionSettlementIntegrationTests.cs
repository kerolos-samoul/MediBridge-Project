using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionSettlementIntegrationTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task InteractAccept_SettlesReservedFundsExactlyOnce_AndReplaysSameKey()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "settle");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "settlement-key-0001");
        using var body = new StringContent("""{"Outcome":"Accept","Feedback":"Useful clinical reminder for patients."}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync($"/api/doctor/messages/{fixture.DeliveryId}/interact", body);
        using var replay = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Accept","Feedback":"Useful clinical reminder for patients."}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var replayDocument = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal("Accepted", data.GetProperty("Status").GetString());
        Assert.Equal("Charged", data.GetProperty("ReservationStatus").GetString());
        Assert.Equal(100m, data.GetProperty("ChargeAmount").GetDecimal());
        Assert.Equal(87.65m, data.GetProperty("DoctorEarnings").GetDecimal());
        Assert.Equal(12.35m, data.GetProperty("PlatformFeeAmount").GetDecimal());
        Assert.False(data.GetProperty("Replayed").GetBoolean());
        Assert.True(replayDocument.RootElement.GetProperty("Data").GetProperty("Replayed").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Equal(DeliveryStatus.Accepted, delivery.Status);
        Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
        Assert.Equal("Useful clinical reminder for patients.", delivery.FeedbackText);
        Assert.Equal(FeedbackQualityStatus.Accepted, delivery.FeedbackQualityStatus);

        var companyWallet = await db.Wallets.AsNoTracking().SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == fixture.CompanyId);
        var doctorWallet = await db.Wallets.AsNoTracking().SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Doctor && wallet.OwnerId == fixture.DoctorId);
        Assert.Equal(0m, companyWallet.AvailableBalance);
        Assert.Equal(0m, companyWallet.ReservedBalance);
        Assert.Equal(92.65m, doctorWallet.AvailableBalance);

        var transactions = await db.WalletTransactions.AsNoTracking().Where(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId).ToListAsync();
        Assert.Single(transactions, transaction => transaction.OperationType == WalletTransactionType.Charge && transaction.IdempotencyKey == DeliveryFinancialOperationKeys.ForCharge(fixture.DeliveryId));
        Assert.Single(transactions, transaction => transaction.OperationType == WalletTransactionType.Earn && transaction.IdempotencyKey == DeliveryFinancialOperationKeys.ForEarn(fixture.DeliveryId));
        Assert.Equal(2, transactions.Count);
        Assert.Equal(2, await db.WalletLedgerEntries.AsNoTracking().CountAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId));
        Assert.Equal(1, await db.DeliveryInteractions.AsNoTracking().CountAsync(interaction => interaction.DeliveryId == fixture.DeliveryId));
        Assert.Equal(1, await db.AuditEvents.AsNoTracking().CountAsync(audit => audit.TargetId == fixture.DeliveryId && audit.EventType == "Phase8InteractionSettlementSucceeded"));
    }

    [Fact]
    public async Task InteractReject_SettlesUsingStoredSnapshots_AndLeavesCompanyAvailableBalanceUnchanged()
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, "reject-settle");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "settlement-reject-key-0001");

        using var response = await client.PostAsync(
            $"/api/doctor/messages/{fixture.DeliveryId}/interact",
            new StringContent("""{"Outcome":"Reject","Feedback":"Plain useful feedback."}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal("Rejected", data.GetProperty("Status").GetString());
        Assert.Equal("Charged", data.GetProperty("ReservationStatus").GetString());
        Assert.Equal(100m, data.GetProperty("ChargeAmount").GetDecimal());
        Assert.Equal(87.65m, data.GetProperty("DoctorEarnings").GetDecimal());
        Assert.Equal(12.35m, data.GetProperty("PlatformFeeAmount").GetDecimal());
        Assert.True(data.GetProperty("FeedbackAccepted").GetBoolean());
        Assert.True(data.GetProperty("FeedbackQualifiesForScore").GetBoolean());
        Assert.False(data.GetProperty("Replayed").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Equal(DeliveryStatus.Rejected, delivery.Status);
        Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
        Assert.Equal(100m, delivery.PricePerMessageSnapshot);
        Assert.Equal(12.345m, delivery.PlatformFeePercentSnapshot);
        Assert.Equal(12.35m, delivery.PlatformFeeAmount);
        Assert.Equal(87.65m, delivery.DoctorEarnings);
        Assert.Equal("Plain useful feedback.", delivery.FeedbackText);
        Assert.Equal(FeedbackQualityStatus.Accepted, delivery.FeedbackQualityStatus);

        var companyWallet = await db.Wallets.AsNoTracking().SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == fixture.CompanyId);
        var doctorWallet = await db.Wallets.AsNoTracking().SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Doctor && wallet.OwnerId == fixture.DoctorId);
        Assert.Equal(0m, companyWallet.AvailableBalance);
        Assert.Equal(0m, companyWallet.ReservedBalance);
        Assert.Equal(92.65m, doctorWallet.AvailableBalance);

        var charge = await db.WalletTransactions.AsNoTracking().SingleAsync(transaction =>
            transaction.RelatedDeliveryId == fixture.DeliveryId && transaction.OperationType == WalletTransactionType.Charge);
        var earn = await db.WalletTransactions.AsNoTracking().SingleAsync(transaction =>
            transaction.RelatedDeliveryId == fixture.DeliveryId && transaction.OperationType == WalletTransactionType.Earn);
        Assert.Equal(DeliveryFinancialOperationKeys.ForCharge(fixture.DeliveryId), charge.IdempotencyKey);
        Assert.Equal(DeliveryFinancialOperationKeys.ForEarn(fixture.DeliveryId), earn.IdempotencyKey);
        Assert.Equal(2, await db.WalletLedgerEntries.AsNoTracking().CountAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId));
        Assert.Equal(1, await db.DeliveryInteractions.AsNoTracking().CountAsync(interaction => interaction.DeliveryId == fixture.DeliveryId));
        Assert.Equal(1, await db.AuditEvents.AsNoTracking().CountAsync(audit => audit.TargetId == fixture.DeliveryId && audit.EventType == "Phase8InteractionSettlementSucceeded"));
    }
}
