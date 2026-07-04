namespace MediBridge.Core.Interfaces.Time;

public readonly record struct EgyptBusinessTimeSnapshot(
    DateTime UtcNow,
    DateTimeOffset EgyptLocalNow,
    DateOnly BusinessDateEgypt);

public interface IEgyptBusinessClock
{
    TimeZoneInfo TimeZone { get; }

    EgyptBusinessTimeSnapshot Capture();
}
