namespace CodexTray.Tests;

public sealed class SessionStateStoreTests
{
    [Fact]
    public void NewStore_IsInactive()
    {
        Assert.Equal(TrayState.Inactive, new SessionStateStore().Current);
    }

    [Fact]
    public void SessionStart_MarksSessionReady()
    {
        var store = new SessionStateStore();

        StateTransition result = store.Apply(Event(CodexHookEventName.SessionStart, "s", source: "startup"));

        Assert.Equal(new StateTransition(TrayState.Inactive, TrayState.Ready, false), result);
    }

    [Fact]
    public void CompactDuringTurn_PreservesBusyState()
    {
        var store = new SessionStateStore();
        store.Apply(Event(CodexHookEventName.UserPromptSubmit, "s", "t"));

        StateTransition result = store.Apply(Event(CodexHookEventName.SessionStart, "s", source: "compact"));

        Assert.Equal(TrayState.Busy, result.Current);
    }

    [Fact]
    public void PromptWithoutSessionStart_CreatesBusySession()
    {
        var store = new SessionStateStore();

        StateTransition result = store.Apply(Event(CodexHookEventName.UserPromptSubmit, "s", "t"));

        Assert.Equal(TrayState.Busy, result.Current);
    }

    [Theory]
    [InlineData((int)CodexHookEventName.Stop)]
    [InlineData((int)CodexHookEventName.Interrupt)]
    public void TerminalEventForCurrentTurn_MarksSessionReady(int eventName)
    {
        var store = new SessionStateStore();
        store.Apply(Event(CodexHookEventName.UserPromptSubmit, "s", "t"));

        StateTransition result = store.Apply(Event((CodexHookEventName)eventName, "s", "t"));

        Assert.Equal(new StateTransition(TrayState.Busy, TrayState.Ready, false), result);
    }

    [Fact]
    public void SessionEnd_RemovesSession()
    {
        var store = new SessionStateStore();
        store.Apply(Event(CodexHookEventName.SessionStart, "s", source: "resume"));

        StateTransition result = store.Apply(Event(CodexHookEventName.SessionEnd, "s"));

        Assert.Equal(TrayState.Inactive, result.Current);
    }

    [Fact]
    public void AggregateState_RemainsBusyUntilEveryBusySessionCompletes()
    {
        var store = new SessionStateStore();
        store.Apply(Event(CodexHookEventName.UserPromptSubmit, "a", "a1"));
        store.Apply(Event(CodexHookEventName.UserPromptSubmit, "b", "b1"));
        store.Apply(Event(CodexHookEventName.Stop, "a", "a1"));

        StateTransition result = store.Apply(Event(CodexHookEventName.Stop, "b", "b1"));

        Assert.Equal(TrayState.Ready, result.Current);
    }

    [Fact]
    public void TerminalBeforePrompt_PreventsLatePromptFromReturningToBusy()
    {
        var store = new SessionStateStore();
        store.Apply(Event(CodexHookEventName.Stop, "s", "t"));

        StateTransition result = store.Apply(Event(CodexHookEventName.UserPromptSubmit, "s", "t"));

        Assert.Equal(TrayState.Ready, result.Current);
    }

    [Fact]
    public void StopForOlderTurn_DoesNotClearNewBusyTurn()
    {
        var store = new SessionStateStore();
        store.Apply(Event(CodexHookEventName.UserPromptSubmit, "s", "t1"));
        store.Apply(Event(CodexHookEventName.UserPromptSubmit, "s", "t2"));

        StateTransition result = store.Apply(Event(CodexHookEventName.Stop, "s", "t1"));

        Assert.Equal(TrayState.Busy, result.Current);
    }

    [Fact]
    public void ValidEvent_ClearsKnownError()
    {
        var store = new SessionStateStore();
        store.SetError();

        StateTransition result = store.Apply(Event(CodexHookEventName.SessionStart, "s", source: "startup"));

        Assert.Equal(TrayState.Ready, result.Current);
    }

    [Fact]
    public void SyntheticState_IsMarkedSynthetic()
    {
        var store = new SessionStateStore();

        StateTransition result = store.SetSynthetic(TrayState.Busy);

        Assert.Equal(new StateTransition(TrayState.Inactive, TrayState.Busy, true), result);
    }

    [Fact]
    public void CompletedTurnRetention_DropsOldestAfterThirtyTwoTurns()
    {
        var store = new SessionStateStore();
        for (int index = 0; index < 33; index++)
        {
            store.Apply(Event(CodexHookEventName.Stop, "s", $"t{index}"));
        }

        StateTransition result = store.Apply(Event(CodexHookEventName.UserPromptSubmit, "s", "t0"));

        Assert.Equal(TrayState.Busy, result.Current);
    }

    [Fact]
    public void RepeatedEvent_DoesNotReportAStateChange()
    {
        var store = new SessionStateStore();
        store.Apply(Event(CodexHookEventName.SessionStart, "s", source: "startup"));

        StateTransition result = store.Apply(Event(CodexHookEventName.SessionStart, "s", source: "resume"));

        Assert.False(result.Changed);
        Assert.Equal(TrayState.Ready, result.Current);
    }

    private static CodexHookEvent Event(
        CodexHookEventName name,
        string sessionId,
        string? turnId = null,
        string? source = null)
    {
        return new CodexHookEvent(name, sessionId, turnId, source, DateTimeOffset.UnixEpoch);
    }
}
