[English](README.md)

<p align="center">
  <img src="assets/OpenGameMate.AppIcon.png" width="128" alt="OpenGameMate 图标">
</p>

<h1 align="center">OpenGameMate</h1>

<p align="center"><strong>让 ChatGPT Voice 看着你玩游戏，并像连麦网友一样陪你聊天。</strong></p>

<p align="center">
  <a href="https://github.com/Remersal/OpenGameMate/actions/workflows/ci.yml"><img src="https://github.com/Remersal/OpenGameMate/actions/workflows/ci.yml/badge.svg" alt="CI 构建"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Remersal/OpenGameMate" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/platform-Windows-0078D4?logo=windows11&amp;logoColor=white" alt="Windows">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&amp;logoColor=white" alt=".NET 8">
  <a href="https://github.com/Remersal/OpenGameMate/releases/tag/v0.1.0"><img src="https://img.shields.io/github/v/release/Remersal/OpenGameMate?include_prereleases&amp;label=release" alt="最新版本"></a>
</p>

<p align="center">
  <a href="https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/OpenGameMate-Setup-0.1.0.exe"><strong>安装版</strong></a> ·
  <a href="https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/OpenGameMate-v0.1.0-win-x64.zip"><strong>便携版</strong></a> ·
  <a href="#实际效果与演示"><strong>演示</strong></a> ·
  <a href="docs/RELEASE_NOTES_0.1.0.md"><strong>文档</strong></a> ·
  <a href="README.md"><strong>English</strong></a>
</p>

<p align="center">
  OpenGameMate 是一款开源 Windows 桌面应用，使用你自行登录的 ChatGPT 网页和 Voice，不需要开发者 API Key。<br>
  对话持续空闲后，它会发送最新游戏画面，让 AI 根据画面接话、吐槽或自然地主动开启话题。<br>
  登录、Voice、屏幕捕获以及陪玩模式的启动与停止始终由你控制。
</p>

> **OpenGameMate is an independent open-source project and is not affiliated with or endorsed by OpenAI.**

## 实际效果与演示

**Demo coming soon。** 仓库目前还没有经过隐私检查的真实录屏，因此这里不会放置模拟演示、假界面或空白占位图。

如果你愿意帮助录制真实流程，请按照 [20～40 秒演示录制指南](docs/DEMO_RECORDING_GUIDE.md) 操作。

## 核心功能

- 配合由你启动和控制的 ChatGPT Voice 对话使用。
- 对话持续空闲后，自动用最新游戏画面更新对话。
- Voice 或网页繁忙时延后自动发送，尽量避免打断。
- 支持从主界面、托盘或可配置的全局快捷键手动截图。
- 使用独立的 WebView2 登录环境打开 ChatGPT。
- 不读取聊天记录、模型回复、Cookie、Token 或语音内容。

## 下载与快速开始

> **v0.1.0 Early Preview 已可下载。** 这是早期公开测试版，不代表所有环境都能兼容。

