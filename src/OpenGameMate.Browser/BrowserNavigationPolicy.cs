namespace OpenGameMate.Browser;

public enum BrowserNavigationCode
{
    AllowedOpenAi,
    AllowedIdentityProvider,
    InvalidUri,
    InsecureScheme,
    NonDefaultPort,
    UntrustedHost,
}

public readonly record struct BrowserNavigationDecision(
    bool IsAllowed,
    BrowserNavigationCode Code,
    string? Host);

/// <summary>
/// Applies a fail-closed policy to top-level WebView2 navigations. It does not
/// inspect or log paths, queries, fragments, page content, cookies, or tokens.
/// </summary>
public sealed class BrowserNavigationPolicy
{
    private static readonly HashSet<string> IdentityProviderHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "accounts.google.com",
            "appleid.apple.com",
            "login.live.com",
            "login.microsoftonline.com",
        };

    public BrowserNavigationDecision Evaluate(string? target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return new(false, BrowserNavigationCode.InvalidUri, null);
        }

        return Evaluate(uri);
    }

    public BrowserNavigationDecision Evaluate(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return new(false, BrowserNavigationCode.InvalidUri, null);
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, BrowserNavigationCode.InsecureScheme, uri.Host);
        }

        if (!uri.IsDefaultPort)
        {
            return new(false, BrowserNavigationCode.NonDefaultPort, uri.Host);
        }

        if (IsOpenAiHost(uri.Host))
        {
            return new(true, BrowserNavigationCode.AllowedOpenAi, uri.Host);
        }

        if (IdentityProviderHosts.Contains(uri.Host))
        {
            return new(true, BrowserNavigationCode.AllowedIdentityProvider, uri.Host);
        }

        return new(false, BrowserNavigationCode.UntrustedHost, uri.Host);
    }

    public static bool IsOpenAiHost(string host) =>
        IsHostOrSubdomain(host, "chatgpt.com") ||
        IsHostOrSubdomain(host, "openai.com");

    private static bool IsHostOrSubdomain(string host, string expectedHost) =>
        host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{expectedHost}", StringComparison.OrdinalIgnoreCase);
}
