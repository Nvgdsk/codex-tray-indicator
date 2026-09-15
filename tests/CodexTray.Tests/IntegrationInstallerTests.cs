namespace CodexTray.Tests;

public sealed class IntegrationInstallerTests
{
    private const string ExePath = @"C:\Program Files\Codex Tray\CodexTray.exe";

    [Fact]
    public async Task Install_WithOneDistro_WritesMergedConfigBeforeEnablingStartup()
    {
        InstallerFixture fixture = InstallerFixture.OneDistro();
        fixture.Store.JsonByDistribution["Ubuntu"] = "{\"foreign\":true}";

        IntegrationResult result = await fixture.Installer.InstallAsync(ExePath, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Ubuntu", result.Distribution);
        Assert.Equal("codex-cli 0.154.0", result.CodexVersion);
        Assert.Equal(5, HookConfigMerger.CountOwnedHandlers(fixture.Store.LastWrittenJson!));
        Assert.Contains("\"foreign\": true", fixture.Store.LastWrittenJson, StringComparison.Ordinal);
        Assert.True(fixture.Store.LastCreateBackup);
        Assert.Equal("Ubuntu", fixture.Settings.WslDistribution);
        Assert.Equal(ExePath, fixture.Settings.StartupPath);
        Assert.Equal(["write:Ubuntu", "startup", "distro:Ubuntu"], fixture.Events);
    }

    [Fact]
    public async Task Install_WhenNoDistroContainsCodex_ReturnsActionableFailure()
    {
        InstallerFixture fixture = InstallerFixture.WithInstallations([]);

        IntegrationResult result = await fixture.Installer.InstallAsync(ExePath, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Codex", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Store.LastWrittenJson);
        Assert.Null(fixture.Settings.StartupPath);
    }

    [Fact]
    public async Task Install_WithMultipleUnselectedDistros_ReturnsActionableFailure()
    {
        InstallerFixture fixture = InstallerFixture.WithInstallations(
        [
            new("Ubuntu", "/usr/bin/codex", "codex-cli 0.154.0"),
            new("Debian", "/usr/local/bin/codex", "codex-cli 0.153.0"),
        ]);

        IntegrationResult result = await fixture.Installer.InstallAsync(ExePath, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("multiple", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ubuntu", result.Message, StringComparison.Ordinal);
        Assert.Contains("Debian", result.Message, StringComparison.Ordinal);
        Assert.Null(fixture.Store.LastWrittenJson);
    }

    [Fact]
    public async Task Install_WithMultipleDistros_ReusesPreviouslySelectedDistro()
    {
        InstallerFixture fixture = InstallerFixture.WithInstallations(
        [
            new("Ubuntu", "/usr/bin/codex", "codex-cli 0.154.0"),
            new("Debian", "/usr/local/bin/codex", "codex-cli 0.153.0"),
        ]);
        fixture.Settings.WslDistribution = "Debian";
        fixture.Events.Clear();

        IntegrationResult result = await fixture.Installer.InstallAsync(ExePath, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Debian", result.Distribution);
        Assert.Equal("Debian", fixture.Detector.ConvertedDistribution);
    }

    [Fact]
    public async Task Install_WithInvalidExistingJson_DoesNotWriteOrChangeRegistry()
    {
        InstallerFixture fixture = InstallerFixture.OneDistro();
        fixture.Store.JsonByDistribution["Ubuntu"] = "not-json";

        IntegrationResult result = await fixture.Installer.InstallAsync(ExePath, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(fixture.Store.LastWrittenJson);
        Assert.Null(fixture.Settings.StartupPath);
        Assert.Null(fixture.Settings.WslDistribution);
    }

    [Fact]
    public async Task Install_WhenConfigWriteFails_DoesNotEnableStartup()
    {
        InstallerFixture fixture = InstallerFixture.OneDistro();
        fixture.Store.WriteException = new IOException("disk full");

        IntegrationResult result = await fixture.Installer.InstallAsync(ExePath, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("disk full", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Settings.StartupPath);
        Assert.Null(fixture.Settings.WslDistribution);
    }

    [Fact]
    public async Task Install_ReinstallKeepsExactlyFiveOwnedHandlers()
    {
        InstallerFixture fixture = InstallerFixture.OneDistro();
        IntegrationResult first = await fixture.Installer.InstallAsync(ExePath, CancellationToken.None);
        fixture.Store.JsonByDistribution["Ubuntu"] = fixture.Store.LastWrittenJson!;

        IntegrationResult second = await fixture.Installer.InstallAsync(ExePath, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(5, HookConfigMerger.CountOwnedHandlers(fixture.Store.LastWrittenJson!));
    }

    [Fact]
    public async Task Uninstall_UsesRecordedDistroBeforeRemovingRegistryValues()
    {
        InstallerFixture fixture = InstallerFixture.OneDistro();
        fixture.Settings.WslDistribution = "Ubuntu";
        fixture.Settings.StartupPath = ExePath;
        fixture.Store.JsonByDistribution["Ubuntu"] = HookConfigMerger.Install("{}", "/old/CodexTray.exe");
        fixture.Detector.ThrowIfFindCalled = true;
        fixture.Events.Clear();

        IntegrationResult result = await fixture.Installer.UninstallAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, HookConfigMerger.CountOwnedHandlers(fixture.Store.LastWrittenJson!));
        Assert.False(fixture.Store.LastCreateBackup);
        Assert.Null(fixture.Settings.StartupPath);
        Assert.Null(fixture.Settings.WslDistribution);
        Assert.Equal(["write:Ubuntu", "remove-startup", "clear-settings"], fixture.Events);
    }

    [Fact]
    public async Task Uninstall_WithoutRecordedDistroFallsBackToDetection()
    {
        InstallerFixture fixture = InstallerFixture.OneDistro();
        fixture.Store.JsonByDistribution["Ubuntu"] = HookConfigMerger.Install("{}", "/old/CodexTray.exe");

        IntegrationResult result = await fixture.Installer.UninstallAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, fixture.Detector.FindCalls);
        Assert.Equal("Ubuntu", fixture.Store.LastWrittenDistribution);
    }

    [Fact]
    public async Task Uninstall_WhenHookWriteFails_LeavesRegistryValuesIntact()
    {
        InstallerFixture fixture = InstallerFixture.OneDistro();
        fixture.Settings.WslDistribution = "Ubuntu";
        fixture.Settings.StartupPath = ExePath;
        fixture.Store.JsonByDistribution["Ubuntu"] = HookConfigMerger.Install("{}", "/old/CodexTray.exe");
        fixture.Store.WriteException = new IOException("read-only filesystem");

        IntegrationResult result = await fixture.Installer.UninstallAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ExePath, fixture.Settings.StartupPath);
        Assert.Equal("Ubuntu", fixture.Settings.WslDistribution);
    }

    [Fact]
    public async Task WslHookConfigStore_ReadMissingFileReturnsEmptyObject()
    {
        var runner = new QueueProcessRunner(new ProcessResult(3, string.Empty, string.Empty));
        var store = new WslHookConfigStore(runner);

        string result = await store.ReadAsync("Ubuntu", CancellationToken.None);

        Assert.Equal("{}", result);
        Assert.Equal("wsl.exe", runner.Requests[0].FileName);
        Assert.Equal("Ubuntu", runner.Requests[0].Arguments[1]);
        Assert.Equal("--exec", runner.Requests[0].Arguments[2]);
        Assert.Contains("hooks.json", runner.Requests[0].Arguments[5], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public async Task WslHookConfigStore_WriteUsesStdinAndAtomicScript(bool createBackup, string flag)
    {
        var runner = new QueueProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var store = new WslHookConfigStore(runner);

        await store.WriteAtomicAsync("Ubuntu", "{\"ok\":true}\n", createBackup, CancellationToken.None);

        ProcessRequest request = Assert.Single(runner.Requests);
        Assert.Equal("{\"ok\":true}\n", request.StandardInput);
        Assert.Equal("--exec", request.Arguments[2]);
        Assert.Equal("sh", request.Arguments[3]);
        Assert.Equal(flag, request.Arguments[^1]);
        Assert.Contains("mktemp", request.Arguments[5], StringComparison.Ordinal);
        Assert.Contains("chmod 600", request.Arguments[5], StringComparison.Ordinal);
        Assert.Contains("mv -f", request.Arguments[5], StringComparison.Ordinal);
    }

    private sealed class InstallerFixture
    {
        private InstallerFixture(IReadOnlyList<WslCodexInstallation> installations)
        {
            Events = [];
            Detector = new FakeDetector(installations);
            Store = new FakeStore(Events);
            Settings = new FakeSettings(Events);
            Installer = new IntegrationInstaller(Detector, Store, Settings);
        }

        public List<string> Events { get; }
        public FakeDetector Detector { get; }
        public FakeStore Store { get; }
        public FakeSettings Settings { get; }
        public IntegrationInstaller Installer { get; }

        public static InstallerFixture OneDistro() => WithInstallations(
            [new("Ubuntu", "/home/user/.local/bin/codex", "codex-cli 0.154.0")]);

        public static InstallerFixture WithInstallations(IReadOnlyList<WslCodexInstallation> installations) =>
            new(installations);
    }

    private sealed class FakeDetector(IReadOnlyList<WslCodexInstallation> installations) : IWslDetector
    {
        public int FindCalls { get; private set; }
        public bool ThrowIfFindCalled { get; set; }
        public string? ConvertedDistribution { get; private set; }

        public Task<IReadOnlyList<WslCodexInstallation>> FindCodexInstallationsAsync(
            CancellationToken cancellationToken)
        {
            FindCalls++;
            if (ThrowIfFindCalled)
            {
                throw new InvalidOperationException("Detection was not expected.");
            }

            return Task.FromResult(installations);
        }

        public Task<string> ConvertWindowsPathAsync(
            string distribution,
            string windowsPath,
            CancellationToken cancellationToken)
        {
            ConvertedDistribution = distribution;
            return Task.FromResult("/mnt/c/Program Files/Codex Tray/CodexTray.exe");
        }
    }

    private sealed class FakeStore(List<string> events) : IWslHookConfigStore
    {
        public Dictionary<string, string> JsonByDistribution { get; } = [];
        public Exception? WriteException { get; set; }
        public string? LastWrittenJson { get; private set; }
        public string? LastWrittenDistribution { get; private set; }
        public bool LastCreateBackup { get; private set; }

        public Task<string> ReadAsync(string distribution, CancellationToken cancellationToken) =>
            Task.FromResult(JsonByDistribution.GetValueOrDefault(distribution, "{}"));

        public Task WriteAtomicAsync(
            string distribution,
            string json,
            bool createBackup,
            CancellationToken cancellationToken)
        {
            if (WriteException is not null)
            {
                throw WriteException;
            }

            LastWrittenDistribution = distribution;
            LastWrittenJson = json;
            LastCreateBackup = createBackup;
            events.Add($"write:{distribution}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettings(List<string> events) : IUserSettings
    {
        private string? _wslDistribution;

        public bool NotificationsEnabled { get; set; } = true;
        public UsbScreenOptions UsbScreen { get; set; } = new();
        public string? StartupPath { get; set; }

        public string? WslDistribution
        {
            get => _wslDistribution;
            set
            {
                _wslDistribution = value;
                if (value is not null)
                {
                    events.Add($"distro:{value}");
                }
            }
        }

        public bool StartupEnabled => StartupPath is not null;

        public void SetStartup(string exePath)
        {
            StartupPath = exePath;
            events.Add("startup");
        }

        public void RemoveStartup()
        {
            StartupPath = null;
            events.Add("remove-startup");
        }

        public void ClearApplicationSettings()
        {
            _wslDistribution = null;
            events.Add("clear-settings");
        }

        public void Dispose()
        {
        }
    }

    private sealed class QueueProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
