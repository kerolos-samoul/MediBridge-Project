using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Campaigns;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");
        builder.HasKey(campaign => campaign.Id);
        builder.HasQueryFilter(campaign => !campaign.IsDeleted);
        builder.Property(campaign => campaign.Title).HasMaxLength(200).IsRequired();
        builder.Property(campaign => campaign.Description).HasMaxLength(4000).IsRequired();
        builder.Property(campaign => campaign.ClinicalResearchInfo).HasMaxLength(4000);
        builder.Property(campaign => campaign.Status).HasConversion<int>();
        builder.Property(campaign => campaign.SubmittedAtUtc);
        builder.HasOne<CompanyProfile>().WithMany().HasForeignKey(campaign => campaign.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(campaign => campaign.MediaFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(campaign => campaign.VoiceNoteFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(campaign => new { campaign.CompanyId, campaign.CreatedAtUtc });
        builder.HasIndex(campaign => new { campaign.Status, campaign.SubmittedAtUtc, campaign.Id });
    }
}

public sealed class CampaignTargetConfiguration : IEntityTypeConfiguration<CampaignTarget>
{
    public void Configure(EntityTypeBuilder<CampaignTarget> builder)
    {
        builder.ToTable("CampaignTargets");
        builder.HasKey(target => target.Id);
        builder.Property(target => target.SpecializationSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(target => target.LocationSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(target => target.ActivityScoreSnapshot).HasPrecision(5, 2);
        builder.Property(target => target.PricePerMessageSnapshot).HasPrecision(18, 2);
        builder.HasOne<Campaign>().WithMany().HasForeignKey(target => target.CampaignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DoctorProfile>().WithMany().HasForeignKey(target => target.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(target => new { target.CampaignId, target.DoctorId }).IsUnique();
    }
}

public sealed class CampaignReviewHistoryConfiguration : IEntityTypeConfiguration<CampaignReviewHistory>
{
    public void Configure(EntityTypeBuilder<CampaignReviewHistory> builder)
    {
        builder.ToTable("CampaignReviewHistories");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.Decision).HasConversion<int>();
        builder.Property(history => history.PriorStatus).HasConversion<int>();
        builder.Property(history => history.ResultingStatus).HasConversion<int>();
        builder.Property(history => history.IdempotencyKey).HasMaxLength(128);
        builder.Property(history => history.Reason).HasMaxLength(1000);
        builder.Property(history => history.Notes).HasMaxLength(2000);
        builder.HasOne<Campaign>().WithMany().HasForeignKey(history => history.CampaignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediBridgeIdentityUser>().WithMany().HasForeignKey(history => history.AdminUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CampaignReviewHistory>().WithMany().HasForeignKey(history => history.CorrectsHistoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(history => new { history.CampaignId, history.CreatedAtUtc });
        builder.HasIndex(history => new { history.CampaignId, history.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");
    }
}

public sealed class CampaignSubmissionAttemptConfiguration : IEntityTypeConfiguration<CampaignSubmissionAttempt>
{
    public void Configure(EntityTypeBuilder<CampaignSubmissionAttempt> builder)
    {
        builder.ToTable("CampaignSubmissionAttempts");
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(attempt => attempt.EstimatedCost).HasPrecision(18, 2);
        builder.Property(attempt => attempt.Currency).HasMaxLength(3).IsRequired();
        builder.HasOne<Campaign>()
            .WithMany()
            .HasForeignKey(attempt => attempt.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(attempt => new { attempt.CampaignId, attempt.IdempotencyKey }).IsUnique();
        builder.HasIndex(attempt => new { attempt.CampaignId, attempt.SubmittedAtUtc, attempt.Id });
    }
}
