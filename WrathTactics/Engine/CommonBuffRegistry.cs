using System.Collections.Generic;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Curated lists of commonly-checked buffs, shown as default entries in the
    /// BuffPickerOverlay when the search field is empty. Names are resolved to
    /// GUIDs lazily via BuffBlueprintProvider (substring match, shortest wins).
    /// </summary>
    public static class CommonBuffRegistry {
        // Preferred names for ally-side buffs (Self / Ally / AllyCount contexts).
        // Order matters — it's the display order in the picker.
        static readonly List<string> AllyCommonNames = new List<string> {
            "Haste",
            "Bless",
            "Good Hope",
            "Heroism",
            "Prayer",
            "Shield of Faith",
            "Mage Armor",
            "Shield",
            "Aid",
            "Barkskin",
            "Stoneskin",
            "Bull's Strength",
            "Bear's Endurance",
            "Cat's Grace",
            "Owl's Wisdom",
            "Mirror Image",
            "Displacement",
            "Blur",
            "Freedom of Movement",
            "Protection from Evil"
        };

        // Preferred names for enemy-side buffs (Enemy* subjects).
        static readonly List<string> EnemyCommonNames = new List<string> {
            "Haste",
            "Mirror Image",
            "Displacement",
            "Blur",
            "Stoneskin",
            "Bless",
            "Bloodlust",
            "Heroism",
            "Mage Armor",
            "Protection from Evil",
            "Greater Heroism",
            "Unbreakable Heart",
            "Death Ward",
            "Mind Blank",
            "Freedom of Movement",
            "Protection from Arrows",
            "Resist Energy",
            "Energy Resistance Communal",
            "Shield",
            "Barkskin"
        };

        static List<string> cachedAllyGuids;
        static List<string> cachedEnemyGuids;

        public static List<string> GetDefaultGuids(ConditionSubject subject) {
            if (IsEnemySubject(subject)) {
                if (cachedEnemyGuids == null) cachedEnemyGuids = Resolve(EnemyCommonNames);
                return cachedEnemyGuids;
            }
            if (cachedAllyGuids == null) cachedAllyGuids = Resolve(AllyCommonNames);
            return cachedAllyGuids;
        }

        /// <summary>
        /// Force cache rebuild on next access — call when the BuffBlueprintProvider
        /// cache may have been invalidated (currently unused).
        /// </summary>
        public static void Invalidate() {
            cachedAllyGuids = null;
            cachedEnemyGuids = null;
        }

        static bool IsEnemySubject(ConditionSubject subject) {
            switch (subject) {
                case ConditionSubject.Enemy:
                case ConditionSubject.EnemyCount:
                case ConditionSubject.EnemyBiggestThreat:
                case ConditionSubject.EnemyLowestThreat:
                case ConditionSubject.EnemyHighestHp:
                case ConditionSubject.EnemyLowestHp:
                case ConditionSubject.EnemyLowestAC:
                case ConditionSubject.EnemyHighestAC:
                case ConditionSubject.EnemyLowestFort:
                case ConditionSubject.EnemyHighestFort:
                case ConditionSubject.EnemyLowestReflex:
                case ConditionSubject.EnemyHighestReflex:
                case ConditionSubject.EnemyLowestWill:
                case ConditionSubject.EnemyHighestWill:
                    return true;
                default:
                    return false;
            }
        }

        static List<string> Resolve(List<string> preferredNames) {
            var buffs = BuffBlueprintProvider.GetBuffs();
            var result = new List<string>();
            foreach (var preferred in preferredNames) {
                var guid = FindBestMatch(buffs, preferred);
                if (guid != null) result.Add(guid);
                else Log.Engine.Warn($"CommonBuffRegistry: no match for \"{preferred}\"");
            }
            return result;
        }

        // Substring match with shortest-name tiebreak. Assumes curated preferred names
        // are the "plain" variant (e.g. "Haste") — shorter matches are preferred over
        // decorated forms like "HasteGreater" / "HasteMass". If a curated name has no
        // plain variant in the blueprint DB, this heuristic may pick the next-shortest
        // decoration; drop problematic names from the curated list in that case.
        static string FindBestMatch(List<BuffBlueprintProvider.BuffEntry> buffs, string preferred) {
            // Normalize: strip spaces + apostrophes for matching, but keep the original
            // cache entry's Name for the comparison target (we normalize both sides).
            string needle = Normalize(preferred);
            BuffBlueprintProvider.BuffEntry best = default;
            bool found = false;
            foreach (var entry in buffs) {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (!Normalize(entry.Name).Contains(needle)) continue;
                if (!found || entry.Name.Length < best.Name.Length) {
                    best = entry;
                    found = true;
                }
            }
            return found ? best.Guid : null;
        }

        static string Normalize(string s) {
            return s.Replace(" ", "").Replace("'", "").ToLowerInvariant();
        }
    }
}
