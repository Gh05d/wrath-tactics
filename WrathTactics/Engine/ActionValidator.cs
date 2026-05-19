using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        public static bool CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
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
                        return true;
                    }
                    case ActionType.UseItem:
                        return CanUseItemAtPoint(action.AbilityId, owner);
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
                    return ResolveCastSpellChain(owner, target, action, out _unused, out _unusedId) != null;
                }
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
