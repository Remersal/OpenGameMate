namespace OpenGameMate.Core;

public static class RuntimePolicy
{
    public static readonly TimeSpan InitialAutomaticCaptureDelay = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan AutomaticCaptureInterval = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan ConversationReminderAfter = TimeSpan.FromHours(2);

    public const int ConversationReminderAfterSuccessfulImages = 60;

    public const int MaximumConcurrentSubmissions = 1;

    public const int MaximumImageWidth = 1920;

    public const int MaximumImageHeight = 1080;
}
