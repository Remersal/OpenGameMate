# Security Policy

## Supported version

Security fixes target the latest `0.1.x` source revision.

## Reporting a vulnerability

Do not include real ChatGPT credentials, cookies, tokens, conversation content, screenshots, or other private data in a public issue. On the GitHub **Security** tab, use **Report a vulnerability** if private reporting is available. Otherwise, open a minimal public issue asking the maintainer for a private contact path without including vulnerability details or secrets.

## Security boundaries

- Top-level WebView2 navigation is fail-closed to OpenAI-owned origins and exact supported identity-provider entry origins.
- Microphone permission is allowed only after user confirmation for an OpenAI-owned HTTPS origin.
- Remote adapter rules can contain only strict, length-bounded selectors. They require the exact OpenGameMate GitHub source, size limits, strict schema/fields, and RSA-PSS/SHA-256 signature verification. Until a maintainer-owned verification public key is published and configured, remote loading remains disabled.
- The adapter never accepts remote executable code and never randomly clicks ambiguous controls.
- The application does not bypass game protections, platform verification, or quotas.
