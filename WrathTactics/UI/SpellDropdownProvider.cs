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
        /// Parsed form of a compound ability key. Level=-1 means unspecified (legacy key).
        /// VariantGuid=null means no variant. MetamagicMask=0 means no metamagic.
        /// </summary>
        public struct ParsedKey {
            public string BlueprintGuid;
            public int Level;
            public string VariantGuid;
            public int MetamagicMask;
        }

        /// <summary>
        /// Builds a compound ability key:
        ///   guid[@L&lt;level&gt;][&gt;V&lt;variantGuid&gt;][#&lt;metamagicMask&gt;]
        /// level &lt; 0 omits the level segment (for non-spellbook abilities). Metamagic is read from the AbilityData.
        /// </summary>
        public static string MakeKey(AbilityData spell, int level = -1, string variantGuid = null) {
            var sb = new System.Text.StringBuilder(spell.Blueprint.AssetGuid.ToString());
            if (level >= 0) sb.Append("@L").Append(level);
            if (!string.IsNullOrEmpty(variantGuid)) sb.Append(">V").Append(variantGuid);
            if (spell.MetamagicData != null && spell.MetamagicData.NotEmpty)
                sb.Append("#").Append((int)spell.MetamagicData.MetamagicMask);
            return sb.ToString();
        }

        /// <summary>
        /// Parses a compound ability key. Missing segments get defaults (Level=-1, VariantGuid=null, MetamagicMask=0).
        /// Legacy keys (bare GUID, or guid#meta) parse cleanly as Level=-1.
        /// </summary>
        public static ParsedKey ParseKey(string key) {
            var result = new ParsedKey { Level = -1 };
            if (string.IsNullOrEmpty(key)) return result;

            int end = key.Length;

            int hash = key.LastIndexOf('#');
            if (hash >= 0 && int.TryParse(key.Substring(hash + 1), out int mask)) {
                result.MetamagicMask = mask;
                end = hash;
            }

            int vIdx = key.IndexOf(">V", StringComparison.Ordinal);
            if (vIdx >= 0 && vIdx < end) {
                result.VariantGuid = key.Substring(vIdx + 2, end - vIdx - 2);
                end = vIdx;
            }

            int lIdx = key.IndexOf("@L", StringComparison.Ordinal);
            if (lIdx >= 0 && lIdx < end) {
                if (int.TryParse(key.Substring(lIdx + 2, end - lIdx - 2), out int lvl))
                    result.Level = lvl;
                end = lIdx;
            }

            result.BlueprintGuid = key.Substring(0, end);
            return result;
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
                int maxLevel = book.MaxSpellLevel;
                for (int level = 0; level <= maxLevel; level++) {
                    // Base known spells — expand AbilityVariants (Command, Plague Storm, …) per variant
                    foreach (var spell in book.GetKnownSpells(level)) {
                        var variants = GetBlueprintComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityVariants>(spell.Blueprint);
                        if (variants != null && variants.m_Variants != null && variants.m_Variants.Length > 0) {
                            foreach (var variant in variants.Variants) {
                                if (variant == null) continue;
                                var key = MakeKey(spell, level, variant.AssetGuid.ToString());
                                if (seen.Add(key))
                                    result.Add(new SpellEntry(
                                        FormatWithInternal($"[L{level}] {spell.Name}: {variant.Name}", variant),
                                        key, variant.Icon));
                            }
                        } else {
                            var key = MakeKey(spell, level);
                            if (seen.Add(key))
                                result.Add(new SpellEntry(
                                    FormatWithInternal($"[L{level}] {spell.Name}", spell.Blueprint),
                                    key, spell.Blueprint.Icon));
                        }
                    }
                    // Custom spells (metamagic variants, fused spells)
                    foreach (var spell in book.GetCustomSpells(level)) {
                        var key = MakeKey(spell, level);
                        if (seen.Add(key)) {
                            var tag = BuildMetamagicTag(spell);
                            var name = tag.Length > 0
                                ? $"[L{level}] {spell.Name} {tag}"
                                : $"[L{level}] {spell.Name}";
                            result.Add(new SpellEntry(
                                FormatWithInternal(name, spell.Blueprint),
                                key, spell.Blueprint.Icon));
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
                            result.Add(new SpellEntry(
                                FormatWithInternal(variant.Name, variant),
                                varGuid, variant.Icon));
                    }
                } else {
                    // Regular ability without variants
                    var guid = ability.Blueprint.AssetGuid.ToString();
                    if (seen.Add(guid))
                        result.Add(new SpellEntry(
                            FormatWithInternal(ability.Name, ability.Blueprint),
                            guid, ability.Blueprint.Icon));
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
                    result.Add(new SpellEntry(
                        FormatWithInternal(activatable.Blueprint.Name, activatable.Blueprint),
                        guid, activatable.Blueprint.Icon));
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

        // Strips the trailing "Ability" from a blueprint's internal name so the suffix
        // stays compact. "FireBlastAbility" → "FireBlast"; "KineticBladeFireBlastAbility"
        // → "KineticBladeFireBlast"; names without the suffix pass through unchanged.
        static string StripAbilitySuffix(string name) {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            return name.EndsWith("Ability") ? name.Substring(0, name.Length - 7) : name;
        }

        // Appends "(InternalName)" to the display label so visually-identical rows
        // (e.g. Kineticist's several "Fire Blast"-named blueprints) are distinguishable.
        // Returns the plain displayName when the blueprint or its name is missing.
        static string FormatWithInternal(string displayName, BlueprintScriptableObject bp) {
            if (bp == null || string.IsNullOrEmpty(bp.name)) return displayName;
            return $"{displayName} ({StripAbilitySuffix(bp.name)})";
        }

        public static List<SpellEntry> GetItemAbilities(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();

            // 1. Equipped item-backed abilities (wands in quickslot, staves, scrolls in quickslot).
            //    These register as facts on the unit with SourceItem set.
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

                result.Add(new SpellEntry(
                    FormatWithInternal($"{ability.Name} {prefix}", ability.Blueprint),
                    guid, ability.Blueprint.Icon));
            }

            // 2. Shared-inventory potions/scrolls. These do NOT register as facts on the unit —
            //    Wrath's own inventory-drink flow scans the shared inventory directly.
            //    Without this branch, UseItem rules could never pick an inventory potion
            //    (e.g. Potion of Invisibility sitting in the party stash).
            var inventory = Kingmaker.Game.Instance?.Player?.Inventory;
            if (inventory != null) {
                foreach (var item in inventory) {
                    if (item == null || item.Count <= 0) continue;
                    var usable = item.Blueprint as BlueprintItemEquipmentUsable;
                    if (usable?.Ability == null) continue;
                    if (usable.Type != UsableItemType.Potion && usable.Type != UsableItemType.Scroll) continue;

                    var guid = usable.Ability.AssetGuid.ToString();
                    if (!seen.Add(guid)) continue;

                    string prefix = usable.Type == UsableItemType.Potion ? "(Potion)" : "(Scroll)";
                    result.Add(new SpellEntry(
                        FormatWithInternal($"{usable.Ability.Name} {prefix}", usable.Ability),
                        guid, usable.Ability.Icon));
                }
            }

            return result.OrderBy(e => e.Name).ToList();
        }
    }
}
