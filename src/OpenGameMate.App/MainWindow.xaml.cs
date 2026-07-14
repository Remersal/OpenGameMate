using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using OpenGameMate.Adapters;
using OpenGameMate.Browser;
using OpenGameMate.Capture;
using OpenGameMate.Configuration;
using OpenGameMate.Core;
using OpenGameMate.Diagnostics;

namespace OpenGameMate.App;

public partial class MainWindow : Window
{
    private static readonly TimeSpan PendingProbeInterval = TimeSpan.FromMilliseconds(250);

    private enum PendingGateResult
    {
        Ready,
        Skipped,
        AdapterInvalid,
    }

    private readonly AppDataPaths _paths;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IDiagnosticLogger _logger;
    private readonly IPrimaryDisplayCapture _capture;
    private readonly GameMateStateMachine _stateMachine = new();
    private readonly AutomaticSendLoop _automaticSendLoop = new();
    private readonly AutomaticPendingSendSlot _automaticPendingSendSlot = new();
    private readonly SubmissionCoordinator _submissionCoordinator;

    private OpenGameMateSettings _settings = new();
    private WebView2? _browserView;
    private ChatGptBrowserSession? _browserSession;
    private IAiWebAdapter? _adapter;
    private ConversationReminderTracker? _reminderTracker;
    private TrayIconController? _trayIcon;
    private GlobalHotKeyManager? _globalHotKeyManager;
    private CancellationTokenSource? _runCancellation;
    private Task? _automaticLoopTask;
    private long? _automaticReadyAudioVersion;
    private AdapterPageState? _automaticReadyPageState;
    private AutomaticPendingSend? _dispatchingAutomaticPending;
    private bool _loaded;
    private bool _isExiting;
    private bool _browserInitializing;
    private bool _auxiliaryOperation;
    private bool _capturingHotKey;

    public MainWindow(AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _settingsStore = new JsonAppSettingsStore(paths.SettingsFile);
        _logger = new JsonLineDiagnosticLogger(paths.LogsDirectory);
        _capture = new PrimaryDisplayCapture(paths.TemporaryDirectory);
        _submissionCoordinator = new SubmissionCoordinator(DispatchSubmissionAsync);
        InitializeComponent();
    }

    private bool IsChinese => _settings.Language switch
    {
        AppLanguage.ChineseSimplified => true,
        AppLanguage.English => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(
            "zh",
            StringComparison.OrdinalIgnoreCase),
    };

    private CompanionPromptLanguage PromptLanguage => IsChinese
        ? CompanionPromptLanguage.ChineseSimplified
        : CompanionPromptLanguage.English;

