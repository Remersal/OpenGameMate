namespace OpenGameMate.Core;

public enum PendingSendObservation
{
    Waiting,
    Ready,
    Expired,
}

public sealed class AutomaticPendingSend
{
    public static readonly TimeSpan MaximumDeferral = TimeSpan.FromSeconds(90);

    public AutomaticPendingSend(DateTimeOffset scheduledAt, DateTimeOffset pendingCreatedAt)
        : this(scheduledAt, pendingCreatedAt, RuntimePolicy.ConversationIdleCaptureDelay)
    {
    }

    public AutomaticPendingSend(
        DateTimeOffset scheduledAt,
        DateTimeOffset pendingCreatedAt,
        TimeSpan requiredIdleStability)
    {
        if (pendingCreatedAt < scheduledAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pendingCreatedAt),
                "Pending creation cannot precede its scheduled occurrence.");
        }

        if (!RuntimePolicy.IsSupportedConversationIdleDelay(requiredIdleStability))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredIdleStability),
                "Idle stability must use a supported runtime policy value.");
        }

        ScheduledAt = scheduledAt;
        PendingCreatedAt = pendingCreatedAt;
        RequiredIdleStability = requiredIdleStability;
    }

    public DateTimeOffset ScheduledAt { get; }

    public DateTimeOffset PendingCreatedAt { get; }

    public TimeSpan RequiredIdleStability { get; }

    public TimeSpan MinimumAudioSilence => RequiredIdleStability;

    public DateTimeOffset ExpiresAt => PendingCreatedAt + MaximumDeferral;

    public DateTimeOffset? DeferredAt { get; private set; }

    public string? DeferredReason { get; private set; }

    public DateTimeOffset? IdleCandidateAt { get; private set; }

    public long IdleStableDurationMs { get; private set; }

    public bool SkippedBecauseConversationBusy { get; private set; }

    public DateTimeOffset? PendingExpiredAt { get; private set; }

    public PendingSendObservation Observe(
        DateTimeOffset observedAt,
        bool pageIdle,
        bool audioSilent,
        TimeSpan audioSilentDuration,
        string deferredReason)
    {
        if (observedAt >= ExpiresAt)
        {
            PendingExpiredAt ??= observedAt;
            SkippedBecauseConversationBusy = true;
            return PendingSendObservation.Expired;
        }

        if (!pageIdle || !audioSilent)
        {
            DeferredAt = observedAt;
            DeferredReason = deferredReason;
            IdleCandidateAt = null;
            IdleStableDurationMs = 0;
            return PendingSendObservation.Waiting;
        }

        IdleCandidateAt ??= observedAt;
        IdleStableDurationMs = Math.Max(
            0,
            (long)(observedAt - IdleCandidateAt.Value).TotalMilliseconds);
        return IdleStableDurationMs >= RequiredIdleStability.TotalMilliseconds &&
               audioSilentDuration >= MinimumAudioSilence
            ? PendingSendObservation.Ready
            : PendingSendObservation.Waiting;
    }
}

public sealed class AutomaticPendingSendSlot
{
    private readonly object _sync = new();
    private AutomaticPendingSend? _pending;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _pending is null ? 0 : 1;
            }
        }
    }

    public bool TryCreate(
        DateTimeOffset scheduledAt,
        DateTimeOffset pendingCreatedAt,
        out AutomaticPendingSend? pending,
        TimeSpan? requiredIdleStability = null)
    {
        lock (_sync)
        {
            if (_pending is not null)
            {
                pending = null;
                return false;
            }

            pending = new AutomaticPendingSend(
                scheduledAt,
                pendingCreatedAt,
                requiredIdleStability ?? RuntimePolicy.ConversationIdleCaptureDelay);
            _pending = pending;
            return true;
        }
    }

    public void Release(AutomaticPendingSend pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        lock (_sync)
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
            }
        }
    }
}
