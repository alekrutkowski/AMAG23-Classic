// Independent host implementation. Protocol sources and evidence: PROTOCOL.md.
// Only configuration regions are writable; this is not a firmware flasher.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Runtime.InteropServices;

public interface IClassicHid {
    List<ClassicNative.DeviceInfo> Devices(bool boot);
    ClassicNative.FeatureResult Get(string path, int id, int length);
    void Set(string path, byte[] report);
    void Pause(int ms);
}
public sealed class ClassicHid : IClassicHid {
    public List<ClassicNative.DeviceInfo> Devices(bool boot) {
        return ClassicNative.FindDevices(boot ? 0x16C0 : 0x20A0, boot ? 0x05DF : 0x422D)
            .Where(d => d.UsagePage == 0xFF00).ToList();
    }
    public ClassicNative.FeatureResult Get(string p, int id, int len) { return ClassicNative.GetFeature(p,id,len); }
    public void Set(string p, byte[] b) { ClassicNative.SetFeature(p,b); }
    public void Pause(int ms) { Thread.Sleep(ms); }
}
public sealed class ClassicConfig {
    public string schema { get; set; } = "amag23-native-config/v1";
    public string capturedAt { get; set; }
    public string revision { get; set; }
    public string firmware { get; set; }
    public string keymap { get; set; }
    public string custom { get; set; }
    public string quick { get; set; }
    public string extra { get; set; } = "";
    public string dual { get; set; }
    public string info { get; set; }
}
public sealed class ClassicPlan {
    public int changedKeys { get; set; }
    public int[] customSlots { get; set; }
    public int[] quickSlots { get; set; }
    public int[] flashPages { get; set; }
    public bool requiresBootloader { get; set; }
    public string note { get; set; }
}
public sealed class ClassicEngine {
    [DllImport("libc", EntryPoint="chmod", SetLastError=true)]
    private static extern int UnixChmod([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
    private static void PrivateMode(string path, uint mode) {
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && UnixChmod(path,mode)!=0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot restrict backup permissions: "+path);
    }
    private readonly IClassicHid hid;
    private readonly string folder;
    private string path;
    private ClassicConfig live;
    private bool identify;
    public string lastBackup { get; private set; }
    public List<string> log { get; private set; } = new List<string>();
    public bool isDemo { get; private set; }
    public ClassicEngine(string backupFolder, bool demo) {
        folder = backupFolder; isDemo = demo;
        hid = demo ? (IClassicHid)new ClassicMockHid() : new ClassicHid();
        Directory.CreateDirectory(folder); PrivateMode(folder,448);
    }
    public ClassicEngine(string backupFolder, IClassicHid transport) {
        folder=backupFolder; hid=transport; Directory.CreateDirectory(folder); PrivateMode(folder,448);
    }
    public void Log(string message) {
        log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
        if (log.Count > 300) log.RemoveAt(0);
    }
    public List<ClassicNative.DeviceInfo> Devices() { return hid.Devices(false); }
    public static byte[] Bytes(string hex, int expected) {
        if (hex == null || hex.Length != expected*2 || hex.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid configuration length or hexadecimal data; expected " + expected + " bytes.");
        return Convert.FromHexString(hex);
    }
    public static string Hex(byte[] b) { return Convert.ToHexString(b); }
    private static string Revision(ClassicConfig c) {
        return Hex(SHA256.HashData(Encoding.ASCII.GetBytes(c.keymap+c.custom+c.quick+c.extra+c.dual)));
    }
    private byte[] ReadBlock(string p,int id,int length,int needed) {
        var r=hid.Get(p,id,length);
        if(r.ReceivedLength < needed || r.Bytes.Length < needed)
            throw new IOException("Short feature report " + id + ", request length " + length + ": expected " + needed + ", received " + r.ReceivedLength);
        return r.Bytes.Take(needed).ToArray();
    }
    private byte[] Group(string p,int first,int count,int size) {
        var b=new byte[count*size];
        for(int i=0;i<count;i++) { Array.Copy(ReadBlock(p,1,first+i,size),0,b,i*size,size); hid.Pause(12); }
        return b;
    }
    private void Ready(string p) { hid.Set(p,new byte[]{1,0x90,0,0,0,0,0,0}); hid.Pause(30); }
    private void Action(string p) { hid.Set(p,new byte[]{1,0x91,0,0,0,0,0,0}); }
    private ClassicConfig ReadOnce(string p) {
        var c=new ClassicConfig();
        Ready(p);
        try {
            var infoReport=hid.Get(p,1,100);
            if(infoReport.ReceivedLength<38 || infoReport.Bytes.Length<38)
                throw new IOException("Short device-information report. No configuration was written.");
            bool modern=infoReport.Bytes[0]>=51 && infoReport.Bytes[0]<=100;
            int infoLength=modern ? infoReport.Bytes[0] : Math.Min(infoReport.ReceivedLength,infoReport.Bytes.Length);
            if(infoReport.ReceivedLength<infoLength || infoReport.Bytes.Length<infoLength)
                throw new IOException("Device-information report is shorter than its own size field.");
            var info=infoReport.Bytes.Take(infoLength).ToArray();
            if(modern && (info[44]!=1 || info[47]!=0))
                throw new InvalidDataException("This device reports a different firmware family. Expected ps2avrGB (family 0), version 1.x. No configuration was written.");
            c.firmware=modern ? String.Format("{0}.{1}.{2}",info[44],info[45],info[46]) : "legacy (pre-size-field)";
            c.info=Hex(info);
            c.keymap=Hex(Group(p,121,4,120));
            c.custom=Hex(Group(p,168,12,168));
            c.quick=Hex(Group(p,180,12,48));
            c.extra=modern && info[45]>=5 ? Hex(Group(p,192,12,28)) : "";
            c.dual=Hex(ReadBlock(p,1,150,128));
            if(!Bytes(c.keymap,480).Any(x=>x>=4 && x<255))
                throw new InvalidDataException("The keymap is entirely empty. Refusing to treat an invalid or blank read as a usable configuration.");
            c.capturedAt=DateTime.UtcNow.ToString("o");
            c.revision=Revision(c);
            return c;
        } finally { Action(p); }
    }
    public ClassicConfig Connect(string requestedPath) {
        live=null;
        var devices=Devices();
        var found=devices.Where(d=>d.Path==requestedPath).ToList();
        if(found.Count!=1) throw new IOException("Select one currently connected ps2avrGB vendor interface.");
        path=requestedPath;
        Log("Reading four layers and both macro banks from USB.");
        var a=ReadOnce(path); var b=ReadOnce(path);
        if(a.revision!=b.revision) throw new IOException("Two configuration reads differed. Stop using the keypad, close AMAG/Bootmapper and reconnect.");
        live=b; SaveBackup(b,"connected");
        Log("Device data read twice and matched. Firmware " + b.firmware + ".");
        return b;
    }
    private void SaveBackup(ClassicConfig c,string tag) {
        string name=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+tag+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".json";
        string f=Path.Combine(folder,name);
        File.WriteAllText(f,JsonSerializer.Serialize(c,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        PrivateMode(f,384); // 0600; containing backup directory is already 0700 on Linux.
        // Check that the persisted backup really contains exactly what was read.
        var saved=JsonSerializer.Deserialize<ClassicConfig>(File.ReadAllText(f));
        if(saved==null || Revision(saved)!=c.revision) throw new IOException("Backup verification failed; upload cancelled.");
        lastBackup=f;
        Log("Backup saved: " + name);
    }
    private ClassicConfig Target(string keymap,string custom,string quick,string extra) {
        if(live==null) throw new InvalidOperationException("Connect and read the keypad first.");
        if(identify) throw new InvalidOperationException("Stop Identify key mode first.");
        var c=new ClassicConfig{keymap=Hex(Bytes(keymap,480)), custom=Hex(Bytes(custom,2016)),
            quick=Hex(Bytes(quick,576)), extra=Hex(Bytes(extra,live.extra.Length/2)),dual=live.dual,info=live.info,firmware=live.firmware};
        c.revision=Revision(c);
        return c;
    }
    private static int[] Slots(string a,string b,int size) {
        var result=new List<int>();
        for(int i=0;i<12;i++) if(a.Substring(i*size*2,size*2)!=b.Substring(i*size*2,size*2)) result.Add(i+1);
        return result.ToArray();
    }
    // A fixed allowlist, not caller-supplied addresses or a generic .hex uploader.
    public static byte[] PagePacket(int address,byte[] data) {
        if(address%128!=0 || address<0x6600 || address>=0x7000 || data.Length!=128)
            throw new InvalidDataException("Outside the allowed configuration pages.");
        var r=new byte[132]; r[0]=2; r[1]=(byte)address; r[2]=(byte)(address>>8); r[3]=(byte)(address>>16);
        Array.Copy(data,0,r,4,128); return r;
    }
    private static List<KeyValuePair<int,byte[]>> Pages(ClassicConfig old,ClassicConfig next) {
        var pages=new List<KeyValuePair<int,byte[]>>();
        foreach(var spec in new[]{ Tuple.Create(0x6600,Bytes(old.keymap,480),Bytes(next.keymap,480)),Tuple.Create(0x6800,Bytes(old.custom,2016),Bytes(next.custom,2016)) }) {
            for(int off=0;off<spec.Item2.Length;off+=128) {
                int count=Math.Min(128,spec.Item2.Length-off);
                if(spec.Item2.Skip(off).Take(count).SequenceEqual(spec.Item3.Skip(off).Take(count))) continue;
                // Original gbmgr initializes missing HEX bytes to FF. Only the trailing
                // 32 bytes in each documented configuration region can require this.
                byte[] data=Enumerable.Repeat((byte)255,128).ToArray();
                Array.Copy(spec.Item3,off,data,0,count);
                pages.Add(new KeyValuePair<int,byte[]>(spec.Item1+off,data));
            }
        }
        return pages;
    }
    private ClassicPlan Plan(ClassicConfig c) {
        var quick=Slots(live.quick,c.quick,48).ToList();
        if(live.extra.Length>0) quick=quick.Union(Slots(live.extra,c.extra,28)).OrderBy(x=>x).ToList();
        var pages=Pages(live,c);
        return new ClassicPlan{changedKeys=Bytes(live.keymap,480).Zip(Bytes(c.keymap,480),(a,b)=>a!=b?1:0).Sum(),
            customSlots=Slots(live.custom,c.custom,168),quickSlots=quick.ToArray(),flashPages=pages.Select(p=>p.Key).ToArray(),requiresBootloader=pages.Count>0,
            note=pages.Count>0 ? "Updates configuration data through the existing bootloader, as AMAG does. No replacement firmware is installed. USB will briefly reconnect. Keep the keypad plugged in. Hardware support has not been tested on your unit." : "Updates the quick-macro EEPROM bank without entering the bootloader. Keep the keypad plugged in until verification finishes."};
    }
    public ClassicPlan Preview(string keymap,string custom,string quick,string extra) { return Plan(Target(keymap,custom,quick,extra)); }
    private string BootDevice() {
        var ds=hid.Devices(true).Where(d=>d.Name=="HIDBoot" && d.Manufacturer=="obdev.at").ToList();
        if(ds.Count>1) throw new IOException("More than one matching HIDBoot device is connected. No flash writes performed.");
        return ds.Count==1 ? ds[0].Path : null;
    }
    private string WaitForDevice(bool boot) {
        for(int i=0;i<100;i++) {
            if(boot) { string p=BootDevice(); if(p!=null) return p; }
            else {
                var d=Devices();
                if(d.Count>1) throw new IOException("More than one keypad is present after reconnect. Stop and reconnect the intended device.");
                if(d.Count==1) return d[0].Path;
            }
            hid.Pause(200);
        }
        throw new IOException("Device did not reappear. Keep the backup. Use the connection log; do not repeatedly upload.");
    }
    private void CheckBootGeometry(string bp) {
        var b=ReadBlock(bp,1,132,7);
        int page=b[1]|(b[2]<<8);
        long flash=(long)b[3]|((long)b[4]<<8)|((long)b[5]<<16)|((long)b[6]<<24);
        if(b[0]!=1 || page!=128 || flash!=32768)
            throw new IOException("Unexpected bootloader geometry. Expected report 1, 128-byte pages, 32768-byte flash. No configuration pages written.");
    }
    private void Flash(List<KeyValuePair<int,byte[]>> pages) {
        if(Devices().Count!=1 || hid.Devices(true).Count!=0)
            throw new IOException("For configuration upload, connect only this keypad and disconnect other HIDBoot devices.");
        Log("Entering the existing bootloader for configuration pages only.");
        // Original gbmgr -bootloader ignore uses report 1, command 0, byte2 FF.
        try { hid.Set(path,new byte[]{1,0,255,0,0,0,0,0}); }
        catch(Exception ex) when(ex is IOException || ex is System.ComponentModel.Win32Exception) {
            // A successful mode switch can disconnect before its control transfer completes.
            // Continue ONLY if the expected boot device subsequently appears and passes geometry.
            Log("Boot-mode request returned a transport error; checking re-enumeration: "+ex.Message);
        }
        string bp=WaitForDevice(true);
        CheckBootGeometry(bp);
        foreach(var p in pages) {
            Log("Writing configuration page 0x"+p.Key.ToString("X4"));
            hid.Set(bp,PagePacket(p.Key,p.Value)); hid.Pause(100);
        }
        hid.Pause(200);
        try { hid.Set(bp,new byte[]{1,1,0,0,0,0,0}); }
        catch(Exception ex) when(ex is IOException || ex is System.ComponentModel.Win32Exception) {
            Log("Return-to-keyboard request returned a transport error; checking re-enumeration: "+ex.Message);
        }
        path=WaitForDevice(false); hid.Pause(400);
    }
    private void QuickBank(string data,bool extra) {
        int group=extra?112:96, total=extra?336:576, slotStride=extra?4:2, first=extra?192:180;
        byte[] source=Bytes(data,total);
        Ready(path);
        try {
            // The original protocol sends 132 bytes but only 96 (or 112) meaningful bytes.
            // Padding overlaps the next slots; ALWAYS rewrite the complete bank in order.
            for(int off=0,slot=0;off<total;off+=group,slot+=slotStride) {
                byte[] packet=new byte[132]; packet[0]=2; packet[1]=(byte)first;
                Array.Copy(source,off,packet,2,group);
                hid.Get(path,2,first+slot); // Empty reply is expected: prepares EEPROM address.
                hid.Set(path,packet); hid.Pause(160);
            }
        } finally { Action(path); }
    }
    public ClassicConfig Upload(string expectedRevision,string keymap,string custom,string quick,string extra,bool confirmConfigurationFlash) {
        var target=Target(keymap,custom,quick,extra);
        var plan=Plan(target);
        if(expectedRevision!=live.revision) throw new IOException("This window has an old device snapshot. Read the keypad again.");
        if(plan.requiresBootloader && !confirmConfigurationFlash) throw new IOException("Configuration-flash confirmation is required.");
        if(target.revision==live.revision) return live;
        Log("Checking for device changes before upload.");
        var fresh=ReadOnce(path);
        if(fresh.revision!=live.revision) { live=null; throw new IOException("The device changed since it was read. Upload cancelled. Reconnect to load its actual contents."); }
        SaveBackup(fresh,"before-upload");
        var pages=Pages(fresh,target);
        try {
            if(pages.Count>0) Flash(pages);
            if(target.quick!=fresh.quick) { Log("Updating the complete quick-macro base bank."); QuickBank(target.quick,false); }
            if(target.extra!=fresh.extra) { Log("Updating the complete quick-macro extension bank."); QuickBank(target.extra,true); }
            Log("Reading the configuration back for byte-for-byte verification.");
            var result=ReadOnce(path);
            if(result.revision!=target.revision) throw new IOException("Read-back verification failed. The backup is retained. Do not assume the intended configuration was saved.");
            live=result; SaveBackup(result,"verified");
            Log("Upload verified: all keymap, macro and dual-action bytes match the intended configuration.");
            return result;
        } catch(Exception ex) {
            live=null;
            Log("Upload stopped: "+ex.Message);
            throw new IOException("Upload did not complete with verification. Backup: "+lastBackup+". "+ex.Message,ex);
        }
    }
    public void Identify(bool enabled) {
        if(path==null || live==null) throw new InvalidOperationException("Read the keypad first.");
        hid.Set(path,new byte[]{1,1,(byte)(enabled?1:0),0,0,0,0,0}); identify=enabled;
        Log(enabled?"Identify mode on. Press a key in the focused capture field.":"Identify mode off.");
    }
    public void RecoverKeyboard() {
        if(Devices().Count>0) throw new IOException("The normal keypad is already present. Use Connect.");
        string bp=BootDevice(); if(bp==null) throw new IOException("No matching HIDBoot device found.");
        CheckBootGeometry(bp);
        live=null;
        try { hid.Set(bp,new byte[]{1,1,0,0,0,0,0}); }
        catch(Exception ex) when(ex is IOException || ex is System.ComponentModel.Win32Exception) {
            Log("Return request disconnected; checking keyboard re-enumeration: "+ex.Message);
        }
        path=WaitForDevice(false);
        Log("Normal keyboard interface found again. No configuration pages written. Connect / Read to reload.");
    }
    public void Close() { if(identify && path!=null) { try { Identify(false); } catch { } } }
}
// Explicit demo and protocol self-test transport. Never selected as fallback on real errors.
public sealed class ClassicMockHid : IClassicHid {
    public byte[] flash = Enumerable.Repeat((byte)255,32768).ToArray();
    public byte[] quick=new byte[576], extra=new byte[336], dual=new byte[128];
    public List<string> trace=new List<string>();
    public bool boot=false;
    private int preparation=0;
    public ClassicMockHid() {
        int[] positions={0,1,2,3,4,5,8,9,10,11,12,13,16,17,18,19,20,21,24,25,26,27,29};
        byte[] defaults={41,30,31,32,33,34,43,20,26,8,21,28,254,4,22,7,9,10,29,27,6,5,44};
        Array.Clear(flash,0x6600,480); Array.Clear(flash,0x6800,2016);
        for(int layer=0;layer<4;layer++) for(int i=0;i<positions.Length;i++) flash[0x6600+layer*120+positions[i]]=defaults[i];
        flash[0x6601]=189;
        byte[] shortcut={103,128,6,128,6,0,103,0}; Array.Copy(shortcut,0,flash,0x6800,shortcut.Length);
        quick[0]=4; quick[1]=4;
    }
    public List<ClassicNative.DeviceInfo> Devices(bool wantBoot) {
        return wantBoot==boot ? new List<ClassicNative.DeviceInfo>{new ClassicNative.DeviceInfo{Path=boot?"DEMO-BOOT":"DEMO-KEYPAD",Name=boot?"HIDBoot":"ps2avrGB (DEMO)",Manufacturer=boot?"obdev.at":"Demo",UsagePage=0xFF00,Usage=1,Vid=boot?0x16C0:0x20A0,Pid=boot?0x05DF:0x422D,Transport="Simulated USB"}} : new List<ClassicNative.DeviceInfo>();
    }
    public ClassicNative.FeatureResult Get(string p,int id,int length) {
        trace.Add("GET "+id+" "+length);
        byte[] b;
        if(boot) { b=new byte[]{1,128,0,0,128,0,0}; }
        else if(id==2) { preparation=length; b=new byte[0]; }
        else if(length==100) { b=new byte[53];b[0]=53;b[44]=1;b[45]=5;b[47]=0; }
        else if(length>=121 && length<=124) b=flash.Skip(0x6600+(length-121)*120).Take(120).ToArray();
        else if(length>=168 && length<=179) b=flash.Skip(0x6800+(length-168)*168).Take(168).ToArray();
        else if(length>=180 && length<=191) b=quick.Skip((length-180)*48).Take(48).ToArray();
        else if(length>=192 && length<=203) b=extra.Skip((length-192)*28).Take(28).ToArray();
        else if(length==150) b=(byte[])dual.Clone();
        else throw new IOException("Unexpected simulated request.");
        return new ClassicNative.FeatureResult{Bytes=b,ReceivedLength=b.Length};
    }
    public void Set(string p,byte[] b) {
        trace.Add("SET "+b[0]+" "+b.Length+" "+Convert.ToHexString(b.Take(4).ToArray()));
        if(boot) {
            if(b[0]==1 && b.Length==7 && b[1]==1) {boot=false;return;}
            if(b[0]!=2 || b.Length!=132) throw new IOException("Invalid boot packet.");
            int address=b[1]|b[2]<<8|b[3]<<16;
            ClassicEngine.PagePacket(address,b.Skip(4).ToArray());
            Array.Copy(b,4,flash,address,128);return;
        }
        if(b[0]==1) { if(b[1]==0 && b.Length==8 && b[2]==255) boot=true; return; }
        byte[] bank; int off;
        if(preparation>=180 && preparation<=191) { bank=quick;off=(preparation-180)*48; }
        else if(preparation>=192 && preparation<=203) {bank=extra;off=(preparation-192)*28;}
        else throw new IOException("EEPROM write was not prepared.");
        Array.Copy(b,2,bank,off,Math.Min(b.Length-2,bank.Length-off)); preparation=0;
    }
    public void Pause(int ms) { }
}
