namespace CodexTray;

internal enum CodexHookEventName
{
    SessionStart,
    UserPromptSubmit,
    Stop,
    Interrupt,
    SessionEnd,
}

internal sealed record CodexHookEvent(
    CodexHookEventName Name,
    string SessionId,
    string? TurnId,
    string? Source,
    DateTimeOffset TimestampUtc);
