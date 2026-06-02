using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Wallets;

public sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");
        builder.HasKey(wallet => wallet.Id);
        builder.HasQueryFilter(wallet => !wallet.IsDeleted);
        builder.Property(wallet => wallet.OwnerType).HasConversion<int>();
        builder.Property(wallet => wallet.AvailableBalance).HasPrecision(18, 2);
        builder.Property(wallet => wallet.ReservedBalance).HasPrecision(18, 2);
        builder.Property(wallet => wallet.Currency).HasMaxLength(3).IsRequired();
        builder.Property(wallet => wallet.ConcurrencyToken).IsRowVersion();
        builder.ToTable(table => table.HasCheckConstraint("CK_Wallets_Balances_NonNegative", "[AvailableBalance] >= 0 AND [ReservedBalance] >= 0"));
        builder.HasOne<MediBridgeIdentityUser>().WithMany().HasForeignKey(wallet => wallet.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        // SQL Server filtered uniqueness preserves historical soft-deleted wallets while allowing one active wallet per owner.
        builder.HasIndex(wallet => new { wallet.OwnerType, wallet.OwnerId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}

public sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("WalletTransactions");
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.OperationType).HasConversion<int>();
        builder.Property(transaction => transaction.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(transaction => transaction.Amount).HasPrecision(18, 2);
        builder.Property(transaction => transaction.Description).HasMaxLength(1000);
        builder.Property(transaction => transaction.Metadata).HasMaxLength(4000);
        builder.ToTable(table => table.HasCheckConstraint("CK_WalletTransactions_Amount_Positive", "[Amount] > 0"));
        builder.HasOne<Wallet>().WithMany().HasForeignKey(transaction => transaction.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DoctorAdDelivery>().WithMany().HasForeignKey(transaction => transaction.RelatedDeliveryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WalletTransaction>().WithMany().HasForeignKey(transaction => transaction.CorrectsTransactionId).OnDelete(DeleteBehavior.Restrict);
        // Retries are deduplicated per wallet operation type, not by idempotency key alone.
        builder.HasIndex(transaction => new { transaction.OperationType, transaction.IdempotencyKey }).IsUnique();
        builder.HasIndex(transaction => new { transaction.WalletId, transaction.CreatedAtUtc });
    }
}

public sealed class WalletLedgerEntryConfiguration : IEntityTypeConfiguration<WalletLedgerEntry>
{
    public void Configure(EntityTypeBuilder<WalletLedgerEntry> builder)
    {
        builder.ToTable("WalletLedgerEntries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Direction).HasConversion<int>();
        builder.Property(entry => entry.BalanceType).HasConversion<int>();
        builder.Property(entry => entry.Amount).HasPrecision(18, 2);
        builder.Property(entry => entry.Currency).HasMaxLength(3).IsRequired();
        builder.Property(entry => entry.IdempotencyKey).HasMaxLength(160);
        builder.ToTable(table => table.HasCheckConstraint("CK_WalletLedgerEntries_Amount_Positive", "[Amount] > 0"));
        builder.HasOne<WalletTransaction>().WithMany().HasForeignKey(entry => entry.WalletTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Wallet>().WithMany().HasForeignKey(entry => entry.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => new { entry.WalletId, entry.CreatedAtUtc });
        builder.HasIndex(entry => new { entry.WalletTransactionId, entry.CreatedAtUtc });
    }
}

public sealed class WithdrawalRequestConfiguration : IEntityTypeConfiguration<WithdrawalRequest>
{
    public void Configure(EntityTypeBuilder<WithdrawalRequest> builder)
    {
        builder.ToTable("WithdrawalRequests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Amount).HasPrecision(18, 2);
        builder.Property(request => request.Status).HasConversion<int>();
        builder.Property(request => request.DecisionReason).HasMaxLength(1000);
        builder.Property(request => request.PayoutReference).HasMaxLength(200);
        builder.Property(request => request.ConcurrencyToken).IsRowVersion();
        builder.ToTable(table => table.HasCheckConstraint("CK_WithdrawalRequests_Amount_Positive", "[Amount] > 0"));
        builder.HasOne<DoctorProfile>().WithMany().HasForeignKey(request => request.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediBridgeIdentityUser>().WithMany().HasForeignKey(request => request.ReviewedByAdminUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
