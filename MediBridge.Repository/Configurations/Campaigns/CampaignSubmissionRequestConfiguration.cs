using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Campaigns;

public sealed class CampaignSubmissionRequestConfiguration : IEntityTypeConfiguration<CampaignSubmissionRequest>
{
    public void Configure(EntityTypeBuilder<CampaignSubmissionRequest> builder)
    {
        builder.ToTable("CampaignSubmissionRequests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.CompanyId).HasMaxLength(450).IsRequired();
        builder.Property(request => request.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(request => request.CampaignId).HasMaxLength(450);
        builder.Property(request => request.RequestHash).HasMaxLength(128);
        builder.Property(request => request.Status).HasConversion<int>();
        builder.HasOne<CompanyProfile>().WithMany().HasForeignKey(request => request.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Campaign>().WithMany().HasForeignKey(request => request.CampaignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(request => new { request.CompanyId, request.IdempotencyKey }).IsUnique();
    }
}
