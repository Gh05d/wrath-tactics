using System;
using System.IO;
using Kingmaker;
using Newtonsoft.Json;
using WrathTactics.Models;

namespace WrathTactics.Persistence {
    public static class ConfigManager {
        static string ConfigDir => Path.Combine(Main.ModPath, "UserSettings");
        static TacticsConfig current;

        public static TacticsConfig Current {
            get {
                if (current == null) Load();
                return current;
            }
        }

        static string GetConfigPath() {
            var gameId = Game.Instance?.Player?.GameId;
            if (string.IsNullOrEmpty(gameId)) return null;
            return Path.Combine(ConfigDir, $"tactics-{gameId}.json");
        }

        public static void Load() {
            var path = GetConfigPath();
            if (path != null && File.Exists(path)) {
                try {
                    var json = File.ReadAllText(path);
                    current = JsonConvert.DeserializeObject<TacticsConfig>(json);
                    Main.Log($"[Config] Loaded config from {path}");
                } catch (Exception ex) {
                    Main.Error(ex, "[Config] Failed to load config, using defaults");
                    current = new TacticsConfig();
                }
            } else {
                current = new TacticsConfig();
                Main.Log("[Config] No existing config, using defaults");
            }
        }

        public static void Save() {
            var path = GetConfigPath();
            if (path == null) {
                Main.Error("[Config] Cannot save — no active game");
                return;
            }

            try {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonConvert.SerializeObject(current, Formatting.Indented);
                File.WriteAllText(path, json);
                Main.Debug($"[Config] Saved config to {path}");
            } catch (Exception ex) {
                Main.Error(ex, "[Config] Failed to save config");
            }
        }

        public static void Reset() {
            current = null;
        }
    }
}
