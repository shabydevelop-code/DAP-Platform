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
const esc = v => String(v ?? "").replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
async function api(path, options = {}) {
  const response = await fetch('/api' + path, {
    ...options, headers: { 'X-GuideMe-Session': token, ...(options.body ? {'Content-Type':'application/json'} : {}), ...options.headers },
    cache: 'no-store'
  });
  if (!response.ok) {
    let body = await response.text();
    try { const err = JSON.parse(body); body = err.detail || err.title || body; } catch {}
    throw new Error(body || 'שגיאה בתקשורת עם השרת המקומי (' + response.status + ')');
  }
  return response.status === 204 ? null : response.json();
}
function notify(message, error = false) {
  const el = $('notice'); el.textContent = message; el.classList.toggle('error', error); el.classList.remove('hidden');
  el.scrollIntoView({behavior:'smooth',block:'nearest'});
}
function clearNotice() { $('notice').classList.add('hidden'); }
async function guarded(action) { try { await action(); } catch(e) { notify(e.message || String(e), true); } }
function showList() {
  current = null; selectedStep = null;
  $('list-view').classList.remove('hidden'); $('detail-view').classList.add('hidden');
  $('page-title').textContent = 'הלומדות שלי'; $('page-subtitle').textContent = 'כל המדריכים, האפליקציות והשלבים במקום אחד.';
  $('breadcrumb').textContent = 'כל הלומדות'; clearNotice();
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
    ? '<table class="table"><thead><tr><th>שם הלומדה</th><th>מזהה</th><th>מפתח לומדה</th><th></th></tr></thead><tbody>' +
      list.map(g => '<tr><td><span class="guide-title">' + esc(g.name) + '</span></td><td><span class="guide-id">#' + g.id + '</span></td><td><span class="guide-id">' + esc(g.key) + '</span></td><td><button class="link" data-open="' + g.id + '">פתיחת לומדה ←</button></td></tr>').join('') +
      '</tbody></table>'
    : '<div class="empty">לא נמצאו לומדות. ניתן ליצור לומדה חדשה.</div>';
  document.querySelectorAll('[data-open]').forEach(b => b.addEventListener('click', () => guarded(() => openGuide(Number(b.dataset.open)))));
}
async function openGuide(id) {
  current = await api('/guides/' + id);
  const g = current.guide;
  $('list-view').classList.add('hidden'); $('detail-view').classList.remove('hidden');
  $('page-title').textContent = g.name; $('page-subtitle').textContent = 'ניהול השלבים, האפליקציות וההגדרות של הלומדה.';
  $('breadcrumb').textContent = g.name;
  $('detail-name').textContent = g.name; $('detail-description').textContent = 'לומדה #' + g.id + ' · ' + current.steps.length + ' שלבים';
  $('guide-key').textContent = g.key;
  $('steps-count').textContent = current.steps.length; $('contexts-count').textContent = current.contexts.length;
  selectTab('steps'); renderSteps(); renderContexts(); clearNotice();
}
function selectTab(tab) {
  document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active',t.dataset.tab===tab));
  $('steps-panel').classList.toggle('hidden',tab!=='steps');
  $('contexts-panel').classList.toggle('hidden',tab!=='contexts');
}
function renderSteps() {
  selectedStep = null; $('step-inspector').innerHTML = '<div class="empty-inspector">בחר שלב להצגת הפרטים שלו</div>';
  const search = $('step-search').value.trim().toLowerCase();
  const steps = (current?.steps || []).filter(s => String(s.order).includes(search) || s.id.toLowerCase().includes(search) || (s.bubble || '').toLowerCase().includes(search));
  $('step-list').innerHTML = steps.length ? steps.map(s =>
    '<button class="step-row" data-step="' + esc(s.id) + '"><span class="step-num">' + s.order + '</span><span class="step-body"><strong>' +
    esc(s.bubble || s.id) + '</strong><small>' + (s.enabled ? 'שלב פעיל' : 'שלב מושבת') + ' · ' + esc(s.id) + '</small></span></button>').join('')
    : '<div class="empty">אין שלבים להצגה</div>';
  document.querySelectorAll('[data-step]').forEach(b => b.addEventListener('click',() => guarded(async () => {
    selectedStep = b.dataset.step;
    document.querySelectorAll('[data-step]').forEach(x => x.classList.toggle('active', x===b));
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
    const locatorText = locators[0]?.Value || '';
    const appText = context ? appLabel(context) : 'לא משויך';

    $('step-inspector').innerHTML =
      '<div class="step-editor-head"><div><h2>שלב ' + step.order + '</h2>' +
      '<p>אפליקציה: ' + esc(appText) + '</p></div>' +
      '<button class="btn secondary small" id="edit-step">עריכת תוכן השלב</button></div>' +
      '<div class="step-summary"><strong>הנחיה למשתמש</strong><p>' +
      esc(step.bubble || 'לא הוגדרה הנחיה') + '</p>' +
      '<span class="chip">' + (step.enabled ? 'פעיל' : 'מושבת') + '</span></div>' +
      '<div class="step-friendly-grid">' +
      '<section class="step-friendly-card"><h3>רכיב במסך</h3>' +
      (target ? '<p>נבחר רכיב באפליקציה' +
        (frames.length ? ' בתוך ' + frames.length + ' מסגרות' : '') + '.</p>' +
        '<p class="step-muted">זיהוי: <code dir="ltr">' + esc(locatorText || 'לא ידוע') + '</code></p>' :
        '<p>עדיין לא נבחר רכיב לשלב הזה.</p>') +
      '</section>' +
      '<section class="step-friendly-card"><h3>מתי ממשיכים לשלב הבא?</h3>' +
      '<p>' + esc(conditionText) + '</p>' +
      (rule?.ExpectedValue ? '<p>ערך צפוי: <strong>' + esc(rule.ExpectedValue) + '</strong></p>' : '') +
      (context?.Runtime === 'Web' && rule ?
        '<button class="btn secondary small" id="edit-validation">עריכת תנאי ההתקדמות</button>' : '') +
      '</section></div>';
    $('edit-step').onclick = () => editStep(step, contextId);
    const conditionEditor = $('edit-validation');
    if (conditionEditor) conditionEditor.onclick = () => editValidation(step, rule);
    if (context?.Runtime === 'Web' && !data.targets.length && !data.rules.length) {
      const section = document.createElement('section');
      section.className = 'target-editor';
      section.innerHTML =
        '<h3>ממפה רכיבים ותנאי התקדמות</h3>' +
        '<p>העבר את העכבר מעל הרכיב הרצוי באתר ולחץ Ctrl+Shift+Space לאישור. Esc לביטול.</p>' +
        '<button id="pick-web-element" class="btn primary">הפעלת ממפה רכיבים</button>' +
        '<div id="pick-result" class="hidden"></div>';
      $('step-inspector').append(section);
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
}
let pickingFor = null;
async function beginWebPicker(guideId, stepId, contextId) {
  if (pickingFor) await api('/target-picker/cancel',{method:'POST',body:'{}'});
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
  pickingFor = {guideId,stepId,contextId,startedAt:Date.now()};
  const box = $('pick-result');
  if (box) { box.dataset.pickedSelector=''; box.classList.remove('hidden'); box.textContent = 'מתחבר ללשונית ומפעיל בחירת רכיב…'; }
  await refreshPicker();
}
async function refreshPicker() {
  if (!pickingFor) return;
  if (current?.guide?.id !== pickingFor.guideId || selectedStep !== pickingFor.stepId) {
    pickingFor = null;
    return;
  }
  const state = await api('/capture/status');
  const box = $('pick-result');
  if (!box) return;
  if (!state.picked) {
    if (state.pickerError) {
      box.textContent = 'לא ניתן להפעיל את ממפה הרכיבים בלשונית שנבחרה: ' + state.pickerError;
      return;
    }
    if (!state.picking) {
      pickingFor = null;
      box.textContent = 'איתור הרכיב בוטל. ניתן להפעיל שוב את הממפה.';
      return;
    }
    if (!state.pickReady && Date.now() - pickingFor.startedAt >= 5000) {
      box.textContent = 'התוסף לא הצליח להפעיל בחירת רכיב. בצע Reload לתוסף GuideMe Instructor ב־chrome://extensions, רענן את אתר היעד ונסה שוב.';
      return;
    }
    box.textContent = state.pickReady ? 'הממפה הופעל בלשונית: ' + (state.targetTitle || state.targetUrl || state.tabId) + '. חפש באנר GuideMe בראש דף היעד; אם אינו מופיע, זו אינה הלשונית שנבחרה. העבר עכבר ובחר באמצעות Ctrl+Shift+Space.' : 'מפעיל בחירת רכיב בלשונית…';
    return;
  }
  const candidate = state.picked;
  if (box.dataset.pickedSelector === candidate.selector) return;
  box.dataset.pickedSelector = candidate.selector;
  box.innerHTML = '<p><strong>הרכיב שנבחר:</strong> '+esc(candidate.label || candidate.tag || 'רכיב')+'</p>'+
    (candidate.framePath?.length ? '<p><strong>נתיב מסגרות:</strong> '+candidate.framePath.map(esc).join(' → ')+'</p>' : '')+
    '<p class="target-selector">'+esc(candidate.selector)+'</p>'+
    '<label for="target-condition">תנאי התקדמות</label>'+
    '<select id="target-condition">'+
    '<option value="clicked">המשתמש לוחץ על הרכיב</option>'+
    '<option value="value-not-empty">הוזן ערך בשדה</option>'+
    '<option value="target-disappeared">הרכיב נעלם בעקבות הפעולה</option>'+
    '</select><div class="target-actions">'+
    '<button id="save-web-target" class="btn primary">אישור ושמירת רכיב</button>'+
    '<button id="cancel-web-target" class="btn secondary">ביטול</button></div>';
  $('save-web-target').onclick = () => guarded(async () => {
    const pickedFor = pickingFor;
    await api('/guides/'+pickedFor.guideId+'/steps/'+encodeURIComponent(pickedFor.stepId)+'/web-target',
      {method:'POST',body:JSON.stringify({condition:$('target-condition').value})});
    pickingFor = null;
    await openGuide(pickedFor.guideId);
    const item = [...document.querySelectorAll('[data-step]')].find(x=>x.dataset.step===pickedFor.stepId);
    item?.click();
    notify('רכיב היעד וכלל ההתקדמות נשמרו ב־V5.');
  });
  $('cancel-web-target').onclick = () => guarded(async () => {
    await api('/target-picker/cancel',{method:'POST',body:'{}'});
    pickingFor = null;
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
}
function modal(title, fields, submit, buttonText='שמירה') {
  $('dialog-title').textContent=title;
  $('dialog-fields').innerHTML='<div class="fields">' + fields.map(f=>{
    const label='<label for="field-'+f.name+'">'+esc(f.label)+'</label>';
    const field=f.choices?'<select id="field-'+f.name+'" name="'+f.name+'">'+f.choices.map(v=>'<option value="'+esc(v.value)+'" '+(v.value===f.value?'selected':'')+'>'+esc(v.text)+'</option>').join('')+'</select>'
    :f.multiline?'<textarea id="field-'+f.name+'" name="'+f.name+'" maxlength="'+(f.max||4000)+'" rows="5">'+esc(f.value||'')+'</textarea>'
    :'<input id="field-'+f.name+'" name="'+f.name+'" value="'+esc(f.value||'')+'" maxlength="'+(f.max||256)+'" '+(f.optional?'':'required')+'>';
    return '<div class="field">'+label+field+(f.help?'<div class="field-help">'+esc(f.help)+'</div>':'')+'</div>';
  }).join('')+'</div>';
  $('submit-dialog').textContent=buttonText;
  const form=$('editor-form');
  form.onsubmit=e=>{e.preventDefault();const values=Object.fromEntries(new FormData(form).entries());guarded(async()=>{await submit(values);$('editor-dialog').close();});};
  $('editor-dialog').showModal();
  form.querySelector('input,textarea,select')?.focus();
}
const runtimeField=(value='Web')=>({name:'runtime',label:'סוג אפליקציה',value,choices:[{value:'Web',text:'Web — דפדפן'},{value:'Windows',text:'Windows — יישום מקומי'}]});
function contextFields(c=null) {
  let raw={};try{raw=JSON.parse(c?.MatchersJson || '[]')[0] || {}}catch{}
  return [{name:'contextId',label:'מזהה אפליקציה',value:c?.ContextId||'',help:'מזהה יציב, לדוגמה web-main או windows-main'},
    runtimeField(c?.Runtime||'Web'),{name:'matcherValue',label:'ערך זיהוי',value:raw.Value||'',help:'Web: localhost:5200 · Windows: שם התהליך ללא exe'}];
}
function contextPayload(v) {return {contextId:v.contextId.trim(),runtime:v.runtime,matcherProperty:v.runtime==='Web'?'UrlHost':'ProcessName',matcherValue:v.matcherValue.trim()};}
async function newGuide() {
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
  $('dialog-title').textContent = 'יצירת לומדה חדשה';
  $('submit-dialog').textContent = 'יצירת לומדה';
  $('dialog-fields').innerHTML =
    '<div class="fields"><div class="field"><label for="guide-new-name">שם הלומדה</label>' +
    '<input id="guide-new-name" name="name" maxlength="256" required autocomplete="off"></div>' +
    '<div class="field"><label>אפליקציות משתתפות</label>' +
    '<p>בחר אפליקציה אחת או יותר מבין האפליקציות הפתוחות. הזיהוי יישמר אוטומטית.</p>' +
    (apps.length ? '<div class="application-picker">' + apps.map((app, i) =>
       '<label class="application-option"><input type="checkbox" name="app" value="' + i + '">' +
       '<span><strong>' + esc(app.title) + '</strong><small>' +
       esc(app.runtime + ' · ' + app.matcherValue) + '</small></span></label>').join('') + '</div>' :
       '<p>לא אותרו אפליקציות. פתח אתר ב־Chrome או תוכנת Windows ולאחר מכן לחץ רענון.</p>') +
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
function addContext() {
  modal('הוספת אפליקציה',contextFields(),async v=>{
    await api('/guides/'+current.guide.id+'/contexts',{method:'POST',body:JSON.stringify(contextPayload(v))});
    const id=current.guide.id;await openGuide(id);selectTab('contexts');notify('האפליקציה נוספה.');
  },'הוספת אפליקציה');
}
function renameContext(c) {
  modal('שינוי שם אפליקציה',[{name:'name',label:'שם תצוגה',value:c.DisplayName||c.ContextId,max:100}],async v=>{
    await api('/guides/'+current.guide.id+'/contexts/'+encodeURIComponent(c.ContextId)+'/name',{method:'PUT',body:JSON.stringify({name:v.name})});
    const id=current.guide.id;await openGuide(id);selectTab('contexts');notify('שם האפליקציה עודכן.');
  });
}
function editContext(c) {
  modal('החלפת יעד אפליקציה',contextFields(c).filter(f=>f.name!=='contextId'),async v=>{
    await api('/guides/'+current.guide.id+'/contexts',{method:'POST',body:JSON.stringify({...contextPayload({...v,contextId:c.ContextId})})});
    const id=current.guide.id;await openGuide(id);selectTab('contexts');notify('יעד האפליקציה עודכן.');
  });
}
async function deleteContext(c) {
  if(!confirm('להסיר את האפליקציה "'+(c.DisplayName||c.ContextId)+'"? ההסרה תתאפשר רק אם אין שלבים שתלויים בה.'))return;
  await api('/guides/'+current.guide.id+'/contexts/'+encodeURIComponent(c.ContextId),{method:'DELETE',body:'{}'});
  const id=current.guide.id;await openGuide(id);selectTab('contexts');notify('האפליקציה הוסרה.');
}
$('guide-search').addEventListener('input',renderGuides);
$('step-search').addEventListener('input',()=>{if(current)renderSteps()});
$('new-guide').onclick=()=>guarded(newGuide);
$('rename-guide').onclick=renameGuide;
$('delete-guide').onclick=deleteGuide;
$('add-context').onclick=addContext;
$('new-step').onclick=newStep;
$('back').onclick=showList;
$('close-dialog').onclick=$('cancel-dialog').onclick=()=>$('editor-dialog').close();
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
    ...(contexts.length > 1 ? [{name:'contextId',label:'אפליקציית השלב',
       value:defaultContext,choices:contexts.map(c => ({
         value:c.ContextId, text:appLabel(c)
       }))}] : []),
    {name:'bubble',label:'טקסט הבועית',value:'',multiline:true,max:4000,
     help:'שלב חדש ייווצר כשלב ידני. בהמשך ניתן יהיה להגדיר רכיב יעד ותנאי התקדמות.'},
    {name:'placement',label:'מיקום בועית מידע ידנית',value:'Center',choices:[
      {value:'Center',text:'מרכז — עד לבחירת רכיב יעד'}
    ]}
  ],async v=>{
    const created=await api('/guides/' + guideId + '/steps', {
      method:'POST',body:JSON.stringify({contextId:v.contextId || defaultContext,
        bubble:v.bubble,placement:v.placement})
    });
    await openGuide(guideId);
    const row=[...document.querySelectorAll('[data-step]')]
       .find(x=>x.dataset.step===created.stepId);
    row?.click();
    notify('השלב נוצר. כרגע התקדמותו ידנית עד שיוגדרו רכיב יעד ותנאי התקדמות.');
  },'יצירת שלב');
}
function editStep(step, assignedContextId) {
  if (!current || !step) return;
  const guideId = current.guide.id;
  const stepId = step.id;
  const contexts = current.contexts;
  const selectedContext = contexts.some(c => c.ContextId === assignedContextId)
    ? assignedContextId : contexts[0]?.ContextId;
  modal('עריכת שלב ' + step.order, [
    ...(contexts.length > 1 ? [{name:'contextId',label:'אפליקציית השלב',
       value:selectedContext,choices:contexts.map(c => ({
         value:c.ContextId, text:(appLabel(c)) + ' · ' + c.Runtime
       }))}] : []),
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
