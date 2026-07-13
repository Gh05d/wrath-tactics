using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        public static AbilityData FindAbility(UnitEntityData owner, string abilityGuid) {
            return FindAbilityEx(owner, abilityGuid, out _);
        }

        /// <summary>
        /// Returns ability and whether it's synthetic (variant/not in owner's fact list).
        /// Synthetic abilities must use Rulebook.Trigger — CreateCastCommand silently rejects them.
        /// </summary>
        public static AbilityData FindAbilityEx(UnitEntityData owner, string abilityKey, out bool isSynthetic) {
            isSynthetic = false;
            if (string.IsNullOrEmpty(abilityKey)) return null;

            var parsed = UI.SpellDropdownProvider.ParseKey(abilityKey);

            foreach (var book in owner.Spellbooks) {
                int minLvl = parsed.Level >= 0 ? parsed.Level : 0;
                int maxLvl = parsed.Level >= 0 ? parsed.Level : book.MaxSpellLevel;
                for (int level = minLvl; level <= maxLvl; level++) {
                    foreach (var spell in book.GetKnownSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
                        if (parsed.MetamagicMask != 0) continue; // metamagic → custom spells path only

                        if (!string.IsNullOrEmpty(parsed.VariantGuid)) {
                            var variant = FindVariantBlueprint(spell.Blueprint, parsed.VariantGuid);
                            if (variant != null) {
                                isSynthetic = true;
                                return MakeVariantData(spell, variant);
                            }
                            var conv = FindConversion(spell, parsed.VariantGuid, parsed.ActionType);
                            if (conv == null) continue;
                            isSynthetic = true;
                            return conv;
                        }
                        return spell;
                    }
                    foreach (var spell in book.GetCustomSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
                        int spellMask = (spell.MetamagicData != null && spell.MetamagicData.NotEmpty)
                            ? (int)spell.MetamagicData.MetamagicMask : 0;
                        if (spellMask != parsed.MetamagicMask) continue;

                        // Metamagic-prepared spells with AbilityVariants (e.g. Quickened Summon Monster II
                        // → 1 wolf / 1d3 dogs) need variant resolution on the custom-spells path too.
                        // MakeVariantData clones the parent's MetamagicData (IL-verified), so the variant
                        // carries the metamagic into the cast pipeline.
                        if (!string.IsNullOrEmpty(parsed.VariantGuid)) {
                            var variant = FindVariantBlueprint(spell.Blueprint, parsed.VariantGuid);
                            if (variant != null) {
                                isSynthetic = true;
                                return MakeVariantData(spell, variant);
                            }
                            var conv = FindConversion(spell, parsed.VariantGuid, parsed.ActionType);
                            if (conv == null) continue;
                            isSynthetic = true;
                            return conv;
                        }
                        return spell;
                    }
                    // Special spells — Cleric Domain / Shaman Spirit / Sorcerer Bloodline /
                    // Witch Patron lists. Stored in Spellbook.m_SpecialSpells, not m_KnownSpells.
                    foreach (var spell in book.GetSpecialSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
                        if (parsed.MetamagicMask != 0) continue;

                        if (!string.IsNullOrEmpty(parsed.VariantGuid)) {
                            var variant = FindVariantBlueprint(spell.Blueprint, parsed.VariantGuid);
                            if (variant != null) {
                                isSynthetic = true;
                                return MakeVariantData(spell, variant);
                            }
                            var conv = FindConversion(spell, parsed.VariantGuid, parsed.ActionType);
                            if (conv == null) continue;
                            isSynthetic = true;
                            return conv;
                        }
                        return spell;
                    }
                }
            }

            // Non-spellbook abilities (class abilities: key is variant-guid-as-primary for legacy compatibility)
            foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;

                // Compound keys must NOT short-circuit to the parent: a conversion key
                // (parent>Vconversion[~An]) matches the parent's GUID here but means the
                // conversion, never the bare parent.
                if (ability.Blueprint.AssetGuid.ToString() == parsed.BlueprintGuid
                    && parsed.MetamagicMask == 0
                    && string.IsNullOrEmpty(parsed.VariantGuid))
                    return ability.Data;

                // Variants: legacy keys store the variant GUID as primary; new keys use BlueprintGuid=parent + VariantGuid=variant.
                var variants = GetBlueprintComponent<AbilityVariants>(ability.Blueprint);
                if (variants?.m_Variants != null) {
                    foreach (var variant in variants.Variants) {
                        if (variant == null || parsed.MetamagicMask != 0) continue;
                        bool legacyMatch = string.IsNullOrEmpty(parsed.VariantGuid)
                            && variant.AssetGuid.ToString() == parsed.BlueprintGuid;
                        bool explicitMatch = !string.IsNullOrEmpty(parsed.VariantGuid)
                            && ability.Blueprint.AssetGuid.ToString() == parsed.BlueprintGuid
                            && variant.AssetGuid.ToString() == parsed.VariantGuid;
                        if (legacyMatch || explicitMatch) {
                            isSynthetic = true;
                            return MakeVariantData(ability.Data, variant);
                        }
                    }
                }

                // Conversions (AbilityData.GetConversions) — explicit compound keys only;
                // the dropdown never emits bare conversion GUIDs for class abilities.
                if (!string.IsNullOrEmpty(parsed.VariantGuid) && parsed.MetamagicMask == 0
                    && ability.Blueprint.AssetGuid.ToString() == parsed.BlueprintGuid) {
                    var conv = FindConversion(ability.Data, parsed.VariantGuid, parsed.ActionType);
                    if (conv != null) {
                        isSynthetic = true;
                        return conv;
                    }
                }
            }

            return null;
        }

        // Constructs a variant AbilityData while preserving the parent's spellbook level.
        // The 2-arg ctor `new AbilityData(parent, variant)` chains to the 4-arg base ctor
        // and silently drops `SpellLevelInSpellbook`, so `Spellbook.GetSpellLevel(variant)`
        // falls through to `GetMinSpellLevel(variant.Blueprint)` which returns -1 (variant
        // blueprints aren't in m_KnownSpellLevels — only their parents are). That makes
        // `GetAvailableForCastSpellCount` return 0 for any spellbook-spell variant, blocking
        // the cast at the validator's slot-count gate. Class-ability variants are unaffected
        // because their Spellbook==null branch skips the gate.
        static AbilityData MakeVariantData(AbilityData parent, BlueprintAbility variant) {
            var data = new AbilityData(parent, variant);
            data.SpellLevelInSpellbook = parent.SpellLevelInSpellbook;
            return data;
        }

        // Resolves a conversion of `parent` by blueprint GUID against the engine's runtime
        // conversion list (AbilityData.GetConversions — variants, spontaneous conversion, and
        // third-party additions like TTT's AddSpecificSpellConversion/AbilityActionTypeConversion
        // via their GetConversions postfix). Called as fallback after FindVariantBlueprint missed,
        // so ordinary variants never take this path.
        //
        // actionType >= 0 additionally matches the conversion's action type — required for
        // same-blueprint conversions (e.g. Quick Channel's move-action channel), where the GUID
        // alone is ambiguous with the parent itself. Without the discriminator, same-blueprint
        // conversions are skipped rather than resolved arbitrarily.
        //
        // The engine builds the conversion list fresh per call (TempList), so normalizing
        // SpellLevelInSpellbook on the returned instance mutates nothing shared — same fix as
        // MakeVariantData (the 2-arg AbilityData ctor drops the parent's spellbook level).
        static AbilityData FindConversion(AbilityData parent, string conversionGuid, int actionType) {
            foreach (var conv in parent.GetConversions()) {
                if (conv?.Blueprint == null) continue;
                if (conv.Blueprint.AssetGuid.ToString() != conversionGuid) continue;
                if (actionType >= 0 && (int)conv.ActionType != actionType) continue;
                if (actionType < 0 && conv.Blueprint == parent.Blueprint) continue;
                conv.SpellLevelInSpellbook = parent.SpellLevelInSpellbook;
                return conv;
            }
            return null;
        }

        // Resolves a variant-guid against either AbilityVariants (static m_Variants[]) or
        // AbilityShadowSpell (runtime SpellList × MaxSpellLevel × School). Returns null if
        // the parent carries neither component or the guid doesn't match a registered variant.
        static BlueprintAbility FindVariantBlueprint(BlueprintAbility parent, string variantGuid) {
            var variants = GetBlueprintComponent<AbilityVariants>(parent);
            if (variants?.m_Variants != null) {
                foreach (var variant in variants.Variants) {
                    if (variant == null) continue;
                    if (variant.AssetGuid.ToString() == variantGuid) return variant;
                }
            }

            var shadow = GetBlueprintComponent<AbilityShadowSpell>(parent);
            if (shadow != null && shadow.SpellList?.Get() != null) {
                foreach (var variant in shadow.GetAvailableSpells()) {
                    if (variant == null) continue;
                    if (variant.AssetGuid.ToString() == variantGuid) return variant;
                }
            }

            return null;
        }

        static T GetBlueprintComponent<T>(BlueprintScriptableObject bp) where T : BlueprintComponent {
            if (bp?.ComponentsArray == null) return null;
            foreach (var c in bp.ComponentsArray) {
                if (c is T typed) return typed;
            }
            return null;
        }
    }
}
