using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Campaigns;

public sealed record CompanyReportingDateRangeQuery(string? FromDateEgypt, string? ToDateEgypt);

public sealed record CompanyReportingDateRange(DateOnly FromDateEgypt, DateOnly ToDateEgypt)
{
    public int InclusiveDayCount => ToDateEgypt.DayNumber - FromDateEgypt.DayNumber + 1;
}

public sealed record CompanyReportingPaginationQuery(int? PageNumber, int? PageSize);

public sealed record CompanyReportingPagination(int PageNumber, int PageSize)
{
    public int Skip => (PageNumber - 1) * PageSize;
}

public enum CompanyReportingDeliveryState
{
    Read = 1,
    Unread = 2,
    Interacted = 3,
    Uninteracted = 4
}

public enum CompanyReportingFeedbackOutcome
{
    Accepted = 1,
    Rejected = 2
}

public enum CompanyReportingFeedbackEligibility
{
    Eligible = 1,
    Ineligible = 2
}

public sealed record CompanyReportingDeliveryFilters(
    DeliveryStatus? Status,
    CompanyReportingDeliveryState? State,
    string? DoctorSpecialization,
    string? DoctorLocation);

public sealed record CompanyReportingFeedbackFilters(
    CompanyReportingFeedbackOutcome? Outcome,
    CompanyReportingFeedbackEligibility? FeedbackEligibility,
    string? DoctorSpecialization,
    string? DoctorLocation);

public sealed record CompanyReportingAnalyticsScope(
    string CompanyId,
    string CampaignId,
    CompanyReportingDateRange DateRange);
