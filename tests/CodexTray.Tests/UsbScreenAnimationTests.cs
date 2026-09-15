namespace CodexTray.Tests;

public sealed class UsbScreenAnimationTests
{
    [Theory]
    [InlineData((int)ScreenOrientation.Portrait)]
    [InlineData((int)ScreenOrientation.ReversePortrait)]
    [InlineData((int)ScreenOrientation.Landscape)]
    [InlineData((int)ScreenOrientation.ReverseLandscape)]
    public void Animation_MovesUnchangedStatusAndClearsPreviousPixels(int orientationValue)
    {
        var orientation = (ScreenOrientation)orientationValue;
        using var first = UsbScreenRenderer.Render(TrayState.Ready, orientation, 0);
        using var moved = UsbScreenRenderer.Render(TrayState.Ready, orientation, 25);
        byte[] original = UsbScreenRenderer.ToRgb565(first);
        byte[] next = UsbScreenRenderer.ToRgb565(moved);
        Assert.False(original.AsSpan().SequenceEqual(next));
        Assert.Contains(Enumerable.Range(0, original.Length / 2), index =>
            original[index * 2] != next[index * 2] &&
            next[index * 2] == next[0] && next[index * 2 + 1] == next[1]);
    }

    [Theory]
    [InlineData((int)ScreenOrientation.Portrait)]
    [InlineData((int)ScreenOrientation.ReversePortrait)]
    [InlineData((int)ScreenOrientation.Landscape)]
    [InlineData((int)ScreenOrientation.ReverseLandscape)]
    public void Animation_KeepsAllContentAwayFromEdgesAcrossBounceAndLongUptime(int orientationValue)
    {
        foreach (long step in new long[] { 0, 1, 19, 40, 80, 200, long.MaxValue })
        {
            using var bitmap = UsbScreenRenderer.Render(TrayState.Busy, (ScreenOrientation)orientationValue, step);
            byte[] pixels = UsbScreenRenderer.ToRgb565(bitmap);
            int foreground = 0;
            for (int index = 0; index < pixels.Length / 2; index++)
            {
                if (pixels[index * 2] == pixels[0] && pixels[index * 2 + 1] == pixels[1]) continue;
                foreground++;
                Assert.InRange(index % bitmap.Width, 24, bitmap.Width - 25);
                Assert.InRange(index / bitmap.Width, 24, bitmap.Height - 25);
            }
            Assert.True(foreground > 1000);
        }
    }

    [Theory]
    [InlineData((int)ScreenOrientation.Portrait)]
    [InlineData((int)ScreenOrientation.Landscape)]
    public void Animation_MovesFarEnoughToClearEntireSolidIndicator(int orientationValue)
    {
        HashSet<int>? fixedAccent = null;
        foreach (long step in new long[] { 0, 12, 25, 39, 43, 53, 120, 129 })
        {
            using var bitmap = UsbScreenRenderer.Render(TrayState.Ready, (ScreenOrientation)orientationValue, step);
            byte[] pixels = UsbScreenRenderer.ToRgb565(bitmap);
            // Exact RGB565 value of the Ready green.
            var accent = Enumerable.Range(0, pixels.Length / 2)
                .Where(index => pixels[index * 2] == 0x2B && pixels[index * 2 + 1] == 0x26).ToHashSet();
            Assert.NotEmpty(accent);
            if (fixedAccent is null) fixedAccent = accent;
            else fixedAccent.IntersectWith(accent);
        }
        Assert.Empty(fixedAccent!);
    }
}
