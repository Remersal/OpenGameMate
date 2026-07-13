namespace OpenGameMate.Core;

/// <summary>
/// Emits fixed two-minute automatic occurrences. Skipped or failed occurrences
/// are not retried; the loop waits for the next periodic tick.
/// </summary>
public sealed class AutomaticSendLoop(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public TimeSpan Interval => RuntimePolicy.AutomaticCaptureInterval;

    public async Task RunAsync(
        Func<bool> shouldRun,
        Func<CancellationToken, Task> onAutomaticOccurrence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shouldRun);
        ArgumentNullException.ThrowIfNull(onAutomaticOccurrence);

        using var timer = new PeriodicTimer(Interval, _timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (!shouldRun())
            {
                continue;
            }

            await onAutomaticOccurrence(cancellationToken);
        }
    }
}
