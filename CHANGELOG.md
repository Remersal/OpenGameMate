# Changelog

## 0.1.0 - 2026-07-14

- Added modular .NET 8 WPF solution with Core, Configuration, Diagnostics, Browser, Capture, Adapters, and App modules.
- Added isolated WebView2 profile, controlled top-level navigation, user-mediated microphone permission, and one automatic browser recovery.
- Added primary-display capture bounded to 1920×1080 with one temporary PNG lifecycle.
- Added conversation-idle automatic capture: ChatGPT web audio and the safe page state must remain stable for 10 seconds before capture or composer work begins; one continuous idle window triggers once and rearms only after activity resumes.
- Added a configurable global manual-capture hotkey with strict gesture validation, persistent settings, no-repeat registration, and conflict-safe rollback.
- Added a capacity-one PendingSend gate: busy conversations are deferred for at most 90 seconds, expired occurrences are skipped without catch-up, and the newest screen is captured only when submission is actually safe to prepare.
- Added WebView2 document-audio and exact page-state stabilization, Voice activity detection outside the composer form, and a full second fail-closed check before submit.
- Added proactive automatic prompts that ask ChatGPT Voice to start one natural, brief topic from the newest game screen instead of producing a mechanical image report.
- Added fail-closed ChatGPT attachment/text/submission adapter and strict signed remote-rule validation with built-in fallback.
- Added persistent privacy-safe adapter, PendingSend, audio-state, preparation, submission, expiry, and WebView2 process-failure diagnostics without webpage, screenshot, prompt, reply, account, token, cookie, or audio content.
- Added bilingual WPF controls, system tray, state display, role initialization, privacy confirmation, degradation states, and user-initiated diagnostics export.
- Archived Phase 0 feasibility evidence and retained its conditional limitations.
