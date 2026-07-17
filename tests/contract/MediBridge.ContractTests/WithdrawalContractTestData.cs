using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.ContractTests;

internal static class WithdrawalContractTestData
{
    public static async Task<string> SeedAdminAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var email = $"admin-{Guid.NewGuid():N}@contract.local";
        var admin = new MediBridgeIdentityUser
        {
            Id = $"admin-{Guid.NewGuid():N}",
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            EmailConfirmed = true,
            EmailVerified = true,
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            LastStatusChangedAtUtc = now
        };

        context.Users.Add(admin);
        await context.SaveChangesAsync();
        return admin.Id;
    }

    public static async Task<DoctorContractSeed> SeedDoctorAsync(IServiceProvider services, AccountStatus accountStatus = AccountStatus.Approved, DoctorMarketplaceStatus marketplaceStatus = DoctorMarketplaceStatus.Active, decimal walletAvailable = 250m)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var email = $"doctor-{suffix}@contract.local";
        var user = new MediBridgeIdentityUser
        {
            Id = $"doctor-user-{suffix}",
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = UserRole.Doctor,
            AccountStatus = accountStatus,
            EmailConfirmed = true,
            EmailVerified = true,
            CreatedAtUtc = now,
            ApprovedAtUtc = accountStatus == AccountStatus.Approved ? now : null,
            LastStatusChangedAtUtc = now
        };
        var doctor = new DoctorProfile
        {
            Id = $"doctor-{suffix}",
            UserId = user.Id,
            Specialization = "Cardiology",
            ExperienceYears = 8,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}",
            Status = marketplaceStatus,
            DailyMessageLimit = 10,
            PricePerMessage = 50m,
            PricingIsActive = true,
            CreatedAtUtc = now
        };
        var wallet = new Wallet
        {
            Id = $"wallet-{suffix}",
            OwnerType = WalletOwnerType.Doctor,
            OwnerId = doctor.Id,
            OwnerUserId = user.Id,
            AvailableBalance = walletAvailable,
            ReservedBalance = 0m,
            Currency = "EGP",
            CreatedAtUtc = now
        };

        context.Users.Add(user);
        context.DoctorProfiles.Add(doctor);
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();
        return new DoctorContractSeed(user.Id, doctor.Id, wallet.Id);
    }

    public static async Task<WithdrawalContractSeed> SeedWithdrawalAsync(
        IServiceProvider services,
        WithdrawalRequestStatus status,
        string? adminUserId = null,
        string? doctorUserId = null,
        string? doctorId = null,
        decimal amount = 100m,
        DateTime? requestedAtUtc = null,
        string? payoutReference = null)
    {
        var adminId = adminUserId ?? await SeedAdminAsync(services);
        DoctorContractSeed doctor = doctorId is null || doctorUserId is null
            ? await SeedDoctorAsync(services, walletAvailable: 150m)
            : new DoctorContractSeed(doctorUserId, doctorId, string.Empty);

        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = string.IsNullOrWhiteSpace(doctor.WalletId)
            ? await context.Wallets.SingleAsync(item => item.OwnerId == doctor.DoctorId)
            : await context.Wallets.SingleAsync(item => item.Id == doctor.WalletId);
        wallet.AvailableBalance = status is WithdrawalRequestStatus.Rejected or WithdrawalRequestStatus.Failed ? 250m : 150m;
        wallet.ReservedBalance = status is WithdrawalRequestStatus.Requested or WithdrawalRequestStatus.Approved ? 100m : 0m;
        var now = requestedAtUtc ?? DateTime.UtcNow;
        var withdrawal = new WithdrawalRequest
        {
            Id = $"withdrawal-{Guid.NewGuid():N}",
            DoctorId = doctor.DoctorId,
            Amount = amount,
            Status = status,
            RequestedAtUtc = now,
            ReviewedByAdminUserId = status == WithdrawalRequestStatus.Requested ? null : adminId,
            ReviewedAtUtc = status == WithdrawalRequestStatus.Requested ? null : now.AddMinutes(10),
            DecisionReason = status == WithdrawalRequestStatus.Rejected ? "Rejected" : null,
            PayoutReference = status == WithdrawalRequestStatus.Paid ? payoutReference ?? "payout-contract" : null,
            PayoutStatusChangedByAdminUserId = status is WithdrawalRequestStatus.Paid or WithdrawalRequestStatus.Failed ? adminId : null,
            PayoutStatusChangedAtUtc = status is WithdrawalRequestStatus.Paid or WithdrawalRequestStatus.Failed ? now.AddMinutes(20) : null,
            PayoutFailureReason = status == WithdrawalRequestStatus.Failed ? "Failed" : null
        };
        var transaction = new WalletTransaction
        {
            Id = $"tx-{Guid.NewGuid():N}",
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.WithdrawalHold,
            IdempotencyKey = $"withdrawal:{withdrawal.Id}:hold",
            Amount = amount,
            WithdrawalRequestId = withdrawal.Id,
            CreatedAtUtc = now
        };
        context.WithdrawalRequests.Add(withdrawal);
        context.WalletTransactions.Add(transaction);
        context.WalletLedgerEntries.AddRange(
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = transaction.Id, WithdrawalRequestId = withdrawal.Id, Direction = WalletLedgerEntryDirection.Debit, BalanceType = WalletBalanceType.Available, Amount = amount, CreatedAtUtc = now },
            new WalletLedgerEntry { Id = $"ledger-{Guid.NewGuid():N}", WalletId = wallet.Id, WalletTransactionId = transaction.Id, WithdrawalRequestId = withdrawal.Id, Direction = WalletLedgerEntryDirection.Credit, BalanceType = WalletBalanceType.Reserved, Amount = amount, CreatedAtUtc = now });
        await context.SaveChangesAsync();
        return new WithdrawalContractSeed(adminId, doctor.UserId, doctor.DoctorId, wallet.Id, withdrawal.Id);
    }
}

internal sealed record DoctorContractSeed(string UserId, string DoctorId, string WalletId);

internal sealed record WithdrawalContractSeed(string AdminUserId, string DoctorUserId, string DoctorId, string WalletId, string WithdrawalId);
