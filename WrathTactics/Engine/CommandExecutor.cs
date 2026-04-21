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
        public static bool Execute(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
            try {
                switch (action.Type) {
                    case ActionType.CastSpell:
                        return ExecuteCastSpell(action, owner, target);
                    case ActionType.CastAbility:
                        return ExecuteCastSpell(action.AbilityId, owner, target);
                    case ActionType.UseItem:
                        return ExecuteUseItem(action.AbilityId, owner, target);
                    case ActionType.ToggleActivatable:
                        return ExecuteToggleActivatable(action.AbilityId, owner, action.ToggleMode);
                    case ActionType.AttackTarget:
                        return ExecuteAttack(owner, target.Unit);
                    case ActionType.Heal:
                        return ExecuteHeal(action, owner, target.Unit);
                    case ActionType.ThrowSplash:
                        return ExecuteThrowSplash(action, owner, target.Unit);
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

        static TargetWrapper BuildTargetWrapper(ResolvedTarget target, UnitEntityData owner) {
            if (target.IsPoint) return new TargetWrapper(target.Point.Value);
            if (target.Unit != null) return new TargetWrapper(target.Unit);
            return new TargetWrapper(owner); // fallback preserves pre-refactor "no target = self" behavior
        }

        static bool ExecuteCastSpell(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
            ItemEntity inventorySource;
            string usedAbilityId;
            var ability = ActionValidator.ResolveCastSpellChain(owner, target, action, out inventorySource, out usedAbilityId);
            if (ability == null) {
                int chainLen = 1 + (action.FallbackAbilityIds?.Count ?? 0);
                Log.Engine.Warn($"ResolveCastSpellChain returned null for {owner.CharacterName}, primary={action.AbilityId}, chain={chainLen}");
                return false;
            }
            bool isFallback = usedAbilityId != action.AbilityId;
            if (isFallback)
                Log.Engine.Info($"CastSpell fallback hit for {owner.CharacterName}: primary={action.AbilityId} -> used={usedAbilityId}");

            var targetWrapper = BuildTargetWrapper(target, owner);

            // Inventory source (scroll/potion) — synthetic AbilityData, Rulebook.Trigger + manual consume.
            // Mirror of ExecuteHeal's inventory path.
            if (inventorySource != null) {
                try {
                    Rulebook.Trigger(new RuleCastSpell(ability, targetWrapper));
                    var usable = inventorySource.Blueprint as BlueprintItemEquipmentUsable;
                    if (usable != null) ConsumeInventoryItem(inventorySource, usable);
                    string tgtDesc = target.IsPoint
                        ? $"point({target.Point.Value.x:F1},{target.Point.Value.z:F1})"
                        : (target.Unit?.CharacterName ?? "self");
                    Log.Engine.Info($"Cast (inventory): {inventorySource.Blueprint.name} -> {ability.Name} on {owner.CharacterName} -> {tgtDesc}");
                    return true;
                } catch (Exception ex) {
                    Log.Engine.Error(ex, $"CastSpell inventory trigger failed for {inventorySource.Blueprint.name}");
                    return false;
                }
            }

            // Spellbook / Wand / class ability — animated cast command.
            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command != null) {
                owner.Commands.Run(command);
                string tgtDesc = target.IsPoint
                    ? $"point({target.Point.Value.x:F1},{target.Point.Value.z:F1})"
                    : (target.Unit?.CharacterName ?? "self");
                Log.Engine.Debug($"Queued spell {ability.Name} on {owner.CharacterName} -> {tgtDesc}");
                return true;
            }

            try {
                Rulebook.Trigger<RuleCastSpell>(new RuleCastSpell(ability, targetWrapper));
                Log.Engine.Debug($"Rulebook-triggered {ability.Name} on {owner.CharacterName} (no animation)");
                return true;
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Rulebook.Trigger fallback failed for {ability.Name}");
                return false;
            }
        }

        static bool ExecuteCastSpell(string abilityGuid, UnitEntityData owner, ResolvedTarget target) {
            bool isSynthetic;
            var ability = ActionValidator.FindAbilityEx(owner, abilityGuid, out isSynthetic);
            if (ability == null) {
                Log.Engine.Warn($"Spell {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            var targetWrapper = BuildTargetWrapper(target, owner);

            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command != null) {
                owner.Commands.Run(command);
                string tgtDesc = target.IsPoint
                    ? $"point({target.Point.Value.x:F1},{target.Point.Value.z:F1})"
                    : (target.Unit?.CharacterName ?? "self");
                Log.Engine.Debug($"Queued ANIMATED {(isSynthetic ? "VARIANT" : "spell")} {ability.Name} on {owner.CharacterName} -> {tgtDesc}");
                return true;
            }

            try {
                Rulebook.Trigger<RuleCastSpell>(new RuleCastSpell(ability, targetWrapper));
                Log.Engine.Debug($"Rulebook-triggered {ability.Name} on {owner.CharacterName} (no animation)");
                return true;
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Rulebook.Trigger fallback failed for {ability.Name}");
                return false;
            }
        }

        static bool ExecuteUseItem(string abilityGuid, UnitEntityData owner, ResolvedTarget target) {
            var ability = owner.Abilities.RawFacts
                .FirstOrDefault(a => a.Blueprint.AssetGuid.ToString() == abilityGuid && a.Data.SourceItem != null)
                ?.Data;

            if (ability == null) {
                Log.Engine.Warn($"Item ability {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            var targetWrapper = BuildTargetWrapper(target, owner);

            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command == null) {
                Log.Engine.Warn($"CreateCastCommand failed for item ability");
                return false;
            }

            owner.Commands.Run(command);
            Log.Engine.Info($"Queued item use on {owner.CharacterName}");
            return true;
        }

        static bool ExecuteToggleActivatable(string abilityGuid, UnitEntityData owner, ToggleMode mode) {
            var activatable = ActionValidator.FindActivatable(owner, abilityGuid);
            if (activatable == null) {
                Log.Engine.Warn($"Activatable {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            if (mode == ToggleMode.Off) {
                activatable.IsOn = false;
                Log.Engine.Info($"Toggled {activatable.Blueprint.name} OFF for {owner.CharacterName}");
            } else {
                activatable.IsOn = true;
                if (!activatable.IsStarted)
                    activatable.TryStart();
                Log.Engine.Info($"Toggled {activatable.Blueprint.name} ON for {owner.CharacterName}");
            }
            return true;
        }

        static bool ExecuteHeal(ActionDef action, UnitEntityData owner, UnitEntityData target) {
            ItemEntity inventorySource;
            var ability = ActionValidator.FindBestHealEx(owner, action.HealMode, action.HealSources, out inventorySource);
            if (ability == null) {
                Log.Engine.Warn($"FindBestHeal returned null for {owner.CharacterName}");
                return false;
            }

            var targetWrapper = target != null
                ? new TargetWrapper(target)
                : new TargetWrapper(owner);

            // Inventory potions/scrolls: synthesized AbilityData (no SourceItem). CreateCastCommand
            // drops these silently. Trigger the rule and consume the stack explicitly — otherwise
            // the game's internal potion-use flow fires too, producing a duplicate floating tooltip.
            if (inventorySource != null) {
                try {
                    Rulebook.Trigger(new RuleCastSpell(ability, targetWrapper));
                    var usable = inventorySource.Blueprint as BlueprintItemEquipmentUsable;
                    if (usable != null) ConsumeInventoryItem(inventorySource, usable);
                    Log.Engine.Info($"Heal (inventory): {inventorySource.Blueprint.name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
                    return true;
                } catch (Exception ex) {
                    Log.Engine.Error(ex, $"Heal inventory trigger failed for {inventorySource.Blueprint.name}");
                    return false;
                }
            }

            // Spellbook spell, class ability, or quickslot wand — animated cast path.
            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command != null) {
                owner.Commands.Run(command);
                Log.Engine.Info($"Heal (animated): {ability.Name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
                return true;
            }

            try {
                Rulebook.Trigger(new RuleCastSpell(ability, targetWrapper));
                Log.Engine.Info($"Heal (rulebook fallback): {ability.Name} on {owner.CharacterName} -> {target?.CharacterName ?? "self"}");
                return true;
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Heal Rulebook.Trigger failed for {ability.Name}");
                return false;
            }
        }

        static void ConsumeInventoryItem(ItemEntity item, BlueprintItemEquipmentUsable usable) {
            int before = item.Count;
            int charges = item.Charges;
            string path;
            if (usable.Type == UsableItemType.Wand) {
                item.Charges--;
                path = "Charges--";
            } else {
                Game.Instance.Player.Inventory.Remove(item, 1);
                path = "Remove";
            }
            Log.Engine.Debug($"Consume {item.Blueprint.name} via {path}: Count {before}->{item.Count}, Charges {charges}->{item.Charges}, Type={usable.Type}");
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
                ConsumeInventoryItem(item, usable);
                Log.Engine.Info($"ThrowSplash: {owner.CharacterName} threw {item.Blueprint.name} at {target.CharacterName}");
                return true;
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"ThrowSplash Rulebook.Trigger failed for {item.Blueprint.name}");
                return false;
            }
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
