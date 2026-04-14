using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static class SplashItemResolver {
        public struct Pick {
            public ItemEntity Item;
            public BlueprintAbility ThrowAbility;
        }

        public static Pick? FindBest(UnitEntityData owner, ThrowSplashMode mode) {
            var inventory = Game.Instance?.Player?.Inventory;
            if (inventory == null) return null;

            var candidates = new List<(ItemEntity item, BlueprintAbility ability, int prio)>();
            foreach (var item in inventory) {
                if (item == null || item.Count <= 0) continue;
                var guid = item.Blueprint.AssetGuid.ToString();
                if (!SplashItemRegistry.IsSplashItem(guid)) continue;

                var usable = item.Blueprint as BlueprintItemEquipmentUsable;
                if (usable == null || usable.Ability == null) {
                    Log.Engine.Trace($"Splash candidate {item.Blueprint.name} has no usable ability — skipping");
                    continue;
                }

                int prio;
                switch (mode) {
                    case ThrowSplashMode.Strongest:
                        prio = SplashItemRegistry.GetDamagePriority(guid);
                        break;
                    case ThrowSplashMode.Cheapest:
                        prio = -SplashItemRegistry.GetCostPriority(guid);
                        break;
                    default:
                        prio = 0;
                        break;
                }

                candidates.Add((item, usable.Ability, prio));
            }

            if (candidates.Count == 0) return null;

            var best = mode == ThrowSplashMode.Any
                ? candidates[0]
                : candidates.OrderByDescending(c => c.prio).First();

            return new Pick { Item = best.item, ThrowAbility = best.ability };
        }
    }
}
