using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Pricing;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class AdminPlatformFeePolicyService : IAdminPlatformFeePolicyService
{
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IValidator<SetPlatformFeePolicyRequestDto> validator;

    public AdminPlatformFeePolicyService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IValidator<SetPlatformFeePolicyRequestDto> validator)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.validator = validator;
    }

    public async Task<PlatformFeePolicyDto?> GetCurrentAsync(
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var admin = await identityUnitOfWork.Users.FindByIdAsync(adminUserId, cancellationToken);
        if (admin is not { Role: UserRole.Admin, AccountStatus: AccountStatus.Approved, IsDeleted: false })
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var now = DateTime.UtcNow;
        var policy = await domainUnitOfWork.PolicyHistory.FindSingleEffectivePlatformFeePolicyAsync(now, cancellationToken);
        return policy is null ? null : new PlatformFeePolicyDto(policy.Id, policy.FeePercent, now, null);
    }

    public async Task<PlatformFeePolicyDto> SetAsync(
        string adminUserId,
        SetPlatformFeePolicyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Platform fee policy request body is required."]);
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new Phase5ValidationException(
                "Validation failed.",
                validation.Errors.Select(error => error.ErrorMessage).ToArray());
        }

        return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var admin = await identityUnitOfWork.Users.FindByIdForUpdateAsync(adminUserId, transactionCancellationToken);
            if (admin is not { Role: UserRole.Admin, AccountStatus: AccountStatus.Approved, IsDeleted: false })
            {
                throw new Phase5ForbiddenException("Forbidden.");
            }

            var now = DateTime.UtcNow;
            var previous = await domainUnitOfWork.PolicyHistory.FindSingleEffectivePlatformFeePolicyAsync(
                now,
                transactionCancellationToken);
            var closedCount = await domainUnitOfWork.PolicyHistory.CloseEffectivePlatformFeePoliciesAsync(
                now,
                now,
                transactionCancellationToken);
            var policyId = Guid.NewGuid().ToString("N");
            var feePercent = request.FeePercent!.Value;
            var reason = request.Reason!.Trim();
            await domainUnitOfWork.PolicyHistory.AddPlatformFeePolicyHistoryAsync(
                policyId,
                feePercent,
                now,
                admin.Id,
                reason,
                transactionCancellationToken);
            await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "PlatformFeePolicyUpdated",
                admin.Id,
                admin.Role.ToString(),
                AuditTargetType.Policy,
                policyId,
                AuditOutcome.Success,
                reason,
                correlationId: null,
                JsonSerializer.Serialize(new
                {
                    PreviousPolicyId = previous?.Id,
                    PreviousFeePercent = previous?.FeePercent,
                    NewPolicyId = policyId,
                    NewFeePercent = feePercent,
                    ClosedPolicyCount = closedCount,
                    EffectiveFromUtc = now
                }),
                now,
                transactionCancellationToken);

            return new PlatformFeePolicyDto(policyId, feePercent, now, null);
        }, cancellationToken);
    }
}
