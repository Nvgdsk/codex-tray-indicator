using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace CodexTray;

internal interface IIpcClient
{
    Task<IpcResponse?> SendAsync(
        IpcMessage message,
        bool expectResponse,
        CancellationToken cancellationToken);
}

internal sealed class IpcClient : IIpcClient
{
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _ioTimeout;

    public IpcClient(
        string pipeName = AppConstants.PipeName,
        TimeSpan? connectTimeout = null,
        TimeSpan? ioTimeout = null)
    {
        _pipeName = pipeName;
        _connectTimeout = connectTimeout ?? AppConstants.PipeConnectTimeout;
        _ioTimeout = ioTimeout ?? AppConstants.PipeIoTimeout;
    }

    public async Task<IpcResponse?> SendAsync(
        IpcMessage message,
        bool expectResponse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();

        await using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            // Enforce the deadline in the pipe connection loop, not through a
            // timer callback that can be delayed by thread-pool starvation.
            await pipe.ConnectAsync(_connectTimeout, cancellationToken).ConfigureAwait(false);

            using var ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ioCancellation.CancelAfter(_ioTimeout);
            await IpcJson.WriteFrameAsync(pipe, message, ioCancellation.Token).ConfigureAwait(false);
            if (!expectResponse)
            {
                return null;
            }

            byte[] responseBytes = await IpcJson.ReadFrameAsync(
                pipe,
                AppConstants.MaxMessageBytes,
                ioCancellation.Token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<IpcResponse>(responseBytes, IpcJson.Options);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
