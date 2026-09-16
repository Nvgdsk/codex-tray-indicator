using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexTray;

internal interface IWeeklyLimitSource
{
    Task<WeeklyLimit?> ReadAsync(CancellationToken cancellationToken);
}

// Uses the selected WSL user's Codex authentication; never reads or copies credentials.
internal sealed class CodexWeeklyLimitSource(Func<string?> distribution, TimeProvider? timeProvider = null) : IWeeklyLimitSource
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private static readonly string InitializeMessage = JsonSerializer.Serialize(new
    {
        id = 0,
        method = "initialize",
        @params = new
        {
            clientInfo = new
            {
                name = "codex_tray",
                title = "Codex Tray Indicator",
                version = typeof(CodexWeeklyLimitSource).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            },
        },
    });

    public async Task<WeeklyLimit?> ReadAsync(CancellationToken cancellationToken)
    {
        string? selected = distribution();
        if (string.IsNullOrWhiteSpace(selected)) return null;
        var start = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (string argument in new[] { "-d", selected, "--exec", "sh", "-lc", "exec codex app-server" })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        if (!process.Start()) throw new IOException("Unable to start Codex app-server.");
        // Drain without retaining diagnostics, which may contain personal configuration.
        Task errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        try
        {
            return await ReadAsync(process.StandardOutput, process.StandardInput, _clock.GetUtcNow(), timeout.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            timeout.Cancel();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            try { await errors.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    internal static async Task<WeeklyLimit?> ReadAsync(TextReader input, TextWriter output,
        DateTimeOffset now, CancellationToken token)
    {
        await SendAsync(output, InitializeMessage, token).ConfigureAwait(false);
        using (await ResponseAsync(input, 0, token).ConfigureAwait(false)) { }
        await SendAsync(output, """{"method":"initialized","params":{}}""", token).ConfigureAwait(false);
        await SendAsync(output, """{"id":1,"method":"account/rateLimits/read","params":{}}""", token).ConfigureAwait(false);
        using JsonDocument response = await ResponseAsync(input, 1, token).ConfigureAwait(false);
        return WeeklyLimit.Parse(response.RootElement.GetProperty("result"), now);
    }

    private static async Task SendAsync(TextWriter output, string message, CancellationToken token)
    {
        await output.WriteLineAsync(message.AsMemory(), token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ResponseAsync(TextReader input, int id, CancellationToken token)
    {
        // Bound both individual lines and notification count, including a server that never replies.
        var character = new char[1];
        for (int messages = 0; messages < 128; messages++)
        {
            var line = new StringBuilder();
            while (true)
            {
                int count = await input.ReadAsync(character.AsMemory(), token).ConfigureAwait(false);
                if (count == 0)
                {
                    if (line.Length == 0) throw new IOException("Codex app-server closed its output.");
                    break;
                }
                if (character[0] == '\n') break;
                if (line.Length >= 65536) throw new InvalidDataException("Codex response is too large.");
                line.Append(character[0]);
            }
            if (string.IsNullOrWhiteSpace(line.ToString())) continue;
            JsonDocument document = JsonDocument.Parse(line.ToString());
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out JsonElement responseId) &&
                responseId.ValueKind == JsonValueKind.Number && responseId.TryGetInt32(out int value) && value == id)
            {
                if (!root.TryGetProperty("error", out _) && root.TryGetProperty("result", out _)) return document;
                document.Dispose();
                throw new InvalidDataException("Codex could not read account limits.");
            }
            document.Dispose();
        }
        throw new InvalidDataException("Codex did not reply to the limits request.");
    }
}
