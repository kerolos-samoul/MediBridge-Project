using System.Globalization;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Validators.Campaigns;

public sealed class CompanyReportingDateRangeValidator
{
    public const int MaximumInclusiveDays = 90;
    private const string DateFormat = "yyyy-MM-dd";
    private readonly IEgyptBusinessClock egyptBusinessClock;

    public CompanyReportingDateRangeValidator(IEgyptBusinessClock egyptBusinessClock)
    {
        this.egyptBusinessClock = egyptBusinessClock;
    }

    public CompanyReportingDateRange Resolve(string? fromDateEgypt, string? toDateEgypt)
    {
        var todayEgypt = egyptBusinessClock.Capture().BusinessDateEgypt;
        var from = ParseOptionalDate(fromDateEgypt, nameof(fromDateEgypt));
        var to = ParseOptionalDate(toDateEgypt, nameof(toDateEgypt));

        if (from is null && to is null)
        {
            to = todayEgypt;
            from = todayEgypt.AddDays(-(MaximumInclusiveDays - 1));
        }
        else if (from is null)
        {
            from = to!.Value.AddDays(-(MaximumInclusiveDays - 1));
        }
        else if (to is null)
        {
            var requestedEnd = from.Value.AddDays(MaximumInclusiveDays - 1);
            to = requestedEnd <= todayEgypt ? requestedEnd : todayEgypt;
        }

        if (from > to)
        {
            throw new Phase5ValidationException("Validation failed.", ["fromDateEgypt must be on or before toDateEgypt."]);
        }

        var resolved = new CompanyReportingDateRange(from!.Value, to!.Value);
        if (resolved.InclusiveDayCount > MaximumInclusiveDays)
        {
            throw new Phase5ValidationException("Validation failed.", ["Reporting date ranges cannot exceed 90 inclusive delivery Egypt business days."]);
        }

        return resolved;
    }

    private static DateOnly? ParseOptionalDate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(value.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new Phase5ValidationException("Validation failed.", [$"{parameterName} must use yyyy-MM-dd format."]);
        }

        return date;
    }
}
