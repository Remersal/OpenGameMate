# OpenGameMate v0.1.0 Architecture

## Dependency direction

```text
OpenGameMate.App
  ├─ OpenGameMate.Core
  ├─ OpenGameMate.Configuration
  ├─ OpenGameMate.Diagnostics ──> OpenGameMate.Core
  ├─ OpenGameMate.Browser
  ├─ OpenGameMate.Capture
  └─ OpenGameMate.Adapters ──> OpenGameMate.Browser + OpenGameMate.Core
```

`OpenGameMate.Core` contains the product state machine and fixed v0.1.0 policies. It has no project dependencies. Configuration and diagnostics are infrastructure modules without WPF or webpage behavior. The App project is the composition root; later phases must keep browser, capture, scheduling and webpage adaptation behind their module boundaries.

## Fixed v0.1.0 policies

- Automatic screenshots run every two minutes; the interval is deliberately absent from user settings.
- At most one submission is active. Manual submission wins a conflict and the automatic occurrence is skipped.
- Ordinary failures are not retried immediately. Quota failures enter `VoiceOnly`; adapter failures enter `AdapterError`.
- Conversation reminders occur after two hours or 60 successful screenshots.
- Screenshot output is at most 1920×1080 and never upscaled.

## Data roots

- Installed: `%LocalAppData%\OpenGameMate\`
- Portable: `<executable-directory>\data\`

Both modes use the same child layout: `settings.json`, `logs`, `WebView2`, `temp`, and `adapter-rules`. Paths are internal operational data and must not be written to diagnostic events.

## Privacy-safe diagnostics

Product diagnostics are JSON Lines records with an allowlisted schema: timestamp, level, event token, state, stable error token, success flag, image dimensions, file size and exception type. There is intentionally no arbitrary message, webpage content, prompt, response, account, credential, token, audio or path field.

## Browser boundary

The browser module owns WebView2 session initialization, event subscriptions, microphone permission mediation and fail-closed top-level navigation decisions. The WPF window owns the WebView2 control and disposes it after the session has detached its handlers. The persistent user data folder is supplied by the composition root; OpenGameMate never reads cookies, tokens, history or account data from it.

Formal v0.1 top-level navigation accepts HTTPS on the default port for OpenAI-owned `chatgpt.com` and `openai.com` hosts (including their subdomains), plus the exact Google, Apple and Microsoft identity-provider entry hosts required for user-controlled sign-in. Unknown HTTPS hosts, lookalikes, non-default ports and non-HTTPS targets are blocked. The old any-HTTPS method remains only as historical Phase 0 test evidence and is not used by the runtime session.

`BrowserRestartGate` allows one automatic recovery after an unexpected close per user-started session. WPF integration will consume this policy later and must wait for the user to confirm Voice again rather than assuming that audio resumed.
