using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.Utility;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static class ActionValidator {
        public static bool CanExecute(ActionDef action, UnitEntityData owner, UnitEntityData target) {
            if (owner.Commands.IsRunning())
                return false;

            switch (action.Type) {
                case ActionType.CastSpell:
                    return CanCastSpell(action.AbilityId, owner, target);
                case ActionType.CastAbility:
                    return CanCastSpell(action.AbilityId, owner, target);
                case ActionType.UseItem:
                    return CanUseItem(action.AbilityId, owner, target);
                case ActionType.ToggleActivatable:
                    return CanToggleActivatable(action.AbilityId, owner);
                case ActionType.AttackTarget:
                    return target != null && target.HPLeft > 0;
                case ActionType.Heal:
                    return FindBestHeal(owner, action.HealMode) != null;
                case ActionType.DoNothing:
                    return true;
                default:
                    return false;
            }
        }

        static bool CanCastSpell(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            if (string.IsNullOrEmpty(abilityGuid)) {
                Main.Log($"[DIAG] CastSpell/CastAbility has EMPTY AbilityId for {owner.CharacterName} — user didn't pick an ability!");
                return false;
            }
            var ability = FindAbility(owner, abilityGuid);
            if (ability == null) {
                Main.Log($"[DIAG] FindAbility FAILED for {owner.CharacterName}, guid={abilityGuid}");
                return false;
            }
            Main.Log($"[DIAG] FindAbility OK: {ability.Name} for {owner.CharacterName}");

            if (ability.Spellbook != null) {
                int level = ability.Spellbook.GetSpellLevel(ability);
                if (ability.Spellbook.GetSpellsPerDay(level) <= 0)
                    return false;
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

        static bool CanToggleActivatable(string abilityGuid, UnitEntityData owner) {
            var activatable = FindActivatable(owner, abilityGuid);
            if (activatable == null) return false;
            return !activatable.IsOn && activatable.IsAvailable;
        }

        public static AbilityData FindAbility(UnitEntityData owner, string abilityGuid) {
            if (string.IsNullOrEmpty(abilityGuid)) return null;

            foreach (var book in owner.Spellbooks) {
                for (int level = 0; level <= 10; level++) {
                    foreach (var spell in book.GetKnownSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() == abilityGuid)
                            return spell;
                    }
                    foreach (var spell in book.GetCustomSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() == abilityGuid)
                            return spell;
                    }
                }
            }

            foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Data.SourceItem != null) continue;

                // Direct match
                if (ability.Blueprint.AssetGuid.ToString() == abilityGuid)
                    return ability.Data;

                // Check variants (e.g. Evil Eye - AC, Evil Eye - Attack Rolls, etc.)
                var variants = GetBlueprintComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityVariants>(ability.Blueprint);
                if (variants != null && variants.m_Variants != null) {
                    foreach (var variant in variants.Variants) {
                        if (variant != null && variant.AssetGuid.ToString() == abilityGuid) {
                            // Create a proper Ability object so CreateCastCommand accepts it
                            var variantAbility = new Kingmaker.UnitLogic.Abilities.Ability(variant, owner.Descriptor);
                            return variantAbility.Data;
                        }
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

        public static AbilityData FindBestHeal(UnitEntityData owner, HealMode mode = HealMode.Any) {
            var heals = new List<(AbilityData ability, int priority)>();

            // Search spellbooks for cure/heal spells
            foreach (var book in owner.Spellbooks) {
                for (int level = 0; level <= 9; level++) {
                    foreach (var spell in book.GetKnownSpells(level)) {
                        if (IsHealingSpell(spell.Blueprint)) {
                            // Check if spell can still be cast
                            if (book.GetSpontaneousSlots(level) > 0 || book.GetSpellsPerDay(level) > 0)
                                heals.Add((spell, level * 10));
                        }
                    }
                }
            }

            // Search abilities (class heals like Lay on Hands)
            foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Data.SourceItem == null && IsHealingSpell(ability.Blueprint)) {
                    heals.Add((ability.Data, 50)); // medium priority
                }
            }

            if (heals.Count == 0) return null;

            switch (mode) {
                case HealMode.Strongest:
                    return heals.OrderByDescending(h => h.priority).First().ability;
                case HealMode.Weakest:
                    return heals.OrderBy(h => h.priority).First().ability;
                case HealMode.Any:
                default:
                    return heals.First().ability;
            }
        }

        static bool IsHealingSpell(BlueprintAbility blueprint) {
            if (blueprint == null) return false;
            string name = (blueprint.Name ?? "").ToLowerInvariant();
            // Match common healing spell names
            return name.Contains("cure") || name.Contains("heal")
                || name.Contains("restoration") || name.Contains("lay on hands")
                || name.Contains("channel positive");
        }
    }
}
