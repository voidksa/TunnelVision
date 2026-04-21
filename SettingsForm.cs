using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Reflection;

namespace TunnelVision
{
    // Modern Win11-style settings with:
    //   - Left sidebar (pill nav, green accent, no system TabControl chrome)
    //   - Mica backdrop applied via DWM (Windows 11)
    //   - Rounded, flat "ModernButton" / switch-style checkboxes
    //   - Single color system (Theme.cs) — matches app icon mint green
    public class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly Action _onSettingsChanged;
        private readonly Action _onManualUpdateCheck;

        private bool _isDark;
        private bool _suppressEvents;

        // Layout regions
        private Panel _sidebar = null!;
        private Panel _content = null!;
        private readonly List<SidebarItem> _sidebarItems = new();
        private Panel? _activePage;

        // General
        private ModernSlider _opacityTrackBar = null!;
        private Label _opacityValueLabel = null!;
        private ModernNumericInput _stepNumeric = null!;
        private ToggleSwitch _osdCheckBox = null!;
        private ColorSwatch _tintPreview = null!;
        private Label _tintLabel = null!;

        // Hotkeys
        private TextBox _toggleHotkeyBox = null!;
        private TextBox _increaseHotkeyBox = null!;
        private TextBox _decreaseHotkeyBox = null!;
        private TextBox _settingsHotkeyBox = null!;

        // Behavior
        private ToggleSwitch _startupCheckBox = null!;
        private ToggleSwitch _smoothCheckBox = null!;
        private ToggleSwitch _blurCheckBox = null!;
        private ToggleSwitch _fullscreenCheckBox = null!;
        private ToggleSwitch _autoUpdateCheckBox = null!;

        // About
        private Label _versionLabel = null!;

        public SettingsForm(AppSettings settings, Action onSettingsChanged, Action onManualUpdateCheck)
        {
            _settings = settings;
            _onSettingsChanged = onSettingsChanged;
            _onManualUpdateCheck = onManualUpdateCheck;

            _isDark = Theme.IsSystemDark();

            InitializeForm();
            BuildLayout();
            LoadSettingsToUI();
            SelectSidebar(0);
        }

        public void SyncOpacityFromExternal()
        {
            if (this.IsDisposed) return;
            this.BeginInvoke(new Action(() =>
            {
                _suppressEvents = true;
                int v = Math.Max(_opacityTrackBar.Minimum, Math.Min(_opacityTrackBar.Maximum, (int)Math.Round(_settings.Opacity * 100)));
                _opacityTrackBar.Value = v;
                _opacityValueLabel.Text = $"{v}%";
                _suppressEvents = false;
            }));
        }

