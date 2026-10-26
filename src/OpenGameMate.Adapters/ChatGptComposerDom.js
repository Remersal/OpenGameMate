// Runs inside each bounded adapter probe. Never inspect messages, history or page HTML.
const ogmVisible = element => {
  if (!element?.isConnected || !element.getClientRects().length) return false;
  for (let node = element; node; node = node.parentElement) {
    if (node.hidden || node.inert || node.getAttribute('aria-hidden') === 'true') return false;
    const style = getComputedStyle(node);
    if (style.display === 'none' || style.visibility === 'hidden' || style.visibility === 'collapse') return false;
  }
  return true;
};
const ogmEditable = element => ogmVisible(element) &&
  !element.matches(':disabled') && !element.hasAttribute('disabled') &&
  !element.readOnly && !element.hasAttribute('readonly') &&
  !element.closest('[aria-disabled="true"],[aria-readonly="true"],[inert]') &&
  ((element instanceof HTMLTextAreaElement) ||
    (element instanceof HTMLInputElement && ['text', 'search'].includes(element.type)) ||
    element.isContentEditable);
const ogmComposers = selector => {
  const candidates = new Set();
  for (const root of document.querySelectorAll(selector)) {
    const markedForm = root.closest('form[data-chatgpt-composer]');
    if (markedForm && root.closest('form') !== markedForm) continue;
    if (ogmEditable(root)) candidates.add(root);
    else for (const element of root.querySelectorAll(
      'textarea,[contenteditable="true"],[contenteditable="plaintext-only"],[role="textbox"]')) {
      if (ogmEditable(element)) candidates.add(element);
    }
  }
  return [...candidates];
};
const ogmScope = composer => composer?.closest('form') ??
  composer?.closest('[data-testid="voice-mode-composer"]') ?? null;
const ogmOwnedButton = (button, scope) => ogmVisible(button) && scope.contains(button) &&
  (!button.form || button.form === scope);
const ogmFileInputs = (selector, scope) => [...document.querySelectorAll(selector)]
  .filter(element => element instanceof HTMLInputElement && element.type === 'file' &&
    !element.matches(':disabled') && element.getAttribute('aria-disabled') !== 'true' &&
    !element.closest('[inert],[aria-disabled="true"]') &&
    (!element.accept || /image|png|jpe?g|gif/i.test(element.accept)) &&
    (element.form ? element.form === scope : scope.contains(element)));
const ogmText = composer => composer instanceof HTMLTextAreaElement || composer instanceof HTMLInputElement
  ? composer.value : composer.textContent === '' ? '' : composer.innerText || composer.textContent;
const ogmAttachments = scope => scope ? [...new Set(ogmAttachmentSelectors.flatMap(
  selector => [...scope.querySelectorAll(selector)].filter(ogmVisible)))] : [];
const ogmHasAttachment = scope => ogmAttachments(scope).length > 0;
const ogmSameAttachments = state => {
  const current = ogmAttachments(state.scope);
  return current.length === state.attachments.length && state.attachments.every(node => current.includes(node));
};
const ogmVoice = () => [...document.querySelectorAll(
  'button[data-testid="end-voice-mode-button"],[data-testid="voice-mode-composer"]')].some(ogmVisible);
const ogmPageState = scope => ogmVoice()
  ? (ogmHasAttachment(scope) ? 'VoiceComposerWithAttachment' : 'VoiceComposer')
  : (ogmHasAttachment(scope) ? 'ComposerWithAttachment' : 'Composer');
const ogmPreparedKey = '__openGameMatePreparedV1';
const ogmSamePreparationTarget = composer => {
  const state = globalThis[ogmPreparedKey];
  return Boolean(state && !state.used && state.composer === composer &&
    state.scope === ogmScope(composer) && state.voice === ogmVoice());
};
const ogmRemember = composer => {
  globalThis[ogmPreparedKey] = { composer, scope: ogmScope(composer), text: ogmText(composer),
    hasAttachment: ogmHasAttachment(ogmScope(composer)), attachments: ogmAttachments(ogmScope(composer)),
    voice: ogmVoice(), used: false };
};
const ogmPrepared = composer => {
  const state = globalThis[ogmPreparedKey];
  return state && !state.used && state.composer === composer && state.scope === ogmScope(composer) &&
    state.text === ogmText(composer) && ogmSameAttachments(state) &&
    state.voice === ogmVoice() ? state : null;
};
const ogmCanInsertText = (composer, allowPreparedAttachment) => {
  if (ogmText(composer)) return false;
  if (allowPreparedAttachment && !ogmSamePreparationTarget(composer)) return false;
  if (!ogmHasAttachment(ogmScope(composer))) return true;
  const state = globalThis[ogmPreparedKey];
  return allowPreparedAttachment && Boolean(state && !state.used && state.composer === composer &&
    state.scope === ogmScope(composer) && state.voice === ogmVoice() && !state.text &&
    (!state.hasAttachment || ogmSameAttachments(state)));
};
