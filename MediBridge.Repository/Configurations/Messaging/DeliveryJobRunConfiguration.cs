using MediBridge.Core.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Messaging;

public sealed class DeliveryJobRunConfiguration : IEntityTypeConfiguration<DeliveryJobRun>
{
    public void Configure(EntityTypeBuilder<DeliveryJobRun> builder)
    {
        builder.ToTable("DeliveryJobRuns", table => table.HasCheckConstraint(
            "CK_DeliveryJobRuns_Counters_NonNegative",
            "[ExaminedCount] >= 0 AND [ActivatedCount] >= 0 AND [ExpiredCount] >= 0 AND [CancelledCount] >= 0 AND [SkippedCount] >= 0 AND [FailedCount] >= 0"));
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasMaxLength(64);
        builder.Property(run => run.JobType).HasConversion<int>();
        builder.Property(run => run.Status).HasConversion<int>();
        builder.Property(run => run.SafeFailureSummary).HasMaxLength(DeliveryJobRun.MaxSafeFailureSummaryLength);
        builder.Property(run => run.ConcurrencyToken).IsRowVersion();
        builder.HasIndex(run => new { run.JobType, run.BusinessDateEgypt, run.StartedAtUtc });
        builder.HasIndex(run => new { run.Status, run.StartedAtUtc });
    }
}
