using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;

namespace WrathTactics.UI {
    public static class SpellDropdownProvider {
        public struct SpellEntry {
            public string Name;
            public string Guid;
            public SpellEntry(string name, string guid) { Name = name; Guid = guid; }
        }

        public static List<SpellEntry> GetSpells(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();

            // Spellbook spells
            foreach (var book in unit.Spellbooks) {
                for (int level = 0; level <= 10; level++) {
                    foreach (var spell in book.GetKnownSpells(level)) {
                        var guid = spell.Blueprint.AssetGuid.ToString();
                        if (seen.Add(guid))
                            result.Add(new SpellEntry($"[L{level}] {spell.Name}", guid));
                    }
                }
            }

            // Class abilities (non-item)
            foreach (var ability in unit.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;
                var guid = ability.Blueprint.AssetGuid.ToString();
                if (seen.Add(guid))
                    result.Add(new SpellEntry($"[Ability] {ability.Name}", guid));
            }

            return result.OrderBy(e => e.Name).ToList();
        }

        public static List<SpellEntry> GetActivatables(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();

            foreach (var activatable in unit.ActivatableAbilities.RawFacts) {
                var guid = activatable.Blueprint.AssetGuid.ToString();
                if (seen.Add(guid))
                    result.Add(new SpellEntry(activatable.Blueprint.Name, guid));
            }

            return result.OrderBy(e => e.Name).ToList();
        }

        public static List<SpellEntry> GetItemAbilities(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();

            foreach (var ability in unit.Abilities.RawFacts) {
                if (ability.Data.SourceItem == null) continue;
                var guid = ability.Blueprint.AssetGuid.ToString();
                if (seen.Add(guid))
                    result.Add(new SpellEntry($"[Item] {ability.Name}", guid));
            }

            return result.OrderBy(e => e.Name).ToList();
        }
    }
}
