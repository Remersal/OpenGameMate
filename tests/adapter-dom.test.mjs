import assert from 'node:assert/strict';
import { test, before, after } from 'node:test';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

// Execute the shipped JS and every production C# probe in a real, isolated DOM.
// The test URL is intercepted before network access; no ChatGPT account is used.
const root = fileURLToPath(new URL('../', import.meta.url));
const source = await readFile(join(root, 'src/OpenGameMate.Adapters/ChatGptWebAdapter.cs'), 'utf8');
const rules = await readFile(join(root, 'src/OpenGameMate.Adapters/ChatGptAdapterRules.cs'), 'utf8');
const helpers = await readFile(join(root, 'src/OpenGameMate.Adapters/ChatGptComposerDom.js'), 'utf8');
const literals = [...source.matchAll(/\$\$"""\r?\n([\s\S]*?)\r?\n\s*"""/g)].map(x => x[1]);
function literal(needle) {
  const matches = literals.filter(x => x.includes(needle));
  assert.equal(matches.length, 1, `unique production probe: ${needle}`);
  return matches[0];
}
const stringRule = name => JSON.parse('"' + rules.match(new RegExp(name + ' = "((?:\\\\.|[^"\\\\])*)"'))[1] + '"');
const arrayRule = name => [...rules.match(new RegExp(name + ' =\\s*\\[([\\s\\S]*?)\\r?\\n\\s*\\],'))[1]
  .matchAll(/"((?:\\.|[^"\\])*)"/g)].map(x => JSON.parse('"' + x[1] + '"'));
