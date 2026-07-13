# OpenGameMate v0.1.0

OpenGameMate 是一个开源 Windows 桌面学习项目：它在独立 WebView2 中打开 ChatGPT，由用户自行登录和开启 Voice；用户明确开始后，程序每两分钟捕获一次主显示器并把截图与简短提示提交到当前对话。

OpenGameMate is an open-source Windows desktop learning project. It opens ChatGPT in an isolated WebView2 session under the user's control. After the user explicitly starts companion mode, it captures the primary display every two minutes and submits the image with a short prompt to the current chat.

OpenGameMate is not an OpenAI product, official extension, or unlimited-quota tool.

## 安全与隐私 / Safety and privacy

- 不读取 ChatGPT 回复、聊天记录、账号、Cookie、Token、完整 HTML 或音频。
- 不注入游戏、不读游戏内存、不模拟全局键鼠、不绕过反作弊、验证码、额度或受保护内容。
- 截图只写入一个应用临时 PNG，提交尝试结束后删除；异常重启不恢复截图或运行状态。
- 首次开始前必须确认“捕获整个主显示器”的隐私风险。
- 后台提交的 Phase 0 单次实验证据已通过，但网页结构、账号能力、独占全屏、额度和平台政策仍会变化。

- Does not read ChatGPT replies, history, accounts, cookies, tokens, full HTML, or audio.
- Does not inject into games, read game memory, synthesize global input, or bypass anti-cheat, verification, quotas, or protected content.
- Keeps one application temporary PNG and deletes it after each attempt; runtime capture state is not restored after a crash.
- Requires explicit acknowledgement of full-primary-display capture risk before first start.
- The Phase 0 background submission path passed a real one-shot test, but webpage structure, account capabilities, fullscreen modes, quotas, and platform policy can change.

See [PRIVACY.md](PRIVACY.md), [SECURITY.md](SECURITY.md), and the archived [Phase 0 feasibility report](docs/PHASE0_FEASIBILITY_REPORT.md).

## 使用 / Use

1. 启动 OpenGameMate。程序只显示主界面，不会自动打开浏览器或截图。
2. 点击“打开 ChatGPT”，自行登录并启动 Voice。
3. 回到主界面点击“我已开启 Voice”。
4. 可选：明确发送一次完整角色设定。
5. 点击“开始陪玩”，阅读并接受整屏捕获提示。
6. 使用主界面或托盘执行立即发送、暂停、恢复和停止。

1. Start OpenGameMate. Only the main window opens; no browser or capture starts automatically.
2. Click **Open ChatGPT**, sign in yourself, and start Voice.
3. Return and click **I started Voice**.
4. Optionally send the full role initialization explicitly.
5. Click **Start** and accept the full-display privacy warning.
6. Use the main window or tray for send-now, pause, resume, and stop.

## 构建 / Build

Requirements: Windows 10 19041 or newer, .NET 8 SDK, and a current Microsoft Edge WebView2 Runtime.

```powershell
dotnet restore .\OpenGameMate.sln
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

`scripts\Publish-Portable.ps1` creates a framework-dependent Windows x64 portable folder. `packaging\OpenGameMate.iss` is the Inno Setup installer definition. Both refuse to reuse a non-empty output folder; the repository never performs recursive deletion.

## 已知限制 / Known limitations

- Remote adapter updates are safe-disabled until the project has an official GitHub repository and a maintainer-owned signing public key. Built-in compiled rules remain available.
- Browser-data one-click recursive deletion is not implemented because repository safety rules prohibit bulk directory deletion; the UI provides the exact manual cleanup procedure.
- No automated test logs in to a real ChatGPT account, opens Voice, captures the desktop, or sends a real message.

Licensed under the [MIT License](LICENSE).
