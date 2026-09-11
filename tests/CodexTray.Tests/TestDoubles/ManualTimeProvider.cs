namespace CodexTray.Tests.TestDoubles;

internal sealed class ManualTimeProvider(DateTimeOffset current) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => current;

    public void Advance(TimeSpan amount)
    {
        current = current.Add(amount);
    }
}
