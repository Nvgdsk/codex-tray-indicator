using System.IO.Ports;
using Microsoft.Win32;

namespace CodexTray;

internal sealed record UsbSerialDevice(string Port, string DeviceId);

internal static class UsbScreenPortDiscovery
{
    public static string? SelectAutoPort(IEnumerable<UsbSerialDevice> devices)
    {
        string[] ports = devices.Where(device =>
                device.DeviceId.Contains("USB35INCHIPSV2", StringComparison.OrdinalIgnoreCase) ||
                device.DeviceId.Contains("VID_1A86&PID_5722", StringComparison.OrdinalIgnoreCase))
            .Select(device => device.Port).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return ports.Length == 1 ? ports[0] : null;
    }

    public static string? Resolve(string selectedPort)
    {
        if (selectedPort == "OFF")
        {
            return null;
        }
        string[] available = SerialPort.GetPortNames();
        if (selectedPort != "AUTO")
        {
            return available.FirstOrDefault(port => string.Equals(port, selectedPort, StringComparison.OrdinalIgnoreCase));
        }

        var devices = new List<UsbSerialDevice>();
        using RegistryKey? usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb is null)
        {
            return null;
        }
        foreach (string deviceName in usb.GetSubKeyNames())
        {
            using RegistryKey? device = usb.OpenSubKey(deviceName);
            if (device is null)
            {
                continue;
            }
            foreach (string instanceName in device.GetSubKeyNames())
            {
                string id = $@"USB\{deviceName}\{instanceName}";
                if (!id.Contains("USB35INCHIPSV2", StringComparison.OrdinalIgnoreCase) &&
                    !id.Contains("VID_1A86&PID_5722", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                using RegistryKey? parameters = device.OpenSubKey(instanceName + @"\Device Parameters");
                if (parameters?.GetValue("PortName") is string port && available.Contains(port, StringComparer.OrdinalIgnoreCase))
                {
                    devices.Add(new UsbSerialDevice(port, id));
                }
            }
        }
        return SelectAutoPort(devices);
    }
}
