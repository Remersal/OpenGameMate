using System.Text.Json.Serialization;

namespace OpenGameMate.Adapters;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatGptAdapterRules
{
    public const int CurrentSchemaVersion = 1;
    private const int MaximumSelectorLength = 256;
    private const int MaximumPreviewSelectors = 16;

    public static ChatGptAdapterRules BuiltIn { get; } = new()
    {
        SchemaVersion = CurrentSchemaVersion,
        RulesVersion = "builtin-2026.07.14-p0-2",
        ComposerSelector = "#prompt-textarea",
        FileInputSelector = "input[type=\"file\"]",
        SendButtonSelector = "button[data-testid=\"send-button\"]",
        BusyButtonSelector = "button[data-testid=\"stop-button\"]",
        AttachmentPreviewSelectors =
        [
            "[data-testid=\"file-thumbnail\"]",
            "[data-testid=\"file-thumbnail-container\"]",
            "[data-testid=\"attachment\"]",
            "[data-testid^=\"composer-attachment\"]",
            "[data-testid*=\"attachment-preview\"]",
            "[data-testid=\"remove-file-button\"]",
            "img[src^=\"blob:\"]",
        ],
        QuotaErrorSelectors =
        [
            "[data-testid=\"usage-limit-error\"]",
            "[data-testid=\"quota-error\"]",
        ],
        PlatformErrorSelectors =
        [
            "[data-testid=\"composer-error\"]",
            "[data-testid=\"message-error\"]",
        ],
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string RulesVersion { get; init; }

    public required string ComposerSelector { get; init; }

    public required string FileInputSelector { get; init; }

    public required string SendButtonSelector { get; init; }

    public required string BusyButtonSelector { get; init; }

    public required string[] AttachmentPreviewSelectors { get; init; }

    public required string[] QuotaErrorSelectors { get; init; }

    public required string[] PlatformErrorSelectors { get; init; }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new AdapterRuleValidationException("Unsupported adapter-rule schema version.");
        }

        ValidateToken(RulesVersion, nameof(RulesVersion));
        ValidateSelector(ComposerSelector, nameof(ComposerSelector));
        ValidateSelector(FileInputSelector, nameof(FileInputSelector));
        ValidateSelector(SendButtonSelector, nameof(SendButtonSelector));
        ValidateSelector(BusyButtonSelector, nameof(BusyButtonSelector));
        RequireAnySelectorToken(ComposerSelector, nameof(ComposerSelector), "prompt", "composer");
        RequireAllSelectorTokens(FileInputSelector, nameof(FileInputSelector), "input", "file");
        RequireAllSelectorTokens(SendButtonSelector, nameof(SendButtonSelector), "button", "send");
        RequireAllSelectorTokens(BusyButtonSelector, nameof(BusyButtonSelector), "button", "stop");
        ValidateSelectorArray(AttachmentPreviewSelectors, nameof(AttachmentPreviewSelectors));
        ValidateSelectorArray(QuotaErrorSelectors, nameof(QuotaErrorSelectors));
        ValidateSelectorArray(PlatformErrorSelectors, nameof(PlatformErrorSelectors));
    }

    private static void ValidateToken(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 64 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new AdapterRuleValidationException($"{name} is not a valid rule token.");
        }
    }

    private static void ValidateSelectorArray(string[] selectors, string name)
    {
        if (selectors is null || selectors.Length > MaximumPreviewSelectors)
        {
            throw new AdapterRuleValidationException($"{name} contains too many selectors.");
        }

        foreach (var selector in selectors)
        {
            ValidateSelector(selector, name);
        }
    }

    private static void ValidateSelector(string selector, string name)
    {
        if (string.IsNullOrWhiteSpace(selector) ||
            selector.Length > MaximumSelectorLength ||
            selector.Any(char.IsControl))
        {
            throw new AdapterRuleValidationException($"{name} contains an invalid selector.");
        }
    }

    private static void RequireAnySelectorToken(
        string selector,
        string name,
        params string[] allowedTokens)
    {
        if (!allowedTokens.Any(
                token => selector.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AdapterRuleValidationException($"{name} is outside the allowed selector scope.");
        }
    }

    private static void RequireAllSelectorTokens(
        string selector,
        string name,
        params string[] requiredTokens)
    {
        if (requiredTokens.Any(
                token => !selector.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AdapterRuleValidationException($"{name} is outside the allowed selector scope.");
        }
    }
}

public sealed class AdapterRuleValidationException : Exception
{
    public AdapterRuleValidationException(string message)
        : base(message)
    {
    }

    public AdapterRuleValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
