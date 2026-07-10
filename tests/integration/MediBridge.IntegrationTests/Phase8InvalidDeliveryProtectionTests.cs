using System.Net;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InvalidDeliveryProtectionTests
{
    [Fact]
    public async Task ReadAndInteract_RejectStaleExpiredCrossOwnerMalformedAndMissingDeliveries_Safely()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await SeedInvalidProtectionBaseAsync(factory, "invalid-delivery");
        var otherDoctor = await Phase8InteractionTestHelpers.SeedApprovedActiveDoctorAsync(
            factory.Services,
            $"other-user-{seed.Suffix}",
            $"other-doctor-{seed.Suffix}",
            $"other-{seed.Suffix}@example.test",
            50m,
            10,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-10));

        await SeedDeliveryVariantAsync(factory, seed, "prior", Phase8InteractionTestHelpers.DefaultTodayEgypt.AddDays(-1));
        await SeedDeliveryVariantAsync(factory, seed, "future", Phase8InteractionTestHelpers.DefaultTodayEgypt.AddDays(1));
        await SeedDeliveryVariantAsync(
            factory,
            seed,
            "expired",
            Phase8InteractionTestHelpers.DefaultTodayEgypt,
            DeliveryStatus.Expired,
            ReservationStatus.Released,
            expiredAtUtc: Phase8InteractionTestHelpers.DefaultUtcNow.AddMinutes(-1));
        var crossOwnerDeliveryId = await SeedDeliveryVariantAsync(factory, seed, "cross-owner", Phase8InteractionTestHelpers.DefaultTodayEgypt);
        await SeedWalletsForConsistencyCaseAsync(factory, seed, "valid");

        using var ownerClient = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var otherDoctorClient = Phase8InteractionTestHelpers.CreateDoctorClient(factory, otherDoctor.UserId);

        foreach (var (deliveryId, expectedStatusCode) in new[]
        {
            ($"delivery-prior-{seed.Suffix}", HttpStatusCode.NotFound),
            ($"delivery-future-{seed.Suffix}", HttpStatusCode.NotFound),
            ($"delivery-expired-{seed.Suffix}", HttpStatusCode.NotFound),
            (" ", HttpStatusCode.BadRequest),
            ($"missing-{seed.Suffix}", HttpStatusCode.NotFound)
        })
        {
            using var readResponse = await ownerClient.PutAsync($"/api/doctor/messages/{Uri.EscapeDataString(deliveryId)}/read", content: null);
            await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(readResponse, expectedStatusCode);

            using var interactRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
                deliveryId,
                $"invalid-{Guid.NewGuid():N}",
                "Accept");
            using var interactResponse = await ownerClient.SendAsync(interactRequest);
            await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(interactResponse, expectedStatusCode);
        }

        using var crossOwnerRead = await otherDoctorClient.PutAsync($"/api/doctor/messages/{crossOwnerDeliveryId}/read", content: null);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(crossOwnerRead, HttpStatusCode.NotFound);
        using var crossOwnerInteract = Phase8InteractionTestHelpers.CreateInteractRequest(crossOwnerDeliveryId, "cross-owner-0001", "Accept");
        using var crossOwnerInteractResponse = await otherDoctorClient.SendAsync(crossOwnerInteract);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(crossOwnerInteractResponse, HttpStatusCode.NotFound);

        var snapshot = await SnapshotTotalsAsync(factory, seed.DoctorId);
        Assert.Equal(4, snapshot.ActiveOrTerminalDeliveryCount);
        Assert.Equal(0, snapshot.ReadCount);
        Assert.Equal(0, snapshot.InteractedCount);
        Assert.Equal(50m, snapshot.CompanyReservedBalance);
        Assert.Equal(25m, snapshot.DoctorAvailableBalance);
        Assert.Equal(0, snapshot.WalletTransactionCount);
        Assert.Equal(0, snapshot.WalletLedgerEntryCount);
    }

    [Theory]
    [InlineData(DeliveryStatus.Accepted, "Accept", "Reject")]
    [InlineData(DeliveryStatus.Rejected, "Reject", "Accept")]
    public async Task ReadAndInteract_CurrentDaySettledDeliveries_CanStillBeReadButNeverMutateAgain(
        DeliveryStatus settledStatus,
        string sameDecision,
        string conflictingDecision)
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(
            factory,
            $"settled-{settledStatus}",
            status: settledStatus,
            reservationStatus: ReservationStatus.Charged,
            interactedAtUtc: Phase8InteractionTestHelpers.DefaultUtcNow.AddMinutes(-5),
            feedbackText: "stored feedback",
            feedbackCreatedAtUtc: Phase8InteractionTestHelpers.DefaultUtcNow.AddMinutes(-5),
            companyReservedBalance: 0m,
            doctorAvailableBalance: 65m);
        var before = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var readResponse = await client.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        using var sameDecisionRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            $"settled-same-{Guid.NewGuid():N}",
            sameDecision);
        using var sameDecisionResponse = await client.SendAsync(sameDecisionRequest);
        var replayData = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(sameDecisionResponse);
        Assert.Equal("Replayed", replayData.GetProperty("IdempotencyStatus").GetString());
        Assert.Equal(settledStatus.ToString(), replayData.GetProperty("Status").GetString());

        using var conflictRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            $"settled-conflict-{Guid.NewGuid():N}",
            conflictingDecision);
        using var conflictResponse = await client.SendAsync(conflictRequest);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(conflictResponse, HttpStatusCode.Conflict);

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.NotNull(after.ReadAtUtc);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.ReservationStatus, after.ReservationStatus);
        Assert.Equal(before.InteractedAtUtc, after.InteractedAtUtc);
        Assert.Equal(before.FeedbackText, after.FeedbackText);
        Assert.Equal(before.CompanyReservedBalance, after.CompanyReservedBalance);
        Assert.Equal(before.DoctorAvailableBalance, after.DoctorAvailableBalance);
        Assert.Equal(before.WalletTransactionCount, after.WalletTransactionCount);
        Assert.Equal(before.WalletLedgerEntryCount, after.WalletLedgerEntryCount);
    }

    [Theory]
    [InlineData("missing-company-wallet")]
    [InlineData("missing-doctor-wallet")]
    [InlineData("deleted-company-wallet")]
    [InlineData("insufficient-company-reserved")]
    [InlineData("mismatched-owner-wallets")]
    public async Task Interact_WalletConsistencyFailures_ReturnSafeFailureWithoutDeliveryOrWalletMutation(string caseName)
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await SeedInvalidProtectionBaseAsync(factory, caseName);
        await SeedDeliveryVariantAsync(factory, seed, caseName, Phase8InteractionTestHelpers.DefaultTodayEgypt);
        await SeedWalletsForConsistencyCaseAsync(factory, seed, caseName);
        var before = await SnapshotCaseAsync(factory, $"delivery-{caseName}-{seed.Suffix}");

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var request = Phase8InteractionTestHelpers.CreateInteractRequest(
            $"delivery-{caseName}-{seed.Suffix}",
            $"wallet-case-{Guid.NewGuid():N}",
            "Accept");
        using var response = await client.SendAsync(request);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(response, HttpStatusCode.InternalServerError);

        var after = await SnapshotCaseAsync(factory, $"delivery-{caseName}-{seed.Suffix}");
        Assert.Equal(before.DeliveryStatus, after.DeliveryStatus);
        Assert.Equal(before.ReservationStatus, after.ReservationStatus);
        Assert.Equal(before.InteractedAtUtc, after.InteractedAtUtc);
        Assert.Equal(before.Wallets, after.Wallets);
        Assert.Equal(0, after.WalletTransactionCount);
        Assert.Equal(0, after.WalletLedgerEntryCount);
        Assert.Equal(1, after.ConsistencyAuditCount);
    }

    [Theory]
    [InlineData("zero-price", 0, 20, 10, 40, 50)]
    [InlineData("negative-earnings", 50, 20, 60, -10, 50)]
    [InlineData("reserved-mismatch", 50, 20, 10, 40, 45)]
    [InlineData("fee-earnings-mismatch", 50, 20, 9, 40, 50)]
    public async Task Interact_InvalidStoredMonetarySnapshots_FailBeforeChargeOrEarn(
        string caseName,
        decimal price,
        decimal feePercent,
        decimal feeAmount,
        decimal doctorEarnings,
        decimal reservedAmount)
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await SeedInvalidProtectionBaseAsync(factory, caseName);
        var deliveryId = await SeedDeliveryVariantAsync(
            factory,
            seed,
            caseName,
            Phase8InteractionTestHelpers.DefaultTodayEgypt);
        await ForceStoredSnapshotAsync(factory, deliveryId, price, feePercent, feeAmount, doctorEarnings, reservedAmount);
        await SeedWalletsForConsistencyCaseAsync(factory, seed, "valid");
        var before = await SnapshotCaseAsync(factory, deliveryId);

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var request = Phase8InteractionTestHelpers.CreateInteractRequest(
            deliveryId,
            $"snapshot-case-{Guid.NewGuid():N}",
            "Reject");
        using var response = await client.SendAsync(request);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(response, HttpStatusCode.InternalServerError);

        var after = await SnapshotCaseAsync(factory, deliveryId);
        Assert.Equal(before.DeliveryStatus, after.DeliveryStatus);
        Assert.Equal(before.ReservationStatus, after.ReservationStatus);
        Assert.Equal(before.Wallets, after.Wallets);
        Assert.Equal(0, after.WalletTransactionCount);
        Assert.Equal(0, after.WalletLedgerEntryCount);
        Assert.Equal(1, after.ConsistencyAuditCount);
    }

    private static async Task<InvalidProtectionSeed> SeedInvalidProtectionBaseAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var doctor = await Phase8InteractionTestHelpers.SeedApprovedActiveDoctorAsync(
            factory.Services,
            $"doctor-user-{suffix}",
            $"doctor-{suffix}",
            $"doctor-{suffix}@example.test",
            50m,
            10,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-10));
        var company = await Phase8InteractionTestHelpers.SeedApprovedCompanyAsync(
            factory.Services,
            $"company-user-{suffix}",
            $"company-{suffix}",
            $"company-{suffix}@example.test",
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-10));

        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            factory.Services,
            $"campaign-{suffix}",
            company.CompanyId,
            CampaignStatus.Approved,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-2),
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-3),
            false,
            null);

        return new InvalidProtectionSeed(suffix, doctor.UserId, doctor.DoctorId, company.UserId, company.CompanyId, $"campaign-{suffix}");
    }

    private static async Task<string> SeedDeliveryVariantAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        InvalidProtectionSeed seed,
        string label,
        DateOnly deliveryDateEgypt,
        DeliveryStatus status = DeliveryStatus.Active,
        ReservationStatus reservationStatus = ReservationStatus.Reserved,
        decimal pricePerMessageSnapshot = 50m,
        decimal platformFeePercentSnapshot = 20m,
        decimal platformFeeAmount = 10m,
        decimal doctorEarnings = 40m,
        decimal reservedAmount = 50m,
        DateTime? expiredAtUtc = null)
    {
        var deliveryId = $"delivery-{label}-{seed.Suffix}";
        var campaignId = $"campaign-{label}-{seed.Suffix}";
        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            factory.Services,
            campaignId,
            seed.CompanyId,
            CampaignStatus.Approved,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-2),
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-3),
            false,
            null);
        await Phase8InteractionTestHelpers.SeedDoctorAdDeliveryAsync(
            factory.Services,
            deliveryId,
            seed.DoctorId,
            campaignId,
            seed.CompanyId,
            deliveryDateEgypt,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddMinutes(-30),
            status,
            reservationStatus,
            pricePerMessageSnapshot,
            platformFeePercentSnapshot,
            platformFeeAmount,
            doctorEarnings,
            reservedAmount,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddMinutes(-30),
            interactedAtUtc: status is DeliveryStatus.Accepted or DeliveryStatus.Rejected
                ? Phase8InteractionTestHelpers.DefaultUtcNow.AddMinutes(-5)
                : null,
            expiredAtUtc: expiredAtUtc);
        return deliveryId;
    }

    private static async Task ForceStoredSnapshotAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        string deliveryId,
        decimal price,
        decimal feePercent,
        decimal feeAmount,
        decimal doctorEarnings,
        decimal reservedAmount)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            ALTER TABLE [DoctorAdDeliveries] NOCHECK CONSTRAINT [CK_DoctorAdDeliveries_Money_NonNegative];
            UPDATE [DoctorAdDeliveries]
            SET [PricePerMessageSnapshot] = {price},
                [PlatformFeePercentSnapshot] = {feePercent},
                [PlatformFeeAmount] = {feeAmount},
                [DoctorEarnings] = {doctorEarnings},
                [ReservedAmount] = {reservedAmount}
            WHERE [Id] = {deliveryId};
            """);
    }

    private static async Task SeedWalletsForConsistencyCaseAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        InvalidProtectionSeed seed,
        string caseName)
    {
        var now = Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-2);
        if (caseName is not "missing-company-wallet")
        {
            await Phase8InteractionTestHelpers.SeedCompanyWalletAsync(
                factory.Services,
                $"company-wallet-{seed.Suffix}",
                caseName == "mismatched-owner-wallets" ? $"wrong-company-{seed.Suffix}" : seed.CompanyId,
                seed.CompanyUserId,
                200m,
                caseName == "insufficient-company-reserved" ? 10m : 50m,
                now,
                isDeleted: caseName == "deleted-company-wallet",
                deletedAtUtc: caseName == "deleted-company-wallet" ? now.AddMinutes(1) : null);
        }

        if (caseName is not "missing-doctor-wallet")
        {
            await Phase8InteractionTestHelpers.SeedDoctorWalletAsync(
                factory.Services,
                $"doctor-wallet-{seed.Suffix}",
                caseName == "mismatched-owner-wallets" ? $"wrong-doctor-{seed.Suffix}" : seed.DoctorId,
                seed.DoctorUserId,
                25m,
                0m,
                now);
        }
    }

    private static async Task<InvalidTotalsSnapshot> SnapshotTotalsAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        string doctorId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return new InvalidTotalsSnapshot(
            await context.DoctorAdDeliveries.CountAsync(item => item.DoctorId == doctorId),
            await context.DoctorAdDeliveries.CountAsync(item => item.DoctorId == doctorId && item.ReadAtUtc != null),
            await context.DoctorAdDeliveries.CountAsync(item => item.DoctorId == doctorId && item.InteractedAtUtc != null),
            await context.Wallets.Where(item => item.OwnerType == WalletOwnerType.Company && item.OwnerId.StartsWith("company-")).SumAsync(item => item.ReservedBalance),
            await context.Wallets.Where(item => item.OwnerType == WalletOwnerType.Doctor && item.OwnerId == doctorId).SumAsync(item => item.AvailableBalance),
            await context.WalletTransactions.CountAsync(),
            await context.WalletLedgerEntries.CountAsync());
    }

    private static async Task<InvalidCaseSnapshot> SnapshotCaseAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        string deliveryId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await context.DoctorAdDeliveries.AsNoTracking().SingleAsync(item => item.Id == deliveryId);
        var wallets = await context.Wallets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => $"{item.Id}:{item.OwnerType}:{item.OwnerId}:{item.AvailableBalance}:{item.ReservedBalance}:{item.IsDeleted}")
            .ToArrayAsync();

        return new InvalidCaseSnapshot(
            delivery.Status,
            delivery.ReservationStatus,
            delivery.InteractedAtUtc,
            wallets,
            await context.WalletTransactions.CountAsync(item => item.RelatedDeliveryId == deliveryId),
            await context.WalletLedgerEntries.CountAsync(item => item.MessageDeliveryId == deliveryId),
            await context.AuditEvents.CountAsync(item => item.TargetId == deliveryId && item.EventType == "Phase8DeliveryInteractionConsistencyFailure"));
    }

    private sealed record InvalidProtectionSeed(
        string Suffix,
        string DoctorUserId,
        string DoctorId,
        string CompanyUserId,
        string CompanyId,
        string CampaignId);

    private sealed record InvalidTotalsSnapshot(
        int ActiveOrTerminalDeliveryCount,
        int ReadCount,
        int InteractedCount,
        decimal CompanyReservedBalance,
        decimal DoctorAvailableBalance,
        int WalletTransactionCount,
        int WalletLedgerEntryCount);

    private sealed record InvalidCaseSnapshot(
        DeliveryStatus DeliveryStatus,
        ReservationStatus ReservationStatus,
        DateTime? InteractedAtUtc,
        IReadOnlyList<string> Wallets,
        int WalletTransactionCount,
        int WalletLedgerEntryCount,
        int ConsistencyAuditCount);
}
