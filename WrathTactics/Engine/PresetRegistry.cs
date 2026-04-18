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
        /// Writes any DefaultPresets whose ID is not already present on disk. Idempotent:
        /// user-deleted or user-edited defaults are left alone because the check is
        /// per-ID, and SeedDefaults only runs on fresh installs for missing IDs.
        /// </summary>
        static void SeedDefaults() {
            int seeded = 0;
            foreach (var preset in DefaultPresets.Build()) {
                if (presets.ContainsKey(preset.Id)) continue;
                PresetManager.Save(preset);
                presets[preset.Id] = preset;
                seeded++;
            }
            if (seeded > 0) Log.Persistence.Info($"Seeded {seeded} default preset(s)");
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

        public static void Save(TacticsRule preset) {
            if (preset == null || string.IsNullOrEmpty(preset.Id)) return;
            PresetManager.Save(preset);
            GetPresets()[preset.Id] = preset;
        }

        /// <summary>
        /// Deletes the preset file and removes every linked rule referencing it from the active config.
        /// </summary>
        public static void Delete(string presetId, TacticsConfig config) {
            if (string.IsNullOrEmpty(presetId)) return;
            PresetManager.Delete(presetId);
            GetPresets().Remove(presetId);

            int removed = 0;
            if (config != null) {
                removed += config.GlobalRules.RemoveAll(r => r.PresetId == presetId);
                foreach (var kv in config.CharacterRules)
                    removed += kv.Value.RemoveAll(r => r.PresetId == presetId);
            }
            Log.Persistence.Info($"Cascade-removed {removed} linked rule(s) for preset id={presetId}");
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
    }
}
