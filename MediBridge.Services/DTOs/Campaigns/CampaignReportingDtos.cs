using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Campaigns;

public sealed class ReportingPageMetadataDto
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

public sealed class PublicDoctorSummaryDto
{
    public string PublicDoctorId { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string ExperienceBand { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}

public sealed class CampaignReportSummaryDto
{
    public string CampaignId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CampaignStatus Status { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public int TargetCount { get; set; }
    public int DeliveredCount { get; set; }
    public int ActiveUnansweredCount { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ExpiredCount { get; set; }
    public int FeedbackCount { get; set; }
    public decimal ReservedAmount { get; set; }
    public decimal ChargedSpend { get; set; }
    public decimal DoctorEarnings { get; set; }
    public decimal PlatformFee { get; set; }
}

public sealed class CampaignReportPageDto
{
    public ReportingPageMetadataDto Page { get; set; } = new();
    public IReadOnlyList<CampaignReportSummaryDto> Items { get; set; } = Array.Empty<CampaignReportSummaryDto>();
}

public sealed class CampaignDeliveryReportRowDto
{
    public string DeliveryId { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public string PublicDoctorId { get; set; } = string.Empty;
    public string DoctorSpecialization { get; set; } = string.Empty;
    public string DoctorExperienceBand { get; set; } = string.Empty;
    public string DoctorLocation { get; set; } = string.Empty;
    public DateOnly DeliveryDateEgypt { get; set; }
    public DateTime DeliveredAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DeliveryStatus Status { get; set; }
    public DateTime? InteractedAtUtc { get; set; }
    public decimal PriceSnapshot { get; set; }
    public decimal ReservedAmount { get; set; }
    public decimal ChargedAmount { get; set; }
    public decimal DoctorEarnings { get; set; }
    public decimal PlatformFee { get; set; }
}

public sealed class DeliveryReportPageDto
{
    public ReportingPageMetadataDto Page { get; set; } = new();
    public IReadOnlyList<CampaignDeliveryReportRowDto> Items { get; set; } = Array.Empty<CampaignDeliveryReportRowDto>();
}

public sealed class CampaignFeedbackReportRowDto
{
    public string DeliveryId { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string FeedbackText { get; set; } = string.Empty;
    public DateTime FeedbackCreatedAtUtc { get; set; }
    public bool FeedbackQualifiesForScore { get; set; }
    public string PublicDoctorId { get; set; } = string.Empty;
    public string DoctorSpecialization { get; set; } = string.Empty;
    public string DoctorExperienceBand { get; set; } = string.Empty;
    public string DoctorLocation { get; set; } = string.Empty;
}

public sealed class FeedbackReportPageDto
{
    public ReportingPageMetadataDto Page { get; set; } = new();
    public IReadOnlyList<CampaignFeedbackReportRowDto> Items { get; set; } = Array.Empty<CampaignFeedbackReportRowDto>();
}

public sealed class RateMetricDto
{
    public decimal Value { get; set; }
    public int Numerator { get; set; }
    public int Denominator { get; set; }
    public bool HasEligibleRecords { get; set; }
}

public sealed class CampaignAnalyticsDto
{
    public string CampaignId { get; set; } = string.Empty;
    public DateOnly FromDateEgypt { get; set; }
    public DateOnly ToDateEgypt { get; set; }
    public int DeliveredCount { get; set; }
    public int ActiveUnansweredCount { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ExpiredCount { get; set; }
    public int FeedbackCount { get; set; }
    public RateMetricDto InteractionRate { get; set; } = new();
    public RateMetricDto AcceptanceRate { get; set; } = new();
    public RateMetricDto RejectionRate { get; set; } = new();
    public RateMetricDto ExpiryRate { get; set; } = new();
    public RateMetricDto FeedbackRate { get; set; } = new();
    public decimal ReservedAmount { get; set; }
    public decimal ChargedSpend { get; set; }
    public decimal DoctorEarnings { get; set; }
    public decimal PlatformFee { get; set; }
}
