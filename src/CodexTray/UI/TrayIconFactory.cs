using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexTray;

internal static class TrayIconFactory
{
    private const int IconSize = 32;

    public static Icon Create(TrayState state)
    {
        using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var fill = new SolidBrush(ColorFor(state)))
        using (var outline = new Pen(Color.FromArgb(255, 31, 41, 55), 2.5f))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var bounds = new RectangleF(3.5f, 3.5f, 25f, 25f);
            graphics.FillEllipse(fill, bounds);
            graphics.DrawEllipse(outline, bounds);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    internal static Color ColorFor(TrayState state) => state switch
    {
        TrayState.Ready => Color.FromArgb(0x22, 0xC5, 0x5E),
        TrayState.Busy => Color.FromArgb(0xEA, 0xB3, 0x08),
        TrayState.Error => Color.FromArgb(0xEF, 0x44, 0x44),
        TrayState.Inactive => Color.FromArgb(0x6B, 0x72, 0x80),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
