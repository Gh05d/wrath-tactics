using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using UnityEngine;
using WrathTactics.Localization;

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
        /// ActionType=-1 means unspecified — only conversion entries that share the parent's
        /// blueprint GUID (e.g. TTT Quick Channel's move-action channel) carry it.
        /// </summary>
        public struct ParsedKey {
            public string BlueprintGuid;
            public int Level;
            public string VariantGuid;
            public int MetamagicMask;
            public int ActionType;
        }

        /// <summary>
        /// Builds a compound ability key:
        ///   guid[@L&lt;level&gt;][&gt;V&lt;variantGuid&gt;][~A&lt;actionType&gt;][#&lt;metamagicMask&gt;]
        /// level &lt; 0 omits the level segment (for non-spellbook abilities). Metamagic is read from the AbilityData.
        /// </summary>
        public static string MakeKey(AbilityData spell, int level = -1, string variantGuid = null, int actionType = -1) {
            int mask = (spell.MetamagicData != null && spell.MetamagicData.NotEmpty)
                ? (int)spell.MetamagicData.MetamagicMask : 0;
            return MakeKeyCore(spell.Blueprint.AssetGuid.ToString(), level, variantGuid, mask, actionType);
        }

        /// <summary>Pure builder behind <see cref="MakeKey"/> — testable without game objects.</summary>
        public static string MakeKeyCore(string blueprintGuid, int level, string variantGuid, int metamagicMask, int actionType) {
            var sb = new System.Text.StringBuilder(blueprintGuid);
            if (level >= 0) sb.Append("@L").Append(level);
            if (!string.IsNullOrEmpty(variantGuid)) sb.Append(">V").Append(variantGuid);
            if (actionType >= 0) sb.Append("~A").Append(actionType);
            if (metamagicMask != 0) sb.Append("#").Append(metamagicMask);
            return sb.ToString();
        }

        /// <summary>
        /// Parses a compound ability key. Missing segments get defaults (Level=-1, VariantGuid=null,
        /// MetamagicMask=0, ActionType=-1). Legacy keys (bare GUID, or guid#meta) parse cleanly as Level=-1.
        /// </summary>
        public static ParsedKey ParseKey(string key) {
            var result = new ParsedKey { Level = -1, ActionType = -1 };
            if (string.IsNullOrEmpty(key)) return result;

            int end = key.Length;

            int hash = key.LastIndexOf('#');
            if (hash >= 0 && int.TryParse(key.Substring(hash + 1), out int mask)) {
                result.MetamagicMask = mask;
                end = hash;
            }

            int aIdx = key.IndexOf("~A", StringComparison.Ordinal);
            if (aIdx >= 0 && aIdx < end) {
                if (int.TryParse(key.Substring(aIdx + 2, end - aIdx - 2), out int act))
                    result.ActionType = act;
                end = aIdx;
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
            int mask = (int)spell.MetamagicData.MetamagicMask;
            int consumed = 0;
            foreach (Metamagic flag in Enum.GetValues(typeof(Metamagic))) {
                if (flag == 0) continue;
                if (!spell.MetamagicData.Has(flag)) continue;
                consumed |= (int)flag;
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
            // Bits not represented in the declared Metamagic enum — raw-bit
            // modded metamagic (e.g. TabletopTweaks Solid Shadows) that wasn't
            // registered through EnumExtender. One "?" per unknown bit so the
            // user still sees that metamagic IS applied to the entry, even when
            // the source mod is unrecognized.
            int leftover = mask & ~consumed;
            while (leftover != 0) {
                if ((leftover & 1) != 0) tag += "?";
                leftover = (int)((uint)leftover >> 1);
            }
            return tag.Length > 0 ? $"[{tag}]" : "";
        }

        public static List<SpellEntry> GetSpells(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();
            var emittedBlueprints = new HashSet<string>();
            var conversionSources = new List<(AbilityData spell, int level)>();

            foreach (var book in unit.Spellbooks) {
                int maxLevel = book.MaxSpellLevel;
                for (int level = 0; level <= maxLevel; level++) {
                    // Base known spells — expand AbilityVariants (Command, Plague Storm, …) per variant
                    foreach (var spell in book.GetKnownSpells(level)) {
                        if (!TryEmitVariantEntries(spell, level, seen, emittedBlueprints, result))
                            EmitBareSpellEntry(spell, level, seen, emittedBlueprints, result);
                        conversionSources.Add((spell, level));
                    }
                    // Custom spells (metamagic variants, fused spells) — expand AbilityVariants
                    // so a metamagic-prepared Summon Monster II surfaces all its sub-options
                    // (1 wolf / 1d3 dogs / …) carrying the metamagic tag, mirroring the known-spells path.
                    foreach (var spell in book.GetCustomSpells(level)) {
                        if (!TryEmitVariantEntries(spell, level, seen, emittedBlueprints, result))
                            EmitBareSpellEntry(spell, level, seen, emittedBlueprints, result);
                        conversionSources.Add((spell, level));
                    }
                    // Special spells — Cleric Domain, Shaman Spirit, Sorcerer Bloodline,
                    // Witch Patron lists (added by AddSpecialSpellList → Spellbook.AddSpecial).
                    // Owlcat's own SpellBookView reads all three collections; mod parity.
                    foreach (var spell in book.GetSpecialSpells(level)) {
                        if (!TryEmitVariantEntries(spell, level, seen, emittedBlueprints, result))
                            EmitBareSpellEntry(spell, level, seen, emittedBlueprints, result);
                        conversionSources.Add((spell, level));
                    }
                }
            }

            // Second pass: runtime conversions (AbilityData.GetConversions). Runs after ALL
            // static enumeration so the emittedBlueprints filter can drop conversions that are
            // already visible some other way — the engine's conversion list re-includes every
            // AbilityVariants entry, and spontaneous conversion offers spells the caster already
            // knows (a cleric's cure line). What survives is genuinely conversion-only content:
            // TTT-style AddSpecificSpellConversion targets (Magic Trick – Fireball's Cluster Bomb)
            // and same-blueprint action-type conversions (Quick Channel).
            foreach (var (spell, level) in conversionSources) {
                var tag = BuildMetamagicTag(spell);
                string tagSuffix = tag.Length > 0 ? " " + tag : "";
                EmitConversionEntries(spell, level, $"[L{level}] {spell.Name}{tagSuffix}", seen, emittedBlueprints, result);
            }

            return result.OrderBy(e => e.Name).ToList();
        }

        public static List<SpellEntry> GetAbilities(UnitEntityData unit) {
            var result = new List<SpellEntry>();
            var seen = new HashSet<string>();
            var emittedBlueprints = new HashSet<string>();
            var conversionSources = new List<AbilityData>();

            // Class abilities (non-item, non-spellbook)
            foreach (var ability in unit.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;
                conversionSources.Add(ability.Data);

                // Check for variants (sub-abilities like Evil Eye - AC)
                var variants = GetBlueprintComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityVariants>(ability.Blueprint);
                if (variants != null && variants.m_Variants != null && variants.m_Variants.Length > 0) {
                    // Add each variant instead of the parent
                    foreach (var variant in variants.Variants) {
                        if (variant == null) continue;
                        var varGuid = variant.AssetGuid.ToString();
                        emittedBlueprints.Add(varGuid);
                        if (seen.Add(varGuid))
                            result.Add(new SpellEntry(
                                FormatWithInternal(variant.Name, variant),
                                varGuid, variant.Icon));
                    }
                } else {
                    // Regular ability without variants
                    var guid = ability.Blueprint.AssetGuid.ToString();
                    emittedBlueprints.Add(guid);
                    if (seen.Add(guid))
                        result.Add(new SpellEntry(
                            FormatWithInternal(ability.Name, ability.Blueprint),
                            guid, ability.Blueprint.Icon));
                }
            }

            // Second pass: runtime conversions — same filter rationale as GetSpells.
            // Class-ability conversion entries carry compound keys (parent>Vconversion[~An]),
            // unlike the legacy bare-GUID entries above; FindAbilityEx resolves both forms.
            foreach (var data in conversionSources)
                EmitConversionEntries(data, -1, data.Name, seen, emittedBlueprints, result);

            return result.OrderBy(e => e.Name).ToList();
        }

        // Emits picker entries for runtime conversions (AbilityData.GetConversions) of one
        // parent ability. Two shapes:
        //  - Different-blueprint conversion (TTT AddSpecificSpellConversion, Preferred Spell):
        //    key = parent>Vconversion. Skipped when the blueprint is already visible anywhere
        //    else in this dropdown (emittedBlueprints) — kills variant/spontaneous-cure noise.
        //  - Same-blueprint conversion (TTT AbilityActionTypeConversion, e.g. Quick Channel):
        //    only the action type differs, so the key carries ~A<actionType> and the label an
        //    " (Move)"-style suffix. Not subject to the emittedBlueprints filter — the parent
        //    is legitimately in the list already.
        // GetConversions executes third-party conversion handlers (TTT raises an EventBus event
        // from a Harmony postfix), so one broken mod component must not kill the whole picker:
        // per-parent catch, warn, continue.
        static void EmitConversionEntries(AbilityData parent, int level, string labelPrefix,
                HashSet<string> seen, HashSet<string> emittedBlueprints, List<SpellEntry> result) {
            List<AbilityData> conversions;
            try {
                conversions = parent.GetConversions().ToList();
            } catch (Exception ex) {
                Logging.Log.UI.Warn($"GetConversions failed for {parent.Blueprint?.name}: {ex.Message}");
                return;
            }

            var parentGuid = parent.Blueprint.AssetGuid.ToString();
            foreach (var conv in conversions) {
                if (conv?.Blueprint == null) continue;
                var convGuid = conv.Blueprint.AssetGuid.ToString();

                if (convGuid == parentGuid) {
                    int convAction = (int)conv.ActionType;
                    if (convAction == (int)parent.ActionType) continue; // indistinguishable duplicate
                    var key = MakeKey(parent, level, convGuid, convAction);
                    if (!seen.Add(key)) continue;
                    result.Add(new SpellEntry(
                        FormatWithInternal($"{labelPrefix}: {conv.Name} ({conv.ActionType})", conv.Blueprint),
                        key, conv.Blueprint.Icon));
                } else {
                    if (!emittedBlueprints.Add(convGuid)) continue;
                    var key = MakeKey(parent, level, convGuid);
                    if (!seen.Add(key)) continue;
                    result.Add(new SpellEntry(
                        FormatWithInternal($"{labelPrefix}: {conv.Name}", conv.Blueprint),
                        key, conv.Blueprint.Icon));
                }
            }
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

        // Emits one picker entry per variant when the parent carries either
        // AbilityVariants (Command, Plague Storm, …) or AbilityShadowSpell
        // (Shadow Conjuration / Evocation and their Greater forms — the engine
        // builds the variant set at runtime from SpellList × MaxSpellLevel × School,
        // not from an m_Variants[] array). Returns true if at least one variant was
        // emitted, false when neither component was present so the caller can fall
        // back to a single bare entry. A spell carrying both components emits from
        // both — no vanilla blueprint does this but the loops are independent.
        static bool TryEmitVariantEntries(AbilityData spell, int level, HashSet<string> seen, HashSet<string> emittedBlueprints, List<SpellEntry> result) {
            bool emitted = false;
            var tag = BuildMetamagicTag(spell);
            string tagSuffix = tag.Length > 0 ? " " + tag : "";

            var variants = GetBlueprintComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityVariants>(spell.Blueprint);
            if (variants != null && variants.m_Variants != null && variants.m_Variants.Length > 0) {
                foreach (var variant in variants.Variants) {
                    if (variant == null) continue;
                    emittedBlueprints.Add(variant.AssetGuid.ToString());
                    var key = MakeKey(spell, level, variant.AssetGuid.ToString());
                    if (seen.Add(key)) {
                        result.Add(new SpellEntry(
                            FormatWithInternal($"[L{level}] {spell.Name}{tagSuffix}: {variant.Name}", variant),
                            key, variant.Icon));
                        emitted = true;
                    }
                }
            }

            var shadow = GetBlueprintComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityShadowSpell>(spell.Blueprint);
            if (shadow != null && shadow.SpellList?.Get() != null) {
                foreach (var variant in shadow.GetAvailableSpells()) {
                    if (variant == null) continue;
                    emittedBlueprints.Add(variant.AssetGuid.ToString());
                    var key = MakeKey(spell, level, variant.AssetGuid.ToString());
                    if (seen.Add(key)) {
                        result.Add(new SpellEntry(
                            FormatWithInternal($"[L{level}] {spell.Name}{tagSuffix}: {variant.Name}", variant),
                            key, variant.Icon));
                        emitted = true;
                    }
                }
            }

            return emitted;
        }

        static void EmitBareSpellEntry(AbilityData spell, int level, HashSet<string> seen, HashSet<string> emittedBlueprints, List<SpellEntry> result) {
            emittedBlueprints.Add(spell.Blueprint.AssetGuid.ToString());
            var key = MakeKey(spell, level);
            if (!seen.Add(key)) return;
            var tag = BuildMetamagicTag(spell);
            var label = tag.Length > 0
                ? $"[L{level}] {spell.Name} {tag}"
                : $"[L{level}] {spell.Name}";
            result.Add(new SpellEntry(
                FormatWithInternal(label, spell.Blueprint),
                key, spell.Blueprint.Icon));
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

                string prefix = "item.prefix.item".i18n();
                var sourceItem = ability.Data.SourceItem;
                if (sourceItem.Blueprint is BlueprintItemEquipmentUsable usable) {
                    switch (usable.Type) {
                        case UsableItemType.Scroll:
                            prefix = "item.prefix.scroll".i18n();
                            break;
                        case UsableItemType.Potion:
                            prefix = "item.prefix.potion".i18n();
                            break;
                        case UsableItemType.Wand:
                            prefix = "item.prefix.wand".i18n();
                            break;
                        default:
                            prefix = "item.prefix.item".i18n();
                            break;
                    }
                }

                result.Add(new SpellEntry(
                    FormatWithInternal($"{ability.Name} {prefix}", ability.Blueprint),
                    guid, ability.Blueprint.Icon));
            }

            // 2. Shared-inventory potions/scrolls. These do NOT register as facts on the unit —
            //    Wrath's own inventory-drink flow scans the shared inventory directly.
            //
            //    Four-pass scan ordered POTION → SCROLL → WAND → UTILITY. Multiple item forms share
            //    an ability blueprint GUID (Scroll of Invisibility, Potion of Invisibility, Wand of
            //    Invisibility all cast "Invisibility"). Iterating in storage order and deduping
            //    by GUID silently drops later entries. Potions are preferred (no UMD, no silence
            //    gate, CL1 reliable); scrolls next; wands; finally Utility (rods, special-power
            //    devices). Utility is last so existing potion/scroll/wand rules keep resolving
            //    to their original consumable; a user adding a rule against a rod-only ability
            //    explicitly opts in. ActionValidator.FindUseItemSource mirrors this ordering so
            //    the runtime pick matches the dropdown label.
            var inventory = Kingmaker.Game.Instance?.Player?.Inventory;
            if (inventory != null) {
                EnumerateInventoryByType(inventory, UsableItemType.Potion, "item.prefix.potion".i18n(), seen, result);
                EnumerateInventoryByType(inventory, UsableItemType.Scroll, "item.prefix.scroll".i18n(), seen, result);
                EnumerateInventoryByType(inventory, UsableItemType.Wand, "item.prefix.wand".i18n(), seen, result);
                EnumerateInventoryByType(inventory, UsableItemType.Utility, "item.prefix.item".i18n(), seen, result);
            }

            return result.OrderBy(e => e.Name).ToList();
        }

        static void EnumerateInventoryByType(
            Kingmaker.Items.ItemsCollection inventory,
            UsableItemType wantedType,
            string prefix,
            HashSet<string> seen,
            List<SpellEntry> result) {
            foreach (var item in inventory) {
                if (item == null || item.Count <= 0) continue;
                var usable = item.Blueprint as BlueprintItemEquipmentUsable;
                if (usable?.Ability == null) continue;
                if (usable.Type != wantedType) continue;
                // Wands track uses via Charges, not stack Count. A spent wand would still have
                // Count=1 but Charges=0 — don't offer it as a UseItem target.
                if (wantedType == UsableItemType.Wand && item.Charges <= 0) continue;

                var guid = usable.Ability.AssetGuid.ToString();
                if (!seen.Add(guid)) continue;

                result.Add(new SpellEntry(
                    FormatWithInternal($"{usable.Ability.Name} {prefix}", usable.Ability),
                    guid, usable.Ability.Icon));
            }
        }
    }
}
