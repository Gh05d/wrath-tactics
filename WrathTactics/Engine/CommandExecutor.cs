using System;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
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
                    case ActionType.CastAbility:
                        return ExecuteCastSpell(action.AbilityId, owner, target);
                    case ActionType.UseItem:
                        return ExecuteUseItem(action.AbilityId, owner, target);
                    case ActionType.ToggleActivatable:
                        return ExecuteToggleActivatable(action.AbilityId, owner);
                    case ActionType.AttackTarget:
                        return ExecuteAttack(owner, target);
                    case ActionType.Heal:
                        return ExecuteHeal(action, owner, target);
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
            bool isSynthetic;
            var ability = ActionValidator.FindAbilityEx(owner, abilityGuid, out isSynthetic);
            if (ability == null) {
                Main.Log($"[DIAG] Spell {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            var targetWrapper = target != null
                ? new TargetWrapper(target)
                : new TargetWrapper(owner);

            // Try animated cast first (works for real abilities AND variants constructed
            // via the two-param AbilityData(parent, variant) constructor)
            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command != null) {
                owner.Commands.Run(command);
                Main.Log($"[DIAG] Queued ANIMATED {(isSynthetic ? "VARIANT" : "spell")} {ability.Name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
                return true;
            }

            // Fallback for edge cases: trigger rule directly (no animation)
            try {
                Rulebook.Trigger<RuleCastSpell>(new RuleCastSpell(ability, targetWrapper));
                Main.Log($"[DIAG] Rulebook-triggered {ability.Name} on {owner.CharacterName} (no animation)");
                return true;
            } catch (Exception ex) {
                Main.Error(ex, $"[Executor] Rulebook.Trigger fallback failed for {ability.Name}");
                return false;
            }
        }

        static bool ExecuteUseItem(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            var ability = owner.Abilities.RawFacts
                .FirstOrDefault(a => a.Blueprint.AssetGuid.ToString() == abilityGuid && a.Data.SourceItem != null)
                ?.Data;

            if (ability == null) {
                Main.DebugLog($"[Executor] Item ability {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            var targetWrapper = target != null
                ? new TargetWrapper(target)
                : new TargetWrapper(owner);

            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command == null) {
                Main.DebugLog($"[Executor] CreateCastCommand failed for item ability");
                return false;
            }

            owner.Commands.Run(command);
            Main.DebugLog($"[Executor] Queued item use on {owner.CharacterName}");
            return true;
        }

        static bool ExecuteToggleActivatable(string abilityGuid, UnitEntityData owner) {
            var activatable = ActionValidator.FindActivatable(owner, abilityGuid);
            if (activatable == null) {
                Main.DebugLog($"[Executor] Activatable {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            activatable.IsOn = true;
            if (!activatable.IsStarted)
                activatable.TryStart();

            Main.DebugLog($"[Executor] Toggled {activatable.Blueprint.name} ON for {owner.CharacterName}");
            return true;
        }

        static bool ExecuteHeal(ActionDef action, UnitEntityData owner, UnitEntityData target) {
            var ability = ActionValidator.FindBestHeal(owner, action.HealMode);
            if (ability == null) {
                Main.Log($"[DIAG] FindBestHeal returned null for {owner.CharacterName}");
                return false;
            }

            var targetWrapper = target != null
                ? new TargetWrapper(target)
                : new TargetWrapper(owner);

            // Items (potions/scrolls) have synthetic AbilityData — CreateCastCommand rejects them.
            // Use Rulebook.Trigger for items, animated command for real spells/abilities.
            bool isItem = ability.SourceItem != null;
            if (!isItem) {
                var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
                if (command != null) {
                    owner.Commands.Run(command);
                    Main.Log($"[Executor] Heal (animated): {ability.Name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
                    return true;
                }
            }

            // Item-backed or fallback — use Rulebook.Trigger (no animation)
            try {
                Rulebook.Trigger<RuleCastSpell>(new RuleCastSpell(ability, targetWrapper));
                Main.Log($"[Executor] Heal (item/rulebook): {ability.Name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
                return true;
            } catch (Exception ex) {
                Main.Error(ex, $"[Executor] Heal Rulebook.Trigger failed for {ability.Name}");
                return false;
            }
        }

        static bool ExecuteAttack(UnitEntityData owner, UnitEntityData target) {
            if (target == null) return false;

            var command = new UnitAttack(target, null);
            owner.Commands.Run(command);
            Main.DebugLog($"[Executor] Queued attack on {owner.CharacterName} -> {target.CharacterName}");
            return true;
        }
    }
}
