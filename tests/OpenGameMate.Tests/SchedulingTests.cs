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
    public void AutomaticLoop_UsesTheConversationIdlePollInterval()
    {
        var loop = new AutomaticSendLoop();

        Assert.Equal(TimeSpan.FromMilliseconds(250), loop.PollInterval);
    }

    [Fact]
    public async Task AutomaticLoop_OpensOnlyOnceDuringAContinuousIdleWindow()
    {
        var startedAt = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(startedAt);
        var loop = new AutomaticSendLoop(timeProvider);
        using var cancellation = new CancellationTokenSource();
        var occurrences = new List<DateTimeOffset>();
        var firstOccurrence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var loopTask = loop.RunAsync(
            () => true,
            _ => Task.FromResult(true),
            (scheduledAt, _) =>
            {
                occurrences.Add(scheduledAt);
                firstOccurrence.TrySetResult();
                return Task.CompletedTask;
            },
            cancellation.Token);

        await firstOccurrence.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal([startedAt], occurrences);

        await WaitForTimerAsync(timeProvider);
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        await WaitForTimerAsync(timeProvider);
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Single(occurrences);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loopTask);
    }

    [Fact]
    public async Task AutomaticLoop_RearmsOnlyAfterConversationActivityResumes()
    {
        var startedAt = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(startedAt);
        var loop = new AutomaticSendLoop(timeProvider);
        using var cancellation = new CancellationTokenSource();
        var idle = true;
        var occurrences = new List<DateTimeOffset>();
        var firstOccurrence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondOccurrence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var loopTask = loop.RunAsync(
            () => true,
            _ => Task.FromResult(idle),
            (scheduledAt, _) =>
            {
                occurrences.Add(scheduledAt);
                if (occurrences.Count == 1)
                {
                    firstOccurrence.TrySetResult();
                }
                else
                {
                    secondOccurrence.TrySetResult();
                    cancellation.Cancel();
                }

                return Task.CompletedTask;
            },
            cancellation.Token);

        await firstOccurrence.Task.WaitAsync(TimeSpan.FromSeconds(1));
        idle = false;
        await WaitForTimerAsync(timeProvider);
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Single(occurrences);

        idle = true;
        await WaitForTimerAsync(timeProvider);
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        await secondOccurrence.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal([startedAt, startedAt.AddMilliseconds(500)], occurrences);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loopTask);
    }

    [Fact]
    public async Task AutomaticLoop_DoesNotOpenAWindowWhilePaused()
    {
        var startedAt = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(startedAt);
        var loop = new AutomaticSendLoop(timeProvider);
        using var cancellation = new CancellationTokenSource();
        var running = false;
        DateTimeOffset? occurrence = null;
        var occurrenceRaised = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var loopTask = loop.RunAsync(
            () => running,
            _ => Task.FromResult(true),
            (scheduledAt, _) =>
            {
                occurrence = scheduledAt;
                occurrenceRaised.TrySetResult();
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        await WaitForTimerAsync(timeProvider);
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Null(occurrence);

        running = true;
        await WaitForTimerAsync(timeProvider);
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        await occurrenceRaised.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(startedAt.AddMilliseconds(500), occurrence);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loopTask);
    }

    private static async Task WaitForTimerAsync(ManualTimeProvider timeProvider)
    {
        for (var attempt = 0; attempt < 100 && timeProvider.ActiveTimerCount == 0; attempt++)
        {
            await Task.Yield();
        }

        Assert.Equal(1, timeProvider.ActiveTimerCount);
    }

    [Fact]
    public void PendingSlot_HasStrictCapacityOfOneAndCanBeReleased()
    {
        var slot = new AutomaticPendingSendSlot();
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00+00:00");

        Assert.True(slot.TryCreate(now, now, out var first));
        Assert.NotNull(first);
        Assert.False(slot.TryCreate(now.AddMinutes(2), now.AddMinutes(2), out var duplicate));
        Assert.Null(duplicate);
        Assert.Equal(1, slot.Count);

        slot.Release(first!);

        Assert.Equal(0, slot.Count);
        Assert.True(slot.TryCreate(now.AddMinutes(2), now.AddMinutes(2), out _));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void PendingSend_RequiresSelectedStableAndSilentDuration(int selectedSeconds)
    {
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00+00:00");
        var requiredDelay = TimeSpan.FromSeconds(selectedSeconds);
        var pending = new AutomaticPendingSend(now, now, requiredDelay);

        Assert.Equal(
            PendingSendObservation.Waiting,
            pending.Observe(now, pageIdle: true, audioSilent: true, TimeSpan.Zero, "idle-stabilizing"));
        Assert.Equal(
            PendingSendObservation.Waiting,
            pending.Observe(
                now.Add(requiredDelay).AddMilliseconds(-1),
                pageIdle: true,
                audioSilent: true,
                requiredDelay.Add(TimeSpan.FromMilliseconds(-1)),
                "idle-stabilizing"));
        Assert.Equal(
            PendingSendObservation.Waiting,
            pending.Observe(
                now.Add(requiredDelay),
                pageIdle: true,
                audioSilent: true,
                requiredDelay.Add(TimeSpan.FromMilliseconds(-1)),
                "idle-stabilizing"));
        Assert.Equal(
            PendingSendObservation.Ready,
            pending.Observe(
                now.Add(requiredDelay),
                pageIdle: true,
                audioSilent: true,
                requiredDelay,
                "idle-stabilizing"));

        Assert.Equal(requiredDelay, pending.RequiredIdleStability);
        Assert.Equal(requiredDelay, pending.MinimumAudioSilence);
    }

    [Fact]
    public void PendingSlot_SnapshotsSelectedDelayForCreatedOccurrence()
    {
        var slot = new AutomaticPendingSendSlot();
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00+00:00");

        Assert.True(slot.TryCreate(now, now, out var pending, TimeSpan.FromSeconds(30)));

        Assert.Equal(TimeSpan.FromSeconds(30), pending?.RequiredIdleStability);
    }

    [Fact]
    public void PendingSend_DoesNotTreatFiveSecondVoiceGapAsIdle()
    {
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00+00:00");
        var pending = new AutomaticPendingSend(now, now);

        pending.Observe(now, true, true, TimeSpan.Zero, "idle-stabilizing");

        Assert.Equal(
            PendingSendObservation.Waiting,
            pending.Observe(
                now.AddMilliseconds(5200),
                true,
                true,
                TimeSpan.FromMilliseconds(5200),
                "idle-stabilizing"));
        Assert.Equal(
            PendingSendObservation.Waiting,
            pending.Observe(
                now.AddMilliseconds(5210),
                true,
                false,
                TimeSpan.Zero,
                "audio-playing"));
        Assert.Null(pending.IdleCandidateAt);
    }

    [Fact]
    public void PendingSend_BusyObservationResetsIdleCandidate()
    {
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00+00:00");
        var pending = new AutomaticPendingSend(now, now);

        pending.Observe(now, true, true, TimeSpan.FromSeconds(3), "idle-stabilizing");
        pending.Observe(now.AddSeconds(2), false, true, TimeSpan.FromSeconds(5), "stop-button-present");

        Assert.Null(pending.IdleCandidateAt);
        Assert.Equal("stop-button-present", pending.DeferredReason);
        Assert.Equal(
            PendingSendObservation.Waiting,
            pending.Observe(
                now.AddSeconds(3),
                true,
                true,
                TimeSpan.FromSeconds(6),
                "idle-stabilizing"));
    }

    [Fact]
    public void PendingSend_ExpiresAfterNinetySecondsWithoutBacklog()
    {
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00+00:00");
        var pending = new AutomaticPendingSend(now, now);

        Assert.Equal(
            PendingSendObservation.Waiting,
            pending.Observe(
                now.AddSeconds(89),
                pageIdle: false,
                audioSilent: false,
                TimeSpan.Zero,
                "audio-playing"));
        Assert.Equal(
            PendingSendObservation.Expired,
            pending.Observe(
                now.AddSeconds(90),
                pageIdle: false,
                audioSilent: false,
                TimeSpan.Zero,
                "audio-playing"));
        Assert.True(pending.SkippedBecauseConversationBusy);
        Assert.Equal(now.AddSeconds(90), pending.PendingExpiredAt);
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

    private sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = initialUtcNow;

        public int ActiveTimerCount
        {
            get
            {
                lock (_sync)
                {
                    return _timers.Count(timer => timer.IsActive);
                }
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new ManualTimer(this, callback, state);
            lock (_sync)
            {
                _timers.Add(timer);
                timer.ChangeCore(dueTime, period);
            }

            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            if (amount < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            DateTimeOffset target;
            lock (_sync)
            {
                target = _utcNow + amount;
            }

            while (true)
            {
                ManualTimer? next;
                lock (_sync)
                {
                    next = _timers
                        .Where(timer => timer.DueAt is not null && timer.DueAt <= target)
                        .OrderBy(timer => timer.DueAt)
                        .FirstOrDefault();
                    if (next is null)
                    {
                        _utcNow = target;
                        return;
                    }

                    _utcNow = next.DueAt!.Value;
                    next.ScheduleNextCore();
                }

                next.Invoke();
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            private bool _disposed;

            public DateTimeOffset? DueAt { get; private set; }

            public bool IsActive => !_disposed && DueAt is not null;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._sync)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    ChangeCore(dueTime, period);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner._sync)
                {
                    _disposed = true;
                    DueAt = null;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Invoke() => callback(state);

            public void ChangeCore(TimeSpan dueTime, TimeSpan period)
            {
                ValidateTimerValue(dueTime, nameof(dueTime));
                ValidateTimerValue(period, nameof(period));
                _period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : owner._utcNow + dueTime;
            }

            public void ScheduleNextCore()
            {
                DueAt = !_disposed && _period != Timeout.InfiniteTimeSpan
                    ? owner._utcNow + _period
                    : null;
            }

            private static void ValidateTimerValue(TimeSpan value, string name)
            {
                if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(name);
                }
            }
        }
    }
}
