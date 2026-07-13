# OpenGameMate Phase 0 PoC

本仓库当前只实现开发文档规定的 Phase 0 技术可行性验证，不是完整应用。

PoC 验证以下边界：

- WPF / .NET 8 / WebView2 基础结构；
- 使用独立用户数据目录打开 ChatGPT 官方网页；
- 由用户本人完成登录与麦克风授权；
- Voice 正常结束应使用 ChatGPT 网页退出控件；“隐藏 ChatGPT”不会结束语音，必要时使用“关闭 ChatGPT（结束语音）”销毁 WebView2 会话；
- 使用 Windows Graphics Capture 捕获主显示器并限制为不超过 1920×1080；
- 尝试在 WebView2 不在前台、鼠标不被移动的情况下加入一张无隐私测试图片和固定文字；
- 仅在用户再次明确确认后尝试提交；
- 不读取 ChatGPT 回复、聊天记录、Cookie、登录令牌或完整页面 HTML。

## 环境要求

- Windows 10 1903 或更新版本；
- .NET 8 SDK；
- Microsoft Edge WebView2 Evergreen Runtime；
- 可访问 `https://chatgpt.com/` 的网络环境。

本工作区已在 `.dotnet/` 中准备隔离的 .NET SDK 8.0.422；该目录被 Git 忽略，不属于源码交付物。

如果系统 WebView2 版本过旧或无法自动更新，可先安装仓库外的隔离 Fixed Runtime（约 300 MB 下载）：

```powershell
& .\scripts\Install-Phase0FixedWebView2.ps1
```

脚本只使用微软官方、带有效 Microsoft 签名的 WebView2 150.0.4078.65 x64 CAB，并展开到 `%LocalAppData%\OpenGameMate\Phase0\WebView2Runtime`。它不修改系统 WebView2，也不会批量删除文件。PoC 会优先选择该目录，找不到时才回退到系统 Evergreen Runtime。

## 构建与启动

在 PowerShell 中进入仓库根目录：

```powershell
$env:DOTNET_CLI_HOME = Join-Path $env:TEMP 'OpenGameMate-dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& .\.dotnet\dotnet.exe restore .\OpenGameMate.sln
& .\.dotnet\dotnet.exe build .\OpenGameMate.sln --configuration Debug
& .\src\OpenGameMate.App\bin\Debug\net8.0-windows10.0.19041.0\OpenGameMate.App.exe
```

如果系统已经安装 .NET 8 SDK，可将 `.\.dotnet\dotnet.exe` 替换为 `dotnet`。

详细人工步骤见 [Phase 0 测试步骤](docs/PHASE0_TEST_STEPS.md)，当前结论见 [Phase 0 可行性报告](docs/PHASE0_FEASIBILITY_REPORT.md)。

## 数据位置

- 独立 WebView2 用户数据：`%LocalAppData%\OpenGameMate\Phase0\UserData`
- 可选隔离 WebView2 Runtime：`%LocalAppData%\OpenGameMate\Phase0\WebView2Runtime`
- 最小验证日志：`%LocalAppData%\OpenGameMate\Phase0\phase0-results.jsonl`
- 主显示器临时截图：`%TEMP%\OpenGameMate\primary-display.png`
- 无隐私上传测试图：`%TEMP%\OpenGameMate\phase0-upload.png`

日志只保存检查项、通过/失败状态和非敏感限制信息，不保存网页正文或账号数据。
