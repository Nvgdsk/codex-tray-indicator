using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CodexTray.Tests;

public sealed class IpcIntegrationTests
{
    [Fact]
    public void Serialize_Event_UsesStableFlatWireShape()
    {
        var hookEvent = new CodexHookEvent(
            CodexHookEventName.UserPromptSubmit,
            "session",
            "turn",
            null,
            new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));

        string json = JsonSerializer.Serialize(IpcMessage.FromEvent(hookEvent), IpcJson.Options);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("event", root.GetProperty("kind").GetString());
        Assert.Equal("UserPromptSubmit", root.GetProperty("event").GetString());
        Assert.Equal("session", root.GetProperty("sessionId").GetString());
        Assert.Equal("turn", root.GetProperty("turnId").GetString());
        Assert.False(root.TryGetProperty("prompt", out _));
    }

    [Fact]
    public void ToCodexHookEvent_RejectsUnsupportedProtocol()
    {
        var message = new IpcMessage(
            2,
            IpcMessageKind.Event,
            CodexHookEventName.SessionEnd,
            "session",
            null,
            null,
            DateTimeOffset.UnixEpoch,
            null);

        Assert.Throws<InvalidDataException>(message.ToCodexHookEvent);
    }

    [Fact]
    public void ToCodexHookEvent_RejectsMissingRequiredFields()
    {
        var message = new IpcMessage(
            1,
            IpcMessageKind.Event,
            CodexHookEventName.UserPromptSubmit,
            "session",
            null,
            null,
            DateTimeOffset.UnixEpoch,
            null);

        Assert.Throws<InvalidDataException>(message.ToCodexHookEvent);
    }

    [Fact]
    public async Task SendAsync_RoundTripsEventAndResponse()
    {
        string pipeName = UniquePipeName();
        using var cancellation = new CancellationTokenSource();
        var received = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new IpcServer(pipeName);
        Task serverTask = server.RunAsync(
            (message, _) =>
            {
                received.TrySetResult(message);
                return Task.FromResult(new IpcResponse(true, TrayState.Busy, null));
            },
            cancellation.Token);
        var client = new IpcClient(pipeName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        IpcMessage request = IpcMessage.FromEvent(Event("session", "turn"));

        IpcResponse? response = await client.SendAsync(request, true, CancellationToken.None);

        Assert.Equal(request, await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(new IpcResponse(true, TrayState.Busy, null), response);
        cancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendAsync_WhenServerAbsent_ReturnsWithinFailOpenBudget()
    {
        var client = new IpcClient(
            UniquePipeName(),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();

        IpcResponse? response = await client.SendAsync(
            IpcMessage.Query(),
            true,
            CancellationToken.None);

        Assert.Null(response);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(750), stopwatch.Elapsed.ToString());
    }

    [Fact]
    public async Task Server_AcceptsTwentyConcurrentClients()
    {
        string pipeName = UniquePipeName();
        using var cancellation = new CancellationTokenSource();
        var server = new IpcServer(pipeName);
        Task serverTask = server.RunAsync(
            (message, _) => Task.FromResult(new IpcResponse(true, message.State, null)),
            cancellation.Token);
        var client = new IpcClient(pipeName, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        Task<IpcResponse?>[] sends = Enumerable.Range(0, 20)
            .Select(_ => client.SendAsync(IpcMessage.ForState(TrayState.Ready), true, CancellationToken.None))
            .ToArray();
        IpcResponse?[] responses = await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.All(responses, response => Assert.Equal(new IpcResponse(true, TrayState.Ready, null), response));
        cancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Server_CanRestartOnSamePipeName()
    {
        string pipeName = UniquePipeName();
        await RunOneServerCycle(pipeName, TrayState.Busy);
        await RunOneServerCycle(pipeName, TrayState.Ready);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Server_ReportsMalformedOrOversizedMessages(bool oversized)
    {
        string pipeName = UniquePipeName();
        using var cancellation = new CancellationTokenSource();
        var protocolError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new IpcServer(pipeName, protocolError: exception => protocolError.TrySetResult(exception));
        Task serverTask = server.RunAsync(
            (_, _) => Task.FromResult(new IpcResponse(true, null, null)),
            cancellation.Token);
        byte[] payload = oversized
            ? Enumerable.Repeat((byte)'x', AppConstants.MaxMessageBytes + 1).Append((byte)'\n').ToArray()
            : "{not-json}\n"u8.ToArray();

        await SendRaw(pipeName, payload);

        Exception error = await protocolError.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.IsAssignableFrom<InvalidDataException>(error);
        cancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Server_StopsPromptlyWhenCancelled()
    {
        string pipeName = UniquePipeName();
        using var cancellation = new CancellationTokenSource();
        var server = new IpcServer(pipeName);
        Task serverTask = server.RunAsync(
            (_, _) => Task.FromResult(new IpcResponse(true, null, null)),
            cancellation.Token);

        cancellation.Cancel();

        await serverTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    private static async Task RunOneServerCycle(string pipeName, TrayState state)
    {
        using var cancellation = new CancellationTokenSource();
        var server = new IpcServer(pipeName);
        Task serverTask = server.RunAsync(
            (_, _) => Task.FromResult(new IpcResponse(true, state, null)),
            cancellation.Token);
        var client = new IpcClient(pipeName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        IpcResponse? response = await client.SendAsync(IpcMessage.Query(), true, CancellationToken.None);

        Assert.Equal(state, response!.State);
        cancellation.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task SendRaw(string pipeName, byte[] payload)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(1_000);
        await client.WriteAsync(payload);
        await client.FlushAsync();
    }

    private static CodexHookEvent Event(string sessionId, string turnId)
    {
        return new CodexHookEvent(
            CodexHookEventName.UserPromptSubmit,
            sessionId,
            turnId,
            null,
            DateTimeOffset.UnixEpoch);
    }

    private static string UniquePipeName() => $"CodexTray.Tests.{Guid.NewGuid():N}";
}
