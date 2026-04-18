using System;
using System.Collections.Generic;
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
            if (path == null || !File.Exists(path)) {
                current = new TacticsConfig();
                Log.Persistence.Info("No existing config, using defaults");
                return;
            }

            try {
                var json = File.ReadAllText(path);
                var settings = new JsonSerializerSettings();
                settings.Converters.Add(new SafeConditionConverter());
                current = JsonConvert.DeserializeObject<TacticsConfig>(json, settings) ?? new TacticsConfig();
                Log.Persistence.Info($"Loaded config from {path}");

                if (CleanupInvalidRules(current)) {
                    Log.Persistence.Warn("Some rules referenced removed condition properties — saving cleaned config.");
                    Save();
                }
            } catch (JsonException ex) {
                Log.Persistence.Error(ex, "Failed to deserialize config — resetting to defaults and overwriting file.");
                current = new TacticsConfig();
                Save();
            } catch (Exception ex) {
                Log.Persistence.Error(ex, "Failed to read config file — using defaults for this session, NOT overwriting.");
                current = new TacticsConfig();
            }
        }

        /// <summary>
        /// Walks the loaded config and strips null conditions / empty groups / empty rules
        /// that the SafeConditionConverter produced for un-parseable entries.
        /// Returns true if anything was removed (caller should re-save).
        /// </summary>
        static bool CleanupInvalidRules(TacticsConfig config) {
            if (config == null) return false;
            bool changed = false;

            foreach (var rule in EnumerateAllRules(config)) {
                if (rule == null) continue;
                if (rule.ConditionGroups == null) continue;
                for (int g = rule.ConditionGroups.Count - 1; g >= 0; g--) {
                    var grp = rule.ConditionGroups[g];
                    if (grp?.Conditions == null) {
                        rule.ConditionGroups.RemoveAt(g);
                        changed = true;
                        continue;
                    }
                    int before = grp.Conditions.Count;
                    grp.Conditions.RemoveAll(c => c == null);
                    if (grp.Conditions.Count != before) changed = true;
                    if (grp.Conditions.Count == 0) {
                        rule.ConditionGroups.RemoveAt(g);
                        changed = true;
                    }
                }
            }

            RemoveRulesWithNoGroups(config, ref changed);
            return changed;
        }

        static IEnumerable<TacticsRule> EnumerateAllRules(TacticsConfig config) {
            if (config.GlobalRules != null)
                foreach (var r in config.GlobalRules) yield return r;
            if (config.CharacterRules != null)
                foreach (var kv in config.CharacterRules)
                    if (kv.Value != null)
                        foreach (var r in kv.Value) yield return r;
        }

        static void RemoveRulesWithNoGroups(TacticsConfig config, ref bool changed) {
            if (config.GlobalRules != null) {
                int before = config.GlobalRules.Count;
                config.GlobalRules.RemoveAll(r => r.ConditionGroups == null || r.ConditionGroups.Count == 0);
                if (config.GlobalRules.Count != before) changed = true;
            }
            if (config.CharacterRules != null) {
                foreach (var kv in config.CharacterRules) {
                    if (kv.Value == null) continue;
                    int before = kv.Value.Count;
                    kv.Value.RemoveAll(r => r.ConditionGroups == null || r.ConditionGroups.Count == 0);
                    if (kv.Value.Count != before) changed = true;
                }
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
