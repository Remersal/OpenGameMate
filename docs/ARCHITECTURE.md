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

- The first automatic screenshot attempt occurs after 30 seconds; later attempts run every two minutes. These timings are deliberately absent from user settings.
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

## Capture boundary

The capture module exposes only primary-display capture. Product output is fixed at a maximum of 1920×1080 with aspect ratio preserved and no upscaling. Callers supply the application temporary directory; the module owns exactly one final PNG and one known in-progress `.tmp` file, serializes capture attempts, atomically replaces the final file after a complete encode, and removes the in-progress file after success or failure.

Capture errors expose stable codes for unsupported systems, missing primary displays, invalid dimensions, timeout, access denial, temporary-file failure and graphics-device failure. Protected content, exclusive-fullscreen black frames and anti-cheat restrictions are reported as compatibility limitations; OpenGameMate does not attempt to bypass them or infer that a dark frame is necessarily an error.

## Scheduling boundary

`AutomaticSendLoop` emits its first occurrence after 30 seconds and waits a fresh two minutes after each later occurrence completes or is skipped; neither timing is user-configurable. It never performs an immediate retry or retains a missed timer tick for catch-up. Pause and runtime state are supplied as a predicate, and manual submissions do not reset the active delay.

`SubmissionCoordinator` allows one active submission and has no queue. An automatic occurrence yields once before starting so a simultaneous manual request can reserve priority; a running manual submission likewise causes the automatic occurrence to be skipped. An operation that already started is not preempted. `ConversationReminderTracker` raises one reminder after two elapsed hours or 60 successful image submissions, whichever occurs first, and resets only when the user starts a new conversation.

## Web-adapter boundary

`ChatGptWebAdapter` may inspect only the current page origin, composer, attachment controls, send control and selector-based upload/error state. It may add the caller-supplied image and prompt and invoke one unambiguous send control. It does not read replies, history, accounts, cookies, tokens, full HTML or audio. Missing or ambiguous controls return `AdapterInvalid`; no random fallback click is attempted.

Adapter rules are strict JSON containing only version tokens and length-bounded CSS selectors. Remote documents use a strict signed envelope: the base64 payload is verified byte-for-byte with RSA-PSS/SHA-256 before its strict schema and allowlisted fields are accepted. The downloader limits the document and payload sizes, requires HTTPS on the default port, and pins the exact `raw.githubusercontent.com/<official-owner>/<official-repository>/main/adapter-rules/chatgpt-v1.signed.json` path, including the final URI after redirects. Every rejection falls back to built-in rules.

The repository currently has no declared official GitHub remote or maintainer-owned signing public key. Until maintainers provide those trust anchors, product composition must pass no verifier; the loader then returns `RemoteDisabled` without making a network request and uses built-in rules. A signing key must never be generated or stored in the client repository.

## WPF composition boundary

The App project is the composition root. Startup displays only the main window and restores settings, never capture or runtime state. User actions create the independent ChatGPT window, confirm Voice, optionally send role text, acknowledge full-display privacy risk, and start the scheduler. WebView2 work is dispatched on the WPF thread; fixed timing remains in Core.

Submission composition is capture → prepare image/text → verify attachment/text → invoke one send control → verify composer/attachment state → delete the local PNG. Automatic capture begins only after at least three seconds of audio silence and six seconds of unchanged safe page/audio state. A Voice state change during preparation abandons that occurrence as an ordinary failure without clicking send; it is not an adapter mismatch. `QuotaReached` cancels automatic work and enters `VoiceOnly`; `AdapterInvalid` cancels work and enters `AdapterError`; ordinary failures return to `Running` without an immediate retry. No branch reads a model reply.

The tray mirrors show/hide browser, send-now, pause/resume, stop, and exit. Closing the browser during a run consumes at most one background recovery and requires explicit Voice confirmation before resuming. Diagnostics export belongs to the Diagnostics module and packages only allowlisted JSONL filenames after a user-selected destination.
