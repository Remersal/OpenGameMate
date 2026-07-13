using OpenGameMate.Core;

namespace OpenGameMate.Diagnostics;

public enum DiagnosticLevel
{
    Debug,
    Information,
    Warning,
    Error
}

public sealed record DiagnosticEvent(
    DateTimeOffset TimestampUtc,
    DiagnosticLevel Level,
    string EventName,
    GameMateState? State = null,
    string? ErrorCode = null,
    bool? Success = null,
    int? ImageWidth = null,
    int? ImageHeight = null,
    long? FileSizeBytes = null,
    string? ExceptionType = null)
{
    public void Validate()
    {
        ValidateToken(EventName, nameof(EventName), 80);
        ValidateOptionalToken(ErrorCode, nameof(ErrorCode), 80);
        ValidateOptionalToken(ExceptionType, nameof(ExceptionType), 160);

        if ((ImageWidth.HasValue || ImageHeight.HasValue) &&
            (!ImageWidth.HasValue || !ImageHeight.HasValue || ImageWidth <= 0 || ImageHeight <= 0))
        {
            throw new DiagnosticEventValidationException(
                "Image width and height must both be positive when either is present.");
        }

        if (FileSizeBytes < 0)
        {
            throw new DiagnosticEventValidationException("File size cannot be negative.");
        }
    }

    private static void ValidateOptionalToken(string? value, string name, int maximumLength)
    {
        if (value is not null)
        {
            ValidateToken(value, name, maximumLength);
        }
    }

    private static void ValidateToken(string value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            throw new DiagnosticEventValidationException(
                $"{name} must be a non-empty ASCII token no longer than {maximumLength} characters.");
        }
    }
}

public sealed class DiagnosticEventValidationException(string message) : Exception(message);
