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

// Read-only, finite-category diagnostics. No URL, file digest or page text is
// returned. Keep this separate from identity decisions and lifecycle sampling.
const ogmSourceKind = value => {
  if (value === null || value === undefined) return 'Unknown';
  if (!value.trim()) return 'Empty';
  const kind = source => /^blob:/i.test(source) ? 'Blob' : /^data:/i.test(source) ? 'Data' :
    /^https?:/i.test(source) ? 'Http' : 'Other';
  const kinds = new Set((value.match(/(?:^|\s)(?:blob:|data:|https?:)/gi) ?? []).map(token => kind(token.trim())));
  return kinds.size > 1 ? 'Mixed' : kind(value.trim());
};
const ogmAttachmentDiagnostics = (scope, preparing = false) => {
  const state = globalThis[ogmPreparedKey];
  const upload = state?.upload;
  const current = ogmAttachments(scope);
  const expected = preparing ? upload?.diagnosticInitial : state?.attachmentIdentities?.[0];
  const image = current.find(node => node === expected?.node) ?? (current.length === 1 ? current[0] : null);
  const actual = image instanceof HTMLImageElement ? ogmAttachmentIdentity(image) : null;
  const wrappers = scope ? [...new Set(ogmAttachmentSelectors.flatMap(selector =>
    [...scope.querySelectorAll(selector)].filter(node => !(node instanceof HTMLImageElement) &&
      !node.matches('button') && ogmVisible(node) && (actual ? node.contains(image) : current.includes(node)))))] : [];
  const ariaNodes = [...new Set(wrappers.flatMap(node => [node,
    ...[...node.querySelectorAll('[aria-busy]')].filter(ogmVisible)]))];
  const ariaValues = ariaNodes.map(node => node.getAttribute('aria-busy')).filter(value => value === 'true' || value === 'false');
  return {
    beforeCount: Math.min(99, preparing ? (expected ? 1 : 0) : state?.attachmentIdentities?.length ?? 0),
    afterCount: Math.min(99, current.length),
    beforeSrc: ogmSourceKind(expected?.source), afterSrc: ogmSourceKind(actual?.source),
    beforeSrcSet: ogmSourceKind(expected?.sourceSet), afterSrcSet: ogmSourceKind(actual?.sourceSet),
    beforeCurrentSrc: ogmSourceKind(expected?.selectedSource), afterCurrentSrc: ogmSourceKind(actual?.selectedSource),
    srcChanged: Boolean(expected && actual && expected.source !== actual.source),
    srcSetChanged: Boolean(expected && actual && expected.sourceSet !== actual.sourceSet),
    currentSrcChanged: Boolean(expected && actual && expected.selectedSource !== actual.selectedSource),
    ariaBusy: ariaValues.length ? ariaValues.includes('true') : null,
    progressBarVisible: wrappers.some(node => [...node.querySelectorAll('[role="progressbar"]')].some(ogmVisible)),
    busyObserved: Boolean(upload?.sawBusy), completionObserved: Boolean(upload?.completed),
    uploadPhase: ({ uploading: 'Uploading', ready: 'Ready', failed: 'Failed', abandoned: 'Abandoned' })[upload?.phase] ?? 'Unknown'
  };
};

// Upload preparation is a different phase from a frozen submission. Only local
// preview bytes can be bound to the File supplied for this attempt. Never fetch a
// server preview, infer identity from a filename, or treat send readiness as upload
// completion. Busy is an optional signal; local content proof remains mandatory.
const ogmLocalPngSourceFailure = source => {
  if (source.startsWith('blob:' + location.origin + '/')) return null;
  const prefix = 'data:image/png;base64,';
  if (!source.startsWith(prefix)) {
    if (/^data:image\/(?:jpeg|jpg)[;,]/i.test(source)) return 'attachment-data-mime-jpeg';
    if (/^data:image\/webp[;,]/i.test(source)) return 'attachment-data-mime-webp';
    if (/^data:image\/png[;,]/i.test(source)) return 'attachment-data-png-header';
    return source.startsWith('data:') ? 'attachment-data-mime-unsupported' : 'attachment-content-unverifiable';
  }
  if (source.length > prefix.length + 4 * Math.ceil(20 * 1024 * 1024 / 3)) return 'attachment-data-size-limit';
  const data = source.slice(prefix.length);
  return data.length > 0 && data.length % 4 === 0 && /^[A-Za-z0-9+/]*={0,2}$/.test(data)
    ? null : 'attachment-data-base64-invalid';
};
const ogmIdentityMime = source => /^data:image\/png[;,]/i.test(source) ? 'Png' :
  /^data:image\/(jpeg|jpg)[;,]/i.test(source) ? 'Jpeg' : /^data:image\/webp[;,]/i.test(source) ? 'Webp' :
  source.startsWith('data:') ? 'Other' : 'Unknown';
