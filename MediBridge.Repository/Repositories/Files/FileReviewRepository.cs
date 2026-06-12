using MediBridge.Core.Entities.Files;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Files;

public sealed class FileReviewRepository : IFileReviewRepository
{
    private readonly MediBridgeDbContext context;

    public FileReviewRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddReviewAsync(FileReview review, CancellationToken cancellationToken = default)
    {
        await context.FileReviews.AddAsync(review, cancellationToken);
    }

    public Task<FileReview?> FindByIdAsync(string reviewId, CancellationToken cancellationToken = default)
    {
        return context.FileReviews.FirstOrDefaultAsync(review => review.Id == reviewId, cancellationToken);
    }

    public async Task<IReadOnlyList<FileReview>> ListByStoredFileAsync(string storedFileId, CancellationToken cancellationToken = default)
    {
        return await context.FileReviews
            .Where(review => review.StoredFileId == storedFileId)
            .OrderBy(review => review.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
