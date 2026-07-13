using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Campaigns;

namespace MediBridge.Services.Services;

public sealed class CompanyReportingService : ICompanyReportingService
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new() { PropertyNamingPolicy = null };
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly CompanyReportingDateRangeValidator dateRangeValidator;
    private readonly CompanyReportingPaginationValidator paginationValidator;
    private readonly CompanyReportingFilterValidator filterValidator;

    public CompanyReportingService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        CompanyReportingDateRangeValidator dateRangeValidator,
        CompanyReportingPaginationValidator paginationValidator,
        CompanyReportingFilterValidator filterValidator)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.dateRangeValidator = dateRangeValidator;
        this.paginationValidator = paginationValidator;
        this.filterValidator = filterValidator;
    }

    public async Task<CampaignReportPageDto> GetCompanyCampaignReportsAsync(
        string actorUserId,
        string? fromDateEgypt,
        string? toDateEgypt,
        string? status,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        var dateRange = dateRangeValidator.Resolve(fromDateEgypt, toDateEgypt);
        var pagination = paginationValidator.Resolve(pageNumber, pageSize);
        var campaignStatus = ParseOptionalCampaignStatus(status);
        var actor = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);

        var page = await domainUnitOfWork.Campaigns.ListCompanyCampaignReportSummariesAsync(
            actor.CompanyId,
            campaignStatus,
            dateRange,
            pagination,
            cancellationToken);
        if (page.Items.Count == 0)
        {
            return CampaignReportingDtoMapper.ToCampaignReportPage(page, pagination);
        }

        var campaignIds = page.Items.Select(item => item.CampaignId).ToArray();
        var aggregates = await domainUnitOfWork.Deliveries.ListCompanyCampaignDeliveryAggregatesAsync(
            actor.CompanyId,
            campaignIds,
            dateRange,
            cancellationToken);
        var aggregatesByCampaignId = aggregates.ToDictionary(aggregate => aggregate.CampaignId, StringComparer.Ordinal);
        var reconciledSummaries = new List<CampaignReportSummaryReadModel>(page.Items.Count);

        foreach (var summary in page.Items)
        {
            var merged = MergeSummary(summary, aggregatesByCampaignId.GetValueOrDefault(summary.CampaignId));
            var countReconciliation = CompanyReportingCalculations.ClassifySummaryCountReconciliation(merged, dateRange);
            if (!countReconciliation.IsConsistent)
            {
                await RecordDiscrepancyAndFailAsync(actor, countReconciliation, cancellationToken);
            }

            var deliverySources = await domainUnitOfWork.Deliveries.ListCompanyCampaignAnalyticsDeliveriesAsync(
                actor.CompanyId,
                summary.CampaignId,
                dateRange,
                cancellationToken);
            var evidence = await domainUnitOfWork.WalletTransactions.ListFinancialEvidenceByDeliveryIdsAsync(
                deliverySources.Select(delivery => delivery.DeliveryId).ToArray(),
                cancellationToken);
            var financialReconciliation = CompanyReportingCalculations.ClassifyFinancialReconciliation(
                summary.CampaignId,
                dateRange,
                deliverySources,
                evidence);
            if (!financialReconciliation.IsConsistent)
            {
                await RecordDiscrepancyAndFailAsync(actor, financialReconciliation, cancellationToken);
            }

            reconciledSummaries.Add(merged);
        }

        return CampaignReportingDtoMapper.ToCampaignReportPage(
            new CompanyReportingPageReadModel<CampaignReportSummaryReadModel>(reconciledSummaries, page.TotalCount),
            pagination);
    }

    public Task<DeliveryReportPageDto> GetCampaignDeliveryReportsAsync(
        string actorUserId,
        string campaignId,
        string? fromDateEgypt,
        string? toDateEgypt,
        string? status,
        string? state,
        string? doctorSpecialization,
        string? doctorLocation,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        return GetCampaignDeliveryReportsCoreAsync(
            actorUserId,
            campaignId,
            fromDateEgypt,
            toDateEgypt,
            status,
            state,
            doctorSpecialization,
            doctorLocation,
            pageNumber,
            pageSize,
            cancellationToken);
    }

    public Task<FeedbackReportPageDto> GetCampaignFeedbackReportsAsync(
        string actorUserId,
        string campaignId,
        string? fromDateEgypt,
        string? toDateEgypt,
        string? outcome,
        string? feedbackEligibility,
        string? doctorSpecialization,
        string? doctorLocation,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        return GetCampaignFeedbackReportsCoreAsync(
            actorUserId,
            campaignId,
            fromDateEgypt,
            toDateEgypt,
            outcome,
            feedbackEligibility,
            doctorSpecialization,
            doctorLocation,
            pageNumber,
            pageSize,
            cancellationToken);
    }

    public Task<CampaignAnalyticsDto> GetCampaignAnalyticsAsync(
        string actorUserId,
        string campaignId,
        string? fromDateEgypt,
        string? toDateEgypt,
        CancellationToken cancellationToken = default)
    {
        return GetCampaignAnalyticsCoreAsync(actorUserId, campaignId, fromDateEgypt, toDateEgypt, cancellationToken);
    }

    private async Task<CompanyReportingActor> ResolveApprovedCompanyActorAsync(string actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var user = await identityUnitOfWork.Users.FindByIdAsync(actorUserId, cancellationToken);
        var profile = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(actorUserId, cancellationToken);
        if (user is { Role: UserRole.Company, AccountStatus: AccountStatus.Approved, IsDeleted: false } && profile is { IsDeleted: false })
        {
            return new CompanyReportingActor(profile.Id, user.Role.ToString(), actorUserId);
        }

        throw new Phase5ForbiddenException("Forbidden.");
    }

    private async Task RecordDiscrepancyAndFailAsync(
        CompanyReportingActor actor,
        CompanyReportingReconciliationReadModel discrepancy,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            actor.CompanyId,
            discrepancy.CampaignId,
            discrepancy.ReportKind,
            Category = discrepancy.Category?.ToString(),
            FromDateEgypt = discrepancy.FromDateEgypt.ToString("yyyy-MM-dd"),
            ToDateEgypt = discrepancy.ToDateEgypt.ToString("yyyy-MM-dd"),
            discrepancy.ExpectedAmount,
            discrepancy.ActualAmount,
            discrepancy.AffectedDeliveryCount,
            DetectedAtUtc = DateTime.UtcNow,
            Outcome = "Blocked"
        }, AuditJsonOptions);

        await domainUnitOfWork.AuditEvents.AddReportingDiscrepancyAsync(
            Guid.NewGuid().ToString("N"),
            actor.ActorUserId,
            actor.ActorRole,
            discrepancy.CampaignId,
            metadata,
            DateTime.UtcNow,
            cancellationToken);
        await domainUnitOfWork.SaveChangesAsync(cancellationToken);
        throw new Phase5ConflictException("Reporting source evidence does not reconcile.");
    }

    private static CampaignStatus? ParseOptionalCampaignStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        if (!Enum.TryParse<CampaignStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new Phase5ValidationException("Validation failed.", ["status is not supported."]);
        }

        return parsed;
    }

    private static CampaignReportSummaryReadModel MergeSummary(
        CampaignReportSummaryReadModel summary,
        CompanyReportingDeliveryAggregateReadModel? aggregate)
    {
        if (aggregate is null)
        {
            return summary;
        }

        return summary with
        {
            DeliveredCount = aggregate.DeliveredCount,
            ActiveUnansweredCount = aggregate.ActiveUnansweredCount,
            AcceptedCount = aggregate.AcceptedCount,
            RejectedCount = aggregate.RejectedCount,
            ExpiredCount = aggregate.ExpiredCount,
            FeedbackCount = aggregate.FeedbackCount,
            ReservedAmount = aggregate.ReservedAmount,
            ChargedSpend = aggregate.ChargedSpend,
            DoctorEarnings = aggregate.DoctorEarnings,
            PlatformFee = aggregate.PlatformFee,
            CampaignActivityAtUtc = MaxUtc(
                summary.CampaignActivityAtUtc,
                aggregate.LatestDeliveredAtUtc,
                aggregate.LatestReadAtUtc,
                aggregate.LatestInteractedAtUtc,
                aggregate.LatestFeedbackCreatedAtUtc)
        };
    }

    private static DateTime MaxUtc(params DateTime?[] values)
        => values.Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(DateTime.MinValue).Max();

    private sealed record CompanyReportingActor(string CompanyId, string ActorRole, string ActorUserId);

    private async Task<DeliveryReportPageDto> GetCampaignDeliveryReportsCoreAsync(
        string actorUserId,
        string campaignId,
        string? fromDateEgypt,
        string? toDateEgypt,
        string? status,
        string? state,
        string? doctorSpecialization,
        string? doctorLocation,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new Phase5NotFoundException("Campaign was not found.");
        }

        var dateRange = dateRangeValidator.Resolve(fromDateEgypt, toDateEgypt);
        var pagination = paginationValidator.Resolve(pageNumber, pageSize);
        var filters = filterValidator.ResolveDeliveryFilters(status, state, doctorSpecialization, doctorLocation);
        var actor = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        var isOwned = await domainUnitOfWork.Campaigns.IsReportVisibleCampaignOwnedByCompanyAsync(
            actor.CompanyId,
            campaignId.Trim(),
            cancellationToken);
        if (!isOwned)
        {
            throw new Phase5NotFoundException("Campaign was not found.");
        }

        var page = await domainUnitOfWork.Deliveries.ListCompanyCampaignDeliveryReportsAsync(
            actor.CompanyId,
            campaignId.Trim(),
            dateRange,
            filters,
            pagination,
            cancellationToken);
        return CampaignReportingDtoMapper.ToDeliveryReportPage(page, pagination);
    }

    private async Task<FeedbackReportPageDto> GetCampaignFeedbackReportsCoreAsync(
        string actorUserId,
        string campaignId,
        string? fromDateEgypt,
        string? toDateEgypt,
        string? outcome,
        string? feedbackEligibility,
        string? doctorSpecialization,
        string? doctorLocation,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new Phase5NotFoundException("Campaign was not found.");
        }

        var dateRange = dateRangeValidator.Resolve(fromDateEgypt, toDateEgypt);
        var pagination = paginationValidator.Resolve(pageNumber, pageSize);
        var filters = filterValidator.ResolveFeedbackFilters(outcome, feedbackEligibility, doctorSpecialization, doctorLocation);
        var actor = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        var isOwned = await domainUnitOfWork.Campaigns.IsReportVisibleCampaignOwnedByCompanyAsync(
            actor.CompanyId,
            campaignId.Trim(),
            cancellationToken);
        if (!isOwned)
        {
            throw new Phase5NotFoundException("Campaign was not found.");
        }

        var page = await domainUnitOfWork.Deliveries.ListCompanyCampaignFeedbackReportsAsync(
            actor.CompanyId,
            campaignId.Trim(),
            dateRange,
            filters,
            pagination,
            cancellationToken);
        return CampaignReportingDtoMapper.ToFeedbackReportPage(page, pagination);
    }

    private async Task<CampaignAnalyticsDto> GetCampaignAnalyticsCoreAsync(
        string actorUserId,
        string campaignId,
        string? fromDateEgypt,
        string? toDateEgypt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new Phase5NotFoundException("Campaign was not found.");
        }

        var dateRange = dateRangeValidator.Resolve(fromDateEgypt, toDateEgypt);
        var actor = await ResolveApprovedCompanyActorAsync(actorUserId, cancellationToken);
        var normalizedCampaignId = campaignId.Trim();
        var isOwned = await domainUnitOfWork.Campaigns.IsReportVisibleCampaignOwnedByCompanyAsync(
            actor.CompanyId,
            normalizedCampaignId,
            cancellationToken);
        if (!isOwned)
        {
            throw new Phase5NotFoundException("Campaign was not found.");
        }

        var deliveries = await domainUnitOfWork.Deliveries.ListCompanyCampaignAnalyticsDeliveriesAsync(
            actor.CompanyId,
            normalizedCampaignId,
            dateRange,
            cancellationToken);
        var evidence = await domainUnitOfWork.WalletTransactions.ListFinancialEvidenceByDeliveryIdsAsync(
            deliveries.Select(delivery => delivery.DeliveryId).ToArray(),
            cancellationToken);
        var reconciliation = CompanyReportingCalculations.ClassifyFinancialReconciliation(
            normalizedCampaignId,
            dateRange,
            deliveries,
            evidence);
        if (!reconciliation.IsConsistent)
        {
            await RecordDiscrepancyAndFailAsync(actor, reconciliation, cancellationToken);
        }

        var analytics = CompanyReportingCalculations.BuildAnalytics(normalizedCampaignId, dateRange, deliveries);
        return CampaignReportingDtoMapper.ToAnalytics(analytics);
    }
}
