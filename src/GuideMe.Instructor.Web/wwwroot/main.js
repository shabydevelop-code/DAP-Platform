"use strict";
const token = document.querySelector('meta[name="guideme-session"]').content;
const $ = id => document.getElementById(id);
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
  current = null; selectedStep = null; updateCaptureContexts();
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
  selectTab('steps'); renderSteps(); renderContexts(); updateCaptureContexts(); clearNotice();
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
    const step = current.steps.find(x=>x.id===selectedStep);
    const stepContext = data.step?.[0]?.ApplicationContextId;
    if (stepContext && current.contexts.some(c=>c.ContextId===stepContext && c.Runtime==='Web')) $('capture-context').value=stepContext;
    const sections = [['שלב',data.step],['רכיבי יעד',data.targets],['כללי התקדמות ולכידה',data.rules]];
    $('step-inspector').innerHTML = '<h2>שלב ' + step.order + '</h2><p style="margin-bottom:19px">' + esc(step.bubble||step.id) + '</p><div class="detail-grid">' +
      sections.map(([title,rows]) => '<div class="detail-block"><small>' + title + '</small><pre>' + esc(JSON.stringify(rows,null,2)) + '</pre></div>').join('') + '</div>';
  })));
}
function matcher(c) { try { return JSON.parse(c.MatchersJson || '[]').map(m=>m.Kind+': '+m.Value).join(', '); }catch{return '—';} }
function renderContexts() {
  $('contexts-list').innerHTML = current.contexts.length ? current.contexts.map((c,i) =>
    '<div class="context-card"><div><span class="chip ' + (c.Runtime === 'Windows'?'win':'') + '">' + esc(c.Runtime) + '</span> <span class="context-name">' + esc(c.DisplayName || c.ContextId) + '</span><div class="context-meta">' + esc(matcher(c)) + '</div></div><div class="context-actions"><button class="btn secondary small" data-context-rename="' + i + '">שינוי שם</button><button class="btn secondary small" data-context-edit="' + i + '">החלפת יעד</button><button class="btn danger small" data-context-delete="' + i + '">הסרה</button></div></div>').join('')
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
    :'<input id="field-'+f.name+'" name="'+f.name+'" value="'+esc(f.value||'')+'" maxlength="'+(f.max||256)+'" required>';
    return '<div class="field">'+label+field+(f.help?'<div class="field-help">'+esc(f.help)+'</div>':'')+'</div>';
  }).join('')+'</div>';
  $('submit-dialog').textContent=buttonText;
  const form=$('editor-form');
  form.onsubmit=e=>{e.preventDefault();const values=Object.fromEntries(new FormData(form).entries());guarded(async()=>{await submit(values);$('editor-dialog').close();});};
  $('editor-dialog').showModal();
  form.querySelector('input,select')?.focus();
}
const runtimeField=(value='Web')=>({name:'runtime',label:'סוג אפליקציה',value,choices:[{value:'Web',text:'Web — דפדפן'},{value:'Windows',text:'Windows — יישום מקומי'}]});
function contextFields(c=null) {
  let raw={};try{raw=JSON.parse(c?.MatchersJson || '[]')[0] || {}}catch{}
  return [{name:'contextId',label:'מזהה אפליקציה',value:c?.ContextId||'',help:'מזהה יציב, לדוגמה web-main או windows-main'},
    runtimeField(c?.Runtime||'Web'),{name:'matcherValue',label:'ערך זיהוי',value:raw.Value||'',help:'Web: localhost:5200 · Windows: שם התהליך ללא exe'}];
}
function contextPayload(v) {return {contextId:v.contextId.trim(),runtime:v.runtime,matcherProperty:v.runtime==='Web'?'UrlHost':'ProcessName',matcherValue:v.matcherValue.trim()};}
function newGuide() {
  modal('יצירת לומדה חדשה',[{name:'name',label:'שם הלומדה'},...contextFields()],async v=>{
    const g=await api('/guides',{method:'POST',body:JSON.stringify({name:v.name.trim(),...contextPayload(v)})});
    await loadGuides(); await openGuide(g.id); notify('הלומדה נוצרה בהצלחה.');
  },'יצירת לומדה');
}
function renameGuide() {
  modal('שינוי שם הלומדה',[{name:'name',label:'שם הלומדה',value:current.guide.name}],async v=>{
    await api('/guides/'+current.guide.id+'/name',{method:'PUT',body:JSON.stringify({name:v.name})});
    const id=current.guide.id;await loadGuides();await openGuide(id);notify('שם הלומדה עודכן.');
  });
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
$('new-guide').onclick=newGuide;
$('rename-guide').onclick=renameGuide;
$('add-context').onclick=addContext;
$('back').onclick=showList;
$('nav-guides').onclick=showList;
$('close-dialog').onclick=$('cancel-dialog').onclick=()=>$('editor-dialog').close();
document.querySelectorAll('.tab').forEach(b=>b.onclick=()=>selectTab(b.dataset.tab));
function updateCaptureContexts() {
  const select = $('capture-context');
  const previous = select.value;
  const contexts = (current?.contexts || []).filter(c => c.Runtime === 'Web');
  select.replaceChildren(new Option(current ? 'בחר אפליקציית Web מהלומדה' : 'פתח לומדה כדי להקליט', ''));
  for (const context of contexts) select.add(new Option(context.DisplayName || context.ContextId, context.ContextId));
  if (contexts.some(c=>c.ContextId===previous)) select.value = previous;
  else if (contexts.length===1) select.value=contexts[0].ContextId;
  $('capture-tab').classList.add('hidden');
  $('capture-select').disabled = !current || !select.value;
  $('capture-start').disabled = true;
}
$('capture-context').onchange = () => {
  $('capture-select').disabled = !current || !$('capture-context').value;
  $('capture-start').disabled = true;
  $('capture-tab').classList.add('hidden');
};
async function refreshCaptureStatus() {
  try {
    const state = await api('/capture/status');
    $('capture-status').textContent = state.recording ? 'מקליט' :
      state.connected ? 'תוסף מחובר' : state.pairingOpen ? 'ממתין לחיבור' : 'תוסף לא מחובר';
    $('capture-target').textContent = state.tabId && state.guideId === current?.guide?.id &&
      state.contextId === $('capture-context').value
      ? 'לשונית מזוהה: ' + (state.targetTitle || state.targetUrl) :
        'בחר אפליקציה מתוך הלומדה ולחץ ״זהה אפליקציה״';
    $('capture-select').disabled = !state.connected || !current || !$('capture-context').value || state.recording;
    $('capture-start').disabled = !state.connected || !(
      state.tabId && state.guideId === current?.guide?.id &&
      state.contextId === $('capture-context').value);
    if (state.recording) $('capture-start').disabled = true;
    $('capture-stop').disabled = !state.recording;
    $('capture-pair').disabled = state.recording;
  } catch {
    $('capture-status').textContent = 'החיבור לשרת אינו זמין';
    $('capture-start').disabled = true;
    $('capture-stop').disabled = true;
  }
}
$('capture-pair').onclick = () => guarded(async () => {
  await api('/capture/pair', { method:'POST', body:'{}' });
  notify('החיבור אושר ל־30 שניות. התוסף יתחבר ברקע; אין צורך לפתוח אותו. אם טרם התחבר, עבור ללשונית אתר וחזור ל־Instructor.');
  await refreshCaptureStatus();
});
async function selectCaptureTab(tabId = null) {
  if (!current || !$('capture-context').value) throw new Error('בחר לומדה ואפליקציית Web.');
  const state = await api('/capture/select', { method:'POST', body: JSON.stringify({
    guideId: current.guide.id, contextId:$('capture-context').value, tabId
  }) });
  if (state.status === 'notFound') {
    $('capture-tab').classList.add('hidden');
    notify('האפליקציה אינה פתוחה או אינה תואמת להגדרת הזיהוי. פתח את האתר ונסה שוב.',true);
  } else if (state.status === 'multiple') {
    const picker = $('capture-tab');
    picker.replaceChildren(new Option('נמצאו כמה לשוניות — בחר יעד', ''));
    for (const tab of state.tabs)
      picker.add(new Option((tab.title || tab.url) + ' · ' + tab.tabId, String(tab.tabId)));
    picker.classList.remove('hidden');
    notify('נמצאו כמה לשוניות מתאימות. בחר את הלשונית הרצויה ברשימה.');
  } else {
    $('capture-tab').classList.add('hidden');
    notify('נבחרה אפליקציית היעד. אפשר להתחיל בהקלטה.');
  }
  await refreshCaptureStatus();
}
$('capture-select').onclick=()=>guarded(()=>selectCaptureTab());
$('capture-tab').onchange=()=>guarded(async()=>{
  const id=Number($('capture-tab').value);
  if (id) await selectCaptureTab(id);
});
$('capture-start').onclick=()=>guarded(async()=>{
  if (!current) throw new Error('יש לפתוח לומדה.');
  await api('/capture/start', { method:'POST', body:JSON.stringify({
    guideId:current.guide.id, contextId:$('capture-context').value
  })});
  $('capture-results').classList.add('hidden');
  notify('הקלטת Web פעילה עבור האפליקציה שנבחרה. בצע פעולה באתר ולאחר מכן עצור את ההקלטה.');
  await refreshCaptureStatus();
});
$('capture-stop').onclick=()=>guarded(async()=>{
  const result = await api('/capture/stop',{method:'POST',body:'{}'});
  const events=result.events || [];
  const box=$('capture-results');box.classList.remove('hidden');box.replaceChildren();
  const heading=document.createElement('h3');heading.textContent='אירועים שנקלטו: '+events.length;
  const note=document.createElement('p');note.textContent='סקירה בלבד; לא בוצע שינוי בשלב קיים או בנתוני V5.';
  const pre=document.createElement('pre');pre.textContent=JSON.stringify(events,null,2);
  box.append(heading,note,pre);
  await refreshCaptureStatus();
});
void refreshCaptureStatus();
setInterval(()=>{if(!document.hidden) void refreshCaptureStatus()},3000);
guarded(loadGuides);
