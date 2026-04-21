using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace TunnelVision
{
    internal static class Program
    {
        private static Mutex? _mutex = null;

        [STAThread]
        static void Main()
        {
            const string appName = "TunnelVision_Unique_App_Mutex";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Tunnel Vision is already running.\nCheck the System Tray (near the clock).", "Tunnel Vision", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Global exception handlers — log to file + friendly dialog with stack trace
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => HandleException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => HandleException(e.ExceptionObject as Exception);

            ApplicationConfiguration.Initialize();
            Application.Run(new OverlayForm());
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
                    "Tunnel Vision — Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch { /* absolutely last-resort: swallow so we don't recurse */ }
        }
    }
}
