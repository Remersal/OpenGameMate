namespace OpenGameMate.Core;

public enum ConversationReminderReason
{
    ElapsedTime,
    SuccessfulImageCount,
}

public sealed class ConversationReminderTracker
{
    private readonly object _sync = new();
    private DateTimeOffset _conversationStartedAt;
    private int _successfulImages;
    private bool _reminderRaised;

    public ConversationReminderTracker(DateTimeOffset conversationStartedAt)
    {
        _conversationStartedAt = conversationStartedAt;
    }

    public int SuccessfulImages
    {
        get
        {
            lock (_sync)
            {
                return _successfulImages;
            }
        }
    }

    public void RecordSuccessfulImage()
    {
        lock (_sync)
        {
            checked
            {
                _successfulImages++;
            }
        }
    }

    public bool TryRaiseReminder(
        DateTimeOffset now,
        out ConversationReminderReason? reason)
    {
        lock (_sync)
        {
            reason = null;
            if (_reminderRaised)
            {
                return false;
            }

            if (_successfulImages >= RuntimePolicy.ConversationReminderAfterSuccessfulImages)
            {
                reason = ConversationReminderReason.SuccessfulImageCount;
            }
            else if (now - _conversationStartedAt >= RuntimePolicy.ConversationReminderAfter)
            {
                reason = ConversationReminderReason.ElapsedTime;
            }

            if (reason is null)
            {
                return false;
            }

            _reminderRaised = true;
            return true;
        }
    }

    public void Reset(DateTimeOffset conversationStartedAt)
    {
        lock (_sync)
        {
            _conversationStartedAt = conversationStartedAt;
            _successfulImages = 0;
            _reminderRaised = false;
        }
    }
}
