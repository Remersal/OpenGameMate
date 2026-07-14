using System;
using System.Reflection;

namespace OpenGameMate.App;

internal static class ProductMetadata
{
    private const string FallbackVersion = "unknown";

    public static string Version { get; } = ResolveVersion();

    public static string DisplayName { get; } = $"OpenGameMate v{Version}";

    private static string ResolveVersion()
    {
        var informationalVersion = typeof(ProductMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return FallbackVersion;
        }

        var metadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadataSeparator > 0
            ? informationalVersion[..metadataSeparator]
            : informationalVersion;
    }
}
