using System;
using System.IO;
using Kingmaker;
using Newtonsoft.Json;
using WrathTactics.Logging;
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
                    Log.Persistence.Info($"Loaded config from {path}");
                } catch (Exception ex) {
                    Log.Persistence.Error(ex, "Failed to load config, using defaults");
                    current = new TacticsConfig();
                }
            } else {
                current = new TacticsConfig();
                Log.Persistence.Info("No existing config, using defaults");
            }
        }

        public static void Save() {
            var path = GetConfigPath();
            if (path == null) {
                Log.Persistence.Warn("Cannot save — no active game");
                return;
            }

            try {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonConvert.SerializeObject(current, Formatting.Indented);
                File.WriteAllText(path, json);
                Log.Persistence.Debug($"Saved config to {path}");
            } catch (Exception ex) {
                Log.Persistence.Error(ex, "Failed to save config");
            }
        }

        public static void Reset() {
            current = null;
        }
    }
}
