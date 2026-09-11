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
        Assert.Contains("procedure CurUninstallStepChanged", script, StringComparison.Ordinal);
        Assert.Contains("--shutdown", script, StringComparison.Ordinal);
        Assert.True(
            script.Split("StopInstalledTray();", StringSplitOptions.None).Length - 1 >= 2,
            "Both replacement and confirmed uninstall must stop the tray.");
    }

    [Fact]
    public void Installer_AbortsWhenInstallOrUninstallMaintenanceFails()
    {
        string script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "installer",
            "CodexTray.iss"));

        Assert.Contains("ResultCode <> 0", script, StringComparison.Ordinal);
        Assert.True(
            script.Split("RaiseException", StringSplitOptions.None).Length - 1 >= 2,
            "Install and uninstall maintenance failures must abort their operation.");
        Assert.DoesNotContain("[UninstallRun]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Uninstaller_DoesNotRemoveIntegrationBeforeUserConfirms()
    {
        string script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "installer",
            "CodexTray.iss"));

        Assert.Contains("procedure CurUninstallStepChanged", script, StringComparison.Ordinal);
        Assert.Contains("CurUninstallStep <> usUninstall", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function InitializeUninstall", script, StringComparison.Ordinal);
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
