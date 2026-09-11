using System.Text.RegularExpressions;

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

internal sealed partial class WslHookConfigStore : IWslHookConfigStore
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    private const string ReadScript = """
        dir="${1:-$HOME/.codex}"
        if [ -f "$dir/hooks.json" ]; then cat "$dir/hooks.json"; else exit 3; fi
        """;

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

    private readonly IProcessRunner _processRunner;
    private readonly string? _configDirectory;

    public WslHookConfigStore(IProcessRunner processRunner)
        : this(processRunner, configDirectory: null)
    {
    }

    internal WslHookConfigStore(IProcessRunner processRunner, string? configDirectory)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        if (configDirectory is not null && !SafeTestDirectory().IsMatch(configDirectory))
        {
            throw new ArgumentException(
                "A test config directory must match /tmp/codex-tray-tests-<32 lowercase hex>.",
                nameof(configDirectory));
        }

        _configDirectory = configDirectory;
    }

    public async Task<string> ReadAsync(string distribution, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);
        var arguments = new List<string>
        {
            "-d", distribution, "--exec", "sh", "-lc", ReadScript, "codex-tray",
        };
        if (_configDirectory is not null)
        {
            arguments.Add(_configDirectory);
        }

        ProcessResult result = await _processRunner.RunAsync(
            new ProcessRequest(
                "wsl.exe",
                arguments,
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

        var arguments = new List<string>
        {
            "-d",
            distribution,
            "--exec",
            "sh",
            "-lc",
            WriteScript,
            "codex-tray",
            createBackup ? "1" : "0",
        };
        if (_configDirectory is not null)
        {
            arguments.Add(_configDirectory);
        }

        ProcessResult result = await _processRunner.RunAsync(
            new ProcessRequest(
                "wsl.exe",
                arguments,
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

    [GeneratedRegex(@"^/tmp/codex-tray-tests-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTestDirectory();
}
