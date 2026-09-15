using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CodexTray;

internal sealed class RegistryUserSettings : IUserSettings
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NotificationsValueName = "NotificationsEnabled";
    private const string DistributionValueName = "WslDistribution";

    public UsbScreenOptions UsbScreen
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AppConstants.RegistrySubKey);
            string port = key?.GetValue("UsbScreenPort") as string ?? "AUTO";
            if (!Regex.IsMatch(port, @"\A(AUTO|OFF|COM[1-9][0-9]*)\z", RegexOptions.IgnoreCase)) port = "AUTO";
            var orientation = key?.GetValue("UsbScreenOrientation") is int value && Enum.IsDefined(typeof(ScreenOrientation), value)
                ? (ScreenOrientation)value : ScreenOrientation.Portrait;
            bool animation = key?.GetValue("UsbScreenAnimationEnabled") is not int enabled || enabled != 0;
            bool mascot = key?.GetValue("UsbScreenMascotEnabled") is not int visible || visible != 0;
            return new UsbScreenOptions(port.ToUpperInvariant(), orientation, animation, mascot);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!Regex.IsMatch(value.Port, @"\A(AUTO|OFF|COM[1-9][0-9]*)\z", RegexOptions.IgnoreCase) ||
                !Enum.IsDefined(value.Orientation))
                throw new ArgumentException("Invalid USB screen port or orientation.", nameof(value));
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AppConstants.RegistrySubKey, writable: true);
            key.SetValue("UsbScreenPort", value.Port.ToUpperInvariant(), RegistryValueKind.String);
            key.SetValue("UsbScreenOrientation", (int)value.Orientation, RegistryValueKind.DWord);
            key.SetValue("UsbScreenAnimationEnabled", value.AnimationEnabled ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("UsbScreenMascotEnabled", value.MascotEnabled ? 1 : 0, RegistryValueKind.DWord);
        }
    }

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
