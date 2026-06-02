using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Profiles;
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
        builder.HasOne<DoctorProfile>().WithMany().HasForeignKey(queue => queue.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Campaign>().WithMany().HasForeignKey(queue => queue.CampaignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(queue => new { queue.DoctorId, queue.Status, queue.QueuedAtUtc, queue.Id });
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
        builder.Property(delivery => delivery.PlatformFeePercentSnapshot).HasPrecision(5, 2);
        builder.Property(delivery => delivery.PlatformFeeAmount).HasPrecision(18, 2);
        builder.Property(delivery => delivery.DoctorEarnings).HasPrecision(18, 2);
        builder.Property(delivery => delivery.ReservedAmount).HasPrecision(18, 2);
        builder.Property(delivery => delivery.FeedbackText).HasMaxLength(4000);
        builder.Property(delivery => delivery.ConcurrencyToken).IsRowVersion();
        builder.ToTable(table => table.HasCheckConstraint("CK_DoctorAdDeliveries_Money_NonNegative", "[PricePerMessageSnapshot] >= 0 AND [PlatformFeeAmount] >= 0 AND [DoctorEarnings] >= 0 AND [ReservedAmount] >= 0"));
        builder.HasOne<DoctorProfile>().WithMany().HasForeignKey(delivery => delivery.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Campaign>().WithMany().HasForeignKey(delivery => delivery.CampaignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompanyProfile>().WithMany().HasForeignKey(delivery => delivery.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(delivery => new { delivery.DoctorId, delivery.DeliveryDateEgypt, delivery.CampaignId }).IsUnique();
    }
}
