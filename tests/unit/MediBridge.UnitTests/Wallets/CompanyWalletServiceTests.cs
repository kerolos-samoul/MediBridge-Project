using FluentValidation;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Payments;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Services.DTOs.Payments;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using MediBridge.Services.Validators.Wallets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.UnitTests.Wallets;

public sealed class CompanyWalletServiceTests
{
    [Theory]
    [InlineData(0, "EGP")]
    [InlineData(-1, "EGP")]
    [InlineData(10.123, "EGP")]
    [InlineData(10, "USD")]
    public async Task CreateMockTopUpAsync_RejectsInvalidMoneyInput(decimal amount, string currency)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<WorkflowValidationException>(() =>
            service.CreateMockTopUpAsync("company-user", "valid-key-123", new MockTopUpRequestDto(amount, currency)));
    }

    [Fact]
    public async Task CreateMockTopUpAsync_RequiresSafeIdempotencyKey()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<WorkflowValidationException>(() =>
            service.CreateMockTopUpAsync("company-user", " ", new MockTopUpRequestDto(10m, "EGP")));

        Assert.DoesNotContain("company-user", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateMockTopUpAsync_ReplayConflictMessageIsSafe()
    {
        var identity = new FakeIdentityUnitOfWork();
        var domain = new FakeDomainUnitOfWork();
        domain.Payments.PriorPayment = new MockPaymentTransaction
        {
            CompanyId = "company-profile",
            WalletId = "wallet-1",
            Amount = 10m,
            Currency = "EGP",
            IdempotencyKey = "replay-key",
            WalletBalanceBefore = 0m,
            WalletBalanceAfter = 10m,
            WalletTransactionId = "transaction-1"
        };
        var service = CreateService(identity, domain);

        var exception = await Assert.ThrowsAsync<WorkflowConflictException>(() =>
            service.CreateMockTopUpAsync("company-user", "replay-key", new MockTopUpRequestDto(11m, "EGP")));

        Assert.DoesNotContain("11", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("replay-key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MockPaymentResultDto_DoesNotExposeProviderRedirectOrCallbackFields()
    {
        var properties = typeof(MockPaymentResultDto).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(properties, property => property.Contains("Redirect", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Contains("Callback", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Contains("Webhook", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateMockTopUpAsync_LocksApprovalStateInsideFinancialTransaction()
    {
        var domain = new FakeDomainUnitOfWork();
        var users = new FakeUserRepository(() => domain.TransactionActive);
        var identity = new FakeIdentityUnitOfWork(users);
        var service = CreateService(identity, domain);

        await service.CreateMockTopUpAsync(
            "company-user",
            "approval-lock-key",
            new MockTopUpRequestDto(10m, "EGP"));

        Assert.True(users.FindByIdForUpdateCalledWithinTransaction);
    }

    private static ICompanyWalletService CreateService(
        FakeIdentityUnitOfWork? identity = null,
        FakeDomainUnitOfWork? domain = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdentityUnitOfWork>(identity ?? new FakeIdentityUnitOfWork());
        services.AddSingleton<IDomainUnitOfWork>(domain ?? new FakeDomainUnitOfWork());
        services.AddSingleton<IValidator<MockTopUpRequestDto>, MockTopUpRequestDtoValidator>();
        services.AddSingleton<ICompanyWalletService, CompanyWalletService>();
        return services.BuildServiceProvider().GetRequiredService<ICompanyWalletService>();
    }

    private sealed class FakeIdentityUnitOfWork : IIdentityUnitOfWork
    {
        public FakeIdentityUnitOfWork(IApplicationUserRepository? users = null)
        {
            Users = users ?? new FakeUserRepository();
        }

        public IApplicationUserRepository Users { get; }
        public IProfileRepository Profiles { get; } = new FakeProfileRepository();
        public IRefreshCredentialRepository RefreshCredentials => throw new NotImplementedException();
        public IPasswordResetFlowRepository PasswordResetFlows => throw new NotImplementedException();
        public IContactVerificationFlowRepository ContactVerificationFlows => throw new NotImplementedException();
        public IAdminAccountDecisionRepository AdminAccountDecisions => throw new NotImplementedException();
        public IAccountResubmissionRepository AccountResubmissions => throw new NotImplementedException();
        public IAccountResubmissionTokenRepository AccountResubmissionTokens => throw new NotImplementedException();
        public IAuthenticationAuditEventRepository AuthenticationAuditEvents => throw new NotImplementedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => await operation(cancellationToken);
    }

    private sealed class FakeDomainUnitOfWork : IDomainUnitOfWork
    {
        public bool TransactionActive { get; private set; }
        public ICampaignRepository Campaigns => throw new NotImplementedException();
        public IMessageQueueRepository MessageQueues => throw new NotImplementedException();
        public IDeliveryRepository Deliveries => throw new NotImplementedException();
        public IWalletRepository Wallets { get; } = new FakeWalletRepository();
        public IWalletTransactionRepository WalletTransactions { get; } = new FakeWalletTransactionRepository();
        public IWalletLedgerEntryRepository WalletLedgerEntries { get; } = new FakeWalletLedgerEntryRepository();
        public FakePaymentRepository Payments { get; } = new();
        IPaymentRepository IDomainUnitOfWork.Payments => Payments;
        public IStoredFileRepository StoredFiles => throw new NotImplementedException();
        public IPolicyHistoryRepository PolicyHistory => throw new NotImplementedException();
        public IAuditEventRepository AuditEvents { get; } = new FakeAuditEventRepository();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            TransactionActive = true;
            try
            {
                await operation(cancellationToken);
            }
            finally
            {
                TransactionActive = false;
            }
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            TransactionActive = true;
            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                TransactionActive = false;
            }
        }
    }

    private sealed class FakeUserRepository : IApplicationUserRepository
    {
        private readonly Func<bool>? isTransactionActive;

        public FakeUserRepository(Func<bool>? isTransactionActive = null)
        {
            this.isTransactionActive = isTransactionActive;
        }

        public bool FindByIdForUpdateCalledWithinTransaction { get; private set; }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<ApplicationUser?>(new ApplicationUser { Id = userId, Role = UserRole.Company, AccountStatus = AccountStatus.Approved });

        public Task<ApplicationUser?> FindByIdForUpdateAsync(string userId, CancellationToken cancellationToken = default)
        {
            FindByIdForUpdateCalledWithinTransaction = isTransactionActive?.Invoke() == true;
            return FindByIdAsync(userId, cancellationToken);
        }

        public Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ApplicationUser?> FindByContactAsync(string contact, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ValidatePasswordAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddAsync(ApplicationUser user, string password, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ApplicationUser>> ListByStatusAsync(AccountStatus status, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountByStatusAsync(AccountStatus status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeProfileRepository : IProfileRepository
    {
        public Task AddDoctorProfileAsync(DoctorProfile profile, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddCompanyProfileAsync(CompanyProfile profile, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DoctorProfile?> FindDoctorProfileByIdAsync(string doctorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DoctorProfile?> FindDoctorProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DoctorProfile>> ListDoctorProfilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DoctorProfile>>(Array.Empty<DoctorProfile>());
        public Task<CompanyProfile?> FindCompanyProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<CompanyProfile?>(new CompanyProfile { Id = "company-profile", UserId = userId });
        public Task<CompanyProfile?> FindCompanyProfileByIdAsync(string companyId, CancellationToken cancellationToken = default)
            => Task.FromResult<CompanyProfile?>(new CompanyProfile { Id = companyId, UserId = "company-user" });
        public Task<CompanyProfile?> FindCompanyProfileByIdForUpdateAsync(string companyId, CancellationToken cancellationToken = default)
            => FindCompanyProfileByIdAsync(companyId, cancellationToken);

        public Task<bool> CompanyLicenseExistsAsync(string licenseNumber, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public MockPaymentTransaction? PriorPayment { get; set; }
        public Task AddMockPaymentAsync(MockPaymentTransaction payment, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<MockPaymentTransaction?> FindByPaymentIdAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<MockPaymentTransaction?>(null);
        public Task<MockPaymentTransaction?> FindByCompanyIdAndIdempotencyKeyAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default) => Task.FromResult(PriorPayment);
        public Task<MockPaymentTransaction?> FindByCompanyIdAndIdempotencyKeyForUpdateAsync(string companyId, string idempotencyKey, CancellationToken cancellationToken = default) => Task.FromResult(PriorPayment);
        public Task<MockPaymentTransaction?> FindByTransactionReferenceAsync(string transactionReference, CancellationToken cancellationToken = default) => Task.FromResult<MockPaymentTransaction?>(null);
        public Task<IReadOnlyList<string>> ListPaymentIdsByCompanyAsync(string companyId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class FakeWalletRepository : IWalletRepository
    {
        public Task AddWalletAsync(string walletId, WalletOwnerType ownerType, string ownerId, string? ownerUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Wallet?> FindActiveWalletByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult<Wallet?>(new Wallet { Id = "wallet-1", OwnerType = ownerType, OwnerId = ownerId, AvailableBalance = 0m, ReservedBalance = 0m });
        public Task<Wallet?> FindActiveWalletForUpdateAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default)
            => FindActiveWalletByOwnerAsync(ownerType, ownerId, cancellationToken);

        public Task<Wallet> GetOrCreateActiveWalletForUpdateAsync(string walletId, WalletOwnerType ownerType, string ownerId, string? ownerUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(new Wallet { Id = "wallet-1", OwnerType = ownerType, OwnerId = ownerId, OwnerUserId = ownerUserId, AvailableBalance = 0m, ReservedBalance = 0m });

        public Task<string?> FindActiveWalletIdByOwnerAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("wallet-1");
        public Task<string?> FindWalletIdByOwnerIncludingDeletedAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task StageAvailableBalanceChangeAsync(string walletId, decimal amountDelta, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StageReservedBalanceChangeAsync(string walletId, decimal amountDelta, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ActiveWalletExistsAsync(WalletOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeWalletTransactionRepository : IWalletTransactionRepository
    {
        public Task AddTransactionAsync(string transactionId, string walletId, WalletTransactionType operationType, string idempotencyKey, decimal amount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> FindTransactionIdByIdempotencyAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IdempotencyKeyExistsAsync(WalletTransactionType operationType, string idempotencyKey, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> ListWalletTransactionIdsAsync(string walletId, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class FakeWalletLedgerEntryRepository : IWalletLedgerEntryRepository
    {
        public Task AddLedgerEntryAsync(string ledgerEntryId, string walletTransactionId, string walletId, WalletLedgerEntryDirection direction, WalletBalanceType balanceType, decimal amount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddLedgerEntryAsync(WalletLedgerEntry ledgerEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListLedgerEntryIdsByWalletAsync(string walletId, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> ListLedgerEntryIdsByWalletTransactionAsync(string walletTransactionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> ListLedgerEntryIdsByReferencesAsync(string? campaignId = null, string? deliveryId = null, string? withdrawalRequestId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class FakeAuditEventRepository : IAuditEventRepository
    {
        public Task AddAuditEventAsync(string auditEventId, string eventType, AuditOutcome outcome, DateTime createdAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddAuditEventAsync(string auditEventId, string eventType, AuditOutcome outcome, DateTime createdAtUtc, string? metadata, string? correctsAuditEventId = null, AuditTargetType? targetType = null, string? targetId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> FindAuditEventIdAsync(string auditEventId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> ListAuditEventIdsAsync(AuditTargetType? targetType = null, string? targetId = null, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> ListAuditEventTargetIdsIncludingHistoricalTargetsAsync(AuditTargetType targetType, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
