using System;
using System.Drawing;
using System.Windows.Forms;

namespace TunnelVision
{
    public class UpdateForm : Form
    {
        private readonly string _newVersion;
        private readonly string _currentVersion;
        private readonly string _downloadUrl;
        private readonly string _releaseNotes;
        private readonly Action? _onSkip;

        public UpdateForm(string newVersion, string downloadUrl, string releaseNotes, string currentVersion, Action? onSkip = null)
        {
            _newVersion = newVersion;
            _downloadUrl = downloadUrl;
            _releaseNotes = releaseNotes ?? "";
            _currentVersion = currentVersion ?? "";
            _onSkip = onSkip;

            InitializeComponent();
            ApplyTheme();
        }

        private void InitializeComponent()
        {
            this.Text = "Update Available — Tunnel Vision";
            this.Size = new Size(520, 440);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            var headerLabel = new Label
            {
                Text = "A new version of Tunnel Vision is available",
                Location = new Point(20, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold)
            };

            var versionLabel = new Label
            {
                Text = $"v{_currentVersion}  →  v{_newVersion}",
                Location = new Point(20, 46),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(120, 170, 255)
            };

            var notesHeader = new Label
            {
                Text = "What's new",
                Location = new Point(20, 80),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            var notesBox = new TextBox
            {
                Location = new Point(20, 104),
                Size = new Size(470, 240),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Text = string.IsNullOrWhiteSpace(_releaseNotes)
                    ? "See the release page for details."
                    : _releaseNotes.Replace("\n", Environment.NewLine),
                Font = new Font("Segoe UI", 9f)
            };

            var downloadButton = new Button
            {
                Text = "Download",
                Location = new Point(380, 360),
                Size = new Size(110, 32)
            };
            downloadButton.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _downloadUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
                this.Close();
            };

            var laterButton = new Button
            {
                Text = "Later",
                Location = new Point(260, 360),
                Size = new Size(110, 32)
            };
            laterButton.Click += (s, e) => this.Close();

            var skipButton = new Button
            {
                Text = "Skip this version",
                Location = new Point(20, 360),
                Size = new Size(140, 32)
            };
            skipButton.Click += (s, e) =>
            {
                _onSkip?.Invoke();
                this.Close();
            };

            this.Controls.Add(headerLabel);
            this.Controls.Add(versionLabel);
            this.Controls.Add(notesHeader);
            this.Controls.Add(notesBox);
            this.Controls.Add(skipButton);
            this.Controls.Add(laterButton);
            this.Controls.Add(downloadButton);

            this.AcceptButton = downloadButton;
            this.CancelButton = laterButton;
        }

        private void ApplyTheme()
        {
            bool isDark = IsDarkMode();

            int useDarkMode = isDark ? 1 : 0;
            try
            {
                NativeMethods.DwmSetWindowAttribute(this.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
            }
            catch { }

            Color backColor = isDark ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
            Color foreColor = isDark ? Color.White : SystemColors.ControlText;
            Color controlBack = isDark ? Color.FromArgb(45, 45, 48) : SystemColors.Window;
            Color buttonBack = isDark ? Color.FromArgb(60, 60, 60) : SystemColors.Control;

            this.BackColor = backColor;
            this.ForeColor = foreColor;

            foreach (Control c in this.Controls)
            {
                c.ForeColor = foreColor;
                if (c is Button btn)
                {
                    btn.BackColor = buttonBack;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = Color.Gray;
                }
                else if (c is TextBox txt)
                {
                    txt.BackColor = controlBack;
                    txt.ForeColor = foreColor;
                }
            }
        }

        private bool IsDarkMode()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object? val = key.GetValue("AppsUseLightTheme");
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
    }
}
