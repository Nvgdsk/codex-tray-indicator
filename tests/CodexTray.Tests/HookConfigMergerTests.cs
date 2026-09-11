using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexTray.Tests;

public sealed class HookConfigMergerTests
{
    private static readonly string[] Events =
        ["SessionStart", "UserPromptSubmit", "Stop", "Interrupt", "SessionEnd"];

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void Install_MissingOrEmptyDocument_AddsOneHandlerForEveryEvent(string existing)
    {
        string result = HookConfigMerger.Install(existing, "/mnt/c/Program Files/CodexTray/CodexTray.exe");

        Assert.Equal(5, HookConfigMerger.CountOwnedHandlers(result));
        JsonObject root = ParseObject(result);
        JsonObject hooks = Assert.IsType<JsonObject>(root["hooks"]);
        foreach (string eventName in Events)
        {
            JsonArray groups = Assert.IsType<JsonArray>(hooks[eventName]);
            JsonObject group = Assert.IsType<JsonObject>(Assert.Single(groups));
            JsonObject handler = Assert.IsType<JsonObject>(
                Assert.Single(Assert.IsType<JsonArray>(group["hooks"])));
            Assert.Equal("command", handler["type"]!.GetValue<string>());
            Assert.Equal(
                "\"/mnt/c/Program Files/CodexTray/CodexTray.exe\" --hook --integration-id codex-tray-indicator-v1",
                handler["command"]!.GetValue<string>());
            Assert.Equal(1, handler["timeout"]!.GetValue<int>());
            Assert.True(handler["async"]!.GetValue<bool>());
        }

        Assert.EndsWith(Environment.NewLine, result, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTwice_ProducesExactlyFiveOwnedHandlers()
    {
        string once = HookConfigMerger.Install("{}", "/mnt/c/Program Files/CodexTray/CodexTray.exe");
        string twice = HookConfigMerger.Install(once, "/mnt/c/Program Files/CodexTray/CodexTray.exe");

        Assert.Equal(5, HookConfigMerger.CountOwnedHandlers(twice));
    }

    [Fact]
    public void Install_ReplacesOldAndDuplicateOwnedHandlers_WithoutChangingForeignValues()
    {
        const string existing = """
            {
              "metadata": { "owner": "user", "enabled": true, "values": [1, null, "x"] },
              "hooks": {
                "SessionStart": [
                  {
                    "matcher": "compact|startup",
                    "extra": { "keep": 7 },
                    "hooks": [
                      { "type": "command", "command": "foreign --integration-id something-else", "timeout": 9 },
                      { "type": "command", "command": "old --integration-id codex-tray-indicator-v1", "old": true },
                      { "type": "command", "command": "duplicate --integration-id codex-tray-indicator-v1" }
                    ]
                  }
                ],
                "UserPromptSubmit": [{ "hooks": [{ "type": "http", "url": "https://example.invalid/hook" }] }],
                "Stop": [{ "matcher": "x", "hooks": [] }],
                "Interrupt": [],
                "SessionEnd": [{ "hooks": [{ "type": "command", "command": "echo foreign" }] }],
                "CustomEvent": [{ "arbitrary": [true, false] }]
              }
            }
            """;
        JsonObject before = ParseObject(existing);

        string result = HookConfigMerger.Install(existing, "/new/path/CodexTray.exe");

        JsonObject after = ParseObject(result);
        Assert.True(JsonNode.DeepEquals(before["metadata"], after["metadata"]));
        Assert.True(JsonNode.DeepEquals(before["hooks"]!["CustomEvent"], after["hooks"]!["CustomEvent"]));
        JsonObject firstGroup = Assert.IsType<JsonObject>(after["hooks"]!["SessionStart"]![0]);
        Assert.Equal("compact|startup", firstGroup["matcher"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(before["hooks"]!["SessionStart"]![0]!["extra"], firstGroup["extra"]));
        Assert.Single(Assert.IsType<JsonArray>(firstGroup["hooks"]));
        Assert.Equal(5, HookConfigMerger.CountOwnedHandlers(result));
        Assert.DoesNotContain("old --integration-id", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Uninstall_RemovesOnlyOwnedHandlersAndEmptyOwnedGroups()
    {
        const string existing = """
            {
              "top": { "keep": "exactly" },
              "hooks": {
                "SessionStart": [
                  { "matcher": "foreign", "hooks": [{ "type": "command", "command": "echo keep", "custom": 12 }] },
                  { "matcher": "owned", "hooks": [{ "type": "command", "command": "x --integration-id codex-tray-indicator-v1" }] }
                ],
                "UserPromptSubmit": [{ "hooks": [
                  { "type": "command", "command": "x --integration-id codex-tray-indicator-v1" },
                  { "type": "http", "url": "https://example.invalid" }
                ] }],
                "Stop": [],
                "Interrupt": [{ "matcher": "stay", "hooks": [] }],
                "SessionEnd": [{ "hooks": [{ "type": "command", "command": "echo keep" }] }]
              }
            }
            """;
        JsonObject before = ParseObject(existing);

        string result = HookConfigMerger.Uninstall(existing);

        JsonObject after = ParseObject(result);
        Assert.Equal(0, HookConfigMerger.CountOwnedHandlers(result));
        Assert.True(JsonNode.DeepEquals(before["top"], after["top"]));
        Assert.True(JsonNode.DeepEquals(before["hooks"]!["SessionStart"]![0], after["hooks"]!["SessionStart"]![0]));
        Assert.Single(Assert.IsType<JsonArray>(after["hooks"]!["SessionStart"]));
        JsonArray promptHandlers = Assert.IsType<JsonArray>(after["hooks"]!["UserPromptSubmit"]![0]!["hooks"]);
        Assert.Single(promptHandlers);
        Assert.Equal("http", promptHandlers[0]!["type"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(before["hooks"]!["Interrupt"], after["hooks"]!["Interrupt"]));
    }

    [Fact]
    public void Install_EscapesPosixDoubleQuotedPath()
    {
        string result = HookConfigMerger.Install("{}", "/mnt/c/a\\b\"$`/CodexTray.exe");
        JsonObject root = ParseObject(result);
        string command = root["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();

        Assert.Equal(
            "\"/mnt/c/a\\\\b\\\"\\$\\`/CodexTray.exe\" --hook --integration-id codex-tray-indicator-v1",
            command);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"hooks\":[]}")]
    public void Install_InvalidShape_ThrowsWithoutReturningReplacement(string existing)
    {
        Assert.ThrowsAny<Exception>(() => HookConfigMerger.Install(existing, "/valid/path.exe"));
    }

    [Fact]
    public void CountOwnedHandlers_IgnoresTokensOutsideCommandHandlers()
    {
        const string json = """
            { "hooks": { "Stop": [{ "hooks": [
              { "type": "http", "command": "x --integration-id codex-tray-indicator-v1" },
              { "type": "command", "command": 42 },
              { "type": "command", "command": "x --integration-id other" }
            ] }] } }
            """;

        Assert.Equal(0, HookConfigMerger.CountOwnedHandlers(json));
    }

    private static JsonObject ParseObject(string json)
    {
        return Assert.IsType<JsonObject>(JsonNode.Parse(json));
    }
}
