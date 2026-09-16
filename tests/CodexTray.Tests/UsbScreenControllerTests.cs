using System.Collections.Concurrent;
using System.IO;

namespace CodexTray.Tests;

public sealed class UsbScreenControllerTests
{
    [Fact]
    public async Task SlowDisplay_DoesNotBlockUpdatesAndReceivesLatestState()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new ConcurrentQueue<TrayState>();
        var connection = new FakeConnection((state, token) =>
        {
            frames.Enqueue(state);
            if (state == TrayState.Inactive)
            {
                started.TrySetResult();
                release.Wait(token);
            }
            if (state == TrayState.Error) finished.TrySetResult();
        });
        var connector = new FakeConnector(() => "COM3", () => connection);
        await using var controller = new UsbScreenController(connector, new(), TrayState.Inactive, TimeSpan.FromSeconds(10));
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            controller.SetState(TrayState.Busy);
            controller.SetState(TrayState.Ready);
            controller.SetState(TrayState.Error);
        }
        finally
        {
            release.Set();
        }
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { TrayState.Inactive, TrayState.Error }, frames.ToArray());
    }

    [Fact]
    public async Task FailedWrite_ReconnectsAndRendersCurrentState()
    {
        int connections = 0;
        var shown = new TaskCompletionSource<TrayState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var broken = new FakeConnection((_, _) => throw new IOException("USB unplugged"));
        var healthy = new FakeConnection((state, _) => shown.TrySetResult(state));
        var connector = new FakeConnector(() => "COM3", () => Interlocked.Increment(ref connections) == 1 ? broken : healthy);
        await using var controller = new UsbScreenController(connector, new(), TrayState.Busy, TimeSpan.FromMilliseconds(20));
        Assert.Equal(TrayState.Busy, await shown.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        Assert.True(broken.Disposed);
        Assert.Equal(2, connections);
        await controller.StopAsync();
        Assert.True(healthy.Disposed);
        Assert.True(healthy.TurnedOff);
    }

    [Fact]
    public async Task MissingDevice_RetriesWithoutRequiringAnotherCodexEvent()
    {
        int probes = 0;
        var shown = new TaskCompletionSource<TrayState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connector = new FakeConnector(
            () => Interlocked.Increment(ref probes) == 1 ? null : "COM3",
            () => new FakeConnection((state, _) => shown.TrySetResult(state)));
        await using var controller = new UsbScreenController(connector, new(), TrayState.Ready, TimeSpan.FromMilliseconds(20));
        Assert.Equal(TrayState.Ready, await shown.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        Assert.True(probes >= 2);
    }

    [Fact]
    public async Task DisabledDisplay_DoesNotProbeOrOpenSerialPorts()
    {
        var connector = new FakeConnector(
            () => throw new IOException("Must not probe"),
            () => throw new IOException("Must not open"));
        await using var controller = new UsbScreenController(connector, new("OFF"), TrayState.Ready, TimeSpan.FromMilliseconds(20));
        await controller.StopAsync();
        Assert.Equal(0, connector.Resolutions);
    }

    [Fact]
    public async Task ReplugOnSamePortWithUnchangedState_RecoversStaleConnection()
    {
        int connections = 0;
        var restored = new TaskCompletionSource<TrayState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = new FakeConnection((_, _) => { }) { OnCheck = () => throw new IOException("Device removed") };
        var replacement = new FakeConnection((state, _) => restored.TrySetResult(state));
        var connector = new FakeConnector(() => "COM3", () => Interlocked.Increment(ref connections) == 1 ? stale : replacement);
        await using var controller = new UsbScreenController(connector, new(), TrayState.Ready, TimeSpan.FromMilliseconds(20));
        Assert.Equal(TrayState.Ready, await restored.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.True(stale.Disposed);
        Assert.Equal(2, connections);
    }

    [Fact]
    public async Task Reconnect_RedrawsUnchangedStateAndOrientationChangeReopensConnection()
    {
        var rendered = new ConcurrentQueue<(TrayState, ScreenOrientation)>();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = new ConcurrentQueue<FakeConnection>();
        var connector = new FakeConnector(() => "COM3", () =>
        {
            var connection = new FakeConnection((_, _) => { });
            connection.OnShow = (state, orientation) =>
            {
                rendered.Enqueue((state, orientation));
                (rendered.Count switch { 1 => first, 2 => second, _ => third }).TrySetResult();
            };
            connections.Enqueue(connection);
            return connection;
        });
        await using var controller = new UsbScreenController(connector, new(), TrayState.Ready, TimeSpan.FromSeconds(10));
        await first.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        controller.Reconnect();
        await second.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        controller.Configure(new("AUTO", ScreenOrientation.Landscape));
        await third.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(new[]
        {
            (TrayState.Ready, ScreenOrientation.Portrait),
            (TrayState.Ready, ScreenOrientation.Portrait),
            (TrayState.Ready, ScreenOrientation.Landscape),
        }, rendered.ToArray());
        Assert.Equal(3, connections.Count);
        Assert.All(connections.Take(2), connection => Assert.True(connection.Disposed));
    }

    [Fact]
    public async Task Animation_RedrawsWithoutCodexEventsAndUsesCurrentStateAfterClockJump()
    {
        var clock = new AnimationClock();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var animated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jumped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new ConcurrentQueue<(TrayState State, long Step)>();
        var connection = new FakeConnection((_, _) => { });
        connection.OnFrame = (state, step) =>
        {
            frames.Enqueue((state, step));
            if (step == 0) first.TrySetResult();
            if (step == 1) animated.TrySetResult();
            if (state == TrayState.Busy) busy.TrySetResult();
            if (step == 4) jumped.TrySetResult();
        };
        var connector = new FakeConnector(() => "COM3", () => connection);
        await using var controller = new UsbScreenController(connector, new(), TrayState.Ready,
            TimeSpan.FromMilliseconds(20), timeProvider: clock);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(10));
        await animated.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        controller.SetState(TrayState.Busy);
        await busy.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(30));
        await jumped.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { (TrayState.Ready, 0L), (TrayState.Ready, 1L), (TrayState.Busy, 1L), (TrayState.Busy, 4L) }, frames.ToArray());
    }

    [Fact]
    public async Task AnimationDisabled_RetainsStaticFrameAndChecksConnection()
    {
        var clock = new AnimationClock();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkedConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new ConcurrentQueue<long>();
        var connection = new FakeConnection((_, _) => { }) { OnCheck = () => checkedConnection.TrySetResult() };
        connection.OnFrame = (_, step) => { frames.Enqueue(step); first.TrySetResult(); };
        var connector = new FakeConnector(() => "COM3", () => connection);
        await using var controller = new UsbScreenController(connector, new(AnimationEnabled: false, MascotEnabled: false), TrayState.Ready,
            TimeSpan.FromMilliseconds(20), timeProvider: clock);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(1));
        controller.SetState(TrayState.Ready);
        await checkedConnection.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(new long[] { 0 }, frames.ToArray());
    }

    [Fact]
    public async Task Mascot_AnimatesWhilePixelShiftIsDisabledAndKeepsCodexState()
    {
        var clock = new AnimationClock();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new ConcurrentQueue<(TrayState State, long Shift, long Mascot)>();
        var connection = new FakeConnection((_, _) => { });
        connection.OnMascot = (state, shift, mascot) =>
        {
            frames.Enqueue((state, shift, mascot));
            if (mascot == 0) first.TrySetResult();
            if (mascot == 1) next.TrySetResult();
        };
        var connector = new FakeConnector(() => "COM3", () => connection);
        await using var controller = new UsbScreenController(connector, new(AnimationEnabled: false), TrayState.Busy,
            TimeSpan.FromMilliseconds(20), timeProvider: clock);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await next.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { (TrayState.Busy, 0L, 0L), (TrayState.Busy, 0L, 1L) }, frames.ToArray());
    }

    private sealed class AnimationClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }

    [Fact]
    public async Task WeeklyLimit_RedrawsWithoutStateOrAnimationChange()
    {
        var initial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updated = new TaskCompletionSource<WeeklyLimit>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new FakeConnection((_, _) => { });
        connection.OnWeeklyLimit = limit =>
        {
            if (limit is null) initial.TrySetResult();
            else updated.TrySetResult(limit);
        };
        var connector = new FakeConnector(() => "COM3", () => connection);
        await using var controller = new UsbScreenController(connector,
            new(AnimationEnabled: false, MascotEnabled: false), TrayState.Busy, TimeSpan.FromSeconds(10));
        await initial.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var limit = new WeeklyLimit(72, DateTimeOffset.UtcNow.AddDays(1));
        controller.SetWeeklyLimit(limit);
        Assert.Equal(limit, await updated.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    private sealed class FakeConnector(Func<string?> resolve, Func<IUsbScreenConnection> connect) : IUsbScreenConnector
    {
        public int Resolutions;
        public string? ResolvePort(string selection)
        {
            Interlocked.Increment(ref Resolutions);
            return resolve();
        }
        public IUsbScreenConnection Connect(string port) => connect();
    }

    private sealed class FakeConnection(Action<TrayState, CancellationToken> show) : IUsbScreenConnection
    {
        public bool Disposed { get; private set; }
        public bool TurnedOff { get; private set; }
        public Action<TrayState, ScreenOrientation>? OnShow { get; set; }
        public Action<TrayState, long>? OnFrame { get; set; }
        public Action<TrayState, long, long>? OnMascot { get; set; }
        public Action<WeeklyLimit?>? OnWeeklyLimit { get; set; }
        public Action? OnCheck { get; init; }
        public void CheckConnection() => OnCheck?.Invoke();
        public void Show(TrayState state, ScreenOrientation orientation, CancellationToken cancellationToken,
            long animationStep = 0, long mascotFrame = 0, bool mascotEnabled = true, WeeklyLimit? weeklyLimit = null)
        {
            show(state, cancellationToken);
            OnShow?.Invoke(state, orientation);
            OnFrame?.Invoke(state, animationStep);
            OnMascot?.Invoke(state, animationStep, mascotFrame);
            OnWeeklyLimit?.Invoke(weeklyLimit);
        }
        public void TurnOff() => TurnedOff = true;
        public void Dispose() => Disposed = true;
    }
}
