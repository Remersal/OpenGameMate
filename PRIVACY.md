# Privacy

OpenGameMate v0.1.0 is designed to minimize retained data.

## What the application processes

- The primary display, only after the user explicitly starts companion mode.
- A single temporary PNG used for the current submission attempt.
- User-selected role text and fixed automatic/manual screenshot prompts.
- Local operational settings and privacy-safe diagnostic events.
- A dedicated WebView2 user-data directory managed by WebView2 to retain the user's sign-in and permissions.

## What the application does not read or collect

OpenGameMate does not read ChatGPT replies, conversation history, account details, cookies, login tokens, complete page HTML, microphone audio, system audio, game memory, or telemetry. It does not run an analytics service.

## Screenshots

Capturing the entire primary display can include notifications, chat windows, account names, or other private information. The application shows a warning before first start. One known temporary PNG is atomically replaced and removed after each attempt. A crash does not restore the prior screenshot or running state.

## Diagnostics

Logs contain allowlisted structured fields such as timestamp, application state, stable error code, image dimensions, file size, and exception type. They do not contain screenshots, prompt text, webpage content, account data, tokens, audio, or full user paths. A diagnostic ZIP is created only after the user chooses a destination.

## Browser profile

WebView2 stores sign-in and permission data in the application's dedicated profile. OpenGameMate does not inspect that data. To clear it, fully exit OpenGameMate, wait for WebView2 processes to stop, and manually delete the directory shown by the application. Recursive deletion is intentionally not performed by the client.
