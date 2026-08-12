using System;
using System.IO;
using System.Text.Json;

namespace SwiftPanel.Services
{
    public static class SettingsService
    {
        private static readonly JsonSerializerOptions s_opts = new()
        {
            WriteIndented    = true,
            PropertyNameCaseInsensitive = true
        };

        private static string SettingsPath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SwiftPanel");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
        }

        /// <summary>Load settings from disk. Returns defaults if file missing or corrupt.</summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json, s_opts) ?? new AppSettings();
                }
            }
            catch { /* corrupt file → fall back to defaults */ }
            return new AppSettings();
        }

        /// <summary>Save settings to disk silently (never throws).</summary>
        public static void Save(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, s_opts);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
