using FluentValidation;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.DTOs.Auth;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class AdminAccountService : IAdminAccountService
{
    private static readonly TimeSpan ResubmissionTokenLifetime = TimeSpan.FromDays(7);

    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IAuthTokenService authTokenService;
    private readonly IValidator<AdminAccountDecisionRequestDto> decisionValidator;

    public AdminAccountService(
        IIdentityUnitOfWork identityUnitOfWork,
        IAuthTokenService authTokenService,
        IValidator<AdminAccountDecisionRequestDto> decisionValidator)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.authTokenService = authTokenService;
        this.decisionValidator = decisionValidator;
    }

    public async Task<PendingAccountPageDto> ListPendingAccountsAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var safePageNumber = Math.Max(pageNumber, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var users = await identityUnitOfWork.Users.ListByStatusAsync(AccountStatus.Pending, safePageNumber, safePageSize, cancellationToken);
        var totalCount = await identityUnitOfWork.Users.CountByStatusAsync(AccountStatus.Pending, cancellationToken);
        var items = new List<PendingAccountDto>(users.Count);

        foreach (var user in users)
        {
            items.Add(new PendingAccountDto
            {
                UserId = user.Id,
                Role = user.Role,
                AccountStatus = user.AccountStatus,
                SubmittedAtUtc = user.CreatedAtUtc,
                VerificationMetadata = await GetVerificationMetadataAsync(user.Id, user.Role, cancellationToken)
            });
        }

        return new PendingAccountPageDto
        {
            Items = items,
            PageNumber = safePageNumber,
            PageSize = safePageSize,
            TotalCount = totalCount
        };
    }

    public async Task<AccountDecisionResultDto> ApplyDecisionAsync(string adminUserId, string targetUserId, AdminAccountDecisionRequestDto request, CancellationToken cancellationToken = default)
    {
        await decisionValidator.ValidateAndThrowAsync(request, cancellationToken);

        return await identityUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var adminUser = await identityUnitOfWork.Users.FindByIdAsync(adminUserId, transactionCancellationToken)
                ?? throw new KeyNotFoundException("Admin account was not found.");

            if (adminUser.Role != UserRole.Admin || adminUser.AccountStatus != AccountStatus.Approved || adminUser.IsDeleted)
            {
                throw new UnauthorizedAccessException("Admin role is required.");
            }

            var targetUser = await identityUnitOfWork.Users.FindByIdAsync(targetUserId, transactionCancellationToken)
                ?? throw new KeyNotFoundException("Target account was not found.");

            var now = DateTime.UtcNow;
            ValidateDecisionTransition(targetUser, request.Decision);
            var resultingStatus = MapDecisionToStatus(request.Decision);
            var decision = new AdminAccountDecision
            {
                AdminUserId = adminUser.Id,
                TargetUserId = targetUser.Id,
                Decision = request.Decision,
                ResultingAccountStatus = resultingStatus,
                Reason = request.Reason?.Trim(),
                Notes = request.Notes?.Trim(),
                CreatedAtUtc = now
            };

            await identityUnitOfWork.AdminAccountDecisions.AddAsync(decision, transactionCancellationToken);

            string? plaintextToken = null;
            if (request.Decision == AdminAccountDecisionType.Reject)
            {
                var (createdPlaintextToken, tokenHash) = authTokenService.CreateOneTimeToken();
                plaintextToken = createdPlaintextToken;
                await identityUnitOfWork.AccountResubmissionTokens.AddAsync(new AccountResubmissionToken
                {
                    UserId = targetUser.Id,
                    TokenHash = tokenHash,
                    ExpiresAtUtc = now.Add(ResubmissionTokenLifetime),
                    CreatedAtUtc = now,
                    CreatedByAdminDecisionId = decision.Id
                }, transactionCancellationToken);
            }

            targetUser.AccountStatus = resultingStatus;
            targetUser.ApprovedAtUtc = resultingStatus == AccountStatus.Approved ? now : null;
            targetUser.LastStatusChangedAtUtc = now;
            await identityUnitOfWork.Users.UpdateAsync(targetUser, transactionCancellationToken);

            if (request.Decision is AdminAccountDecisionType.Suspend or AdminAccountDecisionType.Inactivate)
            {
                await identityUnitOfWork.RefreshCredentials.RevokeByUserAsync(
                    targetUser.Id,
                    request.Decision == AdminAccountDecisionType.Suspend ? "Suspended" : "Inactive",
                    transactionCancellationToken);
            }

            await identityUnitOfWork.AuthenticationAuditEvents.AddAsync(new AuthenticationAuditEvent
            {
                EventType = AuthAuditEventType.AdminDecision,
                ActorUserId = adminUser.Id,
                TargetUserId = targetUser.Id,
                Role = adminUser.Role,
                Outcome = request.Decision.ToString(),
                Reason = request.Reason?.Trim(),
                CorrelationId = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = now
            }, transactionCancellationToken);

            return new AccountDecisionResultDto
            {
                UserId = targetUser.Id,
                ResultingAccountStatus = resultingStatus,
                ResubmissionToken = plaintextToken
            };
        }, cancellationToken);
    }

    private async Task<VerificationMetadataDto> GetVerificationMetadataAsync(string userId, UserRole role, CancellationToken cancellationToken)
    {
        if (role == UserRole.Doctor)
        {
            var profile = await identityUnitOfWork.Profiles.FindDoctorProfileByUserIdAsync(userId, cancellationToken);
            return profile is null
                ? new VerificationMetadataDto()
                : new VerificationMetadataDto
                {
                    DocumentType = profile.VerificationDocumentType,
                    OriginalFileName = profile.VerificationOriginalFileName,
                    ContentType = profile.VerificationContentType,
                    SizeBytes = profile.VerificationSizeBytes,
                    Reference = profile.VerificationReference
                };
        }

        if (role == UserRole.Company)
        {
            var profile = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(userId, cancellationToken);
            return profile is null
                ? new VerificationMetadataDto()
                : new VerificationMetadataDto
                {
                    DocumentType = profile.VerificationDocumentType,
                    OriginalFileName = profile.VerificationOriginalFileName,
                    ContentType = profile.VerificationContentType,
                    SizeBytes = profile.VerificationSizeBytes,
                    Reference = profile.VerificationReference
                };
        }

        return new VerificationMetadataDto();
    }

    private static AccountStatus MapDecisionToStatus(AdminAccountDecisionType decision)
    {
        return decision switch
        {
            AdminAccountDecisionType.Approve => AccountStatus.Approved,
            AdminAccountDecisionType.Reject => AccountStatus.Rejected,
            AdminAccountDecisionType.Suspend => AccountStatus.Suspended,
            AdminAccountDecisionType.Inactivate => AccountStatus.Inactive,
            AdminAccountDecisionType.Reactivate => AccountStatus.Approved,
            _ => throw new ValidationException("Validation failed.")
        };
    }

    private static void ValidateDecisionTransition(ApplicationUser targetUser, AdminAccountDecisionType decision)
    {
        if (targetUser.IsDeleted)
        {
            throw new ValidationException("Validation failed.");
        }

        var isAllowed = (targetUser.AccountStatus, decision) switch
        {
            (AccountStatus.Pending, AdminAccountDecisionType.Approve) => true,
            (AccountStatus.Pending, AdminAccountDecisionType.Reject) => true,
            (AccountStatus.Approved, AdminAccountDecisionType.Suspend) => true,
            (AccountStatus.Approved, AdminAccountDecisionType.Inactivate) => true,
            (AccountStatus.Suspended, AdminAccountDecisionType.Reactivate) => true,
            (AccountStatus.Inactive, AdminAccountDecisionType.Reactivate) => true,
            _ => false
        };

        if (!isAllowed)
        {
            throw new ValidationException("Validation failed.");
        }
    }
}
