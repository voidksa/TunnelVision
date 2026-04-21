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
        private NotifyIcon _trayIcon = null!;
        private IntPtr _lastForegroundWindow = IntPtr.Zero;
        private Rectangle _lastRect = Rectangle.Empty;

        private IntPtr _cachedWindow = IntPtr.Zero;
        private bool _cachedUseDwm = true;

        private AppSettings _settings;
        private SettingsForm? _settingsForm;
        private OsdForm? _osd;
        private bool _isPaused = true;

        // Latest known release info (for manual checks / tray pulse)
        private string _latestVersion = "";
        private string _latestReleaseUrl = "";
        private string _latestReleaseNotes = "";
        private bool _updateAvailable = false;

        private CancellationTokenSource? _updateCts;

        public OverlayForm()
        {
            _settings = AppSettings.Load();

            // v1.1.0: blur is disabled at the engine level. The acrylic backdrop API
            // conflicts with WS_EX_LAYERED + region cutout (the acrylic fills over
            // the focus window). Force it off regardless of saved config — the UI
            // also hides the toggle, and the setting is reserved for a future
            // release that uses a separate blur window with masking.
            _settings.BlurBackground = false;

            // Form configuration
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(_settings.TintColorArgb);
            this.Opacity = _settings.Opacity;
            this.StartPosition = FormStartPosition.Manual;
            this.Visible = false; // Start hidden

            // Cover all screens
            this.Bounds = SystemInformation.VirtualScreen;

            // Initialize Tray Icon
            InitializeTrayIcon();

            if (_settings.AutoCheckUpdates)
            {
                StartUpdateChecks();
            }

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
                    trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
                }

                _trayIcon = new NotifyIcon()
                {
                    Icon = trayIcon,
                    Visible = true,
                    Text = "Tunnel Vision"
                };

                _trayIcon.BalloonTipClicked += TrayIcon_BalloonTipClicked;
                _trayIcon.DoubleClick += (s, e) => TogglePauseInternal();

                RebuildTrayMenu();
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

        private void RebuildTrayMenu()
        {
            if (_trayIcon == null) return;

            bool isDark = Theme.IsSystemDark();
            ContextMenuStrip menu = new ContextMenuStrip
            {
                Renderer = new FluentMenuRenderer(isDark),
                BackColor = isDark ? Theme.Dark.Surface : Theme.Light.Surface,
                ForeColor = isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary,
                DropShadowEnabled = true,
                Padding = new Padding(4),
                Font = new Font("Segoe UI", 9.5f),
                ShowImageMargin = false
            };

            // Apply Win11 rounded corners once the menu has a handle
            menu.HandleCreated += (s, e) => NativeMethods.TryApplyRoundedCorners(menu.Handle, small: false);

            void AddItem(string text, EventHandler handler, bool bold = false)
            {
                var item = new ToolStripMenuItem(text, null, handler)
                {
                    Padding = new Padding(10, 6, 10, 6),
                    Margin = new Padding(0, 2, 0, 2)
                };
                if (bold) item.Font = new Font(menu.Font, FontStyle.Bold);
                menu.Items.Add(item);
            }

            AddItem(_isPaused ? "Resume" : "Pause", (s, e) => TogglePauseInternal());
            menu.Items.Add(new ToolStripSeparator());
            AddItem("Increase intensity", (s, e) => ChangeIntensity(+_settings.IntensityStep));
            AddItem("Decrease intensity", (s, e) => ChangeIntensity(-_settings.IntensityStep));
            menu.Items.Add(new ToolStripSeparator());
            AddItem("Settings…", (s, e) => OpenSettings());

            if (_updateAvailable)
            {
                menu.Items.Add(new ToolStripSeparator());
                AddItem($"Update available: v{_latestVersion}", (s, e) => OpenLatestRelease(), bold: true);
            }
            else
            {
                AddItem("Check for updates", async (s, e) => await ManualUpdateCheckAsync());
            }

            menu.Items.Add(new ToolStripSeparator());
            AddItem("GitHub", (s, e) => OpenUrl(GetRepoUrl()));
            // Defer exit so we return from the click handler before the menu is disposed,
            // otherwise WinForms raises "Collection was modified" as it iterates menu items
            // mid-click.
            AddItem("Exit", (s, e) => this.BeginInvoke(new Action(() => Application.Exit())));

            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.ContextMenuStrip = menu;
        }

        private string GetRepoUrl()
        {
            return "https://github.com/voidksa/TunnelVision";
        }

        private void OpenLatestRelease()
        {
            var url = string.IsNullOrEmpty(_latestReleaseUrl)
                ? GetRepoUrl() + "/releases/latest"
                : _latestReleaseUrl;
            OpenUrl(url);
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

        private void TrayIcon_BalloonTipClicked(object? sender, EventArgs e)
        {
            if (_updateAvailable)
            {
                ShowUpdateDialog();
            }
            else
            {
                OpenSettings();
            }
        }

        private void StartUpdateChecks()
        {
            _updateCts?.Cancel();
            _updateCts = new CancellationTokenSource();
            var token = _updateCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await CheckForUpdateAsync(manual: false);
                    }
                    catch { }

                    try
                    {
                        await Task.Delay(TimeSpan.FromHours(6), token);
                    }
                    catch (TaskCanceledException) { break; }
                }
            }, token);
        }

        private async Task ManualUpdateCheckAsync()
        {
            bool found = await CheckForUpdateAsync(manual: true);
            if (!found && this.IsHandleCreated && !this.IsDisposed && !_shuttingDown)
            {
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        if (_shuttingDown || _trayIcon == null) return;
                        _trayIcon.ShowBalloonTip(4000, "Tunnel Vision", "You're on the latest version.", ToolTipIcon.Info);
                    }));
                }
                catch (ObjectDisposedException) { }
            }
        }

        private async Task<bool> CheckForUpdateAsync(bool manual)
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TunnelVisionUpdateChecker/1.1");
            var url = "https://api.github.com/repos/voidksa/TunnelVision/releases/latest";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return false;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("tag_name", out var tagEl)) return false;
            var latestTag = tagEl.GetString() ?? "";
            string body = doc.RootElement.TryGetProperty("body", out var bodyEl) ? (bodyEl.GetString() ?? "") : "";
            string htmlUrl = doc.RootElement.TryGetProperty("html_url", out var urlEl) ? (urlEl.GetString() ?? "") : "";

            var currentVersion = GetCurrentVersionString();
            var normalizedCurrent = NormalizeTag(currentVersion);
            var normalizedLatest = NormalizeTag(latestTag);

            if (!IsNewer(normalizedLatest, normalizedCurrent))
            {
                _updateAvailable = false;
                return false;
            }

            // Respect skipped version unless this is a manual check
            if (!manual && string.Equals(_settings.SkippedVersion, normalizedLatest, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _latestVersion = normalizedLatest;
            _latestReleaseUrl = htmlUrl;
            _latestReleaseNotes = body;
            _updateAvailable = true;

            if (this.IsHandleCreated && !this.IsDisposed && !_shuttingDown)
            {
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        if (_shuttingDown) return;
                        RebuildTrayMenu();
                        if (manual)
                        {
                            ShowUpdateDialog();
                        }
                        else if (_trayIcon != null)
                        {
                            _trayIcon.ShowBalloonTip(8000,
                                "Tunnel Vision — Update Available",
                                $"Version {normalizedLatest} is available. Click here to see what's new.",
                                ToolTipIcon.Info);
                        }
                    }));
                }
                catch (ObjectDisposedException) { }
            }

            return true;
        }

        private void ShowUpdateDialog()
        {
            try
            {
                var form = new UpdateForm(
                    _latestVersion,
                    string.IsNullOrEmpty(_latestReleaseUrl) ? GetRepoUrl() + "/releases/latest" : _latestReleaseUrl,
                    _latestReleaseNotes,
                    GetCurrentVersionString(),
                    onSkip: () =>
                    {
                        _settings.SkippedVersion = _latestVersion;
                        _settings.Save();
                        _updateAvailable = false;
                        RebuildTrayMenu();
                    });
                form.Show();
            }
            catch
            {
                MessageBox.Show($"A new version is available: {_latestVersion}", "Tunnel Vision", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string GetCurrentVersionString()
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v == null) return "1.0.0";
            return $"{v.Major}.{v.Minor}.{v.Build}";
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

        private void OpenSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_settings, ApplySettings, () => _ = ManualUpdateCheckAsync());
            }
            _settingsForm.Show();
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }

        private void ApplySettings()
        {
            this.Opacity = _settings.Opacity;
            this.BackColor = Color.FromArgb(_settings.TintColorArgb);
            UpdateTimerInterval();
            UpdateAllHotkeys();
            ApplyBackdropEffect();

            if (_settings.AutoCheckUpdates)
            {
                if (_updateCts == null || _updateCts.IsCancellationRequested)
                {
                    StartUpdateChecks();
                }
            }
            else
            {
                _updateCts?.Cancel();
            }

            RebuildTrayMenu();
        }

        private void ApplyBackdropEffect()
        {
            if (!this.IsHandleCreated) return;
            if (_settings.BlurBackground)
            {
                var c = Color.FromArgb(_settings.TintColorArgb);
                // Tint opacity controlled by Form.Opacity slider (in layered alpha); here we
                // use a mid-strength tint so the blur shows through comfortably.
                NativeMethods.ApplyAcrylicBlur(this.Handle, c, 120);
            }
            else
            {
                NativeMethods.DisableBlur(this.Handle);
            }
        }

        private const int HOTKEY_TOGGLE = 1;
        private const int HOTKEY_INCREASE = 2;
        private const int HOTKEY_DECREASE = 3;
        private const int HOTKEY_SETTINGS = 4;

        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT;
                cp.ExStyle |= WS_EX_LAYERED;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
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
            RegisterAllHotkeys();
            ApplyBackdropEffect();
            this.BeginInvoke(new Action(CheckFirstRun));
        }

        private void CheckFirstRun()
        {
            string currentVersion = GetCurrentVersionString();
            bool isFreshInstall = _settings.IsFirstRun;
            bool isUpgrade = !isFreshInstall && _settings.LastRunVersion != currentVersion;

            if (isFreshInstall)
            {
                _trayIcon.ShowBalloonTip(10000,
                    "Tunnel Vision Ready",
                    $"Press {FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey)} to toggle focus.\n" +
                    $"Use {FormatHotkey(_settings.IncreaseHotkeyModifiers, _settings.IncreaseHotkeyKey)} / " +
                    $"{FormatHotkey(_settings.DecreaseHotkeyModifiers, _settings.DecreaseHotkeyKey)} to adjust intensity.",
                    ToolTipIcon.Info);
                _settings.IsFirstRun = false;
            }
            else if (isUpgrade)
            {
                _trayIcon.ShowBalloonTip(8000,
                    $"Tunnel Vision v{currentVersion} — Updated",
                    $"New: intensity hotkeys ({FormatHotkey(_settings.IncreaseHotkeyModifiers, _settings.IncreaseHotkeyKey)} / " +
                    $"{FormatHotkey(_settings.DecreaseHotkeyModifiers, _settings.DecreaseHotkeyKey)}), " +
                    "on-screen indicator, redesigned Settings.",
                    ToolTipIcon.Info);
            }
            else
            {
                // Silent startup: show a quick ready toast so the user knows we're alive.
                _trayIcon.ShowBalloonTip(3000,
                    "Tunnel Vision",
                    $"Running. Press {FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey)} to toggle focus.",
                    ToolTipIcon.Info);
            }

            _settings.LastRunVersion = currentVersion;
            _settings.Save();
        }

        private static string FormatHotkey(int modifiers, int key)
        {
            var parts = new List<string>();
            if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
            parts.Add(((Keys)key).ToString());
            return string.Join("+", parts);
        }

        private void RegisterAllHotkeys()
        {
            RegisterOneHotkey(HOTKEY_TOGGLE, _settings.HotkeyModifiers, _settings.HotkeyKey, "toggle");
            RegisterOneHotkey(HOTKEY_INCREASE, _settings.IncreaseHotkeyModifiers, _settings.IncreaseHotkeyKey, "increase intensity");
            RegisterOneHotkey(HOTKEY_DECREASE, _settings.DecreaseHotkeyModifiers, _settings.DecreaseHotkeyKey, "decrease intensity");
            RegisterOneHotkey(HOTKEY_SETTINGS, _settings.SettingsHotkeyModifiers, _settings.SettingsHotkeyKey, "open settings");
        }

        private void RegisterOneHotkey(int id, int modifiers, int key, string label)
        {
            try
            {
                bool ok = NativeMethods.RegisterHotKey(this.Handle, id, modifiers, key);
                if (!ok)
                {
                    _trayIcon?.ShowBalloonTip(5000,
                        "Tunnel Vision",
                        $"Could not register the {label} hotkey. It may be in use by another app.",
                        ToolTipIcon.Warning);
                }
            }
            catch { }
        }

        private void UnregisterAllHotkeys()
        {
            try { NativeMethods.UnregisterHotKey(this.Handle, HOTKEY_TOGGLE); } catch { }
            try { NativeMethods.UnregisterHotKey(this.Handle, HOTKEY_INCREASE); } catch { }
            try { NativeMethods.UnregisterHotKey(this.Handle, HOTKEY_DECREASE); } catch { }
            try { NativeMethods.UnregisterHotKey(this.Handle, HOTKEY_SETTINGS); } catch { }
        }

        private void UpdateAllHotkeys()
        {
            UnregisterAllHotkeys();
            RegisterAllHotkeys();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_TOGGLE)
                {
                    TogglePauseInternal();
                }
                else if (id == HOTKEY_INCREASE)
                {
                    ChangeIntensity(+_settings.IntensityStep);
                }
                else if (id == HOTKEY_DECREASE)
                {
                    ChangeIntensity(-_settings.IntensityStep);
                }
                else if (id == HOTKEY_SETTINGS)
                {
                    ToggleSettings();
                }
            }
            base.WndProc(ref m);
        }

        private void ToggleSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed && _settingsForm.Visible)
            {
                _settingsForm.Hide();
            }
            else
            {
                OpenSettings();
            }
        }

        private void ChangeIntensity(int deltaPercent)
        {
            // Clamp between 10% and 95% (same as slider bounds)
            int current = (int)Math.Round(_settings.Opacity * 100.0);
            int next = Math.Max(10, Math.Min(95, current + deltaPercent));
            if (next == current && deltaPercent != 0)
            {
                // Already at boundary — still show OSD for feedback
            }

            _settings.Opacity = next / 100.0;
            this.Opacity = _settings.Opacity;
            _settings.Save();

            // Reflect change in open settings window
            _settingsForm?.SyncOpacityFromExternal();

            if (_settings.ShowOsdOnChange)
            {
                ShowOsd(next);
            }
        }

        private void ShowOsd(int percent)
        {
            try
            {
                if (_osd == null || _osd.IsDisposed)
                {
                    _osd = new OsdForm();
                }
                _osd.ShowIntensity(percent, "Darkness");
            }
            catch { }
        }

        private void TogglePauseInternal()
        {
            _isPaused = !_isPaused;
            RebuildTrayMenu();

            if (_isPaused)
            {
                this.Visible = false;
            }
            else
            {
                this.Visible = true;
                _lastForegroundWindow = IntPtr.Zero;
                _cachedWindow = IntPtr.Zero;
            }
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPaused) return;

            try
            {
                IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();

                if (foregroundWindow == this.Handle || (_osd != null && foregroundWindow == _osd.Handle))
                {
                    return;
                }

                Rectangle currentRect = Rectangle.Empty;
                bool isValidWindow = false;

                if (foregroundWindow != IntPtr.Zero)
                {
                    if (foregroundWindow != _cachedWindow)
                    {
                        _cachedWindow = foregroundWindow;

                        StringBuilder classNameSb = new StringBuilder(256);
                        NativeMethods.GetClassName(foregroundWindow, classNameSb, classNameSb.Capacity);
                        string className = classNameSb.ToString();

                        _cachedUseDwm = true;

                        if (className == "Shell_TrayWnd" ||
                            className == "Shell_SecondaryTrayWnd" ||
                            className == "#32768" ||
                            className == "NotifyIconOverflowWindow")
                        {
                            _cachedUseDwm = false;
                        }
                        else if (className == "Windows.UI.Core.CoreWindow")
                        {
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
                        int result = NativeMethods.DwmGetWindowAttribute(foregroundWindow, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT rect, Marshal.SizeOf(typeof(NativeMethods.RECT)));

                        if (result == 0)
                        {
                            currentRect = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
                            isValidWindow = true;
                        }
                    }

                    if (!isValidWindow)
                    {
                        if (NativeMethods.GetWindowRect(foregroundWindow, out NativeMethods.RECT rect))
                        {
                            currentRect = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
                            isValidWindow = true;
                        }
                    }
                }

                if (!isValidWindow || currentRect.Width <= 0 || currentRect.Height <= 0)
                {
                    currentRect = Rectangle.Empty;
                    foregroundWindow = IntPtr.Zero;
                }

                // Auto-pause in fullscreen: if the foreground window fully covers a screen,
                // hide the overlay completely so games/videos are unaffected.
                if (_settings.PauseInFullscreen && isValidWindow && IsFullscreenOnAnyScreen(currentRect))
                {
                    if (this.Visible) this.Visible = false;
                    _lastForegroundWindow = foregroundWindow;
                    _lastRect = currentRect;
                    return;
                }
                else if (!this.Visible && !_isPaused)
                {
                    this.Visible = true;
                }

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
            if (targetRect.IsEmpty || hWnd == IntPtr.Zero)
            {
                this.Region = new Region(new Rectangle(0, 0, this.Width, this.Height));
                return;
            }

            int x = targetRect.X - this.Left;
            int y = targetRect.Y - this.Top;

            Rectangle holeRect = new Rectangle(x, y, targetRect.Width, targetRect.Height);

            Region region = new Region(new Rectangle(0, 0, this.Width, this.Height));

            int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
            bool isMaximized = (style & NativeMethods.WS_MAXIMIZE) == NativeMethods.WS_MAXIMIZE;

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

            this.Region = region;
        }

        private bool IsWindows11OrNewer()
        {
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
        }

        private static bool IsFullscreenOnAnyScreen(Rectangle rect)
        {
            foreach (var screen in Screen.AllScreens)
            {
                // Allow small tolerance (titlebar-less fullscreen apps sometimes differ by a pixel)
                var b = screen.Bounds;
                if (Math.Abs(rect.Left - b.Left) <= 2 &&
                    Math.Abs(rect.Top - b.Top) <= 2 &&
                    Math.Abs(rect.Width - b.Width) <= 4 &&
                    Math.Abs(rect.Height - b.Height) <= 4)
                {
                    return true;
                }
            }
            return false;
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

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        private bool _shuttingDown;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _shuttingDown = true;
            try { _updateCts?.Cancel(); } catch { }
            try { UnregisterAllHotkeys(); } catch { }

            // Hide tray icon immediately so Windows doesn't keep rendering it while
            // we tear things down.
            try { if (_trayIcon != null) _trayIcon.Visible = false; } catch { }

            try
            {
                if (_trayIcon?.ContextMenuStrip != null)
                {
                    _trayIcon.ContextMenuStrip.Dispose();
                    _trayIcon.ContextMenuStrip = null;
                }
            }
            catch { }

            try { _osd?.Dispose(); } catch { }
            try { _trayIcon?.Dispose(); } catch { }
            try { _refreshTimer?.Stop(); _refreshTimer?.Dispose(); } catch { }

            base.OnFormClosing(e);
        }
    }
}
