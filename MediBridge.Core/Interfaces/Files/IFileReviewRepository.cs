using MediBridge.Core.Entities.Files;

namespace MediBridge.Core.Interfaces.Files;

public interface IFileReviewRepository
{
    Task AddReviewAsync(FileReview review, CancellationToken cancellationToken = default);
    Task<FileReview?> FindByIdAsync(string reviewId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileReview>> ListByStoredFileAsync(string storedFileId, CancellationToken cancellationToken = default);
}
