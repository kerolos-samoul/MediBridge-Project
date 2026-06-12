using MediBridge.Core.Entities.Files;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Files;

public sealed class FileAccessGrantAuditConfiguration : IEntityTypeConfiguration<FileAccessGrantAudit>
{
    public void Configure(EntityTypeBuilder<FileAccessGrantAudit> builder)
    {
        builder.ToTable("FileAccessGrantAudits");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.RequesterRole).HasMaxLength(80).IsRequired();
        builder.Property(audit => audit.Outcome).HasConversion<int>();
        builder.Property(audit => audit.Reason).HasMaxLength(1000);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(audit => audit.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediBridgeIdentityUser>().WithMany().HasForeignKey(audit => audit.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(audit => new { audit.StoredFileId, audit.CreatedAtUtc });
        builder.HasIndex(audit => new { audit.RequestedByUserId, audit.CreatedAtUtc });
    }
}
