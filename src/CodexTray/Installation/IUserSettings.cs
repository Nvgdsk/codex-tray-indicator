namespace CodexTray;

internal interface IUserSettings : IDisposable
{
    bool NotificationsEnabled { get; set; }

    string? WslDistribution { get; set; }

    bool StartupEnabled { get; }

    void SetStartup(string exePath);

    void RemoveStartup();

    void ClearApplicationSettings();
}
