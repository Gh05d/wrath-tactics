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
                            var variants = GetBlueprintComponent<AbilityVariants>(spell.Blueprint);
                            if (variants?.m_Variants == null) continue;
                            foreach (var variant in variants.Variants) {
                                if (variant == null) continue;
                                if (variant.AssetGuid.ToString() != parsed.VariantGuid) continue;
                                isSynthetic = true;
                                return MakeVariantData(spell, variant);
                            }
                            continue;
                        }
                        return spell;
                    }
                    foreach (var spell in book.GetCustomSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
                        int spellMask = (spell.MetamagicData != null && spell.MetamagicData.NotEmpty)
                            ? (int)spell.MetamagicData.MetamagicMask : 0;
                        if (spellMask == parsed.MetamagicMask)
                            return spell;
                    }
                    // Special spells — Cleric Domain / Shaman Spirit / Sorcerer Bloodline /
                    // Witch Patron lists. Stored in Spellbook.m_SpecialSpells, not m_KnownSpells.
                    foreach (var spell in book.GetSpecialSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
                        if (parsed.MetamagicMask != 0) continue;

                        if (!string.IsNullOrEmpty(parsed.VariantGuid)) {
                            var variants = GetBlueprintComponent<AbilityVariants>(spell.Blueprint);
                            if (variants?.m_Variants == null) continue;
                            foreach (var variant in variants.Variants) {
                                if (variant == null) continue;
                                if (variant.AssetGuid.ToString() != parsed.VariantGuid) continue;
                                isSynthetic = true;
                                return MakeVariantData(spell, variant);
                            }
                            continue;
                        }
                        return spell;
                    }
                }
            }

            // Non-spellbook abilities (class abilities: key is variant-guid-as-primary for legacy compatibility)
            foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;

                if (ability.Blueprint.AssetGuid.ToString() == parsed.BlueprintGuid && parsed.MetamagicMask == 0)
                    return ability.Data;

                // Variants: legacy keys store the variant GUID as primary; new keys use BlueprintGuid=parent + VariantGuid=variant.
                var variants = GetBlueprintComponent<AbilityVariants>(ability.Blueprint);
                if (variants?.m_Variants == null) continue;
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

        static T GetBlueprintComponent<T>(BlueprintScriptableObject bp) where T : BlueprintComponent {
            if (bp?.ComponentsArray == null) return null;
            foreach (var c in bp.ComponentsArray) {
                if (c is T typed) return typed;
            }
            return null;
        }
    }
}
