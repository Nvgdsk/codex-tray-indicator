using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace CodexTray.Tests;

public sealed class WeeklyLimitTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1900000000);
    private static readonly string[] ExpectedMethods = ["initialize", "initialized", "account/rateLimits/read"];

    [Theory]
    [InlineData("primary")]
    [InlineData("secondary")]
    public void Parse_SelectsSevenDayWindowAndSubtractsUsedPercent(string window)
    {
        using JsonDocument json = JsonDocument.Parse($$$$"""
            {"rateLimits":{"{{{{window}}}}":{"usedPercent":28,"windowDurationMins":10080,"resetsAt":2000000000}}}
            """);
        WeeklyLimit? limit = WeeklyLimit.Parse(json.RootElement, Now);
        Assert.NotNull(limit);
        Assert.Equal(72, limit.RemainingPercent);
    }

    [Fact]
    public void Parse_UsesCodexBucketInsteadOfAnotherModelsQuota()
    {
        using JsonDocument json = JsonDocument.Parse("""
            {"rateLimits":{"secondary":{"usedPercent":90,"windowDurationMins":10080,"resetsAt":2000000000}},
             "rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":10,"windowDurationMins":300,"resetsAt":2000000000},
             "secondary":{"usedPercent":28,"windowDurationMins":10080,"resetsAt":2000000000}},
             "other":{"primary":{"usedPercent":99,"windowDurationMins":10080,"resetsAt":2000000000}}}}
            """);
        Assert.Equal(72, WeeklyLimit.Parse(json.RootElement, Now)!.RemainingPercent);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"rateLimits\":null}")]
    [InlineData("{\"rateLimitsByLimitId\":{\"other\":{}}}")]
    [InlineData("{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":2000000000}}}")]
    [InlineData("{\"rateLimits\":{\"secondary\":{\"usedPercent\":25,\"windowDurationMins\":10080,\"resetsAt\":1800000000}}}")]
    [InlineData("{\"rateLimits\":{\"secondary\":{\"usedPercent\":-1,\"windowDurationMins\":10080,\"resetsAt\":2000000000}}}")]
    [InlineData("{\"rateLimits\":{\"secondary\":{\"usedPercent\":\"25\",\"windowDurationMins\":10080,\"resetsAt\":2000000000}}}")]
    public void Parse_MissingWrongOrExpiredWindowIsUnavailable(string payload)
    {
        using JsonDocument json = JsonDocument.Parse(payload);
        Assert.Null(WeeklyLimit.Parse(json.RootElement, Now));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(110, 0)]
    public void Parse_HandlesFullAndExhaustedQuota(int used, int remaining)
    {
        using JsonDocument json = JsonDocument.Parse($$$$"""
            {"rateLimits":{"secondary":{"usedPercent":{{{{used}}}},"windowDurationMins":10080,"resetsAt":2000000000}}}
            """);
        Assert.Equal(remaining, WeeklyLimit.Parse(json.RootElement, Now)!.RemainingPercent);
    }

    [Fact]
    public async Task Rpc_InitializesThenReadsLimitsAndIgnoresNotifications()
    {
        using var input = new StringReader("""
            {"method":"account/updated","params":{}}
            {"id":0,"result":{}}
            {"method":"account/rateLimits/updated","params":{}}
            {"id":1,"result":{"rateLimits":{"secondary":{"usedPercent":28,"windowDurationMins":10080,"resetsAt":2000000000}}}}
            """);
        using var output = new StringWriter();
        WeeklyLimit? limit = await CodexWeeklyLimitSource.ReadAsync(input, output, Now, TestContext.Current.CancellationToken);
        Assert.Equal(72, limit!.RemainingPercent);
        string[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        string[] methods = lines.Select(line =>
        {
            using JsonDocument json = JsonDocument.Parse(line);
            return json.RootElement.GetProperty("method").GetString();
        }).ToArray()!;
        Assert.Equal(ExpectedMethods, methods);
    }

    [Theory]
    [InlineData("{\"id\":0,\"error\":{\"message\":\"failed\"}}")]
    [InlineData("{\"id\":0,\"result\":{}}\n{\"id\":1,\"error\":{\"message\":\"login required\"}}")]
    [InlineData("")]
    [InlineData("invalid JSON")]
    public async Task Rpc_ErrorOrClosedStreamCannotBecomeAValidPercentage(string responses)
    {
        using var input = new StringReader(responses);
        using var output = new StringWriter();
        await Assert.ThrowsAnyAsync<Exception>(() => CodexWeeklyLimitSource.ReadAsync(input, output, Now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Protocol_QuotaChangeRedrawsWholeFrameThenResumesPartialMascotUpdates()
    {
        using var transport = new RecordingTransport();
        using var screen = new TuringScreenConnection(transport);
        var limit = new WeeklyLimit(72, DateTimeOffset.FromUnixTimeSeconds(2000000000));
        screen.Show(TrayState.Busy, ScreenOrientation.Portrait, CancellationToken.None, weeklyLimit: limit);
        long first = transport.Bytes.Length;
        screen.Show(TrayState.Busy, ScreenOrientation.Portrait, CancellationToken.None, mascotFrame: 1,
            weeklyLimit: limit with { RemainingPercent = 71 });
        Assert.Equal(6 + 320 * 480 * 2, transport.Bytes.Length - first);
        long second = transport.Bytes.Length;
        screen.Show(TrayState.Busy, ScreenOrientation.Portrait, CancellationToken.None, mascotFrame: 2,
            weeklyLimit: limit with { RemainingPercent = 71 });
        Assert.Equal(6 + 112 * 112 * 2, transport.Bytes.Length - second);
        long third = transport.Bytes.Length;
        screen.Show(TrayState.Busy, ScreenOrientation.Portrait, CancellationToken.None, mascotFrame: 3, weeklyLimit: null);
        Assert.Equal(6 + 320 * 480 * 2, transport.Bytes.Length - third);
    }

    [Theory]
    [InlineData((int)ScreenOrientation.Portrait)]
    [InlineData((int)ScreenOrientation.Landscape)]
    public void Renderer_QuotaChangesPixelsAndSavesPreview(int orientationValue)
    {
        var orientation = (ScreenOrientation)orientationValue;
        using var unavailable = UsbScreenRenderer.Render(TrayState.Busy, orientation);
        using var available = UsbScreenRenderer.Render(TrayState.Busy, orientation,
            weeklyLimit: new WeeklyLimit(72, DateTimeOffset.FromUnixTimeSeconds(2000000000)));
        Assert.False(UsbScreenRenderer.ToRgb565(unavailable).AsSpan().SequenceEqual(UsbScreenRenderer.ToRgb565(available)));
        string directory = Path.Combine(AppContext.BaseDirectory, "weekly-limit-previews");
        Directory.CreateDirectory(directory);
        available.Save(Path.Combine(directory, $"Weekly-{orientation}.png"), ImageFormat.Png);
        unavailable.Save(Path.Combine(directory, $"Unavailable-{orientation}.png"), ImageFormat.Png);
    }

    private sealed class RecordingTransport : IUsbScreenTransport
    {
        public MemoryStream Bytes { get; } = new();
        public void Write(byte[] data, int offset, int count) => Bytes.Write(data, offset, count);
        public void Dispose() => Bytes.Dispose();
    }
}
