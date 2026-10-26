using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenGameMate.Browser;
using OpenGameMate.Core;

namespace OpenGameMate.Adapters;

public interface IAiWebAdapter
{
    Task<InputPreparationResult> PrepareImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default);

    Task<TextPreparationResult> PrepareTextAsync(
        string message,
        CancellationToken cancellationToken = default);

    Task<InputPreparationResult> PrepareInputAsync(
        string imagePath,
        string message,
        CancellationToken cancellationToken = default);

    Task<SubmissionResult> SubmitAsync(CancellationToken cancellationToken = default);

    Task<AdapterIdleProbeResult> ProbeIdleAsync(CancellationToken cancellationToken = default);

    Task<SubmissionResult> SubmitPreparedInputOnceAsync(
        long expectedAudioStateVersion,
        TimeSpan requiredSilentDuration,
        AdapterPageState expectedPageState,
        CancellationToken cancellationToken = default);
}

public enum SubmitControlDecision
{
    Invoke,
    Wait,
    Fail,
}

public sealed class ChatGptWebAdapter : IAiWebAdapter
{
    private const string FileInputMarker = "data-opengamemate-file";
    private const int MaximumDiagnosticButtonCandidates = 12;
    private const int MaximumDiagnosticScopeDepth = 8;
    private const int SubmitControlPollAttempts = 120;
    private const int PostSubmitPollAttempts = 40;
    private const int SubmitControlPollIntervalMilliseconds = 250;
    private const int UploadPollAttempts = 40;
    private const int MaximumMessageCharacters = 8000;
    private const long MaximumImageBytes = 20 * 1024 * 1024;
    private static readonly string ComposerDomHelpers = LoadComposerDomHelpers();
    private int _preparedSubmissionAvailable;
    private readonly ChatGptBrowserSession _session;
    private readonly ChatGptAdapterRules _rules;

    public ChatGptWebAdapter(
        ChatGptBrowserSession session,
        ChatGptAdapterRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _rules = rules ?? ChatGptAdapterRules.BuiltIn;
        _rules.Validate();
    }

