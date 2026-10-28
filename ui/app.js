'use strict';
const C=window.Codec,$=id=>document.getElementById(id);
window.amagAppearanceSave = async value => { await api('theme', {theme:value}); };
const token=location.hash.slice(1)||sessionStorage.getItem('amag-token')||'';
if(token)sessionStorage.setItem('amag-token',token);
const S={base:null,data:null,connected:false,layer:0,index:0,physicalIndex:0,layout:C.physical.slice(),tab:'mapper',slot:0,undo:[],redo:[],events:[],eventDirty:false,clipboard:null,shortcut:[],recording:false,recorded:[],held:new Set(),lastTime:0,identify:false,session:null};
const el=(tag,text,cls)=>{const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(cls)n.className=cls;return n;};
function error(e){$('error').textContent=e.message||String(e);$('error').hidden=false;}
function status(text){$('status').textContent=text;}
function clearError(){$('error').hidden=true;}
async function api(path,body){
 const res=await fetch('/api/'+path,{method:body===undefined?'GET':'POST',headers:{'X-Amag-Token':token,...(body===undefined?{}:{'Content-Type':'application/json'})},...(body===undefined?{}:{body:JSON.stringify(body)})});
 const data=await res.json();if(!res.ok||!data.ok)throw Error(data.error||'Request failed');return data;
}
async function busy(text,fn){
 clearError();$('busyText').textContent=text;$('busy').hidden=false;
 try{return await fn();}catch(e){error(e);status('Stopped: '+e.message);return null;}finally{$('busy').hidden=true;}
}
function bind(id,fn){$(id).addEventListener('click',()=>{try{const p=fn();if(p?.catch)p.catch(error);}catch(e){error(e);}});}
const currentCode=()=>S.data?.keymap[S.layer*120+S.index];
const bank=()=>S.tab==='quick'?'quick':'custom';
function dirty(){if(!S.base||!S.data)return false;const d=C.changes(C.decode(S.base),S.data);return d.keys.length+d.custom.length+d.quick.length>0;}
function commit(fn){
 if(!S.data)throw Error('Read the keypad first.');
 const before=C.clone(S.data),next=C.clone(S.data);fn(next);
 if(JSON.stringify(before)===JSON.stringify(next))return;
 S.undo.push(before);if(S.undo.length>60)S.undo.shift();S.redo=[];S.data=next;clearError();render();
}
function discardEventEdits(){if(S.eventDirty&&!confirm('Discard event-table edits that have not been applied?'))return false;S.eventDirty=false;return true;}
function showTab(tab){if(!discardEventEdits())return;S.tab=tab;render();}
function assign(code){
 commit(d=>{for(let layer=0;layer<4;layer++)if($('allLayers').checked||layer===S.layer)d.keymap[layer*120+S.index]=code;});
 status('Assignment edited. Use Upload changes to save to the keypad.');
}
function macroCode(code){return code>=189&&code<=212?{bank:code<201?'custom':'quick',slot:code<201?code-189:code-201}:null;}
function makeOptions(select,macroOnly=false){
 select.replaceChildren();for(const [code,name] of C.keys){if(macroOnly&&(code<4||code>158))continue;const o=el('option',name);o.value=code;select.append(o);}
}
function ensureOption(select,code){if(!Array.from(select.options).some(o=>Number(o.value)===code)){const o=el('option',C.name(code));o.value=code;select.append(o);}select.value=code;}
function pendingText(){
 if(!S.data)return 'No device data loaded';const d=C.changes(C.decode(S.base),S.data);
 const n=d.keys.length+d.custom.length+d.quick.length;
 return n?`${d.keys.length} key(s), ${d.custom.length+d.quick.length} macro(s) changed`:'Matches last USB read';
}
function render(){
 const have=!!S.data;
 $('empty').hidden=have;$('editor').hidden=!have;
 document.querySelectorAll('nav button').forEach(b=>{const active=b.dataset.tab===S.tab;b.classList.toggle('active',active);b.setAttribute('aria-selected',String(active));});
 $('mapper').hidden=S.tab!=='mapper';$('device').hidden=S.tab!=='device';$('macroPanel').hidden=!['custom','quick'].includes(S.tab);
 for(const id of ['export','import','identify','exportKeymap','exportMacros'])$(id).disabled=!have||!S.connected;
 $('upload').disabled=!S.connected||!dirty()||S.identify;
 $('undo').disabled=!S.undo.length;$('redo').disabled=!S.redo.length;
 $('pending').textContent=pendingText();
 $('layers').replaceChildren(...C.layers.map((name,i)=>{const b=el('button',name,i===S.layer?'active':'');b.onclick=()=>{S.layer=i;render();};return b;}));
 if(have){renderMapper();if(['custom','quick'].includes(S.tab))renderMacro();}
 else if(['custom','quick'].includes(S.tab)){$('slots').replaceChildren();$('macroSummary').textContent='Connect / Read to load actual macros.';$('events').replaceChildren();$('macroReferences').textContent='No device configuration loaded.';}
 for(const id of ['macroUndo','assignSlot','parseSequence','parseText','startRecord','addTap','saveEvents','copyMacro','pasteMacro','clearMacro'])$(id).disabled=!have;
 $('layerCaption').textContent=C.layers[S.layer].toUpperCase();
 if(S.base){
  $('connection').textContent=(S.connected?'Connected':'Reconnect required')+' · ps2avrGB · firmware '+S.base.firmware;
  $('deviceDetails').replaceChildren(el('p','USB: 20A0:422D · ps2avrGB · Firmware '+S.base.firmware),el('p','Configuration read: '+new Date(S.base.capturedAt).toLocaleString()),el('p','480 keymap bytes · 12 × 168-byte custom macros · '+(S.data.extra.length?'12 × 76':'12 × 48')+'-byte quick macros'),el('p','Automatic backups: '+(S.session?.backups||'')));
 }
}
function renderMapper(){
 const base=C.decode(S.base),matrix=$('view').value==='matrix';const cells=matrix?Array.from({length:120},(_,i)=>i):S.layout;
 $('keypad').className=matrix?'matrix':'';$('keypad').replaceChildren();
 cells.forEach((index,i)=>{
  const offset=S.layer*120+index,code=S.data.keymap[offset],m=macroCode(code),b=el('button');
  b.dataset.index=index;b.setAttribute('aria-label',`Row ${Math.floor(index/8)+1}, column ${index%8+1}: ${C.name(code)}`);
  b.classList.toggle('selected',index===S.index);b.classList.toggle('changed',code!==base.keymap[offset] || !!(m&&C.hex(C.macroBytes(S.data,m.bank,m.slot))!==C.hex(C.macroBytes(base,m.bank,m.slot))));
  if(!matrix&&i===22)b.classList.add('wide');
  b.append(el('strong',C.name(code)));
  b.append(el('small',m?C.compact(C.events(S.data,m.bank,m.slot)):`R${Math.floor(index/8)+1} C${index%8+1}`));
  b.onclick=()=>{S.index=index;S.physicalIndex=matrix?S.layout.indexOf(index):i;render();};$('keypad').append(b);
 });
 const code=currentCode(),m=macroCode(code),old=C.decode(S.base).keymap[S.layer*120+S.index];
 $('selectionTitle').textContent=`${C.layers[S.layer]} · ROW ${Math.floor(S.index/8)+1}, COLUMN ${S.index%8+1}`;
 $('originalAssignment').textContent=C.name(old);$('editedAssignment').textContent=C.name(code);ensureOption($('assignment'),code);
 $('macroPeek').hidden=!m;if(m)$('peek').textContent=C.compact(C.events(S.data,m.bank,m.slot));
 const oldMacro=macroCode(old);$('previousMacro').hidden=!(m&&oldMacro&&(old!==code||C.hex(C.macroBytes(S.data,m.bank,m.slot))!==C.hex(C.macroBytes(base,oldMacro.bank,oldMacro.slot))));
 if(oldMacro)$('previousSequence').textContent=C.compact(C.events(base,oldMacro.bank,oldMacro.slot));
 $('matrixPosition').value=S.index;renderPalette();
}
function renderPalette(){
 const search=$('search').value.toLowerCase(),category=$('category').value, target=$('palette');
 target.replaceChildren();target.classList.toggle('keyboardpalette',category==='keyboard'&&!search);
 const button=code=>{const b=el('button',C.name(code));b.title='Assign '+C.name(code);b.onclick=()=>{try{assign(code);}catch(e){error(e);}};return b;};
 if(category==='keyboard'&&!search){
  const rows=[
   [41,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72],
   [53,30,31,32,33,34,35,36,37,38,39,45,46,42],
   [43,20,26,8,21,23,28,24,12,18,19,47,48,49],
   [57,4,22,7,9,10,11,13,14,15,51,52,40],
   [104,100,29,27,6,25,5,17,16,54,55,56,108],
   [103,106,105,44,109,110,101,107,80,81,82,79],
   [0,73,74,75,76,77,78,83,84,85,86,87,88],
   [89,90,91,92,93,94,95,96,97,98,99,50]
  ];
  for(const row of rows){const line=el('div',null,'keyboardrow');for(const code of row){const b=button(code);if(code===44)b.classList.add('spacekey');line.append(b);}target.append(line);}
  return;
 }
 for(const [code,name]of C.keys){
  const match=category==='all'||(category==='keyboard'&&code<=110)||(category==='functions'&&((code>=113&&code<=188)||code>=241))||(category==='macros'&&code>=189&&code<=212);
  if(match&&name.toLowerCase().includes(search))target.append(button(code));
 }
}

