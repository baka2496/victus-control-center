using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace VictusQuiet
{
    // One screen whose backlight can be changed. No administrator rights are needed.
    public abstract class DisplayTarget : IDisposable
    {
        public string Name { get; protected set; }
        public string Kind { get; protected set; }
        public bool Internal { get; protected set; }
        public int Minimum { get; protected set; }
        public int Maximum { get; protected set; }
        public int Current { get; protected set; }
        public int Percent { get { return Maximum <= Minimum ? 0 : (int)Math.Round((Current - Minimum) * 100.0 / (Maximum - Minimum)); } }
        protected int RawFromPercent(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            return Minimum + (int)Math.Round(percent * (Maximum - Minimum) / 100.0);
        }
        public abstract void SetPercent(int percent);
        public virtual void Dispose() { }
    }

    // External monitor controlled over the video cable (DDC/CI).
    sealed class DdcDisplay : DisplayTarget
    {
        [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);
        [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
        [DllImport("dxva2.dll")] internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);
        IntPtr handle;
        public DdcDisplay(IntPtr handle, string name)
        {
            this.handle = handle; Name = name; Kind = "外接螢幕 · DDC/CI";
        }
        public bool Read()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint minimum, current, maximum;
                if (GetMonitorBrightness(handle, out minimum, out current, out maximum) && maximum > minimum)
                { Minimum = (int)minimum; Maximum = (int)maximum; Current = (int)current; return true; }
                Thread.Sleep(40);
            }
            return false;
        }
        public override void SetPercent(int percent)
        {
            int raw = RawFromPercent(percent);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (handle != IntPtr.Zero && SetMonitorBrightness(handle, (uint)raw)) { Current = raw; return; }
                Thread.Sleep(50);
            }
            throw new InvalidOperationException("螢幕沒有回應，請確認螢幕選單已開啟 DDC/CI，或按「重新偵測」。");
        }
        public override void Dispose()
        {
            if (handle != IntPtr.Zero) { DestroyPhysicalMonitor(handle); handle = IntPtr.Zero; }
        }
    }

    // Laptop panel controlled through the Windows brightness interface.
    sealed class WmiDisplay : DisplayTarget
    {
        readonly string instance;
        public WmiDisplay(string instance, string name, int current)
        {
            this.instance = instance; Name = name; Kind = "筆電面板 · Windows 亮度"; Internal = true;
            Minimum = 0; Maximum = 100; Current = current;
        }
        public override void SetPercent(int percent)
        {
            int raw = RawFromPercent(percent);
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods"))
                foreach (ManagementObject method in searcher.Get())
                    using (method)
                        if (String.Equals((string)method["InstanceName"], instance, StringComparison.OrdinalIgnoreCase))
                        {
                            method.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)raw });
                            Current = raw; return;
                        }
            throw new InvalidOperationException("找不到內建螢幕的亮度控制，請按「重新偵測」。");
        }
    }

    public static class DisplayBrightness
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PhysicalMonitor { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        delegate bool MonitorCallback(IntPtr monitor, IntPtr context, IntPtr area, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr context, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
        [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);

        // "DISPLAY\AUS24A1\5&1a2b&0&UID4352_0" and "\\?\DISPLAY#AUS24A1#5&1a2b&0&UID4352#{guid}" both become "DISPLAY\AUS24A1\5&1A2B&0&UID4352".
        public static string NormaliseInstance(string instance)
        {
            if (String.IsNullOrEmpty(instance)) return "";
            string value = instance.Trim();
            if (value.StartsWith(@"\\?\")) value = value.Substring(4);
            int guid = value.IndexOf("#{", StringComparison.Ordinal); if (guid >= 0) value = value.Substring(0, guid);
            value = value.Replace('#', '\\');
            int suffix = value.LastIndexOf('_'); if (suffix > value.LastIndexOf('\\')) value = value.Substring(0, suffix);
            return value.ToUpperInvariant();
        }

        static Dictionary<string, string> FriendlyNames()
        {
            Dictionary<string, string> names = new Dictionary<string, string>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID"))
                    foreach (ManagementObject item in searcher.Get())
                        using (item)
                        {
                            ushort[] raw = item["UserFriendlyName"] as ushort[];
                            string name = "";
                            if (raw != null) foreach (ushort c in raw) if (c != 0) name += (char)c;
                            names[NormaliseInstance((string)item["InstanceName"])] = name.Trim();
                        }
            }
            catch (ManagementException) { }
            return names;
        }

        public static List<DisplayTarget> Discover()
        {
            List<DisplayTarget> found = new List<DisplayTarget>();
            Dictionary<string, string> names = FriendlyNames();
            HashSet<string> taken = new HashSet<string>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, CurrentBrightness, Active FROM WmiMonitorBrightness"))
                    foreach (ManagementObject item in searcher.Get())
                        using (item)
                        {
                            if (item["Active"] is bool && !(bool)item["Active"]) continue;
                            string instance = (string)item["InstanceName"];
                            string key = NormaliseInstance(instance), name;
                            if (!names.TryGetValue(key, out name) || name.Length == 0) name = "內建螢幕";
                            found.Add(new WmiDisplay(instance, name, Convert.ToInt32(item["CurrentBrightness"])));
                            taken.Add(key);
                        }
            }
            catch (ManagementException) { }

            List<KeyValuePair<IntPtr, string>> monitors = new List<KeyValuePair<IntPtr, string>>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr context, IntPtr area, IntPtr data)
            {
                MonitorInfo info = new MonitorInfo(); info.Size = Marshal.SizeOf(typeof(MonitorInfo));
                monitors.Add(new KeyValuePair<IntPtr, string>(monitor, GetMonitorInfo(monitor, ref info) ? info.Device : ""));
                return true;
            }, IntPtr.Zero);

            int unnamed = 0;
            foreach (KeyValuePair<IntPtr, string> monitor in monitors)
            {
                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Key, out count) || count == 0) continue;
                PhysicalMonitor[] physical = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor.Key, count, physical)) continue;
                for (uint i = 0; i < count; i++)
                {
                    DisplayDevice device = new DisplayDevice(); device.Size = Marshal.SizeOf(typeof(DisplayDevice));
                    string key = monitor.Value.Length > 0 && EnumDisplayDevices(monitor.Value, i, ref device, 1) ? NormaliseInstance(device.DeviceID) : "";
                    string name;
                    if (key.Length == 0 || !names.TryGetValue(key, out name) || name.Length == 0) { unnamed++; name = "外接螢幕" + (unnamed > 1 ? " " + unnamed : ""); }
                    DdcDisplay display = new DdcDisplay(physical[i].Handle, name);
                    if ((key.Length > 0 && taken.Contains(key)) || !display.Read()) { display.Dispose(); continue; }
                    if (key.Length > 0) taken.Add(key);
                    found.Add(display);
                }
            }
            return found;
        }
    }
}
