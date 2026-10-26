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
const ogmCanonicalText = value => value.replace(/\r\n?/g, '\n');
const ogmContext = () => location.pathname + location.search;
const ogmAttachments = scope => {
  if (!scope) return [];
  const nodes = new Set(ogmAttachmentSelectors.flatMap(
    selector => [...scope.querySelectorAll(selector)].filter(ogmVisible)));
  for (const marker of [...nodes]) {
    for (const image of marker.querySelectorAll('img')) {
      if (ogmVisible(image) && !image.closest('button[data-testid="remove-file-button"]') &&
        (!scope.matches('form') || image.closest('form') === scope)) nodes.add(image);
    }
  }
  // In the observed marked composer, non-button images are pending attachments.
  // Count server-backed images too, so a blob URL change cannot look like clearance.
  if (scope.matches('form[data-chatgpt-composer]')) {
    for (const image of scope.querySelectorAll('img')) {
      if (ogmVisible(image) && !image.closest('button') && image.closest('form') === scope) nodes.add(image);
    }
  }
  const images = [...nodes].filter(node => node instanceof HTMLImageElement);
  const wrappers = [...nodes].filter(node => !(node instanceof HTMLImageElement));
  const removable = node => node.matches('button[data-testid="remove-file-button"]');
  const imageForWrapper = wrapper => {
    const leaves = images.filter(image => wrapper.contains(image));
    if (leaves.length !== 1) return null;
    const related = wrappers.filter(node => node !== wrapper && wrapper.contains(node));
    if (related.some(node => !node.contains(leaves[0]) && !removable(node))) return null;
    if (related.filter(removable).length > 1) return null;
    return leaves[0];
  };
  return [...images, ...wrappers.filter(node => !imageForWrapper(node) &&
    !(removable(node) && wrappers.some(wrapper => wrapper !== node &&
      wrapper.contains(node) && imageForWrapper(wrapper))))];
};
const ogmHasAttachment = scope => ogmAttachments(scope).length > 0;
const ogmAttachmentIdentity = node => ({ node, image: node instanceof HTMLImageElement,
  source: node instanceof HTMLImageElement ? node.src : null,
  sourceSet: node instanceof HTMLImageElement ? node.srcset : null,
  selectedSource: node instanceof HTMLImageElement ? node.currentSrc : null });
const ogmAttachmentFailure = state => {
  const current = ogmAttachments(state.scope);
  if (current.length !== state.attachmentIdentities.length) return 'prepared-attachment-count-changed';
  const remaining = [...current];
  for (const expected of state.attachmentIdentities) {
    let index = remaining.indexOf(expected.node);
    if (index >= 0) {
      if (expected.image && (remaining[index].src !== expected.source || remaining[index].srcset !== expected.sourceSet ||
        remaining[index].currentSrc !== expected.selectedSource)) {
        return 'prepared-attachment-source-changed';
      }
    } else {
      // A detached image may be redrawn only from the very same immutable asset.
      // A filename, image count, mutable HTTP URL or marker alone is not identity.
      if (!expected.image || expected.node.isConnected || expected.sourceSet ||
        !(expected.source.startsWith('blob:') || expected.source.startsWith('data:image/'))) {
        return 'prepared-attachment-node-changed';
      }
      index = remaining.findIndex(node => node instanceof HTMLImageElement &&
        node.src === expected.source && node.srcset === expected.sourceSet && node.currentSrc === expected.selectedSource);
      if (index < 0) return 'prepared-attachment-source-changed';
    }
    remaining.splice(index, 1);
  }
  return 'ok';
};
const ogmSameAttachments = state => ogmAttachmentFailure(state) === 'ok';
const ogmVoice = () => [...document.querySelectorAll(
  'button[data-testid="end-voice-mode-button"],[data-testid="voice-mode-composer"]')].some(ogmVisible) ||
  [...document.querySelectorAll('form[data-chatgpt-composer] button[aria-label="关闭麦克风"],' +
    'form[data-chatgpt-composer] button[aria-label="Turn off microphone"]')].some(button => {
      const form = button.closest('form');
      return form?.hasAttribute('data-chatgpt-composer') && ogmOwnedButton(button, form);
    });
const ogmPageState = scope => ogmVoice()
  ? (ogmHasAttachment(scope) ? 'VoiceComposerWithAttachment' : 'VoiceComposer')
  : (ogmHasAttachment(scope) ? 'ComposerWithAttachment' : 'Composer');
const ogmPreparedKey = '__openGameMatePreparedV1';
const ogmCanRebindComposer = (state, composer) => !state.composer.isConnected &&
  state.scope === ogmScope(composer) && state.scope?.isConnected &&
  state.scope.matches('form[data-chatgpt-composer]') && ogmEditable(composer) &&
  composer.matches('[role="textbox"][contenteditable="true"]');
