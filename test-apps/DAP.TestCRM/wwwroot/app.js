const app=document.querySelector('#app');
const api=async(url,options={})=>{const r=await fetch(url,{headers:{'Content-Type':'application/json'},...options});if(!r.ok)throw new Error(await r.text());return r.status===204?null:r.json()};
const go=hash=>location.hash=hash;
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const row=(cells,hash)=>`<tr class="clickable" data-go="${hash}">${cells.map(x=>`<td>${esc(x)}</td>`).join('')}</tr>`;
document.addEventListener('click',e=>{const n=e.target.closest('[data-go]');if(n)go(n.dataset.go)});

async function customers(){
 const data=await api('/api/customers');
 app.innerHTML=`<section class="panel"><div class="toolbar"><h1>לקוחות</h1></div><table><thead><tr><th>מספר</th><th>שם</th><th>טלפון</th><th>דוא"ל</th></tr></thead><tbody>${data.map(x=>row([x.id,x.name,x.phone,x.email],`#/customer/${x.id}`)).join('')}</tbody></table></section>`;
}
async function customer(id){
 const [c,s]=await Promise.all([api(`/api/customers/${id}`),api(`/api/customers/${id}/sites`)]);
 app.innerHTML=`<div class="breadcrumb"><a data-go="#/">לקוחות</a> / ${esc(c.name)}</div><section class="panel"><h1>${esc(c.name)}</h1><p>${esc(c.phone)} · ${esc(c.email)}</p></section><section class="panel"><div class="toolbar"><h2>אתרים</h2><button class="primary" data-go="#/customer/${id}/site/new">אתר חדש</button></div><table><thead><tr><th>מספר</th><th>שם אתר</th><th>סוג</th><th>כתובת</th></tr></thead><tbody>${s.map(x=>row([x.id,x.name,x.type,x.address],`#/site/${x.id}/details`)).join('')}</tbody></table></section>`;
}
function siteForm(customerId){
 app.innerHTML=`<section class="panel"><h1>אתר חדש</h1><form id="site-form" class="form-grid"><label class="field">שם<input name="name" required></label><label class="field">סוג<select name="type"><option>Office</option><option>Branch</option><option>Warehouse</option></select></label><label class="field full">כתובת<input name="address" required></label><div><button class="primary">שמור</button> <button type="button" data-go="#/customer/${customerId}">ביטול</button></div></form></section>`;
 document.querySelector('#site-form').onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target);const x=await api(`/api/customers/${customerId}/sites`,{method:'POST',body:JSON.stringify(Object.fromEntries(f))});go(`#/site/${x.id}/details`)};
}
async function site(id,tab='details'){
 const s=await api(`/api/sites/${id}`);
 let content='';
 if(tab==='details') content=`<form id="site-edit" class="form-grid"><label class="field">שם<input name="name" value="${esc(s.name)}"></label><label class="field">סוג<select name="type">${['Office','Branch','Warehouse'].map(v=>`<option ${v===s.type?'selected':''}>${v}</option>`).join('')}</select></label><label class="field full">כתובת<input name="address" value="${esc(s.address)}"></label><div><button class="primary">שמור שינויים</button></div></form><div id="server-result"></div>`;
 if(tab==='cases'){const xs=await api(`/api/sites/${id}/cases`);content=`<div class="toolbar"><h2>פניות</h2><button class="primary" data-go="#/site/${id}/case/new">פניה חדשה</button></div><table><thead><tr><th>מספר</th><th>סטטוס</th><th>נושא</th></tr></thead><tbody>${xs.map(x=>row([x.id,x.status,x.subject],`#/case/${x.id}`)).join('')}</tbody></table>`}
 if(tab==='leads'){const xs=await api(`/api/sites/${id}/leads`);content=`<div class="toolbar"><h2>לידים</h2><button class="primary" data-go="#/site/${id}/lead/new">ליד חדש</button></div><table><thead><tr><th>מספר</th><th>מקור</th><th>איש קשר</th><th>סטטוס</th></tr></thead><tbody>${xs.map(x=>row([x.id,x.source,x.contactName,x.status],`#/lead/${x.id}`)).join('')}</tbody></table>`}
 app.innerHTML=`<div class="breadcrumb"><a data-go="#/customer/${s.customerId}">לקוח</a> / ${esc(s.name)}</div><section class="panel"><h1>${esc(s.name)}</h1><nav class="tabs"><button class="${tab==='details'?'active':''}" data-go="#/site/${id}/details">פרטי אתר</button><button class="${tab==='cases'?'active':''}" data-go="#/site/${id}/cases">פניות</button><button class="${tab==='leads'?'active':''}" data-go="#/site/${id}/leads">לידים</button></nav><div id="tab-content">${content}</div></section>`;
 const form=document.querySelector('#site-edit');if(form)form.onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target);const result=document.querySelector('#server-result');result.innerHTML='<div class="notice">שומר בשרת...</div>';const updated=await api(`/api/sites/${id}`,{method:'PUT',body:JSON.stringify(Object.fromEntries(f))});document.querySelector('#tab-content').innerHTML=`<div class="notice">השינויים נשמרו בשרת.</div><p><strong>${esc(updated.name)}</strong></p><p>סוג: ${esc(updated.type)}</p><p>כתובת: ${esc(updated.address)}</p><button data-go="#/site/${id}/details">עריכה נוספת</button>`};
}
async function casePage(id,siteId){
 const fresh=id==='new';const x=fresh?{status:'Open',subject:'',description:''}:await api(`/api/cases/${id}`);
 app.innerHTML=`<section class="panel"><h1>${fresh?'פניה חדשה':'פניה '+id}</h1><form id="record-form" class="form-grid"><label class="field">סטטוס<select name="status">${['Open','In Progress','Closed'].map(v=>`<option ${v===x.status?'selected':''}>${v}</option>`).join('')}</select></label><label class="field">נושא<input name="subject" value="${esc(x.subject)}" required></label><label class="field full">תיאור<textarea name="description">${esc(x.description)}</textarea></label><div><button class="primary">שמור</button></div></form></section>`;
 document.querySelector('#record-form').onsubmit=async e=>{e.preventDefault();const body=JSON.stringify(Object.fromEntries(new FormData(e.target)));const y=await api(fresh?`/api/sites/${siteId}/cases`:`/api/cases/${id}`,{method:fresh?'POST':'PUT',body});go(`#/case/${y.id}`)};
}
async function leadPage(id,siteId){
 const fresh=id==='new';const x=fresh?{source:'Website',contactName:'',status:'New',notes:''}:await api(`/api/leads/${id}`);
 app.innerHTML=`<section class="panel"><h1>${fresh?'ליד חדש':'ליד '+id}</h1><form id="record-form" class="form-grid"><label class="field">מקור<select name="source">${['Website','Referral','Campaign','Phone'].map(v=>`<option ${v===x.source?'selected':''}>${v}</option>`).join('')}</select></label><label class="field">איש קשר<input name="contactName" value="${esc(x.contactName)}" required></label><label class="field">סטטוס<select name="status">${['New','Qualified','Won','Lost'].map(v=>`<option ${v===x.status?'selected':''}>${v}</option>`).join('')}</select></label><label class="field full">הערות<textarea name="notes">${esc(x.notes)}</textarea></label><div><button class="primary">שמור</button></div></form></section>`;
 document.querySelector('#record-form').onsubmit=async e=>{e.preventDefault();const body=JSON.stringify(Object.fromEntries(new FormData(e.target)));const y=await api(fresh?`/api/sites/${siteId}/leads`:`/api/leads/${id}`,{method:fresh?'POST':'PUT',body});go(`#/lead/${y.id}`)};
}
async function route(){
 try{
  const p=location.hash.slice(2).split('/').filter(Boolean);
  if(!p.length)return customers();
  if(p[0]==='customer'&&p.length===2)return customer(+p[1]);
  if(p[0]==='customer'&&p[2]==='site'&&p[3]==='new')return siteForm(+p[1]);
  if(p[0]==='site'&&['details','cases','leads'].includes(p[2]))return site(+p[1],p[2]);
  if(p[0]==='site'&&p[2]==='case'&&p[3]==='new')return casePage('new',+p[1]);
  if(p[0]==='site'&&p[2]==='lead'&&p[3]==='new')return leadPage('new',+p[1]);
  if(p[0]==='case')return casePage(+p[1]);
  if(p[0]==='lead')return leadPage(+p[1]);
  return customers();
 }catch(e){app.innerHTML=`<section class="panel"><h2>שגיאה</h2><pre>${esc(e.message)}</pre></section>`}
}
window.addEventListener('hashchange',route);route();
