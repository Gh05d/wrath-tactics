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
            public string Name;
            public string Guid;
        }

        public static bool IsLoaded => cachedBuffs != null;

        public static List<BuffEntry> GetBuffs() {
            if (cachedBuffs != null) return cachedBuffs;
            Load();
            return cachedBuffs;
        }

        static void Load() {
            try {
                var results = new List<BuffEntry>();
                int skipped = 0;
                ResourcesLibrary.BlueprintsCache.ForEachLoaded((guid, bp) => {
                    if (!(bp is BlueprintBuff buff) || string.IsNullOrEmpty(buff.name)) return;
                    if (IsCrusadeOnlyBuff(buff.name)) { skipped++; return; }
                    results.Add(new BuffEntry {
                        Name = buff.name,
                        Guid = guid.ToString()
                    });
                });
                results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                cachedBuffs = results;
                Log.Engine.Info($"BuffBlueprintProvider: {cachedBuffs.Count} roleplay buff blueprints ({skipped} crusade-only filtered)");
            } catch (Exception ex) {
                Log.Engine.Error(ex, "BuffBlueprintProvider: failed to enumerate buff blueprints");
                cachedBuffs = new List<BuffEntry>();
            }
        }

        /// <summary>
        /// Filters out buffs that only apply in the crusade/tactical-combat mini-game.
        /// Roleplay-mode players should never see these in the HasBuff/MissingBuff picker.
        /// Owlcat uses the "Army" prefix by convention for army-only buffs.
        /// </summary>
        static bool IsCrusadeOnlyBuff(string name) {
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
