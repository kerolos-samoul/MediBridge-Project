using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Files;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class FileAccessService : IFileAccessService
{
    private static readonly TimeSpan SignedUrlLifetime = TimeSpan.FromMinutes(5);
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IFileStorageProvider fileStorageProvider;

    public FileAccessService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IFileStorageProvider fileStorageProvider)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.fileStorageProvider = fileStorageProvider;
    }

    public async Task<FileAccessDto> GetSignedAccessAsync(
        string userId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new WorkflowUnauthorizedException("Authentication denied.");
        }

        var user = await identityUnitOfWork.Users.FindByIdAsync(userId, cancellationToken);
        if (user is null || user.IsDeleted)
        {
            throw new WorkflowUnauthorizedException("Authentication denied.");
        }

        var file = await domainUnitOfWork.StoredFiles.FindStoredFileAsync(fileId, cancellationToken)
            ?? throw new WorkflowNotFoundException("Not found.");
        if (file.StorageState != StorageObjectState.Active || file.DeletedAtUtc is not null)
        {
            throw new WorkflowNotFoundException("Not found.");
        }

        if (user.Role != UserRole.Admin)
        {
            await EnsureCompanyOwnerAsync(user.Id, file, cancellationToken);
        }

        var signedUrl = await fileStorageProvider.CreateSignedReadUrlAsync(
            file.StorageKey,
            file.StorageResourceType,
            SignedUrlLifetime,
            cancellationToken);

        return new FileAccessDto(file.Id, signedUrl.Url.ToString(), signedUrl.ExpiresAtUtc);
    }

    private async Task EnsureCompanyOwnerAsync(
        string userId,
        StoredFile file,
        CancellationToken cancellationToken)
    {
        var company = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(userId, cancellationToken);
        if (company is null || company.IsDeleted)
        {
            throw new WorkflowForbiddenException("Forbidden.");
        }

        if (file.OwnerType == StoredFileOwnerType.Company)
        {
            if (!string.Equals(file.OwnerId, company.Id, StringComparison.Ordinal))
            {
                throw new WorkflowForbiddenException("Forbidden.");
            }

            return;
        }

        if (file.OwnerType == StoredFileOwnerType.Campaign)
        {
            var campaign = await domainUnitOfWork.Campaigns.FindActiveCampaignAsync(file.OwnerId, cancellationToken)
                ?? throw new WorkflowNotFoundException("Not found.");
            if (string.Equals(campaign.CompanyId, company.Id, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new WorkflowForbiddenException("Forbidden.");
    }
}
