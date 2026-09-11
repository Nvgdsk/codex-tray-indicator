using System.Drawing;

namespace CodexTray.Tests;

public sealed class TrayIconFactoryTests
{
    [Theory]
    [InlineData((int)TrayState.Inactive)]
    [InlineData((int)TrayState.Ready)]
    [InlineData((int)TrayState.Busy)]
    [InlineData((int)TrayState.Error)]
    public void Create_ReturnsDisposableColoredIconWithUsableTraySizes(int stateValue)
    {
        TrayState state = (TrayState)stateValue;
        Icon icon = TrayIconFactory.Create(state);

        Assert.NotEqual(IntPtr.Zero, icon.Handle);
        Assert.Equal(32, icon.Width);
        Assert.Equal(32, icon.Height);
        using var small = new Icon(icon, new Size(16, 16));
        using var large = new Icon(icon, new Size(32, 32));
        Assert.NotEqual(IntPtr.Zero, small.Handle);
        Assert.NotEqual(IntPtr.Zero, large.Handle);

        using Bitmap bitmap = icon.ToBitmap();
        Assert.Contains(
            Enumerable.Range(0, bitmap.Width)
                .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y))),
            pixel => pixel.A > 0 && pixel.R + pixel.G + pixel.B > 0);

        icon.Dispose();
        icon.Dispose();
    }
}
