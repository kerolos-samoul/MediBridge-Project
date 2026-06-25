using FluentValidation;
using MediBridge.Services.DTOs.Campaigns;

namespace MediBridge.Services.Validators.Campaigns;

public sealed class CampaignAssetUploadRequestValidator : AbstractValidator<CampaignAssetUploadRequestDto>
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedExtensionsByContentType =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = [".jpg", ".jpeg"],
            ["image/png"] = [".png"],
            ["image/webp"] = [".webp"],
            ["video/mp4"] = [".mp4"],
            ["video/webm"] = [".webm"],
            ["video/quicktime"] = [".mov"]
        };

    private const long MaxCampaignAssetBytes = 50 * 1024 * 1024;

    public CampaignAssetUploadRequestValidator()
    {
        RuleFor(request => request.OriginalFileName)
            .NotEmpty()
            .MaximumLength(255)
            .Must(BeSafeFileName);
        RuleFor(request => request.ContentType)
            .NotEmpty()
            .MaximumLength(120)
            .Must(contentType => AllowedExtensionsByContentType.ContainsKey(contentType));
        RuleFor(request => request.SizeBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(MaxCampaignAssetBytes);
        RuleFor(request => request)
            .Must(HaveMatchingExtension);
    }

    private static bool BeSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            && !fileName.Contains("..", StringComparison.Ordinal);
    }

    private static bool HaveMatchingExtension(CampaignAssetUploadRequestDto request)
    {
        if (!AllowedExtensionsByContentType.TryGetValue(request.ContentType, out var allowedExtensions))
        {
            return false;
        }

        var extension = Path.GetExtension(request.OriginalFileName);
        return allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
