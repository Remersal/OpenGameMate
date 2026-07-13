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

    public bool CheckRemoteAdapterRules { get; init; } = true;

    public bool ShowPrivacyWarningOnFirstStart { get; init; } = true;

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
