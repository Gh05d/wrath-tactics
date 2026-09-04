using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        // `ability` is assigned only on the success path, so a caller can never read a
        // validator-rejected object.
        static bool CanUseItemAtPoint(string abilityGuid, UnitEntityData owner, out AbilityData ability) {
            ability = null;
            var found = FindUseItemSource(owner, abilityGuid, out _);
            if (found == null) return false;
            if (!found.CanTargetPoint) return false;
            if (found.SourceItem != null && found.SourceItem.Charges <= 0) return false;
            if (!found.IsAvailable) {
                Log.Engine.Trace($"CanUseItemAtPoint: {owner.CharacterName} {found.Name} engine-unavailable ({found.GetUnavailableReason()})");
                return false;
            }
            ability = found;
            return true;
        }

        static bool CanUseItem(string abilityGuid, UnitEntityData owner, UnitEntityData target, out AbilityData ability) {
            ability = null;
            var found = FindUseItemSource(owner, abilityGuid, out _);
            if (found == null) return false;
            if (found.SourceItem != null && found.SourceItem.Charges <= 0) return false;
            // Inventory-source items rely on stack Count > 0, which FindUseItemSource already enforces.
            if (!found.IsAvailable) {
                Log.Engine.Trace($"CanUseItem: {owner.CharacterName} {found.Name} engine-unavailable ({found.GetUnavailableReason()})");
                return false;
            }
            if (target != null && !found.CanTarget(new TargetWrapper(target)))
                return false;
            ability = found;
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
