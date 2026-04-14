using System.Collections.Generic;

namespace WrathTactics.Engine {
    /// <summary>
    /// Hardcoded list of vanilla Pathfinder: WotR splash items.
    /// GUIDs are extracted from blueprints.zip and fixed at compile time.
    /// Mod-added items are intentionally not supported.
    /// </summary>
    public static class SplashItemRegistry {
        public struct Entry {
            public string Guid;
            public int DamagePriority;
            public int CostPriority;
        }

        static readonly Entry[] Items = new[] {
            new Entry { Guid = "4639724c4a9cc9544a2f622b66931658", DamagePriority = 35, CostPriority = 20 }, // Acid Flask
            new Entry { Guid = "fd56596e273d1ff49a8c29cc9802ae6e", DamagePriority = 70, CostPriority = 20 }, // Alchemist's Fire
            new Entry { Guid = "a8bc157a846e2d64498915cadd026aef", DamagePriority = 50, CostPriority = 25 }, // Holy Water
        };

        static readonly HashSet<string> LookupSet = new HashSet<string>();
        static readonly Dictionary<string, Entry> LookupMap = new Dictionary<string, Entry>();

        static SplashItemRegistry() {
            foreach (var e in Items) {
                LookupSet.Add(e.Guid);
                LookupMap[e.Guid] = e;
            }
        }

        public static bool IsSplashItem(string blueprintGuid) {
            return !string.IsNullOrEmpty(blueprintGuid) && LookupSet.Contains(blueprintGuid);
        }

        public static int GetDamagePriority(string blueprintGuid) {
            return LookupMap.TryGetValue(blueprintGuid, out var e) ? e.DamagePriority : 0;
        }

        public static int GetCostPriority(string blueprintGuid) {
            return LookupMap.TryGetValue(blueprintGuid, out var e) ? e.CostPriority : 0;
        }
    }
}
