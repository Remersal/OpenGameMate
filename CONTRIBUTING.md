# Contributing

1. Read `AGENTS.md`, the development document, and `docs/ARCHITECTURE.md`.
2. Keep module dependency directions intact.
3. Add or update tests for each behavior change.
4. Run Release build, tests, and formatting before submitting a change.
5. Never add credentials, cookies, tokens, private screenshots, real conversation data, WebView2 profiles, or signing private keys.
6. Do not add game injection, memory reading, global input simulation, protection bypasses, reply/history scraping, or telemetry.
7. Do not recursively delete files or directories. Handle one explicit file at a time and require manual cleanup for directories.

```powershell
dotnet build .\OpenGameMate.sln -c Release
dotnet test .\tests\OpenGameMate.Tests\OpenGameMate.Tests.csproj -c Release --no-build
dotnet format .\OpenGameMate.sln --verify-no-changes --no-restore
```
