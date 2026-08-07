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
            foreach (var pack in changed) {
                // No UI path surfaces this call's failures — a bad write here leaves the
                // in-memory pack shorter than the file on disk, so the stripped member
                // silently reappears on next load. The log is the only trace available.
                if (!PackManager.Save(pack))
                    Log.Persistence.Warn($"Failed to persist pack '{pack.Name}' (id={pack.Id}) after stripping preset id={presetId} — the member may reappear on next load");
            }
            if (changed.Count > 0)
                Log.Persistence.Info($"Stripped preset id={presetId} from {changed.Count} pack(s)");
        }

        // --- Pure logic (no disk, no Game API) ---

        /// <summary>
        /// Returns the rules to append so <paramref name="pack"/> is fully represented in
        /// <paramref name="existing"/>. Members already present as a preset-linked rule —
        /// from this pack, from another pack, or hand-built — are skipped, which makes
        /// re-applying (or applying a second pack sharing members) a sync rather than a
        /// duplicate spam.
        /// Never mutates <paramref name="existing"/>.
        /// </summary>
        public static List<TacticsRule> PlanApply(TacticsPack pack, List<TacticsRule> existing,
            Func<string, bool> presetExists) {

            var plan = new List<TacticsRule>();
            if (pack?.PresetIds == null) return plan;

            // Preset-based dedup: one rule per preset per list, whatever pack asks for it.
            // The old key was PackId+PresetId, which let two packs sharing a member each insert
            // their own copy — and since every "Save List as Pack" mints a fresh pack id over the
            // same presets, re-saving and re-applying spammed the list.
            // Trade-off, deliberately accepted: a preset shared by two packs now exists as ONE
            // rule, carrying the PackId of whichever pack got there first. The second pack shows
            // no chip, and "delete this pack's rules" on the first pack removes a rule the second
            // one also lists. "Remove pack marking" (detach) is the non-destructive alternative,
            // which is why the chip offers both.
            var alreadyLinked = new HashSet<string>(
                (existing ?? new List<TacticsRule>())
                    .Where(r => r != null && !string.IsNullOrEmpty(r.PresetId))
                    .Select(r => r.PresetId));

            foreach (var presetId in pack.PresetIds) {
                if (string.IsNullOrEmpty(presetId)) continue;
                if (alreadyLinked.Contains(presetId)) continue;
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
                alreadyLinked.Add(presetId);
            }
            return plan;
        }

        /// <summary>
        /// How many of the pack's members are already present as a preset-linked rule in
        /// <paramref name="rules"/>, regardless of which pack (if any) that rule belongs to.
        /// Same membership test PlanApply uses to skip them, so the two numbers add up — a
        /// member skipped for any other reason (unresolvable preset, empty or duplicate id)
        /// is deliberately NOT counted here.
        /// </summary>
        public static int CountAlreadyApplied(TacticsPack pack, List<TacticsRule> rules) {
            if (pack?.PresetIds == null || rules == null) return 0;
            // Same membership rule as PlanApply — if these two disagree, added + already-present
            // no longer sums to the pack's member count and the status message misreports again.
            // A preset counted here may actually be owned by a different pack (see PlanApply's
            // trade-off comment) — "already applied" does not mean "already applied by THIS pack".
            var alreadyLinked = new HashSet<string>(
                rules.Where(r => r != null && !string.IsNullOrEmpty(r.PresetId))
                     .Select(r => r.PresetId));
            int count = 0;
            var counted = new HashSet<string>();
            foreach (var presetId in pack.PresetIds) {
                if (string.IsNullOrEmpty(presetId)) continue;
                if (!alreadyLinked.Contains(presetId)) continue;
                if (!counted.Add(presetId)) continue;   // a duplicated member id is one slot, not two
                count++;
            }
            return count;
        }

        /// <summary>
        /// Re-stamps this pack's PackId onto rules that link one of its member presets and
        /// belong to no pack. Makes "remove pack marking" reversible: re-applying the pack
        /// re-adopts the rules it left behind, instead of finding them already linked (and so
        /// skipped by the preset-based dedup) and reporting "already fully applied" forever.
        /// Only touches rules with a null/empty PackId, so it can never take a rule from
        /// another pack. Returns how many it adopted.
        /// </summary>
        public static int AdoptUnownedMembers(TacticsPack pack, List<TacticsRule> rules) {
            if (pack?.PresetIds == null || rules == null) return 0;

            var memberIds = new HashSet<string>(pack.PresetIds.Where(id => !string.IsNullOrEmpty(id)));
            int adopted = 0;
            foreach (var rule in rules) {
                if (rule == null) continue;
                if (!string.IsNullOrEmpty(rule.PackId)) continue;
                if (string.IsNullOrEmpty(rule.PresetId) || !memberIds.Contains(rule.PresetId)) continue;
                rule.PackId = pack.Id;
                adopted++;
            }
            return adopted;
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
