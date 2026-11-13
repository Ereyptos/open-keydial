// Open Keydial — talking to the Keydial: Bluetooth LE GATT (Win32 BluetoothGATT* API),
// whether it is connected, and the application in the foreground.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OpenKeydial
{
    sealed class KeydialBle : IDisposable
    {
        static readonly Guid SvcFFE0 = new Guid("0000FFE0-0000-1000-8000-00805F9B34FB");
        public const string HuionVid = "vid&02256c";
        IntPtr handle = Native.INVALID;
        Native.BTH_LE_GATT_CHARACTERISTIC chData, chCmd;
        bool haveCmd, writeCccd;
        readonly List<IntPtr> events = new List<IntPtr>();
        readonly List<Native.GattEventCallback> keepAlive = new List<Native.GattEventCallback>();
        readonly SynchronizationContext ui;
        public event Action<byte[]> DataFrame;
        public event Action<byte[]> CommandFrame;
        public string DevicePath { get; private set; }

        public KeydialBle(SynchronizationContext ui) { this.ui = ui; }
        public bool IsOpen => handle != Native.INVALID;

        // deviceAwake = false: subscribe on the Windows side without writing the CCCD
        // (writing it while the Keydial sleeps blocks for about 8 seconds).
        public bool TryOpen(bool deviceAwake)
        {
            writeCccd = deviceAwake;
            Close();
            string path = Native.InterfacePaths(SvcFFE0).FirstOrDefault(p => p.IndexOf(HuionVid, StringComparison.OrdinalIgnoreCase) >= 0);
            if (path == null) return false;
            IntPtr h = Native.CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == Native.INVALID) { Log.W("Cannot open the FFE0 service, error " + Marshal.GetLastWin32Error()); return false; }
            handle = h;
            DevicePath = path;
            try
            {
                Native.BluetoothGATTGetServices(h, 0, null, out ushort ns, 0);
                var svcs = new Native.BTH_LE_GATT_SERVICE[Math.Max((int)ns, 1)];
                int hr = Native.BluetoothGATTGetServices(h, (ushort)svcs.Length, svcs, out ns, 0);
                if (hr != 0 || ns == 0) throw new Exception($"GetServices hr=0x{hr:X8}");
                var svc = svcs[0];
                Native.BluetoothGATTGetCharacteristics(h, ref svc, 0, null, out ushort nc, 0);
                var chars = new Native.BTH_LE_GATT_CHARACTERISTIC[Math.Max((int)nc, 1)];
                hr = Native.BluetoothGATTGetCharacteristics(h, ref svc, (ushort)chars.Length, chars, out nc, 0);
                if (hr != 0) throw new Exception($"GetCharacteristics hr=0x{hr:X8}");
                bool haveData = false;
                foreach (var c in chars.Take(nc))
                {
                    if (c.CharacteristicUuid.IsShortUuid != 0 && c.CharacteristicUuid.ShortUuid == 0xFFE1) { chData = c; haveData = true; }
                    if (c.CharacteristicUuid.IsShortUuid != 0 && c.CharacteristicUuid.ShortUuid == 0xFFE2) { chCmd = c; haveCmd = true; }
                }
                if (!haveData) throw new Exception("characteristic FFE1 missing");
                Subscribe(chData, b => DataFrame?.Invoke(b));
                if (haveCmd) Subscribe(chCmd, b => CommandFrame?.Invoke(b));
                Log.W("Keydial service opened: " + path);
                return true;
            }
            catch (Exception e)
            {
                Log.W("Keydial: " + e.Message);
                Close();
                return false;
            }
        }

        void Subscribe(Native.BTH_LE_GATT_CHARACTERISTIC c, Action<byte[]> sink)
        {
            // Enable notifications on the device (CCCD descriptor 0x2902)...
            Native.BluetoothGATTGetDescriptors(handle, ref c, 0, null, out ushort nd, 0);
            if (nd > 0 && writeCccd)
            {
                var descs = new Native.BTH_LE_GATT_DESCRIPTOR[nd];
                Native.BluetoothGATTGetDescriptors(handle, ref c, nd, descs, out nd, 0);
                foreach (var d0 in descs.Where(x => x.DescriptorType == 2))
                {
                    var d = d0;
                    IntPtr v = Marshal.AllocHGlobal(80);
                    try
                    {
                        for (int i = 0; i < 80; i++) Marshal.WriteByte(v, i, 0);
                        Marshal.WriteInt32(v, 0, 2);            // ClientCharacteristicConfiguration
                        Marshal.WriteByte(v, 4, 1);             // short UUID
                        Marshal.WriteInt16(v, 8, 0x2902);
                        Marshal.WriteByte(v, 24, c.IsNotifiable != 0 ? (byte)1 : (byte)0);
                        Marshal.WriteByte(v, 25, c.IsNotifiable == 0 && c.IsIndicatable != 0 ? (byte)1 : (byte)0);
                        int hr = Native.BluetoothGATTSetDescriptorValue(handle, ref d, v, 0);
                        if (hr != 0) Log.W($"CCCD {c.CharacteristicUuid.ShortUuid:X4} hr=0x{hr:X8} (normal while the Keydial sleeps)");
                    }
                    finally { Marshal.FreeHGlobal(v); }
                }
            }
            // ... then listen to value changes on the Windows side.
            Native.GattEventCallback cb = (type, outp, ctx) =>
            {
                try
                {
                    IntPtr pv = Marshal.ReadIntPtr(outp, 16);
                    int size = Marshal.ReadInt32(pv);
                    var b = new byte[size];
                    Marshal.Copy(pv + 4, b, 0, size);
                    ui.Post(_ => sink(b), null);
                }
                catch (Exception e) { Log.W("callback: " + e.Message); }
            };
            keepAlive.Add(cb);
            IntPtr reg = Marshal.AllocHGlobal(40);
            try
            {
                for (int i = 0; i < 40; i++) Marshal.WriteByte(reg, i, 0);
                Marshal.WriteInt16(reg, 0, 1);
                Marshal.StructureToPtr(c, reg + 4, false);
                int hr = Native.BluetoothGATTRegisterEvent(handle, 0, reg, cb, IntPtr.Zero, out IntPtr ev, 0);
                if (hr != 0) throw new Exception($"subscription to {c.CharacteristicUuid.ShortUuid:X4} refused hr=0x{hr:X8}");
                events.Add(ev);
            }
            finally { Marshal.FreeHGlobal(reg); }
        }

        public bool SendCommand(byte[] data, bool log)
        {
            if (!IsOpen || !haveCmd) return false;
            IntPtr v = Marshal.AllocHGlobal(4 + data.Length + 4);
            try
            {
                Marshal.WriteInt32(v, data.Length);
                Marshal.Copy(data, 0, v + 4, data.Length);
                var c = chCmd;
                int hr = Native.BluetoothGATTSetCharacteristicValue(handle, ref c, v, 0, chCmd.IsWritableWithoutResponse != 0 ? 0x20u : 0u);
                if (log || hr != 0) Log.W($"FFE2 command sent [{Log.Hex(data)}] hr=0x{hr:X8}");
                return hr == 0;
            }
            finally { Marshal.FreeHGlobal(v); }
        }

        public void Close()
        {
            foreach (var e in events) Native.BluetoothGATTUnregisterEvent(e, 0);
            events.Clear();
            if (handle != Native.INVALID) Native.CloseHandle(handle);
            handle = Native.INVALID;
            haveCmd = false;
        }

        public void Dispose() => Close();
    }

    static class Foreground
    {
        static uint lastPid;
        static string lastName;
        public static string ExeName()
        {
            IntPtr w = Native.GetForegroundWindow();
            if (w == IntPtr.Zero) return null;
            Native.GetWindowThreadProcessId(w, out uint pid);
            if (pid == lastPid && lastName != null) return lastName;
            string name = null;
            IntPtr h = Native.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (Native.QueryFullProcessImageName(h, 0, sb, ref size)) name = Path.GetFileName(sb.ToString()).ToLowerInvariant();
                Native.CloseHandle(h);
            }
            lastPid = pid;
            lastName = name;
            return name;
        }
    }

    // Is the Keydial connected? Lists the input devices Windows knows (no input is read).
    static class KeydialPresence
    {
        // Keydial Mini K20: Huion VID 256C, PID 8251.
        static bool IsKeydialName(string n) => n.IndexOf("02256c_pid&8251", StringComparison.OrdinalIgnoreCase) >= 0;

        static string DeviceName(IntPtr dev)
        {
            uint size = 0;
            Native.GetRawInputDeviceInfo(dev, 0x20000007, null, ref size); // RIDI_DEVICENAME
            var sb = new StringBuilder((int)size + 1);
            Native.GetRawInputDeviceInfo(dev, 0x20000007, sb, ref size);
            return sb.ToString();
        }

        public static bool Connected()
        {
            uint n = 0;
            int sz = Marshal.SizeOf<Native.RAWINPUTDEVICELIST>();
            Native.GetRawInputDeviceList(null, ref n, (uint)sz);
            var list = new Native.RAWINPUTDEVICELIST[n + 8];
            n = (uint)list.Length;
            int got = (int)Native.GetRawInputDeviceList(list, ref n, (uint)sz);
            for (int i = 0; i < got; i++)
                if (IsKeydialName(DeviceName(list[i].hDevice))) return true;
            return false;
        }
    }
}
