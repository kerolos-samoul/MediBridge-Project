using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Messaging;

public sealed class DoctorMessageQueueConfiguration : IEntityTypeConfiguration<DoctorMessageQueue>
{
    public void Configure(EntityTypeBuilder<DoctorMessageQueue> builder)
    {
        builder.ToTable("DoctorMessageQueues");
        builder.HasKey(queue => queue.Id);
        builder.Property(queue => queue.Status).HasConversion<int>();
        builder.Property(queue => queue.ConcurrencyToken).IsRowVersion();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DoctorMessageQueues_QueuedCampaignSubmittedAtUtc",
            "[Status] <> 1 OR [CampaignSubmittedAtUtc] IS NOT NULL"));
        builder.HasOne<DoctorProfile>().WithMany().HasForeignKey(queue => queue.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Campaign>().WithMany().HasForeignKey(queue => queue.CampaignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(queue => new { queue.CampaignId, queue.DoctorId }).IsUnique();
        builder.HasIndex(queue => new
        {
            queue.Status,
            queue.DoctorId,
            queue.CampaignSubmittedAtUtc,
            queue.Id
        });
    }
}

public sealed class DoctorAdDeliveryConfiguration : IEntityTypeConfiguration<DoctorAdDelivery>
{
    public void Configure(EntityTypeBuilder<DoctorAdDelivery> builder)
    {
        builder.ToTable("DoctorAdDeliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Status).HasConversion<int>();
        builder.Property(delivery => delivery.FeedbackQualityStatus).HasConversion<int?>();
        builder.Property(delivery => delivery.ReservationStatus).HasConversion<int>();
        builder.Property(delivery => delivery.PricePerMessageSnapshot).HasPrecision(18, 2);
        builder.Property(delivery => delivery.PlatformFeePercentSnapshot).HasPrecision(7, 3);
        builder.Property(delivery => delivery.PlatformFeeAmount).HasPrecision(18, 2);
        builder.Property(delivery => delivery.DoctorEarnings).HasPrecision(18, 2);
        builder.Property(delivery => delivery.ReservedAmount).HasPrecision(18, 2);
        builder.Property(delivery => delivery.FeedbackText).HasMaxLength(1000);
        builder.Property(delivery => delivery.ConcurrencyToken).IsRowVersion();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DoctorAdDeliveries_Money_NonNegative",
            "[PricePerMessageSnapshot] > 0 AND [PlatformFeePercentSnapshot] > 0 AND [PlatformFeePercentSnapshot] <= 100 AND [PlatformFeeAmount] > 0 AND [DoctorEarnings] > 0 AND [ReservedAmount] > 0 AND [PlatformFeeAmount] + [DoctorEarnings] = [PricePerMessageSnapshot] AND [ReservedAmount] = [PricePerMessageSnapshot]"));
        builder.HasOne<DoctorProfile>().WithMany().HasForeignKey(delivery => delivery.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Campaign>().WithMany().HasForeignKey(delivery => delivery.CampaignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompanyProfile>().WithMany().HasForeignKey(delivery => delivery.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(delivery => new { delivery.DoctorId, delivery.DeliveryDateEgypt, delivery.CampaignId }).IsUnique();
        builder.HasIndex(delivery => new
        {
            delivery.Status,
            delivery.ReservationStatus,
            delivery.DeliveryDateEgypt,
            delivery.CompanyId,
            delivery.Id
        });
        builder.HasIndex(delivery => new
        {
            delivery.DoctorId,
            delivery.DeliveryDateEgypt,
            delivery.Status,
            delivery.ReservationStatus,
            delivery.Id
        });
        builder.HasIndex(delivery => new
        {
            delivery.DoctorId,
            delivery.DeliveryDateEgypt,
            delivery.DeliveredAtUtc,
            delivery.Id
        });
        builder.HasIndex(delivery => new
        {
            delivery.DoctorId,
            delivery.DeliveryDateEgypt,
            delivery.Id
        });
    }
}

public sealed class DeliveryInteractionConfiguration : IEntityTypeConfiguration<DeliveryInteraction>
{
    public void Configure(EntityTypeBuilder<DeliveryInteraction> builder)
    {
        builder.ToTable("DeliveryInteractions");
        builder.HasKey(interaction => interaction.Id);
        builder.Property(interaction => interaction.Outcome).HasConversion<int>();
        builder.Property(interaction => interaction.IdempotencyKeyHash).HasMaxLength(DeliveryInteraction.MaxHashLength).IsRequired();
        builder.Property(interaction => interaction.RequestFingerprint).HasMaxLength(DeliveryInteraction.MaxHashLength).IsRequired();
        builder.Property(interaction => interaction.FeedbackText).HasMaxLength(DeliveryInteraction.MaxFeedbackLength);
        builder.Property(interaction => interaction.ChargeTransactionId).HasMaxLength(450).IsRequired();
        builder.Property(interaction => interaction.EarnTransactionId).HasMaxLength(450).IsRequired();
        builder.Property(interaction => interaction.AuditEventId).HasMaxLength(450);
        builder.Property(interaction => interaction.ConcurrencyToken).IsRowVersion();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_DeliveryInteractions_Outcome", "[Outcome] IN (1, 2)");
            table.HasCheckConstraint("CK_DeliveryInteractions_Feedback_Length", "[FeedbackText] IS NULL OR LEN([FeedbackText]) <= 2000");
        });
        builder.HasOne<DoctorAdDelivery>().WithMany().HasForeignKey(interaction => interaction.DeliveryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DoctorProfile>().WithMany().HasForeignKey(interaction => interaction.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WalletTransaction>().WithMany().HasForeignKey(interaction => interaction.ChargeTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WalletTransaction>().WithMany().HasForeignKey(interaction => interaction.EarnTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AuditEvent>().WithMany().HasForeignKey(interaction => interaction.AuditEventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(interaction => interaction.DeliveryId).IsUnique();
        builder.HasIndex(interaction => new { interaction.DoctorId, interaction.IdempotencyKeyHash }).IsUnique();
    }
}
