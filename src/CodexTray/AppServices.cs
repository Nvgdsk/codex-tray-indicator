using System.Windows.Forms;

namespace CodexTray;

internal interface ITrayApplicationRunner
{
    int Run();
}

internal interface IUserMessageSink
{
    void Show(string title, string text, bool isError);
}

internal sealed record AppServices(
    IHookCommandRunner HookRunner,
    IIntegrationInstaller Installer,
    ITrayApplicationRunner TrayRunner,
    IUserMessageSink Messages,
    Stream StandardInput,
    TextWriter StandardOutput,
    string ExecutablePath)
{
    public static AppServices CreateProduction()
    {
        var processRunner = new ProcessRunner();
        var detector = new WslDetector(processRunner);
        var configStore = new WslHookConfigStore(processRunner);
        var settings = new RegistryUserSettings();
        var client = new IpcClient();
        var hookRunner = new HookCommandRunner(client, TimeProvider.System);
        var installer = new IntegrationInstaller(detector, configStore, settings);
        var trayRunner = new WinFormsTrayApplicationRunner(
            mutex => new TrayApplicationContext(
                new SessionStateStore(),
                new IpcServer(),
                settings,
                mutex));

        return new AppServices(
            hookRunner,
            installer,
            trayRunner,
            new WinFormsUserMessageSink(),
            Console.OpenStandardInput(),
            Console.Out,
            Environment.ProcessPath
                ?? throw new InvalidOperationException("Unable to determine the executable path."));
    }
}

internal sealed class WinFormsTrayApplicationRunner(
    Func<Mutex, ApplicationContext> contextFactory) : ITrayApplicationRunner
{
    public int Run()
    {
        var mutex = new Mutex(
            initiallyOwned: true,
            @"Local\" + AppConstants.PipeName,
            out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return 0;
        }

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(contextFactory(mutex));
            return 0;
        }
        catch
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            mutex.Dispose();
            throw;
        }
    }
}

internal sealed class WinFormsUserMessageSink : IUserMessageSink
{
    public void Show(string title, string text, bool isError)
    {
        MessageBox.Show(
            text,
            title,
            MessageBoxButtons.OK,
            isError ? MessageBoxIcon.Error : MessageBoxIcon.Information);
    }
}
