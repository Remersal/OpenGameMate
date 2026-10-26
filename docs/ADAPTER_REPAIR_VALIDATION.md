# Composer adapter repair: verification and limits

This change targets the preparation and confirmation paths discussed in [issue #1](https://github.com/Remersal/OpenGameMate/issues/1) and [issue #2](https://github.com/Remersal/OpenGameMate/issues/2). It was developed against `9f023642fa5d698202a740f4779c66e664487cbe`; the existing Windows checkout was clean at that same commit and was left untouched. The supplied candidate archive could not complete the supported Windows materialization flow, so its patch was not applied or claimed as verified.

## Behavior

- All probes use one resolver for the existing `#prompt-textarea` and `voice-mode-composer` anchors. Known wrappers may contain an editor. Hidden, disabled, readonly, inert and inaccessible editors are excluded; two eligible editors still fail closed. There is no page-wide arbitrary textbox fallback.
- File inputs must belong to the resolved form or explicit Voice container. External inputs with an explicit matching form owner are supported. Multiple eligible owned inputs are rejected. With no owned input, the existing paste fallback targets only the same validated composer and must produce a visible attachment preview.
- The selected input and composer are checked again immediately before CDP file selection. CDP already dispatches upload events: the adapter no longer dispatches duplicate input/change events. Selecting a file alone does not prove that the application accepted an attachment.
- Preparation preserves existing drafts and unrelated attachments. Text and image preparation share the same composer and scope rules. A target or Voice transition during image preparation returns NotReady.
- A successful preparation grants one submission attempt. Cancellation and a submission attempt consume that permit. Immediately before clicking, the composer, scope, text, attachment nodes and Voice state must still match the prepared snapshot. Replacing an attachment with another one invalidates the attempt.
- Confirmation requires the same composer and scope, with every prepared channel cleared: text for text-only input, preview for image-only input, both for combined input. A lost file-input marker, missing/replaced editor or partial clearance is an unconfirmed outcome. No model reply or message history is read. Clearance is UI evidence, not proof of server-side delivery.
- Preparation diagnostics now record bounded stage codes and candidate counts. The selected built-in rules version is logged. Diagnostics continue to exclude message text, HTML, screenshots, credentials and user paths.

## Windows evidence (2026-10-26)

| Check | Result |
| --- | --- |
| Existing SDK | .NET SDK 8.0.422, Windows x64 |
| Release solution build | Passed, 0 warnings, 0 errors |
| xUnit unit/state-machine tests | 127 passed, 0 failed, 0 skipped |
| Production JS in isolated headless Edge | 40 passed; all navigations intercepted to synthetic fixtures |
| Production C# with isolated WebView2 | 21 workflow assertions passed; runtime 131.0.2903.63 |
| Format verification | Passed |
| Portable win-x64 publish | Passed, framework-dependent |
| Release metadata and artifact privacy scans | Passed |

The Edge suite extracts the actual C# probe strings and executes the shipped JS helper. The WebView2 harness calls the actual adapter methods through an invisible HWND and a new profile, with every request fulfilled locally. Its PNG and messages are synthetic. These checks cover role text, combined and staged image/text preparation, ambiguous controls, hidden/inactive controls, duplicate upload events, repeat/cancel behavior, partial observation and Voice transitions. CI runs both suites on Windows.

```powershell
dotnet build OpenGameMate.sln -c Release
dotnet test tests/OpenGameMate.Tests -c Release --no-build
node --test tests/adapter-dom.test.mjs
dotnet run --project tests/OpenGameMate.WebView2Smoke -c Release --no-build
dotnet format OpenGameMate.sln --verify-no-changes --no-restore
./scripts/Publish-Portable.ps1 -OutputDirectory ./artifacts/adapter-validation
```

Node 24 and installed Edge are required for the JS suite; `OGM_TEST_BROWSER` can name an existing compatible browser executable. The smoke harness requires installed WebView2. Test profiles stay in dedicated temporary directories; they contain synthetic data and are never uploaded.

## Real ChatGPT boundary

One optional, read-only probe used a new, unauthenticated WebView2 profile against the actual ChatGPT site. It reported official origin, NotReady and zero eligible composers. It did not sign in, request microphone permission, start Voice, capture a screen, upload an image or send a message. This observation cannot establish whether the current authenticated ChatGPT/Voice DOM is compatible, or distinguish a sign-in page, an intermediate challenge or another readiness condition. No new general selectors were inferred from it.

The public issue #2 log places the reported failures in preparation, before Submit. It lacks enough structural/build detail to identify a unique cause. Issue #1's exact reported error token does not exist in the examined main/v0.1.0 source; the reporter's build provenance remains unverified. This PR therefore references those issues without claiming a proven live resolution or closing them.

Authenticated acceptance requires the user to open the reviewed build, sign in themselves, start Voice themselves, and knowingly approve a synthetic text and tiny synthetic PNG test. Test the active composer, accepted attachment, text preparation and exactly one confirmed send, then repeat after leaving/reentering Voice and after an interrupted preparation. Do not use real desktop captures, other conversations or private browser profiles. If controls remain ambiguous, keep the failure and collect only the bounded diagnostics.

The branch is intended for review as a draft PR. No main-branch push, merge or release is part of this verification.
