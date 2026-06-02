using MediBridge.Core.Entities.Profiles;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Identity;

public sealed class DoctorProfileConfiguration : IEntityTypeConfiguration<DoctorProfile>
{
    public void Configure(EntityTypeBuilder<DoctorProfile> builder)
    {
        builder.ToTable("DoctorProfiles");
        builder.HasKey(profile => profile.Id);
        builder.HasQueryFilter(profile => !profile.IsDeleted);
        builder.Ignore(profile => profile.User);
        builder.Property(profile => profile.Specialization).HasMaxLength(160).IsRequired();
        builder.Property(profile => profile.Location).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.VerificationDocumentType).HasMaxLength(80).IsRequired();
        builder.Property(profile => profile.VerificationOriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(profile => profile.VerificationContentType).HasMaxLength(120).IsRequired();
        builder.Property(profile => profile.VerificationReference).HasMaxLength(500).IsRequired();
        builder.Property(profile => profile.ActivityScore).HasPrecision(5, 2);
        builder.Property(profile => profile.PricePerMessage).HasPrecision(18, 2);
        builder.Property(profile => profile.Status).HasConversion<int>();
        builder.HasOne<MediBridgeIdentityUser>()
            .WithOne()
            .HasForeignKey<DoctorProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(profile => profile.UserId).IsUnique();
    }
}

public sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.ToTable("CompanyProfiles");
        builder.HasKey(profile => profile.Id);
        builder.HasQueryFilter(profile => !profile.IsDeleted);
        builder.Ignore(profile => profile.User);
        builder.Property(profile => profile.CompanyName).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.LicenseNumber).HasMaxLength(120).IsRequired();
        builder.Property(profile => profile.ContactName).HasMaxLength(160).IsRequired();
        builder.Property(profile => profile.VerificationDocumentType).HasMaxLength(80).IsRequired();
        builder.Property(profile => profile.VerificationOriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(profile => profile.VerificationContentType).HasMaxLength(120).IsRequired();
        builder.Property(profile => profile.VerificationReference).HasMaxLength(500).IsRequired();
        builder.HasOne<MediBridgeIdentityUser>()
            .WithOne()
            .HasForeignKey<CompanyProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(profile => profile.UserId).IsUnique();
        builder.HasIndex(profile => profile.LicenseNumber)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
