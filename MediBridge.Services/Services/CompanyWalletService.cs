using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Payments;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class CompanyWalletService : ICompanyWalletService
{
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IValidator<MockTopUpRequestDto> topUpValidator;

    public CompanyWalletService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IValidator<MockTopUpRequestDto> topUpValidator)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.topUpValidator = topUpValidator;
    }

    public async Task<CompanyWalletDto> GetCompanyWalletAsync(string companyUserId, CancellationToken cancellationToken = default)
    {
        return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var company = await GetApprovedCompanyAsync(companyUserId, transactionCancellationToken);
            var wallet = await GetOrCreateCompanyWalletAsync(company.Id, company.UserId, transactionCancellationToken);
            return new CompanyWalletDto(wallet.WalletId, company.Id, wallet.AvailableBalance, wallet.ReservedBalance, "EGP");
        }, cancellationToken);
    }

    public async Task<MockPaymentResultDto> CreateMockTopUpAsync(string companyUserId, string idempotencyKey, MockTopUpRequestDto request, CancellationToken cancellationToken = default)
    {
        var safeIdempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        var validation = await topUpValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new WorkflowValidationException("Validation failed.");
        }

        return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var company = await GetApprovedCompanyAsync(companyUserId, transactionCancellationToken);
            var priorPayment = await domainUnitOfWork.Payments.FindByCompanyIdAndIdempotencyKeyForUpdateAsync(company.Id, safeIdempotencyKey, transactionCancellationToken);
            if (priorPayment is not null)
            {
                if (priorPayment.Amount == request.Amount && string.Equals(priorPayment.Currency, request.Currency, StringComparison.Ordinal))
                {
                    return ToPaymentResult(priorPayment);
                }

                throw new WorkflowConflictException("Idempotency conflict.");
            }

            var wallet = await GetOrCreateCompanyWalletAsync(company.Id, company.UserId, transactionCancellationToken);
            var walletTransactionId = Guid.NewGuid().ToString("N");
            var auditEventId = Guid.NewGuid().ToString("N");
            var walletBalanceBefore = wallet.AvailableBalance;
            var walletBalanceAfter = walletBalanceBefore + request.Amount;

            MoneyRules.EnsureValid(walletBalanceAfter, nameof(walletBalanceAfter));
            await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(wallet.WalletId, request.Amount, transactionCancellationToken);
            await domainUnitOfWork.WalletTransactions.AddTransactionAsync(
                walletTransactionId,
                wallet.WalletId,
                WalletTransactionType.TopUp,
                CreateWalletTransactionIdempotencyKey(company.Id, safeIdempotencyKey),
                request.Amount,
                transactionCancellationToken);
            await domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(
                Guid.NewGuid().ToString("N"),
                walletTransactionId,
                wallet.WalletId,
                WalletLedgerEntryDirection.Credit,
                WalletBalanceType.Available,
                request.Amount,
                transactionCancellationToken);
            await domainUnitOfWork.AuditEvents.AddAuditEventAsync(
                auditEventId,
                "MockPaymentTopUpSucceeded",
                AuditOutcome.Success,
                DateTime.UtcNow,
                "Mock top-up succeeded.",
                targetType: AuditTargetType.WalletTransaction,
                targetId: walletTransactionId,
                cancellationToken: transactionCancellationToken);

            var payment = new MockPaymentTransaction
            {
                CompanyId = company.Id,
                WalletId = wallet.WalletId,
                Amount = request.Amount,
                Currency = "EGP",
                Status = PaymentStatus.Succeeded,
                CreatedAtUtc = DateTime.UtcNow,
                IdempotencyKey = safeIdempotencyKey,
                WalletBalanceBefore = walletBalanceBefore,
                WalletBalanceAfter = walletBalanceAfter,
                WalletTransactionId = walletTransactionId,
                AuditEventId = auditEventId
            };

            await domainUnitOfWork.Payments.AddMockPaymentAsync(payment, transactionCancellationToken);
            return ToPaymentResult(payment);
        }, cancellationToken);
    }

    private async Task<Core.Entities.Profiles.CompanyProfile> GetApprovedCompanyAsync(string companyUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            throw new WorkflowUnauthorizedException("Authentication denied.");
        }

        var user = await identityUnitOfWork.Users.FindByIdForUpdateAsync(companyUserId, cancellationToken)
            ?? throw new WorkflowUnauthorizedException("Authentication denied.");

        if (user.Role != UserRole.Company || user.AccountStatus != AccountStatus.Approved || user.IsDeleted)
        {
            throw new WorkflowForbiddenException("Forbidden.");
        }

        var company = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(companyUserId, cancellationToken);
        if (company is null || company.IsDeleted)
        {
            throw new WorkflowNotFoundException("Not found.");
        }

        return company;
    }

    private async Task<WalletSnapshot> GetOrCreateCompanyWalletAsync(string companyId, string companyUserId, CancellationToken cancellationToken)
    {
        var wallet = await domainUnitOfWork.Wallets.GetOrCreateActiveWalletForUpdateAsync(
            Guid.NewGuid().ToString("N"),
            WalletOwnerType.Company,
            companyId,
            companyUserId,
            cancellationToken);
        return new WalletSnapshot(wallet.Id, wallet.AvailableBalance, wallet.ReservedBalance);
    }

    private static string ValidateIdempotencyKey(string idempotencyKey)
    {
        var safeKey = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(safeKey) || safeKey.Length < 8 || safeKey.Length > 128)
        {
            throw new WorkflowValidationException("Validation failed.");
        }

        return safeKey;
    }

    private static MockPaymentResultDto ToPaymentResult(MockPaymentTransaction payment)
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

    private static string CreateWalletTransactionIdempotencyKey(string companyId, string idempotencyKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{companyId}:{idempotencyKey}"));
        return Convert.ToHexString(bytes);
    }

    private sealed record WalletSnapshot(string WalletId, decimal AvailableBalance, decimal ReservedBalance);
}
