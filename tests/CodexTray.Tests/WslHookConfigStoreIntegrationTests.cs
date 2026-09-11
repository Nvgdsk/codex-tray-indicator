using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodexTray.Tests;

public sealed partial class WslHookConfigStoreIntegrationTests
{
    private const string Distribution = "Ubuntu";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InstallReinstallUninstall_IsAtomicAndPreservesForeignHooks()
    {
        string directory = "/tmp/codex-tray-tests-" + Guid.NewGuid().ToString("N");
        Assert.Matches(SafeTestDirectory(), directory);
        var runner = new ProcessRunner();
        var store = new WslHookConfigStore(runner, directory);
        const string original = """
            {
              "metadata": { "test": true },
              "hooks": {
                "SessionStart": [{ "matcher": "startup", "hooks": [
                  { "type": "command", "command": "echo foreign-start", "timeout": 7 }
                ] }],
                "Stop": [{ "hooks": [
                  { "type": "command", "command": "echo foreign-stop", "custom": { "keep": true } }
                ] }]
              }
            }
            """;

        try
        {
            await RunWslAsync(
                runner,
                "set -eu; umask 077; mkdir -p \"$1\"; cat > \"$1/hooks.json\"",
                directory,
                original);

            string before = await store.ReadAsync(Distribution, CancellationToken.None);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(before)));

            string installed = HookConfigMerger.Install(before, "/mnt/c/CodexTray/CodexTray.exe");
            await store.WriteAtomicAsync(Distribution, installed, true, CancellationToken.None);
            string afterInstall = await store.ReadAsync(Distribution, CancellationToken.None);
            Assert.Equal(5, HookConfigMerger.CountOwnedHandlers(afterInstall));
            Assert.Equal(2, CountForeignHandlers(afterInstall));

            string reinstalled = HookConfigMerger.Install(afterInstall, "/mnt/c/New/CodexTray.exe");
            await store.WriteAtomicAsync(Distribution, reinstalled, true, CancellationToken.None);
            string afterReinstall = await store.ReadAsync(Distribution, CancellationToken.None);
            Assert.Equal(5, HookConfigMerger.CountOwnedHandlers(afterReinstall));
            Assert.Equal(2, CountForeignHandlers(afterReinstall));

            ProcessResult backup = await RunWslAsync(
                runner,
                "cat \"$1/hooks.json.codextray.bak\"",
                directory,
                standardInput: null);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(backup.StandardOutput)));

            string uninstalled = HookConfigMerger.Uninstall(afterReinstall);
            await store.WriteAtomicAsync(Distribution, uninstalled, false, CancellationToken.None);
            string afterUninstall = await store.ReadAsync(Distribution, CancellationToken.None);
            Assert.Equal(0, HookConfigMerger.CountOwnedHandlers(afterUninstall));
            Assert.Equal(2, CountForeignHandlers(afterUninstall));
        }
        finally
        {
            Assert.Matches(SafeTestDirectory(), directory);
            await RunWslAsync(
                runner,
                "set -eu; case \"$1\" in /tmp/codex-tray-tests-[0-9a-f]*) rm -rf -- \"$1\" ;; *) exit 64 ;; esac",
                directory,
                standardInput: null);
        }
    }

    private static int CountForeignHandlers(string json)
    {
        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(json));
        JsonObject hooks = Assert.IsType<JsonObject>(root["hooks"]);
        return hooks
            .SelectMany(property => property.Value as JsonArray ?? [])
            .OfType<JsonObject>()
            .SelectMany(group => group["hooks"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Count(handler =>
                handler["type"]?.GetValue<string>() == "command" &&
                !handler["command"]!.GetValue<string>().Contains(
                    "--integration-id codex-tray-indicator-v1",
                    StringComparison.Ordinal));
    }

    private static async Task<ProcessResult> RunWslAsync(
        IProcessRunner runner,
        string script,
        string directory,
        string? standardInput)
    {
        ProcessResult result = await runner.RunAsync(
            new ProcessRequest(
                "wsl.exe",
                ["-d", Distribution, "--exec", "sh", "-lc", script, "codex-tray-test", directory],
                standardInput,
                TimeSpan.FromSeconds(15)),
            CancellationToken.None);
        Assert.True(
            result.ExitCode == 0,
            $"WSL command failed ({result.ExitCode}): {result.StandardError}");
        return result;
    }

    [GeneratedRegex(@"^/tmp/codex-tray-tests-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTestDirectory();
}
