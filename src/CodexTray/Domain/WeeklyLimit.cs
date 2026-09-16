using System.Text.Json;

namespace CodexTray;

internal sealed record WeeklyLimit(int RemainingPercent, DateTimeOffset ResetsAt)
{
    public static WeeklyLimit? Parse(JsonElement result, DateTimeOffset now)
    {
        if (result.ValueKind != JsonValueKind.Object) return null;
        JsonElement bucket;
        if (result.TryGetProperty("rateLimitsByLimitId", out JsonElement buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            if (!buckets.TryGetProperty("codex", out bucket)) return null;
        }
        else if (!result.TryGetProperty("rateLimits", out bucket)) return null;
        if (bucket.ValueKind != JsonValueKind.Object) return null;
        if (bucket.TryGetProperty("limitId", out JsonElement id) && id.ValueKind == JsonValueKind.String && id.GetString() != "codex")
            return null;
        return ParseWindow(bucket, "primary", now) ?? ParseWindow(bucket, "secondary", now);
    }

    private static WeeklyLimit? ParseWindow(JsonElement bucket, string name, DateTimeOffset now)
    {
        if (!bucket.TryGetProperty(name, out JsonElement window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("windowDurationMins", out JsonElement duration) || duration.ValueKind != JsonValueKind.Number ||
            !duration.TryGetInt32(out int minutes) || minutes != 7 * 24 * 60 ||
            !window.TryGetProperty("usedPercent", out JsonElement used) || used.ValueKind != JsonValueKind.Number ||
            !used.TryGetDouble(out double percent) || !double.IsFinite(percent) || percent < 0 ||
            !window.TryGetProperty("resetsAt", out JsonElement reset) || reset.ValueKind != JsonValueKind.Number ||
            !reset.TryGetInt64(out long seconds) || seconds <= now.ToUnixTimeSeconds() ||
            seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds()) return null;
        return new WeeklyLimit((int)Math.Floor(Math.Clamp(100 - percent, 0, 100)), DateTimeOffset.FromUnixTimeSeconds(seconds));
    }
}