    private string T(string chinese, string english) => IsChinese ? chinese : english;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
        }
        catch (ConfigurationValidationException exception)
        {
            _settings = new OpenGameMateSettings();
            await SafeLogAsync(
                "settings.load-failed",
                DiagnosticLevel.Error,
                errorCode: "invalid-settings",
                exceptionType: exception.GetType().Name);
            MessageBox.Show(
                T(
                    "设置文件无效，本次将使用默认设置且不会覆盖原文件。",
                    "The settings file is invalid. Defaults will be used without overwriting it."),
                "OpenGameMate",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        SelectLanguageItem(_settings.Language);
        ApplyLocalization();
        CreateTrayIcon();
        InitializeGlobalHotKey();
        _loaded = true;
        UpdateUi();
        AddActivity(T("应用已启动；不会自动打开浏览器或捕获桌面。", "Started without opening a browser or capturing the desktop."));
        await SafeLogAsync("app.started", DiagnosticLevel.Information, success: true);
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayIconController(
            () => OnUi(ShowMainWindow),
            () => OnUi(ShowMainWindow),
            () => OnUi(Hide),
            () => OnUi(async () => await SendNowAsync()),
            () => OnUi(PauseOrResume),
            () => OnUi(StopRun),
            () => OnUi(ExitApplication));
    }

    private void InitializeGlobalHotKey()
    {
        try
        {
            _globalHotKeyManager = new GlobalHotKeyManager(this);
            _globalHotKeyManager.Pressed += GlobalHotKeyManager_Pressed;
            var hotKey = ManualCaptureHotKey.Parse(_settings.ManualCaptureHotKey);
            if (!_globalHotKeyManager.TrySet(hotKey, out var errorCode))
            {
                AddActivity(T(
                    $"主动截图快捷键 {hotKey.DisplayText} 注册失败，请更换组合。",
                    $"Could not register {hotKey.DisplayText}; choose another capture hotkey."));
                _ = SafeLogAsync(
                    "hotkey.register-failed",
                    DiagnosticLevel.Warning,
                    errorCode: $"win32-{errorCode}");
            }
        }
        catch (Exception exception)
        {
            _ = ReportExceptionAsync("hotkey.initialize-failed", "hotkey-initialize", exception);
        }
    }

    private void GlobalHotKeyManager_Pressed(object? sender, EventArgs e) =>
        OnUi(async () => await HandleManualCaptureHotKeyAsync());

    private async Task HandleManualCaptureHotKeyAsync()
    {
        if (_capturingHotKey)
        {
            return;
        }

        if (_stateMachine.State != GameMateState.Running)
        {
            AddActivity(T(
                "主动截图快捷键仅在陪玩运行时生效。",
                "The capture hotkey is active only while companion mode is running."));
            return;
        }

        await SendNowAsync();
    }

    private async void LanguageComboBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_loaded || LanguageComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item ||
            item.Tag is not string tag || !Enum.TryParse<AppLanguage>(tag, out var language))
        {
            return;
        }

        _settings = _settings with { Language = language };
        ApplyLocalization();
        UpdateUi();
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            await ReportExceptionAsync("settings.save-failed", "settings-save", exception);
        }
    }

    private void SelectLanguageItem(AppLanguage language)
    {
        foreach (var entry in LanguageComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (entry.Tag is string tag && tag.Equals(language.ToString(), StringComparison.Ordinal))
            {
                LanguageComboBox.SelectedItem = entry;
                return;
            }
        }
    }

    private void ChangeHotKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotKey = true;
        ApplyLocalization();
        HotKeyTextBox.Focus();
        Keyboard.Focus(HotKeyTextBox);
    }

    private async void HotKeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotKey)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _capturingHotKey = false;
            ApplyLocalization();
            return;
        }

        if (IsModifierKey(key))
        {
            return;
        }

        if (!TryCreateHotKey(key, Keyboard.Modifiers, out var hotKey))
        {
            AddActivity(T(
                "快捷键必须包含 Ctrl、Alt、Shift 或 Win，并使用 A-Z、0-9 或 F1-F12。",
                "Use Ctrl, Alt, Shift, or Win with an A-Z, 0-9, or F1-F12 key."));
            return;
        }

        await ChangeManualCaptureHotKeyAsync(hotKey);
    }

    private async Task ChangeManualCaptureHotKeyAsync(ManualCaptureHotKey hotKey)
    {
        if (_globalHotKeyManager is null)
        {
            AddActivity(T("全局快捷键尚未就绪。", "The global hotkey service is not ready."));
            return;
        }

        var previousSettings = _settings;
        var previousHotKey = ManualCaptureHotKey.Parse(previousSettings.ManualCaptureHotKey);
        if (!_globalHotKeyManager.TrySet(hotKey, out var errorCode))
        {
            AddActivity(T(
                $"快捷键 {hotKey.DisplayText} 已被占用或无法注册，仍使用 {previousHotKey.DisplayText}。",
                $"{hotKey.DisplayText} is unavailable; {previousHotKey.DisplayText} remains active."));
            await SafeLogAsync(
                "hotkey.register-failed",
                DiagnosticLevel.Warning,
                errorCode: $"win32-{errorCode}");
            return;
        }

        _settings = _settings with { ManualCaptureHotKey = hotKey.DisplayText };
        try
        {
            await _settingsStore.SaveAsync(_settings);
            _capturingHotKey = false;
            AddActivity(T(
                $"主动截图快捷键已改为 {hotKey.DisplayText}。",
                $"The capture hotkey is now {hotKey.DisplayText}."));
            await SafeLogAsync("hotkey.updated", DiagnosticLevel.Information, success: true);
        }
        catch (Exception exception)
        {
            _ = _globalHotKeyManager.TrySet(previousHotKey, out _);
            _settings = previousSettings;
            await ReportExceptionAsync("settings.save-failed", "hotkey-settings-save", exception);
        }
        finally
        {
            ApplyLocalization();
            UpdateUi();
        }
    }

    private static bool TryCreateHotKey(
        Key key,
        ModifierKeys keyboardModifiers,
        out ManualCaptureHotKey hotKey)
    {
        hotKey = null!;
        var modifiers = HotKeyModifiers.None;
        if (keyboardModifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotKeyModifiers.Control;
        }

        if (keyboardModifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotKeyModifiers.Alt;
        }

        if (keyboardModifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotKeyModifiers.Shift;
        }

        if (keyboardModifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotKeyModifiers.Windows;
        }

        string? keyName = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(CultureInfo.InvariantCulture),
            >= Key.F1 and <= Key.F12 => key.ToString(),
            _ => null,
        };
        return keyName is not null &&
               ManualCaptureHotKey.TryParse(
                   new ManualCaptureHotKey(modifiers, keyName).DisplayText,
                   out hotKey);
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;

    private void ApplyLocalization()
    {
        Title = ProductMetadata.DisplayName;
        SubtitleText.Text = T(
            "让 ChatGPT Voice 根据游戏画面自然陪聊",
            "Natural ChatGPT Voice companionship grounded in your game screen");
        PrivacySummaryText.Text = T(
            "OpenGameMate 只在你开始后捕获整个主显示器。它不读取回复、聊天记录、Cookie、Token 或音频。",
            "OpenGameMate captures the entire primary display only after you start. It does not read replies, history, cookies, tokens, or audio.");
        BrowserGroup.Header = T("1. ChatGPT 与 Voice", "1. ChatGPT and Voice");
        OpenBrowserButton.Content = T("打开 ChatGPT", "Open ChatGPT");
        ShowBrowserButton.Content = T("显示窗口", "Show window");
        HideBrowserButton.Content = T("隐藏到托盘", "Hide to tray");
        CloseBrowserButton.Content = T("关闭网页并结束 Voice", "Close page and end Voice");
        BrowserInstructionText.Text = T(
            "请在右侧内嵌页面自行登录并开启 Voice，然后在这里确认。",
            "Sign in and start Voice in the embedded page, then confirm here.");
        ConfirmVoiceButton.Content = T("我已开启 Voice", "I started Voice");
        BrowserPaneTitleText.Text = "ChatGPT";
        BrowserPaneCaptionText.Text = T(
            "内嵌网页 · 登录与 Voice 由你控制",
            "Embedded page · sign-in and Voice stay under your control");
        BrowserPlaceholderTitleText.Text = T("ChatGPT 将显示在这里", "ChatGPT will appear here");
        BrowserPlaceholderDescriptionText.Text = T(
            "点击左侧“打开 ChatGPT”，网页会直接嵌入 OpenGameMate，不再弹出独立窗口。",
            "Select Open ChatGPT on the left. The page stays inside OpenGameMate instead of opening a separate window.");
        RoleGroup.Header = T("2. 可选角色初始化", "2. Optional role initialization");
        RoleInstructionText.Text = T(
            "首次使用可由你明确发送一次完整陪玩设定；不会自动发送。",
            "You may explicitly send the full companion role once; it is never sent automatically.");
        SendFullRoleButton.Content = T("发送完整设定", "Send full role");
        SendShortRoleButton.Content = T("发送简短提醒", "Send short reminder");
        NewConversationButton.Content = T("已新建对话，不发送提醒", "New chat created; send nothing");
        RoleStatusText.Text = _settings.RolePromptSent
            ? T("已记录完整角色设定发送成功。", "Full role initialization was recorded as sent.")
            : T("尚未记录角色初始化。", "Role initialization has not been recorded.");
        RunGroup.Header = T("3. 陪玩控制", "3. Companion controls");
        RunInstructionText.Text = T(
            "ChatGPT 网页音频停止且页面空闲稳定约 10 秒后，才截图并自动发送。",
            "A screenshot is captured and sent only after ChatGPT web audio stops and the page remains idle for about 10 seconds.");
        StartButton.Content = T("开始陪玩", "Start");
        SendNowButton.Content = T("立即发送", "Send now");
        HotKeyLabelText.Text = T("主动截图快捷键", "Capture hotkey");
        ChangeHotKeyButton.Content = _capturingHotKey
            ? T("请按组合键", "Press keys")
            : T("更改", "Change");
        HotKeyTextBox.Text = _capturingHotKey
            ? T("按下新快捷键，Esc 取消", "Press a new hotkey; Esc cancels")
            : ManualCaptureHotKey.Parse(_settings.ManualCaptureHotKey).DisplayText;
        StopButton.Content = T("停止", "Stop");
        MaintenanceGroup.Header = T("4. 数据、规则与诊断", "4. Data, rules, and diagnostics");
        RetryAdapterButton.Content = T("重新检查内置规则", "Retry built-in rules");
        ExportDiagnosticsButton.Content = T("导出诊断包", "Export diagnostics");
        OpenDataFolderButton.Content = T("打开数据目录", "Open data folder");
        BrowserDataHelpButton.Content = T("浏览器数据清理说明", "Browser-data cleanup help");
        RemoteRulesText.Text = T(
            _settings.CheckRemoteAdapterRules
                ? "远程规则：已请求检查，但当前版本未配置官方仓库和签名公钥；安全使用内置规则。"
                : "远程规则检查已在设置中禁用；安全使用内置规则。",
            _settings.CheckRemoteAdapterRules
                ? "Remote rule checks were requested, but this build has no official repository and signing key; built-in rules are used safely."
                : "Remote rule checks are disabled in settings; built-in rules are used safely.");
        ActivityGroup.Header = T("活动记录（不含网页内容）", "Activity (no webpage content)");
    }

    private async void OpenBrowserButton_Click(object sender, RoutedEventArgs e) =>
        await EnsureBrowserAsync(activate: true);

    private void ShowBrowserButton_Click(object sender, RoutedEventArgs e) => ShowMainWindow();

    private void HideBrowserButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void CloseBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        CloseEmbeddedBrowser(applyStateTransition: true);
        AddActivity(T("ChatGPT 网页已关闭；Voice 会话已结束。", "ChatGPT page closed; the Voice session ended."));
    }

    private async Task EnsureBrowserAsync(bool activate)
    {
        if (_browserSession is not null || _browserInitializing)
        {
            if (activate)
            {
                ShowMainWindow();
            }

            return;
        }

        _browserInitializing = true;
        BrowserPlaceholder.Visibility = Visibility.Visible;
        BrowserPlaceholderTitleText.Text = T("正在准备 ChatGPT", "Preparing ChatGPT");
        BrowserPlaceholderDescriptionText.Text = T(
            "正在初始化独立的 WebView2 用户数据环境，请稍候。",
            "Initializing the isolated WebView2 user-data environment.");
        UpdateUi();

        try
        {
            _browserView = new WebView2();
            Panel.SetZIndex(_browserView, 0);
            BrowserHost.Children.Insert(0, _browserView);

            _browserSession = new ChatGptBrowserSession(
                _browserView,
                _paths.WebViewUserDataDirectory,
                PromptForMicrophoneAsync,
                FindBrowserRuntime());
            _browserSession.StatusChanged += BrowserSession_StatusChanged;
            _browserSession.ProcessFailed += BrowserSession_ProcessFailed;
            _browserSession.AudioStateChanged += BrowserSession_AudioStateChanged;
            await _browserSession.InitializeAsync();
            _adapter = new ChatGptWebAdapter(_browserSession, ChatGptAdapterRules.BuiltIn);
            BrowserPlaceholder.Visibility = Visibility.Collapsed;

            if (_stateMachine.State == GameMateState.Idle)
            {
                ApplyTrigger(GameMateTrigger.BrowserInitialized);
            }

            AddActivity(T(
                "ChatGPT 已嵌入主窗口；登录和 Voice 由你控制。",
                "ChatGPT is embedded in the main window; sign-in and Voice remain under your control."));
            await SafeLogAsync("browser.initialized", DiagnosticLevel.Information, success: true);
            await SafeLogAsync(
                "adapter.rules-selected",
                DiagnosticLevel.Information,
                errorCode: _settings.CheckRemoteAdapterRules
                    ? "remote-unavailable"
                    : "remote-disabled",
                success: true);
            await SafeLogAsync(
                "webview.audio-state",
                DiagnosticLevel.Information,
                audioState: _browserSession.IsDocumentPlayingAudio
                    ? WebAudioState.Playing
                    : WebAudioState.Silent,
                audioSilentDurationMs: _browserSession.IsDocumentPlayingAudio ? 0 : 0);
        }
        catch (Exception exception)
        {
            await ReportExceptionAsync("browser.initialization-failed", "browser-init", exception);
            CleanupFailedBrowserInitialization();
        }
        finally
        {
            _browserInitializing = false;
        }

        UpdateUi();
    }

    private string? FindBrowserRuntime()
    {
        var productRuntime = ChatGptBrowserSession.FindFixedRuntimeFolder(
            Path.Combine(_paths.RootDirectory, "WebView2Runtime"));
        if (productRuntime is not null)
        {
            return productRuntime;
        }

        if (_paths.Mode != AppDataMode.Installed)
        {
            return null;
        }

        return ChatGptBrowserSession.FindFixedRuntimeFolder(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenGameMate",
            "Phase0",
            "WebView2Runtime"));
    }

    private void CleanupFailedBrowserInitialization()
    {
        CloseEmbeddedBrowser(applyStateTransition: false);
    }

    private void BrowserSession_StatusChanged(object? sender, string message) =>
        Dispatcher.Invoke(() => AddActivity(message));

    private async void BrowserSession_ProcessFailed(
        object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedEventArgs args)
    {
        await SafeLogAsync(
            "webview.process-failed",
            DiagnosticLevel.Error,
            errorCode: args.ProcessFailedKind.ToString(),
            success: false);
    }

    private async void BrowserSession_AudioStateChanged(
        object? sender,
        BrowserAudioStateChangedEventArgs args)
    {
        await SafeLogAsync(
            "webview.audio-state-changed",
            DiagnosticLevel.Information,
            audioState: args.IsPlaying ? WebAudioState.Playing : WebAudioState.Silent,
            audioSilentDurationMs: args.IsPlaying ? 0 : 0);
    }

    private void CloseEmbeddedBrowser(bool applyStateTransition)
    {
        _runCancellation?.Cancel();

        if (_browserSession is not null)
        {
            _browserSession.StatusChanged -= BrowserSession_StatusChanged;
            _browserSession.ProcessFailed -= BrowserSession_ProcessFailed;
            _browserSession.AudioStateChanged -= BrowserSession_AudioStateChanged;
            _browserSession.Dispose();
        }

        _browserSession = null;
        _adapter = null;

        if (_browserView is not null)
        {
            BrowserHost.Children.Remove(_browserView);
            _browserView.Dispose();
            _browserView = null;
        }

        BrowserPlaceholder.Visibility = Visibility.Visible;
        BrowserPlaceholderTitleText.Text = T("ChatGPT 将显示在这里", "ChatGPT will appear here");
        BrowserPlaceholderDescriptionText.Text = T(
            "点击左侧“打开 ChatGPT”，网页会直接嵌入 OpenGameMate，不再弹出独立窗口。",
            "Select Open ChatGPT on the left. The page stays inside OpenGameMate instead of opening a separate window.");

        if (applyStateTransition && _stateMachine.CanApply(GameMateTrigger.BrowserClosed))
        {
            ApplyTrigger(GameMateTrigger.BrowserClosed);
        }
        else
        {
            UpdateUi();
        }
    }

    private Task<bool> PromptForMicrophoneAsync(Uri uri)
    {
        var result = MessageBox.Show(
            this,
            T(
                $"ChatGPT 官方页面 {uri.Host} 请求麦克风权限。OpenGameMate 不读取或录制音频。是否允许？",
                $"The official ChatGPT page {uri.Host} requests microphone access. OpenGameMate does not read or record audio. Allow?"),
            T("ChatGPT 麦克风权限", "ChatGPT microphone permission"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    private void ConfirmVoiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_browserSession?.IsOnChatGpt() != true)
        {
            AddActivity(T("请先让浏览器回到 chatgpt.com。", "Return the browser to chatgpt.com first."));
            return;
        }

        if (_stateMachine.State == GameMateState.Stopped)
        {
            ApplyTrigger(GameMateTrigger.Reset);
            ApplyTrigger(GameMateTrigger.BrowserInitialized);
        }

        if (_stateMachine.State == GameMateState.Idle)
        {
            ApplyTrigger(GameMateTrigger.BrowserInitialized);
        }

        if (_stateMachine.State == GameMateState.BrowserReady)
        {
            ApplyTrigger(GameMateTrigger.VoiceConfirmed);
            AddActivity(T("Voice 已由用户确认。", "Voice was confirmed by the user."));
            _ = SafeLogAsync("voice.user-confirmed", DiagnosticLevel.Information, success: true);
        }

        UpdateUi();
    }

    private async void SendFullRoleButton_Click(object sender, RoutedEventArgs e) =>
        await SendRoleTextAsync(CompanionPrompts.FullRole(PromptLanguage), markFullRoleSent: true, resetConversation: false);

    private async void SendShortRoleButton_Click(object sender, RoutedEventArgs e) =>
        await SendRoleTextAsync(CompanionPrompts.ShortReminder(PromptLanguage), markFullRoleSent: false, resetConversation: true);

    private void NewConversationButton_Click(object sender, RoutedEventArgs e)
    {
        _reminderTracker?.Reset(DateTimeOffset.UtcNow);
        AddActivity(T("已按用户确认重置会话提醒计数；未向网页发送内容。", "Conversation reminder counters reset without sending webpage content."));
        UpdateUi();
    }

    private async Task SendRoleTextAsync(string text, bool markFullRoleSent, bool resetConversation)
    {
        if (_adapter is null || _browserSession?.IsOnChatGpt() != true ||
            _stateMachine.State is not (GameMateState.BrowserReady or GameMateState.Ready or GameMateState.Paused))
        {
            AddActivity(T("角色提示未发送：请先打开 ChatGPT，并在开始陪玩前操作。", "Role text was not sent: open ChatGPT and do this before starting."));
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            T("这会向当前真实 ChatGPT 对话发送角色文字。确认发送？", "This will send role text to the current real ChatGPT chat. Continue?"),
            "OpenGameMate",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _auxiliaryOperation = true;
        UpdateUi();
        try
        {
            var preparation = await _adapter.PrepareTextAsync(text);
            if (!preparation.TextInserted || preparation.Status != WebAdapterStatus.Succeeded)
            {
                HandleAdapterFailure(preparation.Status, "role-prepare");
                return;
            }

            var submission = await _adapter.SubmitAsync();
            await LogAdapterDiagnosticsAsync(submission);
            if (submission.Status != WebAdapterStatus.Succeeded ||
                !submission.TriggerInvoked ||
                !(submission.ComposerCleared || submission.AttachmentCleared))
            {
                HandleAdapterFailure(submission.Status, "role-submit");
                return;
            }

            if (markFullRoleSent)
            {
                _settings = _settings with { RolePromptSent = true };
                await _settingsStore.SaveAsync(_settings);
                RoleStatusText.Text = T("已记录完整角色设定发送成功。", "Full role initialization was recorded as sent.");
            }

            if (resetConversation)
            {
                _reminderTracker?.Reset(DateTimeOffset.UtcNow);
            }

            AddActivity(T("角色文字已提交；未读取 ChatGPT 回复。", "Role text submitted without reading the reply."));
            await SafeLogAsync("role-text.submitted", DiagnosticLevel.Information, success: true);
        }
        catch (Exception exception)
        {
            await ReportExceptionAsync("role-text.failed", "role-text", exception);
        }
        finally
        {
            _auxiliaryOperation = false;
            UpdateUi();
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stateMachine.State != GameMateState.Ready)
        {
            return;
        }

        if (!await ConfirmPrivacyBeforeFirstCaptureAsync())
        {
            return;
        }

        BeginRun(resetConversationTracker: true);
    }

    private async Task<bool> ConfirmPrivacyBeforeFirstCaptureAsync()
    {
        if (!_settings.ShowPrivacyWarningOnFirstStart)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            T(
                "OpenGameMate 会在 ChatGPT 网页音频停止且页面空闲连续稳定约 10 秒后，才捕获整个主显示器并发送到当前对话。画面可能包含通知、聊天、账号名或其他私人内容。请先关闭敏感窗口。独占全屏、受保护内容和反作弊环境可能黑屏或失败，程序不会绕过保护。\n\n是否理解风险并开始？",
                "OpenGameMate captures the entire primary display only after ChatGPT web audio stops and the page remains idle for about 10 seconds, then sends it to the current chat. Notifications, chats, account names, or other private content may be included. Close sensitive windows first. Exclusive fullscreen, protected content, and anti-cheat environments may fail or produce black frames; protections are not bypassed.\n\nDo you understand the risk and want to start?"),
            T("整屏捕获隐私确认", "Full-display capture privacy confirmation"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        _settings = _settings with { ShowPrivacyWarningOnFirstStart = false };
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            await ReportExceptionAsync("settings.save-failed", "privacy-setting", exception);
        }

        return true;
    }

    private void BeginRun(bool resetConversationTracker)
    {
        if (_stateMachine.State != GameMateState.Ready)
        {
            return;
        }

        ApplyTrigger(GameMateTrigger.Start);
        if (resetConversationTracker || _reminderTracker is null)
        {
            _reminderTracker = new ConversationReminderTracker(DateTimeOffset.UtcNow);
        }

        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        _automaticLoopTask = RunAutomaticLoopAsync(_runCancellation.Token);
        AddActivity(T(
            "陪玩已开始；网页音频停止且页面空闲稳定 10 秒后自动发送。",
            "Companion mode started; automatic capture waits for 10 seconds of stable page and web-audio silence."));
        _ = SafeLogAsync("run.started", DiagnosticLevel.Information, success: true);
        UpdateUi();
    }

    private async Task RunAutomaticLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _automaticSendLoop.RunAsync(
                () => _stateMachine.State == GameMateState.Running,
                ObserveConversationIdleAsync,
                ProcessAutomaticOccurrenceAsync,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ReportExceptionAsync("scheduler.failed", "scheduler", exception);
        }
    }

    private async Task<bool> ObserveConversationIdleAsync(CancellationToken cancellationToken)
    {
        if (_stateMachine.State != GameMateState.Running ||
            _browserSession is null ||
            _adapter is null)
        {
            return false;
        }

        var audio = _browserSession.GetAudioSnapshot(DateTimeOffset.UtcNow);
        if (!audio.IsKnown || audio.IsPlaying)
        {
            return false;
        }

        var probe = await _adapter.ProbeIdleAsync(cancellationToken);
        if (probe.Status != WebAdapterStatus.AdapterInvalid)
        {
            return probe.IsSafeToPrepare;
        }

        await SafeLogAsync(
            "scheduler.adapter-invalid",
            DiagnosticLevel.Warning,
            errorCode: probe.Code,
            adapterDiagnostics: probe.Diagnostics,
            audioState: ToWebAudioState(audio),
            audioSilentDurationMs: (long)audio.SilentDuration.TotalMilliseconds);
        if (_stateMachine.State == GameMateState.Running)
        {
            ApplyTrigger(GameMateTrigger.AdapterInvalid);
            AddActivity(T(
                "网页适配失效；空闲检测已停止且不会操作输入区域。",
                "The webpage adapter failed; idle detection stopped without touching the composer."));
            _runCancellation?.Cancel();
        }

        return false;
    }

    private async Task ProcessAutomaticOccurrenceAsync(
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken)
    {
        var createdAt = DateTimeOffset.UtcNow;
        if (!_automaticPendingSendSlot.TryCreate(scheduledAt, createdAt, out var pending) ||
            pending is null)
        {
            await SafeLogAsync(
                "pending.capacity-skipped",
                DiagnosticLevel.Information,
                errorCode: "pending-capacity-one",
                scheduledAt: scheduledAt,
                pendingCreatedAt: createdAt);
            return;
        }

        await SafeLogAsync(
            "pending.created",
            DiagnosticLevel.Information,
            success: true,
            scheduledAt: pending.ScheduledAt,
            pendingCreatedAt: pending.PendingCreatedAt);
        try
        {
            var gate = await WaitForPendingIdleAsync(pending, cancellationToken);
            if (gate == PendingGateResult.AdapterInvalid)
            {
                ApplyTrigger(GameMateTrigger.AdapterInvalid);
                _runCancellation?.Cancel();
                AddActivity(T(
                    "网页适配失效；待发送截图已放弃，且不会随机点击。",
                    "The webpage adapter failed; the pending screenshot was abandoned without random clicks."));
                UpdateUi();
                return;
            }

            if (gate != PendingGateResult.Ready)
            {
                return;
            }

            _dispatchingAutomaticPending = pending;
            var result = await _submissionCoordinator.RunAutomaticAsync(cancellationToken);
            if (result.Status != SubmissionDispatchStatus.Executed)
            {
                await SafeLogAsync(
                    "submission.automatic-skipped",
                    DiagnosticLevel.Information,
                    errorCode: result.Status.ToString(),
                    scheduledAt: pending.ScheduledAt,
                    pendingCreatedAt: pending.PendingCreatedAt);
            }
        }
        finally
        {
            _dispatchingAutomaticPending = null;
            _automaticReadyAudioVersion = null;
            _automaticReadyPageState = null;
            _automaticPendingSendSlot.Release(pending);
        }
    }

    private async Task<PendingGateResult> WaitForPendingIdleAsync(
        AutomaticPendingSend pending,
        CancellationToken cancellationToken)
    {
        string? lastDeferredReason = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stateMachine.State != GameMateState.Running)
            {
                await SafeLogAsync(
                    "pending.skipped-state-change",
                    DiagnosticLevel.Information,
                    errorCode: "manual-priority-or-run-stopped",
                    scheduledAt: pending.ScheduledAt,
                    pendingCreatedAt: pending.PendingCreatedAt);
                return PendingGateResult.Skipped;
            }

            if (_browserSession is null || _adapter is null)
            {
                return PendingGateResult.AdapterInvalid;
            }

            var observedAt = DateTimeOffset.UtcNow;
            var audio = _browserSession.GetAudioSnapshot(observedAt);
            var probe = await _adapter.ProbeIdleAsync(cancellationToken);
            if (probe.Status == WebAdapterStatus.AdapterInvalid)
            {
                await SafeLogAsync(
                    "pending.adapter-invalid",
                    DiagnosticLevel.Warning,
                    errorCode: probe.Code,
                    adapterDiagnostics: probe.Diagnostics,
                    scheduledAt: pending.ScheduledAt,
                    pendingCreatedAt: pending.PendingCreatedAt,
                    audioState: ToWebAudioState(audio),
                    audioSilentDurationMs: (long)audio.SilentDuration.TotalMilliseconds);
                return PendingGateResult.AdapterInvalid;
            }

            var audioSilent = audio.IsKnown && !audio.IsPlaying;
            var deferredReason = !audio.IsKnown
                ? "audio-unknown"
                : audio.IsPlaying
                    ? "audio-playing"
                    : !probe.IsSafeToPrepare
                        ? GetUnsafePreparationReason(probe)
                        : "idle-stabilizing";
            var previousCandidate = pending.IdleCandidateAt;
            var observation = pending.Observe(
                observedAt,
                probe.IsSafeToPrepare,
                audioSilent,
                audio.SilentDuration,
                deferredReason);
            if (observation == PendingSendObservation.Expired)
            {
                await SafeLogAsync(
                    "pending.expired",
                    DiagnosticLevel.Information,
                    errorCode: "conversation-busy-timeout",
                    scheduledAt: pending.ScheduledAt,
                    pendingCreatedAt: pending.PendingCreatedAt,
                    deferredAt: pending.DeferredAt,
                    deferredReason: pending.DeferredReason,
                    idleCandidateAt: pending.IdleCandidateAt,
                    idleStableDurationMs: pending.IdleStableDurationMs,
                    audioState: ToWebAudioState(audio),
                    audioSilentDurationMs: (long)audio.SilentDuration.TotalMilliseconds,
                    skippedBecauseConversationBusy: true,
                    pendingExpiredAt: pending.PendingExpiredAt);
                return PendingGateResult.Skipped;
            }

            if (previousCandidate is null && pending.IdleCandidateAt is not null)
            {
                await SafeLogAsync(
                    "pending.idle-candidate",
                    DiagnosticLevel.Information,
                    scheduledAt: pending.ScheduledAt,
                    pendingCreatedAt: pending.PendingCreatedAt,
                    adapterDiagnostics: probe.Diagnostics,
                    idleCandidateAt: pending.IdleCandidateAt,
                    idleStableDurationMs: pending.IdleStableDurationMs,
                    audioState: ToWebAudioState(audio),
                    audioSilentDurationMs: (long)audio.SilentDuration.TotalMilliseconds);
            }

            if (observation == PendingSendObservation.Ready)
            {
                _automaticReadyAudioVersion = audio.Version;
                _automaticReadyPageState = probe.Diagnostics.PageState;
                await SafeLogAsync(
                    "pending.ready",
                    DiagnosticLevel.Information,
                    success: true,
                    adapterDiagnostics: probe.Diagnostics,
                    scheduledAt: pending.ScheduledAt,
                    pendingCreatedAt: pending.PendingCreatedAt,
                    idleCandidateAt: pending.IdleCandidateAt,
                    idleStableDurationMs: pending.IdleStableDurationMs,
                    audioState: ToWebAudioState(audio),
                    audioSilentDurationMs: (long)audio.SilentDuration.TotalMilliseconds);
                return PendingGateResult.Ready;
            }

            if (pending.DeferredReason is not null &&
                !string.Equals(lastDeferredReason, pending.DeferredReason, StringComparison.Ordinal))
            {
                lastDeferredReason = pending.DeferredReason;
                await SafeLogAsync(
                    "pending.deferred",
                    DiagnosticLevel.Information,
                    scheduledAt: pending.ScheduledAt,
                    pendingCreatedAt: pending.PendingCreatedAt,
                    adapterDiagnostics: probe.Diagnostics,
                    deferredAt: pending.DeferredAt,
                    deferredReason: pending.DeferredReason,
                    audioState: ToWebAudioState(audio),
                    audioSilentDurationMs: (long)audio.SilentDuration.TotalMilliseconds);
            }

            await Task.Delay(PendingProbeInterval, cancellationToken);
        }
    }

    private static WebAudioState ToWebAudioState(BrowserAudioSnapshot audio) =>
        !audio.IsKnown
            ? WebAudioState.Unknown
            : audio.IsPlaying
                ? WebAudioState.Playing
                : WebAudioState.Silent;

    private async void SendNowButton_Click(object sender, RoutedEventArgs e) => await SendNowAsync();

    private async Task SendNowAsync()
    {
        if (_stateMachine.State != GameMateState.Running)
        {
            return;
        }

        var result = await _submissionCoordinator.RunManualAsync(_runCancellation?.Token ?? CancellationToken.None);
        if (result.Status != SubmissionDispatchStatus.Executed)
        {
            AddActivity(T("当前已有发送任务；手动请求未排队。", "A submission is active; the manual request was not queued."));
        }
    }

    private Task<SubmissionOutcome> DispatchSubmissionAsync(
        SubmissionOrigin origin,
        CancellationToken cancellationToken)
    {
        if (Dispatcher.CheckAccess())
        {
            return ExecuteSubmissionAsync(origin, cancellationToken);
        }

        return Dispatcher.InvokeAsync(() => ExecuteSubmissionAsync(origin, cancellationToken)).Task.Unwrap();
    }

    private async Task<SubmissionOutcome> ExecuteSubmissionAsync(
        SubmissionOrigin origin,
        CancellationToken cancellationToken)
    {
        if (_stateMachine.State != GameMateState.Running || _adapter is null)
        {
            return SubmissionOutcome.OrdinaryFailure;
        }

        ApplyTrigger(GameMateTrigger.BeginSend);
        UpdateUi();
        SubmissionOutcome outcome;
        try
        {
            outcome = await PerformSubmissionAsync(origin, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = SubmissionOutcome.OrdinaryFailure;
        }
        catch (ScreenCaptureException exception)
        {
            outcome = SubmissionOutcome.OrdinaryFailure;
            await SafeLogAsync(
                "capture.failed",
                DiagnosticLevel.Warning,
                errorCode: exception.Code.ToString(),
                exceptionType: exception.GetType().Name);
            AddActivity(T(
                $"截图失败：{exception.Code}。独占全屏可尝试无边框模式。",
                $"Capture failed: {exception.Code}. Try borderless mode for exclusive fullscreen."));
        }
        catch (Exception exception)
        {
            outcome = SubmissionOutcome.OrdinaryFailure;
            await ReportExceptionAsync("submission.failed", "submission", exception);
        }
        finally
        {
            try
            {
                _capture.DeleteTemporaryScreenshot();
            }
            catch (Exception exception)
            {
                await ReportExceptionAsync("capture.cleanup-failed", "capture-cleanup", exception);
            }
        }

        CompleteSubmission(outcome);
        UpdateUi();
        return outcome;
    }

    private async Task<SubmissionOutcome> PerformSubmissionAsync(
        SubmissionOrigin origin,
        CancellationToken cancellationToken)
    {
        if (origin == SubmissionOrigin.Automatic)
        {
            return await PerformAutomaticSubmissionAsync(cancellationToken);
        }

        var capture = await _capture.CaptureAsync(cancellationToken);
        await SafeLogAsync(
            "capture.succeeded",
            DiagnosticLevel.Information,
            success: true,
            imageWidth: capture.OutputWidth,
            imageHeight: capture.OutputHeight,
            fileSizeBytes: capture.FileBytes);

        var message = origin == SubmissionOrigin.Manual
            ? CompanionPrompts.ManualScreenshot(PromptLanguage)
            : CompanionPrompts.AutomaticScreenshot(PromptLanguage);
        var preparation = await _adapter!.PrepareInputAsync(capture.Path, message, cancellationToken);
        if (preparation.Status != WebAdapterStatus.Succeeded ||
            !preparation.ImageAdded ||
            !preparation.TextInserted)
        {
            return MapAdapterStatus(preparation.Status);
        }

        var submission = await _adapter.SubmitAsync(cancellationToken);
        await LogAdapterDiagnosticsAsync(submission);
        if (submission.Status != WebAdapterStatus.Succeeded ||
            !submission.TriggerInvoked ||
            !(submission.ComposerCleared || submission.AttachmentCleared))
        {
            return MapAdapterStatus(submission.Status);
        }

        await SafeLogAsync("submission.succeeded", DiagnosticLevel.Information, success: true);
        AddActivity(origin == SubmissionOrigin.Manual
            ? T("手动画面已发送。", "Manual screen sent.")
            : T("自动画面已发送。", "Automatic screen sent."));
        return SubmissionOutcome.Succeeded;
    }

    private async Task<SubmissionOutcome> PerformAutomaticSubmissionAsync(
        CancellationToken cancellationToken)
    {
        var pending = _dispatchingAutomaticPending;
        var expectedAudioVersion = _automaticReadyAudioVersion;
        var expectedPageState = _automaticReadyPageState;
        if (pending is null || expectedAudioVersion is null || expectedPageState is null ||
            !await AutomaticGuardIsValidAsync(
                expectedAudioVersion.Value,
                expectedPageState.Value,
                cancellationToken))
        {
            await SafeLogAsync(
                "submission.pre-capture-guard-failed",
                DiagnosticLevel.Information,
                errorCode: "idle-state-changed",
                scheduledAt: pending?.ScheduledAt,
                pendingCreatedAt: pending?.PendingCreatedAt);
            return SubmissionOutcome.OrdinaryFailure;
        }

        var capture = await _capture.CaptureAsync(cancellationToken);
        await SafeLogAsync(
            "capture.succeeded",
            DiagnosticLevel.Information,
            success: true,
            imageWidth: capture.OutputWidth,
            imageHeight: capture.OutputHeight,
            fileSizeBytes: capture.FileBytes,
            scheduledAt: pending.ScheduledAt,
            pendingCreatedAt: pending.PendingCreatedAt);

        if (!await AutomaticGuardIsValidAsync(
                expectedAudioVersion.Value,
                expectedPageState.Value,
                cancellationToken))
        {
            await SafeLogAsync(
                "submission.post-capture-guard-failed",
                DiagnosticLevel.Information,
                errorCode: "idle-state-changed",
                scheduledAt: pending.ScheduledAt,
                pendingCreatedAt: pending.PendingCreatedAt);
            return SubmissionOutcome.OrdinaryFailure;
        }

        var attachStartedAt = DateTimeOffset.UtcNow;
        await SafeLogAsync(
            "submission.attach-started",
            DiagnosticLevel.Information,
            scheduledAt: pending.ScheduledAt,
            pendingCreatedAt: pending.PendingCreatedAt,
            attachStartedAt: attachStartedAt);
        var attachment = await _adapter!.PrepareImageAsync(capture.Path, cancellationToken);
        if (attachment.Status != WebAdapterStatus.Succeeded || !attachment.ImageAdded)
        {
            return MapAdapterStatus(attachment.Status);
        }

        var afterAttachAudio = _browserSession!.GetAudioSnapshot(DateTimeOffset.UtcNow);
        var voiceStateChangedAfterAttach =
            afterAttachAudio.Version != expectedAudioVersion.Value || afterAttachAudio.IsPlaying;
        await SafeLogAsync(
            "submission.attachment-prepared",
            voiceStateChangedAfterAttach ? DiagnosticLevel.Warning : DiagnosticLevel.Information,
            errorCode: voiceStateChangedAfterAttach ? "voice-state-changed-after-attach" : null,
            success: !voiceStateChangedAfterAttach,
            scheduledAt: pending.ScheduledAt,
            pendingCreatedAt: pending.PendingCreatedAt,
            attachStartedAt: attachStartedAt,
            voiceStateChangedAfterAttach: voiceStateChangedAfterAttach,
            audioState: ToWebAudioState(afterAttachAudio),
            audioSilentDurationMs: (long)afterAttachAudio.SilentDuration.TotalMilliseconds);
        if (voiceStateChangedAfterAttach)
        {
            return SubmissionOutcome.OrdinaryFailure;
        }

        var message = CompanionPrompts.AutomaticScreenshot(PromptLanguage);
        var text = await _adapter.PrepareTextAsync(message, cancellationToken);
        var textSetAt = DateTimeOffset.UtcNow;
        if (text.Status != WebAdapterStatus.Succeeded || !text.TextInserted)
        {
            return MapAdapterStatus(text.Status);
        }

        var expectedPreparedPageState = expectedPageState.Value switch
        {
            AdapterPageState.Composer => AdapterPageState.ComposerWithAttachment,
            _ => AdapterPageState.Unknown,
        };
        if (expectedPreparedPageState == AdapterPageState.Unknown)
        {
            return SubmissionOutcome.OrdinaryFailure;
        }

        var preparedGate = await WaitForPreparedInputReadyAsync(
            pending,
            expectedAudioVersion.Value,
            expectedPreparedPageState,
            cancellationToken);
        await SafeLogAsync(
            "submission.pre-submit-check",
            preparedGate.Ready ? DiagnosticLevel.Information : DiagnosticLevel.Warning,
            errorCode: preparedGate.Ready
                ? null
                : preparedGate.VoiceStateChanged
                    ? "voice-state-changed-after-attach"
                    : preparedGate.Expired
                        ? "conversation-busy-timeout"
                        : preparedGate.Probe?.Code ?? "prepared-input-not-ready",
            success: preparedGate.Ready,
            adapterDiagnostics: preparedGate.Probe?.Diagnostics,
            scheduledAt: pending.ScheduledAt,
            pendingCreatedAt: pending.PendingCreatedAt,
            attachStartedAt: attachStartedAt,
            textSetAt: textSetAt,
            voiceStateChangedAfterAttach: preparedGate.VoiceStateChanged,
            skippedBecauseConversationBusy: preparedGate.Expired ? true : null,
            pendingExpiredAt: preparedGate.Expired ? DateTimeOffset.UtcNow : null);
        if (!preparedGate.Ready)
        {
            return preparedGate.Probe?.Status == WebAdapterStatus.AdapterInvalid
                ? SubmissionOutcome.AdapterInvalid
                : SubmissionOutcome.OrdinaryFailure;
        }

        var submitStartedAt = DateTimeOffset.UtcNow;
        var submission = await _adapter.SubmitPreparedInputOnceAsync(
            expectedAudioVersion.Value,
            AutomaticPendingSend.RequiredIdleStability,
            expectedPreparedPageState,
            cancellationToken);
        await SafeLogAsync(
            "adapter.submit-probe",
            submission.Status == WebAdapterStatus.Succeeded
                ? DiagnosticLevel.Information
                : DiagnosticLevel.Warning,
            errorCode: submission.Code,
            success: submission.Status == WebAdapterStatus.Succeeded,
            adapterDiagnostics: submission.Diagnostics,
            scheduledAt: pending.ScheduledAt,
            pendingCreatedAt: pending.PendingCreatedAt,
            attachStartedAt: attachStartedAt,
            textSetAt: textSetAt,
            submitStartedAt: submitStartedAt,
            voiceStateChangedAfterAttach: false);
        if (submission.Status != WebAdapterStatus.Succeeded ||
            !submission.TriggerInvoked ||
            !(submission.ComposerCleared || submission.AttachmentCleared))
        {
            return MapAdapterStatus(submission.Status);
        }

        await SafeLogAsync(
            "submission.succeeded",
            DiagnosticLevel.Information,
            success: true,
            scheduledAt: pending.ScheduledAt,
            pendingCreatedAt: pending.PendingCreatedAt,
            attachStartedAt: attachStartedAt,
            textSetAt: textSetAt,
            submitStartedAt: submitStartedAt,
            voiceStateChangedAfterAttach: false);
        AddActivity(T("自动画面已发送。", "Automatic screen sent."));
        return SubmissionOutcome.Succeeded;
    }

    private async Task<bool> AutomaticGuardIsValidAsync(
        long expectedAudioVersion,
        AdapterPageState expectedPageState,
        CancellationToken cancellationToken)
    {
        if (_browserSession is null || _adapter is null)
        {
            return false;
        }

        var audio = _browserSession.GetAudioSnapshot(DateTimeOffset.UtcNow);
        if (!audio.IsKnown || audio.IsPlaying || audio.Version != expectedAudioVersion ||
            audio.SilentDuration < AutomaticPendingSend.RequiredIdleStability)
        {
            return false;
        }

        var probe = await _adapter.ProbeIdleAsync(cancellationToken);
        return probe.IsSafeToPrepare && probe.Diagnostics.PageState == expectedPageState;
    }

    private static string GetUnsafePreparationReason(AdapterIdleProbeResult probe)
    {
        if (probe.Diagnostics.PageState is
            AdapterPageState.ComposerWithAttachment or
            AdapterPageState.VoiceComposerWithAttachment)
        {
            return "composer-not-empty";
        }

        return string.Equals(probe.Code, "ok", StringComparison.Ordinal)
            ? "page-not-ready"
            : probe.Code;
    }

    private async Task<PreparedInputGateResult> WaitForPreparedInputReadyAsync(
        AutomaticPendingSend pending,
        long expectedAudioVersion,
        AdapterPageState expectedPageState,
        CancellationToken cancellationToken)
    {
        AdapterIdleProbeResult? lastProbe = null;
        while (DateTimeOffset.UtcNow < pending.ExpiresAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_browserSession is null || _adapter is null)
            {
                return new(false, false, false, lastProbe);
            }

            var audio = _browserSession.GetAudioSnapshot(DateTimeOffset.UtcNow);
            if (!audio.IsKnown || audio.IsPlaying || audio.Version != expectedAudioVersion ||
                audio.SilentDuration < AutomaticPendingSend.RequiredIdleStability)
            {
                return new(false, true, false, lastProbe);
            }

            lastProbe = await _adapter.ProbeIdleAsync(cancellationToken);
            if (lastProbe.Status == WebAdapterStatus.AdapterInvalid)
            {
                return new(false, false, false, lastProbe);
            }

            if (lastProbe.Diagnostics.PageState != expectedPageState)
            {
                return new(false, true, false, lastProbe);
            }

            if (lastProbe.IsIdle)
            {
                var attachmentPresent = lastProbe.Diagnostics.PageState is
                    AdapterPageState.ComposerWithAttachment or
                    AdapterPageState.VoiceComposerWithAttachment;
                return attachmentPresent
                    ? new(true, false, false, lastProbe)
                    : new(false, false, false, lastProbe);
            }

            await Task.Delay(PendingProbeInterval, cancellationToken);
        }

        return new(false, false, true, lastProbe);
    }

    private sealed record PreparedInputGateResult(
        bool Ready,
        bool VoiceStateChanged,
        bool Expired,
        AdapterIdleProbeResult? Probe);

    private static SubmissionOutcome MapAdapterStatus(WebAdapterStatus status) => status switch
    {
        WebAdapterStatus.QuotaReached => SubmissionOutcome.QuotaReached,
        WebAdapterStatus.AdapterInvalid => SubmissionOutcome.AdapterInvalid,
        _ => SubmissionOutcome.OrdinaryFailure,
    };

    private void CompleteSubmission(SubmissionOutcome outcome)
    {
        if (_stateMachine.State != GameMateState.Sending)
        {
            return;
        }

        switch (outcome)
        {
            case SubmissionOutcome.Succeeded:
                ApplyTrigger(GameMateTrigger.SendSucceeded);
                _reminderTracker?.RecordSuccessfulImage();
                RaiseConversationReminderIfDue();
                break;
            case SubmissionOutcome.QuotaReached:
                ApplyTrigger(GameMateTrigger.QuotaReached);
                _runCancellation?.Cancel();
                AddActivity(T("检测到图片额度限制，已进入纯语音模式，不会重试或新建对话绕过限制。", "Image quota was detected. Voice-only mode is active; no retry or new-chat bypass will be attempted."));
                _trayIcon?.Notify("OpenGameMate", T("已进入纯语音模式。", "Voice-only mode is active."));
                break;
            case SubmissionOutcome.AdapterInvalid:
                ApplyTrigger(GameMateTrigger.AdapterInvalid);
                _runCancellation?.Cancel();
                AddActivity(T("网页适配失效，已立即暂停且不会随机点击。", "The webpage adapter failed closed; no random click was attempted."));
                _trayIcon?.Notify("OpenGameMate", T("网页适配失效，已暂停。", "Web adapter failed; paused."));
                break;
            default:
                ApplyTrigger(GameMateTrigger.SendFailed);
                AddActivity(T("本次发送失败；不会立即重试，下个周期再尝试。", "This send failed; no immediate retry will occur."));
                break;
        }

        _ = SafeLogAsync(
            "submission.completed",
            outcome == SubmissionOutcome.Succeeded ? DiagnosticLevel.Information : DiagnosticLevel.Warning,
            errorCode: outcome == SubmissionOutcome.Succeeded ? null : outcome.ToString(),
            success: outcome == SubmissionOutcome.Succeeded);
    }

    private void RaiseConversationReminderIfDue()
    {
        if (_reminderTracker?.TryRaiseReminder(DateTimeOffset.UtcNow, out var reason) != true)
        {
            return;
        }

        AddActivity(T(
            $"会话提醒已触发（{reason}）：请自行新建 ChatGPT 对话，再选择是否发送角色提醒。",
            $"Conversation reminder ({reason}): create a new ChatGPT chat, then choose whether to send a role reminder."));
        _trayIcon?.Notify(
            "OpenGameMate",
            T("建议自行新建 ChatGPT 对话。", "Consider creating a new ChatGPT chat."));
        _ = SafeLogAsync("conversation.reminder", DiagnosticLevel.Information, errorCode: reason?.ToString());
    }

    private void PauseResumeButton_Click(object sender, RoutedEventArgs e) => PauseOrResume();

    private void PauseOrResume()
    {
        if (_stateMachine.State == GameMateState.Running)
        {
            ApplyTrigger(GameMateTrigger.Pause);
            AddActivity(T("已暂停；不会累计空闲时间。", "Paused; idle time is not accumulated."));
        }
        else if (_stateMachine.State == GameMateState.Paused)
        {
            ApplyTrigger(GameMateTrigger.Resume);
            AddActivity(T("已恢复；重新等待连续 10 秒空闲。", "Resumed; waiting for a fresh 10-second idle window."));
        }

        UpdateUi();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopRun();

    private void StopRun()
    {
        _runCancellation?.Cancel();
        if (_stateMachine.CanApply(GameMateTrigger.Stop))
        {
            ApplyTrigger(GameMateTrigger.Stop);
            AddActivity(T("已停止自动截图；Voice 窗口由你决定是否继续或关闭。", "Automatic capture stopped; you control whether Voice remains open."));
            _ = SafeLogAsync("run.stopped", DiagnosticLevel.Information, success: true);
        }

        try
        {
            _capture.DeleteTemporaryScreenshot();
        }
        catch
        {
        }

        UpdateUi();
    }

    private void RetryAdapterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stateMachine.State != GameMateState.AdapterError || _browserSession?.IsOnChatGpt() != true)
        {
            return;
        }

        _adapter = new ChatGptWebAdapter(_browserSession, ChatGptAdapterRules.BuiltIn);
        ApplyTrigger(GameMateTrigger.AdapterRecovered);
        AddActivity(T("已重新加载内置规则。请确认 Voice 后再开始；若仍失败请导出诊断。", "Built-in rules reloaded. Confirm Voice before restarting; export diagnostics if it still fails."));
        UpdateUi();
    }

    private void HandleAdapterFailure(WebAdapterStatus status, string operation)
    {
        AddActivity(T($"网页操作失败：{status}。", $"Web operation failed: {status}."));
        _ = SafeLogAsync("adapter.operation-failed", DiagnosticLevel.Warning, errorCode: operation);
        if (status == WebAdapterStatus.AdapterInvalid && _stateMachine.CanApply(GameMateTrigger.AdapterInvalid))
        {
            ApplyTrigger(GameMateTrigger.AdapterInvalid);
            _runCancellation?.Cancel();
        }
    }

    private void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = T("导出 OpenGameMate 诊断包", "Export OpenGameMate diagnostics"),
            Filter = "ZIP (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"OpenGameMate-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var count = DiagnosticExportService.Export(_paths.LogsDirectory, dialog.FileName);
            AddActivity(T($"已导出诊断包（{count} 个日志文件）。", $"Diagnostics exported ({count} log files)."));
            _ = SafeLogAsync("diagnostics.exported", DiagnosticLevel.Information, success: true);
        }
        catch (Exception exception)
        {
            _ = ReportExceptionAsync("diagnostics.export-failed", "diagnostics-export", exception);
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(_paths.RootDirectory);
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            _ = ReportExceptionAsync("data-folder.open-failed", "open-data-folder", exception);
        }
    }

    private void BrowserDataHelpButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            T(
                $"浏览器登录数据位于：\n{_paths.WebViewUserDataDirectory}\n\n仓库安全规则禁止程序递归批量删除目录。若要清除登录数据，请先完全退出 OpenGameMate，等待 WebView2 进程结束，再手动删除该目录。",
                $"Browser sign-in data is stored at:\n{_paths.WebViewUserDataDirectory}\n\nRepository safety rules prohibit recursive bulk deletion. To clear sign-in data, fully exit OpenGameMate, wait for WebView2 processes to end, then delete this directory manually."),
            T("浏览器数据清理", "Browser-data cleanup"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ApplyTrigger(GameMateTrigger trigger)
    {
        var transition = _stateMachine.Apply(trigger);
        _ = SafeLogAsync(
            "state.transition",
            DiagnosticLevel.Information,
            errorCode: $"{transition.PreviousState}-{trigger}-{transition.CurrentState}");
        UpdateUi();
    }

    private void UpdateUi()
    {
        if (!IsInitialized)
        {
            return;
        }

        var state = _stateMachine.State;
        StateText.Text = state.ToString();
        StateIndicator.Fill = state switch
        {
            GameMateState.Running => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 139, 91)),
            GameMateState.Sending => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 111, 237)),
            GameMateState.AdapterError => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(190, 76, 65)),
            GameMateState.VoiceOnly => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 132, 41)),
            _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 135, 132)),
        };
        var browserAvailable = _browserSession is not null && _browserView is not null;
        OpenBrowserButton.IsEnabled = !browserAvailable && !_browserInitializing;
        ShowBrowserButton.IsEnabled = browserAvailable;
        HideBrowserButton.IsEnabled = browserAvailable;
        CloseBrowserButton.IsEnabled = browserAvailable;
        BrowserPaneStatusText.Text = _browserInitializing
            ? T("正在初始化", "Initializing")
            : browserAvailable
                ? T("已连接", "Connected")
                : T("尚未打开", "Not opened");
        ConfirmVoiceButton.IsEnabled = browserAvailable &&
            state is GameMateState.BrowserReady or GameMateState.Stopped or GameMateState.Idle;
        var canSendRole = !_auxiliaryOperation && _adapter is not null &&
            state is GameMateState.BrowserReady or GameMateState.Ready or GameMateState.Paused;
        SendFullRoleButton.IsEnabled = canSendRole;
        SendShortRoleButton.IsEnabled = canSendRole;
        NewConversationButton.IsEnabled = state is GameMateState.Ready or GameMateState.Running or GameMateState.Paused;
        StartButton.IsEnabled = state == GameMateState.Ready && !_auxiliaryOperation;
        SendNowButton.IsEnabled = state == GameMateState.Running;
        PauseResumeButton.IsEnabled = state is GameMateState.Running or GameMateState.Paused;
        PauseResumeButton.Content = state == GameMateState.Paused
            ? T("恢复", "Resume")
            : T("暂停", "Pause");
        StopButton.IsEnabled = IsRunRelatedState(state) || state == GameMateState.AdapterError;
        RetryAdapterButton.IsEnabled = state == GameMateState.AdapterError;
        RunStatusText.Text = StateDescription(state);

        _trayIcon?.Update(
            IsChinese,
            state.ToString(),
            browserAvailable,
            state == GameMateState.Running,
            state is GameMateState.Running or GameMateState.Paused,
            state == GameMateState.Paused,
            StopButton.IsEnabled);
    }

    private string StateDescription(GameMateState state) => state switch
    {
        GameMateState.Idle => T("等待打开 ChatGPT。", "Waiting for ChatGPT."),
        GameMateState.BrowserReady => T("浏览器已就绪；请登录、开启 Voice 并确认。", "Browser ready; sign in, start Voice, and confirm."),
        GameMateState.Ready => T("Voice 已确认；可发送角色设定或开始陪玩。", "Voice confirmed; initialize the role or start."),
        GameMateState.Running => T(
            "正在运行；网页音频停止且页面空闲稳定 10 秒后自动发送。",
            "Running; automatic capture waits for 10 seconds of stable page and web-audio silence."),
        GameMateState.Sending => T("正在处理一张截图；不会并发或排队。", "Processing one screenshot; no concurrency or queue."),
        GameMateState.Paused => T("已暂停；恢复后重新等待 10 秒空闲。", "Paused; a fresh 10-second idle window is required after resuming."),
        GameMateState.VoiceOnly => T("图片额度受限；保持纯语音，不再自动发送。", "Image quota limited; Voice-only mode, no more automatic sends."),
        GameMateState.AdapterError => T("网页适配失效；已停止发送。", "Web adapter failed; sending is stopped."),
        GameMateState.Stopped => T("已停止；若要重新开始，请再次确认 Voice。", "Stopped; confirm Voice again to restart."),
        _ => state.ToString(),
    };

    private static bool IsRunRelatedState(GameMateState state) =>
        state is GameMateState.Running or GameMateState.Sending or GameMateState.Paused or GameMateState.VoiceOnly;

    private void AddActivity(string message)
    {
        ActivityList.Items.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (ActivityList.Items.Count > 200)
        {
            ActivityList.Items.RemoveAt(0);
        }

        ActivityList.ScrollIntoView(ActivityList.Items[^1]);
    }

    private async Task SafeLogAsync(
        string eventName,
        DiagnosticLevel level,
        string? errorCode = null,
        bool? success = null,
        int? imageWidth = null,
        int? imageHeight = null,
        long? fileSizeBytes = null,
        string? exceptionType = null,
        AdapterDiagnostics? adapterDiagnostics = null,
        DateTimeOffset? scheduledAt = null,
        DateTimeOffset? pendingCreatedAt = null,
        DateTimeOffset? deferredAt = null,
        string? deferredReason = null,
        DateTimeOffset? idleCandidateAt = null,
        long? idleStableDurationMs = null,
        WebAudioState? audioState = null,
        long? audioSilentDurationMs = null,
        DateTimeOffset? attachStartedAt = null,
        DateTimeOffset? textSetAt = null,
        DateTimeOffset? submitStartedAt = null,
        bool? voiceStateChangedAfterAttach = null,
        bool? skippedBecauseConversationBusy = null,
        DateTimeOffset? pendingExpiredAt = null)
    {
        try
        {
            await _logger.AppendAsync(new DiagnosticEvent(
                DateTimeOffset.UtcNow,
                level,
                eventName,
                _stateMachine.State,
                errorCode,
                success,
                imageWidth,
                imageHeight,
                fileSizeBytes,
                exceptionType,
                adapterDiagnostics,
                scheduledAt,
                pendingCreatedAt,
                deferredAt,
                deferredReason,
                idleCandidateAt,
                idleStableDurationMs,
                audioState,
                audioSilentDurationMs,
                attachStartedAt,
                textSetAt,
                submitStartedAt,
                voiceStateChangedAfterAttach,
                skippedBecauseConversationBusy,
                pendingExpiredAt));
        }
        catch
        {
        }
    }

    private Task LogAdapterDiagnosticsAsync(SubmissionResult submission) =>
        SafeLogAsync(
            "adapter.submit-probe",
            submission.Status == WebAdapterStatus.Succeeded
                ? DiagnosticLevel.Information
                : DiagnosticLevel.Warning,
            errorCode: submission.Code,
            success: submission.Status == WebAdapterStatus.Succeeded,
            adapterDiagnostics: submission.Diagnostics);

    private async Task ReportExceptionAsync(string eventName, string errorCode, Exception exception)
    {
        AddActivity(T($"操作失败：{errorCode}（{exception.GetType().Name}）。", $"Operation failed: {errorCode} ({exception.GetType().Name})."));
        await SafeLogAsync(
            eventName,
            DiagnosticLevel.Error,
            errorCode,
            success: false,
            exceptionType: exception.GetType().Name);
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnUi(Action action) => Dispatcher.BeginInvoke(action);

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting && IsRunRelatedState(_stateMachine.State))
        {
            var result = MessageBox.Show(
                this,
                T("陪玩仍在运行。确认停止并退出？", "Companion mode is running. Stop and exit?"),
                "OpenGameMate",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        _isExiting = true;
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = null;
        CloseEmbeddedBrowser(applyStateTransition: false);
        try
        {
            _capture.DeleteTemporaryScreenshot();
        }
        catch
        {
        }
        _trayIcon?.Dispose();
        _trayIcon = null;
        _globalHotKeyManager?.Dispose();
        _globalHotKeyManager = null;
    }
}
