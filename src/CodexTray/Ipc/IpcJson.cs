using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexTray;

internal static class IpcJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var payload = new MemoryStream();
        byte[] buffer = new byte[4096];

        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            int newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            int count = newline >= 0 ? newline : read;
            if (payload.Length + count > maximumBytes)
            {
                throw new InvalidDataException($"IPC message exceeds {maximumBytes} bytes.");
            }

            payload.Write(buffer, 0, count);
            if (newline >= 0)
            {
                break;
            }
        }

        if (payload.Length == 0)
        {
            throw new InvalidDataException("IPC message is empty.");
        }

        return payload.ToArray();
    }

    public static async Task WriteFrameAsync<T>(
        Stream stream,
        T value,
        CancellationToken cancellationToken)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(new JsonStringEnumConverter<IpcMessageKind>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<CodexHookEventName>());
        options.Converters.Add(new JsonStringEnumConverter<TrayState>());
        return options;
    }
}
