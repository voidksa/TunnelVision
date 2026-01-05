using System.Threading;

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

            ApplicationConfiguration.Initialize();
            Application.Run(new OverlayForm());
        }
    }
}
