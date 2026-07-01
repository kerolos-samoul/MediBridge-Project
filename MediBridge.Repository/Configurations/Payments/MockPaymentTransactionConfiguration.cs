using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Payments;

public sealed class MockPaymentTransactionConfiguration : IEntityTypeConfiguration<MockPaymentTransaction>
{
    public void Configure(EntityTypeBuilder<MockPaymentTransaction> builder)
    {
        builder.ToTable("MockPaymentTransactions");
        builder.HasKey(payment => payment.PaymentId);
        builder.Property(payment => payment.CompanyId).IsRequired();
        builder.Property(payment => payment.WalletId).IsRequired();
        builder.Property(payment => payment.Amount).HasPrecision(18, 2);
        builder.Property(payment => payment.Currency).HasMaxLength(3).IsRequired();
        builder.Property(payment => payment.Status).HasConversion<int>().IsRequired();
        builder.Property(payment => payment.TransactionReference).HasMaxLength(160).IsRequired();
        builder.Property(payment => payment.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(payment => payment.WalletBalanceBefore).HasPrecision(18, 2);
        builder.Property(payment => payment.WalletBalanceAfter).HasPrecision(18, 2);
        builder.Property(payment => payment.WalletTransactionId).IsRequired();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_MockPaymentTransactions_Amount_Positive", "[Amount] > 0");
            table.HasCheckConstraint("CK_MockPaymentTransactions_Currency_EGP", "[Currency] = 'EGP'");
            table.HasCheckConstraint("CK_MockPaymentTransactions_Status_Succeeded", $"[Status] = {(int)PaymentStatus.Succeeded}");
            table.HasCheckConstraint("CK_MockPaymentTransactions_Balances_NonNegative", "[WalletBalanceBefore] >= 0 AND [WalletBalanceAfter] >= 0");
        });
        builder.HasOne<CompanyProfile>().WithMany().HasForeignKey(payment => payment.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Wallet>().WithMany().HasForeignKey(payment => payment.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WalletTransaction>().WithMany().HasForeignKey(payment => payment.WalletTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AuditEvent>().WithMany().HasForeignKey(payment => payment.AuditEventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(payment => payment.TransactionReference).IsUnique();
        builder.HasIndex(payment => new { payment.CompanyId, payment.IdempotencyKey }).IsUnique();
        builder.HasIndex(payment => new { payment.CompanyId, payment.CreatedAtUtc });
    }
}
