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
        /// that the SafeConditionConverter produced for un-parseable entries. Each strip
        /// emits a WARN so user reports of "my rule disappeared" can be triaged from the
        /// log alone (rule Name + Id + which step removed it).
        /// Returns true if anything was removed (caller should re-save).
        /// </summary>
        static bool CleanupInvalidRules(TacticsConfig config) {
            if (config == null) return false;
            bool changed = false;

            if (config.GlobalRules != null) {
                foreach (var rule in config.GlobalRules)
                    changed |= CleanupRuleGroups(rule, "Global");
            }
            if (config.CharacterRules != null) {
                foreach (var kv in config.CharacterRules) {
                    if (kv.Value == null) continue;
                    string scope = $"Character[{kv.Key}]";
                    foreach (var rule in kv.Value)
                        changed |= CleanupRuleGroups(rule, scope);
                }
            }

            RemoveRulesWithNoGroups(config, ref changed);
            return changed;
        }

        static bool CleanupRuleGroups(TacticsRule rule, string scope) {
            if (rule == null) return false;
            if (rule.ConditionGroups == null) return false;
            bool changed = false;
            string tag = $"{scope} rule '{rule.Name}' (id={rule.Id})";
            for (int g = rule.ConditionGroups.Count - 1; g >= 0; g--) {
                var grp = rule.ConditionGroups[g];
                if (grp?.Conditions == null) {
                    Log.Persistence.Warn($"Cleanup: {tag} — removing group {g} (group or its Conditions list was null)");
                    rule.ConditionGroups.RemoveAt(g);
                    changed = true;
                    continue;
                }
                int before = grp.Conditions.Count;
                grp.Conditions.RemoveAll(c => c == null);
                int dropped = before - grp.Conditions.Count;
                if (dropped > 0) {
                    Log.Persistence.Warn($"Cleanup: {tag} — dropped {dropped} null condition(s) from group {g} (SafeConditionConverter returned null, likely an unknown enum value in the JSON)");
                    changed = true;
                }
                if (grp.Conditions.Count == 0) {
                    Log.Persistence.Warn($"Cleanup: {tag} — removing group {g} (all conditions were dropped or it loaded empty)");
                    rule.ConditionGroups.RemoveAt(g);
                    changed = true;
                }
            }
            return changed;
        }

        static void RemoveRulesWithNoGroups(TacticsConfig config, ref bool changed) {
            // Linked rules (PresetId set) legitimately carry an empty body — their
            // ConditionGroups/Action/Target are resolved from the preset at runtime.
            // Only strip standalone rules with no groups.
            if (config.GlobalRules != null) {
                for (int i = config.GlobalRules.Count - 1; i >= 0; i--) {
                    var r = config.GlobalRules[i];
                    if (string.IsNullOrEmpty(r.PresetId) &&
                        (r.ConditionGroups == null || r.ConditionGroups.Count == 0)) {
                        Log.Persistence.Warn($"Cleanup: stripping Global rule '{r.Name}' (id={r.Id}) — no condition groups and no preset link");
                        config.GlobalRules.RemoveAt(i);
                        changed = true;
                    }
                }
            }
            if (config.CharacterRules != null) {
                foreach (var kv in config.CharacterRules) {
                    if (kv.Value == null) continue;
                    for (int i = kv.Value.Count - 1; i >= 0; i--) {
                        var r = kv.Value[i];
                        if (string.IsNullOrEmpty(r.PresetId) &&
                            (r.ConditionGroups == null || r.ConditionGroups.Count == 0)) {
                            Log.Persistence.Warn($"Cleanup: stripping Character[{kv.Key}] rule '{r.Name}' (id={r.Id}) — no condition groups and no preset link");
                            kv.Value.RemoveAt(i);
                            changed = true;
                        }
                    }
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
