using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace CodexTray;

internal sealed class IpcServer
{
    private readonly string _pipeName;
    private readonly int _maximumMessageBytes;
    private readonly TimeSpan _ioTimeout;
    private readonly Action<Exception>? _protocolError;

    public event Action<Exception>? ProtocolError;

    public IpcServer(
        string pipeName = AppConstants.PipeName,
        int maximumMessageBytes = AppConstants.MaxMessageBytes,
        TimeSpan? ioTimeout = null,
        Action<Exception>? protocolError = null)
    {
        _pipeName = pipeName;
        _maximumMessageBytes = maximumMessageBytes;
        _ioTimeout = ioTimeout ?? AppConstants.PipeIoTimeout;
        _protocolError = protocolError;
    }

    public async Task RunAsync(
        Func<IpcMessage, CancellationToken, Task<IpcResponse>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var workers = new List<Task>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                workers.RemoveAll(task => task.IsCompleted);
                workers.Add(HandleConnectionAsync(pipe, handler, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        Func<IpcMessage, CancellationToken, Task<IpcResponse>> handler,
        CancellationToken serverCancellation)
    {
        await using (pipe.ConfigureAwait(false))
        {
            using var ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
            ioCancellation.CancelAfter(_ioTimeout);
            try
            {
                byte[] requestBytes = await IpcJson.ReadFrameAsync(
                    pipe,
                    _maximumMessageBytes,
                    ioCancellation.Token).ConfigureAwait(false);
                IpcMessage? message = JsonSerializer.Deserialize<IpcMessage>(requestBytes, IpcJson.Options);
                if (message is null)
                {
                    throw new InvalidDataException("IPC request deserialized to null.");
                }

                message.Validate();
                IpcResponse response = await handler(message, ioCancellation.Token).ConfigureAwait(false);
                await TryWriteResponse(pipe, response, ioCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (serverCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (
                exception is InvalidDataException or JsonException or IOException or OperationCanceledException)
            {
                var protocolException = exception as InvalidDataException
                    ?? new InvalidDataException("Invalid IPC request.", exception);
                _protocolError?.Invoke(protocolException);
                ProtocolError?.Invoke(protocolException);
                await TryWriteResponse(
                    pipe,
                    new IpcResponse(false, null, protocolException.Message),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task TryWriteResponse(
        Stream pipe,
        IpcResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            await IpcJson.WriteFrameAsync(pipe, response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }
}
