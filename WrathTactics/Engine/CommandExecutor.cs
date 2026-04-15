using System;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.Utility;
using WrathTactics.Logging;
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
                    case ActionType.ThrowSplash:
                        return ExecuteThrowSplash(action, owner, target);
                    case ActionType.DoNothing:
                        return true;
                    default:
                        return false;
                }
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Failed to execute {action.Type} for {owner.CharacterName}");
                return false;
            }
        }

        static bool ExecuteCastSpell(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            bool isSynthetic;
            var ability = ActionValidator.FindAbilityEx(owner, abilityGuid, out isSynthetic);
            if (ability == null) {
                Log.Engine.Warn($"Spell {abilityGuid} not found on {owner.CharacterName}");
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
                Log.Engine.Debug($"Queued ANIMATED {(isSynthetic ? "VARIANT" : "spell")} {ability.Name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
                return true;
            }

            // Fallback for edge cases: trigger rule directly (no animation)
            try {
                Rulebook.Trigger<RuleCastSpell>(new RuleCastSpell(ability, targetWrapper));
                Log.Engine.Debug($"Rulebook-triggered {ability.Name} on {owner.CharacterName} (no animation)");
                return true;
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Rulebook.Trigger fallback failed for {ability.Name}");
                return false;
            }
        }

        static bool ExecuteUseItem(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            var ability = owner.Abilities.RawFacts
                .FirstOrDefault(a => a.Blueprint.AssetGuid.ToString() == abilityGuid && a.Data.SourceItem != null)
                ?.Data;

            if (ability == null) {
                Log.Engine.Warn($"Item ability {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            var targetWrapper = target != null
                ? new TargetWrapper(target)
                : new TargetWrapper(owner);

            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command == null) {
                Log.Engine.Warn($"CreateCastCommand failed for item ability");
                return false;
            }

            owner.Commands.Run(command);
            Log.Engine.Info($"Queued item use on {owner.CharacterName}");
            return true;
        }

        static bool ExecuteToggleActivatable(string abilityGuid, UnitEntityData owner) {
            var activatable = ActionValidator.FindActivatable(owner, abilityGuid);
            if (activatable == null) {
                Log.Engine.Warn($"Activatable {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            activatable.IsOn = true;
            if (!activatable.IsStarted)
                activatable.TryStart();

            Log.Engine.Info($"Toggled {activatable.Blueprint.name} ON for {owner.CharacterName}");
            return true;
        }

        static bool ExecuteHeal(ActionDef action, UnitEntityData owner, UnitEntityData target) {
            var ability = ActionValidator.FindBestHeal(owner, action.HealMode);
            if (ability == null) {
                Log.Engine.Warn($"FindBestHeal returned null for {owner.CharacterName}");
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
                    Log.Engine.Info($"Heal (animated): {ability.Name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
                    return true;
                }
            }

            // Item-backed or fallback — use Rulebook.Trigger (no animation)
            try {
                Rulebook.Trigger<RuleCastSpell>(new RuleCastSpell(ability, targetWrapper));
                Log.Engine.Info($"Heal (item/rulebook): {ability.Name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
                return true;
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Heal Rulebook.Trigger failed for {ability.Name}");
                return false;
            }
        }

        static bool ExecuteThrowSplash(ActionDef action, UnitEntityData owner, UnitEntityData target) {
            if (target == null) {
                Log.Engine.Warn($"ThrowSplash: no target for {owner.CharacterName}");
                return false;
            }

            var pick = SplashItemResolver.FindBest(owner, action.SplashMode);
            if (!pick.HasValue) {
                Log.Engine.Warn($"ThrowSplash: no splash items available for {owner.CharacterName}");
                return false;
            }

            var item = pick.Value.Item;
            var usable = item.Blueprint as BlueprintItemEquipmentUsable;
            if (usable == null) {
                Log.Engine.Warn($"ThrowSplash: {item.Blueprint.name} is not a usable item");
                return false;
            }

            // CreateCastCommand silently drops synthetic AbilityData not registered on the unit.
            // Use Rulebook.Trigger with SourceItem set, then manually consume the stack.
            var data = new AbilityData(usable.Ability, owner.Descriptor) {
                OverrideCasterLevel = usable.CasterLevel,
                OverrideSpellLevel = usable.SpellLevel,
            };
            var tw = new TargetWrapper(target);

            try {
                Rulebook.Trigger(new RuleCastSpell(data, tw));
                ConsumeSplashItem(item, usable);
                Log.Engine.Info($"ThrowSplash: {owner.CharacterName} threw {item.Blueprint.name} at {target.CharacterName}");
                return true;
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"ThrowSplash Rulebook.Trigger failed for {item.Blueprint.name}");
                return false;
            }
        }

        static void ConsumeSplashItem(ItemEntity item, BlueprintItemEquipmentUsable usable) {
            if (usable.Type == UsableItemType.Potion || usable.Type == UsableItemType.Scroll) {
                Game.Instance.Player.Inventory.Remove(item, 1);
                return;
            }
            if (item.IsSpendCharges) {
                item.Charges--;
                return;
            }
            // Utility-type splash items (Alchemist's Fire, Acid Flask): consume one from stack
            Game.Instance.Player.Inventory.Remove(item, 1);
        }

        static bool ExecuteAttack(UnitEntityData owner, UnitEntityData target) {
            if (target == null) return false;

            var command = new UnitAttack(target, null);
            owner.Commands.Run(command);
            Log.Engine.Info($"Queued attack on {owner.CharacterName} -> {target.CharacterName}");
            return true;
        }
    }
}
