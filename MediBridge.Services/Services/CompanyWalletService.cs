using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Wallets;

namespace MediBridge.Services.Services;

public sealed class CompanyWalletService : ICompanyWalletService
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new() { PropertyNamingPolicy = null };
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IValidator<TopUpCompanyWalletRequestDto> topUpValidator;

    public CompanyWalletService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IValidator<TopUpCompanyWalletRequestDto> topUpValidator)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.topUpValidator = topUpValidator;
    }

    public async Task<CompanyWalletDto> GetCompanyWalletAsync(string actorUserId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePagination(pageNumber, pageSize);
        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        var wallet = await domainUnitOfWork.Wallets.FindActiveWalletByOwnerAsync(WalletOwnerType.Company, company.CompanyId, cancellationToken);
        if (wallet is null)
        {
            throw new Phase5NotFoundException("Company wallet was not found.");
        }

        var skip = (pageNumber - 1) * pageSize;
        var totalCount = await domainUnitOfWork.WalletTransactions.CountWalletTransactionsAsync(wallet.Id, cancellationToken);
        var transactions = await domainUnitOfWork.WalletTransactions.ListWalletTransactionsAsync(wallet.Id, skip, pageSize, cancellationToken);

        return new CompanyWalletDto
        {
            WalletId = wallet.Id,
            AvailableBalance = wallet.AvailableBalance,
            ReservedBalance = wallet.ReservedBalance,
            Currency = wallet.Currency,
            Transactions = new CompanyWalletTransactionPageDto
            {
                Page = new CompanyWalletPageMetadataDto
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalCount = totalCount
                },
                Items = transactions.Select(MapTransaction).ToArray()
            }
        };
    }

    public async Task<TopUpCompanyWalletResultDto> TopUpCompanyWalletAsync(string actorUserId, string? idempotencyKey, TopUpCompanyWalletRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Top-up request body is required."]);
        }

        var normalizedIdempotencyKey = CompanyWalletTopUpValidation.NormalizeIdempotencyKey(idempotencyKey);
        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        var validationResult = await topUpValidator.ValidateAsync(request, cancellationToken);
        if (validationResult.IsValid is false)
        {
            await AddAuditAsync(
                "Phase5CompanyWalletTopUpValidationFailed",
                actorUserId,
                company.ActorRole,
                AuditTargetType.Company,
                company.CompanyId,
                AuditOutcome.Denied,
                "Company wallet top-up validation failed.",
                new { ErrorCount = validationResult.Errors.Count },
                cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new Phase5ValidationException("Validation failed.", validationResult.Errors.Select(error => error.ErrorMessage).ToArray());
        }

        var normalizedDescription = NormalizeDescription(request.Description);
        var processingResult = await domainUnitOfWork.ExecuteInTransactionAsync(
            transactionCancellationToken => ProcessTopUpInTransactionAsync(
                actorUserId,
                company,
                normalizedIdempotencyKey,
                request.Amount,
                normalizedDescription,
                transactionCancellationToken),
            cancellationToken);

        if (processingResult.Exception is not null)
        {
            throw processingResult.Exception;
        }

        return processingResult.Result ?? throw new InvalidOperationException("Wallet top-up processing did not produce a result.");
    }

    private async Task<TopUpProcessingResult> ProcessTopUpInTransactionAsync(
        string actorUserId,
        CompanyActor company,
        string idempotencyKey,
        decimal amount,
        string? description,
        CancellationToken cancellationToken)
    {
        var wallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateByOwnerAsync(WalletOwnerType.Company, company.CompanyId, cancellationToken);
        if (wallet is null)
        {
            return TopUpProcessingResult.Failure(new Phase5NotFoundException("Company wallet was not found."));
        }

        var existingTransaction = await domainUnitOfWork.WalletTransactions.FindTransactionByIdempotencyAsync(WalletTransactionType.TopUp, idempotencyKey, cancellationToken);
        if (existingTransaction is not null)
        {
            if (!string.Equals(existingTransaction.WalletId, wallet.Id, StringComparison.Ordinal) ||
                existingTransaction.Amount != amount ||
                !string.Equals(existingTransaction.Description, description, StringComparison.Ordinal))
            {
                await AddAuditAsync(
                    "Phase5CompanyWalletTopUpConflict",
                    actorUserId,
                    company.ActorRole,
                    AuditTargetType.Wallet,
                    wallet.Id,
                    AuditOutcome.Denied,
                    "Company wallet top-up idempotency conflict.",
                    new { WalletId = wallet.Id },
                    cancellationToken);
                return TopUpProcessingResult.Failure(new Phase5ConflictException("Company wallet top-up conflicts with a previous request."));
            }

            await AddAuditAsync(
                "Phase5CompanyWalletTopUpIdempotentReplay",
                actorUserId,
                company.ActorRole,
                AuditTargetType.WalletTransaction,
                existingTransaction.Id,
                AuditOutcome.Info,
                "Company wallet top-up replayed.",
                new { WalletId = wallet.Id, TransactionId = existingTransaction.Id },
                cancellationToken);
            return TopUpProcessingResult.Success(new TopUpCompanyWalletResultDto
            {
                WalletId = wallet.Id,
                TransactionId = existingTransaction.Id,
                AvailableBalance = wallet.AvailableBalance,
                ReservedBalance = wallet.ReservedBalance,
                Currency = wallet.Currency,
                IdempotencyStatus = "Replayed"
            });
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(wallet.Id, amount, cancellationToken);
        await domainUnitOfWork.WalletTransactions.AddTransactionAsync(new WalletTransaction
        {
            Id = transactionId,
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.TopUp,
            IdempotencyKey = idempotencyKey,
            Amount = amount,
            Description = description,
            Metadata = JsonSerializer.Serialize(new { Gateway = "MvpStub", Currency = "EGP" }, AuditJsonOptions),
            CreatedAtUtc = now
        }, cancellationToken);
        await domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(new WalletLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletTransactionId = transactionId,
            WalletId = wallet.Id,
            Direction = WalletLedgerEntryDirection.Credit,
            BalanceType = WalletBalanceType.Available,
            Amount = amount,
            Currency = wallet.Currency,
            CompanyId = company.CompanyId,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = now
        }, cancellationToken);

        await AddAuditAsync(
            "Phase5CompanyWalletTopUpSucceeded",
            actorUserId,
            company.ActorRole,
            AuditTargetType.Wallet,
            wallet.Id,
            AuditOutcome.Success,
            "Company wallet top-up succeeded.",
            new { WalletId = wallet.Id, TransactionId = transactionId, Amount = amount, Currency = wallet.Currency },
            cancellationToken);

        return TopUpProcessingResult.Success(new TopUpCompanyWalletResultDto
        {
            WalletId = wallet.Id,
            TransactionId = transactionId,
            AvailableBalance = wallet.AvailableBalance,
            ReservedBalance = wallet.ReservedBalance,
            Currency = wallet.Currency,
            IdempotencyStatus = "Created"
        });
    }

    private async Task<CompanyActor> ResolveApprovedCompanyActorAsync(string actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var user = await identityUnitOfWork.Users.FindByIdAsync(actorUserId, cancellationToken);
        var profile = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(actorUserId, cancellationToken);
        if (user is { Role: UserRole.Company, AccountStatus: AccountStatus.Approved, IsDeleted: false } && profile is { IsDeleted: false })
        {
            return new CompanyActor(profile.Id, user.Role.ToString());
        }

        await AddAuditAsync(
            "Phase5CompanyWalletOwnershipDenied",
            actorUserId,
            user?.Role.ToString() ?? "Unknown",
            AuditTargetType.User,
            actorUserId,
            AuditOutcome.Denied,
            "Actor is not an approved active company user.",
            new { ActorUserId = actorUserId },
            cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);
        throw new Phase5ForbiddenException("Forbidden.");
    }

    private static CompanyWalletTransactionDto MapTransaction(WalletTransaction transaction)
    {
        return new CompanyWalletTransactionDto
        {
            TransactionId = transaction.Id,
            OperationType = transaction.OperationType,
            Amount = transaction.Amount,
            Currency = "EGP",
            CreatedAtUtc = transaction.CreatedAtUtc,
            Description = transaction.Description
        };
    }

    private static void ValidatePagination(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100)
        {
            throw new Phase5ValidationException("Validation failed.", ["PageNumber must be at least 1 and PageSize must be between 1 and 100."]);
        }
    }

    private static string? NormalizeDescription(string? description)
    {
        var normalized = description?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private async Task AddAuditAsync(
        string eventType,
        string? actorUserId,
        string? actorRole,
        AuditTargetType targetType,
        string targetId,
        AuditOutcome outcome,
        string reason,
        object metadata,
        CancellationToken cancellationToken)
    {
        await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
            Guid.NewGuid().ToString("N"),
            eventType,
            actorUserId,
            actorRole,
            targetType,
            targetId,
            outcome,
            reason,
            correlationId: null,
            JsonSerializer.Serialize(metadata, AuditJsonOptions),
            DateTime.UtcNow,
            cancellationToken);
    }

    private sealed record CompanyActor(string CompanyId, string ActorRole);

    private sealed record TopUpProcessingResult(TopUpCompanyWalletResultDto? Result, Phase5WorkflowException? Exception)
    {
        public static TopUpProcessingResult Success(TopUpCompanyWalletResultDto result)
        {
            return new TopUpProcessingResult(result, null);
        }

        public static TopUpProcessingResult Failure(Phase5WorkflowException exception)
        {
            return new TopUpProcessingResult(null, exception);
        }
    }
}
