using System.Threading.Channels;

namespace CodexTray;

internal interface IUsbScreenConnector
{
    string? ResolvePort(string selection);
    IUsbScreenConnection Connect(string port);
}

internal sealed class UsbScreenConnector : IUsbScreenConnector
{
    public string? ResolvePort(string selection) => UsbScreenPortDiscovery.Resolve(selection);
    public IUsbScreenConnection Connect(string port) => new TuringScreenConnection(new SerialScreenTransport(port));
}

internal sealed class UsbScreenController : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IUsbScreenConnector _connector;
    private readonly TimeSpan _retryInterval;
    private readonly TimeSpan _animationInterval;
    private readonly TimeSpan _mascotInterval = TimeSpan.FromMilliseconds(500);
    private readonly TimeProvider _clock;
    private readonly long _animationStarted;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte> _changes = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly Task _worker;
    private UsbScreenOptions _options;
    private TrayState _state;
    private WeeklyLimit? _weeklyLimit;
    private long _version;
    private int _stopping;
    private string _status = "USB screen: Connecting...";

    public UsbScreenController(IUsbScreenConnector connector, UsbScreenOptions options, TrayState initialState,
        TimeSpan? retryInterval = null, TimeProvider? timeProvider = null, TimeSpan? animationInterval = null)
    {
        _connector = connector;
        _options = options;
        _state = initialState;
        _retryInterval = retryInterval ?? TimeSpan.FromSeconds(3);
        _animationInterval = animationInterval ?? TimeSpan.FromSeconds(10);
        if (_retryInterval <= TimeSpan.Zero || _animationInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryInterval), "USB timer intervals must be positive.");
        _clock = timeProvider ?? TimeProvider.System;
        _animationStarted = _clock.GetTimestamp();
        _worker = Task.Run(RunAsync);
    }

    public event Action<string>? StatusChanged;
    public string Status => Volatile.Read(ref _status);

    public void SetState(TrayState state)
    {
        lock (_gate) _state = state;
        _changes.Writer.TryWrite(0);
    }

    public void SetWeeklyLimit(WeeklyLimit? limit)
    {
        lock (_gate) _weeklyLimit = limit;
        _changes.Writer.TryWrite(0);
    }

    public void Configure(UsbScreenOptions options)
    {
        lock (_gate)
        {
            _options = options;
            _version++;
        }
        _changes.Writer.TryWrite(0);
    }

    public void Reconnect()
    {
        lock (_gate) _version++;
        _changes.Writer.TryWrite(0);
    }

    public Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            _stop.Cancel();
            _changes.Writer.TryComplete();
        }
        return _worker;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        CancellationToken token = _stop.Token;
        IUsbScreenConnection? connection = null;
        string? connectedPort = null;
        TrayState? displayedState = null;
        WeeklyLimit? displayedWeeklyLimit = null;
        long? displayedAnimationStep = null;
        long? displayedMascotFrame = null;
        long? lastPortProbe = null;
        string? availablePort = null;
        long appliedVersion = -1;

        void Close(bool turnOff)
        {
            if (connection is not null)
            {
                if (turnOff)
                {
                    try { connection.TurnOff(); } catch { }
                }
                try { connection.Dispose(); } catch { }
            }
            connection = null;
            connectedPort = null;
            displayedState = null;
            displayedWeeklyLimit = null;
            displayedAnimationStep = null;
            displayedMascotFrame = null;
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                while (_changes.Reader.TryRead(out _)) { }
                UsbScreenOptions options;
                TrayState state;
                WeeklyLimit? weeklyLimit;
                long version;
                lock (_gate) (options, state, version, weeklyLimit) = (_options, _state, _version, _weeklyLimit);
                if (weeklyLimit is not null && weeklyLimit.ResetsAt <= _clock.GetUtcNow()) weeklyLimit = null;
                try
                {
                    if (appliedVersion != version)
                    {
                        Close(turnOff: !options.Enabled);
                        appliedVersion = version;
                        lastPortProbe = null;
                    }
                    if (!options.Enabled)
                    {
                        SetStatus("USB screen: Off");
                    }
                    else
                    {
                        long timestamp = _clock.GetTimestamp();
                        if (connection is null || lastPortProbe is null ||
                            _clock.GetElapsedTime(lastPortProbe.Value, timestamp) >= _retryInterval)
                        {
                            availablePort = _connector.ResolvePort(options.Port);
                            lastPortProbe = timestamp;
                        }
                        string? port = availablePort;
                        token.ThrowIfCancellationRequested();
                        if (!string.Equals(port, connectedPort, StringComparison.OrdinalIgnoreCase))
                        {
                            Close(turnOff: false);
                        }
                        if (port is null)
                        {
                            SetStatus(options.Port == "AUTO"
                                ? "USB screen: Not found / choose COM port"
                                : $"USB screen: {options.Port} not connected");
                        }
                        else
                        {
                            if (connection is null)
                            {
                                connection = _connector.Connect(port);
                                connectedPort = port;
                            }
                            long animationStep = options.AnimationEnabled
                                ? _clock.GetElapsedTime(_animationStarted).Ticks / _animationInterval.Ticks : 0;
                            long mascotFrame = options.MascotEnabled
                                ? _clock.GetElapsedTime(_animationStarted).Ticks / _mascotInterval.Ticks : 0;
                            if (displayedState != state || displayedAnimationStep != animationStep || displayedMascotFrame != mascotFrame ||
                                displayedWeeklyLimit != weeklyLimit)
                            {
                                connection.Show(state, options.Orientation, token, animationStep, mascotFrame, options.MascotEnabled, weeklyLimit);
                                displayedState = state;
                                displayedAnimationStep = animationStep;
                                displayedMascotFrame = mascotFrame;
                                displayedWeeklyLimit = weeklyLimit;
                            }
                            else
                            {
                                // A fast replug can preserve the COM name while invalidating
                                // the open handle. A six-byte screen-on command detects it.
                                connection.CheckConnection();
                            }
                            SetStatus($"USB screen: Connected ({port})");
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Close(turnOff: false);
                    SetStatus($"USB screen: Unavailable — {exception.Message.Split('\n')[0].Trim()}");
                }

                // USB availability, pixel shift and mascot have independent clocks.
                // Codex state remains driven by IPC events.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (options.Enabled)
                {
                    TimeSpan interval = _retryInterval;
                    if (options.AnimationEnabled && connection is not null)
                    {
                        long elapsedTicks = _clock.GetElapsedTime(_animationStarted).Ticks;
                        TimeSpan untilAnimation = TimeSpan.FromTicks(_animationInterval.Ticks - elapsedTicks % _animationInterval.Ticks);
                        if (untilAnimation < interval) interval = untilAnimation;
                    }
                    if (options.MascotEnabled && connection is not null)
                    {
                        long elapsedTicks = _clock.GetElapsedTime(_animationStarted).Ticks;
                        TimeSpan untilMascot = TimeSpan.FromTicks(_mascotInterval.Ticks - elapsedTicks % _mascotInterval.Ticks);
                        if (untilMascot < interval) interval = untilMascot;
                    }
                    wait.CancelAfter(interval);
                }
                try
                {
                    await _changes.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            Close(turnOff: true);
        }
    }

    private void SetStatus(string status)
    {
        if (Status == status) return;
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(status);
    }
}
