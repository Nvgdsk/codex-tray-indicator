namespace CodexTray.Tests;

public sealed class ProgramModeTests
{
    private const string ExePath = @"C:\Program Files\Codex Tray\CodexTray.exe";

    [Fact]
    public async Task Hook_WhenRunnerThrows_RemainsFailOpen()
    {
        TestServices fixture = TestServices.Create();
        fixture.HookRunner.Exception = new IOException("pipe failure");

        int result = await ApplicationHost.RunAsync(
            new AppCommand(AppMode.Hook, null, null),
            fixture.Services,
            CancellationToken.None);

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData((int)AppMode.HookTest)]
    [InlineData((int)AppMode.QueryState)]
    [InlineData((int)AppMode.Shutdown)]
    public async Task DiagnosticClientMode_WhenRunnerThrows_ReturnsTwo(int modeValue)
    {
        TestServices fixture = TestServices.Create();
        fixture.HookRunner.Exception = new IOException("pipe failure");

        int result = await ApplicationHost.RunAsync(
            new AppCommand((AppMode)modeValue, modeValue == (int)AppMode.HookTest ? "ready" : null, null),
            fixture.Services,
            CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task Query_ReturnsRunnerExitCodeAndUsesInjectedStreams()
    {
        TestServices fixture = TestServices.Create();
        fixture.HookRunner.ExitCode = 2;

        int result = await ApplicationHost.RunAsync(
            new AppCommand(AppMode.QueryState, null, null),
            fixture.Services,
            CancellationToken.None);

        Assert.Equal(2, result);
        Assert.Same(fixture.Input, fixture.HookRunner.Input);
        Assert.Same(fixture.Output, fixture.HookRunner.Output);
    }

    [Fact]
    public async Task Install_SuccessReturnsZeroAndExplainsHookTrust()
    {
        TestServices fixture = TestServices.Create();
        fixture.Installer.InstallResult = new IntegrationResult(
            true,
            "installed",
            "Ubuntu",
            "codex-cli 0.154.0");

        int result = await ApplicationHost.RunAsync(
            new AppCommand(AppMode.Install, null, null),
            fixture.Services,
            CancellationToken.None);

        Assert.Equal(0, result);
        Assert.Equal(ExePath, fixture.Installer.InstalledPath);
        UserMessage message = Assert.Single(fixture.Messages.Messages);
        Assert.False(message.IsError);
        Assert.Contains("Ubuntu", message.Text, StringComparison.Ordinal);
        Assert.Contains("codex-cli 0.154.0", message.Text, StringComparison.Ordinal);
        Assert.Contains(
            "Start Codex, enter /hooks, review Codex Tray Indicator, and choose Trust.",
            message.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_FailureReturnsNonzeroAndShowsError()
    {
        TestServices fixture = TestServices.Create();
        fixture.Installer.InstallResult = new IntegrationResult(false, "no Codex", null, null);

        int result = await ApplicationHost.RunAsync(
            new AppCommand(AppMode.Install, null, null),
            fixture.Services,
            CancellationToken.None);

        Assert.NotEqual(0, result);
        UserMessage message = Assert.Single(fixture.Messages.Messages);
        Assert.True(message.IsError);
        Assert.Contains("no Codex", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uninstall_SuccessReturnsZeroAndReportsForeignHooksPreserved()
    {
        TestServices fixture = TestServices.Create();
        fixture.Installer.UninstallResult = new IntegrationResult(true, "removed", "Ubuntu", null);

        int result = await ApplicationHost.RunAsync(
            new AppCommand(AppMode.Uninstall, null, null),
            fixture.Services,
            CancellationToken.None);

        Assert.Equal(0, result);
        UserMessage message = Assert.Single(fixture.Messages.Messages);
        Assert.False(message.IsError);
        Assert.Contains("foreign hooks were preserved", message.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tray_ReturnsRunnerCodeWithoutUsingMaintenanceOrHookServices()
    {
        TestServices fixture = TestServices.Create();
        fixture.TrayRunner.ExitCode = 0;
        fixture.HookRunner.Exception = new InvalidOperationException("must not run");
        fixture.Installer.Exception = new InvalidOperationException("must not run");

        int result = await ApplicationHost.RunAsync(
            new AppCommand(AppMode.Tray, null, null),
            fixture.Services,
            CancellationToken.None);

        Assert.Equal(0, result);
        Assert.Equal(1, fixture.TrayRunner.RunCalls);
        Assert.Empty(fixture.Messages.Messages);
    }

    private sealed class TestServices
    {
        private TestServices()
        {
            HookRunner = new FakeHookRunner();
            Installer = new FakeInstaller();
            TrayRunner = new FakeTrayRunner();
            Messages = new RecordingMessages();
            Input = new MemoryStream();
            Output = new StringWriter();
            Services = new AppServices(
                HookRunner,
                Installer,
                TrayRunner,
                Messages,
                Input,
                Output,
                ExePath);
        }

        public FakeHookRunner HookRunner { get; }
        public FakeInstaller Installer { get; }
        public FakeTrayRunner TrayRunner { get; }
        public RecordingMessages Messages { get; }
        public Stream Input { get; }
        public TextWriter Output { get; }
        public AppServices Services { get; }

        public static TestServices Create() => new();
    }

    private sealed class FakeHookRunner : IHookCommandRunner
    {
        public int ExitCode { get; set; }
        public Exception? Exception { get; set; }
        public Stream? Input { get; private set; }
        public TextWriter? Output { get; private set; }

        public Task<int> RunAsync(
            AppCommand command,
            Stream stdin,
            TextWriter stdout,
            CancellationToken cancellationToken)
        {
            Input = stdin;
            Output = stdout;
            return Exception is null ? Task.FromResult(ExitCode) : Task.FromException<int>(Exception);
        }
    }

    private sealed class FakeInstaller : IIntegrationInstaller
    {
        public IntegrationResult InstallResult { get; set; } = new(true, "ok", "Ubuntu", "version");
        public IntegrationResult UninstallResult { get; set; } = new(true, "ok", "Ubuntu", null);
        public Exception? Exception { get; set; }
        public string? InstalledPath { get; private set; }

        public Task<IntegrationResult> InstallAsync(string exePath, CancellationToken cancellationToken)
        {
            InstalledPath = exePath;
            return Result(InstallResult);
        }

        public Task<IntegrationResult> UninstallAsync(CancellationToken cancellationToken) =>
            Result(UninstallResult);

        private Task<IntegrationResult> Result(IntegrationResult result) => Exception is null
            ? Task.FromResult(result)
            : Task.FromException<IntegrationResult>(Exception);
    }

    private sealed class FakeTrayRunner : ITrayApplicationRunner
    {
        public int ExitCode { get; set; }
        public int RunCalls { get; private set; }

        public int Run()
        {
            RunCalls++;
            return ExitCode;
        }
    }

    private sealed class RecordingMessages : IUserMessageSink
    {
        public List<UserMessage> Messages { get; } = [];

        public void Show(string title, string text, bool isError)
        {
            Messages.Add(new UserMessage(title, text, isError));
        }
    }

    private sealed record UserMessage(string Title, string Text, bool IsError);
}
