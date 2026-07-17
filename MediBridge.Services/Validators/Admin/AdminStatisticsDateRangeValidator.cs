using System.Globalization;
using MediBridge.Core.Interfaces.Admin;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Validators.Admin;

public sealed class AdminStatisticsDateRangeValidator
{
    private const int MaximumInclusiveDays = 90;
    private readonly IEgyptBusinessClock businessClock;

    public AdminStatisticsDateRangeValidator(IEgyptBusinessClock businessClock)
    {
        this.businessClock = businessClock;
    }

    public AdminStatisticsDateRange Resolve(string? fromDateEgypt, string? toDateEgypt)
    {
        var today = businessClock.Capture().BusinessDateEgypt;
        var from = Parse(fromDateEgypt, nameof(fromDateEgypt));
        var to = Parse(toDateEgypt, nameof(toDateEgypt));
        if (from is null && to is null)
        {
            to = today;
            from = today.AddDays(-(MaximumInclusiveDays - 1));
        }
        else if (from is null)
        {
            from = to!.Value.AddDays(-(MaximumInclusiveDays - 1));
        }
        else if (to is null)
        {
            to = from.Value.AddDays(MaximumInclusiveDays - 1);
            if (to.Value > today)
            {
                to = today;
            }
        }

        var range = new AdminStatisticsDateRange(from!.Value, to!.Value);
        if (range.FromDateEgypt > range.ToDateEgypt)
        {
            throw new Phase5ValidationException("Validation failed.", ["fromDateEgypt must be on or before toDateEgypt."]);
        }

        if (range.InclusiveDayCount > MaximumInclusiveDays)
        {
            throw new Phase5ValidationException("Validation failed.", ["Date range must not exceed 90 inclusive days."]);
        }

        return range;
    }

    private static DateOnly? Parse(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new Phase5ValidationException("Validation failed.", [$"{fieldName} must be a valid yyyy-MM-dd date."]);
        }

        return parsed;
    }
}
