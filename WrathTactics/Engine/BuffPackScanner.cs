using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    /// <summary>
    /// One-time, chunked full scan of the blueprint pack that force-loads every
    /// BlueprintBuff into the BlueprintsCache, so the HasBuff/MissingBuff picker lists
    /// ALL buffs — not just the lazily-loaded subset.
    ///
    /// Why this exists (verified via IL): <c>BlueprintsCache.ForEachLoaded</c> iterates
    /// <c>m_LoadedBlueprints</c> and passes <c>entry.Blueprint</c>, which stays null until
    /// something references the buff. So buffs like Bane / Slow / Studied Target /
    /// Negative Level only showed up after a unit applied them mid-session. Force-loading
    /// every index entry closes that gap.
    ///
    /// Cost: ALL ~75k blueprints become resident for the session. Eviction is NOT an
    /// option — <c>RemoveCachedBlueprint</c> drops the index Offset entirely, breaking
    /// future lazy loads. That cost is why the result is persisted (<see cref="BuffIndexCache"/>)
    /// and the scan runs only when the disk cache is missing/stale.
    ///
    /// Runs on the main thread, spread across frames (<see cref="Pump"/> from
    /// Main.OnUpdate) to avoid a multi-second freeze. SimpleBlueprint is a plain POCO
    /// loaded under a Monitor lock, but per-blueprint post-load hooks are not verified
    /// thread-safe — hence main thread, not a background task.
    /// </summary>
    public static class BuffPackScanner {
        // Blueprints force-loaded per frame. The scan only runs once per version/locale
        // (then it's a disk-cache hit), so the exact value is non-critical. Higher =
        // faster completion, more per-frame cost.
        const int PerFrameBudget = 250;

        static Queue<BlueprintGuid> pending;
        static bool started;
        static bool completed;
        static int loaded;

        public static bool Completed => completed;
        public static bool InProgress => started && !completed;

        /// <summary>
        /// Idempotent. On first call: serves the disk cache if valid, otherwise queues a
        /// full scan to be drained by <see cref="Pump"/>. Requires Game.Instance to be up
        /// (BlueprintsCache + version/locale stamp), so call from OnUpdate after the
        /// Game.Instance.Player guard.
        /// </summary>
        public static void EnsureStarted() {
            if (started) return;
            started = true;
            try {
                if (BuffIndexCache.TryLoad(out var fromDisk)) {
                    BuffBlueprintProvider.SetCache(fromDisk);
                    completed = true;
                    Log.Engine.Info($"BuffPackScanner: loaded {fromDisk.Count} buffs from disk cache (no scan)");
                    return;
                }
                var cache = ResourcesLibrary.BlueprintsCache;
                var index = cache?.m_LoadedBlueprints;
                if (index == null) {
                    Log.Engine.Warn("BuffPackScanner: BlueprintsCache index unavailable — skipping scan");
                    completed = true;
                    return;
                }
                pending = new Queue<BlueprintGuid>(index.Keys);
                Log.Engine.Info($"BuffPackScanner: no valid disk cache — warming {pending.Count} blueprint entries");
            } catch (Exception ex) {
                Log.Engine.Error(ex, "BuffPackScanner: EnsureStarted failed");
                completed = true;
            }
        }

        /// <summary>Call once per frame. Force-loads up to PerFrameBudget entries.</summary>
        public static void Pump() {
            if (completed || pending == null) return;
            try {
                var cache = ResourcesLibrary.BlueprintsCache;
                int budget = PerFrameBudget;
                while (budget-- > 0 && pending.Count > 0) {
                    var guid = pending.Dequeue();
                    try { cache.Load(guid); }
                    catch (Exception) { /* skip individual unreadable entry */ }
                    loaded++;
                }
                if (pending.Count == 0) {
                    pending = null;
                    completed = true;
                    BuffBlueprintProvider.OnFullScanComplete();
                    Log.Engine.Info($"BuffPackScanner: scan complete, {loaded} entries warmed");
                }
            } catch (Exception ex) {
                Log.Engine.Error(ex, "BuffPackScanner: Pump failed");
                completed = true;
                pending = null;
            }
        }
    }
}
