namespace CodexTray.Tests;

public sealed class NotificationPolicyTests
{
    [Theory]
    [InlineData((int)CodexHookEventName.Stop, true)]
    [InlineData((int)CodexHookEventName.Interrupt, true)]
    [InlineData((int)CodexHookEventName.SessionStart, false)]
    [InlineData((int)CodexHookEventName.SessionEnd, false)]
    [InlineData((int)CodexHookEventName.UserPromptSubmit, false)]
    public void ShouldNotify_RequiresCompletedBusyTurn(int causeValue, bool expected)
    {
        var transition = new StateTransition(TrayState.Busy, TrayState.Ready, false);

        Assert.Equal(
            expected,
            NotificationPolicy.ShouldNotify(transition, (CodexHookEventName)causeValue));
    }

    [Theory]
    [InlineData((int)TrayState.Inactive, (int)TrayState.Ready, false)]
    [InlineData((int)TrayState.Ready, (int)TrayState.Ready, false)]
    [InlineData((int)TrayState.Busy, (int)TrayState.Inactive, false)]
    [InlineData((int)TrayState.Busy, (int)TrayState.Error, false)]
    [InlineData((int)TrayState.Busy, (int)TrayState.Ready, true)]
    public void ShouldNotify_RejectsOtherOrSyntheticTransitions(
        int previousValue,
        int currentValue,
        bool synthetic)
    {
        var transition = new StateTransition(
            (TrayState)previousValue,
            (TrayState)currentValue,
            synthetic);

        Assert.False(NotificationPolicy.ShouldNotify(transition, CodexHookEventName.Stop));
    }

    [Fact]
    public void ShouldNotify_WithoutCause_ReturnsFalse()
    {
        var transition = new StateTransition(TrayState.Busy, TrayState.Ready, false);

        Assert.False(NotificationPolicy.ShouldNotify(transition, cause: null));
    }
}
