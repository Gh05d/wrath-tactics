using System.Collections.Generic;
using System.Linq;
using Kingmaker;

namespace WrathTactics.Engine {
    /// <summary>
    /// Static list of class-and-group values for the HasClass condition dropdown.
    /// Groups (Spellcaster/Arcane/Divine/Martial) are sentinel strings; concrete
    /// classes use the blueprint's internal name with trailing "Class" stripped
    /// (locale-independent, stable across game patches).
    /// </summary>
    public static class ClassProvider {
        public struct ClassEntry {
            public string Value;   // "group:spellcaster" or "class:Wizard"
            public string Label;   // "[Group] Spellcaster" / "Wizard" / "Lich (Mythic)"
            public bool IsGroup;
        }

        static List<ClassEntry> cache;

        public static IReadOnlyList<ClassEntry> GetAll() {
            if (cache != null) return cache;

            var list = new List<ClassEntry> {
                new ClassEntry { Value = "group:spellcaster", Label = "[Group] Spellcaster",   IsGroup = true },
                new ClassEntry { Value = "group:arcane",      Label = "[Group] Arcane Caster", IsGroup = true },
                new ClassEntry { Value = "group:divine",      Label = "[Group] Divine Caster", IsGroup = true },
                new ClassEntry { Value = "group:martial",     Label = "[Group] Martial",       IsGroup = true },
            };

            var root = Game.Instance?.BlueprintRoot?.Progression;
            if (root != null) {
                if (root.AvailableCharacterClasses != null) {
                    foreach (var bp in root.AvailableCharacterClasses
                        .Where(b => b != null)
                        .OrderBy(b => StripSuffix(b.name))) {
                        var stripped = StripSuffix(bp.name);
                        list.Add(new ClassEntry {
                            Value = $"class:{stripped}",
                            Label = stripped,
                        });
                    }
                }
                if (root.AvailableCharacterMythics != null) {
                    foreach (var bp in root.AvailableCharacterMythics
                        .Where(b => b != null)
                        .OrderBy(b => StripSuffix(b.name))) {
                        var stripped = StripSuffix(bp.name);
                        list.Add(new ClassEntry {
                            Value = $"class:{stripped}",
                            Label = $"{stripped} (Mythic)",
                        });
                    }
                }
            }

            cache = list;
            return cache;
        }

        public static string StripSuffix(string name) {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            return name.EndsWith("Class") ? name.Substring(0, name.Length - 5) : name;
        }

        /// <summary>Resolve a stored value to its display label; returns the value itself if unknown.</summary>
        public static string GetLabel(string value) {
            if (string.IsNullOrEmpty(value)) return "";
            foreach (var e in GetAll())
                if (e.Value == value) return e.Label;
            return value;
        }
    }
}
