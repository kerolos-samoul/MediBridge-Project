using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Files;

namespace MediBridge.Services.Interfaces;

public interface IFileWorkflowService
{
    Task<FileDto> UploadVerificationDocumentAsync(string actorUserId, UserRole role, FileWorkflowUpload upload, CancellationToken cancellationToken = default);
    Task<FileDto> UploadCampaignFileAsync(string actorUserId, string campaignId, StoredFilePurpose purpose, FileWorkflowUpload upload, CancellationToken cancellationToken = default);
    Task<FileDto> ReplaceCampaignFileAsync(string actorUserId, string campaignId, string storedFileId, FileWorkflowUpload upload, CancellationToken cancellationToken = default);
    Task<DeleteFileResultDto> DeleteCampaignFileAsync(string actorUserId, string campaignId, string storedFileId, CancellationToken cancellationToken = default);
    Task<FileAccessGrantDto> CreatePrivateAccessGrantAsync(string actorUserId, UserRole role, string storedFileId, CancellationToken cancellationToken = default);
    Task<FileReviewDto> ReviewFileAsync(string adminUserId, string storedFileId, FileReviewRequestDto request, CancellationToken cancellationToken = default);
    Task<FileDto> ReplaceFileAsync(string actorUserId, UserRole role, string storedFileId, FileWorkflowUpload upload, CancellationToken cancellationToken = default);
    Task<DeleteFileResultDto> DeleteFileAsync(string actorUserId, UserRole role, string storedFileId, CancellationToken cancellationToken = default);
    Task<PendingFileReviewPageDto> ListPendingReviewsAsync(string adminUserId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileReviewDto>> GetReviewHistoryAsync(string adminUserId, string storedFileId, CancellationToken cancellationToken = default);
}

public sealed record FileWorkflowUpload(string FileName, string ContentType, long Length, Stream Content);
