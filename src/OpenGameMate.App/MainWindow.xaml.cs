using System.Text;
using System.Windows;
using System.ComponentModel;
using System.IO;
using System.Windows.Interop;
using OpenGameMate.Adapters;
using OpenGameMate.Browser;
using OpenGameMate.Capture;
using OpenGameMate.Core;
using OpenGameMate.Diagnostics;

namespace OpenGameMate.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string TestMessage =
        "OpenGameMate Phase 0 background input test. Please do not respond; this message is part of a user-approved feasibility test.";

    private readonly string _userDataFolder;
    private readonly string? _browserExecutableFolder;
    private readonly Phase0EvidenceRecorder _recorder;
    private readonly PrimaryDisplayCapture _primaryDisplayCapture = new();
    private BrowserWindow? _browserWindow;
    private ChatGptBrowserSession? _browserSession;
    private IAiWebAdapter? _adapter;
    private string? _currentTestImagePath;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenGameMate",
            "Phase0");
        _userDataFolder = Path.Combine(localRoot, "UserData");
        _browserExecutableFolder = ChatGptBrowserSession.FindFixedRuntimeFolder(
            Path.Combine(localRoot, "WebView2Runtime"));
        _recorder = new Phase0EvidenceRecorder(Path.Combine(localRoot, "phase0-results.jsonl"));
        UserDataFolderText.Text = "独立用户数据目录：%LocalAppData%\\OpenGameMate\\Phase0\\UserData";
        AddUiEvidence(_browserExecutableFolder is null
            ? "未找到隔离 Fixed Runtime，将使用系统 Evergreen WebView2。"
            : "已选择隔离 Fixed Runtime；不会使用本机过期的系统 WebView2 131。" );
        AddUiEvidence("等待初始化。真实登录、Voice 与网页行为必须由用户手动验证。");
    }

    private async void InitializeBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_browserWindow is null)
            {
                _browserWindow = new BrowserWindow();
                _browserWindow.Closed += BrowserWindow_Closed;
                // WebView2 needs a realized WPF host (and therefore an HWND)
                // before EnsureCoreWebView2Async can complete reliably.
                _browserWindow.ShowForUser();
                _browserSession = new ChatGptBrowserSession(
                    _browserWindow.WebView,
                    _userDataFolder,
                    PromptForMicrophoneAsync,
                    _browserExecutableFolder);
                _browserSession.StatusChanged += (_, message) => Dispatcher.Invoke(() => AddUiEvidence(message));
                await _browserSession.InitializeAsync();
                _adapter = new ChatGptWebAdapter(_browserSession);
                await RecordAsync("webview2-initialization", ValidationStatus.Passed,
                    $"Isolated UDF initialized; official ChatGPT navigation requested; runtime={_browserSession.Core.Environment.BrowserVersionString}; fixedRuntime={_browserExecutableFolder is not null}.");
            }

            _browserWindow.ShowForUser();
            InitializeBrowserButton.IsEnabled = false;
        }
        catch (Exception exception)
        {
            AddUiEvidence($"初始化失败：{exception.GetType().Name} — {exception.Message}");
            await RecordAsync("webview2-initialization", ValidationStatus.Failed,
                $"Initialization failed: {exception.GetType().Name}.");
        }
    }

    private void ShowBrowserButton_Click(object sender, RoutedEventArgs e) => _browserWindow?.ShowForUser();

    private void HideBrowserButton_Click(object sender, RoutedEventArgs e) => _browserWindow?.Hide();

    private void CloseBrowserButton_Click(object sender, RoutedEventArgs e) => _browserWindow?.Close();

    private void BrowserWindow_Closed(object? sender, EventArgs e)
    {
        _browserWindow = null;
        _browserSession = null;
        _adapter = null;
        InitializeBrowserButton.IsEnabled = true;
        if (!_isExiting)
        {
            AddUiEvidence("ChatGPT WebView2 已关闭；活动语音和麦克风会话已终止。独立用户数据目录仍保留，可重新初始化并继续登录状态。");
        }
    }

    private async void RecordLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_browserSession?.IsOnChatGpt() != true)
        {
            AddUiEvidence("无法记录登录：WebView2 尚未位于 chatgpt.com。");
            return;
        }

        await RecordAsync("user-login", ValidationStatus.Passed,
            "User manually confirmed login in the isolated official ChatGPT WebView2 session.");
    }

    private async void RecordMicrophoneButton_Click(object sender, RoutedEventArgs e)
    {
        if (_browserSession?.IsOnChatGpt() != true)
        {
            AddUiEvidence("无法记录麦克风：WebView2 尚未位于 chatgpt.com。");
            return;
        }

        await RecordAsync("voice-microphone", ValidationStatus.Passed,
            "User manually confirmed Voice microphone operation; no audio was captured by OpenGameMate.");
    }

    private async void PrepareBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (PrepareConsentCheckBox.IsChecked != true ||
            _adapter is null ||
            _browserWindow is null ||
            _browserSession?.IsOnChatGpt() != true)
        {
            AddUiEvidence("准备实验未启动：请先初始化、登录、打开对话并勾选同意。");
            return;
        }

        PrepareBackgroundButton.IsEnabled = false;
        try
        {
            _currentTestImagePath = TestImageFactory.Create();
            _browserWindow.Hide();
            AddUiEvidence("倒计时 5 秒：现在切换到游戏或记事本，保持鼠标不动。");
            await Task.Delay(TimeSpan.FromSeconds(5));

            var browserHandle = new WindowInteropHelper(_browserWindow).Handle.ToInt64();
            var mainHandle = new WindowInteropHelper(this).Handle.ToInt64();
            var before = NativeFocusProbe.Capture();
            if (before.ForegroundWindow == browserHandle || before.ForegroundWindow == mainHandle)
            {
                await RecordAsync("background-input", ValidationStatus.Failed,
                    "Aborted before DOM mutation because an OpenGameMate window was still foreground.");
                AddUiEvidence("实验已安全中止：倒计时结束时前台仍是 OpenGameMate 窗口。");
                return;
            }

            var result = await _adapter.PrepareInputAsync(_currentTestImagePath, TestMessage);
            var after = NativeFocusProbe.Capture();
            var focusStable = before.SameForegroundAndCursor(after);
            var passed = result.ImageAdded && result.TextInserted && focusStable;
            var detail =
                $"fileSelected={result.FileInputSelected}; previewDetected={result.AttachmentPreviewDetected}; " +
                $"textInserted={result.TextInserted}; focusAndCursorStable={focusStable}; code={result.Code}.";
            await RecordAsync("background-input", passed ? ValidationStatus.Passed : ValidationStatus.Failed, detail);

            AddUiEvidence(passed
                ? "后台加入实验通过自动检查：图片已加入输入区、文字已写入、前台焦点和鼠标未变化。请显示 ChatGPT 人工确认图片预览。"
                : $"后台加入实验失败：{detail}");
            SubmitBackgroundButton.IsEnabled = passed;
        }
        catch (Exception exception)
        {
            await RecordAsync("background-input", ValidationStatus.Failed,
                $"Unhandled failure: {exception.GetType().Name}.");
            AddUiEvidence($"后台加入实验异常：{exception.GetType().Name} — {exception.Message}");
        }
        finally
        {
            PrepareBackgroundButton.IsEnabled = true;
        }
    }

    private async void RecordPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        await RecordAsync("attachment-preview", ValidationStatus.Passed,
            "User manually confirmed that the test image preview is visible in the ChatGPT input area.");
    }

    private async void SubmitBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (SubmitConsentCheckBox.IsChecked != true || _adapter is null || _browserWindow is null)
        {
            AddUiEvidence("提交实验未启动：需要明确勾选发送确认。");
            return;
        }

        SubmitBackgroundButton.IsEnabled = false;
        try
        {
            _browserWindow.Hide();
            AddUiEvidence("提交倒计时 5 秒：现在切回游戏或记事本，保持鼠标不动。");
            await Task.Delay(TimeSpan.FromSeconds(5));

            var browserHandle = new WindowInteropHelper(_browserWindow).Handle.ToInt64();
            var mainHandle = new WindowInteropHelper(this).Handle.ToInt64();
            var before = NativeFocusProbe.Capture();
            if (before.ForegroundWindow == browserHandle || before.ForegroundWindow == mainHandle)
            {
                await RecordAsync("background-submit", ValidationStatus.Failed,
                    "Aborted before submission because an OpenGameMate window was still foreground.");
                AddUiEvidence("提交实验已安全中止：倒计时结束时前台仍是 OpenGameMate 窗口。");
                return;
            }

            var result = await _adapter.SubmitAsync();
            var after = NativeFocusProbe.Capture();
            var focusStable = before.SameForegroundAndCursor(after);
            var submittedStateObserved = result.ComposerCleared || result.AttachmentCleared;
            var passed = result.TriggerInvoked && submittedStateObserved && focusStable;
            var detail =
                $"triggerInvoked={result.TriggerInvoked}; composerCleared={result.ComposerCleared}; " +
                $"attachmentCleared={result.AttachmentCleared}; focusAndCursorStable={focusStable}; code={result.Code}.";
            await RecordAsync("background-submit", passed ? ValidationStatus.Passed : ValidationStatus.Failed, detail);
            AddUiEvidence(passed ? "后台提交实验通过允许的状态检查。" : $"后台提交实验失败：{detail}");

            DeleteCurrentTestImage();
            SubmitConsentCheckBox.IsChecked = false;
        }
        catch (Exception exception)
        {
            await RecordAsync("background-submit", ValidationStatus.Failed,
                $"Unhandled failure: {exception.GetType().Name}.");
            AddUiEvidence($"后台提交实验异常：{exception.GetType().Name} — {exception.Message}");
        }
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureButton.IsEnabled = false;
        try
        {
            var result = await _primaryDisplayCapture.CaptureAsync();
            var dimensionsValid = result.OutputWidth <= 1920 && result.OutputHeight <= 1080;
            var status = dimensionsValid ? ValidationStatus.Passed : ValidationStatus.Failed;
            var detail =
                $"source={result.SourceWidth}x{result.SourceHeight}; output={result.OutputWidth}x{result.OutputHeight}; " +
                $"bytes={result.FileBytes}; fixedTemporaryPath=true.";
            CaptureResultText.Text =
                $"源：{result.SourceWidth}×{result.SourceHeight}；输出：{result.OutputWidth}×{result.OutputHeight}；" +
                $"大小：{result.FileBytes:N0} 字节；路径：%TEMP%\\OpenGameMate\\primary-display.png";
            await RecordAsync("primary-display-capture", status, detail);
        }
        catch (Exception exception)
        {
            CaptureResultText.Text = $"捕获失败：{exception.GetType().Name} — {exception.Message}";
            await RecordAsync("primary-display-capture", ValidationStatus.Failed,
                $"Capture failed: {exception.GetType().Name}.");
        }
        finally
        {
            CaptureButton.IsEnabled = true;
        }
    }

    private async void DeleteScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        var deleted = _primaryDisplayCapture.DeleteTemporaryScreenshot();
        CaptureResultText.Text = deleted ? "临时截图已删除。" : "没有临时截图需要删除。";
        await RecordAsync("temporary-screenshot-cleanup", ValidationStatus.Passed,
            deleted ? "The single fixed temporary screenshot was deleted." : "No temporary screenshot existed.");
    }

    private Task<bool> PromptForMicrophoneAsync(Uri uri)
    {
        var choice = MessageBox.Show(
            this,
            $"ChatGPT 官方页面请求麦克风权限：{uri.Host}\n\n是否允许？OpenGameMate 不读取或录制音频。",
            "ChatGPT 麦克风权限",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return Task.FromResult(choice == MessageBoxResult.Yes);
    }

    private async Task RecordAsync(string checkId, ValidationStatus status, string safeDetail)
    {
        await _recorder.AppendAsync(checkId, status, safeDetail);
        AddUiEvidence($"[{status}] {checkId}: {safeDetail}");
    }

    private void AddUiEvidence(string message)
    {
        EvidenceList.Items.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        EvidenceList.ScrollIntoView(EvidenceList.Items[^1]);
    }

    private void DeleteCurrentTestImage()
    {
        if (_currentTestImagePath is null || !File.Exists(_currentTestImagePath))
        {
            _currentTestImagePath = null;
            return;
        }

        File.Delete(_currentTestImagePath);
        _currentTestImagePath = null;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        DeleteCurrentTestImage();
        _primaryDisplayCapture.DeleteTemporaryScreenshot();
        if (_browserWindow is not null)
        {
            _browserWindow.Close();
        }
    }
}
