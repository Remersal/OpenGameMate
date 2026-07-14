namespace OpenGameMate.Core;

public static class RuntimePolicy
{
    public static readonly TimeSpan ConversationIdleCaptureDelay = TimeSpan.FromSeconds(10);

    public static readonly IReadOnlyList<int> SupportedConversationIdleDelaySeconds =
        Array.AsReadOnly([10, 15, 30, 60]);

    public static readonly TimeSpan ConversationIdlePollInterval = TimeSpan.FromMilliseconds(250);

    public static readonly TimeSpan ConversationReminderAfter = TimeSpan.FromHours(2);

    public const int ConversationReminderAfterSuccessfulImages = 60;

    public const int MaximumConcurrentSubmissions = 1;

    public const int MaximumImageWidth = 1920;

    public const int MaximumImageHeight = 1080;

    public static bool IsSupportedConversationIdleDelay(TimeSpan delay) =>
        SupportedConversationIdleDelaySeconds.Any(seconds => delay == TimeSpan.FromSeconds(seconds));
}
