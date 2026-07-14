using System.Text.Json.Serialization;

namespace OpenGameMate.Configuration;

public enum AppLanguage
{
    System,
    ChineseSimplified,
    English
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpenGameMateSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public AppLanguage Language { get; init; } = AppLanguage.System;

    // Retained in schema v1 for compatibility. Runtime checks remain fail-closed until
    // an official source and maintainer-owned verification key are configured.
    public bool CheckRemoteAdapterRules { get; init; } = true;

    public bool ShowPrivacyWarningOnFirstStart { get; init; } = true;

    public bool RolePromptSent { get; init; }

    public string ManualCaptureHotKey { get; init; } =
        global::OpenGameMate.Configuration.ManualCaptureHotKey.DefaultGesture;

    public int ConversationIdleDelaySeconds { get; init; } = 10;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ConfigurationValidationException(
                $"Unsupported settings schema version '{SchemaVersion}'.");
        }

        if (!Enum.IsDefined(Language))
        {
            throw new ConfigurationValidationException("Unsupported application language.");
        }

        _ = global::OpenGameMate.Configuration.ManualCaptureHotKey.Parse(ManualCaptureHotKey);

        if (ConversationIdleDelaySeconds is not (10 or 15 or 30 or 60))
        {
            throw new ConfigurationValidationException(
                "Conversation idle delay must be 10, 15, 30, or 60 seconds.");
        }
    }
}

public sealed class ConfigurationValidationException : Exception
{
    public ConfigurationValidationException(string message)
        : base(message)
    {
    }

    public ConfigurationValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
