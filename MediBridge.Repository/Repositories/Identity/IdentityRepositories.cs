using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
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
        var identityUser = await context.Users
            .FromSqlInterpolated($"""
                SELECT *
                FROM [Users] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {userId}
                """)
            .SingleOrDefaultAsync(cancellationToken);
        return identityUser?.ToDomain();
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

    public Task<DoctorProfile?> FindDoctorProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return context.DoctorProfiles.FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
    }

    public Task<DoctorProfile?> FindDoctorProfileByIdAsync(string doctorId, CancellationToken cancellationToken = default)
    {
        return context.DoctorProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.Id == doctorId && !profile.IsDeleted, cancellationToken);
    }

    public Task<DoctorProfile?> FindDoctorProfileByIdForUpdateAsync(
        string doctorId,
        CancellationToken cancellationToken = default)
    {
        return context.DoctorProfiles
            .FromSqlInterpolated($"""
                SELECT *
                FROM [DoctorProfiles] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {doctorId}
                    AND [IsDeleted] = CAST(0 AS bit)
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<CompanyProfile?> FindCompanyProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return context.CompanyProfiles.FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
    }

    public Task<CompanyProfile?> FindCompanyProfileByIdAsync(string companyId, CancellationToken cancellationToken = default)
    {
        return context.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.Id == companyId && !profile.IsDeleted, cancellationToken);
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

    public async Task<IReadOnlyList<DoctorProfile>> SearchEligibleDoctorsAsync(EligibleDoctorSearchCriteria criteria, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ApplyEligibleDoctorFilters(criteria)
            .OrderByDescending(profile => profile.ActivityScore)
            .ThenBy(profile => profile.PricePerMessage)
            .ThenBy(profile => profile.Id)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Max(take, 1))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorProfile>> ListEligibleDoctorsByIdsAsync(IReadOnlyCollection<string> doctorIds, CancellationToken cancellationToken = default)
    {
        var distinctIds = doctorIds
            .Where(id => string.IsNullOrWhiteSpace(id) is false)
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctIds.Length == 0)
        {
            return Array.Empty<DoctorProfile>();
        }

        return await ApplyEligibleDoctorFilters(new EligibleDoctorSearchCriteria(null, null, null, null, null, null, null))
            .Where(profile => distinctIds.Contains(profile.Id))
            .OrderByDescending(profile => profile.ActivityScore)
            .ThenBy(profile => profile.PricePerMessage)
            .ThenBy(profile => profile.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountEligibleDoctorsAsync(EligibleDoctorSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        return ApplyEligibleDoctorFilters(criteria).CountAsync(cancellationToken);
    }

    public async Task<LockedDoctorDeliveryEligibilityReadModel?> FindDoctorDeliveryEligibilityForUpdateAsync(
        string doctorId,
        CancellationToken cancellationToken = default)
    {
        var profile = await context.DoctorProfiles
            .FromSqlInterpolated($"SELECT * FROM [DoctorProfiles] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {doctorId}")
            .SingleOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var user = await context.Users
            .FromSqlInterpolated($"SELECT * FROM [Users] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {profile.UserId}")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        return new LockedDoctorDeliveryEligibilityReadModel(
            profile.Id,
            profile.UserId,
            user.Role,
            user.AccountStatus,
            user.IsDeleted,
            profile.IsDeleted,
            profile.Status,
            profile.PricePerMessage,
            profile.DailyMessageLimit);
    }

    public async Task<IReadOnlyList<string>> ListApprovedNonDeletedDoctorIdsForActivityScoringAsync(
        string? afterDoctorId,
        int take,
        CancellationToken cancellationToken = default)
    {
        return await ApprovedNonDeletedDoctorQuery(includeSuspended: true)
            .Where(profile => afterDoctorId == null || string.Compare(profile.Id, afterDoctorId) > 0)
            .OrderBy(profile => profile.Id)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(profile => profile.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListApprovedNonDeletedDoctorIdsForWeeklyEnforcementAsync(
        string? afterDoctorId,
        int take,
        CancellationToken cancellationToken = default)
    {
        return await ApprovedNonDeletedDoctorQuery(includeSuspended: true)
            .Where(profile => afterDoctorId == null || string.Compare(profile.Id, afterDoctorId) > 0)
            .OrderBy(profile => profile.Id)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(profile => profile.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<DoctorProfile?> FindDoctorProfileForEnforcementUpdateByIdAsync(string doctorId, CancellationToken cancellationToken = default)
    {
        return context.DoctorProfiles
            .FromSqlInterpolated($"""
                SELECT *
                FROM [DoctorProfiles] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {doctorId}
                    AND [IsDeleted] = CAST(0 AS bit)
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorProfile>> ListExpiredSuspendedDoctorsForUpdateAsync(
        DateTime nowUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        var doctorRole = UserRole.Doctor.ToString();
        var approvedStatus = AccountStatus.Approved.ToString();
        return await context.DoctorProfiles
            .FromSqlInterpolated($"""
                SELECT TOP({Math.Clamp(take, 1, 1000)}) profiles.*
                FROM [DoctorProfiles] AS profiles WITH (UPDLOCK, ROWLOCK, READPAST)
                INNER JOIN [Users] AS users ON users.[Id] = profiles.[UserId]
                WHERE profiles.[IsDeleted] = CAST(0 AS bit)
                    AND profiles.[Status] = {(int)DoctorMarketplaceStatus.Suspended}
                    AND profiles.[SuspendedUntilUtc] IS NOT NULL
                    AND profiles.[SuspendedUntilUtc] <= {nowUtc}
                    AND users.[IsDeleted] = CAST(0 AS bit)
                    AND users.[Role] = {doctorRole}
                    AND users.[AccountStatus] = {approvedStatus}
                ORDER BY profiles.[SuspendedUntilUtc], profiles.[Id]
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ApplyCurrentActivityScoreAsync(
        string doctorId,
        decimal activityScore,
        DateTime calculatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureUtc(calculatedAtUtc, nameof(calculatedAtUtc));
        if (activityScore is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(activityScore), activityScore, "Activity score must be between 0.0 and 100.0.");
        }

        var rounded = Math.Round(activityScore, 1, MidpointRounding.AwayFromZero);
        var rows = await context.DoctorProfiles
            .Where(profile => profile.Id == doctorId && !profile.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(profile => profile.ActivityScore, rounded)
                .SetProperty(profile => profile.UpdatedAtUtc, calculatedAtUtc),
                cancellationToken);
        return rows == 1;
    }

    public Task ApplyEnforcementStateChangesAsync(DoctorProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        context.DoctorProfiles.Update(profile);
        return Task.CompletedTask;
    }

    public async Task<bool> SuspensionOverlapsEgyptWeekAsync(
        string doctorId,
        DateOnly weekStartDateEgypt,
        DateOnly weekEndDateEgypt,
        CancellationToken cancellationToken = default)
    {
        if (weekStartDateEgypt.DayOfWeek != DayOfWeek.Monday || weekEndDateEgypt != weekStartDateEgypt.AddDays(7))
        {
            throw new ArgumentException("Suspension overlap checks require a Monday-to-Monday Cairo week.");
        }

        var weekStartUtc = ConvertCairoDateStartToUtc(weekStartDateEgypt);
        var weekEndUtc = ConvertCairoDateStartToUtc(weekEndDateEgypt);
        return await context.DoctorProfiles
            .AsNoTracking()
            .AnyAsync(profile => profile.Id == doctorId
                && profile.SuspendedAtUtc != null
                && profile.SuspendedUntilUtc != null
                && profile.SuspendedAtUtc < weekEndUtc
                && profile.SuspendedUntilUtc > weekStartUtc,
                cancellationToken);
    }

    private IQueryable<DoctorProfile> ApplyEligibleDoctorFilters(EligibleDoctorSearchCriteria criteria)
    {
        var query =
            from profile in context.DoctorProfiles
            join user in context.Users on profile.UserId equals user.Id
            where !profile.IsDeleted
                  && profile.Status == DoctorMarketplaceStatus.Active
                  && profile.PricePerMessage > 0
                  && profile.DailyMessageLimit > 0
                  && !user.IsDeleted
                  && user.Role == UserRole.Doctor
                  && user.AccountStatus == AccountStatus.Approved
            select profile;

        if (string.IsNullOrWhiteSpace(criteria.Specialization) is false)
        {
            var specialization = criteria.Specialization.Trim();
            query = query.Where(profile => profile.Specialization == specialization);
        }

        if (criteria.MinExperienceYears.HasValue)
        {
            query = query.Where(profile => profile.ExperienceYears >= criteria.MinExperienceYears.Value);
        }

        if (criteria.MaxExperienceYears.HasValue)
        {
            query = query.Where(profile => profile.ExperienceYears <= criteria.MaxExperienceYears.Value);
        }

        if (string.IsNullOrWhiteSpace(criteria.Location) is false)
        {
            var location = criteria.Location.Trim();
            query = query.Where(profile => profile.Location == location);
        }

        if (criteria.MinActivityScore.HasValue)
        {
            query = query.Where(profile => profile.ActivityScore >= criteria.MinActivityScore.Value);
        }

        if (criteria.MinPrice.HasValue)
        {
            query = query.Where(profile => profile.PricePerMessage >= criteria.MinPrice.Value);
        }

        if (criteria.MaxPrice.HasValue)
        {
            query = query.Where(profile => profile.PricePerMessage <= criteria.MaxPrice.Value);
        }

        return query;
    }

    private IQueryable<DoctorProfile> ApprovedNonDeletedDoctorQuery(bool includeSuspended)
    {
        var query =
            from profile in context.DoctorProfiles.AsNoTracking()
            join user in context.Users.AsNoTracking() on profile.UserId equals user.Id
            where !profile.IsDeleted
                && !user.IsDeleted
                && user.Role == UserRole.Doctor
                && user.AccountStatus == AccountStatus.Approved
            select profile;

        return includeSuspended ? query : query.Where(profile => profile.Status != DoctorMarketplaceStatus.Suspended);
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Profile update timestamps must be UTC.", parameterName);
        }
    }

    private static DateTime ConvertCairoDateStartToUtc(DateOnly dateEgypt)
    {
        var local = new DateTime(dateEgypt.Year, dateEgypt.Month, dateEgypt.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, CairoTimeZone.Value);
    }

    private static readonly Lazy<TimeZoneInfo> CairoTimeZone = new(() =>
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId("Africa/Cairo", out var windowsId))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }

            throw;
        }
    });
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
                    AND [SupersededAtUtc] IS NULL
                    AND [MaxAttemptsReachedAtUtc] IS NULL
                    AND [ExpiresAtUtc] >= {now}
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ContactVerificationFlow>> ListUnconsumedByUserDestinationAsync(
        string userId,
        ContactVerificationChannel channel,
        string destinationHash,
        CancellationToken cancellationToken = default)
    {
        return await context.ContactVerificationFlows
            .Where(flow => flow.UserId == userId
                           && flow.Channel == channel
                           && flow.DestinationHash == destinationHash
                           && flow.ConsumedAtUtc == null)
            .OrderByDescending(flow => flow.CreatedAtUtc)
            .ToListAsync(cancellationToken);
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
