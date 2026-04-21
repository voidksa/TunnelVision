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

        // Intensity step size (percentage points per adjustment)
        public int IntensityStep { get; set; } = 5;

        // Show an on-screen indicator when intensity changes
        public bool ShowOsdOnChange { get; set; } = true;

        // Primary Hotkey: Toggle Pause/Resume
        // Default: Ctrl (2) + Alt (1) = 3, Key: T
        public int HotkeyModifiers { get; set; } = 3;
        public int HotkeyKey { get; set; } = (int)ConsoleKey.T;

        // Increase intensity hotkey (Ctrl+Alt+Up = 38)
        public int IncreaseHotkeyModifiers { get; set; } = 3;
        public int IncreaseHotkeyKey { get; set; } = 38; // Keys.Up

        // Decrease intensity hotkey (Ctrl+Alt+Down = 40)
        public int DecreaseHotkeyModifiers { get; set; } = 3;
        public int DecreaseHotkeyKey { get; set; } = 40; // Keys.Down

        // Open/close settings window hotkey (Ctrl+Alt+S = 83)
        public int SettingsHotkeyModifiers { get; set; } = 3;
        public int SettingsHotkeyKey { get; set; } = 83; // Keys.S

        // New in 1.1.0: visual effects
        public bool BlurBackground { get; set; } = false;

        // Tint color (ARGB packed). Default = pure black.
        public int TintColorArgb { get; set; } = unchecked((int)0xFF000000);

        // When true, pause dimming if the foreground window covers the whole screen
        // (fullscreen games, videos, presentations).
        public bool PauseInFullscreen { get; set; } = true;

        // Update checker
        public string SkippedVersion { get; set; } = "";
        public bool AutoCheckUpdates { get; set; } = true;

        public bool IsFirstRun { get; set; } = true;

        // Tracks the app version that last ran with this config file.
        // When it differs from the current build we can show a "What's new" balloon.
        public string LastRunVersion { get; set; } = "";

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
