using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Identity;

public sealed class MediBridgeIdentityUserConfiguration : IEntityTypeConfiguration<MediBridgeIdentityUser>
{
    public void Configure(EntityTypeBuilder<MediBridgeIdentityUser> builder)
    {
        builder.ToTable("Users");
        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(user => user.AccountStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(user => user.CreatedAtUtc).IsRequired();
        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [NormalizedEmail] IS NOT NULL");
        builder.HasIndex(user => user.PhoneNumber)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [PhoneNumber] IS NOT NULL");
    }
}
