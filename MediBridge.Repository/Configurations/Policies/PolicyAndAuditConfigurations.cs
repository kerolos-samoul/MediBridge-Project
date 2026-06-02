using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Policies;

public sealed class DoctorPriceHistoryConfiguration : IEntityTypeConfiguration<DoctorPriceHistory>
{
    public void Configure(EntityTypeBuilder<DoctorPriceHistory> builder)
    {
        builder.ToTable("DoctorPriceHistories");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.PreviousPricePerMessage).HasPrecision(18, 2);
        builder.Property(history => history.NewPricePerMessage).HasPrecision(18, 2);
        builder.Property(history => history.Reason).HasMaxLength(1000);
        builder.HasOne<DoctorProfile>().WithMany().HasForeignKey(history => history.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediBridgeIdentityUser>().WithMany().HasForeignKey(history => history.ChangedByAdminUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DoctorPriceHistory>().WithMany().HasForeignKey(history => history.CorrectsHistoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(history => new { history.DoctorId, history.CreatedAtUtc });
    }
}

public sealed class PlatformFeePolicyHistoryConfiguration : IEntityTypeConfiguration<PlatformFeePolicyHistory>
{
    public void Configure(EntityTypeBuilder<PlatformFeePolicyHistory> builder)
    {
        builder.ToTable("PlatformFeePolicyHistories");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.FeePercent).HasPrecision(5, 2);
        builder.Property(history => history.Reason).HasMaxLength(1000);
        builder.HasOne<MediBridgeIdentityUser>().WithMany().HasForeignKey(history => history.ChangedByAdminUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformFeePolicyHistory>().WithMany().HasForeignKey(history => history.CorrectsHistoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(history => new { history.EffectiveFromUtc, history.EffectiveToUtc });
    }
}

public sealed class ActivityScoreHistoryConfiguration : IEntityTypeConfiguration<ActivityScoreHistory>
{
    public void Configure(EntityTypeBuilder<ActivityScoreHistory> builder)
    {
        builder.ToTable("ActivityScoreHistories");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.ActivityScore).HasPrecision(5, 2);
        builder.Property(history => history.ResponseSpeedScore).HasPrecision(5, 2);
        builder.Property(history => history.EngagementScore).HasPrecision(5, 2);
        builder.Property(history => history.FeedbackScore).HasPrecision(5, 2);
        builder.HasOne<DoctorProfile>().WithMany().HasForeignKey(history => history.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActivityScoreHistory>().WithMany().HasForeignKey(history => history.CorrectsHistoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(history => new { history.DoctorId, history.CreatedAtUtc });
    }
}

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(auditEvent => auditEvent.Id);
        builder.Property(auditEvent => auditEvent.EventType).HasMaxLength(160).IsRequired();
        builder.Property(auditEvent => auditEvent.ActorRole).HasMaxLength(80);
        builder.Property(auditEvent => auditEvent.TargetType).HasConversion<int?>();
        builder.Property(auditEvent => auditEvent.Outcome).HasConversion<int>();
        builder.Property(auditEvent => auditEvent.Reason).HasMaxLength(1000);
        builder.Property(auditEvent => auditEvent.CorrelationId).HasMaxLength(160);
        builder.Property(auditEvent => auditEvent.Metadata).HasMaxLength(4000);
        builder.HasOne<MediBridgeIdentityUser>().WithMany().HasForeignKey(auditEvent => auditEvent.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AuditEvent>().WithMany().HasForeignKey(auditEvent => auditEvent.CorrectsAuditEventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(auditEvent => new { auditEvent.TargetType, auditEvent.TargetId, auditEvent.CreatedAtUtc });
    }
}
