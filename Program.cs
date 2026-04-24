using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TunnelVision
{
    internal static class Program
    {
        private static Mutex? _mutex = null;

        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();

            // Global exception handlers
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => HandleException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => HandleException(e.ExceptionObject as Exception);

            // ============ Flag routing ============
            // --uninstall  → remove shortcuts, registry, files, exit
            // --install    → force installer UI even if already installed
            // (none)       → installer if no `.installed` marker exists, otherwise overlay

            if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                RunUninstall();
                return;
            }

            bool forceInstall = args.Any(a => a.Equals("--install", StringComparison.OrdinalIgnoreCase));
            string exePath = Application.ExecutablePath;
            string exeDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
            string markerPath = Path.Combine(exeDir, ".installed");

            if (forceInstall || !File.Exists(markerPath))
            {
                // Installer owns the message loop here. When install completes it
                // launches the installed exe and calls Application.Exit.
                Application.Run(new InstallerForm(exePath));
                return;
            }

            // ============ Normal overlay path ============
            const string appName = "TunnelVision_Unique_App_Mutex";
            _mutex = new Mutex(true, appName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "Tunnel Vision is already running.\nCheck the System Tray (near the clock).",
                    "Tunnel Vision", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.Run(new OverlayForm());
        }

        private static void RunUninstall()
        {
            var confirm = MessageBox.Show(
                "Remove Tunnel Vision from this computer?\n\n" +
                "This deletes the installed files, desktop and Start Menu shortcuts, and the startup entry.",
                "Uninstall Tunnel Vision",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            string exePath = Application.ExecutablePath;
            string installDir = Path.GetDirectoryName(exePath) ?? "";

            // Kill any running overlay instance first so the exe isn't locked.
            try
            {
                foreach (var p in Process.GetProcessesByName("TunnelVision"))
                {
                    if (p.Id != Environment.ProcessId)
                    {
                        try { p.Kill(); p.WaitForExit(3000); } catch { }
                    }
                }
            }
            catch { }

            // Remove startup entry
            try
            {
                using RegistryKey? run = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
                run?.DeleteValue("TunnelVision", throwOnMissingValue: false);
            }
            catch { }

            // Remove uninstall registry entry
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\TunnelVision", throwOnMissingSubKey: false);
            }
            catch { }

            // Remove shortcuts
            try
            {
                string desktop = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Tunnel Vision.lnk");
                if (File.Exists(desktop)) File.Delete(desktop);
            }
            catch { }
            try
            {
                string startMenu = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Tunnel Vision.lnk");
                if (File.Exists(startMenu)) File.Delete(startMenu);
            }
            catch { }

            // Schedule self-deletion: spawn a cmd that waits a second and deletes our
            // install folder after we exit. Can't delete our own exe from here.
            if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
            {
                try
                {
                    string cmd = $"/C ping 127.0.0.1 -n 2 > nul & rmdir /S /Q \"{installDir}\"";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = cmd,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch { }
            }

            MessageBox.Show("Tunnel Vision has been uninstalled.", "Tunnel Vision",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void HandleException(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");

                MessageBox.Show(
                    $"{ex.GetType().Name}: {ex.Message}\n\nDetails saved to crash.log next to the executable.",
                    "Tunnel Vision — Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }
    }
}