        private void InitializeForm()
        {
            this.Text = "Tunnel Vision — Settings";
            this.ClientSize = new Size(780, 540);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5f);
            this.DoubleBuffered = true;
            this.BackColor = _isDark ? Theme.Dark.Background : Theme.Light.Background;
            this.ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary;

            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.TryApplyMica(this.Handle, _isDark);
            NativeMethods.TryApplyRoundedCorners(this.Handle, small: false);
        }

        // ============================== Layout ==============================

        private void BuildLayout()
        {
            _sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 210,
                BackColor = _isDark ? Theme.Dark.Surface : Theme.Light.Surface,
                Padding = new Padding(12, 16, 12, 16)
            };

            _content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(28, 24, 28, 20),
                AutoScroll = false
            };

            // Sidebar header (app name + icon)
            var header = new Label
            {
                Text = "Tunnel Vision",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary,
                Location = new Point(12, 8),
                AutoSize = true
            };

            var subtitle = new Label
            {
                Text = "Settings",
                Font = new Font("Segoe UI", 9f),
                ForeColor = _isDark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary,
                Location = new Point(12, 32),
                AutoSize = true
            };

            _sidebar.Controls.Add(header);
            _sidebar.Controls.Add(subtitle);

            // Sidebar items
            AddSidebarItem("General", 68, BuildGeneralPage());
            AddSidebarItem("Hotkeys", 112, BuildHotkeysPage());
            AddSidebarItem("Behavior", 156, BuildBehaviorPage());
            AddSidebarItem("About", 200, BuildAboutPage());

            this.Controls.Add(_content);
            this.Controls.Add(_sidebar);
        }

        private void AddSidebarItem(string text, int y, Panel page)
        {
            var item = new SidebarItem(text, _isDark)
            {
                Location = new Point(8, y),
                Size = new Size(194, 38),
                Page = page
            };
            int index = _sidebarItems.Count;
            item.Click += (s, e) => SelectSidebar(index);

            _sidebar.Controls.Add(item);
            _sidebarItems.Add(item);

            page.Visible = false;
            page.Dock = DockStyle.Fill;
            _content.Controls.Add(page);
        }

        private void SelectSidebar(int index)
        {
            for (int i = 0; i < _sidebarItems.Count; i++)
            {
                var it = _sidebarItems[i];
                it.SetSelected(i == index);
                if (it.Page != null) it.Page.Visible = (i == index);
                if (i == index) _activePage = it.Page;
            }
        }

        // ============================== Pages ==============================

        private Panel BuildPage()
        {
            return new Panel
            {
                BackColor = Color.Transparent,
                AutoScroll = false
            };
        }

        private Label SectionTitle(string text, int y) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary,
            Location = new Point(0, y),
            AutoSize = true
        };

        private Label FieldLabel(string text, int x, int y) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary,
            Location = new Point(x, y),
            AutoSize = true
        };

        private Label FieldDescription(string text, int x, int y) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9f),
            ForeColor = _isDark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary,
            Location = new Point(x, y),
            AutoSize = true,
            MaximumSize = new Size(440, 0)
        };

        private Panel BuildGeneralPage()
        {
            var page = BuildPage();
            page.Controls.Add(SectionTitle("General", 0));

            page.Controls.Add(FieldLabel("Darkness level", 0, 48));
            page.Controls.Add(FieldDescription("How much to dim the background. Hotkeys can change this live.", 0, 70));

            _opacityTrackBar = new ModernSlider(_isDark)
            {
                Minimum = 10,
                Maximum = 95,
                Location = new Point(0, 100),
                Size = new Size(380, 26),
                Value = (int)(_settings.Opacity * 100)
            };
            _opacityTrackBar.ValueChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                _settings.Opacity = _opacityTrackBar.Value / 100.0;
                _opacityValueLabel.Text = $"{_opacityTrackBar.Value}%";
                _settings.Save();
                _onSettingsChanged?.Invoke();
            };

            _opacityValueLabel = new Label
            {
                Text = $"{_opacityTrackBar.Value}%",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Theme.Accent,
                Location = new Point(390, 100),
                AutoSize = true
            };

            page.Controls.Add(_opacityTrackBar);
            page.Controls.Add(_opacityValueLabel);

            page.Controls.Add(FieldLabel("Step size", 0, 160));
            page.Controls.Add(FieldDescription("How much the intensity hotkeys change darkness per press.", 0, 182));

            _stepNumeric = new ModernNumericInput(_isDark)
            {
                Location = new Point(0, 214),
                Size = new Size(90, 30),
                Minimum = 1,
                Maximum = 25,
                Value = Math.Max(1, Math.Min(25, _settings.IntensityStep))
            };
            _stepNumeric.ValueChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                _settings.IntensityStep = _stepNumeric.Value;
                _settings.Save();
            };

            var stepSuffix = new Label
            {
                Text = "%  per keypress",
                Location = new Point(96, 219),
                ForeColor = _isDark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary,
                AutoSize = true
            };

            page.Controls.Add(_stepNumeric);
            page.Controls.Add(stepSuffix);

            _osdCheckBox = BuildSwitch("Show on-screen indicator when intensity changes", 262, _settings.ShowOsdOnChange, (chk) =>
            {
                _settings.ShowOsdOnChange = chk;
                _settings.Save();
            });
            page.Controls.Add(_osdCheckBox);

            // Tint color picker
            page.Controls.Add(FieldLabel("Dim color", 0, 310));
            page.Controls.Add(FieldDescription("Pick the tint. Default is pure black for maximum contrast.", 0, 332));

            _tintPreview = new ColorSwatch(_isDark)
            {
                Location = new Point(0, 364),
                Size = new Size(48, 32),
                SwatchColor = Color.FromArgb(_settings.TintColorArgb)
            };
            _tintPreview.Click += (s, e) => PickTintColor();

            _tintLabel = new Label
            {
                Text = ColorToHex(Color.FromArgb(_settings.TintColorArgb)),
                Location = new Point(58, 372),
                ForeColor = _isDark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary,
                AutoSize = true
            };

            var pickButton = new ModernButton("Pick color", _isDark)
            {
                Location = new Point(140, 364),
                Size = new Size(110, 32)
            };
            pickButton.Click += (s, e) => PickTintColor();

            var resetTint = new ModernButton("Reset", _isDark)
            {
                Location = new Point(258, 364),
                Size = new Size(80, 32)
            };
            resetTint.Click += (s, e) =>
            {
                _settings.TintColorArgb = unchecked((int)0xFF000000);
                _settings.Save();
                UpdateTintUI();
                _onSettingsChanged?.Invoke();
            };

            page.Controls.Add(_tintPreview);
            page.Controls.Add(_tintLabel);
            page.Controls.Add(pickButton);
            page.Controls.Add(resetTint);

            return page;
        }

        private void PickTintColor()
        {
            using var dlg = new ColorDialog
            {
                Color = Color.FromArgb(_settings.TintColorArgb),
                FullOpen = true
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var c = Color.FromArgb(255, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                _settings.TintColorArgb = c.ToArgb();
                _settings.Save();
                UpdateTintUI();
                _onSettingsChanged?.Invoke();
            }
        }

        private void UpdateTintUI()
        {
            var c = Color.FromArgb(_settings.TintColorArgb);
            _tintPreview.SwatchColor = c;
            _tintLabel.Text = ColorToHex(c);
        }

        private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private Panel BuildHotkeysPage()
        {
            var page = BuildPage();
            page.Controls.Add(SectionTitle("Hotkeys", 0));
            page.Controls.Add(FieldDescription("Click a field and press the key combination you want.", 0, 38));

            page.Controls.Add(FieldLabel("Toggle focus", 0, 80));
            _toggleHotkeyBox = MakeHotkeyBox(new Point(0, 106), HotkeyKind.Toggle);
            page.Controls.Add(_toggleHotkeyBox);
            page.Controls.Add(MakeResetButton(new Point(330, 106), HotkeyKind.Toggle));

            page.Controls.Add(FieldLabel("Increase intensity", 0, 158));
            _increaseHotkeyBox = MakeHotkeyBox(new Point(0, 184), HotkeyKind.Increase);
            page.Controls.Add(_increaseHotkeyBox);
            page.Controls.Add(MakeResetButton(new Point(330, 184), HotkeyKind.Increase));

            page.Controls.Add(FieldLabel("Decrease intensity", 0, 236));
            _decreaseHotkeyBox = MakeHotkeyBox(new Point(0, 262), HotkeyKind.Decrease);
            page.Controls.Add(_decreaseHotkeyBox);
            page.Controls.Add(MakeResetButton(new Point(330, 262), HotkeyKind.Decrease));

            page.Controls.Add(FieldLabel("Open / close settings", 0, 314));
            _settingsHotkeyBox = MakeHotkeyBox(new Point(0, 340), HotkeyKind.Settings);
            page.Controls.Add(_settingsHotkeyBox);
            page.Controls.Add(MakeResetButton(new Point(330, 340), HotkeyKind.Settings));

            return page;
        }

        private enum HotkeyKind { Toggle, Increase, Decrease, Settings }

        private TextBox MakeHotkeyBox(Point location, HotkeyKind kind)
        {
            var tb = new TextBox
            {
                Location = location,
                Width = 320,
                Height = 32,
                Font = new Font("Segoe UI", 10f),
                ReadOnly = true,
                Cursor = Cursors.Hand,
                BackColor = _isDark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover,
                ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Tag = kind
            };
            tb.KeyDown += HotkeyBox_KeyDown;
            return tb;
        }

        private ModernButton MakeResetButton(Point location, HotkeyKind kind)
        {
            var btn = new ModernButton("Reset", _isDark)
            {
                Location = location,
                Size = new Size(90, 32)
            };
            btn.Click += (s, e) => ResetHotkey(kind);
            return btn;
        }

        private Panel BuildBehaviorPage()
        {
            var page = BuildPage();
            page.Controls.Add(SectionTitle("Behavior", 0));

            _startupCheckBox = BuildSwitch("Run on Windows startup", 60, _settings.RunOnStartup, (chk) =>
            {
                _settings.RunOnStartup = chk;
                SetStartup(chk);
                _settings.Save();
            });
            page.Controls.Add(_startupCheckBox);

            _smoothCheckBox = BuildSwitch("Smooth movement (~60 FPS tracking, slightly higher CPU)", 108, _settings.SmoothMovement, (chk) =>
            {
                _settings.SmoothMovement = chk;
                _settings.Save();
                _onSettingsChanged?.Invoke();
            });
            page.Controls.Add(_smoothCheckBox);

            // NOTE: Blur background option is intentionally hidden in v1.1.0.
            // Windows' ACCENT_ENABLE_ACRYLICBLURBEHIND conflicts with our WS_EX_LAYERED
            // overlay and region-based cutout — the acrylic material fills the cutout
            // area, defeating the focus highlight. A proper implementation (separate
            // blur window with masked region) is planned for a later release.
            // The field is still present (_blurCheckBox) so layout calculations and
            // LoadSettingsToUI don't break.
            _blurCheckBox = new ToggleSwitch(_isDark) { Visible = false };

            _fullscreenCheckBox = BuildSwitch("Auto-pause in fullscreen (games, videos, presentations)", 156, _settings.PauseInFullscreen, (chk) =>
            {
                _settings.PauseInFullscreen = chk;
                _settings.Save();
            });
            page.Controls.Add(_fullscreenCheckBox);

            _autoUpdateCheckBox = BuildSwitch("Check for updates automatically (every 6 hours)", 204, _settings.AutoCheckUpdates, (chk) =>
            {
                _settings.AutoCheckUpdates = chk;
                _settings.Save();
                _onSettingsChanged?.Invoke();
            });
            page.Controls.Add(_autoUpdateCheckBox);

            return page;
        }

        private Panel BuildAboutPage()
        {
            var page = BuildPage();
            page.Controls.Add(SectionTitle("About", 0));

            var title = new Label
            {
                Text = "Tunnel Vision",
                Location = new Point(0, 56),
                AutoSize = true,
                Font = new Font("Segoe UI", 22f, FontStyle.Bold),
                ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary
            };

            _versionLabel = new Label
            {
                Text = "Version " + GetVersionString(),
                Location = new Point(0, 106),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Theme.Accent
            };

            var tagline = new Label
            {
                Text = "Focus on what matters. Dim the rest.",
                Location = new Point(0, 134),
                AutoSize = true,
                ForeColor = _isDark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary
            };

            var checkUpdates = new ModernButton("Check for updates", _isDark, primary: true)
            {
                Location = new Point(0, 180),
                Size = new Size(170, 36)
            };
            checkUpdates.Click += (s, e) => _onManualUpdateCheck?.Invoke();

            var github = new ModernButton("GitHub", _isDark)
            {
                Location = new Point(180, 180),
                Size = new Size(120, 36)
            };
            github.Click += (s, e) => OpenUrl("https://github.com/voidksa/TunnelVision");

            var report = new ModernButton("Report an issue", _isDark)
            {
                Location = new Point(310, 180),
                Size = new Size(150, 36)
            };
            report.Click += (s, e) => OpenUrl("https://github.com/voidksa/TunnelVision/issues/new");

            var credits = new Label
            {
                Text = "Made with ♥ by voidksa · MIT License",
                Location = new Point(0, 240),
                AutoSize = true,
                ForeColor = _isDark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary
            };

            page.Controls.Add(title);
            page.Controls.Add(_versionLabel);
            page.Controls.Add(tagline);
            page.Controls.Add(checkUpdates);
            page.Controls.Add(github);
            page.Controls.Add(report);
            page.Controls.Add(credits);

            return page;
        }

        // ============================== Controls: Switch ==============================

        private ToggleSwitch BuildSwitch(string label, int y, bool initial, Action<bool> onChanged)
        {
            var sw = new ToggleSwitch(_isDark)
            {
                Location = new Point(0, y),
                Size = new Size(520, 36),
                Text = label,
                Checked = initial
            };
            sw.CheckedChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                onChanged(sw.Checked);
            };
            return sw;
        }

        // ============================== Helpers ==============================

        private void LoadSettingsToUI()
        {
            _suppressEvents = true;
            _opacityTrackBar.Value = Math.Max(_opacityTrackBar.Minimum, Math.Min(_opacityTrackBar.Maximum, (int)(_settings.Opacity * 100)));
            _opacityValueLabel.Text = $"{_opacityTrackBar.Value}%";
            _stepNumeric.Value = Math.Max(_stepNumeric.Minimum, Math.Min(_stepNumeric.Maximum, _settings.IntensityStep));
            // Note: ModernNumericInput (custom) replaces NumericUpDown so dark theme works.
            _osdCheckBox.Checked = _settings.ShowOsdOnChange;
            _startupCheckBox.Checked = _settings.RunOnStartup;
            _smoothCheckBox.Checked = _settings.SmoothMovement;
            _blurCheckBox.Checked = _settings.BlurBackground;
            _fullscreenCheckBox.Checked = _settings.PauseInFullscreen;
            _autoUpdateCheckBox.Checked = _settings.AutoCheckUpdates;
            UpdateHotkeyDisplay();
            UpdateTintUI();
            _suppressEvents = false;
        }

        private void UpdateHotkeyDisplay()
        {
            _toggleHotkeyBox.Text = "   " + FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
            _increaseHotkeyBox.Text = "   " + FormatHotkey(_settings.IncreaseHotkeyModifiers, _settings.IncreaseHotkeyKey);
            _decreaseHotkeyBox.Text = "   " + FormatHotkey(_settings.DecreaseHotkeyModifiers, _settings.DecreaseHotkeyKey);
            _settingsHotkeyBox.Text = "   " + FormatHotkey(_settings.SettingsHotkeyModifiers, _settings.SettingsHotkeyKey);
        }

        private static string FormatHotkey(int modifiers, int key)
        {
            string mods = "";
            if ((modifiers & NativeMethods.MOD_CONTROL) != 0) mods += "Ctrl + ";
            if ((modifiers & NativeMethods.MOD_ALT) != 0) mods += "Alt + ";
            if ((modifiers & NativeMethods.MOD_SHIFT) != 0) mods += "Shift + ";
            if ((modifiers & NativeMethods.MOD_WIN) != 0) mods += "Win + ";
            return mods + (Keys)key;
        }

        private void HotkeyBox_KeyDown(object? sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu)
                return;

            int modifiers = 0;
            if (e.Control) modifiers |= NativeMethods.MOD_CONTROL;
            if (e.Alt) modifiers |= NativeMethods.MOD_ALT;
            if (e.Shift) modifiers |= NativeMethods.MOD_SHIFT;

            var kind = (HotkeyKind)(((TextBox)sender!).Tag ?? HotkeyKind.Toggle);
            AssignHotkey(kind, modifiers, (int)e.KeyCode);
        }

        private void AssignHotkey(HotkeyKind kind, int modifiers, int key)
        {
            if (ConflictsWithOther(kind, modifiers, key))
            {
                MessageBox.Show(this, "This key combination is already assigned to another action.",
                    "Tunnel Vision", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            switch (kind)
            {
                case HotkeyKind.Toggle:
                    _settings.HotkeyModifiers = modifiers;
                    _settings.HotkeyKey = key;
                    break;
                case HotkeyKind.Increase:
                    _settings.IncreaseHotkeyModifiers = modifiers;
                    _settings.IncreaseHotkeyKey = key;
                    break;
                case HotkeyKind.Decrease:
                    _settings.DecreaseHotkeyModifiers = modifiers;
                    _settings.DecreaseHotkeyKey = key;
                    break;
                case HotkeyKind.Settings:
                    _settings.SettingsHotkeyModifiers = modifiers;
                    _settings.SettingsHotkeyKey = key;
                    break;
            }

            _settings.Save();
            UpdateHotkeyDisplay();
            _onSettingsChanged?.Invoke();
        }

        private bool ConflictsWithOther(HotkeyKind targetKind, int modifiers, int key)
        {
            bool Same(int m1, int k1, int m2, int k2) => m1 == m2 && k1 == k2;

            if (targetKind != HotkeyKind.Toggle && Same(modifiers, key, _settings.HotkeyModifiers, _settings.HotkeyKey)) return true;
            if (targetKind != HotkeyKind.Increase && Same(modifiers, key, _settings.IncreaseHotkeyModifiers, _settings.IncreaseHotkeyKey)) return true;
            if (targetKind != HotkeyKind.Decrease && Same(modifiers, key, _settings.DecreaseHotkeyModifiers, _settings.DecreaseHotkeyKey)) return true;
            if (targetKind != HotkeyKind.Settings && Same(modifiers, key, _settings.SettingsHotkeyModifiers, _settings.SettingsHotkeyKey)) return true;
            return false;
        }

        private void ResetHotkey(HotkeyKind kind)
        {
            switch (kind)
            {
                case HotkeyKind.Toggle:
                    _settings.HotkeyModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
                    _settings.HotkeyKey = (int)Keys.T;
                    break;
                case HotkeyKind.Increase:
                    _settings.IncreaseHotkeyModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
                    _settings.IncreaseHotkeyKey = (int)Keys.Up;
                    break;
                case HotkeyKind.Decrease:
                    _settings.DecreaseHotkeyModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
                    _settings.DecreaseHotkeyKey = (int)Keys.Down;
                    break;
                case HotkeyKind.Settings:
                    _settings.SettingsHotkeyModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
                    _settings.SettingsHotkeyKey = (int)Keys.S;
                    break;
            }

            _settings.Save();
            UpdateHotkeyDisplay();
            _onSettingsChanged?.Invoke();
        }

        private void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key == null) return;
                    if (enable) key.SetValue("TunnelVision", Application.ExecutablePath);
                    else key.DeleteValue("TunnelVision", false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to update startup settings: " + ex.Message);
            }
        }

        private string GetVersionString()
        {
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                if (ver != null) return $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
            catch { }
            return "1.0.0";
        }

        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                try { _settings.Save(); } catch { }
            }
            base.OnFormClosing(e);
        }
    }

    // ========================== Custom controls ==========================

    public class SidebarItem : Control
    {
        private readonly bool _dark;
        private bool _selected;
        private bool _hover;
        public Panel? Page { get; set; }

        public SidebarItem(string text, bool dark)
        {
            this.Text = text;
            _dark = dark;
            this.Cursor = Cursors.Hand;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint
                        | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint
                        | ControlStyles.ResizeRedraw
                        | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Font = new Font("Segoe UI", 10f);
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            this.Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, this.Width, this.Height);

            Color bg = Color.Transparent;
            if (_selected)
            {
                bg = _dark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover;
            }
            else if (_hover)
            {
                bg = _dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
            }

            if (bg != Color.Transparent)
            {
                using var path = Theme.RoundedRect(new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1), 8);
                using var brush = new SolidBrush(bg);
                g.FillPath(brush, path);
            }

            // Accent indicator bar on the left when selected
            if (_selected)
            {
                using var accentBrush = new SolidBrush(Theme.Accent);
                using var path = Theme.RoundedRect(new Rectangle(4, 10, 3, bounds.Height - 20), 2);
                g.FillPath(accentBrush, path);
            }

            // Text
            var textColor = _dark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary;
            TextRenderer.DrawText(g, this.Text, this.Font,
                new Rectangle(18, 0, this.Width - 24, this.Height),
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    public class ModernButton : Control
    {
        private readonly bool _dark;
        private readonly bool _primary;
        private bool _hover;
        private bool _pressed;

        public ModernButton(string text, bool dark, bool primary = false)
        {
            this.Text = text;
            _dark = dark;
            _primary = primary;
            this.Cursor = Cursors.Hand;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint
                        | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint
                        | ControlStyles.ResizeRedraw
                        | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color bg, border, fg;
            if (_primary)
            {
                bg = _pressed ? Theme.AccentDark : (_hover ? Theme.AccentHover : Theme.Accent);
                border = Theme.AccentDark;
                fg = Color.FromArgb(20, 20, 20);
            }
            else
            {
                bg = _pressed
                    ? (_dark ? Theme.Dark.SurfacePressed : Theme.Light.SurfacePressed)
                    : (_hover ? (_dark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover)
                              : (_dark ? Theme.Dark.Surface : Theme.Light.Surface));
                border = _dark ? Theme.Dark.Border : Theme.Light.Border;
                fg = _dark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary;
            }

            using (var path = Theme.RoundedRect(bounds, 6))
            using (var brush = new SolidBrush(bg))
            using (var pen = new Pen(border, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, this.Text, this.Font, new Rectangle(0, 0, this.Width, this.Height),
                fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Modern numeric input: rounded, dark-themed, custom up/down buttons on the right.
    // Replaces NumericUpDown which can't be dark-themed cleanly.
    public class ModernNumericInput : Control
    {
        private readonly bool _dark;
        private readonly TextBox _textBox;
        private int _min = 0;
        private int _max = 100;
        private int _value = 0;
        private bool _suppress;

        public event EventHandler? ValueChanged;

        public int Minimum { get => _min; set { _min = value; ClampValue(); } }
        public int Maximum { get => _max; set { _max = value; ClampValue(); } }
        public int Value
        {
            get => _value;
            set
            {
                int nv = Math.Max(_min, Math.Min(_max, value));
                if (nv == _value) return;
                _value = nv;
                _suppress = true;
                _textBox.Text = nv.ToString();
                _suppress = false;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ModernNumericInput(bool dark)
        {
            _dark = dark;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint
                        | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint
                        | ControlStyles.ResizeRedraw
                        | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;

            // Create the child TextBox FIRST so OnResize (fired by Size changes) finds it.
            _textBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10f),
                BackColor = _dark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover,
                ForeColor = _dark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary,
                TextAlign = HorizontalAlignment.Center
            };
            _textBox.KeyPress += (s, e) =>
            {
                if (!char.IsDigit(e.KeyChar) && e.KeyChar != '\b') e.Handled = true;
            };
            _textBox.TextChanged += (s, e) =>
            {
                if (_suppress) return;
                if (int.TryParse(_textBox.Text, out int v)) { Value = v; }
            };
            _textBox.Leave += (s, e) =>
            {
                _suppress = true;
                _textBox.Text = _value.ToString();
                _suppress = false;
            };

            this.Controls.Add(_textBox);
            this.Size = new Size(90, 30); // safe now: OnResize sees _textBox
            Value = _min;
        }

        private void ClampValue() { Value = Math.Max(_min, Math.Min(_max, _value)); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Guard: Size can be set inside the base constructor before _textBox is initialized.
            if (_textBox == null) return;
            // text area takes most space; 22px column on the right for +/- buttons
            _textBox.SetBounds(8, (this.Height - _textBox.PreferredHeight) / 2, this.Width - 32, _textBox.PreferredHeight);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int btnCol = this.Width - 22;
            if (e.X < btnCol) return;

            if (e.Y < this.Height / 2) Value++;
            else Value--;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value += Math.Sign(e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background pill
            var bounds = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            var bgColor = _dark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover;
            var border = _dark ? Theme.Dark.Border : Theme.Light.Border;

            using (var path = Theme.RoundedRect(bounds, 6))
            using (var brush = new SolidBrush(bgColor))
            using (var pen = new Pen(border, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            // +/- buttons area on the right
            int btnCol = this.Width - 22;
            var fg = _dark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary;

            // Up arrow
            var upRect = new Rectangle(btnCol, 2, 20, this.Height / 2 - 2);
            DrawArrow(g, upRect, true, fg);

            // Down arrow
            var dnRect = new Rectangle(btnCol, this.Height / 2, 20, this.Height / 2 - 2);
            DrawArrow(g, dnRect, false, fg);

            // Separator between text and buttons
            using (var pen = new Pen(border, 1f))
            {
                g.DrawLine(pen, btnCol - 2, 6, btnCol - 2, this.Height - 6);
            }
        }

        private void DrawArrow(Graphics g, Rectangle rect, bool up, Color color)
        {
            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            using var pen = new Pen(color, 1.5f);
            if (up)
            {
                g.DrawLine(pen, cx - 4, cy + 2, cx, cy - 2);
                g.DrawLine(pen, cx, cy - 2, cx + 4, cy + 2);
            }
            else
            {
                g.DrawLine(pen, cx - 4, cy - 2, cx, cy + 2);
                g.DrawLine(pen, cx, cy + 2, cx + 4, cy - 2);
            }
        }
    }

    // Rounded color swatch that paints ONLY inside the rounded shape — no BackColor
    // rectangle showing at the corners. Uses SupportsTransparentBackColor.
    public class ColorSwatch : Control
    {
        private readonly bool _dark;
        private Color _color = Color.Black;

        public Color SwatchColor
        {
            get => _color;
            set { _color = value; Invalidate(); }
        }

        public ColorSwatch(bool dark)
        {
            _dark = dark;
            this.Cursor = Cursors.Hand;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint
                        | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint
                        | ControlStyles.ResizeRedraw
                        | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using var path = Theme.RoundedRect(rect, 6);
            using var brush = new SolidBrush(_color);
            using var pen = new Pen(_dark ? Theme.Dark.Border : Theme.Light.Border, 1f);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
    }

    // Win11-style slider with flat track and circular thumb.
    public class ModernSlider : Control
    {
        private readonly bool _dark;
        private int _min = 0, _max = 100, _value = 50;
        private bool _dragging;
        private bool _hover;
        public event EventHandler? ValueChanged;

        public int Minimum { get => _min; set { _min = value; ClampValue(); Invalidate(); } }
        public int Maximum { get => _max; set { _max = value; ClampValue(); Invalidate(); } }
        public int Value
        {
            get => _value;
            set
            {
                int nv = Math.Max(_min, Math.Min(_max, value));
                if (nv == _value) return;
                _value = nv;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ModernSlider(bool dark)
        {
            _dark = dark;
            this.Cursor = Cursors.Hand;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint
                        | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint
                        | ControlStyles.ResizeRedraw
                        | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Height = 26;
        }

        private void ClampValue() { if (_value < _min) _value = _min; else if (_value > _max) _value = _max; }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _dragging = true;
            UpdateValueFromPoint(e.X);
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) UpdateValueFromPoint(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int step = Math.Max(1, (_max - _min) / 20);
            Value += Math.Sign(e.Delta) * step;
            base.OnMouseWheel(e);
        }

        private void UpdateValueFromPoint(int x)
        {
            int pad = 8;
            int w = this.Width - pad * 2;
            if (w <= 0) return;
            double ratio = (double)(x - pad) / w;
            ratio = Math.Max(0, Math.Min(1, ratio));
            Value = _min + (int)Math.Round(ratio * (_max - _min));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int pad = 8;
            int y = this.Height / 2;
            int trackH = 4;

            // Track (dim)
            var trackRect = new Rectangle(pad, y - trackH / 2, this.Width - pad * 2, trackH);
            using (var path = Theme.RoundedRect(trackRect, trackH / 2))
            using (var brush = new SolidBrush(_dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(210, 210, 210)))
            {
                g.FillPath(brush, path);
            }

            // Fill (accent green)
            double ratio = _max > _min ? (double)(_value - _min) / (_max - _min) : 0;
            int fillW = (int)Math.Round((this.Width - pad * 2) * ratio);
            if (fillW > 1)
            {
                var fillRect = new Rectangle(pad, y - trackH / 2, fillW, trackH);
                using var path = Theme.RoundedRect(fillRect, trackH / 2);
                using var brush = new SolidBrush(Theme.Accent);
                g.FillPath(brush, path);
            }

            // Thumb (circle)
            int knobDiameter = _hover || _dragging ? 18 : 14;
            int knobX = pad + fillW - knobDiameter / 2;
            int knobY = y - knobDiameter / 2;
            var knob = new Rectangle(knobX, knobY, knobDiameter, knobDiameter);

            // Knob outer ring (accent)
            using (var brush = new SolidBrush(Theme.Accent))
            {
                g.FillEllipse(brush, knob);
            }

            // Knob inner (surface color) — "donut" hole like Fluent sliders
            int holeInset = _dragging ? 3 : 4;
            var hole = new Rectangle(knob.X + holeInset, knob.Y + holeInset, knob.Width - holeInset * 2, knob.Height - holeInset * 2);
            using (var brush = new SolidBrush(_dark ? Theme.Dark.Background : Theme.Light.Background))
            {
                g.FillEllipse(brush, hole);
            }
        }
    }

    public class ToggleSwitch : Control
    {
        private bool _checked;
        private readonly bool _dark;
        public event EventHandler? CheckedChanged;
        public bool Checked
        {
            get => _checked;
            set { if (_checked == value) return; _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        public ToggleSwitch(bool dark)
        {
            _dark = dark;
            this.Cursor = Cursors.Hand;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint
                        | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint
                        | ControlStyles.ResizeRedraw
                        | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Font = new Font("Segoe UI", 9.5f);
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int trackW = 40, trackH = 20;
            var track = new Rectangle(0, (this.Height - trackH) / 2, trackW, trackH);

            Color trackColor = _checked
                ? Theme.Accent
                : (_dark ? Color.FromArgb(80, 80, 80) : Color.FromArgb(200, 200, 200));

            using (var path = Theme.RoundedRect(track, trackH / 2))
            using (var brush = new SolidBrush(trackColor))
            {
                g.FillPath(brush, path);
            }

            int knobDiameter = trackH - 6;
            int knobX = _checked ? track.Right - knobDiameter - 3 : track.Left + 3;
            int knobY = track.Top + 3;
            var knob = new Rectangle(knobX, knobY, knobDiameter, knobDiameter);

            using (var brush = new SolidBrush(Color.White))
            {
                g.FillEllipse(brush, knob);
            }

            // Label
            var textColor = _dark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary;
            TextRenderer.DrawText(g, this.Text, this.Font,
                new Rectangle(trackW + 12, 0, this.Width - trackW - 12, this.Height),
                textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
        }
    }
}
