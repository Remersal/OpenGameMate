namespace OpenGameMate.Core;

public sealed class GameMateStateMachine
{
    private static readonly IReadOnlyDictionary<(GameMateState, GameMateTrigger), GameMateState> Transitions =
        new Dictionary<(GameMateState, GameMateTrigger), GameMateState>
        {
            [(GameMateState.Idle, GameMateTrigger.BrowserInitialized)] = GameMateState.BrowserReady,
            [(GameMateState.Idle, GameMateTrigger.Stop)] = GameMateState.Stopped,

            [(GameMateState.BrowserReady, GameMateTrigger.VoiceConfirmed)] = GameMateState.Ready,
            [(GameMateState.BrowserReady, GameMateTrigger.AdapterInvalid)] = GameMateState.AdapterError,
            [(GameMateState.BrowserReady, GameMateTrigger.BrowserClosed)] = GameMateState.Idle,
            [(GameMateState.BrowserReady, GameMateTrigger.Stop)] = GameMateState.Stopped,

            [(GameMateState.Ready, GameMateTrigger.Start)] = GameMateState.Running,
            [(GameMateState.Ready, GameMateTrigger.AdapterInvalid)] = GameMateState.AdapterError,
            [(GameMateState.Ready, GameMateTrigger.BrowserClosed)] = GameMateState.Idle,
            [(GameMateState.Ready, GameMateTrigger.Stop)] = GameMateState.Stopped,

            [(GameMateState.Running, GameMateTrigger.BeginSend)] = GameMateState.Sending,
            [(GameMateState.Running, GameMateTrigger.Pause)] = GameMateState.Paused,
            [(GameMateState.Running, GameMateTrigger.AdapterInvalid)] = GameMateState.AdapterError,
            [(GameMateState.Running, GameMateTrigger.BrowserClosed)] = GameMateState.Idle,
            [(GameMateState.Running, GameMateTrigger.Stop)] = GameMateState.Stopped,

            [(GameMateState.Sending, GameMateTrigger.SendSucceeded)] = GameMateState.Running,
            [(GameMateState.Sending, GameMateTrigger.SendFailed)] = GameMateState.Running,
            [(GameMateState.Sending, GameMateTrigger.QuotaReached)] = GameMateState.VoiceOnly,
            [(GameMateState.Sending, GameMateTrigger.AdapterInvalid)] = GameMateState.AdapterError,
            [(GameMateState.Sending, GameMateTrigger.BrowserClosed)] = GameMateState.Idle,
            [(GameMateState.Sending, GameMateTrigger.Stop)] = GameMateState.Stopped,

            [(GameMateState.Paused, GameMateTrigger.Resume)] = GameMateState.Running,
            [(GameMateState.Paused, GameMateTrigger.AdapterInvalid)] = GameMateState.AdapterError,
            [(GameMateState.Paused, GameMateTrigger.BrowserClosed)] = GameMateState.Idle,
            [(GameMateState.Paused, GameMateTrigger.Stop)] = GameMateState.Stopped,

            [(GameMateState.VoiceOnly, GameMateTrigger.BrowserClosed)] = GameMateState.Idle,
            [(GameMateState.VoiceOnly, GameMateTrigger.Stop)] = GameMateState.Stopped,

            [(GameMateState.AdapterError, GameMateTrigger.AdapterRecovered)] = GameMateState.BrowserReady,
            [(GameMateState.AdapterError, GameMateTrigger.BrowserClosed)] = GameMateState.Idle,
            [(GameMateState.AdapterError, GameMateTrigger.Stop)] = GameMateState.Stopped,

            [(GameMateState.Stopped, GameMateTrigger.Reset)] = GameMateState.Idle
        };

    private readonly object _sync = new();
    private GameMateState _state;

    public GameMateStateMachine(GameMateState initialState = GameMateState.Idle)
    {
        _state = initialState;
    }

    public event EventHandler<GameMateStateChangedEventArgs>? StateChanged;

    public GameMateState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public bool CanApply(GameMateTrigger trigger)
    {
        lock (_sync)
        {
            return Transitions.ContainsKey((_state, trigger));
        }
    }

    public StateTransition Apply(GameMateTrigger trigger)
    {
        StateTransition transition;
        lock (_sync)
        {
            if (!Transitions.TryGetValue((_state, trigger), out var nextState))
            {
                throw new InvalidStateTransitionException(_state, trigger);
            }

            transition = new StateTransition(_state, nextState, trigger);
            _state = nextState;
        }

        StateChanged?.Invoke(this, new GameMateStateChangedEventArgs(transition));
        return transition;
    }

    public bool TryApply(GameMateTrigger trigger, out StateTransition? transition)
    {
        StateTransition appliedTransition;
        lock (_sync)
        {
            if (!Transitions.TryGetValue((_state, trigger), out var nextState))
            {
                transition = null;
                return false;
            }

            appliedTransition = new StateTransition(_state, nextState, trigger);
            _state = nextState;
        }

        transition = appliedTransition;
        StateChanged?.Invoke(this, new GameMateStateChangedEventArgs(appliedTransition));
        return true;
    }
}
