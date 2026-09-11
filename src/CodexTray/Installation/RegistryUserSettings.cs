using Microsoft.Win32;

namespace CodexTray;

internal sealed class RegistryUserSettings : IUserSettings
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NotificationsValueName = "NotificationsEnabled";
    private const string DistributionValueName = "WslDistribution";

    public bool NotificationsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AppConstants.RegistrySubKey);
            return key?.GetValue(NotificationsValueName) switch
            {
                int value => value != 0,
                string value when bool.TryParse(value, out bool parsed) => parsed,
                _ => true,
            };
        }
        set
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AppConstants.RegistrySubKey, writable: true);
            key.SetValue(NotificationsValueName, value ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    public string? WslDistribution
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AppConstants.RegistrySubKey);
            return key?.GetValue(DistributionValueName) as string;
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                using RegistryKey? existing = Registry.CurrentUser.OpenSubKey(
                    AppConstants.RegistrySubKey,
                    writable: true);
                existing?.DeleteValue(DistributionValueName, throwOnMissingValue: false);
                return;
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AppConstants.RegistrySubKey, writable: true);
            key.SetValue(DistributionValueName, value, RegistryValueKind.String);
        }
    }

    public bool StartupEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunSubKey);
            return key?.GetValue(AppConstants.StartupValueName) is string value &&
                !string.IsNullOrWhiteSpace(value);
        }
    }

    public void SetStartup(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true);
        key.SetValue(
            AppConstants.StartupValueName,
            $"\"{exePath.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            RegistryValueKind.String);
    }

    public void RemoveStartup()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
        key?.DeleteValue(AppConstants.StartupValueName, throwOnMissingValue: false);
    }

    public void ClearApplicationSettings()
    {
        Registry.CurrentUser.DeleteSubKeyTree(AppConstants.RegistrySubKey, throwOnMissingSubKey: false);
    }

    public void Dispose()
    {
    }
}
