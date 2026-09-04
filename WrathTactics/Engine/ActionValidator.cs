using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        // `abilitySlot` carries the resolved ability's RuntimeActionType so the evaluator can
        // tell which UnitCommand slot the rule will occupy. It stays null for action types
        // that are not ability-backed and on every `false` return; ActionSlots.Classify
        // supplies the fallback. Validation logic below is unchanged — same conditions, same
        // ordering, same log lines.
        public static bool CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target,
                                      out UnitCommand.CommandType? abilitySlot) {
            abilitySlot = null;

            if (!target.IsValid && RequiresValidTarget(action.Type))
                return false;

            if (target.IsPoint) {
                switch (action.Type) {
                    case ActionType.CastSpell:
                    case ActionType.CastAbility: {
                        ItemEntity _unused;
                        string _unusedId;
                        var ability = ResolveCastSpellChain(owner, target, action, out _unused, out _unusedId);
                        if (ability == null) return false;
                        if (!ability.CanTargetPoint) {
                            Log.Engine.Trace($"CanCastAbilityAtPoint: {owner.CharacterName} ability '{ability.Name}' is not point-castable");
                            return false;
                        }
                        abilitySlot = ability.RuntimeActionType;
                        return true;
                    }
                    case ActionType.UseItem: {
                        if (!CanUseItemAtPoint(action.AbilityId, owner, out var itemAbility)) return false;
                        abilitySlot = itemAbility.RuntimeActionType;
                        return true;
                    }
                    default:
                        return false;
                }
            }

            var unit = target.Unit;
            switch (action.Type) {
                case ActionType.CastSpell:
                case ActionType.CastAbility: {
                    ItemEntity _unused;
                    string _unusedId;
                    var ability = ResolveCastSpellChain(owner, target, action, out _unused, out _unusedId);
                    if (ability == null) return false;
                    abilitySlot = ability.RuntimeActionType;
                    return true;
                }
                case ActionType.UseItem: {
                    if (!CanUseItem(action.AbilityId, owner, unit, out var itemAbility)) return false;
                    abilitySlot = itemAbility.RuntimeActionType;
                    return true;
                }
                case ActionType.ToggleActivatable:
                    return CanToggleActivatable(action.AbilityId, owner, action.ToggleMode);
                case ActionType.AttackTarget:
                    return unit != null && unit.HPLeft > 0;
                case ActionType.Heal: {
                    // Self-heal when no explicit target is resolved — mirrors ExecuteHeal's
                    // `target ?? owner` fallback. Auto-mode reads the unit for affinity check.
                    var heal = FindBestHeal(owner, unit ?? owner, action.HealMode, action.HealSources, action.HealEnergy);
                    if (heal == null) return false;
                    abilitySlot = heal.RuntimeActionType;
                    return true;
                }
                case ActionType.ThrowSplash:
                    return unit != null && SplashItemResolver.FindBest(owner, action.SplashMode).HasValue;
                case ActionType.SwitchWeaponSet:
                    return CanSwitchWeaponSet(owner, action.WeaponSetIndex);
                case ActionType.DoNothing:
                    return true;
                default:
                    return false;
            }
        }

        static bool RequiresValidTarget(ActionType type) {
            return type != ActionType.ToggleActivatable
                && type != ActionType.Heal
                && type != ActionType.DoNothing
                && type != ActionType.SwitchWeaponSet;
        }
    }
}
