using System.Text;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class DoctorMessageService : IDoctorMessageService
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IEgyptBusinessClock businessClock;
    private readonly IFileWorkflowService fileWorkflowService;

    public DoctorMessageService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IEgyptBusinessClock businessClock,
        IFileWorkflowService fileWorkflowService)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.businessClock = businessClock;
        this.fileWorkflowService = fileWorkflowService;
    }

    public async Task<TodayInboxDto> GetTodayInboxAsync(
        string actorUserId,
        int? pageSize = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var take = pageSize ?? DefaultPageSize;
        if (take is < 1 or > MaximumPageSize)
        {
            throw new Phase7BadRequestException("Page size must be between 1 and 100.");
        }

        var doctorId = await ResolveApprovedDoctorIdAsync(actorUserId, cancellationToken);
        var snapshot = businessClock.Capture();
        var after = DecodeCursor(cursor, doctorId, snapshot.BusinessDateEgypt);
        var rows = await domainUnitOfWork.Deliveries.ListTodayPageAsync(
            doctorId,
            snapshot.BusinessDateEgypt,
            after,
            take + 1,
            cancellationToken);

        var page = rows.Take(take).ToArray();
        var assets = await domainUnitOfWork.Deliveries.ListApprovedAssetsAsync(
            page.Select(row => row.CampaignId).Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken);
        var assetsByCampaign = assets
            .GroupBy(asset => asset.CampaignId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ApprovedDeliveryAssetReadModel>)group
                .OrderBy(asset => asset.FileId, StringComparer.Ordinal)
                .ToArray(), StringComparer.Ordinal);

        var items = page.Select(row => new TodayMessageDto(
            row.DeliveryId,
            row.CampaignId,
            row.Status,
            row.DeliveryDateEgypt,
            AsUtc(row.DeliveredAtUtc),
            row.Title,
            row.Description,
            row.ClinicalResearchInfo,
            assetsByCampaign.TryGetValue(row.CampaignId, out var campaignAssets)
                ? campaignAssets.Select(asset => new DeliveryAssetDto(
                    asset.FileId,
                    asset.Purpose,
                    asset.OriginalFileName,
                    asset.ContentType,
                    asset.SizeBytes,
                    asset.ReviewStatus,
                    $"/api/doctor/messages/{Uri.EscapeDataString(row.DeliveryId)}/assets/{Uri.EscapeDataString(asset.FileId)}/access"))
                    .ToArray()
                : Array.Empty<DeliveryAssetDto>()))
            .ToArray();

        var nextCursor = rows.Count > take && page.Length > 0
            ? EncodeCursor(new TodayInboxCursorPayload(
                doctorId,
                snapshot.BusinessDateEgypt,
                AsUtc(page[^1].DeliveredAtUtc),
                page[^1].DeliveryId))
            : null;

        return new TodayInboxDto(snapshot.BusinessDateEgypt, items, nextCursor);
    }

    public async Task<DeliveryAssetAccessGrantDto> CreateDeliveryAssetAccessGrantAsync(
        string actorUserId,
        string deliveryId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deliveryId) || string.IsNullOrWhiteSpace(fileId))
        {
            throw new Phase7NotFoundException("Delivery asset was not found.");
        }

        await ResolveApprovedDoctorIdAsync(actorUserId, cancellationToken);
        return await fileWorkflowService.CreateDeliveryAssetAccessGrantAsync(
            actorUserId,
            deliveryId,
            fileId,
            cancellationToken);
    }

    private async Task<string> ResolveApprovedDoctorIdAsync(string actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new Phase7ForbiddenException("Doctor access is required.");
        }

        var user = await identityUnitOfWork.Users.FindByIdAsync(actorUserId, cancellationToken);
        var profile = await identityUnitOfWork.Profiles.FindDoctorProfileByUserIdAsync(actorUserId, cancellationToken);
        if (user is null
            || profile is null
            || user.Role != UserRole.Doctor
            || user.AccountStatus != AccountStatus.Approved
            || user.IsDeleted
            || profile.IsDeleted
            || profile.Status != DoctorMarketplaceStatus.Active)
        {
            throw new Phase7ForbiddenException("Doctor access is required.");
        }

        return profile.Id;
    }

    private static TodayDeliveryCursor? DecodeCursor(string? cursor, string doctorId, DateOnly businessDateEgypt)
    {
        if (cursor is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cursor))
        {
            throw new Phase7BadRequestException("Cursor is invalid.");
        }

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(cursor));
            var payload = JsonSerializer.Deserialize<TodayInboxCursorPayload>(json)
                ?? throw new JsonException("Cursor payload is missing.");
            if (!string.Equals(payload.DoctorId, doctorId, StringComparison.Ordinal)
                || payload.BusinessDateEgypt != businessDateEgypt
                || payload.DeliveredAtUtc.Kind != DateTimeKind.Utc
                || string.IsNullOrWhiteSpace(payload.DeliveryId))
            {
                throw new Phase7BadRequestException("Cursor is invalid.");
            }

            return new TodayDeliveryCursor(payload.DeliveredAtUtc, payload.DeliveryId);
        }
        catch (Phase7BadRequestException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw new Phase7BadRequestException("Cursor is invalid.");
        }
    }

    private static string EncodeCursor(TodayInboxCursorPayload payload)
    {
        return Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }
}
