# OpenGameMate v0.1.0

OpenGameMate 是一个开源 Windows 桌面学习项目。它在应用主窗口内嵌的独立 WebView2 环境中打开 ChatGPT，由用户自行登录并开启 Voice。用户明确开始陪玩后，程序仅在 ChatGPT 网页音频停止、页面持续安全空闲达到所选的 10、15、30 或 60 秒时，捕获最新主显示器画面并提交简短提示。

OpenGameMate 是独立的开源项目，与 OpenAI 无隶属关系，也未获得 OpenAI 的认可或背书。

## 安全与隐私

- 不读取 ChatGPT 回复、聊天记录、账号、Cookie、Token、完整 HTML 或音频。
- 不注入游戏、不读取游戏内存、不模拟全局键鼠，也不绕过反作弊、验证码、额度或其他保护措施。
- 截图仅临时保存于应用专用位置，并在每次提交尝试结束后删除。
- 首次开始陪玩前，用户必须确认捕获整个主显示器可能带来的隐私风险。
- 第三方陪玩或自动化工具可能违反游戏或平台规则，并可能导致警告、限制或封禁账号；请自行确认相关规则并承担使用风险。

详细说明请参阅 [PRIVACY.md](PRIVACY.md)、[SECURITY.md](SECURITY.md) 和 [Phase 0 可行性报告](docs/PHASE0_FEASIBILITY_REPORT.md)。

## 使用方法

1. 启动 OpenGameMate。程序不会自动打开浏览器或捕获屏幕。
2. 点击“打开 ChatGPT”，自行登录并开启 Voice。
3. 返回主界面，确认 Voice 已开启。
4. 可选：明确发送一次完整角色设定。
5. 点击“开始陪玩”，阅读并接受整屏捕获提示。
6. 使用主界面、托盘或可配置的全局快捷键主动截图。
7. 在陪玩控制区选择自动截图前的连续空闲等待时间：10、15、30 或 60 秒。

## 从源码构建

要求：Windows 10 19041 或更高版本、.NET 8 SDK，以及最新的 Microsoft Edge WebView2 Runtime。

```powershell
dotnet restore .\OpenGameMate.sln
.\scripts\Validate-ReleaseMetadata.ps1
dotnet build .\OpenGameMate.sln -c Release
dotnet test .\tests\OpenGameMate.Tests\OpenGameMate.Tests.csproj -c Release --no-build
```

从源码运行：

```powershell
dotnet run --project .\src\OpenGameMate.App\OpenGameMate.App.csproj -c Release
```

更多构建、打包和已知限制请参阅英文版 [README.md](README.md)。项目采用 [MIT License](LICENSE)。
