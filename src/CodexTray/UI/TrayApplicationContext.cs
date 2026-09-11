using System.Drawing;
using System.IO;
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
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _icons = Enum.GetValues<TrayState>().ToDictionary(state => state, TrayIconFactory.Create);

        var titleItem = new ToolStripMenuItem("Codex Tray Indicator") { Enabled = false };
        _statusItem = new ToolStripMenuItem { Enabled = false };
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
            new ToolStripSeparator(),
            _startupItem,
            _notificationsItem,
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
        UpdateVisual(_stateStore.Current);

        _server.ProtocolError += ServerOnProtocolError;
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
                    _uiContext.Post(_ => ExitThread(), null);
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
        _serverCancellation.Cancel();
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
