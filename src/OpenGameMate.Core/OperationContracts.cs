namespace OpenGameMate.Core;

public enum ValidationStatus
{
    NotTested,
    Passed,
    Failed,
    Limited
}

public enum WebAdapterStatus
{
    Succeeded,
    NotReady,
    InvalidInput,
    AdapterInvalid,
    QuotaReached,
    PlatformError,
}

public enum AdapterPageState
{
    Unknown,
    Composer,
    ComposerWithAttachment,
    VoiceComposer,
    VoiceComposerWithAttachment,
}

public enum AdapterFailureStage
{
    None,
    Readiness,
    ComposerScope,
    ButtonDiscovery,
    ButtonValidation,
    Invocation,
    PostSubmitObservation,
    RuntimeEvaluation,
}

public sealed record AdapterButtonCandidate(
    string TagName,
    int ScopeDepth,
    string? Type,
    string? Role,
    bool Disabled,
    bool AriaDisabled,
    string? AriaLabel,
    string? DataTestId);

public sealed record AdapterDiagnostics(
    AdapterPageState PageState,
    int DetectedButtonCount,
    IReadOnlyList<AdapterButtonCandidate> CandidateButtons,
    AdapterFailureStage FailureStage);

public sealed record InputPreparationResult(
    bool FileInputSelected,
    bool AttachmentPreviewDetected,
    bool TextInserted,
    string Code,
    WebAdapterStatus Status)
{
    public bool ImageAdded => FileInputSelected || AttachmentPreviewDetected;
}

public sealed record TextPreparationResult(
    bool TextInserted,
    string Code,
    WebAdapterStatus Status);

public sealed record SubmissionResult(
    bool TriggerInvoked,
    bool ComposerCleared,
    bool AttachmentCleared,
    string Code,
    WebAdapterStatus Status,
    AdapterDiagnostics? Diagnostics = null);

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
