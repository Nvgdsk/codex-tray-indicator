namespace CodexTray;

internal enum AppMode
{
    Tray,
    Hook,
    HookTest,
    QueryState,
    Install,
    Uninstall,
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
            "--install" => ParseSingle(args, AppMode.Install),
            "--uninstall" => ParseSingle(args, AppMode.Uninstall),
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
}
