namespace CodexTray;

internal enum ScreenOrientation
{
    Portrait,
    ReversePortrait,
    Landscape,
    ReverseLandscape,
}

internal sealed record UsbScreenOptions(string Port = "AUTO", ScreenOrientation Orientation = ScreenOrientation.Portrait,
    bool AnimationEnabled = true, bool MascotEnabled = true)
{
    public bool Enabled => Port != "OFF";

    public int Width => Orientation is ScreenOrientation.Landscape or ScreenOrientation.ReverseLandscape ? 480 : 320;

    public int Height => 153600 / Width;
}
