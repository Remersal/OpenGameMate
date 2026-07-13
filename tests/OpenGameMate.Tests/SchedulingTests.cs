using OpenGameMate.Core;

namespace OpenGameMate.Tests;

public sealed class SchedulingTests
{
    [Fact]
    public async Task ManualSubmissionInProgress_CausesAutomaticOccurrenceToBeSkipped()
    {
        var manualStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseManual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var coordinator = new SubmissionCoordinator(async (origin, _) =>
        {
            Interlocked.Increment(ref invocationCount);
            if (origin == SubmissionOrigin.Manual)
            {
                manualStarted.SetResult();
                await releaseManual.Task;
            }

            return SubmissionOutcome.Succeeded;
        });

        var manualTask = coordinator.RunManualAsync();
        await manualStarted.Task;

        var automatic = await coordinator.RunAutomaticAsync();
        releaseManual.SetResult();
        var manual = await manualTask;

        Assert.Equal(SubmissionDispatchStatus.SkippedForManualPriority, automatic.Status);
        Assert.Equal(SubmissionDispatchStatus.Executed, manual.Status);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task ManualRequestRacingWithAutomaticOccurrence_WinsBeforeAutomaticStarts()
    {
        var invocations = new List<SubmissionOrigin>();
        var coordinator = new SubmissionCoordinator((origin, _) =>
        {
            lock (invocations)
            {
                invocations.Add(origin);
            }

            return Task.FromResult(SubmissionOutcome.Succeeded);
        });

        var automaticTask = coordinator.RunAutomaticAsync();
        var manual = await coordinator.RunManualAsync();
        var automatic = await automaticTask;

        Assert.Equal(SubmissionDispatchStatus.Executed, manual.Status);
        Assert.Equal(SubmissionDispatchStatus.SkippedForManualPriority, automatic.Status);
        Assert.Equal([SubmissionOrigin.Manual], invocations);
    }

    [Fact]
    public async Task BusyCoordinator_DoesNotQueueAnotherManualSubmission()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var coordinator = new SubmissionCoordinator(async (_, _) =>
        {
            Interlocked.Increment(ref invocationCount);
            firstStarted.SetResult();
            await releaseFirst.Task;
            return SubmissionOutcome.OrdinaryFailure;
        });

        var firstTask = coordinator.RunManualAsync();
        await firstStarted.Task;

        var second = await coordinator.RunManualAsync();
        releaseFirst.SetResult();
        var first = await firstTask;

        Assert.Equal(SubmissionDispatchStatus.SkippedBusy, second.Status);
        Assert.Equal(SubmissionOutcome.OrdinaryFailure, first.Outcome);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task OrdinaryFailure_IsReturnedWithoutAnImmediateRetry()
    {
        var invocationCount = 0;
        var coordinator = new SubmissionCoordinator((_, _) =>
        {
            Interlocked.Increment(ref invocationCount);
            return Task.FromResult(SubmissionOutcome.OrdinaryFailure);
        });

        var result = await coordinator.RunAutomaticAsync();

        Assert.Equal(SubmissionDispatchStatus.Executed, result.Status);
        Assert.Equal(SubmissionOutcome.OrdinaryFailure, result.Outcome);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void AutomaticLoop_UsesTheFixedRuntimeInterval()
    {
        var loop = new AutomaticSendLoop();

        Assert.Equal(TimeSpan.FromMinutes(2), loop.Interval);
    }

    [Fact]
    public void ConversationReminder_RaisesOnceAtSixtySuccessfulImages()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-14T10:00:00+08:00");
        var tracker = new ConversationReminderTracker(startedAt);
        for (var index = 0; index < 60; index++)
        {
            tracker.RecordSuccessfulImage();
        }

        Assert.True(tracker.TryRaiseReminder(startedAt.AddMinutes(30), out var reason));
        Assert.Equal(ConversationReminderReason.SuccessfulImageCount, reason);
        Assert.False(tracker.TryRaiseReminder(startedAt.AddHours(3), out _));
    }

    [Fact]
    public void ConversationReminder_RaisesAtTwoHoursAndCanBeReset()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-14T10:00:00+08:00");
        var tracker = new ConversationReminderTracker(startedAt);

        Assert.False(tracker.TryRaiseReminder(startedAt.AddHours(2).AddTicks(-1), out _));
        Assert.True(tracker.TryRaiseReminder(startedAt.AddHours(2), out var reason));
        Assert.Equal(ConversationReminderReason.ElapsedTime, reason);

        var restartedAt = startedAt.AddHours(3);
        tracker.Reset(restartedAt);

        Assert.Equal(0, tracker.SuccessfulImages);
        Assert.False(tracker.TryRaiseReminder(restartedAt.AddMinutes(1), out _));
    }
}