const ogmIdentityDiagnostics = (upload, scope, code, proofRan = true) => {
  if (!upload) return null;
  const attachments = ogmAttachments(scope);
  const image = attachments.length === 1 && attachments[0] instanceof HTMLImageElement ? attachments[0] : null;
  const source = image?.src ?? '';
  const trace = proofRan || code === 'ok' ? upload.lastProof : null;
  const reason = code.replace(/^prepared-/, '');
  const reject = reason.includes('data-mime-') ? 'Mime' : reason.includes('data-png-header') ? 'Header' :
    reason.includes('data-base64') ? 'Base64' : reason.includes('data-size') ? 'Size' : image?.srcset ? 'SrcSet' :
    reason.includes('source-change-limit') ? 'SourceLimit' : reason === 'attachment-content-unverifiable' ? 'UntrustedSource' : 'None';
  const failure = ({ 'attachment-local-fetch-failed': 'Fetch', 'attachment-local-response-failed': 'Response',
    'attachment-data-decode-failed': 'DataDecode', 'attachment-original-file-read-failed': 'OriginalFile',
    'attachment-local-bytes-failed': 'Bytes', 'attachment-local-digest-failed': 'Digest',
    'attachment-local-proof-timeout': 'Timeout', 'attachment-content-changed': 'ByteMismatch',
    'attachment-image-not-ready': 'Decode', 'attachment-source-change-limit': 'SourceLimit' })[reason] ??
    (reason === 'attachment-local-read-failed' ? 'Other' : 'None');
  const operation = trace?.failureOperation ?? trace?.operation;
  const failureStage = ({ Fetch: 'Fetch', Response: 'Response', DataDecode: 'DataDecode', OriginalFile: 'OriginalFile',
    Bytes: 'Bytes', Digest: 'Digest', Decode: 'Decode' })[failure];
  const stage = reject !== 'None' ? 'SourceValidation' : failureStage ??
    ({ source: 'SourceValidation', 'data-decode': 'DataDecode', fetch: 'Fetch', 'response-blob': 'Response',
      'original-file': 'OriginalFile', 'array-buffer': 'Bytes', digest: 'Digest', complete: 'Complete' })[operation] ?? 'NotStarted';
  const result = reject !== 'None' ? 'Rejected' : failure === 'Timeout' ? 'TimedOut' : failure === 'ByteMismatch' ? 'Mismatched' :
    failure !== 'None' ? 'Error' : trace?.valid ? 'Matched' : 'NotChecked';
  const elapsed = reject !== 'None' || failure === 'Decode' || !proofRan ? null : trace?.elapsed;
  return { mime: reject !== 'None' ? ogmIdentityMime(source) : trace?.mime ?? ogmIdentityMime(source),
    stage, result, sourceRejectReason: reject, proofFailure: failure,
    exceptionKind: failure === 'Timeout' ? 'Timeout' : failure === 'Decode' ? 'None' : trace?.exceptionKind ?? 'None',
    duration: elapsed === null || elapsed === undefined ? 'None' : elapsed < 100 ? 'Under100Ms' :
      elapsed < 500 ? 'Under500Ms' : elapsed < 2000 ? 'Under2Seconds' : 'AtLeast2Seconds',
    sourceCount: Math.min(99, Math.max(0, upload.sourceChecks)),
    dimensionsEqual: image && upload.width ? image.naturalWidth === upload.width && image.naturalHeight === upload.height : null,
    originalFileVerified: Boolean(upload.originalFileVerified) };
};
const ogmAttachmentBusy = (scope, image) => {
  const wrappers = new Set(ogmAttachmentSelectors.flatMap(selector =>
    [...scope.querySelectorAll(selector)].filter(node => !(node instanceof HTMLImageElement) &&
      !node.matches('button') && node.contains(image) && ogmVisible(node))));
  return [...wrappers].some(node => node.getAttribute('aria-busy') === 'true' ||
    [...node.querySelectorAll('[aria-busy="true"],[role="progressbar"]')].some(ogmVisible));
};
const ogmDecodedImageMatches = (image, upload) => image instanceof HTMLImageElement &&
  image.complete && image.naturalWidth === upload.width && image.naturalHeight === upload.height &&
  image.naturalWidth > 0 && image.naturalWidth <= 1920 && image.naturalHeight > 0 && image.naturalHeight <= 1080 &&
  !image.srcset && image.currentSrc === image.src;
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
    proofs: new Map(), sourceChecks: 0, sawBusy: false, seenImage: false, completed: null, failure: null };
  state.upload = upload;
  const fail = code => { upload.failure ??= code; };
  const hash = async (blob, stage = () => {}) => {
    if (blob.size !== size || blob.size > 20 * 1024 * 1024) return false;
    stage('array-buffer');
    const bytes = await blob.arrayBuffer();
    const view = new DataView(bytes);
    if (bytes.byteLength < 33 || view.getUint32(0) !== 0x89504e47 || view.getUint32(4) !== 0x0d0a1a0a ||
      view.getUint32(8) !== 13 || view.getUint32(12) !== 0x49484452) return false;
    const width = view.getUint32(16), height = view.getUint32(20);
    if (!width || width > 1920 || !height || height > 1080) return false;
    stage('digest');
    const actual = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
    const matches = [...actual].map(value => value.toString(16).padStart(2, '0')).join('') === digest;
    if (matches) { upload.width = width; upload.height = height; }
    return matches;
  };
  upload.prove = source => {
    const proof = { valid: false, operation: 'source', exceptionKind: 'None', mime: ogmIdentityMime(source) };
    upload.lastProof = proof;
    const started = performance.now();
    let operation = 'source';
    const sourceFailure = ogmLocalPngSourceFailure(source);
    if (sourceFailure) { fail(sourceFailure); return proof; }
    if (upload.sourceChecks++ >= 8) { fail('attachment-source-change-limit'); return proof; }
    // A local URL is only a locator: verify its bytes against the original File.
    // Never fetch HTTP previews or return bytes, source values or digests.
    proof.pending = Promise.race([
      (async () => {
        let blob;
        if (source.startsWith('data:')) {
          // The bytes are already in this owned preview. Decode locally: fetch(data:)
          // adds an unnecessary connect-src dependency even when img-src permits it.
          operation = 'data-decode';
          const binary = atob(source.slice('data:image/png;base64,'.length));
          const bytes = new Uint8Array(binary.length);
          for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
          blob = new Blob([bytes], { type: 'image/png' });
        } else {
          operation = 'fetch';
          const response = await fetch(source, { credentials: 'omit', redirect: 'error' });
          operation = 'response-blob';
          blob = await response.blob();
        }
        operation = 'original-file';
        proof.valid = await upload.fileProof && await hash(blob, stage => { operation = stage; });
        if (!proof.valid) fail('attachment-content-changed');
      })(),
      new Promise((_, reject) => setTimeout(() => reject(Object.assign(new Error(), { name: 'OgmLocalProofTimeout' })), 2000))
    ]).then(() => { proof.operation = 'complete'; }).catch(error => {
      proof.failureOperation = operation;
      const timeout = error?.name === 'OgmLocalProofTimeout';
      proof.exceptionKind = timeout ? 'Timeout' :
        (['TypeError', 'SecurityError', 'OperationError', 'EncodingError', 'AbortError', 'DataError', 'NotReadableError'].includes(error?.name)
          ? error.name : 'Other');
      fail(timeout ? 'attachment-local-proof-timeout' :
        ({ fetch: 'attachment-local-fetch-failed', 'response-blob': 'attachment-local-response-failed',
          'data-decode': 'attachment-data-decode-failed', 'original-file': 'attachment-original-file-read-failed',
          'array-buffer': 'attachment-local-bytes-failed', digest: 'attachment-local-digest-failed' })[operation] ??
        'attachment-local-read-failed');
    }).finally(() => { proof.elapsed = performance.now() - started; });
    return proof;
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
    upload.diagnosticInitial ??= identity;
    if (upload.completed && !sameSource(upload.completed, identity)) {
      fail('attachment-source-changed-after-completion'); return;
    }
    // Only attachment wrappers containing this single image own this signal.
    const busy = ogmAttachmentBusy(state.scope, image);
    upload.previous = identity;
    if (busy) {
      if (upload.completed) { fail('attachment-upload-restarted'); return; }
      upload.sawBusy = true;
    } else if (upload.sawBusy && !upload.completed) upload.completed = identity;
    if (identity.sourceSet) {
      fail('attachment-source-set-unverifiable'); return;
    }
    const sourceFailure = ogmLocalPngSourceFailure(identity.source);
    if (sourceFailure) { fail(sourceFailure); return; }
    if (!upload.file || upload.proofs.has(identity.source)) return;
    upload.fileProof ??= hash(upload.file).then(valid => { upload.originalFileVerified = valid; return valid; })
      .catch(() => { fail('attachment-original-file-read-failed'); return false; });
    upload.proofs.set(identity.source, upload.prove(identity.source));
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
  if (proof?.valid && [...upload.proofs.values()].every(item => item.valid) &&
    !ogmAttachmentBusy(state.scope, image) && ogmDecodedImageMatches(image, upload)) {
    upload.fileVerified = true;
    ogmStopUpload('ready');
    ogmRemember(composer, true);
    return { code: 'ok' };
  }
  return { code: 'attachment-upload-pending' };
};

