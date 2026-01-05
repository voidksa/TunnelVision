using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace TunnelVision
{
    public class SettingsForm : Form
    {
        private AppSettings _settings;
        private Action _onSettingsChanged;

        private TrackBar _opacityTrackBar;
        private Label _opacityValueLabel;
        private CheckBox _startupCheckBox;
        private CheckBox _smoothCheckBox;
        private TextBox _hotkeyTextBox;
        private Button _resetHotkeyButton;
        private Button _closeButton;
        private PictureBox _githubIcon;
        private Label _versionLabel;

        public SettingsForm(AppSettings settings, Action onSettingsChanged)
        {
            _settings = settings;
            _onSettingsChanged = onSettingsChanged;

            InitializeComponent();
            ApplyTheme();
            LoadSettingsToUI();
        }

        private void ApplyTheme()
        {
            bool isDark = IsDarkMode();

            // Apply DWM Dark Mode to Title Bar
            if (isDark)
            {
                int useDarkMode = 1;
                NativeMethods.DwmSetWindowAttribute(this.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
            }
            else
            {
                int useDarkMode = 0;
                NativeMethods.DwmSetWindowAttribute(this.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
            }

            Color backColor = isDark ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
            Color foreColor = isDark ? Color.White : SystemColors.ControlText;
            Color controlBack = isDark ? Color.FromArgb(45, 45, 48) : SystemColors.Window;
            Color controlFore = isDark ? Color.White : SystemColors.WindowText;
            Color buttonBack = isDark ? Color.FromArgb(60, 60, 60) : SystemColors.Control;

            this.BackColor = backColor;
            this.ForeColor = foreColor;

            foreach (Control c in this.Controls)
            {
                UpdateControlTheme(c, foreColor, controlBack, controlFore, buttonBack);
            }
        }

        private void UpdateControlTheme(Control c, Color foreColor, Color controlBack, Color controlFore, Color buttonBack)
        {
            c.ForeColor = foreColor;

            if (c is TextBox txt)
            {
                txt.BackColor = controlBack;
                txt.ForeColor = controlFore;
                txt.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is Button btn)
            {
                btn.BackColor = buttonBack;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = Color.Gray;
            }
            else if (c is CheckBox chk)
            {
                // Checkbox usually inherits parent colors nicely
            }
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
            return false; // Default to light if check fails
        }

        private void InitializeComponent()
        {
            this.Text = "Tunnel Vision Settings";
            this.Size = new Size(350, 400);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            // Opacity Group
            Label opacityLabel = new Label() { Text = "Darkness Level:", Location = new Point(20, 20), AutoSize = true };

            _opacityTrackBar = new TrackBar()
            {
                Minimum = 10,
                Maximum = 95,
                TickFrequency = 5,
                Location = new Point(20, 45),
                Width = 280,
                Value = (int)(_settings.Opacity * 100)
            };
            _opacityTrackBar.Scroll += (s, e) =>
            {
                _settings.Opacity = _opacityTrackBar.Value / 100.0;
                _opacityValueLabel.Text = $"{_opacityTrackBar.Value}%";
                _onSettingsChanged?.Invoke();
            };

            _opacityValueLabel = new Label()
            {
                Text = $"{_opacityTrackBar.Value}%",
                Location = new Point(300, 45),
                AutoSize = true
            };

            // Options Group
            _startupCheckBox = new CheckBox()
            {
                Text = "Run on Windows Startup",
                Location = new Point(25, 100),
                Width = 250,
                Checked = _settings.RunOnStartup
            };
            _startupCheckBox.CheckedChanged += (s, e) =>
            {
                _settings.RunOnStartup = _startupCheckBox.Checked;
                SetStartup(_settings.RunOnStartup);
                _settings.Save();
            };

            _smoothCheckBox = new CheckBox()
            {
                Text = "Smooth Movement (Higher CPU)",
                Location = new Point(25, 130),
                Width = 250,
                Checked = _settings.SmoothMovement
            };
            _smoothCheckBox.CheckedChanged += (s, e) =>
            {
                _settings.SmoothMovement = _smoothCheckBox.Checked;
                _onSettingsChanged?.Invoke();
                _settings.Save();
            };

            // Hotkey Group
            Label hotkeyLabel = new Label() { Text = "Global Hotkey (Click to change):", Location = new Point(25, 170), AutoSize = true };

            _hotkeyTextBox = new TextBox()
            {
                Location = new Point(25, 195),
                Width = 180,
                ReadOnly = true, // We will handle key down manually
                BackColor = Color.White
            };
            _hotkeyTextBox.KeyDown += HotkeyTextBox_KeyDown;

            _resetHotkeyButton = new Button()
            {
                Text = "Reset",
                Location = new Point(215, 193),
                Size = new Size(60, 25)
            };
            _resetHotkeyButton.Click += (s, e) => ResetHotkey();

            // Close Button
            _closeButton = new Button()
            {
                Text = "Close",
                Location = new Point(120, 280),
                Size = new Size(100, 30),
            };
            _closeButton.Click += (s, e) => this.Hide();

            // Add Controls
            this.Controls.Add(opacityLabel);
            this.Controls.Add(_opacityTrackBar);
            this.Controls.Add(_opacityValueLabel);
            this.Controls.Add(_startupCheckBox);
            this.Controls.Add(_smoothCheckBox);
            this.Controls.Add(hotkeyLabel);
            this.Controls.Add(_hotkeyTextBox);
            this.Controls.Add(_resetHotkeyButton);
            this.Controls.Add(_closeButton);

            _githubIcon = new PictureBox()
            {
                Size = new Size(24, 24),
                Location = new Point(this.ClientSize.Width - 32, 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand,
                Image = GetGithubImage(),
                SizeMode = PictureBoxSizeMode.Zoom,
                TabStop = false
            };
            _githubIcon.Click += (s, e) => OpenUrl("https://github.com/voidksa/TunnelVision");
            this.Controls.Add(_githubIcon);

            _versionLabel = new Label()
            {
                Text = "v" + GetVersionString(),
                Location = new Point(20, 320),
                AutoSize = true
            };
            this.Controls.Add(_versionLabel);

            this.Shown += async (s, e) => await LoadGithubIconAsync();
        }

        private Image GetGithubImage()
        {
            try
            {
                var base64 = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAACXBIWXMAAAsTAAALEwEAmpwYAAABQElEQVRIie2Uv0sDQRSGv0mJdJgHk4g4AqQeQJ2QvCkIuQZxJkCkQYgEoQWmYUpmQfQh5w2C1bV0rQH6Wm6m1cN9b8kZbqK0b3fJv3zZx7q1Vb3IYkqFv8CwEJYwA6f1cQ0Wm0u2sWJYl0v0fEo4tQpYgkC1P9H6gE4u2lYArQb4hMksR3g5x0tRjS8qJ3oQp9w4Qj0m3fQpQWcP4kR9m4wHqVqvYqkC1xM8l1oR7g1kQ3cA0lLk7wCkZf8r3wYgI2cQq4m7U1cM2bWbJf4M1q3n6Uj0fNwWbC7gK0k5gqYgP1uKJ8rW+gC8YwY8Fv3f4qVQnKq6O5gH3b7wS2jIYwWcT1iP1Wf0r3oYb0z2Uj8zY+QYv6nY9qC0mJ0Ww4h3UoQF+GHB8oYJQmGmKJ3FQnJkC8oYzM6QdRZxF6vV8V7mKIk9V3QH0oJvRkYgY+gI1o19mJp3j6VhT+QZfGvYb6gE1r5QYwEAAAAASUVORK5CYII=";
                var bytes = Convert.FromBase64String(base64);
                using (var ms = new System.IO.MemoryStream(bytes))
                {
                    return Image.FromStream(ms);
                }
            }
            catch { }
            return SystemIcons.Information.ToBitmap();
        }

        private async Task LoadGithubIconAsync()
        {
            var urls = new[]
            {
                "https://github.githubassets.com/favicons/favicon.png",
                "https://github.com/favicon.ico"
            };
            foreach (var u in urls)
            {
                try
                {
                    using var http = new HttpClient();
                    var bytes = await http.GetByteArrayAsync(u);
                    using var ms = new System.IO.MemoryStream(bytes);
                    var img = Image.FromStream(ms);
                    _githubIcon.Image = img;
                    return;
                }
                catch { }
            }
        }

        private string GetVersionString()
        {
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                if (ver != null)
                {
                    return $"{ver.Major}.{ver.Minor}.{ver.Build}";
                }
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

        private void LoadSettingsToUI()
        {
            _opacityTrackBar.Value = (int)(_settings.Opacity * 100);
            _opacityValueLabel.Text = $"{_opacityTrackBar.Value}%";
            _startupCheckBox.Checked = _settings.RunOnStartup;
            _smoothCheckBox.Checked = _settings.SmoothMovement;
            UpdateHotkeyDisplay();
        }

        private void UpdateHotkeyDisplay()
        {
            string mods = "";
            if ((_settings.HotkeyModifiers & NativeMethods.MOD_CONTROL) != 0) mods += "Ctrl + ";
            if ((_settings.HotkeyModifiers & NativeMethods.MOD_ALT) != 0) mods += "Alt + ";
            if ((_settings.HotkeyModifiers & NativeMethods.MOD_SHIFT) != 0) mods += "Shift + ";

            _hotkeyTextBox.Text = $"{mods}{(Keys)_settings.HotkeyKey}";
        }

        private void HotkeyTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true; // Prevent typing

            // Ignore single modifier keys
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu)
                return;

            int modifiers = 0;
            if (e.Control) modifiers |= NativeMethods.MOD_CONTROL;
            if (e.Alt) modifiers |= NativeMethods.MOD_ALT;
            if (e.Shift) modifiers |= NativeMethods.MOD_SHIFT;

            // Require at least one modifier? Or allow single keys? 
            // Better to require modifier to avoid accidents, but user might want F1.
            // Let's allow F-keys without modifiers, but others with.
            // For now, flexible.

            if (modifiers == 0 && (e.KeyCode < Keys.F1 || e.KeyCode > Keys.F24))
            {
                // Maybe warn? Or just allow it. User asked for flexibility.
                // But Ctrl+Alt+T is default.
            }

            _settings.HotkeyModifiers = modifiers;
            _settings.HotkeyKey = (int)e.KeyCode;

            UpdateHotkeyDisplay();
            _settings.Save();
            _onSettingsChanged?.Invoke();
        }

        private void ResetHotkey()
        {
            _settings.HotkeyModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
            _settings.HotkeyKey = (int)Keys.T;
            UpdateHotkeyDisplay();
            _settings.Save();
            _onSettingsChanged?.Invoke();
        }

        private void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (enable)
                        key.SetValue("TunnelVision", Application.ExecutablePath);
                    else
                        key.DeleteValue("TunnelVision", false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to update startup settings: " + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                _settings.Save();
            }
            base.OnFormClosing(e);
        }
    }
}
