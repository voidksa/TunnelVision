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
        // Whether the current foreground window is a transient popup (context
        // menu, tooltip, flyout). Cached so we can act on it every tick, not
        // only when the foreground changes.
        private bool _cachedIsTransientPopup = false;

        private AppSettings _settings;
        private SettingsForm? _settingsForm;
        private OsdForm? _osd;
        private bool _isPaused = true;

        // 4 blur strips positioned around the focus window when blur is on.
        // Each panel renders a captured+blurred slice of the desktop underneath
        // itself, plus a tint overlay. The capture-and-blur pipeline is driven
        // from this form (see _blurCaptureTimer and RefreshBlurCapture).
        private BlurPanelForm? _blurTop, _blurBottom, _blurLeft, _blurRight;

        // Drives periodic desktop capture + blur for the 4 panels.
        private Timer? _blurCaptureTimer;
        private const int BlurCaptureIntervalMs = 500;

        // Cached most-recent blurred snapshot of the virtual desktop. The blur
        // pass runs on a background thread and writes the result here; the UI
        // thread re-slices it whenever the panels move so the visual stays in
        // lock-step with window dragging, not with the 500 ms capture cadence.
        private Bitmap? _cachedBlurredFull;
        private readonly object _cachedBlurLock = new();
        private volatile bool _blurCaptureInFlight;

        // Latest known release info (for manual checks / tray pulse)
        private string _latestVersion = "";
        private string _latestReleaseUrl = "";
        private string _latestReleaseNotes = "";
        private bool _updateAvailable = false;

        private CancellationTokenSource? _updateCts;

        public OverlayForm()
        {
            _settings = AppSettings.Load();

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
            // Defer + close the main form (instead of Application.Exit()).
            // Application.Exit iterates Application.OpenForms and calls Close on
            // each — but with our four blur panels + OSD + Settings all registered,
            // that iteration can race with form disposal and throw
            // "Collection was modified". Closing the root form lets Application.Run
            // unwind naturally and the runtime cleans up the rest.
            AddItem("Exit", (s, e) => this.BeginInvoke(new Action(() => this.Close())));

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
                // Switch to the 4-panel manual-blur model.
                // Hide the single-window dim (it would double up with the panels).
                this.Opacity = 0;
                EnsureBlurPanels();
                UpdateBlurPanelTints();
                UpdateBlurPanels(_lastRect);
                StartBlurCaptureLoop();
            }
            else
            {
                // Tear down panels if they were up, and restore classic dim.
                StopBlurCaptureLoop();
                HideBlurPanels();
                this.Opacity = _settings.Opacity;
            }
        }

        // Periodic capture + Gaussian-ish blur of the virtual desktop. Runs at
        // ~2 fps by default which is enough for an "ambient" aesthetic without
        // burning CPU. We capture the whole virtual screen once per tick, blur
        // it, then slice the result into each visible panel's rect.
        private void StartBlurCaptureLoop()
        {
            if (_blurCaptureTimer != null) return;
            _blurCaptureTimer = new Timer { Interval = BlurCaptureIntervalMs };
            _blurCaptureTimer.Tick += (s, e) => RefreshBlurCapture();
            _blurCaptureTimer.Start();
            // Prime the first frame immediately so the user doesn't see solid
            // tint for half a second when they toggle blur on.
            RefreshBlurCapture();
        }

        private void StopBlurCaptureLoop()
        {
            _blurCaptureTimer?.Stop();
            _blurCaptureTimer?.Dispose();
            _blurCaptureTimer = null;
        }

        private void RefreshBlurCapture()
        {
            if (!_settings.BlurBackground || _isPaused || _shuttingDown) return;
            if (_blurCaptureInFlight) return;

            Rectangle vs = SystemInformation.VirtualScreen;

            // Capture on UI thread (CopyFromScreen requires it for DPI handling),
            // blur on a background Task.
            Bitmap full;
            try
            {
                full = new Bitmap(vs.Width, vs.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(full);
                g.CopyFromScreen(vs.Location, Point.Empty, vs.Size);
            }
            catch
            {
                return;
            }

            _blurCaptureInFlight = true;
            Task.Run(() =>
            {
                Bitmap? blurred = null;
                try
                {
                    blurred = ImageBlur.FastBlur(full);
                }
                catch { }
                finally
                {
                    full.Dispose();
                }

                if (blurred == null) { _blurCaptureInFlight = false; return; }

                if (this.IsHandleCreated && !this.IsDisposed && !_shuttingDown)
                {
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            // Swap the shared reference under UI-thread serialization.
                            Bitmap? old;
                            lock (_cachedBlurLock)
                            {
                                old = _cachedBlurredFull;
                                _cachedBlurredFull = blurred;
                            }
                            // All four panels now point to the new bitmap, then we
                            // dispose the old one. Because panels never touch the
                            // bitmap off the UI thread, this swap-then-dispose is safe.
                            _blurTop?.SetSharedBitmap(blurred);
                            _blurBottom?.SetSharedBitmap(blurred);
                            _blurLeft?.SetSharedBitmap(blurred);
                            _blurRight?.SetSharedBitmap(blurred);
                            old?.Dispose();
                            _blurCaptureInFlight = false;
                        }));
                    }
                    catch
                    {
                        blurred.Dispose();
                        _blurCaptureInFlight = false;
                    }
                }
                else
                {
                    blurred.Dispose();
                    _blurCaptureInFlight = false;
                }
            });
        }

        // Updates just the source-rect for each panel (cheap: no bitmap copy).
        // Called every time panels are repositioned so the content they show
        // matches the new position at 60 fps, without waiting for the next
        // capture cycle.
        private void ReslicePanelBitmapsFromCache()
        {
            var vs = SystemInformation.VirtualScreen;
            SetPanelSourceRect(_blurTop, vs);
            SetPanelSourceRect(_blurBottom, vs);
            SetPanelSourceRect(_blurLeft, vs);
            SetPanelSourceRect(_blurRight, vs);
        }

        private static void SetPanelSourceRect(BlurPanelForm? panel, Rectangle vs)
        {
            if (panel == null || !panel.Visible || panel.IsDisposed) return;
            if (panel.Width <= 0 || panel.Height <= 0) return;

            int x = panel.Left - vs.Left;
            int y = panel.Top - vs.Top;
            panel.SetSourceRect(new Rectangle(x, y, panel.Width, panel.Height));
        }

        private void EnsureBlurPanels()
        {
            _blurTop    ??= new BlurPanelForm();
            _blurBottom ??= new BlurPanelForm();
            _blurLeft   ??= new BlurPanelForm();
            _blurRight  ??= new BlurPanelForm();
        }

        private void UpdateBlurPanelTints()
        {
            var tint = Color.FromArgb(_settings.TintColorArgb);
            double pct = _settings.Opacity;
            _blurTop?.UpdateTint(tint, pct);
            _blurBottom?.UpdateTint(tint, pct);
            _blurLeft?.UpdateTint(tint, pct);
            _blurRight?.UpdateTint(tint, pct);
        }

        private void HideBlurPanels()
        {
            if (_blurTop != null) _blurTop.Visible = false;
            if (_blurBottom != null) _blurBottom.Visible = false;
            if (_blurLeft != null) _blurLeft.Visible = false;
            if (_blurRight != null) _blurRight.Visible = false;
        }

        // Position the 4 acrylic strips around the focus rect. Called every
        // frame that the focus window moves. Skipped entirely when blur is off.
        private void UpdateBlurPanels(Rectangle focus)
        {
            if (!_settings.BlurBackground || _isPaused) return;
            EnsureBlurPanels();

            var vs = SystemInformation.VirtualScreen;

            // If no focus rect, fill everything with one blur panel (top strip
            // takes the whole screen, others hidden).
            if (focus.IsEmpty || focus.Width <= 0 || focus.Height <= 0)
            {
                _blurTop!.SetRect(vs);
                _blurBottom!.SetRect(Rectangle.Empty);
                _blurLeft!.SetRect(Rectangle.Empty);
                _blurRight!.SetRect(Rectangle.Empty);
                return;
            }

            // Clamp focus to virtual screen
            int fx = Math.Max(vs.Left, focus.Left);
            int fy = Math.Max(vs.Top, focus.Top);
            int fr = Math.Min(vs.Right, focus.Right);
            int fb = Math.Min(vs.Bottom, focus.Bottom);

            var topStrip    = Rectangle.FromLTRB(vs.Left, vs.Top, vs.Right, fy);
            var bottomStrip = Rectangle.FromLTRB(vs.Left, fb,      vs.Right, vs.Bottom);
            var leftStrip   = Rectangle.FromLTRB(vs.Left, fy,      fx,        fb);
            var rightStrip  = Rectangle.FromLTRB(fr,      fy,      vs.Right,  fb);

            _blurTop!.SetRect(topStrip);
            _blurBottom!.SetRect(bottomStrip);
            _blurLeft!.SetRect(leftStrip);
            _blurRight!.SetRect(rightStrip);

            // Panels just moved — re-slice from the cached blurred bitmap so
            // each panel shows the content that's CURRENTLY behind it, not
            // what was behind it at the last capture (500 ms ago). Without
            // this, dragging a window drags ghost content with it.
            ReslicePanelBitmapsFromCache();
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

            _settings.Opacity = next / 100.0;
            if (_settings.BlurBackground)
            {
                UpdateBlurPanelTints();
            }
            else
            {
                this.Opacity = _settings.Opacity;
            }
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
                HideBlurPanels();
            }
            else
            {
                _lastForegroundWindow = IntPtr.Zero;
                _cachedWindow = IntPtr.Zero;

                if (_settings.BlurBackground)
                {
                    // Keep main overlay hidden in blur mode; panels are the visual.
                    this.Visible = false;
                    ApplyBackdropEffect();
                }
                else
                {
                    this.Visible = true;
                }
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

                        // Transient popups (right-click menus, tooltips, flyouts...)
                        // must not become the tracked focus — otherwise the cutout
                        // retargets them and the user can't see the underlying
                        // context they clicked on. We also stop covering them.
                        _cachedIsTransientPopup = false;
                        if (className == "#32768" ||                // classic context menu
                            className == "tooltips_class32" ||       // classic tooltip
                            className == "DropDown" ||               // WPF
                            className == "Xaml_WindowedPopupClass" ||// WinUI 3
                            className == "Popup" ||
                            className.StartsWith("Popup") ||
                            className.StartsWith("Windows.UI.Popups"))
                        {
                            _cachedIsTransientPopup = true;
                            _cachedUseDwm = false;
                        }
                        else if (className == "Shell_TrayWnd" ||
                            className == "Shell_SecondaryTrayWnd" ||
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
                    HideBlurPanels();
                    _lastForegroundWindow = foregroundWindow;
                    _lastRect = currentRect;
                    return;
                }

                // Transient popup (context menu, tooltip, flyout, combobox dropdown):
                // get out of the way entirely so the popup renders unobstructed.
                // We're TopMost, and popups aren't always TopMost themselves, so
                // covering them with any paint makes them invisible. Hide now; we'll
                // come back when the popup closes and the foreground reverts to a
                // normal window.
                if (_cachedIsTransientPopup)
                {
                    if (this.Visible) this.Visible = false;
                    HideBlurPanels();
                    return;
                }

                // Normal foreground — restore whichever visual mode is active.
                if (!_isPaused)
                {
                    if (_settings.BlurBackground)
                    {
                        // Blur mode: main form stays hidden, panels come back.
                        if (this.Visible) this.Visible = false;
                    }
                    else if (!this.Visible)
                    {
                        this.Visible = true;
                    }
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
            // Blur mode uses 4 separate acrylic panels around the focus rect —
            // no Region on the main form is needed (we hid it). Keep the blur
            // panels in sync with the focus rect here.
            if (_settings.BlurBackground)
            {
                UpdateBlurPanels(targetRect);
                return;
            }

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

            try { _blurCaptureTimer?.Stop(); _blurCaptureTimer?.Dispose(); } catch { }
            try { _osd?.Dispose(); } catch { }
            try { _blurTop?.Dispose(); } catch { }
            try { _blurBottom?.Dispose(); } catch { }
            try { _blurLeft?.Dispose(); } catch { }
            try { _blurRight?.Dispose(); } catch { }
            try { _trayIcon?.Dispose(); } catch { }
            try { _refreshTimer?.Stop(); _refreshTimer?.Dispose(); } catch { }

            base.OnFormClosing(e);
        }
    }
}
