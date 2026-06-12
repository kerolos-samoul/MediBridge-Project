namespace MediBridge.Services.Config;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public bool UploadsEnabled { get; set; } = true;
    public long DocumentMaxBytes { get; set; } = 10 * 1024 * 1024;
    public long ImageMaxBytes { get; set; } = 10 * 1024 * 1024;
    public long AudioMaxBytes { get; set; } = 25 * 1024 * 1024;
    public long VideoMaxBytes { get; set; } = 100 * 1024 * 1024;
    public int AccessGrantLifetimeMinutes { get; set; } = 10;
    public string[] AllowedDocumentContentTypes { get; set; } =
    [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    ];
    public string[] AllowedImageContentTypes { get; set; } = ["image/jpeg", "image/png"];
    public string[] AllowedAudioContentTypes { get; set; } = ["audio/mpeg"];
    public string[] AllowedVideoContentTypes { get; set; } = ["video/mp4"];
    public string[] AllowedExtensions { get; set; } = [".pdf", ".docx", ".jpg", ".jpeg", ".png", ".mp3", ".mp4"];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        RequireExact(DocumentMaxBytes, 10 * 1024 * 1024, nameof(DocumentMaxBytes), errors);
        RequireExact(ImageMaxBytes, 10 * 1024 * 1024, nameof(ImageMaxBytes), errors);
        RequireExact(AudioMaxBytes, 25 * 1024 * 1024, nameof(AudioMaxBytes), errors);
        RequireExact(VideoMaxBytes, 100 * 1024 * 1024, nameof(VideoMaxBytes), errors);
        RequireExact(AccessGrantLifetimeMinutes, 10, nameof(AccessGrantLifetimeMinutes), errors);
        RequireSet(AllowedDocumentContentTypes, ["application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"], "document content types", errors);
        RequireSet(AllowedImageContentTypes, ["image/jpeg", "image/png"], "image content types", errors);
        RequireSet(AllowedAudioContentTypes, ["audio/mpeg"], "audio content types", errors);
        RequireSet(AllowedVideoContentTypes, ["video/mp4"], "video content types", errors);
        RequireSet(AllowedExtensions, [".pdf", ".docx", ".jpg", ".jpeg", ".png", ".mp3", ".mp4"], "extensions", errors);
        return errors;
    }

    private static void RequireExact(long actual, long expected, string name, ICollection<string> errors)
    {
        if (actual != expected)
        {
            errors.Add($"{name} must be {expected}.");
        }
    }

    private static void RequireSet(IEnumerable<string> actual, IEnumerable<string> expected, string name, ICollection<string> errors)
    {
        if (actual.Select(value => value.ToLowerInvariant()).OrderBy(value => value)
            .SequenceEqual(expected.Select(value => value.ToLowerInvariant()).OrderBy(value => value)) is false)
        {
            errors.Add($"Allowed {name} must match Phase 4 requirements exactly.");
        }
    }
}
