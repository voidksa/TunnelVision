using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using Timer = System.Windows.Forms.Timer;
using Microsoft.Win32;

namespace TunnelVision
{
    public class OverlayForm : Form
    {
        private Timer _refreshTimer;
        private NotifyIcon _trayIcon;
        private IntPtr _lastForegroundWindow = IntPtr.Zero;
        private Rectangle _lastRect = Rectangle.Empty;

        private IntPtr _cachedWindow = IntPtr.Zero;
        private bool _cachedUseDwm = true;

        private AppSettings _settings;
        private SettingsForm? _settingsForm;
        private bool _isPaused = true;

        public OverlayForm()
        {
            _settings = AppSettings.Load();

            // Form configuration
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = Color.Black;
            this.Opacity = _settings.Opacity;
            this.StartPosition = FormStartPosition.Manual;
            this.Visible = false; // Start hidden

            // Cover all screens
            this.Bounds = SystemInformation.VirtualScreen;

            // Initialize Tray Icon
            InitializeTrayIcon();

            StartUpdateChecks();

            // Initialize Timer
            _refreshTimer = new Timer();
            UpdateTimerInterval();
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            // Force handle creation to ensure hotkeys are registered and FirstRun check works
            var h = this.Handle;

            // Boost process priority to ensure hotkeys work even when system is busy
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch { }
        }

        private void UpdateTimerInterval()
        {
            // 15ms is roughly 60fps
            _refreshTimer.Interval = _settings.SmoothMovement ? 15 : 50;
        }

        private void InitializeTrayIcon()
        {
            try
            {
                // Try to load icon from file, fallback to EXE resource
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                Icon trayIcon;

                if (File.Exists(iconPath))
                {
                    trayIcon = new Icon(iconPath);
                    this.Icon = trayIcon;
                }
                else
                {
                    // Fallback to embedded icon
                    trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }

                _trayIcon = new NotifyIcon()
                {
                    Icon = trayIcon,
                    Visible = true,
                    Text = "Tunnel Vision"
                };

                // Context Menu
                ContextMenuStrip menu = new ContextMenuStrip();

                // Apply Dark Mode if needed
                if (IsDarkMode())
                {
                    menu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
                    menu.ForeColor = Color.White;
                    menu.BackColor = Color.FromArgb(32, 32, 32);
                }

                menu.Items.Add("Settings", null, (s, e) => OpenSettings());
                menu.Items.Add("-");
                menu.Items.Add("Pause/Resume", null, (s, e) => TogglePauseInternal());
                menu.Items.Add("-");
                menu.Items.Add("GitHub", null, (s, e) => OpenUrl(GetRepoUrl()));
                menu.Items.Add("-");
                menu.Items.Add("Exit", null, (s, e) => Application.Exit());

                _trayIcon.ContextMenuStrip = menu;
                _trayIcon.DoubleClick += (s, e) => OpenSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tray Icon Error: {ex.Message}");
                _trayIcon = new NotifyIcon()
                {
                    Icon = SystemIcons.Application,
                    Visible = true,
                    Text = "Tunnel Vision"
                };
            }
        }

