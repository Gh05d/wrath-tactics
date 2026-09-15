using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        // A move rule is executable while the unit can move and is still outside the
        // requested bracket. "Already within" is a normal fall-through, not an error: the
        // rule is meant to be re-evaluated every tick and go quiet once in range.
        public static bool CanMoveToTarget(UnitEntityData owner, ResolvedTarget target, RangeBracket within) {
            if (!TryGetMoveDestination(owner, target, out _, out float distance)) return false;
            if (!RangeBrackets.Beyond(distance, within)) {
                Log.Engine.Trace($"CanMoveToTarget: {owner.CharacterName} already within {within} ({distance:F1} m)");
                return false;
            }
            return true;
        }

        public static bool TryGetMoveDestination(UnitEntityData owner, ResolvedTarget target,
                                                 out Vector3 destination, out float distance) {
            destination = default;
            distance = 0f;
            if (owner?.State == null || !owner.State.CanMove) return false;
            if (target.IsPoint) {
                destination = target.Point.Value;
            } else if (target.Unit != null) {
                destination = target.Unit.Position;
            } else {
                return false;
            }
            distance = Vector3.Distance(owner.Position, destination);
            return true;
        }
    }
}
