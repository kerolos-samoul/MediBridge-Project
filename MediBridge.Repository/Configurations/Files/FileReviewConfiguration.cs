using MediBridge.Core.Entities.Files;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Files;

public sealed class FileReviewConfiguration : IEntityTypeConfiguration<FileReview>
{
    public void Configure(EntityTypeBuilder<FileReview> builder)
    {
        builder.ToTable("FileReviews");
        builder.HasKey(review => review.Id);
        builder.Property(review => review.Decision).HasConversion<int>();
        builder.Property(review => review.Reason).HasMaxLength(1000);
        builder.Property(review => review.Notes).HasMaxLength(2000);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(review => review.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediBridgeIdentityUser>().WithMany().HasForeignKey(review => review.AdminUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FileReview>().WithMany().HasForeignKey(review => review.CorrectsReviewId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(review => new { review.StoredFileId, review.CreatedAtUtc });
        builder.HasIndex(review => review.CorrectsReviewId);
    }
}
