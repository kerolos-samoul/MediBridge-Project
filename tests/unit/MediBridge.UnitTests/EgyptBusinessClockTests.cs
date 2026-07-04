using MediBridge.Services.Services;
using MediBridge.UnitTests.TestDoubles;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class EgyptBusinessClockTests
{
    [Fact]
    public void Capture_ChangesBusinessDateAtCairoMidnight()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 21, 59, 59, TimeSpan.Zero));
        var clock = new EgyptBusinessClock(time);

        Assert.Equal(new DateOnly(2026, 1, 1), clock.Capture().BusinessDateEgypt);

        time.Advance(TimeSpan.FromSeconds(1));

        var afterMidnight = clock.Capture();
        Assert.Equal(new DateOnly(2026, 1, 2), afterMidnight.BusinessDateEgypt);
        Assert.Equal(0, afterMidnight.EgyptLocalNow.Hour);
    }

    [Fact]
    public void Capture_UsesCairoRulesInsteadOfHostLocalTime()
    {
        var instant = new DateTimeOffset(2026, 7, 2, 21, 30, 0, TimeSpan.Zero);
        var clock = new EgyptBusinessClock(new FakeTimeProvider(instant));

        var snapshot = clock.Capture();

        Assert.Equal(instant.UtcDateTime, snapshot.UtcNow);
        Assert.Equal(new DateOnly(2026, 7, 3), snapshot.BusinessDateEgypt);
        Assert.Equal(TimeSpan.FromHours(3), snapshot.EgyptLocalNow.Offset);
    }

    [Fact]
    public void Constructor_UsesWindowsIdFallbackWhenIanaLookupIsUnavailable()
    {
        var resolvedIds = new List<string>();
        var cairo = TimeZoneInfo.FindSystemTimeZoneById(EgyptBusinessClock.CairoIanaTimeZoneId);
        TimeZoneInfo Resolver(string id)
        {
            resolvedIds.Add(id);
            if (id == EgyptBusinessClock.CairoIanaTimeZoneId)
            {
                throw new TimeZoneNotFoundException();
            }

            return cairo;
        }

        var clock = new EgyptBusinessClock(
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            Resolver,
            _ => "Egypt Standard Time");

        Assert.Same(cairo, clock.TimeZone);
        Assert.Equal(new[] { EgyptBusinessClock.CairoIanaTimeZoneId, "Egypt Standard Time" }, resolvedIds);
    }

    [Fact]
    public void Constructor_RejectsMissingOrNonDstCairoZone()
    {
        Assert.Throws<InvalidOperationException>(() => new EgyptBusinessClock(
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            _ => throw new TimeZoneNotFoundException(),
            _ => null));

        Assert.Throws<InvalidOperationException>(() => new EgyptBusinessClock(
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            _ => TimeZoneInfo.Utc,
            _ => null));
    }

    [Theory]
    [InlineData("2026-04-23T21:59:00Z", 2, 23)]
    [InlineData("2026-04-23T22:01:00Z", 3, 1)]
    [InlineData("2026-10-29T20:59:00Z", 3, 23)]
    [InlineData("2026-10-29T21:01:00Z", 2, 23)]
    public void Capture_FollowsEgyptDaylightSavingTransitions(string utcText, int expectedOffsetHours, int expectedLocalHour)
    {
        var clock = new EgyptBusinessClock(new FakeTimeProvider(DateTimeOffset.Parse(utcText)));

        var snapshot = clock.Capture();

        Assert.Equal(TimeSpan.FromHours(expectedOffsetHours), snapshot.EgyptLocalNow.Offset);
        Assert.Equal(expectedLocalHour, snapshot.EgyptLocalNow.Hour);
    }
}
