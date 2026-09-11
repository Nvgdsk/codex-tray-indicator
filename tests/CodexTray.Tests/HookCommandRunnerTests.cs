using System.Text;
using CodexTray.Tests.TestDoubles;

namespace CodexTray.Tests;

public sealed class HookCommandRunnerTests
{
    [Fact]
    public async Task Hook_WithValidPayload_SendsSanitizedEvent()
    {
        var client = new RecordingIpcClient();
        var runner = Runner(client);
        using var output = new StringWriter();

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.Hook, null, AppConstants.IntegrationId),
            Utf8("""{"hook_event_name":"UserPromptSubmit","session_id":"s","turn_id":"t","prompt":"secret"}"""),
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        IpcMessage message = Assert.Single(client.Messages);
        Assert.Equal(CodexHookEventName.UserPromptSubmit, message.Event);
        Assert.Equal("s", message.SessionId);
        Assert.Equal("t", message.TurnId);
        Assert.DoesNotContain("secret", message.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task Hook_WithoutIntegrationMarker_RemainsCompatible()
    {
        var client = new RecordingIpcClient();
        var runner = Runner(client);

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.Hook, null, null),
            Utf8("""{"hook_event_name":"SessionStart","session_id":"s","source":"startup"}"""),
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Single(client.Messages);
    }

    [Fact]
    public async Task Hook_WithWrongIntegrationMarker_FailsOpenWithoutSending()
    {
        var client = new RecordingIpcClient();
        var runner = Runner(client);

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.Hook, null, "different-integration"),
            Utf8("""{"hook_event_name":"SessionEnd","session_id":"s"}"""),
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(client.Messages);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"hook_event_name\":\"Other\",\"session_id\":\"s\"}")]
    public async Task Hook_WithInvalidInput_FailsOpenWithoutSending(string json)
    {
        var client = new RecordingIpcClient();
        var runner = Runner(client);

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.Hook, null, null),
            Utf8(json),
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(client.Messages);
    }

    [Fact]
    public async Task Hook_WithOversizedInput_FailsOpenWithoutSending()
    {
        var client = new RecordingIpcClient();
        var runner = Runner(client);
        using var input = new MemoryStream(new byte[AppConstants.MaxMessageBytes + 1]);

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.Hook, null, null),
            input,
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(client.Messages);
    }

    [Fact]
    public async Task Hook_WhenPipeUnavailable_StillReturnsSuccess()
    {
        var client = new RecordingIpcClient { Response = null };
        var runner = Runner(client);

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.Hook, null, null),
            Utf8("""{"hook_event_name":"SessionEnd","session_id":"s"}"""),
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData("busy", (int)TrayState.Busy)]
    [InlineData("ready", (int)TrayState.Ready)]
    [InlineData("inactive", (int)TrayState.Inactive)]
    [InlineData("error", (int)TrayState.Error)]
    public async Task HookTest_MapsRequestedState(string value, int expectedState)
    {
        var client = new RecordingIpcClient
        {
            Response = new IpcResponse(true, (TrayState)expectedState, null),
        };
        var runner = Runner(client);

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.HookTest, value, null),
            Stream.Null,
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal((TrayState)expectedState, Assert.Single(client.Messages).State);
    }

    [Fact]
    public async Task QueryState_PrintsOneStateName()
    {
        var client = new RecordingIpcClient
        {
            Response = new IpcResponse(true, TrayState.Ready, null),
        };
        var runner = Runner(client);
        using var output = new StringWriter();

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.QueryState, null, null),
            Stream.Null,
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal($"Ready{Environment.NewLine}", output.ToString());
        Assert.True(client.ExpectResponse);
    }

    [Fact]
    public async Task QueryState_WhenPipeUnavailable_ReturnsDiagnosticFailure()
    {
        var client = new RecordingIpcClient { Response = null };
        var runner = Runner(client);

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.QueryState, null, null),
            Stream.Null,
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Shutdown_SendsShutdownRequestAndRequiresAcknowledgement()
    {
        var client = new RecordingIpcClient
        {
            Response = new IpcResponse(true, TrayState.Inactive, null),
        };
        var runner = Runner(client);

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.Shutdown, null, null),
            Stream.Null,
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(IpcMessageKind.Shutdown, Assert.Single(client.Messages).Kind);
        Assert.True(client.ExpectResponse);
    }

    [Fact]
    public async Task Hook_WhenClientThrows_FailsOpen()
    {
        var client = new RecordingIpcClient { Exception = new IOException("pipe failed") };
        var runner = Runner(client);

        int exitCode = await runner.RunAsync(
            new AppCommand(AppMode.Hook, null, null),
            Utf8("""{"hook_event_name":"SessionEnd","session_id":"s"}"""),
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    private static HookCommandRunner Runner(IIpcClient client)
    {
        return new HookCommandRunner(
            client,
            new ManualTimeProvider(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero)));
    }

    private static MemoryStream Utf8(string value) => new(Encoding.UTF8.GetBytes(value));

    private sealed class RecordingIpcClient : IIpcClient
    {
        public List<IpcMessage> Messages { get; } = [];

        public IpcResponse? Response { get; set; } = new(true, null, null);

        public Exception? Exception { get; set; }

        public bool ExpectResponse { get; private set; }

        public Task<IpcResponse?> SendAsync(
            IpcMessage message,
            bool expectResponse,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            ExpectResponse = expectResponse;
            return Exception is null
                ? Task.FromResult(Response)
                : Task.FromException<IpcResponse?>(Exception);
        }
    }
}
