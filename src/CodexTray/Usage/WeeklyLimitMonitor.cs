using System.Threading.Channels;

namespace CodexTray;

internal sealed class WeeklyLimitMonitor : IAsyncDisposable
{
    private readonly IWeeklyLimitSource _source;
    private readonly Action<WeeklyLimit?> _updated;
    private readonly Func<bool> _enabled;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte> _refresh = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly Task _worker;

    public WeeklyLimitMonitor(IWeeklyLimitSource source, Action<WeeklyLimit?> updated,
        Func<bool>? enabled = null, TimeSpan? interval = null)
    {
        _source = source;
        _updated = updated;
        _enabled = enabled ?? (() => true);
        _interval = interval ?? TimeSpan.FromMinutes(1);
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _worker = Task.Run(RunAsync);
    }

    public void Refresh() => _refresh.Writer.TryWrite(0);

    public Task StopAsync()
    {
        _stop.Cancel();
        _refresh.Writer.TryComplete();
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
        try
        {
            while (!token.IsCancellationRequested)
            {
                while (_refresh.Reader.TryRead(out _)) { }
                WeeklyLimit? limit = null;
                try
                {
                    if (_enabled()) limit = await _source.ReadAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception) { /* A failed account read must not affect Codex lifecycle state. */ }
                token.ThrowIfCancellationRequested();
                _updated(limit);
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                wait.CancelAfter(_interval);
                try { await _refresh.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
