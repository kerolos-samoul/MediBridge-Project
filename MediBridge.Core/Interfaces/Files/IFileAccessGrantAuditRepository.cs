using MediBridge.Core.Entities.Files;

namespace MediBridge.Core.Interfaces.Files;

public interface IFileAccessGrantAuditRepository
{
    Task AddAsync(FileAccessGrantAudit audit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileAccessGrantAudit>> ListByStoredFileAsync(string storedFileId, CancellationToken cancellationToken = default);
}
