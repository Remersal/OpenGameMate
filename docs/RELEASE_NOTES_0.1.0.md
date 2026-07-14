# OpenGameMate 0.1.0 Release Notes

This source revision implements the v0.1.0 Early Preview architecture and product flow after the Phase 0 Conditional Go result.

## Release readiness report

**Current status:** `Feature Complete, Pending User RC Validation`

### Completed

- All planned v0.1.0 functional modules and their integration.
- Release build, release-metadata validation, static formatting validation, and 105/105 automated tests for feature baseline `fd0677f`.
- User-controlled targeted validation of sign-in, microphone permission, role initialization, screenshot attachment/submission, Voice-busy deferral, foreground/mouse preservation, proactive image topics, and the automatic screenshot-to-ChatGPT-to-Voice response loop.
- Portable package generation and documentation for privacy, security, contribution, testing, and release handling.

### Pending before a public-test decision

- User-owned RC acceptance in a real game environment, including continuous Voice, foreground behavior, the 30-minute run, the two-hour run, network/page exceptions, image-quota behavior, and exit/recovery.
- Installer executable generation plus install, launch, upgrade/replace, uninstall, and residue checks on a machine with Inno Setup 6.
- Publication of signed remote adapter rules after an official GitHub source and maintainer-owned verification public key are available.

The detailed, fillable user procedure is [USER_RC_CHECKLIST_0.1.0.md](USER_RC_CHECKLIST_0.1.0.md). No public-test readiness claim should be made until the required user RC items have recorded outcomes and release blockers have an explicit disposition.

## Included

- Bilingual WPF application shell with ChatGPT WebView2 embedded in the main window and an isolated user-data environment.
- Installed and portable data modes.
- User-controlled sign-in, microphone permission, Voice confirmation, and optional role initialization.
- Primary-display capture at no more than 1920×1080 with temporary-file cleanup.
- First automatic update attempt after 30 seconds, then a fresh two-minute delay after each later occurrence; send-now, pause/resume, stop, tray controls, and one browser recovery.
- Capacity-one PendingSend with no screenshot backlog: busy conversations defer for at most 90 seconds, then expire without catch-up.
- WebView2 document-audio, exact Voice/page-state stabilization, and a second fail-closed pre-submit check before the newest primary-display capture is sent.
- Proactive idle-time image prompts that ask ChatGPT Voice to start one natural, brief topic from the latest screen.
- Fail-closed page adapter, quota/adapter degradation states, conversation reminders, WebView2 process-failure logging, and structured diagnostics export.
- Portable publish script, Inno Setup installer definition, CI workflow, privacy/security/contribution documentation, and bilingual issue templates.

## Release limitations

- Remote adapter downloads are disabled until the official GitHub source and maintainer-owned public verification key exist.
- Recursive one-click deletion of the WebView2 profile is omitted under repository safety rules; the UI provides manual cleanup instructions.
- The portable package is framework-dependent and requires .NET 8 Desktop Runtime and WebView2 Runtime.
- The installer definition is present, but an installer executable can only be produced on a machine with Inno Setup 6.
- Targeted user-controlled live checks passed for sign-in, microphone permission, image attachment/submission, a 30-second first occurrence, later two-minute occurrences, Voice-busy deferral, a 90-second busy expiry, foreground/mouse stability, and a proactive Voice response after an idle-time image.
- The 30-minute and two-hour user RC soak runs have not been completed. This targeted evidence must not be interpreted as long-duration stability approval or a public-release decision.
