using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using UnityEngine;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        // A move rule is executable while the unit can move and is still outside the
        // requested bracket. "Already within" is a normal fall-through, not an error: the
        // rule is meant to be re-evaluated every tick and go quiet once in range.
        /// <summary>A walk we issued earlier is still heading for (about) the same spot. A
        /// UnitMoveTo does its walking BEFORE it starts (TickApproaching; Start = arrival),
        /// so re-issuing it every tick would interrupt it unstarted, refund its cooldown and
        /// restart the path — visible as a stutter and as INFO spam. Deck 2026-09-16.</summary>
        internal const float WalkRetargetMeters = 1.5f;

        public static bool CanMoveToTarget(UnitEntityData owner, ResolvedTarget target, RangeBracket within) {
            return CanMoveToTarget(owner, target, within, out _);
        }

        /// <summary>True when the walk rule is still needed: the unit is walking there
        /// already. The evaluator turns this into a hold on lower rules — any Standard or
        /// Move command they issued would cancel the walk (paired slots).</summary>
        public static bool CanMoveToTarget(UnitEntityData owner, ResolvedTarget target, RangeBracket within,
                                           out bool walkInProgress) {
            walkInProgress = false;
            if (!TryGetMoveDestination(owner, target, out var destination, out float distance)) return false;
            if (!RangeBrackets.Beyond(distance, within)) {
                Log.Engine.Trace($"CanMoveToTarget: {owner.CharacterName} already within {within} ({distance:F1} m)");
                return false;
            }
            var current = owner.Commands?.Raw?[(int)UnitCommand.CommandType.Move] as UnitMoveTo;
            if (current != null && !current.IsFinished && PlayerCommandGuard.IsOurs(owner, current)
                && Vector3.Distance(current.Target, destination) <= WalkRetargetMeters) {
                Log.Engine.Trace($"CanMoveToTarget: {owner.CharacterName} walk in progress ({distance:F1} m left)");
                walkInProgress = true;
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
