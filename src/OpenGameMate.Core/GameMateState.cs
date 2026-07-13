namespace OpenGameMate.Core;

public enum GameMateState
{
    Idle,
    BrowserReady,
    Ready,
    Running,
    Sending,
    Paused,
    VoiceOnly,
    AdapterError,
    Stopped
}

public enum GameMateTrigger
{
    BrowserInitialized,
    VoiceConfirmed,
    Start,
    BeginSend,
    SendSucceeded,
    SendFailed,
    Pause,
    Resume,
    QuotaReached,
    AdapterInvalid,
    AdapterRecovered,
    BrowserClosed,
    Stop,
    Reset
}

public sealed record StateTransition(
    GameMateState PreviousState,
    GameMateState CurrentState,
    GameMateTrigger Trigger);

public sealed class GameMateStateChangedEventArgs(StateTransition transition) : EventArgs
{
    public StateTransition Transition { get; } = transition;
}

public sealed class InvalidStateTransitionException(
    GameMateState state,
    GameMateTrigger trigger) : InvalidOperationException(
        $"Trigger '{trigger}' is not valid while OpenGameMate is in state '{state}'.")
{
    public GameMateState State { get; } = state;

    public GameMateTrigger Trigger { get; } = trigger;
}
