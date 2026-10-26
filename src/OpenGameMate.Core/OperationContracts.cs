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

public enum WebAudioState
{
    Unknown,
    Silent,
    Playing,
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
    AttachmentPreparation,
}

public enum AttachmentSourceKind { Unknown, Empty, Blob, Data, Http, Other, Mixed }

public enum AttachmentUploadPhase { Unknown, Uploading, Ready, Failed, Abandoned }

public sealed record AdapterAttachmentDiagnostics(
    int BeforeCount,
    int AfterCount,
    AttachmentSourceKind BeforeSrc,
    AttachmentSourceKind AfterSrc,
    AttachmentSourceKind BeforeSrcSet,
    AttachmentSourceKind AfterSrcSet,
    AttachmentSourceKind BeforeCurrentSrc,
    AttachmentSourceKind AfterCurrentSrc,
    bool SrcChanged,
    bool SrcSetChanged,
    bool CurrentSrcChanged,
    bool? AriaBusy,
    bool ProgressBarVisible,
    bool BusyObserved,
    bool CompletionObserved,
    AttachmentUploadPhase UploadPhase);

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
    AdapterFailureStage FailureStage,
    AdapterAttachmentDiagnostics? Attachment = null);

public sealed record InputPreparationResult(
    bool FileInputSelected,
    bool AttachmentPreviewDetected,
    bool TextInserted,
    string Code,
    WebAdapterStatus Status,
    AdapterDiagnostics? Diagnostics = null)
{
    public bool ImageAdded => FileInputSelected || AttachmentPreviewDetected;
}

public sealed record AdapterIdleProbeResult(
    bool DomainCorrect,
    int ComposerCount,
    int StopButtonCount,
    int SendButtonCount,
    bool SendButtonDisabled,
    bool SendButtonInComposerForm,
    string Code,
    WebAdapterStatus Status,
    AdapterDiagnostics Diagnostics)
{
    public bool IsSafeToPrepare =>
        DomainCorrect &&
        ComposerCount == 1 &&
        StopButtonCount == 0 &&
        SendButtonCount is 0 or 1 &&
        (SendButtonCount == 0 || SendButtonInComposerForm) &&
        Diagnostics.PageState == AdapterPageState.Composer &&
        Status != WebAdapterStatus.AdapterInvalid;

    public bool IsIdle =>
        DomainCorrect &&
        ComposerCount == 1 &&
        StopButtonCount == 0 &&
        SendButtonCount == 1 &&
        !SendButtonDisabled &&
        SendButtonInComposerForm &&
        Status == WebAdapterStatus.Succeeded;
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
