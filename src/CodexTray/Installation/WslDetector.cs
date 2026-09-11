using System.Text;

namespace CodexTray;

internal sealed record WslCodexInstallation(
    string Distribution,
    string CodexPath,
    string Version);

internal interface IWslDetector
{
    Task<IReadOnlyList<WslCodexInstallation>> FindCodexInstallationsAsync(
        CancellationToken cancellationToken);

    Task<string> ConvertWindowsPathAsync(
        string distribution,
        string windowsPath,
        CancellationToken cancellationToken);
}

internal sealed class WslDetector(IProcessRunner processRunner) : IWslDetector
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    public async Task<IReadOnlyList<WslCodexInstallation>> FindCodexInstallationsAsync(
        CancellationToken cancellationToken)
    {
        ProcessResult listResult = await processRunner.RunAsync(
            new ProcessRequest(
                "wsl.exe",
                ["--list", "--quiet"],
                null,
                CommandTimeout,
                Encoding.Unicode),
            cancellationToken).ConfigureAwait(false);
        if (listResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to list WSL distributions: {ErrorText(listResult)}");
        }

        var installations = new List<WslCodexInstallation>();
        foreach (string distribution in ParseDistributionList(listResult.StandardOutput))
        {
            ProcessResult probe = await processRunner.RunAsync(
                new ProcessRequest(
                    "wsl.exe",
                    [
                        "-d",
                        distribution,
                        "--exec",
                        "sh",
                        "-lc",
                        "command -v codex 2>/dev/null && codex --version",
                    ],
                    null,
                    CommandTimeout),
                cancellationToken).ConfigureAwait(false);
            if (probe.ExitCode != 0)
            {
                continue;
            }

            string[] lines = probe.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length >= 2)
            {
                installations.Add(new WslCodexInstallation(distribution, lines[0], lines[1]));
            }
        }

        return installations;
    }

    public async Task<string> ConvertWindowsPathAsync(
        string distribution,
        string windowsPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsPath);

        ProcessResult result = await processRunner.RunAsync(
            new ProcessRequest(
                "wsl.exe",
                ["-d", distribution, "--exec", "wslpath", "-a", "-u", "--", windowsPath],
                null,
                CommandTimeout),
            cancellationToken).ConfigureAwait(false);
        string converted = result.StandardOutput.Trim();
        if (result.ExitCode != 0 || converted.Length == 0)
        {
            throw new InvalidOperationException(
                $"Unable to convert the CodexTray path for {distribution}: {ErrorText(result)}");
        }

        return converted;
    }

    internal static IReadOnlyList<string> ParseDistributionList(string value)
    {
        return value
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !name.Equals("docker-desktop", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ErrorText(ProcessResult result)
    {
        string message = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        return string.IsNullOrWhiteSpace(message) ? $"exit code {result.ExitCode}" : message.Trim();
    }
}
