# OpenGameMate v0.1.0

OpenGameMate 是一个开源 Windows 桌面学习项目：它在主窗口内嵌的独立 WebView2 环境中打开 ChatGPT，由用户自行登录和开启 Voice。用户明确开始后，只有在 ChatGPT 网页音频停止且页面连续安全空闲约 10 秒时，程序才捕获最新主显示器画面并提交简短提示；同一空闲段只触发一次，恢复交谈后重新计时。

OpenGameMate is an open-source Windows desktop learning project. It opens ChatGPT in an isolated WebView2 session under the user's control. After the user explicitly starts companion mode, a fresh primary-display image and short prompt are submitted only after ChatGPT web audio stops and the page remains safely idle for about 10 seconds. A continuous idle window triggers only once and is rearmed after conversation activity resumes.

OpenGameMate is not an OpenAI product, official extension, or unlimited-quota tool.

## 安全与隐私 / Safety and privacy

- 不读取 ChatGPT 回复、聊天记录、账号、Cookie、Token、完整 HTML 或音频。
- 不注入游戏、不读游戏内存、不模拟全局键鼠、不绕过反作弊、验证码、额度或受保护内容。
- 截图只写入一个应用临时 PNG，提交尝试结束后删除；异常重启不恢复截图或运行状态。
- 首次开始前必须确认“捕获整个主显示器”的隐私风险。
- 后台图片提交、Voice 忙碌延后、90 秒过期跳过和空闲后主动话题已完成小范围真实验证；网页结构、账号能力、独占全屏、额度和平台政策仍会变化，完整 RC 与长时间稳定性仍需单独验收。

- Does not read ChatGPT replies, history, accounts, cookies, tokens, full HTML, or audio.
- Does not inject into games, read game memory, synthesize global input, or bypass anti-cheat, verification, quotas, or protected content.
- Keeps one application temporary PNG and deletes it after each attempt; runtime capture state is not restored after a crash.
- Requires explicit acknowledgement of full-primary-display capture risk before first start.
- Background image submission, Voice-busy deferral, 90-second expiry, and proactive idle-time topics passed targeted live checks. Webpage structure, account capabilities, fullscreen modes, quotas, and platform policy can still change; full RC and soak acceptance remain separate gates.

See [PRIVACY.md](PRIVACY.md), [SECURITY.md](SECURITY.md), and the archived [Phase 0 feasibility report](docs/PHASE0_FEASIBILITY_REPORT.md).

## 使用 / Use

1. 启动 OpenGameMate。程序只显示主界面，不会自动打开浏览器或截图。
2. 点击“打开 ChatGPT”，自行登录并启动 Voice。
3. 回到主界面点击“我已开启 Voice”。
4. 可选：明确发送一次完整角色设定。
5. 点击“开始陪玩”，阅读并接受整屏捕获提示。
6. 使用主界面、托盘或可配置的全局快捷键执行主动截图；可在陪玩控制区按下新的快捷键组合进行修改。

1. Start OpenGameMate. Only the main window opens; no browser or capture starts automatically.
2. Click **Open ChatGPT**, sign in yourself, and start Voice.
3. Return and click **I started Voice**.
4. Optionally send the full role initialization explicitly.
5. Click **Start** and accept the full-display privacy warning.
6. Use the main window, tray, or configurable global hotkey for a manual capture. Press a new key combination in the companion controls to change it.

## 构建 / Build

Requirements: Windows 10 19041 or newer, .NET 8 SDK, and a current Microsoft Edge WebView2 Runtime.

```powershell
dotnet restore .\OpenGameMate.sln
.\scripts\Validate-ReleaseMetadata.ps1
dotnet build .\OpenGameMate.sln -c Release
dotnet test .\tests\OpenGameMate.Tests\OpenGameMate.Tests.csproj -c Release --no-build
```

Run from source:

```powershell
dotnet run --project .\src\OpenGameMate.App\OpenGameMate.App.csproj -c Release
```

Portable data mode:

```powershell
OpenGameMate.App.exe --portable
```

Installed mode stores operational data under `%LocalAppData%\OpenGameMate\`; portable mode uses `data\` beside the executable. The application does not recursively delete the WebView2 profile; use the in-app cleanup instructions after fully exiting.

## 打包 / Packaging

`scripts\Publish-Portable.ps1` creates a framework-dependent Windows x64 portable folder. Pass a new empty `-OutputDirectory` for every release build. `scripts\Build-Installer.ps1` publishes to that directory and passes its exact path and version to `packaging\OpenGameMate.iss`. Release scripts never clear or reuse a non-empty output folder and never perform recursive deletion.

`scripts\Validate-ReleaseMetadata.ps1` checks the shared .NET version, Windows manifest, versioned Release Notes, release-script syntax, forbidden recursive-delete commands, and required Inno macros without creating an artifact.

## 已知限制 / Known limitations

- Remote adapter updates are safe-disabled until the project has an official GitHub repository and a maintainer-owned signing public key. Built-in compiled rules remain available.
- Browser-data one-click recursive deletion is not implemented because repository safety rules prohibit bulk directory deletion; the UI provides the exact manual cleanup procedure.
- No automated test logs in to a real ChatGPT account, opens Voice, captures the desktop, or sends a real message.

Licensed under the [MIT License](LICENSE).
