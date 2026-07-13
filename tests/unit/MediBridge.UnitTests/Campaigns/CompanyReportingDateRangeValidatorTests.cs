using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Campaigns;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CompanyReportingDateRangeValidatorTests
{
    private readonly CompanyReportingDateRangeValidator validator = new(new FixedEgyptBusinessClock(new DateOnly(2026, 7, 13)));

    [Fact]
    public void Resolve_DefaultsBothOmittedToLatestNinetyInclusiveDaysEndingToday()
    {
        var range = validator.Resolve(null, null);

        Assert.Equal(new DateOnly(2026, 4, 15), range.FromDateEgypt);
        Assert.Equal(new DateOnly(2026, 7, 13), range.ToDateEgypt);
        Assert.Equal(90, range.InclusiveDayCount);
    }

    [Fact]
    public void Resolve_DefaultsOnlyFromToEarlierOfEightyNineDaysAfterFromOrToday()
    {
        var boundedByToday = validator.Resolve("2026-07-01", null);
        var boundedByWindow = validator.Resolve("2026-04-01", null);

        Assert.Equal(new DateOnly(2026, 7, 13), boundedByToday.ToDateEgypt);
        Assert.Equal(new DateOnly(2026, 6, 29), boundedByWindow.ToDateEgypt);
        Assert.Equal(90, boundedByWindow.InclusiveDayCount);
    }

    [Fact]
    public void Resolve_DefaultsOnlyToToEightyNineDaysBeforeTo()
    {
        var range = validator.Resolve(null, "2026-07-13");

        Assert.Equal(new DateOnly(2026, 4, 15), range.FromDateEgypt);
        Assert.Equal(new DateOnly(2026, 7, 13), range.ToDateEgypt);
        Assert.Equal(90, range.InclusiveDayCount);
    }

    [Fact]
    public void Resolve_AcceptsNinetyDayInclusiveRange()
    {
        var range = validator.Resolve("2026-04-15", "2026-07-13");

        Assert.Equal(90, range.InclusiveDayCount);
    }

    [Fact]
    public void Resolve_RejectsNinetyOneDayInclusiveRange()
    {
        var ex = Assert.Throws<Phase5ValidationException>(() => validator.Resolve("2026-04-14", "2026-07-13"));

        Assert.Contains(ex.Errors, error => error.Contains("90", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2026/07/13", "2026-07-13")]
    [InlineData("not-a-date", "2026-07-13")]
    [InlineData("2026-07-13", "13-07-2026")]
    public void Resolve_RejectsMalformedDates(string? from, string? to)
    {
        Assert.Throws<Phase5ValidationException>(() => validator.Resolve(from, to));
    }

    [Fact]
    public void Resolve_RejectsFromAfterTo()
    {
        Assert.Throws<Phase5ValidationException>(() => validator.Resolve("2026-07-14", "2026-07-13"));
    }

    private sealed class FixedEgyptBusinessClock : IEgyptBusinessClock
    {
        private readonly DateOnly businessDateEgypt;

        public FixedEgyptBusinessClock(DateOnly businessDateEgypt)
        {
            this.businessDateEgypt = businessDateEgypt;
        }

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public EgyptBusinessTimeSnapshot Capture()
            => new(DateTime.UtcNow, DateTimeOffset.UtcNow, businessDateEgypt);
    }
}