function renderMacro(){
 const b=bank(),custom=b==='custom';$('macroTitle').textContent=custom?'Custom Macro':'Quick Macro';
 $('macroCapacity').textContent=custom?'12 slots · 84 events per slot · delays in 100 ms steps':`12 slots · ${S.data.extra.length?76:48} events per slot · no delays`;
 $('recordDelays').disabled=!custom;if(!custom)$('recordDelays').checked=false;
 $('slots').replaceChildren();
 for(let i=0;i<12;i++){
  const btn=el('button',(custom?'CstMac ':'QMac ')+(i+1),S.slot===i?'active':'');
  btn.classList.toggle('changed',C.hex(C.macroBytes(S.data,b,i))!==C.hex(C.macroBytes(C.decode(S.base),b,i)));
  btn.onclick=()=>{if(!discardEventEdits())return;S.slot=i;renderMacro();};$('slots').append(btn);
 }
 const refs=C.references(S.data,(custom?189:201)+S.slot);
 $('macroReferences').textContent=refs.length?'Assigned to '+refs.map(r=>`${C.layers[r.layer]} R${Math.floor(r.index/8)+1} C${r.index%8+1}`).join(' · '):'Not assigned to a key. Use "Assign slot to selected key" to connect this sequence to a keypad key.';
 S.events=C.events(S.data,b,S.slot);S.eventDirty=false;
 $('sequenceInput').value=C.compact(S.events)==='(empty)'?'':C.compact(S.events);
 $('macroSummary').textContent=C.compact(S.events);
 renderEvents();
}
function renderEvents(){
 $('events').replaceChildren();
 S.events.forEach((e,i)=>{
  const row=el('tr');row.append(el('td',String(i+1)));
  const mode=el('select');[['true','Down'],['false','Up']].forEach(([v,n])=>{const o=el('option',n);o.value=v;mode.append(o);});mode.value=String(e.down);mode.setAttribute('aria-label','Event '+(i+1)+' direction');
  const key=el('select');makeOptions(key,true);ensureOption(key,e.code);key.setAttribute('aria-label','Event '+(i+1)+' key');
  const delay=el('input');delay.type='number';delay.min=0;delay.max=12700;delay.step=100;delay.value=e.delay;delay.disabled=bank()==='quick';delay.setAttribute('aria-label','Event '+(i+1)+' delay ms');
  mode.onchange=()=>{e.down=mode.value==='true';eventDirty();};key.onchange=()=>{e.code=Number(key.value);eventDirty();};delay.onchange=()=>{e.delay=Number(delay.value);eventDirty();};
  for(const control of [mode,key,delay]){const td=el('td');td.append(control);row.append(td);}
  const actions=el('td');
  for(const [text,title,fn]of [['↑','Move event up',()=>{if(i)[S.events[i-1],S.events[i]]=[S.events[i],S.events[i-1]];}],['↓','Move event down',()=>{if(i<S.events.length-1)[S.events[i+1],S.events[i]]=[S.events[i],S.events[i+1]];}],['×','Delete event',()=>S.events.splice(i,1)]]){
   const btn=el('button',text);btn.title=title;btn.setAttribute('aria-label',title+' '+(i+1));btn.onclick=()=>{fn();eventDirty();renderEvents();};actions.append(btn);
  }
  row.append(actions);$('events').append(row);
 });
 $('eventCount').textContent=`${S.events.length} / ${bank()==='custom'?84:S.data.extra.length?76:48} events`;
 $('eventMessage').textContent=S.eventDirty?'Event edits are not applied yet. Finish the sequence, then click Apply event edits.':'';
}
function eventDirty(){S.eventDirty=true;$('eventMessage').textContent='Event edits are not applied yet. Click Apply event edits when finished.';}
function applyEvents(ev){const b=bank(),slot=S.slot;commit(d=>C.writeEvents(d,b,slot,ev));S.eventDirty=false;renderMacro();status('Macro edited. Upload changes saves it to the keypad.');}
function payload(){return {...C.encode(S.data),revision:S.base.revision};}
function load(config){
 S.base=config;S.data=C.decode(config);S.connected=true;S.undo=[];S.redo=[];S.eventDirty=false;S.events=[];
 render();status('Actual device configuration loaded.');
}
async function refresh(){const res=await api('devices');const old=$('devices').value;$('devices').replaceChildren();
 if(!res.devices.length){const o=el('option','No ps2avrGB device found');o.value='';$('devices').append(o);}
 res.devices.forEach((d,i)=>{const o=el('option',`${d.Name} · ${d.Transport}`);o.value=d.Path;$('devices').append(o);if(d.Path===old)$('devices').value=old;});
 $('read').disabled=!res.devices.length;
}
async function log(){const res=await api('log');$('log').textContent=res.lines.join('\n');}
function save(text,name,mime='application/json'){const u=URL.createObjectURL(new Blob([text],{type:mime}));const a=el('a');a.href=u;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(u),1000);}
async function previewUpload(){
 if(S.eventDirty)throw Error('Apply the event-table edits first.');
 const result=await busy('Preparing upload...',()=>api('preview',payload()));if(!result)return;
 const p=result.plan;
 $('uploadSummary').textContent=`${p.changedKeys} changed key assignment(s); Custom Macro slots: ${p.customSlots.join(', ')||'none'}; Quick Macro slots: ${p.quickSlots.join(', ')||'none'}.`;
 $('uploadNote').textContent=p.note;
 if(p.flashPages.length)$('uploadSummary').append(el('p','Configuration pages: '+p.flashPages.map(n=>'0x'+n.toString(16).toUpperCase()).join(', ')));
 $('flashAckRow').hidden=!p.requiresBootloader;$('flashAck').checked=false;$('confirmUpload').disabled=p.requiresBootloader;
 $('uploadDialog').dataset.flash=String(p.requiresBootloader);$('uploadDialog').showModal();
}
function init(){
 makeOptions($('assignment'));makeOptions($('eventKey'),true);
 for(let i=0;i<120;i++){const o=el('option',`Row ${Math.floor(i/8)+1}, column ${i%8+1}`);o.value=i;$('matrixPosition').append(o);}
 document.querySelectorAll('nav button').forEach(b=>b.onclick=()=>showTab(b.dataset.tab));
 bind('refresh',()=>busy('Finding keypad...',refresh));
 bind('read',async()=>{
  if((dirty()||S.eventDirty)&&!confirm('Discard pending edits and read the actual keypad?'))return;
  const p=$('devices').value;if(!p)throw Error('No keypad selected.');
  S.connected=false;render();
  await busy('Reading actual key assignments and macros...',async()=>{const res=await api('read',{path:p});load(res.config);});
  await log();
 });
 bind('assign',()=>assign(Number($('assignment').value)));
 bind('undo',()=>{if(!discardEventEdits())return;if(S.undo.length){S.redo.push(C.clone(S.data));S.data=S.undo.pop();render();}});
 bind('redo',()=>{if(!discardEventEdits())return;if(S.redo.length){S.undo.push(C.clone(S.data));S.data=S.redo.pop();render();}});
 bind('editMacro',()=>{const m=macroCode(currentCode());if(m){S.slot=m.slot;showTab(m.bank);}});
 $('view').onchange=render;$('search').oninput=renderPalette;$('category').onchange=renderPalette;
 bind('setMatrixPosition',()=>{
  if(S.physicalIndex<0)throw Error('Select one of the 23 physical keys first.');
  const n=Number($('matrixPosition').value),other=S.layout.indexOf(n);
  if(other>=0&&other!==S.physicalIndex){S.layout[other]=S.layout[S.physicalIndex];}
  S.layout[S.physicalIndex]=n;S.index=n;render();status('Display mapping adjusted; no device bytes changed.');
 });
 bind('resetLayout',()=>{S.layout=C.physical.slice();S.index=0;S.physicalIndex=0;render();});
 $('shortcutCapture').onfocus=()=>{S.shortcut=[];$('shortcutCapture').value='Press shortcut...';$('assignShortcut').disabled=true;};
 $('shortcutCapture').onkeydown=e=>{
  e.preventDefault();if(e.repeat)return;const code=C.eventCode(e);if(code===null){error(Error('Unsupported key '+e.code));return;}
  if(!S.shortcut.includes(code))S.shortcut.push(code);
  $('shortcutCapture').value=S.shortcut.map(C.name).join('+');$('assignShortcut').disabled=!S.data||!S.shortcut.length;
 };
 bind('assignShortcut',()=>{
  if(!S.shortcut.length)return;const slot=C.freeCustom(S.data);if(slot<0)throw Error('No empty unassigned Custom Macro slot. Edit a slot manually.');
  const ev=[...S.shortcut.map(code=>({code,down:true,delay:0})),...S.shortcut.slice().reverse().map(code=>({code,down:false,delay:0}))];
  commit(d=>{C.writeEvents(d,'custom',slot,ev);for(let l=0;l<4;l++)if($('allLayers').checked||l===S.layer)d.keymap[l*120+S.index]=189+slot;});
  status('Shortcut assigned through Custom '+(slot+1)+'. Upload changes to save.');
 });
 bind('parseSequence',()=>applyEvents(C.sequence($('sequenceInput').value)));
 bind('parseText',()=>applyEvents(C.usText($('textInput').value)));
 bind('saveEvents',()=>applyEvents(S.events));
 bind('addTap',()=>{const code=Number($('eventKey').value);S.events.push({code,down:true,delay:0},{code,down:false,delay:0});eventDirty();renderEvents();});
 bind('clearMacro',()=>{if(confirm('Clear this macro slot in the editor?'))applyEvents([]);});
 bind('copyMacro',()=>{S.clipboard=C.clone(S.events);status('Macro copied within this app.');});
 bind('pasteMacro',()=>{if(!S.clipboard)throw Error('Copy a macro first.');applyEvents(C.clone(S.clipboard));});
 bind('macroUndo',()=>{
  if(!confirm('Restore this slot from the last USB read?'))return;
  const b=bank(),slot=S.slot,original=C.decode(S.base);
  commit(d=>{if(b==='custom')d.custom.splice(slot*168,168,...original.custom.slice(slot*168,(slot+1)*168));else{d.quick.splice(slot*48,48,...original.quick.slice(slot*48,(slot+1)*48));if(d.extra.length)d.extra.splice(slot*28,28,...original.extra.slice(slot*28,(slot+1)*28));}});
 });
 bind('assignSlot',()=>{assign((bank()==='custom'?189:201)+S.slot);status(`Slot assigned to ${C.layers[S.layer]} R${Math.floor(S.index/8)+1} C${S.index%8+1}. Upload changes to save.`);});
 bind('export',()=>{if(S.base)save(JSON.stringify(S.base,null,2),'AMAG23-device-backup.json');status('Exported the last actual USB read, not pending edits.');});
 bind('exportKeymap',()=>save(C.hexFile(S.data,'keymap'),'AMAG23-keymap.hex','text/plain'));
 bind('exportMacros',()=>save(C.hexFile(S.data,'custom'),'AMAG23-custom-macros.hex','text/plain'));
 bind('import',()=>{$('file').value='';$('file').click();});
 $('file').onchange=async()=>{try{
  const f=$('file').files[0];if(!f)return;if(f.size>131072)throw Error('File is too large.');const t=await f.text();
  const next=f.name.toLowerCase().endsWith('.hex')?C.importHex(t,S.data):C.decode(JSON.parse(t));
  if(next.extra.length!==S.data.extra.length)throw Error('Backup quick-macro capacity does not match this device.');
  commit(d=>Object.assign(d,next));status('File staged in the editor. Nothing has been uploaded. Dual-action and lighting settings are not imported.');
 }catch(e){error(e);}};
 bind('upload',previewUpload);bind('cancelUpload',()=>$('uploadDialog').close());
 $('flashAck').onchange=()=>{$('confirmUpload').disabled=!$('flashAck').checked;};
 bind('confirmUpload',async()=>{
  const flashing=$('uploadDialog').dataset.flash==='true';if(flashing&&!$('flashAck').checked)return;
  $('uploadDialog').close();
  await busy('Backing up, uploading and verifying...',async()=>{
   try{const result=await api('upload',{...payload(),flashConfirmed:flashing});load(result.config);status('Upload verified. Device configuration matches the editor.');}
   catch(e){S.connected=false;render();throw e;}
  });await log();
 });
 bind('refreshLog',log);
 bind('recover',async()=>{if(confirm('Send only the command to leave the existing bootloader? No configuration pages will be written.')){await busy('Returning to keyboard mode...',()=>api('recover',{}));await refresh();await log();}});
 bind('identify',async()=>{
  const r=await busy('Starting key identification...',()=>api('identify',{enabled:true}));if(!r)return;
  S.identify=true;$('identifyInput').value='';$('identifyResult').textContent='';$('identifyDialog').showModal();$('identifyInput').focus();render();
 });
 $('identifyInput').oninput=()=>{
  const m=$('identifyInput').value.match(/\.(\d+),(\d+)\./);if(!m)return;
  const col=Number(m[1]),row=Number(m[2]);if(col>=8||row>=15){$('identifyResult').textContent='Outside the expected 8 × 15 matrix.';return;}
  S.index=row*8+col;S.physicalIndex=S.layout.indexOf(S.index);if(S.physicalIndex<0)$('view').value='matrix';
  $('identifyResult').textContent=`This key is row ${row+1}, column ${col+1}: ${C.name(currentCode())}.`;$('identifyInput').value='';renderMapper();
 };
 const stopIdentify=async()=>{const r=await busy('Restoring normal keyboard mode...',()=>api('identify',{enabled:false}));if(r){S.identify=false;$('identifyDialog').close();render();}};
 bind('stopIdentify',stopIdentify);$('identifyDialog').addEventListener('cancel',e=>{e.preventDefault();stopIdentify().catch(error);});
 bind('startRecord',()=>{S.recording=true;S.recorded=[];S.held.clear();S.lastTime=0;$('recorder').classList.add('recording');$('recorder').textContent='Recording. Type your sequence here.';$('recorder').focus();$('startRecord').disabled=true;$('stopRecord').disabled=false;});
 const record=(e,down)=>{
  if(!S.recording)return;e.preventDefault();if(e.repeat)return;const code=C.eventCode(e);if(code===null){error(Error('Unsupported key '+e.code));return;}
  if(down===S.held.has(code))return;
  const now=performance.now();if($('recordDelays').checked&&S.recorded.length&&S.lastTime)S.recorded.at(-1).delay=Math.min(12700,Math.round((now-S.lastTime)/100)*100);
  S.lastTime=now;down?S.held.add(code):S.held.delete(code);S.recorded.push({code,down,delay:0});$('recorder').textContent=C.describe(S.recorded);
 };
 $('recorder').onkeydown=e=>record(e,true);$('recorder').onkeyup=e=>record(e,false);
 bind('stopRecord',()=>{
  S.recording=false;for(const code of S.held)S.recorded.push({code,down:false,delay:0});S.held.clear();$('recorder').classList.remove('recording');$('startRecord').disabled=false;$('stopRecord').disabled=true;
  applyEvents(S.recorded);
 });
 window.addEventListener('beforeunload',e=>{if(dirty()||S.eventDirty||S.identify){e.preventDefault();e.returnValue='';}});
 render();
 busy('Opening local app...',async()=>{S.session=await api('session');$('appVersion').textContent = 'v'+S.session.version;window.AmagAppearance.load(S.session.theme);$('demo').hidden=!S.session.demo;await refresh();await log();});
}
init();
