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
let saveState = 'saved';
let pendingChanges = false;
let saveInFlight = 0;
function updateSaveStatus() {
  const el = $('save-status');
  if (!el) return;
  const state = saveInFlight ? 'saving' : saveState === 'error' ? 'error' : pendingChanges ? 'dirty' : saveState;
  el.dataset.state = state;
  el.textContent = ({saved:'כל השינויים נשמרו',dirty:'שינויים שלא נשמרו',
    saving:'שומר שינויים...',error:'השמירה נכשלה'})[state];
}
function markDirty() { pendingChanges = true; saveState = 'dirty'; updateSaveStatus(); }
function markClean() { pendingChanges = false; saveState = 'saved'; updateSaveStatus(); }
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
  if (writing) { saveInFlight++; updateSaveStatus(); }
  let response;
  try {
    response = await fetch('/api' + path, {
    ...options, headers: { 'X-GuideMe-Session': token, ...(options.body ? {'Content-Type':'application/json'} : {}), ...options.headers },
    cache: 'no-store'
    });
  } catch (error) {
    if (writing) { saveInFlight--; saveState = 'error'; updateSaveStatus(); }
    throw error;
  }
  if (writing) {
    saveInFlight--;
    if (response.ok) markClean();
    else { saveState = 'error'; updateSaveStatus(); }
  }
  if (!response.ok) {
    let body = await response.text();
    try { const err = JSON.parse(body); body = err.detail || err.title || body; } catch {}
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
    ? '<table class="table"><thead><tr><th>שם הלומדה</th><th>מזהה</th></tr></thead><tbody>' +
      list.map(g => '<tr><td><button type="button" class="guide-title guide-open-link" data-open="' + g.id + '">' + esc(g.name) + '</button></td><td><span class="guide-id">#' + g.id + '</span></td></tr>').join('') +
      '</tbody></table>'
    : '<div class="empty">לא נמצאו לומדות. ניתן ליצור לומדה חדשה.</div>';
  document.querySelectorAll('[data-open]').forEach(b => b.addEventListener('click', () => guarded(() => openGuide(Number(b.dataset.open)))));
}
async function openGuide(id) {
  if (!confirmDiscard()) return;
  current = await api('/guides/' + id);
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
      '<div class="step-editor-actions"><button class="btn secondary small" id="edit-step">עריכת תוכן השלב</button>' +
      '<button class="btn primary small" id="test-from-step"' + (step.enabled ? '' : ' disabled') + '>בדיקה משלב זה</button></div></div>' +
      '<div class="step-summary"><strong>הנחיה למשתמש</strong><p>' +
      esc(step.bubble || 'לא הוגדרה הנחיה') + '</p>' +
      '<span class="chip">' + (step.enabled ? 'פעיל' : 'מושבת') + '</span></div>' +
      '<div class="step-friendly-grid">' +
      '<section class="step-friendly-card"><h3>רכיב במסך</h3>' +
      (target ? '<p>נבחר רכיב באפליקציה' +
        (frames.length ? ' בתוך ' + frames.length + ' מסגרות' : '') + '.</p>' +
        (locatorText ? '<p>רכיב שנבחר: <strong>' + esc(friendlyName) + '</strong></p>' +
          '<p class="target-technical">מזהה רכיב: <code dir="ltr">' + esc(locatorText) + '</code></p>' : '') :
        '<p>עדיין לא נבחר רכיב לשלב הזה.</p>') +
      ((context?.Runtime === 'Web' || context?.Runtime === 'Windows') && target ?
        '<button class="btn secondary small" id="replace-web-target">בחירת רכיב אחר</button>' : '') +
      '</section>' +
      '<section class="step-friendly-card"><h3>מתי ממשיכים לשלב הבא?</h3>' +
      '<p>' + esc(conditionText) + '</p>' +
      (rule?.ExpectedValue ? '<p>ערך צפוי: <strong>' + esc(rule.ExpectedValue) + '</strong></p>' : '') +
      (context?.Runtime === 'Web' && rule ?
        '<button class="btn secondary small" id="edit-validation">עריכת תנאי ההתקדמות</button>' : '') +
      '</section></div>' +
      (context?.Runtime === 'Web' && target && rule ?
        '<section class="target-editor"><h3>תנאי השלמה נוסף</h3>' +
        (completionRule ? '<p>רכיב: <strong>' + esc(completionElement?.DisplayName || completionSelector) +
          '</strong> · חוק: <strong>' + completionLabel + '</strong></p>' +
           (completionRule?.Kind === 'attribute-equals' ? '<p>מאפיין: <code dir="ltr">' +
             esc(completionAttribute.name || '') + '</code> · ערך צפוי: <code dir="ltr">' +
             esc(completionAttribute.value || '') + '</code></p>' : '') +
          '<button class="btn secondary small" id="remove-completion">הסרת תנאי השלמה</button>' :
          '<p>בחר רכיב ובדוק הופעה, היעלמות או שינוי מאפיין בעקבות הפעולה.</p>' +
          '<button class="btn secondary small" id="add-completion">בחירת רכיב לתנאי השלמה</button>') +
        '</section>' : '');
    $('edit-step').onclick = () => editStep(step, contextId);
    $('test-from-step').onclick = () => guarded(async () => {
      if (!current || !confirmDiscard()) return;
      const button = $('test-from-step');
      button.disabled = true;
      try {
        await verifyLearnerTransport();
        await api('/guides/' + current.guide.id + '/test?startStepId=' + encodeURIComponent(step.id),
          {method:'POST', body:'{}'});
        notify('ה־Learner הופעל משלב ' + step.order + '. הבועית תוצג כשהרכיב המתאים יהיה זמין.');
      } finally { button.disabled = false; }
    });
    const addCompletion = $('add-completion');
    if (addCompletion) addCompletion.onclick = () => guarded(() =>
      beginWebPicker(current.guide.id, step.id, contextId, false, true));
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
    const targetReplace = $('replace-web-target');
    if (targetReplace) targetReplace.onclick = () => guarded(() =>
      context?.Runtime === 'Windows' ? $('pick-windows-element').click() :
        beginWebPicker(current.guide.id, step.id, contextId, true));
    const conditionEditor = $('edit-validation');
    if (conditionEditor) conditionEditor.onclick = () => editValidation(step, rule);
    if (context?.Runtime === 'Windows') {
      const section = document.createElement('section');
      section.className = 'target-editor';
      section.innerHTML = '<h3>בחירת רכיב</h3>' +
        '<button id="pick-windows-element" class="btn primary">הפעלת ממפה רכיבים</button>' +
        '<div id="pick-result" class="hidden" aria-live="polite"></div>';
      $('step-inspector').append(section);
      const matchers = parseJson(context.MatchersJson, []);
      const resolveWindow = async () => {
        const windows = (await api('/discovery')).windows || [];
        const matches = windows.filter(w => matchers.some(m => {
          const kind = String(m.Kind || '').toLowerCase();
          const value = String(m.Value || '').toLowerCase();
          if (!value) return false;
          if (kind === 'processname')
            return w.processName.toLowerCase().replace(/\\.exe$/, '') === value.replace(/\\.exe$/, '');
          if (kind === 'windowtitlecontains') return w.title.toLowerCase().includes(value);
          return false;
        }));
        if (matches.length !== 1)
          throw new Error(matches.length ? 'נמצאו מספר חלונות תואמים. יש לסגור חלונות כפולים לפני הבחירה.' :
            'לא נמצא חלון פעיל התואם לאפליקציה המשויכת לשלב.');
        return matches[0].processId;
      };
      $('pick-windows-element').onclick = () => guarded(async () => {
        const pid = await resolveWindow();
        const result = $('pick-result');
        result.classList.remove('hidden');
        result.textContent = 'ממפה הרכיבים פעיל. עבור לאפליקציית היעד, העבר את העכבר מעל הרכיב ולחץ Ctrl+Shift+Space לבחירה.';
        await api('/windows/picker/start', {method:'POST',body:JSON.stringify({processId:pid})});
        const guideId = current.guide.id, stepId = step.id;
        const poll = async () => {
          if (current?.guide?.id !== guideId || selectedStep !== stepId || !result.isConnected) {
            await api('/windows/picker/cancel',{method:'POST',body:'{}'});
            return;
          }
          const status = await api('/windows/picker/status');
          if (status.error) { result.textContent = status.error; return; }
          if (status.picked && status.selection) {
            const info = status.selection;
            const candidate = {
              selector: info.locators?.[0]?.value || '',
              label: info.name || info.controlType,
              tag: info.controlType === 'ControlType.Edit' ? 'input' : 'button',
              locators: info.locators || []
            };
            pickingFor = {guideId,stepId,contextId,runtime:'Windows',processId:pid,
              replacing:!!target,completion:false,startedAt:Date.now()};
            renderPickedTarget(candidate, result);
            if (info.error) {
              const warning = document.createElement('p');
              warning.setAttribute('role','alert');
              warning.textContent = info.error;
              result.prepend(warning);
              $('save-web-target').disabled = true;
            }
            return;
          }
          if (!status.picking) { result.textContent = 'בחירת הרכיב הופסקה.'; return; }
          setTimeout(() => void guarded(poll), 500);
        };
        setTimeout(() => void guarded(poll), 500);
      });
    }
    if (context?.Runtime === 'Web') {
      const section = document.createElement('section');
      section.className = 'target-editor';
      section.innerHTML =
        (!target && !rule
          ? '<h3>בחירת רכיב</h3>' +
            '<button id="pick-web-element" class="btn primary">הפעלת ממפה רכיבים</button>'
          : '') +
        '<div id="pick-result" class="hidden" aria-live="polite"></div>';
      $('step-inspector').append(section);
      if (!target && !rule)
        $('pick-web-element').onclick = () => guarded(() =>
          beginWebPicker(current.guide.id, step.id, contextId));
    }

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
async function beginWebPicker(guideId, stepId, contextId, replacing=false, completion=false) {
  if (pickingFor) {
    await api('/target-picker/cancel',{method:'POST',body:'{}'});
    pickingFor = null;
  }
  const selected = await api('/capture/select', {method:'POST',
    body:JSON.stringify({guideId,contextId})});
  if (selected.status === 'notFound')
    throw new Error('לא נמצאה לשונית מתאימה. פתח את האפליקציה באתר ונסה שוב.');
  let tabId = null;
  if (selected.status === 'multiple') {
    const tabs = selected.tabs || [];
    const choices = tabs.map((t,i) => (i+1)+'. '+(t.title || t.url)).join('\\n');
    const answer = prompt('נמצאו כמה לשוניות מתאימות. הקלד את מספר הלשונית:\\n'+choices);
    if (answer === null) return;
    const i = Number(answer)-1;
    if (!Number.isInteger(i) || !tabs[i]) throw new Error('בחירת לשונית אינה תקינה.');
    tabId = tabs[i].tabId;
    await api('/capture/select',{method:'POST',
      body:JSON.stringify({guideId,contextId,tabId})});
  }
  await api('/target-picker/start',{method:'POST',body:JSON.stringify({guideId,contextId})});
  pickingFor = {guideId,stepId,contextId,replacing,completion,startedAt:Date.now()};
  const box = $('pick-result');
  if (!box) throw new Error('אזור אישור הרכיב חסר בממשק. פתח מחדש את השלב.');
  if (box) { box.dataset.pickedSelector=''; box.classList.remove('hidden'); box.textContent = 'מתחבר ללשונית ומפעיל בחירת רכיב…'; }
  await refreshPicker();
}
async function refreshPicker() {
  if (!pickingFor) return;
  if (current?.guide?.id !== pickingFor.guideId || selectedStep !== pickingFor.stepId) {
    pickingFor = null;
    return;
  }
  const requestSession = pickingFor;
  const state = await api('/capture/status');
  if (!pickingFor || pickingFor !== requestSession ||
      current?.guide?.id !== requestSession.guideId || selectedStep !== requestSession.stepId) return;
  const box = $('pick-result');
  if (!box) return;
  if (!state.picked) {
    if (state.pickerError) {
      box.textContent = 'לא ניתן להפעיל את ממפה הרכיבים בלשונית שנבחרה: ' + state.pickerError;
      return;
    }
    if (!state.picking) {
      pickingFor = null;
      markClean();
      box.textContent = 'איתור הרכיב בוטל. ניתן להפעיל שוב את הממפה.';
      return;
    }
    if (!state.connected) {
      box.textContent = 'אין כרגע תקשורת עם תוסף Instructor. בדוק שהוא פעיל ב־Chrome.';
      return;
    }
    if (!state.pickReady && Date.now() - pickingFor.startedAt >= 5000) {
      box.textContent = 'ממתין לאישור הפעלת הממפה בלשונית שנבחרה. אם לא מופיעה מסגרת בחלון היעד, ניתן לבטל ולנסות שוב.';
      return;
    }
    box.textContent = state.pickReady ? 'ממפה פעיל: העבר עכבר מעל רכיב ולחץ Ctrl+Shift+Space לבחירה (Esc לביטול).' : 'מפעיל בחירת רכיב…';
    return;
  }
  renderPickedTarget(state.picked, box);
}
function renderPickedTarget(candidate, box) {
  if (!pickingFor) return;
  if (box.dataset.pickedSelector === candidate.selector) return;
  box.dataset.pickedSelector = candidate.selector;
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
