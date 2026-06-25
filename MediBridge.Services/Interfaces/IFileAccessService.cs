using MediBridge.Services.DTOs.Files;

namespace MediBridge.Services.Interfaces;

public interface IFileAccessService
{
    Task<FileAccessDto> GetSignedAccessAsync(
        string userId,
        string fileId,
        CancellationToken cancellationToken = default);
}
