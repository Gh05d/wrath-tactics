using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        public static AbilityData FindAbility(UnitEntityData owner, string abilityGuid) {
            return FindAbilityEx(owner, abilityGuid, out _);
        }

        /// <summary>
        /// Returns ability and whether it's synthetic (variant/not in owner's fact list).
        /// Synthetic abilities must use Rulebook.Trigger — CreateCastCommand silently rejects them.
        ///
        /// Multi-spellbook units (Magus + Wizard, standalone mythic books) hold a separate copy
        /// of the same spell per book. The scan prefers the first copy that is castable RIGHT NOW
        /// (HasCastableSlot) across all spellbooks that hold the spell at the key's level; only
        /// when none is castable does it fall back to the first match, so callers still get a
        /// non-null ability for logging and wand/scroll fallthrough. First-book-wins starved
        /// later books' slots (v1.27.1 bug). Known limits/side effects:
        ///  - Level pinning: a book holding the spell at a DIFFERENT level than the key's @L
        ///    segment is never scanned (pre-existing; widen the level window if a report hits it).
        ///  - The returned copy tracks live slot state, so DC/CL-derived metrics
        ///    (SpellDCMinusSave via PickMetrics) deliberately follow the copy that would cast.
        /// </summary>
        public static AbilityData FindAbilityEx(UnitEntityData owner, string abilityKey, out bool isSynthetic) {
            isSynthetic = false;
            if (string.IsNullOrEmpty(abilityKey)) return null;

            var parsed = UI.SpellDropdownProvider.ParseKey(abilityKey);

            AbilityData fallback = null;
            bool fallbackSynthetic = false;

            // Resolves the parsed key's variant/conversion segment against a matched book spell.
            // Null = the key doesn't resolve on this copy (e.g. conversion not offered here).
            AbilityData Resolve(AbilityData spell, out bool synthetic) {
                synthetic = false;
                if (string.IsNullOrEmpty(parsed.VariantGuid)) return spell;

                // Metamagic-prepared spells with AbilityVariants (e.g. Quickened Summon Monster II
                // → 1 wolf / 1d3 dogs) need variant resolution on the custom-spells path too.
                // MakeVariantData clones the parent's MetamagicData (IL-verified), so the variant
                // carries the metamagic into the cast pipeline.
                var variant = FindVariantBlueprint(spell.Blueprint, parsed.VariantGuid);
                if (variant != null) {
                    synthetic = true;
                    return MakeVariantData(spell, variant);
                }
                var conv = FindConversion(spell, parsed.VariantGuid, parsed.ActionType);
                if (conv == null) return null;
                synthetic = true;
                return conv;
            }

            // Non-null = return this candidate now (castable from its book); otherwise the
            // first resolved copy is remembered as the nothing-castable fallback.
            AbilityData Consider(AbilityData spell, out bool synthetic) {
                synthetic = false;
                // Slot state of the BOOK copy. Every candidate — plain, variant, conversion —
                // slot-probes via ConvertedFrom back to this very copy (see HasCastableSlot),
                // so a dry book can skip Resolve entirely once a fallback exists: no
                // MakeVariantData allocations, no engine GetConversions (fresh TempList plus
                // third-party postfixes like TTT's) on the per-tick path.
                bool bookHasSlots = spell.Spellbook.GetAvailableForCastSpellCount(spell) != 0;
                if (!bookHasSlots && fallback != null) return null;

                var candidate = Resolve(spell, out synthetic);
                if (candidate == null) return null;
                if (HasCastableSlot(candidate)) return candidate;
                if (bookHasSlots) {
                    // Slots are there but the engine gates the cast (forbidden book, silence, …).
                    // Without this trace the caller's dry-path log would misattribute the block
                    // to spent slots.
                    Log.Engine.Trace($"FindAbilityEx: {owner.CharacterName} {candidate.Name} has slots in {spell.Spellbook.Blueprint?.name} but is engine-unavailable ({candidate.GetUnavailableReason()})");
                }
                if (fallback == null) {
                    fallback = candidate;
                    fallbackSynthetic = synthetic;
                }
                return null;
            }

            foreach (var book in owner.Spellbooks) {
                int minLvl = parsed.Level >= 0 ? parsed.Level : 0;
                int maxLvl = parsed.Level >= 0 ? parsed.Level : book.MaxSpellLevel;
                for (int level = minLvl; level <= maxLvl; level++) {
                    foreach (var spell in book.GetKnownSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
                        if (parsed.MetamagicMask != 0) continue; // metamagic → custom spells path only

                        var hit = Consider(spell, out bool synthetic);
                        if (hit != null) { isSynthetic = synthetic; return hit; }
                    }
                    foreach (var spell in book.GetCustomSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
                        int spellMask = (spell.MetamagicData != null && spell.MetamagicData.NotEmpty)
                            ? (int)spell.MetamagicData.MetamagicMask : 0;
                        if (spellMask != parsed.MetamagicMask) continue;

                        var hit = Consider(spell, out bool synthetic);
                        if (hit != null) { isSynthetic = synthetic; return hit; }
                    }
                    // Special spells — Cleric Domain / Shaman Spirit / Sorcerer Bloodline /
                    // Witch Patron lists. Stored in Spellbook.m_SpecialSpells, not m_KnownSpells.
                    foreach (var spell in book.GetSpecialSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
                        if (parsed.MetamagicMask != 0) continue;

                        var hit = Consider(spell, out bool synthetic);
                        if (hit != null) { isSynthetic = synthetic; return hit; }
                    }
                }
            }

            if (fallback != null) {
                isSynthetic = fallbackSynthetic;
                return fallback;
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

        /// <summary>
        /// Shared "castable right now from its spellbook" gate — the ONLY slot/availability
        /// check for spellbook copies. FindAbilityEx's preference scan and FindCastSpellSource's
        /// final gate must never disagree (a drift silently re-opens the wand/scroll fallthrough
        /// this gate exists to prevent), so both call this. Caller guarantees Spellbook != null.
        ///
        /// Slot probe goes through ConvertedFrom: Spellbook::GetAvailableForCastSpellCount
        /// compares blueprint refs strictly (IL_0056-0061), and variant/conversion AbilityData
        /// carries the variant blueprint while the prepared slot is keyed on the parent.
        /// != 0 (not > 0): cantrips return the -1 sentinel.
        /// </summary>
        internal static bool HasCastableSlot(AbilityData ability) {
            var forSlots = ability.ConvertedFrom ?? ability;
            return ability.Spellbook.GetAvailableForCastSpellCount(forSlots) != 0 && ability.IsAvailable;
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
