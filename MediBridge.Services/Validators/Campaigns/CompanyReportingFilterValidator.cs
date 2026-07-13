using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Validators.Campaigns;

public sealed class CompanyReportingFilterValidator
{
    private const int MaximumDoctorFilterLength = 120;

    public CompanyReportingDeliveryFilters ResolveDeliveryFilters(
        string? status,
        string? state,
        string? doctorSpecialization,
        string? doctorLocation)
    {
        return new CompanyReportingDeliveryFilters(
            ParseEnum<DeliveryStatus>(status, nameof(status), allowValues: [DeliveryStatus.Active, DeliveryStatus.Accepted, DeliveryStatus.Rejected, DeliveryStatus.Expired]),
            ParseEnum<CompanyReportingDeliveryState>(state, nameof(state)),
            NormalizeDoctorFilter(doctorSpecialization, nameof(doctorSpecialization)),
            NormalizeDoctorFilter(doctorLocation, nameof(doctorLocation)));
    }

    public CompanyReportingFeedbackFilters ResolveFeedbackFilters(
        string? outcome,
        string? feedbackEligibility,
        string? doctorSpecialization,
        string? doctorLocation)
    {
        return new CompanyReportingFeedbackFilters(
            ParseEnum<CompanyReportingFeedbackOutcome>(outcome, nameof(outcome)),
            ParseEnum<CompanyReportingFeedbackEligibility>(feedbackEligibility, nameof(feedbackEligibility)),
            NormalizeDoctorFilter(doctorSpecialization, nameof(doctorSpecialization)),
            NormalizeDoctorFilter(doctorLocation, nameof(doctorLocation)));
    }

    private static string? NormalizeDoctorFilter(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumDoctorFilterLength)
        {
            throw new Phase5ValidationException("Validation failed.", [$"{parameterName} cannot exceed 120 characters."]);
        }

        return normalized;
    }

    private static TEnum? ParseEnum<TEnum>(string? value, string parameterName, IReadOnlyCollection<TEnum>? allowValues = null)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed)
            || (allowValues is not null && !allowValues.Contains(parsed)))
        {
            throw new Phase5ValidationException("Validation failed.", [$"{parameterName} is not supported."]);
        }

        return parsed;
    }
}
