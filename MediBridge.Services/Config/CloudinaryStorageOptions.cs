namespace MediBridge.Services.Config;

public sealed class CloudinaryStorageOptions
{
    public const string SectionName = "CloudinaryStorage";
    public const string SecretEnvironmentVariableName = "CLOUDINARY_URL";

    public string? CloudName { get; set; }
    public string? CloudinaryUrl { get; set; }
    public bool UseSecureUrls { get; set; } = true;
    public string FolderPrefix { get; set; } = "medibridge";

    public IReadOnlyList<string> Validate(bool uploadEnabled, string? cloudinaryUrl = null)
    {
        var resolvedCloudinaryUrl = cloudinaryUrl ?? ResolveCloudinaryUrl();
        var errors = new List<string>();
        if (UseSecureUrls is false)
        {
            errors.Add("Cloudinary secure URLs must be enabled.");
        }

        if (string.IsNullOrWhiteSpace(FolderPrefix))
        {
            errors.Add("Cloudinary folder prefix is required.");
        }

        if (uploadEnabled && string.IsNullOrWhiteSpace(resolvedCloudinaryUrl))
        {
            errors.Add($"{SecretEnvironmentVariableName} environment variable is required when uploads are enabled.");
        }

        return errors;
    }

    public string? ResolveCloudinaryUrl()
    {
        var environmentValue = ReadSecretFromEnvironment();
        return string.IsNullOrWhiteSpace(environmentValue)
            ? CloudinaryUrl
            : environmentValue;
    }

    public static string? ReadSecretFromEnvironment()
    {
        return Environment.GetEnvironmentVariable(SecretEnvironmentVariableName);
    }
}
