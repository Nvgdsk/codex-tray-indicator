namespace CodexTray.Tests;

public sealed class InstallerDefinitionTests
{
    [Fact]
    public void Installer_ShutsDownWindowlessTrayBeforeReplaceAndRemoval()
    {
        string script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "installer",
            "CodexTray.iss"));

        Assert.Contains("function PrepareToInstall", script, StringComparison.Ordinal);
        Assert.Contains("function InitializeUninstall", script, StringComparison.Ordinal);
        Assert.Contains("--shutdown", script, StringComparison.Ordinal);
        Assert.True(
            script.Split("StopInstalledTray();", StringSplitOptions.None).Length - 1 >= 2,
            "Both install and uninstall preparation must stop the tray.");
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
