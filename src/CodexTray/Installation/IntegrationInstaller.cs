namespace CodexTray;

internal sealed record IntegrationResult(
    bool Success,
    string Message,
    string? Distribution,
    string? CodexVersion);

internal sealed class IntegrationInstaller(
    IWslDetector detector,
    IWslHookConfigStore configStore,
    IUserSettings settings)
{
    public async Task<IntegrationResult> InstallAsync(
        string exePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);

        try
        {
            IReadOnlyList<WslCodexInstallation> installations =
                await detector.FindCodexInstallationsAsync(cancellationToken).ConfigureAwait(false);
            Selection selection = SelectInstallation(installations, settings.WslDistribution);
            if (selection.Installation is null)
            {
                return Failure(selection.Error!);
            }

            WslCodexInstallation installation = selection.Installation;
            string wslExePath = await detector.ConvertWindowsPathAsync(
                installation.Distribution,
                exePath,
                cancellationToken).ConfigureAwait(false);
            string existing = await configStore.ReadAsync(
                installation.Distribution,
                cancellationToken).ConfigureAwait(false);
            string merged = HookConfigMerger.Install(existing, wslExePath);

            await configStore.WriteAtomicAsync(
                installation.Distribution,
                merged,
                createBackup: true,
                cancellationToken).ConfigureAwait(false);

            settings.SetStartup(exePath);
            settings.WslDistribution = installation.Distribution;
            return new IntegrationResult(
                true,
                $"Codex Tray hooks installed in {installation.Distribution}.",
                installation.Distribution,
                installation.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(exception.Message);
        }
    }

    public async Task<IntegrationResult> UninstallAsync(CancellationToken cancellationToken)
    {
        try
        {
            WslCodexInstallation? installation;
            string? recordedDistribution = settings.WslDistribution;
            if (!string.IsNullOrWhiteSpace(recordedDistribution))
            {
                installation = new WslCodexInstallation(recordedDistribution, string.Empty, string.Empty);
            }
            else
            {
                IReadOnlyList<WslCodexInstallation> installations =
                    await detector.FindCodexInstallationsAsync(cancellationToken).ConfigureAwait(false);
                Selection selection = SelectInstallation(installations, selectedDistribution: null);
                if (selection.Installation is null)
                {
                    return Failure(selection.Error!);
                }

                installation = selection.Installation;
            }

            string existing = await configStore.ReadAsync(
                installation.Distribution,
                cancellationToken).ConfigureAwait(false);
            string merged = HookConfigMerger.Uninstall(existing);
            await configStore.WriteAtomicAsync(
                installation.Distribution,
                merged,
                createBackup: false,
                cancellationToken).ConfigureAwait(false);

            settings.RemoveStartup();
            settings.ClearApplicationSettings();
            return new IntegrationResult(
                true,
                $"Codex Tray hooks removed from {installation.Distribution}.",
                installation.Distribution,
                string.IsNullOrWhiteSpace(installation.Version) ? null : installation.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(exception.Message);
        }
    }

    private static Selection SelectInstallation(
        IReadOnlyList<WslCodexInstallation> installations,
        string? selectedDistribution)
    {
        if (!string.IsNullOrWhiteSpace(selectedDistribution))
        {
            WslCodexInstallation? selected = installations.FirstOrDefault(
                item => item.Distribution.Equals(selectedDistribution, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return new Selection(selected, null);
            }
        }

        return installations.Count switch
        {
            0 => new Selection(null, "Codex CLI was not found in any WSL distribution."),
            1 => new Selection(installations[0], null),
            _ => new Selection(
                null,
                "Codex CLI was found in multiple WSL distributions: " +
                string.Join(", ", installations.Select(item => item.Distribution)) +
                ". Select one by installing once with a saved WSL distribution."),
        };
    }

    private static IntegrationResult Failure(string message) =>
        new(false, message, null, null);

    private sealed record Selection(WslCodexInstallation? Installation, string? Error);
}
