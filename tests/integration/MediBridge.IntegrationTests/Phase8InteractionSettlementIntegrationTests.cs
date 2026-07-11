using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionSettlementIntegrationTests
{
    [Fact]
    public async Task Interact_AcceptSettlesActiveReservedDeliveryOnce_WithChargeEarnLedgerEvidence()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var readAtUtc = Phase8InteractionTestHelpers.DefaultUtcNow.AddMinutes(-10);
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "accept", readAtUtc: readAtUtc);

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var request = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "accept-key-0001",
            "Accept");
        using var response = await client.SendAsync(request);

        var data = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(response);
        Assert.Equal(seed.DeliveryId, data.GetProperty("DeliveryId").GetString());
        Assert.Equal("Accepted", data.GetProperty("Status").GetString());
        Assert.Equal("Created", data.GetProperty("IdempotencyStatus").GetString());

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Equal(DeliveryStatus.Accepted, after.Status);
        Assert.Equal(ReservationStatus.Charged, after.ReservationStatus);
        Assert.Equal(readAtUtc, after.ReadAtUtc);
        Assert.NotNull(after.InteractedAtUtc);
        Assert.Equal(200m, after.CompanyAvailableBalance);
        Assert.Equal(0m, after.CompanyReservedBalance);
        Assert.Equal(65m, after.DoctorAvailableBalance);
        Assert.Equal(0m, after.DoctorReservedBalance);
        Assert.Equal(2, after.WalletTransactionCount);
        Assert.Equal(2, after.WalletLedgerEntryCount);
        Assert.Equal(1, after.InteractionOperationCount);
        Assert.Equal(1, after.SettlementAuditCount);
        Assert.Equal(DeliveryFinancialOperationKeys.ForCharge(seed.DeliveryId), after.ChargeIdempotencyKey);
        Assert.Equal(50m, after.ChargeAmount);
        Assert.Equal(DeliveryFinancialOperationKeys.ForEarn(seed.DeliveryId), after.EarnIdempotencyKey);
        Assert.Equal(40m, after.EarnAmount);
        Assert.True(after.HasCompanyReservedDebitLedger);
        Assert.True(after.HasDoctorAvailableCreditLedger);
    }

    [Fact]
    public async Task Interact_RejectUsesSameFinancialSettlement_AsAccept()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "reject");

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var request = Phase8InteractionTestHelpers.CreateInteractRequest(
            seed.DeliveryId,
            "reject-key-0001",
            "Reject");
        using var response = await client.SendAsync(request);

        var data = await Phase8InteractionTestHelpers.ReadSuccessDataAsync(response);
        Assert.Equal("Rejected", data.GetProperty("Status").GetString());
        Assert.Equal("Created", data.GetProperty("IdempotencyStatus").GetString());

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Equal(DeliveryStatus.Rejected, after.Status);
        Assert.Equal(ReservationStatus.Charged, after.ReservationStatus);
        Assert.Null(after.ReadAtUtc);
        Assert.NotNull(after.InteractedAtUtc);
        Assert.Equal(0m, after.CompanyReservedBalance);
        Assert.Equal(65m, after.DoctorAvailableBalance);
        Assert.Equal(DeliveryFinancialOperationKeys.ForCharge(seed.DeliveryId), after.ChargeIdempotencyKey);
        Assert.Equal(DeliveryFinancialOperationKeys.ForEarn(seed.DeliveryId), after.EarnIdempotencyKey);
        Assert.True(after.HasCompanyReservedDebitLedger);
        Assert.True(after.HasDoctorAvailableCreditLedger);
    }

    [Fact]
    public async Task Interact_ValidationFailuresReturnBadRequestBeforeSettlement()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "validation");
        var before = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
        using var missingKey = await client.PostAsJsonAsync($"/api/doctor/messages/{seed.DeliveryId}/interact", new { Decision = "Accept" });
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(missingKey, HttpStatusCode.BadRequest);

        using var invalidDecision = Phase8InteractionTestHelpers.CreateInteractRequest(seed.DeliveryId, "valid-key-0001", "Maybe");
        using var invalidDecisionResponse = await client.SendAsync(invalidDecision);
        await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(invalidDecisionResponse, HttpStatusCode.BadRequest);

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Equal(before, after);
    }
}
