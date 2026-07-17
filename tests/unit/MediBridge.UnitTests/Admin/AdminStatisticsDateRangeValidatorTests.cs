using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Admin;
using Xunit;

namespace MediBridge.UnitTests.Admin;

public sealed class AdminStatisticsDateRangeValidatorTests
{
    private readonly AdminStatisticsDateRangeValidator validator = new(new FixedEgyptBusinessClock(new DateOnly(2026, 7, 13)));

    [Fact]
    public void Resolve_DefaultsOmittedDatesToLatestNinetyInclusiveEgyptBusinessDays()
    {
        var range = validator.Resolve(null, null);

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
        Assert.Throws<Phase5ValidationException>(() => validator.Resolve("2026-04-14", "2026-07-13"));
    }

    [Fact]
    public void Resolve_RejectsFromAfterTo()
    {
        Assert.Throws<Phase5ValidationException>(() => validator.Resolve("2026-07-14", "2026-07-13"));
    }

    [Theory]
    [InlineData("2026/07/13", "2026-07-13")]
    [InlineData("not-a-date", "2026-07-13")]
    [InlineData("2026-07-13", "13-07-2026")]
    public void Resolve_RejectsMalformedDates(string? from, string? to)
    {
        Assert.Throws<Phase5ValidationException>(() => validator.Resolve(from, to));
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
