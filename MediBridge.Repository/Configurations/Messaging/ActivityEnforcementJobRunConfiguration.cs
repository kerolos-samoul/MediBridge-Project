using MediBridge.Core.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Messaging;

public sealed class ActivityEnforcementJobRunConfiguration : IEntityTypeConfiguration<ActivityEnforcementJobRun>
{
    public void Configure(EntityTypeBuilder<ActivityEnforcementJobRun> builder)
    {
        builder.ToTable("ActivityEnforcementJobRuns", table =>
        {
            table.HasCheckConstraint("CK_ActivityEnforcementJobRuns_Counters_NonNegative", "[ProcessedCount] >= 0 AND [SkippedCount] >= 0 AND [CreatedCount] >= 0 AND [UpdatedCount] >= 0 AND [FailedCount] >= 0");
        });
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasMaxLength(64);
        builder.Property(run => run.JobType).HasConversion<int>();
        builder.Property(run => run.Status).HasConversion<int>();
        builder.Property(run => run.RequestedByAdminUserId).HasMaxLength(450);
        builder.Property(run => run.SafeFailureSummary).HasMaxLength(ActivityEnforcementJobRun.MaxSafeFailureSummaryLength);
        builder.HasIndex(run => new { run.JobType, run.StartedAtUtc });
        builder.HasIndex(run => new { run.JobType, run.TargetScoreDateEgypt });
        builder.HasIndex(run => new { run.JobType, run.TargetWeekStartDateEgypt });
    }
}
