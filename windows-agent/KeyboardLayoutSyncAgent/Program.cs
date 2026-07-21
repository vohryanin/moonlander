using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
        private const uint IdleRawHidPauseMs = 5000;

        private readonly AgentDiagnostics diagnostics = new AgentDiagnostics();
        private readonly NotifyIcon notifyIcon;
        private readonly System.Windows.Forms.Timer timer;
        private readonly ToolStripMenuItem startupMenuItem;
        private readonly HotKeyWindow gridHotKeyWindow;
        private Icon currentIcon;
        private GridOverlayForm gridOverlayForm;
        private DiagnosticsForm diagnosticsForm;
        private bool pausedForIdle;
        private KeyboardLayoutKind? lastSentLayout;
        private DateTime lastSentAt = DateTime.MinValue;

        public TrayAppContext()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Sync now", null, delegate { SyncNow(true); });
            menu.Items.Add("Refresh telemetry", null, delegate { RefreshTelemetry(); });
            menu.Items.Add("Grid mode (Ctrl+Alt+G)", null, delegate { ShowGridMode(); });
            menu.Items.Add("Diagnostics", null, delegate { ShowDiagnostics(); });

            startupMenuItem = new ToolStripMenuItem("Start with Windows");
            startupMenuItem.Checked = StartupManager.IsEnabled();
            startupMenuItem.Click += delegate { ToggleStartup(); };
            menu.Items.Add(startupMenuItem);

            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += delegate { SyncNow(true); };

            SetStatus(AgentStatusKind.Warning, null, "Keyboard layout sync: starting");
            SetLastAction("agent started", true);

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
                if (diagnosticsForm != null)
                {
                    diagnosticsForm.Close();
                    diagnosticsForm.Dispose();
                    diagnosticsForm = null;
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
            SetLastAction(enable ? "autostart enabled" : "autostart disabled", true);
        }

        private void SyncNow(bool force)
        {
            uint idleMilliseconds;
            bool hasIdle = WindowsIdle.TryGetIdleMilliseconds(out idleMilliseconds);
            if (hasIdle)
            {
                diagnostics.IdleMilliseconds = idleMilliseconds;
            }

            if (!force && hasIdle && idleMilliseconds >= IdleRawHidPauseMs)
            {
                if (!pausedForIdle)
                {
                    pausedForIdle = true;
                    SetStatus(AgentStatusKind.Warning, lastSentLayout, "Keyboard layout sync: paused while Windows is idle");
                    SetLastAction("paused: Windows idle " + idleMilliseconds + " ms", true);
                }
                else
                {
                    UpdateDiagnosticsTime();
                }
                return;
            }

            if (pausedForIdle)
            {
                SetLastAction("resumed: Windows input returned after " + idleMilliseconds + " ms idle", true);
            }
            pausedForIdle = false;

            KeyboardLayoutKind layout;
            if (!WindowsLayout.TryGetForegroundLayout(out layout))
            {
                SetStatus(AgentStatusKind.Warning, null, "Keyboard layout sync: unsupported layout");
                SetLastAction("skipped: unsupported foreground layout", true);
                return;
            }

            if (!force &&
                lastSentLayout.HasValue &&
                lastSentLayout.Value == layout)
            {
                SetLastAction("skipped: " + LayoutName(layout) + " is already synced", false);
                return;
            }

            RawHidSendSummary summary = RawHidSender.SendLayout(layout);
            SetLastSummary(summary);
            if (summary.Accepted > 0)
            {
                MarkSent(layout);
                SetStatus(AgentStatusKind.Synced, layout, "Keyboard layout sync: " + LayoutName(layout) + " accepted");
                SetLastAction(SyncActionText(force, layout, "accepted", summary), true);
            }
            else if (summary.IgnoredTemporary > 0)
            {
                MarkSent(layout);
                SetStatus(AgentStatusKind.TemporaryIgnored, layout, "Keyboard layout sync: " + LayoutName(layout) + " ignored temporarily");
                SetLastAction(SyncActionText(force, layout, "ignored temporarily", summary), true);
            }
            else if (summary.NoAck > 0)
            {
                MarkSent(layout);
                SetStatus(AgentStatusKind.Warning, layout, "Keyboard layout sync: " + LayoutName(layout) + " sent, no ack");
                SetLastAction(SyncActionText(force, layout, "sent, no ack", summary), true);
            }
            else if (summary.DeviceCount > 0)
            {
                SetStatus(AgentStatusKind.Error, layout, "Keyboard layout sync: Raw HID write failed");
                SetLastAction(SyncActionText(force, layout, "write failed", summary), true);
            }
            else
            {
                SetStatus(AgentStatusKind.Error, layout, "Keyboard layout sync: Moonlander Raw HID not found");
                SetLastAction(SyncActionText(force, layout, "Raw HID not found", summary), true);
            }
        }

        private void MarkSent(KeyboardLayoutKind layout)
        {
            lastSentLayout = layout;
            lastSentAt = DateTime.UtcNow;
            diagnostics.LastSentLayout = layout;
            diagnostics.LastSentAt = lastSentAt;
        }

        private void SetStatus(AgentStatusKind status, KeyboardLayoutKind? layout, string text)
        {
            notifyIcon.Text = text.Length <= 63 ? text : text.Substring(0, 63);
            diagnostics.Status = status;
            diagnostics.CurrentLayout = layout;
            diagnostics.StatusText = text;
            diagnostics.IsPausedForIdle = pausedForIdle;
            UpdateDiagnosticsTime();
            SetIcon(status, layout);
        }

        private void SetIcon(AgentStatusKind status, KeyboardLayoutKind? layout)
        {
            string text = layout.HasValue ? LayoutName(layout.Value) : "--";
            Color background;
            Color foreground = Color.White;

            switch (status)
            {
                case AgentStatusKind.Synced:
                    background = Color.FromArgb(20, 126, 72);
                    break;
                case AgentStatusKind.TemporaryIgnored:
                    background = Color.FromArgb(45, 100, 190);
                    break;
                case AgentStatusKind.Error:
                    background = Color.FromArgb(180, 40, 40);
                    break;
                default:
                    background = Color.FromArgb(245, 178, 32);
                    foreground = Color.FromArgb(24, 24, 24);
                    break;
            }

            Icon oldIcon = currentIcon;
            currentIcon = TrayIconFactory.CreateTextIcon(text, background, foreground);
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

        private void SetLastSummary(RawHidSendSummary summary)
        {
            diagnostics.DeviceCount = summary.DeviceCount;
            diagnostics.Accepted = summary.Accepted;
            diagnostics.IgnoredTemporary = summary.IgnoredTemporary;
            diagnostics.UnknownCommand = summary.UnknownCommand;
            diagnostics.UnknownLayout = summary.UnknownLayout;
            diagnostics.NoAck = summary.NoAck;
            diagnostics.Failed = summary.Failed;
            if (summary.Telemetry != null && summary.Telemetry.HasData)
            {
                diagnostics.Telemetry = summary.Telemetry.Clone();
            }
            UpdateDiagnosticsTime();
        }

        private void SetLastAction(string action, bool writeLog)
        {
            diagnostics.LastAction = action;
            diagnostics.LogPath = AgentLog.LogPath;
            UpdateDiagnosticsTime();

            if (writeLog)
            {
                AgentLog.Write(action);
            }
        }

        private void UpdateDiagnosticsTime()
        {
            diagnostics.UpdatedAt = DateTime.Now;
        }

        private AgentDiagnostics GetDiagnosticsSnapshot()
        {
            uint idleMilliseconds;
            if (WindowsIdle.TryGetIdleMilliseconds(out idleMilliseconds))
            {
                diagnostics.IdleMilliseconds = idleMilliseconds;
            }

            diagnostics.IsPausedForIdle = pausedForIdle;
            diagnostics.LogPath = AgentLog.LogPath;
            UpdateDiagnosticsTime();
            return diagnostics.Clone();
        }

        private static string SyncActionText(bool force, KeyboardLayoutKind layout, string result, RawHidSendSummary summary)
        {
            return (force ? "manual" : "timer") + " sync " + LayoutName(layout) + ": " +
                result + " (" + summary.ToCompactString() + ")";
        }

        private void RefreshTelemetry()
        {
            RefreshTelemetry(true, "manual telemetry");
        }

        private void RefreshTelemetry(bool writeLog, string actionPrefix)
        {
            RawHidSendSummary summary = RawHidSender.RequestTelemetry();
            SetLastSummary(summary);

            KeyboardLayoutKind? layout = TelemetryLayout(summary.Telemetry);
            if (!layout.HasValue)
            {
                layout = lastSentLayout;
            }

            if (summary.Accepted > 0)
            {
                SetStatus(AgentStatusKind.Synced, layout, "Keyboard telemetry: accepted");
                SetLastAction(actionPrefix + ": accepted (" + summary.ToCompactString() + ")", writeLog);
            }
            else if (summary.NoAck > 0)
            {
                SetStatus(AgentStatusKind.Warning, layout, "Keyboard telemetry: sent, no ack");
                SetLastAction(actionPrefix + ": sent, no ack (" + summary.ToCompactString() + ")", writeLog);
            }
            else if (summary.DeviceCount > 0)
            {
                SetStatus(AgentStatusKind.Error, layout, "Keyboard telemetry: Raw HID write failed");
                SetLastAction(actionPrefix + ": write failed (" + summary.ToCompactString() + ")", writeLog);
            }
            else
            {
                SetStatus(AgentStatusKind.Error, layout, "Keyboard telemetry: Moonlander Raw HID not found");
                SetLastAction(actionPrefix + ": Raw HID not found", writeLog);
            }
        }

        private static KeyboardLayoutKind? TelemetryLayout(KeyboardTelemetry telemetry)
        {
            if (telemetry == null || !telemetry.HasData)
            {
                return null;
            }

            if (telemetry.LangShouldBe == (int)KeyboardLayoutKind.Russian)
            {
                return KeyboardLayoutKind.Russian;
            }

            if (telemetry.LangShouldBe == (int)KeyboardLayoutKind.English)
            {
                return KeyboardLayoutKind.English;
            }

            return null;
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
            SetLastAction("grid mode opened", true);
        }

        private void ShowDiagnostics()
        {
            if (diagnosticsForm != null && !diagnosticsForm.IsDisposed)
            {
                diagnosticsForm.Activate();
                return;
            }

            diagnosticsForm = new DiagnosticsForm(
                delegate { return GetDiagnosticsSnapshot(); },
                delegate { RefreshDiagnosticsTelemetry(); });
            diagnosticsForm.FormClosed += delegate { diagnosticsForm = null; };
            diagnosticsForm.Show();
            SetLastAction("diagnostics opened", true);
        }

        private void RefreshDiagnosticsTelemetry()
        {
            RefreshTelemetry(false, "diagnostics telemetry");
        }
    }

    internal sealed class AgentDiagnostics
    {
        public DateTime StartedAt = DateTime.Now;
        public DateTime UpdatedAt = DateTime.Now;
        public AgentStatusKind Status = AgentStatusKind.Warning;
        public KeyboardLayoutKind? CurrentLayout;
        public KeyboardLayoutKind? LastSentLayout;
        public DateTime LastSentAt = DateTime.MinValue;
        public string StatusText = "starting";
        public string LastAction = "starting";
        public string LogPath = AgentLog.LogPath;
        public uint IdleMilliseconds;
        public bool IsPausedForIdle;
        public int DeviceCount;
        public int Accepted;
        public int IgnoredTemporary;
        public int UnknownCommand;
        public int UnknownLayout;
        public int NoAck;
        public int Failed;
        public KeyboardTelemetry Telemetry = new KeyboardTelemetry();

        public AgentDiagnostics Clone()
        {
            AgentDiagnostics clone = new AgentDiagnostics();
            clone.StartedAt = StartedAt;
            clone.UpdatedAt = UpdatedAt;
            clone.Status = Status;
            clone.CurrentLayout = CurrentLayout;
            clone.LastSentLayout = LastSentLayout;
            clone.LastSentAt = LastSentAt;
            clone.StatusText = StatusText;
            clone.LastAction = LastAction;
            clone.LogPath = LogPath;
            clone.IdleMilliseconds = IdleMilliseconds;
            clone.IsPausedForIdle = IsPausedForIdle;
            clone.DeviceCount = DeviceCount;
            clone.Accepted = Accepted;
            clone.IgnoredTemporary = IgnoredTemporary;
            clone.UnknownCommand = UnknownCommand;
            clone.UnknownLayout = UnknownLayout;
            clone.NoAck = NoAck;
            clone.Failed = Failed;
            clone.Telemetry = Telemetry == null ? new KeyboardTelemetry() : Telemetry.Clone();
            return clone;
        }
    }

    internal sealed class DiagnosticsForm : Form
    {
        private const int WindowWidth = 760;
        private const int WindowHeight = 560;
        private const int ToolbarHeight = 36;
        private const int TextRefreshIntervalMs = 1000;
        private const int LiveTelemetryIntervalMs = 1000;
        private const string LiveTelemetryHint = "1s polling, only while this window is open";

        private readonly Func<AgentDiagnostics> snapshotProvider;
        private readonly Action telemetryRefreshAction;
        private readonly TextBox textBox;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly System.Windows.Forms.Timer liveTelemetryTimer;
        private readonly CheckBox liveTelemetryCheckBox;

        public DiagnosticsForm(Func<AgentDiagnostics> snapshotProvider, Action telemetryRefreshAction)
        {
            this.snapshotProvider = snapshotProvider;
            this.telemetryRefreshAction = telemetryRefreshAction;

            Text = "Keyboard Layout Sync Diagnostics";
            Width = WindowWidth;
            Height = WindowHeight;
            StartPosition = FormStartPosition.CenterScreen;

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ToolbarHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            var toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.FlowDirection = FlowDirection.LeftToRight;
            toolbar.WrapContents = false;
            toolbar.Padding = new Padding(6, 5, 6, 4);
            layout.Controls.Add(toolbar, 0, 0);

            var refreshTelemetryButton = new Button();
            refreshTelemetryButton.Text = "Refresh now";
            refreshTelemetryButton.AutoSize = true;
            refreshTelemetryButton.Margin = new Padding(0, 0, 12, 0);
            refreshTelemetryButton.Click += delegate { RefreshTelemetryOnce(); };
            toolbar.Controls.Add(refreshTelemetryButton);

            liveTelemetryCheckBox = new CheckBox();
            liveTelemetryCheckBox.Text = "Live telemetry";
            liveTelemetryCheckBox.AutoSize = true;
            liveTelemetryCheckBox.Margin = new Padding(0, 5, 12, 0);
            liveTelemetryCheckBox.CheckedChanged += delegate { SetLiveTelemetry(liveTelemetryCheckBox.Checked); };
            toolbar.Controls.Add(liveTelemetryCheckBox);

            var copyButton = new Button();
            copyButton.Text = "Copy";
            copyButton.AutoSize = true;
            copyButton.Margin = new Padding(0, 0, 12, 0);
            copyButton.Click += delegate { CopyDiagnostics(); };
            toolbar.Controls.Add(copyButton);

            var openLogFolderButton = new Button();
            openLogFolderButton.Text = "Open log folder";
            openLogFolderButton.AutoSize = true;
            openLogFolderButton.Margin = new Padding(0, 0, 12, 0);
            openLogFolderButton.Click += delegate { OpenLogFolder(); };
            toolbar.Controls.Add(openLogFolderButton);

            var hintLabel = new Label();
            hintLabel.Text = LiveTelemetryHint;
            hintLabel.AutoSize = true;
            hintLabel.Margin = new Padding(0, 7, 0, 0);
            toolbar.Controls.Add(hintLabel);

            textBox = new TextBox();
            textBox.Dock = DockStyle.Fill;
            textBox.Multiline = true;
            textBox.ReadOnly = true;
            textBox.ScrollBars = ScrollBars.Both;
            textBox.WordWrap = false;
            textBox.Font = new Font(FontFamily.GenericMonospace, 9, FontStyle.Regular, GraphicsUnit.Point);
            layout.Controls.Add(textBox, 0, 1);

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = TextRefreshIntervalMs;
            refreshTimer.Tick += delegate { RefreshText(); };
            refreshTimer.Start();

            liveTelemetryTimer = new System.Windows.Forms.Timer();
            liveTelemetryTimer.Interval = LiveTelemetryIntervalMs;
            liveTelemetryTimer.Tick += delegate { RefreshTelemetryOnce(); };

            RefreshText();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                liveTelemetryTimer.Stop();
                refreshTimer.Dispose();
                liveTelemetryTimer.Dispose();
                textBox.Dispose();
            }

            base.Dispose(disposing);
        }

        private void SetLiveTelemetry(bool enabled)
        {
            if (enabled)
            {
                RefreshTelemetryOnce();
                liveTelemetryTimer.Start();
                return;
            }

            liveTelemetryTimer.Stop();
            RefreshText();
        }

        private void RefreshTelemetryOnce()
        {
            if (telemetryRefreshAction != null)
            {
                telemetryRefreshAction();
            }

            RefreshText();
        }

        private void CopyDiagnostics()
        {
            RefreshText();
            Clipboard.SetText(textBox.Text);
        }

        private void OpenLogFolder()
        {
            string logPath = AgentLog.LogPath;
            string directory = Path.GetDirectoryName(logPath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            try
            {
                if (File.Exists(logPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + logPath + "\"");
                    return;
                }

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Process.Start("explorer.exe", "\"" + directory + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Could not open log folder." + Environment.NewLine + ex.Message,
                    "Keyboard Layout Sync Diagnostics",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void RefreshText()
        {
            string text = BuildText(snapshotProvider());
            if (textBox.Text != text)
            {
                textBox.Text = text;
                textBox.SelectionStart = 0;
                textBox.SelectionLength = 0;
            }
        }

        private static string BuildText(AgentDiagnostics diagnostics)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Keyboard Layout Sync Agent");
            builder.AppendLine();
            builder.AppendLine("Status:          " + diagnostics.Status + " - " + diagnostics.StatusText);
            builder.AppendLine("Current layout:  " + LayoutText(diagnostics.CurrentLayout));
            builder.AppendLine("Last sent:       " + LayoutText(diagnostics.LastSentLayout) + " at " + DateText(diagnostics.LastSentAt));
            builder.AppendLine("Windows idle:    " + IdleText(diagnostics.IdleMilliseconds));
            builder.AppendLine("Idle pause:      " + (diagnostics.IsPausedForIdle ? "yes" : "no"));
            builder.AppendLine("Last action:     " + diagnostics.LastAction);
            builder.AppendLine("Updated:         " + DateText(diagnostics.UpdatedAt));
            builder.AppendLine("Started:         " + DateText(diagnostics.StartedAt));
            builder.AppendLine();
            builder.AppendLine("Raw HID last summary");
            builder.AppendLine("Devices:         " + diagnostics.DeviceCount);
            builder.AppendLine("Accepted:        " + diagnostics.Accepted);
            builder.AppendLine("Ignored temp:    " + diagnostics.IgnoredTemporary);
            builder.AppendLine("No ACK:          " + diagnostics.NoAck);
            builder.AppendLine("Failed:          " + diagnostics.Failed);
            builder.AppendLine("Unknown command: " + diagnostics.UnknownCommand);
            builder.AppendLine("Unknown layout:  " + diagnostics.UnknownLayout);
            builder.AppendLine();
            AppendTelemetry(builder, diagnostics.Telemetry);
            builder.AppendLine();
            builder.AppendLine("Log file:        " + diagnostics.LogPath);
            builder.AppendLine();
            builder.AppendLine("Recent log");
            builder.AppendLine(AgentLog.ReadTail(24));
            return builder.ToString();
        }

        private static void AppendTelemetry(StringBuilder builder, KeyboardTelemetry telemetry)
        {
            builder.AppendLine("Keyboard telemetry");
            if (telemetry == null || !telemetry.HasData)
            {
                builder.AppendLine("(no telemetry yet)");
                return;
            }

            builder.AppendLine("Received:        " + DateText(telemetry.ReceivedAt));
            builder.AppendLine("Command/status:  " + telemetry.Command + " / " + telemetry.Status);
            builder.AppendLine("Lang should/current: " + LangText(telemetry.LangShouldBe) + " / " + LangText(telemetry.LangCurrent));
            builder.AppendLine("Highest layer:   " + telemetry.HighestLayer);
            builder.AppendLine("Layer state:     0x" + telemetry.LayerState.ToString("X8"));
            builder.AppendLine("Lighting idle:   " + (telemetry.LightingIdleSleeping ? "sleeping" : "awake"));
            builder.AppendLine("RGB enabled:     " + (telemetry.RgbEnabled ? "yes" : "no"));
            builder.AppendLine("Mouse dirs:      " + telemetry.MouseDirectionsText());
            builder.AppendLine("Mouse flags:     " + telemetry.MouseFlagsText());
            builder.AppendLine("Mouse velocity:  " + telemetry.MouseVelocityText());
            builder.AppendLine("Shift should/current: " + telemetry.ShiftShouldBe + " / " + telemetry.ShiftCurrent);
            builder.AppendLine("Pressed counts:  lang=" + telemetry.LangPressedCount +
                ", shift=" + telemetry.ShiftPressedCount +
                ", langShift=" + telemetry.LangShiftPressedCount +
                ", comboStack=" + telemetry.ComboStackSize +
                ", comboActive=" + telemetry.ComboActiveKeyCount);
        }

        private static string LayoutText(KeyboardLayoutKind? layout)
        {
            if (!layout.HasValue)
            {
                return "-";
            }

            return layout.Value == KeyboardLayoutKind.Russian ? "RU" : "EN";
        }

        private static string LangText(int lang)
        {
            if (lang == (int)KeyboardLayoutKind.Russian)
            {
                return "RU";
            }
            if (lang == (int)KeyboardLayoutKind.English)
            {
                return "EN";
            }

            return lang.ToString();
        }

        private static string IdleText(uint milliseconds)
        {
            return (milliseconds / 1000.0).ToString("0.0") + " s (" + milliseconds + " ms)";
        }

        private static string DateText(DateTime dateTime)
        {
            if (dateTime == DateTime.MinValue)
            {
                return "-";
            }

            return dateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    internal static class AgentLog
    {
        private const long MaxLogBytes = 512 * 1024;
        private static readonly object SyncRoot = new object();
        private static readonly string logPath = BuildLogPath();

        public static string LogPath
        {
            get { return logPath; }
        }

        public static void Write(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    string directory = Path.GetDirectoryName(logPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    RotateIfNeeded();
                    File.AppendAllText(
                        logPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        public static string ReadTail(int maxLines)
        {
            try
            {
                lock (SyncRoot)
                {
                    if (!File.Exists(logPath))
                    {
                        return "(log is empty)";
                    }

                    string[] lines = File.ReadAllLines(logPath, Encoding.UTF8);
                    int start = Math.Max(0, lines.Length - maxLines);
                    StringBuilder builder = new StringBuilder();
                    for (int i = start; i < lines.Length; i++)
                    {
                        builder.AppendLine(lines[i]);
                    }

                    return builder.ToString();
                }
            }
            catch
            {
                return "(log is unavailable)";
            }
        }

        private static string BuildLogPath()
        {
            string directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(directory))
            {
                directory = Application.StartupPath;
            }

            return Path.Combine(Path.Combine(directory, "KeyboardLayoutSyncAgent"), "agent.log");
        }

        private static void RotateIfNeeded()
        {
            FileInfo info = new FileInfo(logPath);
            if (!info.Exists || info.Length < MaxLogBytes)
            {
                return;
            }

            string oldPath = logPath + ".old";
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }

            File.Move(logPath, oldPath);
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
        private const int IconSize = 32;
        private const int IconMax = IconSize - 1;
        private const int TextFontSize = 21;
        private const int TextPaddingX = 2;
        private const int TextPaddingY = 2;
        private static readonly TextFormatFlags IconTextFlags =
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine;
        private static readonly string[] FontNames = { "Arial", "Segoe UI", "Tahoma" };

        public static Icon CreateTextIcon(string text, Color background, Color foreground)
        {
            Rectangle textBounds = new Rectangle(
                TextPaddingX,
                TextPaddingY,
                IconSize - TextPaddingX * 2,
                IconSize - TextPaddingY * 2);

            using (var bitmap = new Bitmap(IconSize, IconSize))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(background))
            using (var font = CreateIconFont())
            {
                graphics.Clear(Color.Transparent);
                graphics.FillRectangle(brush, 0, 0, IconMax, IconMax);
                TextRenderer.DrawText(
                    graphics,
                    text,
                    font,
                    textBounds,
                    foreground,
                    IconTextFlags);

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

        private static Font CreateIconFont()
        {
            foreach (string fontName in FontNames)
            {
                try
                {
                    return new Font(fontName, TextFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                }
                catch
                {
                }
            }

            return new Font(FontFamily.GenericSansSerif, TextFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
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

    internal static class WindowsIdle
    {
        public static bool IsIdleFor(uint milliseconds)
        {
            uint idleMilliseconds;
            return TryGetIdleMilliseconds(out idleMilliseconds) &&
                idleMilliseconds >= milliseconds;
        }

        public static bool TryGetIdleMilliseconds(out uint idleMilliseconds)
        {
            LastInputInfo info = new LastInputInfo();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LastInputInfo));

            if (!GetLastInputInfo(ref info))
            {
                idleMilliseconds = 0;
                return false;
            }

            idleMilliseconds = GetTickCount() - info.dwTime;
            return true;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetLastInputInfo(ref LastInputInfo plii);

        [DllImport("kernel32.dll")]
        private static extern uint GetTickCount();

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint cbSize;
            public uint dwTime;
        }
    }

    internal sealed class KeyboardTelemetry
    {
        public bool HasData;
        public DateTime ReceivedAt = DateTime.MinValue;
        public int Command;
        public int Layout;
        public int Status;
        public int LangShouldBe;
        public int LangCurrent;
        public int HighestLayer;
        public uint LayerState;
        public bool LightingIdleSleeping;
        public bool RgbEnabled;
        public int MouseDirections;
        public int MouseFlags;
        public int MouseVelocityX;
        public int MouseVelocityY;
        public int MouseScale;
        public int ShiftShouldBe;
        public int ShiftCurrent;
        public int LangPressedCount;
        public int ShiftPressedCount;
        public int LangShiftPressedCount;
        public int ComboStackSize;
        public int ComboActiveKeyCount;
        public int TelemetryVersion;

        public KeyboardTelemetry Clone()
        {
            KeyboardTelemetry clone = new KeyboardTelemetry();
            clone.HasData = HasData;
            clone.ReceivedAt = ReceivedAt;
            clone.Command = Command;
            clone.Layout = Layout;
            clone.Status = Status;
            clone.LangShouldBe = LangShouldBe;
            clone.LangCurrent = LangCurrent;
            clone.HighestLayer = HighestLayer;
            clone.LayerState = LayerState;
            clone.LightingIdleSleeping = LightingIdleSleeping;
            clone.RgbEnabled = RgbEnabled;
            clone.MouseDirections = MouseDirections;
            clone.MouseFlags = MouseFlags;
            clone.MouseVelocityX = MouseVelocityX;
            clone.MouseVelocityY = MouseVelocityY;
            clone.MouseScale = MouseScale;
            clone.ShiftShouldBe = ShiftShouldBe;
            clone.ShiftCurrent = ShiftCurrent;
            clone.LangPressedCount = LangPressedCount;
            clone.ShiftPressedCount = ShiftPressedCount;
            clone.LangShiftPressedCount = LangShiftPressedCount;
            clone.ComboStackSize = ComboStackSize;
            clone.ComboActiveKeyCount = ComboActiveKeyCount;
            clone.TelemetryVersion = TelemetryVersion;
            return clone;
        }

        public string MouseDirectionsText()
        {
            StringBuilder builder = new StringBuilder();
            AppendDirection(builder, 1, "down");
            AppendDirection(builder, 2, "up");
            AppendDirection(builder, 4, "left");
            AppendDirection(builder, 8, "right");
            return builder.Length == 0 ? "none" : builder.ToString();
        }

        public string MouseFlagsText()
        {
            StringBuilder builder = new StringBuilder();
            AppendFlag(builder, 1, "precision");
            AppendFlag(builder, 2, "boost");
            AppendFlag(builder, 4, "large-active");
            AppendFlag(builder, 8, "velocity");
            return builder.Length == 0 ? "none" : builder.ToString();
        }

        public string MouseVelocityText()
        {
            if (MouseScale <= 0)
            {
                return MouseVelocityX + " / " + MouseVelocityY + " raw";
            }

            double x = (double)MouseVelocityX / MouseScale;
            double y = (double)MouseVelocityY / MouseScale;
            return x.ToString("0.00") + " / " + y.ToString("0.00") +
                " px/tick (" + MouseVelocityX + " / " + MouseVelocityY + " raw)";
        }

        public string ToCompactString()
        {
            if (!HasData)
            {
                return "telemetry=no";
            }

            return "telemetry=yes, layer=" + HighestLayer +
                ", lang=" + LangShouldBe + "/" + LangCurrent +
                ", idle=" + (LightingIdleSleeping ? "sleep" : "awake") +
                ", mouseDirs=" + MouseDirections +
                ", mouseFlags=" + MouseFlags;
        }

        private void AppendDirection(StringBuilder builder, int bit, string text)
        {
            if ((MouseDirections & bit) == 0)
            {
                return;
            }

            AppendWord(builder, text);
        }

        private void AppendFlag(StringBuilder builder, int bit, string text)
        {
            if ((MouseFlags & bit) == 0)
            {
                return;
            }

            AppendWord(builder, text);
        }

        private static void AppendWord(StringBuilder builder, string text)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(text);
        }
    }

    internal sealed class RawHidResponse
    {
        public HostLangSyncStatus Status;
        public KeyboardTelemetry Telemetry;

        public static RawHidResponse FromStatus(HostLangSyncStatus status)
        {
            RawHidResponse response = new RawHidResponse();
            response.Status = status;
            response.Telemetry = new KeyboardTelemetry();
            return response;
        }
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
        public KeyboardTelemetry Telemetry;

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

        public void Add(RawHidResponse response)
        {
            if (response == null)
            {
                Add(HostLangSyncStatus.WriteFailed);
                return;
            }

            Add(response.Status);
            if (response.Telemetry != null && response.Telemetry.HasData)
            {
                Telemetry = response.Telemetry.Clone();
            }
        }

        public string ToCompactString()
        {
            return "devices=" + DeviceCount +
                ", accepted=" + Accepted +
                ", ignored=" + IgnoredTemporary +
                ", noAck=" + NoAck +
                ", failed=" + Failed +
                ", unknownCommand=" + UnknownCommand +
                ", unknownLayout=" + UnknownLayout +
                ", " + (Telemetry == null ? "telemetry=no" : Telemetry.ToCompactString());
        }
    }

    internal static class RawHidSender
    {
        private const ushort QmkRawHidUsagePage = 0xff60;
        private const ushort QmkRawHidUsage = 0x0061;
        private const int HidpStatusSuccess = 0x00110000;
        private const int HostLangSyncPacketSize = 32;
        private const int AckReadTimeoutMs = 120;
        private const byte HostLangSyncSetLayout = 1;
        private const byte HostLangSyncGetStatus = 2;

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

        public static RawHidSendSummary RequestTelemetry()
        {
            var summary = new RawHidSendSummary();
            List<HidDeviceInfo> devices = FindRawHidDevices();
            summary.DeviceCount = devices.Count;

            for (int i = 0; i < devices.Count; i++)
            {
                summary.Add(TrySendPacket(devices[i], CreateStatusPacket()));
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

        private static RawHidResponse TrySendLayout(HidDeviceInfo device, KeyboardLayoutKind layout)
        {
            return TrySendPacket(device, CreateLayoutPacket(layout));
        }

        private static RawHidResponse TrySendPacket(HidDeviceInfo device, byte[] payload)
        {
            using (SafeFileHandle handle = OpenDevice(device.Path, GenericRead | GenericWrite))
            {
                if (handle == null || handle.IsInvalid)
                {
                    return TrySendPacketWithoutAck(device, payload)
                        ? RawHidResponse.FromStatus(HostLangSyncStatus.NoAck)
                        : RawHidResponse.FromStatus(HostLangSyncStatus.WriteFailed);
                }

                byte[] report = CreateOutputReport(device, payload);
                if (!TryWriteReport(handle, report))
                {
                    return RawHidResponse.FromStatus(HostLangSyncStatus.WriteFailed);
                }

                RawHidResponse response;
                if (TryReadAck(handle, device, out response))
                {
                    return response;
                }

                return RawHidResponse.FromStatus(HostLangSyncStatus.NoAck);
            }
        }

        private static bool TrySendPacketWithoutAck(HidDeviceInfo device, byte[] payload)
        {
            using (SafeFileHandle handle = OpenDevice(device.Path, GenericWrite))
            {
                if (handle == null || handle.IsInvalid)
                {
                    return false;
                }

                return TryWriteReport(handle, CreateOutputReport(device, payload));
            }
        }

        private static byte[] CreateOutputReport(HidDeviceInfo device, byte[] payload)
        {
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

        private static bool TryReadAck(SafeFileHandle handle, HidDeviceInfo device, out RawHidResponse response)
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
                response = RawHidResponse.FromStatus(HostLangSyncStatus.NoAck);
                return false;
            }

            if (!readOk || bytesRead == 0)
            {
                response = RawHidResponse.FromStatus(HostLangSyncStatus.NoAck);
                return false;
            }

            return TryParseAck(input, (int)bytesRead, out response);
        }

        private static bool TryParseAck(byte[] report, int length, out RawHidResponse response)
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
                    response = new RawHidResponse();
                    response.Status = (HostLangSyncStatus)report[offset + 7];
                    response.Telemetry = ParseTelemetry(report, length, offset);
                    return true;
                }
            }

            response = RawHidResponse.FromStatus(HostLangSyncStatus.NoAck);
            return false;
        }

        private static KeyboardTelemetry ParseTelemetry(byte[] report, int length, int offset)
        {
            KeyboardTelemetry telemetry = new KeyboardTelemetry();
            if (length < offset + 32)
            {
                return telemetry;
            }

            if (report[offset + 29] == 0)
            {
                return telemetry;
            }

            telemetry.HasData = true;
            telemetry.ReceivedAt = DateTime.Now;
            telemetry.Command = report[offset + 5];
            telemetry.Layout = report[offset + 6];
            telemetry.Status = report[offset + 7];
            telemetry.LangShouldBe = report[offset + 8];
            telemetry.LangCurrent = report[offset + 9];
            telemetry.HighestLayer = report[offset + 10];
            telemetry.LayerState = ReadUInt32(report, offset + 11);
            telemetry.LightingIdleSleeping = report[offset + 15] != 0;
            telemetry.RgbEnabled = report[offset + 16] != 0;
            telemetry.MouseDirections = report[offset + 17];
            telemetry.MouseFlags = report[offset + 18];
            telemetry.MouseVelocityX = ReadInt16(report, offset + 19);
            telemetry.MouseVelocityY = ReadInt16(report, offset + 21);
            telemetry.ShiftShouldBe = report[offset + 23];
            telemetry.ShiftCurrent = report[offset + 24];
            telemetry.LangPressedCount = report[offset + 25];
            telemetry.ShiftPressedCount = report[offset + 26];
            telemetry.ComboStackSize = report[offset + 27];
            telemetry.MouseScale = report[offset + 28];
            telemetry.TelemetryVersion = report[offset + 29];
            telemetry.LangShiftPressedCount = report[offset + 30];
            telemetry.ComboActiveKeyCount = report[offset + 31];
            return telemetry;
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)data[offset] |
                ((uint)data[offset + 1] << 8) |
                ((uint)data[offset + 2] << 16) |
                ((uint)data[offset + 3] << 24);
        }

        private static int ReadInt16(byte[] data, int offset)
        {
            ushort value = (ushort)(data[offset] | (data[offset + 1] << 8));
            return (short)value;
        }

        private static byte[] CreateLayoutPacket(KeyboardLayoutKind layout)
        {
            byte[] packet = new byte[HostLangSyncPacketSize];
            packet[0] = (byte)'M';
            packet[1] = (byte)'L';
            packet[2] = (byte)'N';
            packet[3] = (byte)'G';
            packet[4] = 1;
            packet[5] = HostLangSyncSetLayout;
            packet[6] = (byte)layout;
            return packet;
        }

        private static byte[] CreateStatusPacket()
        {
            byte[] packet = new byte[HostLangSyncPacketSize];
            packet[0] = (byte)'M';
            packet[1] = (byte)'L';
            packet[2] = (byte)'N';
            packet[3] = (byte)'G';
            packet[4] = 1;
            packet[5] = HostLangSyncGetStatus;
            packet[6] = 0;
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
