namespace MediBridge.UnitTests.TestDoubles;

public sealed class FakeTimeProvider : TimeProvider
{
    private readonly object sync = new();
    private DateTimeOffset utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow)
    {
        this.utcNow = utcNow.ToUniversalTime();
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (sync)
        {
            return utcNow;
        }
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        lock (sync)
        {
            utcNow = value.ToUniversalTime();
        }
    }

    public void Advance(TimeSpan amount)
    {
        lock (sync)
        {
            utcNow = utcNow.Add(amount);
        }
    }
}
