namespace OpenGameMate.Capture;

public sealed record ScreenCaptureResult(
    string Path,
    int SourceWidth,
    int SourceHeight,
    int OutputWidth,
    int OutputHeight,
    long FileBytes);

public enum CaptureFailureCode
{
    UnsupportedPlatform,
    PrimaryMonitorUnavailable,
    InvalidSourceDimensions,
    TimedOut,
    AccessDenied,
    TemporaryFileFailure,
    GraphicsDeviceFailure,
    Unknown,
}

public sealed class ScreenCaptureException : Exception
{
    public ScreenCaptureException(
        CaptureFailureCode code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public CaptureFailureCode Code { get; }
}

public interface IPrimaryDisplayCapture
{
    string TemporaryScreenshotPath { get; }

    Task<ScreenCaptureResult> CaptureAsync(CancellationToken cancellationToken = default);

    bool DeleteTemporaryScreenshot();
}
