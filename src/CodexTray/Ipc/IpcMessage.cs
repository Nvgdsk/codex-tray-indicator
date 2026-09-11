using System.IO;

namespace CodexTray;

internal enum IpcMessageKind
{
    Event,
    SetState,
    QueryState,
    Shutdown,
}

internal sealed record IpcMessage(
    int ProtocolVersion,
    IpcMessageKind Kind,
    CodexHookEventName? Event,
    string? SessionId,
    string? TurnId,
    string? Source,
    DateTimeOffset? TimestampUtc,
    TrayState? State)
{
    public static IpcMessage FromEvent(CodexHookEvent hookEvent)
    {
        ArgumentNullException.ThrowIfNull(hookEvent);
        return new IpcMessage(
            AppConstants.ProtocolVersion,
            IpcMessageKind.Event,
            hookEvent.Name,
            hookEvent.SessionId,
            hookEvent.TurnId,
            hookEvent.Source,
            hookEvent.TimestampUtc,
            null);
    }

    public static IpcMessage ForState(TrayState state) =>
        new(AppConstants.ProtocolVersion, IpcMessageKind.SetState, null, null, null, null, null, state);

    public static IpcMessage Query() =>
        new(AppConstants.ProtocolVersion, IpcMessageKind.QueryState, null, null, null, null, null, null);

    public static IpcMessage Shutdown() =>
        new(AppConstants.ProtocolVersion, IpcMessageKind.Shutdown, null, null, null, null, null, null);

    public CodexHookEvent ToCodexHookEvent()
    {
        ValidateProtocol();
        if (Kind != IpcMessageKind.Event ||
            Event is null ||
            string.IsNullOrWhiteSpace(SessionId) ||
            TimestampUtc is null)
        {
            throw new InvalidDataException("The IPC event is missing required fields.");
        }

        if (Event is CodexHookEventName.UserPromptSubmit
                or CodexHookEventName.Stop
                or CodexHookEventName.Interrupt &&
            string.IsNullOrWhiteSpace(TurnId))
        {
            throw new InvalidDataException("The IPC event requires a turn ID.");
        }

        return new CodexHookEvent(Event.Value, SessionId, TurnId, Source, TimestampUtc.Value);
    }

    public void Validate()
    {
        ValidateProtocol();
        switch (Kind)
        {
            case IpcMessageKind.Event:
                _ = ToCodexHookEvent();
                break;
            case IpcMessageKind.SetState when State is null:
                throw new InvalidDataException("The set-state request requires a state.");
            case IpcMessageKind.SetState:
            case IpcMessageKind.QueryState:
            case IpcMessageKind.Shutdown:
                break;
            default:
                throw new InvalidDataException("Unsupported IPC message kind.");
        }
    }

    private void ValidateProtocol()
    {
        if (ProtocolVersion != AppConstants.ProtocolVersion)
        {
            throw new InvalidDataException($"Unsupported IPC protocol version {ProtocolVersion}.");
        }
    }
}
