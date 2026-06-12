using MediBridge.Core.Enums;
using MediBridge.Services.Config;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Validators.Files;

public sealed class FileUploadRequestValidator
{
    private readonly FileStorageOptions options;

    public FileUploadRequestValidator(FileStorageOptions options)
    {
        this.options = options;
    }

    public IReadOnlyList<string> Validate(StoredFilePurpose purpose, FileWorkflowUpload upload)
    {
        var errors = new List<string>();
        if (upload.Length <= 0)
        {
            errors.Add("File must not be empty.");
        }

        if (Path.GetFileName(upload.FileName) != upload.FileName || upload.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errors.Add("File name is unsafe.");
        }

        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) is false)
        {
            errors.Add("File extension is not allowed.");
        }

        var allowedContentTypes = GetAllowedContentTypes(purpose, extension);
        if (allowedContentTypes.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase) is false)
        {
            errors.Add("File content type does not match the requested purpose and extension.");
        }

        var maxBytes = GetMaxBytes(purpose, upload.ContentType);
        if (upload.Length > maxBytes)
        {
            errors.Add("File exceeds the configured size limit.");
        }

        return errors;
    }

    public static StoredFileStorageResourceType ResolveResourceType(string contentType)
    {
        return contentType switch
        {
            "image/jpeg" or "image/png" => StoredFileStorageResourceType.Image,
            "video/mp4" => StoredFileStorageResourceType.Video,
            _ => StoredFileStorageResourceType.Raw
        };
    }

    private string[] GetAllowedContentTypes(StoredFilePurpose purpose, string extension)
    {
        return purpose switch
        {
            StoredFilePurpose.VerificationDocument or StoredFilePurpose.ClinicalResearchAttachment
                => extension switch
                {
                    ".pdf" => ["application/pdf"],
                    ".docx" => ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
                    _ => []
                },
            StoredFilePurpose.VoiceNote => extension == ".mp3" ? options.AllowedAudioContentTypes : [],
            StoredFilePurpose.CampaignMedia
                => extension switch
                {
                    ".jpg" or ".jpeg" => ["image/jpeg"],
                    ".png" => ["image/png"],
                    ".mp4" => ["video/mp4"],
                    _ => []
                },
            _ => []
        };
    }

    private long GetMaxBytes(StoredFilePurpose purpose, string contentType)
    {
        return purpose switch
        {
            StoredFilePurpose.VerificationDocument or StoredFilePurpose.ClinicalResearchAttachment => options.DocumentMaxBytes,
            StoredFilePurpose.VoiceNote => options.AudioMaxBytes,
            StoredFilePurpose.CampaignMedia when contentType == "video/mp4" => options.VideoMaxBytes,
            StoredFilePurpose.CampaignMedia => options.ImageMaxBytes,
            _ => 0
        };
    }
}
