namespace CodexTray;

internal enum AppMode
{
    Tray,
    Hook,
    HookTest,
    QueryState,
    Install,
    Uninstall,
    Shutdown,
}

internal sealed record AppCommand(AppMode Mode, string? Value, string? IntegrationId);

internal static class CommandLine
{
    private static readonly HashSet<string> HookTestStates =
        new(StringComparer.OrdinalIgnoreCase) { "busy", "ready", "inactive", "error" };

    public static AppCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new AppCommand(AppMode.Tray, null, null);
        }

        return args[0] switch
        {
            "--hook" => ParseHook(args),
            "--hook-test" => ParseHookTest(args),
            "--query-state" => ParseSingle(args, AppMode.QueryState),
            "--install" => ParseMaintenance(args, AppMode.Install),
            "--uninstall" => ParseMaintenance(args, AppMode.Uninstall),
            "--shutdown" => ParseSingle(args, AppMode.Shutdown),
            _ => throw new ArgumentException($"Unknown argument: {args[0]}", nameof(args)),
        };
    }

    private static AppCommand ParseHook(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            return new AppCommand(AppMode.Hook, null, null);
        }

        if (args.Count == 3 &&
            args[1] == "--integration-id" &&
            !string.IsNullOrWhiteSpace(args[2]))
        {
            return new AppCommand(AppMode.Hook, null, args[2]);
        }

        throw new ArgumentException("Invalid --hook arguments.", nameof(args));
    }

    private static AppCommand ParseHookTest(IReadOnlyList<string> args)
    {
        if (args.Count != 2 || !HookTestStates.Contains(args[1]))
        {
            throw new ArgumentException("--hook-test requires busy, ready, inactive, or error.", nameof(args));
        }

        return new AppCommand(AppMode.HookTest, args[1].ToLowerInvariant(), null);
    }

    private static AppCommand ParseSingle(IReadOnlyList<string> args, AppMode mode)
    {
        if (args.Count != 1)
        {
            throw new ArgumentException($"{args[0]} does not accept additional arguments.", nameof(args));
        }

        return new AppCommand(mode, null, null);
    }

    private static AppCommand ParseMaintenance(IReadOnlyList<string> args, AppMode mode)
    {
        return args.Count switch
        {
            1 => new AppCommand(mode, null, null),
            2 when args[1] == "--quiet" => new AppCommand(mode, "quiet", null),
            _ => throw new ArgumentException($"{args[0]} accepts only the optional --quiet argument.", nameof(args)),
        };
    }
}
