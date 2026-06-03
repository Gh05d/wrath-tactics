using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    public static class BuffBlueprintProvider {
        static List<BuffEntry> cachedBuffs;

        public struct BuffEntry {
            public string Name;          // Localized display name (e.g. "Bless"/"Segen").
            public string InternalName;  // Blueprint identifier (e.g. "BlessBuff") — kept for search fallback.
            public string Guid;
        }

        public static bool IsLoaded => cachedBuffs != null;

        /// <summary>
        /// Returns the buff list. Once <see cref="BuffPackScanner"/> has populated the
        /// authoritative cache (from disk or a completed full scan), that frozen list is
        /// returned. Until then — the brief window before the scan finishes — a live
        /// snapshot of currently-loaded buffs is returned UNCACHED, so the picker isn't
        /// empty and the scanner can still replace it.
        /// </summary>
        public static List<BuffEntry> GetBuffs() {
            if (cachedBuffs != null) return cachedBuffs;
            return EnumerateLoaded();
        }

        /// <summary>Install the authoritative cache (disk-cache hit path).</summary>
        internal static void SetCache(List<BuffEntry> buffs) {
            cachedBuffs = buffs;
        }

        /// <summary>
        /// Called by <see cref="BuffPackScanner"/> when the full pack scan finishes: every
        /// BlueprintBuff is now loaded, so a final enumeration captures the complete set,
        /// which is then persisted for future sessions.
        /// </summary>
        internal static void OnFullScanComplete() {
            cachedBuffs = EnumerateLoaded();
            BuffIndexCache.Save(cachedBuffs);
            CommonBuffRegistry.Invalidate();
            Log.Engine.Info($"BuffBlueprintProvider: {cachedBuffs.Count} buffs after full scan");
        }

        static List<BuffEntry> EnumerateLoaded() {
            var results = new List<BuffEntry>();
            try {
                int skipped = 0;
                ResourcesLibrary.BlueprintsCache.ForEachLoaded((guid, bp) => {
                    // bp is null for index entries not yet loaded — ForEachLoaded passes
                    // entry.Blueprint without a null filter (verified via IL).
                    if (!(bp is BlueprintBuff buff) || string.IsNullOrEmpty(buff.name)) return;
                    if (IsCrusadeOnlyBuff(buff.name)) { skipped++; return; }
                    // BlueprintBuff.Name = localized display name; .name = internal id.
                    // Fallback to the internal id when the localized string is empty
                    // (some hidden/system buffs ship without a display name).
                    var localized = string.IsNullOrEmpty(buff.Name) ? buff.name : buff.Name;
                    results.Add(new BuffEntry {
                        Name = localized,
                        InternalName = buff.name,
                        Guid = guid.ToString()
                    });
                });
                results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            } catch (Exception ex) {
                Log.Engine.Error(ex, "BuffBlueprintProvider: failed to enumerate buff blueprints");
            }
            return results;
        }

        /// <summary>
        /// Filters out buffs that only apply in the crusade/tactical-combat mini-game.
        /// Roleplay-mode players should never see these in the HasBuff/MissingBuff picker.
        /// Owlcat uses the "Army" prefix by convention for army-only buffs.
        /// </summary>
        internal static bool IsCrusadeOnlyBuff(string name) {
            return name.StartsWith("Army", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Force a cache refresh (e.g. called when the panel opens to pick up newly loaded buffs).
        /// </summary>
        public static void Invalidate() {
            cachedBuffs = null;
        }

        public static string GetName(string guid) {
            if (cachedBuffs == null || string.IsNullOrEmpty(guid)) return guid;
            foreach (var entry in cachedBuffs) {
                if (entry.Guid == guid) return entry.Name;
            }
            return guid;
        }
    }
}
