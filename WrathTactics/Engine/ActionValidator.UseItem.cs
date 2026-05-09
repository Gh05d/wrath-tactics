using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        static bool CanUseItemAtPoint(string abilityGuid, UnitEntityData owner) {
            var ability = FindUseItemSource(owner, abilityGuid, out _);
            if (ability == null) return false;
            if (!ability.CanTargetPoint) return false;
            if (ability.SourceItem != null && ability.SourceItem.Charges <= 0) return false;
            if (!ability.IsAvailable) {
                Log.Engine.Trace($"CanUseItemAtPoint: {owner.CharacterName} {ability.Name} engine-unavailable ({ability.GetUnavailableReason()})");
                return false;
            }
            return true;
        }

        static bool CanUseItem(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            var ability = FindUseItemSource(owner, abilityGuid, out var inventorySource);
            if (ability == null) return false;
            if (ability.SourceItem != null && ability.SourceItem.Charges <= 0) return false;
            // Inventory-source items rely on stack Count > 0, which FindUseItemSource already enforces.
            if (!ability.IsAvailable) {
                Log.Engine.Trace($"CanUseItem: {owner.CharacterName} {ability.Name} engine-unavailable ({ability.GetUnavailableReason()})");
                return false;
            }
            if (target != null && !ability.CanTarget(new TargetWrapper(target)))
                return false;
            return true;
        }

        public static AbilityData FindUseItemSource(UnitEntityData owner, string abilityGuid, out ItemEntity inventorySource) {
            inventorySource = null;
            if (string.IsNullOrEmpty(abilityGuid)) return null;

            // 1. Equipped item-backed ability (wand/scroll in quickslot).
            foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Blueprint.AssetGuid.ToString() == abilityGuid && ability.Data.SourceItem != null)
                    return ability.Data;
            }

            // 2. Shared inventory — potions, then scrolls, then wands, then Utility. Mirrors
            // SpellDropdownProvider's four-pass ordering so "UseItem: Invisibility" labelled
            // "(Potion)" consumes a potion, not whichever form of Invisibility happens to appear
            // first in storage order. Utility (rods, special-power devices) is last so existing
            // potion/scroll/wand rules keep resolving to their original consumable; a user adding
            // a rod-only ability rule explicitly opts in.
            var inventory = Kingmaker.Game.Instance?.Player?.Inventory;
            if (inventory == null) return null;
            var potion = FindInventoryUsable(inventory, owner, abilityGuid,
                UsableItemType.Potion, out inventorySource);
            if (potion != null) return potion;
            var scroll = FindInventoryUsable(inventory, owner, abilityGuid,
                UsableItemType.Scroll, out inventorySource);
            if (scroll != null) return scroll;
            var wand = FindInventoryUsable(inventory, owner, abilityGuid,
                UsableItemType.Wand, out inventorySource);
            if (wand != null) return wand;
            return FindInventoryUsable(inventory, owner, abilityGuid,
                UsableItemType.Utility, out inventorySource);
        }

        static AbilityData FindInventoryUsable(
            ItemsCollection inventory,
            UnitEntityData owner,
            string abilityGuid,
            UsableItemType wantedType,
            out ItemEntity inventorySource) {
            inventorySource = null;
            foreach (var item in inventory) {
                if (item == null || item.Count <= 0) continue;
                var usable = item.Blueprint as BlueprintItemEquipmentUsable;
                if (usable?.Ability == null) continue;
                if (usable.Type != wantedType) continue;
                if (usable.Ability.AssetGuid.ToString() != abilityGuid) continue;
                // Wands track uses via Charges. A spent wand still has Count=1 but Charges=0
                // and must not be queued for cast (the engine would silently drop it).
                if (wantedType == UsableItemType.Wand && item.Charges <= 0) continue;

                inventorySource = item;
                return new AbilityData(usable.Ability, owner.Descriptor) {
                    OverrideCasterLevel = usable.CasterLevel,
                    OverrideSpellLevel = usable.SpellLevel,
                };
            }
            return null;
        }
    }
}
