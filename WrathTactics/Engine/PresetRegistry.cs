using System.Collections.Generic;
using System.Linq;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.Engine {
    /// <summary>
    /// In-memory cache of preset rules keyed by PresetId. Owns resolution (Resolve),
    /// persistence forwarding (Save/Delete), and cascade cleanup when a preset is removed.
    /// </summary>
    public static class PresetRegistry {
        static Dictionary<string, TacticsRule> presets;

        public static void Reload() {
            presets = PresetManager.LoadAll().ToDictionary(r => r.Id, r => r);
            Log.Persistence.Info($"PresetRegistry loaded {presets.Count} presets");
            SeedDefaults();
        }

        /// <summary>
        /// Writes any DefaultPresets whose ID has never been seeded before. Uses a sentinel
        /// file {ModPath}/Presets/.seeded-defaults to track seeded IDs persistently, so:
        ///   - First install: all defaults written, all IDs added to sentinel.
        ///   - User deletes a default: ID is still in sentinel → skip on next load.
        ///   - New mod version adds default #7: ID not in sentinel → seed once.
        ///   - User edits a default: file still present, ID in sentinel → skip.
        /// </summary>
        static void SeedDefaults() {
            var presetDir = System.IO.Path.Combine(Main.ModPath, "Presets");
            var sentinelPath = System.IO.Path.Combine(presetDir, ".seeded-defaults");

            var seeded = new System.Collections.Generic.HashSet<string>();
            if (System.IO.File.Exists(sentinelPath)) {
                foreach (var line in System.IO.File.ReadAllLines(sentinelPath)) {
                    var id = line?.Trim();
                    if (!string.IsNullOrEmpty(id)) seeded.Add(id);
                }
            }

            int newSeeds = 0;
            foreach (var preset in DefaultPresets.Build()) {
                if (seeded.Contains(preset.Id)) continue;
                // If ID isn't in the sentinel but the file is already on disk (upgrade from
                // pre-sentinel version), we skip Save and just mark it seeded. One-time risk:
                // a default the user deleted before the sentinel existed will re-seed once.
                if (!presets.ContainsKey(preset.Id)) {
                    PresetManager.Save(preset);
                    presets[preset.Id] = preset;
                    newSeeds++;
                }
                seeded.Add(preset.Id);
            }

            try {
                System.IO.Directory.CreateDirectory(presetDir);
                System.IO.File.WriteAllLines(sentinelPath, seeded);
            } catch (System.Exception ex) {
                Log.Persistence.Error(ex, $"Failed to write default-seed sentinel at {sentinelPath}");
            }

            if (newSeeds > 0) Log.Persistence.Info($"Seeded {newSeeds} default preset(s)");
        }

        static Dictionary<string, TacticsRule> GetPresets() {
            if (presets == null) Reload();
            return presets;
        }

        public static IReadOnlyList<TacticsRule> All() {
            return GetPresets().Values
                .OrderBy(r => r.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static TacticsRule Get(string presetId) {
            if (string.IsNullOrEmpty(presetId)) return null;
            GetPresets().TryGetValue(presetId, out var rule);
            return rule;
        }

        /// <summary>
        /// For a rule that may be linked (PresetId != null), returns the preset's
        /// current authoritative data. For standalone rules, returns the rule itself.
        /// Dangling links (PresetId set but preset gone) fall back to the rule's own data.
        /// </summary>
        public static TacticsRule Resolve(TacticsRule rule) {
            if (rule == null || string.IsNullOrEmpty(rule.PresetId)) return rule;
            var preset = Get(rule.PresetId);
            return preset ?? rule;
        }

        /// <summary>
        /// Returns true on successful disk write. The in-memory dict is updated regardless so
        /// the UI reflects what the user just typed even if the file write failed — callers
        /// must surface a false return to the user so they know the change isn't persisted.
        /// </summary>
        public static bool Save(TacticsRule preset) {
            if (preset == null || string.IsNullOrEmpty(preset.Id)) return false;
            bool ok = PresetManager.Save(preset);
            GetPresets()[preset.Id] = preset;
            return ok;
        }

        /// <summary>
        /// Deletes the preset file and removes every linked rule referencing it from the active
        /// config. Returns true iff the file removal succeeded — cascade cleanup runs either way
        /// so the in-memory state is consistent even when the filesystem write failed.
        /// </summary>
        public static bool Delete(string presetId, TacticsConfig config) {
            if (string.IsNullOrEmpty(presetId)) return false;
            bool ok = PresetManager.Delete(presetId);
            GetPresets().Remove(presetId);

            int removed = 0;
            if (config != null) {
                removed += config.GlobalRules.RemoveAll(r => r.PresetId == presetId);
                foreach (var kv in config.CharacterRules)
                    removed += kv.Value.RemoveAll(r => r.PresetId == presetId);
            }
            Log.Persistence.Info($"Cascade-removed {removed} linked rule(s) for preset id={presetId}");
            return ok;
        }

        /// <summary>
        /// Materializes the preset's current logic into the rule and clears PresetId.
        /// Call immediately before allowing any user edit on a linked rule.
        /// </summary>
        public static void BreakLink(TacticsRule rule) {
            if (rule == null || string.IsNullOrEmpty(rule.PresetId)) return;
            var preset = Get(rule.PresetId);
            if (preset != null) {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(preset);
                var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<TacticsRule>(json);
                rule.Name = copy.Name;
                rule.CooldownRounds = copy.CooldownRounds;
                rule.ConditionGroups = copy.ConditionGroups;
                rule.Action = copy.Action;
                rule.Target = copy.Target;
            }
            rule.PresetId = null;
        }

        /// <summary>
        /// Promotes a standalone rule into a new preset and links the original rule to it.
        /// The original rule's body (ConditionGroups / Action / Target) is cleared — all
        /// future reads go through Resolve() to the preset. Caller must ConfigManager.Save
        /// and rebuild the affected UI after this returns.
        /// </summary>
        public static TacticsRule PromoteRuleToPreset(TacticsRule rule) {
            if (rule == null) return null;
            if (!string.IsNullOrEmpty(rule.PresetId)) {
                Log.Persistence.Warn("PromoteRuleToPreset called on an already-linked rule — ignored");
                return Get(rule.PresetId);
            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(rule);
            var preset = Newtonsoft.Json.JsonConvert.DeserializeObject<TacticsRule>(json);
            preset.Id = System.Guid.NewGuid().ToString();
            preset.PresetId = null;
            // Atomic: if the save fails we leave the source rule untouched rather than
            // mutating it into a linked state pointing at a non-existent preset.
            if (!Save(preset)) {
                Log.Persistence.Error($"PromoteRuleToPreset: save failed for new preset id={preset.Id}, leaving source rule intact");
                return null;
            }

            rule.PresetId = preset.Id;
            rule.ConditionGroups = new System.Collections.Generic.List<Models.ConditionGroup>();
            rule.Action = new Models.ActionDef();
            rule.Target = new Models.TargetDef();
            return preset;
        }
    }
}
