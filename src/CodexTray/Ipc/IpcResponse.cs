namespace CodexTray;

internal sealed record IpcResponse(bool Ok, TrayState? State, string? Error);
