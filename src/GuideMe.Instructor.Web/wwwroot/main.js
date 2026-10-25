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
function notify(message, error = false) {
  const el = $('notice'); el.textContent = message; el.classList.toggle('error', error); el.classList.remove('hidden');
  el.setAttribute('role',error?'alert':'status');
  el.scrollIntoView({behavior:'auto',block:'nearest'});
  if (error) { el.setAttribute('tabindex','-1'); el.focus(); }
}
function clearNotice() { $('notice').classList.add('hidden'); }
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
  $('steps-count').textContent = current.steps.length; $('contexts-count').textContent = current.contexts.length;
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
    // Only offer a tooltip when the one-line label is actually truncated.
    if (name.scrollWidth <= name.clientWidth + 1) return;
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
    const target = data.targets.find(x => x.Purpose === 'Primary') || data.targets[0];
    const element = (data.elements || []).find(x => x.ElementId === target?.ElementId);
    const rule = data.rules.find(x => x.RuleType === 'PrimaryValidation');
    const locators = parseJson(element?.LocatorsJson, []);
    const frames = parseJson(element?.FrameContextJson, []);
    const conditionText = rule ? validationLabel(rule.Kind) : 'לא הוגדר תנאי';
    const completionRule = data.rules.find(x => x.RuleType === 'AdditionalCompletionCondition');
    const completionTarget = data.targets.find(x => x.UsageId === completionRule?.TargetUsageId);
    const completionElement = data.elements.find(x => x.ElementId === completionTarget?.ElementId);
    const completionSelector = parseJson(completionElement?.LocatorsJson, [])[0]?.Value || '';
    const completionLabel = completionRule?.Kind === 'target-appeared' ? 'הופיע' :
      completionRule?.Kind === 'attribute-equals' ? 'מאפיין השתנה לערך צפוי' : 'נעלם';
    const completionAttribute = completionRule?.Kind === 'attribute-equals'
      ? parseJson(completionRule.ExpectedValue, {}) : {};
    const locatorText = locators[0]?.Value || '';
    const friendlyName = element?.DisplayName || (locatorText === 'a' ? 'קישור' : locatorText);
    const appText = context ? appLabel(context) : 'לא משויך';

    $('step-inspector').innerHTML =
      '<div class="step-editor-head"><div><h2>שלב ' + step.order + '</h2>' +
      '<p>אפליקציה: ' + esc(appText) + '</p></div>' +
      '<div class="step-editor-actions">' +
      '<button class="btn danger small" id="delete-step">מחיקת שלב</button>' +
      '<button class="btn primary small" id="test-from-step"' + (step.enabled ? '' : ' disabled') + '>בדיקה משלב זה</button></div></div>' +
      '<div class="step-summary"><strong>הנחיה למשתמש</strong><p>' +
      esc(step.bubble || 'לא הוגדרה הנחיה') + '</p>' +
      '<div class="step-summary-actions"><span class="chip">' + (step.enabled ? 'פעיל' : 'מושבת') + '</span>' +
      '<button class="btn secondary small" id="edit-step">עריכת תוכן השלב</button></div></div>' +
      '<div class="step-friendly-grid">' +
      '<section class="step-friendly-card"><h3>רכיב במסך</h3>' +
      (target ? '<p>נבחר רכיב באפליקציה' +
        (frames.length ? ' בתוך ' + frames.length + ' מסגרות' : '') + '.</p>' +
        (locatorText ? '<p>רכיב שנבחר: <strong>' + esc(friendlyName) + '</strong></p>' +
          '<p class="target-technical">מזהה רכיב: <code dir="ltr">' + esc(locatorText) + '</code></p>' : '') :
        '<p>עדיין לא נבחר רכיב לשלב הזה.</p>') +
      ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') ?
        (target ? '<button class="btn secondary small" id="replace-web-target">בחירת רכיב אחר</button>' +
          ((current.variables || []).length ? ' <button class="btn secondary small" id="bind-target-variable">שימוש במשתנה במזהה הרכיב</button>' : '') :
          '<button id="pick-element" class="btn primary">הפעלת ממפה רכיבים</button>') +
        '<div id="pick-result" class="hidden" aria-live="polite"></div>' : '') +
      '</section>' +
      '<section class="step-friendly-card"><h3>מתי ממשיכים לשלב הבא?</h3>' +
      '<p>' + esc(!target ? 'יוגדר לאחר בחירת הרכיב.' : conditionText) + '</p>' +
      (rule?.ExpectedValue ? '<p>ערך צפוי: <strong>' + esc(rule.ExpectedValue) + '</strong></p>' : '') +
      ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') && rule ?
        '<button class="btn secondary small" id="edit-validation">עריכת תנאי ההתקדמות</button>' : '') +
      '</section></div>' +
      (context?.Runtime === 'Web' && target && rule ?
        '<section class="step-friendly-card completion-card"><h3>תנאי השלמה נוסף</h3>' +
        (completionRule ? '<p>רכיב שנבחר: <strong>' + esc(completionElement?.DisplayName || completionSelector || 'לא הוגדר שם') +
          '</strong></p>' +
          (completionSelector ? '<p class="target-technical">מזהה רכיב: <code dir="ltr">' +
            esc(completionSelector) + '</code></p>' : '') +
          '<p>תנאי השלמה: <strong>' + esc(completionLabel) + '</strong></p>' +
           (completionRule?.Kind === 'attribute-equals' ? '<p>מאפיין: <code dir="ltr">' +
             esc(completionAttribute.name || '') + '</code> · ערך צפוי: <code dir="ltr">' +
             esc(completionAttribute.value || '') + '</code></p>' : '') +
          '<button class="btn secondary small" id="remove-completion">הסרת תנאי השלמה</button>' :
          '<p>בחר רכיב ובדוק הופעה, היעלמות או שינוי מאפיין בעקבות הפעולה.</p>' +
          '<button class="btn secondary small" id="add-completion">בחירת רכיב לתנאי השלמה</button>') +
        '</section>' : '') +
      '<section class="step-friendly-card completion-card" aria-labelledby="step-variables-title">' +
      '<h3 id="step-variables-title">משתנים</h3>' +
      (variableActions.length ? variableActions.map(action => {
        const variable = (current.variables || []).find(v => v.id === action.variableId);
        const source = (current.variables || []).find(v => v.id === action.sourceVariableId);
        const label = action.kind === 'SetValue' ? 'הזנת ערך' :
          action.kind === 'Clear' ? 'איפוס' :
          action.kind === 'CopyFromVariable' ? 'העתקה מ־' + (source?.name || 'משתנה אחר') :
          'קליטה מרכיב (' + (action.property || 'ערך') + ')';
        return '<div class="guide-variable-row"><span><strong>' + esc(variable?.name || action.variableId) +
          '</strong> · ' + esc(label) + '</span>' +
          '<button class="btn danger small" data-remove-variable-action="' + esc(action.id) + '">הסרה</button></div>';
      }).join('') : '<p>לא הוגדרו פעולות משתנים לשלב.</p>') +
      '<button class="btn secondary small" id="add-variable-action"' +
      ((current.variables || []).length ? '' : ' disabled') + '>הוספת פעולת משתנה</button></section>';
    $('add-variable-action').onclick = () => editStepVariableAction(step, data.targets, context?.Runtime);
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
        notify('ה־Learner הופעל משלב ' + step.order + '. הבועית תוצג כשהרכיב המתאים יהיה זמין.');
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
    const bindTarget = $('bind-target-variable');
    if (bindTarget) bindTarget.onclick = () => bindTargetToVariable(step, context?.Runtime, locatorText);
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
  const kind = validationChoices.some(x=>x.value===rule.Kind) ? rule.Kind : 'clicked';
  modal('עריכת תנאי התקדמות — שלב ' + step.order, [
    {name:'kind',label:'מתי המערכת תעבור לשלב הבא?',value:kind,choices:validationChoices},
    {name:'expectedValue',label:'ערך צפוי (רק עבור ״השדה מכיל ערך מסוים״)',
      value:rule.ExpectedValue || '',max:500,optional:true,
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
            ? Number($('completion-state-choice').value) : null})});
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
async function beginTargetPicker(runtime, guideId, stepId, contextId, replacing=false, completion=false, context=null) {
  const adapter = targetAdapters[runtime];
  if (!adapter) throw new Error('סביבת היעד אינה נתמכת.');
  if (completion && !adapter.saveCompletion)
    throw new Error('תנאי השלמה נוסף אינו נתמך כרגע בסביבת יעד זו.');
  if (pickingFor) {
    await targetAdapters[pickingFor.runtime].cancel();
    pickingFor = null;
  }
  const session = {runtime,guideId,stepId,contextId,replacing,completion,context,startedAt:Date.now()};
  const box = $('pick-result');
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
  const box = $('pick-result');
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
  if (box.dataset.pickedSelector === candidate.selector) return;
  box.dataset.pickedSelector = candidate.selector;
  box.innerHTML = '<p><strong>הרכיב שנבחר:</strong> '+esc(candidate.label || candidate.tag || 'רכיב')+'</p>'+
    (candidate.framePath?.length ? '<p><strong>נתיב מסגרות:</strong> '+candidate.framePath.map(esc).join(' → ')+'</p>' : '')+
    '<p class="target-technical">מזהה רכיב: <code dir="ltr">'+esc(candidate.selector)+'</code></p>'+
    (!pickingFor.completion && candidate.gridChoices?.length ?
      '<label for="grid-row-choice">זיהוי שורה לפי תוכן בעמודה</label>'+
      '<select id="grid-row-choice"><option value="">בחירת הרכיב הרגילה (לפי מיקום)</option>'+
      candidate.gridChoices.map((choice,i)=>'<option value="'+i+'">'+esc(choice.header)+': '+esc(choice.value)+'</option>').join('')+
      '</select><p>בחירה לפי ערך תישאר תקפה גם לאחר מיון הטבלה. אם הערך אינו ייחודי, הוא לא יוצע.</p>' : '')+
    (pickingFor.replacing ? '' : '<label for="target-condition">' + (pickingFor.completion ? 'חוק השלמה' : 'תנאי התקדמות') + '</label>')+
    (pickingFor.replacing ? '' : '<select id="target-condition">'+
    (pickingFor.completion ? '<option value="target-appeared">הופיע</option><option value="target-vanished">נעלם</option><option value="attribute-equals">מאפיין משתנה לערך צפוי</option>' :
    '<option value="clicked" '+(['input','textarea','select'].includes(candidate.tag)?'':'selected')+'>המשתמש לוחץ על הרכיב</option>'+
    '<option value="value-not-empty" '+(['input','textarea','select'].includes(candidate.tag)?'selected':'')+'>הוזן ערך בשדה</option>'+
    '<option value="target-disappeared">הרכיב נעלם בעקבות הפעולה</option>')+
    '</select>')+
    (pickingFor.completion ? '<div id="attribute-condition-fields" class="hidden">' +
      '<label for="completion-state-choice">המצב שנלכד מהרכיב</label>' +
      '<select id="completion-state-choice">' +
      (candidate.stateAttributes || []).map((state, index) =>
        '<option value="' + index + '">' + esc((state.label || 'רכיב') + ': ' + state.name + ' = ' + state.value) + '</option>').join('') +
      '</select>' +
      ((candidate.stateAttributes || []).length ? '<p>בחר את מצב הרכיב הרצוי. הערכים נקראו מאפליקציית היעד.</p>' :
        '<p>לא נמצאו מאפייני מצב מתאימים ברכיב. בחר רכיב אחר, למשל כותרת העמודה.</p>') +
      '</div>' : '')+
    '<div class="target-actions">'+
    '<button id="save-web-target" class="btn primary">אישור ושמירת רכיב</button>'+
    '<button id="cancel-web-target" class="btn secondary">ביטול</button></div>';
  if (requestSession.runtime === 'Windows' && state.selection?.error) {
    const warning = document.createElement('p');
    warning.textContent = state.selection.error;
    warning.setAttribute('role','alert');
    box.prepend(warning);
    $('save-web-target').disabled = true;
  }
  if (pickingFor.completion) {
    const condition = $('target-condition');
    const updateFields = () => $('attribute-condition-fields').classList.toggle('hidden',
      condition.value !== 'attribute-equals');
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
      if (pickedFor.completion)
        await adapter.saveCompletion(pickedFor, candidate);
      else
        await api('/guides/'+pickedFor.guideId+'/steps/'+encodeURIComponent(pickedFor.stepId)+'/primary-target',
          {method:'POST',body:JSON.stringify({
            condition:$('target-condition')?.value || 'clicked',
            replacing:pickedFor.replacing,gridChoice:gridChoiceIndex,
            processId:pickedFor.processId || null})});
      pickingFor = null;
      result.textContent = 'הרכיב נשמר בהצלחה.';
      await openGuide(pickedFor.guideId);
      const item = [...document.querySelectorAll('[data-step]')]
        .find(x => x.dataset.step === pickedFor.stepId);
      if (item) await item.click();
      notify(pickedFor.replacing
        ? 'הרכיב הוחלף בהצלחה. תנאי ההתקדמות נשאר ללא שינוי.'
        : pickedFor.completion ? 'תנאי ההשלמה נשמר.' : 'רכיב היעד ותנאי ההתקדמות נשמרו.');
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
    markClean();
    box.textContent = 'הבחירה בוטלה.';
  });
}
setInterval(() => { if (pickingFor) void guarded(refreshPicker); }, 1500);
function appLabel(c) { if(c.DisplayName) return c.DisplayName; try { const m=JSON.parse(c.MatchersJson || '[]')[0]; return m?.Value ? c.Runtime + ' · ' + m.Value : c.ContextId; } catch { return c.ContextId; } }
function matcher(c) { try { return JSON.parse(c.MatchersJson || '[]').map(m=>m.Kind+': '+m.Value).join(', '); }catch{return '—';} }
function renderContexts() {
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
    notify('ה־Learner הופעל לבדיקת הלומדה מתחילתה. יש להכין את אפליקציית היעד למצב ההתחלתי.');
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
function bindTargetToVariable(step, runtime, locatorText) {
  if (!current?.variables?.length) return;
  const guideId = current.guide.id, stepId = step.id;
  const attributeOptions = [...locatorText.matchAll(/\[\s*([\w-]+)\s*=\s*(['"])(.*?)\2\s*\]/g)]
    .map(match => ({value:match[1],text:match[1] + ' · ' + match[3]}));
  if (runtime === 'Web' && !attributeOptions.length) {
    notify('לא נמצא מאפיין בעל ערך במזהה הרכיב. יש לבחור רכיב עם מזהה מתאים.', true);
    return;
  }
  modal('שימוש במשתנה — שלב ' + step.order, [
    {name:'variableId',label:'משתנה מהלומדה',value:current.variables[0].id,
     choices:current.variables.map(v => ({value:v.id,text:v.name}))},
    ...(runtime === 'Web'
      ? [{name:'attribute',label:'המאפיין שערכו יוחלף במשתנה',
          value:attributeOptions[0].value,choices:attributeOptions}]
      : [])
  ], async values => {
    await api('/guides/' + guideId + '/steps/' + encodeURIComponent(stepId) + '/bind-variable',
      {method:'POST',body:JSON.stringify({variableId:values.variableId,
        attribute:values.attribute || null})});
    await openGuide(guideId);
    const row = [...document.querySelectorAll('[data-step]')].find(x => x.dataset.step === stepId);
    row?.click();
    notify('המשתנה קושר למזהה הרכיב.');
  });
}
function editStepVariableAction(step, targets, runtime) {
  if (!current || !current.variables?.length) return;
  const guideId = current.guide.id, stepId = step.id;
  const options = current.variables.map(v => ({value:v.id,text:v.name}));
  const targetOptions = (targets || []).map(t => ({value:t.UsageId,
    text:t.Purpose === 'Primary' ? 'הרכיב הראשי' : 'רכיב נוסף'}));
  const properties = runtime === 'Windows'
    ? [{value:'name',text:'שם הרכיב'},{value:'automation-id',text:'מזהה הרכיב'}]
    : [{value:'text',text:'טקסט'},{value:'value',text:'ערך שדה'},
       {value:'frame-url-fragment',text:'כתובת המסגרת'}];
  modal('פעולת משתנה — שלב ' + step.order, [
    {name:'variableId',label:'משתנה לעדכון',value:options[0].value,choices:options},
    {name:'kind',label:'פעולה',value:'SetValue',choices:[
      {value:'SetValue',text:'הזנת ערך'}, {value:'Clear',text:'איפוס משתנה'},
      {value:'CopyFromVariable',text:'העתקה ממשתנה אחר'},
      ...(targetOptions.length ? [{value:'ReadFromTarget',text:'קליטה מהאפליקציה'}] : [])]},
    {name:'value',label:'ערך (עבור הזנת ערך)',value:'',optional:true,max:1000},
    {name:'sourceVariableId',label:'משתנה מקור (עבור העתקה)',
     value:options[0].value,choices:options},
    ...(targetOptions.length ? [
      {name:'targetUsageId',label:'רכיב לקריאה',value:targetOptions[0].value,choices:targetOptions},
      {name:'property',label:'ערך לקריאה',value:properties[0].value,choices:properties}
    ] : []),
    {name:'timing',label:'מועד הפעולה',value:'AfterAction',choices:[
      {value:'BeforeAction',text:'לפני הפעולה בשלב'},
      {value:'AfterAction',text:'אחרי הפעולה בשלב'}]}
  ],async values => {
    await api('/guides/' + guideId + '/steps/' + encodeURIComponent(stepId) + '/variables',
      {method:'POST',body:JSON.stringify(values)});
    await openGuide(guideId);
    const row = [...document.querySelectorAll('[data-step]')]
      .find(x => x.dataset.step === stepId);
    row?.click();
    notify('פעולת המשתנה נוספה.');
  },'הוספת פעולה');
  // Only show fields needed for the selected operation.
  const operation = $('field-kind');
  const updateFields = () => {
    $('field-value').closest('.field').classList.toggle('hidden', operation.value !== 'SetValue');
    $('field-sourceVariableId').closest('.field').classList.toggle('hidden',
      operation.value !== 'CopyFromVariable');
    $('field-targetUsageId')?.closest('.field').classList.toggle('hidden',
      operation.value !== 'ReadFromTarget');
    $('field-property')?.closest('.field').classList.toggle('hidden',
      operation.value !== 'ReadFromTarget');
  };
  operation.addEventListener('change', updateFields);
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
