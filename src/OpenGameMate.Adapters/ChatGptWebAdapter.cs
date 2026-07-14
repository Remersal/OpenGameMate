using System.Text.Json;
using System.Runtime.InteropServices;
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
    private const int SubmitControlPollIntervalMilliseconds = 250;
    private const int MaximumMessageCharacters = 8000;
    private const long MaximumImageBytes = 20 * 1024 * 1024;
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
    }

    public async Task<InputPreparationResult> PrepareImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
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
              document.querySelectorAll('input[{{FileInputMarker}}]')
                .forEach(element => element.removeAttribute('{{FileInputMarker}}'));
              const candidates = [...document.querySelectorAll({{fileInputSelectorJson}})]
                .filter(element => !element.disabled &&
                  (!element.accept || /image|png|jpe?g|gif/i.test(element.accept)));
              if (candidates.length !== 1) {
                return { code: 'file-input-count', count: candidates.length };
              }
              candidates[0].setAttribute('{{FileInputMarker}}', 'true');
              return { code: 'ok', count: 1 };
            })()
            """);

        var attachmentMethod = "file-input";
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

            using (await CallCdpAsync(
                "DOM.setFileInputFiles",
                new { files = new[] { Path.GetFullPath(imagePath) }, nodeId = fileNodeId }))
            {
            }

            attachmentResult = await EvaluateAsync(
                $$"""
                (() => {
                  const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
                  if (!fileInput) return { code: 'file-input-lost', fileSelected: false };
                  fileInput.dispatchEvent(new Event('input', { bubbles: true }));
                  fileInput.dispatchEvent(new Event('change', { bubbles: true }));
                  return { code: 'file-input-events', fileSelected: fileInput.files?.length === 1 };
                })()
                """);
        }
        else if (GetCode(locateFileInput) == "file-input-count")
        {
            // The current ChatGPT composer can omit a persistent file input. A paste event is
            // the narrow fallback: it targets only the composer and never opens a native picker.
            attachmentMethod = "composer-paste";
            var imageBase64Json = JsonSerializer.Serialize(
                Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, cancellationToken)));
            attachmentResult = await EvaluateAsync(
                $$"""
                (async () => {
                  const composers = [...document.querySelectorAll({{composerSelectorJson}})]
                    .filter(element => !element.hasAttribute('disabled'));
                  if (composers.length !== 1) {
                    return { code: 'composer-count-before-paste', count: composers.length,
                      fileSelected: false };
                  }

                  const binary = atob({{imageBase64Json}});
                  const bytes = new Uint8Array(binary.length);
                  for (let index = 0; index < binary.length; index++) {
                    bytes[index] = binary.charCodeAt(index);
                  }
                  const transfer = new DataTransfer();
                  transfer.items.add(new File([bytes], 'opengamemate-screen.png', { type: 'image/png' }));
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
                  await new Promise(resolve => setTimeout(resolve, 350));
                  return { code: 'paste-event-dispatched', fileSelected: false };
                })()
                """);
        }
        else
        {
            return new(false, false, false, GetCode(locateFileInput), WebAdapterStatus.AdapterInvalid);
        }

        var inputFileSelected = false;
        var inputTextInserted = false;
        if (insertText)
        {
            var inputResult = await InsertTextAsync(message!, composerSelectorJson);
            inputFileSelected = GetBoolean(inputResult, "fileSelected");
            inputTextInserted = GetBoolean(inputResult, "textInserted");
        }

        await Task.Delay(750, cancellationToken);
        var messageJson = JsonSerializer.Serialize(message ?? string.Empty);
        var expectTextJson = insertText ? "true" : "false";
        var probeResult = await EvaluateAsync(
            $$"""
            (() => {
              const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
              const composer = document.querySelector({{composerSelectorJson}});
              const scope = composer?.closest('form') ?? document;
              const previewSelectors = {{previewSelectorsJson}};
              const quotaSelectors = {{quotaSelectorsJson}};
              const platformErrorSelectors = {{platformErrorSelectorsJson}};
              const actual = composer instanceof HTMLTextAreaElement || composer instanceof HTMLInputElement
                ? composer.value
                : composer?.innerText;
              return {
                code: 'ok',
                fileSelected: fileInput?.files?.length === 1,
                previewDetected: previewSelectors.some(selector => scope.querySelector(selector)) ||
                  Boolean(scope.querySelector('img[src^="blob:"]')),
                textInserted: {{expectTextJson}} && actual === {{messageJson}},
                quotaDetected: quotaSelectors.some(selector => document.querySelector(selector)),
                platformErrorDetected: platformErrorSelectors.some(selector => document.querySelector(selector))
              };
            })()
            """);

        var fileSelected = GetBoolean(probeResult, "fileSelected") ||
            inputFileSelected ||
            GetBoolean(attachmentResult, "fileSelected");
        var previewDetected = GetBoolean(probeResult, "previewDetected");
        var textInserted = GetBoolean(probeResult, "textInserted") ||
            inputTextInserted;
        var status = ClassifyPreparationProbe(
            fileSelected || previewDetected,
            insertText ? textInserted : true,
            GetBoolean(probeResult, "quotaDetected"),
            GetBoolean(probeResult, "platformErrorDetected"));
        return new(
            fileSelected,
            previewDetected,
            textInserted,
            $"{attachmentMethod}:{GetCode(attachmentResult)}:{GetCode(probeResult)}",
            status);
    }

    public async Task<TextPreparationResult> PrepareTextAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
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
            var result = await InsertTextAsync(message, composerSelectorJson);
            var inserted = GetBoolean(result, "textInserted");
            return new(
                inserted,
                GetCode(result),
                inserted ? WebAdapterStatus.Succeeded : WebAdapterStatus.AdapterInvalid);
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

                  const composers = [...document.querySelectorAll({{composerSelectorJson}})]
                    .filter(element => !element.hasAttribute('disabled'));
                  const composer = composers.length === 1 ? composers[0] : null;
                  const form = composer?.closest('form') ?? null;
                  const stopButtons = [...document.querySelectorAll({{busyButtonSelectorJson}})];
                  const sendButtons = form
                    ? [...form.querySelectorAll({{sendButtonSelectorJson}})]
                    : [];
                  const sendDisabled = sendButtons.length === 1 &&
                    (sendButtons[0].disabled ||
                      sendButtons[0].getAttribute('aria-disabled') === 'true');
                  const attachmentSelectors = {{attachmentPreviewSelectorsJson}};
                  const hasAttachment = Boolean(form) &&
                    (attachmentSelectors.some(selector => form.querySelector(selector)) ||
                      Boolean(form.querySelector('img[src^="blob:"]')));
                  const activeVoiceSelector =
                    'button[data-testid="end-voice-mode-button"],' +
                    '[data-testid="voice-mode-composer"]';
                  const hasVoiceControl = Boolean(document.querySelector(activeVoiceSelector));
                  const pageState = hasVoiceControl
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

              const composers = [...document.querySelectorAll({{composerSelectorJson}})]
                .filter(element => !element.hasAttribute('disabled'));
              if (composers.length !== 1) {
                return { code: 'composer-count-before-submit',
                  sendButtonCount: 0, sendDisabled: false, busyButtonCount: 0,
                  diagnostics: emptyDiagnostics('ComposerScope') };
              }

              const composer = composers[0];
              const scope = composer.closest('form');
              if (!scope) {
                return { code: 'composer-form-missing',
                  sendButtonCount: 0, sendDisabled: false, busyButtonCount: 0,
                  diagnostics: emptyDiagnostics('ComposerScope') };
              }

              const attachmentSelectors = {{attachmentPreviewSelectorsJson}};
              const hasAttachment = attachmentSelectors.some(selector => scope.querySelector(selector)) ||
                Boolean(scope.querySelector('img[src^="blob:"]'));
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
              const allButtons = [...scope.querySelectorAll('button')];
              const discoveredButtons = allButtons.map(button => ({ button, scopeDepth: 0 }));
              const seenButtons = new Set(allButtons);
              for (const button of document.querySelectorAll(
                {{busyButtonSelectorJson}} + ',' + {{sendButtonSelectorJson}})) {
                const scopeDepth = distanceFromScope(button);
                if (scopeDepth <= {{MaximumDiagnosticScopeDepth}} && !seenButtons.has(button)) {
                  seenButtons.add(button);
                  discoveredButtons.push({ button, scopeDepth });
                }
              }
              const busyButtons = [...document.querySelectorAll({{busyButtonSelectorJson}})]
                .filter(button => distanceFromScope(button) <= {{MaximumDiagnosticScopeDepth}});
              const hasVoiceControl = busyButtons.length === 1 || Boolean(scope.querySelector(
                'button[data-testid="voice-mode-button"],button[data-testid="end-voice-mode-button"],' +
                '[data-testid="voice-mode-composer"]'));
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
                failureStage
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
                      const composers = [...document.querySelectorAll({{composerSelectorJson}})]
                        .filter(element => !element.hasAttribute('disabled'));
                      if (composers.length !== 1) return { code: 'composer-count-before-invoke', invoked: false };
                      const scope = composers[0].closest('form');
                      if (!scope) return { code: 'composer-form-missing-before-invoke', invoked: false };
                      const busyButtons = [...document.querySelectorAll({{busyButtonSelectorJson}})];
                      const buttons = [...scope.querySelectorAll({{sendButtonSelectorJson}})];
                      const attachmentSelectors = {{attachmentPreviewSelectorsJson}};
                      const hasAttachment = attachmentSelectors.some(selector => scope.querySelector(selector)) ||
                        Boolean(scope.querySelector('img[src^="blob:"]'));
                      const hasVoiceControl = busyButtons.length === 1 || Boolean(scope.querySelector(
                        'button[data-testid="voice-mode-button"],button[data-testid="end-voice-mode-button"],' +
                        '[data-testid="voice-mode-composer"]'));
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
                      button.click();
                      return { code: 'ok', invoked: true };
                    })()
                    """);
                triggerInvoked = GetBoolean(invokeResult, "invoked");
                submitCode = GetCode(invokeResult);
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

        await Task.Delay(1000, cancellationToken);
        var stateResult = await EvaluateAsync(
            $$"""
            (() => {
              const composer = document.querySelector({{composerSelectorJson}});
              const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
              const quotaSelectors = {{quotaSelectorsJson}};
              const platformErrorSelectors = {{platformErrorSelectorsJson}};
              const text = composer instanceof HTMLTextAreaElement || composer instanceof HTMLInputElement
                ? composer.value
                : composer?.innerText;
              return {
                code: 'ok',
                composerCleared: !text,
                attachmentCleared: !fileInput || !fileInput.files || fileInput.files.length === 0,
                quotaDetected: quotaSelectors.some(selector => document.querySelector(selector)),
                platformErrorDetected: platformErrorSelectors.some(selector => document.querySelector(selector))
              };
            })()
            """);

        var quotaDetected = GetBoolean(stateResult, "quotaDetected");
        var platformErrorDetected = GetBoolean(stateResult, "platformErrorDetected");
        var composerCleared = GetBoolean(stateResult, "composerCleared");
        var attachmentCleared = GetBoolean(stateResult, "attachmentCleared");
        var status = quotaDetected
            ? WebAdapterStatus.QuotaReached
            : platformErrorDetected
                ? WebAdapterStatus.PlatformError
                : composerCleared || attachmentCleared
                    ? WebAdapterStatus.Succeeded
                    : WebAdapterStatus.AdapterInvalid;
        var code = status switch
        {
            WebAdapterStatus.QuotaReached => "quota-detected",
            WebAdapterStatus.PlatformError => "platform-error-detected",
            WebAdapterStatus.AdapterInvalid => "submission-state-not-observed",
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

    private async Task<JsonElement> EvaluateAsync(string expression)
    {
        using var response = await CallCdpAsync(
            "Runtime.evaluate",
            new { expression, returnByValue = true, awaitPromise = true });
        return ParseRuntimeEvaluateResponse(response.RootElement);
    }

    private async Task<JsonElement> InsertTextAsync(string message, string composerSelectorJson)
    {
        var messageJson = JsonSerializer.Serialize(message);
        return await EvaluateAsync(
            $$"""
            (() => {
              const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
              const composers = [...document.querySelectorAll({{composerSelectorJson}})]
                .filter(element => !element.hasAttribute('disabled'));
              if (composers.length !== 1) {
                return { code: 'composer-count', count: composers.length,
                  fileSelected: fileInput?.files?.length === 1, textInserted: false };
              }

              const composer = composers[0];
              const text = {{messageJson}};
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
              return {
                code: 'ok',
                fileSelected: fileInput?.files?.length === 1,
                textInserted: actual === text
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

        return new(pageState, detectedButtonCount, candidates, failureStage);
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
