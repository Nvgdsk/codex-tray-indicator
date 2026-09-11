using System.Text;
using CodexTray.Tests.TestDoubles;

namespace CodexTray.Tests;

public sealed class HookPayloadParserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryParse_UserPromptSubmit_ExtractsOnlyLifecycleIdentity()
    {
        byte[] json = """
            {
              "hook_event_name":"UserPromptSubmit",
              "session_id":"s1",
              "turn_id":"t1",
              "prompt":"secret prompt",
              "last_assistant_message":"secret response",
              "transcript_path":"/secret/transcript.jsonl",
              "cwd":"/secret/workspace",
              "model":"secret-model"
            }
            """u8.ToArray();
        var clock = new ManualTimeProvider(Now);

        bool parsed = HookPayloadParser.TryParse(json, clock, out CodexHookEvent? value);

        Assert.True(parsed);
        Assert.Equal(
            new CodexHookEvent(CodexHookEventName.UserPromptSubmit, "s1", "t1", null, Now),
            value);
        Assert.DoesNotContain("secret", value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SessionStart", null, "startup", (int)CodexHookEventName.SessionStart)]
    [InlineData("SessionStart", null, "compact", (int)CodexHookEventName.SessionStart)]
    [InlineData("UserPromptSubmit", "turn", null, (int)CodexHookEventName.UserPromptSubmit)]
    [InlineData("Stop", "turn", null, (int)CodexHookEventName.Stop)]
    [InlineData("Interrupt", "turn", null, (int)CodexHookEventName.Interrupt)]
    [InlineData("SessionEnd", null, null, (int)CodexHookEventName.SessionEnd)]
    public void TryParse_SupportedEvent_MapsExactName(
        string eventName,
        string? turnId,
        string? source,
        int expectedName)
    {
        string json = $$"""
            {"hook_event_name":"{{eventName}}","session_id":"session","turn_id":{{Json(turnId)}},"source":{{Json(source)}}}
            """;

        bool parsed = HookPayloadParser.TryParse(
            Encoding.UTF8.GetBytes(json),
            new ManualTimeProvider(Now),
            out CodexHookEvent? value);

        Assert.True(parsed);
        Assert.Equal((CodexHookEventName)expectedName, value!.Name);
        Assert.Equal("session", value.SessionId);
        Assert.Equal(turnId, value.TurnId);
        Assert.Equal(source, value.Source);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":\"\",\"turn_id\":\"t\"}")]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":\"s\"}")]
    [InlineData("{\"hook_event_name\":\"Interrupt\",\"session_id\":\"s\"}")]
    [InlineData("{\"hook_event_name\":\"UserPromptSubmit\",\"session_id\":\"s\"}")]
    [InlineData("{\"hook_event_name\":\"Other\",\"session_id\":\"s\"}")]
    [InlineData("{")]
    public void TryParse_InvalidPayload_ReturnsFalse(string json)
    {
        bool parsed = HookPayloadParser.TryParse(
            Encoding.UTF8.GetBytes(json),
            new ManualTimeProvider(Now),
            out CodexHookEvent? value);

        Assert.False(parsed);
        Assert.Null(value);
    }

    [Fact]
    public void TryParse_OversizedPayload_ReturnsFalse()
    {
        byte[] json = new byte[AppConstants.MaxMessageBytes + 1];

        bool parsed = HookPayloadParser.TryParse(
            json,
            new ManualTimeProvider(Now),
            out CodexHookEvent? value);

        Assert.False(parsed);
        Assert.Null(value);
    }

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";
}
