namespace CodexTray;

internal static class ApplicationHost
{
    private const string TrustInstruction =
        "Start Codex, enter /hooks, review Codex Tray Indicator, and choose Trust.";

    public static async Task<int> RunAsync(
        AppCommand command,
        AppServices services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(services);

        try
        {
            return command.Mode switch
            {
                AppMode.Tray => services.TrayRunner.Run(),
                AppMode.Hook or AppMode.HookTest or AppMode.QueryState or AppMode.Shutdown =>
                    await services.HookRunner.RunAsync(
                        command,
                        services.StandardInput,
                        services.StandardOutput,
                        cancellationToken).ConfigureAwait(false),
                AppMode.Install => await InstallAsync(services, cancellationToken).ConfigureAwait(false),
                AppMode.Uninstall => await UninstallAsync(services, cancellationToken).ConfigureAwait(false),
                _ => 2,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (command.Mode == AppMode.Hook)
        {
            _ = exception;
            return 0;
        }
        catch (Exception exception) when (
            command.Mode is AppMode.HookTest or AppMode.QueryState or AppMode.Shutdown)
        {
            _ = exception;
            return 2;
        }
        catch (Exception exception)
        {
            services.Messages.Show("Codex Tray Indicator", exception.Message, isError: true);
            return 1;
        }
    }

    private static async Task<int> InstallAsync(
        AppServices services,
        CancellationToken cancellationToken)
    {
        IntegrationResult result = await services.Installer.InstallAsync(
            services.ExecutablePath,
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            services.Messages.Show("Codex Tray installation failed", result.Message, isError: true);
            return 1;
        }

        string details = string.Join(
            " / ",
            new[] { result.Distribution, result.CodexVersion }.Where(value => !string.IsNullOrWhiteSpace(value)));
        services.Messages.Show(
            "Codex Tray installed",
            $"{result.Message}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}{TrustInstruction}",
            isError: false);
        return 0;
    }

    private static async Task<int> UninstallAsync(
        AppServices services,
        CancellationToken cancellationToken)
    {
        IntegrationResult result = await services.Installer.UninstallAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            services.Messages.Show("Codex Tray removal failed", result.Message, isError: true);
            return 1;
        }

        try
        {
            _ = await services.HookRunner.RunAsync(
                new AppCommand(AppMode.Shutdown, null, null),
                services.StandardInput,
                services.StandardOutput,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Hook removal already succeeded; an absent or exiting tray is acceptable here.
        }

        services.Messages.Show(
            "Codex Tray removed",
            $"{result.Message}{Environment.NewLine}{Environment.NewLine}Foreign hooks were preserved.",
            isError: false);
        return 0;
    }
}