    public async Task<InputPreparationResult> PrepareInputAsync(
        string imagePath,
        string message,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _preparedSubmissionAvailable, 0);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await PrepareInputCoreAsync(imagePath, message, insertText: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or IOException or
                UnauthorizedAccessException or COMException)
        {
            return new(false, false, false, "adapter-operation-failed", WebAdapterStatus.AdapterInvalid);
        }
        finally
        {
            await ReleaseUnfinishedUploadAsync();
        }
    }

    public async Task<InputPreparationResult> PrepareImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _preparedSubmissionAvailable, 0);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await PrepareInputCoreAsync(imagePath, message: null, insertText: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or IOException or
                UnauthorizedAccessException or COMException)
        {
            return new(false, false, false, "adapter-operation-failed", WebAdapterStatus.AdapterInvalid);
        }
        finally
        {
            await ReleaseUnfinishedUploadAsync();
        }
    }

    private async Task<InputPreparationResult> PrepareInputCoreAsync(
        string imagePath,
        string? message,
        bool insertText,
        CancellationToken cancellationToken)
    {
        if (!_session.IsInitialized || !_session.IsOnChatGpt())
        {
            return new(false, false, false, "not-on-chatgpt", WebAdapterStatus.NotReady);
        }

        if (insertText && (message is null || !IsMessageValid(message)))
        {
            return new(false, false, false, "message-invalid", WebAdapterStatus.InvalidInput);
        }

        if (!File.Exists(imagePath))
        {
            return new(false, false, false, "image-file-missing", WebAdapterStatus.InvalidInput);
        }

        var imageLength = new FileInfo(imagePath).Length;
        if (imageLength is <= 0 or > MaximumImageBytes)
        {
            return new(false, false, false, "image-file-size-invalid", WebAdapterStatus.InvalidInput);
        }

        var fileInputSelectorJson = JsonSerializer.Serialize(_rules.FileInputSelector);
        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var imageDigestJson = JsonSerializer.Serialize(Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant());
        var composerSelectorJson = JsonSerializer.Serialize(_rules.ComposerSelector);
        var previewSelectorsJson = JsonSerializer.Serialize(_rules.AttachmentPreviewSelectors);
        var quotaSelectorsJson = JsonSerializer.Serialize(_rules.QuotaErrorSelectors);
        var platformErrorSelectorsJson = JsonSerializer.Serialize(_rules.PlatformErrorSelectors);

        var locateFileInput = await EvaluateAsync(
            $$"""
            (() => {
              if (!(location.hostname === 'chatgpt.com' || location.hostname.endsWith('.chatgpt.com'))) {
                return { code: 'unexpected-origin', count: 0 };
              }
              ogmStopUpload('abandoned');
              globalThis[ogmPreparedKey] = null;
              const composers = ogmComposers({{composerSelectorJson}});
              if (composers.length !== 1) return { code: 'composer-count', count: composers.length };
              const composer = composers[0];
              const scope = ogmScope(composer);
              if (!scope) return { code: 'composer-scope-missing', count: 0 };
              if (ogmText(composer) || ogmHasAttachment(scope)) {
                return { code: 'composer-not-empty', count: 0 };
              }
              ogmRemember(composer);
              ogmBeginUpload(composer, {{imageDigestJson}}, {{imageLength}});
              document.querySelectorAll('input[{{FileInputMarker}}]')
                .forEach(element => element.removeAttribute('{{FileInputMarker}}'));
              const candidates = ogmFileInputs({{fileInputSelectorJson}}, scope);
              if (candidates.length !== 1) {
                return { code: candidates.length === 0 ? 'file-input-none' : 'file-input-ambiguous',
                  count: candidates.length };
              }
              candidates[0].setAttribute('{{FileInputMarker}}', 'true');
              globalThis[ogmPreparedKey].fileInput = candidates[0];
              const upload = globalThis[ogmPreparedKey].upload;
              const bind = () => upload.bind(candidates[0].files?.[0]);
              candidates[0].addEventListener('input', bind, { capture: true });
              upload.unbind = () => candidates[0].removeEventListener('input', bind, { capture: true });
              return { code: 'ok', count: 1 };
            })()
            """);

        JsonElement attachmentResult;
        if (GetCode(locateFileInput) == "ok")
        {
            using var documentResponse = await CallCdpAsync("DOM.getDocument", new { depth = 1, pierce = true });
            var rootNodeId = documentResponse.RootElement
                .GetProperty("root")
                .GetProperty("nodeId")
                .GetInt32();

            using var queryResponse = await CallCdpAsync(
                "DOM.querySelector",
                new { nodeId = rootNodeId, selector = $"input[{FileInputMarker}=\"true\"]" });
            var fileNodeId = queryResponse.RootElement.GetProperty("nodeId").GetInt32();
            if (fileNodeId == 0)
            {
                return new(false, false, false, "marked-file-input-not-found", WebAdapterStatus.AdapterInvalid);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var targetCheck = await EvaluateAsync(
                $$"""
                (() => {
                  const composers = ogmComposers({{composerSelectorJson}});
                  if (composers.length !== 1 || !ogmSamePreparationTarget(composers[0])) {
                    return { code: 'preparation-target-changed' };
                  }
                  const inputs = ogmFileInputs({{fileInputSelectorJson}}, ogmScope(composers[0]));
                  const marked = document.querySelectorAll('input[{{FileInputMarker}}="true"]');
                  return { code: inputs.length === 1 && marked.length === 1 &&
                    inputs[0] === marked[0] && inputs[0] === globalThis[ogmPreparedKey].fileInput
                      ? 'file-input-target-checked' : 'preparation-target-changed' };
                })()
                """);
            if (GetCode(targetCheck) != "file-input-target-checked")
            {
                return new(false, false, false, GetCode(targetCheck), WebAdapterStatus.NotReady);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using (await CallCdpAsync(
                "DOM.setFileInputFiles",
                new { files = new[] { Path.GetFullPath(imagePath) }, nodeId = fileNodeId }))
            {
            }

            attachmentResult = await EvaluateAsync(
                $$"""
                (() => {
                  const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
                  const composers = ogmComposers({{composerSelectorJson}});
                  if (!fileInput || composers.length !== 1 || !ogmSamePreparationTarget(composers[0])) {
                    return { code: 'file-input-lost', fileSelected: false };
                  }
                  // DOM.setFileInputFiles already dispatches input/change in Chromium.
                  return { code: 'file-input-selected', fileSelected: fileInput.files?.length === 1 };
                })()
                """);
        }
        else if (GetCode(locateFileInput) is "file-input-none" or "file-input-ambiguous")
        {
            // Never choose between ambiguous native inputs. The separate paste path targets
            // only the same validated composer and still requires a visible accepted preview.
            var imageBase64Json = JsonSerializer.Serialize(Convert.ToBase64String(imageBytes));
            attachmentResult = await EvaluateAsync(
                $$"""
                (async () => {
                  const composers = ogmComposers({{composerSelectorJson}});
                  if (composers.length !== 1) {
                    return { code: 'composer-count-before-paste', count: composers.length,
                      fileSelected: false };
                  }

                  const target = composers[0];
                  if (!ogmSamePreparationTarget(target) || ogmText(target) || ogmHasAttachment(ogmScope(target))) {
                    return { code: 'composer-changed-before-paste', fileSelected: false };
                  }
                  const binary = atob({{imageBase64Json}});
                  const bytes = new Uint8Array(binary.length);
                  for (let index = 0; index < binary.length; index++) {
                    bytes[index] = binary.charCodeAt(index);
                  }
                  const transfer = new DataTransfer();
                  const file = new File([bytes], 'opengamemate-screen.png', { type: 'image/png' });
                  globalThis[ogmPreparedKey].upload.bind(file);
                  transfer.items.add(file);
                  const pasteEvent = new ClipboardEvent('paste', {
                    bubbles: true,
                    cancelable: true,
                    composed: true,
                    clipboardData: transfer
                  });
                  if (!pasteEvent.clipboardData) {
                    Object.defineProperty(pasteEvent, 'clipboardData', { value: transfer });
                  }
                  composers[0].dispatchEvent(pasteEvent);
                  return { code: 'paste-event-dispatched', fileSelected: false };
                })()
                """);
        }
        else
        {
            return new(false, false, false, GetPreparationCode(locateFileInput), WebAdapterStatus.AdapterInvalid);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var inputFileSelected = false;
        if (insertText)
        {
            var inputResult = await InsertTextAsync(message!, composerSelectorJson, allowPreparedAttachment: true);
            inputFileSelected = GetBoolean(inputResult, "fileSelected");
        }

        var messageJson = JsonSerializer.Serialize(message ?? string.Empty);
        var expectTextJson = insertText ? "true" : "false";
        JsonElement probeResult = default;
        try
        {
            for (var attempt = 0; attempt < UploadPollAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                probeResult = await EvaluateAsync(
            $$"""
            (async () => {
              const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
              const composers = ogmComposers({{composerSelectorJson}});
              const composer = composers.length === 1 ? composers[0] : null;
              const scope = ogmScope(composer);
              if (!scope) return { code: 'composer-scope-lost', fileSelected: false,
                previewDetected: false, textInserted: false };
              const uploadResult = await ogmPollUpload(composer);
              const previewSelectors = {{previewSelectorsJson}};
              const quotaSelectors = {{quotaSelectorsJson}};
              const platformErrorSelectors = {{platformErrorSelectorsJson}};
              const actual = composer instanceof HTMLTextAreaElement || composer instanceof HTMLInputElement
                ? composer.value
                : composer?.innerText;
              return {
                code: uploadResult.code,
                pageState: ogmPageState(scope),
                attachmentDiagnostics: ogmAttachmentDiagnostics(scope, true),
                fileSelected: fileInput?.files?.length === 1,
                previewDetected: ogmHasAttachment(scope),
                textInserted: {{expectTextJson}} && ogmCanonicalText(actual) === ogmCanonicalText({{messageJson}}),
                quotaDetected: quotaSelectors.some(selector => document.querySelector(selector)),
                platformErrorDetected: platformErrorSelectors.some(selector => document.querySelector(selector))
              };
            })()
            """);
                if (GetCode(probeResult) != "attachment-upload-pending" ||
                    GetBoolean(probeResult, "quotaDetected") || GetBoolean(probeResult, "platformErrorDetected"))
                {
                    break;
                }

                if (attempt < UploadPollAttempts - 1)
                {
                    await Task.Delay(SubmitControlPollIntervalMilliseconds, cancellationToken);
                }
            }
        }
        finally
        {
            // On cancellation or timeout keep the user's draft, revoke the permit
            // and release the bounded observer/File reference. Never paste again.
            await ReleaseUnfinishedUploadAsync();
        }

        if (GetCode(probeResult) == "attachment-upload-pending")
        {
            var timeoutResult = await EvaluateAsync(
                "({ code: globalThis[ogmPreparedKey]?.upload?.timeoutCode ?? 'attachment-upload-timeout' })");
            probeResult = JsonSerializer.SerializeToElement(new
            {
                code = GetCode(timeoutResult),
                fileSelected = GetBoolean(probeResult, "fileSelected"),
                previewDetected = GetBoolean(probeResult, "previewDetected"),
                textInserted = GetBoolean(probeResult, "textInserted"),
                attachmentDiagnostics = probeResult.GetProperty("attachmentDiagnostics"),
                pageState = probeResult.GetProperty("pageState"),
            });
        }

        var fileSelected = GetBoolean(probeResult, "fileSelected") ||
            inputFileSelected ||
            GetBoolean(attachmentResult, "fileSelected");
        var previewDetected = GetBoolean(probeResult, "previewDetected");
        var textInserted = GetBoolean(probeResult, "textInserted");
        var status = GetCode(probeResult) == "preparation-target-changed"
            ? WebAdapterStatus.NotReady
            : ClassifyPreparationProbe(
            previewDetected,
            insertText ? textInserted : true,
            GetBoolean(probeResult, "quotaDetected"),
            GetBoolean(probeResult, "platformErrorDetected"));
        if (status == WebAdapterStatus.Succeeded && GetCode(probeResult) != "ok")
        {
            status = WebAdapterStatus.AdapterInvalid;
        }
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _preparedSubmissionAvailable, status == WebAdapterStatus.Succeeded ? 1 : 0);
        var preparationCode = status switch
        {
            WebAdapterStatus.Succeeded => "ok",
            WebAdapterStatus.NotReady => "preparation-target-changed",
            WebAdapterStatus.QuotaReached => "quota-detected",
            WebAdapterStatus.PlatformError => "platform-error-detected",
            _ => GetCode(probeResult) != "ok" ? GetPreparationCode(probeResult)
                : !previewDetected ? "attachment-not-observed"
                : insertText && !textInserted ? "text-not-observed" : "preparation-failed",
        };
        return new(fileSelected, previewDetected, textInserted, preparationCode, status,
            new AdapterDiagnostics(TryReadEnum(probeResult, "pageState", AdapterPageState.Unknown), 0, [],
                status == WebAdapterStatus.Succeeded ? AdapterFailureStage.None : AdapterFailureStage.AttachmentPreparation,
                ParseAttachmentDiagnostics(probeResult)));
    }

    private async Task ReleaseUnfinishedUploadAsync()
    {
        if (!_session.IsInitialized || !_session.IsOnChatGpt())
        {
            return;
        }

        try
        {
            await EvaluateAsync(
                "(() => { const upload = globalThis[ogmPreparedKey]?.upload; " +
                "if (upload?.phase === 'uploading') { upload.timeoutCode = upload.failure ?? " +
                "(!upload.seenImage ? (ogmHasAttachment(globalThis[ogmPreparedKey].scope) ? " +
                "'attachment-content-unverifiable' : 'attachment-not-observed') : !upload.sawBusy ? " +
                "'attachment-completion-unobserved' : 'attachment-upload-timeout'); ogmStopUpload('failed'); } " +
                "return { code: 'upload-observation-released' }; })()");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or COMException)
        {
            // A detached/navigated document owns no submit permit. Cleanup must
            // not hide the original failure or cancellation from the caller.
        }
    }

    public async Task<TextPreparationResult> PrepareTextAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var allowPreparedAttachment = Interlocked.Exchange(ref _preparedSubmissionAvailable, 0) == 1;
        cancellationToken.ThrowIfCancellationRequested();
        if (!_session.IsInitialized || !_session.IsOnChatGpt())
        {
            return new(false, "not-on-chatgpt", WebAdapterStatus.NotReady);
        }

        if (!IsMessageValid(message))
        {
            return new(false, "message-invalid", WebAdapterStatus.InvalidInput);
        }

        try
        {
            var composerSelectorJson = JsonSerializer.Serialize(_rules.ComposerSelector);
            var result = await InsertTextAsync(message, composerSelectorJson, allowPreparedAttachment);
            cancellationToken.ThrowIfCancellationRequested();
            var inserted = GetBoolean(result, "textInserted");
            Interlocked.Exchange(ref _preparedSubmissionAvailable, inserted ? 1 : 0);
            return new(
                inserted,
                GetPreparationCode(result),
                inserted ? WebAdapterStatus.Succeeded
                    : GetCode(result) == "preparation-target-changed"
                        ? WebAdapterStatus.NotReady : WebAdapterStatus.AdapterInvalid);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or IOException or
                UnauthorizedAccessException or COMException)
        {
            return new(false, "adapter-operation-failed", WebAdapterStatus.AdapterInvalid);
        }
    }

    public async Task<AdapterIdleProbeResult> ProbeIdleAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_session.IsInitialized || !_session.IsOnChatGpt())
        {
            return CreateIdleProbeFailure("not-on-chatgpt", WebAdapterStatus.NotReady);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sendButtonSelectorJson = JsonSerializer.Serialize(_rules.SendButtonSelector);
            var busyButtonSelectorJson = JsonSerializer.Serialize(_rules.BusyButtonSelector);
            var composerSelectorJson = JsonSerializer.Serialize(_rules.ComposerSelector);
            var attachmentPreviewSelectorsJson = JsonSerializer.Serialize(
                _rules.AttachmentPreviewSelectors);
            var result = await EvaluateAsync(
                $$"""
                (() => {
                  const domainCorrect = location.hostname === 'chatgpt.com' ||
                    location.hostname.endsWith('.chatgpt.com');
                  if (!domainCorrect) {
                    return { code: 'unexpected-origin', domainCorrect: false,
                      composerCount: 0, stopButtonCount: 0, sendButtonCount: 0,
                      sendButtonDisabled: false, sendButtonInComposerForm: false,
                      diagnostics: { pageState: 'Unknown', detectedButtonCount: 0,
                        candidateButtons: [], failureStage: 'Readiness' } };
                  }

                  const composers = ogmComposers({{composerSelectorJson}});
                  const composer = composers.length === 1 ? composers[0] : null;
                  const form = ogmScope(composer);
                  const stopButtons = [...document.querySelectorAll({{busyButtonSelectorJson}})].filter(ogmVisible);
                  const sendButtons = form
                    ? [...form.querySelectorAll({{sendButtonSelectorJson}})].filter(button => ogmOwnedButton(button, form))
                    : [];
                  const sendDisabled = sendButtons.length === 1 &&
                    (sendButtons[0].disabled ||
                      sendButtons[0].getAttribute('aria-disabled') === 'true');
                  const attachmentSelectors = {{attachmentPreviewSelectorsJson}};
                  const hasAttachment = ogmHasAttachment(form);
                  const activeVoiceSelector =
                    'button[data-testid="end-voice-mode-button"],' +
                    '[data-testid="voice-mode-composer"]';
                  const hasVoiceControl = ogmVoice();
                  // An empty unified composer can replace submit with an unclassified activity
                  // control. Keep automatic preparation closed until send readiness is explicit.
                  const unclassifiedUnifiedState = form?.hasAttribute('data-chatgpt-composer') &&
                    sendButtons.length === 0 && !hasVoiceControl;
                  const pageState = composers.length !== 1 || !form || unclassifiedUnifiedState ? 'Unknown' : hasVoiceControl
                    ? (hasAttachment ? 'VoiceComposerWithAttachment' : 'VoiceComposer')
                    : (hasAttachment ? 'ComposerWithAttachment' : 'Composer');
                  const allFormButtons = form ? [...form.querySelectorAll('button')] : [];
                  const activeVoiceButtons = [...document.querySelectorAll(
                    'button[data-testid="end-voice-mode-button"],' +
                    '[data-testid="voice-mode-composer"] button')];
                  const diagnosticButtons = [...new Set([...allFormButtons, ...activeVoiceButtons])];
                  const candidateButtons = diagnosticButtons.length > 0
                    ? diagnosticButtons
                      .slice(0, {{MaximumDiagnosticButtonCandidates}})
                      .map(button => ({
                        tagName: button.tagName.toLowerCase(),
                        scopeDepth: 0,
                        type: button.getAttribute('type'),
                        role: button.getAttribute('role'),
                        disabled: Boolean(button.disabled),
                        ariaDisabled: button.getAttribute('aria-disabled') === 'true',
                        ariaLabel: button.getAttribute('aria-label'),
                        dataTestId: button.getAttribute('data-testid')
                      }))
                    : [];
                  const failureStage = composers.length !== 1 || !form
                    ? 'ComposerScope'
                    : sendButtons.length > 1 || stopButtons.length > 1
                      ? 'ButtonValidation'
                      : sendButtons.length !== 1
                        ? 'ButtonDiscovery'
                        : 'None';
                  const code = composers.length !== 1
                    ? 'composer-count-idle-probe'
                    : !form
                      ? 'composer-form-missing-idle-probe'
                      : stopButtons.length !== 0
                        ? 'stop-button-present'
                        : sendButtons.length !== 1
                          ? 'send-button-count-idle-probe'
                          : sendDisabled
                            ? 'send-button-disabled-idle-probe'
                            : 'ok';
                  return {
                    code,
                    domainCorrect: true,
                    composerCount: composers.length,
                    stopButtonCount: stopButtons.length,
                    sendButtonCount: sendButtons.length,
                    sendButtonDisabled: sendDisabled,
                    sendButtonInComposerForm: Boolean(form) && sendButtons.length === 1,
                    diagnostics: {
                      pageState,
                      detectedButtonCount: diagnosticButtons.length,
                      candidateButtons,
                      failureStage
                    }
                  };
                })()
                """);

            var composerCount = GetInt32(result, "composerCount");
            var stopButtonCount = GetInt32(result, "stopButtonCount");
            var sendButtonCount = GetInt32(result, "sendButtonCount");
            var domainCorrect = GetBoolean(result, "domainCorrect");
            var sendDisabled = GetBoolean(result, "sendButtonDisabled");
            var sendInForm = GetBoolean(result, "sendButtonInComposerForm");
            var status = composerCount > 1 || stopButtonCount > 1 || sendButtonCount > 1 ||
                         GetCode(result) == "composer-form-missing-idle-probe"
                ? WebAdapterStatus.AdapterInvalid
                : domainCorrect && composerCount == 1 && stopButtonCount == 0 &&
                  sendButtonCount == 1 && !sendDisabled && sendInForm
                    ? WebAdapterStatus.Succeeded
                    : WebAdapterStatus.NotReady;
            return new(
                domainCorrect,
                composerCount,
                stopButtonCount,
                sendButtonCount,
                sendDisabled,
                sendInForm,
                GetCode(result),
                status,
                ParseAdapterDiagnostics(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or IOException or
                UnauthorizedAccessException or COMException)
        {
            return CreateIdleProbeFailure("adapter-operation-failed", WebAdapterStatus.AdapterInvalid);
        }
    }

    private static AdapterIdleProbeResult CreateIdleProbeFailure(
        string code,
        WebAdapterStatus status) =>
        new(
            false,
            0,
            0,
            0,
            false,
            false,
            code,
            status,
            new AdapterDiagnostics(
                AdapterPageState.Unknown,
                0,
                [],
                AdapterFailureStage.Readiness));

    public async Task<SubmissionResult> SubmitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await SubmitCoreAsync(
                SubmitControlPollAttempts,
                expectedAudioStateVersion: null,
                requiredSilentDuration: TimeSpan.Zero,
                expectedPageState: null,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or IOException or
                UnauthorizedAccessException or COMException)
        {
            return new(
                false,
                false,
                false,
                "adapter-operation-failed",
                WebAdapterStatus.AdapterInvalid,
                new AdapterDiagnostics(
                    AdapterPageState.Unknown,
                    0,
                    [],
                    AdapterFailureStage.RuntimeEvaluation));
        }
    }

    public async Task<SubmissionResult> SubmitPreparedInputOnceAsync(
        long expectedAudioStateVersion,
        TimeSpan requiredSilentDuration,
        AdapterPageState expectedPageState,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await SubmitCoreAsync(
                pollAttempts: 1,
                expectedAudioStateVersion,
                requiredSilentDuration,
                expectedPageState,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or IOException or
                UnauthorizedAccessException or COMException)
        {
            return new(
                false,
                false,
                false,
                "adapter-operation-failed",
                WebAdapterStatus.AdapterInvalid,
                new AdapterDiagnostics(
                    AdapterPageState.Unknown,
                    0,
                    [],
                    AdapterFailureStage.RuntimeEvaluation));
        }
    }

    private async Task<SubmissionResult> SubmitCoreAsync(
        int pollAttempts,
        long? expectedAudioStateVersion,
        TimeSpan requiredSilentDuration,
        AdapterPageState? expectedPageState,
        CancellationToken cancellationToken)
    {
        var preparedSubmissionAvailable = Interlocked.Exchange(ref _preparedSubmissionAvailable, 0) == 1;
        cancellationToken.ThrowIfCancellationRequested();
        if (!_session.IsInitialized || !_session.IsOnChatGpt())
        {
            return new(
                false,
                false,
                false,
                "not-on-chatgpt",
                WebAdapterStatus.NotReady,
                new AdapterDiagnostics(
                    AdapterPageState.Unknown,
                    0,
                    [],
                    AdapterFailureStage.Readiness));
        }

        if (!preparedSubmissionAvailable)
        {
            return new(false, false, false, "input-not-prepared", WebAdapterStatus.AdapterInvalid);
        }

        var sendButtonSelectorJson = JsonSerializer.Serialize(_rules.SendButtonSelector);
        var busyButtonSelectorJson = JsonSerializer.Serialize(_rules.BusyButtonSelector);
        var composerSelectorJson = JsonSerializer.Serialize(_rules.ComposerSelector);
        var quotaSelectorsJson = JsonSerializer.Serialize(_rules.QuotaErrorSelectors);
        var platformErrorSelectorsJson = JsonSerializer.Serialize(_rules.PlatformErrorSelectors);
        var attachmentPreviewSelectorsJson = JsonSerializer.Serialize(_rules.AttachmentPreviewSelectors);
        var requireAttachmentJson = expectedAudioStateVersion is null ? "false" : "true";
        var expectedPageStateJson = expectedPageState is null
            ? "null"
            : JsonSerializer.Serialize(expectedPageState.Value.ToString());

        var adapterDiagnostics = new AdapterDiagnostics(
            AdapterPageState.Unknown,
            0,
            [],
            AdapterFailureStage.Readiness);
        var triggerInvoked = false;
        var submitCode = "send-control-timeout";
        for (var attempt = 0; attempt < pollAttempts; attempt++)
        {
            if (expectedAudioStateVersion is not null &&
                !AudioGuardAllowsSubmit(expectedAudioStateVersion.Value, requiredSilentDuration))
            {
                return new(
                    false,
                    false,
                    false,
                    "audio-state-changed-before-submit",
                    WebAdapterStatus.NotReady,
                    adapterDiagnostics);
            }

            var controlResult = await EvaluateAsync(
                $$"""
            (() => {
              const emptyDiagnostics = failureStage => ({
                pageState: 'Unknown', detectedButtonCount: 0,
                candidateButtons: [], failureStage
              });
              if (!(location.hostname === 'chatgpt.com' || location.hostname.endsWith('.chatgpt.com'))) {
                return { code: 'unexpected-origin',
                  sendButtonCount: 0, sendDisabled: false, busyButtonCount: 0,
                  diagnostics: emptyDiagnostics('Readiness') };
              }

              const composers = ogmComposers({{composerSelectorJson}});
              if (composers.length !== 1) {
                return { code: 'composer-count-before-submit',
                  sendButtonCount: 0, sendDisabled: false, busyButtonCount: 0,
                  diagnostics: emptyDiagnostics('ComposerScope') };
              }

              const composer = composers[0];
              const scope = ogmScope(composer);
              if (!scope) {
                return { code: 'composer-form-missing',
                  sendButtonCount: 0, sendDisabled: false, busyButtonCount: 0,
                  diagnostics: emptyDiagnostics('ComposerScope') };
              }

              const attachmentSelectors = {{attachmentPreviewSelectorsJson}};
              const hasAttachment = ogmHasAttachment(scope);
              const distanceFromScope = element => {
                const scopeAncestors = new Map();
                let current = scope;
                let distance = 0;
                while (current && distance <= {{MaximumDiagnosticScopeDepth}}) {
                  scopeAncestors.set(current, distance++);
                  current = current.parentElement;
                }
                current = element;
                distance = 0;
                while (current && distance <= {{MaximumDiagnosticScopeDepth}}) {
                  if (scopeAncestors.has(current)) return distance + scopeAncestors.get(current);
                  current = current.parentElement;
                  distance++;
                }
                return {{MaximumDiagnosticScopeDepth + 1}};
              };
              const allButtons = [...scope.querySelectorAll('button')].filter(button => ogmOwnedButton(button, scope));
              const discoveredButtons = allButtons.map(button => ({ button, scopeDepth: 0 }));
              const seenButtons = new Set(allButtons);
              for (const button of document.querySelectorAll(
                {{busyButtonSelectorJson}} + ',' + {{sendButtonSelectorJson}})) {
                if (!ogmVisible(button)) continue;
                const scopeDepth = distanceFromScope(button);
                if (scopeDepth <= {{MaximumDiagnosticScopeDepth}} && !seenButtons.has(button)) {
                  seenButtons.add(button);
                  discoveredButtons.push({ button, scopeDepth });
                }
              }
              const busyButtons = [...document.querySelectorAll({{busyButtonSelectorJson}})].filter(ogmVisible);
              const hasVoiceControl = ogmVoice();
              const pageState = hasVoiceControl
                ? (hasAttachment ? 'VoiceComposerWithAttachment' : 'VoiceComposer')
                : (hasAttachment ? 'ComposerWithAttachment' : 'Composer');
              const candidateButtons = discoveredButtons
                .slice(0, {{MaximumDiagnosticButtonCandidates}})
                .map(({ button, scopeDepth }) => ({
                  tagName: button.tagName.toLowerCase(),
                  scopeDepth,
                  type: button.getAttribute('type'),
                  role: button.getAttribute('role'),
                  disabled: Boolean(button.disabled),
                  ariaDisabled: button.getAttribute('aria-disabled') === 'true',
                  ariaLabel: button.getAttribute('aria-label'),
                  dataTestId: button.getAttribute('data-testid')
                }));
              const diagnostics = failureStage => ({
                pageState,
                detectedButtonCount: discoveredButtons.length,
                candidateButtons,
                failureStage,
                attachment: ogmAttachmentDiagnostics(scope)
              });
              const buttons = allButtons.filter(button => button.matches({{sendButtonSelectorJson}}));
              const sendDisabled = buttons.length === 1 &&
                (buttons[0].disabled || buttons[0].getAttribute('aria-disabled') === 'true');
              return {
                code: 'ok',
                sendButtonCount: buttons.length,
                sendDisabled,
                busyButtonCount: busyButtons.length,
                hasAttachment,
                diagnostics: diagnostics('None')
              };
            })()
            """);

            adapterDiagnostics = ParseAdapterDiagnostics(controlResult);
            var sendButtonCount = GetInt32(controlResult, "sendButtonCount");
            var busyButtonCount = GetInt32(controlResult, "busyButtonCount");
            if (!HasExpectedPageState(adapterDiagnostics.PageState, expectedPageState))
            {
                return new(
                    false,
                    false,
                    false,
                    "page-state-changed-before-submit",
                    WebAdapterStatus.NotReady,
                    adapterDiagnostics with { FailureStage = AdapterFailureStage.ButtonValidation });
            }

            if (expectedAudioStateVersion is not null &&
                !GetBoolean(controlResult, "hasAttachment"))
            {
                return new(
                    false,
                    false,
                    false,
                    "attachment-missing-before-submit",
                    WebAdapterStatus.AdapterInvalid,
                    adapterDiagnostics with { FailureStage = AdapterFailureStage.ButtonValidation });
            }

            var decision = DetermineSubmitControlDecision(
                sendButtonCount,
                GetBoolean(controlResult, "sendDisabled"),
                busyButtonCount);
            if (GetCode(controlResult) != "ok")
            {
                return new(
                    false,
                    false,
                    false,
                    GetCode(controlResult),
                    WebAdapterStatus.AdapterInvalid,
                    adapterDiagnostics);
            }

            if (decision == SubmitControlDecision.Fail)
            {
                var failureStage = sendButtonCount > 1 || busyButtonCount > 1
                    ? AdapterFailureStage.ButtonValidation
                    : AdapterFailureStage.ButtonDiscovery;
                return new(
                    false,
                    false,
                    false,
                    "send-control-ambiguous",
                    WebAdapterStatus.AdapterInvalid,
                    adapterDiagnostics with { FailureStage = failureStage });
            }

            if (decision == SubmitControlDecision.Invoke)
            {
                var identityResult = await EvaluateAsync(
                    $$"""
                    (async () => {
                      const composers = ogmComposers({{composerSelectorJson}});
                      if (composers.length !== 1) return { code: 'composer-count-before-invoke' };
                      const attachmentDiagnostics = ogmAttachmentDiagnostics(ogmScope(composers[0]));
                      const code = await ogmVerifyPreparedLocalSource(composers[0]);
                      return { code, attachmentDiagnostics };
                    })()
                    """);
                cancellationToken.ThrowIfCancellationRequested();
                var identityDiagnostics = ParseAttachmentDiagnostics(identityResult);
                if (GetCode(identityResult) != "ok")
                {
                    return new(false, false, false, GetCode(identityResult), WebAdapterStatus.AdapterInvalid,
                        adapterDiagnostics with { FailureStage = AdapterFailureStage.Invocation, Attachment = identityDiagnostics });
                }

                // Local hashing yields. Recheck audio immediately afterward,
                // before the existing synchronous target/button/identity guard.
                if (expectedAudioStateVersion is not null &&
                    !AudioGuardAllowsSubmit(expectedAudioStateVersion.Value, requiredSilentDuration))
                {
                    return new(
                        false,
                        false,
                        false,
                        "audio-state-changed-before-invoke",
                        WebAdapterStatus.NotReady,
                        adapterDiagnostics with { FailureStage = AdapterFailureStage.ButtonValidation });
                }

                var invokeResult = await EvaluateAsync(
                    $$"""
                    (() => {
                      const diagnosticComposers = ogmComposers({{composerSelectorJson}});
                      const attachmentDiagnostics = ogmAttachmentDiagnostics(
                        diagnosticComposers.length === 1 ? ogmScope(diagnosticComposers[0]) : null);
                      const result = (() => {
                      const composers = ogmComposers({{composerSelectorJson}});
                      if (composers.length !== 1) return { code: 'composer-count-before-invoke', invoked: false };
                      const scope = ogmScope(composers[0]);
                      if (!scope) return { code: 'composer-form-missing-before-invoke', invoked: false };
                      const busyButtons = [...document.querySelectorAll({{busyButtonSelectorJson}})].filter(ogmVisible);
                      const buttons = [...scope.querySelectorAll({{sendButtonSelectorJson}})].filter(button => ogmOwnedButton(button, scope));
                      const attachmentSelectors = {{attachmentPreviewSelectorsJson}};
                      const hasAttachment = ogmHasAttachment(scope);
                      const hasVoiceControl = ogmVoice();
                      const pageState = hasVoiceControl
                        ? (hasAttachment ? 'VoiceComposerWithAttachment' : 'VoiceComposer')
                        : (hasAttachment ? 'ComposerWithAttachment' : 'Composer');
                      const expectedPageState = {{expectedPageStateJson}};
                      if (expectedPageState !== null && pageState !== expectedPageState) {
                        return { code: 'page-state-changed-before-invoke', invoked: false };
                      }
                      if ({{requireAttachmentJson}} && !hasAttachment) {
                        return { code: 'attachment-missing-before-invoke', invoked: false };
                      }
                      if (busyButtons.length !== 0 || buttons.length !== 1) {
                        return { code: 'send-control-changed-before-invoke', invoked: false };
                      }
                      const button = buttons[0];
                      if (button.disabled || button.getAttribute('aria-disabled') === 'true') {
                        return { code: 'send-button-disabled-before-invoke', invoked: false };
                      }
                      const preparedCode = ogmPreparedFailure(composers[0]);
                      if (preparedCode !== 'ok') return { code: preparedCode, invoked: false };
                      const prepared = ogmPrepared(composers[0]);
                      if (!prepared || (!prepared.text && !prepared.hasAttachment)) {
                        return { code: 'prepared-input-changed', invoked: false };
                      }
                      prepared.used = true;
                      prepared.invoked = true;
                      button.click();
                      return { code: 'ok', invoked: true };
                      })();
                      return { ...result, attachmentDiagnostics };
                    })()
                    """);
                triggerInvoked = GetBoolean(invokeResult, "invoked");
                submitCode = GetCode(invokeResult);
                var invocationDiagnostics = ParseAttachmentDiagnostics(invokeResult);
                adapterDiagnostics = adapterDiagnostics with
                {
                    Attachment = triggerInvoked && identityDiagnostics is { SrcChanged: true }
                        ? identityDiagnostics : invocationDiagnostics,
                };
                if (!triggerInvoked)
                {
                    var invokeStatus = submitCode == "page-state-changed-before-invoke"
                        ? WebAdapterStatus.NotReady
                        : WebAdapterStatus.AdapterInvalid;
                    return new(
                        false,
                        false,
                        false,
                        submitCode,
                        invokeStatus,
                        adapterDiagnostics with { FailureStage = AdapterFailureStage.Invocation });
                }

                break;
            }

            if (attempt == pollAttempts - 1)
            {
                return new(
                    false,
                    false,
                    false,
                    expectedAudioStateVersion is null
                        ? submitCode
                        : "conversation-busy-before-submit",
                    expectedAudioStateVersion is null
                        ? WebAdapterStatus.AdapterInvalid
                        : WebAdapterStatus.NotReady,
                    adapterDiagnostics with { FailureStage = AdapterFailureStage.ButtonValidation });
            }

            await Task.Delay(SubmitControlPollIntervalMilliseconds, cancellationToken);
        }

        JsonElement stateResult = default;
        for (var attempt = 0; attempt < PostSubmitPollAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(SubmitControlPollIntervalMilliseconds, cancellationToken);
            }

            stateResult = await EvaluateAsync(
            $$"""
            (() => {
              const composers = ogmComposers({{composerSelectorJson}});
              const composer = composers.length === 1 ? composers[0] : null;
              const prepared = globalThis[ogmPreparedKey];
              const sameScope = Boolean(composer && prepared?.invoked &&
                prepared.composer === composer && prepared.scope === ogmScope(composer) &&
                prepared.voice === ogmVoice());
              const composerCleared = sameScope && Boolean(prepared.text) && !ogmText(composer);
              const attachmentCleared = sameScope && prepared.hasAttachment &&
                !ogmHasAttachment(prepared.scope);
              return {
                code: sameScope ? 'ok' : 'submission-scope-changed',
                composerCleared,
                attachmentCleared,
                submissionObserved: sameScope && Boolean(prepared.text || prepared.hasAttachment) &&
                  (!prepared.text || composerCleared) && (!prepared.hasAttachment || attachmentCleared),
                quotaDetected: {{quotaSelectorsJson}}.some(selector => document.querySelector(selector)),
                platformErrorDetected: {{platformErrorSelectorsJson}}.some(selector => document.querySelector(selector))
              };
            })()
            """);
            if (GetCode(stateResult) != "ok" || GetBoolean(stateResult, "submissionObserved") ||
                GetBoolean(stateResult, "quotaDetected") || GetBoolean(stateResult, "platformErrorDetected"))
            {
                break;
            }
        }

        var quotaDetected = GetBoolean(stateResult, "quotaDetected");
        var platformErrorDetected = GetBoolean(stateResult, "platformErrorDetected");
        var composerCleared = GetBoolean(stateResult, "composerCleared");
        var attachmentCleared = GetBoolean(stateResult, "attachmentCleared");
        var status = quotaDetected
            ? WebAdapterStatus.QuotaReached
            : platformErrorDetected
                ? WebAdapterStatus.PlatformError
                : GetBoolean(stateResult, "submissionObserved")
                    ? WebAdapterStatus.Succeeded
                    : WebAdapterStatus.AdapterInvalid;
        var code = status switch
        {
            WebAdapterStatus.QuotaReached => "quota-detected",
            WebAdapterStatus.PlatformError => "platform-error-detected",
            WebAdapterStatus.AdapterInvalid => GetCode(stateResult) == "ok"
                ? "submission-state-not-observed" : GetCode(stateResult),
            _ => GetCode(stateResult),
        };

        return new(
            triggerInvoked,
            composerCleared,
            attachmentCleared,
            code,
            status,
            status == WebAdapterStatus.Succeeded
                ? adapterDiagnostics
                : adapterDiagnostics with { FailureStage = AdapterFailureStage.PostSubmitObservation });
    }

    private bool AudioGuardAllowsSubmit(
        long expectedAudioStateVersion,
        TimeSpan requiredSilentDuration)
    {
        var snapshot = _session.GetAudioSnapshot(DateTimeOffset.UtcNow);
        return snapshot.IsKnown &&
               !snapshot.IsPlaying &&
               snapshot.Version == expectedAudioStateVersion &&
               snapshot.SilentDuration >= requiredSilentDuration;
    }

    public static bool HasExpectedPageState(
        AdapterPageState currentPageState,
        AdapterPageState? expectedPageState) =>
        expectedPageState is null || currentPageState == expectedPageState;

    private static string LoadComposerDomHelpers()
    {
        using var stream = typeof(ChatGptWebAdapter).Assembly.GetManifestResourceStream(
            "OpenGameMate.Adapters.ChatGptComposerDom.js")
            ?? throw new InvalidOperationException("Composer helpers are missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task<JsonElement> EvaluateAsync(string expression)
    {
        var attachmentSelectorsJson = JsonSerializer.Serialize(_rules.AttachmentPreviewSelectors);
        expression = $$"""
            (() => {
              if (location.protocol !== 'https:' || (location.port && location.port !== '443') ||
                !(location.hostname === 'chatgpt.com' || location.hostname.endsWith('.chatgpt.com'))) {
                return { code: 'unexpected-origin' };
              }
              const ogmAttachmentSelectors = {{attachmentSelectorsJson}};
              {{ComposerDomHelpers}}
              return ({{expression}});
            })()
            """;
        using var response = await CallCdpAsync(
            "Runtime.evaluate",
            new { expression, returnByValue = true, awaitPromise = true });
        return ParseRuntimeEvaluateResponse(response.RootElement);
    }

    private async Task<JsonElement> InsertTextAsync(
        string message,
        string composerSelectorJson,
        bool allowPreparedAttachment = false)
    {
        var messageJson = JsonSerializer.Serialize(message);
        var allowPreparedAttachmentJson = allowPreparedAttachment ? "true" : "false";
        return await EvaluateAsync(
            $$"""
            (() => {
              const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
              const composers = ogmComposers({{composerSelectorJson}});
              if (composers.length !== 1) {
                return { code: 'composer-count', count: composers.length,
                  fileSelected: fileInput?.files?.length === 1, textInserted: false };
              }

              const composer = composers[0];
              if (!ogmScope(composer)) return { code: 'composer-scope-missing', textInserted: false };
              if ({{allowPreparedAttachmentJson}} && !ogmSamePreparationTarget(composer)) {
                return { code: 'preparation-target-changed', textInserted: false };
              }
              if (!ogmCanInsertText(composer, {{allowPreparedAttachmentJson}})) {
                return { code: 'composer-not-empty', textInserted: false };
              }
              const text = ogmCanonicalText({{messageJson}});
              if (composer instanceof HTMLTextAreaElement || composer instanceof HTMLInputElement) {
                const prototype = composer instanceof HTMLTextAreaElement
                  ? HTMLTextAreaElement.prototype
                  : HTMLInputElement.prototype;
                const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set;
                if (!setter) return { code: 'native-value-setter-missing', fileSelected: true, textInserted: false };
                setter.call(composer, text);
              } else if (composer.isContentEditable) {
                const paragraph = document.createElement('p');
                paragraph.textContent = text;
                composer.replaceChildren(paragraph);
              } else {
                return { code: 'unsupported-composer',
                  fileSelected: fileInput?.files?.length === 1, textInserted: false };
              }

              composer.dispatchEvent(new InputEvent('input', {
                bubbles: true,
                inputType: 'insertText',
                data: text
              }));
              const actual = composer instanceof HTMLTextAreaElement || composer instanceof HTMLInputElement
                ? composer.value
                : composer.innerText;
              const matches = ogmCanonicalText(actual) === ogmCanonicalText(text);
              if (matches) ogmRemember(composer, {{allowPreparedAttachmentJson}});
              return {
                code: matches ? 'ok' : 'text-not-observed',
                fileSelected: fileInput?.files?.length === 1,
                textInserted: matches
              };
            })()
            """);
    }

    private static bool IsMessageValid(string message) =>
        !string.IsNullOrWhiteSpace(message) && message.Length <= MaximumMessageCharacters;

    public static WebAdapterStatus ClassifyPreparationProbe(
        bool imageAdded,
        bool textInserted,
        bool quotaDetected,
        bool platformErrorDetected)
    {
        if (quotaDetected)
        {
            return WebAdapterStatus.QuotaReached;
        }

        if (platformErrorDetected)
        {
            return WebAdapterStatus.PlatformError;
        }

        return imageAdded && textInserted
            ? WebAdapterStatus.Succeeded
            : WebAdapterStatus.AdapterInvalid;
    }

    public static SubmitControlDecision DetermineSubmitControlDecision(
        int sendButtonCount,
        bool sendButtonDisabled,
        int busyButtonCount)
    {
        if (sendButtonCount is < 0 or > 1 || busyButtonCount is < 0 or > 1)
        {
            return SubmitControlDecision.Fail;
        }

        if (busyButtonCount == 1 || (sendButtonCount == 1 && sendButtonDisabled))
        {
            return SubmitControlDecision.Wait;
        }

        return sendButtonCount == 1
            ? SubmitControlDecision.Invoke
            : SubmitControlDecision.Fail;
    }

    public static JsonElement ParseRuntimeEvaluateResponse(string responseJson)
    {
        using var response = JsonDocument.Parse(responseJson);
        return ParseRuntimeEvaluateResponse(response.RootElement);
    }

    public static AdapterDiagnostics ParseAdapterDiagnostics(JsonElement submitResult)
    {
        if (!submitResult.TryGetProperty("diagnostics", out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Object)
        {
            return new(
                AdapterPageState.Unknown,
                0,
                [],
                AdapterFailureStage.RuntimeEvaluation);
        }

        var pageState = TryReadEnum(diagnostics, "pageState", AdapterPageState.Unknown);
        var failureStage = TryReadEnum(
            diagnostics,
            "failureStage",
            AdapterFailureStage.RuntimeEvaluation);
        var detectedButtonCount = diagnostics.TryGetProperty("detectedButtonCount", out var count) &&
            count.TryGetInt32(out var parsedCount) && parsedCount >= 0
                ? parsedCount
                : 0;
        var candidates = new List<AdapterButtonCandidate>();
        if (diagnostics.TryGetProperty("candidateButtons", out var candidateArray) &&
            candidateArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidateArray.EnumerateArray().Take(MaximumDiagnosticButtonCandidates))
            {
                if (candidate.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                candidates.Add(new AdapterButtonCandidate(
                    ReadBoundedString(candidate, "tagName", 32) ?? "button",
                    ReadBoundedInteger(candidate, "scopeDepth", 0, 32),
                    ReadBoundedString(candidate, "type", 32),
                    ReadBoundedString(candidate, "role", 32),
                    GetBoolean(candidate, "disabled"),
                    GetBoolean(candidate, "ariaDisabled"),
                    ReadBoundedString(candidate, "ariaLabel", 120, allowLocalizedText: true),
                    ReadBoundedString(candidate, "dataTestId", 120)));
            }
        }

        return new(pageState, detectedButtonCount, candidates, failureStage, ParseAttachmentDiagnostics(diagnostics, "attachment"));
    }

    public static AdapterAttachmentDiagnostics? ParseAttachmentDiagnostics(
        JsonElement result,
        string propertyName = "attachmentDiagnostics")
    {
        if (!result.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        AttachmentSourceKind Kind(string name)
        {
            var kind = TryReadEnum(value, name, AttachmentSourceKind.Unknown);
            return Enum.IsDefined(kind) ? kind : AttachmentSourceKind.Unknown;
        }

        var phase = TryReadEnum(value, "uploadPhase", AttachmentUploadPhase.Unknown);
        return new(
            ReadBoundedInteger(value, "beforeCount", 0, 99), ReadBoundedInteger(value, "afterCount", 0, 99),
            Kind("beforeSrc"), Kind("afterSrc"), Kind("beforeSrcSet"), Kind("afterSrcSet"),
            Kind("beforeCurrentSrc"), Kind("afterCurrentSrc"),
            GetBoolean(value, "srcChanged"), GetBoolean(value, "srcSetChanged"), GetBoolean(value, "currentSrcChanged"),
            value.TryGetProperty("ariaBusy", out var busy) && busy.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? busy.GetBoolean() : null,
            GetBoolean(value, "progressBarVisible"), GetBoolean(value, "busyObserved"), GetBoolean(value, "completionObserved"),
            Enum.IsDefined(phase) ? phase : AttachmentUploadPhase.Unknown);
    }

    private static JsonElement ParseRuntimeEvaluateResponse(JsonElement response)
    {
        if (response.TryGetProperty("exceptionDetails", out _))
        {
            throw new InvalidOperationException("The page-side adapter probe failed.");
        }

        if (!response.TryGetProperty("result", out var remoteObject) ||
            remoteObject.ValueKind != JsonValueKind.Object ||
            !remoteObject.TryGetProperty("value", out var value))
        {
            throw new InvalidOperationException("The page-side adapter probe returned an unsupported response shape.");
        }

        return value.Clone();
    }

    private async Task<JsonDocument> CallCdpAsync(string method, object parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        var result = await _session.Core.CallDevToolsProtocolMethodAsync(method, json);
        return JsonDocument.Parse(result);
    }

    private static string GetCode(JsonElement element) =>
        element.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "unknown"
            : "unknown";

    private static string GetPreparationCode(JsonElement element)
    {
        var code = GetCode(element);
        return element.TryGetProperty("count", out var count) && count.TryGetInt32(out var value)
            ? $"{code}-c{Math.Clamp(value, 0, 999)}"
            : code;
    }

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private static int GetInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : -1;

    private static TEnum TryReadEnum<TEnum>(JsonElement element, string propertyName, TEnum fallback)
        where TEnum : struct, Enum =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String &&
        Enum.TryParse<TEnum>(value.GetString(), ignoreCase: false, out var parsed)
            ? parsed
            : fallback;

    private static string? ReadBoundedString(
        JsonElement element,
        string propertyName,
        int maximumLength,
        bool allowLocalizedText = false)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var result = value.GetString();
        if (string.IsNullOrWhiteSpace(result) || result.Length > maximumLength || result.Any(char.IsControl))
        {
            return null;
        }

        if (!allowLocalizedText && result.Any(
                character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            return null;
        }

        return result;
    }

    private static int ReadBoundedInteger(
        JsonElement element,
        string propertyName,
        int minimum,
        int maximum) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt32(out var result) && result >= minimum && result <= maximum
            ? result
            : minimum;
}
