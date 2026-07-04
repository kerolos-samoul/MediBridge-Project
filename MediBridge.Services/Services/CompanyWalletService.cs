using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.DTOs.Payments;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Wallets;

namespace MediBridge.Services.Services;

public sealed class CompanyWalletService : ICompanyWalletService
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new() { PropertyNamingPolicy = null };
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IValidator<TopUpCompanyWalletRequestDto> topUpValidator;
    private readonly IValidator<MockTopUpRequestDto> mockTopUpValidator;

    public CompanyWalletService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IValidator<TopUpCompanyWalletRequestDto> topUpValidator,
        IValidator<MockTopUpRequestDto> mockTopUpValidator)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.topUpValidator = topUpValidator;
        this.mockTopUpValidator = mockTopUpValidator;
    }

    public async Task<CompanyWalletDto> GetCompanyWalletAsync(string actorUserId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePagination(pageNumber, pageSize);
        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        var wallet = await domainUnitOfWork.Wallets.FindActiveWalletByOwnerAsync(WalletOwnerType.Company, company.CompanyId, cancellationToken);
        if (wallet is null)
        {
            var createdAtUtc = DateTime.UtcNow;
            wallet = await domainUnitOfWork.ExecuteInTransactionAsync(
                transactionCancellationToken => domainUnitOfWork.Wallets.GetOrCreateActiveWalletForUpdateAsync(
                    Guid.NewGuid().ToString("N"),
                    WalletOwnerType.Company,
                    company.CompanyId,
                    company.UserId,
                    createdAtUtc,
                    transactionCancellationToken),
                cancellationToken);
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

    public async Task<MockPaymentResultDto> CreateMockTopUpAsync(
        string actorUserId,
        string? idempotencyKey,
        MockTopUpRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Mock checkout request body is required."]);
        }

        var normalizedIdempotencyKey = CompanyWalletTopUpValidation.NormalizeIdempotencyKey(idempotencyKey);
        var validationResult = await mockTopUpValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new Phase5ValidationException(
                "Validation failed.",
                validationResult.Errors.Select(error => error.ErrorMessage).ToArray());
        }

        var company = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        return await domainUnitOfWork.ExecuteInTransactionAsync(
            transactionCancellationToken => ProcessMockCheckoutAsync(
                actorUserId,
                company,
                normalizedIdempotencyKey,
                request,
                transactionCancellationToken),
            cancellationToken);
    }

    private async Task<TopUpProcessingResult> ProcessTopUpInTransactionAsync(
        string actorUserId,
        CompanyActor company,
        string idempotencyKey,
        decimal amount,
        string? description,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var wallet = await domainUnitOfWork.Wallets.GetOrCreateActiveWalletForUpdateAsync(
            Guid.NewGuid().ToString("N"),
            WalletOwnerType.Company,
            company.CompanyId,
            company.UserId,
            now,
            cancellationToken);

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
        await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(wallet.Id, amount, now, cancellationToken);
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

    private async Task<MockPaymentResultDto> ProcessMockCheckoutAsync(
        string actorUserId,
        CompanyActor company,
        string idempotencyKey,
        MockTopUpRequestDto request,
        CancellationToken cancellationToken)
    {
        var existingPayment = await domainUnitOfWork.Payments.FindByCompanyAndIdempotencyForUpdateAsync(
            company.CompanyId,
            idempotencyKey,
            cancellationToken);
        if (existingPayment is not null)
        {
            if (existingPayment.Amount != request.Amount ||
                !string.Equals(existingPayment.Currency, request.Currency, StringComparison.Ordinal))
            {
                throw new Phase5ConflictException("Mock checkout conflicts with a previous request.");
            }

            return ToMockPaymentResult(existingPayment);
        }

        var now = DateTime.UtcNow;
        var wallet = await domainUnitOfWork.Wallets.GetOrCreateActiveWalletForUpdateAsync(
            Guid.NewGuid().ToString("N"),
            WalletOwnerType.Company,
            company.CompanyId,
            company.UserId,
            now,
            cancellationToken);
        var paymentId = Guid.NewGuid().ToString("N");
        var transactionId = Guid.NewGuid().ToString("N");
        var auditEventId = Guid.NewGuid().ToString("N");
        var walletTransactionIdempotencyKey = CreateMockWalletTransactionIdempotencyKey(company.CompanyId, idempotencyKey);
        var balanceBefore = wallet.AvailableBalance;
        var balanceAfter = MoneyRules.EnsureValid(
            balanceBefore + request.Amount,
            nameof(MockPaymentTransaction.WalletBalanceAfter));

        await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(wallet.Id, request.Amount, now, cancellationToken);
        await domainUnitOfWork.WalletTransactions.AddTransactionAsync(new WalletTransaction
        {
            Id = transactionId,
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.TopUp,
            IdempotencyKey = walletTransactionIdempotencyKey,
            Amount = request.Amount,
            Description = "Mock checkout top-up",
            Metadata = JsonSerializer.Serialize(new { Gateway = "MockCheckout", PaymentId = paymentId, Currency = "EGP" }, AuditJsonOptions),
            CreatedAtUtc = now
        }, cancellationToken);
        await domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(new WalletLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletTransactionId = transactionId,
            WalletId = wallet.Id,
            Direction = WalletLedgerEntryDirection.Credit,
            BalanceType = WalletBalanceType.Available,
            Amount = request.Amount,
            Currency = wallet.Currency,
            CompanyId = company.CompanyId,
            IdempotencyKey = walletTransactionIdempotencyKey,
            CreatedAtUtc = now
        }, cancellationToken);
        await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
            auditEventId,
            "MockPaymentTopUpSucceeded",
            actorUserId,
            company.ActorRole,
            AuditTargetType.WalletTransaction,
            transactionId,
            AuditOutcome.Success,
            "Mock checkout top-up succeeded.",
            correlationId: null,
            JsonSerializer.Serialize(new { WalletId = wallet.Id, TransactionId = transactionId, Amount = request.Amount, Currency = "EGP" }, AuditJsonOptions),
            now,
            cancellationToken);

        var payment = new MockPaymentTransaction
        {
            PaymentId = paymentId,
            CompanyId = company.CompanyId,
            WalletId = wallet.Id,
            Amount = request.Amount,
            Currency = request.Currency,
            Status = PaymentStatus.Succeeded,
            CreatedAtUtc = now,
            IdempotencyKey = idempotencyKey,
            WalletBalanceBefore = balanceBefore,
            WalletBalanceAfter = balanceAfter,
            WalletTransactionId = transactionId,
            AuditEventId = auditEventId
        };
        await domainUnitOfWork.Payments.AddMockPaymentAsync(payment, cancellationToken);
        return ToMockPaymentResult(payment);
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
            return new CompanyActor(profile.Id, profile.UserId, user.Role.ToString());
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

    private static string CreateMockWalletTransactionIdempotencyKey(string companyId, string idempotencyKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"mock-checkout:{companyId}:{idempotencyKey}"));
        return Convert.ToHexString(bytes);
    }

    private static MockPaymentResultDto ToMockPaymentResult(MockPaymentTransaction payment)
    {
        return new MockPaymentResultDto(
            payment.PaymentId,
            payment.CompanyId,
            payment.Amount,
            payment.Status.ToString(),
            payment.CreatedAtUtc,
            payment.TransactionReference,
            payment.WalletBalanceBefore,
            payment.WalletBalanceAfter);
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

    private sealed record CompanyActor(string CompanyId, string UserId, string ActorRole);

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
