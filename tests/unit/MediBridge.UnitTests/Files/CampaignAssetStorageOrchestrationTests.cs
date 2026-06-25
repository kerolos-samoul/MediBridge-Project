using FluentValidation;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Payments;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using MediBridge.Services.Validators.Campaigns;
using Xunit;

namespace MediBridge.UnitTests.Files;

public sealed class CampaignAssetStorageOrchestrationTests
{
    [Fact]
    public async Task UploadAssetAsync_UploadsStreamOnceAndPersistsProviderKey()
    {
        var storage = new RecordingFileStorageProvider();
        var storedFiles = new RecordingStoredFileRepository();
        var service = CreateService(storage: storage, storedFiles: storedFiles);

        using var stream = new MemoryStream([1, 2, 3]);
        var asset = await service.UploadAssetAsync(
            "company-user",
            "campaign-1",
            new CampaignAssetUploadRequestDto("campaign.png", "image/png", 3),
            stream);

        Assert.Single(storage.UploadCalls);
        Assert.Equal("provider/key", storedFiles.AddedFile?.StorageKey);
        Assert.Equal("image", storedFiles.AddedFile?.StorageResourceType);
        Assert.Equal(storedFiles.AddedFile?.Id, asset.AssetId);
    }

    [Fact]
    public async Task UploadAssetAsync_ValidationFailureDoesNotCallProvider()
    {
        var storage = new RecordingFileStorageProvider();
        var service = CreateService(storage: storage);

        using var stream = new MemoryStream([1]);

        await Assert.ThrowsAsync<WorkflowValidationException>(() =>
            service.UploadAssetAsync(
                "company-user",
                "campaign-1",
                new CampaignAssetUploadRequestDto("campaign.exe", "application/x-msdownload", 1),
                stream));

        Assert.Empty(storage.UploadCalls);
    }

    [Fact]
    public async Task UploadAssetAsync_DatabaseFailureDeletesUploadedObjectAndPreservesOriginalException()
    {
        var storage = new RecordingFileStorageProvider();
        var storedFiles = new RecordingStoredFileRepository { ThrowOnAdd = true };
        var service = CreateService(storage: storage, storedFiles: storedFiles);

        using var stream = new MemoryStream([1, 2, 3]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadAssetAsync(
                "company-user",
                "campaign-1",
                new CampaignAssetUploadRequestDto("campaign.png", "image/png", 3),
                stream));

        Assert.Equal("database failed", exception.Message);
        Assert.Contains(storage.DeleteCalls, call => call.StorageKey == "provider/key" && call.ResourceType == "image");
    }

