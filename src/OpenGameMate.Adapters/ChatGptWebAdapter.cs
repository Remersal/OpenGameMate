using System.Text.Json;
using System.Runtime.InteropServices;
using OpenGameMate.Browser;
using OpenGameMate.Core;

namespace OpenGameMate.Adapters;

public interface IAiWebAdapter
{
    Task<TextPreparationResult> PrepareTextAsync(
        string message,
        CancellationToken cancellationToken = default);

    Task<InputPreparationResult> PrepareInputAsync(
        string imagePath,
        string message,
        CancellationToken cancellationToken = default);

    Task<SubmissionResult> SubmitAsync(CancellationToken cancellationToken = default);
}

public sealed class ChatGptWebAdapter : IAiWebAdapter
{
    private const string FileInputMarker = "data-opengamemate-file";
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
            return await PrepareInputCoreAsync(imagePath, message, cancellationToken);
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
        string message,
        CancellationToken cancellationToken)
    {
        if (!_session.IsInitialized || !_session.IsOnChatGpt())
        {
            return new(false, false, false, "not-on-chatgpt", WebAdapterStatus.NotReady);
        }

        if (!IsMessageValid(message))
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

        var inputResult = await InsertTextAsync(message, composerSelectorJson);

        await Task.Delay(750, cancellationToken);
        var messageJson = JsonSerializer.Serialize(message);
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
                textInserted: actual === {{messageJson}},
                quotaDetected: quotaSelectors.some(selector => document.querySelector(selector)),
                platformErrorDetected: platformErrorSelectors.some(selector => document.querySelector(selector))
              };
            })()
            """);

        var fileSelected = GetBoolean(probeResult, "fileSelected") ||
            GetBoolean(inputResult, "fileSelected") ||
            GetBoolean(attachmentResult, "fileSelected");
        var previewDetected = GetBoolean(probeResult, "previewDetected");
        var textInserted = GetBoolean(probeResult, "textInserted") ||
            GetBoolean(inputResult, "textInserted");
        var status = ClassifyPreparationProbe(
            fileSelected || previewDetected,
            textInserted,
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

    public async Task<SubmissionResult> SubmitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await SubmitCoreAsync(cancellationToken);
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

    private async Task<SubmissionResult> SubmitCoreAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsInitialized || !_session.IsOnChatGpt())
        {
            return new(false, false, false, "not-on-chatgpt", WebAdapterStatus.NotReady);
        }

        var sendButtonSelectorJson = JsonSerializer.Serialize(_rules.SendButtonSelector);
        var composerSelectorJson = JsonSerializer.Serialize(_rules.ComposerSelector);
        var quotaSelectorsJson = JsonSerializer.Serialize(_rules.QuotaErrorSelectors);
        var platformErrorSelectorsJson = JsonSerializer.Serialize(_rules.PlatformErrorSelectors);

        var submitResult = await EvaluateAsync(
            $$"""
            (() => {
              const buttons = [...document.querySelectorAll({{sendButtonSelectorJson}})];
              if (buttons.length !== 1) return { code: 'send-button-count', count: buttons.length, invoked: false };
              const button = buttons[0];
              if (button.disabled || button.getAttribute('aria-disabled') === 'true') {
                return { code: 'send-button-disabled', invoked: false };
              }
              button.click();
              return { code: 'ok', invoked: true };
            })()
            """);

        if (!GetBoolean(submitResult, "invoked"))
        {
            return new(false, false, false, GetCode(submitResult), WebAdapterStatus.AdapterInvalid);
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
            true,
            composerCleared,
            attachmentCleared,
            code,
            status);
    }

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

    public static JsonElement ParseRuntimeEvaluateResponse(string responseJson)
    {
        using var response = JsonDocument.Parse(responseJson);
        return ParseRuntimeEvaluateResponse(response.RootElement);
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
}
