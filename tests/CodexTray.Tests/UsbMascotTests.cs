using System.IO;

namespace CodexTray.Tests;

public sealed class UsbMascotTests
{
    [Theory]
    [InlineData((int)TrayState.Ready)]
    [InlineData((int)TrayState.Busy)]
    [InlineData((int)TrayState.Inactive)]
    [InlineData((int)TrayState.Error)]
    public void Mascot_HasAnimatedFramesForEveryState(int stateValue)
    {
        using var first = UsbMascotRenderer.Render((TrayState)stateValue, 0);
        using var next = UsbMascotRenderer.Render((TrayState)stateValue, 1);
        Assert.Equal(112, first.Width);
        Assert.Equal(112, first.Height);
        Assert.False(UsbScreenRenderer.ToRgb565(first).AsSpan().SequenceEqual(UsbScreenRenderer.ToRgb565(next)));
    }

    [Fact]
    public void Mascot_DistinguishesWorkingRestingSleepingAndError()
    {
        var frames = Enum.GetValues<TrayState>().Select(state =>
        {
            using var bitmap = UsbMascotRenderer.Render(state, 0);
            return UsbScreenRenderer.ToRgb565(bitmap);
        }).ToArray();
        for (int i = 0; i < frames.Length; i++)
            for (int j = i + 1; j < frames.Length; j++) Assert.False(frames[i].AsSpan().SequenceEqual(frames[j]));
    }

    [Fact]
    public void Protocol_AnimatesOnlyMascotRectangleAndUsesAbsoluteScreenCoordinates()
    {
        using var transport = new RecordingTransport();
        using var screen = new TuringScreenConnection(transport);
        screen.Show(TrayState.Ready, ScreenOrientation.Portrait, CancellationToken.None, 0, 0);
        int initialBytes = (int)transport.Bytes.Length;
        screen.Show(TrayState.Ready, ScreenOrientation.Portrait, CancellationToken.None, 0, 1);
        byte[] delta = transport.Bytes.ToArray()[initialBytes..];
        Assert.Equal(6 + 112 * 112 * 2, delta.Length);
        // Portrait origin (30,104), mascot at offset (74,48): (104,152)..(215,263).
        Assert.Equal(new byte[] { 26, 9, 131, 93, 7, 197 }, delta[..6]);
        using var mascot = UsbMascotRenderer.Render(TrayState.Ready, 1);
        Assert.Equal(UsbScreenRenderer.ToRgb565(mascot), delta[6..]);
    }

    [Fact]
    public void Protocol_StateChangeOrPixelShiftRedrawsEntireFrameAndClearsOldMascot()
    {
        using var transport = new RecordingTransport();
        using var screen = new TuringScreenConnection(transport);
        screen.Show(TrayState.Ready, ScreenOrientation.Portrait, CancellationToken.None, 0, 0);
        int initialBytes = (int)transport.Bytes.Length;
        screen.Show(TrayState.Busy, ScreenOrientation.Portrait, CancellationToken.None, 0, 1);
        Assert.Equal(6 + 320 * 480 * 2, transport.Bytes.Length - initialBytes);
        int shiftedStart = (int)transport.Bytes.Length;
        screen.Show(TrayState.Busy, ScreenOrientation.Portrait, CancellationToken.None, 1, 2);
        Assert.Equal(6 + 320 * 480 * 2, transport.Bytes.Length - shiftedStart);
    }

    [Fact]
    public void Protocol_DisabledMascotRemainsStaticAndCanBeEnabledWithFullRedraw()
    {
        using var transport = new RecordingTransport();
        using var screen = new TuringScreenConnection(transport);
        screen.Show(TrayState.Ready, ScreenOrientation.Portrait, CancellationToken.None, 0, 0, false);
        int initialBytes = (int)transport.Bytes.Length;
        screen.Show(TrayState.Ready, ScreenOrientation.Portrait, CancellationToken.None, 0, 1, true);
        Assert.Equal(6 + 320 * 480 * 2, transport.Bytes.Length - initialBytes);
    }

    private sealed class RecordingTransport : IUsbScreenTransport
    {
        public MemoryStream Bytes { get; } = new();
        public void Write(byte[] data, int offset, int count) => Bytes.Write(data, offset, count);
        public void Dispose() { }
    }
}
