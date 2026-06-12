using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace KeyboardLayoutSyncAgent
{
    internal enum KeyboardLayoutKind : byte
    {
        English = 0,
        Russian = 1
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext());
        }
    }

    internal sealed class TrayAppContext : ApplicationContext
    {
        private const int ForceResendIntervalMs = 1500;

        private readonly NotifyIcon notifyIcon;
        private readonly Timer timer;
        private KeyboardLayoutKind? lastSentLayout;
        private DateTime lastSentAt = DateTime.MinValue;

        public TrayAppContext()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Application;
            notifyIcon.Text = "Keyboard layout sync";
            notifyIcon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Sync now", null, delegate { SyncNow(true); });
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += delegate { SyncNow(true); };

            timer = new Timer();
            timer.Interval = 300;
            timer.Tick += delegate { SyncNow(false); };
            timer.Start();

            SyncNow(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        private void SyncNow(bool force)
        {
            KeyboardLayoutKind layout;
            if (!WindowsLayout.TryGetForegroundLayout(out layout))
            {
                SetTrayText("Keyboard layout sync: unsupported layout");
                return;
            }

            if (!force &&
                lastSentLayout.HasValue &&
                lastSentLayout.Value == layout &&
                (DateTime.UtcNow - lastSentAt).TotalMilliseconds < ForceResendIntervalMs)
            {
                return;
            }

            int sentCount = RawHidSender.SendLayout(layout);
            if (sentCount > 0)
            {
                lastSentLayout = layout;
                lastSentAt = DateTime.UtcNow;
                SetTrayText("Keyboard layout sync: " + LayoutName(layout));
            }
            else
            {
                SetTrayText("Keyboard layout sync: Raw HID not found");
            }
        }

        private void SetTrayText(string text)
        {
            notifyIcon.Text = text.Length <= 63 ? text : text.Substring(0, 63);
        }

        private static string LayoutName(KeyboardLayoutKind layout)
        {
            return layout == KeyboardLayoutKind.Russian ? "RU" : "EN";
        }
    }

    internal static class WindowsLayout
    {
        private const ushort PrimaryLanguageMask = 0x03ff;
        private const ushort EnglishPrimaryLanguage = 0x0009;
        private const ushort RussianPrimaryLanguage = 0x0019;

        public static bool TryGetForegroundLayout(out KeyboardLayoutKind layout)
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            uint processId;
            uint threadId = foregroundWindow == IntPtr.Zero
                ? 0
                : GetWindowThreadProcessId(foregroundWindow, out processId);

            IntPtr keyboardLayout = GetKeyboardLayout(threadId);
            ushort languageId = unchecked((ushort)(keyboardLayout.ToInt64() & 0xffff));
            ushort primaryLanguage = unchecked((ushort)(languageId & PrimaryLanguageMask));

            if (primaryLanguage == EnglishPrimaryLanguage)
            {
                layout = KeyboardLayoutKind.English;
                return true;
            }

            if (primaryLanguage == RussianPrimaryLanguage)
            {
                layout = KeyboardLayoutKind.Russian;
                return true;
            }

            layout = KeyboardLayoutKind.English;
            return false;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);
    }

    internal static class RawHidSender
    {
        private const ushort QmkRawHidUsagePage = 0xff60;
        private const ushort QmkRawHidUsage = 0x0061;
        private const int HidpStatusSuccess = 0x00110000;

        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;

        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        public static int SendLayout(KeyboardLayoutKind layout)
        {
            int sentCount = 0;
            List<HidDeviceInfo> devices = FindRawHidDevices();
            for (int i = 0; i < devices.Count; i++)
            {
                if (TrySendLayout(devices[i], layout))
                {
                    sentCount++;
                }
            }
            return sentCount;
        }

        private static List<HidDeviceInfo> FindRawHidDevices()
        {
            var devices = new List<HidDeviceInfo>();
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr infoSet = SetupDiGetClassDevs(
                ref hidGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                DigcfPresent | DigcfDeviceInterface);

            if (infoSet == InvalidHandleValue)
            {
                return devices;
            }

            try
            {
                uint index = 0;
                while (true)
                {
                    var interfaceData = new SpDeviceInterfaceData();
                    interfaceData.cbSize = (uint)Marshal.SizeOf(typeof(SpDeviceInterfaceData));

                    if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    {
                        break;
                    }

                    string path = GetDevicePath(infoSet, ref interfaceData);
                    if (!string.IsNullOrEmpty(path))
                    {
                        HidDeviceInfo device;
                        if (TryReadRawHidDeviceInfo(path, out device))
                        {
                            devices.Add(device);
                        }
                    }

                    index++;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(infoSet);
            }

            return devices;
        }

        private static bool TryReadRawHidDeviceInfo(string path, out HidDeviceInfo device)
        {
            device = null;
            using (SafeFileHandle handle = OpenDevice(path, 0))
            {
                if (handle == null || handle.IsInvalid)
                {
                    return false;
                }

                HidpCaps caps;
                if (!TryGetCaps(handle, out caps))
                {
                    return false;
                }

                if (caps.UsagePage != QmkRawHidUsagePage || caps.Usage != QmkRawHidUsage)
                {
                    return false;
                }

                if (caps.OutputReportByteLength == 0)
                {
                    return false;
                }

                device = new HidDeviceInfo(path, caps.OutputReportByteLength);
                return true;
            }
        }

        private static bool TrySendLayout(HidDeviceInfo device, KeyboardLayoutKind layout)
        {
            byte[] payload = CreateLayoutPacket(layout);
            int reportLength = Math.Max(device.OutputReportByteLength, payload.Length + 1);
            byte[] report = new byte[reportLength];

            report[0] = 0;
            Buffer.BlockCopy(payload, 0, report, 1, Math.Min(payload.Length, report.Length - 1));

            using (SafeFileHandle handle = OpenDevice(device.Path, GenericWrite))
            {
                if (handle == null || handle.IsInvalid)
                {
                    return false;
                }

                uint written;
                if (WriteFile(handle, report, (uint)report.Length, out written, IntPtr.Zero) &&
                    written == report.Length)
                {
                    return true;
                }

                return HidD_SetOutputReport(handle, report, (uint)report.Length);
            }
        }

        private static byte[] CreateLayoutPacket(KeyboardLayoutKind layout)
        {
            byte[] packet = new byte[32];
            packet[0] = (byte)'M';
            packet[1] = (byte)'L';
            packet[2] = (byte)'N';
            packet[3] = (byte)'G';
            packet[4] = 1;
            packet[5] = 1;
            packet[6] = (byte)layout;
            return packet;
        }

        private static string GetDevicePath(IntPtr infoSet, ref SpDeviceInterfaceData interfaceData)
        {
            uint requiredSize;
            SetupDiGetDeviceInterfaceDetail(
                infoSet,
                ref interfaceData,
                IntPtr.Zero,
                0,
                out requiredSize,
                IntPtr.Zero);

            if (requiredSize == 0)
            {
                return null;
            }

            IntPtr detailData = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);

                if (!SetupDiGetDeviceInterfaceDetail(
                    infoSet,
                    ref interfaceData,
                    detailData,
                    requiredSize,
                    out requiredSize,
                    IntPtr.Zero))
                {
                    return null;
                }

                IntPtr pathPointer = new IntPtr(detailData.ToInt64() + 4);
                return Marshal.PtrToStringAuto(pathPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(detailData);
            }
        }

        private static SafeFileHandle OpenDevice(string path, uint desiredAccess)
        {
            return CreateFile(
                path,
                desiredAccess,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);
        }

        private static bool TryGetCaps(SafeFileHandle handle, out HidpCaps caps)
        {
            caps = new HidpCaps();
            IntPtr preparsedData;

            if (!HidD_GetPreparsedData(handle, out preparsedData))
            {
                return false;
            }

            try
            {
                return HidP_GetCaps(preparsedData, out caps) == HidpStatusSuccess;
            }
            finally
            {
                HidD_FreePreparsedData(preparsedData);
            }
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            SafeFileHandle file,
            byte[] buffer,
            uint numberOfBytesToWrite,
            out uint numberOfBytesWritten,
            IntPtr overlapped);

        private sealed class HidDeviceInfo
        {
            public readonly string Path;
            public readonly int OutputReportByteLength;

            public HidDeviceInfo(string path, int outputReportByteLength)
            {
                Path = path;
                OutputReportByteLength = outputReportByteLength;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;

            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }
    }
}
