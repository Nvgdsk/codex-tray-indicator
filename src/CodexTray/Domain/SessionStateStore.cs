namespace CodexTray;

internal sealed class SessionStateStore
{
    private const int CompletedTurnLimit = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private bool _hasError;
    private TrayState? _syntheticState;

    public TrayState Current
    {
        get
        {
            lock (_gate)
            {
                return ComputeState();
            }
        }
    }

    public StateTransition Apply(CodexHookEvent hookEvent)
    {
        ArgumentNullException.ThrowIfNull(hookEvent);

        lock (_gate)
        {
            TrayState previous = ComputeState();
            _hasError = false;
            _syntheticState = null;

            switch (hookEvent.Name)
            {
                case CodexHookEventName.SessionEnd:
                    _sessions.Remove(hookEvent.SessionId);
                    break;

                case CodexHookEventName.SessionStart when
                    string.Equals(hookEvent.Source, "compact", StringComparison.Ordinal):
                    break;

                case CodexHookEventName.SessionStart:
                    GetOrCreateSession(hookEvent.SessionId);
                    break;

                case CodexHookEventName.UserPromptSubmit:
                    ApplyPrompt(hookEvent);
                    break;

                case CodexHookEventName.Stop:
                case CodexHookEventName.Interrupt:
                    ApplyTerminalEvent(hookEvent);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(hookEvent));
            }

            return new StateTransition(previous, ComputeState(), false);
        }
    }

    public StateTransition SetSynthetic(TrayState state)
    {
        lock (_gate)
        {
            TrayState previous = ComputeState();
            _hasError = false;
            _syntheticState = state;
            return new StateTransition(previous, ComputeState(), true);
        }
    }

    public StateTransition SetError()
    {
        lock (_gate)
        {
            TrayState previous = ComputeState();
            _syntheticState = null;
            _hasError = true;
            return new StateTransition(previous, TrayState.Error, false);
        }
    }

    private void ApplyPrompt(CodexHookEvent hookEvent)
    {
        SessionRecord session = GetOrCreateSession(hookEvent.SessionId);
        string turnId = hookEvent.TurnId!;
        if (!session.CompletedTurns.Contains(turnId))
        {
            session.CurrentTurnId = turnId;
        }
    }

    private void ApplyTerminalEvent(CodexHookEvent hookEvent)
    {
        SessionRecord session = GetOrCreateSession(hookEvent.SessionId);
        string turnId = hookEvent.TurnId!;
        session.RememberCompleted(turnId);
        if (string.Equals(session.CurrentTurnId, turnId, StringComparison.Ordinal))
        {
            session.CurrentTurnId = null;
        }
    }

    private SessionRecord GetOrCreateSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out SessionRecord? session))
        {
            session = new SessionRecord();
            _sessions.Add(sessionId, session);
        }

        return session;
    }

    private TrayState ComputeState()
    {
        if (_hasError)
        {
            return TrayState.Error;
        }

        if (_syntheticState is { } syntheticState)
        {
            return syntheticState;
        }

        if (_sessions.Values.Any(session => session.CurrentTurnId is not null))
        {
            return TrayState.Busy;
        }

        return _sessions.Count > 0 ? TrayState.Ready : TrayState.Inactive;
    }

    private sealed class SessionRecord
    {
        private readonly Queue<string> _completedOrder = new();

        public string? CurrentTurnId { get; set; }

        public HashSet<string> CompletedTurns { get; } = new(StringComparer.Ordinal);

        public void RememberCompleted(string turnId)
        {
            if (!CompletedTurns.Add(turnId))
            {
                return;
            }

            _completedOrder.Enqueue(turnId);
            if (_completedOrder.Count > CompletedTurnLimit)
            {
                CompletedTurns.Remove(_completedOrder.Dequeue());
            }
        }
    }
}
