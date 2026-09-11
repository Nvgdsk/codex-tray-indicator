namespace CodexTray;

internal sealed record StateTransition(
    TrayState Previous,
    TrayState Current,
    bool IsSynthetic)
{
    public bool Changed => Previous != Current;
}