const message = 'OpenGameMate synthetic adapter check';
const imageBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aGZsAAAAASUVORK5CYII=';
const values = {
  composerSelectorJson: JSON.stringify(stringRule('ComposerSelector')),
  fileInputSelectorJson: JSON.stringify(stringRule('FileInputSelector')),
  sendButtonSelectorJson: JSON.stringify(stringRule('SendButtonSelector')),
  busyButtonSelectorJson: JSON.stringify(stringRule('BusyButtonSelector')),
  previewSelectorsJson: JSON.stringify(arrayRule('AttachmentPreviewSelectors')),
  attachmentPreviewSelectorsJson: JSON.stringify(arrayRule('AttachmentPreviewSelectors')),
  attachmentSelectorsJson: JSON.stringify(arrayRule('AttachmentPreviewSelectors')),
  quotaSelectorsJson: JSON.stringify(arrayRule('QuotaErrorSelectors')),
  platformErrorSelectorsJson: JSON.stringify(arrayRule('PlatformErrorSelectors')),
  FileInputMarker: source.match(/FileInputMarker = "([^"]+)"/)[1],
  MaximumDiagnosticButtonCandidates: source.match(/MaximumDiagnosticButtonCandidates = (\d+)/)[1],
  MaximumDiagnosticScopeDepth: source.match(/MaximumDiagnosticScopeDepth = (\d+)/)[1],
  'MaximumDiagnosticScopeDepth + 1': String(Number(source.match(/MaximumDiagnosticScopeDepth = (\d+)/)[1]) + 1),
  expectTextJson: 'true', messageJson: JSON.stringify(message), imageBase64Json: JSON.stringify(imageBase64),
  requireAttachmentJson: 'false', expectedPageStateJson: 'null', allowPreparedAttachmentJson: 'false',
  ComposerDomHelpers: helpers,
};
function render(template, overrides = {}) {
  const map = { ...values, ...overrides };
  const result = template.replace(/\{\{([^}]+)\}\}/g, (_, key) => {
    assert.ok(Object.hasOwn(map, key), `known interpolation ${key}`);
    return map[key];
  });
  assert.ok(!result.includes('{{'), 'no unrendered C# interpolation');
  return result;
}
const probes = {
  locate: literal('globalThis[ogmPreparedKey] = null;'),
  validateFile: literal("? 'file-input-target-checked'"),
  events: literal("code: 'file-input-selected'"),
  paste: literal("code: 'paste-event-dispatched'"),
  prepare: literal('previewDetected: ogmHasAttachment(scope)'),
  idle: literal("'composer-count-idle-probe'"),
  control: literal('const emptyDiagnostics'),
  invoke: literal('prepared.used = true'),
  observe: literal('submissionObserved:'),
  insert: literal("'native-value-setter-missing'"),
};
const wrapper = literal('{{ComposerDomHelpers}}');
let browser, socket, imagePath, frameId;
let sequence = 0;
let pageLoaded;
const requests = new Map();
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function cdp(method, params = {}) {
  const id = ++sequence;
  return await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => { requests.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 10000);
    requests.set(id, { resolve, reject, timeout });
    socket.send(JSON.stringify({ id, method, params }));
  });
}
async function evaluate(expression) {
  const result = await cdp('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  assert.equal(result.exceptionDetails, undefined, JSON.stringify(result.exceptionDetails));
  return result.result.value;
}
async function probe(name, overrides) {
  return await evaluate(render(wrapper, { expression: render(probes[name], overrides) }));
}
const composer = '<textarea id="prompt-textarea"></textarea>';
const send = '<button type="button" data-testid="send-button">Send</button>';
const input = '<input type="file" accept="image/*" hidden>';
function form(content = composer + input + send) { return `<form id="target">${content}</form>`; }
async function fixture(html = form()) {
  await cdp('Page.setDocumentContent', { frameId, html: '<!doctype html><html><body>' + html + '</body></html>' });
  await evaluate('globalThis.__openGameMatePreparedV1 = null; globalThis.clicks = 0; globalThis.changes = 0;');
  await evaluate(`document.querySelectorAll('[data-testid="send-button"]').forEach(b => b.onclick = () => globalThis.clicks++);`);
}
async function addPreview() {
  await evaluate(`(() => { const preview=document.createElement('div'); preview.dataset.testid='attachment'; preview.textContent='synthetic.png'; document.querySelector('#target').append(preview); })()`);
}
async function attachFile() {
  const { root: { nodeId } } = await cdp('DOM.getDocument');
  const { nodeId: inputNode } = await cdp('DOM.querySelector', { nodeId, selector: 'input[data-opengamemate-file="true"]' });
  assert.ok(inputNode);
  assert.equal((await probe('validateFile')).code, 'file-input-target-checked');
  await cdp('DOM.setFileInputFiles', { nodeId: inputNode, files: [imagePath] });
  return await probe('events');
}
async function prepareText() {
  const result = await probe('insert');
  assert.equal(result.textInserted, true);
}
before(async () => {
  const candidates = [process.env.OGM_TEST_BROWSER,
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Google/Chrome/Application/chrome.exe'].filter(Boolean);
  const executable = candidates.find(existsSync);
  assert.ok(executable, 'installed Edge or OGM_TEST_BROWSER is required');
  const directory = resolve(tmpdir(), `OpenGameMate-synthetic-dom-${process.pid}-${Date.now()}`);
  await mkdir(directory, { recursive: true });
  imagePath = join(directory, 'synthetic.png');
  await writeFile(imagePath, Buffer.from(imageBase64, 'base64'));
  browser = spawn(executable, ['--headless=new', '--disable-gpu', '--disable-background-networking',
    '--no-first-run', '--remote-debugging-port=0', '--user-data-dir=' + join(directory, 'profile'), 'about:blank'],
  { windowsHide: true, stdio: 'ignore' });
  let port;
  for (let attempt = 0; attempt < 100; attempt++) {
    try { port = (await readFile(join(directory, 'profile/DevToolsActivePort'), 'utf8')).split('\n')[0]; break; }
    catch { await delay(100); }
  }
  assert.ok(port, 'isolated browser started');
  const pages = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  socket = new WebSocket(pages.find(x => x.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  socket.onmessage = event => {
    const data = JSON.parse(event.data);
    if (data.id) {
      const pending = requests.get(data.id);
      if (!pending) return;
      clearTimeout(pending.timeout); requests.delete(data.id);
      if (data.error) pending.reject(new Error(JSON.stringify(data.error))); else pending.resolve(data.result);
    } else if (data.method === 'Page.loadEventFired') {
      pageLoaded?.();
    } else if (data.method === 'Fetch.requestPaused') {
      void cdp('Fetch.fulfillRequest', { requestId: data.params.requestId, responseCode: 200,
        responseHeaders: [{ name: 'Content-Type', value: 'text/html' }],
        body: Buffer.from('<!doctype html><html><body data-ogm-fixture="true">Synthetic adapter fixture</body></html>').toString('base64') });
    }
  };
  await cdp('Page.enable');
  await cdp('Fetch.enable', { patterns: [{ urlPattern: '*' }] });
  await cdp('Page.navigate', { url: 'https://chatgpt.com/opengamemate-synthetic-test' });
  let fixtureReady = false;
  for (let attempt = 0; attempt < 50; attempt++) {
    fixtureReady = await evaluate("location.hostname === 'chatgpt.com' && document.readyState === 'complete' && document.body?.hasAttribute('data-ogm-fixture')");
    if (fixtureReady) break;
    await delay(100);
  }
  assert.equal(fixtureReady, true, 'intercepted synthetic document is complete');
  frameId = (await cdp('Page.getFrameTree')).frameTree.frame.id;
}, { timeout: 25000 });
after(async () => {
  if (socket?.readyState === WebSocket.OPEN) {
    try { await cdp('Browser.close'); } catch { /* Browser closes before its response on some versions. */ }
    socket.close();
  }
  if (browser && browser.exitCode === null) browser.kill();
});

test('each production probe compiles in the shipped origin wrapper', async () => {
  for (const name of Object.keys(probes)) new Function(render(wrapper, { expression: render(probes[name]) }));
});
test('text preparation and idle/control/invoke share a single visible composer', async () => {
  await fixture(form('<textarea id="prompt-textarea" hidden></textarea>' + composer + send));
  assert.equal((await probe('idle')).composerCount, 1);
  await prepareText();
  assert.equal((await probe('control')).sendButtonCount, 1);
  assert.equal((await probe('invoke')).invoked, true);
});
test('known prompt wrapper resolves its unique visible contenteditable', async () => {
  await fixture(form('<div id="prompt-textarea"><div contenteditable="true" role="textbox"></div></div>' + send));
  await prepareText();
  assert.equal((await probe('idle')).composerCount, 1);
});
test('overlapping prompt and known Voice anchors deduplicate the same editor', async () => {
  await fixture(form('<div data-testid="voice-mode-composer"><div id="prompt-textarea" contenteditable="true" role="textbox"></div>' + send + '</div>'));
  assert.equal((await probe('idle')).composerCount, 1);
});
const unifiedEditor = '<div role="textbox" contenteditable="true"></div>';
const unified = content => `<form id="target" data-chatgpt-composer>${content}</form>`;
test('live unified form resolves its unique editor and semantic submit control', async () => {
  await fixture(unified(unifiedEditor + '<button type="submit">Send</button>'));
  await evaluate('document.querySelector("form").onsubmit=e=>e.preventDefault()');
  await prepareText();
  assert.equal((await probe('control')).sendButtonCount, 1);
  assert.equal((await probe('invoke')).invoked, true);
});
test('an empty contenteditable paragraph is empty, while a whitespace draft is preserved', async () => {
  await fixture(unified('<div role="textbox" contenteditable="true"><p><br></p></div>' + send));
  await prepareText();
  await fixture(unified('<div role="textbox" contenteditable="true"> </div>' + send));
  assert.equal((await probe('insert')).code, 'composer-not-empty');
});
test('unmarked form and unrelated textbox do not become composer candidates', async () => {
  await fixture(form(unifiedEditor + send));
  assert.equal((await probe('idle')).composerCount, 0);
  await fixture(unified(unifiedEditor + send) + '<form>' + unifiedEditor + '</form>');
  assert.equal((await probe('idle')).composerCount, 1);
});
test('multiple live-form editors or multiple marked forms fail closed', async () => {
  for (const html of [unified(unifiedEditor + unifiedEditor + send), unified(unifiedEditor + send) + unified(unifiedEditor + send)]) {
    await fixture(html);
    assert.equal((await probe('insert')).textInserted, false);
    assert.equal((await probe('idle')).composerCount, 2);
  }
});
test('inactive unified editors are filtered and foreign nested forms are rejected', async () => {
  for (const attribute of ['hidden', 'aria-readonly="true"', 'aria-disabled="true"', 'inert']) {
    await fixture(unified(`<section ${attribute}>${unifiedEditor}</section>` + unifiedEditor + send));
    assert.equal((await probe('idle')).composerCount, 1);
  }
  await fixture(unified(send));
  await evaluate(`(() => {const foreign=document.createElement('form');foreign.innerHTML=${JSON.stringify(unifiedEditor)};document.querySelector('form').append(foreign);})()`);
  assert.equal((await probe('idle')).composerCount, 0);
});
test('unified state without an explicit send control cannot start automatic preparation', async () => {
  await fixture(unified(unifiedEditor + '<button type="button">Activity</button>'));
  assert.equal((await probe('idle')).diagnostics.pageState, 'Unknown');
});
test('semantic submit controls owned by a foreign form are never counted or clicked', async () => {
  await fixture(unified(unifiedEditor + '<button type="submit" form="foreign">Wrong form</button>') + '<form id="foreign"></form>');
  await prepareText();
  assert.equal((await probe('control')).sendButtonCount, 0);
  assert.equal((await probe('invoke')).invoked, false);
  await fixture(unified(unifiedEditor + '<button type="submit">Send</button><button type="submit" form="foreign">Wrong form</button>') + '<form id="foreign"></form>');
  await evaluate('document.querySelector("#target").onsubmit=e=>e.preventDefault()');
  await prepareText();
  assert.equal((await probe('control')).sendButtonCount, 1);
  assert.equal((await probe('invoke')).invoked, true);
});
test('ambiguous native inputs stay untouched while the unique live editor accepts paste', async () => {
  await fixture(unified(unifiedEditor + input + input + input + '<input type="file" hidden>' + send));
  await evaluate(`document.querySelector('[role=textbox]').onpaste=e=>{const p=document.createElement('div');p.dataset.testid='attachment';p.textContent='synthetic.png';document.querySelector('form').append(p);e.preventDefault();}`);
  assert.equal((await probe('locate')).code, 'file-input-ambiguous');
  assert.equal(await evaluate('document.querySelectorAll("input[data-opengamemate-file]").length'), 0);
  await probe('paste');
  assert.equal((await probe('insert', {allowPreparedAttachmentJson:'true'})).textInserted, true);
  assert.equal((await probe('prepare')).previewDetected, true);
});
for (const attribute of ['hidden', 'disabled', 'readonly', 'aria-disabled="true"', 'aria-readonly="true"', 'style="display:none"', 'style="visibility:hidden"']) {
  test(`inactive composer excluded: ${attribute}`, async () => {
    await fixture(form(`<textarea id="prompt-textarea" ${attribute}></textarea>` + composer + send));
    assert.equal((await probe('idle')).composerCount, 1);
  });
}
test('hidden/inert/aria-disabled ancestor excludes its editor', async () => {
  for (const attribute of ['hidden', 'inert', 'aria-hidden="true"', 'aria-disabled="true"']) {
    await fixture(form(`<div ${attribute}>${composer}</div>` + composer + send));
    assert.equal((await probe('idle')).composerCount, 1);
  }
});
test('explicit disabled/readonly contenteditable anchors are excluded', async () => {
  for (const attribute of ['disabled', 'readonly']) {
    await fixture(form(`<div id="prompt-textarea" contenteditable="true" ${attribute}>Inactive</div>` + composer + send));
    assert.equal((await probe('idle')).composerCount, 1);
  }
});
test('two editable composers reject text, image, control and invocation without mutation', async () => {
  await fixture(form(composer + composer + input + send));
  for (const name of ['insert', 'locate', 'control', 'invoke']) assert.match((await probe(name)).code, /composer-count/);
  assert.equal(await evaluate('globalThis.clicks'), 0);
  assert.equal(await evaluate('document.querySelectorAll("input[data-opengamemate-file]").length'), 0);
});
test('a random textbox outside approved composer anchors is never selected', async () => {
  await fixture(form('<div contenteditable="true" role="textbox"></div>' + send));
  assert.equal((await probe('idle')).composerCount, 0);
  assert.equal((await probe('insert')).textInserted, false);
});
test('no supported composer scope fails closed without document-wide fallback', async () => {
  await fixture(composer + input + send);
  assert.equal((await probe('locate')).code, 'composer-scope-missing');
  assert.equal((await probe('insert')).textInserted, false);
});
test('explicit Voice wrapper supports text but makes automatic preparation unsafe', async () => {
  await fixture('<section id="target" data-testid="voice-mode-composer">' + composer + send + '</section>');
  const idle = await probe('idle');
  assert.equal(idle.diagnostics.pageState, 'VoiceComposer');
  await prepareText();
  assert.equal((await probe('invoke')).invoked, true);
});
test('inactive Voice launch button does not classify text composer as active Voice', async () => {
  await fixture(form(composer + send + '<button data-testid="voice-mode-button">Start Voice</button>'));
  assert.equal((await probe('idle')).diagnostics.pageState, 'Composer');
});
test('scoped file input wins only by form ownership; unrelated page inputs are untouched', async () => {
  await fixture(form() + form(input + input));
  assert.equal((await probe('locate')).code, 'ok');
  assert.equal(await evaluate('document.querySelector("input[data-opengamemate-file]").form.id'), 'target');
});
test('two eligible scoped inputs reject, including general versus camera inputs', async () => {
  await fixture(form(composer + input + '<input type="file" accept="image/*" capture="environment" hidden>' + send));
  assert.equal((await probe('locate')).code, 'file-input-ambiguous');
  assert.equal(await evaluate('document.querySelectorAll("input[data-opengamemate-file]").length'), 0);
});
test('file target is revalidated after interruption or a second input appears', async () => {
  await fixture(); await probe('locate');
  await evaluate('document.querySelector("#prompt-textarea").disabled=true');
  assert.equal((await probe('validateFile')).code, 'preparation-target-changed');
  await fixture(); await probe('locate');
  await evaluate('document.querySelector("#target").append(document.querySelector("input").cloneNode())');
  assert.equal((await probe('validateFile')).code, 'preparation-target-changed');
});
test('a changed target during attachment observation cannot become a new preparation', async () => {
  await fixture(); await probe('locate'); await addPreview();
  await evaluate('document.querySelector("textarea").replaceWith(document.querySelector("textarea").cloneNode())');
  assert.equal((await probe('prepare', { expectTextJson: 'false' })).code, 'preparation-target-changed');
});
test('disabled and wrong-media inputs are excluded; external form ownership is honored', async () => {
  await fixture(form(composer + '<input type="file" disabled accept="image/*">' +
    '<input type="file" accept="audio/*">' + '<input type="file" form="other" accept="image/*">' + send) +
    '<form id="other"></form><input type="file" form="target" accept="image/*" hidden>');
  assert.equal((await probe('locate')).code, 'ok');
  assert.equal(await evaluate('document.querySelector("input[data-opengamemate-file]").parentElement.tagName'), 'BODY');
});
test('no owned file input uses only the unique composer paste path', async () => {
  await fixture(form(composer + send) + input + input + input);
  await evaluate(`document.querySelector('#prompt-textarea').onpaste = e => {
    globalThis.pastedFiles=e.clipboardData.files.length;
    const p=document.createElement('div');p.dataset.testid='attachment';p.textContent='synthetic.png';
    document.querySelector('#target').append(p);e.preventDefault();};`);
  assert.equal((await probe('locate')).code, 'file-input-none');
  assert.equal((await probe('paste')).code, 'paste-event-dispatched');
  assert.equal(await evaluate('globalThis.pastedFiles'), 1);
  assert.equal((await probe('prepare', { expectTextJson: 'false' })).previewDetected, true);
});
test('CDP image selection plus text preparation observes the real preview and one upload event', async () => {
  await fixture();
  await evaluate(`document.querySelector('input').onchange = () => {
    globalThis.changes++;
    if(!document.querySelector('[data-testid="attachment"]')) {
      const p=document.createElement('div');p.dataset.testid='attachment';p.textContent='synthetic.png';
      document.querySelector('#target').append(p);}};`);
  assert.equal((await probe('locate')).code, 'ok');
  assert.equal((await attachFile()).fileSelected, true);
  assert.equal(await evaluate('globalThis.changes'), 1);
  assert.equal((await probe('insert', { allowPreparedAttachmentJson: 'true' })).textInserted, true);
  const result = await probe('prepare');
  assert.equal(result.previewDetected, true);
  assert.equal(result.textInserted, true);
});
test('file selection without an accepted preview does not prove attachment reception', async () => {
  await fixture(); await probe('locate'); await attachFile();
  assert.equal((await probe('prepare', { expectTextJson: 'false' })).previewDetected, false);
});
test('existing user text or unrelated preview is never replaced or sent', async () => {
  await fixture(form('<textarea id="prompt-textarea">Synthetic existing draft</textarea>' + input + send));
  assert.equal((await probe('insert')).code, 'composer-not-empty');
  assert.equal((await probe('locate')).code, 'composer-not-empty');
  await fixture(); await addPreview();
  assert.equal((await probe('insert')).code, 'composer-not-empty');
});
test('repeated preparation does not overwrite a prepared draft', async () => {
  await fixture(); await prepareText();
  assert.equal((await probe('insert')).code, 'composer-not-empty');
});
test('duplicate invocation is rejected even when the first click left the draft intact', async () => {
  await fixture(); await prepareText();
  assert.equal((await probe('invoke')).invoked, true);
  assert.equal((await probe('invoke')).invoked, false);
  assert.equal(await evaluate('globalThis.clicks'), 1);
});
test('changed composer, text, scope or Voice state invalidates a prepared send', async () => {
  for (const mutation of [
    `document.querySelector('#prompt-textarea').replaceWith(document.querySelector('#prompt-textarea').cloneNode(true))`,
    `document.querySelector('#prompt-textarea').value='Changed synthetic text'`,
    `const f=document.createElement('form');document.body.append(f);f.append(document.querySelector('#prompt-textarea'),document.querySelector('button'))`,
    `const b=document.createElement('button');b.dataset.testid='end-voice-mode-button';b.textContent='End Voice';document.body.append(b)`]) {
    await fixture(); await prepareText(); await evaluate(mutation);
    assert.equal((await probe('invoke')).invoked, false);
    assert.equal(await evaluate('globalThis.clicks'), 0);
  }
});
test('replacing a prepared attachment cannot send a different attachment with the same text', async () => {
  await fixture(); await probe('locate'); await addPreview();
  await probe('insert', { allowPreparedAttachmentJson: 'true' }); await probe('prepare');
  await evaluate('document.querySelector("[data-testid=attachment]").replaceWith(document.querySelector("[data-testid=attachment]").cloneNode(true))');
  assert.equal((await probe('invoke')).invoked, false);
  assert.equal(await evaluate('globalThis.clicks'), 0);
});
test('disabled, hidden or ambiguous send controls and active stop controls never click', async () => {
  for (const buttons of [send + send, '<button disabled data-testid="send-button">Send</button>',
    '<button hidden data-testid="send-button">Send</button>', send + '<button data-testid="stop-button">Stop</button>']) {
    await fixture(form(composer + buttons)); await prepareText();
    assert.equal((await probe('invoke')).invoked, false);
    assert.equal(await evaluate('globalThis.clicks'), 0);
  }
});
test('hidden stale send and stop controls are filtered consistently before invocation', async () => {
  await fixture(form(composer + send + '<button hidden data-testid="send-button">Hidden</button>' +
    '<button hidden data-testid="stop-button">Hidden</button>'));
  await prepareText();
  assert.equal((await probe('idle')).sendButtonCount, 1);
  assert.equal((await probe('control')).sendButtonCount, 1);
  assert.equal((await probe('invoke')).invoked, true);
});
test('lost marker alone and text-only attachmentCleared never prove sending', async () => {
  await fixture(); await prepareText(); await probe('invoke');
  await evaluate('document.querySelectorAll("input[data-opengamemate-file]").forEach(x=>x.removeAttribute("data-opengamemate-file"))');
  const result = await probe('observe');
  assert.equal(result.submissionObserved, false);
  assert.equal(result.attachmentCleared, false);
});
test('successful text submission requires the same composer to clear', async () => {
  await fixture(); await prepareText();
  await evaluate(`document.querySelector('button').onclick=()=>{globalThis.clicks++;document.querySelector('textarea').value='';}`);
  await probe('invoke');
  assert.equal((await probe('observe')).submissionObserved, true);
});
test('combined prepared image and text require both to clear', async () => {
  await fixture(); await probe('locate'); await addPreview();
  await probe('insert', { allowPreparedAttachmentJson: 'true' });
  await probe('prepare'); await probe('invoke');
  await evaluate('document.querySelector("textarea").value=""');
  assert.equal((await probe('observe')).submissionObserved, false);
  await evaluate('document.querySelector("[data-testid=attachment]").remove()');
  assert.equal((await probe('observe')).submissionObserved, true);
});
test('image-only submission observes preview clearance without using the file marker', async () => {
  await fixture(); await probe('locate'); await addPreview();
  await probe('prepare', { expectTextJson: 'false' }); await probe('invoke');
  await evaluate('document.querySelector("input").remove()');
  assert.equal((await probe('observe')).submissionObserved, false);
  await evaluate('document.querySelector("[data-testid=attachment]").remove()');
  assert.equal((await probe('observe')).submissionObserved, true);
});
test('post-click composer removal or replacement is an unconfirmed outcome', async () => {
  await fixture(); await prepareText(); await probe('invoke');
  await evaluate('document.querySelector("textarea").remove()');
  assert.equal((await probe('observe')).submissionObserved, false);
});
test('quota and platform errors remain observable without reading their text', async () => {
  await fixture(form() + '<div data-testid="quota-error">Synthetic quota</div><div data-testid="composer-error">Synthetic error</div>');
  const result = await probe('observe');
  assert.equal(result.quotaDetected, true); assert.equal(result.platformErrorDetected, true);
});
test('all production probes reject an unrelated origin before reading or mutating controls', async () => {
  await cdp('Page.navigate', { url: 'https://example.invalid/opengamemate-synthetic-test' });
  for (let attempt=0; attempt<50; attempt++) { if(await evaluate("location.hostname === 'example.invalid'")) break; await delay(50); }
  frameId = (await cdp('Page.getFrameTree')).frameTree.frame.id;
  await fixture();
  for (const name of Object.keys(probes)) assert.equal((await probe(name)).code, 'unexpected-origin');
  assert.equal(await evaluate('globalThis.clicks'), 0);
});
