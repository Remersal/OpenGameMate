namespace OpenGameMate.Core;

/// <summary>
/// Opens one automatic-send window when the observable ChatGPT page first
/// becomes idle. The window remains latched until page or WebView audio
/// activity resumes, so a continuous idle period cannot create a backlog.
/// The pending-send gate owns the ten-second stability delay and performs no
/// capture or composer mutation before that delay has elapsed.
/// </summary>
public sealed class AutomaticSendLoop(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public TimeSpan PollInterval => RuntimePolicy.ConversationIdlePollInterval;

    public async Task RunAsync(
        Func<bool> shouldRun,
        Func<CancellationToken, Task<bool>> isConversationIdle,
        Func<DateTimeOffset, CancellationToken, Task> onIdleWindowOpened,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shouldRun);
        ArgumentNullException.ThrowIfNull(isConversationIdle);
        ArgumentNullException.ThrowIfNull(onIdleWindowOpened);

        var armed = true;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!shouldRun())
            {
                armed = true;
                await Task.Delay(PollInterval, _timeProvider, cancellationToken);
                continue;
            }

            var idle = await isConversationIdle(cancellationToken);
            if (!idle)
            {
                armed = true;
            }
            else if (armed)
            {
                armed = false;
                await onIdleWindowOpened(_timeProvider.GetUtcNow(), cancellationToken);
            }

            await Task.Delay(PollInterval, _timeProvider, cancellationToken);
        }
    }
}
