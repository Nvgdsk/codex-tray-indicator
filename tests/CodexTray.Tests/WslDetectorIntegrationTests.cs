namespace CodexTray.Tests;

public sealed class WslDetectorIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task FindCodexInstallations_FindsCodexInConfiguredWslDistributions()
    {
        var detector = new WslDetector(new ProcessRunner());

        IReadOnlyList<WslCodexInstallation> installations =
            await detector.FindCodexInstallationsAsync(CancellationToken.None);

        Assert.NotEmpty(installations);
        Assert.All(installations, installation =>
        {
            Assert.False(string.IsNullOrWhiteSpace(installation.Distribution));
            Assert.StartsWith("/", installation.CodexPath);
            Assert.Matches(@"^codex-cli \d+\.\d+\.\d+(?:[-+].+)?$", installation.Version);
        });
    }
}
