using MediBridge.Core.Entities.Files;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Files;

public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("StoredFiles");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.OwnerType).HasConversion<int>();
        builder.Property(file => file.Purpose).HasConversion<int>();
        builder.Property(file => file.OriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(file => file.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(file => file.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(file => file.StorageResourceType).HasMaxLength(40).IsRequired();
        builder.Property(file => file.StorageState).HasConversion<int>();
        builder.Property(file => file.SupersededByFileId).HasMaxLength(450);
        builder.Property(file => file.Visibility).HasConversion<int>();
        builder.Property(file => file.ReviewStatus).HasConversion<int>();
        builder.Property(file => file.ReviewReason).HasMaxLength(1000);
        builder.HasOne<MediBridgeIdentityUser>().WithMany().HasForeignKey(file => file.ReviewedByAdminId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(file => new { file.OwnerType, file.OwnerId, file.Purpose });
        builder.HasIndex(file => new { file.OwnerType, file.OwnerId, file.Purpose, file.StorageState });
        builder.HasIndex(file => new { file.ReviewStatus, file.CreatedAtUtc });
        builder.HasIndex(file => file.SupersededByFileId);
    }
}
