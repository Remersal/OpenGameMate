# OpenGameMate 0.1.0 Release Notes

This source revision implements the v0.1.0 Early Preview architecture and product flow after the Phase 0 Conditional Go result.

## Release readiness report

**Current status:** `Ready for Public Testing`

### Completed

- All planned v0.1.0 functional modules and their integration.
- Release build, release-metadata validation, static formatting validation, and 127/127 automated tests for the current source revision.
- User-controlled targeted validation of sign-in, microphone permission, role initialization, screenshot attachment/submission, Voice-busy deferral, foreground/mouse preservation, proactive image topics, and the automatic screenshot-to-ChatGPT-to-Voice response loop.
- User-completed real-environment RC acceptance confirmed normal ChatGPT sign-in, continuous Voice operation, role initialization, manual and automatic image submission, Voice-safe automatic capture, image-grounded AI responses, selectable capture-delay behavior, and long-duration actual use.
- Portable package generation and documentation for privacy, security, contribution, testing, and release handling.
- User-selected OpenGameMate artwork is embedded as the application, window, tray, shortcut, uninstaller, and installer icon.
- The installer supports Simplified Chinese and English, automatically preferring the matching Windows UI language while allowing manual language selection.
- First installation requires explicit acknowledgment that third-party companion or automation tools may lead to game-account warnings, restrictions, suspension, or permanent bans; silent installation cannot bypass this warning.
- Release builds use the virtual `/_/` source root and automatically reject tracked text or packaged binaries containing local Windows user-profile paths.

### Public-test follow-up

- Install, launch, upgrade/replace, uninstall, and residue checks for the generated installer on clean supported Windows environments.
- Authenticode signing before broad public distribution; the locally generated preview installer is currently unsigned.
- Publication of signed remote adapter rules after a maintainer-owned verification public key is published and the runtime trust anchors are configured for this repository.
- Exception-matrix observations not explicitly supplied with the final user RC confirmation remain documented test targets during public testing; they are not recorded as passing without evidence.

The recorded user evidence is in [USER_RC_CHECKLIST_0.1.0.md](USER_RC_CHECKLIST_0.1.0.md). The source revision is ready for public testing with the limitations below; this status is not a claim that every installation environment, ChatGPT page revision, account quota, or exception path has been validated.

## Included

- Bilingual WPF application shell with ChatGPT WebView2 embedded in the main window and an isolated user-data environment.
- Installed and portable data modes.
- User-controlled sign-in, microphone permission, Voice confirmation, and optional role initialization.
- Primary-display capture at no more than 1920×1080 with temporary-file cleanup.
- Conversation-idle automatic updates: capture and composer work begin only after ChatGPT web audio stops and the page remains safely idle for the selected 10, 15, 30, or 60 continuous seconds (default 10); each idle window triggers at most once, and a pending occurrence snapshots its delay.
- Configurable global manual-capture hotkey with strict modifier/key validation and conflict-safe preservation of the previous binding.
- Capacity-one PendingSend with no screenshot backlog: unstable conversations defer for at most 90 seconds, then expire without catch-up.
- WebView2 document-audio, exact Voice/page-state stabilization, and a second fail-closed pre-submit check before the newest primary-display capture is sent.
- Proactive idle-time image prompts that ask ChatGPT Voice to start one natural, brief topic from the latest screen.
- Fail-closed page adapter, quota/adapter degradation states, conversation reminders, WebView2 process-failure logging, and structured diagnostics export.
- Portable publish script, Inno Setup installer definition, CI workflow, privacy/security/contribution documentation, and bilingual issue templates.

## Release limitations

- Remote adapter downloads are disabled until a maintainer-owned public verification key is published and configured for this repository.
- Recursive one-click deletion of the WebView2 profile is omitted under repository safety rules; the UI provides manual cleanup instructions.
- The portable package is framework-dependent and requires .NET 8 Desktop Runtime and WebView2 Runtime.
- A local Inno Setup 6 installer executable has been generated; it remains an unsigned preview artifact until maintainer-owned Authenticode signing is configured.
- User-controlled live checks and final RC acceptance passed for sign-in, Voice, role initialization, image attachment/submission, Voice-safe automatic capture, image-grounded responses, the selectable idle-delay mechanism, and long-duration actual use.
- The user confirmed long-duration stability acceptance without supplying resource-measurement detail; no CPU or memory figures are asserted in these release notes.
