namespace CodexTray;

internal static class NotificationPolicy
{
    public static bool ShouldNotify(
        StateTransition transition,
        CodexHookEventName? cause)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return !transition.IsSynthetic &&
            transition.Previous == TrayState.Busy &&
            transition.Current == TrayState.Ready &&
            cause is CodexHookEventName.Stop or CodexHookEventName.Interrupt;
    }
}
