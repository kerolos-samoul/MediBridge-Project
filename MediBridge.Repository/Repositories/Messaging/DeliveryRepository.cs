using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Messaging;

public sealed class DeliveryRepository : IDeliveryRepository
{
    private readonly MediBridgeDbContext context;

    public DeliveryRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddDeliveryAsync(
        string deliveryId,
        string doctorId,
        string campaignId,
        string companyId,
        DateOnly deliveryDateEgypt,
        decimal pricePerMessageSnapshot,
        decimal platformFeePercentSnapshot,
        decimal platformFeeAmount,
        decimal doctorEarnings,
        decimal reservedAmount,
        DateTime deliveredAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (deliveredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The delivery activation timestamp must be UTC.", nameof(deliveredAtUtc));
        }

        var delivery = new DoctorAdDelivery
        {
            Id = deliveryId,
            DoctorId = doctorId,
            CampaignId = campaignId,
            CompanyId = companyId,
            DeliveryDateEgypt = deliveryDateEgypt,
            DeliveredAtUtc = deliveredAtUtc,
            CreatedAtUtc = deliveredAtUtc,
            Status = DeliveryStatus.Active,
            ReservationStatus = ReservationStatus.Reserved
        };
        delivery.ApplySettlementSnapshot(pricePerMessageSnapshot, platformFeePercentSnapshot, platformFeeAmount, doctorEarnings, reservedAmount);

        await context.DoctorAdDeliveries.AddAsync(delivery, cancellationToken);
    }

