namespace CodexTray;

internal static class AppConstants
{
    public const string PipeName = "CodexTray.Status.v1";
    public const string IntegrationId = "codex-tray-indicator-v1";
    public const int ProtocolVersion = 1;
    public const int MaxMessageBytes = 65_536;
    public static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan PipeIoTimeout = TimeSpan.FromSeconds(1);
    public const string RegistrySubKey = @"Software\CodexTray";
    public const string StartupValueName = "CodexTray";
}
