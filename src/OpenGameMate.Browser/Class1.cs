using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace OpenGameMate.Browser;

public sealed class ChatGptBrowserSession
{
    private readonly WebView2 _webView;
    private readonly string _userDataFolder;
    private readonly Func<Uri, Task<bool>> _microphonePermissionPrompt;
    private readonly string? _browserExecutableFolder;

    public ChatGptBrowserSession(
        WebView2 webView,
        string userDataFolder,
        Func<Uri, Task<bool>> microphonePermissionPrompt,
        string? browserExecutableFolder = null)
    {
        _webView = webView;
        _userDataFolder = userDataFolder;
        _microphonePermissionPrompt = microphonePermissionPrompt;
        _browserExecutableFolder = browserExecutableFolder;
    }

    public event EventHandler<string>? StatusChanged;

    public bool IsInitialized => _webView.CoreWebView2 is not null;

    public CoreWebView2 Core => _webView.CoreWebView2
        ?? throw new InvalidOperationException("WebView2 has not been initialized.");

    public async Task InitializeAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        Directory.CreateDirectory(_userDataFolder);
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: _browserExecutableFolder,
            userDataFolder: _userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);

        Core.Settings.AreDevToolsEnabled = false;
        Core.Settings.AreDefaultScriptDialogsEnabled = true;
        Core.Settings.IsStatusBarEnabled = false;
        Core.NavigationStarting += OnNavigationStarting;
        Core.NavigationCompleted += OnNavigationCompleted;
        Core.NewWindowRequested += OnNewWindowRequested;
        Core.PermissionRequested += OnPermissionRequested;
        Core.ProcessFailed += (_, args) =>
            RaiseStatus($"WebView2 process failure: {args.ProcessFailedKind}");

        Core.Navigate("https://chatgpt.com/");
        RaiseStatus($"WebView2 {Core.Environment.BrowserVersionString} initialized with an isolated user data folder.");
        RaiseStatus("Phase 0 HTTPS navigation diagnostic mode is active; host restrictions are disabled.");
    }

    public static string? FindFixedRuntimeFolder(string runtimeRoot)
    {
        if (!Directory.Exists(runtimeRoot))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(
                runtimeRoot,
                "Microsoft.WebView2.FixedVersionRuntime.*.x64",
                SearchOption.TopDirectoryOnly)
            .Where(folder => File.Exists(Path.Combine(folder, "msedgewebview2.exe")))
            .OrderByDescending(folder => folder, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static bool IsOfficialOpenAiUri(Uri? uri)
    {
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return IsHostOrSubdomain(uri.Host, "chatgpt.com") ||
               IsHostOrSubdomain(uri.Host, "openai.com");
    }

    public static bool IsAllowedPhase0NavigationUri(Uri? uri) =>
        uri is not null && uri.Scheme == Uri.UriSchemeHttps;

    public bool IsOnChatGpt()
    {
        return Uri.TryCreate(Core.Source, UriKind.Absolute, out var uri) &&
               IsHostOrSubdomain(uri.Host, "chatgpt.com");
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
        {
            args.Cancel = true;
            RaiseStatus("Blocked navigation because the target was not an absolute URI.");
            return;
        }

        if (IsAllowedPhase0NavigationUri(uri))
        {
            RaiseStatus($"Allowed HTTPS navigation host={uri.Host}; diagnostic mode does not restrict hosts.");
            return;
        }

        args.Cancel = true;
        RaiseStatus($"Blocked non-HTTPS navigation host={uri.Host}; scheme={uri.Scheme}. No path or query was recorded.");
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            RaiseStatus($"Navigation failed: {args.WebErrorStatus}");
            return;
        }

        if (Uri.TryCreate(Core.Source, UriKind.Absolute, out var uri))
        {
            RaiseStatus($"HTTPS page navigation completed; host={uri.Host}.");
            return;
        }

        RaiseStatus("HTTPS page navigation completed.");
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && IsAllowedPhase0NavigationUri(uri))
        {
            Core.Navigate(uri.AbsoluteUri);
            RaiseStatus($"Opened HTTPS new-window target in the isolated WebView2 session; host={uri.Host}.");
            return;
        }

        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var blockedUri))
        {
            RaiseStatus($"Blocked new-window host={blockedUri.Host}; scheme={blockedUri.Scheme}. No path or query was recorded.");
            return;
        }

        RaiseStatus("Blocked a new window because the target was not an absolute URI.");
    }

    private async void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            args.Handled = true;
            if (args.PermissionKind != CoreWebView2PermissionKind.Microphone ||
                !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
                !IsOfficialOpenAiUri(uri))
            {
                args.State = CoreWebView2PermissionState.Deny;
                RaiseStatus($"Denied non-microphone or non-official permission: {args.PermissionKind}.");
                return;
            }

            var allowed = await _microphonePermissionPrompt(uri);
            args.State = allowed
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
            RaiseStatus(allowed
                ? "The user allowed microphone access for the official ChatGPT page."
                : "The user denied microphone access.");
        }
        catch (Exception exception)
        {
            args.State = CoreWebView2PermissionState.Deny;
            RaiseStatus($"Permission handling failed: {exception.GetType().Name}.");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static bool IsHostOrSubdomain(string host, string expectedHost) =>
        host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{expectedHost}", StringComparison.OrdinalIgnoreCase);

    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);
}