        private string GetRepoUrl()
        {
            return "https://github.com/voidksa/TunnelVision";
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void StartUpdateChecks()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await CheckForUpdateAsync();
                    }
                    catch { }
                    await Task.Delay(TimeSpan.FromHours(6));
                }
            });
        }

        private async Task CheckForUpdateAsync()
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TunnelVisionUpdateChecker/1.0");
            var url = "https://api.github.com/repos/voidksa/TunnelVision/releases/latest";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tag_name", out var tagEl)) return;
            var latestTag = tagEl.GetString() ?? "";

            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            var normalizedCurrent = NormalizeTag(currentVersion);
            var normalizedLatest = NormalizeTag(latestTag);

            if (IsNewer(normalizedLatest, normalizedCurrent))
            {
                this.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var form = new UpdateForm(normalizedLatest, GetRepoUrl() + "/releases/latest");
                        form.Show();
                    }
                    catch
                    {
                        MessageBox.Show($"A new version is available: {normalizedLatest}", "Tunnel Vision", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }));
            }
        }

        private string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return "0.0.0";
            return tag.StartsWith("v") ? tag.Substring(1) : tag;
        }

        private bool IsNewer(string a, string b)
        {
            try
            {
                var av = a.Split('.');
                var bv = b.Split('.');
                int amaj = int.Parse(av[0]);
                int amin = int.Parse(av.Length > 1 ? av[1] : "0");
                int apat = int.Parse(av.Length > 2 ? av[2] : "0");
                int bmaj = int.Parse(bv[0]);
                int bmin = int.Parse(bv.Length > 1 ? bv[1] : "0");
                int bpat = int.Parse(bv.Length > 2 ? bv[2] : "0");
                if (amaj != bmaj) return amaj > bmaj;
                if (amin != bmin) return amin > bmin;
                return apat > bpat;
            }
            catch { }
            return false;
        }

        private bool IsDarkMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val != null)
                        {
                            return (int)val == 0;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Color.FromArgb(64, 64, 64);
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuBorder => Color.FromArgb(45, 45, 48);
            public override Color MenuItemPressedGradientBegin => Color.FromArgb(64, 64, 64);
            public override Color MenuItemPressedGradientEnd => Color.FromArgb(64, 64, 64);
            public override Color ToolStripDropDownBackground => Color.FromArgb(32, 32, 32);
            public override Color ImageMarginGradientBegin => Color.FromArgb(32, 32, 32);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(32, 32, 32);
            public override Color ImageMarginGradientEnd => Color.FromArgb(32, 32, 32);
        }

        private void OpenSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_settings, ApplySettings);
            }
            _settingsForm.Show();
            _settingsForm.BringToFront();
        }

        private void ApplySettings()
        {
            this.Opacity = _settings.Opacity;
            UpdateTimerInterval();
            UpdateHotkey();
        }

        private const int HOTKEY_ID = 1;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT; // Click-through
                cp.ExStyle |= WS_EX_LAYERED;     // Layered
                cp.ExStyle |= WS_EX_TOOLWINDOW;  // No taskbar
                cp.ExStyle |= WS_EX_NOACTIVATE;  // No focus
                return cp;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            // Prevent the form from being shown if it is paused (startup)
            if (_isPaused && value)
            {
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkey();

            // Move CheckFirstRun here and invoke it to ensure message loop is ready
            this.BeginInvoke(new Action(CheckFirstRun));
        }

        private void CheckFirstRun()
        {
            if (_settings.IsFirstRun)
            {
                _trayIcon.ShowBalloonTip(10000, "Tunnel Vision Ready", "Press Ctrl+Alt+T to toggle.\nClick here to change settings.", ToolTipIcon.Info);
                _settings.IsFirstRun = false;
                _settings.Save();
            }
        }

        private void RegisterHotkey()
        {
            try
            {
                bool success = NativeMethods.RegisterHotKey(this.Handle, HOTKEY_ID, _settings.HotkeyModifiers, _settings.HotkeyKey);
                if (!success)
                {
                    MessageBox.Show("Could not register Global Hotkey (Ctrl+Alt+T).\nIt might be in use by another application.", "Tunnel Vision Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error registering hotkey: {ex.Message}", "Tunnel Vision Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UnregisterHotkey()
        {
            try
            {
                NativeMethods.UnregisterHotKey(this.Handle, HOTKEY_ID);
            }
            catch { }
        }

        private void UpdateHotkey()
        {
            UnregisterHotkey();
            RegisterHotkey();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                // Toggle pause via hotkey
                TogglePauseInternal();
            }
            base.WndProc(ref m);
        }

        private void TogglePauseInternal()
        {
            _isPaused = !_isPaused;

            // Update Menu Text if possible
            if (_trayIcon.ContextMenuStrip != null && _trayIcon.ContextMenuStrip.Items.Count > 0)
            {
                var item = _trayIcon.ContextMenuStrip.Items[0]; // Assuming Pause is first
                if (item != null) item.Text = _isPaused ? "Resume" : "Pause";
            }

            if (_isPaused)
            {
                this.Visible = false;
            }
            else
            {
                this.Visible = true;
                _lastForegroundWindow = IntPtr.Zero;
                _cachedWindow = IntPtr.Zero; // Force re-check of window class
            }
        }

        private void TogglePause(ToolStripMenuItem item)
        {
            TogglePauseInternal();
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPaused) return;

            try
            {
                IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();

                // If foreground window is this overlay, ignore
                if (foregroundWindow == this.Handle)
                {
                    return;
                }

                Rectangle currentRect = Rectangle.Empty;
                bool isValidWindow = false;

                if (foregroundWindow != IntPtr.Zero)
                {
                    // Check logic only if window changed
                    if (foregroundWindow != _cachedWindow)
                    {
                        _cachedWindow = foregroundWindow;

                        StringBuilder classNameSb = new StringBuilder(256);
                        NativeMethods.GetClassName(foregroundWindow, classNameSb, classNameSb.Capacity);
                        string className = classNameSb.ToString();

                        _cachedUseDwm = true;

                        // Taskbar and Context Menus often behave better with GetWindowRect
                        if (className == "Shell_TrayWnd" ||
                            className == "Shell_SecondaryTrayWnd" ||
                            className == "#32768" || // Context Menus
                            className == "NotifyIconOverflowWindow") // System Tray Overflow
                        {
                            _cachedUseDwm = false;
                        }
                        else if (className == "Windows.UI.Core.CoreWindow")
                        {
                            // Check for Start Menu or Search to use GetWindowRect instead of DWM
                            // DWM bounds often fail or return incorrect sizes for these UWP system windows
                            try
                            {
                                NativeMethods.GetWindowThreadProcessId(foregroundWindow, out uint pid);
                                using (var p = Process.GetProcessById((int)pid))
                                {
                                    string processName = p.ProcessName.ToLower();
                                    if (processName == "startmenuexperiencehost" ||
                                        processName == "searchhost" ||
                                        processName == "searchapp")
                                    {
                                        _cachedUseDwm = false;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (_cachedUseDwm)
                    {
                        // Try to get the actual visible frame bounds
                        int result = NativeMethods.DwmGetWindowAttribute(foregroundWindow, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT rect, Marshal.SizeOf(typeof(NativeMethods.RECT)));

                        if (result == 0) // S_OK
                        {
                            currentRect = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
                            isValidWindow = true;
                        }
                    }

                    if (!isValidWindow)
                    {
                        // Fallback or forced GetWindowRect
                        if (NativeMethods.GetWindowRect(foregroundWindow, out NativeMethods.RECT rect))
                        {
                            currentRect = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
                            isValidWindow = true;
                        }
                    }
                }

                // If invalid window or minimized/zero size, treat as empty (full dim)
                if (!isValidWindow || currentRect.Width <= 0 || currentRect.Height <= 0)
                {
                    currentRect = Rectangle.Empty;
                    foregroundWindow = IntPtr.Zero; // Treat as no window
                }

                // Optimization: Only update Region if something changed
                if (foregroundWindow == _lastForegroundWindow && currentRect == _lastRect)
                {
                    return;
                }

                _lastForegroundWindow = foregroundWindow;
                _lastRect = currentRect;

                UpdateHole(currentRect, foregroundWindow);
            }
            catch
            {
                // Ignore errors
            }
        }

        private void UpdateHole(Rectangle targetRect, IntPtr hWnd)
        {
            // If no target, fill everything (no hole)
            if (targetRect.IsEmpty || hWnd == IntPtr.Zero)
            {
                this.Region = new Region(new Rectangle(0, 0, this.Width, this.Height));
                return;
            }

            // Convert screen coordinates to client coordinates (essential if form is not at 0,0)
            int x = targetRect.X - this.Left;
            int y = targetRect.Y - this.Top;

            Rectangle holeRect = new Rectangle(x, y, targetRect.Width, targetRect.Height);

            // Create a region that covers the whole form
            Region region = new Region(new Rectangle(0, 0, this.Width, this.Height));

            // Check if window is maximized
            int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
            bool isMaximized = (style & NativeMethods.WS_MAXIMIZE) == NativeMethods.WS_MAXIMIZE;

            // If not maximized, assume rounded corners (Windows 11 style)
            // Adjust radius as needed (9 seems to be standard for Win11)
            if (!isMaximized && IsWindows11OrNewer())
            {
                using (GraphicsPath path = GetRoundedRect(holeRect, 9))
                {
                    region.Exclude(path);
                }
            }
            else
            {
                region.Exclude(holeRect);
            }

            // Apply the region
            this.Region = region;
        }

        private bool IsWindows11OrNewer()
        {
            // Simple check: Windows 10 build 22000+ is Windows 11
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
        }

        private GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(bounds.Location, size);
            GraphicsPath path = new GraphicsPath();

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            // Top left arc  
            path.AddArc(arc, 180, 90);

            // Top right arc  
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom right arc  
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom left arc 
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            NativeMethods.UnregisterHotKey(this.Handle, HOTKEY_ID);
            _trayIcon.Dispose();
            base.OnFormClosing(e);
        }
    }
}
