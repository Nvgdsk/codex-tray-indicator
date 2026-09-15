using System.Drawing;
using System.Drawing.Imaging;

namespace CodexTray;

internal static class UsbMascotRenderer
{
    public const int Size = 112;

    public static Bitmap Render(TrayState state, long frame)
    {
        var bitmap = new Bitmap(Size, Size, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(UsbScreenRenderer.Background);
        using var outline = new SolidBrush(Color.FromArgb(0x33, 0x41, 0x55));
        using var body = new SolidBrush(state == TrayState.Inactive
            ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0xE2, 0xE8, 0xF0));
        using var highlight = new SolidBrush(Color.FromArgb(0xF8, 0xFA, 0xFC));
        using var visor = new SolidBrush(UsbScreenRenderer.Background);
        using var accent = new SolidBrush(TrayIconFactory.ColorFor(state));
        int phase = (int)(Math.Max(0, frame) % 8);
        int bob = phase is 1 or 2 or 5 or 6 ? 1 : 0;
        void Rect(Brush brush, int x, int y, int width, int height) =>
            graphics.FillRectangle(brush, x * 4, y * 4, width * 4, height * 4);

        // A 28×28 pixel robot, drawn at an exact 4× scale without interpolation.
        Rect(outline, 13, 1 + bob, 2, 4);
        Rect(state == TrayState.Error && phase % 2 == 1 ? outline : accent, 12, bob, 4, 2);
        Rect(outline, 7, 5 + bob, 14, 10);
        Rect(outline, 8, 4 + bob, 12, 12);
        Rect(body, 8, 5 + bob, 12, 10);
        Rect(highlight, 9, 5 + bob, 10, 1);
        Rect(visor, 9, 7 + bob, 10, 7);
        Rect(outline, 12, 16 + bob, 4, 1);
        Rect(outline, 9, 17 + bob, 10, 7);
        Rect(body, 10, 17 + bob, 8, 6);
        Rect(outline, 12, 19 + bob, 4, 3);
        Rect(accent, 13, 20 + bob, 2, 1);
        Rect(outline, 8, 25, 5, 2);
        Rect(outline, 15, 25, 5, 2);
        Rect(body, 9, 25, 3, 1);
        Rect(body, 16, 25, 3, 1);

        if (state == TrayState.Inactive)
        {
            Rect(accent, 10, 11 + bob, 2, 1);
            Rect(accent, 16, 11 + bob, 2, 1);
            Rect(outline, 5, 18 + bob, 4, 2);
            Rect(outline, 19, 18 + bob, 4, 2);
            Rect(body, 5, 20 + bob, 3, 2);
            Rect(body, 20, 20 + bob, 3, 2);
            int z = 2 + phase % 3;
            Rect(accent, 23, z, 3, 1);
            Rect(accent, 24, z + 1, 1, 1);
            Rect(accent, 23, z + 2, 3, 1);
        }
        else if (state == TrayState.Error)
        {
            foreach (int eyeX in new[] { 10, 16 })
            {
                Rect(accent, eyeX, 9 + bob, 1, 1);
                Rect(accent, eyeX + 1, 10 + bob, 1, 1);
                Rect(accent, eyeX, 11 + bob, 1, 1);
            }
            Rect(accent, 12, 13 + bob, 4, 1);
            int hands = phase % 2 == 0 ? 10 : 12;
            Rect(outline, 5, 15 + bob, 4, 2);
            Rect(outline, 19, 15 + bob, 4, 2);
            Rect(body, 3, hands, 3, 4);
            Rect(body, 22, hands, 3, 4);
        }
        else
        {
            int eyeHeight = phase == 6 ? 1 : 3;
            Rect(accent, 10, 9 + bob, 2, eyeHeight);
            Rect(accent, 16, 9 + bob, 2, eyeHeight);
            Rect(accent, 12, 13 + bob, 4, 1);
            if (state == TrayState.Busy)
            {
                Rect(outline, 5, 19 + bob, 4, 2);
                Rect(outline, 19, 19 + bob, 4, 2);
                Rect(body, 6, 20 + phase % 2, 3, 2);
                Rect(body, 19, 21 - phase % 2, 3, 2);
                Rect(outline, 4, 23, 20, 3);
                Rect(body, 5, 23, 18, 1);
                for (int key = 0; key < 6; key++)
                    Rect(key == phase % 6 ? accent : visor, 6 + key * 3, 24, 2, 1);
            }
            else
            {
                Rect(outline, 5, 18 + bob, 4, 2);
                Rect(body, 5, 20 + bob, 3, 2);
                Rect(outline, 19, 17 + bob, 4, 2);
                int wave = (phase % 4) switch { 1 => 2, 2 => 4, _ => 0 };
                Rect(body, 22, 16 - wave, 3, 3);
            }
        }
        return bitmap;
    }
}
