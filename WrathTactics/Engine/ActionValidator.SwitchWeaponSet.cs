using Kingmaker.EntitySystem.Entities;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        // Bounds-check the target slot and reject if the unit is already on it. The
        // already-on check is load-bearing: without it the rule re-fires every tick and
        // the engine keeps issuing UnitSwitchHandEquipmentSet commands — which, since
        // SwitchWeaponSet doesn't queue a tracker-watched UnitCommand for ActiveRuleTracker
        // in some edge cases, lets lower-priority rules thrash.
        public static bool CanSwitchWeaponSet(UnitEntityData owner, int targetIndex) {
            if (owner?.Body == null) return false;
            var sets = owner.Body.HandsEquipmentSets;
            if (sets == null) return false;
            if (targetIndex < 0 || targetIndex >= sets.Count) {
                Log.Engine.Trace($"CanSwitchWeaponSet: index {targetIndex} out of range (count={sets.Count}) for {owner.CharacterName}");
                return false;
            }
            var current = owner.Body.CurrentHandsEquipmentSet;
            int currentIndex = current != null ? sets.IndexOf(current) : -1;
            if (currentIndex == targetIndex) {
                Log.Engine.Trace($"CanSwitchWeaponSet: {owner.CharacterName} already on set {targetIndex}");
                return false;
            }
            return true;
        }
    }
}
