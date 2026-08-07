using System;
using System.Collections.Generic;
using System.Linq;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.Engine {
    /// <summary>
    /// In-memory cache of rule packs keyed by pack id, plus the pure planning logic that
    /// turns a pack into rules. Mirrors PresetRegistry: the dict is updated even when the
    /// disk write fails so the UI reflects what the user just did — callers must surface
    /// a false return.
    /// </summary>
    public static class PackRegistry {
        static Dictionary<string, TacticsPack> packs;

        public static void Reload() {
            packs = PackManager.LoadAll().ToDictionary(p => p.Id, p => p);
            Log.Persistence.Info($"PackRegistry loaded {packs.Count} packs");
        }

        static Dictionary<string, TacticsPack> GetPacks() {
            if (packs == null) Reload();
            return packs;
        }

        public static IReadOnlyList<TacticsPack> All() {
            return GetPacks().Values
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static TacticsPack Get(string packId) {
            if (string.IsNullOrEmpty(packId)) return null;
            GetPacks().TryGetValue(packId, out var pack);
            return pack;
        }

        public static bool Save(TacticsPack pack) {
            if (pack == null || string.IsNullOrEmpty(pack.Id)) return false;
            bool ok = PackManager.Save(pack);
            GetPacks()[pack.Id] = pack;
            return ok;
        }

        /// <summary>
        /// Deletes the pack definition. Rules already applied from it are deliberately left
        /// in place — deleting an organisational label must not wipe a character's setup.
        /// Their PackId dangles, which only costs them the colour tint.
        /// </summary>
        public static bool Delete(string packId) {
            if (string.IsNullOrEmpty(packId)) return false;
            bool ok = PackManager.Delete(packId);
            GetPacks().Remove(packId);
            return ok;
        }

        /// <summary>
        /// Removes a deleted preset from every pack that lists it and persists the changes.
        /// Called from PresetRegistry.Delete so a pack can never carry an unresolvable member.
        /// </summary>
        public static void RemovePresetFromPacks(string presetId) {
            var changed = StripPreset(GetPacks().Values.ToList(), presetId);
            foreach (var pack in changed) PackManager.Save(pack);
            if (changed.Count > 0)
                Log.Persistence.Info($"Stripped preset id={presetId} from {changed.Count} pack(s)");
        }

        // --- Pure logic (no disk, no Game API) ---

        /// <summary>
        /// Returns the rules to append so <paramref name="pack"/> is fully represented in
        /// <paramref name="existing"/>. Members already present *from this pack* are skipped,
        /// which makes re-applying a sync rather than a duplicate. Members present from a
        /// different pack (or as a hand-made link) are still added: each pack owns its own
        /// rules so removing one pack never strips another's.
        /// Never mutates <paramref name="existing"/>.
        /// </summary>
        public static List<TacticsRule> PlanApply(TacticsPack pack, List<TacticsRule> existing,
            Func<string, bool> presetExists) {

            var plan = new List<TacticsRule>();
            if (pack?.PresetIds == null) return plan;

            var alreadyFromThisPack = new HashSet<string>(
                (existing ?? new List<TacticsRule>())
                    .Where(r => r != null && r.PackId == pack.Id && !string.IsNullOrEmpty(r.PresetId))
                    .Select(r => r.PresetId));

            foreach (var presetId in pack.PresetIds) {
                if (string.IsNullOrEmpty(presetId)) continue;
                if (alreadyFromThisPack.Contains(presetId)) continue;
                if (presetExists != null && !presetExists(presetId)) {
                    Log.Persistence.Warn($"Pack '{pack.Name}': member preset id={presetId} no longer exists — skipped");
                    continue;
                }
                // Body stays empty by design: PresetRegistry.Resolve supplies conditions,
                // action and target at evaluation time.
                plan.Add(new TacticsRule {
                    Id = Guid.NewGuid().ToString(),
                    Enabled = true,
                    PresetId = presetId,
                    PackId = pack.Id,
                });
                alreadyFromThisPack.Add(presetId);
            }
            return plan;
        }

        /// <summary>
        /// How many of the pack's members are already present as rules of THIS pack in
        /// <paramref name="rules"/>. Same membership test PlanApply uses to skip them, so the
        /// two numbers add up — a member skipped for any other reason (unresolvable preset,
        /// empty or duplicate id) is deliberately NOT counted here.
        /// </summary>
        public static int CountAlreadyApplied(TacticsPack pack, List<TacticsRule> rules) {
            if (pack?.PresetIds == null || rules == null) return 0;
            var fromThisPack = new HashSet<string>(
                rules.Where(r => r != null && r.PackId == pack.Id && !string.IsNullOrEmpty(r.PresetId))
                     .Select(r => r.PresetId));
            int count = 0;
            var counted = new HashSet<string>();
            foreach (var presetId in pack.PresetIds) {
                if (string.IsNullOrEmpty(presetId)) continue;
                if (!fromThisPack.Contains(presetId)) continue;
                if (!counted.Add(presetId)) continue;   // a duplicated member id is one slot, not two
                count++;
            }
            return count;
        }

        /// <summary>Distinct pack ids present in a rule list, in first-appearance order.</summary>
        public static List<string> AppliedPackIds(List<TacticsRule> rules) {
            var result = new List<string>();
            if (rules == null) return result;
            var seen = new HashSet<string>();
            foreach (var rule in rules) {
                if (rule == null || string.IsNullOrEmpty(rule.PackId)) continue;
                if (seen.Add(rule.PackId)) result.Add(rule.PackId);
            }
            return result;
        }

        /// <summary>Removes a preset id from all packs; returns the packs that changed.</summary>
        public static List<TacticsPack> StripPreset(List<TacticsPack> allPacks, string presetId) {
            var changed = new List<TacticsPack>();
            if (allPacks == null || string.IsNullOrEmpty(presetId)) return changed;
            foreach (var pack in allPacks) {
                if (pack?.PresetIds == null) continue;
                if (pack.PresetIds.RemoveAll(id => id == presetId) > 0) changed.Add(pack);
            }
            return changed;
        }
    }
}
