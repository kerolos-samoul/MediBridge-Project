using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9ScopeGuardTests : IClassFixture<TestHost.WebAppFactory>
{
    private readonly TestHost.WebAppFactory factory;

    public Phase9ScopeGuardTests(TestHost.WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Phase9JobsAndAdminActions_DoNotMutateOutOfScopeFinancialQueueDeliveryCampaignPaymentOrWithdrawalRows()
    {
        await factory.InitializeDatabaseAsync();
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var doctor = await Phase9TestHelpers.SeedApprovedDoctorAsync(factory.Services, minimumWeeklyRequirement: 2);
        var deliveryId = await Phase9TestHelpers.SeedDeliveryAsync(
            factory.Services,
            doctor.DoctorId,
            new DateOnly(2026, 6, 30),
            new DateTime(2026, 6, 30, 8, 0, 0, DateTimeKind.Utc),
            DeliveryStatus.Accepted,
            new DateTime(2026, 6, 30, 9, 0, 0, DateTimeKind.Utc),
            "Useful clinical feedback for scope guard.");
        var before = await CaptureScopeAsync();

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IActivityScoreService>()
                .RunDailyScoreAsync(new DateOnly(2026, 7, 11), admin.Id, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IWeeklyEnforcementService>()
                .RunWeeklyEnforcementAsync(new DateOnly(2026, 6, 29), admin.Id, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IAdminActivityEnforcementService>()
                .ApplyDoctorEnforcementActionAsync(
                    doctor.DoctorId,
                    new DoctorEnforcementActionRequestDto(DoctorEnforcementActionType.ReduceDailyLimit, "Scope guard reduce limit reason", 4, null),
                    admin.Id,
                    correlationId: "phase9-scope-guard",
                    CancellationToken.None);
        }

        var after = await CaptureScopeAsync();
        Assert.Equal(before with { Phase9DeliveryId = deliveryId }, after with { Phase9DeliveryId = deliveryId });
    }

    private async Task<Phase9ScopeSnapshot> CaptureScopeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var deliveryFacts = await context.DoctorAdDeliveries
            .AsNoTracking()
            .OrderBy(delivery => delivery.Id)
            .Select(delivery => new DeliveryFacts(
                delivery.Id,
                delivery.Status,
                delivery.ReservationStatus,
                delivery.ReadAtUtc,
                delivery.InteractedAtUtc,
                delivery.ExpiredAtUtc,
                delivery.DoctorEarnings,
                delivery.PlatformFeeAmount))
            .ToArrayAsync();

        return new Phase9ScopeSnapshot(
            await context.DoctorMessageQueues.CountAsync(),
            await context.Wallets.CountAsync(),
            await context.WalletTransactions.CountAsync(),
            await context.WalletLedgerEntries.CountAsync(),
            await context.CampaignReviewHistories.CountAsync(),
            await context.MockPaymentTransactions.CountAsync(),
            await context.WithdrawalRequests.CountAsync(),
            string.Join("|", deliveryFacts.Select(fact => $"{fact.Id}:{(int)fact.Status}:{(int)fact.ReservationStatus}:{fact.ReadAtUtc:o}:{fact.InteractedAtUtc:o}:{fact.ExpiredAtUtc:o}:{fact.DoctorEarnings}:{fact.PlatformFeeAmount}")),
            Phase9DeliveryId: null);
    }

    private sealed record DeliveryFacts(
        string Id,
        DeliveryStatus Status,
        ReservationStatus ReservationStatus,
        DateTime? ReadAtUtc,
        DateTime? InteractedAtUtc,
        DateTime? ExpiredAtUtc,
        decimal DoctorEarnings,
        decimal PlatformFeeAmount);

    private sealed record Phase9ScopeSnapshot(
        int QueueCount,
        int WalletCount,
        int WalletTransactionCount,
        int WalletLedgerCount,
        int CampaignReviewHistoryCount,
        int MockPaymentTransactionCount,
        int WithdrawalRequestCount,
        string DeliveryFacts,
        string? Phase9DeliveryId);
}
