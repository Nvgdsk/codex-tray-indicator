using System.Diagnostics;

namespace CodexTray.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_WhenChildDoesNotReadStdin_TimesOutDuringWrite()
    {
        var runner = new ProcessRunner();
        var request = new ProcessRequest(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 10"],
            new string('x', 5 * 1024 * 1024),
            TimeSpan.FromMilliseconds(250));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(
            () => runner.RunAsync(request, CancellationToken.None));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Timeout took {watch.Elapsed}.");
    }
}