    public Task<string?> FindDeliveryIdAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries
            .Where(delivery => delivery.DoctorId == doctorId && delivery.DeliveryDateEgypt == deliveryDateEgypt && delivery.CampaignId == campaignId)
            .Select(delivery => delivery.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<DeliveryReservationReplayReadModel?> FindReservationReplayAsync(
        string doctorId,
        DateOnly deliveryDateEgypt,
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.DoctorId == doctorId
                && delivery.DeliveryDateEgypt == deliveryDateEgypt
                && delivery.CampaignId == campaignId)
            .Select(delivery => new DeliveryReservationReplayReadModel(
                delivery.Id,
                delivery.DoctorId,
                delivery.CampaignId,
                delivery.CompanyId,
                delivery.DeliveryDateEgypt,
                delivery.Status,
                delivery.ReservationStatus,
                delivery.ReservedAmount))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> DeliveryExistsAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries.AnyAsync(delivery => delivery.DoctorId == doctorId && delivery.DeliveryDateEgypt == deliveryDateEgypt && delivery.CampaignId == campaignId, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListDeliveryHistoryIdsAsync(string doctorId, DateOnly? fromDateEgypt = null, DateOnly? toDateEgypt = null, DeliveryStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = context.DoctorAdDeliveries.Where(delivery => delivery.DoctorId == doctorId);
        if (fromDateEgypt is not null)
        {
            query = query.Where(delivery => delivery.DeliveryDateEgypt >= fromDateEgypt);
        }

        if (toDateEgypt is not null)
        {
            query = query.Where(delivery => delivery.DeliveryDateEgypt <= toDateEgypt);
        }

        if (status is not null)
        {
            query = query.Where(delivery => delivery.Status == status);
        }

        return await query
            .OrderBy(delivery => delivery.DeliveryDateEgypt)
            .ThenBy(delivery => delivery.CreatedAtUtc)
            .Select(delivery => delivery.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OverdueDeliveryReadModel>> ListOverduePageAsync(
        DateOnly currentBusinessDateEgypt,
        OverdueDeliveryCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "Page size must be between 1 and 1000.");
        }

        var query = context.DoctorAdDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.DeliveryDateEgypt < currentBusinessDateEgypt
                && delivery.Status == DeliveryStatus.Active
                && delivery.ReservationStatus == ReservationStatus.Reserved);
        if (after is not null)
        {
            query = query.Where(delivery => delivery.DeliveryDateEgypt > after.DeliveryDateEgypt
                || delivery.DeliveryDateEgypt == after.DeliveryDateEgypt && delivery.CreatedAtUtc > after.CreatedAtUtc
                || delivery.DeliveryDateEgypt == after.DeliveryDateEgypt && delivery.CreatedAtUtc == after.CreatedAtUtc && string.Compare(delivery.Id, after.Id) > 0);
        }

        return await query
            .OrderBy(delivery => delivery.DeliveryDateEgypt)
            .ThenBy(delivery => delivery.CreatedAtUtc)
            .ThenBy(delivery => delivery.Id)
            .Take(take)
            .Select(delivery => new OverdueDeliveryReadModel(delivery.Id, delivery.DeliveryDateEgypt, delivery.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<DoctorAdDelivery?> FindActiveReservedForUpdateAsync(string deliveryId, CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries
            .FromSqlInterpolated($"SELECT * FROM [DoctorAdDeliveries] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {deliveryId} AND [Status] = {(int)DeliveryStatus.Active} AND [ReservationStatus] = {(int)ReservationStatus.Reserved}")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<int> CountForDoctorOnDateAsync(string doctorId, DateOnly businessDateEgypt, CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries
            .AsNoTracking()
            .CountAsync(delivery => delivery.DoctorId == doctorId && delivery.DeliveryDateEgypt == businessDateEgypt, cancellationToken);
    }

    public Task<bool> HasOverdueActiveReservedForCompanyAsync(string companyId, DateOnly currentBusinessDateEgypt, CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries
            .FromSqlInterpolated($"""
                SELECT *
                FROM [DoctorAdDeliveries] WITH (READCOMMITTEDLOCK)
                WHERE [CompanyId] = {companyId}
                    AND [DeliveryDateEgypt] < {currentBusinessDateEgypt}
                    AND [Status] = {(int)DeliveryStatus.Active}
                    AND [ReservationStatus] = {(int)ReservationStatus.Reserved}
                """)
            .AsNoTracking()
            .AnyAsync(cancellationToken);
    }

    public async Task<bool> TryMarkExpiredAndReleasedAsync(string deliveryId, DateTime expiredAtUtc, CancellationToken cancellationToken = default)
    {
        var delivery = await FindActiveReservedForUpdateAsync(deliveryId, cancellationToken);
        if (delivery is null)
        {
            return false;
        }

        delivery.MarkExpiredAndReleased(expiredAtUtc);
        return true;
    }

    public async Task<IReadOnlyList<TodayDeliveryReadModel>> ListTodayPageAsync(
        string doctorId,
        DateOnly businessDateEgypt,
        TodayDeliveryCursor? after,
        int takePlusOne,
        CancellationToken cancellationToken = default)
    {
        if (takePlusOne is < 2 or > 101)
        {
            throw new ArgumentOutOfRangeException(nameof(takePlusOne), takePlusOne, "The repository page size must be PageSize + 1, between 2 and 101.");
        }

        var query =
            from delivery in context.DoctorAdDeliveries.AsNoTracking()
            join campaign in context.Campaigns.AsNoTracking() on delivery.CampaignId equals campaign.Id
            where delivery.DoctorId == doctorId
                && delivery.DeliveryDateEgypt == businessDateEgypt
                && (delivery.Status == DeliveryStatus.Active
                    || delivery.Status == DeliveryStatus.Accepted
                    || delivery.Status == DeliveryStatus.Rejected)
            select new { delivery, campaign };
        if (after is not null)
        {
            query = query.Where(item => item.delivery.DeliveredAtUtc > after.DeliveredAtUtc
                || item.delivery.DeliveredAtUtc == after.DeliveredAtUtc && string.Compare(item.delivery.Id, after.Id) > 0);
        }

        return await query
            .OrderBy(item => item.delivery.DeliveredAtUtc)
            .ThenBy(item => item.delivery.Id)
            .Take(takePlusOne)
            .Select(item => new TodayDeliveryReadModel(
                item.delivery.Id,
                item.delivery.DoctorId,
                item.delivery.CampaignId,
                item.delivery.Status,
                item.delivery.DeliveryDateEgypt,
                item.delivery.DeliveredAtUtc,
                item.campaign.Title,
                item.campaign.Description,
                item.campaign.ClinicalResearchInfo))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovedDeliveryAssetReadModel>> ListApprovedAssetsAsync(
        IReadOnlyCollection<string> campaignIds,
        CancellationToken cancellationToken = default)
    {
        var boundedCampaignIds = campaignIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Take(100).ToArray();
        if (boundedCampaignIds.Length == 0)
        {
            return Array.Empty<ApprovedDeliveryAssetReadModel>();
        }

        return await context.StoredFiles
            .AsNoTracking()
            .Where(file => file.RelatedCampaignId != null
                && boundedCampaignIds.Contains(file.RelatedCampaignId)
                && file.UploadStatus == StoredFileUploadStatus.Stored
                && file.ReviewStatus == StoredFileReviewStatus.Approved
                && (file.Purpose == StoredFilePurpose.CampaignMedia
                    || file.Purpose == StoredFilePurpose.VoiceNote
                    || file.Purpose == StoredFilePurpose.ClinicalResearchAttachment)
                && file.DeletedAtUtc == null
                && file.ReplacedByFileId == null)
            .OrderBy(file => file.RelatedCampaignId)
            .ThenBy(file => file.Id)
            .Select(file => new ApprovedDeliveryAssetReadModel(
                file.Id,
                file.RelatedCampaignId!,
                file.Purpose,
                file.OriginalFileName,
                file.ContentType,
                file.SizeBytes,
                file.ReviewStatus))
            .ToListAsync(cancellationToken);
    }

    public Task<DeliveryAssetAuthorizationReadModel?> FindDeliveryAssetAuthorizationAsync(
        string doctorId,
        string deliveryId,
        string fileId,
        DateOnly businessDateEgypt,
        CancellationToken cancellationToken = default)
    {
        return (
            from delivery in context.DoctorAdDeliveries.AsNoTracking()
            join file in context.StoredFiles.AsNoTracking() on delivery.CampaignId equals file.RelatedCampaignId
            where delivery.Id == deliveryId
                && delivery.DoctorId == doctorId
                && delivery.DeliveryDateEgypt == businessDateEgypt
                && file.Id == fileId
                && file.UploadStatus == StoredFileUploadStatus.Stored
                && file.ReviewStatus == StoredFileReviewStatus.Approved
                && (file.Purpose == StoredFilePurpose.CampaignMedia
                    || file.Purpose == StoredFilePurpose.VoiceNote
                    || file.Purpose == StoredFilePurpose.ClinicalResearchAttachment)
                && file.DeletedAtUtc == null
                && file.ReplacedByFileId == null
            select new DeliveryAssetAuthorizationReadModel(
                delivery.Id,
                delivery.DoctorId,
                delivery.DeliveryDateEgypt,
                delivery.CampaignId,
                file.Id,
                file.StorageKey,
                file.StorageProvider,
                file.StorageResourceType,
                file.StorageDeliveryType))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<DoctorAdDelivery?> FindOwnedCurrentDayForReadAsync(
        string doctorId,
        string deliveryId,
        DateOnly businessDateEgypt,
        CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries
            .FromSqlInterpolated($"""
                SELECT *
                FROM [DoctorAdDeliveries] WITH (UPDLOCK, ROWLOCK)
                WHERE [Id] = {deliveryId}
                    AND [DoctorId] = {doctorId}
                    AND [DeliveryDateEgypt] = {businessDateEgypt}
                    AND [Status] IN ({(int)DeliveryStatus.Active}, {(int)DeliveryStatus.Accepted}, {(int)DeliveryStatus.Rejected})
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ReadTrackingReplayReadModel?> TryMarkReadAsync(
        string deliveryId,
        DateTime readAtUtc,
        CancellationToken cancellationToken = default)
    {
        var delivery = context.DoctorAdDeliveries.Local.FirstOrDefault(candidate => candidate.Id == deliveryId)
            ?? await context.DoctorAdDeliveries.FirstOrDefaultAsync(candidate => candidate.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        var alreadyRead = delivery.ReadAtUtc is not null;
        delivery.MarkRead(readAtUtc);
        return new ReadTrackingReplayReadModel(delivery.Id, delivery.Status, delivery.ReadAtUtc!.Value, alreadyRead);
    }

    public Task<DoctorAdDelivery?> FindOwnedActiveReservedForInteractionAsync(
        string doctorId,
        string deliveryId,
        DateOnly businessDateEgypt,
        CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries
            .FromSqlInterpolated($"""
                SELECT *
                FROM [DoctorAdDeliveries] WITH (UPDLOCK, ROWLOCK)
                WHERE [Id] = {deliveryId}
                    AND [DoctorId] = {doctorId}
                    AND [DeliveryDateEgypt] = {businessDateEgypt}
                    AND [Status] = {(int)DeliveryStatus.Active}
                    AND [ReservationStatus] = {(int)ReservationStatus.Reserved}
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<InteractionReplayReadModel?> FindSettledInteractionReplayAsync(
        string doctorId,
        string deliveryId,
        CancellationToken cancellationToken = default)
    {
        return (
            from delivery in context.DoctorAdDeliveries.AsNoTracking()
            join interaction in context.DeliveryInteractions.AsNoTracking() on delivery.Id equals interaction.DeliveryId
            where delivery.Id == deliveryId
                && delivery.DoctorId == doctorId
                && delivery.ReservationStatus == ReservationStatus.Charged
                && (delivery.Status == DeliveryStatus.Accepted || delivery.Status == DeliveryStatus.Rejected)
            select new InteractionReplayReadModel(
                delivery.Id,
                delivery.Status,
                delivery.ReservationStatus,
                delivery.InteractedAtUtc!.Value,
                delivery.ReadAtUtc,
                interaction.Outcome,
                interaction.FeedbackText,
                interaction.FeedbackQualifiesForScore,
                delivery.ReservedAmount,
                delivery.DoctorEarnings,
                delivery.PlatformFeeAmount,
                interaction.RequestFingerprint,
                interaction.IdempotencyKeyHash))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryMarkInteractedAndChargedAsync(
        string deliveryId,
        DeliveryInteractionOutcome outcome,
        DateTime interactedAtUtc,
        string? feedbackText,
        FeedbackQualityStatus? feedbackQualityStatus,
        CancellationToken cancellationToken = default)
    {
        var delivery = context.DoctorAdDeliveries.Local.FirstOrDefault(candidate => candidate.Id == deliveryId)
            ?? await context.DoctorAdDeliveries.FirstOrDefaultAsync(candidate => candidate.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return false;
        }

        delivery.MarkInteractedAndCharged(outcome, interactedAtUtc, feedbackText, feedbackQualityStatus);
        return true;
    }

    public async Task<ActivityScoreAggregateReadModel> GetActivityScoreAggregateAsync(
        string doctorId,
        DateOnly windowStartDateEgypt,
        DateOnly windowEndDateEgypt,
        CancellationToken cancellationToken = default)
    {
        if (windowEndDateEgypt < windowStartDateEgypt)
        {
            throw new ArgumentException("Activity score window end cannot be before start.", nameof(windowEndDateEgypt));
        }

        var rows = await context.DoctorAdDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.DoctorId == doctorId
                && delivery.DeliveryDateEgypt >= windowStartDateEgypt
                && delivery.DeliveryDateEgypt <= windowEndDateEgypt
                && (delivery.Status == DeliveryStatus.Active
                    || delivery.Status == DeliveryStatus.Accepted
                    || delivery.Status == DeliveryStatus.Rejected
                    || delivery.Status == DeliveryStatus.Expired))
            .Select(delivery => new
            {
                delivery.Status,
                delivery.DeliveredAtUtc,
                delivery.InteractedAtUtc,
                delivery.FeedbackText
            })
            .ToListAsync(cancellationToken);

        var interactedRows = rows
            .Where(row => row.Status is DeliveryStatus.Accepted or DeliveryStatus.Rejected && row.InteractedAtUtc is not null)
            .ToArray();
        var contributionSum = interactedRows.Sum(row =>
        {
            var responseHours = (decimal)(row.InteractedAtUtc!.Value - row.DeliveredAtUtc).TotalHours;
            var contribution = (1m - responseHours / 24m) * 100m;
            return Math.Clamp(contribution, 0m, 100m);
        });
        var feedbackQualified = interactedRows.Count(row => CountNonWhitespace(row.FeedbackText) >= 15);

        return new ActivityScoreAggregateReadModel(
            doctorId,
            rows.Count,
            interactedRows.Length,
            feedbackQualified,
            contributionSum,
            interactedRows.Length);
    }

    public async Task<WeeklyInteractionCountReadModel> GetWeeklyInteractionCountAsync(
        string doctorId,
        DateOnly weekStartDateEgypt,
        DateOnly weekEndDateEgypt,
        CancellationToken cancellationToken = default)
    {
        if (weekStartDateEgypt.DayOfWeek != DayOfWeek.Monday || weekEndDateEgypt != weekStartDateEgypt.AddDays(7))
        {
            throw new ArgumentException("Weekly interaction counts require a Monday-to-Monday Cairo week.", nameof(weekStartDateEgypt));
        }

        var count = await context.DoctorAdDeliveries
            .AsNoTracking()
            .CountAsync(delivery => delivery.DoctorId == doctorId
                && delivery.DeliveryDateEgypt >= weekStartDateEgypt
                && delivery.DeliveryDateEgypt < weekEndDateEgypt
                && (delivery.Status == DeliveryStatus.Accepted || delivery.Status == DeliveryStatus.Rejected)
                && delivery.InteractedAtUtc != null,
                cancellationToken);

        return new WeeklyInteractionCountReadModel(doctorId, weekStartDateEgypt, weekEndDateEgypt, count);
    }

    private static int CountNonWhitespace(string? value)
    {
        return string.IsNullOrEmpty(value) ? 0 : value.Count(character => !char.IsWhiteSpace(character));
    }

    public Task<IReadOnlyList<CompanyReportingDeliveryAggregateReadModel>> ListCompanyCampaignDeliveryAggregatesAsync(
        string companyId,
        IReadOnlyCollection<string> campaignIds,
        CompanyReportingDateRange dateRange,
        CancellationToken cancellationToken = default)
    {
        var scopedCampaignIds = campaignIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (scopedCampaignIds.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<CompanyReportingDeliveryAggregateReadModel>>(Array.Empty<CompanyReportingDeliveryAggregateReadModel>());
        }

        return ListCompanyCampaignDeliveryAggregatesCoreAsync(companyId, scopedCampaignIds, dateRange, cancellationToken);
    }

    public Task<CompanyReportingPageReadModel<CampaignDeliveryReportRowReadModel>> ListCompanyCampaignDeliveryReportsAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CompanyReportingDeliveryFilters filters,
        CompanyReportingPagination pagination,
        CancellationToken cancellationToken = default)
    {
        return ListCompanyCampaignDeliveryReportsCoreAsync(companyId, campaignId, dateRange, filters, pagination, cancellationToken);
    }

    public Task<CompanyReportingPageReadModel<CampaignFeedbackReportRowReadModel>> ListCompanyCampaignFeedbackReportsAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CompanyReportingFeedbackFilters filters,
        CompanyReportingPagination pagination,
        CancellationToken cancellationToken = default)
    {
        return ListCompanyCampaignFeedbackReportsCoreAsync(companyId, campaignId, dateRange, filters, pagination, cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyReportingDeliverySourceReadModel>> ListCompanyCampaignAnalyticsDeliveriesAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CancellationToken cancellationToken = default)
    {
        return await QueryCompanyCampaignDeliveries(companyId, campaignId, dateRange)
            .OrderBy(delivery => delivery.DeliveryDateEgypt)
            .ThenBy(delivery => delivery.Id)
            .Select(delivery => new CompanyReportingDeliverySourceReadModel(
                delivery.Id,
                delivery.CampaignId,
                delivery.Status,
                delivery.ReservationStatus,
                delivery.DeliveryDateEgypt,
                delivery.ReservedAmount,
                delivery.PricePerMessageSnapshot,
                delivery.DoctorEarnings,
                delivery.PlatformFeeAmount,
                delivery.FeedbackText != null && delivery.FeedbackText.Trim() != string.Empty))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListCompanyCampaignDeliveryIdsAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CancellationToken cancellationToken = default)
    {
        return await QueryCompanyCampaignDeliveries(companyId, campaignId, dateRange)
            .OrderBy(delivery => delivery.Id)
            .Select(delivery => delivery.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CompanyReportingDeliveryAggregateReadModel>> ListCompanyCampaignDeliveryAggregatesCoreAsync(
        string companyId,
        IReadOnlyCollection<string> campaignIds,
        CompanyReportingDateRange dateRange,
        CancellationToken cancellationToken)
    {
        return await context.DoctorAdDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.CompanyId == companyId
                && campaignIds.Contains(delivery.CampaignId)
                && delivery.DeliveryDateEgypt >= dateRange.FromDateEgypt
                && delivery.DeliveryDateEgypt <= dateRange.ToDateEgypt)
            .GroupBy(delivery => delivery.CampaignId)
            .Select(group => new CompanyReportingDeliveryAggregateReadModel(
                group.Key,
                group.Count(),
                group.Count(delivery => delivery.Status == DeliveryStatus.Active),
                group.Count(delivery => delivery.Status == DeliveryStatus.Accepted),
                group.Count(delivery => delivery.Status == DeliveryStatus.Rejected),
                group.Count(delivery => delivery.Status == DeliveryStatus.Expired),
                group.Count(delivery => delivery.FeedbackText != null && delivery.FeedbackText.Trim() != string.Empty),
                group.Where(delivery => delivery.Status == DeliveryStatus.Active).Sum(delivery => delivery.ReservedAmount),
                group.Where(delivery => delivery.Status == DeliveryStatus.Accepted || delivery.Status == DeliveryStatus.Rejected).Sum(delivery => delivery.PricePerMessageSnapshot),
                group.Where(delivery => delivery.Status == DeliveryStatus.Accepted || delivery.Status == DeliveryStatus.Rejected).Sum(delivery => delivery.DoctorEarnings),
                group.Where(delivery => delivery.Status == DeliveryStatus.Accepted || delivery.Status == DeliveryStatus.Rejected).Sum(delivery => delivery.PlatformFeeAmount),
                group.Select(delivery => (DateTime?)delivery.DeliveredAtUtc).Max(),
                group.Select(delivery => delivery.ReadAtUtc).Max(),
                group.Select(delivery => delivery.InteractedAtUtc).Max(),
                group.Select(delivery => delivery.FeedbackCreatedAtUtc).Max()))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<DoctorAdDelivery> QueryCompanyCampaignDeliveries(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange)
        => context.DoctorAdDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.CompanyId == companyId
                && delivery.CampaignId == campaignId
                && delivery.DeliveryDateEgypt >= dateRange.FromDateEgypt
                && delivery.DeliveryDateEgypt <= dateRange.ToDateEgypt);

    private async Task<CompanyReportingPageReadModel<CampaignDeliveryReportRowReadModel>> ListCompanyCampaignDeliveryReportsCoreAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CompanyReportingDeliveryFilters filters,
        CompanyReportingPagination pagination,
        CancellationToken cancellationToken)
    {
        var query =
            from delivery in QueryCompanyCampaignDeliveries(companyId, campaignId, dateRange)
            join doctor in context.DoctorProfiles.AsNoTracking() on delivery.DoctorId equals doctor.Id
            select new { delivery, doctor };

        if (filters.Status is not null)
        {
            query = query.Where(item => item.delivery.Status == filters.Status);
        }

        query = filters.State switch
        {
            CompanyReportingDeliveryState.Read => query.Where(item => item.delivery.ReadAtUtc != null),
            CompanyReportingDeliveryState.Unread => query.Where(item => item.delivery.ReadAtUtc == null),
            CompanyReportingDeliveryState.Interacted => query.Where(item => item.delivery.InteractedAtUtc != null),
            CompanyReportingDeliveryState.Uninteracted => query.Where(item => item.delivery.InteractedAtUtc == null),
            _ => query
        };

        if (filters.DoctorSpecialization is not null)
        {
            query = query.Where(item => item.doctor.Specialization == filters.DoctorSpecialization);
        }

        if (filters.DoctorLocation is not null)
        {
            query = query.Where(item => item.doctor.Location == filters.DoctorLocation);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.delivery.DeliveryDateEgypt)
            .ThenByDescending(item => item.delivery.DeliveredAtUtc)
            .ThenBy(item => item.delivery.Id)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(item => new CampaignDeliveryReportRowReadModel(
                item.delivery.Id,
                item.delivery.CampaignId,
                new PublicDoctorSummaryReadModel(
                    item.doctor.Id,
                    item.doctor.Specialization,
                    item.doctor.ExperienceYears <= 5 ? "0-5 years" : item.doctor.ExperienceYears <= 10 ? "6-10 years" : "11+ years",
                    item.doctor.Location),
                item.delivery.DeliveryDateEgypt,
                item.delivery.DeliveredAtUtc,
                item.delivery.ReadAtUtc,
                item.delivery.Status,
                item.delivery.InteractedAtUtc,
                item.delivery.PricePerMessageSnapshot,
                item.delivery.Status == DeliveryStatus.Active ? item.delivery.ReservedAmount : 0m,
                item.delivery.Status == DeliveryStatus.Accepted || item.delivery.Status == DeliveryStatus.Rejected ? item.delivery.PricePerMessageSnapshot : 0m,
                item.delivery.Status == DeliveryStatus.Accepted || item.delivery.Status == DeliveryStatus.Rejected ? item.delivery.DoctorEarnings : 0m,
                item.delivery.Status == DeliveryStatus.Accepted || item.delivery.Status == DeliveryStatus.Rejected ? item.delivery.PlatformFeeAmount : 0m))
            .ToListAsync(cancellationToken);

        return new CompanyReportingPageReadModel<CampaignDeliveryReportRowReadModel>(items, totalCount);
    }

    private async Task<CompanyReportingPageReadModel<CampaignFeedbackReportRowReadModel>> ListCompanyCampaignFeedbackReportsCoreAsync(
        string companyId,
        string campaignId,
        CompanyReportingDateRange dateRange,
        CompanyReportingFeedbackFilters filters,
        CompanyReportingPagination pagination,
        CancellationToken cancellationToken)
    {
        var query =
            from delivery in QueryCompanyCampaignDeliveries(companyId, campaignId, dateRange)
            join doctor in context.DoctorProfiles.AsNoTracking() on delivery.DoctorId equals doctor.Id
            where delivery.FeedbackText != null
                && delivery.FeedbackText.Trim() != string.Empty
                && delivery.FeedbackCreatedAtUtc != null
                && (delivery.Status == DeliveryStatus.Accepted || delivery.Status == DeliveryStatus.Rejected)
            select new { delivery, doctor };

        if (filters.Outcome is not null)
        {
            var status = filters.Outcome == CompanyReportingFeedbackOutcome.Accepted
                ? DeliveryStatus.Accepted
                : DeliveryStatus.Rejected;
            query = query.Where(item => item.delivery.Status == status);
        }

        if (filters.FeedbackEligibility is not null)
        {
            query = filters.FeedbackEligibility == CompanyReportingFeedbackEligibility.Eligible
                ? query.Where(item => item.delivery.FeedbackQualityStatus == FeedbackQualityStatus.Accepted)
                : query.Where(item => item.delivery.FeedbackQualityStatus != FeedbackQualityStatus.Accepted);
        }

        if (filters.DoctorSpecialization is not null)
        {
            query = query.Where(item => item.doctor.Specialization == filters.DoctorSpecialization);
        }

        if (filters.DoctorLocation is not null)
        {
            query = query.Where(item => item.doctor.Location == filters.DoctorLocation);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.delivery.FeedbackCreatedAtUtc)
            .ThenBy(item => item.delivery.Id)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(item => new CampaignFeedbackReportRowReadModel(
                item.delivery.Id,
                item.delivery.CampaignId,
                item.delivery.Status == DeliveryStatus.Accepted ? CompanyReportingFeedbackOutcome.Accepted : CompanyReportingFeedbackOutcome.Rejected,
                item.delivery.FeedbackText!.Trim(),
                item.delivery.FeedbackCreatedAtUtc!.Value,
                item.delivery.FeedbackQualityStatus == FeedbackQualityStatus.Accepted,
                new PublicDoctorSummaryReadModel(
                    item.doctor.Id,
                    item.doctor.Specialization,
                    item.doctor.ExperienceYears <= 5 ? "0-5 years" : item.doctor.ExperienceYears <= 10 ? "6-10 years" : "11+ years",
                    item.doctor.Location)))
            .ToListAsync(cancellationToken);

        return new CompanyReportingPageReadModel<CampaignFeedbackReportRowReadModel>(items, totalCount);
    }
}
