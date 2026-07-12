using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProfileActivityScoreHistory = MediBridge.Core.Entities.Profiles.ActivityScoreHistory;

namespace MediBridge.Repository.Configurations.Identity;

public sealed class ActivityScoreHistoryConfiguration : IEntityTypeConfiguration<ProfileActivityScoreHistory>
{
    public void Configure(EntityTypeBuilder<ProfileActivityScoreHistory> builder)
    {
        builder.ToTable("DoctorActivityScoreHistories", table =>
        {
            table.HasCheckConstraint("CK_DoctorActivityScoreHistories_Counts_NonNegative", "[DeliveredCount] >= 0 AND [InteractedCount] >= 0 AND [FeedbackQualifiedCount] >= 0");
            table.HasCheckConstraint("CK_DoctorActivityScoreHistories_Scores_Range", "[ResponseSpeedScore] >= 0 AND [ResponseSpeedScore] <= 100 AND [EngagementScore] >= 0 AND [EngagementScore] <= 100 AND [FeedbackScore] >= 0 AND [FeedbackScore] <= 100 AND [FinalScore] >= 0 AND [FinalScore] <= 100");
        });
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Id).HasMaxLength(64);
        builder.Property(snapshot => snapshot.DoctorId).HasMaxLength(450).IsRequired();
        builder.Property(snapshot => snapshot.ResponseSpeedScore).HasPrecision(5, 1);
        builder.Property(snapshot => snapshot.EngagementScore).HasPrecision(5, 1);
        builder.Property(snapshot => snapshot.FeedbackScore).HasPrecision(5, 1);
        builder.Property(snapshot => snapshot.FinalScore).HasPrecision(5, 1);
        builder.Property(snapshot => snapshot.CalculationMode).HasConversion<int>();
        builder.Property(snapshot => snapshot.JobRunId).HasMaxLength(64);
        builder.HasIndex(snapshot => new { snapshot.DoctorId, snapshot.ScoreDateEgypt }).IsUnique();
        builder.HasIndex(snapshot => new { snapshot.ScoreDateEgypt, snapshot.DoctorId });
        builder.HasOne<DoctorProfile>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediBridge.Core.Entities.Messaging.ActivityEnforcementJobRun>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.JobRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WeeklyEnforcementDecisionConfiguration : IEntityTypeConfiguration<WeeklyEnforcementDecision>
{
    public void Configure(EntityTypeBuilder<WeeklyEnforcementDecision> builder)
    {
        builder.ToTable("WeeklyEnforcementDecisions", table =>
        {
            table.HasCheckConstraint("CK_WeeklyEnforcementDecisions_Counts_NonNegative", "[MinimumWeeklyRequirement] >= 0 AND [InteractionCount] >= 0 AND [RollingViolationCountAfterDecision] >= 0");
        });
        builder.HasKey(decision => decision.Id);
        builder.Property(decision => decision.Id).HasMaxLength(64);
        builder.Property(decision => decision.DoctorId).HasMaxLength(450).IsRequired();
        builder.Property(decision => decision.Decision).HasConversion<int>();
        builder.Property(decision => decision.JobRunId).HasMaxLength(64);
        builder.HasIndex(decision => new { decision.DoctorId, decision.WeekStartDateEgypt }).IsUnique();
        builder.HasIndex(decision => new { decision.WeekStartDateEgypt, decision.Decision, decision.DoctorId });
        builder.HasOne<DoctorProfile>()
            .WithMany()
            .HasForeignKey(decision => decision.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediBridge.Core.Entities.Messaging.ActivityEnforcementJobRun>()
            .WithMany()
            .HasForeignKey(decision => decision.JobRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DoctorWeeklyViolationConfiguration : IEntityTypeConfiguration<DoctorWeeklyViolation>
{
    public void Configure(EntityTypeBuilder<DoctorWeeklyViolation> builder)
    {
        builder.ToTable("DoctorWeeklyViolations", table =>
        {
            table.HasCheckConstraint("CK_DoctorWeeklyViolations_Counts", "[MinimumWeeklyRequirement] >= 0 AND [InteractionCount] >= 0 AND [RollingViolationCount] >= 0 AND [InteractionCount] < [MinimumWeeklyRequirement]");
        });
        builder.HasKey(violation => violation.Id);
        builder.Property(violation => violation.Id).HasMaxLength(64);
        builder.Property(violation => violation.DoctorId).HasMaxLength(450).IsRequired();
        builder.Property(violation => violation.WeeklyEnforcementDecisionId).HasMaxLength(64).IsRequired();
        builder.Property(violation => violation.AuditEventId).HasMaxLength(450);
        builder.HasIndex(violation => new { violation.DoctorId, violation.WeekStartDateEgypt }).IsUnique();
        builder.HasIndex(violation => new { violation.WeekStartDateEgypt, violation.DoctorId });
        builder.HasOne<DoctorProfile>()
            .WithMany()
            .HasForeignKey(violation => violation.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WeeklyEnforcementDecision>()
            .WithMany()
            .HasForeignKey(violation => violation.WeeklyEnforcementDecisionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AuditEvent>()
            .WithMany()
            .HasForeignKey(violation => violation.AuditEventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DoctorEnforcementActionConfiguration : IEntityTypeConfiguration<DoctorEnforcementAction>
{
    public void Configure(EntityTypeBuilder<DoctorEnforcementAction> builder)
    {
        builder.ToTable("DoctorEnforcementActions", table =>
        {
            table.HasCheckConstraint("CK_DoctorEnforcementActions_DailyLimits_NonNegative", "[PreviousDailyMessageLimit] >= 0 AND ([NewDailyMessageLimit] IS NULL OR [NewDailyMessageLimit] >= 0)");
        });
        builder.HasKey(action => action.Id);
        builder.Property(action => action.Id).HasMaxLength(64);
        builder.Property(action => action.DoctorId).HasMaxLength(450).IsRequired();
        builder.Property(action => action.ActorAdminUserId).HasMaxLength(450);
        builder.Property(action => action.ActionType).HasConversion<int>();
        builder.Property(action => action.Reason).HasMaxLength(1000);
        builder.Property(action => action.PreviousStatus).HasConversion<int>();
        builder.Property(action => action.NewStatus).HasConversion<int>();
        builder.Property(action => action.CorrelationId).HasMaxLength(128);
        builder.Property(action => action.AuditEventId).HasMaxLength(450);
        builder.HasIndex(action => new { action.DoctorId, action.CreatedAtUtc });
        builder.HasIndex(action => new { action.ActorAdminUserId, action.CreatedAtUtc });
        builder.HasOne<DoctorProfile>()
            .WithMany()
            .HasForeignKey(action => action.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AuditEvent>()
            .WithMany()
            .HasForeignKey(action => action.AuditEventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