- [下载 Windows x64 安装版](https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/OpenGameMate-Setup-0.1.0.exe)
- [下载 Windows x64 便携版 ZIP](https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/OpenGameMate-v0.1.0-win-x64.zip)
- [查看 SHA-256 校验值](https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/SHA256SUMS.txt)
- [查看 GitHub Release](https://github.com/Remersal/OpenGameMate/releases/tag/v0.1.0)

安装包目前**没有 Authenticode 数字签名**，Windows 可能显示未知发布者或信誉提示。安装时还必须明确确认可能存在的游戏账号风险。便携版依赖 .NET 8 Desktop Runtime 和 WebView2 Runtime。

安装或解压后：

1. 启动 OpenGameMate。程序不会自动打开 ChatGPT，也不会自动截图。
2. 点击“打开 ChatGPT”，自行登录并开启 Voice。
3. 回到 OpenGameMate 确认 Voice 已开启，然后启动陪玩模式并接受主显示器捕获提示。
4. 继续玩游戏和聊天。达到所选空闲时间后，OpenGameMate 可以发送最新画面；你也可以随时手动截图。

## 工作原理

```text
你正常玩游戏并与 AI 聊天
        ↓
ChatGPT Voice 和网页进入持续空闲状态
        ↓
OpenGameMate 捕获最新主显示器画面
        ↓
图片和一段简短提示被发送到你的 ChatGPT 对话
        ↓
ChatGPT 根据画面接话或主动开启新话题
```

空闲等待可选 10、15、30 或 60 秒，默认 10 秒。完整等待结束前不会截图或操作输入区域；同一连续空闲段最多触发一次，恢复活动后才会重新计时。

## 隐私与安全

- 登录、麦克风授权、Voice 启动和陪玩模式启动都需要用户主动参与。
- 只捕获主显示器，并仅使用一个临时 PNG；提交尝试结束后会删除该文件。
- 不读取 ChatGPT 回复、历史、账号资料、Cookie、Token、完整网页 HTML、麦克风音频或系统音频。
- 不注入游戏、不读取游戏内存、不模拟全局输入，也不绕过反作弊、验证、额度或受保护内容。
- 第三方陪玩或自动化工具仍可能与游戏或平台规则冲突，并可能导致警告、限制、暂封或封禁。

使用前请阅读完整的 [隐私说明](PRIVACY.md) 和 [安全策略](SECURITY.md)。归档的 [Phase 0 可行性报告](docs/PHASE0_FEASIBILITY_REPORT.md) 保留了早期技术证据与限制。

## 已知限制

- 需要 Windows 10 19041 或更高版本；打包版本还需要 .NET 8 Desktop Runtime 和较新的 Microsoft Edge WebView2 Runtime。
- 当前只捕获主显示器；独占全屏、受保护内容、显示驱动和反作弊行为可能影响结果。
- ChatGPT 网页结构、Voice 与图片上传能力、账号额度、地区可用性和平台政策都可能变化。
- 远程规则更新仍在等待维护者签名信任锚，当前使用内置网页规则。
- 定向真实测试和用户 RC 验收已经通过，但仍需要收集更多系统与游戏的公开兼容性报告。

详细证据边界请参阅 [v0.1.0 Release Notes](docs/RELEASE_NOTES_0.1.0.md) 和 [测试计划](docs/V0.1_TEST_PLAN.md)。

## 从源码构建

要求：Windows 10 19041 或更高版本、[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，以及较新的 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。

```powershell
dotnet restore .\OpenGameMate.sln
.\scripts\Validate-ReleaseMetadata.ps1
dotnet build .\OpenGameMate.sln -c Release
dotnet test .\tests\OpenGameMate.Tests\OpenGameMate.Tests.csproj -c Release --no-build
dotnet run --project .\src\OpenGameMate.App\OpenGameMate.App.csproj -c Release
```

便携数据模式：

```powershell
OpenGameMate.App.exe --portable
```

安装模式把运行数据保存在 `%LocalAppData%\OpenGameMate\`；便携模式使用可执行文件旁的 `data\`。开发与打包细节请参阅 [CONTRIBUTING.md](CONTRIBUTING.md) 和 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

## 贡献方式

欢迎提交 Bug、兼容性结果、文档修正和范围明确的贡献。

- [提交 Bug 或兼容性报告](https://github.com/Remersal/OpenGameMate/issues/new/choose)
- [查看已有 Issues](https://github.com/Remersal/OpenGameMate/issues)
- 修改代码或工作流前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。
- 安全漏洞请按照 [SECURITY.md](SECURITY.md) 的私密流程报告；不要在公开 Issue 中提交凭据、Token、聊天内容或私人截图。

OpenGameMate 目前仍是早期公开测试版，欢迎提交兼容性反馈、Issue 和贡献。如果你希望继续关注项目，可以点一个 Star。

## License 与非官方声明

OpenGameMate 使用 [MIT License](LICENSE)。

**OpenGameMate is an independent open-source project and is not affiliated with or endorsed by OpenAI.** ChatGPT 与 OpenAI 是其各自所有者的商标。
