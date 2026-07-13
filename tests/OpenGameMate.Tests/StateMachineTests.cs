using OpenGameMate.Core;

namespace OpenGameMate.Tests;

public sealed class StateMachineTests
{
    [Fact]
    public void HappyPath_UsesDocumentedStatesAndTransitions()
    {
        var machine = new GameMateStateMachine();

        Assert.Equal(GameMateState.BrowserReady, machine.Apply(GameMateTrigger.BrowserInitialized).CurrentState);
        Assert.Equal(GameMateState.Ready, machine.Apply(GameMateTrigger.VoiceConfirmed).CurrentState);
        Assert.Equal(GameMateState.Running, machine.Apply(GameMateTrigger.Start).CurrentState);
        Assert.Equal(GameMateState.Sending, machine.Apply(GameMateTrigger.BeginSend).CurrentState);
        Assert.Equal(GameMateState.Running, machine.Apply(GameMateTrigger.SendSucceeded).CurrentState);
        Assert.Equal(GameMateState.Paused, machine.Apply(GameMateTrigger.Pause).CurrentState);
        Assert.Equal(GameMateState.Running, machine.Apply(GameMateTrigger.Resume).CurrentState);
        Assert.Equal(GameMateState.Sending, machine.Apply(GameMateTrigger.BeginSend).CurrentState);
        Assert.Equal(GameMateState.VoiceOnly, machine.Apply(GameMateTrigger.QuotaReached).CurrentState);
        Assert.Equal(GameMateState.Stopped, machine.Apply(GameMateTrigger.Stop).CurrentState);
        Assert.Equal(GameMateState.Idle, machine.Apply(GameMateTrigger.Reset).CurrentState);
    }

    [Fact]
    public void InvalidTransition_FailsClosedWithoutChangingState()
    {
        var machine = new GameMateStateMachine();

        var exception = Assert.Throws<InvalidStateTransitionException>(
            () => machine.Apply(GameMateTrigger.BeginSend));

        Assert.Equal(GameMateState.Idle, exception.State);
        Assert.Equal(GameMateTrigger.BeginSend, exception.Trigger);
        Assert.Equal(GameMateState.Idle, machine.State);
    }

    [Fact]
    public void AdapterFailure_RequiresExplicitRecovery()
    {
        var machine = new GameMateStateMachine(GameMateState.Running);

        Assert.True(machine.TryApply(GameMateTrigger.AdapterInvalid, out var failed));
        Assert.Equal(GameMateState.AdapterError, failed?.CurrentState);
        Assert.False(machine.TryApply(GameMateTrigger.Resume, out _));
        Assert.Equal(GameMateState.BrowserReady, machine.Apply(GameMateTrigger.AdapterRecovered).CurrentState);
    }

    [Fact]
    public void RuntimePolicy_IsFixedForVersionZeroPointOne()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), RuntimePolicy.AutomaticCaptureInterval);
        Assert.Equal(TimeSpan.FromHours(2), RuntimePolicy.ConversationReminderAfter);
        Assert.Equal(60, RuntimePolicy.ConversationReminderAfterSuccessfulImages);
        Assert.Equal(1, RuntimePolicy.MaximumConcurrentSubmissions);
        Assert.Equal(1920, RuntimePolicy.MaximumImageWidth);
        Assert.Equal(1080, RuntimePolicy.MaximumImageHeight);
    }
}
