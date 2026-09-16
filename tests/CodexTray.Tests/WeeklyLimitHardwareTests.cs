using System.Runtime.CompilerServices;

namespace CodexTray.Tests;

internal sealed class WeeklyLimitHardwareFactAttribute : FactAttribute
{
    public WeeklyLimitHardwareFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEXTRAY_TEST_WEEKLY_DISTRIBUTION")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_PORT")))
            Skip = "Set both CODEXTRAY_TEST_WEEKLY_DISTRIBUTION and CODEXTRAY_TEST_USB_PORT to display a real weekly quota.";
    }
}

public sealed class WeeklyLimitHardwareTests
{
    [WeeklyLimitHardwareFact]
    [Trait("Category", "UsbHardware")]
    public async Task RealScreen_AcceptsCurrentWeeklyQuotaFrame()
    {
        var source = new CodexWeeklyLimitSource(() => Environment.GetEnvironmentVariable("CODEXTRAY_TEST_WEEKLY_DISTRIBUTION"));
        WeeklyLimit? limit = await source.ReadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(limit);
        string port = Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_PORT")!;
        Assert.Matches(@"\ACOM[1-9][0-9]*\z", port);
        Assert.Equal(port, UsbScreenPortDiscovery.Resolve("AUTO"), ignoreCase: true);
        IpcResponse? previous = await new IpcClient().SendAsync(IpcMessage.Query(), true, TestContext.Current.CancellationToken);
        var orientation = Enum.Parse<ScreenOrientation>(Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_ORIENTATION") ?? "Portrait");
        using var transport = new CountingTransport(new SerialScreenTransport(port));
        using var connection = new TuringScreenConnection(transport);
        connection.Show(previous?.State ?? TrayState.Inactive, orientation, TestContext.Current.CancellationToken, weeklyLimit: limit);
        Assert.Equal(28 + 6 + 320 * 480 * 2, transport.BytesWritten);
    }

    private sealed class CountingTransport(IUsbScreenTransport inner) : IUsbScreenTransport
    {
        public int BytesWritten { get; private set; }
        public void Write(byte[] data, int offset, int count)
        {
            inner.Write(data, offset, count);
            BytesWritten += count;
        }
        public void Dispose() => inner.Dispose();
    }
}
