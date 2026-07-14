[简体中文](README.zh-CN.md)

<p align="center">
  <img src="assets/OpenGameMate.AppIcon.png" width="128" alt="OpenGameMate icon">
</p>

<h1 align="center">OpenGameMate</h1>

<p align="center"><strong>Let ChatGPT Voice watch your gameplay and chat like a gaming buddy.</strong></p>

<p align="center">
  <a href="https://github.com/Remersal/OpenGameMate/actions/workflows/ci.yml"><img src="https://github.com/Remersal/OpenGameMate/actions/workflows/ci.yml/badge.svg" alt="CI build"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Remersal/OpenGameMate" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/platform-Windows-0078D4?logo=windows11&amp;logoColor=white" alt="Windows">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&amp;logoColor=white" alt=".NET 8">
  <a href="https://github.com/Remersal/OpenGameMate/releases/tag/v0.1.0"><img src="https://img.shields.io/github/v/release/Remersal/OpenGameMate?include_prereleases&amp;label=release" alt="Latest Release"></a>
</p>

<p align="center">
  <a href="https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/OpenGameMate-Setup-0.1.0.exe"><strong>Installer</strong></a> ·
  <a href="https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/OpenGameMate-v0.1.0-win-x64.zip"><strong>Portable</strong></a> ·
  <a href="#demo"><strong>Demo</strong></a> ·
  <a href="docs/RELEASE_NOTES_0.1.0.md"><strong>Documentation</strong></a> ·
  <a href="README.zh-CN.md"><strong>简体中文</strong></a>
</p>

<p align="center">
  An open-source Windows desktop app that uses the ChatGPT website and Voice session you sign in to yourself—no developer API key required.<br>
  After the conversation becomes idle, it sends the latest game screen so the AI can respond, tease, or start a new topic naturally.<br>
  You stay in control of sign-in, Voice, screen capture, and when companion mode runs.
</p>

> **OpenGameMate is an independent open-source project and is not affiliated with or endorsed by OpenAI.**

## Demo

**Demo coming soon.** The repository does not yet contain a privacy-reviewed recording, so no simulated product footage is shown here.

Want to help record the real flow? Follow the [20–40 second demo recording guide](docs/DEMO_RECORDING_GUIDE.md).

## Core features

- Works alongside a ChatGPT Voice conversation that you start and control.
- Automatically updates the conversation with the latest game screen after a continuous idle period.
- Defers automatic sending while Voice or the page is busy, helping avoid interruptions.
- Supports user-triggered screenshots from the app, tray, or a configurable global hotkey.
- Keeps ChatGPT sign-in in a dedicated WebView2 browser environment.
- Does not read chat history, replies, cookies, tokens, or voice content.

## Download and quick start

> **v0.1.0 Early Preview is available.** This is a public testing build, not a claim of universal compatibility.

- [Download the Windows x64 installer](https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/OpenGameMate-Setup-0.1.0.exe)
- [Download the Windows x64 portable ZIP](https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/OpenGameMate-v0.1.0-win-x64.zip)
- [View SHA-256 checksums](https://github.com/Remersal/OpenGameMate/releases/download/v0.1.0/SHA256SUMS.txt)
- [Read the GitHub Release](https://github.com/Remersal/OpenGameMate/releases/tag/v0.1.0)

The installer is currently **not Authenticode-signed**, so Windows may show an unknown-publisher or reputation warning. Installation also requires explicit acknowledgement of the possible game-account risk. The portable package is framework-dependent and requires .NET 8 Desktop Runtime and WebView2 Runtime.

After installing or extracting:

1. Start OpenGameMate. It does not open ChatGPT or capture the screen automatically.
2. Select **Open ChatGPT**, sign in yourself, and start Voice.
3. Confirm Voice in OpenGameMate, then start companion mode and accept the primary-display capture notice.
4. Keep playing and talking. After the selected idle period, OpenGameMate can send the newest screen; you can also trigger a screenshot manually.

## How it works

```text
You play and talk
        ↓
ChatGPT Voice and the page become continuously idle
        ↓
OpenGameMate captures the latest primary-display image
        ↓
The image and a short prompt are sent to your ChatGPT conversation
        ↓
ChatGPT can react to the screen or start a new topic
```

The idle wait can be set to 10, 15, 30, or 60 seconds (default: 10). Nothing is captured or inserted before the full wait completes. One continuous idle period triggers at most once and is rearmed after activity resumes.

## Privacy and safety

- Sign-in, microphone permission, Voice startup, and companion-mode startup require user participation.
- Screen capture is limited to the primary display and one temporary PNG, which is removed after the submission attempt.
- OpenGameMate does not read ChatGPT replies, history, account details, cookies, tokens, full page HTML, microphone audio, or system audio.
- It does not inject into games, read game memory, simulate global input, or bypass anti-cheat, verification, quotas, or protected content.
- Third-party companion or automation tools may still conflict with game or platform rules and may lead to warnings, restrictions, suspension, or bans.

Read the complete [Privacy Policy](PRIVACY.md) and [Security Policy](SECURITY.md) before use. The archived [Phase 0 feasibility report](docs/PHASE0_FEASIBILITY_REPORT.md) records the original technical evidence and limitations.

## Known limitations

- Windows 10 version 19041 or newer is required, together with .NET 8 Desktop Runtime and a current Microsoft Edge WebView2 Runtime for packaged builds.
- Capture currently targets the primary display only. Exclusive fullscreen, protected content, display drivers, and anti-cheat behavior can affect results.
- ChatGPT page structure, Voice availability, image upload support, account quotas, regional access, and platform policy can change.
- The built-in page rules are used while maintainer-owned signing trust anchors for remote rule updates are still being prepared.
- Targeted live checks and user RC acceptance have passed, but public compatibility reports across more systems and games are still needed.

See the [v0.1.0 Release Notes](docs/RELEASE_NOTES_0.1.0.md) and [test plan](docs/V0.1_TEST_PLAN.md) for the detailed evidence boundary.

## Build from source

Requirements: Windows 10 version 19041 or newer, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and a current [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

```powershell
dotnet restore .\OpenGameMate.sln
.\scripts\Validate-ReleaseMetadata.ps1
dotnet build .\OpenGameMate.sln -c Release
dotnet test .\tests\OpenGameMate.Tests\OpenGameMate.Tests.csproj -c Release --no-build
dotnet run --project .\src\OpenGameMate.App\OpenGameMate.App.csproj -c Release
```

Portable data mode:

```powershell
OpenGameMate.App.exe --portable
```

Installed mode stores operational data under `%LocalAppData%\OpenGameMate\`; portable mode uses `data\` beside the executable. See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for development and packaging details.

## Contributing

Bug reports, compatibility results, documentation fixes, and focused contributions are welcome.

- [Open a bug or compatibility report](https://github.com/Remersal/OpenGameMate/issues/new/choose)
- [Browse existing issues](https://github.com/Remersal/OpenGameMate/issues)
- Read [CONTRIBUTING.md](CONTRIBUTING.md) before changing code or workflows.
- Report vulnerabilities using the private process described in [SECURITY.md](SECURITY.md); never post credentials, tokens, chats, or private screenshots publicly.

OpenGameMate is still an early public preview. Feedback, compatibility reports, and contributions are welcome. If you would like to follow its development, consider starring the repository.

## License and project status

OpenGameMate is available under the [MIT License](LICENSE).

**OpenGameMate is an independent open-source project and is not affiliated with or endorsed by OpenAI.** ChatGPT and OpenAI are trademarks of their respective owners.
