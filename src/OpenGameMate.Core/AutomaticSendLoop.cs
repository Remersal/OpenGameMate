namespace OpenGameMate.Core;

/// <summary>
/// Emits one initial automatic occurrence after thirty seconds, then fixed
/// two-minute occurrences. Skipped or failed occurrences are not retried.
/// </summary>
public sealed class AutomaticSendLoop(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public TimeSpan InitialDelay => RuntimePolicy.InitialAutomaticCaptureDelay;

    public TimeSpan Interval => RuntimePolicy.AutomaticCaptureInterval;

    public async Task RunAsync(
        Func<bool> shouldRun,
        Func<DateTimeOffset, CancellationToken, Task> onAutomaticOccurrence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shouldRun);
        ArgumentNullException.ThrowIfNull(onAutomaticOccurrence);

        await Task.Delay(InitialDelay, _timeProvider, cancellationToken);
        if (shouldRun())
        {
            await onAutomaticOccurrence(_timeProvider.GetUtcNow(), cancellationToken);
        }

        while (true)
        {
            await Task.Delay(Interval, _timeProvider, cancellationToken);
            if (!shouldRun())
            {
                continue;
            }

            await onAutomaticOccurrence(_timeProvider.GetUtcNow(), cancellationToken);
        }
    }
}
