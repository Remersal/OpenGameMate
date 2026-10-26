# Composer adapter repair: verification and limits

This change targets the preparation and confirmation paths discussed in [issue #1](https://github.com/Remersal/OpenGameMate/issues/1) and [issue #2](https://github.com/Remersal/OpenGameMate/issues/2). It was developed against `9f023642fa5d698202a740f4779c66e664487cbe`; the existing Windows checkout was clean at that same commit and was left untouched. The supplied candidate archive could not complete the supported Windows materialization flow, so its patch was not applied or claimed as verified.

## Behavior

- All probes use one resolver for `#prompt-textarea`, `voice-mode-composer`, and the live-observed `form[data-chatgpt-composer] [role="textbox"][contenteditable="true"]` structure. The new editor must belong to the marked form; a nested foreign form is rejected. Hidden, disabled, readonly and inert editors are excluded; two eligible editors still fail closed. There is no page-wide arbitrary textbox fallback. An empty contenteditable paragraph is recognized without discarding whitespace text drafts.
- File inputs must belong to the resolved form or explicit Voice container. External inputs with an explicit matching form owner are supported. Multiple native inputs are never selected arbitrarily. With absent or ambiguous native inputs, the separate paste path targets only the same validated composer and must produce a visible attachment preview.
- The selected input and composer are checked again immediately before CDP file selection. CDP already dispatches upload events: the adapter no longer dispatches duplicate input/change events. Selecting a file alone does not prove that the application accepted an attachment.
- Preparation preserves existing drafts and unrelated attachments. Text and image preparation share the same composer and scope rules. A target or Voice transition during image preparation returns NotReady.
- A successful preparation grants one submission attempt. Cancellation and a submission attempt consume that permit. Immediately before clicking, the composer, scope, text, attachment nodes and Voice state must still match the prepared snapshot. Replacing an attachment with another one invalidates the attempt.
- Submit discovery also supports a unique semantic `button[type="submit"]` in the marked composer form, excluding buttons owned by another form. An unclassified marked-form state without an explicit send control remains unsafe for automatic preparation.
- Confirmation polls for at most 40 observations, 250 ms apart, using the same composer and scope. Every prepared channel must clear: text for text-only input, preview for image-only input, both for combined input. A lost marker, missing/replaced editor or partial clearance is still unconfirmed. Polling never repeats the send. The production adapter reads no replies or message history.
- Preparation diagnostics now record bounded stage codes and candidate counts. The selected built-in rules version is logged. Diagnostics continue to exclude message text, HTML, screenshots, credentials and user paths.

## Windows evidence (2026-10-26)

| Check | Result |
| --- | --- |
| Existing SDK | .NET SDK 8.0.422, Windows x64 |
| Release solution build | Passed, 0 warnings, 0 errors |
| xUnit unit/state-machine tests | 127 passed, 0 failed, 0 skipped |
| Production JS in isolated headless Edge | 48 passed; all navigations intercepted to synthetic fixtures |
| Production C# with isolated WebView2 | 26 workflow assertions passed; runtime 131.0.2903.63 |
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

An initial unauthenticated read-only probe reported zero eligible composers. The user subsequently signed in manually. A separate visible verifier reused the application's existing session through the supported WebView2 environment API, without reading/copying credentials or opening a remote-debugging port. The original window stayed open. Bounded structural inspection found one visible editable textbox in `form[data-chatgpt-composer]`, no legacy anchors, and four owned image-capable native inputs. This evidence grounds the narrowed rule above.

With explicit user approval, exactly one synthetic 640x360 PNG containing only “OpenGameMate TEST” and the text “这是上传测试，请回复 OK” were prepared and submitted through the production adapter. A visible blob preview and exact text insertion were confirmed; no native file input was chosen. The former fixed one-second observation returned an unconfirmed outcome while both prepared channels were still present. Independent read-only accessibility inspection subsequently found the exact approved text and a visible exact “OK” below it in the same conversation group. No second real send was attempted. The test fixture's legacy message-attribute selector also missed the current message nodes; that negative probe was not treated as proof of failed delivery.

The final bounded observer is covered by a production-C# test that clears both channels after 1.5 seconds and proves only one click. It was not followed by another live send; that would require a further approved test. The live ordinary-composer upload/text path succeeded, while the final observer change has synthetic coverage. Voice, microphone behavior, automatic idle behavior and Windows 10 remain outside this live acceptance. No desktop, old conversations, credentials or raw logs were captured/uploaded. No verification challenge was bypassed.

The public issue #2 log places the reported failures in preparation, before Submit. It lacks enough structural/build detail to identify a unique cause. Issue #1's exact reported error token does not exist in the examined main/v0.1.0 source; the reporter's build provenance remains unverified. This PR therefore references those issues without claiming a proven live resolution or closing them.

Further live confirmation or Voice acceptance requires a new knowingly approved synthetic test. The user must start any Voice session themselves; no microphone or Voice action is automatic. Do not use desktop captures, other conversations or private browser data. If controls remain ambiguous, keep the failure and collect only bounded diagnostics.

The branch is intended for review as a draft PR. No main-branch push, merge or release is part of this verification.
