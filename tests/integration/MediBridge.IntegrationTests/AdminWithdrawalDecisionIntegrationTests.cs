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

public sealed class AdminWithdrawalDecisionIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminWithdrawalDecisionIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ApprovePreservesHoldAndRejectReleasesHoldExactlyOnce()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedRequestedWithdrawalAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", seed.AdminUserId));

        using var approve = await client.PutAsJsonAsync($"/api/admin/withdrawals/{seed.WithdrawalId}/approve", new { Note = "Approved for payout" });
        using var secondApprove = await client.PutAsJsonAsync($"/api/admin/withdrawals/{seed.WithdrawalId}/approve", new { Note = "Retry" });
        using var approveThenReject = await client.PutAsJsonAsync($"/api/admin/withdrawals/{seed.WithdrawalId}/reject", new { Reason = "Too late" });

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondApprove.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, approveThenReject.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var wallet = await context.Wallets.SingleAsync(item => item.Id == seed.WalletId);
            Assert.Equal(150m, wallet.AvailableBalance);
            Assert.Equal(100m, wallet.ReservedBalance);
            var withdrawal = await context.WithdrawalRequests.SingleAsync(item => item.Id == seed.WithdrawalId);
            Assert.Equal(WithdrawalRequestStatus.Approved, withdrawal.Status);
            Assert.Equal(seed.AdminUserId, withdrawal.ReviewedByAdminUserId);
            Assert.NotNull(withdrawal.ReviewedAtUtc);
            Assert.Equal("Approved for payout", withdrawal.DecisionReason);
            var audit = await context.AuditEvents.SingleAsync(item => item.TargetId == seed.WithdrawalId && item.EventType == "WithdrawalApproved");
            Assert.Equal(seed.AdminUserId, audit.ActorUserId);
            Assert.Contains("\"PriorStatus\":\"Requested\"", audit.Metadata);
            Assert.Contains("\"ResultingStatus\":\"Approved\"", audit.Metadata);
            Assert.Contains("\"ChangedAtUtc\"", audit.Metadata);
        }

        var rejectSeed = await SeedRequestedWithdrawalAsync();
        using var missingReason = await client.PutAsJsonAsync($"/api/admin/withdrawals/{rejectSeed.WithdrawalId}/reject", new { Reason = "" });
        using var reject = await client.PutAsJsonAsync($"/api/admin/withdrawals/{rejectSeed.WithdrawalId}/reject", new { Reason = "Invalid payout details" });
        using var secondReject = await client.PutAsJsonAsync($"/api/admin/withdrawals/{rejectSeed.WithdrawalId}/reject", new { Reason = "Retry" });

        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondReject.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var wallet = await context.Wallets.SingleAsync(item => item.Id == rejectSeed.WalletId);
            Assert.Equal(250m, wallet.AvailableBalance);
            Assert.Equal(0m, wallet.ReservedBalance);
            var withdrawal = await context.WithdrawalRequests.SingleAsync(item => item.Id == rejectSeed.WithdrawalId);
            Assert.Equal(WithdrawalRequestStatus.Rejected, withdrawal.Status);
            Assert.Equal(seed.AdminUserId, withdrawal.ReviewedByAdminUserId);
            Assert.NotNull(withdrawal.ReviewedAtUtc);
            Assert.Equal("Invalid payout details", withdrawal.DecisionReason);
            Assert.Equal(1, await context.WalletTransactions.CountAsync(item => item.WithdrawalRequestId == rejectSeed.WithdrawalId && item.OperationType == WalletTransactionType.WithdrawalRelease));
            var audit = await context.AuditEvents.SingleAsync(item => item.TargetId == rejectSeed.WithdrawalId && item.EventType == "WithdrawalRejected");
            Assert.Equal(seed.AdminUserId, audit.ActorUserId);
            Assert.Contains("\"PriorStatus\":\"Requested\"", audit.Metadata);
            Assert.Contains("\"ResultingStatus\":\"Rejected\"", audit.Metadata);
        }
    }

    private async Task<WithdrawalSeed> SeedRequestedWithdrawalAsync()
    {
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        using var scope = factory.Services.CreateScope();
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
            CreatedAtUtc = DateTime.UtcNow
        };
        var withdrawal = new WithdrawalRequest
        {
            Id = $"withdrawal-{Guid.NewGuid():N}",
            DoctorId = doctor.DoctorId,
            Amount = 100m,
            Status = WithdrawalRequestStatus.Requested,
            RequestedAtUtc = DateTime.UtcNow
        };
        var transaction = new WalletTransaction
        {
            Id = $"tx-{Guid.NewGuid():N}",
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.WithdrawalHold,
            IdempotencyKey = $"withdrawal:{withdrawal.Id}:hold",
            Amount = 100m,
            WithdrawalRequestId = withdrawal.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
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
