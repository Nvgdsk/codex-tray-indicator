using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CodexTray;

internal static class UsbScreenRenderer
{
    internal static readonly Color Background = Color.FromArgb(0x0F, 0x17, 0x2A);

    public static Bitmap Render(TrayState state, ScreenOrientation orientation, long animationStep = 0,
        long mascotFrame = 0, bool mascotEnabled = true, WeeklyLimit? weeklyLimit = null)
    {
        var options = new UsbScreenOptions(Orientation: orientation);
        int width = options.Width;
        int height = options.Height;
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Background);
        using var accent = new SolidBrush(TrayIconFactory.ColorFor(state));
        using var white = new SolidBrush(Color.FromArgb(0xF8, 0xFA, 0xFC));
        using var titleFont = new Font("Segoe UI", 30, FontStyle.Bold, GraphicsUnit.Pixel);
        using var stateFont = new Font("Segoe UI", 42, FontStyle.Bold, GraphicsUnit.Pixel);
        using var detailFont = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        const int contentWidth = 260;
        Point origin = ContentOrigin(orientation, animationStep);
        graphics.TranslateTransform(origin.X, origin.Y);
        graphics.DrawString("CODEX", titleFont, white, new RectangleF(0, 0, contentWidth, 40), center);
        if (mascotEnabled)
        {
            using Bitmap mascot = UsbMascotRenderer.Render(state, mascotFrame);
            graphics.DrawImageUnscaled(mascot, 74, 48);
        }
        else
        {
            graphics.FillEllipse(accent, contentWidth / 2 - 45, 56, 90, 90);
        }
        graphics.DrawString(state.ToString(), stateFont, accent,
            new RectangleF(0, 166, contentWidth, 58), center);
        string detail = weeklyLimit is null ? "Weekly remaining: —" : $"Weekly remaining: {weeklyLimit.RemainingPercent}%";
        graphics.DrawString(detail, detailFont, white,
            new RectangleF(0, 232, contentWidth, 30), center);
        using var track = new SolidBrush(Color.FromArgb(0x33, 0x41, 0x55));
        graphics.FillRectangle(track, 8, 263, contentWidth - 16, 7);
        if (weeklyLimit is not null && weeklyLimit.RemainingPercent > 0)
        {
            using var fill = new SolidBrush(weeklyLimit.RemainingPercent <= 10 ? Color.OrangeRed :
                weeklyLimit.RemainingPercent <= 25 ? Color.Gold : Color.FromArgb(0x22, 0xC5, 0x5E));
            graphics.FillRectangle(fill, 8, 263, (contentWidth - 16) * weeklyLimit.RemainingPercent / 100f, 7);
        }
        return bitmap;
    }

    public static Rectangle MascotBounds(ScreenOrientation orientation, long animationStep)
    {
        Point origin = ContentOrigin(orientation, animationStep);
        return new Rectangle(origin.X + 74, origin.Y + 48, UsbMascotRenderer.Size, UsbMascotRenderer.Size);
    }

    private static Point ContentOrigin(ScreenOrientation orientation, long step)
    {
        var options = new UsbScreenOptions(Orientation: orientation);
        return new Point(24 + Bounce(step, options.Width - 260 - 48), 24 + Bounce(step, options.Height - 272 - 48));
    }

    private static int Bounce(long step, int travel)
    {
        if (travel <= 0) return 0;
        int period = travel * 2;
        // Reduce before multiplying so arbitrarily long uptime cannot overflow.
        long phase = (travel / 2 + Math.Max(0, step) % period * 6) % period;
        return (int)(phase <= travel ? phase : period - phase);
    }

    public static byte[] ToRgb565(Bitmap bitmap)
    {
        var result = new byte[bitmap.Width * bitmap.Height * 2];
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[bitmap.Width * 3];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int source = x * 3;
                    int value = ((row[source + 2] >> 3) << 11) | ((row[source + 1] >> 2) << 5) | (row[source] >> 3);
                    int target = (y * bitmap.Width + x) * 2;
                    result[target] = (byte)value;
                    result[target + 1] = (byte)(value >> 8);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return result;
    }
}
