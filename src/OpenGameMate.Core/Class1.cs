namespace OpenGameMate.Core;

public enum ValidationStatus
{
    NotTested,
    Passed,
    Failed,
    Limited
}

public sealed record InputPreparationResult(
    bool FileInputSelected,
    bool AttachmentPreviewDetected,
    bool TextInserted,
    string Code)
{
    public bool ImageAdded => FileInputSelected || AttachmentPreviewDetected;
}

public sealed record SubmissionResult(
    bool TriggerInvoked,
    bool ComposerCleared,
    bool AttachmentCleared,
    string Code);

public sealed record FocusSnapshot(
    long ForegroundWindow,
    uint ForegroundProcessId,
    int CursorX,
    int CursorY)
{
    public bool SameForegroundAndCursor(FocusSnapshot other) =>
        ForegroundWindow == other.ForegroundWindow &&
        ForegroundProcessId == other.ForegroundProcessId &&
        CursorX == other.CursorX &&
        CursorY == other.CursorY;
}
