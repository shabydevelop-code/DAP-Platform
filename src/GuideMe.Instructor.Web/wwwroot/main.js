"use strict";
const token = document.querySelector('meta[name="guideme-session"]').content;
const $ = id => document.getElementById(id);
 // Keep one live session per open Instructor page. The server terminates
 // after the last window closes; reloads are tolerated by a short grace period.
 let windowSocket;
 function connectWindowSession() {
   if (windowSocket && (windowSocket.readyState === WebSocket.OPEN ||
       windowSocket.readyState === WebSocket.CONNECTING)) return;
   const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
   const url = protocol + '//' + location.host +
     '/window-session?token=' + encodeURIComponent(token);
   windowSocket = new WebSocket(url);
   windowSocket.addEventListener('close', () => {
     // Do not intentionally disconnect on navigation or page hiding;
     // the browser closes the connection when the page is actually destroyed.
     setTimeout(connectWindowSession, 900);
   });
 }
 connectWindowSession();

let guides = [], current = null, selectedStep = null;
let stepSelectionGeneration = 0;
let modalReturnFocus = null;
let pendingChanges = false;
let saveInFlight = 0;
function markDirty() { pendingChanges = true; }
function markClean() { pendingChanges = false; }
function confirmDiscard() {
  if (!pendingChanges) return true;
  if (!confirm('יש שינויים שלא נשמרו. לצאת בלי לשמור אותם?')) return false;
  markClean();
  return true;
}
window.addEventListener('beforeunload', event => {
  if (!pendingChanges && !saveInFlight) return;
  event.preventDefault();
  event.returnValue = '';
});

const esc = v => String(v ?? "").replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
async function api(path, options = {}) {
  const writing = options.method && !['GET','HEAD'].includes(options.method.toUpperCase());
  if (writing) saveInFlight++;
  let response;
  try {
    response = await fetch('/api' + path, {
    ...options, headers: { 'X-GuideMe-Session': token, ...(options.body ? {'Content-Type':'application/json'} : {}), ...options.headers },
    cache: 'no-store'
    });
  } catch (error) {
    if (writing) saveInFlight--;
    throw error;
  }
  if (writing) {
    saveInFlight--;
    if (response.ok) markClean();
  }
  if (!response.ok) {
    let body = await response.text();
    try { const err = JSON.parse(body); body = err.message || err.detail || err.title || body; } catch {}
    throw new Error(body || 'שגיאה בתקשורת עם השרת המקומי (' + response.status + ')');
  }
  return response.status === 204 ? null : response.json();
}
let noticeTimer = null;
function clearNotice() {
  if (noticeTimer !== null) { clearTimeout(noticeTimer); noticeTimer = null; }
  const el = $('notice');
  el.classList.add('hidden');
  el.replaceChildren();
}
function notify(message, error = false, kind = "success") {
  const dialog = $('editor-dialog');
  if (dialog?.open && (error || kind === 'warning')) {
    let inline = $('dialog-notice');
    if (!inline) {
      inline = document.createElement('div');
      inline.id = 'dialog-notice';
      inline.setAttribute('role', 'alert');
      inline.dir = 'rtl';
      inline.style.cssText = 'margin:12px 0;padding:10px 12px;border:1px solid #dba5a5;border-radius:8px;background:#fff1f1;color:#842626;white-space:pre-wrap;overflow-wrap:anywhere;text-align:right;';
      const fields = $('dialog-fields');
      fields?.parentNode?.insertBefore(inline, fields);
    }
    inline.textContent = String(message);
    inline.hidden = false;
    return;
  }
  clearNotice();
  const el = $('notice');
  const type = error ? 'error' : (['success', 'info', 'warning'].includes(kind) ? kind : 'info');
  el.classList.remove('error', 'success', 'info', 'warning');
  el.classList.add(type);
  const symbol = document.createElement('span');
  symbol.className = 'notice-symbol';
  symbol.textContent = {error:'!', warning:'!', success:'✓', info:'i'}[type];
  symbol.setAttribute('aria-hidden', 'true');
  el.append(symbol);
  const text = document.createElement('span');
  text.className = 'notice-text';
  text.textContent = message;
  el.append(text);
  el.setAttribute('role', error || type === 'warning' ? 'alert' : 'status');
  if (error || type === 'warning') {
    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'notice-close';
    close.textContent = '×';
    close.setAttribute('aria-label', 'סגירת הודעה');
    close.addEventListener('click', clearNotice);
    el.append(close);
  }
  el.classList.remove('hidden');
  if (!error && type !== 'warning') noticeTimer = setTimeout(clearNotice, 3000);
}
async function guarded(action) { try { await action(); } catch(e) { notify(e.message || String(e), true); } }
function showList() {
  if (!confirmDiscard()) return;
  stepSelectionGeneration++;
  current = null; selectedStep = null;
  $('list-view').classList.remove('hidden'); $('detail-view').classList.add('hidden');
  $('new-guide').classList.remove('hidden');
  $('back').classList.add('hidden');
  clearNotice();
  document.querySelectorAll('.tab').forEach(t => {
    const active = false;
    t.classList.toggle('active', active);
    t.setAttribute('aria-selected', String(active));
    t.tabIndex = active ? 0 : -1;
  });
  $('library-title').focus();
}
async function loadGuides() {
  guides = await api('/guides');
  $('total-guides').textContent = guides.length;
  renderGuides();
}
function renderGuides() {
  const search = $('guide-search').value.trim().toLowerCase();
  const list = guides.filter(g => g.name.toLowerCase().includes(search) || String(g.id).includes(search) || g.key.toLowerCase().includes(search));
  $('guides-table').innerHTML = list.length
    ? '<table class="table"><thead><tr><th>שם הלומדה</th><th>מזהה</th><th>מספר שלבים</th></tr></thead><tbody>' +
      list.map(g => '<tr><td><button type="button" class="guide-title guide-open-link" data-open="' + g.id + '">' + esc(g.name) + '</button></td><td><span class="guide-id">#' + g.id + '</span></td><td>' + g.stepCount + '</td></tr>').join('') +
      '</tbody></table>'
    : '<div class="empty">לא נמצאו לומדות. ניתן ליצור לומדה חדשה.</div>';
  document.querySelectorAll('[data-open]').forEach(b => b.addEventListener('click', () => guarded(() => openGuide(Number(b.dataset.open)))));
}
async function openGuide(id) {
  if (!confirmDiscard()) return;
  current = await api('/guides/' + id);
  current.variables = await api('/guides/' + id + '/variables');
  // Resolve the friendly title from live application discovery. Contexts
  // persist their runtime/matcher, not the transient window title.
  try {
    const { apps } = await discoverApplications();
    for (const context of current.contexts) {
      const identity = JSON.parse(context.MatchersJson || '[]')[0];
      const match = apps.find(app => app.runtime === context.Runtime &&
        app.matcherProperty.toLowerCase() === String(identity?.Kind || '').toLowerCase() &&
        app.matcherValue.toLowerCase() === String(identity?.Value || '').toLowerCase());
      if (match) context.DiscoveredTitle = match.title;
    }
  } catch {
    // Discovery is optional; persisted matcher labels remain available.
  }
  const g = current.guide;
  $('list-view').classList.add('hidden'); $('detail-view').classList.remove('hidden');
  $('new-guide').classList.add('hidden');
  $('back').classList.remove('hidden');
  $('detail-name').textContent = g.name;
  $('steps-count').textContent = current.steps.length;
  selectTab('steps');
  $('step-search').value = '';
  renderSteps(); renderContexts(); clearNotice();
  const firstStep = [...(current.steps || [])].sort((a,b) => a.order - b.order)[0];
  if (firstStep) {
    const row = [...document.querySelectorAll('#step-list [data-step]')]
      .find(item => item.dataset.step === firstStep.id);
    row?.click();
  } else {
    $('detail-name').focus();
  }
}
function selectTab(tab) {
  document.querySelectorAll('.tab').forEach(t => {
    const active=t.dataset.tab===tab;
    t.classList.toggle('active',active);
    t.setAttribute('aria-selected',String(active));
    t.tabIndex=active?0:-1;
  });
  $('steps-panel').classList.toggle('hidden',tab!=='steps');
  $('contexts-panel').classList.toggle('hidden',tab!=='contexts');
}
document.querySelectorAll('.tab').forEach(tabButton => tabButton.addEventListener('keydown', e => {
  const tabs = [...document.querySelectorAll('.tab')];
  const index = tabs.indexOf(tabButton);
  let next = index;
  if (e.key === 'ArrowRight') next = (index - 1 + tabs.length) % tabs.length;
  else if (e.key === 'ArrowLeft') next = (index + 1) % tabs.length;
  else if (e.key === 'Home') next = 0;
  else if (e.key === 'End') next = tabs.length - 1;
  else return;
  e.preventDefault();
  tabs[next].focus();
  selectTab(tabs[next].dataset.tab);
}));
function syncStepListHeight() {
  const layout = document.querySelector('#steps-panel .steps-layout');
  if (!layout) return;
  if (window.matchMedia('(max-width:700px)').matches) {
    layout.style.removeProperty('--guideme-steps-layout-height');
    return;
  }
  const top = layout.getBoundingClientRect().top;
  if (top <= 0 || !layout.getClientRects().length) return;
  // Size the grid itself, so both the list and inspector fill its height.
  // Measuring the list alone leaves unused space below the scrolling region.
  const available = Math.max(280, Math.floor(window.innerHeight - top - 24));
  layout.style.setProperty('--guideme-steps-layout-height', available + 'px');
}
window.addEventListener('resize', () => requestAnimationFrame(syncStepListHeight));

