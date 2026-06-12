using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Files;

public sealed class FileAccessGrantAudit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoredFileId { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string RequesterRole { get; set; } = string.Empty;
    public FileAccessGrantOutcome Outcome { get; set; }
    public string? Reason { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
