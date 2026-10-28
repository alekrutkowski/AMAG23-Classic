using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

public static class ClassicNative
{
    private static Win32Exception OsError(int code, string message) {
        return new Win32Exception(code, message + " [OS error " + code + ": " + new Win32Exception(code).Message + "]");
    }
    public sealed class DeviceInfo
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Manufacturer { get; set; }
        public int Vid { get; set; }
        public int Pid { get; set; }
        public int UsagePage { get; set; }
        public int Usage { get; set; }
        public string Transport { get; set; }
    }

    public sealed class FeatureResult
    {
        public byte[] Bytes { get; set; }
        public int ReceivedLength { get; set; }
    }

    private static int VID = 0x20A0;
    private static int PID = 0x422D;
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [DllImport("hid.dll", ExactSpelling = true)]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", ExactSpelling = true)]
    private static extern int HidP_GetCaps(IntPtr preparsedData, IntPtr caps);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] buffer, int length);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr infoSet, IntPtr deviceInfo, ref Guid classGuid, uint index, ref SP_DEVICE_INTERFACE_DATA interfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr infoSet, ref SP_DEVICE_INTERFACE_DATA interfaceData, IntPtr detailData, uint detailSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr infoSet);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int LinuxOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int LinuxIoctl(int fd, ulong request, [In, Out] byte[] data);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int LinuxClose(int fd);

    public static List<DeviceInfo> FindDevices(int vid = 0x20A0, int pid = 0x422D)
    {
        VID = vid; PID = pid;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return FindWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return FindLinux();
        throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
    }

    private static List<DeviceInfo> FindLinux()
    {
        var devices = new List<DeviceInfo>();
        if (!Directory.Exists("/sys/class/hidraw")) return devices;

        foreach (string dir in Directory.GetFileSystemEntries("/sys/class/hidraw", "hidraw*"))
        {
            string uevent = System.IO.Path.Combine(dir, "device", "uevent");
            if (!File.Exists(uevent)) continue;
            string text;
            try { text = File.ReadAllText(uevent); } catch { continue; }
            Match id = Regex.Match(text, @"(?m)^HID_ID=([0-9a-fA-F]+):([0-9a-fA-F]+):([0-9a-fA-F]+)\s*$", RegexOptions.CultureInvariant);
            if (!id.Success) continue;
            int bus, vid, pid;
            if (!int.TryParse(id.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out bus)) continue;
            if (!int.TryParse(id.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out vid)) continue;
            if (!int.TryParse(id.Groups[3].Value, System.Globalization.NumberStyles.HexNumber, null, out pid)) continue;
            if (bus != 3 || vid != VID || pid != PID) continue;
            Match name = Regex.Match(text, @"(?m)^HID_NAME=(.*)$");
            string dev = "/dev/" + System.IO.Path.GetFileName(dir);
            byte[] desc;
            try { desc = File.ReadAllBytes(System.IO.Path.Combine(dir, "device", "report_descriptor")); }
            catch { continue; }
            bool vendor = false;
            for (int k = 0; k + 2 < desc.Length; k++) if (desc[k] == 6 && desc[k+1] == 0 && desc[k+2] == 255) vendor = true;
            if (!vendor) continue;
            string manufacturer = "";
            string productName = name.Success ? name.Groups[1].Value.Trim() : "";
            try {
                DirectoryInfo di = new DirectoryInfo(System.IO.Path.Combine(dir, "device")).ResolveLinkTarget(true) as DirectoryInfo;
                for (int k = 0; di != null && k < 8; k++, di = di.Parent) {
                    string f = System.IO.Path.Combine(di.FullName, "manufacturer");
                    if (File.Exists(f)) { manufacturer = File.ReadAllText(f).Trim(); }
                    f = System.IO.Path.Combine(di.FullName, "product");
                    if (File.Exists(f)) { productName = File.ReadAllText(f).Trim(); break; }
                }
            } catch { }

            devices.Add(new DeviceInfo {
                Path = dev, Name = productName, Manufacturer = manufacturer,
                Vid = vid, Pid = pid, UsagePage = 0xFF00, Usage = 1, Transport = "Linux hidraw"
            });
        }
        devices.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return devices;
    }

    private static SafeFileHandle OpenWindows(string path, bool metadataOnly)
    {
        uint[] accessModes = metadataOnly
            ? new uint[] { 0, GENERIC_READ | GENERIC_WRITE, GENERIC_READ }
            : new uint[] { GENERIC_READ | GENERIC_WRITE, GENERIC_READ };
        int lastError = 0;
        foreach (uint access in accessModes)
        {
            SafeFileHandle handle = CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            lastError = Marshal.GetLastWin32Error();
            handle.Dispose();
        }
        throw OsError(lastError, "Could not open HID device: " + path);
    }

    private static List<DeviceInfo> FindWindows()
    {
        var devices = new List<DeviceInfo>();
        Guid hidGuid;
        HidD_GetHidGuid(out hidGuid);
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) throw OsError(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed");
        try
        {
            for (uint i = 0; i < 2048; ++i)
            {
                var data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break; // ERROR_NO_MORE_ITEMS
                    throw OsError(error, "HID enumeration failed");
                }
                uint required = 0;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                if (required < 8 || required > 32768) continue;
                IntPtr detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W has cbSize 8 on x64, 6 on x86.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    uint actual;
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out actual, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (String.IsNullOrEmpty(path)) continue;
                    string pathLower = path.ToLowerInvariant();
                    if (!pathLower.Contains(String.Format("vid_{0:x4}&pid_{1:x4}", VID, PID))) continue;

                    var item = new DeviceInfo {
                        Path = path, Name = "Unknown", Manufacturer = "Unknown", Vid = VID, Pid = PID,
                        UsagePage = -1, Usage = -1, Transport = "Windows HID"
                    };
                    try
                    {
                        using (SafeFileHandle handle = OpenWindows(path, true))
                        {
                            byte[] product = new byte[256];
                            if (HidD_GetProductString(handle, product, product.Length)) item.Name = System.Text.Encoding.Unicode.GetString(product).Split('\0')[0];
                            Array.Clear(product, 0, product.Length);
                            if (HidD_GetManufacturerString(handle, product, product.Length)) item.Manufacturer = System.Text.Encoding.Unicode.GetString(product).Split('\0')[0];
                            var attr = new HIDD_ATTRIBUTES();
                            attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                            if (HidD_GetAttributes(handle, ref attr))
                            {
                                if (attr.VendorID != VID || attr.ProductID != PID) continue;
                            }
                            IntPtr ppd = IntPtr.Zero;
                            if (HidD_GetPreparsedData(handle, out ppd))
                            {
                                try
                                {
                                    IntPtr caps = Marshal.AllocHGlobal(128);
                                    try
                                    {
                                        for (int n = 0; n < 128; n++) Marshal.WriteByte(caps, n, 0);
                                        if (HidP_GetCaps(ppd, caps) >= 0)
                                        {
                                            item.Usage = (ushort)Marshal.ReadInt16(caps, 0);
                                            item.UsagePage = (ushort)Marshal.ReadInt16(caps, 2);
                                        }
                                    }
                                    finally { Marshal.FreeHGlobal(caps); }
                                }
                                finally { HidD_FreePreparsedData(ppd); }
                            }
                        }
                    }
                    catch (Win32Exception) { /* VID/PID is still confirmed by HID path */ }
                    // Never choose consumer-control collection (usage page 0x000C).
                    if (item.UsagePage != -1 && item.UsagePage != 0xFF00) continue;
                    devices.Add(item);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return devices;
    }

    public static FeatureResult GetFeature(string path, int reportId, int length)
    {
        if (length < 2 || length > 4096) throw new ArgumentOutOfRangeException("length");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return GetFeatureWindows(path, reportId, length);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return GetFeatureLinux(path, reportId, length);
        throw new PlatformNotSupportedException();
    }

    private static FeatureResult GetFeatureWindows(string path, int reportId, int length)
    {
        using (SafeFileHandle handle = OpenWindows(path, false))
        {
            byte[] bytes = new byte[length];
            bytes[0] = (byte)reportId; // ps2avrGB protocol: vendor feature report ID 1
            if (!HidD_GetFeature(handle, bytes, length))
                throw OsError(Marshal.GetLastWin32Error(), "HidD_GetFeature(" + length + ") failed");
            return new FeatureResult { Bytes = bytes, ReceivedLength = length };
        }
    }

    private static FeatureResult GetFeatureLinux(string path, int reportId, int length)
    {
        int fd = LinuxOpen(path, 2 | 0x80000); // O_RDWR | O_CLOEXEC (Linux)
        if (fd < 0) throw OsError(Marshal.GetLastWin32Error(), "Cannot open " + path + " (check hidraw permissions)");
        try
        {
            byte[] bytes = new byte[length];
            bytes[0] = (byte)reportId;
            // Linux HIDIOCGFEATURE(len) = _IOC(_IOC_READ|_IOC_WRITE, 'H', 0x07, len).
            ulong request = 0xC0000000UL | ((ulong)length << 16) | ((ulong)'H' << 8) | 7UL;
            int count = LinuxIoctl(fd, request, bytes);
            if (count < 0) throw OsError(Marshal.GetLastWin32Error(), "HIDIOCGFEATURE(" + length + ") failed");
            if (count > length) throw new InvalidDataException("Kernel returned more bytes than requested");
            return new FeatureResult { Bytes = bytes, ReceivedLength = count };
        }
        finally { LinuxClose(fd); }
    }

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetManufacturerString(SafeFileHandle handle, byte[] buffer, int length);

    public static void SetFeature(string path, byte[] data) {
        if (data == null || data.Length < 2 || data.Length > 203) throw new ArgumentException("Invalid report size.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            using (SafeFileHandle h = OpenWindows(path, false)) {
                if (!HidD_SetFeature(h, data, data.Length)) throw OsError(Marshal.GetLastWin32Error(), "HidD_SetFeature failed");
            }
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            int fd = LinuxOpen(path, 2 | 0x80000);
            if (fd < 0) throw OsError(Marshal.GetLastWin32Error(), "Cannot open HID device. Check hidraw permissions.");
            try {
                ulong request = 0xC0000000UL | ((ulong)data.Length << 16) | ((ulong)'H' << 8) | 6UL;
                int n = LinuxIoctl(fd, request, data);
                if (n < 0) throw OsError(Marshal.GetLastWin32Error(), "HIDIOCSFEATURE failed");
                if (n != data.Length) throw new IOException("Short HID feature write.");
            } finally { LinuxClose(fd); }
        } else throw new PlatformNotSupportedException("Windows or Linux required.");
    }

}
