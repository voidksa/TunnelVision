using System;
using System.IO;
using System.Text.Json;

namespace TunnelVision
{
    public class AppSettings
    {
        public double Opacity { get; set; } = 0.8;
        public bool RunOnStartup { get; set; } = false;
        public bool SmoothMovement { get; set; } = true;

        // Hotkey Settings
        // Default: Ctrl (2) + Alt (1) = 3, Key: T
        public int HotkeyModifiers { get; set; } = 3;
        public int HotkeyKey { get; set; } = (int)ConsoleKey.T; // Using int to avoid dependency issues, but cast to Keys later

        public bool IsFirstRun { get; set; } = true;

        private static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
