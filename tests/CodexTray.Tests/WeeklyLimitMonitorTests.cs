using System.Collections.Concurrent;
using System.IO;

namespace CodexTray.Tests;

public sealed class WeeklyLimitMonitorTests
{
    [Fact]
    public async Task Monitor_ReadsImmediatelyClearsFailedReadAndRecoversWithoutCodexEvents()
    {
        var limit = new WeeklyLimit(72, DateTimeOffset.UtcNow.AddDays(1));
        var observations = new ConcurrentQueue<WeeklyLimit?>();
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var source = new FakeSource(_ => Interlocked.Increment(ref calls) switch
        {
            1 => Task.FromResult<WeeklyLimit?>(limit),
            2 => throw new IOException("Account unavailable"),
            _ => Task.FromResult<WeeklyLimit?>(limit with { RemainingPercent = 71 }),
        });
        await using var monitor = new WeeklyLimitMonitor(source, value =>
        {
            observations.Enqueue(value);
            if (value?.RemainingPercent == 71) recovered.TrySetResult();
        }, interval: TimeSpan.FromMilliseconds(20));
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await monitor.StopAsync();
        Assert.Equal(72, observations.ElementAt(0)!.RemainingPercent);
        Assert.Null(observations.ElementAt(1));
        Assert.Equal(71, observations.ElementAt(2)!.RemainingPercent);
    }

    [Fact]
    public async Task Monitor_StopCancelsReadInProgress()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeSource(async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        await using var monitor = new WeeklyLimitMonitor(source, _ => { });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await monitor.StopAsync().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    private sealed class FakeSource(Func<CancellationToken, Task<WeeklyLimit?>> read) : IWeeklyLimitSource
    {
        public Task<WeeklyLimit?> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
