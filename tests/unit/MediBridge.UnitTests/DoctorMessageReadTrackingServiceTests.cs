using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Entities.Policies;
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
using MediBridge.Core.Interfaces.Time;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Services.DTOs.Files;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using MediBridge.Services.Validators.Messaging;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class DoctorMessageReadTrackingServiceTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly BusinessDate = new(2026, 7, 11);

    [Fact]
    public async Task MarkReadAsync_RejectsEmptyActorWithoutStartingTransaction()
    {
        var harness = ReadServiceHarness.Create();

        var exception = await Assert.ThrowsAsync<Phase7ForbiddenException>(() =>
            harness.Service.MarkReadAsync(" ", "delivery-1"));

        Assert.Equal("Doctor access is required.", exception.Message);
        Assert.Equal(0, harness.Domain.TransactionsStarted);
        Assert.Equal(0, harness.Domain.Deliveries.FindReadCalls);
    }

    [Fact]
    public async Task MarkReadAsync_RejectsBlankDeliveryWithoutResolvingDoctor()
    {
        var harness = ReadServiceHarness.Create();

        var exception = await Assert.ThrowsAsync<Phase7NotFoundException>(() =>
            harness.Service.MarkReadAsync("doctor-user-1", " "));

        Assert.Equal("Delivery was not found.", exception.Message);
        Assert.Equal(0, harness.Identity.Users.FindByIdCalls);
        Assert.Equal(0, harness.Domain.TransactionsStarted);
    }

    [Fact]
    public async Task MarkReadAsync_RecordsFirstReadForApprovedDoctor()
    {
        var harness = ReadServiceHarness.Create();
        harness.Domain.Deliveries.ReadResult = new ReadTrackingReplayReadModel(
            "delivery-1",
            DeliveryStatus.Active,
            UtcNow,
            AlreadyRead: false);

        var result = await harness.Service.MarkReadAsync("doctor-user-1", "delivery-1");

        Assert.Equal("doctor-user-1", harness.Identity.Users.LastUserId);
        Assert.Equal("doctor-user-1", harness.Identity.Profiles.LastUserId);
        Assert.Equal("doctor-1", harness.Domain.Deliveries.LastDoctorId);
        Assert.Equal("delivery-1", harness.Domain.Deliveries.LastDeliveryId);
        Assert.Equal(BusinessDate, harness.Domain.Deliveries.LastBusinessDate);
        Assert.Equal(UtcNow, harness.Domain.Deliveries.LastReadAtUtc);
        Assert.Equal("delivery-1", result.DeliveryId);
        Assert.Equal(DeliveryStatus.Active, result.Status);
        Assert.False(result.AlreadyRead);
        Assert.Equal(1, harness.Domain.TransactionsStarted);
    }

    [Fact]
    public async Task MarkReadAsync_ReplaysAlreadyReadTimestamp()
    {
        var firstReadAtUtc = UtcNow.AddMinutes(-15);
        var harness = ReadServiceHarness.Create();
        harness.Domain.Deliveries.ReadResult = new ReadTrackingReplayReadModel(
            "delivery-1",
            DeliveryStatus.Active,
            firstReadAtUtc,
            AlreadyRead: true);

        var result = await harness.Service.MarkReadAsync("doctor-user-1", "delivery-1");

        Assert.True(result.AlreadyRead);
        Assert.Equal(firstReadAtUtc, result.ReadAtUtc);
        Assert.Equal(UtcNow, harness.Domain.Deliveries.LastReadAtUtc);
        Assert.Equal(1, harness.Domain.TransactionsStarted);
    }

    [Fact]
    public async Task MarkReadAsync_ReturnsNotFoundForMissingOwnedCurrentDayDelivery()
    {
        var harness = ReadServiceHarness.Create();
        harness.Domain.Deliveries.DeliveryForRead = null;

        var exception = await Assert.ThrowsAsync<Phase7NotFoundException>(() =>
            harness.Service.MarkReadAsync("doctor-user-1", "missing-delivery"));

        Assert.Equal("Delivery was not found.", exception.Message);
        Assert.Equal(1, harness.Domain.Deliveries.FindReadCalls);
        Assert.Equal(0, harness.Domain.Deliveries.TryMarkReadCalls);
    }

    [Fact]
    public async Task MarkReadAsync_PropagatesCancellationToIdentityLookup()
    {
        var harness = ReadServiceHarness.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            harness.Service.MarkReadAsync("doctor-user-1", "delivery-1", cancellation.Token));

        Assert.True(harness.Identity.Users.LastCancellationToken.IsCancellationRequested);
        Assert.Equal(0, harness.Domain.TransactionsStarted);
    }

    private sealed class ReadServiceHarness
    {
        private ReadServiceHarness(FakeDomainUnitOfWork domain, FakeIdentityUnitOfWork identity, DoctorMessageService service)
        {
            Domain = domain;
            Identity = identity;
            Service = service;
        }

        public FakeDomainUnitOfWork Domain { get; }
        public FakeIdentityUnitOfWork Identity { get; }
        public DoctorMessageService Service { get; }

        public static ReadServiceHarness Create()
        {
            var domain = new FakeDomainUnitOfWork();
            var identity = new FakeIdentityUnitOfWork();
            var service = new DoctorMessageService(
                domain,
                identity,
                new FixedBusinessClock(UtcNow, BusinessDate),
                new ThrowingFileWorkflowService(),
                new DoctorInteractionRequestValidator(),
                new DoctorInteractionIdempotency());
            return new ReadServiceHarness(domain, identity, service);
        }
    }

    private sealed class FixedBusinessClock(DateTime utcNow, DateOnly businessDate) : IEgyptBusinessClock
    {
        public TimeZoneInfo TimeZone { get; } = TimeZoneInfo.Utc;
        public EgyptBusinessTimeSnapshot Capture() => new(utcNow, new DateTimeOffset(utcNow), businessDate);
    }

    private sealed class FakeDomainUnitOfWork : IDomainUnitOfWork
    {
        public FakeDeliveryRepository Deliveries { get; } = new();
        IDeliveryRepository IDomainUnitOfWork.Deliveries => Deliveries;
        public int TransactionsStarted { get; private set; }

        public ICampaignRepository Campaigns => throw new NotSupportedException();
        public IProfileRepository Profiles => throw new NotSupportedException();
        public IMessageQueueRepository MessageQueues => throw new NotSupportedException();
        public IDeliveryInteractionRepository DeliveryInteractions => throw new NotSupportedException();
        public IDeliveryJobRunRepository DeliveryJobRuns => throw new NotSupportedException();
        public IDeliveryRecoveryDispatchRepository DeliveryRecoveryDispatches => throw new NotSupportedException();
        public IWalletRepository Wallets => throw new NotSupportedException();
        public IWalletTransactionRepository WalletTransactions => throw new NotSupportedException();
        public IWalletLedgerEntryRepository WalletLedgerEntries => throw new NotSupportedException();
        public IPaymentRepository Payments => throw new NotSupportedException();
        public IStoredFileRepository StoredFiles => throw new NotSupportedException();
        public IFileReviewRepository FileReviews => throw new NotSupportedException();
        public IFileAccessGrantAuditRepository FileAccessGrantAudits => throw new NotSupportedException();
        public IPolicyHistoryRepository PolicyHistory => throw new NotSupportedException();
        public IAuditEventRepository AuditEvents => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);

        public Task<T> ExecuteIsolatedInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            TransactionsStarted++;
            return operation(cancellationToken);
        }
    }

    private sealed class FakeIdentityUnitOfWork : IIdentityUnitOfWork
    {
        public FakeApplicationUserRepository Users { get; } = new();
        public FakeProfileRepository Profiles { get; } = new();
        IApplicationUserRepository IIdentityUnitOfWork.Users => Users;
        IProfileRepository IIdentityUnitOfWork.Profiles => Profiles;
        public IRefreshCredentialRepository RefreshCredentials => throw new NotSupportedException();
        public IPasswordResetFlowRepository PasswordResetFlows => throw new NotSupportedException();
        public IContactVerificationFlowRepository ContactVerificationFlows => throw new NotSupportedException();
        public IAdminAccountDecisionRepository AdminAccountDecisions => throw new NotSupportedException();
        public IAccountResubmissionRepository AccountResubmissions => throw new NotSupportedException();
        public IAccountResubmissionTokenRepository AccountResubmissionTokens => throw new NotSupportedException();
        public IAuthenticationAuditEventRepository AuthenticationAuditEvents => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class FakeApplicationUserRepository : IApplicationUserRepository
    {
        public int FindByIdCalls { get; private set; }
        public string? LastUserId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public ApplicationUser? User { get; set; } = new()
        {
            Id = "doctor-user-1",
            Email = "doctor@example.test",
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Approved
        };

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            FindByIdCalls++;
            LastUserId = userId;
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(User);
        }

        public Task<ApplicationUser?> FindByIdForUpdateAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApplicationUser?> FindByContactAsync(string contact, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ValidatePasswordAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(ApplicationUser user, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApplicationUser>> ListByStatusAsync(AccountStatus status, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountByStatusAsync(AccountStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeProfileRepository : IProfileRepository
    {
        public string? LastUserId { get; private set; }
        public DoctorProfile? DoctorProfile { get; set; } = new()
        {
            Id = "doctor-1",
            UserId = "doctor-user-1",
            Status = DoctorMarketplaceStatus.Active
        };

        public Task<DoctorProfile?> FindDoctorProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(DoctorProfile);
        }

        public Task AddDoctorProfileAsync(DoctorProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddCompanyProfileAsync(CompanyProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DoctorProfile?> FindDoctorProfileByIdForUpdateAsync(string doctorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CompanyProfile?> FindCompanyProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CompanyProfile?> FindCompanyProfileByIdAsync(string companyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CompanyProfile?> FindCompanyProfileByIdForUpdateAsync(string companyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CompanyLicenseExistsAsync(string licenseNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DoctorProfile>> SearchEligibleDoctorsAsync(EligibleDoctorSearchCriteria criteria, int skip, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DoctorProfile>> ListEligibleDoctorsByIdsAsync(IReadOnlyCollection<string> doctorIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountEligibleDoctorsAsync(EligibleDoctorSearchCriteria criteria, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LockedDoctorDeliveryEligibilityReadModel?> FindDoctorDeliveryEligibilityForUpdateAsync(string doctorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeDeliveryRepository : IDeliveryRepository
    {
        public int FindReadCalls { get; private set; }
        public int TryMarkReadCalls { get; private set; }
        public string? LastDoctorId { get; private set; }
        public string? LastDeliveryId { get; private set; }
        public DateOnly LastBusinessDate { get; private set; }
        public DateTime LastReadAtUtc { get; private set; }
        public DoctorAdDelivery? DeliveryForRead { get; set; } = new()
        {
            Id = "delivery-1",
            DoctorId = "doctor-1",
            Status = DeliveryStatus.Active,
            ReservationStatus = ReservationStatus.Reserved
        };
        public ReadTrackingReplayReadModel? ReadResult { get; set; } = new("delivery-1", DeliveryStatus.Active, UtcNow, AlreadyRead: false);

        public Task<DoctorAdDelivery?> FindOwnedCurrentDayForReadAsync(
            string doctorId,
            string deliveryId,
            DateOnly businessDateEgypt,
            CancellationToken cancellationToken = default)
        {
            FindReadCalls++;
            LastDoctorId = doctorId;
            LastDeliveryId = deliveryId;
            LastBusinessDate = businessDateEgypt;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(DeliveryForRead);
        }

        public Task<ReadTrackingReplayReadModel?> TryMarkReadAsync(
            string deliveryId,
            DateTime readAtUtc,
            CancellationToken cancellationToken = default)
        {
            TryMarkReadCalls++;
            LastDeliveryId = deliveryId;
            LastReadAtUtc = readAtUtc;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReadResult);
        }

        public Task AddDeliveryAsync(string deliveryId, string doctorId, string campaignId, string companyId, DateOnly deliveryDateEgypt, decimal pricePerMessageSnapshot, decimal platformFeePercentSnapshot, decimal platformFeeAmount, decimal doctorEarnings, decimal reservedAmount, DateTime deliveredAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> FindDeliveryIdAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeliveryReservationReplayReadModel?> FindReservationReplayAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeliveryExistsAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListDeliveryHistoryIdsAsync(string doctorId, DateOnly? fromDateEgypt = null, DateOnly? toDateEgypt = null, DeliveryStatus? status = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OverdueDeliveryReadModel>> ListOverduePageAsync(DateOnly currentBusinessDateEgypt, OverdueDeliveryCursor? after, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DoctorAdDelivery?> FindActiveReservedForUpdateAsync(string deliveryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountForDoctorOnDateAsync(string doctorId, DateOnly businessDateEgypt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasOverdueActiveReservedForCompanyAsync(string companyId, DateOnly currentBusinessDateEgypt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryMarkExpiredAndReleasedAsync(string deliveryId, DateTime expiredAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TodayDeliveryReadModel>> ListTodayPageAsync(string doctorId, DateOnly businessDateEgypt, TodayDeliveryCursor? after, int takePlusOne, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApprovedDeliveryAssetReadModel>> ListApprovedAssetsAsync(IReadOnlyCollection<string> campaignIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeliveryAssetAuthorizationReadModel?> FindDeliveryAssetAuthorizationAsync(string doctorId, string deliveryId, string fileId, DateOnly businessDateEgypt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DoctorAdDelivery?> FindOwnedActiveReservedForInteractionAsync(string doctorId, string deliveryId, DateOnly businessDateEgypt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractionReplayReadModel?> FindSettledInteractionReplayAsync(string doctorId, string deliveryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryMarkInteractedAndChargedAsync(string deliveryId, DeliveryInteractionOutcome outcome, DateTime interactedAtUtc, string? feedbackText, FeedbackQualityStatus? feedbackQualityStatus, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingFileWorkflowService : IFileWorkflowService
    {
        public Task<FileDto> UploadVerificationDocumentAsync(string actorUserId, UserRole role, FileWorkflowUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileDto> UploadCampaignFileAsync(string actorUserId, string campaignId, StoredFilePurpose purpose, FileWorkflowUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileDto> ReplaceCampaignFileAsync(string actorUserId, string campaignId, string storedFileId, FileWorkflowUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeleteFileResultDto> DeleteCampaignFileAsync(string actorUserId, string campaignId, string storedFileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileAccessGrantDto> CreatePrivateAccessGrantAsync(string actorUserId, UserRole role, string storedFileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeliveryAssetAccessGrantDto> CreateDeliveryAssetAccessGrantAsync(string actorUserId, string deliveryId, string fileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileReviewDto> ReviewFileAsync(string adminUserId, string storedFileId, FileReviewRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileDto> ReplaceFileAsync(string actorUserId, UserRole role, string storedFileId, FileWorkflowUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeleteFileResultDto> DeleteFileAsync(string actorUserId, UserRole role, string storedFileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PendingFileReviewPageDto> ListPendingReviewsAsync(string adminUserId, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FileReviewDto>> GetReviewHistoryAsync(string adminUserId, string storedFileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
