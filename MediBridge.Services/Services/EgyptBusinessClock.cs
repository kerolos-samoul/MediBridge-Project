using MediBridge.Core.Interfaces.Time;

namespace MediBridge.Services.Services;

public sealed class EgyptBusinessClock : IEgyptBusinessClock
{
    public const string CairoIanaTimeZoneId = "Africa/Cairo";

    private readonly TimeProvider timeProvider;

    public EgyptBusinessClock(TimeProvider timeProvider)
        : this(timeProvider, TimeZoneInfo.FindSystemTimeZoneById, ResolveWindowsId)
    {
    }

    public EgyptBusinessClock(
        TimeProvider timeProvider,
        Func<string, TimeZoneInfo> timeZoneResolver,
        Func<string, string?> windowsIdResolver)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(timeZoneResolver);
        ArgumentNullException.ThrowIfNull(windowsIdResolver);
        TimeZone = ResolveCairoTimeZone(timeZoneResolver, windowsIdResolver);
    }

    public TimeZoneInfo TimeZone { get; }

    public EgyptBusinessTimeSnapshot Capture()
    {
        var utcNow = timeProvider.GetUtcNow();
        var egyptLocalNow = TimeZoneInfo.ConvertTime(utcNow, TimeZone);
        return new EgyptBusinessTimeSnapshot(
            utcNow.UtcDateTime,
            egyptLocalNow,
            DateOnly.FromDateTime(egyptLocalNow.DateTime));
    }

    private static TimeZoneInfo ResolveCairoTimeZone(
        Func<string, TimeZoneInfo> timeZoneResolver,
        Func<string, string?> windowsIdResolver)
    {
        TimeZoneInfo? zone = null;
        try
        {
            zone = timeZoneResolver(CairoIanaTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        if (zone is null)
        {
            var windowsId = windowsIdResolver(CairoIanaTimeZoneId);
            if (!string.IsNullOrWhiteSpace(windowsId))
            {
                try
                {
                    zone = timeZoneResolver(windowsId);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }
        }

        if (zone is null || !zone.SupportsDaylightSavingTime || zone.GetAdjustmentRules().Length == 0)
        {
            throw new InvalidOperationException("A DST-capable Africa/Cairo time zone is required for Egypt business time.");
        }

        return zone;
    }

    private static string? ResolveWindowsId(string ianaId)
    {
        return TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var windowsId) ? windowsId : null;
    }
}
