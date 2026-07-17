using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Admin;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class WithdrawalService : IWithdrawalService
{
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IValidator<CreateWithdrawalRequestDto> createValidator;
    private readonly IValidator<AdminWithdrawalDecisionRequestDto> decisionValidator;
    private readonly IValidator<MarkWithdrawalPaidRequestDto> paidValidator;
    private readonly IValidator<MarkWithdrawalFailedRequestDto> failedValidator;

    public WithdrawalService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IValidator<CreateWithdrawalRequestDto> createValidator,
        IValidator<AdminWithdrawalDecisionRequestDto> decisionValidator,
        IValidator<MarkWithdrawalPaidRequestDto> paidValidator,
        IValidator<MarkWithdrawalFailedRequestDto> failedValidator)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.createValidator = createValidator;
        this.decisionValidator = decisionValidator;
        this.paidValidator = paidValidator;
        this.failedValidator = failedValidator;
    }

    public async Task<DoctorWithdrawalDto> CreateWithdrawalAsync(string actorUserId, CreateWithdrawalRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Withdrawal request body is required."]);
        }

        await ValidateOrThrowAsync(createValidator, request, cancellationToken);
        var amount = WithdrawalMoneyRules.EnsureRequestAmount(request.Amount!.Value, WithdrawalMoneyRules.Currency);

        return await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            var (user, doctor) = await RequireApprovedDoctorAsync(actorUserId, forUpdate: true, requireActiveMarketplace: true, transactionCancellationToken);
            var wallet = await domainUnitOfWork.Wallets.GetOrCreateActiveWalletForUpdateAsync(
                Guid.NewGuid().ToString("N"),
                WalletOwnerType.Doctor,
                doctor.Id,
                user.Id,
                DateTime.UtcNow,
                transactionCancellationToken);
            if (wallet.AvailableBalance < amount)
            {
                throw new Phase5ConflictException("Withdrawable settled earnings are insufficient.");
            }

            var now = DateTime.UtcNow;
            var withdrawal = new WithdrawalRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                DoctorId = doctor.Id,
                Amount = amount,
                Status = WithdrawalRequestStatus.Requested,
                RequestedAtUtc = now
            };
            await domainUnitOfWork.WithdrawalRequests.AddAsync(withdrawal, transactionCancellationToken);
            await MoveAvailableToHeldAsync(wallet.Id, withdrawal.Id, amount, now, transactionCancellationToken);
            await AddAuditAsync("WithdrawalRequested", actorUserId, UserRole.Doctor, withdrawal, null, WithdrawalRequestStatus.Requested, "Doctor withdrawal requested.", now, transactionCancellationToken);
            return ToDoctorDto(withdrawal);
        }, cancellationToken);
    }

    public async Task<DoctorWithdrawalPageDto> ListDoctorWithdrawalsAsync(string actorUserId, int? pageNumber, int? pageSize, WithdrawalRequestStatus? status, CancellationToken cancellationToken = default)
    {
        var (_, doctor) = await RequireApprovedDoctorAsync(actorUserId, forUpdate: false, requireActiveMarketplace: false, cancellationToken);
        var pagination = CreatePagination(pageNumber, pageSize);
        var page = await domainUnitOfWork.WithdrawalRequests.ListDoctorOwnedAsync(doctor.Id, status, pagination, cancellationToken);
        return new DoctorWithdrawalPageDto(ToPageMetadata(page), page.Items.Select(ToDoctorDto).ToArray());
    }

    public async Task<AdminWithdrawalPageDto> ListAdminWithdrawalsAsync(
        string adminUserId,
        int? pageNumber,
        int? pageSize,
        WithdrawalRequestStatus? status,
        string? doctorId,
        DateTime? requestedFromUtc,
        DateTime? requestedToUtc,
        DateTime? reviewedFromUtc,
        DateTime? reviewedToUtc,
        decimal? minimumAmount,
        decimal? maximumAmount,
        string? payoutReference,
        CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(adminUserId, forUpdate: false, cancellationToken);
        var pagination = CreatePagination(pageNumber, pageSize);
        if (minimumAmount is < 0m)
        {
            throw new Phase5ValidationException("Validation failed.", ["minimumAmount cannot be negative."]);
        }

        if (maximumAmount is < 0m)
        {
            throw new Phase5ValidationException("Validation failed.", ["maximumAmount cannot be negative."]);
        }

        if (minimumAmount is not null && maximumAmount is not null && minimumAmount > maximumAmount)
        {
            throw new Phase5ValidationException("Validation failed.", ["minimumAmount must be less than or equal to maximumAmount."]);
        }

        var page = await domainUnitOfWork.WithdrawalRequests.ListAdminAsync(
            new AdminWithdrawalFilters(
                status,
                Normalize(doctorId),
                requestedFromUtc,
                requestedToUtc,
                reviewedFromUtc,
                reviewedToUtc,
                minimumAmount,
                maximumAmount,
                Normalize(payoutReference),
                pagination),
            cancellationToken);
        return new AdminWithdrawalPageDto(ToPageMetadata(page), page.Items.Select(ToAdminDto).ToArray());
    }

    public async Task<AdminWithdrawalDto> ApproveAsync(string adminUserId, string withdrawalId, AdminWithdrawalDecisionRequestDto? request, CancellationToken cancellationToken = default)
    {
        request ??= new AdminWithdrawalDecisionRequestDto(null, null);
        await ValidateOrThrowAsync(decisionValidator, request, cancellationToken);
        return await TransitionAsync(adminUserId, withdrawalId, WithdrawalRequestStatus.Approved, request.Note, request.Reason, null, cancellationToken);
    }

    public async Task<AdminWithdrawalDto> RejectAsync(string adminUserId, string withdrawalId, AdminWithdrawalDecisionRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new Phase5ValidationException("Validation failed.", ["Reason is required."]);
        }

        await ValidateOrThrowAsync(decisionValidator, request, cancellationToken);
        return await TransitionAsync(adminUserId, withdrawalId, WithdrawalRequestStatus.Rejected, request.Note, request.Reason, null, cancellationToken);
    }

    public async Task<AdminWithdrawalDto> MarkPaidAsync(string adminUserId, string withdrawalId, MarkWithdrawalPaidRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Payout request body is required."]);
        }

        await ValidateOrThrowAsync(paidValidator, request, cancellationToken);
        return await TransitionAsync(adminUserId, withdrawalId, WithdrawalRequestStatus.Paid, null, null, request.PayoutReference!.Trim(), cancellationToken);
    }

    public async Task<AdminWithdrawalDto> MarkFailedAsync(string adminUserId, string withdrawalId, MarkWithdrawalFailedRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Payout failure request body is required."]);
        }

        await ValidateOrThrowAsync(failedValidator, request, cancellationToken);
        return await TransitionAsync(adminUserId, withdrawalId, WithdrawalRequestStatus.Failed, null, request.Reason, null, cancellationToken);
    }

    private async Task<AdminWithdrawalDto> TransitionAsync(
        string adminUserId,
        string withdrawalId,
        WithdrawalRequestStatus targetStatus,
        string? note,
        string? reason,
        string? payoutReference,
        CancellationToken cancellationToken)
    {
        return await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            var admin = await RequireAdminAsync(adminUserId, forUpdate: true, transactionCancellationToken);
            var withdrawal = await domainUnitOfWork.WithdrawalRequests.FindByIdForUpdateAsync(withdrawalId, transactionCancellationToken)
                ?? throw new Phase5NotFoundException("Withdrawal request was not found.");
            var priorStatus = withdrawal.Status;
            if (!WithdrawalStateTransitions.CanTransition(priorStatus, targetStatus))
            {
                throw new Phase5ConflictException("Withdrawal transition conflicts with the current state.");
            }

            var now = DateTime.UtcNow;
            switch (targetStatus)
            {
                case WithdrawalRequestStatus.Approved:
                    withdrawal.Status = WithdrawalRequestStatus.Approved;
                    withdrawal.ReviewedByAdminUserId = admin.Id;
                    withdrawal.ReviewedAtUtc = now;
                    withdrawal.DecisionReason = Normalize(reason ?? note);
                    break;
                case WithdrawalRequestStatus.Rejected:
                    await ReleaseHeldToAvailableAsync(withdrawal, now, transactionCancellationToken);
                    withdrawal.Status = WithdrawalRequestStatus.Rejected;
                    withdrawal.ReviewedByAdminUserId = admin.Id;
                    withdrawal.ReviewedAtUtc = now;
                    withdrawal.DecisionReason = Normalize(reason);
                    break;
                case WithdrawalRequestStatus.Paid:
                    await FinalizeHeldPayoutAsync(withdrawal, now, transactionCancellationToken);
                    withdrawal.Status = WithdrawalRequestStatus.Paid;
                    withdrawal.PayoutReference = payoutReference;
                    withdrawal.PayoutStatusChangedByAdminUserId = admin.Id;
                    withdrawal.PayoutStatusChangedAtUtc = now;
                    break;
                case WithdrawalRequestStatus.Failed:
                    await ReleaseHeldToAvailableAsync(withdrawal, now, transactionCancellationToken);
                    withdrawal.Status = WithdrawalRequestStatus.Failed;
                    withdrawal.PayoutFailureReason = Normalize(reason);
                    withdrawal.PayoutStatusChangedByAdminUserId = admin.Id;
                    withdrawal.PayoutStatusChangedAtUtc = now;
                    break;
            }

            await AddAuditAsync($"Withdrawal{targetStatus}", admin.Id, UserRole.Admin, withdrawal, priorStatus, targetStatus, Normalize(reason ?? note) ?? "Withdrawal status changed.", now, transactionCancellationToken);
            return ToAdminDto(withdrawal);
        }, cancellationToken);
    }

    private async Task<(Core.Entities.Identity.ApplicationUser User, Core.Entities.Profiles.DoctorProfile Doctor)> RequireApprovedDoctorAsync(
        string userId,
        bool forUpdate,
        bool requireActiveMarketplace,
        CancellationToken cancellationToken)
    {
        var user = forUpdate
            ? await identityUnitOfWork.Users.FindByIdForUpdateAsync(userId, cancellationToken)
            : await identityUnitOfWork.Users.FindByIdAsync(userId, cancellationToken);
        var doctor = user is null ? null : await identityUnitOfWork.Profiles.FindDoctorProfileByUserIdAsync(user.Id, cancellationToken);
        if (user is not { Role: UserRole.Doctor, AccountStatus: AccountStatus.Approved, IsDeleted: false }
            || doctor is null
            || doctor.IsDeleted
            || (requireActiveMarketplace && doctor.Status != DoctorMarketplaceStatus.Active))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        return (user, doctor);
    }

    private async Task<Core.Entities.Identity.ApplicationUser> RequireAdminAsync(string adminUserId, bool forUpdate, CancellationToken cancellationToken)
    {
        var admin = forUpdate
            ? await identityUnitOfWork.Users.FindByIdForUpdateAsync(adminUserId, cancellationToken)
            : await identityUnitOfWork.Users.FindByIdAsync(adminUserId, cancellationToken);
        if (admin is not { Role: UserRole.Admin, AccountStatus: AccountStatus.Approved, IsDeleted: false })
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        return admin;
    }

    private async Task MoveAvailableToHeldAsync(string walletId, string withdrawalId, decimal amount, DateTime now, CancellationToken cancellationToken)
    {
        var transactionId = Guid.NewGuid().ToString("N");
        await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(walletId, -amount, now, cancellationToken);
        await domainUnitOfWork.Wallets.StageReservedBalanceChangeAsync(walletId, amount, now, cancellationToken);
        await domainUnitOfWork.WalletTransactions.AddTransactionAsync(new WalletTransaction
        {
            Id = transactionId,
            WalletId = walletId,
            OperationType = WalletTransactionType.WithdrawalHold,
            IdempotencyKey = $"withdrawal:{withdrawalId}:hold",
            Amount = amount,
            WithdrawalRequestId = withdrawalId,
            CreatedAtUtc = now
        }, cancellationToken);
        await AddLedgerAsync(transactionId, walletId, withdrawalId, WalletLedgerEntryDirection.Debit, WalletBalanceType.Available, amount, now, cancellationToken);
        await AddLedgerAsync(transactionId, walletId, withdrawalId, WalletLedgerEntryDirection.Credit, WalletBalanceType.Reserved, amount, now, cancellationToken);
    }

    private async Task ReleaseHeldToAvailableAsync(WithdrawalRequest withdrawal, DateTime now, CancellationToken cancellationToken)
    {
        var wallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateByOwnerAsync(WalletOwnerType.Doctor, withdrawal.DoctorId, cancellationToken)
            ?? throw new Phase5ConflictException("Doctor wallet was not found.");
        if (await domainUnitOfWork.WalletTransactions.IdempotencyKeyExistsAsync(WalletTransactionType.WithdrawalRelease, $"withdrawal:{withdrawal.Id}:release", cancellationToken))
        {
            throw new Phase5ConflictException("Withdrawal release was already recorded.");
        }

        var transactionId = Guid.NewGuid().ToString("N");
        await domainUnitOfWork.Wallets.StageReservedBalanceChangeAsync(wallet.Id, -withdrawal.Amount, now, cancellationToken);
        await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(wallet.Id, withdrawal.Amount, now, cancellationToken);
        await domainUnitOfWork.WalletTransactions.AddTransactionAsync(new WalletTransaction
        {
            Id = transactionId,
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.WithdrawalRelease,
            IdempotencyKey = $"withdrawal:{withdrawal.Id}:release",
            Amount = withdrawal.Amount,
            WithdrawalRequestId = withdrawal.Id,
            CreatedAtUtc = now
        }, cancellationToken);
        await AddLedgerAsync(transactionId, wallet.Id, withdrawal.Id, WalletLedgerEntryDirection.Debit, WalletBalanceType.Reserved, withdrawal.Amount, now, cancellationToken);
        await AddLedgerAsync(transactionId, wallet.Id, withdrawal.Id, WalletLedgerEntryDirection.Credit, WalletBalanceType.Available, withdrawal.Amount, now, cancellationToken);
    }

    private async Task FinalizeHeldPayoutAsync(WithdrawalRequest withdrawal, DateTime now, CancellationToken cancellationToken)
    {
        var wallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateByOwnerAsync(WalletOwnerType.Doctor, withdrawal.DoctorId, cancellationToken)
            ?? throw new Phase5ConflictException("Doctor wallet was not found.");
        if (await domainUnitOfWork.WalletTransactions.IdempotencyKeyExistsAsync(WalletTransactionType.WithdrawalFinalizePayout, $"withdrawal:{withdrawal.Id}:paid", cancellationToken))
        {
            throw new Phase5ConflictException("Withdrawal payout was already recorded.");
        }

        var transactionId = Guid.NewGuid().ToString("N");
        await domainUnitOfWork.Wallets.StageReservedBalanceChangeAsync(wallet.Id, -withdrawal.Amount, now, cancellationToken);
        await domainUnitOfWork.WalletTransactions.AddTransactionAsync(new WalletTransaction
        {
            Id = transactionId,
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.WithdrawalFinalizePayout,
            IdempotencyKey = $"withdrawal:{withdrawal.Id}:paid",
            Amount = withdrawal.Amount,
            WithdrawalRequestId = withdrawal.Id,
            CreatedAtUtc = now
        }, cancellationToken);
        await AddLedgerAsync(transactionId, wallet.Id, withdrawal.Id, WalletLedgerEntryDirection.Debit, WalletBalanceType.Reserved, withdrawal.Amount, now, cancellationToken);
    }

    private Task AddLedgerAsync(string transactionId, string walletId, string withdrawalId, WalletLedgerEntryDirection direction, WalletBalanceType balanceType, decimal amount, DateTime now, CancellationToken cancellationToken)
        => domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(new WalletLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletTransactionId = transactionId,
            WalletId = walletId,
            Direction = direction,
            BalanceType = balanceType,
            Amount = amount,
            WithdrawalRequestId = withdrawalId,
            CreatedAtUtc = now
        }, cancellationToken);

    private Task AddAuditAsync(string eventType, string actorUserId, UserRole actorRole, WithdrawalRequest withdrawal, WithdrawalRequestStatus? priorStatus, WithdrawalRequestStatus resultingStatus, string reason, DateTime now, CancellationToken cancellationToken)
        => domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
            Guid.NewGuid().ToString("N"),
            eventType,
            actorUserId,
            actorRole.ToString(),
            AuditTargetType.WithdrawalRequest,
            withdrawal.Id,
            AuditOutcome.Success,
            reason,
            correlationId: null,
            JsonSerializer.Serialize(new
            {
                withdrawal.DoctorId,
                withdrawal.Amount,
                Currency = WithdrawalMoneyRules.Currency,
                PriorStatus = priorStatus?.ToString(),
                ResultingStatus = resultingStatus.ToString(),
                ChangedAtUtc = now
            }),
            now,
            cancellationToken);

    private static AdminPagination CreatePagination(int? pageNumber, int? pageSize)
    {
        if (pageNumber is < 1)
        {
            throw new Phase5ValidationException("Validation failed.", ["PageNumber must be at least 1."]);
        }

        if (pageSize is < 1 or > 100)
        {
            throw new Phase5ValidationException("Validation failed.", ["PageSize must be between 1 and 100."]);
        }

        return new AdminPagination(pageNumber ?? 1, pageSize ?? 20);
    }

    private static async Task ValidateOrThrowAsync<T>(IValidator<T> validator, T request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new Phase5ValidationException("Validation failed.", validation.Errors.Select(error => error.ErrorMessage).ToArray());
        }
    }

    private static PageMetadataDto ToPageMetadata<T>(AdminPageReadModel<T> page)
        => new(page.PageNumber, page.PageSize, page.TotalCount, (int)Math.Ceiling(page.TotalCount / (double)page.PageSize), page.PageNumber > 1, page.PageNumber * page.PageSize < page.TotalCount);

    private static DoctorWithdrawalDto ToDoctorDto(WithdrawalRequest request)
        => new(request.Id, request.Amount, WithdrawalMoneyRules.Currency, request.Status, request.RequestedAtUtc, request.ReviewedAtUtc, request.DecisionReason, request.PayoutReference, request.PayoutStatusChangedAtUtc);

    private static AdminWithdrawalDto ToAdminDto(WithdrawalRequest request)
        => new(request.Id, request.DoctorId, request.DoctorId, request.Amount, WithdrawalMoneyRules.Currency, request.Status, request.RequestedAtUtc, request.ReviewedAtUtc, request.DecisionReason, request.PayoutReference, request.PayoutStatusChangedAtUtc);

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
