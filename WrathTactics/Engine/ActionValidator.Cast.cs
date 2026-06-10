using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.Utility;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        public static AbilityData ResolveCastSpellChain(
            UnitEntityData owner,
            ResolvedTarget target,
            ActionDef action,
            out ItemEntity inventorySource,
            out string usedAbilityId) {

            inventorySource = null;
            usedAbilityId = null;

            if (!string.IsNullOrEmpty(action.AbilityId)) {
                var primary = FindCastSpellSource(owner, target, action.AbilityId, action.Sources, out inventorySource);
                if (primary != null) {
                    usedAbilityId = action.AbilityId;
                    return primary;
                }
            }

            if (action.FallbackAbilityIds == null) return null;
            foreach (var id in action.FallbackAbilityIds) {
                if (string.IsNullOrEmpty(id)) continue;
                var ability = FindCastSpellSource(owner, target, id, action.Sources, out inventorySource);
                if (ability != null) {
                    usedAbilityId = id;
                    return ability;
                }
            }

            inventorySource = null;
            return null;
        }

        /// <summary>
        /// Resolves which source should cast the requested spell. Mirrors FindBestHealEx but
        /// the spell is fixed (not "best heal"); selects the first viable source in priority order:
        ///   1. Spellbook slot         (Spell bit)
        ///   2. Wand in quickslot      (Spell bit, implicit fallback like Heal)
        ///   3. Scroll from inventory  (Scroll bit, UMD-gated)
        ///   4. Potion from inventory  (Potion bit, self-only)
        /// Matching is STRICT on blueprint GUID + variant + metamagic — the compoundKey contains
        /// all three and FindAbility parses them.
        ///
        /// `target` is used by the Scroll (UMD gate) and Potion (self-only filter) branches that
        /// Tasks 4 and 5 slot into the same method — unused at the Spell-bit-only stage.
        ///
        /// Returns null if no source matches. Sets `inventorySource` to a consumable ItemEntity
        /// for Scroll/Potion picks (callers must call ConsumeInventoryItem); null for spellbook
        /// and wand picks (wand charges decrement via the cast command pipeline automatically).
        /// </summary>
        public static AbilityData FindCastSpellSource(
            UnitEntityData owner,
            ResolvedTarget target,
            string compoundKey,
            SpellSourceMask mask,
            out ItemEntity inventorySource) {

            inventorySource = null;
            if (string.IsNullOrEmpty(compoundKey)) return null;

            bool wantSpell  = (mask & SpellSourceMask.Spell)  != 0;
            bool wantScroll = (mask & SpellSourceMask.Scroll) != 0;
            bool wantPotion = (mask & SpellSourceMask.Potion) != 0;

            // Parse the compound key once — reused by the wand branch (and by the scroll/potion
            // branches added in Tasks 4 & 5). FindAbility re-parses internally; unavoidable
            // without a new overload, but we avoid a third parse in the wand loop.
            var parsed = UI.SpellDropdownProvider.ParseKey(compoundKey);

            // 1. Spellbook slot — use the existing FindAbility which parses level/variant/metamagic.
            if (wantSpell) {
                var ability = FindAbility(owner, compoundKey);
                if (ability != null && ability.Spellbook != null) {
                    // Spellbook::GetAvailableForCastSpellCount loops m_MemorizedSpells[level]
                    // and counts slots where `slot.SpellShell.Blueprint == ability.Blueprint`
                    // (IL_0056-0061, raw reference equality). For variant AbilityData (vanilla
                    // AbilityVariants, AbilityShadowSpell, metamagic-prepared variants) our
                    // ability.Blueprint is the variant blueprint while the prepared slot is
                    // keyed on the parent — so reference equality always fails and the count
                    // returns 0. Query against ConvertedFrom (set by the 2-arg AbilityData ctor
                    // to point at the parent) when present; the cast itself still runs on the
                    // variant. Property used (not m_ConvertedFrom field) because field access
                    // requires the publicizer at test runtime and breaks Mono's Assembly-CSharp
                    // loading; the public property exists in vanilla and works in both contexts.
                    var spellForSlots = ability.ConvertedFrom ?? ability;
                    int slots = ability.Spellbook.GetAvailableForCastSpellCount(spellForSlots);
                    if (slots != 0 && ability.IsAvailable) return ability;
                    if (!ability.IsAvailable) {
                        Log.Engine.Trace($"FindCastSpellSource: {owner.CharacterName} spellbook {ability.Name} engine-unavailable ({ability.GetUnavailableReason()})");
                    } else {
                        // Slot count returned 0 — spell isn't currently castable. With the
                        // ConvertedFrom fix above this is almost always "prepared slot already
                        // spent this rest" rather than a variant-mismatch artifact.
                        int maskInt = ability.MetamagicData != null ? (int)ability.MetamagicData.MetamagicMask : 0;
                        Log.Engine.Trace($"FindCastSpellSource: {owner.CharacterName} spellbook {ability.Name} no available slots (variant={ability.Blueprint.AssetGuid}, metamagicMask={maskInt})");
                    }
                }

                // Class ability path (no spellbook, no inventory source, resource-gated).
                // Guard against wand abilities, which also have Spellbook==null but carry a SourceItem —
                // those must go through the wand branch below for a proper charge check.
                if (ability != null && ability.Spellbook == null && ability.SourceItem == null) {
                    var resource = ability.Blueprint.GetComponent<AbilityResourceLogic>();
                    bool resourceOk = true;
                    if (resource != null && resource.IsSpendResource) {
                        var required = (BlueprintScriptableObject)ability.OverrideRequiredResource
                            ?? resource.RequiredResource;
                        if (required != null) {
                            int available = owner.Resources.GetResourceAmount(required);
                            int cost = resource.CalculateCost(ability);
                            if (available < cost) resourceOk = false;
                        }
                    }
                    if (resourceOk) {
                        if (ability.IsAvailable) return ability;
                        Log.Engine.Trace($"FindCastSpellSource: {owner.CharacterName} ability {ability.Name} engine-unavailable ({ability.GetUnavailableReason()})");
                    }
                    // resource exhausted or engine-gated -> fall through to wand/scroll/potion branches (if enabled by mask)
                }

                // 2. Wand in quickslot — search owner.Abilities.RawFacts for an item-backed ability
                // whose blueprint GUID matches the parsed rule key and that has charges remaining.
                // If the rule key carries metamagic or a variant, skip the wand search entirely —
                // Wrath ships no wands with either.
                if (parsed.MetamagicMask == 0 && string.IsNullOrEmpty(parsed.VariantGuid)) {
                    foreach (var fact in owner.Abilities.RawFacts) {
                        var data = fact.Data;
                        if (data?.SourceItem == null) continue;
                        if (data.SourceItem.Charges <= 0) continue;
                        if (fact.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
                        if (!data.IsAvailable) {
                            Log.Engine.Trace($"FindCastSpellSource: {owner.CharacterName} wand {data.Name} engine-unavailable ({data.GetUnavailableReason()})");
                            continue;
                        }
                        return data;
                    }
                }
            }

            // 3. Scroll from inventory — strict match on blueprint GUID + metamagic + variant.
            // UMD-gated: if the spell is not on the caster's class list, require UMD + 11 >= DC.
            // Unlike Heal, no "risky fallback" — scroll is simply skipped on UMD fail.
            // Strict match: scrolls/potions never carry metamagic or variant in Wrath, so if
            // the rule key encodes either, skip the inventory scan entirely (mirrors the wand
            // branch's outer guard above).
            var inventory = Kingmaker.Game.Instance?.Player?.Inventory;
            if (inventory != null && (wantScroll || wantPotion)
                && parsed.MetamagicMask == 0
                && string.IsNullOrEmpty(parsed.VariantGuid)) {
                foreach (var item in inventory) {
                    if (item == null || item.Count <= 0) continue;
                    var usable = item.Blueprint as BlueprintItemEquipmentUsable;
                    if (usable?.Ability == null) continue;
                    if (usable.Ability.AssetGuid.ToString() != parsed.BlueprintGuid) continue;

                    bool isScroll = usable.Type == UsableItemType.Scroll;
                    bool isPotion = usable.Type == UsableItemType.Potion;

                    if (isScroll && !wantScroll) continue;
                    if (isPotion && !wantPotion) continue;
                    if (!isScroll && !isPotion) continue;

                    if (isScroll) {
                        // UMD gate mirrors Heal but skips on fail (no fallback-burn).
                        bool canCastNatively = CanCastSpellFromSpellbook(owner, usable.Ability);
                        if (!canCastNatively) {
                            int dc = 20 + usable.CasterLevel;
                            int umd = owner.Stats.SkillUseMagicDevice.ModifiedValue;
                            if (umd + 11 < dc) {
                                Log.Engine.Trace($"CastSpell scroll {item.Blueprint.name}: UMD {umd} vs DC {dc} (< 50%), skipping");
                                continue;
                            }
                        }

                        var scrollAbility = new AbilityData(usable.Ability, owner.Descriptor) {
                            OverrideCasterLevel = usable.CasterLevel,
                            OverrideSpellLevel = usable.SpellLevel,
                        };
                        inventorySource = item;
                        return scrollAbility;
                    }

                    if (isPotion) {
                        // Potions are self-only in this model (Wrath's potion ability data almost always
                        // has CanTargetSelf=true only). Skip silently when target isn't the owner.
                        bool targetIsSelf = !target.IsPoint && target.Unit == owner;
                        if (!targetIsSelf) {
                            Log.Engine.Trace($"CastSpell potion {item.Blueprint.name}: target is not self, skipping");
                            continue;
                        }
                        var potionAbility = new AbilityData(usable.Ability, owner.Descriptor) {
                            OverrideCasterLevel = usable.CasterLevel,
                            OverrideSpellLevel = usable.SpellLevel,
                        };
                        inventorySource = item;
                        return potionAbility;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// True iff the unit has the given spell known/prepared in one of their spellbooks
        /// AND still has a slot available to cast it right now. Used to bypass the UMD
        /// check on scrolls — a character who can cast the spell themselves doesn't need
        /// an activation check. GetAvailableForCastSpellCount handles prepared, spontaneous,
        /// Arcanist-hybrid, and opposition-school cases uniformly.
        /// </summary>
        static bool CanCastSpellFromSpellbook(UnitEntityData owner, BlueprintAbility spell) {
            if (spell == null || owner?.Spellbooks == null) return false;
            foreach (var book in owner.Spellbooks) {
                int maxLevel = book.MaxSpellLevel;
                for (int level = 0; level <= maxLevel; level++) {
                    foreach (var known in book.GetKnownSpells(level)) {
                        if (known?.Blueprint != spell) continue;
                        if (book.GetAvailableForCastSpellCount(known) != 0) return true;
                    }
                    // Custom spells = metamagic-prepared / fused variants. Without this
                    // loop a spell prepared only as a metamagic variant counts as "not
                    // castable natively" and its scroll is pushed through the UMD gate.
                    foreach (var custom in book.GetCustomSpells(level)) {
                        if (custom?.Blueprint != spell) continue;
                        if (book.GetAvailableForCastSpellCount(custom) != 0) return true;
                    }
                    foreach (var special in book.GetSpecialSpells(level)) {
                        if (special?.Blueprint != spell) continue;
                        if (book.GetAvailableForCastSpellCount(special) != 0) return true;
                    }
                }
            }
            return false;
        }
    }
}