    [Fact]
    public async Task UploadAssetAsync_CompensationFailureIsAuditedWithoutReplacingOriginalException()
    {
        var storage = new RecordingFileStorageProvider { ThrowOnDelete = true };
        var storedFiles = new RecordingStoredFileRepository { ThrowOnAdd = true };
        var auditLogger = new RecordingAuditLogger();
        var service = CreateService(storage: storage, storedFiles: storedFiles, auditLogger: auditLogger);

        using var stream = new MemoryStream([1, 2, 3]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadAssetAsync(
                "company-user",
                "campaign-1",
                new CampaignAssetUploadRequestDto("campaign.png", "image/png", 3),
                stream));

        Assert.Equal("database failed", exception.Message);
        var audit = Assert.Single(auditLogger.Events);
        Assert.Equal(AuditEventCategory.System, audit.Category);
        Assert.StartsWith("FileStorageCompensationFailed:", audit.Action, StringComparison.Ordinal);
        Assert.DoesNotContain("provider/key", audit.SubjectId ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAssetAsync_PassesCancellationToProviderUpload()
    {
        var storage = new RecordingFileStorageProvider();
        var service = CreateService(storage: storage);
        using var stream = new MemoryStream([1, 2, 3]);
        using var cts = new CancellationTokenSource();

        await service.UploadAssetAsync(
            "company-user",
            "campaign-1",
            new CampaignAssetUploadRequestDto("campaign.png", "image/png", 3),
            stream,
            cts.Token);

        Assert.Equal(cts.Token, storage.UploadCalls.Single().CancellationToken);
    }

    private static CampaignWorkflowService CreateService(
        RecordingFileStorageProvider? storage = null,
        RecordingStoredFileRepository? storedFiles = null,
        RecordingAuditLogger? auditLogger = null)
    {
        return new CampaignWorkflowService(
            new FakeIdentityUnitOfWork(),
            new FakeDomainUnitOfWork(storedFiles ?? new RecordingStoredFileRepository()),
            new CreateCampaignDraftRequestDtoValidator(),
            new CampaignAssetUploadRequestValidator(),
            storage ?? new RecordingFileStorageProvider(),
            auditLogger ?? new RecordingAuditLogger(),
            new FakeCurrentUserContext());
    }

    private sealed class RecordingFileStorageProvider : IFileStorageProvider
    {
        public bool ThrowOnDelete { get; set; }
        public List<(FileStorageUpload Request, Stream Content, CancellationToken CancellationToken)> UploadCalls { get; } = [];
        public List<(string StorageKey, string ResourceType)> DeleteCalls { get; } = [];

        public Task<FileStorageUploadResult> UploadAsync(FileStorageUpload request, Stream content, CancellationToken cancellationToken = default)
        {
            UploadCalls.Add((request, content, cancellationToken));
            return Task.FromResult(new FileStorageUploadResult("provider/key", "image"));
        }

        public Task<SignedFileUrl> CreateSignedReadUrlAsync(string storageKey, string resourceType, TimeSpan lifetime, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task DeleteAsync(string storageKey, string resourceType, CancellationToken cancellationToken = default)
        {
            DeleteCalls.Add((storageKey, resourceType));
            if (ThrowOnDelete)
            {
                throw new FileStorageUnavailableException("delete failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStoredFileRepository : IStoredFileRepository
    {
        public bool ThrowOnAdd { get; set; }
        public StoredFile? AddedFile { get; private set; }

        public Task AddStoredFileAsync(StoredFile storedFile, CancellationToken cancellationToken = default)
        {
            if (ThrowOnAdd)
            {
                throw new InvalidOperationException("database failed");
            }

            AddedFile = storedFile;
            return Task.CompletedTask;
        }

        public Task AddStoredFileAsync(string storedFileId, StoredFileOwnerType ownerType, string ownerId, StoredFilePurpose purpose, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StoredFile?> FindStoredFileAsync(string storedFileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<StoredFile?> FindStoredFileForUpdateAsync(string storedFileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string?> FindActiveStoredFileIdAsync(string storedFileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ListActiveStoredFileIdsByOwnerAsync(StoredFileOwnerType ownerType, string ownerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ListStoredFileReviewIdsAsync(StoredFileReviewStatus reviewStatus, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasStoredFileAsync(StoredFileOwnerType ownerType, string ownerId, StoredFilePurpose purpose, StoredFileReviewStatus reviewStatus, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeIdentityUnitOfWork : IIdentityUnitOfWork
    {
        public IApplicationUserRepository Users { get; } = new FakeUserRepository();
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
        public FakeDomainUnitOfWork(IStoredFileRepository storedFiles)
        {
            StoredFiles = storedFiles;
        }

        public ICampaignRepository Campaigns { get; } = new FakeCampaignRepository();
        public IMessageQueueRepository MessageQueues => throw new NotImplementedException();
        public IDeliveryRepository Deliveries => throw new NotImplementedException();
        public IWalletRepository Wallets => throw new NotImplementedException();
        public IWalletTransactionRepository WalletTransactions => throw new NotImplementedException();
        public IWalletLedgerEntryRepository WalletLedgerEntries => throw new NotImplementedException();
        public IPaymentRepository Payments => throw new NotImplementedException();
        public IStoredFileRepository StoredFiles { get; }
        public IPolicyHistoryRepository PolicyHistory => throw new NotImplementedException();
        public IAuditEventRepository AuditEvents => throw new NotImplementedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => await operation(cancellationToken);
    }

    private sealed class FakeUserRepository : IApplicationUserRepository
    {
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<ApplicationUser?>(new ApplicationUser { Id = userId, Role = UserRole.Company, AccountStatus = AccountStatus.Approved });

        public Task<ApplicationUser?> FindByIdForUpdateAsync(string userId, CancellationToken cancellationToken = default)
            => FindByIdAsync(userId, cancellationToken);

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
        public Task<CompanyProfile?> FindCompanyProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<CompanyProfile?>(new CompanyProfile { Id = "company-1", UserId = userId });

        public Task AddDoctorProfileAsync(DoctorProfile profile, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddCompanyProfileAsync(CompanyProfile profile, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DoctorProfile?> FindDoctorProfileByIdAsync(string doctorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DoctorProfile?> FindDoctorProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DoctorProfile>> ListDoctorProfilesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CompanyLicenseExistsAsync(string licenseNumber, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeCampaignRepository : ICampaignRepository
    {
        public Task<Campaign?> FindActiveCampaignAsync(string campaignId, CancellationToken cancellationToken = default)
            => Task.FromResult<Campaign?>(new Campaign { Id = campaignId, CompanyId = "company-1", Status = CampaignStatus.Draft });

        public Task<Campaign?> FindActiveCampaignForUpdateAsync(string campaignId, CancellationToken cancellationToken = default)
            => FindActiveCampaignAsync(campaignId, cancellationToken);

        public Task AddCampaignAsync(string campaignId, string companyId, CampaignStatus status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string?> FindActiveCampaignIdAsync(string campaignId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ListActiveCampaignIdsByCompanyAsync(string companyId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddCampaignTargetAsync(string campaignTargetId, string campaignId, string doctorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddCampaignTargetAsync(CampaignTarget campaignTarget, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CampaignTarget>> ListCampaignTargetsAsync(string campaignId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ReplaceCampaignTargetsAsync(string campaignId, IReadOnlyCollection<CampaignTarget> campaignTargets, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ListCampaignTargetIdsAsync(string campaignId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddCampaignReviewHistoryAsync(string reviewHistoryId, string campaignId, string adminUserId, CampaignReviewDecision decision, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddCampaignReviewHistoryAsync(CampaignReviewHistory history, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CampaignReviewHistory?> FindCampaignReviewByIdempotencyKeyAsync(string campaignId, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddCampaignReviewHistoryCorrectionAsync(string reviewHistoryId, string correctsHistoryId, string campaignId, string adminUserId, CampaignReviewDecision decision, string reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ListCampaignReviewHistoryIdsAsync(string campaignId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string?> FindCampaignIdIncludingDeletedAsync(string campaignId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<AuditEvent> Events { get; } = [];

        public Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => true;
        public string? UserId => "company-user";
        public string? Role => "Company";
        public bool? IsApproved => true;
        public string? CorrelationId => "correlation-id";
    }
}
