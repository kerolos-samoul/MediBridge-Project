using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Files;

public sealed class FileReview
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoredFileId { get; set; } = string.Empty;
    public string AdminUserId { get; set; } = string.Empty;
    public FileReviewDecision Decision { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CorrectsReviewId { get; set; }
}
