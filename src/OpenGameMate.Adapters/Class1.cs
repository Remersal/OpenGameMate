using System.Text.Json;
using OpenGameMate.Browser;
using OpenGameMate.Core;

namespace OpenGameMate.Adapters;

public interface IAiWebAdapter
{
    Task<InputPreparationResult> PrepareInputAsync(string imagePath, string message);

    Task<SubmissionResult> SubmitAsync();
}

public sealed class ChatGptWebAdapter : IAiWebAdapter
{
    private const string FileInputMarker = "data-opengamemate-phase0-file";
    private readonly ChatGptBrowserSession _session;

    public ChatGptWebAdapter(ChatGptBrowserSession session)
    {
        _session = session;
    }

    public async Task<InputPreparationResult> PrepareInputAsync(string imagePath, string message)
    {
        if (!_session.IsInitialized || !_session.IsOnChatGpt())
        {
            return new(false, false, false, "not-on-chatgpt");
        }

        if (!File.Exists(imagePath))
        {
            return new(false, false, false, "image-file-missing");
        }

        var locateFileInput = await EvaluateAsync(
            """
            (() => {
              if (!(location.hostname === 'chatgpt.com' || location.hostname.endsWith('.chatgpt.com'))) {
                return { code: 'unexpected-origin', count: 0 };
              }
              document.querySelectorAll('input[data-opengamemate-phase0-file]')
                .forEach(element => element.removeAttribute('data-opengamemate-phase0-file'));
              const candidates = [...document.querySelectorAll('input[type="file"]')]
                .filter(element => !element.disabled &&
                  (!element.accept || /image|png|jpe?g|gif/i.test(element.accept)));
              if (candidates.length !== 1) {
                return { code: 'file-input-count', count: candidates.length };
              }
              candidates[0].setAttribute('data-opengamemate-phase0-file', 'true');
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
                return new(false, false, false, "marked-file-input-not-found");
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
                Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath)));
            attachmentResult = await EvaluateAsync(
                $$"""
                (async () => {
                  const composers = [...document.querySelectorAll('#prompt-textarea')]
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
                  transfer.items.add(new File([bytes], 'opengamemate-phase0.png', { type: 'image/png' }));
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
            return new(false, false, false, GetCode(locateFileInput));
        }

        var messageJson = JsonSerializer.Serialize(message);
        var inputResult = await EvaluateAsync(
            $$"""
            (() => {
              const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
              const composers = [...document.querySelectorAll('#prompt-textarea')]
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

        await Task.Delay(750);
        var probeResult = await EvaluateAsync(
            $$"""
            (() => {
              const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
              const composer = document.querySelector('#prompt-textarea');
              const scope = composer?.closest('form') ?? document;
              const previewSelectors = [
                '[data-testid="file-thumbnail"]',
                '[data-testid="file-thumbnail-container"]',
                '[data-testid="attachment"]',
                '[data-testid^="composer-attachment"]',
                '[data-testid*="attachment-preview"]',
                '[data-testid="remove-file-button"]'
              ];
              const actual = composer instanceof HTMLTextAreaElement || composer instanceof HTMLInputElement
                ? composer.value
                : composer?.innerText;
              return {
                code: 'ok',
                fileSelected: fileInput?.files?.length === 1,
                previewDetected: previewSelectors.some(selector => scope.querySelector(selector)) ||
                  Boolean(scope.querySelector('img[src^="blob:"]')),
                textInserted: actual === {{messageJson}}
              };
            })()
            """);

        var fileSelected = GetBoolean(probeResult, "fileSelected") ||
            GetBoolean(inputResult, "fileSelected") ||
            GetBoolean(attachmentResult, "fileSelected");
        var previewDetected = GetBoolean(probeResult, "previewDetected");
        return new(
            fileSelected,
            previewDetected,
            GetBoolean(probeResult, "textInserted") || GetBoolean(inputResult, "textInserted"),
            $"{attachmentMethod}:{GetCode(attachmentResult)}:{GetCode(probeResult)}");
    }

    public async Task<SubmissionResult> SubmitAsync()
    {
        if (!_session.IsInitialized || !_session.IsOnChatGpt())
        {
            return new(false, false, false, "not-on-chatgpt");
        }

        var submitResult = await EvaluateAsync(
            """
            (() => {
              const buttons = [...document.querySelectorAll('button[data-testid="send-button"]')];
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
            return new(false, false, false, GetCode(submitResult));
        }

        await Task.Delay(1000);
        var stateResult = await EvaluateAsync(
            $$"""
            (() => {
              const composer = document.querySelector('#prompt-textarea');
              const fileInput = document.querySelector('input[{{FileInputMarker}}="true"]');
              const text = composer instanceof HTMLTextAreaElement || composer instanceof HTMLInputElement
                ? composer.value
                : composer?.innerText;
              return {
                code: 'ok',
                composerCleared: !text,
                attachmentCleared: !fileInput || !fileInput.files || fileInput.files.length === 0
              };
            })()
            """);

        return new(
            true,
            GetBoolean(stateResult, "composerCleared"),
            GetBoolean(stateResult, "attachmentCleared"),
            GetCode(stateResult));
    }

    private async Task<JsonElement> EvaluateAsync(string expression)
    {
        using var response = await CallCdpAsync(
            "Runtime.evaluate",
            new { expression, returnByValue = true, awaitPromise = true });
        return ParseRuntimeEvaluateResponse(response.RootElement);
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
            throw new InvalidOperationException("The page-side Phase 0 probe failed.");
        }

        if (!response.TryGetProperty("result", out var remoteObject) ||
            remoteObject.ValueKind != JsonValueKind.Object ||
            !remoteObject.TryGetProperty("value", out var value))
        {
            throw new InvalidOperationException("The page-side Phase 0 probe returned an unsupported response shape.");
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
