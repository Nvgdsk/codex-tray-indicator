namespace CodexTray;

internal sealed class HookCommandRunner
{
    private readonly IIpcClient _client;
    private readonly TimeProvider _clock;

    public HookCommandRunner(IIpcClient client, TimeProvider clock)
    {
        _client = client;
        _clock = clock;
    }

    public async Task<int> RunAsync(
        AppCommand command,
        Stream stdin,
        TextWriter stdout,
        CancellationToken cancellationToken)
    {
        return command.Mode switch
        {
            AppMode.Hook => await RunHook(command, stdin, cancellationToken).ConfigureAwait(false),
            AppMode.HookTest => await RunHookTest(command, cancellationToken).ConfigureAwait(false),
            AppMode.QueryState => await RunQuery(stdout, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("The command is not an IPC client mode.", nameof(command)),
        };
    }

    private async Task<int> RunHook(
        AppCommand command,
        Stream stdin,
        CancellationToken cancellationToken)
    {
        try
        {
            if (command.IntegrationId is not null &&
                !string.Equals(command.IntegrationId, AppConstants.IntegrationId, StringComparison.Ordinal))
            {
                return 0;
            }

            byte[]? payload = await ReadBoundedAsync(stdin, cancellationToken).ConfigureAwait(false);
            if (payload is null ||
                !HookPayloadParser.TryParse(payload, _clock, out CodexHookEvent? hookEvent))
            {
                return 0;
            }

            await _client.SendAsync(
                IpcMessage.FromEvent(hookEvent!),
                expectResponse: false,
                cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> RunHookTest(
        AppCommand command,
        CancellationToken cancellationToken)
    {
        TrayState state = command.Value switch
        {
            "busy" => TrayState.Busy,
            "ready" => TrayState.Ready,
            "inactive" => TrayState.Inactive,
            "error" => TrayState.Error,
            _ => throw new ArgumentException("Unsupported hook test state.", nameof(command)),
        };

        IpcResponse? response = await _client.SendAsync(
            IpcMessage.ForState(state),
            expectResponse: true,
            cancellationToken).ConfigureAwait(false);
        return response?.Ok == true ? 0 : 2;
    }

    private async Task<int> RunQuery(TextWriter stdout, CancellationToken cancellationToken)
    {
        IpcResponse? response = await _client.SendAsync(
            IpcMessage.Query(),
            expectResponse: true,
            cancellationToken).ConfigureAwait(false);
        if (response?.Ok != true || response.State is null)
        {
            return 2;
        }

        await stdout.WriteLineAsync(response.State.Value.ToString()).ConfigureAwait(false);
        return 0;
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        var payload = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return payload.Length == 0 ? null : payload.ToArray();
            }

            if (payload.Length + read > AppConstants.MaxMessageBytes)
            {
                return null;
            }

            payload.Write(buffer, 0, read);
        }
    }
}
