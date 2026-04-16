using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
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

        /// <summary>
        /// Builds a compound key for an AbilityData. Normal spells use the blueprint GUID;
        /// metamagic variants use "guid#metamagicMask" so each variant is uniquely identifiable.
        /// </summary>
        public static string MakeKey(AbilityData spell) {
            var guid = spell.Blueprint.AssetGuid.ToString();
            if (spell.MetamagicData != null && spell.MetamagicData.NotEmpty)
                return $"{guid}#{(int)spell.MetamagicData.MetamagicMask}";
            return guid;
        }

        /// <summary>
        /// Parses a compound key. Returns the blueprint GUID and metamagic mask (0 if none).
        /// </summary>
        public static void ParseKey(string key, out string guid, out int metamagicMask) {
            int sep = key.IndexOf('#');
            if (sep < 0) {
                guid = key;
                metamagicMask = 0;
            } else {
                guid = key.Substring(0, sep);
                int.TryParse(key.Substring(sep + 1), out metamagicMask);
            }
        }

        static string BuildMetamagicTag(AbilityData spell) {
            if (spell.MetamagicData == null || !spell.MetamagicData.NotEmpty)
                return "";
            var tag = "";
            foreach (Metamagic flag in Enum.GetValues(typeof(Metamagic))) {
                if (flag == 0) continue;
                if (!spell.MetamagicData.Has(flag)) continue;
                switch (flag) {
                    case Metamagic.Empower: tag += "E"; break;
                    case Metamagic.Maximize: tag += "M"; break;
                    case Metamagic.Quicken: tag += "Q"; break;
                    case Metamagic.Extend: tag += "X"; break;
                    case Metamagic.Heighten: tag += "H"; break;
                    case Metamagic.Reach: tag += "R"; break;
                    case Metamagic.CompletelyNormal: tag += "N"; break;
                    case Metamagic.Persistent: tag += "P"; break;
                    case Metamagic.Selective: tag += "S"; break;
                    case Metamagic.Bolstered: tag += "B"; break;
                    default: tag += "?"; break;
                }
            }
            return tag.Length > 0 ? $"[{tag}]" : "";
        }

        public static List<SpellEntry> GetSpells(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();

            foreach (var book in unit.Spellbooks) {
                for (int level = 0; level <= 9; level++) {
                    // Base known spells
                    foreach (var spell in book.GetKnownSpells(level)) {
                        var key = spell.Blueprint.AssetGuid.ToString();
                        if (seen.Add(key))
                            result.Add(new SpellEntry($"[L{level}] {spell.Name}", key, spell.Blueprint.Icon));
                    }
                    // Custom spells (metamagic variants, fused spells)
                    foreach (var spell in book.GetCustomSpells(level)) {
                        var key = MakeKey(spell);
                        if (seen.Add(key)) {
                            var tag = BuildMetamagicTag(spell);
                            var name = tag.Length > 0
                                ? $"[L{level}] {spell.Name} {tag}"
                                : $"[L{level}] {spell.Name}";
                            result.Add(new SpellEntry(name, key, spell.Blueprint.Icon));
                        }
                    }
                }
            }

            return result.OrderBy(e => e.Name).ToList();
        }

        public static List<SpellEntry> GetAbilities(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();

            // Class abilities (non-item, non-spellbook)
            foreach (var ability in unit.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;

                // Check for variants (sub-abilities like Evil Eye - AC)
                var variants = GetBlueprintComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityVariants>(ability.Blueprint);
                if (variants != null && variants.m_Variants != null && variants.m_Variants.Length > 0) {
                    // Add each variant instead of the parent
                    foreach (var variant in variants.Variants) {
                        if (variant == null) continue;
                        var varGuid = variant.AssetGuid.ToString();
                        if (seen.Add(varGuid))
                            result.Add(new SpellEntry(variant.Name, varGuid, variant.Icon));
                    }
                } else {
                    // Regular ability without variants
                    var guid = ability.Blueprint.AssetGuid.ToString();
                    if (seen.Add(guid))
                        result.Add(new SpellEntry(ability.Name, guid, ability.Blueprint.Icon));
                }
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

        static T GetBlueprintComponent<T>(BlueprintScriptableObject bp) where T : BlueprintComponent {
            if (bp?.ComponentsArray == null) return null;
            foreach (var c in bp.ComponentsArray) {
                if (c is T typed) return typed;
            }
            return null;
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
