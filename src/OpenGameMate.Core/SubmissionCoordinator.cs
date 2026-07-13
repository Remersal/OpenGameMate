namespace OpenGameMate.Core;

public enum SubmissionOrigin
{
    Automatic,
    Manual,
}

public enum SubmissionOutcome
{
    Succeeded,
    OrdinaryFailure,
    QuotaReached,
    AdapterInvalid,
}

public enum SubmissionDispatchStatus
{
    Executed,
    SkippedBusy,
    SkippedForManualPriority,
}

public sealed record SubmissionDispatchResult(
    SubmissionOrigin Origin,
    SubmissionDispatchStatus Status,
    SubmissionOutcome? Outcome = null);

/// <summary>
/// Coordinates one submission at a time without a queue. A manual request that
/// has started before an automatic occurrence causes that occurrence to be
/// skipped. Once an operation has started it is never preempted.
/// </summary>
public sealed class SubmissionCoordinator
{
    private readonly object _sync = new();
    private readonly Func<SubmissionOrigin, CancellationToken, Task<SubmissionOutcome>> _submitAsync;
    private SubmissionOrigin? _activeOrigin;
    private long _manualRequestVersion;

    public SubmissionCoordinator(
        Func<SubmissionOrigin, CancellationToken, Task<SubmissionOutcome>> submitAsync)
    {
        ArgumentNullException.ThrowIfNull(submitAsync);
        _submitAsync = submitAsync;
    }

    public SubmissionOrigin? ActiveOrigin
    {
        get
        {
            lock (_sync)
            {
                return _activeOrigin;
            }
        }
    }

    public Task<SubmissionDispatchResult> RunManualAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _manualRequestVersion);
        return RunAsync(SubmissionOrigin.Manual, null, cancellationToken);
    }

    public async Task<SubmissionDispatchResult> RunAutomaticAsync(
        CancellationToken cancellationToken = default)
    {
        var observedManualRequestVersion = Interlocked.Read(ref _manualRequestVersion);
        await Task.Yield();
        return await RunAsync(
            SubmissionOrigin.Automatic,
            observedManualRequestVersion,
            cancellationToken);
    }

    private async Task<SubmissionDispatchResult> RunAsync(
        SubmissionOrigin origin,
        long? observedManualRequestVersion,
        CancellationToken cancellationToken)
    {
        var skippedStatus = TryBegin(origin, observedManualRequestVersion);
        if (skippedStatus is not null)
        {
            return new(origin, skippedStatus.Value);
        }

        try
        {
            var outcome = await _submitAsync(origin, cancellationToken);
            return new(origin, SubmissionDispatchStatus.Executed, outcome);
        }
        finally
        {
            lock (_sync)
            {
                _activeOrigin = null;
            }
        }
    }

    private SubmissionDispatchStatus? TryBegin(
        SubmissionOrigin origin,
        long? observedManualRequestVersion)
    {
        lock (_sync)
        {
            if (origin == SubmissionOrigin.Automatic &&
                observedManualRequestVersion != Interlocked.Read(ref _manualRequestVersion))
            {
                return SubmissionDispatchStatus.SkippedForManualPriority;
            }

            if (_activeOrigin is not null)
            {
                return origin == SubmissionOrigin.Automatic &&
                       _activeOrigin == SubmissionOrigin.Manual
                    ? SubmissionDispatchStatus.SkippedForManualPriority
                    : SubmissionDispatchStatus.SkippedBusy;
            }

            _activeOrigin = origin;
            return null;
        }
    }
}
