using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Identity;

public sealed class ApplicationUserRepository : IApplicationUserRepository
{
    private readonly MediBridgeDbContext context;
    private readonly UserManager<MediBridgeIdentityUser> userManager;

    public ApplicationUserRepository(MediBridgeDbContext context, UserManager<MediBridgeIdentityUser> userManager)
    {
        this.context = context;
        this.userManager = userManager;
    }

    public async Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await context.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        return user?.ToDomain();
    }

    public async Task<ApplicationUser?> FindByIdForUpdateAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await context.Users
            .FromSqlInterpolated($"""
                SELECT *
                FROM [Users] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {userId}
                """)
            .SingleOrDefaultAsync(cancellationToken);
        return user?.ToDomain();
    }

    public async Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = await context.Users.FirstOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail || candidate.Email == email,
            cancellationToken);

        return user?.ToDomain();
    }

    public async Task<ApplicationUser?> FindByContactAsync(string contact, CancellationToken cancellationToken = default)
    {
        var normalizedContact = contact.Trim();
        var normalizedEmail = NormalizeEmail(normalizedContact);
        var user = await context.Users.FirstOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail || candidate.Email == normalizedContact || candidate.PhoneNumber == normalizedContact,
            cancellationToken);

        return user?.ToDomain();
    }

    public async Task<bool> ValidatePasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var identityUser = await context.Users.FirstOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail || candidate.Email == email,
            cancellationToken);

        return identityUser is not null && await userManager.CheckPasswordAsync(identityUser, password);
    }

    public async Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var identityUser = await context.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{userId}' was not found.");

        identityUser.PasswordHash = userManager.PasswordHasher.HashPassword(identityUser, newPassword);
        identityUser.SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public async Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        await context.Users.AddAsync(MediBridgeIdentityUser.FromDomain(user), cancellationToken);
    }

    public async Task AddAsync(ApplicationUser user, string password, CancellationToken cancellationToken = default)
    {
        var identityUser = MediBridgeIdentityUser.FromDomain(user);
        var result = await userManager.CreateAsync(identityUser, password);

        if (result.Succeeded is false)
        {
            if (result.Errors.Any(IsDuplicateIdentityError))
            {
                throw new IdentityRecordConflictException("Duplicate email, phone, or license.");
            }

            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(error => error.Description)));
        }

        user.Id = identityUser.Id;
    }

    public async Task UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var existingUser = await context.Users.FirstOrDefaultAsync(candidate => candidate.Id == user.Id, cancellationToken);
        if (existingUser is null)
        {
            throw new InvalidOperationException($"User '{user.Id}' was not found.");
        }

        existingUser.Email = user.Email;
        existingUser.UserName = user.Id;
        existingUser.NormalizedEmail = NormalizeEmail(user.Email);
        existingUser.NormalizedUserName = NormalizeEmail(user.Id);
        existingUser.PhoneNumber = user.PhoneNumber;
        existingUser.Role = user.Role;
        existingUser.AccountStatus = user.AccountStatus;
        existingUser.EmailVerified = user.EmailVerified;
        existingUser.PhoneVerified = user.PhoneVerified;
        existingUser.CreatedAtUtc = user.CreatedAtUtc;
        existingUser.ApprovedAtUtc = user.ApprovedAtUtc;
        existingUser.LastStatusChangedAtUtc = user.LastStatusChangedAtUtc;
        existingUser.IsDeleted = user.IsDeleted;
        existingUser.DeletedAtUtc = user.DeletedAtUtc;
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        return context.Users.AnyAsync(candidate => !candidate.IsDeleted && candidate.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    public Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        return context.Users.AnyAsync(candidate => !candidate.IsDeleted && candidate.PhoneNumber == phoneNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<ApplicationUser>> ListByStatusAsync(AccountStatus status, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var safePageNumber = Math.Max(pageNumber, 1);
        var safePageSize = Math.Max(pageSize, 1);

        return await context.Users
            .Where(candidate => !candidate.IsDeleted && candidate.AccountStatus == status)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .Skip((safePageNumber - 1) * safePageSize)
            .Take(safePageSize)
            .Select(candidate => candidate.ToDomain())
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountByStatusAsync(AccountStatus status, CancellationToken cancellationToken = default)
    {
        return context.Users.CountAsync(candidate => !candidate.IsDeleted && candidate.AccountStatus == status, cancellationToken);
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }

    private static bool IsDuplicateIdentityError(IdentityError error)
    {
        return error.Code.StartsWith("Duplicate", StringComparison.OrdinalIgnoreCase)
            || error.Description.Contains("already", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ProfileRepository : IProfileRepository
{
    private readonly MediBridgeDbContext context;

    public ProfileRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddDoctorProfileAsync(DoctorProfile profile, CancellationToken cancellationToken = default)
    {
        await context.DoctorProfiles.AddAsync(profile, cancellationToken);
    }

    public async Task AddCompanyProfileAsync(CompanyProfile profile, CancellationToken cancellationToken = default)
    {
        await context.CompanyProfiles.AddAsync(profile, cancellationToken);
    }

    public Task<DoctorProfile?> FindDoctorProfileByIdAsync(string doctorId, CancellationToken cancellationToken = default)
    {
        return context.DoctorProfiles.FirstOrDefaultAsync(profile => profile.Id == doctorId, cancellationToken);
    }

    public Task<DoctorProfile?> FindDoctorProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return context.DoctorProfiles.FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorProfile>> ListDoctorProfilesAsync(CancellationToken cancellationToken = default)
    {
        return await context.DoctorProfiles
            .OrderBy(profile => profile.CreatedAtUtc)
            .ThenBy(profile => profile.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<CompanyProfile?> FindCompanyProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return context.CompanyProfiles.FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
    }

    public Task<CompanyProfile?> FindCompanyProfileByIdAsync(string companyId, CancellationToken cancellationToken = default)
    {
        return context.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.Id == companyId, cancellationToken);
    }

    public Task<CompanyProfile?> FindCompanyProfileByIdForUpdateAsync(
        string companyId,
        CancellationToken cancellationToken = default)
    {
        return context.CompanyProfiles
            .FromSqlInterpolated($"""
                SELECT *
                FROM [CompanyProfiles] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {companyId}
                    AND [IsDeleted] = CAST(0 AS bit)
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> CompanyLicenseExistsAsync(string licenseNumber, CancellationToken cancellationToken = default)
    {
        return context.CompanyProfiles.AnyAsync(profile => profile.LicenseNumber == licenseNumber, cancellationToken);
    }
}

public sealed class RefreshCredentialRepository : IRefreshCredentialRepository
{
    private readonly MediBridgeDbContext context;

    public RefreshCredentialRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAsync(RefreshCredential credential, CancellationToken cancellationToken = default)
    {
        await context.RefreshCredentials.AddAsync(credential, cancellationToken);
    }

    public Task<RefreshCredential?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return context.RefreshCredentials.FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
    }

    public Task<RefreshCredential?> FindByTokenHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return context.RefreshCredentials
            .FromSqlInterpolated($"SELECT * FROM [RefreshCredentials] WITH (UPDLOCK, ROWLOCK) WHERE [TokenHash] = {tokenHash}")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RefreshCredential>> FindActiveFamilyCredentialsAsync(string familyId, CancellationToken cancellationToken = default)
    {
        return await context.RefreshCredentials
            .Where(token => token.FamilyId == familyId && token.RevokedAtUtc == null && token.ExpiresAtUtc >= DateTime.UtcNow)
            .OrderByDescending(token => token.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeByUserAsync(string userId, string reason, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var tokens = await context.RefreshCredentials
            .Where(token => token.UserId == userId && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = now;
            token.RevocationReason = reason;
        }
    }

    public async Task RevokeFamilyAsync(string familyId, string reason, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var tokens = await context.RefreshCredentials
            .Where(token => token.FamilyId == familyId && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = now;
            token.RevocationReason = reason;
        }
    }
}

public sealed class PasswordResetFlowRepository : IPasswordResetFlowRepository
{
    private readonly MediBridgeDbContext context;

    public PasswordResetFlowRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAsync(PasswordResetFlow flow, CancellationToken cancellationToken = default)
    {
        await context.PasswordResetFlows.AddAsync(flow, cancellationToken);
    }

    public Task<PasswordResetFlow?> FindUnconsumedByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return context.PasswordResetFlows
            .FromSqlInterpolated($"""
                SELECT *
                FROM [PasswordResetFlows] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TokenHash] = {tokenHash}
                    AND [ConsumedAtUtc] IS NULL
                    AND [ExpiresAtUtc] >= {now}
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task MarkConsumedAsync(PasswordResetFlow flow, DateTime consumedAtUtc, CancellationToken cancellationToken = default)
    {
        flow.ConsumedAtUtc = consumedAtUtc;
        return Task.CompletedTask;
    }
}

public sealed class ContactVerificationFlowRepository : IContactVerificationFlowRepository
{
    private readonly MediBridgeDbContext context;

    public ContactVerificationFlowRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAsync(ContactVerificationFlow flow, CancellationToken cancellationToken = default)
    {
        await context.ContactVerificationFlows.AddAsync(flow, cancellationToken);
    }

    public Task<ContactVerificationFlow?> FindUnconsumedByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return context.ContactVerificationFlows
            .FromSqlInterpolated($"""
                SELECT *
                FROM [ContactVerificationFlows] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TokenHash] = {tokenHash}
                    AND [ConsumedAtUtc] IS NULL
                    AND [ExpiresAtUtc] >= {now}
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<ContactVerificationFlow?> FindLatestActiveForUpdateAsync(
        string userId,
        ContactVerificationChannel channel,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var channelValue = channel.ToString();
        return context.ContactVerificationFlows
            .FromSqlInterpolated($"""
                SELECT TOP(1) *
                FROM [ContactVerificationFlows] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [UserId] = {userId}
                    AND [Channel] = {channelValue}
                    AND [ConsumedAtUtc] IS NULL
                    AND [SupersededAtUtc] IS NULL
                    AND [MaxAttemptsReachedAtUtc] IS NULL
                    AND [ExpiresAtUtc] > {nowUtc}
                ORDER BY [CreatedAtUtc] DESC
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SupersedeActiveAsync(
        string userId,
        ContactVerificationChannel channel,
        DateTime supersededAtUtc,
        CancellationToken cancellationToken = default)
    {
        var flows = await context.ContactVerificationFlows
            .Where(flow => flow.UserId == userId
                && flow.Channel == channel
                && flow.ConsumedAtUtc == null
                && flow.SupersededAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var flow in flows)
        {
            flow.SupersededAtUtc = supersededAtUtc;
        }
    }

    public Task RecordFailedAttemptAsync(ContactVerificationFlow flow, DateTime attemptedAtUtc, CancellationToken cancellationToken = default)
    {
        flow.FailedAttemptCount++;
        if (flow.FailedAttemptCount >= flow.MaxAttemptCount)
        {
            flow.MaxAttemptsReachedAtUtc = attemptedAtUtc;
        }

        return Task.CompletedTask;
    }

    public Task MarkSentAsync(ContactVerificationFlow flow, DateTime sentAtUtc, CancellationToken cancellationToken = default)
    {
        flow.LastSentAtUtc = sentAtUtc;
        flow.ResendCount++;
        flow.ResendWindowStartedAtUtc ??= sentAtUtc;
        return Task.CompletedTask;
    }

    public Task MarkConsumedAsync(ContactVerificationFlow flow, DateTime consumedAtUtc, CancellationToken cancellationToken = default)
    {
        flow.ConsumedAtUtc = consumedAtUtc;
        return Task.CompletedTask;
    }
}

public sealed class AdminAccountDecisionRepository : IAdminAccountDecisionRepository
{
    private readonly MediBridgeDbContext context;

    public AdminAccountDecisionRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAsync(AdminAccountDecision decision, CancellationToken cancellationToken = default)
    {
        await context.AdminAccountDecisions.AddAsync(decision, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAccountDecision>> ListByTargetUserIdAsync(string targetUserId, CancellationToken cancellationToken = default)
    {
        return await context.AdminAccountDecisions
            .Where(decision => decision.TargetUserId == targetUserId)
            .OrderByDescending(decision => decision.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}

public sealed class AccountResubmissionRepository : IAccountResubmissionRepository
{
    private readonly MediBridgeDbContext context;

    public AccountResubmissionRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAsync(AccountResubmission resubmission, CancellationToken cancellationToken = default)
    {
        await context.AccountResubmissions.AddAsync(resubmission, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountResubmission>> ListByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await context.AccountResubmissions
            .Where(resubmission => resubmission.UserId == userId)
            .OrderByDescending(resubmission => resubmission.SubmittedAtUtc)
            .ToListAsync(cancellationToken);
    }
}

public sealed class AccountResubmissionTokenRepository : IAccountResubmissionTokenRepository
{
    private readonly MediBridgeDbContext context;

    public AccountResubmissionTokenRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAsync(AccountResubmissionToken token, CancellationToken cancellationToken = default)
    {
        await context.AccountResubmissionTokens.AddAsync(token, cancellationToken);
    }

    public Task<AccountResubmissionToken?> FindUnconsumedByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return context.AccountResubmissionTokens.FirstOrDefaultAsync(token => token.TokenHash == tokenHash && token.ConsumedAtUtc == null && token.ExpiresAtUtc >= now, cancellationToken);
    }

    public Task<AccountResubmissionToken?> FindUnconsumedByTokenHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return context.AccountResubmissionTokens
            .FromSqlInterpolated($"""
                SELECT *
                FROM [AccountResubmissionTokens] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TokenHash] = {tokenHash}
                    AND [ConsumedAtUtc] IS NULL
                    AND [ExpiresAtUtc] >= {now}
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task MarkConsumedAsync(AccountResubmissionToken token, DateTime consumedAtUtc, CancellationToken cancellationToken = default)
    {
        token.ConsumedAtUtc = consumedAtUtc;
        return Task.CompletedTask;
    }
}

public sealed class AuthenticationAuditEventRepository : IAuthenticationAuditEventRepository
{
    private readonly MediBridgeDbContext context;

    public AuthenticationAuditEventRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAsync(AuthenticationAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await context.AuthenticationAuditEvents.AddAsync(auditEvent, cancellationToken);
    }

    public async Task<IReadOnlyList<AuthenticationAuditEvent>> ListByTargetUserIdAsync(string targetUserId, CancellationToken cancellationToken = default)
    {
        return await context.AuthenticationAuditEvents
            .Where(auditEvent => auditEvent.TargetUserId == targetUserId)
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
