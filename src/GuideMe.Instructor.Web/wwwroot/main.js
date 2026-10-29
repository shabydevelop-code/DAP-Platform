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
  current = null; selectedStep = null;
  $('list-view').classList.remove('hidden'); $('detail-view').classList.add('hidden');
  $('new-guide').classList.remove('hidden');
  clearNotice();
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
  const g = current.guide;
  $('list-view').classList.add('hidden'); $('detail-view').classList.remove('hidden');
  $('new-guide').classList.add('hidden');
  $('detail-name').textContent = g.name; $('detail-description').textContent = 'לומדה #' + g.id + ' · ' + current.steps.length + ' שלבים';
  $('steps-count').textContent = current.steps.length;
  selectTab('steps'); renderSteps(); renderContexts(); clearNotice();
  $('detail-name').focus();
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
  selectedStep = null; $('step-inspector').innerHTML = '<div class="empty-inspector">בחר שלב להצגת הפרטים שלו</div>';
  const search = $('step-search').value.trim().toLowerCase();
  const steps = (current?.steps || []).filter(s => String(s.order).includes(search) || s.id.toLowerCase().includes(search) || (s.bubble || '').toLowerCase().includes(search));
  requestAnimationFrame(syncStepListHeight);
  $('step-list').innerHTML = steps.length ? steps.map(s =>
    '<div class="step-row' + (s.enabled ? '' : ' step-disabled') + '" role="button" tabindex="0" aria-pressed="false" aria-label="' + esc('שלב ' + s.order + ': ' + (s.bubble || 'שלב ' + s.order) + (s.enabled ? '' : ', מושבת')) + '" data-step="' + esc(s.id) + '"><span class="step-num">' + s.order + '</span><span class="step-body"><strong>' +
    esc(s.bubble || 'שלב ' + s.order) + '</strong></span>' + (s.enabled ? '' : '<span class="step-disabled-badge">מושבת</span>') + '</div>').join('')
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
    if (name.scrollWidth <= name.clientWidth + 1 &&
        name.scrollHeight <= name.clientHeight + 1) return;
    activeTipRow = row;
    tip.textContent = name.textContent;
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
  $('step-list').addEventListener('scroll', hideTip, {passive:true});
  document.querySelectorAll('[data-step]').forEach(b => b.addEventListener('keydown', e => {
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); b.click(); }
  }));
  document.querySelectorAll('[data-step]').forEach(b => b.addEventListener('click',() => guarded(async () => {
    if (!confirmDiscard()) return;
    selectedStep = b.dataset.step;
    document.querySelectorAll('[data-step]').forEach(x => { x.classList.toggle('active', x===b); x.setAttribute('aria-pressed', String(x===b)); });
    const data = await api('/guides/' + current.guide.id + '/step/' + encodeURIComponent(selectedStep));
    const variableActions = await api('/guides/' + current.guide.id + '/steps/' + encodeURIComponent(selectedStep) + '/variables');
    current.stepContexts ??= {};
    current.stepContexts[selectedStep] = data.step?.[0]?.ApplicationContextId || null;
    const step = current.steps.find(x=>x.id===selectedStep);
    const contextId = data.step?.[0]?.ApplicationContextId;
    const context = current.contexts.find(c => c.ContextId === contextId);
    const target = data.targets.find(x => x.Purpose === 'Primary');
    const element = (data.elements || []).find(x => x.ElementId === target?.ElementId);
    const rule = data.rules.find(x => x.RuleType === 'PrimaryValidation');
    const locators = parseJson(element?.LocatorsJson, []);
    const frames = parseJson(element?.FrameContextJson, []);
    const identifierBinding = (data.anchors || []).find(a =>
      a.ElementId === target?.ElementId && a.Relation === 'SharedContainer');
    const identifierElement = (data.elements || []).find(e =>
      e.ElementId === identifierBinding?.AnchorElementId);
    const identifierLocator = parseJson(identifierElement?.LocatorsJson, [])[0]?.Value || '';
    const boundVariableId = identifierLocator.match(/\{\{var:([^}]+)\}\}/)?.[1];
    const boundVariable = (current.variables || []).find(v => v.id === boundVariableId);
    const conditionText = rule ? validationLabel(rule.Kind) : 'לא הוגדר תנאי';
    const completionRule = data.rules.find(x => x.RuleType === 'AdditionalCompletionCondition');
    const completionTarget = data.targets.find(x => x.UsageId === completionRule?.TargetUsageId);
    const completionElement = data.elements.find(x => x.ElementId === completionTarget?.ElementId);
    const completionSelector = parseJson(completionElement?.LocatorsJson, [])[0]?.Value || '';
    const completionVariableId = completionSelector.match(/\{\{var:([^}]+)\}\}/)?.[1];
    const completionVariable = (current.variables || []).find(v => v.id === completionVariableId);
    const completionVariableName = completionVariable?.name || 'המשתנה שנבחר';
    const completionDisplayName = (completionElement?.DisplayName || '').trim();
    const completionElementName = completionDisplayName &&
      !/^[#.[>:+~]/.test(completionDisplayName) &&
      !/^[a-z][\\w-]*\\[[^\\]]+\\]/i.test(completionDisplayName)
      ? completionDisplayName
      : /^h[1-6](?=:|$)/i.test(completionSelector) ? 'כותרת במסך'
      : 'הרכיב שנבחר';
    const completionAttribute = completionRule?.Kind === 'attribute-equals'
      ? parseJson(completionRule.ExpectedValue, {}) : {};
    const completionDescription = completionVariableId
      ? 'הטקסט ברכיב "' + completionElementName + '" מכיל את ערך המשתנה "' + completionVariableName + '".'
      : completionRule?.Kind === 'target-appeared'
        ? 'הרכיב "' + completionElementName + '" יופיע במסך.'
      : completionRule?.Kind === 'attribute-equals'
        ? 'המאפיין "' + (completionAttribute.name || 'הנבחר') + '" של הרכיב "' +
          completionElementName + '" יהיה שווה ל־"' + (completionAttribute.value ?? '') + '".'
      : 'הרכיב "' + completionElementName + '" ייעלם מהמסך.';
    const locatorText = locators[0]?.Value || '';
    const friendlyName = element?.DisplayName || (locatorText === 'a' ? 'קישור' : locatorText);
    const appText = context ? appLabel(context) : 'לא משויך';

    $('step-inspector').innerHTML =
      '<div class="step-editor-head"><div><h2>שלב ' + step.order + '</h2>' +
      '<p>אפליקציה: ' + esc(appText) + '</p></div>' +
      '<div class="step-editor-actions">' +
      '<button class="btn secondary small" id="duplicate-step">שכפל שלב</button>' +
      '<button class="btn danger small" id="delete-step">מחיקת שלב</button>' +
      '<button class="btn primary small" id="test-from-step"' + (step.enabled ? '' : ' disabled') + '>בדיקה משלב זה</button></div></div>' +
      '<div class="step-summary"><div class="step-summary-heading"><strong>הנחיה למשתמש</strong>' +
       '<span class="chip">' + (step.enabled ? 'פעיל' : 'מושבת') + '</span></div>' +
       '<div class="step-summary-content"><p>' + esc(step.bubble || 'לא הוגדרה הנחיה') + '</p>' +
       '<button class="btn secondary small" id="edit-step">עריכת תוכן השלב</button></div></div>' +
       '<div class="step-friendly-grid">' +
      '<section class="step-friendly-card"><h3>רכיב במסך</h3>' +
      '<div id="primary-target-summary">' +
      (target ? '<p>נבחר רכיב באפליקציה' +
        (frames.length ? ' בתוך ' + frames.length + ' מסגרות' : '') + '.</p>' +
        (locatorText ? '<p>רכיב שנבחר: <strong>' + esc(friendlyName) + '</strong></p>' : '') :
        '<p>עדיין לא נבחר רכיב לשלב הזה.</p>') +
      '</div>' +
      ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') ?
        (target ? '<div class="target-action-buttons">' +
          '<div class="target-action-primary">' +
          '<button class="btn secondary small" id="replace-web-target">בחירת רכיב אחר</button>' +
          '<button class="btn danger small" id="remove-primary-target">הסרת רכיב</button>' +
          '</div>' +
          ((current.variables || []).length && context?.Runtime === 'Web' ?
            '<div class="target-action-identifier">' +
            (identifierBinding ? '<p>רכיב זיהוי מקושר למשתנה: <strong>' + esc(boundVariable?.name || 'משתנה') + '</strong></p>' : '') +
            '<button class="btn secondary small" id="identify-action">' +
            (identifierBinding ? 'בחירת רכיב זיהוי אחר' : 'בחירת רכיב זיהוי לפי משתנה') + '</button>' +
            '</div>' : '') +
          '</div>' :
          '<button id="pick-element" class="btn primary">הפעלת ממפה רכיבים</button>') +
        '<div id="pick-result" class="hidden" aria-live="polite"></div>' : '') +
      '</section>' +
      '<section class="step-friendly-card"><h3>מתי ממשיכים לשלב הבא?</h3>' +
      '<p>' + esc(!target ? 'יוגדר לאחר בחירת הרכיב.' : conditionText) + '</p>' +
      (rule?.ExpectedValue ? '<p>ערך צפוי: <strong>' + esc(rule.ExpectedValue) + '</strong></p>' : '') +
      ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') && target ?
        '<button class="btn secondary small" id="edit-validation">' +
        (rule ? 'עריכת תנאי ההתקדמות' : 'הגדרת תנאי התקדמות') + '</button>' : '') +
      '</section></div>' +
      (context?.Runtime === 'Web' && target && rule ?
        '<section class="step-friendly-card completion-card"><h3>תנאי השלמה נוסף</h3>' +
        (completionRule ? '<div class="completion-details">' +
          '<div class="completion-detail"><span class="completion-detail-label">השלב יושלם כאשר:</span>' +
          '<strong>' + esc(completionDescription) + '</strong></div>' +
          '</div><button class="btn secondary small" id="remove-completion">הסרת תנאי</button>' :
          '<p>בחר רכיב ובדוק הופעה, היעלמות או שינוי מאפיין בעקבות הפעולה.</p>' +
          '<button class="btn secondary small" id="add-completion">בחירת רכיב לתנאי השלמה</button>') +
        '<div id="completion-pick-result" class="hidden" aria-live="polite"></div>' +
        '</section>' : '') +
      '<section class="step-friendly-card completion-card" aria-labelledby="step-variables-title">' +
      '<h3 id="step-variables-title">משתנים</h3>' +
      (variableActions.length ? variableActions.map(action => {
        const variable = (current.variables || []).find(v => v.id === action.variableId);
        const source = (current.variables || []).find(v => v.id === action.sourceVariableId);
        const read = action.kind === 'ReadFromTarget';
        const usage = read ? data.targets.find(t => t.UsageId === action.targetUsageId) : null;
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
        const description = read
          ? '<div class="variable-action-details"><div>מקור: <strong>טקסט הרכיב שנבחר</strong></div>' +
            '<div>חילוץ: <strong>' + esc(extraction) + '</strong></div>' +
            (sourceElement?.DisplayName
              ? '<div>דוגמה מהבחירה: <strong>' + esc(sourceName) + '</strong>' +
                (sampleMatch !== null ? ' ← <strong>' + esc(sampleMatch) + '</strong>' : '') + '</div>'
              : '') +
            '</div>'
          : '<div class="variable-action-details">' + esc(
              action.kind === 'SetValue' ? 'ערך קבוע' :
              action.kind === 'Clear' ? 'איפוס המשתנה' :
              'העתקה מהמשתנה ' + (source?.name || 'שנבחר')) + '</div>';
        return '<div class="guide-variable-row"><div><strong>' + esc(variable?.name || action.variableId) +
          '</strong>' + description +
          '</div><span class="context-actions">' +
          '<button class="btn danger small" data-remove-variable-action="' + esc(action.id) + '">הסרה</button></span></div>';
      }).join('') : '<p>לא הוגדרו פעולות משתנים לשלב.</p>') +
      '<button class="btn secondary small" id="add-variable-action"' +
      ((current.variables || []).length ? '' : ' disabled') + '>הוספת פעולה למשתנה</button>' +
      '<div id="variable-pick-result" class="hidden" aria-live="polite"></div></section>';
    $('add-variable-action').onclick = () => editStepVariableAction(step, context);
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
    const addCompletion = $('add-completion');
    if (addCompletion) addCompletion.onclick = () => guarded(() =>
      beginTargetPicker('Web', current.guide.id, step.id, contextId, false, true, context));
    const removeCompletion = $('remove-completion');
    if (removeCompletion) removeCompletion.onclick = () => guarded(async () => {
      if (!confirm('להסיר את תנאי ההשלמה הנוסף?')) return;
      await api('/guides/'+current.guide.id+'/steps/'+encodeURIComponent(step.id)+'/completion-target',
        {method:'DELETE',body:'{}'});
      const guideId=current.guide.id;
      await openGuide(guideId);
      const item=[...document.querySelectorAll('[data-step]')].find(x=>x.dataset.step===step.id);
      if(item) item.click();
      notify('תנאי ההשלמה הוסר.');
    });
    const startElementPicker = (replacing = false, completion = false) =>
      beginTargetPicker(context.Runtime, current.guide.id, step.id, contextId, replacing, completion, context);
    const targetReplace = $('replace-web-target');
    if (targetReplace) targetReplace.onclick = () => guarded(() => startElementPicker(true));
    const removePrimary = $('remove-primary-target');
    if (removePrimary) removePrimary.onclick = () => guarded(async () => {
      if (!confirm('להסיר את הרכיב מהשלב? תנאי האימות ותנאי ההשלמה התלויים בו יוסרו גם הם.')) return;
      await api('/guides/' + current.guide.id + '/steps/' +
        encodeURIComponent(step.id) + '/primary-target', {method:'DELETE',body:'{}'});
      const guideId = current.guide.id;
      await openGuide(guideId);
      const item = [...document.querySelectorAll('[data-step]')]
        .find(x => x.dataset.step === step.id);
      if (item) item.click();
      notify('הרכיב הוסר מהשלב.');
    });
    const identifyAction = $('identify-action');
    if (identifyAction) identifyAction.onclick = () => {
      const variables = current.variables || [];
      const start = variableId => guarded(() =>
        beginTargetPicker('Web', current.guide.id, step.id, contextId,
          false, false, context, null, {variableId}));
      if (variables.length === 1) start(variables[0].id);
      else modal('בחירת משתנה לזיהוי', [
        {name:'variableId',label:'משתנה',value:variables[0].id,
          choices:variables.map(v => ({value:v.id,text:v.name}))}
      ], values => start(values.variableId));
    };
    const conditionEditor = $('edit-validation');
    if (conditionEditor) conditionEditor.onclick = () => editValidation(step, rule);
    const startButton = $('pick-element');
    if (startButton) startButton.onclick = () => guarded(() => startElementPicker());

  })));
}
function parseJson(value, fallback) {
  try { return value ? JSON.parse(value) : fallback; } catch { return fallback; }
}
const validationChoices = [
  {value:'clicked',text:'לחיצה על הרכיב'},
  {value:'value-not-empty',text:'הוזן ערך בשדה'},
  {value:'value-equals',text:'השדה מכיל ערך מסוים'},
  {value:'target-disappeared',text:'הרכיב נעלם מהמסך'}
];
function validationLabel(kind) {
  return validationChoices.find(x=>x.value===kind)?.text ||
    'תנאי מתקדם שהוגדר במערכת';
}
function editValidation(step, rule) {
  const kind = validationChoices.some(x=>x.value===rule?.Kind) ? rule.Kind : 'clicked';
  modal('עריכת תנאי התקדמות — שלב ' + step.order, [
    {name:'kind',label:'מתי המערכת תעבור לשלב הבא?',value:kind,choices:validationChoices},
    {name:'expectedValue',label:'ערך צפוי (רק עבור ״השדה מכיל ערך מסוים״)',
      value:rule?.ExpectedValue || '',max:500,optional:true,
      help:'הזנת ערך נדרשת רק כאשר בוחרים התאמה לערך מסוים. אין להשתמש כאן בסיסמאות או במידע אישי.'}
  ],async values=>{
    await api('/guides/'+current.guide.id+'/steps/'+encodeURIComponent(step.id)+'/validation',
      {method:'PUT',body:JSON.stringify({kind:values.kind,
        expectedValue:values.kind === 'value-equals' ? values.expectedValue : null})});
    const guideId=current.guide.id, stepId=step.id;
    await openGuide(guideId);
    document.querySelectorAll('[data-step]').forEach(b=>{
      if(b.dataset.step===stepId)b.click();
    });
    notify('תנאי ההתקדמות עודכן.');
  });
  const kindControl = $('field-kind');
  const valueContainer = $('field-expectedValue')?.closest('.field');
  const updateValueVisibility = () => {
    if (valueContainer) valueContainer.classList.toggle('hidden',kindControl?.value !== 'value-equals');
    const valueInput = $('field-expectedValue');
    if (valueInput) valueInput.required = kindControl?.value === 'value-equals';
  };
  kindControl?.addEventListener('change', updateValueVisibility);
  updateValueVisibility();
}

