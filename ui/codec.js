/* Independent ps2avrGB configuration codec. No framework or remote dependency. */
(function(root){
'use strict';
const keys = new Map([[0,'None'],[255,'Unused']]);
const add=(n,names)=>names.forEach((s,i)=>keys.set(n+i,s));
add(4,'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split(''));
add(30,['1','2','3','4','5','6','7','8','9','0']);
add(40,['Enter','Esc','Backspace','Tab','Space','Minus','Equal','LeftBracket','RightBracket','Backslash','Hash','Semicolon','Quote','Backquote','Comma','Period','Slash','CapsLock']);
add(58,Array.from({length:12},(_,i)=>'F'+(i+1)));
add(70,['PrintScreen','ScrollLock','Pause','Insert','Home','PageUp','Delete','End','PageDown','Right','Left','Down','Up','NumLock','NumpadDivide','NumpadMultiply','NumpadSubtract','NumpadAdd','NumpadEnter','Numpad1','Numpad2','Numpad3','Numpad4','Numpad5','Numpad6','Numpad7','Numpad8','Numpad9','Numpad0','NumpadDecimal','IntlBackslash','Menu']);
add(103,['Ctrl','Shift','Alt','Win','RightCtrl','RightShift','RightAlt','RightWin']);
add(113,['Power','Sleep','Wake','Email','Search','BrowserHome','BrowserBack','BrowserForward','BrowserStop','Refresh','Favorites','NextTrack','PreviousTrack','StopMedia','PlayPause','Mute','VolumeUp','VolumeDown','Media','Calculator','MyComputer','Screensaver','Record','Rewind','Minimize','Eject']);
add(141,['Hanja','Hangul','KeyboardPower','NumpadEqual',...Array.from({length:12},(_,i)=>'F'+(i+13)),'NumpadComma','NumpadEqualSign']);
keys.set(176,'Esc/Tilde');
add(189,Array.from({length:12},(_,i)=>'Custom '+(i+1)));
add(201,Array.from({length:12},(_,i)=>'Quick '+(i+1)));
add(214,Array.from({length:24},(_,i)=>'Dual action '+(i+1)));
[[254,'FN'],[253,'LED mode'],[252,'Toggle FN2'],[251,'Normal layer'],[250,'LED brighter'],[249,'LED dimmer'],[248,'FN2'],[247,'Toggle FN3'],[246,'FN3'],[245,'LED on/off'],[244,'Lock keys'],[243,'Lock Win'],[242,'Quick record'],[241,'RGB mode']].forEach(([n,s])=>keys.set(n,s));
const name=n=>keys.get(n)||'Code 0x'+n.toString(16).padStart(2,'0').toUpperCase();
const physical=[0,1,2,3,4,5,8,9,10,11,12,13,16,17,18,19,20,21,24,25,26,27,29];
const layers=['Normal','FN','FN2','FN3'];
const parseHex=(s,n)=>{if(typeof s!=='string'||s.length!==n*2||!/^[\da-f]*$/i.test(s))throw Error('Invalid '+n+'-byte configuration block.');return Array.from({length:n},(_,i)=>parseInt(s.slice(2*i,2*i+2),16));};
const hex=a=>a.map(x=>{if(!Number.isInteger(x)||x<0||x>255)throw Error('Byte out of range');return x.toString(16).padStart(2,'0');}).join('').toUpperCase();
function decode(c){
 if(c.schema!=='amag23-native-config/v1')throw Error('Not an AMAG23 Classic native backup. Old draft profiles are not device backups.');
 if(c.extra!==''&&c.extra?.length!==672)throw Error('Invalid quick-macro extension size.');
 return {keymap:parseHex(c.keymap,480),custom:parseHex(c.custom,2016),quick:parseHex(c.quick,576),extra:parseHex(c.extra,c.extra.length/2)};
}
const encode=d=>Object.fromEntries(['keymap','custom','quick','extra'].map(k=>[k,hex(d[k])]));
const clone=x=>JSON.parse(JSON.stringify(x));
function macroBytes(d,bank,slot){
 if(slot<0||slot>11)throw Error('Invalid macro slot.');
 return bank==='custom'?d.custom.slice(slot*168,(slot+1)*168):d.quick.slice(slot*48,(slot+1)*48).concat(d.extra.length?d.extra.slice(slot*28,(slot+1)*28):[]);
}
function events(d,bank,slot){
 const a=macroBytes(d,bank,slot),held=new Set(),out=[];
 for(let i=0;i<a.length;i+=(bank==='custom'?2:1)){
  const code=a[i]; if(code===0||code===255)break;
  const down=!held.has(code); down?held.add(code):held.delete(code);
  out.push({code,down,delay:bank==='custom'?(a[i+1]&127)*100:0,flag:bank==='custom'?a[i+1]:null});
 }
 return out;
}
function validateEvents(ev,bank,capacity){
 const held=new Set();
 if(ev.length>capacity)throw Error('Macro has '+ev.length+' events; this slot holds '+capacity+'. A tap uses two events.');
 ev.forEach((e,i)=>{
  if(!Number.isInteger(e.code)||!keys.has(e.code)||e.code<4||e.code>158)throw Error('Unsupported macro key at event '+(i+1)+'. Use a keyboard or media key.');
  if(e.down===held.has(e.code))throw Error('Event '+(i+1)+': '+name(e.code)+' is already '+(e.down?'held':'released')+'.');
  e.down?held.add(e.code):held.delete(e.code);
  if(!Number.isInteger(e.delay)||e.delay<0||e.delay>12700||e.delay%100)throw Error('Delays must be multiples of 100 ms, from 0 to 12700.');
  if(bank==='quick'&&e.delay)throw Error('Quick macros do not support delays. Use a Custom macro.');
 });
 if(held.size)throw Error('Release these keys before saving the sequence: '+[...held].map(name).join(', '));
}
function writeEvents(d,bank,slot,ev){
 const capacity=bank==='custom'?84:(d.extra.length?76:48); validateEvents(ev,bank,capacity);
 if(bank==='custom'){
  const b=Array(168).fill(0);ev.forEach((e,i)=>{b[2*i]=e.code;b[2*i+1]=(e.down?128:0)+e.delay/100;});d.custom.splice(slot*168,168,...b);
 }else{
  const b=Array(capacity).fill(0);ev.forEach((e,i)=>b[i]=e.code);d.quick.splice(slot*48,48,...b.slice(0,48));if(d.extra.length)d.extra.splice(slot*28,28,...b.slice(48));
 }
}
function resolve(s){
 const aliases={control:'ctrl',lctrl:'ctrl',lshift:'shift',lalt:'alt',super:'win',meta:'win',windows:'win',escape:'esc',return:'enter',del:'delete',pgup:'pageup',pgdn:'pagedown','+':'equal'};
 const value=aliases[s.toLowerCase()]||s.toLowerCase();
 for(const [c,n]of keys)if(n.toLowerCase()===value)return c;
 throw Error('Unknown key: '+s+'. Use key names such as Enter, Comma or F5.');
}
function sequence(text){
 let result=[];
 for(const raw of text.split(/[,\n]+/).map(s=>s.trim()).filter(Boolean)){
  const wait=raw.match(/^(?:wait|delay)\s*(?:\(\s*(\d+)\s*\)|(\d+))$/i);
  if(wait){if(!result.length)throw Error('Put a wait after a key event.');result.at(-1).delay+=Number(wait[1]||wait[2]);continue;}
  const action=raw.match(/^(down|up)\s+(.+)$/i);
  if(action){result.push({code:resolve(action[2]),down:action[1].toLowerCase()==='down',delay:0});continue;}
  const codes=raw.split('+').map(s=>resolve(s.trim()));
  if(new Set(codes).size!==codes.length)throw Error('A shortcut cannot contain the same key twice.');
  result.push(...codes.map(code=>({code,down:true,delay:0})),...codes.slice().reverse().map(code=>({code,down:false,delay:0})));
 }
 return result;
}
function usText(text){
 const plain='1234567890-=[]\\;\'`,./ ',shifted='!@#$%^&*()_+{}|:"~<>? ';
 const ids=[30,31,32,33,34,35,36,37,38,39,45,46,47,48,49,51,52,53,54,55,56,44];
 const out=[];
 for(const ch of text){
  let code,shift=false;
  if(/[a-z]/i.test(ch)&&ch.length===1){code=ch.toUpperCase().charCodeAt(0)-61;shift=ch!==ch.toLowerCase();}
  else if(ch==='\n')code=40;else if(ch==='\t')code=43;
  else if(plain.includes(ch))code=ids[plain.indexOf(ch)];
  else if(shifted.includes(ch)){code=ids[shifted.indexOf(ch)];shift=true;}
  else throw Error('Text conversion supports US keyboard characters only. Cannot convert '+JSON.stringify(ch)+'. Use key recording for your keyboard layout.');
  if(shift)out.push({code:104,down:true,delay:0});
  out.push({code,down:true,delay:0},{code,down:false,delay:0});
  if(shift)out.push({code:104,down:false,delay:0});
 }
 return out;
}
function eventCode(e){
 if(/^Key[A-Z]$/.test(e.code))return e.code.charCodeAt(3)-61;
 if(/^Digit[0-9]$/.test(e.code))return e.code[5]==='0'?39:29+Number(e.code[5]);
 const table={ControlLeft:103,ShiftLeft:104,AltLeft:105,MetaLeft:106,ControlRight:107,ShiftRight:108,AltRight:109,MetaRight:110,Escape:41,ArrowRight:79,ArrowLeft:80,ArrowDown:81,ArrowUp:82,BracketLeft:47,BracketRight:48,Backquote:53};
 if(e.code in table)return table[e.code];
 try{return resolve(e.code);}catch{return null;}
}
function describe(ev){
 if(!ev.length)return '(empty)';
 return ev.map(e=>name(e.code)+(e.down?' ↓':' ↑')+(e.delay?' · '+e.delay+' ms':'')).join('  ');
}
function compact(ev){
 if(!ev.length)return '(empty)';
 const result=[];let i=0;
 while(i<ev.length){
  const first=i,down=[];while(i<ev.length&&ev[i].down&&!ev[i].delay){down.push(ev[i].code);i++;}
  let ok=down.length>0&&i+down.length<=ev.length;
  for(let k=0;ok&&k<down.length;k++)if(ev[i+k].down||ev[i+k].code!==down[down.length-1-k]||(k<down.length-1&&ev[i+k].delay))ok=false;
  if(ok){result.push(down.map(name).join('+'));i+=down.length;if(ev[i-1].delay)result.push('wait('+ev[i-1].delay+')');}
  else{i=first;const e=ev[i++];result.push((e.down?'Down ':'Up ')+name(e.code));if(e.delay)result.push('wait('+e.delay+')');}
 }
 return result.join(', ');
}
const references=(d,code)=>d.keymap.flatMap((c,i)=>c===code?[{layer:Math.floor(i/120),index:i%120}]:[]);
function freeCustom(d){for(let i=0;i<12;i++)if(!references(d,189+i).length&&!events(d,'custom',i).length)return i;return -1;}
function changes(a,b){
 const custom=[],quick=[];
 for(let i=0;i<12;i++){
  if(hex(macroBytes(a,'custom',i))!==hex(macroBytes(b,'custom',i)))custom.push(i);
  if(hex(macroBytes(a,'quick',i))!==hex(macroBytes(b,'quick',i)))quick.push(i);
 }
 return {keys:b.keymap.flatMap((v,i)=>v!==a.keymap[i]?[i]:[]),custom,quick};
}
function hexFile(d,bank){
 const arr=bank==='keymap'?d.keymap:d.custom,start=bank==='keymap'?0x6600:0x6800;const lines=[];
 for(let off=0;off<arr.length;off+=16){const b=arr.slice(off,off+16),addr=start+off,record=[b.length,addr>>8,addr&255,0,...b];record.push((-record.reduce((s,x)=>s+x,0))&255);lines.push(':'+hex(record));}
 return lines.join('\r\n')+'\r\n:00000001FF\r\n';
}
function importHex(text,d){
 const next=clone(d),seen=new Set();let count=0,ended=false;
 for(const line of text.trim().split(/\r?\n/).map(s=>s.trim()).filter(Boolean)){
  if(ended)throw Error('HEX content after EOF.');
  if(!/^:[\da-f]+$/i.test(line)||line.length%2!==1)throw Error('Malformed Intel HEX line.');
  const b=parseHex(line.slice(1),(line.length-1)/2);if(b.length<5||b.length!==b[0]+5||b.reduce((s,x)=>s+x,0)%256)throw Error('HEX length or checksum mismatch.');
  const type=b[3],address=b[1]*256+b[2];
  if(type===1){if(b[0]!==0||address!==0)throw Error('Invalid EOF');ended=true;continue;}
  if(type!==0)throw Error('Only configuration-data HEX records are accepted, not firmware or extended addresses.');
  for(let i=0;i<b[0];i++){
   const a=address+i;if(seen.has(a))throw Error('Overlapping HEX records.');seen.add(a);
   if(a>=0x6600&&a<0x67e0)next.keymap[a-0x6600]=b[4+i];
   else if(a>=0x6800&&a<0x6fe0)next.custom[a-0x6800]=b[4+i];
   else throw Error('HEX includes an address outside keymap/custom-macro data: 0x'+a.toString(16));count++;
  }
 }
 if(!count)throw Error('No configuration bytes found.');return next;
}
const api={keys,name,physical,layers,parseHex,hex,decode,encode,clone,macroBytes,events,validateEvents,writeEvents,resolve,sequence,usText,eventCode,describe,compact,references,freeCustom,changes,hexFile,importHex};
if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.Codec=api;
})(typeof window!=='undefined'?window:globalThis);
