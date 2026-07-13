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