// A preview can change from blob to data after preparation while Send is disabled.
// Re-prove only this single owned local image before the final synchronous guard.
// A filename/count/marker never substitutes for original-File byte equality.
const ogmVerifyPreparedLocalSource = async composer => {
  const state = globalThis[ogmPreparedKey];
  const code = ogmPreparedFailure(composer);
  if (code !== 'ok' && code !== 'prepared-attachment-source-changed') return code;
  const upload = state.upload;
  if (code === 'ok') {
    if (upload && state.hasAttachment && ogmAttachmentBusy(state.scope, ogmAttachments(state.scope)[0])) {
      return 'prepared-attachment-upload-incomplete';
    }
    return 'ok';
  }
  const current = ogmAttachments(state.scope);
  const expected = state.attachmentIdentities;
  const image = current[0];
  if (!upload?.fileVerified || upload.failure || expected.length !== 1 || current.length !== 1 ||
    !expected[0].image || !(image instanceof HTMLImageElement) ||
    (expected[0].node !== image && expected[0].node.isConnected)) return code;
  if (expected[0].sourceSet || image.srcset) {
    return 'prepared-attachment-content-unverifiable';
  }
  const sourceFailure = ogmLocalPngSourceFailure(expected[0].source) ?? ogmLocalPngSourceFailure(image.src);
  if (sourceFailure) return 'prepared-' + sourceFailure;
  if (ogmAttachmentBusy(state.scope, image)) {
    return 'prepared-attachment-image-not-ready';
  }
  const source = image.src;
  try {
    await Promise.race([image.decode(), new Promise((_, reject) =>
      setTimeout(() => reject(new Error('local-image-decode-timeout')), 2000))]);
  } catch { return 'prepared-attachment-image-not-ready'; }
  if (image.src !== source || !ogmDecodedImageMatches(image, upload)) return 'prepared-attachment-image-not-ready';
  const proof = upload.prove(source);
  await proof.pending;
  const afterCode = ogmPreparedFailure(composer);
  if (globalThis[ogmPreparedKey] !== state) return 'prepared-state-changed';
  if (afterCode !== 'ok' && afterCode !== 'prepared-attachment-source-changed') return afterCode;
  const after = ogmAttachments(state.scope);
  if (after.length !== 1 || after[0] !== image || image.src !== source || image.srcset ||
    !ogmDecodedImageMatches(image, upload) || ogmAttachmentBusy(state.scope, image)) return code;
  if (!proof.valid) return 'prepared-' + (upload.failure ?? 'attachment-content-unverifiable');
  state.attachments = after;
  state.attachmentIdentities = after.map(ogmAttachmentIdentity);
  return 'ok';
};
