using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
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
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignTarget> CampaignTargets => Set<CampaignTarget>();
    public DbSet<CampaignSubmissionRequest> CampaignSubmissionRequests => Set<CampaignSubmissionRequest>();
    public DbSet<CampaignSubmissionAttempt> CampaignSubmissionAttempts => Set<CampaignSubmissionAttempt>();
    public DbSet<CampaignReviewHistory> CampaignReviewHistories => Set<CampaignReviewHistory>();
    public DbSet<DoctorMessageQueue> DoctorMessageQueues => Set<DoctorMessageQueue>();
    public DbSet<DoctorAdDelivery> DoctorAdDeliveries => Set<DoctorAdDelivery>();
    public DbSet<DeliveryJobRun> DeliveryJobRuns => Set<DeliveryJobRun>();
    public DbSet<DeliveryRecoveryDispatch> DeliveryRecoveryDispatches => Set<DeliveryRecoveryDispatch>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<WalletLedgerEntry> WalletLedgerEntries => Set<WalletLedgerEntry>();
    public DbSet<MockPaymentTransaction> MockPaymentTransactions => Set<MockPaymentTransaction>();
    public DbSet<WithdrawalRequest> WithdrawalRequests => Set<WithdrawalRequest>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<FileReview> FileReviews => Set<FileReview>();
    public DbSet<FileAccessGrantAudit> FileAccessGrantAudits => Set<FileAccessGrantAudit>();
    public DbSet<DoctorPriceHistory> DoctorPriceHistories => Set<DoctorPriceHistory>();
    public DbSet<PlatformFeePolicyHistory> PlatformFeePolicyHistories => Set<PlatformFeePolicyHistory>();
    public DbSet<ActivityScoreHistory> ActivityScoreHistories => Set<ActivityScoreHistory>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(MediBridgeDbContext).Assembly);
    }
}