const ogmSamePreparationTarget = composer => {
  const state = globalThis[ogmPreparedKey];
  return Boolean(state && !state.used && state.context === ogmContext() &&
    state.scope === ogmScope(composer) && state.voice === ogmVoice() &&
    state.text === ogmCanonicalText(ogmText(composer)) &&
    (state.composer === composer || (ogmCanRebindComposer(state, composer) &&
      (state.upload?.phase === 'uploading' || ogmSameAttachments(state)))));
};
const ogmRemember = (composer, preserveUpload = false) => {
  const previous = globalThis[ogmPreparedKey];
  globalThis[ogmPreparedKey] = { composer, scope: ogmScope(composer), text: ogmCanonicalText(ogmText(composer)),
    hasAttachment: ogmHasAttachment(ogmScope(composer)), attachments: ogmAttachments(ogmScope(composer)),
    attachmentIdentities: ogmAttachments(ogmScope(composer)).map(ogmAttachmentIdentity),
    context: ogmContext(), voice: ogmVoice(), used: false,
    upload: preserveUpload && previous?.scope === ogmScope(composer) ? previous.upload : null,
    fileInput: preserveUpload && previous?.scope === ogmScope(composer) ? previous.fileInput : null };
};
const ogmPreparedFailure = composer => {
  const state = globalThis[ogmPreparedKey];
  if (!state) return 'prepared-state-missing';
  if (state.used) return 'prepared-state-consumed';
  if (state.context !== ogmContext()) return 'prepared-context-changed';
  if (state.scope !== ogmScope(composer)) return 'prepared-scope-changed';
  if (state.composer !== composer && !ogmCanRebindComposer(state, composer)) return 'prepared-composer-changed';
  if (state.text !== ogmCanonicalText(ogmText(composer))) return 'prepared-text-changed';
  if (state.voice !== ogmVoice()) return 'prepared-voice-changed';
  if (state.upload && state.upload.phase !== 'ready') return 'prepared-attachment-upload-incomplete';
  return ogmAttachmentFailure(state);
};
const ogmPrepared = composer => {
  if (ogmPreparedFailure(composer) !== 'ok') return null;
  const state = globalThis[ogmPreparedKey];
  state.composer = composer;
  state.attachments = ogmAttachments(state.scope);
  state.attachmentIdentities = state.attachments.map(ogmAttachmentIdentity);
  return state;
};
const ogmCanInsertText = (composer, allowPreparedAttachment) => {
  if (ogmText(composer)) return false;
  if (allowPreparedAttachment && !ogmSamePreparationTarget(composer)) return false;
  const state = globalThis[ogmPreparedKey];
  if (state?.upload?.phase === 'uploading') return allowPreparedAttachment && !state.text;
  if (allowPreparedAttachment && state?.hasAttachment && !ogmSameAttachments(state)) return false;
  if (!ogmHasAttachment(ogmScope(composer))) return true;
  return allowPreparedAttachment && Boolean(state && !state.used && state.composer === composer &&
    state.scope === ogmScope(composer) && state.voice === ogmVoice() && !state.text &&
    (!state.hasAttachment || ogmSameAttachments(state)));
};

