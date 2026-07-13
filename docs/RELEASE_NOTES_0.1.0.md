# OpenGameMate 0.1.0 Release Notes

This source revision implements the v0.1.0 Early Preview architecture and product flow after the Phase 0 Conditional Go result.

## Included

- Bilingual WPF main window and independent ChatGPT WebView2 window.
- Installed and portable data modes.
- User-controlled sign-in, microphone permission, Voice confirmation, and optional role initialization.
- Primary-display capture at no more than 1920×1080 with temporary-file cleanup.
- Fixed two-minute automatic scheduling, send-now, pause/resume, stop, tray controls, and one browser recovery.
- Fail-closed page adapter, quota/adapter degradation states, conversation reminders, and structured diagnostics export.
- Portable publish script, Inno Setup installer definition, CI workflow, privacy/security/contribution documentation, and bilingual issue templates.

## Release limitations

- Remote adapter downloads are disabled until the official GitHub source and maintainer-owned public verification key exist.
- Recursive one-click deletion of the WebView2 profile is omitted under repository safety rules; the UI provides manual cleanup instructions.
- The portable package is framework-dependent and requires .NET 8 Desktop Runtime and WebView2 Runtime.
- The installer definition is present, but an installer executable can only be produced on a machine with Inno Setup 6.
- This build was not automatically launched, did not capture the user's desktop, and did not send a real ChatGPT message. Phase 0 supplies the existing one-shot live evidence.
