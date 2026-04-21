using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.Utility;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static class ActionValidator {
        public static bool CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
            if (!target.IsValid && RequiresValidTarget(action.Type))
                return false;

            if (target.IsPoint) {
                switch (action.Type) {
                    case ActionType.CastSpell: {
                        ItemEntity _unused;
                        var ability = FindCastSpellSource(owner, target, action.AbilityId, action.Sources, out _unused);
                        if (ability == null) return false;
                        if (!ability.CanTargetPoint) {
                            Log.Engine.Trace($"CanCastAbilityAtPoint: {owner.CharacterName} ability '{ability.Name}' is not point-castable");
                            return false;
                        }
                        return true;
                    }
                    case ActionType.CastAbility:
                        return CanCastAbilityAtPoint(action.AbilityId, owner);
                    case ActionType.UseItem:
                        return CanUseItemAtPoint(action.AbilityId, owner);
                    default:
                        return false;
                }
            }

            var unit = target.Unit;
            switch (action.Type) {
                case ActionType.CastSpell: {
                    ItemEntity _unused;
                    return FindCastSpellSource(owner, target, action.AbilityId, action.Sources, out _unused) != null;
                }
                case ActionType.CastAbility:
                    return CanCastSpell(action.AbilityId, owner, unit);
                case ActionType.UseItem:
                    return CanUseItem(action.AbilityId, owner, unit);
                case ActionType.ToggleActivatable:
                    return CanToggleActivatable(action.AbilityId, owner, action.ToggleMode);
                case ActionType.AttackTarget:
                    return unit != null && unit.HPLeft > 0;
                case ActionType.Heal:
                    return FindBestHeal(owner, action.HealMode, action.HealSources) != null;
                case ActionType.ThrowSplash:
                    return unit != null && SplashItemResolver.FindBest(owner, action.SplashMode).HasValue;
                case ActionType.DoNothing:
                    return true;
                default:
                    return false;
            }
        }

        static bool RequiresValidTarget(ActionType type) {
            return type != ActionType.ToggleActivatable
                && type != ActionType.Heal
                && type != ActionType.DoNothing;
        }

        static bool CanCastAbilityAtPoint(string abilityGuid, UnitEntityData owner) {
            var ability = FindAbility(owner, abilityGuid);
            if (ability == null) return false;
            if (!ability.CanTargetPoint) {
                Log.Engine.Trace($"CanCastAbilityAtPoint: {owner.CharacterName} ability '{ability.Name}' is not point-castable");
                return false;
            }
            if (ability.Spellbook != null
                && ability.Spellbook.GetAvailableForCastSpellCount(ability) <= 0)
                return false;
            var resource = ability.Blueprint.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityResourceLogic>();
            if (resource != null && resource.IsSpendResource) {
                var required = (Kingmaker.Blueprints.BlueprintScriptableObject)ability.OverrideRequiredResource
                    ?? resource.RequiredResource;
                if (required != null) {
                    int available = owner.Resources.GetResourceAmount(required);
                    int cost = resource.CalculateCost(ability);
                    if (available < cost) return false;
                }
            }
            return true;
        }

        static bool CanUseItemAtPoint(string abilityGuid, UnitEntityData owner) {
            var ability = FindAbilityFromItem(owner, abilityGuid);
            if (ability == null) return false;
            if (!ability.CanTargetPoint) return false;
            if (ability.SourceItem != null && ability.SourceItem.Charges <= 0) return false;
            return true;
        }

        static bool CanCastSpell(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            if (string.IsNullOrEmpty(abilityGuid)) {
                Log.Engine.Warn($"CastSpell/CastAbility has EMPTY AbilityId for {owner.CharacterName} — user didn't pick an ability!");
                return false;
            }
            var ability = FindAbility(owner, abilityGuid);
            if (ability == null) {
                Log.Engine.Warn($"FindAbility FAILED for {owner.CharacterName}, guid={abilityGuid}");
                return false;
            }
            Log.Engine.Trace($"FindAbility OK: {ability.Name} for {owner.CharacterName}");

            // Spellbook check: GetAvailableForCastSpellCount handles prepared (memorized+Available),
            // spontaneous (GetSpontaneousSlots), and Arcanist-hybrid correctly. GetSpellsPerDay is
            // the MAX per-day capacity and never decrements — using it here lets exhausted casters
            // loop-queue the same spell forever.
            if (ability.Spellbook != null) {
                if (ability.Spellbook.GetAvailableForCastSpellCount(ability) <= 0) {
                    Log.Engine.Trace($"CanCastSpell: {owner.CharacterName} has no remaining slots for {ability.Name}");
                    return false;
                }
            }

            // Resource check for class abilities (Lay on Hands, Channel Energy, Bloodline powers,
            // bardic performance, etc.) — these have AbilityResourceLogic and no Spellbook.
            // Mirrors AbilityResourceLogic.Spend: honors OverrideRequiredResource, skips when
            // IsSpendResource is false (display-only resources).
            var resource = ability.Blueprint.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityResourceLogic>();
            if (resource != null && resource.IsSpendResource) {
                var required = (Kingmaker.Blueprints.BlueprintScriptableObject)ability.OverrideRequiredResource
                    ?? resource.RequiredResource;
                if (required != null) {
                    int available = owner.Resources.GetResourceAmount(required);
                    int cost = resource.CalculateCost(ability);
                    if (available < cost) {
                        Log.Engine.Trace($"CanCastSpell: {owner.CharacterName} short on {required.name} for {ability.Name} ({available}/{cost})");
                        return false;
                    }
                }
            }

            if (target != null && !ability.CanTarget(new TargetWrapper(target)))
                return false;

            return true;
        }

        static bool CanUseItem(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            var ability = FindAbilityFromItem(owner, abilityGuid);
            if (ability == null) return false;
            if (ability.SourceItem != null && ability.SourceItem.Charges <= 0) return false;
            if (target != null && !ability.CanTarget(new TargetWrapper(target)))
                return false;
            return true;
        }

        static bool CanToggleActivatable(string abilityGuid, UnitEntityData owner, ToggleMode mode) {
            var activatable = FindActivatable(owner, abilityGuid);
            if (activatable == null) return false;
            if (mode == ToggleMode.Off)
                return activatable.IsOn;
            return !activatable.IsOn && activatable.IsAvailable;
        }

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
                            var variants = GetBlueprintComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityVariants>(spell.Blueprint);
                            if (variants?.m_Variants == null) continue;
                            foreach (var variant in variants.Variants) {
                                if (variant == null) continue;
                                if (variant.AssetGuid.ToString() != parsed.VariantGuid) continue;
                                isSynthetic = true;
                                return new AbilityData(spell, variant);
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
                }
            }

            // Non-spellbook abilities (class abilities: key is variant-guid-as-primary for legacy compatibility)
            foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;

                if (ability.Blueprint.AssetGuid.ToString() == parsed.BlueprintGuid && parsed.MetamagicMask == 0)
                    return ability.Data;

                // Variants: legacy keys store the variant GUID as primary; new keys use BlueprintGuid=parent + VariantGuid=variant.
                var variants = GetBlueprintComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityVariants>(ability.Blueprint);
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
                        return new AbilityData(ability.Data, variant);
                    }
                }
            }

            return null;
        }

        static AbilityData FindAbilityFromItem(UnitEntityData owner, string abilityGuid) {
            foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Blueprint.AssetGuid.ToString() == abilityGuid && ability.Data.SourceItem != null)
                    return ability.Data;
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

        public static ActivatableAbility FindActivatable(UnitEntityData owner, string abilityGuid) {
            if (string.IsNullOrEmpty(abilityGuid)) return null;
            return owner.ActivatableAbilities.RawFacts
                .FirstOrDefault(a => a.Blueprint.AssetGuid.ToString() == abilityGuid);
        }

        public static AbilityData FindBestHeal(UnitEntityData owner, HealMode mode = HealMode.Any, HealSourceMask sources = HealSourceMask.All) {
            return FindBestHealEx(owner, mode, sources, out _);
        }

        /// <summary>
        /// Returns best heal ability plus the inventory ItemEntity it came from (null for
        /// spellbook spells, class abilities, and quickslot/equipped wands). Caller must
        /// consume the item via Inventory.Remove after casting — synthesized AbilityData
        /// from inventory doesn't auto-consume through Rulebook.Trigger.
        ///
        /// `sources` masks which classes of heal are eligible. Spell covers spellbook casts,
        /// class abilities, and wand/staff activations (all character-driven). Scroll and
        /// Potion are inventory consumables. Default All keeps the legacy behaviour.
        /// </summary>
        public static AbilityData FindBestHealEx(UnitEntityData owner, HealMode mode, HealSourceMask sources, out ItemEntity inventorySource) {
            inventorySource = null;
            var heals = new List<(AbilityData ability, int priority, ItemEntity source, HealSourceMask category)>();
            bool wantSpell  = (sources & HealSourceMask.Spell)  != 0;
            bool wantScroll = (sources & HealSourceMask.Scroll) != 0;
            bool wantPotion = (sources & HealSourceMask.Potion) != 0;

            // Search spellbooks for cure/heal spells
            if (wantSpell) foreach (var book in owner.Spellbooks) {
                int maxLevel = book.MaxSpellLevel;
                for (int level = 0; level <= maxLevel; level++) {
                    foreach (var spell in book.GetKnownSpells(level)) {
                        if (IsHealingSpell(spell.Blueprint)) {
                            // GetSpellsPerDay is MAX capacity — always positive for a caster who
                            // has ANY level-N slot, even if the specific spell isn't prepared.
                            // GetAvailableForCastSpellCount is the correct per-spell "can I cast
                            // this right now" check (handles prepared + spontaneous uniformly).
                            if (book.GetAvailableForCastSpellCount(spell) > 0)
                                heals.Add((spell, 100 + level * 10, null, HealSourceMask.Spell)); // highest priority: spellbook spells
                        }
                    }
                }
            }

            // Class abilities (Lay on Hands, Channel Positive Energy)
            // Must check resource availability — some abilities are per-day
            if (wantSpell) foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;
                if (!IsHealingSpell(ability.Blueprint)) continue;

                // Check resource cost — skip if no uses left
                var resource = ability.Data.Blueprint.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityResourceLogic>();
                if (resource?.RequiredResource != null) {
                    int available = owner.Resources.GetResourceAmount(resource.RequiredResource);
                    if (available < resource.Amount) {
                        Log.Engine.Trace($"Skipping heal ability {ability.Blueprint.name} for {owner.CharacterName}: resource {resource.RequiredResource.name}={available}/{resource.Amount}");
                        continue;
                    }
                }

                heals.Add((ability.Data, 80, null, HealSourceMask.Spell)); // next priority: class features
            }

            // Item-backed abilities (wands, staves, equipped healing items)
            if (wantSpell) foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Data.SourceItem == null) continue;
                if (ability.Data.SourceItem.Charges <= 0) continue;
                if (IsHealingSpell(ability.Blueprint))
                    heals.Add((ability.Data, 30, null, HealSourceMask.Spell)); // wands/staves — character-driven
            }

            // Healing potions/scrolls from inventory
            var inventory = Kingmaker.Game.Instance?.Player?.Inventory;
            int invTotal = 0, invUsable = 0, invHealing = 0;
            // Scrolls the user can't reliably activate (UMD < DC - 10 AND no native cast)
            // are collected here and only folded into the final candidate list if nothing
            // better is available — risky scroll beats no heal at all.
            var fallbackScrolls = new List<(AbilityData ability, int priority, ItemEntity source, HealSourceMask category)>();
            if (inventory != null && (wantScroll || wantPotion)) {
                foreach (var item in inventory) {
                    if (item == null || item.Count <= 0) continue;
                    invTotal++;
                    var usable = item.Blueprint as Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable;
                    if (usable == null || usable.Ability == null) continue;
                    invUsable++;
                    string itemName = item.Blueprint.name ?? "?";
                    string abilityName = usable.Ability.Name ?? usable.Ability.name ?? "?";
                    if (!IsHealingSpell(usable.Ability)) {
                        Log.Engine.Trace($"  inventory item {itemName} (ability '{abilityName}'): NOT a healing spell");
                        continue;
                    }
                    invHealing++;

                    bool isPotion = usable.Type == Kingmaker.Blueprints.Items.Equipment.UsableItemType.Potion;
                    bool isScroll = usable.Type == Kingmaker.Blueprints.Items.Equipment.UsableItemType.Scroll;
                    if (isPotion && !wantPotion) continue;
                    if (isScroll && !wantScroll) continue;
                    if (!isPotion && !isScroll) continue; // ignore other inventory-usable types for heal

                    // Synthesize AbilityData with item's caster/spell level overrides
                    var itemAbility = new AbilityData(usable.Ability, owner.Descriptor) {
                        OverrideCasterLevel = usable.CasterLevel,
                        OverrideSpellLevel = usable.SpellLevel,
                    };

                    int priority = isPotion ? 10 : 20;
                    var category = isPotion ? HealSourceMask.Potion : HealSourceMask.Scroll;

                    // UMD gate for scrolls: d20 + UMD vs DC 20 + scroll.CasterLevel. Ten outcomes
                    // (11..20) clear threshold when UMD + 11 >= DC, so UMD + 11 < DC is < 50% success.
                    // Bypass the check only when the character can cast this spell right now from
                    // their own spellbook (known + available slot) — mere spell-list membership
                    // isn't enough, because running out of slots is common in long fights.
                    if (isScroll) {
                        bool canCastNatively = CanCastSpellFromSpellbook(owner, usable.Ability);
                        if (!canCastNatively) {
                            int dc = 20 + usable.CasterLevel;
                            int umd = owner.Stats.SkillUseMagicDevice.ModifiedValue;
                            if (umd + 11 < dc) {
                                Log.Engine.Trace($"  inventory item {itemName}: deferring scroll — UMD {umd} vs DC {dc} (< 50% success), last-resort only");
                                fallbackScrolls.Add((itemAbility, priority, item, category));
                                continue;
                            }
                        }
                    }

                    Log.Engine.Trace($"  inventory item {itemName} (ability '{abilityName}'): IS heal — added");
                    heals.Add((itemAbility, priority, item, category));
                }
            }

            if (heals.Count == 0 && fallbackScrolls.Count > 0) {
                Log.Engine.Debug($"FindBestHeal for {owner.CharacterName}: no safe heal — falling back to {fallbackScrolls.Count} UMD-risky scroll(s)");
                heals.AddRange(fallbackScrolls);
            }

            Log.Engine.Debug($"FindBestHeal for {owner.CharacterName}: total inventory items={invTotal}, usable={invUsable}, healing={invHealing}, heals candidates total={heals.Count}");
            if (heals.Count == 0) return null;

            (AbilityData ability, int priority, ItemEntity source, HealSourceMask category) pick;
            switch (mode) {
                case HealMode.Weakest:
                    pick = heals.OrderBy(h => h.priority).First();
                    break;
                case HealMode.Strongest:
                case HealMode.Any:
                default:
                    pick = heals.OrderByDescending(h => h.priority).First();
                    break;
            }
            inventorySource = pick.source;
            return pick.ability;
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
                if (ability != null
                    && ability.Spellbook != null
                    && ability.Spellbook.GetAvailableForCastSpellCount(ability) > 0) {
                    return ability;
                }

                // Class ability path (no spellbook, no inventory source, resource-gated).
                // Guard against wand abilities, which also have Spellbook==null but carry a SourceItem —
                // those must go through the wand branch below for a proper charge check.
                if (ability != null && ability.Spellbook == null && ability.SourceItem == null) {
                    var resource = ability.Blueprint.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityResourceLogic>();
                    if (resource == null || !resource.IsSpendResource) return ability;
                    var required = (Kingmaker.Blueprints.BlueprintScriptableObject)ability.OverrideRequiredResource
                        ?? resource.RequiredResource;
                    if (required == null) return ability;
                    int available = owner.Resources.GetResourceAmount(required);
                    int cost = resource.CalculateCost(ability);
                    if (available >= cost) return ability;
                    // resource exhausted -> fall through to wand/scroll/potion branches (if enabled by mask)
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
                    var usable = item.Blueprint as Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable;
                    if (usable?.Ability == null) continue;
                    if (usable.Ability.AssetGuid.ToString() != parsed.BlueprintGuid) continue;

                    bool isScroll = usable.Type == Kingmaker.Blueprints.Items.Equipment.UsableItemType.Scroll;
                    bool isPotion = usable.Type == Kingmaker.Blueprints.Items.Equipment.UsableItemType.Potion;

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
                        if (book.GetAvailableForCastSpellCount(known) > 0) return true;
                    }
                }
            }
            return false;
        }

        static bool IsHealingSpell(BlueprintAbility blueprint) {
            if (blueprint == null) return false;
            // Check both display name (localized) and internal blueprint name (English)
            string displayName = (blueprint.Name ?? "").ToLowerInvariant();
            string internalName = (blueprint.name ?? "").ToLowerInvariant();
            return MatchesHealKeyword(displayName) || MatchesHealKeyword(internalName);
        }

        static bool MatchesHealKeyword(string n) {
            return n.Contains("cure") || n.Contains("heal")
                || n.Contains("restoration") || n.Contains("lay on hands")
                || n.Contains("channel positive")
                || n.Contains("wunden") || n.Contains("heilung");  // German fallback
        }
    }
}
