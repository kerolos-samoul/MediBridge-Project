using MediBridge.Core.Entities.Identity;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediBridge.Repository.Configurations.Identity;

public sealed class RefreshCredentialConfiguration : IEntityTypeConfiguration<RefreshCredential>
{
    public void Configure(EntityTypeBuilder<RefreshCredential> builder)
    {
        builder.ToTable("RefreshCredentials");
        builder.HasKey(token => token.Id);
        builder.Ignore(token => token.User);
        builder.Property(token => token.TokenHash).HasMaxLength(256).IsRequired();
        builder.Property(token => token.FamilyId).HasMaxLength(64).IsRequired();
        builder.Property(token => token.RevocationReason).HasMaxLength(80);
        builder.Property(token => token.ReplacedByTokenHash).HasMaxLength(256);
        builder.HasOne<MediBridgeIdentityUser>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.FamilyId });
    }
}

public sealed class PasswordResetFlowConfiguration : IEntityTypeConfiguration<PasswordResetFlow>
{
    public void Configure(EntityTypeBuilder<PasswordResetFlow> builder)
    {
        builder.ToTable("PasswordResetFlows");
        builder.HasKey(flow => flow.Id);
        builder.Ignore(flow => flow.User);
        builder.Property(flow => flow.TokenHash).HasMaxLength(256).IsRequired();
        builder.Property(flow => flow.RequestCorrelationId).HasMaxLength(128).IsRequired();
        builder.HasOne<MediBridgeIdentityUser>()
            .WithMany()
            .HasForeignKey(flow => flow.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(flow => flow.TokenHash).IsUnique();
    }
}

public sealed class ContactVerificationFlowConfiguration : IEntityTypeConfiguration<ContactVerificationFlow>
{
    public void Configure(EntityTypeBuilder<ContactVerificationFlow> builder)
    {
        builder.ToTable("ContactVerificationFlows");
        builder.HasKey(flow => flow.Id);
        builder.Ignore(flow => flow.User);
        builder.Property(flow => flow.Channel).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(flow => flow.DestinationHash).HasMaxLength(256).IsRequired();
        builder.Property(flow => flow.TokenHash).HasMaxLength(256).IsRequired();
        builder.HasOne<MediBridgeIdentityUser>()
            .WithMany()
            .HasForeignKey(flow => flow.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(flow => flow.TokenHash).IsUnique();
    }
}

public sealed class AdminAccountDecisionConfiguration : IEntityTypeConfiguration<AdminAccountDecision>
{
    public void Configure(EntityTypeBuilder<AdminAccountDecision> builder)
    {
        builder.ToTable("AdminAccountDecisions");
        builder.HasKey(decision => decision.Id);
        builder.Ignore(decision => decision.AdminUser);
        builder.Ignore(decision => decision.TargetUser);
        builder.Property(decision => decision.Decision).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(decision => decision.ResultingAccountStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(decision => decision.Reason).HasMaxLength(1000);
        builder.Property(decision => decision.Notes).HasMaxLength(2000);
        builder.HasOne<MediBridgeIdentityUser>()
            .WithMany()
            .HasForeignKey(decision => decision.AdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediBridgeIdentityUser>()
            .WithMany()
            .HasForeignKey(decision => decision.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(decision => decision.TargetUserId);
    }
}

public sealed class AccountResubmissionConfiguration : IEntityTypeConfiguration<AccountResubmission>
{
    public void Configure(EntityTypeBuilder<AccountResubmission> builder)
    {
        builder.ToTable("AccountResubmissions");
        builder.HasKey(resubmission => resubmission.Id);
        builder.Ignore(resubmission => resubmission.User);
        builder.Property(resubmission => resubmission.UpdatedProfileFields).HasMaxLength(4000).IsRequired();
        builder.Property(resubmission => resubmission.UpdatedVerificationMetadata).HasMaxLength(4000).IsRequired();
        builder.HasOne<MediBridgeIdentityUser>()
            .WithMany()
            .HasForeignKey(resubmission => resubmission.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(resubmission => resubmission.UserId);
    }
}

public sealed class AccountResubmissionTokenConfiguration : IEntityTypeConfiguration<AccountResubmissionToken>
{
    public void Configure(EntityTypeBuilder<AccountResubmissionToken> builder)
    {
        builder.ToTable("AccountResubmissionTokens");
        builder.HasKey(token => token.Id);
        builder.Ignore(token => token.User);
        builder.Property(token => token.TokenHash).HasMaxLength(256).IsRequired();
        builder.Property(token => token.CreatedByAdminDecisionId).HasMaxLength(450).IsRequired();
        builder.HasOne<MediBridgeIdentityUser>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdminAccountDecision>()
            .WithMany()
            .HasForeignKey(token => token.CreatedByAdminDecisionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.UserId);
    }
}

public sealed class AuthenticationAuditEventConfiguration : IEntityTypeConfiguration<AuthenticationAuditEvent>
{
    public void Configure(EntityTypeBuilder<AuthenticationAuditEvent> builder)
    {
        builder.ToTable("AuthenticationAuditEvents");
        builder.HasKey(auditEvent => auditEvent.Id);
        builder.Property(auditEvent => auditEvent.EventType).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(auditEvent => auditEvent.Role).HasConversion<string>().HasMaxLength(32);
        builder.Property(auditEvent => auditEvent.Outcome).HasMaxLength(80).IsRequired();
        builder.Property(auditEvent => auditEvent.Reason).HasMaxLength(1000);
        builder.Property(auditEvent => auditEvent.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasOne<MediBridgeIdentityUser>()
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediBridgeIdentityUser>()
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(auditEvent => auditEvent.TargetUserId);
        builder.HasIndex(auditEvent => auditEvent.CreatedAtUtc);
    }
}
