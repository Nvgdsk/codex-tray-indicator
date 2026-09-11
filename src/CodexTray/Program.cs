namespace CodexTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppCommand command;
        try
        {
            command = CommandLine.Parse(args);
        }
        catch (ArgumentException exception)
        {
            if (args.Length > 0 && args[0] == "--hook")
            {
                return 0;
            }

            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        try
        {
            AppServices services = AppServices.CreateProduction();
            return ApplicationHost.RunAsync(command, services, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch when (command.Mode == AppMode.Hook)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return command.Mode is AppMode.HookTest or AppMode.QueryState or AppMode.Shutdown ? 2 : 1;
        }
    }
}
