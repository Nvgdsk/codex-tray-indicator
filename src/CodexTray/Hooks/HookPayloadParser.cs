using System.Text.Json;

namespace CodexTray;

internal static class HookPayloadParser
{
    public static bool TryParse(
        ReadOnlySpan<byte> utf8,
        TimeProvider clock,
        out CodexHookEvent? value)
    {
        value = null;
        if (utf8.IsEmpty || utf8.Length > AppConstants.MaxMessageBytes)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetRequiredString(root, "hook_event_name", out string? eventText) ||
                !TryGetRequiredString(root, "session_id", out string? sessionId) ||
                !TryMapEvent(eventText, out CodexHookEventName eventName))
            {
                return false;
            }

            string? turnId = GetOptionalString(root, "turn_id");
            if (RequiresTurnId(eventName) && string.IsNullOrWhiteSpace(turnId))
            {
                return false;
            }

            string? source = eventName == CodexHookEventName.SessionStart
                ? GetOptionalString(root, "source")
                : null;

            value = new CodexHookEvent(
                eventName,
                sessionId!,
                turnId,
                source,
                clock.GetUtcNow());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetRequiredString(
        JsonElement root,
        string propertyName,
        out string? value)
    {
        value = GetOptionalString(root, propertyName);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement property)
            ? property.GetString()
            : null;
    }

    private static bool TryMapEvent(string? value, out CodexHookEventName eventName)
    {
        switch (value)
        {
            case "SessionStart":
                eventName = CodexHookEventName.SessionStart;
                return true;
            case "UserPromptSubmit":
                eventName = CodexHookEventName.UserPromptSubmit;
                return true;
            case "Stop":
                eventName = CodexHookEventName.Stop;
                return true;
            case "Interrupt":
                eventName = CodexHookEventName.Interrupt;
                return true;
            case "SessionEnd":
                eventName = CodexHookEventName.SessionEnd;
                return true;
            default:
                eventName = default;
                return false;
        }
    }

    private static bool RequiresTurnId(CodexHookEventName eventName)
    {
        return eventName is CodexHookEventName.UserPromptSubmit
            or CodexHookEventName.Stop
            or CodexHookEventName.Interrupt;
    }
}
