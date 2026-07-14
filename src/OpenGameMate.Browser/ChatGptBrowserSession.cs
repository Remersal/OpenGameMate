using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace OpenGameMate.Browser;

public sealed class BrowserAudioStateChangedEventArgs(bool isPlaying, DateTimeOffset observedAt) : EventArgs
{
    public bool IsPlaying { get; } = isPlaying;

    public DateTimeOffset ObservedAt { get; } = observedAt;
}

public sealed record BrowserAudioSnapshot(
    bool IsKnown,
    bool IsPlaying,
    long Version,
    DateTimeOffset StateChangedAt,
    TimeSpan SilentDuration);

public sealed class ChatGptBrowserSession : IDisposable
{
    private readonly WebView2 _webView;
    private readonly string _userDataFolder;
    private readonly Func<Uri, Task<bool>> _microphonePermissionPrompt;
    private readonly string? _browserExecutableFolder;
    private readonly BrowserNavigationPolicy _navigationPolicy;
    private readonly object _audioSync = new();
    private bool _eventHandlersAttached;
    private bool _disposed;
    private bool _audioStateKnown;
    private bool _audioPlaying;
    private long _audioStateVersion;
    private DateTimeOffset _audioStateChangedAt;

    public ChatGptBrowserSession(
        WebView2 webView,
        string userDataFolder,
        Func<Uri, Task<bool>> microphonePermissionPrompt,
        string? browserExecutableFolder = null,
        BrowserNavigationPolicy? navigationPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataFolder);
        ArgumentNullException.ThrowIfNull(microphonePermissionPrompt);

        _webView = webView;
        _userDataFolder = Path.GetFullPath(userDataFolder);
        _microphonePermissionPrompt = microphonePermissionPrompt;
        _browserExecutableFolder = browserExecutableFolder;
        _navigationPolicy = navigationPolicy ?? new BrowserNavigationPolicy();
    }

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<CoreWebView2ProcessFailedEventArgs>? ProcessFailed;

    public event EventHandler<BrowserAudioStateChangedEventArgs>? AudioStateChanged;

    public bool IsInitialized => _webView.CoreWebView2 is not null;

    public bool IsDocumentPlayingAudio => IsInitialized && Core.IsDocumentPlayingAudio;

    public CoreWebView2 Core => _webView.CoreWebView2
        ?? throw new InvalidOperationException("WebView2 has not been initialized.");

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
        Core.ProcessFailed += OnProcessFailed;
        Core.IsDocumentPlayingAudioChanged += OnIsDocumentPlayingAudioChanged;
        _eventHandlersAttached = true;
        UpdateAudioState(Core.IsDocumentPlayingAudio, DateTimeOffset.UtcNow);

        Core.Navigate("https://chatgpt.com/");
        RaiseStatus($"WebView2 {Core.Environment.BrowserVersionString} initialized with an isolated user data folder.");
        RaiseStatus("Formal fail-closed top-level navigation policy is active.");
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
        return uri is { IsAbsoluteUri: true, IsDefaultPort: true } &&
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               BrowserNavigationPolicy.IsOpenAiHost(uri.Host);
    }

    public static bool IsAllowedPhase0NavigationUri(Uri? uri) =>
        uri is not null && uri.Scheme == Uri.UriSchemeHttps;

    public bool IsOnChatGpt()
    {
        return Uri.TryCreate(Core.Source, UriKind.Absolute, out var uri) &&
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.IsDefaultPort &&
               (uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase));
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var decision = _navigationPolicy.Evaluate(args.Uri);
        if (decision.IsAllowed)
        {
            RaiseStatus($"Allowed top-level navigation host={decision.Host}; policy={decision.Code}.");
            return;
        }

        args.Cancel = true;
        RaiseStatus(decision.Host is null
            ? $"Blocked top-level navigation; policy={decision.Code}."
            : $"Blocked top-level navigation host={decision.Host}; policy={decision.Code}.");
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
            RaiseStatus($"Page navigation completed; host={uri.Host}.");
            return;
        }

        RaiseStatus("Page navigation completed.");
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        var decision = _navigationPolicy.Evaluate(args.Uri);
        if (decision.IsAllowed)
        {
            Core.Navigate(args.Uri);
            RaiseStatus($"Opened an allowed new-window target in the isolated session; host={decision.Host}.");
            return;
        }

        RaiseStatus(decision.Host is null
            ? $"Blocked a new-window target; policy={decision.Code}."
            : $"Blocked new-window host={decision.Host}; policy={decision.Code}.");
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

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args)
    {
        RaiseStatus($"WebView2 process failure: {args.ProcessFailedKind}");
        ProcessFailed?.Invoke(this, args);
    }

    private void OnIsDocumentPlayingAudioChanged(object? sender, object args) =>
        UpdateAudioState(Core.IsDocumentPlayingAudio, DateTimeOffset.UtcNow, raiseEvent: true);

    public BrowserAudioSnapshot GetAudioSnapshot(DateTimeOffset observedAt)
    {
        lock (_audioSync)
        {
            return new(
                _audioStateKnown,
                _audioPlaying,
                _audioStateVersion,
                _audioStateChangedAt,
                _audioStateKnown && !_audioPlaying && observedAt >= _audioStateChangedAt
                    ? observedAt - _audioStateChangedAt
                    : TimeSpan.Zero);
        }
    }

    private void UpdateAudioState(
        bool isPlaying,
        DateTimeOffset observedAt,
        bool raiseEvent = false)
    {
        lock (_audioSync)
        {
            if (!_audioStateKnown || _audioPlaying != isPlaying)
            {
                _audioStateKnown = true;
                _audioPlaying = isPlaying;
                _audioStateChangedAt = observedAt;
                _audioStateVersion++;
            }
        }

        if (raiseEvent)
        {
            AudioStateChanged?.Invoke(
                this,
                new BrowserAudioStateChangedEventArgs(isPlaying, observedAt));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_eventHandlersAttached && _webView.CoreWebView2 is not null)
        {
            Core.NavigationStarting -= OnNavigationStarting;
            Core.NavigationCompleted -= OnNavigationCompleted;
            Core.NewWindowRequested -= OnNewWindowRequested;
            Core.PermissionRequested -= OnPermissionRequested;
            Core.ProcessFailed -= OnProcessFailed;
            Core.IsDocumentPlayingAudioChanged -= OnIsDocumentPlayingAudioChanged;
            _eventHandlersAttached = false;
        }

        StatusChanged = null;
        ProcessFailed = null;
        AudioStateChanged = null;
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);
}
