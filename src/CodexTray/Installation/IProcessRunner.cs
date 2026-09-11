using System.Text;

namespace CodexTray;

internal sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput,
    TimeSpan Timeout,
    Encoding? StandardOutputEncoding = null);

internal sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
