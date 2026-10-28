"""Chromium integration tests against an explicitly simulated HTTP API. No USB."""
import json, hashlib, re, sys
from types import SimpleNamespace
from pathlib import Path
from urllib.parse import urlparse
from playwright.sync_api import sync_playwright, expect
ROOT=Path(__file__).resolve().parents[1]
positions=[0,1,2,3,4,5,8,9,10,11,12,13,16,17,18,19,20,21,24,25,26,27,29]
keys=[41,189,31,32,33,34,43,20,26,8,21,28,254,4,22,7,9,10,29,27,6,5,44]
a=bytearray(480)
for layer in range(4):
 for p,k in zip(positions,keys): a[layer*120+p]=k
m=bytearray(2016);m[:8]=bytes([103,128,6,128,6,0,103,0])
config=dict(schema='amag23-native-config/v1',capturedAt='2026-10-28T10:00:00Z',firmware='1.5.0',keymap=a.hex().upper(),custom=m.hex().upper(),quick='00'*576,extra='00'*336,dual='00'*128,info='00'*50)
def rev(c): return hashlib.sha256(''.join(c[k] for k in ['keymap','custom','quick','extra','dual']).encode()).hexdigest().upper()
config['revision']=rev(config)
state={'config':config,'upload':0,'fail_read':False,'fail_upload':False,'calls':[],'theme':'system'}
def handle(route):
 req=route.request;path=urlparse(req.url).path
 if path.startswith('/api/'):
  assert req.headers.get('x-amag-token')=='testtoken'
  body=req.post_data_json if req.method=='POST' else None
  state['calls'].append(path)
  result={'ok':True}
  if path=='/api/session':result.update(version=(ROOT/'VERSION').read_text().strip(),theme=state['theme'],demo=True,backups='Local demo test backups')
  elif path=='/api/theme':
   assert body['theme'] in ('system','light','dark');state['theme']=body['theme'];result['theme']=state['theme']
  elif path=='/api/devices':result['devices']=[dict(Path='DEMO',Name='ps2avrGB',Transport='Simulated USB')]
  elif path=='/api/read':
   result.update({'ok':False,'error':'Simulated HID access denied'}) if state['fail_read'] else result.update(config=state['config'])
  elif path=='/api/log':result.update(lines=['Demo transport: no physical USB device used.'])
  elif path=='/api/preview':
   b=state['config'];keycount=sum(x!=y for x,y in zip(bytes.fromhex(b['keymap']),bytes.fromhex(body['keymap'])))
   cs=[i+1 for i in range(12) if b['custom'][i*336:(i+1)*336]!=body['custom'][i*336:(i+1)*336]]
   qs=[i+1 for i in range(12) if b['quick'][i*96:(i+1)*96]!=body['quick'][i*96:(i+1)*96]]
   need=bool(keycount or cs)
   result['plan']=dict(changedKeys=keycount,customSlots=cs,quickSlots=qs,requiresBootloader=need,flashPages=[0x6600] if need else [],note='SIMULATED TEST: native hardware has not been accessed.')
  elif path=='/api/upload':
   if state['fail_upload']:result.update(ok=False,error='Simulated read-back verification failure')
   else:
    assert body['revision']==state['config']['revision'];state['upload']+=1
    for k in ['keymap','custom','quick','extra']:state['config'][k]=body[k]
    state['config']['revision']=rev(state['config']);result['config']=state['config']
  route.fulfill(status=200 if result['ok'] else 400,content_type='application/json',body=json.dumps(result));return
 name='index.html' if path=='/' else path.lstrip('/')
 file=ROOT/'ui'/name
 if not file.is_file():route.fulfill(status=404,body='Not found');return
 mime={'.html':'text/html','.js':'text/javascript','.css':'text/css'}[file.suffix]
 route.fulfill(status=200,content_type=mime,body=file.read_text(),headers={'Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'"})
with sync_playwright() as p:
 browser=p.chromium.launch(**({'executable_path':'/usr/bin/chromium'} if Path('/usr/bin/chromium').is_file() else {}),headless=True,args=['--no-sandbox'])
 page=browser.new_page(viewport={'width':1360,'height':980},device_scale_factor=1)
 errors=[];page.on('pageerror',lambda e:errors.append(str(e)));page.on('dialog',lambda d:d.accept())
 def mock_call(url, method, body):
  class Route:
   request=SimpleNamespace(url=url, method=method, headers={'x-amag-token':'testtoken'},post_data_json=body)
   def fulfill(self, **kwargs): self.result=kwargs
  r=Route();handle(r);return {'status':r.result['status'],'body':r.result['body']}
 page.expose_function('mockCall',mock_call)
 html=(ROOT/'ui'/'index.html').read_text()
 html=re.sub(r'<script[^>]*></script>|<link[^>]*>', '', html)
 page.set_content(html)
 page.add_style_tag(content=(ROOT/'ui'/'app.css').read_text())
 page.evaluate("""() => {
  Object.defineProperty(window,'sessionStorage',{value:{getItem:()=> 'testtoken',setItem:()=>{}}});
  window.fetch=async(url,options={})=>{const r=await window.mockCall(url,options.method||'GET',options.body?JSON.parse(options.body):null);return new Response(r.body,{status:r.status,headers:{'Content-Type':'application/json'}});};
 }""")
 page.add_script_tag(content=(ROOT/'ui'/'theme.js').read_text())
 page.add_script_tag(content=(ROOT/'ui'/'codec.js').read_text())
 page.add_script_tag(content=(ROOT/'ui'/'app.js').read_text())
 expect(page.locator('#busy')).to_be_hidden()
 expect(page.locator('#upload')).to_be_disabled();expect(page.locator('#keypad button')).to_have_count(0)
 expect(page.locator('#appVersion')).to_have_text('v'+(ROOT/'VERSION').read_text().strip())
 assert page.locator('#appearance').input_value() == 'system'
 page.emulate_media(color_scheme='dark')
 expect(page.locator('html')).to_have_attribute('data-effective-theme','dark')
 assert page.evaluate('getComputedStyle(document.body).backgroundColor') == 'rgb(17, 24, 33)'
 page.locator('#appearance').select_option('light')
 expect(page.locator('html')).to_have_attribute('data-effective-theme','light')
 assert state['theme']=='light'
 page.locator('#appearance').select_option('dark')
 expect(page.locator('html')).to_have_attribute('data-effective-theme','dark')
 assert state['theme']=='dark'
 page.emulate_media(color_scheme='light')
 expect(page.locator('html')).to_have_attribute('data-effective-theme','dark')
 page.locator('#appearance').select_option('system')
 expect(page.locator('html')).to_have_attribute('data-effective-theme','light')
 page.emulate_media(color_scheme='dark')
 expect(page.locator('html')).to_have_attribute('data-effective-theme','dark')
 page.emulate_media(color_scheme='light')
 expect(page.locator('html')).to_have_attribute('data-effective-theme','light')
 page.locator('#read').click();expect(page.locator('#busy')).to_be_hidden()
 expect(page.locator('#keypad button')).to_have_count(23)
 page.locator('#keypad button[data-index="1"]').click()
 expect(page.locator('#peek')).to_have_text('Ctrl+C')
 if '--screenshots' in sys.argv:
  (ROOT/'docs').mkdir(exist_ok=True)
  page.screenshot(path=str(ROOT/'docs'/'app-light.png'),full_page=True)
  page.locator('#appearance').select_option('dark')
  page.screenshot(path=str(ROOT/'docs'/'app-dark.png'),full_page=True)
  page.locator('#appearance').select_option('system')
 page.locator('#editMacro').click();expect(page.locator('#events tr')).to_have_count(4)
 page.locator('#sequenceInput').fill('Ctrl+C, wait(300), Alt+Tab, Enter');page.locator('#parseSequence').click()
 expect(page.locator('#macroSummary')).to_contain_text('wait(300)');expect(page.locator('#upload')).to_be_enabled()
 # Screenshots cover the primary screen, not intermediary edit states.
 page.locator('#undo').click();expect(page.locator('#macroSummary')).to_have_text('Ctrl+C')
 page.locator('#redo').click();expect(page.locator('#macroSummary')).to_contain_text('Alt+Tab')
 page.get_by_role('tab',name='Key Mapper',exact=True).click()
 page.locator('#keypad button[data-index="0"]').click()
 page.locator('#search').fill('F5');page.locator('#palette button',has_text='F5').click()
 expect(page.locator('#editedAssignment')).to_have_text('F5');expect(page.locator('#originalAssignment')).to_have_text('Esc')
 page.locator('#shortcutCapture').click();page.keyboard.press('Control+Shift+s');page.locator('#assignShortcut').click()
 expect(page.locator('#peek')).to_have_text('Ctrl+Shift+S')
 page.locator('#upload').click();expect(page.locator('#uploadDialog')).to_be_visible();expect(page.locator('#confirmUpload')).to_be_disabled()
 page.locator('#flashAck').check();page.locator('#confirmUpload').click();expect(page.locator('#busy')).to_be_hidden()
 assert state['upload']==1
 expect(page.locator('#upload')).to_be_disabled();expect(page.locator('#status')).to_contain_text('Upload verified')
 page.get_by_role('tab',name='Quick Macro',exact=True).click();page.locator('#sequenceInput').fill('Ctrl+V');page.locator('#parseSequence').click()
 page.locator('#upload').click();expect(page.locator('#flashAckRow')).to_be_hidden();expect(page.locator('#confirmUpload')).to_be_enabled()
 page.locator('#confirmUpload').click();expect(page.locator('#busy')).to_be_hidden();assert state['upload']==2
 page.locator('#sequenceInput').fill('A, wait(250)');page.locator('#parseSequence').click();expect(page.locator('#error')).to_be_visible()
 page.locator('#sequenceInput').fill('B');page.locator('#parseSequence').click();state['fail_upload']=True
 page.locator('#upload').click();page.locator('#confirmUpload').click();expect(page.locator('#busy')).to_be_hidden()
 expect(page.locator('#error')).to_contain_text('Simulated read-back verification failure');expect(page.locator('#upload')).to_be_disabled()
 state['fail_read']=True;page.locator('#read').click();expect(page.locator('#busy')).to_be_hidden();expect(page.locator('#error')).to_contain_text('Simulated HID access denied')
 expect(page.locator('#upload')).to_be_disabled()
 state['fail_read']=False;state['fail_upload']=False;page.locator('#read').click();expect(page.locator('#busy')).to_be_hidden()
 page.get_by_role('tab',name='Key Mapper',exact=True).click();page.locator('#view').select_option('matrix');expect(page.locator('#keypad button')).to_have_count(120)
 page.locator('#view').select_option('keypad');page.set_viewport_size({'width':720,'height':980})
 assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Horizontal overflow'
 assert not errors, errors
 browser.close()
print('PASS Chromium integration: OS/theme syncing and persistence, empty state, actual-read rendering, macro decoding/editing, undo/redo, shortcut recording, conditional upload confirmation, verified-save UI, native error UI, matrix view, responsive layout. Simulated HTTP API only; localhost transport/CSP are not exercised by this test.')
