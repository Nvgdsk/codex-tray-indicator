using System.Drawing;
using System.IO;

namespace CodexTray.Tests;

public sealed class UsbScreenTests
{
    [Fact]
    public void Protocol_SendsPortraitSetupAndCompleteLittleEndianRgb565Frame()
    {
        using var transport = new RecordingTransport();
        using var screen = new TuringScreenConnection(transport);
        screen.Show(TrayState.Ready, ScreenOrientation.Portrait, CancellationToken.None);

        byte[] bytes = transport.Bytes.ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 121, 100, 1, 64, 1, 224, 0, 0, 0, 0, 0 }, bytes[..16]);
        Assert.Equal(109, bytes[21]); // Screen on.
        Assert.Equal(110, bytes[27]); // Brightness.
        Assert.Equal(new byte[] { 0, 0, 4, 253, 223, 197 }, bytes[28..34]);
        Assert.Equal(34 + 320 * 480 * 2, bytes.Length);
        Assert.All(transport.WriteSizes.Skip(4), size => Assert.InRange(size, 1, 320 * 8));
        using Bitmap frame = UsbScreenRenderer.Render(TrayState.Ready, ScreenOrientation.Portrait);
        Color pixel = frame.GetPixel(160, 205);
        ushort rgb565 = (ushort)(((pixel.R >> 3) << 11) | ((pixel.G >> 2) << 5) | (pixel.B >> 3));
        int offset = 34 + (205 * 320 + 160) * 2;
        Assert.Equal((byte)rgb565, bytes[offset]);
        Assert.Equal((byte)(rgb565 >> 8), bytes[offset + 1]);
    }

    [Theory]
    [InlineData((int)ScreenOrientation.Portrait, 320, 480, 100)]
    [InlineData((int)ScreenOrientation.ReversePortrait, 320, 480, 101)]
    [InlineData((int)ScreenOrientation.Landscape, 480, 320, 102)]
    [InlineData((int)ScreenOrientation.ReverseLandscape, 480, 320, 103)]
    public void RendererAndProtocol_UseSelectedOrientation(int orientationValue, int width, int height, int command)
    {
        var orientation = (ScreenOrientation)orientationValue;
        using Bitmap bitmap = UsbScreenRenderer.Render(TrayState.Busy, orientation);
        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);
        using var transport = new RecordingTransport();
        using var screen = new TuringScreenConnection(transport);
        screen.Show(TrayState.Busy, orientation, CancellationToken.None);
        Assert.Equal(command, transport.Bytes.ToArray()[6]);
        Assert.Equal(34 + width * height * 2, transport.Bytes.Length);
    }

    [Theory]
    [InlineData((int)TrayState.Ready, 0x22C55E)]
    [InlineData((int)TrayState.Busy, 0xEAB308)]
    [InlineData((int)TrayState.Error, 0xEF4444)]
    [InlineData((int)TrayState.Inactive, 0x6B7280)]
    public void Renderer_DuplicatesTrayColor(int stateValue, int rgb)
    {
        using Bitmap bitmap = UsbScreenRenderer.Render((TrayState)stateValue, ScreenOrientation.Portrait);
        Assert.Contains(Enumerable.Range(0, bitmap.Width).SelectMany(x =>
            Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y).ToArgb() & 0xFFFFFF)), pixel => pixel == rgb);
    }

    [Fact]
    public void Discovery_OnlyAutoSelectsSingleRecognizedTuringDevice()
    {
        UsbSerialDevice[] devices =
        [
            new("COM1", @"ACPI\PNP0501\0"),
            new("COM3", @"USB\VID_1A86&PID_5722\USB35INCHIPSV2"),
            new("COM8", @"USB\VID_1A86&PID_7523\OTHER"),
        ];
        Assert.Equal("COM3", UsbScreenPortDiscovery.SelectAutoPort(devices));
        Assert.Null(UsbScreenPortDiscovery.SelectAutoPort(devices[..1]));
        Assert.Null(UsbScreenPortDiscovery.SelectAutoPort(devices.Append(
            new UsbSerialDevice("COM4", @"USB\VID_1A86&PID_5722\ANOTHER"))));
    }

    [Fact]
    public void Protocol_CancelledFrameWritesNothing()
    {
        using var transport = new RecordingTransport();
        using var screen = new TuringScreenConnection(transport);
        Assert.Throws<OperationCanceledException>(() => screen.Show(
            TrayState.Ready, ScreenOrientation.Portrait, new CancellationToken(true)));
        Assert.Equal(0, transport.Bytes.Length);
    }

    [Fact]
    public void Rgb565_UsesRowOrderAndLittleEndianPrimaryColorsWithPaddedBitmapStride()
    {
        using var bitmap = new Bitmap(3, 2, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        Color[] colors = [Color.Red, Color.Lime, Color.Blue, Color.White, Color.Black, Color.Yellow];
        for (int i = 0; i < colors.Length; i++) bitmap.SetPixel(i % 3, i / 3, colors[i]);
        Assert.Equal(new byte[] { 0, 248, 224, 7, 31, 0, 255, 255, 0, 0, 224, 255 },
            UsbScreenRenderer.ToRgb565(bitmap));
    }

    [Fact]
    public void CancellationDuringPixelTransfer_CompletesFrameBeforeNextControlCommand()
    {
        using var cancellation = new CancellationTokenSource();
        using var transport = new RecordingTransport(size => { if (size > 16) cancellation.Cancel(); });
        using var screen = new TuringScreenConnection(transport);
        screen.Show(TrayState.Ready, ScreenOrientation.Portrait, cancellation.Token);
        Assert.Equal(34 + 320 * 480 * 2, transport.Bytes.Length);
        screen.TurnOff();
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 108 }, transport.Bytes.ToArray()[^6..]);
    }

    private sealed class RecordingTransport(Action<int>? onWrite = null) : IUsbScreenTransport
    {
        public MemoryStream Bytes { get; } = new();
        public List<int> WriteSizes { get; } = [];
        public void Write(byte[] data, int offset, int count)
        {
            WriteSizes.Add(count);
            Bytes.Write(data, offset, count);
            onWrite?.Invoke(count);
        }
        public void Dispose() { }
    }
}
