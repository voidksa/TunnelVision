using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TunnelVision
{
    // Fluent-themed installer. Runs the first time someone launches a freshly
    // extracted TunnelVision.exe — detected by the absence of a `.installed`
    // marker file next to the exe. On completion:
    //   - Copies TunnelVision.exe into the chosen install path
    //   - Writes a `.installed` marker so the next launch skips this UI
    //   - Optionally creates desktop + Start Menu shortcuts
    //   - Optionally registers Run-on-startup
    //   - Launches the installed exe and exits this one.
    public class InstallerForm : Form
    {
        private readonly bool _isDark;
        private readonly string _sourceExePath;

        private TextBox _pathBox = null!;
        private ModernButton _browseBtn = null!;
        private ToggleSwitch _desktopShortcut = null!;
        private ToggleSwitch _startMenuShortcut = null!;
        private ToggleSwitch _runOnStartup = null!;
        private ModernButton _installBtn = null!;
        private ModernButton _cancelBtn = null!;
        private Label _statusLabel = null!;
        private PictureBox _logo = null!;

        public InstallerForm(string sourceExePath)
        {
            _sourceExePath = sourceExePath;
            _isDark = Theme.IsSystemDark();

            InitializeForm();
            BuildLayout();
        }

        private void InitializeForm()
        {
            this.Text = "Install Tunnel Vision";
            this.ClientSize = new Size(640, 560);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5f);
            this.DoubleBuffered = true;
            this.BackColor = _isDark ? Theme.Dark.Background : Theme.Light.Background;
            this.ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary;
            try { this.Icon = Icon.ExtractAssociatedIcon(_sourceExePath); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.TryApplyMica(this.Handle, _isDark);
            NativeMethods.TryApplyRoundedCorners(this.Handle, small: false);
        }

        private void BuildLayout()
        {
            // ========== Header (logo + title + tagline) ==========
            _logo = new PictureBox
            {
                Location = new Point(32, 32),
                Size = new Size(72, 72),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            try
            {
                string icoPath = Path.Combine(Path.GetDirectoryName(_sourceExePath) ?? "", "app.ico");
                if (File.Exists(icoPath))
                {
                    _logo.Image = new Icon(icoPath).ToBitmap();
                }
                else
                {
                    _logo.Image = Icon.ExtractAssociatedIcon(_sourceExePath)?.ToBitmap();
                }
            }
            catch { }

            var title = new Label
            {
                Text = "Install Tunnel Vision",
                Location = new Point(120, 40),
                AutoSize = true,
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary
            };

            var subtitle = new Label
            {
                Text = "Focus on what matters. Dim the rest.",
                Location = new Point(120, 76),
                AutoSize = true,
                ForeColor = _isDark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary,
                Font = new Font("Segoe UI", 10f)
            };

            // ========== Path selector ==========
            var pathLabel = new Label
            {
                Text = "Install location",
                Location = new Point(32, 150),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary
            };

            var pathHint = new Label
            {
                Text = "Tunnel Vision will live in this folder. You can change it anytime.",
                Location = new Point(32, 174),
                AutoSize = true,
                ForeColor = _isDark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary,
                Font = new Font("Segoe UI", 9f)
            };

            _pathBox = new TextBox
            {
                Location = new Point(32, 206),
                Size = new Size(460, 30),
                Font = new Font("Segoe UI", 10f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = _isDark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover,
                ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary,
                Text = DefaultInstallPath()
            };

            _browseBtn = new ModernButton("Browse…", _isDark)
            {
                Location = new Point(500, 205),
                Size = new Size(108, 32)
            };
            _browseBtn.Click += (s, e) => PickFolder();

            // ========== Options ==========
            var optionsLabel = new Label
            {
                Text = "Options",
                Location = new Point(32, 262),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = _isDark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary
            };

            _desktopShortcut = BuildSwitch("Create a desktop shortcut", 292, true);
            _startMenuShortcut = BuildSwitch("Add to Start menu", 332, true);
            _runOnStartup = BuildSwitch("Run Tunnel Vision when Windows starts", 372, false);

            // ========== Footer (status + buttons) ==========
            _statusLabel = new Label
            {
                Location = new Point(32, 445),
                Size = new Size(576, 40),
                Text = "",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Theme.Accent
            };

            _installBtn = new ModernButton("Install", _isDark, primary: true)
            {
                Location = new Point(490, 500),
                Size = new Size(120, 38)
            };
            _installBtn.Click += (s, e) => _ = RunInstallAsync();

            _cancelBtn = new ModernButton("Cancel", _isDark)
            {
                Location = new Point(360, 500),
                Size = new Size(120, 38)
            };
            _cancelBtn.Click += (s, e) => this.Close();

            // ========== Add controls ==========
            this.Controls.AddRange(new Control[]
            {
                _logo, title, subtitle,
                pathLabel, pathHint, _pathBox, _browseBtn,
                optionsLabel, _desktopShortcut, _startMenuShortcut, _runOnStartup,
                _statusLabel,
                _cancelBtn, _installBtn
            });

            // (ModernButton is not an IButtonControl — AcceptButton/CancelButton
            //  would require implementing that interface. Not worth the plumbing
            //  for this small dialog; keyboard users can Tab to the button.)
        }

        private ToggleSwitch BuildSwitch(string label, int y, bool initial)
        {
            var sw = new ToggleSwitch(_isDark)
            {
                Location = new Point(32, y),
                Size = new Size(440, 32),
                Text = label,
                Checked = initial
            };
            return sw;
        }

        private static string DefaultInstallPath()
        {
            // LocalAppData is writable without admin and is the conventional
            // per-user install root for portable apps on Windows.
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "TunnelVision");
        }

        private void PickFolder()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Choose a folder to install Tunnel Vision",
                UseDescriptionForTitle = true,
                InitialDirectory = _pathBox.Text
            };
            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                // Append TunnelVision subfolder if the user picked a parent root
                string chosen = dlg.SelectedPath;
                if (!chosen.EndsWith("TunnelVision", StringComparison.OrdinalIgnoreCase))
                {
                    chosen = Path.Combine(chosen, "TunnelVision");
                }
                _pathBox.Text = chosen;
            }
        }

        private void SetBusy(bool busy, string message)
        {
            _statusLabel.Text = message;
            _installBtn.Enabled = !busy;
            _cancelBtn.Enabled = !busy;
            _browseBtn.Enabled = !busy;
            _pathBox.Enabled = !busy;
            _desktopShortcut.Enabled = !busy;
            _startMenuShortcut.Enabled = !busy;
            _runOnStartup.Enabled = !busy;
        }

        private async Task RunInstallAsync()
        {
            string targetDir = (_pathBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                MessageBox.Show(this, "Please choose an install folder.", "Tunnel Vision",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                SetBusy(true, "Installing…");

                string targetExe = Path.Combine(targetDir, "TunnelVision.exe");

                await Task.Run(() =>
                {
                    Directory.CreateDirectory(targetDir);
                    File.Copy(_sourceExePath, targetExe, overwrite: true);

                    // Copy the icon next to the exe if we can find one.
                    string? sourceDir = Path.GetDirectoryName(_sourceExePath);
                    if (sourceDir != null)
                    {
                        string icoSrc = Path.Combine(sourceDir, "app.ico");
                        string icoDst = Path.Combine(targetDir, "app.ico");
                        if (File.Exists(icoSrc)) File.Copy(icoSrc, icoDst, overwrite: true);
                    }

                    // Write the marker file so the next launch skips the installer.
                    File.WriteAllText(Path.Combine(targetDir, ".installed"),
                        $"{DateTime.UtcNow:O}\n{Environment.UserName}\n");
                });

                // Shortcuts + startup — lightweight, stay on UI thread.
                if (_desktopShortcut.Checked)
                {
                    CreateShortcut(
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Tunnel Vision.lnk"),
                        targetExe, "Tunnel Vision — Focus on what matters");
                }
                if (_startMenuShortcut.Checked)
                {
                    string startMenu = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                        "Tunnel Vision.lnk");
                    CreateShortcut(startMenu, targetExe, "Tunnel Vision — Focus on what matters");
                }
                if (_runOnStartup.Checked)
                {
                    SetStartup(true, targetExe);
                }

                // Write an uninstall entry in the registry so the app shows up in
                // "Apps & features". The uninstall action just deletes the folder
                // and the Run key.
                WriteUninstallEntry(targetDir, targetExe);

                SetBusy(false, "Installed. Launching…");

                // Launch installed exe and quit this one.
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    UseShellExecute = true,
                    WorkingDirectory = targetDir
                });

                Application.Exit();
            }
            catch (Exception ex)
            {
                SetBusy(false, "");
                MessageBox.Show(this,
                    "Install failed: " + ex.Message,
                    "Tunnel Vision",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string description)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Description = description;
                shortcut.Save();
            }
            catch
            {
                // Non-critical. A user without COM/WScript access just won't get a shortcut.
            }
        }

        private static void SetStartup(bool enable, string exePath)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
                if (key == null) return;
                if (enable) key.SetValue("TunnelVision", exePath);
                else key.DeleteValue("TunnelVision", throwOnMissingValue: false);
            }
            catch { }
        }

        private static void WriteUninstallEntry(string installDir, string exePath)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\TunnelVision");
                key.SetValue("DisplayName", "Tunnel Vision");
                key.SetValue("DisplayIcon", exePath);
                key.SetValue("DisplayVersion", "1.1.0");
                key.SetValue("Publisher", "voidksa");
                key.SetValue("InstallLocation", installDir);
                key.SetValue("URLInfoAbout", "https://github.com/voidksa/TunnelVision");
                // Uninstall string: relaunch the exe with an --uninstall flag
                key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
