using MediBridge.Core.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Messaging;

public sealed class DeliveryInteractionOperationConfiguration : IEntityTypeConfiguration<DeliveryInteractionOperation>
{
    public void Configure(EntityTypeBuilder<DeliveryInteractionOperation> builder)
    {
        builder.ToTable("DeliveryInteractionOperations");
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.DoctorId).IsRequired();
        builder.Property(operation => operation.DeliveryId).IsRequired();
        builder.Property(operation => operation.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(DeliveryInteractionOperation.MaxIdempotencyKeyLength);
        builder.Property(operation => operation.Decision).HasConversion<int>();
        builder.Property(operation => operation.Status).HasConversion<int>();
        builder.Property(operation => operation.FeedbackText).HasMaxLength(DeliveryInteractionOperation.MaxFeedbackTextLength);
        builder.Property(operation => operation.SafeFailureSummary).HasMaxLength(DeliveryInteractionOperation.MaxSafeFailureSummaryLength);
        builder.Property(operation => operation.ConcurrencyToken).IsRowVersion();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_DeliveryInteractionOperations_IdempotencyKey_Length",
                "LEN([IdempotencyKey]) BETWEEN 8 AND 128");
            table.HasCheckConstraint(
                "CK_DeliveryInteractionOperations_FeedbackText_Length",
                "[FeedbackText] IS NULL OR LEN([FeedbackText]) <= 1000");
        });
        builder.HasOne<DoctorAdDelivery>()
            .WithMany()
            .HasForeignKey(operation => operation.DeliveryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(operation => new
        {
            operation.DoctorId,
            operation.DeliveryId,
            operation.IdempotencyKey
        }).IsUnique();
        builder.HasIndex(operation => new
        {
            operation.DeliveryId,
            operation.Decision
        });
    }
}
