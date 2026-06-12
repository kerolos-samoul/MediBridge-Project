using MediBridge.Core.Entities.Files;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Files;

public sealed class FileAccessGrantAuditRepository : IFileAccessGrantAuditRepository
{
    private readonly MediBridgeDbContext context;

    public FileAccessGrantAuditRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddAsync(FileAccessGrantAudit audit, CancellationToken cancellationToken = default)
    {
        if (ContainsSecretLikeMaterial(audit.Reason))
        {
            audit.Reason = "Grant audit reason redacted.";
        }

        await context.FileAccessGrantAudits.AddAsync(audit, cancellationToken);
    }

    public async Task<IReadOnlyList<FileAccessGrantAudit>> ListByStoredFileAsync(string storedFileId, CancellationToken cancellationToken = default)
    {
        return await context.FileAccessGrantAudits
            .Where(audit => audit.StoredFileId == storedFileId)
            .OrderBy(audit => audit.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    private static bool ContainsSecretLikeMaterial(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               (value.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("cloudinary://", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("api_secret", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }
}
