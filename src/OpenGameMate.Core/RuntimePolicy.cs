namespace OpenGameMate.Core;

public static class RuntimePolicy
{
    public static readonly TimeSpan ConversationIdleCaptureDelay = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan ConversationIdlePollInterval = TimeSpan.FromMilliseconds(250);

    public static readonly TimeSpan ConversationReminderAfter = TimeSpan.FromHours(2);

    public const int ConversationReminderAfterSuccessfulImages = 60;

    public const int MaximumConcurrentSubmissions = 1;

    public const int MaximumImageWidth = 1920;

    public const int MaximumImageHeight = 1080;
}