// Upload preparation is a different phase from a frozen submission. Only local
// preview bytes can be bound to the File supplied for this attempt. Never fetch a
// server preview, infer identity from a filename, or treat send readiness as upload
// completion. A scoped semantic busy -> idle transition is required as well.
const ogmStopUpload = phase => {
  const upload = globalThis[ogmPreparedKey]?.upload;
  if (!upload) return;
  upload.observer?.disconnect();
  upload.unbind?.();
  upload.file = null;
  upload.proofs.clear();
  upload.phase = phase;
};
const ogmBeginUpload = (composer, digest, size) => {
  const state = globalThis[ogmPreparedKey];
  const upload = { phase: 'uploading', digest, size, file: null, fileProof: null,
    proofs: new Map(), sawBusy: false, seenImage: false, completed: null, failure: null };
  state.upload = upload;
  const fail = code => { upload.failure ??= code; };
  const hash = async blob => {
    if (blob.size !== size || blob.size > 20 * 1024 * 1024) return false;
    const bytes = await blob.arrayBuffer();
    const actual = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
    return [...actual].map(value => value.toString(16).padStart(2, '0')).join('') === digest;
  };
  const sameSource = (left, right) => left.node === right.node && left.source === right.source &&
    left.sourceSet === right.sourceSet;
  upload.sample = () => {
    if (upload.phase !== 'uploading' || upload.failure) return;
    const current = globalThis[ogmPreparedKey];
    if (!current || current.scope !== state.scope || current.context !== ogmContext() ||
      current.voice !== ogmVoice() || (current.composer.isConnected &&
        current.text !== ogmCanonicalText(ogmText(current.composer)))) {
      fail('preparation-target-changed'); return;
    }
    const attachments = ogmAttachments(state.scope);
    if (attachments.length > 1 || (upload.seenImage && attachments.length !== 1)) {
      fail('attachment-count-changed-during-upload'); return;
    }
    const image = attachments[0];
    if (!(image instanceof HTMLImageElement)) return;
    upload.seenImage = true;
    const identity = ogmAttachmentIdentity(image);
    if (upload.completed && !sameSource(upload.completed, identity)) {
      fail('attachment-source-changed-after-completion'); return;
    }
    // Only attachment wrappers containing this single image own this signal.
    const wrappers = new Set(ogmAttachmentSelectors.flatMap(selector =>
      [...state.scope.querySelectorAll(selector)].filter(node => !(node instanceof HTMLImageElement) &&
        !node.matches('button') && node.contains(image) && ogmVisible(node))));
    const busy = [...wrappers].some(node => node.getAttribute('aria-busy') === 'true' ||
      [...node.querySelectorAll('[aria-busy="true"],[role="progressbar"]')].some(ogmVisible));
    if (upload.previous && !sameSource(upload.previous, identity) && !busy && !upload.sawBusy) {
      fail('attachment-source-changed-without-upload'); return;
    }
    upload.previous = identity;
    if (busy) {
      if (upload.completed) { fail('attachment-upload-restarted'); return; }
      upload.sawBusy = true;
    } else if (upload.sawBusy && !upload.completed) upload.completed = identity;
    if (identity.sourceSet) {
      fail('attachment-source-set-unverifiable'); return;
    }
    const local = identity.source.startsWith('data:image/png;base64,') ||
      identity.source.startsWith('blob:' + location.origin + '/');
    if (!local) { fail('attachment-content-unverifiable'); return; }
    if (!upload.file || upload.proofs.has(identity.source)) return;
    if (upload.proofs.size >= 8) { fail('attachment-source-change-limit'); return; }
    upload.fileProof ??= hash(upload.file);
    const proof = { valid: false };
    // This fetch is restricted above to same-origin blob URLs or inline PNG data.
    // Credentials, remote URLs, pixels and hashes never leave this page probe.
    proof.pending = Promise.race([
      (async () => {
        const response = await fetch(identity.source, { credentials: 'omit', redirect: 'error' });
        proof.valid = await upload.fileProof && await hash(await response.blob());
        if (!proof.valid) fail('attachment-content-changed');
      })(),
      new Promise((_, reject) => setTimeout(() => reject(new Error('local-preview-timeout')), 2000))
    ]).catch(() => fail('attachment-content-unverifiable'));
    upload.proofs.set(identity.source, proof);
  };
  upload.observer = new MutationObserver(() => upload.sample());
  upload.observer.observe(state.scope, { subtree: true, childList: true, attributes: true,
    attributeFilter: ['src', 'srcset', 'aria-busy', 'role', 'hidden', 'style', 'class'] });
  upload.bind = file => {
    if (upload.file && file !== upload.file) fail('attachment-file-changed-during-upload');
    if (!upload.file && file instanceof File) upload.file = file;
    upload.sample();
  };
};
const ogmPollUpload = async composer => {
  const state = globalThis[ogmPreparedKey];
  if (!ogmSamePreparationTarget(composer)) return { code: 'preparation-target-changed' };
  const upload = state.upload;
  if (!upload || upload.phase !== 'uploading') return { code: 'attachment-upload-state-invalid' };
  upload.sample();
  await Promise.all([...upload.proofs.values()].map(proof => proof.pending));
  // File hashing yields: recheck the target, lifecycle and final identity afterward.
  if (!ogmSamePreparationTarget(composer)) return { code: 'preparation-target-changed' };
  upload.sample();
  if (upload.failure) return { code: upload.failure };
  const attachments = ogmAttachments(state.scope);
  const image = attachments[0];
  const proof = image instanceof HTMLImageElement ? upload.proofs.get(image.src) : null;
  if (upload.completed && proof?.valid && [...upload.proofs.values()].every(item => item.valid) &&
    image.complete && image.naturalWidth > 0 && image.currentSrc === image.src) {
    ogmStopUpload('ready');
    ogmRemember(composer, true);
    return { code: 'ok' };
  }
  return { code: 'attachment-upload-pending' };
};
