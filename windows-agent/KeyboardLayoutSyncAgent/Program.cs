using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace KeyboardLayoutSyncAgent
{
    internal enum KeyboardLayoutKind : byte
    {
        English = 0,
        Russian = 1
    }

    internal enum AgentStatusKind
    {
        Synced,
        TemporaryIgnored,
        Warning,
        Error
    }

    internal enum HostLangSyncStatus : byte
    {
        Accepted = 1,
        IgnoredTemporary = 2,
        UnknownCommand = 3,
        UnknownLayout = 4,
        NoAck = 250,
        WriteFailed = 251
    }

    internal static class Program
    {
        private const string SingleInstanceMutexName = "Local\\KeyboardLayoutSyncAgent";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayAppContext());
            }
        }
    }

    internal sealed class TrayAppContext : ApplicationContext
    {
        private const int ForceResendIntervalMs = 1500;

        private readonly NotifyIcon notifyIcon;
        private readonly System.Windows.Forms.Timer timer;
        private readonly ToolStripMenuItem startupMenuItem;
        private readonly HotKeyWindow gridHotKeyWindow;
        private Icon currentIcon;
        private GridOverlayForm gridOverlayForm;
        private KeyboardLayoutKind? lastSentLayout;
        private DateTime lastSentAt = DateTime.MinValue;

        public TrayAppContext()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Sync now", null, delegate { SyncNow(true); });
            menu.Items.Add("Grid mode (Ctrl+Alt+G)", null, delegate { ShowGridMode(); });

            startupMenuItem = new ToolStripMenuItem("Start with Windows");
            startupMenuItem.Checked = StartupManager.IsEnabled();
            startupMenuItem.Click += delegate { ToggleStartup(); };
            menu.Items.Add(startupMenuItem);

            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += delegate { SyncNow(true); };

            SetStatus(AgentStatusKind.Warning, null, "Keyboard layout sync: starting");

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 300;
            timer.Tick += delegate { SyncNow(false); };
            timer.Start();

            gridHotKeyWindow = new HotKeyWindow(delegate { ShowGridMode(); });
            SyncNow(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                gridHotKeyWindow.Dispose();
                if (gridOverlayForm != null)
                {
                    gridOverlayForm.Close();
                    gridOverlayForm.Dispose();
                    gridOverlayForm = null;
                }
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                if (currentIcon != null)
                {
                    currentIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private void ToggleStartup()
        {
            bool enable = !StartupManager.IsEnabled();
            StartupManager.SetEnabled(enable);
            startupMenuItem.Checked = StartupManager.IsEnabled();
            SetStatus(AgentStatusKind.Warning, lastSentLayout, enable
                ? "Keyboard layout sync: autostart enabled"
                : "Keyboard layout sync: autostart disabled");
        }

        private void SyncNow(bool force)
        {
            KeyboardLayoutKind layout;
            if (!WindowsLayout.TryGetForegroundLayout(out layout))
            {
                SetStatus(AgentStatusKind.Warning, null, "Keyboard layout sync: unsupported layout");
                return;
            }

            if (!force &&
                lastSentLayout.HasValue &&
                lastSentLayout.Value == layout &&
                (DateTime.UtcNow - lastSentAt).TotalMilliseconds < ForceResendIntervalMs)
            {
                return;
            }

            RawHidSendSummary summary = RawHidSender.SendLayout(layout);
            if (summary.Accepted > 0)
            {
                MarkSent(layout);
                SetStatus(AgentStatusKind.Synced, layout, "Keyboard layout sync: " + LayoutName(layout) + " accepted");
            }
            else if (summary.IgnoredTemporary > 0)
            {
                MarkSent(layout);
                SetStatus(AgentStatusKind.TemporaryIgnored, layout, "Keyboard layout sync: " + LayoutName(layout) + " ignored temporarily");
            }
            else if (summary.NoAck > 0)
            {
                MarkSent(layout);
                SetStatus(AgentStatusKind.Warning, layout, "Keyboard layout sync: " + LayoutName(layout) + " sent, no ack");
            }
            else if (summary.DeviceCount > 0)
            {
                SetStatus(AgentStatusKind.Error, layout, "Keyboard layout sync: Raw HID write failed");
            }
            else
            {
                SetStatus(AgentStatusKind.Error, layout, "Keyboard layout sync: Moonlander Raw HID not found");
            }
        }

        private void MarkSent(KeyboardLayoutKind layout)
        {
            lastSentLayout = layout;
            lastSentAt = DateTime.UtcNow;
        }

        private void SetStatus(AgentStatusKind status, KeyboardLayoutKind? layout, string text)
        {
            notifyIcon.Text = text.Length <= 63 ? text : text.Substring(0, 63);
            SetIcon(status, layout);
        }

        private void SetIcon(AgentStatusKind status, KeyboardLayoutKind? layout)
        {
            string text = layout.HasValue ? LayoutName(layout.Value) : "--";
            Color color;

            switch (status)
            {
                case AgentStatusKind.Synced:
                    color = Color.FromArgb(32, 148, 83);
                    break;
                case AgentStatusKind.TemporaryIgnored:
                    color = Color.FromArgb(79, 126, 201);
                    break;
                case AgentStatusKind.Error:
                    color = Color.FromArgb(196, 57, 57);
                    break;
                default:
                    color = Color.FromArgb(190, 139, 28);
                    break;
            }

            Icon oldIcon = currentIcon;
            currentIcon = TrayIconFactory.CreateTextIcon(text, color);
            notifyIcon.Icon = currentIcon;
            if (oldIcon != null)
            {
                oldIcon.Dispose();
            }
        }

        private static string LayoutName(KeyboardLayoutKind layout)
        {
            return layout == KeyboardLayoutKind.Russian ? "RU" : "EN";
        }

        private void ShowGridMode()
        {
            if (gridOverlayForm != null && !gridOverlayForm.IsDisposed)
            {
                gridOverlayForm.Activate();
                return;
            }

            gridOverlayForm = new GridOverlayForm();
            gridOverlayForm.FormClosed += delegate { gridOverlayForm = null; };
            gridOverlayForm.Show();
        }
    }

    internal sealed class HotKeyWindow : NativeWindow, IDisposable
    {
        private const int HotKeyId = 1;
        private const int WmHotKey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;

        private readonly Action onHotKey;
        private bool registered;

        public HotKeyWindow(Action onHotKey)
        {
            this.onHotKey = onHotKey;
            CreateHandle(new CreateParams());
            registered = RegisterHotKey(Handle, HotKeyId, ModControl | ModAlt, (uint)Keys.G);
        }

        public void Dispose()
        {
            if (registered)
            {
                UnregisterHotKey(Handle, HotKeyId);
                registered = false;
            }

            DestroyHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotKey && m.WParam.ToInt32() == HotKeyId)
            {
                onHotKey();
                return;
            }

            base.WndProc(ref m);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    internal sealed class GridOverlayForm : Form
    {
        private const int GridSize = 3;
        private const int MaxDepth = 3;

        private static readonly string[] CellLabels = new string[]
        {
            "Q", "W", "E",
            "A", "S", "D",
            "Z", "X", "C"
        };

        private Rectangle virtualScreen;
        private Rectangle currentArea;
        private int depth;

        public GridOverlayForm()
        {
            virtualScreen = SystemInformation.VirtualScreen;
            currentArea = virtualScreen;

            Bounds = virtualScreen;
            BackColor = Color.Black;
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
            Opacity = 0.84;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            int cellIndex;

            if (TryGetCellIndex(key, out cellIndex))
            {
                SelectCell(cellIndex);
                return true;
            }

            switch (key)
            {
                case Keys.Escape:
                    Close();
                    return true;
                case Keys.Enter:
                case Keys.Space:
                    WarpAndClose();
                    return true;
                case Keys.Back:
                    ResetGrid();
                    return true;
                case Keys.M:
                    ClickAndClose(MouseClickKind.Left);
                    return true;
                case Keys.Oemcomma:
                    ClickAndClose(MouseClickKind.Middle);
                    return true;
                case Keys.OemPeriod:
                    ClickAndClose(MouseClickKind.Right);
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using (var dimBrush = new SolidBrush(Color.FromArgb(190, 8, 16, 14)))
            using (var areaBrush = new SolidBrush(Color.FromArgb(80, 42, 196, 117)))
            using (var borderPen = new Pen(Color.FromArgb(230, 94, 229, 148), 3))
            using (var gridPen = new Pen(Color.FromArgb(190, 236, 255, 241), 1))
            using (var labelBrush = new SolidBrush(Color.White))
            using (var hintBrush = new SolidBrush(Color.FromArgb(220, 235, 255, 240)))
            using (var labelFont = new Font(FontFamily.GenericSansSerif, LabelFontSize(), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var hintFont = new Font(FontFamily.GenericSansSerif, 15, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                e.Graphics.Clear(Color.Black);
                e.Graphics.FillRectangle(dimBrush, ClientRectangle);

                Rectangle area = ToClientRectangle(currentArea);
                e.Graphics.FillRectangle(areaBrush, area);
                e.Graphics.DrawRectangle(borderPen, area);

                DrawGrid(e.Graphics, area, gridPen);
                DrawCellLabels(e.Graphics, labelFont, labelBrush, format);
                DrawHint(e.Graphics, area, hintFont, hintBrush, format);
            }
        }

        private void SelectCell(int cellIndex)
        {
            currentArea = GetCellRectangle(currentArea, cellIndex);
            depth++;

            if (depth >= MaxDepth || currentArea.Width <= GridSize || currentArea.Height <= GridSize)
            {
                WarpAndClose();
                return;
            }

            Invalidate();
        }

        private void ResetGrid()
        {
            currentArea = virtualScreen;
            depth = 0;
            Invalidate();
        }

        private void WarpAndClose()
        {
            Point center = CenterOf(currentArea);
            Cursor.Position = center;
            Close();
        }

        private void ClickAndClose(MouseClickKind clickKind)
        {
            Point center = CenterOf(currentArea);
            Cursor.Position = center;
            SendMouseClick(clickKind);
            Close();
        }

        private static Point CenterOf(Rectangle rectangle)
        {
            return new Point(
                rectangle.Left + rectangle.Width / 2,
                rectangle.Top + rectangle.Height / 2);
        }

        private Rectangle ToClientRectangle(Rectangle screenRectangle)
        {
            return new Rectangle(
                screenRectangle.Left - virtualScreen.Left,
                screenRectangle.Top - virtualScreen.Top,
                screenRectangle.Width,
                screenRectangle.Height);
        }

        private static Rectangle GetCellRectangle(Rectangle area, int cellIndex)
        {
            int column = cellIndex % GridSize;
            int row = cellIndex / GridSize;

            int left = area.Left + area.Width * column / GridSize;
            int right = area.Left + area.Width * (column + 1) / GridSize;
            int top = area.Top + area.Height * row / GridSize;
            int bottom = area.Top + area.Height * (row + 1) / GridSize;

            if (right <= left)
            {
                right = left + 1;
            }
            if (bottom <= top)
            {
                bottom = top + 1;
            }

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static bool TryGetCellIndex(Keys key, out int cellIndex)
        {
            switch (key)
            {
                case Keys.Q:
                    cellIndex = 0;
                    return true;
                case Keys.W:
                    cellIndex = 1;
                    return true;
                case Keys.E:
                    cellIndex = 2;
                    return true;
                case Keys.A:
                    cellIndex = 3;
                    return true;
                case Keys.S:
                    cellIndex = 4;
                    return true;
                case Keys.D:
                    cellIndex = 5;
                    return true;
                case Keys.Z:
                    cellIndex = 6;
                    return true;
                case Keys.X:
                    cellIndex = 7;
                    return true;
                case Keys.C:
                    cellIndex = 8;
                    return true;
            }

            cellIndex = -1;
            return false;
        }

        private void DrawGrid(Graphics graphics, Rectangle area, Pen gridPen)
        {
            for (int i = 1; i < GridSize; i++)
            {
                int x = area.Left + area.Width * i / GridSize;
                int y = area.Top + area.Height * i / GridSize;

                graphics.DrawLine(gridPen, x, area.Top, x, area.Bottom);
                graphics.DrawLine(gridPen, area.Left, y, area.Right, y);
            }
        }

        private void DrawCellLabels(Graphics graphics, Font font, Brush brush, StringFormat format)
        {
            for (int i = 0; i < CellLabels.Length; i++)
            {
                Rectangle cell = ToClientRectangle(GetCellRectangle(currentArea, i));
                graphics.DrawString(CellLabels[i], font, brush, cell, format);
            }
        }

        private void DrawHint(Graphics graphics, Rectangle area, Font font, Brush brush, StringFormat format)
        {
            string hint = "Grid mode: QWE/ASD/ZXC select, Enter jumps, M clicks, Esc cancels";
            Rectangle hintArea = new Rectangle(area.Left, Math.Max(area.Top + 8, 8), area.Width, 24);
            graphics.DrawString(hint, font, brush, hintArea, format);
        }

        private int LabelFontSize()
        {
            int smallestSide = Math.Min(currentArea.Width, currentArea.Height);
            int size = smallestSide / 8;

            if (size < 22)
            {
                return 22;
            }
            if (size > 64)
            {
                return 64;
            }

            return size;
        }

        private static void SendMouseClick(MouseClickKind clickKind)
        {
            switch (clickKind)
            {
                case MouseClickKind.Left:
                    MouseEvent(MouseEventfLeftDown, MouseEventfLeftUp);
                    break;
                case MouseClickKind.Middle:
                    MouseEvent(MouseEventfMiddleDown, MouseEventfMiddleUp);
                    break;
                case MouseClickKind.Right:
                    MouseEvent(MouseEventfRightDown, MouseEventfRightUp);
                    break;
            }
        }

        private static void MouseEvent(uint down, uint up)
        {
            mouse_event(down, 0, 0, 0, UIntPtr.Zero);
            mouse_event(up, 0, 0, 0, UIntPtr.Zero);
        }

        private enum MouseClickKind
        {
            Left,
            Middle,
            Right
        }

        private const uint MouseEventfLeftDown = 0x0002;
        private const uint MouseEventfLeftUp = 0x0004;
        private const uint MouseEventfRightDown = 0x0008;
        private const uint MouseEventfRightUp = 0x0010;
        private const uint MouseEventfMiddleDown = 0x0020;
        private const uint MouseEventfMiddleUp = 0x0040;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    }

    internal static class TrayIconFactory
    {
        public static Icon CreateTextIcon(string text, Color background)
        {
            using (var bitmap = new Bitmap(16, 16))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(background))
            using (var pen = new Pen(Color.White))
            using (var font = new Font(FontFamily.GenericSansSerif, 6, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.Transparent);
                graphics.FillRectangle(brush, 0, 0, 15, 15);
                graphics.DrawRectangle(pen, 0, 0, 15, 15);
                TextRenderer.DrawText(
                    graphics,
                    text,
                    font,
                    new Rectangle(0, 1, 16, 14),
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                IntPtr iconHandle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(iconHandle).Clone();
                }
                finally
                {
                    DestroyIcon(iconHandle);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }

    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "KeyboardLayoutSyncAgent";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (key == null)
                {
                    return false;
                }

                string value = key.GetValue(RunValueName) as string;
                return string.Equals(value, GetRunCommand(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    key.SetValue(RunValueName, GetRunCommand(), RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
        }

        private static string GetRunCommand()
        {
            return "\"" + Application.ExecutablePath + "\"";
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

    internal sealed class RawHidSendSummary
    {
        public int DeviceCount;
        public int Accepted;
        public int IgnoredTemporary;
        public int UnknownCommand;
        public int UnknownLayout;
        public int NoAck;
        public int Failed;

        public void Add(HostLangSyncStatus status)
        {
            switch (status)
            {
                case HostLangSyncStatus.Accepted:
                    Accepted++;
                    break;
                case HostLangSyncStatus.IgnoredTemporary:
                    IgnoredTemporary++;
                    break;
                case HostLangSyncStatus.UnknownCommand:
                    UnknownCommand++;
                    break;
                case HostLangSyncStatus.UnknownLayout:
                    UnknownLayout++;
                    break;
                case HostLangSyncStatus.NoAck:
                    NoAck++;
                    break;
                default:
                    Failed++;
                    break;
            }
        }
    }

    internal static class RawHidSender
    {
        private const ushort QmkRawHidUsagePage = 0xff60;
        private const ushort QmkRawHidUsage = 0x0061;
        private const int HidpStatusSuccess = 0x00110000;
        private const int HostLangSyncPacketSize = 32;
        private const int AckReadTimeoutMs = 120;

        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;

        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        public static RawHidSendSummary SendLayout(KeyboardLayoutKind layout)
        {
            var summary = new RawHidSendSummary();
            List<HidDeviceInfo> devices = FindRawHidDevices();
            summary.DeviceCount = devices.Count;

            for (int i = 0; i < devices.Count; i++)
            {
                summary.Add(TrySendLayout(devices[i], layout));
            }

            return summary;
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

                HiddAttributes attributes = ReadAttributes(handle);
                string manufacturer = ReadManufacturerString(handle);
                string product = ReadProductString(handle);
                if (!IsMoonlanderDevice(path, attributes, manufacturer, product))
                {
                    return false;
                }

                device = new HidDeviceInfo(
                    path,
                    caps.OutputReportByteLength,
                    caps.InputReportByteLength,
                    manufacturer,
                    product,
                    attributes.VendorID,
                    attributes.ProductID);
                return true;
            }
        }

        private static HostLangSyncStatus TrySendLayout(HidDeviceInfo device, KeyboardLayoutKind layout)
        {
            using (SafeFileHandle handle = OpenDevice(device.Path, GenericRead | GenericWrite))
            {
                if (handle == null || handle.IsInvalid)
                {
                    return TrySendLayoutWithoutAck(device, layout)
                        ? HostLangSyncStatus.NoAck
                        : HostLangSyncStatus.WriteFailed;
                }

                byte[] report = CreateOutputReport(device, layout);
                if (!TryWriteReport(handle, report))
                {
                    return HostLangSyncStatus.WriteFailed;
                }

                HostLangSyncStatus status;
                if (TryReadAck(handle, device, out status))
                {
                    return status;
                }

                return HostLangSyncStatus.NoAck;
            }
        }

        private static bool TrySendLayoutWithoutAck(HidDeviceInfo device, KeyboardLayoutKind layout)
        {
            using (SafeFileHandle handle = OpenDevice(device.Path, GenericWrite))
            {
                if (handle == null || handle.IsInvalid)
                {
                    return false;
                }

                return TryWriteReport(handle, CreateOutputReport(device, layout));
            }
        }

        private static byte[] CreateOutputReport(HidDeviceInfo device, KeyboardLayoutKind layout)
        {
            byte[] payload = CreateLayoutPacket(layout);
            int reportLength = Math.Max(device.OutputReportByteLength, payload.Length + 1);
            byte[] report = new byte[reportLength];

            report[0] = 0;
            Buffer.BlockCopy(payload, 0, report, 1, Math.Min(payload.Length, report.Length - 1));
            return report;
        }

        private static bool TryWriteReport(SafeFileHandle handle, byte[] report)
        {
            uint written;
            if (WriteFile(handle, report, (uint)report.Length, out written, IntPtr.Zero) &&
                written == report.Length)
            {
                return true;
            }

            return HidD_SetOutputReport(handle, report, (uint)report.Length);
        }

        private static bool TryReadAck(SafeFileHandle handle, HidDeviceInfo device, out HostLangSyncStatus status)
        {
            int reportLength = Math.Max(device.InputReportByteLength, HostLangSyncPacketSize + 1);
            byte[] input = new byte[reportLength];
            uint bytesRead = 0;
            bool readOk = false;

            Thread readThread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    readOk = ReadFile(handle, input, (uint)input.Length, out bytesRead, IntPtr.Zero);
                }
                catch
                {
                    readOk = false;
                }
            }));
            readThread.IsBackground = true;
            readThread.Start();

            if (!readThread.Join(AckReadTimeoutMs))
            {
                handle.Dispose();
                readThread.Join(50);
                status = HostLangSyncStatus.NoAck;
                return false;
            }

            if (!readOk || bytesRead == 0)
            {
                status = HostLangSyncStatus.NoAck;
                return false;
            }

            return TryParseAck(input, (int)bytesRead, out status);
        }

        private static bool TryParseAck(byte[] report, int length, out HostLangSyncStatus status)
        {
            for (int offset = 0; offset <= 1; offset++)
            {
                if (length >= offset + 8 &&
                    report[offset + 0] == (byte)'M' &&
                    report[offset + 1] == (byte)'L' &&
                    report[offset + 2] == (byte)'N' &&
                    report[offset + 3] == (byte)'G' &&
                    report[offset + 4] == 1)
                {
                    status = (HostLangSyncStatus)report[offset + 7];
                    return true;
                }
            }

            status = HostLangSyncStatus.NoAck;
            return false;
        }

        private static byte[] CreateLayoutPacket(KeyboardLayoutKind layout)
        {
            byte[] packet = new byte[HostLangSyncPacketSize];
            packet[0] = (byte)'M';
            packet[1] = (byte)'L';
            packet[2] = (byte)'N';
            packet[3] = (byte)'G';
            packet[4] = 1;
            packet[5] = 1;
            packet[6] = (byte)layout;
            return packet;
        }

        private static bool IsMoonlanderDevice(string path, HiddAttributes attributes, string manufacturer, string product)
        {
            string text = ((path ?? string.Empty) + " " +
                           (manufacturer ?? string.Empty) + " " +
                           (product ?? string.Empty)).ToLowerInvariant();

            return text.Contains("moonlander") ||
                   text.Contains("zsa") ||
                   text.Contains("vid_3297") ||
                   attributes.VendorID == 0x3297;
        }

        private static HiddAttributes ReadAttributes(SafeFileHandle handle)
        {
            var attributes = new HiddAttributes();
            attributes.Size = Marshal.SizeOf(typeof(HiddAttributes));
            HidD_GetAttributes(handle, ref attributes);
            return attributes;
        }

        private static string ReadManufacturerString(SafeFileHandle handle)
        {
            return ReadHidString(handle, HidD_GetManufacturerString);
        }

        private static string ReadProductString(SafeFileHandle handle)
        {
            return ReadHidString(handle, HidD_GetProductString);
        }

        private delegate bool HidStringReader(SafeFileHandle hidDeviceObject, byte[] buffer, uint bufferLength);

        private static string ReadHidString(SafeFileHandle handle, HidStringReader reader)
        {
            byte[] buffer = new byte[256];
            if (!reader(handle, buffer, (uint)buffer.Length))
            {
                return string.Empty;
            }

            return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
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

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HiddAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetManufacturerString(SafeFileHandle hidDeviceObject, byte[] buffer, uint bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetProductString(SafeFileHandle hidDeviceObject, byte[] buffer, uint bufferLength);

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            SafeFileHandle file,
            byte[] buffer,
            uint numberOfBytesToRead,
            out uint numberOfBytesRead,
            IntPtr overlapped);

        private sealed class HidDeviceInfo
        {
            public readonly string Path;
            public readonly int OutputReportByteLength;
            public readonly int InputReportByteLength;
            public readonly string Manufacturer;
            public readonly string Product;
            public readonly ushort VendorId;
            public readonly ushort ProductId;

            public HidDeviceInfo(
                string path,
                int outputReportByteLength,
                int inputReportByteLength,
                string manufacturer,
                string product,
                ushort vendorId,
                ushort productId)
            {
                Path = path;
                OutputReportByteLength = outputReportByteLength;
                InputReportByteLength = inputReportByteLength;
                Manufacturer = manufacturer;
                Product = product;
                VendorId = vendorId;
                ProductId = productId;
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
        private struct HiddAttributes
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
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