function renderSteps() {
  // Invalidate outstanding inspector requests from the previous step list.
  stepSelectionGeneration++;
  selectedTargetPreviewValue = null;
  selectedValidationExpectedValue = null;
  selectedCompletionDescription = null;
  selectedStep = null; $('step-inspector').innerHTML = '<div class="empty-inspector">בחר שלב להצגת הפרטים שלו</div>';
  const search = $('step-search').value.trim().toLowerCase();
  const steps = (current?.steps || []).filter(s => String(s.order).includes(search) || s.id.toLowerCase().includes(search) || (s.bubble || '').toLowerCase().includes(search));
  requestAnimationFrame(syncStepListHeight);
  $('step-list').innerHTML = steps.length ? steps.map(s =>
    '<div class="step-row' + (s.enabled ? '' : ' step-disabled') + '" role="button" tabindex="0" aria-pressed="false" aria-label="' + esc('שלב ' + s.order + ': ' + friendlyInstructionText(s.bubble || 'שלב ' + s.order) + (s.enabled ? '' : ', מושבת')) + '" data-step="' + esc(s.id) + '"><span class="step-drag-handle" draggable="true" role="img" aria-label="גרירת שלב לשינוי סדר" title="גרור לשינוי סדר">⠿</span><span class="step-num">' + s.order + '</span><span class="step-body"><strong>' +
    stepListInstructionHtml(s.bubble || 'שלב ' + s.order) + '</strong></span>' + (s.enabled ? '' : '<span class="step-disabled-badge">מושבת</span>') + '</div>').join('')
    : '<div class="empty">אין שלבים להצגה</div>';
  const tip = document.getElementById('step-name-tooltip') || (() => {
    const node = document.createElement('div');
    node.id = 'step-name-tooltip';
    node.className = 'step-name-tooltip';
    node.setAttribute('role', 'tooltip');
    node.hidden = true;
    document.body.appendChild(node);
    return node;
  })();
  let activeTipRow = null;
  const hideTip = () => {
    if (activeTipRow) activeTipRow.removeAttribute('aria-describedby');
    activeTipRow = null;
    tip.hidden = true;
  };
  const showTip = row => {
    const name = row.querySelector('.step-body strong');
    if (!name) return;
    hideTip();
    // Show the full text only when the two-line label is clipped.
    if (![...name.querySelectorAll('.step-instruction-line')].some(line => line.scrollWidth > line.clientWidth + 1) &&
        name.scrollHeight <= name.clientHeight + 1) return;
    activeTipRow = row;
    const step = (current?.steps || []).find(s => s.id === row.dataset.step);
    tip.innerHTML = friendlyInstructionHtml(step?.bubble || name.textContent);
    tip.hidden = false;
    row.setAttribute('aria-describedby', tip.id);
    const rect = row.getBoundingClientRect();
    const width = tip.offsetWidth;
    tip.style.left = Math.max(8, Math.min(window.innerWidth - width - 8, rect.right - width)) + 'px';
    const below = rect.bottom + 6;
    tip.style.top = (below + tip.offsetHeight <= window.innerHeight - 8
      ? below : Math.max(8, rect.top - tip.offsetHeight - 6)) + 'px';
  };
  document.querySelectorAll('[data-step]').forEach(row => {
    row.addEventListener('mouseenter', () => showTip(row));
    row.addEventListener('mouseleave', () => { if (activeTipRow === row && document.activeElement !== row) hideTip(); });
    row.addEventListener('focus', () => showTip(row));
    row.addEventListener('blur', () => { if (activeTipRow === row) hideTip(); });
    row.addEventListener('keydown', e => { if (e.key === 'Escape') hideTip(); });
  });

  let draggingStepId = null;
  document.querySelectorAll('.step-drag-handle').forEach(handle => {
    const row = handle.closest('[data-step]');
    handle.addEventListener('dragstart', event => {
      if ($('step-search').value.trim()) { event.preventDefault(); return; }
      draggingStepId = row.dataset.step;
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', draggingStepId);
      row.classList.add('step-dragging');
      hideTip();
    });
    handle.addEventListener('dragend', () => {
      draggingStepId = null;
      document.querySelectorAll('.step-drop-before,.step-drop-after,.step-dragging')
        .forEach(item => item.classList.remove('step-drop-before','step-drop-after','step-dragging'));
    });
  });
  document.querySelectorAll('[data-step]').forEach(row => {
    row.addEventListener('dragover', event => {
      if (!draggingStepId || draggingStepId === row.dataset.step) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = 'move';
      row.classList.toggle('step-drop-before', event.clientY < row.getBoundingClientRect().top + row.offsetHeight / 2);
      row.classList.toggle('step-drop-after', event.clientY >= row.getBoundingClientRect().top + row.offsetHeight / 2);
    });
    row.addEventListener('dragleave', () => row.classList.remove('step-drop-before','step-drop-after'));
    row.addEventListener('drop', event => {
      event.preventDefault();
      const sourceId = draggingStepId;
      draggingStepId = null;
      document.querySelectorAll('.step-drop-before,.step-drop-after')
        .forEach(item => item.classList.remove('step-drop-before','step-drop-after'));
      if (!sourceId || sourceId === row.dataset.step || !confirmDiscard()) return;
      const original = [...current.steps];
      const from = original.findIndex(step => step.id === sourceId);
      const target = original.findIndex(step => step.id === row.dataset.step);
      if (from < 0 || target < 0) return;
      const after = event.clientY >= row.getBoundingClientRect().top + row.offsetHeight / 2;
      const ordered = [...original];
      const [moved] = ordered.splice(from, 1);
      let index = ordered.findIndex(step => step.id === row.dataset.step);
      if (after) index++;
      ordered.splice(index, 0, moved);
      if (ordered.every((step,i) => step.id === original[i].id)) return;
      guarded(async () => {
        await api('/guides/' + current.guide.id + '/steps/order', {
          method:'PUT', body:JSON.stringify({stepIds:ordered.map(step => step.id)})
        });
        ordered.forEach((step,i) => { step.order = i + 1; });
        current.steps = ordered;
        const keepSelected = selectedStep;
        renderSteps();
        if (keepSelected) document.querySelectorAll('[data-step]').forEach(item => {
          if (item.dataset.step === keepSelected) item.click();
        });
      });
    });
  });
  $('step-list').addEventListener('scroll', hideTip, {passive:true});
  document.querySelectorAll('[data-step]').forEach(b => b.addEventListener('keydown', e => {
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); b.click(); }
  }));
  document.querySelectorAll('[data-step]').forEach(b => b.addEventListener('click',() => guarded(async () => {
    if (!confirmDiscard()) return;
    const stepId = b.dataset.step;
    const guide = current;
    const guideId = guide.guide.id;
    const selectionGeneration = ++stepSelectionGeneration;
    selectedStep = stepId;
    selectedTargetPreviewValue = null;
    selectedValidationExpectedValue = null;
    selectedCompletionDescription = null;
    $('step-inspector').innerHTML = '<div class="empty-inspector" role="status">טוען את פרטי השלב...</div>';
    document.querySelectorAll('[data-step]').forEach(x => { x.classList.toggle('active', x===b); x.setAttribute('aria-pressed', String(x===b)); });
    const [data, variableActions] = await Promise.all([
      api('/guides/' + guideId + '/step/' + encodeURIComponent(stepId)),
      api('/guides/' + guideId + '/steps/' + encodeURIComponent(stepId) + '/variables')
    ]);
    // Never let an earlier selection overwrite the active step's inspector.
    if (selectionGeneration !== stepSelectionGeneration || current !== guide || selectedStep !== stepId)
      return;
    current.stepContexts ??= {};
    current.stepContexts[stepId] = data.step?.[0]?.ApplicationContextId || null;
    const step = current.steps.find(x=>x.id===stepId);
    if (!step) return;
    const contextId = data.step?.[0]?.ApplicationContextId;
    const context = current.contexts.find(c => c.ContextId === contextId);
    const target = data.targets.find(x => x.Purpose === 'Primary');
    const element = (data.elements || []).find(x => x.ElementId === target?.ElementId);
    const rule = data.rules.find(x => x.RuleType === 'PrimaryValidation');
    const locators = parseJson(element?.LocatorsJson, []);
    const frames = parseJson(element?.FrameContextJson, []);
    const conditionText = rule ? validationLabel(rule.Kind) : 'לא הוגדר תנאי';
    const identityRules = data.rules.filter(x => x.RuleType === 'AdditionalCompletionCondition' &&
      parseJson(x.OptionsJson, {})?.identity === true);
    const completionRules = data.rules.filter(x => x.RuleType === 'AdditionalCompletionCondition' &&
      parseJson(x.OptionsJson, {})?.identity !== true)
      .sort((a, b) => (Number(a.RuleOrder) || 0) - (Number(b.RuleOrder) || 0));
    const identityItems = identityRules.map(rule => {
      const target = data.targets.find(x => x.UsageId === rule.TargetUsageId);
      const element = data.elements.find(x => x.ElementId === target?.ElementId);
      const locators = parseJson(element?.LocatorsJson, []);
      return {rule, ...screenElementPresentation(context?.Runtime, element, locators)};
    });
    const completionItems = completionRules.map(rule => {
      const target = data.targets.find(x => x.UsageId === rule.TargetUsageId);
      const element = data.elements.find(x => x.ElementId === target?.ElementId);
      const locators = parseJson(element?.LocatorsJson, []);
      const selector = locators[0]?.Value || '';
      const classification = classifyTarget(context?.Runtime, element, locators);
      const type = classification.label || (/^h[1-6](?=:|$)/i.test(selector) ? 'כותרת במסך' : null);
      const displayName = (element?.DisplayName || '').trim();
      const name = displayName && !/^[#.[>:+~]/.test(displayName) &&
        !/^[a-z][\\w-]*\\[[^\\]]+\\]/i.test(displayName) ? displayName :
        /^h[1-6](?=:|$)/i.test(selector) ? 'כותרת במסך' : 'הרכיב שנבחר';
      const attribute = rule.Kind === 'attribute-equals' ? parseJson(rule.ExpectedValue, {}) : {};
      const description = rule.Kind === 'value-contains'
        ? 'מכיל את הערך ' + friendlyInstructionHtml(rule.ExpectedValue || '')
        : rule.Kind === 'target-appeared' ? 'מופיע במסך'
        : rule.Kind === 'attribute-equals'
          ? 'המאפיין "' + esc(attribute.name || 'הנבחר') + '" שווה ל־"' + esc(attribute.value ?? '') + '"'
          : 'תנאי השלמה';
      return {rule, locators, name, type, description};
    });
    const completionRule = completionRules[0];
    const completionElementName = completionItems[0]?.name || '';
    const primaryPresentation = screenElementPresentation(context?.Runtime, element, locators);
    const screenElementOrderKey = current.guide.id + ':' + stepId;
    const screenElements = [
      ...(target ? [{elementId:target.ElementId, isActionTarget:true, presentation:primaryPresentation}] : []),
      ...identityItems.map((item, index) => ({
        elementId:data.targets.find(x => x.UsageId === item.rule.TargetUsageId)?.ElementId,
        isActionTarget:false, presentation:item, index
      }))
    ];
    const previousOrder = screenElementDisplayOrder.get(screenElementOrderKey) || [];
    const elementOrder = [
      ...previousOrder.filter(id => screenElements.some(item => item.elementId === id)),
      ...screenElements.map(item => item.elementId).filter(id => !previousOrder.includes(id))
    ];
    screenElementDisplayOrder.set(screenElementOrderKey, elementOrder);
    screenElements.sort((a,b) => elementOrder.indexOf(a.elementId) - elementOrder.indexOf(b.elementId));
    const editableTargetBinding = primaryPresentation.binding;
    selectedTargetPreviewValue = editableTargetBinding?.value || null;
    selectedValidationExpectedValue = rule?.ExpectedValue || null;
    selectedCompletionDescription = completionRule?.Kind === 'value-contains'
      ? () => esc(completionElementName) + ' מכיל את הערך ' + friendlyInstructionHtml(completionRule.ExpectedValue || '') + '.'
      : null;
    const appText = context ? appLabel(context) : 'לא משויך';

    $('step-inspector').innerHTML =
      '<div class="step-editor-head"><div><h2>שלב ' + step.order + '</h2></div>' +
      '<div class="step-editor-actions">' +
      '<button type="button" id="step-enabled-button" class="btn small step-state-button ' +
       (step.enabled ? 'is-enabled' : 'is-disabled') +
       '" aria-pressed="' + (step.enabled ? 'true' : 'false') + '">' +
       (step.enabled ? 'שלב פעיל' : 'שלב מושבת') + '</button>' +
       '<button class="btn secondary small" id="duplicate-step">שכפל שלב</button>' +
      '<button class="btn danger small" id="delete-step">מחיקת שלב</button>' +
      '<button class="btn primary small" id="test-from-step"' + (step.enabled ? '' : ' disabled') + '>בדיקה משלב זה</button></div></div>' +
      '<div class="step-application"><strong>אפליקציית יעד</strong><span title="' + esc(appText) + '">' + esc(appText) + '</span></div>' +
      '<div class="step-summary"><div class="step-summary-heading"><strong>הנחיה למשתמש</strong>' +
       '</div>' +
       '<div class="step-summary-content"><div class="step-summary-rich" dir="rtl">' + friendlyInstructionHtml(step.bubble || 'לא הוגדרה הנחיה') + '</div>' +
       '<div class="context-actions"><button class="btn secondary small" id="edit-step">עריכת תוכן השלב</button></div></div></div>' +
       '<div class="step-friendly-grid">' +
      '<section class="step-friendly-card"><h3>רכיב במסך</h3>' +
      '<div class="screen-element-list">' +
      screenElements.map(item =>
        '<div class="screen-target-bubble ' + (item.isActionTarget ? 'screen-target-primary' : 'screen-target-identity') + '">' +
        '<div class="completion-details">' +
        renderScreenElementDetails(item.presentation) + '</div>' +
        ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') ?
          renderScreenElementActions(item.isActionTarget, item.index) : '') +
        (item.isActionTarget ? '<div id="pick-result" class="hidden" aria-live="polite"></div>' : '') +
        '</div>'
      ).join('') +
      (!target ? '<div class="screen-target-bubble screen-target-primary"><p id="empty-primary-target-message">עדיין לא נבחר רכיב לשלב הזה.</p>' +
        ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') ?
          '<button id="pick-element" class="btn primary">הפעלת ממפה רכיבים</button>' +
          '<div id="pick-result" class="hidden" aria-live="polite"></div>' : '') +
        '</div>' : '') +
      '</div>' +
      ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') ?
        '<div class="identity-targets">' +
        (target ? '<button class="btn secondary small" id="add-identity">הוספת מזהה אימות</button>' :
          '<p>יש לבחור תחילה רכיב במסך.</p>') +
        '<div id="identity-pick-result" class="screen-target-bubble screen-target-capture hidden" aria-live="polite"></div></div>' : '') +
      '</section>' +
      '<section class="step-friendly-card"><h3>מתי ממשיכים לשלב הבא?</h3>' +
      '<p>' + esc(!target ? 'יוגדר לאחר בחירת הרכיב.' : conditionText) + '</p>' +
      (rule?.ExpectedValue ? '<div class="validation-expected-block"><span class="validation-expected-label">ערך צפוי:</span><strong class="validation-expected-preview" dir="auto">' + friendlyInstructionHtml(rule.ExpectedValue) + '</strong></div>' : '') +
      ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') && target ?
        '<div class="validation-actions">' +
        '<button class="btn secondary small" id="edit-validation">' +
        (rule ? 'עריכה' : 'הגדרת תנאי התקדמות') + '</button>' +
        (rule ? '<button class="btn danger small" id="remove-validation">הסרה</button>' : '') +
        '</div>' : '') +
      '</section></div>' +
      ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') ?
        '<section class="step-friendly-card completion-card"><h3>תנאי השלמה נוספים</h3>' +
        completionItems.map((item, index) =>
          '<div class="completion-item" data-completion-index="' + index + '">' +
          '<div class="completion-details"><div class="completion-target-summary"><p><strong>' +
          esc(item.name) + '</strong>' +
          (item.type ? ' <span class="field-help">(' + esc(item.type) + ')</span>' : '') +
          '</p></div><div class="completion-detail"><span class="completion-detail-label">תנאי:</span>' +
          '<strong class="completion-description-preview">' + item.description + '</strong></div></div>' +
          '<div class="target-action-buttons"><div class="target-action-primary">' +
          '<button class="btn secondary small" data-completion-action="replace" data-index="' + index + '">בחירת רכיב אחר</button>' +
          '<button class="btn secondary small" data-completion-action="edit" data-index="' + index + '">עדכון</button>' +
          '<button class="btn danger small" data-completion-action="remove" data-index="' + index + '">הסרה</button>' +
          '</div></div></div>'
        ).join('') +
        (!target ? '<p>יש לבחור תחילה רכיב במסך.</p>' :
          '<button class="btn secondary small" id="add-completion">הוספת תנאי השלמה</button>') +
        '<div id="completion-pick-result" class="hidden" aria-live="polite"></div>' +
        '</section>' : '') +
      '<section class="step-friendly-card completion-card" aria-labelledby="step-variables-title">' +
      '<h3 id="step-variables-title">משתנים</h3>' +
      (variableActions.length ? variableActions.map(action => {
        const variable = (current.variables || []).find(v => v.id === action.variableId);
        const usage = data.targets.find(t => t.UsageId === action.targetUsageId);
        const sourceElement = usage ? (data.elements || []).find(e => e.ElementId === usage.ElementId) : null;
        const sourceLocator = parseJson(sourceElement?.LocatorsJson, [])[0]?.Value || '';
        const sourceName = sourceElement?.DisplayName || sourceLocator || 'רכיב שלא זוהה';
        const extraction = !action.pattern ? 'הערך המלא' :
          action.pattern === '([0-9]+)' ? 'מספר בלבד' : 'חילוץ מותאם אישית';
        const sampleMatch = action.pattern ? (() => {
          try {
            const match = sourceName.match(new RegExp(action.pattern));
            return match ? (match[1] ?? match[0]) : null;
          } catch { return null; }
        })() : sourceName;
        const description = '<div class="variable-action-details"><div>מקור: <strong>טקסט הרכיב שנבחר</strong></div>' +
          '<div>חילוץ: <strong>' + esc(extraction) + '</strong></div>' +
          (sourceElement?.DisplayName
            ? '<div>דוגמה מהבחירה: <strong>' + esc(sourceName) + '</strong>' +
              (sampleMatch !== null ? ' ← <strong>' + esc(sampleMatch) + '</strong>' : '') + '</div>'
            : '') + '</div>';
        return '<div class="guide-variable-row"><div><strong>' + esc(variable?.name || action.variableId) +
          '</strong>' + description +
          '</div><span class="context-actions">' +
          '<button class="btn secondary small" data-edit-variable-action="' + esc(action.id) + '">עריכה</button>' +
          '<button class="btn danger small" data-remove-variable-action="' + esc(action.id) + '">הסרה</button></span></div>';
      }).join('') : '<p>לא הוגדרו פעולות משתנים לשלב.</p>') +
      '<button class="btn secondary small" id="add-variable-action"' +
      ((current.variables || []).length ? '' : ' disabled') + '>הוספת פעולה למשתנה</button>' +
      '<div id="variable-pick-result" class="hidden" aria-live="polite"></div></section>';
    $('add-variable-action').onclick = () => editStepVariableAction(step, context);
    document.querySelectorAll('[data-edit-variable-action]').forEach(button =>
      button.onclick = () => {
        const action = variableActions.find(item => item.id === button.dataset.editVariableAction);
        if (!action) return;
        const choices = (current.variables || []).map(v => ({value:v.id,text:v.name}));
        const patternMode = !action.pattern ? 'full' :
          action.pattern === '([0-9]+)' ? 'number' : 'custom';
        modal('עריכת פעולת משתנה', [
          {name:'variableId',label:'משתנה לעדכון',value:action.variableId,choices},
          {name:'extraction',label:'אופן חילוץ הערך',value:patternMode,
           choices:[{value:'full',text:'הערך המלא'},{value:'number',text:'מספר בלבד'},
             {value:'custom',text:'חילוץ מותאם אישית'}]},
          {name:'pattern',label:'ביטוי לחילוץ מותאם אישית',value:patternMode === 'custom' ? action.pattern : '',
           max:300}
        ], async values => {
          const pattern = values.extraction === 'number' ? '([0-9]+)' :
            values.extraction === 'custom' ? values.pattern : null;
          if (values.extraction === 'custom' && !pattern?.trim())
            throw new Error('יש להזין ביטוי לחילוץ.');
          await api('/guides/' + current.guide.id + '/steps/' + encodeURIComponent(step.id) +
            '/variables/' + encodeURIComponent(action.id), {
              method:'PUT',body:JSON.stringify({variableId:values.variableId,pattern})
            });
          await reloadSelectedStep(current.guide.id,step.id);
          notify('פעולת המשתנה עודכנה.');
        }, 'שמירה');
        const extraction = $('field-extraction');
        const custom = $('field-pattern')?.closest('.field');
        const update = () => custom?.classList.toggle('hidden', extraction?.value !== 'custom');
        extraction?.addEventListener('change',update);
        update();
      });
    document.querySelectorAll('[data-remove-variable-action]').forEach(button =>
      button.onclick = () => guarded(async () => {
        if (!confirm('להסיר את פעולת המשתנה?')) return;
        const guideId = current.guide.id, stepId = step.id;
        await api('/guides/' + guideId + '/steps/' + encodeURIComponent(stepId) +
          '/variables/' + encodeURIComponent(button.dataset.removeVariableAction),
          {method:'DELETE', body:'{}'});
        await openGuide(guideId);
        document.querySelectorAll('[data-step]').forEach(row => {
          if (row.dataset.step === stepId) row.click();
        });
      }));
    $('edit-step').onclick = () => editStep(step, contextId);
    $('step-enabled-button').onclick = () => guarded(async () => {
      const button = $('step-enabled-button');
      const enabled = !step.enabled;
      button.disabled = true;
      try {
        await api('/guides/' + current.guide.id + '/steps/' + encodeURIComponent(step.id), {
          method:'PUT', body:JSON.stringify({
            bubble:step.bubble, enabled, contextId:contextId || null
          })
        });
        step.enabled = enabled;
        button.textContent = enabled ? 'שלב פעיל' : 'שלב מושבת';
        button.classList.toggle('is-enabled', enabled);
        button.classList.toggle('is-disabled', !enabled);
        button.setAttribute('aria-pressed', String(enabled));
        $('test-from-step').disabled = !enabled;
        const row = [...document.querySelectorAll('[data-step]')]
          .find(item => item.dataset.step === step.id);
        if (row) {
          row.classList.toggle('step-disabled', !enabled);
          row.setAttribute('aria-label', 'שלב ' + step.order + ': ' +
            friendlyInstructionText(step.bubble || 'שלב ' + step.order) +
            (enabled ? '' : ', מושבת'));
          row.querySelector('.step-disabled-badge')?.remove();
          if (!enabled) {
            const badge = document.createElement('span');
            badge.className = 'step-disabled-badge';
            badge.textContent = 'מושבת';
            row.append(badge);
          }
        }
      } catch (error) {
        throw error;
      } finally {
        if (button.isConnected) button.disabled = false;
      }
    });
    $('duplicate-step').onclick = () => guarded(async () => {
      if (!current || !confirmDiscard()) return;
      const guideId = current.guide.id;
      const button = $('duplicate-step');
      button.disabled = true;
      try {
        const result = await api('/guides/' + guideId + '/steps/' +
          encodeURIComponent(step.id) + '/duplicate', {method:'POST', body:'{}'});
        await loadGuides();
        await openGuide(guideId);
        const row = [...document.querySelectorAll('[data-step]')]
          .find(item => item.dataset.step === result.stepId);
        if (row) row.click();
        notify('השלב שוכפל ונפתח לעריכה.');
      } finally { if (button.isConnected) button.disabled = false; }
    });
    $('delete-step').onclick = () => guarded(async () => {
      if (!current || !confirmDiscard()) return;
      const guideId = current.guide.id;
      const stepId = step.id;
      if (!confirm('למחוק את שלב ' + step.order + '?\n' +
        (step.bubble || 'ללא הנחיה') +
        '\n\nהשלב, הרכיבים וכללי ההתקדמות הייחודיים לו יימחקו לצמיתות.')) return;
      const button = $('delete-step');
      button.disabled = true;
      try {
        await api('/guides/' + guideId + '/steps/' + encodeURIComponent(stepId),
          {method:'DELETE', body:'{}'});
        await loadGuides();
        await openGuide(guideId);
        notify('שלב ' + step.order + ' נמחק. סדר השלבים עודכן.');
      } finally { if (button.isConnected) button.disabled = false; }
    });
    $('test-from-step').onclick = () => guarded(async () => {
      if (!current || !confirmDiscard()) return;
      const button = $('test-from-step');
      button.disabled = true;
      try {
        await verifyTestTransport(current.guide.id, step.id);
        await api('/guides/' + current.guide.id + '/test?startStepId=' + encodeURIComponent(step.id),
          {method:'POST', body:'{}'});
        notify('ה־Learner הופעל משלב ' + step.order + '. הבועית תוצג כשהרכיב המתאים יהיה זמין.', false, 'info');
      } finally { button.disabled = false; }
    });
    const addIdentity = $('add-identity');
    if (addIdentity) addIdentity.onclick = () => guarded(() =>
      beginTargetPicker(context.Runtime, current.guide.id, step.id, contextId,
        false, true, context, null, {kind:'target-appeared', identity:true}));
    document.querySelectorAll('[data-identity-action]').forEach(button => {
      button.onclick = () => guarded(async () => {
        const item = identityItems[Number(button.dataset.index)];
        if (!item) return;
        const id = item.rule.TargetUsageId;
        const action = button.dataset.identityAction;
        if (action === 'set-action') {
          const guideId = current.guide.id;
          await api('/guides/'+guideId+'/steps/'+encodeURIComponent(step.id)+'/action-target',
            {method:'PUT',body:JSON.stringify({identityUsageId:id})});
          await reloadSelectedStep(guideId, step.id);
          notify('יעד הפעולה עודכן.');
          return;
        }
        if (action === 'replace') {
          return beginTargetPicker(context.Runtime, current.guide.id, step.id,
            contextId, true, true, context, null,
            {kind:'target-appeared', identity:true, completionId:id});
        }
        if (action === 'edit') {
          return editPrimaryTargetValue({
            kind:'target', step, locators:item.locators,
            binding:editableLocatorBinding(context?.Runtime, item.element, item.locators),
            identityUsageId:id
          });
        }
        if (action === 'remove') {
          if (!confirm('להסיר את מזהה האימות?')) return;
          await api('/guides/'+current.guide.id+'/steps/'+encodeURIComponent(step.id)+
            '/completion-target?completionId='+encodeURIComponent(id), {method:'DELETE'});
          await reloadSelectedStep(current.guide.id, step.id);
          notify('מזהה האימות הוסר.');
        }
      });
    });
    const addCompletion = $('add-completion');
    if (addCompletion) addCompletion.onclick = () => guarded(() =>
      beginTargetPicker(context.Runtime, current.guide.id, step.id, contextId, false, true, context));
    document.querySelectorAll('[data-completion-action]').forEach(button => {
      button.onclick = () => {
        const item = completionItems[Number(button.dataset.index)];
        if (!item) return;
        const completionId = item.rule.TargetUsageId;
        if (button.dataset.completionAction === 'edit')
          return editCompletionCondition(step, item.rule, item.locators);
        if (button.dataset.completionAction === 'replace')
          return guarded(() => beginTargetPicker(context.Runtime, current.guide.id, step.id,
            contextId, true, true, context, null,
            {kind:item.rule.Kind, expectedValue:item.rule.ExpectedValue || '', completionId}));
        if (button.dataset.completionAction === 'remove') return guarded(async () => {
          if (!confirm('להסיר את תנאי ההשלמה?')) return;
          await api('/guides/'+current.guide.id+'/steps/'+encodeURIComponent(step.id)+
            '/completion-target?completionId='+encodeURIComponent(completionId), {method:'DELETE'});
          await reloadSelectedStep(current.guide.id, step.id);
          notify('תנאי ההשלמה הוסר.');
        });
      };
    });
    const startElementPicker = (replacing = false, completion = false) =>
      beginTargetPicker(context.Runtime, current.guide.id, step.id, contextId, replacing, completion, context);
    const bindTarget = $('bind-target-variable');
    if (bindTarget) bindTarget.onclick = () => {
      if (editableTargetBinding) return editPrimaryTargetValue({
        kind:'target', step, locators, binding:editableTargetBinding
      });
      notify('לרכיב זה אין ערך זיהוי טקסטואלי שניתן לערוך. ניתן לבחור רכיב אחר.', true);
    };
    const targetReplace = $('replace-web-target');
    if (targetReplace) targetReplace.onclick = () => guarded(() => startElementPicker(true));
    const removePrimary = $('remove-primary-target');
    if (removePrimary) removePrimary.onclick = () => guarded(async () => {
      if (identityItems.length > 0) {
        notify('לא ניתן להסיר את יעד הפעולה כאשר קיימים רכיבים נוספים. יש להגדיר תחילה רכיב אחר כיעד פעולה.', true);
        return;
      }
      if (!confirm('להסיר את הרכיב מהשלב? תנאי האימות ותנאי ההשלמה התלויים בו יוסרו גם הם.')) return;
      await api('/guides/' + current.guide.id + '/steps/' +
        encodeURIComponent(step.id) + '/primary-target', {method:'DELETE',body:'{}'});
      await reloadSelectedStep(current.guide.id, step.id);
      notify('הרכיב הוסר מהשלב.');
    });
    const conditionEditor = $('edit-validation');
    if (conditionEditor) conditionEditor.onclick = () => editValidation(step, rule);
    const removeValidation = $('remove-validation');
    if (removeValidation) removeValidation.onclick = () => guarded(async () => {
      if (!confirm('להסיר את תנאי ההתקדמות מהשלב?')) return;
      const guideId = current.guide.id, stepId = step.id;
      await api('/guides/' + guideId + '/steps/' + encodeURIComponent(stepId) + '/validation',
        {method:'DELETE',body:'{}'});
      await openGuide(guideId);
      const item = [...document.querySelectorAll('[data-step]')].find(x => x.dataset.step === stepId);
      if (item) item.click();
      notify('תנאי ההתקדמות הוסר.');
    });
    const startButton = $('pick-element');
    if (startButton) startButton.onclick = () => guarded(() => startElementPicker());
    syncPickerButtons();
    document.dispatchEvent(new CustomEvent('guideme:step-rendered', {detail:{stepId}}));

  })));
}
function parseJson(value, fallback) {
  try { return value ? JSON.parse(value) : fallback; } catch { return fallback; }
}
const validationChoices = [
  {value:'clicked',text:'לחיצה על הרכיב'},
  {value:'value-not-empty',text:'הוזן ערך בשדה'},
  {value:'value-equals',text:'השדה מכיל ערך מסוים'}
];
function validationLabel(kind) {
  return validationChoices.find(x=>x.value===kind)?.text ||
    'תנאי מתקדם שהוגדר במערכת';
}
function editCompletionCondition(step, rule, locators) {
  const locator = (locators || [])[0];
  const binding = locatorTextBinding(locator);
  const fields = [];
  if (binding) fields.push({
    name:'locatorValue', label:'ערך לזיהוי הרכיב', value:binding.value,
    max:400, multiline:true
  });
  fields.push({
    name:'kind', label:'חוק השלמה', value:rule.Kind,
    choices:[
      {value:'target-appeared',text:'הרכיב מופיע במסך'},
      {value:'value-contains',text:'הרכיב מכיל ערך מסוים'}
    ]
  });
  fields.push({
    name:'expectedValue', label:'ערך צפוי', value:rule.ExpectedValue || '',
    max:500, multiline:true, optional:true
  });
  modal('עריכת תנאי השלמה — שלב ' + step.order, fields, async values => {
    const expected = values.kind === 'value-contains' ? values.expectedValue.trim() : null;
    if (values.kind === 'value-contains' && !expected)
      rejectInstructorField($('field-expectedValue'), 'יש להזין ערך צפוי.');
    if (binding) {
      const locatorValue = values.locatorValue.trim();
      if (!locatorValue)
        rejectInstructorField($('field-locatorValue'), 'יש להזין ערך לזיהוי הרכיב.');
      if (locatorValue !== binding.value) {
        await api('/guides/'+current.guide.id+'/steps/'+encodeURIComponent(step.id)+
          '/completion-target/variable?completionId='+encodeURIComponent(rule.TargetUsageId),
          {method:'POST',body:JSON.stringify({locatorValue})});
      }
    }
    await api('/guides/'+current.guide.id+'/steps/'+encodeURIComponent(step.id)+
      '/completion-target?completionId='+encodeURIComponent(rule.TargetUsageId),
      {method:'PUT',body:JSON.stringify({kind:values.kind,expectedValue:expected})});
    await reloadSelectedStep(current.guide.id, step.id);
    notify('תנאי ההשלמה עודכן.');
  });
  const kind = $('field-kind');
  const expectedField = $('field-expectedValue')?.closest('.field');
  const refresh = () => {
    if (expectedField) expectedField.classList.toggle('hidden', kind.value !== 'value-contains');
  };
  kind.addEventListener('change', refresh);
  refresh();
}
function editValidation(step, rule) {
  const kind = validationChoices.some(x=>x.value===rule?.Kind) ? rule.Kind : 'clicked';
  const variables = current?.variables || [];
  const references = new Map();
  const display = String(rule?.ExpectedValue || '').replace(/\{\{var:([^}]+)\}\}/g, (token, id) => {
    const variable = variables.find(v => v.id === id);
    const label = '【' + variablePreviewValue(variable) + '】';
    references.set(label, id);
    return label;
  });
  modal('עריכת תנאי התקדמות — שלב ' + step.order, [
    {name:'kind',label:'מתי המערכת תעבור לשלב הבא?',value:kind,choices:validationChoices},
    {name:'expectedValue',label:'ערך צפוי',value:display,max:500,multiline:true,optional:true,
      help:'אפשר לשלב טקסט ומשתנים בערך הצפוי.'}
  ],async values=>{
    let expected = values.expectedValue;
    for (const label of [...references.keys()].sort((a,b)=>b.length-a.length))
      expected = expected.split(label).join('{{var:' + references.get(label) + '}}');
    if (/【[^】]*】/.test(expected))
      rejectInstructorField($('field-expectedValue'), 'סימון משתנה לא מוכר.');
    if (values.kind === 'value-equals' && !expected.trim())
      rejectInstructorField($('field-expectedValue'), 'יש להזין ערך צפוי.');
    await api('/guides/'+current.guide.id+'/steps/'+encodeURIComponent(step.id)+'/validation',
      {method:'PUT',body:JSON.stringify({kind:values.kind,
        expectedValue:values.kind === 'value-equals' ? expected : null})});
    await reloadSelectedStep(current.guide.id,step.id);
    notify('תנאי ההתקדמות עודכן.');
  });
  const kindControl = $('field-kind');
  const input = $('field-expectedValue');
  const valueContainer = input?.closest('.field');
  let caretStart = input?.selectionStart || 0, caretEnd = input?.selectionEnd || 0;
  const trackCaret = () => { caretStart=input.selectionStart; caretEnd=input.selectionEnd; };
  ['click','keyup','select'].forEach(event=>input?.addEventListener(event,trackCaret));
  if (variables.length && valueContainer) {
    const variableControls = document.createElement('div');
    variableControls.style.display = 'grid';
    variableControls.style.gap = '8px';
    variableControls.style.marginTop = '12px';
    variableControls.style.justifyItems = 'start';
    const selector = document.createElement('select');
    selector.style.width = '100%';
    selector.setAttribute('aria-label','משתנה');
    variables.forEach(v => {
      const option = document.createElement('option');
      option.value=v.id; option.textContent=v.name; selector.append(option);
    });
    const add = document.createElement('button');
    add.type='button'; add.className='btn secondary small';
    add.textContent='הוסף משתנה';
    add.addEventListener('click',()=>{
      const variable=variables.find(v=>v.id===selector.value);
      if (!variable) return;
      const label='【'+variablePreviewValue(variable)+'】';
      if (references.has(label) && references.get(label)!==variable.id) {
        notify('לשני משתנים יש אותו ערך מוצג.',true); return;
      }
      const spans=[...input.value.matchAll(/【[^】]*】/g)]
        .map(m=>({start:m.index,end:m.index+m[0].length}));
      if(spans.some(p=>(caretStart>p.start&&caretStart<p.end)||(caretEnd>p.start&&caretEnd<p.end))) {
        notify('יש לבחור משתנה בשלמותו או למקם את הסמן מחוץ לסימון.',true);return;
      }
      references.set(label,variable.id);
      input.setRangeText(label,caretStart,caretEnd,'end');
      trackCaret();
      input.dispatchEvent(new Event('input',{bubbles:true}));
      input.focus();
    });
    variableControls.append(selector,add);
    valueContainer.append(variableControls);
  }
  const updateValueVisibility = () => {
    const visible=kindControl?.value==='value-equals';
    valueContainer?.classList.toggle('hidden',!visible);
    if(input) { input.required=visible; if (!visible) clearInstructorFieldError(input); }
  };
  kindControl?.addEventListener('change',updateValueVisibility);
  updateValueVisibility();
}

