using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Repository.Data.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Data;

public sealed class MediBridgeDbContext : IdentityDbContext<MediBridgeIdentityUser>
{
    public MediBridgeDbContext(DbContextOptions<MediBridgeDbContext> options)
        : base(options)
    {
    }

    public DbSet<DoctorProfile> DoctorProfiles => Set<DoctorProfile>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<RefreshCredential> RefreshCredentials => Set<RefreshCredential>();
    public DbSet<PasswordResetFlow> PasswordResetFlows => Set<PasswordResetFlow>();
    public DbSet<ContactVerificationFlow> ContactVerificationFlows => Set<ContactVerificationFlow>();
    public DbSet<AdminAccountDecision> AdminAccountDecisions => Set<AdminAccountDecision>();
    public DbSet<AccountResubmission> AccountResubmissions => Set<AccountResubmission>();
    public DbSet<AccountResubmissionToken> AccountResubmissionTokens => Set<AccountResubmissionToken>();
    public DbSet<AuthenticationAuditEvent> AuthenticationAuditEvents => Set<AuthenticationAuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(MediBridgeDbContext).Assembly);
    }
}
