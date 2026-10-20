using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.View;
using UnityEngine;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        // "Move away from target": walk until the unit is farther than ActionDef.MoveWithin
        // from the anchor (the target unit/point, or — for Target: Self — the enemies around
        // the unit). Mirrors MoveToTarget: "already beyond" is a silent fall-through, a walk
        // that is still gaining distance is not re-issued, and the destination is clamped to
        // the navmesh (ObstacleAnalyzer.TraceAlongNavmesh, the engine's own push/roam helper)
        // so a wall or cliff behind the unit yields a sideways escape instead of a no-op.

        /// <summary>Enemies this close to the unit count as "what I am fleeing from" when the
        /// rule targets Self (their centroid is the anchor). Same radius as the Short bracket.</summary>
        internal const float SelfAnchorRadiusMeters = 10f;

        /// <summary>approachRadius of the flee walk: the engine ends the walk inside it, so it
        /// must stay small — the point was chosen for its distance from the anchor.</summary>
        const float FleeApproachRadiusMeters = 1f;

        public static bool CanMoveAway(UnitEntityData owner, ResolvedTarget target, RangeBracket beyond,
                                       out bool walkInProgress) {
            walkInProgress = false;
            if (!TryGetFleeAnchor(owner, target, out var anchor, out float distance)) return false;
            float required = RangeBrackets.MaxMeters(beyond);
            if (distance > required) {
                Log.Engine.Trace($"CanMoveAway: {owner.CharacterName} already beyond {beyond} ({distance:F1} m)");
                return false;
            }
            var current = owner.Commands?.Raw?[(int)UnitCommand.CommandType.Move] as UnitMoveTo;
            if (current != null && !current.IsFinished && PlayerCommandGuard.IsOurs(owner, current)
                && DistanceXZ(current.Target, anchor) >= distance + FleeGeometry.MinGainMeters) {
                Log.Engine.Trace($"CanMoveAway: {owner.CharacterName} flee walk in progress ({distance:F1} m from anchor)");
                walkInProgress = true;
                return false;
            }
            if (!TryGetFleeDestination(owner, anchor, distance, required, out _)) {
                Log.Engine.Trace($"CanMoveAway: {owner.CharacterName} has no reachable point farther from the anchor ({distance:F1} m)");
                return false;
            }
            return true;
        }

        /// <summary>The position the unit moves away from. Target: Self means "the enemies
        /// around me": centroid of in-combat enemies within SelfAnchorRadiusMeters, else the
        /// nearest enemy. No enemy at all → nothing to flee from.</summary>
        public static bool TryGetFleeAnchor(UnitEntityData owner, ResolvedTarget target,
                                            out Vector3 anchor, out float distance) {
            anchor = default;
            distance = 0f;
            if (owner?.State == null || !owner.State.CanMove) return false;
            if (target.IsPoint) {
                anchor = target.Point.Value;
            } else if (target.Unit != null && target.Unit != owner) {
                anchor = target.Unit.Position;
            } else if (target.Unit == owner) {
                var enemies = TargetResolver.VisibleEnemies(owner).ToList();
                if (enemies.Count == 0) return false;
                var near = enemies
                    .Where(e => DistanceXZ(e.Position, owner.Position) <= SelfAnchorRadiusMeters)
                    .Select(e => (e.Position.x, e.Position.z))
                    .ToList();
                if (near.Count > 0) {
                    var c = FleeGeometry.Centroid(near).Value;
                    anchor = new Vector3(c.x, owner.Position.y, c.z);
                } else {
                    anchor = enemies.OrderBy(e => (e.Position - owner.Position).sqrMagnitude).First().Position;
                }
            } else {
                return false;
            }
            distance = DistanceXZ(owner.Position, anchor);
            return true;
        }

        /// <summary>Picks the navmesh-reachable candidate farthest from the anchor. False when
        /// no candidate gains FleeGeometry.MinGainMeters (the unit is cornered).</summary>
        public static bool TryGetFleeDestination(UnitEntityData owner, Vector3 anchor, float distance, float required,
                                                 out Vector3 destination) {
            destination = default;
            var pos = owner.Position;
            var facing = owner.OrientationDirection;
            var away = FleeGeometry.AwayDirection((pos.x, pos.z), (anchor.x, anchor.z), (facing.x, facing.z));
            float step = FleeGeometry.StepLength(distance, required);
            var candidates = FleeGeometry.CandidatePoints((pos.x, pos.z), away, step);
            var reachable = new List<(float x, float z)>(candidates.Count);
            var reachable3 = new List<Vector3>(candidates.Count);
            foreach (var c in candidates) {
                var end = new Vector3(c.x, pos.y, c.z);
                var hit = ObstacleAnalyzer.TraceAlongNavmesh(pos, end);
                reachable.Add((hit.x, hit.z));
                reachable3.Add(hit);
            }
            int idx = FleeGeometry.PickFarthest((anchor.x, anchor.z), reachable, distance);
            if (idx < 0) return false;
            destination = reachable3[idx];
            return true;
        }

        static float DistanceXZ(Vector3 a, Vector3 b) {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
