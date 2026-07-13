# Security Policy

## Supported version

Security fixes target the latest `0.1.x` source revision.

## Reporting a vulnerability

Do not include real ChatGPT credentials, cookies, tokens, conversation content, screenshots, or other private data in a public issue. Use the repository owner's private security-reporting channel once the official GitHub repository is established. Until then, provide a minimal local reproduction with secrets removed.

## Security boundaries

- Top-level WebView2 navigation is fail-closed to OpenAI-owned origins and exact supported identity-provider entry origins.
- Microphone permission is allowed only after user confirmation for an OpenAI-owned HTTPS origin.
- Remote adapter rules can contain only strict, length-bounded selectors. They require an exact official GitHub source, size limits, strict schema/fields, and RSA-PSS/SHA-256 signature verification. Without official trust anchors, remote loading is disabled.
- The adapter never accepts remote executable code and never randomly clicks ambiguous controls.
- The application does not bypass game protections, platform verification, or quotas.
