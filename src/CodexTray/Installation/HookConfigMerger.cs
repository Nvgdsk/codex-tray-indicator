using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexTray;

internal static class HookConfigMerger
{
    private const string OwnedToken = "--integration-id " + AppConstants.IntegrationId;

    private static readonly string[] EventNames =
        ["SessionStart", "UserPromptSubmit", "Stop", "Interrupt", "SessionEnd"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static string Install(string existingJson, string wslExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wslExePath);
        JsonObject root = ParseRoot(existingJson);
        JsonObject hooks = GetOrCreateHooks(root);

        RemoveOwnedHandlers(hooks);
        string command = $"\"{EscapePosixDoubleQuoted(wslExePath)}\" --hook {OwnedToken}";
        foreach (string eventName in EventNames)
        {
            JsonArray groups = GetOrCreateEventGroups(hooks, eventName);
            var handler = new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
                ["timeout"] = 1,
            };
            // Codex always runs SessionEnd synchronously and warns if async is enabled.
            if (eventName != "SessionEnd")
            {
                handler["async"] = true;
            }

            groups.Add(new JsonObject
            {
                ["hooks"] = new JsonArray { handler },
            });
        }

        return Serialize(root);
    }

    public static string Uninstall(string existingJson)
    {
        JsonObject root = ParseRoot(existingJson);
        if (root["hooks"] is null)
        {
            return Serialize(root);
        }

        JsonObject hooks = root["hooks"] as JsonObject
            ?? throw new JsonException("The hooks property must be a JSON object.");
        RemoveOwnedHandlers(hooks);
        return Serialize(root);
    }

    public static int CountOwnedHandlers(string json)
    {
        JsonObject root = ParseRoot(json);
        if (root["hooks"] is null)
        {
            return 0;
        }

        JsonObject hooks = root["hooks"] as JsonObject
            ?? throw new JsonException("The hooks property must be a JSON object.");
        int count = 0;
        foreach (string eventName in EventNames)
        {
            if (hooks[eventName] is not JsonArray groups)
            {
                continue;
            }

            foreach (JsonNode? groupNode in groups)
            {
                if (groupNode is not JsonObject group || group["hooks"] is not JsonArray handlers)
                {
                    continue;
                }

                count += handlers.Count(IsOwnedHandler);
            }
        }

        return count;
    }

    private static JsonObject ParseRoot(string existingJson)
    {
        string json = string.IsNullOrWhiteSpace(existingJson) ? "{}" : existingJson;
        return JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("The hook configuration root must be a JSON object.");
    }

    private static JsonObject GetOrCreateHooks(JsonObject root)
    {
        if (root["hooks"] is null)
        {
            var hooks = new JsonObject();
            root["hooks"] = hooks;
            return hooks;
        }

        return root["hooks"] as JsonObject
            ?? throw new JsonException("The hooks property must be a JSON object.");
    }

    private static JsonArray GetOrCreateEventGroups(JsonObject hooks, string eventName)
    {
        if (hooks[eventName] is null)
        {
            var groups = new JsonArray();
            hooks[eventName] = groups;
            return groups;
        }

        return hooks[eventName] as JsonArray
            ?? throw new JsonException($"The {eventName} hook property must be a JSON array.");
    }

    private static void RemoveOwnedHandlers(JsonObject hooks)
    {
        foreach (string eventName in EventNames)
        {
            if (hooks[eventName] is null)
            {
                continue;
            }

            JsonArray groups = hooks[eventName] as JsonArray
                ?? throw new JsonException($"The {eventName} hook property must be a JSON array.");
            for (int groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
            {
                if (groups[groupIndex] is not JsonObject group || group["hooks"] is not JsonArray handlers)
                {
                    continue;
                }

                bool removedOwnedHandler = false;
                for (int handlerIndex = handlers.Count - 1; handlerIndex >= 0; handlerIndex--)
                {
                    if (!IsOwnedHandler(handlers[handlerIndex]))
                    {
                        continue;
                    }

                    handlers.RemoveAt(handlerIndex);
                    removedOwnedHandler = true;
                }

                if (removedOwnedHandler && handlers.Count == 0)
                {
                    groups.RemoveAt(groupIndex);
                }
            }
        }
    }

    private static bool IsOwnedHandler(JsonNode? node)
    {
        if (node is not JsonObject handler ||
            handler["type"] is not JsonValue typeValue ||
            handler["command"] is not JsonValue commandValue ||
            !typeValue.TryGetValue(out string? type) ||
            !commandValue.TryGetValue(out string? command))
        {
            return false;
        }

        return type.Equals("command", StringComparison.OrdinalIgnoreCase) &&
            ContainsOwnedToken(command);
    }

    private static bool ContainsOwnedToken(string command)
    {
        int searchIndex = 0;
        while (searchIndex < command.Length)
        {
            int tokenIndex = command.IndexOf(OwnedToken, searchIndex, StringComparison.Ordinal);
            if (tokenIndex < 0)
            {
                return false;
            }

            int tokenEnd = tokenIndex + OwnedToken.Length;
            bool hasLeftBoundary = tokenIndex == 0 || char.IsWhiteSpace(command[tokenIndex - 1]);
            bool hasRightBoundary = tokenEnd == command.Length || char.IsWhiteSpace(command[tokenEnd]);
            if (hasLeftBoundary && hasRightBoundary)
            {
                return true;
            }

            searchIndex = tokenIndex + 1;
        }

        return false;
    }

    private static string EscapePosixDoubleQuoted(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
    }

    private static string Serialize(JsonObject root)
    {
        return root.ToJsonString(SerializerOptions) + Environment.NewLine;
    }
}
