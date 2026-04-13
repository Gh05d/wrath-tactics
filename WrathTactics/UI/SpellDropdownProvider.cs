using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace WrathTactics.UI {
    public static class SpellDropdownProvider {
        public struct SpellEntry {
            public string Name;
            public string Guid;
            public Sprite Icon;
            public SpellEntry(string name, string guid, Sprite icon = null) {
                Name = name; Guid = guid; Icon = icon;
            }
        }

        public static List<SpellEntry> GetSpells(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();

            // Spellbook spells (max level 9)
            foreach (var book in unit.Spellbooks) {
                for (int level = 0; level <= 9; level++) {
                    foreach (var spell in book.GetKnownSpells(level)) {
                        var guid = spell.Blueprint.AssetGuid.ToString();
                        if (seen.Add(guid))
                            result.Add(new SpellEntry($"[L{level}] {spell.Name}", guid, spell.Blueprint.Icon));
                    }
                }
            }

            // Class abilities (non-item)
            foreach (var ability in unit.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;
                var guid = ability.Blueprint.AssetGuid.ToString();
                if (seen.Add(guid))
                    result.Add(new SpellEntry($"[Ability] {ability.Name}", guid, ability.Blueprint.Icon));
            }

            return result.OrderBy(e => e.Name).ToList();
        }

        public static List<SpellEntry> GetActivatables(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();

            foreach (var activatable in unit.ActivatableAbilities.RawFacts) {
                var guid = activatable.Blueprint.AssetGuid.ToString();
                if (seen.Add(guid))
                    result.Add(new SpellEntry(activatable.Blueprint.Name, guid, activatable.Blueprint.Icon));
            }

            return result.OrderBy(e => e.Name).ToList();
        }

        public static List<SpellEntry> GetItemAbilities(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();

            foreach (var ability in unit.Abilities.RawFacts) {
                if (ability.Data.SourceItem == null) continue;
                var guid = ability.Blueprint.AssetGuid.ToString();
                if (!seen.Add(guid)) continue;

                string prefix = "(Item)";
                var sourceItem = ability.Data.SourceItem;
                if (sourceItem.Blueprint is BlueprintItemEquipmentUsable usable) {
                    switch (usable.Type) {
                        case UsableItemType.Scroll:
                            prefix = "(Scroll)";
                            break;
                        case UsableItemType.Potion:
                            prefix = "(Potion)";
                            break;
                        case UsableItemType.Wand:
                            prefix = "(Wand)";
                            break;
                        default:
                            prefix = "(Item)";
                            break;
                    }
                }

                result.Add(new SpellEntry($"{ability.Name} {prefix}", guid, ability.Blueprint.Icon));
            }

            return result.OrderBy(e => e.Name).ToList();
        }
    }
}
