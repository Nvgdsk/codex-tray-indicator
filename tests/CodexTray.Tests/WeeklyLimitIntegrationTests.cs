using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CodexTray.Tests;

internal sealed class WeeklyLimitIntegrationFactAttribute : FactAttribute
{
    public WeeklyLimitIntegrationFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEXTRAY_TEST_WEEKLY_DISTRIBUTION")))
            Skip = "Set CODEXTRAY_TEST_WEEKLY_DISTRIBUTION to query a real signed-in WSL Codex account.";
    }
}

public sealed class WeeklyLimitIntegrationTests
{
    [WeeklyLimitIntegrationFact]
    [Trait("Category", "Integration")]
    public async Task RealCodex_ReturnsWeeklyQuotaAndRendersIt()
    {
        string distribution = Environment.GetEnvironmentVariable("CODEXTRAY_TEST_WEEKLY_DISTRIBUTION")!;
        var source = new CodexWeeklyLimitSource(() => distribution);
        WeeklyLimit? limit = await source.ReadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(limit);
        Assert.InRange(limit.RemainingPercent, 0, 100);
        Assert.True(limit.ResetsAt > DateTimeOffset.UtcNow);
        string directory = Path.Combine(AppContext.BaseDirectory, "weekly-limit-previews");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "live-limit.json"), JsonSerializer.Serialize(limit),
            TestContext.Current.CancellationToken);
        using var bitmap = UsbScreenRenderer.Render(TrayState.Inactive, ScreenOrientation.Portrait, weeklyLimit: limit);
        bitmap.Save(Path.Combine(directory, "Live-Portrait.png"), ImageFormat.Png);
    }
}
