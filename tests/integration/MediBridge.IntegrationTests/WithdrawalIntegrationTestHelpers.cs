using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.IntegrationTests;

internal static class WithdrawalIntegrationTestHelpers
{
    public static async Task<string> SeedDoctorWalletAsync(IServiceProvider services, string doctorId, string doctorUserId, decimal availableBalance, decimal reservedBalance = 0m)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = new Wallet
        {
            Id = $"wallet-{Guid.NewGuid():N}",
            OwnerType = WalletOwnerType.Doctor,
            OwnerId = doctorId,
            OwnerUserId = doctorUserId,
            AvailableBalance = availableBalance,
            ReservedBalance = reservedBalance,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow
        };

        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();
        return wallet.Id;
    }

    public static async Task<WithdrawalSeed> SeedWithdrawalAsync(
        IServiceProvider services,
        WithdrawalRequestStatus status,
        decimal amount = 100m,
        decimal availableBalance = 150m,
        decimal reservedBalance = 100m,
        string? adminUserId = null,
        DateTime? requestedAtUtc = null,
        string? payoutReference = null)
    {
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(services);
        var adminId = adminUserId ?? (await Phase6IdentityTestHelpers.CreateAdminAsync(services)).Id;
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var requestedAt = requestedAtUtc ?? now.AddHours(-1);
        var wallet = new Wallet
        {
            Id = $"wallet-{Guid.NewGuid():N}",
            OwnerType = WalletOwnerType.Doctor,
            OwnerId = doctor.DoctorId,
            OwnerUserId = doctor.UserId,
            AvailableBalance = availableBalance,
            ReservedBalance = reservedBalance,
            Currency = "EGP",
            CreatedAtUtc = requestedAt
        };
        var withdrawal = new WithdrawalRequest
        {
            Id = $"withdrawal-{Guid.NewGuid():N}",
            DoctorId = doctor.DoctorId,
            Amount = amount,
            Status = status,
            RequestedAtUtc = requestedAt,
            ReviewedByAdminUserId = status is WithdrawalRequestStatus.Approved or WithdrawalRequestStatus.Rejected or WithdrawalRequestStatus.Paid or WithdrawalRequestStatus.Failed ? adminId : null,
            ReviewedAtUtc = status is WithdrawalRequestStatus.Approved or WithdrawalRequestStatus.Rejected or WithdrawalRequestStatus.Paid or WithdrawalRequestStatus.Failed ? requestedAt.AddMinutes(10) : null,
            DecisionReason = status == WithdrawalRequestStatus.Rejected ? "Rejected in fixture" : null,
            PayoutReference = status == WithdrawalRequestStatus.Paid ? payoutReference ?? "payout-fixture" : null,
            PayoutStatusChangedByAdminUserId = status is WithdrawalRequestStatus.Paid or WithdrawalRequestStatus.Failed ? adminId : null,
            PayoutStatusChangedAtUtc = status is WithdrawalRequestStatus.Paid or WithdrawalRequestStatus.Failed ? requestedAt.AddMinutes(20) : null,
            PayoutFailureReason = status == WithdrawalRequestStatus.Failed ? "Failed in fixture" : null
        };
        var hold = new WalletTransaction
        {
            Id = $"tx-{Guid.NewGuid():N}",
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.WithdrawalHold,
            IdempotencyKey = $"withdrawal:{withdrawal.Id}:hold",
            Amount = amount,
            WithdrawalRequestId = withdrawal.Id,
            CreatedAtUtc = requestedAt
        };

        context.Wallets.Add(wallet);
        context.WithdrawalRequests.Add(withdrawal);
        context.WalletTransactions.Add(hold);
        context.WalletLedgerEntries.AddRange(
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = hold.Id, WithdrawalRequestId = withdrawal.Id, Direction = WalletLedgerEntryDirection.Debit, BalanceType = WalletBalanceType.Available, Amount = amount, CreatedAtUtc = requestedAt },
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = hold.Id, WithdrawalRequestId = withdrawal.Id, Direction = WalletLedgerEntryDirection.Credit, BalanceType = WalletBalanceType.Reserved, Amount = amount, CreatedAtUtc = requestedAt });
        await context.SaveChangesAsync();
        return new WithdrawalSeed(adminId, doctor.UserId, doctor.DoctorId, wallet.Id, withdrawal.Id);
    }

    public static async Task<(decimal Available, decimal Reserved)> GetWalletBalancesAsync(IServiceProvider services, string walletId)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == walletId);
        return (wallet.AvailableBalance, wallet.ReservedBalance);
    }
}

internal sealed record WithdrawalSeed(string AdminUserId, string DoctorUserId, string DoctorId, string WalletId, string WithdrawalId);
