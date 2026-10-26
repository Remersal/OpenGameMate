using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OpenGameMate.Adapters;
using OpenGameMate.Browser;
using OpenGameMate.Core;

namespace OpenGameMate.WebView2Smoke;

internal static class Program
{
    private static int _exitCode;
    private static int _assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await RunAsync(args.Contains("--probe-live-readiness", StringComparer.Ordinal));
                if (_assertions > 0)
                {
                    Console.WriteLine($"PASS: {_assertions} WebView2 workflow assertions; synthetic HTML only.");
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                _exitCode = 1;
            }
            finally
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        Dispatcher.Run();
        return _exitCode;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }

        _assertions++;
        Console.WriteLine($"PASS: {description}");
    }

    private static async Task RunAsync(bool probeLiveReadiness)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"OpenGameMate-WebView2Smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, "synthetic.png");
        await File.WriteAllBytesAsync(image, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aGZsAAAAASUVORK5CYII="));

        using var view = new WebView2();
        // An invisible HWND hosts WebView2; never show or capture a desktop window.
        using var host = new HwndSource(new HwndSourceParameters("OpenGameMate synthetic tests")
        {
            Width = 800,
            Height = 600,
            WindowStyle = unchecked((int)0x80000000),
        });
        host.RootVisual = view;
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));
        var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (!args.IsSuccess)
            {
                navigated.TrySetException(args.InitializationException);
                return;
            }

            var core = view.CoreWebView2;
            if (!probeLiveReadiness)
            {
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, request) =>
                {
                    // Fulfill every request locally before network access, including the initial URL.
                    var bytes = Encoding.UTF8.GetBytes("<!doctype html><html><body>Synthetic test</body></html>");
                    request.Response = core.Environment.CreateWebResourceResponse(
                        new MemoryStream(bytes), 200, "OK", "Content-Type: text/html; charset=utf-8");
                };
            }
            core.NavigationCompleted += (_, _) => navigated.TrySetResult();
        };
        using var session = new ChatGptBrowserSession(view, Path.Combine(directory, "profile"), _ => Task.FromResult(false));
        await session.InitializeAsync();
        await navigated.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Console.WriteLine($"WebView2 runtime: {session.Core.Environment.BrowserVersionString}");
        var adapter = new ChatGptWebAdapter(session);
        const string message = "OpenGameMate synthetic workflow check";

        if (probeLiveReadiness)
        {
            // A new empty profile, read-only readiness metadata, no sign-in or real send.
            await Task.Delay(TimeSpan.FromSeconds(10));
            var probe = await adapter.ProbeIdleAsync();
            Console.WriteLine($"LIVE READ-ONLY: status={probe.Status}; code={probe.Code}; " +
                $"origin={probe.DomainCorrect}; composers={probe.ComposerCount}; send={probe.SendButtonCount}; " +
                $"stop={probe.StopButtonCount}; page={probe.Diagnostics.PageState}");
            return;
        }

        async Task<JsonElement> Eval(string script)
        {
            var response = await session.Core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new { expression = script, returnByValue = true, awaitPromise = true }));
            return ChatGptWebAdapter.ParseRuntimeEvaluateResponse(response);
        }

        async Task Fixture(bool acceptsImage = true, bool clearsImage = true, bool pasteOnly = false)
        {
            var frameTree = await session.Core.CallDevToolsProtocolMethodAsync("Page.getFrameTree", "{}");
            using var document = JsonDocument.Parse(frameTree);
            var frameId = document.RootElement.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
            var html = "<!doctype html><html><body><form id='target'><textarea id='prompt-textarea'></textarea>" +
                (pasteOnly ? "" : "<input type='file' accept='image/*' hidden>") +
                "<button type='button' data-testid='send-button'>Send</button></form></body></html>";
            await session.Core.CallDevToolsProtocolMethodAsync("Page.setDocumentContent", JsonSerializer.Serialize(new { frameId, html }));
            await Eval("globalThis.__openGameMatePreparedV1=null; globalThis.clicks=0; globalThis.changes=0;");
            await Eval("globalThis.acceptFile=file=>{const p=document.createElement('div');p.dataset.testid='attachment';p.setAttribute('aria-busy','true');const i=document.createElement('img');i.width=64;i.height=36;i.src=URL.createObjectURL(file);p.append(i);document.querySelector('#target').append(p);setTimeout(()=>p.setAttribute('aria-busy','false'),80);};");
            var attach = acceptsImage
                ? "acceptFile(e.clipboardData?.files[0]??e.target.files[0]);"
                : "";
            await Eval(pasteOnly
                ? "document.querySelector('textarea').onpaste=e=>{globalThis.changes++;" + attach + "e.preventDefault();};"
                : "document.querySelector('input').onchange=e=>{globalThis.changes++;" + attach + "};");
            await Eval("document.querySelector('button').onclick=()=>{globalThis.clicks++;document.querySelector('textarea').value='';" +
                (clearsImage ? "document.querySelectorAll('[data-testid=attachment]').forEach(x=>x.remove());" : "") + "};");
        }

        await Fixture();
        var text = await adapter.PrepareTextAsync(message);
        Check(text.Status == WebAdapterStatus.Succeeded && text.TextInserted, "role text prepares through production C#");
        var submission = await adapter.SubmitAsync();
        Check(submission.Status == WebAdapterStatus.Succeeded && submission.ComposerCleared && !submission.AttachmentCleared,
            "text send requires observed text clearance, without marker-based image inference");
        Check((await adapter.SubmitAsync()).Code == "input-not-prepared", "a second Submit cannot click again");
        Check((await Eval("globalThis.clicks")).GetInt32() == 1, "exactly one click after repeated Submit");

        await Fixture();
        var combined = await adapter.PrepareInputAsync(image, message);
        Check(combined.Status == WebAdapterStatus.Succeeded && combined.AttachmentPreviewDetected && combined.TextInserted,
            "combined image and text prepare through CDP and the shared composer resolver");
        Check((await Eval("globalThis.changes")).GetInt32() == 1, "CDP upload dispatches exactly one change");
        submission = await adapter.SubmitAsync();
        Check(submission.Status == WebAdapterStatus.Succeeded && submission.ComposerCleared && submission.AttachmentCleared,
            "combined send confirms both text and preview clearance");

        await Fixture();
        var imageOnly = await adapter.PrepareImageAsync(image);
        Check(imageOnly.Status == WebAdapterStatus.Succeeded, "automatic image stage prepares");
        text = await adapter.PrepareTextAsync(message);
        Check(text.Status == WebAdapterStatus.Succeeded, "automatic text stage extends only its own image preparation");
        var cancelled = new CancellationToken(canceled: true);
        try
        {
            await adapter.SubmitAsync(cancelled);
            throw new InvalidOperationException("Cancelled submit did not cancel.");
        }
        catch (OperationCanceledException)
        {
            Check(true, "cancelled submission propagates cancellation");
        }

        Check((await adapter.SubmitAsync()).Code == "input-not-prepared", "cancellation consumes the prepared submit permit");
        Check((await Eval("globalThis.clicks")).GetInt32() == 0, "cancellation never clicks or permits a retry");

        await Fixture(clearsImage: false);
        await adapter.PrepareInputAsync(image, message);
        submission = await adapter.SubmitAsync();
        Check(submission.Status == WebAdapterStatus.AdapterInvalid && submission.ComposerCleared && !submission.AttachmentCleared,
            "partial clearance is an unconfirmed send");
        Check((await adapter.SubmitAsync()).Code == "input-not-prepared", "unconfirmed send cannot be retried without new preparation");

        await Fixture(acceptsImage: false);
        imageOnly = await adapter.PrepareImageAsync(image);
        Check(imageOnly.Status == WebAdapterStatus.AdapterInvalid && imageOnly.Code == "attachment-not-observed",
            "file selection alone is not accepted attachment evidence");
        Check((await adapter.SubmitAsync()).Code == "input-not-prepared", "failed image preparation cannot submit");
        Check((await adapter.PrepareTextAsync(message)).Status == WebAdapterStatus.Succeeded,
            "a fresh empty text draft does not inherit failed upload state");
        Check((await adapter.SubmitAsync()).Status == WebAdapterStatus.Succeeded,
            "text-only preparation after an unaccepted image has its own one-shot permit");

        await Fixture();
        await adapter.PrepareTextAsync(message);
        await Eval("(() => {const b=document.createElement('button');b.dataset.testid='end-voice-mode-button';b.textContent='End Voice';document.body.append(b);return true;})()");
        Check((await adapter.ProbeIdleAsync()).Diagnostics.PageState == AdapterPageState.VoiceComposer,
            "active Voice transition is reported by the production idle probe");
        Check((await adapter.SubmitAsync()).Status == WebAdapterStatus.AdapterInvalid, "Voice transition invalidates prepared manual send");
        Check((await Eval("globalThis.clicks")).GetInt32() == 0, "Voice transition does not invoke send");

        await Fixture(pasteOnly: true);
        combined = await adapter.PrepareInputAsync(image, message);
        Check(combined.Status == WebAdapterStatus.Succeeded, "composer paste prepares only when the synthetic receiver accepts it");
        Check((await adapter.SubmitAsync()).Status == WebAdapterStatus.Succeeded, "paste workflow confirms actual preview clearance");

        await Fixture(pasteOnly: true);
        await Eval("(() => {const form=document.querySelector('form');form.setAttribute('data-chatgpt-composer','');form.onsubmit=e=>e.preventDefault();const editor=document.createElement('div');editor.contentEditable='true';editor.setAttribute('role','textbox');editor.onpaste=document.querySelector('textarea').onpaste;document.querySelector('textarea').replaceWith(editor);for(let n=0;n<4;n++){const i=document.createElement('input');i.type='file';i.accept='image/*';i.hidden=true;form.append(i);}const b=document.querySelector('button');b.removeAttribute('data-testid');b.type='submit';b.onclick=()=>{globalThis.clicks++;editor.replaceChildren();form.querySelectorAll('[data-testid=attachment]').forEach(p=>p.remove());};return true;})()");
        combined = await adapter.PrepareInputAsync(image, message);
        Check(combined.Status == WebAdapterStatus.Succeeded && combined.AttachmentPreviewDetected && combined.TextInserted,
            "live unified form prepares through its unique editable receiver with ambiguous native inputs");
        Check((await Eval("[...document.querySelectorAll('input')].every(i=>i.files.length===0)")).GetBoolean(),
            "ambiguous native file inputs are never selected");
        Check((await adapter.SubmitAsync()).Status == WebAdapterStatus.Succeeded,
            "unified form invokes one semantic submit and confirms both channels");

        await Fixture();
        await adapter.PrepareInputAsync(image, message);
        await Eval("document.querySelector('button').onclick=()=>{globalThis.clicks++;setTimeout(()=>{document.querySelector('textarea').value='';document.querySelectorAll('[data-testid=attachment]').forEach(p=>p.remove());},1500);};");
        submission = await adapter.SubmitAsync();
        Check(submission.Status == WebAdapterStatus.Succeeded && submission.ComposerCleared && submission.AttachmentCleared,
            "asynchronous clearance after the former one-second deadline is observed without a second click");
        Check((await Eval("globalThis.clicks")).GetInt32() == 1, "post-submit observation polls never repeat the send");

        await Fixture(pasteOnly: true);
        await Eval("(() => {const form=document.querySelector('form');form.setAttribute('data-chatgpt-composer','');const editor=document.createElement('div');editor.contentEditable='true';editor.setAttribute('role','textbox');document.querySelector('textarea').replaceWith(editor);const mic=document.createElement('button');mic.type='button';mic.setAttribute('aria-label','关闭麦克风');form.append(mic);form.querySelector('[data-testid=send-button]').onclick=()=>{globalThis.clicks++;form.querySelector('[role=textbox]').replaceChildren();};return true;})()");
        text = await adapter.PrepareTextAsync(message);
        Check(text.Status == WebAdapterStatus.Succeeded, "marked Voice-like composer prepares synthetic role text");
        await Eval("(() => {const e=document.querySelector('[role=textbox]');e.replaceWith(e.cloneNode(true));return true;})()");
        submission = await adapter.SubmitAsync();
        Check(submission.Status == WebAdapterStatus.Succeeded, "identical detached editor redraw in the same form remains prepared");
        Check((await Eval("globalThis.clicks")).GetInt32() == 1 && (await adapter.SubmitAsync()).Code == "input-not-prepared",
            "editor redraw does not grant a second submit permit");

        async Task PrepareImageLeafAsync()
        {
            await Fixture();
            var prepared = await adapter.PrepareInputAsync(image, message);
            Check(prepared.Status == WebAdapterStatus.Succeeded, "synthetic immutable attachment leaf prepares through production C#");
        }

        await PrepareImageLeafAsync();
        await Eval("(() => {const p=document.querySelector('[data-testid=attachment]');p.replaceWith(p.cloneNode(true));return true;})()");
        Check((await adapter.SubmitAsync()).Status == WebAdapterStatus.Succeeded,
            "same immutable attachment leaf survives wrapper redraw without changing the payload");

        await PrepareImageLeafAsync();
        await Eval("document.querySelector('img').src='data:image/png;base64,DIFFERENT'");
        submission = await adapter.SubmitAsync();
        Check(submission.Code == "prepared-attachment-source-changed" && !submission.TriggerInvoked,
            "same-node image swap returns a bounded source-change code without clicking");
        Check((await adapter.SubmitAsync()).Code == "input-not-prepared" && (await Eval("globalThis.clicks")).GetInt32() == 0,
            "a rejected image swap cannot be retried using the consumed permit");

        async Task DelayedUploadFixtureAsync(string script, bool busy = true)
        {
            await Fixture(pasteOnly: true);
            await Eval("document.querySelector('textarea').onpaste=e=>{globalThis.changes++;const file=e.clipboardData.files[0];globalThis.originalFile=file;" +
                "const p=document.createElement('div');p.dataset.testid='attachment';p.dataset.fileName=file.name;" +
                (busy ? "p.setAttribute('aria-busy','true');" : "") +
                "const i=document.createElement('img');i.width=64;i.height=36;i.src=URL.createObjectURL(file);p.append(i);document.querySelector('form').append(p);" +
                script + "e.preventDefault();};");
        }

        await DelayedUploadFixtureAsync("setTimeout(()=>i.src=URL.createObjectURL(file),500);setTimeout(()=>i.src=URL.createObjectURL(file),1200);setTimeout(()=>p.setAttribute('aria-busy','false'),1800);");
        var started = DateTimeOffset.UtcNow;
        combined = await adapter.PrepareInputAsync(image, message);
        Check(combined.Status == WebAdapterStatus.Succeeded && DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(1700),
            "preparation waits beyond 750ms for repeated content-bound preview changes and explicit completion");
        Check((await Eval("globalThis.changes")).GetInt32() == 1 && (await Eval("globalThis.clicks")).GetInt32() == 0,
            "upload observation neither repeats paste nor submits");
        Check((await Eval("globalThis.__openGameMatePreparedV1.upload.file===null")).GetBoolean(),
            "completed preparation releases its original File reference");
        Check((await adapter.SubmitAsync()).Status == WebAdapterStatus.Succeeded &&
            (await adapter.SubmitAsync()).Code == "input-not-prepared" && (await Eval("globalThis.clicks")).GetInt32() == 1,
            "completed delayed upload grants exactly one production submit permit");

        foreach (var (mutation, expectedCode) in new[]
        {
            ("i.src=URL.createObjectURL(new File(['different bytes'],file.name,{type:'image/png'}));", "attachment-content-changed"),
            ("i.src='https://chatgpt.com/synthetic-upload-result.png';", "attachment-content-unverifiable"),
            ("document.querySelector('form').append(p.cloneNode(true));", "attachment-count-changed-during-upload"),
            ("p.remove();", "attachment-count-changed-during-upload"),
            ("document.querySelector('textarea').value='edited';", "preparation-target-changed"),
            ("document.querySelector('form').replaceWith(document.querySelector('form').cloneNode(true));", "preparation-target-changed"),
            ("const b=document.createElement('button');b.dataset.testid='end-voice-mode-button';document.body.append(b);", "preparation-target-changed"),
        })
        {
            await DelayedUploadFixtureAsync("setTimeout(()=>{" + mutation + "},500);setTimeout(()=>p.setAttribute('aria-busy','false'),900);");
            combined = await adapter.PrepareInputAsync(image, message);
            Check(combined.Code == expectedCode && combined.Status != WebAdapterStatus.Succeeded,
                $"upload mutation fails with bounded code: {expectedCode}");
            Check((await adapter.SubmitAsync()).Code == "input-not-prepared" && (await Eval("globalThis.clicks")).GetInt32() == 0,
                "rejected upload mutation never grants a click or retry");
        }

        await DelayedUploadFixtureAsync("setTimeout(()=>p.setAttribute('aria-busy','false'),300);");
        await adapter.PrepareInputAsync(image, message);
        await Eval("document.querySelector('img').src=URL.createObjectURL(originalFile)");
        Check((await adapter.SubmitAsync()).Code == "prepared-attachment-source-changed" && (await Eval("globalThis.clicks")).GetInt32() == 0,
            "even equal bytes at a new URL are rejected after completion freezes the snapshot");

        await DelayedUploadFixtureAsync("setTimeout(()=>p.setAttribute('aria-busy','false'),300);setTimeout(()=>i.src=URL.createObjectURL(file),400);");
        combined = await adapter.PrepareInputAsync(image, message);
        Check(combined.Code == "attachment-source-changed-after-completion",
            "source changes between completion and final preparation observation cannot be re-baselined");
        Check((await adapter.SubmitAsync()).Code == "input-not-prepared" && (await Eval("globalThis.clicks")).GetInt32() == 0,
            "a completion race grants no production submit permit");

        await DelayedUploadFixtureAsync("");
        using (var duringUpload = new CancellationTokenSource(TimeSpan.FromMilliseconds(900)))
        {
            try
            {
                await adapter.PrepareInputAsync(image, message, duringUpload.Token);
                throw new InvalidOperationException("Upload preparation did not cancel.");
            }
            catch (OperationCanceledException)
            {
                Check(true, "cancellation during upload observation propagates");
            }
        }

        Check((await Eval("globalThis.changes===1 && globalThis.clicks===0 && document.querySelector('textarea').value.length>0 && document.querySelector('img')!==null && globalThis.__openGameMatePreparedV1.upload.file===null")).GetBoolean() &&
            (await adapter.SubmitAsync()).Code == "input-not-prepared",
            "cancelled preparation keeps the draft, releases observation and never retries paste/send");

        foreach (var busy in new[] { true, false })
        {
            await DelayedUploadFixtureAsync("", busy);
            combined = await adapter.PrepareInputAsync(image, message);
            Check(combined.Code == (busy ? "attachment-upload-timeout" : "attachment-completion-unobserved") &&
                combined.Status == WebAdapterStatus.AdapterInvalid && combined.AttachmentPreviewDetected && combined.TextInserted,
                "timeout distinguishes pending upload from missing completion evidence");
            Check((await Eval("globalThis.changes===1 && globalThis.clicks===0 && document.querySelector('textarea').value.length>0 && document.querySelector('img')!==null && globalThis.__openGameMatePreparedV1.upload.file===null")).GetBoolean() &&
                (await adapter.SubmitAsync()).Code == "input-not-prepared",
                "timed out upload keeps both draft channels, releases observation and grants no submit permit");
        }
    }
}
