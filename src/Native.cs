// Open Keydial — Win32 interop.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenKeydial
{
    static class Native
    {
        public static readonly IntPtr INVALID = new IntPtr(-1);

        [StructLayout(LayoutKind.Explicit, Size = 20)]
        public struct BTH_LE_UUID
        {
            [FieldOffset(0)] public byte IsShortUuid;
            [FieldOffset(4)] public ushort ShortUuid;
            [FieldOffset(4)] public Guid LongUuid;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct BTH_LE_GATT_SERVICE
        {
            [FieldOffset(0)] public BTH_LE_UUID ServiceUuid;
            [FieldOffset(20)] public ushort AttributeHandle;
        }

        [StructLayout(LayoutKind.Explicit, Size = 36)]
        public struct BTH_LE_GATT_CHARACTERISTIC
        {
            [FieldOffset(0)] public ushort ServiceHandle;
            [FieldOffset(4)] public BTH_LE_UUID CharacteristicUuid;
            [FieldOffset(24)] public ushort AttributeHandle;
            [FieldOffset(26)] public ushort CharacteristicValueHandle;
            [FieldOffset(28)] public byte IsBroadcastable;
            [FieldOffset(29)] public byte IsReadable;
            [FieldOffset(30)] public byte IsWritable;
            [FieldOffset(31)] public byte IsWritableWithoutResponse;
            [FieldOffset(32)] public byte IsSignedWritable;
            [FieldOffset(33)] public byte IsNotifiable;
            [FieldOffset(34)] public byte IsIndicatable;
            [FieldOffset(35)] public byte HasExtendedProperties;
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        public struct BTH_LE_GATT_DESCRIPTOR
        {
            [FieldOffset(0)] public ushort ServiceHandle;
            [FieldOffset(2)] public ushort CharacteristicHandle;
            [FieldOffset(4)] public int DescriptorType;
            [FieldOffset(8)] public BTH_LE_UUID DescriptorUuid;
            [FieldOffset(28)] public ushort AttributeHandle;
        }

        public delegate void GattEventCallback(int eventType, IntPtr eventOutParameter, IntPtr context);

        [DllImport("BluetoothApis.dll")] public static extern int BluetoothGATTGetServices(IntPtr h, ushort count, [Out] BTH_LE_GATT_SERVICE[] buf, out ushort actual, uint flags);
        [DllImport("BluetoothApis.dll")] public static extern int BluetoothGATTGetCharacteristics(IntPtr h, ref BTH_LE_GATT_SERVICE svc, ushort count, [Out] BTH_LE_GATT_CHARACTERISTIC[] buf, out ushort actual, uint flags);
        [DllImport("BluetoothApis.dll")] public static extern int BluetoothGATTGetDescriptors(IntPtr h, ref BTH_LE_GATT_CHARACTERISTIC ch, ushort count, [Out] BTH_LE_GATT_DESCRIPTOR[] buf, out ushort actual, uint flags);
        [DllImport("BluetoothApis.dll")] public static extern int BluetoothGATTSetDescriptorValue(IntPtr h, ref BTH_LE_GATT_DESCRIPTOR d, IntPtr value, uint flags);
        [DllImport("BluetoothApis.dll")] public static extern int BluetoothGATTSetCharacteristicValue(IntPtr h, ref BTH_LE_GATT_CHARACTERISTIC ch, IntPtr value, ulong reliableWriteContext, uint flags);
        [DllImport("BluetoothApis.dll")] public static extern int BluetoothGATTRegisterEvent(IntPtr hService, int eventType, IntPtr paramIn, GattEventCallback cb, IntPtr ctx, out IntPtr eventHandle, uint flags);
        [DllImport("BluetoothApis.dll")] public static extern int BluetoothGATTUnregisterEvent(IntPtr eventHandle, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }
        [DllImport("setupapi.dll", SetLastError = true)] public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, out uint required, IntPtr devInfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        public static List<string> InterfacePaths(Guid g)
        {
            var res = new List<string>();
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
            if (set == INVALID) return res;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var d = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out uint req, IntPtr.Zero);
                    IntPtr buf = Marshal.AllocHGlobal((int)req);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                            res.Add(Marshal.PtrToStringUni(buf + 4));
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return res;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy, mouseData; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        public struct INPUT
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(8)] public MOUSEINPUT mi;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }
        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern short VkKeyScan(char ch);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w, out uint pid);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint GetRawInputDeviceInfo(IntPtr dev, uint cmd, StringBuilder data, ref uint size);
        [DllImport("user32.dll")] public static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] list, ref uint n, uint size);
    }
}
