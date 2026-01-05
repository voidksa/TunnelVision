using System;
using System.Drawing;
using System.Windows.Forms;

namespace TunnelVision
{
    public class UpdateForm : Form
    {
        private readonly string _version;
        private readonly string _downloadUrl;
        private Label _label;
        private Button _downloadButton;
        private Button _closeButton;

        public UpdateForm(string version, string downloadUrl)
        {
            _version = version;
            _downloadUrl = downloadUrl;
            InitializeComponent();
            ApplyTheme();
        }

        private void InitializeComponent()
        {
            this.Text = "Update Available";
            this.Size = new Size(420, 180);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;

            _label = new Label()
            {
                Text = "A new version is available: v" + _version,
                Location = new Point(20, 20),
                AutoSize = true
            };

            _downloadButton = new Button()
            {
                Text = "Download",
                Location = new Point(220, 100),
                Size = new Size(80, 30)
            };
            _downloadButton.Click += (s, e) =>
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

            _closeButton = new Button()
            {
                Text = "Close",
                Location = new Point(310, 100),
                Size = new Size(80, 30)
            };
            _closeButton.Click += (s, e) => this.Close();

            this.Controls.Add(_label);
            this.Controls.Add(_downloadButton);
            this.Controls.Add(_closeButton);
        }

        private void ApplyTheme()
        {
            bool isDark = IsDarkMode();
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
            this.BackColor = backColor;
            this.ForeColor = foreColor;
            foreach (Control c in this.Controls)
            {
                c.ForeColor = foreColor;
                if (c is Button btn)
                {
                    btn.BackColor = isDark ? Color.FromArgb(60, 60, 60) : SystemColors.Control;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = Color.Gray;
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
    }
}
