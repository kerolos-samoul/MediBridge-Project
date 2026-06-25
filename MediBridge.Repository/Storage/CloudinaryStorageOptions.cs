namespace MediBridge.Repository.Storage;

public sealed class CloudinaryStorageOptions
{
    public const string SectionName = "CloudinaryStorage";

    public string CloudinaryUrl { get; set; } = string.Empty;
    public string FolderPrefix { get; set; } = "medibridge";
    public bool UseSecureUrls { get; set; } = true;
    public int SignedUrlMinutes { get; set; } = 5;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CloudinaryUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(FolderPrefix);

        if (SignedUrlMinutes is < 1 or > 60)
        {
            throw new InvalidOperationException("Cloudinary signed URL lifetime must be between 1 and 60 minutes.");
        }
    }
}
