using System.Diagnostics;

namespace CodexTray.Tests;

public sealed class SingleInstanceIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SecondTrayProcess_ExitsQuicklyWithoutOwningThePipe()
    {
        string executable = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CodexTray",
            "bin",
            "Release",
            "net8.0-windows",
            "win-x64",
            "CodexTray.exe");
        Assert.True(File.Exists(executable), $"Missing test executable: {executable}");

        using Process first = Start(executable);
        try
        {
            await WaitForStateAsync(executable, "Inactive", TimeSpan.FromSeconds(5));

            using Process second = Start(executable);
            using var secondTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await second.WaitForExitAsync(secondTimeout.Token);

            Assert.Equal(0, second.ExitCode);
            Assert.False(first.HasExited);
            Assert.Equal("Inactive", await QueryStateAsync(executable));
        }
        finally
        {
            using Process shutdown = Start(executable, "--shutdown", redirectOutput: true);
            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await shutdown.WaitForExitAsync(shutdownTimeout.Token);
                await first.WaitForExitAsync(shutdownTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!first.HasExited)
                {
                    first.Kill(entireProcessTree: true);
                    await first.WaitForExitAsync();
                }
            }
        }
    }

    private static async Task WaitForStateAsync(
        string executable,
        string expected,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await QueryStateAsync(executable) == expected)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Tray did not report {expected} within {timeout}.");
    }

    private static async Task<string?> QueryStateAsync(string executable)
    {
        using Process query = Start(executable, "--query-state", redirectOutput: true);
        await query.WaitForExitAsync();
        return query.ExitCode == 0 ? (await query.StandardOutput.ReadToEndAsync()).Trim() : null;
    }

    private static Process Start(
        string executable,
        string? arguments = null,
        bool redirectOutput = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
        };
        if (arguments is not null)
        {
            startInfo.ArgumentList.Add(arguments);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {executable}.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodexTray.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Unable to locate the repository root.");
    }
}
