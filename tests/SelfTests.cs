using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class ClassicSelfTests {
    private static void Check(bool pass,string message) {if(!pass)throw new Exception("SELF-TEST FAILED: "+message);}
    private static void Reject(Action action,string message){try{action();}catch{return;}throw new Exception("Expected rejection: "+message);}
    private sealed class FaultHid : IClassicHid {
        public ClassicMockHid inner=new ClassicMockHid(); public bool failPage=false,corruptReadback=false,badGeometry=false; private bool uploaded=false;
        public List<ClassicNative.DeviceInfo> Devices(bool boot){return inner.Devices(boot);}
        public ClassicNative.FeatureResult Get(string p,int id,int len){
            var r=inner.Get(p,id,len);
            if(inner.boot&&badGeometry)r.Bytes[1]=64;
            if(uploaded&&corruptReadback&&!inner.boot&&len==121)r.Bytes[0]^=1;
            return r;
        }
        public void Set(string p,byte[] b){
            if(inner.boot&&b[0]==2){if(failPage)throw new IOException("Injected page-write failure");uploaded=true;}
            inner.Set(p,b);
        }
        public void Pause(int ms){}
    }
    public static string[] Run(){
        var results=new List<string>();string dir=Path.Combine(Path.GetTempPath(),"AMAG23-test-"+Guid.NewGuid().ToString("N"));
        try {
            var mock=new ClassicMockHid();var engine=new ClassicEngine(dir,mock);var before=engine.Connect("DEMO-KEYPAD");
            Check(before.keymap.Length==960&&before.custom.Length==4032&&before.quick.Length==1152&&before.extra.Length==672,"full read sizes");
            Check(before.keymap.Substring(2,2)=="BD","existing Custom 1 mapping is read, not invented");
            results.Add("PASS full native configuration read and revision");
            Check(File.Exists(engine.lastBackup),"connect backup exists");results.Add("PASS persisted backup verification");
            var map=ClassicEngine.Bytes(before.keymap,480);map[0]=58;
            var plan=engine.Preview(ClassicEngine.Hex(map),before.custom,before.quick,before.extra);
            Check(plan.changedKeys==1&&plan.flashPages.SequenceEqual(new[]{0x6600}),"changed-page planning");results.Add("PASS page plan and key diff");
            var allFlash=(byte[])mock.flash.Clone();
            var after=engine.Upload(before.revision,ClassicEngine.Hex(map),before.custom,before.quick,before.extra,true);
            Check(after.keymap==ClassicEngine.Hex(map),"verified keymap upload");
            Check(mock.flash.Take(0x6600).SequenceEqual(allFlash.Take(0x6600))&&mock.flash.Skip(0x6680).SequenceEqual(allFlash.Skip(0x6680)),"untouched flash remains unchanged");
            Check(mock.trace.Any(x=>x=="SET 2 132 02006600"),"exact configuration packet header");
            Check(!mock.boot,"returns to keyboard");results.Add("PASS real upload code: 132-byte flash packet, allowlist, return, read-back");
            var q=ClassicEngine.Bytes(after.quick,576);for(int i=0;i<q.Length;i++)q[i]=(byte)(4+i%26);
            var postQuick=engine.Upload(after.revision,after.keymap,after.custom,ClassicEngine.Hex(q),after.extra,false);
            Check(mock.quick.SequenceEqual(q),"quick-macro overlapping padding restored by whole-bank upload");results.Add("PASS complete quick bank and overlap preservation");
            var ext=ClassicEngine.Bytes(postQuick.extra,336);for(int i=0;i<ext.Length;i++)ext[i]=(byte)(4+i%26);
            var postExt=engine.Upload(postQuick.revision,postQuick.keymap,postQuick.custom,postQuick.quick,ClassicEngine.Hex(ext),false);
            Check(mock.extra.SequenceEqual(ext),"extra bank complete");results.Add("PASS complete extended quick bank");
            var custom=ClassicEngine.Bytes(postExt.custom,2016);custom[167]=5;custom[168]=6;custom[169]=128;
            var postCustom=engine.Upload(postExt.revision,postExt.keymap,ClassicEngine.Hex(custom),postExt.quick,postExt.extra,true);
            Check(postCustom.custom==ClassicEngine.Hex(custom),"macro crossing page boundary");results.Add("PASS custom macro cross-page preservation");
            Reject(()=>engine.Upload("stale",postCustom.keymap,postCustom.custom,postCustom.quick,postCustom.extra,true),"stale revision");results.Add("PASS stale-window rejection");
            Reject(()=>engine.Preview("00",postCustom.custom,postCustom.quick,postCustom.extra),"invalid lengths");results.Add("PASS malformed configuration rejection");
            foreach(int address in new[]{0,0x6500,0x6580,0x6601,0x7000,0x7800})Reject(()=>ClassicEngine.PagePacket(address,new byte[128]),"page allowlist");results.Add("PASS firmware/bootloader/dual-action address rejection");
            map=ClassicEngine.Bytes(postCustom.keymap,480);map[2]=59;
            Reject(()=>engine.Upload(postCustom.revision,ClassicEngine.Hex(map),postCustom.custom,postCustom.quick,postCustom.extra,false),"missing consent");results.Add("PASS configuration-flash consent enforcement");
            mock.quick[10]^=1;
            Reject(()=>engine.Upload(postCustom.revision,ClassicEngine.Hex(map),postCustom.custom,postCustom.quick,postCustom.extra,true),"external change detected");results.Add("PASS optimistic concurrency: fresh read must match");
            foreach(string fault in new[]{"page","verify","geometry"}){
                var f=new FaultHid();var e=new ClassicEngine(dir,f);var original=e.Connect("DEMO-KEYPAD");
                var a=ClassicEngine.Bytes(original.keymap,480);a[0]=60;
                f.failPage=fault=="page";f.corruptReadback=fault=="verify";f.badGeometry=fault=="geometry";
                Reject(()=>e.Upload(original.revision,ClassicEngine.Hex(a),original.custom,original.quick,original.extra,true),fault);
                Check(File.Exists(e.lastBackup),"backup survives failure");
                if(fault=="geometry")Check(!f.inner.trace.Any(t=>t.StartsWith("SET 2 132")),"geometry checked before flash write");
                results.Add("PASS "+fault+" failure detection and backup retention");
            }
            return results.ToArray();
        } finally {if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
