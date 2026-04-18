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
        public static bool CanExecute(ActionDef action, UnitEntityData owner, UnitEntityData target) {
            // No IsRunning() guard — auto-attacks would block everything in real-time mode.
            // Cooldown system + game's command queue handle conflicts.
            switch (action.Type) {
                case ActionType.CastSpell:
                    return CanCastSpell(action.AbilityId, owner, target);
                case ActionType.CastAbility:
                    return CanCastSpell(action.AbilityId, owner, target);
                case ActionType.UseItem:
                    return CanUseItem(action.AbilityId, owner, target);
                case ActionType.ToggleActivatable:
                    return CanToggleActivatable(action.AbilityId, owner, action.ToggleMode);
                case ActionType.AttackTarget:
                    return target != null && target.HPLeft > 0;
                case ActionType.Heal:
                    return FindBestHeal(owner, action.HealMode, action.HealSources) != null;
                case ActionType.ThrowSplash:
                    return target != null && SplashItemResolver.FindBest(owner, action.SplashMode).HasValue;
                case ActionType.DoNothing:
                    return true;
                default:
                    return false;
            }
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
                            if (book.GetSpontaneousSlots(level) > 0 || book.GetSpellsPerDay(level) > 0)
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
