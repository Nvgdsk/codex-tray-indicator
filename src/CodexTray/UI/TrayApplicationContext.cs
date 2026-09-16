using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Windows.Forms;

namespace CodexTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SessionStateStore _stateStore;
    private readonly IpcServer _server;
    private readonly IUserSettings _settings;
    private readonly Mutex _mutex;
    private readonly SynchronizationContext _uiContext;
    private readonly CancellationTokenSource _serverCancellation = new();
    private readonly Dictionary<TrayState, Icon> _icons;
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _notificationsItem;
    private readonly ToolStripMenuItem _usbStatusItem;
    private readonly UsbScreenController _usbScreen;
    private readonly WeeklyLimitMonitor _weeklyLimitMonitor;
    private UsbScreenOptions _usbScreenOptions;
    private bool _disposed;

    public TrayApplicationContext(
        SessionStateStore stateStore,
        IpcServer server,
        IUserSettings settings,
        Mutex mutex)
    {
        _stateStore = stateStore;
        _server = server;
        _settings = settings;
        _mutex = mutex;
        _usbScreenOptions = settings.UsbScreen;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _icons = Enum.GetValues<TrayState>().ToDictionary(state => state, TrayIconFactory.Create);

        var titleItem = new ToolStripMenuItem("Codex Tray Indicator") { Enabled = false };
        _statusItem = new ToolStripMenuItem { Enabled = false };
        _usbStatusItem = new ToolStripMenuItem { Enabled = false };
        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = settings.StartupEnabled,
        };
        _startupItem.Click += StartupItemOnClick;
        _notificationsItem = new ToolStripMenuItem("Notifications")
        {
            CheckOnClick = true,
            Checked = settings.NotificationsEnabled,
        };
        _notificationsItem.Click += NotificationsItemOnClick;
        var testItem = new ToolStripMenuItem("Reconnect / Test");
        testItem.Click += TestItemOnClick;
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            titleItem,
            _statusItem,
            _usbStatusItem,
            new ToolStripSeparator(),
            _startupItem,
            _notificationsItem,
            CreateUsbScreenMenu(),
            testItem,
            new ToolStripSeparator(),
            exitItem,
        ]);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _icons[_stateStore.Current],
            Visible = true,
        };
        _usbScreen = new UsbScreenController(new UsbScreenConnector(), _usbScreenOptions, _stateStore.Current);
        _weeklyLimitMonitor = new WeeklyLimitMonitor(new CodexWeeklyLimitSource(() => _settings.WslDistribution),
            _usbScreen.SetWeeklyLimit, enabled: () => _settings.UsbScreen.Enabled);
        UpdateVisual(_stateStore.Current);
        _usbStatusItem.Text = _usbScreen.Status;
        _usbScreen.StatusChanged += UsbScreenOnStatusChanged;

        _server.ProtocolError += ServerOnProtocolError;
        _server.ResponseSent += ServerOnResponseSent;
        _ = ObserveServerAsync();
    }

    private async Task ObserveServerAsync()
    {
        try
        {
            await _server.RunAsync(HandleMessageAsync, _serverCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception) when (!_serverCancellation.IsCancellationRequested)
        {
            StateTransition transition = _stateStore.SetError();
            PostTransition(transition, cause: null);
        }
    }

    private Task<IpcResponse> HandleMessageAsync(
        IpcMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (message.Kind)
            {
                case IpcMessageKind.Event:
                    {
                        CodexHookEvent hookEvent = message.ToCodexHookEvent();
                        StateTransition transition = _stateStore.Apply(hookEvent);
                        PostTransition(transition, hookEvent.Name);
                        return Task.FromResult(new IpcResponse(true, transition.Current, null));
                    }

                case IpcMessageKind.SetState:
                    {
                        StateTransition transition = _stateStore.SetSynthetic(message.State!.Value);
                        PostTransition(transition, cause: null);
                        return Task.FromResult(new IpcResponse(true, transition.Current, null));
                    }

                case IpcMessageKind.QueryState:
                    return Task.FromResult(new IpcResponse(true, _stateStore.Current, null));

                case IpcMessageKind.Shutdown:
                    return Task.FromResult(new IpcResponse(true, _stateStore.Current, null));

                default:
                    throw new InvalidDataException("Unsupported IPC message kind.");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            StateTransition transition = _stateStore.SetError();
            PostTransition(transition, cause: null);
            return Task.FromResult(new IpcResponse(false, transition.Current, exception.Message));
        }
    }

    private void ServerOnProtocolError(Exception exception)
    {
        StateTransition transition = _stateStore.SetError();
        PostTransition(transition, cause: null);
    }

    private void ServerOnResponseSent(IpcMessage message)
    {
        if (message.Kind == IpcMessageKind.Shutdown)
        {
            _uiContext.Post(_ => ExitThread(), null);
        }
    }

    private void PostTransition(StateTransition transition, CodexHookEventName? cause)
    {
        _uiContext.Post(
            _ =>
            {
                if (_disposed)
                {
                    return;
                }

                if (transition.Changed)
                {
                    UpdateVisual(transition.Current);
                }

                if (_settings.NotificationsEnabled && NotificationPolicy.ShouldNotify(transition, cause))
                {
                    _notifyIcon.ShowBalloonTip(
                        3000,
                        "Codex finished",
                        "Codex is ready for the next prompt.",
                        ToolTipIcon.Info);
                }
            },
            null);
    }

    private void UpdateVisual(TrayState state)
    {
        string label = state switch
        {
            TrayState.Inactive => "Inactive",
            TrayState.Ready => "Ready",
            TrayState.Busy => "Busy",
            TrayState.Error => "Error",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        _notifyIcon.Icon = _icons[state];
        _notifyIcon.Text = $"Codex: {label}";
        _statusItem.Text = $"Status: {label}";
        _usbScreen.SetState(state);
    }

    private ToolStripMenuItem CreateUsbScreenMenu()
    {
        var screenMenu = new ToolStripMenuItem("USB screen (Turing 3.5\")");
        screenMenu.DropDownOpening += (_, _) =>
        {
            foreach (ToolStripItem item in screenMenu.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
            screenMenu.DropDownItems.Clear();
            var auto = new ToolStripMenuItem("Automatic") { Checked = _usbScreenOptions.Port == "AUTO" };
            auto.Click += (_, _) => ConfigureUsbScreen(_usbScreenOptions with { Port = "AUTO" });
            var off = new ToolStripMenuItem("Off") { Checked = !_usbScreenOptions.Enabled };
            off.Click += (_, _) => ConfigureUsbScreen(_usbScreenOptions with { Port = "OFF" });
            screenMenu.DropDownItems.AddRange([auto, off, new ToolStripSeparator()]);
            try
            {
                string[] ports = SerialPort.GetPortNames();
                if (_usbScreenOptions.Port is not "AUTO" and not "OFF")
                    ports = ports.Append(_usbScreenOptions.Port).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (string port in ports.OrderBy(port => port, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new ToolStripMenuItem(port) { Checked = _usbScreenOptions.Port == port };
                    item.Click += (_, _) => ConfigureUsbScreen(_usbScreenOptions with { Port = port });
                    screenMenu.DropDownItems.Add(item);
                }
            }
            catch
            {
                screenMenu.DropDownItems.Add(new ToolStripMenuItem("COM ports unavailable") { Enabled = false });
            }
            var orientationMenu = new ToolStripMenuItem("Orientation");
            foreach (ScreenOrientation orientation in Enum.GetValues<ScreenOrientation>())
            {
                string label = orientation switch
                {
                    ScreenOrientation.Portrait => "Portrait (320 × 480)",
                    ScreenOrientation.ReversePortrait => "Portrait upside down",
                    ScreenOrientation.Landscape => "Landscape (480 × 320)",
                    _ => "Landscape upside down",
                };
                var item = new ToolStripMenuItem(label) { Checked = _usbScreenOptions.Orientation == orientation };
                item.Click += (_, _) => ConfigureUsbScreen(_usbScreenOptions with { Orientation = orientation });
                orientationMenu.DropDownItems.Add(item);
            }
            var reconnect = new ToolStripMenuItem("Reconnect screen") { Enabled = _usbScreenOptions.Enabled };
            reconnect.Click += (_, _) => _usbScreen.Reconnect();
            var animation = new ToolStripMenuItem("Pixel shift animation") { Checked = _usbScreenOptions.AnimationEnabled };
            animation.Click += (_, _) => ConfigureUsbScreen(_usbScreenOptions with { AnimationEnabled = !_usbScreenOptions.AnimationEnabled });
            var mascot = new ToolStripMenuItem("Animated character") { Checked = _usbScreenOptions.MascotEnabled };
            mascot.Click += (_, _) => ConfigureUsbScreen(_usbScreenOptions with { MascotEnabled = !_usbScreenOptions.MascotEnabled });
            screenMenu.DropDownItems.AddRange([new ToolStripSeparator(), orientationMenu, animation, mascot, reconnect]);
        };
        return screenMenu;
    }

    private void ConfigureUsbScreen(UsbScreenOptions options)
    {
        try
        {
            _settings.UsbScreen = options;
            _usbScreenOptions = options;
            _usbScreen.Configure(options);
            _weeklyLimitMonitor.Refresh();
        }
        catch (Exception exception)
        {
            _usbStatusItem.Text = $"USB screen: Cannot save settings — {exception.Message}";
        }
    }

    private void UsbScreenOnStatusChanged(string status)
    {
        _uiContext.Post(_ =>
        {
            if (!_disposed) _usbStatusItem.Text = status;
        }, null);
    }

    private void StartupItemOnClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (_startupItem.Checked)
            {
                _settings.SetStartup(Application.ExecutablePath);
            }
            else
            {
                _settings.RemoveStartup();
            }
        }
        catch
        {
            _startupItem.Checked = _settings.StartupEnabled;
            PostTransition(_stateStore.SetError(), cause: null);
        }
    }

    private void NotificationsItemOnClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            _settings.NotificationsEnabled = _notificationsItem.Checked;
        }
        catch
        {
            _notificationsItem.Checked = _settings.NotificationsEnabled;
            PostTransition(_stateStore.SetError(), cause: null);
        }
    }

    private void TestItemOnClick(object? sender, EventArgs eventArgs)
    {
        _usbScreen.Reconnect();
        _weeklyLimitMonitor.Refresh();
        StateTransition transition = _stateStore.SetSynthetic(TrayState.Ready);
        PostTransition(transition, cause: null);
    }

    protected override void ExitThreadCore()
    {
        if (_disposed)
        {
            base.ExitThreadCore();
            return;
        }

        _disposed = true;
        _server.ProtocolError -= ServerOnProtocolError;
        _server.ResponseSent -= ServerOnResponseSent;
        _serverCancellation.Cancel();
        _usbScreen.StatusChanged -= UsbScreenOnStatusChanged;
        _weeklyLimitMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _usbScreen.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        foreach (Icon icon in _icons.Values)
        {
            icon.Dispose();
        }

        _serverCancellation.Dispose();
        _settings.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex.Dispose();
        base.ExitThreadCore();
    }
}
