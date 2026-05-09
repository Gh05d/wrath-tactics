using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.Utility;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        // GUID of CreatureAbilities/NegativeEnergyAffinity.jbp — the exact fact
        // CureLightWounds.jbp's ContextConditionHasFact gates the heal-vs-damage flip on.
        // Resolved lazily through ResourcesLibrary; cached for the session.
        const string NegativeEnergyAffinityGuid = "d5ee498e19722854198439629c1841a5";
        static BlueprintFeature s_negativeEnergyAffinity;
        static bool s_negativeEnergyAffinityResolved;

        static BlueprintFeature NegativeEnergyAffinityBp() {
            if (s_negativeEnergyAffinityResolved) return s_negativeEnergyAffinity;
            try {
                s_negativeEnergyAffinity = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(NegativeEnergyAffinityGuid);
                if (s_negativeEnergyAffinity == null)
                    Log.Engine.Warn($"NegativeEnergyAffinity blueprint {NegativeEnergyAffinityGuid} not found; falling back to feature-name detection.");
            } catch (InvalidOperationException ex) {
                // ResourcesLibrary throws this if accessed before BlueprintsCache is initialised.
                // Other exceptions (NRE, etc.) indicate a real defect; let them propagate so we
                // notice rather than permanently caching a missing blueprint.
                Log.Engine.Error(ex, "NegativeEnergyAffinity blueprint lookup failed (engine not ready)");
            }
            s_negativeEnergyAffinityResolved = true;
            return s_negativeEnergyAffinity;
        }

        /// <summary>
        /// True when positive energy damages and negative energy heals this unit. Mirrors the
        /// engine-authoritative check from CureLightWounds.jbp: ContextConditionHasFact on
        /// blueprint <c>d5ee498e19722854198439629c1841a5</c> (NegativeEnergyAffinity). The
        /// fact is added transitively to all vanilla undead via UndeadType → UndeadImmunities
        /// → NegativeEnergyAffinity, and directly to Dhampir via NegativeEnergyAffinityDhampir.
        /// Lich-MC picks it up post-LichTrueFeature through the same UndeadType chain.
        ///
        /// The legacy substring fallback on <c>Progression.Features</c> is kept as a defensive
        /// net for mod-added units that name their affinity feature "Dhampir*" or
        /// "NegativeEnergyAffinity*" without chaining through the canonical blueprint. The
        /// dropped <c>Blueprint.Type.name.Contains("undead")</c> branch was dead code — every
        /// vanilla undead carries a specific subtype name (Skeleton, VampireSpawn, Mummy, …);
        /// none contain the literal "undead" substring.
        /// </summary>
        public static bool IsNegativeEnergyAffine(UnitEntityData unit) {
            var d = unit?.Descriptor;
            if (d == null) return false;

            var bp = NegativeEnergyAffinityBp();
            if (bp != null && UnitHelper.HasFact(d, bp)) return true;

            // Legacy substring fallback for mod-added affinity sources that don't chain
            // through the canonical blueprint. Cheap; only iterates progression features.
            var progression = d.Progression;
            if (progression?.Features != null) {
                foreach (var fact in progression.Features.Enumerable) {
                    var fname = fact?.Blueprint?.name ?? "";
                    if (fname.IndexOf("NegativeEnergyAffinity", StringComparison.OrdinalIgnoreCase) >= 0
                     || fname.IndexOf("Dhampir", StringComparison.OrdinalIgnoreCase) >= 0) {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
            if (!target.IsValid && RequiresValidTarget(action.Type))
                return false;

            if (target.IsPoint) {
                switch (action.Type) {
                    case ActionType.CastSpell: {
                        ItemEntity _unused;
                        string _unusedId;
                        var ability = ResolveCastSpellChain(owner, target, action, out _unused, out _unusedId);
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
                    string _unusedId;
                    return ResolveCastSpellChain(owner, target, action, out _unused, out _unusedId) != null;
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
                    // Self-heal when no explicit target is resolved — mirrors ExecuteHeal's
                    // `target ?? owner` fallback. Auto-mode reads the unit for affinity check.
                    return FindBestHeal(owner, unit ?? owner, action.HealMode, action.HealSources, action.HealEnergy) != null;
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

        public static AbilityData FindBestHeal(
            UnitEntityData owner,
            UnitEntityData target,
            HealMode mode = HealMode.Any,
            HealSourceMask sources = HealSourceMask.All,
            HealEnergyType pin = HealEnergyType.Auto) {
            return FindBestHealEx(owner, target, mode, sources, pin, out _);
        }

        /// <summary>
        /// Returns best heal ability plus the inventory ItemEntity it came from (null for
        /// spellbook spells, class abilities, and quickslot/equipped wands). Caller must
        /// consume the item via Inventory.Remove after casting — synthesized AbilityData
        /// from inventory doesn't auto-consume through Rulebook.Trigger.
        ///
        /// `target` drives Auto-mode energy detection (undead → Negative, else Positive).
        /// `pin` overrides the auto-pick: Positive / Negative force a specific energy type
        /// regardless of target affinity (power-user override; null result if no match).
        /// `sources` masks which classes of heal are eligible. Spell covers spellbook casts,
        /// class abilities, and wand/staff activations (all character-driven). Scroll and
        /// Potion are inventory consumables. Default All keeps the legacy behaviour.
        /// </summary>
        public static AbilityData FindBestHealEx(
            UnitEntityData owner,
            UnitEntityData target,
            HealMode mode,
            HealSourceMask sources,
            HealEnergyType pin,
            out ItemEntity inventorySource) {
            inventorySource = null;
            var heals = new List<(AbilityData ability, int priority, ItemEntity source, HealSourceMask category)>();
            bool wantSpell  = (sources & HealSourceMask.Spell)  != 0;
            bool wantScroll = (sources & HealSourceMask.Scroll) != 0;
            bool wantPotion = (sources & HealSourceMask.Potion) != 0;

            // Resolve required energy once per call. Auto mode reads target affinity; Positive
            // / Negative pins ignore the target check (user-explicit override, no safety-net).
            HealEnergyType requiredEnergy =
                pin == HealEnergyType.Positive ? HealEnergyType.Positive
                : pin == HealEnergyType.Negative ? HealEnergyType.Negative
                : (IsNegativeEnergyAffine(target) ? HealEnergyType.Negative : HealEnergyType.Positive);

            // Local helper — "is this candidate's energy type acceptable for this rule?"
            // Returns false for None (non-heal blueprints) and for the wrong energy type.
            bool MatchesEnergy(BlueprintAbility bp) => ClassifyHeal(bp) == requiredEnergy;

            // Search spellbooks for cure/heal spells. Iterates known + special lists; the
            // latter covers Cleric domain Cure spells, Life-spirit Shaman heals, etc.
            // GetAvailableForCastSpellCount returns -1 for cantrips (unlimited); 0 means
            // no slot or spell-not-in-book.
            if (wantSpell) foreach (var book in owner.Spellbooks) {
                int maxLevel = book.MaxSpellLevel;
                for (int level = 0; level <= maxLevel; level++) {
                    foreach (var spell in book.GetKnownSpells(level)) {
                        if (MatchesEnergy(spell.Blueprint)) {
                            if (book.GetAvailableForCastSpellCount(spell) == 0) continue;
                            if (!spell.IsAvailable) {
                                Log.Engine.Trace($"Skipping heal spell {spell.Blueprint.name} for {owner.CharacterName}: engine-unavailable ({spell.GetUnavailableReason()})");
                                continue;
                            }
                            heals.Add((spell, 100 + level * 10, null, HealSourceMask.Spell)); // highest priority: spellbook spells
                        }
                    }
                    foreach (var spell in book.GetSpecialSpells(level)) {
                        if (MatchesEnergy(spell.Blueprint)) {
                            if (book.GetAvailableForCastSpellCount(spell) == 0) continue;
                            if (!spell.IsAvailable) {
                                Log.Engine.Trace($"Skipping heal special-spell {spell.Blueprint.name} for {owner.CharacterName}: engine-unavailable ({spell.GetUnavailableReason()})");
                                continue;
                            }
                            heals.Add((spell, 100 + level * 10, null, HealSourceMask.Spell));
                        }
                    }
                    // Custom spells: prepared metamagic variants (Empowered/Quickened CMW etc.)
                    // live here, not in m_KnownSpells. Skipping this list made metamagic-prepared
                    // cures invisible to the heal picker.
                    foreach (var spell in book.GetCustomSpells(level)) {
                        if (MatchesEnergy(spell.Blueprint)) {
                            if (book.GetAvailableForCastSpellCount(spell) == 0) continue;
                            if (!spell.IsAvailable) {
                                Log.Engine.Trace($"Skipping heal custom-spell {spell.Blueprint.name} for {owner.CharacterName}: engine-unavailable ({spell.GetUnavailableReason()})");
                                continue;
                            }
                            heals.Add((spell, 100 + level * 10, null, HealSourceMask.Spell));
                        }
                    }
                }
            }

            // Class abilities (Lay on Hands, Channel Positive Energy)
            // Must check resource availability — some abilities are per-day
            if (wantSpell) foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;
                if (!MatchesEnergy(ability.Blueprint)) continue;

                // Engine-authoritative cost — honors ResourceCostIncreasing/DecreasingFacts and
                // custom IAbilityResourceCostCalculator. Plain .Amount underreports for
                // abilities with cost-modifier facts.
                var resource = ability.Data.Blueprint.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityResourceLogic>();
                if (resource?.RequiredResource != null) {
                    int available = owner.Resources.GetResourceAmount(resource.RequiredResource);
                    int cost = resource.CalculateCost(ability.Data);
                    if (available < cost) {
                        Log.Engine.Trace($"Skipping heal ability {ability.Blueprint.name} for {owner.CharacterName}: resource {resource.RequiredResource.name}={available}/{cost}");
                        continue;
                    }
                }

                // Engine-side gate — covers Blueprint.CasterRestrictions (e.g. Prestige Plus'
                // Auto Heal carrying AbilityCasterInCombat{Not=true}), forbidden spellbooks,
                // UnitState.CanCast, TemporarilyDisabled. The engine greys the ability out and
                // would silently drop the command post-queue — without this filter, the Heal
                // rule picks Auto Heal as the "best" in-combat heal, the cast is dropped, the
                // rule is marked as fired, and fall-through to backup heals is blocked.
                if (!ability.Data.IsAvailable) {
                    Log.Engine.Trace($"Skipping heal ability {ability.Blueprint.name} for {owner.CharacterName}: engine-unavailable ({ability.Data.GetUnavailableReason()})");
                    continue;
                }

                heals.Add((ability.Data, 80, null, HealSourceMask.Spell)); // next priority: class features
            }

            // Item-backed abilities (wands, staves, equipped healing items)
            if (wantSpell) foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Data.SourceItem == null) continue;
                if (ability.Data.SourceItem.Charges <= 0) continue;
                if (!MatchesEnergy(ability.Blueprint)) continue;
                if (!ability.Data.IsAvailable) {
                    Log.Engine.Trace($"Skipping heal wand {ability.Blueprint.name} for {owner.CharacterName}: engine-unavailable ({ability.Data.GetUnavailableReason()})");
                    continue;
                }
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
                    if (!MatchesEnergy(usable.Ability)) {
                        Log.Engine.Trace($"  inventory item {itemName} (ability '{abilityName}'): wrong energy type for required {requiredEnergy}");
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
        /// Iterates the CastSpell fallback chain (primary AbilityId + FallbackAbilityIds in order)
        /// and returns the first entry whose FindCastSpellSource resolution succeeds. `usedAbilityId`
        /// reports which entry won so the executor can log/report meaningfully. Empty / null ids
        /// are skipped silently — a half-filled fallback row shouldn't block later ones.
        /// </summary>
        /// <summary>
        /// Classifies a heal blueprint by energy type. Returns None for non-heal spells.
        /// Keyword-based; matches both internal (English, stable) name and localised display
        /// name. The German tables disambiguate "Wunden heilen" (Cure) vs "Wunden zufügen"
        /// (Inflict) — the bare "wunden" in 1.2.0 was harmless because Inflict was never
        /// searched; with Negative-energy detection added, the substring is replaced by the
        /// full bigram in each table.
        /// </summary>
        static HealEnergyType ClassifyHeal(BlueprintAbility blueprint) {
            if (blueprint == null) return HealEnergyType.None;
            string n = (blueprint.name ?? "").ToLowerInvariant();
            string d = (blueprint.Name ?? "").ToLowerInvariant();

            // Negative first: defence-in-depth against future Positive-keyword changes that
            // might overlap (e.g. "channel" alone). The current Positive table requires
            // "channel positive" specifically, but ordering keeps results stable on edits.
            if (MatchesNegativeKeyword(n) || MatchesNegativeKeyword(d)) return HealEnergyType.Negative;
            if (MatchesPositiveKeyword(n) || MatchesPositiveKeyword(d)) return HealEnergyType.Positive;
            return HealEnergyType.None;
        }

        static bool MatchesPositiveKeyword(string n) {
            // "restoration" intentionally excluded — Lesser/Normal/Greater Restoration remove
            // ability damage / drain / negative levels but do NOT restore HP. Including them
            // made the Heal action burn 300-900g Restoration scrolls on a low-HP ally.
            // Known imprecision: "cure" matches Cure Disease / Cure Deafness / Neutralize
            // Poison — these are rare in typical inventories and the UMD gate limits mis-casts.
            return n.Contains("cure")
                || n.Contains("heal")
                || n.Contains("lay on hands")
                || n.Contains("channel positive")
                // German display names — "wunden heilen" disambiguates from "wunden zufügen"
                || n.Contains("wunden heilen")
                || n.Contains("heilung")
                || n.Contains("auflegen");
        }

        static bool MatchesNegativeKeyword(string n) {
            return n.Contains("inflict")
                || n.Contains("harm")
                || n.Contains("channel negative")
                // German display names
                || n.Contains("wunden zufügen")
                || n.Contains("negative energie");
        }
    }
}