let pickingFor = null;
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
        {method:'POST',body:JSON.stringify({kind:$('target-condition').value,
          stateAttributeIndex:$('target-condition').value === 'attribute-equals' && (candidate.stateAttributes || []).length
            ? Number($('completion-state-choice').value) : null,
          variableId:$('target-condition').value === 'target-appeared-variable'
            ? $('completion-variable-choice')?.value : null,
          selectedTag:candidate.tag})});
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
        tag:state.selection?.controlType === 'ControlType.Edit' ? 'input' : 'button'};
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
    session.completion ? 'completion-pick-result' : 'pick-result');
}
async function beginTargetPicker(runtime, guideId, stepId, contextId, replacing=false, completion=false, context=null, variableSource=null, identifier=null) {
  const adapter = targetAdapters[runtime];
  if (!adapter) throw new Error('סביבת היעד אינה נתמכת.');
  if (completion && !adapter.saveCompletion)
    throw new Error('תנאי השלמה נוסף אינו נתמך כרגע בסביבת יעד זו.');
  if (pickingFor) {
    await targetAdapters[pickingFor.runtime].cancel();
    pickingFor = null;
  }
  const session = {runtime,guideId,stepId,contextId,replacing,completion,context,variableSource,identifier,startedAt:Date.now()};
  const box = pickerResultBox(session);
  if (!box) throw new Error('אזור אישור הרכיב חסר בממשק.');
  box.dataset.pickedSelector = '';
  box.classList.remove('hidden');
  box.textContent = 'מפעיל ממפה רכיבים…';
  try {
    await adapter.start(session);
    pickingFor = session;
    await refreshPicker();
    if (runtime === 'Windows' && session.activated === false)
      notify('Windows לא אישר העברת פוקוס אוטומטית. הממפה פעיל; עבור לחלון האפליקציה כדי לבחור רכיב.', true);
  } catch (error) {
    box.textContent = error.message || String(error);
    throw error;
  }
}
async function refreshPicker() {
  if (!pickingFor) return;
  if (current?.guide?.id !== pickingFor.guideId || selectedStep !== pickingFor.stepId) {
    pickingFor = null;
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
      box.textContent = 'לא ניתן להפעיל את ממפה הרכיבים: ' + (state.error || state.pickerError);
      return;
    }
    if (!state.picking) {
      pickingFor = null;
      markClean();
      box.textContent = 'איתור הרכיב בוטל. ניתן להפעיל שוב את הממפה.';
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
  const candidate = adapter.selected(state);
  if (!requestSession.completion && !requestSession.variableSource && !requestSession.identifier)
    $('primary-target-summary')?.classList.add('hidden');
  if (box.dataset.pickedSelector === candidate.selector) return;
  box.dataset.pickedSelector = candidate.selector;
  const kindLabel = candidate.tag === 'textarea' ? 'שדה טקסט רב־שורות' :
    candidate.tag === 'input' ? 'שדה קלט' : candidate.tag === 'select' ? 'רשימת בחירה' :
    candidate.tag === 'button' ? 'כפתור' : candidate.tag === 'a' ? 'קישור' : 'רכיב במסך';
  box.innerHTML = '<div class="picked-target-fields"><p><strong>'+esc(candidate.label || candidate.tag || 'רכיב')+'</strong></p>'+
    '<p class="picked-target-kind">'+esc(kindLabel)+'</p>'+
    (!pickingFor.completion && !pickingFor.identifier &&
     !['button','a','input','select','textarea'].includes(candidate.tag) &&
     candidate.gridChoices?.length ?
      '<label for="grid-row-choice">זיהוי שורה לפי תוכן בעמודה</label>'+
      '<select id="grid-row-choice"><option value="">בחירת הרכיב הרגילה (לפי מיקום)</option>'+
      candidate.gridChoices.map((choice,i)=>'<option value="'+i+'">'+esc(choice.header)+': '+esc(choice.value)+'</option>').join('')+
      '</select><p>בחירה לפי ערך תישאר תקפה גם לאחר מיון הטבלה. אם הערך אינו ייחודי, הוא לא יוצע.</p>' : '')+
    (pickingFor.completion ? '<label for="target-condition">חוק השלמה</label>' +
      '<select id="target-condition"><option value="target-appeared">הופיע</option>' +
      '<option value="target-appeared-variable">מכיל ערך של משתנה</option>' +
      '<option value="target-vanished">נעלם</option></select>' : '')+
    (pickingFor.completion ? '<div id="completion-variable-fields" class="hidden">' +
      '<label for="completion-variable-choice">משתנה להשוואה</label>' +
      '<select id="completion-variable-choice">' +
      (current?.variables || []).map(v => '<option value="' + esc(v.id) + '">' + esc(v.name) + '</option>').join('') +
      '</select><p>ההשלמה תתבצע רק כאשר הרכיב שנבחר מכיל את ערך המשתנה.</p></div>' : '')+
    (pickingFor.variableSource ? variableExtractionEditor(candidate, requestSession.runtime) : '') +
    '</div><div class="target-actions">'+
    '<button id="save-web-target" class="btn primary">' + (pickingFor.identifier ? 'שמירת רכיב הזיהוי' : 'אישור ושמירת רכיב') + '</button>'+
    '<button id="cancel-web-target" class="btn secondary">ביטול</button></div>';
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
    $('save-web-target').disabled = true;
  }
  if (pickingFor.completion) {
    const condition = $('target-condition');
    const updateFields = () => {
      $('completion-variable-fields').classList.toggle('hidden',
        condition.value !== 'target-appeared-variable');
    };
    condition.addEventListener('change', updateFields);
    updateFields();
  }
  $('save-web-target').onclick = async () => {
    const saveButton = $('save-web-target');
    const cancelButton = $('cancel-web-target');
    const pickedFor = pickingFor;
    if (!pickedFor || saveButton.disabled) return;
    saveButton.disabled = true;
    cancelButton.disabled = true;
    const originalLabel = saveButton.textContent;
    saveButton.textContent = 'שומר רכיב...';
    let result = $('target-save-feedback');
    if (!result) {
      result = document.createElement('p');
      result.id = 'target-save-feedback';
      result.setAttribute('role', 'status');
      box.appendChild(result);
    }
    result.textContent = '';
    try {
      const gridChoice = $('grid-row-choice')?.value;
      const gridChoiceIndex = gridChoice === undefined || gridChoice === '' ? null : Number(gridChoice);
      const adapter = targetAdapters[pickedFor.runtime];
      if (pickedFor.identifier) {
        await api('/guides/'+pickedFor.guideId+'/steps/'+encodeURIComponent(pickedFor.stepId)+'/identified-action',
          {method:'POST',body:JSON.stringify({variableId:pickedFor.identifier.variableId})});
      }
      else if (pickedFor.variableSource) {
        const mode = $('variable-extract-choice')?.value || 'full';
        const pattern = mode === 'number' ? '([0-9]+)' :
          mode === 'custom' ? $('variable-extract-pattern')?.value : null;
        if (mode === 'custom' && !pattern?.trim())
          throw new Error('יש להזין ביטוי לחילוץ מותאם אישית.');
        await api('/guides/'+pickedFor.guideId+'/steps/'+encodeURIComponent(pickedFor.stepId)+'/variable-source',
          {method:'POST',body:JSON.stringify({
            ...pickedFor.variableSource,pattern,
            processId:pickedFor.processId || null,gridChoice:gridChoiceIndex})});
      }
      else if (pickedFor.completion)
        await adapter.saveCompletion(pickedFor, candidate);
      else
        await api('/guides/'+pickedFor.guideId+'/steps/'+encodeURIComponent(pickedFor.stepId)+'/primary-target',
          {method:'POST',body:JSON.stringify({
            condition:'',
            replacing:pickedFor.replacing,gridChoice:gridChoiceIndex,
            processId:pickedFor.processId || null})});
      pickingFor = null;
      result.textContent = 'הרכיב נשמר בהצלחה.';
      await openGuide(pickedFor.guideId);
      const item = [...document.querySelectorAll('[data-step]')]
        .find(x => x.dataset.step === pickedFor.stepId);
      if (item) await item.click();
      notify(pickedFor.identifier ? 'רכיב הזיהוי קושר למשתנה ולרכיב הפעולה.' :
        pickedFor.variableSource ? 'רכיב מקור המשתנה נשמר ללא שינוי רכיב הפעולה.'
        : pickedFor.replacing
        ? 'הרכיב הוחלף בהצלחה. תנאי ההתקדמות נשאר ללא שינוי.'
        : pickedFor.completion ? 'תנאי ההשלמה נשמר.' : 'רכיב היעד נשמר. ניתן להגדיר תנאי התקדמות בנפרד.');
    } catch (error) {
      result.textContent = 'השמירה נכשלה: ' + (error.message || String(error));
      result.classList.add('save-error');
      saveButton.disabled = false;
      cancelButton.disabled = false;
      saveButton.textContent = originalLabel;
    }
  };
  $('cancel-web-target').onclick = () => guarded(async () => {
    await targetAdapters[pickingFor.runtime].cancel();
    pickingFor = null;
    $('primary-target-summary')?.classList.remove('hidden');
    markClean();
    box.textContent = 'הבחירה בוטלה.';
  });
}
setInterval(() => { if (pickingFor) void guarded(refreshPicker); }, 1500);
function appLabel(c) { if(c.DisplayName) return c.DisplayName; try { const m=JSON.parse(c.MatchersJson || '[]')[0]; return m?.Value ? c.Runtime + ' · ' + m.Value : c.ContextId; } catch { return c.ContextId; } }
function matcher(c) { try { return JSON.parse(c.MatchersJson || '[]').map(m=>m.Kind+': '+m.Value).join(', '); }catch{return '—';} }
function renderContexts() {
  $('apps-count').textContent = '(' + current.contexts.length + ')';
  $('contexts-list').innerHTML = current.contexts.length ? current.contexts.map((c,i) =>
    '<div class="context-card"><div><span class="chip ' + (c.Runtime === 'Windows'?'win':'') + '">' + esc(c.Runtime) + '</span> <span class="context-name">' + esc(appLabel(c)) + '</span><div class="context-meta">' + esc(matcher(c)) + '</div></div><div class="context-actions"><button class="btn secondary small" data-context-rename="' + i + '">שינוי שם</button><button class="btn secondary small" data-context-edit="' + i + '">החלפת יעד</button><button class="btn danger small" data-context-delete="' + i + '">הסרה</button></div></div>').join('')
    : '<div class="empty">טרם נוספו אפליקציות ללומדה.</div>';
  document.querySelectorAll('[data-context-rename]').forEach(b=>b.onclick=()=>renameContext(current.contexts[Number(b.dataset.contextRename)]));
  document.querySelectorAll('[data-context-edit]').forEach(b=>b.onclick=()=>editContext(current.contexts[Number(b.dataset.contextEdit)]));
  document.querySelectorAll('[data-context-delete]').forEach(b=>b.onclick=()=>guarded(()=>deleteContext(current.contexts[Number(b.dataset.contextDelete)])));
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
      notify('המשתנה נמחק.');
    }));
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
    notify(variable ? 'המשתנה עודכן.' : 'המשתנה נוסף ללומדה.');
  });
}
$('add-guide-variable').onclick = () => editGuideVariable();
function modal(title, fields, submit, buttonText='שמירה') {
  modalReturnFocus = document.activeElement;
  $('dialog-title').textContent=title;
  $('dialog-fields').innerHTML='<div class="fields">' + fields.map(f=>{
    const label='<label for="field-'+f.name+'">'+esc(f.label)+'</label>';
    const helpId='help-'+f.name;
    const helpAttr=f.help?' aria-describedby="'+helpId+'"':'';
    const field=f.choices?'<select id="field-'+f.name+'" name="'+f.name+'"'+helpAttr+'>'+f.choices.map(v=>'<option value="'+esc(v.value)+'" '+(v.value===f.value?'selected':'')+'>'+esc(v.text)+'</option>').join('')+'</select>'
    :f.multiline?'<textarea id="field-'+f.name+'" name="'+f.name+'"'+helpAttr+' maxlength="'+(f.max||4000)+'" rows="3">'+esc(f.value||'')+'</textarea>'
    :'<input id="field-'+f.name+'" name="'+f.name+'"'+helpAttr+' value="'+esc(f.value||'')+'" maxlength="'+(f.max||256)+'" '+(f.optional?'':'required')+'>';
    return '<div class="field">'+label+field+(f.help?'<div class="field-help" id="'+helpId+'">'+esc(f.help)+'</div>':'')+'</div>';
  }).join('')+'</div>';
  $('submit-dialog').textContent=buttonText;
  const form=$('editor-form');
  form.onsubmit=e=>{e.preventDefault();const values=Object.fromEntries(new FormData(form).entries());guarded(async()=>{await submit(values);$('editor-dialog').close();});};
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
  $('refresh-applications').onclick = () => guarded(newGuide);
  $('editor-form').onsubmit = event => {
    event.preventDefault();
    guarded(async () => {
      const name = $('guide-new-name').value.trim();
      const selected = [...document.querySelectorAll('input[name="app"]:checked')]
        .map(el => apps[Number(el.value)]);
      if (!selected.length) throw new Error('יש לבחור לפחות אפליקציה אחת.');
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
async function chooseContextApplication(existing = null) {
  const guideId = current.guide.id;
  const { discovery, apps: discovered } = await discoverApplications();
  const apps = discovered.filter(app => existing ||
    !current.contexts.some(c => {
      let matcher; try { matcher = JSON.parse(c.MatchersJson || '[]')[0]; } catch {}
      return c.Runtime === app.runtime && matcher?.Value?.toLowerCase() === app.matcherValue.toLowerCase();
    }));
  $('dialog-title').textContent = existing ? 'החלפת יעד אפליקציה' : 'הוספת אפליקציה';
  $('submit-dialog').textContent = existing ? 'החלפת יעד' : 'הוספת אפליקציה';
  $('dialog-fields').innerHTML =
    '<div class="fields"><div class="field"><label>בחר אפליקציה פתוחה</label>' +
    '<p>הזיהוי יישמר אוטומטית. אין צורך להזין פרטים טכניים.</p>' +
    applicationChoices(apps) +
    '<button type="button" class="btn secondary small" id="refresh-applications">רענון אפליקציות</button>' +
    (!discovery.webConnected ? '<p>תוסף Instructor אינו מחובר — רשימת אתרי Web אינה זמינה.</p>' : '') +
    (existing ? '<p>מיפויי השלבים הקיימים לא ישונו. לאחר ההחלפה יש לוודא שהם מתאימים לאפליקציה החדשה.</p>' : '') +
    '</div></div>';
  $('refresh-applications').onclick = () => guarded(() => chooseContextApplication(existing));
  $('editor-form').onsubmit = event => {
    event.preventDefault();
    guarded(async () => {
      const indexes = [...document.querySelectorAll('input[name="app"]:checked')].map(el => Number(el.value));
      if (indexes.length !== 1) throw new Error('יש לבחור אפליקציה אחת.');
      const app = apps[indexes[0]];
      if (!app) throw new Error('האפליקציה שנבחרה אינה זמינה.');
      if (existing && !confirm('להחליף את יעד האפליקציה? מיפויי השלבים הקיימים יישמרו ללא שינוי וייתכן שיידרש למפות אותם מחדש.')) return;
      const used = new Set(current.contexts.map(c => c.ContextId));
      let contextId = existing?.ContextId;
      if (!contextId) {
        let index = 1;
        while (used.has('app-' + index)) index++;
        contextId = 'app-' + index;
      }
      await api('/guides/' + guideId + '/contexts', {
        method:'POST', body:JSON.stringify({
          contextId, runtime:app.runtime,
          matcherProperty:app.matcherProperty, matcherValue:app.matcherValue
        })
      });
      $('editor-dialog').close();
      await openGuide(guideId);
      selectTab('contexts');
      notify(existing ? 'יעד האפליקציה עודכן. יש לבדוק את מיפויי השלבים.' : 'האפליקציה נוספה.');
    });
  };
  if (!$('editor-dialog').open) $('editor-dialog').showModal();
}
function addContext() { return guarded(() => chooseContextApplication()); }
function renameContext(c) {
  modal('שינוי שם אפליקציה',[{name:'name',label:'שם תצוגה',value:c.DisplayName||c.ContextId,max:100}],async v=>{
    await api('/guides/'+current.guide.id+'/contexts/'+encodeURIComponent(c.ContextId)+'/name',{method:'PUT',body:JSON.stringify({name:v.name})});
    const id=current.guide.id;await openGuide(id);selectTab('contexts');notify('שם האפליקציה עודכן.');
  });
}
function editContext(c) { return guarded(() => chooseContextApplication(c)); }
async function deleteContext(c) {
  if(!confirm('להסיר את האפליקציה "'+(c.DisplayName||c.ContextId)+'"? ההסרה תתאפשר רק אם אין שלבים שתלויים בה.'))return;
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
    notify('ה־Learner הופעל לבדיקת הלומדה מתחילתה. יש להכין את אפליקציית היעד למצב ההתחלתי.', false, 'info');
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
        bubble:v.bubble,placement:"Center"})
    });
    await openGuide(guideId);
    const row=[...document.querySelectorAll('[data-step]')]
       .find(x=>x.dataset.step===created.stepId);
    row?.click();
    notify('השלב נוצר. כרגע התקדמותו ידנית עד שיוגדרו רכיב יעד ותנאי התקדמות.');
  },'יצירת שלב');
}
function editStepVariableAction(step, context) {
  if (!current?.variables?.length || !context) return;
  const guideId = current.guide.id, stepId = step.id;
  const choices = current.variables.map(v => ({value:v.id,text:v.name}));
  modal('הוספת פעולה למשתנה — שלב ' + step.order, [
    {name:'variableId',label:'משתנה לעדכון',value:choices[0].value,choices},
    {name:'kind',label:'פעולה',value:'ReadFromTarget',choices:[
      {value:'ReadFromTarget',text:'קליטת ערך מהאפליקציה'},
      {value:'SetValue',text:'הזנת ערך קבוע'},
      {value:'Clear',text:'איפוס משתנה'},
      {value:'CopyFromVariable',text:'העתקה ממשתנה אחר'}]},
    {name:'value',label:'ערך קבוע',value:'',optional:true,max:1000},
    {name:'sourceVariableId',label:'משתנה מקור להעתקה',value:choices[0].value,choices}
  ], async values => {
    if (values.kind === 'ReadFromTarget') {
      await beginTargetPicker(context.Runtime, guideId, stepId, context.ContextId,
        false, false, context, {variableId:values.variableId});
      return;
    }
    await api('/guides/' + guideId + '/steps/' + encodeURIComponent(stepId) + '/variables',
      {method:'POST',body:JSON.stringify({
        variableId:values.variableId, kind:values.kind,
        value:values.value, sourceVariableId:values.sourceVariableId})});
    await openGuide(guideId);
    const row = [...document.querySelectorAll('[data-step]')].find(x => x.dataset.step === stepId);
    row?.click();
    notify('פעולת המשתנה נוספה.');
  },'המשך');
  const operation = $('field-kind');
  const updateFields = () => {
    $('field-value').closest('.field').classList.toggle('hidden',operation.value !== 'SetValue');
    $('field-sourceVariableId').closest('.field').classList.toggle('hidden',
      operation.value !== 'CopyFromVariable');
  };
  operation.addEventListener('change',updateFields);
  updateFields();
}
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
    {name:'bubble', label:'טקסט הבועית', value:step.bubble || '', max:4000, multiline:true,
     help:'השינוי ישפיע על תוכן הבועית בלבד. רכיבי יעד וכללי התקדמות אינם משתנים.'},
    {name:'enabled', label:'מצב השלב', value: step.enabled ? 'true' : 'false',
     choices:[{value:'true',text:'פעיל'},{value:'false',text:'מושבת'}]}
  ], async values => {
    await api('/guides/' + guideId + '/steps/' + encodeURIComponent(stepId), {
      method:'PUT', body:JSON.stringify({bubble:values.bubble, enabled:values.enabled === 'true',
        contextId:values.contextId || selectedContext || null})
    });
    await openGuide(guideId);
    const row = [...document.querySelectorAll('[data-step]')].find(b=>b.dataset.step===stepId);
    if (row) row.click();
    notify('פרטי השלב נשמרו.');
  });
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
