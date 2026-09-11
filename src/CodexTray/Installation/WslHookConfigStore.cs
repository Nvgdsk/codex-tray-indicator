namespace CodexTray;

internal interface IWslHookConfigStore
{
    Task<string> ReadAsync(string distribution, CancellationToken cancellationToken);

    Task WriteAtomicAsync(
        string distribution,
        string json,
        bool createBackup,
        CancellationToken cancellationToken);
}

internal sealed class WslHookConfigStore(IProcessRunner processRunner) : IWslHookConfigStore
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    private const string ReadScript =
        "if [ -f \"$HOME/.codex/hooks.json\" ]; then cat \"$HOME/.codex/hooks.json\"; else exit 3; fi";

    private const string WriteScript = """
        set -eu
        umask 077
        create_backup="$1"
        dir="${2:-$HOME/.codex}"
        target="$dir/hooks.json"
        mkdir -p "$dir"
        tmp=$(mktemp "$dir/hooks.json.codextray.XXXXXX")
        trap 'rm -f "$tmp"' EXIT HUP INT TERM
        cat > "$tmp"
        if [ "$create_backup" = "1" ] && [ -f "$target" ] && [ ! -f "$dir/hooks.json.codextray.bak" ]; then cp -p "$target" "$dir/hooks.json.codextray.bak"; fi
        chmod 600 "$tmp"
        mv -f "$tmp" "$target"
        trap - EXIT HUP INT TERM
        """;

    public async Task<string> ReadAsync(string distribution, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);
        ProcessResult result = await processRunner.RunAsync(
            new ProcessRequest(
                "wsl.exe",
                ["-d", distribution, "--exec", "sh", "-lc", ReadScript],
                null,
                CommandTimeout),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode == 3)
        {
            return "{}";
        }

        if (result.ExitCode != 0)
        {
            throw new IOException(
                $"Unable to read ~/.codex/hooks.json in {distribution}: {ErrorText(result)}");
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput) ? "{}" : result.StandardOutput;
    }

    public async Task WriteAtomicAsync(
        string distribution,
        string json,
        bool createBackup,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);
        ArgumentNullException.ThrowIfNull(json);

        ProcessResult result = await processRunner.RunAsync(
            new ProcessRequest(
                "wsl.exe",
                [
                    "-d",
                    distribution,
                    "--exec",
                    "sh",
                    "-lc",
                    WriteScript,
                    "codex-tray",
                    createBackup ? "1" : "0",
                ],
                json,
                CommandTimeout),
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new IOException(
                $"Unable to replace ~/.codex/hooks.json in {distribution}: {ErrorText(result)}");
        }
    }

    private static string ErrorText(ProcessResult result)
    {
        string message = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        return string.IsNullOrWhiteSpace(message) ? $"exit code {result.ExitCode}" : message.Trim();
    }
}