let pickingFor = null;
let pickerStarting = false;
// All runtimes share the same authoring lifecycle. Adapters only handle native capture.
function pickerEvent(session, event, message = '') {
  if (event === 'started') {
    pickingFor = session;
  } else if (event === 'selected') {
    session.phase = 'selected';
  } else if (event === 'cancelled' || event === 'failed' || event === 'saved') {
    if (pickingFor === session) pickingFor = null;
    if (event === 'cancelled' || event === 'failed')
      $('primary-target-summary')?.classList.remove('hidden');
  }
  if (!session.completion && !session.variableSource) {
    const emptyMessage = $('empty-primary-target-message');
    if (emptyMessage)
      emptyMessage.classList.toggle('hidden', event === 'selected');
    const pickButton = $('pick-element');
    if (pickButton) pickButton.classList.toggle('hidden', event === 'selected');
  }
  // Once a completion target has been picked, the disabled pick button
  // serves no purpose. Restore it if picking is cancelled or fails.
  if (session.completion) {
    const pickButton = $(session.completionEdit?.identity ? 'add-identity' : 'add-completion');
    if (pickButton) pickButton.classList.toggle('hidden', event === 'selected');
  }
  syncPickerButtons();
  if (message) {
    const box = pickerResultBox(session);
    if (box) box.textContent = message;
  }
}
function syncPickerButtons() {
  const busy = !!pickingFor || pickerStarting;
  for (const id of ['pick-element','replace-web-target','add-completion','add-identity','pick-variable-source']) {
    const button = $(id);
    if (!button) continue;
    button.disabled = busy;
    if (id === 'replace-web-target') button.classList.toggle('hidden', busy);
    if (id === 'add-identity') button.classList.toggle('hidden', busy);
  }
}
// Target lifecycle is shared. Only runtime-specific discovery and transport
// belong to adapters; confirmation, validation editing and persistence stay central.
const targetAdapters = {
  Web: {
    async start(session) {
      const {guideId,contextId} = session;
    const selected = await api('/capture/select', {method:'POST',
      body:JSON.stringify({guideId,contextId})});
    if (selected.status === 'notFound')
      throw new Error('לא נמצאה לשונית מתאימה. פתח את האפליקציה באתר ונסה שוב.');
    let tabId = null;
    if (selected.status === 'multiple') {
      const tabs = selected.tabs || [];
      const choices = tabs.map((t,i) => (i+1)+'. '+(t.title || t.url)).join('\n');
      const answer = prompt('נמצאו כמה לשוניות מתאימות. הקלד את מספר הלשונית:\n'+choices);
      if (answer === null) throw new Error('בחירת הלשונית בוטלה.');
      const i = Number(answer)-1;
      if (!Number.isInteger(i) || !tabs[i]) throw new Error('בחירת לשונית אינה תקינה.');
      tabId = tabs[i].tabId;
      await api('/capture/select',{method:'POST',
        body:JSON.stringify({guideId,contextId,tabId})});
    }
    await api('/target-picker/start',{method:'POST',body:JSON.stringify({guideId,contextId})});
  
    },
    async status() { return api('/capture/status'); },
    async cancel() { return api('/target-picker/cancel', {method:'POST',body:'{}'}); },
    selected(state) { return state.picked; },
    async saveCompletion(session, candidate) {
      return api('/guides/'+session.guideId+'/steps/'+encodeURIComponent(session.stepId)+'/completion-target',
        {method:'POST',body:JSON.stringify({kind:session.completionEdit?.identity ? 'target-appeared' : $('target-condition')?.value,
          stateAttributeIndex:$('target-condition')?.value === 'attribute-equals' && (candidate.stateAttributes || []).length
            ? Number($('completion-state-choice').value) : null,
          selectedTag:candidate.tag,
          expectedValue:$('target-condition')?.value === 'value-contains'
            ? variableEditorResult($('completion-fixed-value')) : null,
          replacing:session.replacing,completionId:session.completionEdit?.completionId,identity:!!session.completionEdit?.identity})});
    }
  },
  Windows: {
    async start(session) {
      const windows = (await api('/discovery')).windows || [];
      const matchers = parseJson(session.context.MatchersJson, []);
      const matches = windows.filter(w => matchers.some(m => {
        const kind = String(m.Kind || '').toLowerCase();
        const value = String(m.Value || '').toLowerCase();
        return kind === 'processname' && value &&
          w.processName.toLowerCase().replace(/\\.exe$/, '') === value.replace(/\\.exe$/, '');
      }));
      if (matches.length !== 1)
        throw new Error(matches.length ? 'נמצאו מספר חלונות תואמים.' : 'לא נמצא חלון מתאים לאפליקציה.');
      session.processId = matches[0].processId;
      const launch = await api('/windows/picker/start',
        {method:'POST',body:JSON.stringify({processId:session.processId})});
      session.activated = launch.activated;
    },
    async status() { return api('/windows/picker/status'); },
    async cancel() { return api('/windows/picker/cancel',{method:'POST',body:'{}'}); },
    selected(state) {
      return {selector:state.selection?.locators?.[0]?.value || '',
        label:state.selection?.name || state.selection?.controlType,
        capturedValue:state.selection?.capturedValue,
        tag:({
          'ControlType.Edit':'input',
          'ControlType.Document':'textarea',
          'ControlType.ComboBox':'select',
          'ControlType.Button':'button',
          'ControlType.Hyperlink':'a'
        })[state.selection?.controlType] || 'div'};
    },
    async saveCompletion(session) {
      return api('/guides/'+session.guideId+'/steps/'+encodeURIComponent(session.stepId)+'/completion-target',
        {method:'POST',body:JSON.stringify({kind:$('target-condition')?.value,
          expectedValue:$('target-condition')?.value === 'value-contains' ? variableEditorResult($('completion-fixed-value')) : null,processId:session.processId,replacing:session.replacing,completionId:session.completionEdit?.completionId,identity:!!session.completionEdit?.identity})});
    },
  }
};
// The picker transport is shared; each authoring purpose owns its preview.
function variableExtractionEditor(candidate, runtime) {
  const samples = runtime === 'Windows'
    ? [{property:'name',value:candidate.label || ''}]
    : (candidate.variableSamples || []);
  const selected = samples.find(sample => sample.value && sample.property === 'value') ||
    samples.find(sample => sample.value);
  const sample = selected?.value || '';
  if (pickingFor?.variableSource) {
    pickingFor.variableSource.property = selected?.property || (runtime === 'Windows' ? 'name' : 'text');
  }
  const number = sample.match(/[0-9]+/)?.[0];
  return '<div class="field"><label for="variable-extract-choice">ערך לקליטה</label>' +
    '<select id="variable-extract-choice" data-sample="' + esc(sample) + '">' +
    '<option value="full">' + esc(sample ? sample + ' — הערך המלא' : 'הערך המלא בזמן ההרצה') + '</option>' +
    (number ? '<option value="number">' + esc(number + ' — המספר מתוך הערך') + '</option>' : '') +
    '<option value="custom">חילוץ מותאם אישית</option></select>' +
    '<p id="variable-extract-preview" aria-live="polite"></p></div>' +
    '<div class="field hidden"><label for="variable-extract-pattern">ביטוי לחילוץ (Regex)</label>' +
    '<input id="variable-extract-pattern" dir="ltr" maxlength="300" placeholder="([0-9]+)" />' +
    '<p>אם הוגדרה קבוצת לכידה, יישמר תוכן הקבוצה הראשונה.</p></div>';
}
function pickerResultBox(session) {
  return $(session.variableSource ? 'variable-pick-result' :
    session.completionEdit?.identity ? 'identity-pick-result' : session.completion ? 'completion-pick-result' : 'pick-result');
}
async function beginTargetPicker(runtime, guideId, stepId, contextId, replacing=false, completion=false, context=null, variableSource=null, completionEdit=null) {
  const adapter = targetAdapters[runtime];
  if (!adapter) throw new Error('סביבת היעד אינה נתמכת.');
  if (completion && !adapter.saveCompletion)
    throw new Error('תנאי השלמה נוסף אינו נתמך כרגע בסביבת יעד זו.');
  if (pickingFor || pickerStarting) return;
  pickerStarting = true;
  syncPickerButtons();
  const session = {runtime,guideId,stepId,contextId,replacing,completion,context,variableSource,completionEdit,startedAt:Date.now()};
  const box = pickerResultBox(session);
  if (!box) {
    pickerStarting = false;
    syncPickerButtons();
    throw new Error('אזור אישור הרכיב חסר בממשק.');
  }
  box.dataset.pickedSelector = '';
  box.classList.remove('hidden');
  box.textContent = 'מפעיל ממפה רכיבים…';
  try {
    await adapter.start(session);
    pickerStarting = false;
    pickerEvent(session, 'started');
    await refreshPicker();
    if (runtime === 'Windows' && session.activated === false)
      notify('Windows לא אישר העברת פוקוס אוטומטית. הממפה פעיל; עבור לחלון האפליקציה כדי לבחור רכיב.', true);
  } catch (error) {
    pickerStarting = false;
    try { await adapter.cancel(); } catch { /* Preserve original failure. */ }
    pickerEvent(session, 'failed', error.message || String(error));
    throw error;
  }
}
async function refreshPicker() {
  if (!pickingFor) return;
  if (current?.guide?.id !== pickingFor.guideId || selectedStep !== pickingFor.stepId) {
    const abandoned = pickingFor;
    await targetAdapters[abandoned.runtime].cancel();
    pickerEvent(abandoned, 'cancelled');
    return;
  }
  const requestSession = pickingFor;
  const adapter = targetAdapters[requestSession.runtime];
  const state = await adapter.status();
  if (!pickingFor || pickingFor !== requestSession ||
      current?.guide?.id !== requestSession.guideId || selectedStep !== requestSession.stepId) return;
  const box = pickerResultBox(requestSession);
  if (!box) return;
  if (!state.picked) {
    if (state.error || state.pickerError) {
      try { await adapter.cancel(); } catch { /* Keep the reported picker error. */ }
      pickerEvent(requestSession, 'failed', 'לא ניתן להפעיל את ממפה הרכיבים: ' + (state.error || state.pickerError));
      return;
    }
    if (!state.picking) {
      pickerEvent(requestSession, 'cancelled', 'איתור הרכיב בוטל. ניתן להפעיל שוב את הממפה.');
      return;
    }
    if (requestSession.runtime === 'Web' && !state.connected) {
      box.textContent = 'אין כרגע תקשורת עם תוסף Instructor. בדוק שהוא פעיל ב־Chrome.';
      return;
    }
    if (requestSession.runtime === 'Web' && !state.pickReady && Date.now() - pickingFor.startedAt >= 5000) {
      box.textContent = 'ממתין לאישור הפעלת הממפה בלשונית שנבחרה. אם לא מופיעה מסגרת בחלון היעד, ניתן לבטל ולנסות שוב.';
      return;
    }
    box.textContent = (requestSession.runtime === 'Windows' || state.pickReady) ? 'ממפה פעיל: העבר עכבר מעל רכיב ולחץ Ctrl+Shift+Space לבחירה (Esc לביטול).' : 'מפעיל בחירת רכיב…';
    return;
  }
  pickerEvent(requestSession, 'selected');
  const candidate = adapter.selected(state);
  if (!requestSession.completion && !requestSession.variableSource)
    $('primary-target-summary')?.classList.add('hidden');
  if (box.dataset.pickedSelector === candidate.selector) return;
  if (requestSession.autoSaving) return;
  box.dataset.pickedSelector = candidate.selector;
  const kindLabel = candidate.tag === 'textarea' ? 'שדה טקסט רב־שורות' :
    candidate.tag === 'input' ? 'שדה קלט' : candidate.tag === 'select' ? 'רשימת בחירה' :
    candidate.tag === 'button' ? 'כפתור' : candidate.tag === 'a' ? 'קישור' : 'רכיב במסך';
  // Prefer the unique text value corresponding to the captured element.
  // The transport still constructs the locator; the editor changes only its value.
  const capturedText = String(candidate.label || '').trim();
  const matchingChoices = requestSession.runtime === 'Web' &&
    !['input','textarea','select'].includes((candidate.tag || '').toLowerCase())
    ? (candidate.gridChoices || []).map((choice, index) => ({choice,index}))
        .filter(({choice}) => choice.unique !== false && capturedText && String(choice.value || '').trim() === capturedText)
    : [];
  const capturedChoice = matchingChoices.length === 1 ? matchingChoices[0] : null;
  const contentMatch = requestSession.runtime === 'Web'
    ? [...(candidate.selector || '').matchAll(/:cell-text\(\s*\d+\s*,\s*("(?:[^"\\]|\\.)*")\s*\)/g)]
    : [];
  const selectorText = contentMatch.length === 1
    ? (() => { try { return JSON.parse(contentMatch[0][1]); } catch { return null; } })()
    : null;
  const observedText = (candidate.variableSamples || [])
    .find(sample => sample.property === 'text' && String(sample.value || '').trim());
  const editableContent = capturedChoice ? String(capturedChoice.choice.value) :
    selectorText !== null ? selectorText :
    requestSession.runtime === 'Web' && !['input','textarea','select'].includes((candidate.tag || '').toLowerCase())
      ? String(observedText?.value || '').trim() || null :
    requestSession.runtime === 'Windows' && String(candidate.label || '').trim()
      ? String(candidate.label).trim() : null;
  // The captured value is the control's actual content, not its label/name.
  const capturedSample = requestSession.runtime === 'Windows'
    ? candidate.capturedValue
    : (candidate.variableSamples || []).find(sample => sample.property === 'value')?.value ??
      (candidate.variableSamples || []).find(sample => sample.property === 'text')?.value ??
      (!['input','textarea','select'].includes((candidate.tag || '').toLowerCase())
        ? candidate.label : null);
  // A stable technical locator is sufficient: never impose the captured label
  // as an extra content constraint. Preserve text only when the locator itself
  // already depends on text (or has no technical identity).
  const hasSemanticContentLocator = requestSession.runtime === 'Web' &&
    (String(candidate.selector || '').includes(':exact-text(') ||
     String(candidate.selector || '').includes(':cell-text('));
  // Content matching is opt-in. Only prefill when the captured selector
  // already contains an explicit semantic text matcher; otherwise a stable
  // structural locator must not acquire a new text constraint.
  const initialContentValue = hasSemanticContentLocator
    ? String(selectorText ?? editableContent ?? '').trim()
    : '';
  box.innerHTML = '<div class="picked-target-fields" dir="rtl">' +
    '<p><strong>'+esc(candidate.label || candidate.tag || 'רכיב')+'</strong> <span class="picked-target-kind">('+esc(kindLabel)+')</span></p>'+
    ((!pickingFor.completion || pickingFor.completionEdit?.identity) && !pickingFor.variableSource ?
      '<div class="field"><label for="captured-object-value">הערך שנקלט</label>' +
      '<textarea id="captured-object-value" rows="2" readonly dir="auto" ' +
      'placeholder="לא נקלט ערך"></textarea></div>' : '') +
    ((!pickingFor.completion || pickingFor.completionEdit?.identity) && !pickingFor.variableSource ?
      variableValueFieldMarkup('picked-content-value', 'זיהוי לפי תוכן') +
      GuideMeAnyValueUI.renderOption('picked-content-any', false) : '') +
    (pickingFor.completion && !pickingFor.completionEdit?.identity ? '<label for="target-condition">חוק השלמה</label>' +
      '<select id="target-condition">' +
      '<option value="target-appeared">הרכיב מופיע במסך</option>' +
      '<option value="value-contains">הרכיב מכיל ערך מסוים</option>' +
'</select>' : '')+
    (pickingFor.completion && !pickingFor.completionEdit?.identity ? '<div id="completion-fixed-value-fields" class="hidden">' +
      variableValueFieldMarkup('completion-fixed-value', 'ערך צפוי', 500) +
      '</div>' : '') +
    (pickingFor.variableSource ? variableExtractionEditor(candidate, requestSession.runtime) : '') +
    '</div>' +
    '<div class="target-actions">' +
    '<button id="save-web-target" class="btn primary small">שמירה</button>' +
    '<button id="cancel-web-target" class="btn secondary small">ביטול</button>' +
    '</div>';
  if ((!pickingFor.completion || pickingFor.completionEdit?.identity) && !pickingFor.variableSource) {
    const capturedField = $('captured-object-value');
    if (capturedField) capturedField.value = String(capturedSample ?? '');
    const input = $('picked-content-value');
    if (input) {
      initializeVariableField(input, $('picked-content-value-variables'), initialContentValue);
      const any = $('picked-content-any');
      if (any) {
        const updateAny = () => {
          input.closest('.field')?.classList.toggle('hidden', any.checked);
          input.disabled = any.checked;
          $('picked-content-value-variables').style.display = any.checked ? 'none' : '';
        };
        any.addEventListener('change', updateAny);
        updateAny();
      }
    }
  }
  if (pickingFor.variableSource) {
    const extract = $('variable-extract-choice');
    const custom = $('variable-extract-pattern')?.closest('.field');
    const updateExtraction = () => {
      if (custom) custom.classList.toggle('hidden',extract?.value !== 'custom');
      const preview = $('variable-extract-preview');
      if (!preview) return;
      const sample = extract?.dataset.sample || '';
      if (extract?.value === 'number') {
        preview.textContent = 'יישמר לדוגמה: ' + (sample.match(/[0-9]+/)?.[0] || 'לא נמצא מספר');
      } else if (extract?.value === 'custom') {
        try {
          const m = sample.match(new RegExp($('variable-extract-pattern')?.value || ''));
          preview.textContent = 'יישמר לדוגמה: ' + (m ? (m[1] ?? m[0]) : 'לא נמצאה התאמה');
        } catch { preview.textContent = 'ביטוי לא תקין'; }
      } else preview.textContent = 'יישמר לדוגמה: ' + sample;
    };
    extract?.addEventListener('change',updateExtraction);
    $('variable-extract-pattern')?.addEventListener('input',updateExtraction);
    updateExtraction();
  }
  if (requestSession.runtime === 'Windows' && state.selection?.error) {
    const warning = document.createElement('p');
    warning.textContent = state.selection.error;
    warning.setAttribute('role','alert');
    box.prepend(warning);
    if ($('save-web-target')) $('save-web-target').disabled = true;
  }
  if (pickingFor.completion && !pickingFor.completionEdit?.identity) {
    const condition = $('target-condition');
    const updateFields = () => {
      $('completion-fixed-value-fields').classList.toggle('hidden', condition.value !== 'value-contains');
      const expected = $('completion-fixed-value');
      if (expected) expected.required = condition.value === 'value-contains';
    };
    condition.addEventListener('change', updateFields);
    if (pickingFor.completionEdit) {
      condition.value = pickingFor.completionEdit.kind;
      const expected = $('completion-fixed-value');
      if (expected) expected.value = pickingFor.completionEdit.expectedValue;
    }
    updateFields();
    initializeVariableField($('completion-fixed-value'), $('completion-fixed-value-variables'));
  }
  const savePickedTarget = async () => {
    const saveButton = $('save-web-target');
    const cancelButton = $('cancel-web-target');
    const pickedFor = pickingFor;
    if (!pickedFor || pickedFor.autoSaving || saveButton?.disabled) return;
    const requiredCaptureFields = [];
    if (pickedFor.variableSource && $('variable-extract-choice')?.value === 'custom')
      requiredCaptureFields.push($('variable-extract-pattern'));
    if (pickedFor.completion && !pickedFor.completionEdit?.identity && $('target-condition')?.value === 'value-contains')
      requiredCaptureFields.push($('completion-fixed-value'));
    for (const field of requiredCaptureFields) {
      if (field && !String(field.value || '').trim()) {
        showInstructorFieldError(field, 'יש למלא שדה זה לפני השמירה.');
        return;
      }
    }
    pickedFor.autoSaving = true;
    if (saveButton) saveButton.disabled = true;
    if (cancelButton) cancelButton.disabled = true;
    const originalLabel = saveButton?.textContent;
    if (saveButton) saveButton.textContent = 'שומר רכיב...';
    let result = $('target-save-feedback');
    if (!result) {
      result = document.createElement('p');
      result.id = 'target-save-feedback';
      result.setAttribute('role', 'status');
      box.appendChild(result);
    }
    result.textContent = '';
    try {
      const gridChoiceIndex = (!pickedFor.completion || pickedFor.completionEdit?.identity) && !pickedFor.variableSource
        ? capturedChoice?.index ?? null : null;
      const adapter = targetAdapters[pickedFor.runtime];
      if (pickedFor.variableSource) {
        const mode = $('variable-extract-choice')?.value || 'full';
        const pattern = mode === 'number' ? '([0-9]+)' :
          mode === 'custom' ? $('variable-extract-pattern')?.value : null;
        await api('/guides/'+pickedFor.guideId+'/steps/'+encodeURIComponent(pickedFor.stepId)+'/variable-source',
          {method:'POST',body:JSON.stringify({
            ...pickedFor.variableSource,pattern,
            processId:pickedFor.processId || null,gridChoice:gridChoiceIndex})});
      }
      else if (pickedFor.completion && !pickedFor.completionEdit?.identity)
        await adapter.saveCompletion(pickedFor, candidate);
      else {
        const contentInput = $('picked-content-value');
        const anyContent = !!$('picked-content-any')?.checked;
        // Empty content means structural identification only; no semantic text constraint.
        const enteredContent = contentInput && !anyContent ? variableEditorResult(contentInput).trim() : '';
        const contentValue = enteredContent || null;
        if (contentInput && contentValue !== null && contentValue.length > 400)
          rejectInstructorField(contentInput, 'תוכן זיהוי הרכיב ארוך מדי (עד 400 תווים).');
        const identity = !!pickedFor.completionEdit?.identity;
        await api('/guides/'+pickedFor.guideId+'/steps/'+encodeURIComponent(pickedFor.stepId)+
          (identity ? '/completion-target' : '/primary-target'),
          {method:'POST',body:JSON.stringify({
            ...(identity ? {kind:'target-appeared',identity:true,completionId:pickedFor.completionEdit?.completionId} : {condition:''}),
            replacing:pickedFor.replacing,gridChoice:gridChoiceIndex,
            contentValue,anyContent,selectedTag:candidate.tag,processId:pickedFor.processId || null})});
      }
      pickerEvent(pickedFor, 'saved');
      result.textContent = 'הרכיב נשמר בהצלחה.';
      await openGuide(pickedFor.guideId);
      const item = [...document.querySelectorAll('[data-step]')]
        .find(x => x.dataset.step === pickedFor.stepId);
      if (item) await item.click();
      notify(pickedFor.variableSource ? 'רכיב מקור המשתנה נשמר ללא שינוי רכיב הפעולה.'
        : pickedFor.completionEdit?.identity ? (pickedFor.replacing ? 'מזהה האימות הוחלף.' : 'מזהה האימות נשמר.')
        : pickedFor.completion && pickedFor.replacing ? 'תנאי ההשלמה עודכן.'
        : pickedFor.replacing
        ? 'הרכיב הוחלף בהצלחה. יש להגדיר מחדש מתי ממשיכים לשלב הבא.'
        : pickedFor.completion ? 'תנאי ההשלמה נשמר.' : 'רכיב היעד נשמר. ניתן להגדיר תנאי התקדמות בנפרד.');
    } catch (error) {
      if (!(error instanceof InstructorFieldValidationError)) {
        result.textContent = 'השמירה נכשלה: ' + (error.message || String(error));
        result.classList.add('save-error');
      }
      pickedFor.autoSaving = false;
      if (saveButton) { saveButton.disabled = false; saveButton.textContent = originalLabel; }
      if (cancelButton) cancelButton.disabled = false;
    }
  };
  $('save-web-target').onclick = savePickedTarget;
  if ($('cancel-web-target')) $('cancel-web-target').onclick = () => guarded(async () => {
    const cancelled = pickingFor;
    await targetAdapters[cancelled.runtime].cancel();
    pickerEvent(cancelled, 'cancelled');
    markClean();
    box.textContent = 'הבחירה בוטלה.';
  });
}
let pickerPolling = false;
setInterval(() => {
  if (!pickingFor || pickerPolling) return;
  pickerPolling = true;
  void guarded(refreshPicker).finally(() => { pickerPolling = false; });
}, 1500);
function appLabel(c) {
  const title = (c.DiscoveredTitle || c.DisplayName || '').trim();
  let technical = c.ContextId;
  try {
    const match = JSON.parse(c.MatchersJson || '[]')[0];
    if (match?.Value) technical = c.Runtime + ' · ' + match.Value;
  } catch {}
  return title ? title + ' — ' + technical : technical;
}
function matcher(c) { try { return JSON.parse(c.MatchersJson || '[]').map(m=>m.Kind+': '+m.Value).join(', '); }catch{return '—';} }
function renderContexts() {
  $('apps-count').textContent = '(' + current.contexts.length + ')';
  $('contexts-list').innerHTML = current.contexts.length ? current.contexts.map((c,i) => {
    const used = current.steps.some(step => current.stepContexts?.[step.id] === c.ContextId);
    const target = matcher(c);
    const title = (c.DiscoveredTitle || c.DisplayName || '').trim();
    return '<div class="context-card"><div>' +
      (title ? '<strong class="context-title">' + esc(title) + '</strong>' : '') +
      '<span class="chip ' + (c.Runtime === 'Windows' ? 'win' : '') + '">' +
      esc(c.Runtime) + '</span><div class="context-meta">' + esc(target) +
      '</div></div><div class="context-actions"><button class="btn danger small" data-context-delete="' + i + '"' +
      (used ? ' disabled title="האפליקציה משויכת לשלבים בלומדה"' : '') +
      '>הסר</button></div></div>';
  }).join('') : '<div class="empty">טרם נוספו אפליקציות ללומדה.</div>';
  document.querySelectorAll('[data-context-delete]').forEach(b =>
    b.onclick = () => guarded(() => deleteContext(current.contexts[Number(b.dataset.contextDelete)])));
  renderGuideVariables();
}
function renderGuideVariables() {
  const list = $('guide-variables-list');
  if (!current || !list) return;
  $('variables-count').textContent = '(' + current.variables.length + ')';
  list.innerHTML = current.variables.length
    ? current.variables.map((variable, index) =>
      '<div class="guide-variable-row"><div><strong>' + esc(variable.name) +
      '</strong><p>' + (variable.initialValue === null ? 'ללא ערך התחלתי' :
      'ערך התחלתי: ' + esc(variable.initialValue)) + '</p></div>' +
      '<div class="context-actions"><button class="btn secondary small" data-variable-edit="' + index + '">עריכה</button>' +
      '<button class="btn danger small" data-variable-delete="' + index + '">הסרה</button></div></div>').join('')
    : '<div class="empty">עדיין לא הוגדרו משתנים בלומדה.</div>';
  list.querySelectorAll('[data-variable-edit]').forEach(button =>
    button.onclick = () => editGuideVariable(current.variables[Number(button.dataset.variableEdit)]));
  list.querySelectorAll('[data-variable-delete]').forEach(button =>
    button.onclick = () => guarded(async () => {
      const variable = current.variables[Number(button.dataset.variableDelete)];
      if (!confirm('למחוק את המשתנה "' + variable.name + '"?')) return;
      await api('/guides/' + current.guide.id + '/variables/' + encodeURIComponent(variable.id),
        {method:'DELETE',body:'{}'});
      current.variables = await api('/guides/' + current.guide.id + '/variables');
      renderGuideVariables();
      refreshStepVariablePreviews();
      notify('המשתנה נמחק.');
    }));
}
let selectedTargetPreviewValue = null;
let selectedValidationExpectedValue = null;
let selectedCompletionDescription = null;
function refreshStepVariablePreviews() {
  if (!current) return;
  // Update the existing list in place so the selected step and scroll position
  // remain intact. Both text and accessible labels use the current variable values.
  document.querySelectorAll('#step-list [data-step]').forEach(row => {
    const step = current.steps.find(s => s.id === row.dataset.step);
    if (!step) return;
    const preview = friendlyInstructionText(step.bubble || 'שלב ' + step.order);
    const label = row.querySelector('.step-body strong');
    if (label) label.innerHTML = stepListInstructionHtml(step.bubble || 'שלב ' + step.order);
    row.setAttribute('aria-label', 'שלב ' + step.order + ': ' + preview +
      (step.enabled ? '' : ', מושבת'));
  });
  // The inspector uses the same current variables; update its instruction
  // without recreating the step editor or discarding the active selection.
  const inspector = document.querySelector('#step-inspector .step-summary-content > p');
  const step = current.steps.find(s => s.id === selectedStep);
  if (inspector && step) inspector.innerHTML =
    friendlyInstructionHtml(step.bubble || 'לא הוגדרה הנחיה');
  // Keep the selected target preview in sync with the same variable updates.
  // Its locator template is retained from the step fetch, not rewritten.
  const targetPreview = document.querySelector('#primary-target-summary .target-business-value-preview');
  if (targetPreview && selectedTargetPreviewValue != null)
    targetPreview.innerHTML = friendlyInstructionHtml(selectedTargetPreviewValue);
  const completionPreview = document.querySelector('.completion-description-preview');
  if (completionPreview && selectedCompletionDescription != null)
    completionPreview.innerHTML = selectedCompletionDescription();
  const validationPreview = document.querySelector('.validation-expected-preview');
  if (validationPreview && selectedValidationExpectedValue != null)
    validationPreview.innerHTML = friendlyInstructionHtml(selectedValidationExpectedValue);
}
function editGuideVariable(variable = null) {
  if (!current) return;
  const guideId = current.guide.id;
  modal(variable ? 'עריכת משתנה' : 'הוספת משתנה', [
    {name:'name',label:'שם המשתנה',value:variable?.name || '',max:80,
     help:'השם מוצג לעורך; מזהה המשתנה נוצר אוטומטית.'},
    {name:'initialValue',label:'ערך התחלתי (לא חובה)',value:variable?.initialValue || '',
     max:1000,optional:true}
  ], async values => {
    const path = '/guides/' + guideId + '/variables' +
      (variable ? '/' + encodeURIComponent(variable.id) : '');
    await api(path, {method:variable?'PUT':'POST',
      body:JSON.stringify({name:values.name,initialValue:values.initialValue || null})});
    current.variables = await api('/guides/' + guideId + '/variables');
    renderGuideVariables();
    refreshStepVariablePreviews();
    notify(variable ? 'המשתנה עודכן.' : 'המשתנה נוסף ללומדה.');
  });
}
$('add-guide-variable').onclick = () => editGuideVariable();
// One validation policy for dialog forms, inline editors and capture confirmations.
function clearInstructorFieldError(field) {
  if (!field) return;
  field.setCustomValidity('');
  field.removeAttribute('aria-invalid');
  field.parentElement?.querySelector('.instructor-field-error')?.remove();
}
function showInstructorFieldError(field, message) {
  if (!field) return false;
  clearInstructorFieldError(field);
  field.setCustomValidity(message);
  field.setAttribute('aria-invalid', 'true');
  const error = document.createElement('p');
  error.className = 'instructor-field-error';
  error.setAttribute('role', 'alert');
  error.textContent = message;
  field.insertAdjacentElement('afterend', error);
  field.focus();
  return false;
}
class InstructorFieldValidationError extends Error {}
function rejectInstructorField(field, message) {
  showInstructorFieldError(field, message);
  throw new InstructorFieldValidationError(message);
}
function validateInstructorSelection(container, selector, message) {
  if (container.querySelector(selector)) {
    container.querySelectorAll('.instructor-selection-error').forEach(el => el.remove());
    return true;
  }
  container.querySelectorAll('.instructor-selection-error').forEach(el => el.remove());
  const error = document.createElement('p');
  error.className = 'instructor-field-error instructor-selection-error';
  error.setAttribute('role', 'alert');
  error.textContent = message;
  container.append(error);
  container.querySelector('input[name="app"]')?.focus();
  return false;
}
function validateInstructorForm(form) {
  for (const field of form.querySelectorAll('input[required], textarea[required], select[required]')) {
    if (field.disabled || field.closest('.hidden,[hidden]')) continue;
    const empty = field.type === 'checkbox' || field.type === 'radio'
      ? ![...form.elements].some(item => item.name === field.name && item.checked)
      : !String(field.value ?? '').trim();
    if (empty) return showInstructorFieldError(field, 'יש למלא שדה זה לפני השמירה.');
    clearInstructorFieldError(field);
  }
  return true;
}
document.addEventListener('input', event => {
  if (event.target?.matches?.('input,textarea,select')) clearInstructorFieldError(event.target);
}, true);
document.addEventListener('change', event => {
  if (event.target?.matches?.('input,textarea,select')) clearInstructorFieldError(event.target);
}, true);
document.addEventListener('submit', event => {
  if (!validateInstructorForm(event.target)) {
    event.preventDefault();
    event.stopImmediatePropagation();
  }
}, true);


function modal(title, fields, submit, buttonText='שמירה') {
  modalReturnFocus = document.activeElement;
  $('dialog-title').textContent=title;
  $('dialog-notice')?.remove();
  $('dialog-fields').innerHTML='<div class="fields">' + fields.map(f=>{
    const label='<label for="field-'+f.name+'">'+esc(f.label)+'</label>';
    const helpId='help-'+f.name;
    const helpAttr=f.help?' aria-describedby="'+helpId+'"':'';
    const requiredAttr=f.optional?'':' required';
    const field=f.choices?'<select id="field-'+f.name+'" name="'+f.name+'"'+helpAttr+requiredAttr+'>'+f.choices.map(v=>'<option value="'+esc(v.value)+'" '+(v.value===f.value?'selected':'')+'>'+esc(v.text)+'</option>').join('')+'</select>'
    :f.multiline?'<textarea id="field-'+f.name+'" name="'+f.name+'"'+helpAttr+requiredAttr+' maxlength="'+(f.max||4000)+'" rows="3">'+esc(f.value||'')+'</textarea>'
    :'<input id="field-'+f.name+'" name="'+f.name+'"'+helpAttr+' value="'+esc(f.value||'')+'" maxlength="'+(f.max||256)+'" '+(f.optional?'':'required')+'>';
    return '<div class="field">'+label+field+(f.help?'<div class="field-help" id="'+helpId+'">'+esc(f.help)+'</div>':'')+'</div>';
  }).join('')+'</div>';
  $('submit-dialog').textContent=buttonText;
  const form=$('editor-form');
  form.noValidate = true;
  form.onsubmit=e=>{
    e.preventDefault();
    $('dialog-notice')?.remove();
    if (!validateInstructorForm(form)) return;
    const values=Object.fromEntries(new FormData(form).entries());
    guarded(async()=>{try { await submit(values); $('editor-dialog').close(); }
      catch (error) { if (!(error instanceof InstructorFieldValidationError)) throw error; }
    });
  };
  const baseline = new URLSearchParams(new FormData(form)).toString();
  const onChange = () => {
    if (new URLSearchParams(new FormData(form)).toString() !== baseline) markDirty();
    else markClean();
  };
  form.oninput = onChange;
  form.onchange = onChange;
  $('editor-dialog').showModal();
  form.querySelector('input,textarea,select')?.focus();
}
$('editor-dialog').addEventListener('close', () => {
  if (modalReturnFocus?.isConnected && !modalReturnFocus.closest('[hidden],.hidden')) modalReturnFocus.focus();
  modalReturnFocus = null;
  if (pendingChanges) markClean();
});
async function discoverApplications() {
  const discovery = await api('/discovery');
  const unique = new Map();
  for (const tab of (discovery.web || [])) {
    try {
      const url = new URL(tab.url);
      if (url.protocol !== 'http:' && url.protocol !== 'https:') continue;
      if (url.host === location.host) continue;
      const key = 'Web:' + url.host;
      if (!unique.has(key))
        unique.set(key, {runtime:'Web', title:tab.title || url.host,
          matcherProperty:'UrlHost', matcherValue:url.host});
    } catch {}
  }
  for (const win of (discovery.windows || [])) {
    if (!win.processName) continue;
    const key = 'Windows:' + win.processName.toLowerCase();
    if (!unique.has(key))
      unique.set(key, {runtime:'Windows',title:win.title || win.processName,
        matcherProperty:'ProcessName',matcherValue:win.processName});
  }
  const apps = [...unique.values()];
  return { discovery, apps };
}
function applicationChoices(apps, selectedValues = []) {
  return apps.length ? '<div class="application-picker">' + apps.map((app, i) =>
    '<label class="application-option"><input type="checkbox" name="app" value="' + i + '"' +
    (selectedValues.includes(i) ? ' checked' : '') + '>' +
    '<span><strong>' + esc(app.title) + '</strong><small>' +
    esc(app.runtime + ' · ' + app.matcherValue) + '</small></span></label>').join('') + '</div>' :
    '<p>לא אותרו אפליקציות. פתח אתר ב־Chrome או תוכנת Windows ולאחר מכן לחץ רענון.</p>';
}
async function newGuide() {
  const { discovery, apps } = await discoverApplications();
  $('dialog-title').textContent = 'יצירת לומדה חדשה';
  $('submit-dialog').textContent = 'יצירת לומדה';
  $('dialog-fields').innerHTML =
    '<div class="fields"><div class="field"><label for="guide-new-name">שם הלומדה</label>' +
    '<input id="guide-new-name" name="name" maxlength="256" required autocomplete="off"></div>' +
    '<div class="field"><label>אפליקציות משתתפות</label>' +
    '<p>בחר אפליקציה אחת או יותר מבין האפליקציות הפתוחות. הזיהוי יישמר אוטומטית.</p>' +
    applicationChoices(apps) +
    '<button type="button" class="btn secondary small" id="refresh-applications">רענון אפליקציות</button>' +
    (!discovery.webConnected ? '<p>תוסף Instructor אינו מחובר — רשימת אתרי Web אינה זמינה.</p>' : '') +
    '</div></div>';
  $('editor-form').noValidate = true;
  $('refresh-applications').onclick = () => guarded(newGuide);
  $('editor-form').onsubmit = event => {
    event.preventDefault();
    if (!validateInstructorForm(event.currentTarget)) return;
    guarded(async () => {
      const name = $('guide-new-name').value.trim();
      const selected = [...document.querySelectorAll('input[name="app"]:checked')]
        .map(el => apps[Number(el.value)]);
      if (!validateInstructorSelection($('dialog-fields'), 'input[name="app"]:checked', 'יש לבחור לפחות אפליקציה אחת.')) return;
      const applications = selected.map((app, index) => ({
        contextId:'app-' + (index + 1), runtime:app.runtime,
        matcherProperty:app.matcherProperty,matcherValue:app.matcherValue
      }));
      const guide = await api('/guides', {method:'POST',
        body:JSON.stringify({name,applications})});
      $('editor-dialog').close();
      await loadGuides();
      await openGuide(guide.id);
      notify('הלומדה נוצרה עם ' + applications.length + ' אפליקציות.');
    });
  };
  if (!$('editor-dialog').open) $('editor-dialog').showModal();
  $('guide-new-name').focus();
}
function renameGuide() {
  modal('שינוי שם הלומדה',[{name:'name',label:'שם הלומדה',value:current.guide.name}],async v=>{
    await api('/guides/'+current.guide.id+'/name',{method:'PUT',body:JSON.stringify({name:v.name})});
    const id=current.guide.id;await loadGuides();await openGuide(id);notify('שם הלומדה עודכן.');
  });
}
function deleteGuide() {
  if (!current) return;
  const guide = { ...current.guide };
  // Typing the exact name is required before the destructive request is sent.
  modal('מחיקת לומדה', [
    {name:'name', label:'הקלד את שם הלומדה לאישור מחיקה', value:'', max:256,
      help:'פעולה בלתי הפיכה: הלומדה וכל שלביה, רכיביה וכללי ההתקדמות שלה יימחקו. שם לאישור: ' + guide.name}
  ], async values => {
    if (values.name !== guide.name)
      throw new Error('שם הלומדה אינו תואם. המחיקה לא בוצעה.');
    await api('/guides/' + guide.id, {
      method:'DELETE', body:JSON.stringify({name: values.name})
    });
    showList();
    await loadGuides();
    notify('הלומדה "' + guide.name + '" נמחקה.');
  }, 'מחיקה לצמיתות');
}
async function chooseContextApplication() {
  const guideId = current.guide.id;
  const { discovery, apps: discovered } = await discoverApplications();
  const apps = discovered.filter(app =>
    !current.contexts.some(c => {
      let matcher; try { matcher = JSON.parse(c.MatchersJson || '[]')[0]; } catch {}
      return c.Runtime === app.runtime && matcher?.Value?.toLowerCase() === app.matcherValue.toLowerCase();
    }));
  $('dialog-title').textContent = 'הוספת אפליקציה';
  $('submit-dialog').textContent = 'הוספת אפליקציה';
  $('dialog-fields').innerHTML =
    '<div class="fields"><div class="field"><label>בחר אפליקציה פתוחה</label>' +
    '<p>הזיהוי יישמר אוטומטית. אין צורך להזין פרטים טכניים.</p>' +
    applicationChoices(apps) +
    '<button type="button" class="btn secondary small" id="refresh-applications">רענון אפליקציות</button>' +
    (!discovery.webConnected ? '<p>תוסף Instructor אינו מחובר — רשימת אתרי Web אינה זמינה.</p>' : '') +
    '</div></div>';
  $('editor-form').noValidate = true;
  $('refresh-applications').onclick = () => guarded(() => chooseContextApplication());
  $('editor-form').onsubmit = event => {
    event.preventDefault();
    guarded(async () => {
      const indexes = [...document.querySelectorAll('input[name="app"]:checked')].map(el => Number(el.value));
      if (!validateInstructorSelection($('dialog-fields'), 'input[name="app"]:checked', 'יש לבחור אפליקציה אחת.')) return;
      const app = apps[indexes[0]];
      if (!app) throw new Error('האפליקציה שנבחרה אינה זמינה.');
      const used = new Set(current.contexts.map(c => c.ContextId));
      let index = 1;
      while (used.has('app-' + index)) index++;
      const contextId = 'app-' + index;
      await api('/guides/' + guideId + '/contexts', {
        method:'POST', body:JSON.stringify({
          contextId, runtime:app.runtime,
          matcherProperty:app.matcherProperty, matcherValue:app.matcherValue
        })
      });
      $('editor-dialog').close();
      await openGuide(guideId);
      selectTab('contexts');
      notify('האפליקציה נוספה.');
    });
  };
  if (!$('editor-dialog').open) $('editor-dialog').showModal();
}
function addContext() { return guarded(() => chooseContextApplication()); }
function addContext() { return guarded(() => chooseContextApplication()); }
async function deleteContext(c) {
  if (current.steps.some(step => current.stepContexts?.[step.id] === c.ContextId))
    throw new Error('האפליקציה משויכת לשלבים בלומדה.');
  await api('/guides/'+current.guide.id+'/contexts/'+encodeURIComponent(c.ContextId),{method:'DELETE',body:'{}'});
  const id=current.guide.id;await openGuide(id);selectTab('contexts');notify('האפליקציה הוסרה.');
}
$('guide-search').addEventListener('input',renderGuides);
$('step-search').addEventListener('input',()=>{if(current)renderSteps()});
$('new-guide').onclick=()=>guarded(newGuide);
// The Learner extension must acknowledge a real Native Host ping before
// launching GuideMe.exe. A missing content script means Instructor needs a
// reload after the extension was installed or updated.
async function verifyTestTransport(guideId, startStepId = null) {
  const query = startStepId ? '?startStepId=' + encodeURIComponent(startStepId) : '';
  const requirements = await api('/guides/' + guideId + '/test-requirements' + query);
  if (requirements.requiresChromeExtension) await verifyLearnerTransport();
}
function verifyLearnerTransport() {
  return new Promise((resolve, reject) => {
    const requestId = crypto.randomUUID();
    let settled = false;
    const finish = error => {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      document.removeEventListener('guideme:instructor-test-pong', onPong);
      if (error) reject(new Error(error)); else resolve();
    };
    const onPong = event => {
      if (event.detail?.requestId !== requestId) return;
      finish(event.detail.ok ? null :
        'תוסף ה־Learner לא הצליח לתקשר עם Native Host: ' + (event.detail.error || 'שגיאה לא ידועה'));
    };
    document.addEventListener('guideme:instructor-test-pong', onPong);
    const timeout = setTimeout(() => finish(
      'לא התקבל אישור מתוסף ה־Learner. ודא שהתוסף GuideMe פעיל ב־Chrome ורענן את חלון ה־Instructor (Ctrl+R) לאחר עדכון התוסף.'
    ), 6000);
    document.dispatchEvent(new CustomEvent('guideme:instructor-test-ping', { detail: { requestId } }));
  });
}
$('test-guide').onclick=()=>guarded(async()=>{
  if (!current || !confirmDiscard()) return;
  const button=$('test-guide');
  button.disabled=true;
  try {
    await verifyTestTransport(current.guide.id);
    await api('/guides/'+current.guide.id+'/test',{method:'POST',body:'{}'});
    notify('ה־Learner הופעל לבדיקת הלומדה. יש להכין את אפליקציית היעד.', false, 'info');
  } finally { button.disabled=false; }
});
$('rename-guide').onclick=renameGuide;
$('delete-guide').onclick=deleteGuide;
$('add-context').onclick=addContext;
$('new-step').onclick=newStep;
$('back').onclick=showList;
$('close-dialog').onclick=$('cancel-dialog').onclick=()=>{
  if (confirmDiscard()) $('editor-dialog').close();
};
$('editor-dialog').addEventListener('cancel', e => {
  if (!confirmDiscard()) e.preventDefault();
});
document.querySelectorAll('.tab').forEach(b=>b.onclick=()=>selectTab(b.dataset.tab));
function newStep() {
  if (!current) return;
  const guideId = current.guide.id;
  const contexts = current.contexts;
  if (!contexts.length) {
    selectTab('contexts');
    notify('יש להוסיף אפליקציה ללומדה לפני יצירת שלב.', true);
    return;
  }
  const last = current.steps[current.steps.length - 1];
  const previousContext = last ? (current.stepContexts?.[last.id] || null) : null;
  const defaultContext = contexts.find(c => c.ContextId === previousContext)?.ContextId ||
    contexts[0].ContextId;
  modal('הוספת שלב', [
    {name:'contextId',label:'אפליקציית יעד',value:defaultContext,
     choices:contexts.map(c => ({value:c.ContextId,text:appLabel(c)}))},
    {name:'bubble',label:'מה להציג ללומד?',value:'',multiline:true,max:4000,
     help:'לאחר יצירת השלב ניתן לבחור רכיב יעד ולהגדיר תנאי התקדמות.'}
  ],async v=>{
    const created=await api('/guides/' + guideId + '/steps', {
      method:'POST',body:JSON.stringify({contextId:v.contextId || defaultContext,
        bubble:variableEditorResult($('field-bubble')),placement:"Center"})
    });
    await openGuide(guideId);
    const row=[...document.querySelectorAll('[data-step]')]
       .find(x=>x.dataset.step===created.stepId);
    if (row) {
      row.click();
      // Scroll only the steps list; do not move the entire page.
      const list = $('step-list');
      const rowBounds = row.getBoundingClientRect();
      const listBounds = list.getBoundingClientRect();
      if (rowBounds.bottom > listBounds.bottom)
        list.scrollTop += rowBounds.bottom - listBounds.bottom;
      else if (rowBounds.top < listBounds.top)
        list.scrollTop -= listBounds.top - rowBounds.top;
    }
    notify('השלב נוצר. כרגע התקדמותו ידנית עד שיוגדרו רכיב יעד ותנאי התקדמות.');
  },'יצירת שלב');
  const input = $('field-bubble');
  const container = document.createElement('div');
  input?.closest('.field')?.append(container);
  setupVariableInsertion(input, container, current.variables || []);
}
function editStepVariableAction(step, context) {
  if (!current?.variables?.length || !context) return;
  const guideId = current.guide.id, stepId = step.id;
  const choices = current.variables.map(v => ({value:v.id,text:v.name}));
  modal('הוספת פעולה למשתנה — שלב ' + step.order, [
    {name:'variableId',label:'משתנה לעדכון',value:choices[0].value,choices}
  ], async values => {
    await beginTargetPicker(context.Runtime, guideId, stepId, context.ContextId,
      false, false, context, {variableId:values.variableId});
  },'המשך');
}
// Render variable references as friendly, non-editable labels in Instructor previews.
function variablePreviewValue(variable) {
  const value = variable?.initialValue;
  return value == null || String(value).trim() === '' ? variable?.name || 'משתנה לא קיים' : String(value);
}
function friendlyInstructionText(value) {
  return String(value || '').replace(/\{\{var:([^}]+)\}\}/g, (_, id) => {
    const variable = (current?.variables || []).find(v => v.id === id);
    return '[' + (variable?.name || 'משתנה לא קיים') + ']';
  });
}
function stepListInstructionHtml(value) {
  // Explicit newlines alone create rows; each row truncates independently.
  return String(value || '').split(/\r?\n/).slice(0, 2)
    .map(line => '<span class="step-instruction-line">' + friendlyInstructionHtml(line) + '</span>')
    .join('');
}
function friendlyInstructionHtml(value) {
  // Authoring screens display the variable identity, not its sample/initial value.
  // Escaping each segment independently prevents variable names from injecting HTML.
  return String(value || '').split(/(\{\{var:[^{}]+\}\})/g).map(part => {
    const match = /^\{\{var:([^{}]+)\}\}$/.exec(part);
    if (!match) return esc(part);
    const variable = (current?.variables || []).find(v => v.id === match[1]);
    return '<span class="instruction-variable" title="משתנה בלומדה">' +
      esc(variable?.name || 'משתנה לא קיים') + '</span>';
  }).join('');
}
// The inspector and editor share one value extraction rule; the CSS selector
// remains a private implementation detail.
// Only a semantic text value may be edited; never expose or modify the CSS structure.
// Single runtime-neutral classification for Inspector target-value editing.
// Extend these categories here when additional UI control types are introduced.
// Runtime adapters only identify the control. Inspector policy is shared.
function classifyWebTarget(element, locators) {
  const tag = String(element?.ControlType || '').toLowerCase();
  const selector = String(locators?.[0]?.Value || '');
  const hasTag = name => new RegExp('(?:^|[\\s>+~])' + name +
    '(?=[:.#\\[\\s>+~]|$)', 'i').test(selector);
  if (tag === 'textarea' || hasTag('textarea'))
    return {label:'שדה טקסט רב־שורות', dataEntry:true};
  if (tag === 'select' || hasTag('select'))
    return {label:'רשימת בחירה', dataEntry:true};
  if (tag === 'input' || hasTag('input')) {
    const buttonInput = /\[type\s*=\s*["']?(?:button|submit|reset|image)["']?\s*\]/i.test(selector);
    return buttonInput ? {label:'כפתור', dataEntry:false}
      : {label:'שדה קלט', dataEntry:true};
  }
  const labels = {button:'כפתור',a:'קישור'};
  for (const [name,label] of Object.entries(labels))
    if (tag === name || hasTag(name)) return {label,dataEntry:false};
  return {label:null,dataEntry:false};
}
function classifyWindowsTarget(element) {
  const type = String(element?.ControlType || '').toLowerCase();
  const types = {
    'controltype.edit':['שדה קלט',true],
    'controltype.document':['שדה טקסט רב־שורות',true],
    'controltype.combobox':['רשימת בחירה',true],
    'controltype.button':['כפתור',false],
    'controltype.hyperlink':['קישור',false],
    'controltype.text':['טקסט',false],
    'controltype.dataitem':['פריט נתונים',false],
    'controltype.listitem':['פריט ברשימה',false]
  };
  const match = types[type];
  return match ? {label:match[0],dataEntry:match[1]}
    : {label:null,dataEntry:false};
}
function classifyTarget(runtime, element, locators) {
  return runtime === 'Windows'
    ? classifyWindowsTarget(element)
    : classifyWebTarget(element, locators);
}

function locatorTextBinding(locator) {
  if (!locator?.Value) return null;
  const original = locator.Value;
  // Windows UIA Name is the semantic value used to locate the control.
  // AutomationId is a technical identifier and must not be edited as text.
  if (locator.Strategy === 'name')
    return {value:original, build:next=>next, buildAny:()=>'*', anyStrategy:'name-nonempty'};
  if (locator.Strategy === 'name-nonempty')
    return {value:'', any:true, build:next=>next, buildAny:()=>'*', strategy:'name', anyStrategy:'name-nonempty'};
  if (locator.Strategy === 'automation-id-nonempty')
    return {value:original, any:true, build:next=>original, buildAny:()=>original,
      strategy:'automation-id', anyStrategy:'automation-id-nonempty'};
  if (locator.Strategy !== 'css') return null;
  const anyCell = original.match(/:cell-any\((\d+)\)$/);
  if (anyCell) return {value:'', any:true, build:next=>original.replace(/:cell-any\(\d+\)$/, ':cell-text(' + anyCell[1] + ',' + JSON.stringify(next) + ')'), buildAny:()=>original};
  if (original.endsWith(':nonempty-text')) return {value:'', any:true, build:next=>original.replace(/:nonempty-text$/, ':exact-text(' + JSON.stringify(next) + ')'), buildAny:()=>original};
  const matchers = [...original.matchAll(/:(?:cell-text\(\s*\d+\s*,\s*|exact-text\(\s*)("(?:[^"\\]|\\.)*")\s*\)/g)];
  if (matchers.length === 1) {
    const match = matchers[0], quoted = match[1];
    let value;
    try { value = JSON.parse(quoted); } catch { return null; }
    const offset = match.index + match[0].indexOf(quoted);
    return {value, build:next=>original.slice(0, offset) + JSON.stringify(next) +
      original.slice(offset + quoted.length), buildAny:()=>original.replace(match[0], match[0].startsWith(':cell-text') ? ':cell-any(' + (/\d+/.exec(match[0])?.[0] || '1') + ')' : '')};
  }
  if (matchers.length || /:cell-text\(|:exact-text\(/.test(original)) return null;
  // name and data-* attributes identify the element technically; they are not visible content.
  const attributes = [...original.matchAll(/\[(?:aria-label|placeholder|title)\s*=\s*("(?:[^"\\]|\\.)*")\]/g)];
  if (attributes.length === 1) {
    const match = attributes[0], quoted = match[1];
    try {
      const value = JSON.parse(quoted);
      const offset = match.index + match[0].indexOf(quoted);
      return {value, build:next=>original.slice(0,offset) + JSON.stringify(next) +
        original.slice(offset + quoted.length)};
    } catch { return null; }
  }
  // A display label alone does not prove that the locator uses text for identification.
  return null;
}
// Both the action target and identity guards use the same semantic editor.
function editableLocatorBinding(runtime, element, locators) {
  const locator = (locators || [])[0];
  const binding = locatorTextBinding(locator);
  if (binding) return binding;
  if (runtime === 'Web' && locator?.Strategy === 'css' && locator.Value &&
      !/:exact-text\(|:cell-text\(|:cell-any\(|:nonempty-text/.test(locator.Value))
    return {value:'', build:next=>locator.Value + ':exact-text(' + JSON.stringify(next) + ')',
      buildAny:()=>locator.Value + ':nonempty-text', strategy:'css'};
  if (runtime === 'Windows' && locator?.Strategy === 'automation-id' &&
      element?.DisplayName &&
      ['ControlType.Button','ControlType.Hyperlink','ControlType.Text','ControlType.DataItem',
       'ControlType.ListItem','ControlType.ComboBox'].includes(element.ControlType))
    return {value:element.DisplayName, build:next=>next, strategy:'name',
      buildAny:()=>locator.Value, anyStrategy:'automation-id-nonempty'};
  return null;
}
// A single presentation model for action targets and identity qualifiers.
// Role affects only the badge and the available role-switch action.
function screenElementPresentation(runtime, element, locators) {
  const locator = (locators || [])[0];
  const selector = String(locator?.Value || '');
  const classification = classifyTarget(runtime, element, locators);
  const binding = editableLocatorBinding(runtime, element, locators);
  const heading = /(?:^|[ >+~])h([1-6])(?=[:.#\[\s>+~]|$)/i.exec(selector);
  const anyValue = (runtime === 'Web' && /:(?:cell-any\(\d+\)|nonempty-text)$/.test(selector)) ||
    (runtime === 'Windows' && ['name-nonempty','automation-id-nonempty'].includes(locator?.Strategy));
  return {
    element, locators, binding,
    name:element?.DisplayName || (selector === 'a' ? 'קישור' : selector || 'הרכיב שנבחר'),
    type:classification.label || (heading ? 'כותרת (H' + heading[1] + ')' :
      /(?:^|[ >+~])(?:td|th)(?=[:.#\[\s>+~]|$)/i.test(selector) ? 'תא בטבלה' :
      /:cell-(?:text|any)\(/.test(selector) ? 'שורה בטבלה' :
      binding?.value ? 'רכיב טקסט' : 'רכיב במסך'),
    content:anyValue ? 'כל ערך' : binding?.value || null
  };
}
const screenElementDisplayOrder = new Map();
function renderScreenElementDetails(item) {
  return '<p class="target-name-line"><strong>' + esc(item.name) + '</strong></p>' +
    '<p class="field-help">סוג רכיב: ' + esc(item.type) + '</p>' +
    (item.content ? '<p>זיהוי לפי תוכן: <strong class="target-business-value-preview">' +
      friendlyInstructionHtml(item.content) + '</strong></p>' : '');
}
function renderScreenElementActions(isActionTarget, index) {
  const attribute = isActionTarget ? '' : ' data-index="' + index + '"';
  const button = (action, title, css = 'secondary') =>
    '<button type="button" class="btn ' + css + ' small" ' +
    (isActionTarget ? 'id="' + action + '"' : 'data-identity-action="' + action + '"' + attribute) +
    '>' + title + '</button>';
  const roleButton = isActionTarget
    ? '<button type="button" class="btn secondary small screen-element-role active" aria-label="יעד פעולה נוכחי" aria-pressed="true" disabled>יעד פעולה</button>'
    : '<button type="button" class="btn secondary small screen-element-role" data-identity-action="set-action" data-index="' + index +
      '" aria-label="יעד פעולה" aria-pressed="false">יעד פעולה</button>';
  return '<div class="target-action-buttons"><div class="target-action-primary">' +
    button(isActionTarget ? 'replace-web-target' : 'replace', 'בחירת רכיב אחר') +
    button(isActionTarget ? 'bind-target-variable' : 'edit', 'עריכה') +
    button(isActionTarget ? 'remove-primary-target' : 'remove', 'הסרה', 'danger') +
    roleButton + '</div></div>';
}
async function editPrimaryTargetValue({kind, step, locators, binding:preparedBinding, identityUsageId = null}) {
  if (!current || kind !== 'target') return;
  const locator = (locators || [])[0];
  if (!locator?.Value) {
    notify('לא נמצא מזהה רכיב שניתן לערוך.', true);
    return;
  }
  const binding = preparedBinding || locatorTextBinding(locator);
  if (!binding) {
    notify('לרכיב זה אין ערך זיהוי טקסטואלי שניתן לערוך. ניתן לבחור רכיב אחר.', true);
    return;
  }
  // Resolve variables from the guide, regardless of which element role is edited.
  current.variables = await api('/guides/' + current.guide.id + '/variables');
  const supportsAny = !!binding.buildAny || !!binding.any;
  modal('עריכת רכיב במסך — שלב ' + step.order, [
    {name:'locatorValue', label:'ערך לזיהוי הרכיב', value:binding.value,
      multiline:true, max:400, optional:!!binding.any}
  ], async values => {
    const input = $('field-locatorValue');
    const anySelected = !!$('edit-primary-target-any')?.checked;
    const value = anySelected ? '' : variableEditorResult(input);
    if (!anySelected && !value.trim())
      rejectInstructorField(input, 'יש להזין ערך לזיהוי הרכיב.');
    const template = anySelected ? binding.buildAny() : binding.build(value);
    const nextStrategy = anySelected ? (binding.anyStrategy || binding.strategy || null) :
      (locator.Strategy === 'name-nonempty' ? 'name' : binding.strategy || null);
    if (template.length > 400)
      rejectInstructorField(input, 'מזהה הרכיב ארוך מדי.');
    if (template === locator.Value && (!nextStrategy || nextStrategy === locator.Strategy))
      return;
    const path = identityUsageId
      ? '/completion-target/variable?completionId='+encodeURIComponent(identityUsageId)
      : '/primary-target/variable';
    await api('/guides/'+current.guide.id+'/steps/'+encodeURIComponent(step.id)+path, {
      method:'POST', body:JSON.stringify({locatorValue:template, locatorStrategy:nextStrategy})
    });
    await reloadSelectedStep(current.guide.id, step.id);
    notify('ערך הרכיב עודכן.');
  });
  const input = $('field-locatorValue');
  const field = input.closest('.field');
  const variableControls = document.createElement('div');
  field.append(variableControls);
  initializeVariableField(input, variableControls, binding.value);
  if (supportsAny) {
    const option = document.createElement('div');
    option.innerHTML = GuideMeAnyValueUI.renderOption('edit-primary-target-any', !!binding.any);
    field.append(option);
    const checkbox = $('edit-primary-target-any');
    const refresh = () => {
      input.disabled = checkbox.checked;
      input.required = !checkbox.checked;
      variableControls.classList.remove('hidden');
      variableControls.querySelectorAll('select,button').forEach(control => {
        control.disabled = checkbox.checked;
      });
    };
    checkbox.addEventListener('change', refresh);
    refresh();
  }
}
// One insertion editor for every variable-aware field. Persistence is supplied
// by the caller; cursor handling, token rendering and validation are shared.
async function reloadSelectedStep(guideId, stepId) {
  const row = [...document.querySelectorAll('#step-list [data-step]')]
    .find(b => b.dataset.step === stepId);
  if (!row) return;
  const scrollPositions = [];
  for (let node = document.getElementById('step-inspector'); node; node = node.parentElement)
    scrollPositions.push([node, node.scrollTop, node.scrollLeft]);
  const windowPosition = [window.scrollX, window.scrollY];
  const rendered = new Promise(resolve => {
    const onRender = event => {
      if (event.detail?.stepId !== stepId) return;
      document.removeEventListener('guideme:step-rendered', onRender);
      resolve();
    };
    document.addEventListener('guideme:step-rendered', onRender);
  });
  row.click();
  await rendered;
  for (const [node, top, left] of scrollPositions) {
    if (node.isConnected) { node.scrollTop = top; node.scrollLeft = left; }
  }
  window.scrollTo(...windowPosition);
}
// Shared token insertion for inline expected-value fields.
function variableEditorResult(input) {
  if (!input) return '';
  let value = input.value;
  const references = input._variableReferences || new Map();
  for (const label of [...references.keys()].sort((a,b)=>b.length-a.length))
    value = value.split(label).join('{{var:' + references.get(label) + '}}');
  if (/【[^】]*】/.test(value)) throw new Error('סימון משתנה לא מוכר.');
  return value;
}
// Shared authoring field for both target identification and completion conditions.
// It uses the same variable references and the same serialization on save.
// The same compact value editor is used by both target selection and completion.
function variableValueFieldMarkup(id, label, maxLength = 400) {
  return '<div class="field"><label for="' + esc(id) + '">' + esc(label) + '</label>' +
    '<textarea id="' + esc(id) + '" rows="1" maxlength="' + maxLength + '" dir="rtl"></textarea>' +
    '<div id="' + esc(id + '-variables') + '"></div></div>';
}
function initializeVariableField(input, controls, template = '') {
  if (!input) return;
  const variables = current?.variables || [];
  const refs = new Map();
  input.value = String(template).replace(/\{\{var:([^}]+)\}\}/g, (token, id) => {
    const variable = variables.find(v => v.id === id);
    if (!variable) return token;
    const label = '【' + variablePreviewValue(variable) + '】';
    if (refs.has(label) && refs.get(label) !== id)
      throw new Error('לשני משתנים יש אותו ערך מוצג. יש להבחין בין ערכיהם לפני העריכה.');
    refs.set(label, id);
    return label;
  });
  setupVariableInsertion(input, controls, variables);
  if (input._variableReferences) {
    for (const [label, id] of refs) input._variableReferences.set(label, id);
  }
}
function setupVariableInsertion(input, container, variables) {
  if (!input || !container || !variables.length) return;
  const references = new Map();
  input._variableReferences = references;
  const select = document.createElement('select');
  select.setAttribute('aria-label','משתנה');
  select.style.width = '100%';
  variables.forEach(v => {
    const option=document.createElement('option');
    option.value=v.id; option.textContent=v.name; select.append(option);
  });
  const add=document.createElement('button');
  add.type='button'; add.className='btn secondary small';
  add.textContent='הוסף משתנה';
  const controls=document.createElement('div');
  controls.style.display='grid'; controls.style.gap='8px';
  controls.style.marginTop='12px'; controls.style.justifyItems='start';
  controls.append(select,add); container.append(controls);
  let start=input.selectionStart, end=input.selectionEnd;
  const track=()=>{start=input.selectionStart;end=input.selectionEnd;};
  ['select','click','keyup'].forEach(e=>input.addEventListener(e,track));
  add.onclick=()=>{
    const variable=variables.find(v=>v.id===select.value);
    if(!variable)return;
    const label='【'+variablePreviewValue(variable)+'】';
    if(references.has(label)&&references.get(label)!==variable.id) {
      notify('לשני משתנים יש אותו ערך מוצג.',true);return;
    }
    const spans=[...input.value.matchAll(/【[^】]*】/g)].map(m=>({start:m.index,end:m.index+m[0].length}));
    if(spans.some(p=>(start>p.start&&start<p.end)||(end>p.start&&end<p.end))) {
      notify('יש לבחור משתנה בשלמותו או למקם את הסמן מחוץ לסימון.',true);return;
    }
    references.set(label,variable.id);
    input.setRangeText(label,start,end,'end');
    track();input.dispatchEvent(new Event('input',{bubbles:true}));input.focus();
  };
}
function openVariableTextEditor({title, original, variables, maxLength, saveLabel, save, fieldLabel = 'טקסט', description = ''}) {
  const references = new Map();
  const display = String(original).replace(/\{\{var:([^}]+)\}\}/g, (token, id) => {
    const variable = variables.find(v => v.id === id);
    const label = '【' + variablePreviewValue(variable) + '】';
    if (references.has(label) && references.get(label) !== id)
      throw new Error('למשתנים שונים יש אותו ערך מוצג. יש להגדיר ערכים שונים לפני העריכה.');
    references.set(label, id);
    return label;
  });
  modal(title, [
    {name:'sourceText', label:fieldLabel, value:display, max:maxLength, multiline:true,
     help:'מקם את הסמן בטקסט ולחץ על הוסף משתנה. אפשר גם להחליף טקסט מסומן.'},
    {name:'variableId', label:'משתנה', value:variables[0].id,
     choices:variables.map(v => ({value:v.id, text:v.name}))}
  ], async values => {
    let result = values.sourceText;
    for (const label of [...references.keys()].sort((a,b) => b.length-a.length))
      result = result.split(label).join('{{var:' + references.get(label) + '}}');
    if (/【[^】]*】/.test(result))
      rejectInstructorField($('field-sourceText'), 'סימון משתנה לא מוכר. יש להוסיף משתנים באמצעות הכפתור.');
    if (result.length > maxLength)
      rejectInstructorField($('field-sourceText'), 'הטקסט ארוך מדי.');
    await save(result);
  }, saveLabel);
  if (description) {
    const textareaField = $('field-sourceText').closest('.field');
    const detail = document.createElement('p');
    detail.textContent = description;
    detail.className = 'target-field-label';
    detail.setAttribute('dir', 'rtl');
    textareaField.prepend(detail);
  }
  const textarea = $('field-sourceText');
  const selector = $('field-variableId');
  const add = document.createElement('button');
  add.type = 'button';
  add.className = 'btn secondary small';
  add.style.marginTop = '8px';
  add.textContent = 'הוסף משתנה';
  selector.closest('.field').append(add);
  let caretStart = textarea.selectionStart, caretEnd = textarea.selectionEnd;
  const trackCaret = () => {
    caretStart = textarea.selectionStart; caretEnd = textarea.selectionEnd;
  };
  for (const event of ['select','keyup','click']) textarea.addEventListener(event, trackCaret);
  add.addEventListener('click', () => {
    const variable = variables.find(v => v.id === selector.value);
    if (!variable) return;
    const label = '【' + variablePreviewValue(variable) + '】';
    if (references.has(label) && references.get(label) !== variable.id) {
      notify('לשני משתנים יש אותו ערך מוצג. יש להגדיר ערכים שונים לפני השילוב.', true);
      return;
    }
    const start = caretStart, end = caretEnd;
    const spans = [...textarea.value.matchAll(/【[^】]*】/g)]
      .map(m => ({start:m.index, end:m.index+m[0].length}));
    if (spans.some(p => (start > p.start && start < p.end) ||
      (end > p.start && end < p.end))) {
      notify('יש לבחור את המשתנה בשלמותו או למקם את הסמן מחוץ לסימון.', true);
      return;
    }
    references.set(label, variable.id);
    textarea.setRangeText(label, start, end, 'end');
    trackCaret();
    textarea.dispatchEvent(new Event('input', {bubbles:true}));
    textarea.focus();
  });
}
// Edit-friendly placeholders preserve stable variable IDs on save.
function editStep(step, assignedContextId) {
  if (!current || !step) return;
  const guideId = current.guide.id;
  const stepId = step.id;
  const contexts = current.contexts;
  const selectedContext = contexts.some(c => c.ContextId === assignedContextId)
    ? assignedContextId : contexts[0]?.ContextId;
  modal('עריכת שלב ' + step.order, [
    {name:'contextId',label:'אפליקציית יעד',value:selectedContext,
     choices:contexts.map(c => ({value:c.ContextId,text:appLabel(c)}))},
    {name:'bubble', label:'טקסט הבועית', value:String(step.bubble || '').replace(/\{\{var:([^}]+)\}\}/g, (_, id) => {
       const variable=(current.variables || []).find(v=>v.id===id);
       return variable ? '【'+variablePreviewValue(variable)+'】' : '{{var:'+id+'}}';
     }), max:4000, multiline:true,
     help:'השינוי ישפיע על תוכן הבועית בלבד. רכיבי יעד וכללי התקדמות אינם משתנים.'}
  ], async values => {
    await api('/guides/' + guideId + '/steps/' + encodeURIComponent(stepId), {
      method:'PUT', body:JSON.stringify({bubble:variableEditorResult($('field-bubble')), enabled:step.enabled,
        contextId:values.contextId || selectedContext || null})
    });
    await openGuide(guideId);
    const row = [...document.querySelectorAll('[data-step]')].find(b=>b.dataset.step===stepId);
    if (row) row.click();
    notify('פרטי השלב נשמרו.');
  });
  const input = $('field-bubble');
  const container = document.createElement('div');
  input?.closest('.field')?.append(container);
  setupVariableInsertion(input, container, current.variables || []);
  if (input?._variableReferences) {
    for (const match of String(step.bubble || '').matchAll(/\{\{var:([^}]+)\}\}/g)) {
      const variable=(current.variables || []).find(v=>v.id===match[1]);
      if (variable) input._variableReferences.set('【'+variablePreviewValue(variable)+'】', variable.id);
    }
  }
}
// Connection is automatic. No standalone pairing or recording controls.
async function refreshExtensionStatus() {
  try {
    const state = await api('/capture/status');
    $('capture-status').textContent = state.connected ? 'תוסף מחובר' : 'תוסף לא מחובר';
    $('capture-status').classList.toggle('disconnected', !state.connected);
  } catch {
    $('capture-status').textContent = 'התוסף אינו זמין';
    $('capture-status').classList.add('disconnected');
  }
}
void refreshExtensionStatus();
setInterval(() => { if (!document.hidden) void refreshExtensionStatus(); }, 3000);
guarded(loadGuides);
