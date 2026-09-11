namespace CodexTray.Tests;

public sealed class WslDetectorTests
{
    [Fact]
    public void ParseDistributionList_RemovesNulsBlankLinesAndInfrastructureDistros()
    {
        string raw = "U\0b\0u\0n\0t\0u\0\r\0\n\0\r\0\n\0d\0o\0c\0k\0e\0r\0-\0d\0e\0s\0k\0t\0o\0p\0\r\0\n\0Debian\r\n";

        IReadOnlyList<string> result = WslDetector.ParseDistributionList(raw);

        Assert.Equal(["Ubuntu", "Debian"], result);
    }

    [Fact]
    public async Task FindCodexInstallations_ReturnsOnlyDistrosContainingCodex()
    {
        var runner = new ScriptedProcessRunner(
            Success("Ubuntu\r\nDebian\r\ndocker-desktop\r\n"),
            Success("/home/user/.local/bin/codex\ncodex-cli 0.154.0\n"),
            Failure("codex not found"));
        var detector = new WslDetector(runner);

        IReadOnlyList<WslCodexInstallation> result =
            await detector.FindCodexInstallationsAsync(CancellationToken.None);

        Assert.Equal(
            [new WslCodexInstallation("Ubuntu", "/home/user/.local/bin/codex", "codex-cli 0.154.0")],
            result);
        Assert.Equal(["--list", "--quiet"], runner.Requests[0].Arguments);
        Assert.Equal("Ubuntu", runner.Requests[1].Arguments[1]);
        Assert.Equal("Debian", runner.Requests[2].Arguments[1]);
        Assert.DoesNotContain(runner.Requests, request => request.Arguments.Contains("docker-desktop"));
    }

    [Fact]
    public async Task FindCodexInstallations_ReturnsEveryMatchingDistro()
    {
        var runner = new ScriptedProcessRunner(
            Success("Ubuntu\nДистрибутив\n"),
            Success("/usr/bin/codex\ncodex-cli 0.154.0\n"),
            Success("/opt/codex\ncodex-cli 0.153.4\n"));
        var detector = new WslDetector(runner);

        IReadOnlyList<WslCodexInstallation> result =
            await detector.FindCodexInstallationsAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Дистрибутив", result[1].Distribution);
    }

    [Fact]
    public async Task FindCodexInstallations_WithNoMatches_ReturnsEmptyList()
    {
        var runner = new ScriptedProcessRunner(
            Success("Debian\n"),
            Failure("not found"));
        var detector = new WslDetector(runner);

        IReadOnlyList<WslCodexInstallation> result =
            await detector.FindCodexInstallationsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindCodexInstallations_WhenWslListFails_ThrowsActionableError()
    {
        var runner = new ScriptedProcessRunner(Failure("WSL service unavailable"));
        var detector = new WslDetector(runner);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => detector.FindCodexInstallationsAsync(CancellationToken.None));

        Assert.Contains("WSL", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("service unavailable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConvertWindowsPath_PassesDistroAndPathAsSeparateArguments()
    {
        var runner = new ScriptedProcessRunner(
            Success("/mnt/c/Users/test-user/Documents/Codex Tray/CodexTray.exe\n"));
        var detector = new WslDetector(runner);
        const string windowsPath = @"C:\Users\test-user\Documents\Codex Tray\CodexTray.exe";

        string result = await detector.ConvertWindowsPathAsync(
            "Ubuntu 24.04",
            windowsPath,
            CancellationToken.None);

        Assert.Equal("/mnt/c/Users/test-user/Documents/Codex Tray/CodexTray.exe", result);
        Assert.Equal(
            ["-d", "Ubuntu 24.04", "--", "wslpath", "-a", "-u", "--", windowsPath],
            runner.Requests[0].Arguments);
    }

    private static ProcessResult Success(string output) => new(0, output, string.Empty);

    private static ProcessResult Failure(string error) => new(1, string.Empty, error);

    private sealed class ScriptedProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
