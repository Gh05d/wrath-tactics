using System;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.Utility;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static class CommandExecutor {
        public static bool Execute(ActionDef action, UnitEntityData owner, UnitEntityData target) {
            try {
                switch (action.Type) {
                    case ActionType.CastSpell:
                        return ExecuteCastSpell(action.AbilityId, owner, target);
                    case ActionType.UseItem:
                        return ExecuteUseItem(action.AbilityId, owner, target);
                    case ActionType.ToggleActivatable:
                        return ExecuteToggleActivatable(action.AbilityId, owner);
                    case ActionType.AttackTarget:
                        return ExecuteAttack(owner, target);
                    case ActionType.DoNothing:
                        return true;
                    default:
                        return false;
                }
            } catch (Exception ex) {
                Main.Error(ex, $"[Executor] Failed to execute {action.Type} for {owner.CharacterName}");
                return false;
            }
        }

        static bool ExecuteCastSpell(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            var ability = ActionValidator.FindAbility(owner, abilityGuid);
            if (ability == null) {
                Main.Debug($"[Executor] Spell {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            var targetWrapper = target != null
                ? new TargetWrapper(target)
                : new TargetWrapper(owner);

            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command == null) {
                Main.Debug($"[Executor] CreateCastCommand returned null for {ability.Name}");
                return false;
            }

            owner.Commands.Run(command);
            Main.Debug($"[Executor] Queued spell {ability.Name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
            return true;
        }

        static bool ExecuteUseItem(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            var ability = owner.Abilities.RawFacts
                .FirstOrDefault(a => a.Blueprint.AssetGuid.ToString() == abilityGuid && a.Data.SourceItem != null)
                ?.Data;

            if (ability == null) {
                Main.Debug($"[Executor] Item ability {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            var targetWrapper = target != null
                ? new TargetWrapper(target)
                : new TargetWrapper(owner);

            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command == null) {
                Main.Debug($"[Executor] CreateCastCommand failed for item ability");
                return false;
            }

            owner.Commands.Run(command);
            Main.Debug($"[Executor] Queued item use on {owner.CharacterName}");
            return true;
        }

        static bool ExecuteToggleActivatable(string abilityGuid, UnitEntityData owner) {
            var activatable = ActionValidator.FindActivatable(owner, abilityGuid);
            if (activatable == null) {
                Main.Debug($"[Executor] Activatable {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            activatable.IsOn = true;
            if (!activatable.IsStarted)
                activatable.TryStart();

            Main.Debug($"[Executor] Toggled {activatable.Blueprint.name} ON for {owner.CharacterName}");
            return true;
        }

        static bool ExecuteAttack(UnitEntityData owner, UnitEntityData target) {
            if (target == null) return false;

            var command = new UnitAttack(target, null);
            owner.Commands.Run(command);
            Main.Debug($"[Executor] Queued attack on {owner.CharacterName} -> {target.CharacterName}");
            return true;
        }
    }
}
