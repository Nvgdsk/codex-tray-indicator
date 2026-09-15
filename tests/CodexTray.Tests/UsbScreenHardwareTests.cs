using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace CodexTray.Tests;

// Explicit opt-in prevents ordinary builds from taking a port owned by the tray.
public sealed class UsbScreenHardwareFactAttribute : FactAttribute
{
    public UsbScreenHardwareFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_PORT")))
            Skip = "Run scripts/test-usb-screen.ps1 with the tray USB output stopped.";
    }
}

public sealed class UsbScreenHardwareTests
{
    [UsbScreenHardwareFact]
    [Trait("Category", "UsbHardware")]
    public async Task RealScreen_AcceptsAllFourStatusFramesAndRestoresPreviousState()
    {
        string port = Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_PORT")!;
        Assert.Matches(new Regex(@"\ACOM[1-9][0-9]*\z", RegexOptions.IgnoreCase), port);
        string? discovered = UsbScreenPortDiscovery.Resolve("AUTO");
        Assert.Equal(port, discovered, ignoreCase: true);
        var orientation = Enum.Parse<ScreenOrientation>(Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_ORIENTATION") ?? "Portrait");
        string previewDirectory = Path.Combine(FindRepositoryRoot(), "artifacts", "usb-screen-previews");
        Directory.CreateDirectory(previewDirectory);
        foreach (TrayState state in Enum.GetValues<TrayState>())
        {
            foreach (ScreenOrientation previewOrientation in new[] { ScreenOrientation.Portrait, ScreenOrientation.Landscape })
            {
                using Bitmap preview = UsbScreenRenderer.Render(state, previewOrientation);
                preview.Save(Path.Combine(previewDirectory, $"{state}-{previewOrientation}.png"), ImageFormat.Png);
            }
        }

        IpcResponse? current = await new IpcClient().SendAsync(IpcMessage.Query(), true, CancellationToken.None);
        TrayState previous = current?.State ?? TrayState.Inactive;
        using var transport = new CountingTransport(new SerialScreenTransport(port));
        using var screen = new TuringScreenConnection(transport);
        try
        {
            foreach (TrayState state in new[] { TrayState.Inactive, TrayState.Busy, TrayState.Error, TrayState.Ready })
            {
                screen.Show(state, orientation, CancellationToken.None);
                await Task.Delay(750);
            }
        }
        finally
        {
            screen.Show(previous, orientation, CancellationToken.None);
        }
        int restoreSize = previous == TrayState.Ready ? 112 * 112 * 2 : 320 * 480 * 2;
        Assert.Equal(28 + 4 * (6 + 320 * 480 * 2) + 6 + restoreSize, transport.BytesWritten);
    }

    [UsbScreenHardwareFact]
    [Trait("Category", "UsbHardware")]
    public async Task RealScreen_AcceptsAnimationFramesOfUnchangedStatus()
    {
        string port = Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_PORT")!;
        Assert.Matches(new Regex(@"\ACOM[1-9][0-9]*\z", RegexOptions.IgnoreCase), port);
        var orientation = Enum.Parse<ScreenOrientation>(Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_ORIENTATION") ?? "Portrait");
        IpcResponse? current = await new IpcClient().SendAsync(IpcMessage.Query(), true, CancellationToken.None);
        TrayState previous = current?.State ?? TrayState.Inactive;
        using var transport = new CountingTransport(new SerialScreenTransport(port));
        using var screen = new TuringScreenConnection(transport);
        string previews = Path.Combine(FindRepositoryRoot(), "artifacts", "usb-screen-previews");
        Directory.CreateDirectory(previews);
        try
        {
            foreach (long step in new long[] { 0, 1, 12, 25, 39 })
            {
                screen.Show(TrayState.Ready, orientation, CancellationToken.None, step);
                using Bitmap preview = UsbScreenRenderer.Render(TrayState.Ready, orientation, step);
                preview.Save(Path.Combine(previews, $"Ready-{orientation}-step-{step}.png"), ImageFormat.Png);
                await Task.Delay(750);
            }
        }
        finally
        {
            screen.Show(previous, orientation, CancellationToken.None);
        }
        Assert.Equal(28 + 6 * (6 + 320 * 480 * 2), transport.BytesWritten);
    }

    [UsbScreenHardwareFact]
    [Trait("Category", "UsbHardware")]
    public async Task RealScreen_AnimatesMascotWithPartialUpdates()
    {
        string port = Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_PORT")!;
        Assert.Matches(new Regex(@"\ACOM[1-9][0-9]*\z", RegexOptions.IgnoreCase), port);
        var orientation = Enum.Parse<ScreenOrientation>(Environment.GetEnvironmentVariable("CODEXTRAY_TEST_USB_ORIENTATION") ?? "Portrait");
        IpcResponse? current = await new IpcClient().SendAsync(IpcMessage.Query(), true, CancellationToken.None);
        TrayState previous = current?.State ?? TrayState.Inactive;
        using var transport = new CountingTransport(new SerialScreenTransport(port));
        using var screen = new TuringScreenConnection(transport);
        string previews = Path.Combine(FindRepositoryRoot(), "artifacts", "usb-screen-previews");
        Directory.CreateDirectory(previews);
        try
        {
            foreach (TrayState state in new[] { TrayState.Ready, TrayState.Busy, TrayState.Error, TrayState.Inactive })
            {
                screen.Show(state, orientation, CancellationToken.None);
                for (long frame = 1; frame <= 3; frame++)
                {
                    int before = transport.BytesWritten;
                    screen.Show(state, orientation, CancellationToken.None, 0, frame);
                    Assert.Equal(6 + 112 * 112 * 2, transport.BytesWritten - before);
                    using Bitmap preview = UsbScreenRenderer.Render(state, orientation, 0, frame);
                    preview.Save(Path.Combine(previews, $"{state}-{orientation}-mascot-{frame}.png"), ImageFormat.Png);
                    await Task.Delay(350);
                }
            }
        }
        finally
        {
            screen.Show(previous, orientation, CancellationToken.None);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodexTray.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
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
