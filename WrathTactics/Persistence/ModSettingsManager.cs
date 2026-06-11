using System;
using System.IO;
using Newtonsoft.Json;
using WrathTactics.Logging;

namespace WrathTactics.Persistence {
    /// <summary>
    /// Machine-global mod settings — NOT per-save. TacticsConfig (ConfigManager) is
    /// keyed on GameId and reloads on every area load; device/UI preferences like the
    /// HUD button visibility belong here so they hold across saves and campaigns and
    /// can be written without an active game.
    /// </summary>
    public class ModSettings {
        [JsonProperty] public bool ShowHudButton { get; set; } = true;
    }

    public static class ModSettingsManager {
        static string SettingsPath => Path.Combine(Main.ModPath, "UserSettings", "settings.json");
        static ModSettings current;

        public static ModSettings Current {
            get {
                if (current == null) Load();
                return current;
            }
        }

        static void Load() {
            try {
                if (File.Exists(SettingsPath)) {
                    current = JsonConvert.DeserializeObject<ModSettings>(File.ReadAllText(SettingsPath))
                        ?? new ModSettings();
                    Log.Persistence.Info($"Loaded mod settings from {SettingsPath}");
                    return;
                }
            } catch (Exception ex) {
                Log.Persistence.Error(ex, "Failed to read mod settings — using defaults for this session, NOT overwriting.");
            }
            current = new ModSettings();
        }

        public static bool Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(Current, Formatting.Indented));
                Log.Persistence.Debug($"Saved mod settings to {SettingsPath}");
                return true;
            } catch (Exception ex) {
                Log.Persistence.Error(ex, "Failed to save mod settings");
                return false;
            }
        }
    }
}
