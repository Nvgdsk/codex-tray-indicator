namespace CodexTray.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Parse_WithoutArguments_SelectsTrayMode()
    {
        AppCommand result = CommandLine.Parse([]);

        Assert.Equal(new AppCommand(AppMode.Tray, null, null), result);
    }

    [Theory]
    [InlineData("--hook", (int)AppMode.Hook)]
    [InlineData("--query-state", (int)AppMode.QueryState)]
    [InlineData("--install", (int)AppMode.Install)]
    [InlineData("--uninstall", (int)AppMode.Uninstall)]
    [InlineData("--shutdown", (int)AppMode.Shutdown)]
    public void Parse_SingleModeArgument_SelectsRequestedMode(string argument, int expected)
    {
        AppCommand result = CommandLine.Parse([argument]);

        Assert.Equal((AppMode)expected, result.Mode);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Parse_HookWithIntegrationId_CapturesMarker()
    {
        AppCommand result = CommandLine.Parse(
            ["--hook", "--integration-id", "codex-tray-indicator-v1"]);

        Assert.Equal(AppMode.Hook, result.Mode);
        Assert.Equal("codex-tray-indicator-v1", result.IntegrationId);
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("ready")]
    [InlineData("inactive")]
    [InlineData("error")]
    public void Parse_HookTest_CapturesSupportedState(string state)
    {
        AppCommand result = CommandLine.Parse(["--hook-test", state]);

        Assert.Equal(AppMode.HookTest, result.Mode);
        Assert.Equal(state, result.Value);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--hook-test")]
    [InlineData("--hook-test", "blue")]
    [InlineData("--query-state", "extra")]
    [InlineData("--install", "--uninstall")]
    [InlineData("--query-state", "--integration-id", "marker")]
    public void Parse_InvalidArguments_Throws(params string[] arguments)
    {
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(arguments));
    }
}
