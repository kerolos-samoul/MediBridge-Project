using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminWithdrawalPayoutIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminWithdrawalPayoutIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task MarkPaidFinalizesHoldAndMarkFailedReleasesHoldExactlyOnce()
    {
        await factory.InitializeDatabaseAsync();
        var paidSeed = await SeedApprovedWithdrawalAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", paidSeed.AdminUserId));

        using var missingReference = await client.PutAsJsonAsync($"/api/admin/withdrawals/{paidSeed.WithdrawalId}/mark-paid", new { PayoutReference = "" });
        using var paid = await client.PutAsJsonAsync($"/api/admin/withdrawals/{paidSeed.WithdrawalId}/mark-paid", new { PayoutReference = "bank-ref-001" });
        using var paidRetry = await client.PutAsJsonAsync($"/api/admin/withdrawals/{paidSeed.WithdrawalId}/mark-paid", new { PayoutReference = "bank-ref-001" });
        using var paidThenFailed = await client.PutAsJsonAsync($"/api/admin/withdrawals/{paidSeed.WithdrawalId}/mark-failed", new { Reason = "Too late" });
        using var paidThenReject = await client.PutAsJsonAsync($"/api/admin/withdrawals/{paidSeed.WithdrawalId}/reject", new { Reason = "Too late" });

        Assert.Equal(HttpStatusCode.BadRequest, missingReference.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, paidRetry.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, paidThenFailed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, paidThenReject.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var wallet = await context.Wallets.SingleAsync(item => item.Id == paidSeed.WalletId);
            Assert.Equal(150m, wallet.AvailableBalance);
            Assert.Equal(0m, wallet.ReservedBalance);
            var withdrawal = await context.WithdrawalRequests.SingleAsync(item => item.Id == paidSeed.WithdrawalId);
            Assert.Equal(WithdrawalRequestStatus.Paid, withdrawal.Status);
            Assert.Equal("bank-ref-001", withdrawal.PayoutReference);
            Assert.Equal(paidSeed.AdminUserId, withdrawal.PayoutStatusChangedByAdminUserId);
            Assert.NotNull(withdrawal.PayoutStatusChangedAtUtc);
            Assert.Equal(1, await context.WalletTransactions.CountAsync(item => item.WithdrawalRequestId == paidSeed.WithdrawalId && item.OperationType == WalletTransactionType.WithdrawalFinalizePayout));
            Assert.Equal(0, await context.WalletTransactions.CountAsync(item => item.WithdrawalRequestId == paidSeed.WithdrawalId && item.OperationType == WalletTransactionType.WithdrawalRelease));
        }

        var failedSeed = await SeedApprovedWithdrawalAsync();
        using var missingFailureReason = await client.PutAsJsonAsync($"/api/admin/withdrawals/{failedSeed.WithdrawalId}/mark-failed", new { Reason = "" });
        using var failed = await client.PutAsJsonAsync($"/api/admin/withdrawals/{failedSeed.WithdrawalId}/mark-failed", new { Reason = "Bank rejected payout" });
        using var failedRetry = await client.PutAsJsonAsync($"/api/admin/withdrawals/{failedSeed.WithdrawalId}/mark-failed", new { Reason = "Retry" });
        using var failedThenPaid = await client.PutAsJsonAsync($"/api/admin/withdrawals/{failedSeed.WithdrawalId}/mark-paid", new { PayoutReference = "late" });

        Assert.Equal(HttpStatusCode.BadRequest, missingFailureReason.StatusCode);
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, failedRetry.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, failedThenPaid.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var wallet = await context.Wallets.SingleAsync(item => item.Id == failedSeed.WalletId);
            Assert.Equal(250m, wallet.AvailableBalance);
            Assert.Equal(0m, wallet.ReservedBalance);
            var withdrawal = await context.WithdrawalRequests.SingleAsync(item => item.Id == failedSeed.WithdrawalId);
            Assert.Equal(WithdrawalRequestStatus.Failed, withdrawal.Status);
            Assert.Equal("Bank rejected payout", withdrawal.PayoutFailureReason);
            Assert.Equal(paidSeed.AdminUserId, withdrawal.PayoutStatusChangedByAdminUserId);
            Assert.NotNull(withdrawal.PayoutStatusChangedAtUtc);
            Assert.Equal(1, await context.WalletTransactions.CountAsync(item => item.WithdrawalRequestId == failedSeed.WithdrawalId && item.OperationType == WalletTransactionType.WithdrawalRelease));
            Assert.Equal(0, await context.WalletTransactions.CountAsync(item => item.WithdrawalRequestId == failedSeed.WithdrawalId && item.OperationType == WalletTransactionType.WithdrawalFinalizePayout));
        }
    }

    private async Task<WithdrawalSeed> SeedApprovedWithdrawalAsync()
    {
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = new Wallet { Id = $"wallet-{Guid.NewGuid():N}", OwnerType = WalletOwnerType.Doctor, OwnerId = doctor.DoctorId, OwnerUserId = doctor.UserId, AvailableBalance = 150m, ReservedBalance = 100m, Currency = "EGP", CreatedAtUtc = DateTime.UtcNow };
        var withdrawal = new WithdrawalRequest { Id = $"withdrawal-{Guid.NewGuid():N}", DoctorId = doctor.DoctorId, Amount = 100m, Status = WithdrawalRequestStatus.Approved, RequestedAtUtc = DateTime.UtcNow.AddHours(-1), ReviewedAtUtc = DateTime.UtcNow.AddMinutes(-30), ReviewedByAdminUserId = admin.Id };
        var transaction = new WalletTransaction { Id = $"tx-{Guid.NewGuid():N}", WalletId = wallet.Id, OperationType = WalletTransactionType.WithdrawalHold, IdempotencyKey = $"withdrawal:{withdrawal.Id}:hold", Amount = 100m, WithdrawalRequestId = withdrawal.Id, CreatedAtUtc = DateTime.UtcNow.AddHours(-1) };
        context.Wallets.Add(wallet);
        context.WithdrawalRequests.Add(withdrawal);
        context.WalletTransactions.Add(transaction);
        context.WalletLedgerEntries.AddRange(
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = transaction.Id, WithdrawalRequestId = withdrawal.Id, Direction = WalletLedgerEntryDirection.Debit, BalanceType = WalletBalanceType.Available, Amount = 100m },
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = transaction.Id, WithdrawalRequestId = withdrawal.Id, Direction = WalletLedgerEntryDirection.Credit, BalanceType = WalletBalanceType.Reserved, Amount = 100m });
        await context.SaveChangesAsync();
        return new WithdrawalSeed(admin.Id, wallet.Id, withdrawal.Id);
    }

    private sealed record WithdrawalSeed(string AdminUserId, string WalletId, string WithdrawalId);
}
