using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodexTray.Tests;

public sealed class ReleaseContractTests
{
    [Fact]
    public void InstallerVersionComesFromTheProjectBuildDefine()
    {
        string repositoryRoot = FindRepositoryRoot();
        string projectVersion = ReadProjectVersion(repositoryRoot);
        string installer = File.ReadAllText(Path.Combine(repositoryRoot, "installer", "CodexTray.iss"));

        Assert.Matches(new Regex(@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z"), projectVersion);
        Assert.DoesNotContain($"AppVersion={projectVersion}", installer, StringComparison.Ordinal);
        Assert.Contains("#ifndef MyAppVersion", installer, StringComparison.Ordinal);
        Assert.Contains("#error", installer, StringComparison.Ordinal);
        Assert.Contains("AppVersion={#MyAppVersion}", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseBuildRequiresExplicitUnsignedAcknowledgementAndPassesProjectVersion()
    {
        string script = ReadReleaseScript();

        Assert.Contains("[switch]$AllowUnsigned", script, StringComparison.Ordinal);
        Assert.Contains("[xml]", script, StringComparison.Ordinal);
        Assert.Contains("Project.PropertyGroup.Version", script, StringComparison.Ordinal);
        Assert.Contains("/DMyAppVersion=$projectVersion", script, StringComparison.Ordinal);
        Assert.Contains("10.0.401", script, StringComparison.Ordinal);
        Assert.Contains("7.1.0", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseBuildValidatesExactAssetsSignaturesAndChecksums()
    {
        string script = ReadReleaseScript();

        Assert.Contains("CodexTray.exe", script, StringComparison.Ordinal);
        Assert.Contains("CodexTraySetup.exe", script, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", script, StringComparison.Ordinal);
        Assert.Contains("Compare-Object", script, StringComparison.Ordinal);
        Assert.Contains("Get-AuthenticodeSignature", script, StringComparison.Ordinal);
        Assert.Contains("-not $AllowUnsigned", script, StringComparison.Ordinal);
        Assert.Contains("Get-Content -LiteralPath $checksumPath", script, StringComparison.Ordinal);
        Assert.True(
            script.Split("Get-FileHash", StringSplitOptions.None).Length - 1 >= 2,
            "The release build must calculate hashes when writing and independently verifying the checksum manifest.");
    }

    [Fact]
    public void ReleaseBuildStopsThePreviousDistApplicationBeforeReplacingIt()
    {
        string script = ReadReleaseScript();

        Assert.Contains("$previousApplicationPath", script, StringComparison.Ordinal);
        Assert.Contains("'--shutdown'", script, StringComparison.Ordinal);
        Assert.Contains("Wait-Process", script, StringComparison.Ordinal);
        Assert.Contains("Refusing to replace a running", script, StringComparison.Ordinal);
    }

    private static string ReadProjectVersion(string repositoryRoot)
    {
        XDocument project = XDocument.Load(Path.Combine(repositoryRoot, "src", "CodexTray", "CodexTray.csproj"));
        XElement[] versionElements = project.Descendants("Version").ToArray();

        return Assert.Single(versionElements).Value;
    }

    private static string ReadReleaseScript()
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "build-release.ps1"));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodexTray.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Unable to locate the repository root.");
    }
}
