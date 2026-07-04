using MediBridge.Core.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Messaging;

public sealed class DeliveryRecoveryDispatchConfiguration : IEntityTypeConfiguration<DeliveryRecoveryDispatch>
{
    public void Configure(EntityTypeBuilder<DeliveryRecoveryDispatch> builder)
    {
        builder.ToTable("DeliveryRecoveryDispatches");
        builder.HasKey(dispatch => dispatch.Id);
        builder.Property(dispatch => dispatch.Id).HasMaxLength(64);
        builder.Property(dispatch => dispatch.JobType).HasConversion<int>();
        builder.Property(dispatch => dispatch.Status).HasConversion<int>();
        builder.Property(dispatch => dispatch.SchedulerJobId).HasMaxLength(100);
        builder.Property(dispatch => dispatch.DependsOnDispatchId).HasMaxLength(64);
        builder.Property(dispatch => dispatch.SafeFailureSummary).HasMaxLength(DeliveryRecoveryDispatch.MaxSafeFailureSummaryLength);
        builder.Property(dispatch => dispatch.ConcurrencyToken).IsRowVersion();
        builder.HasIndex(dispatch => new { dispatch.BusinessDateEgypt, dispatch.JobType }).IsUnique();
        builder.HasIndex(dispatch => new { dispatch.Status, dispatch.ClaimedAtUtc });
        builder.HasOne<DeliveryRecoveryDispatch>()
            .WithMany()
            .HasForeignKey(dispatch => dispatch.DependsOnDispatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
