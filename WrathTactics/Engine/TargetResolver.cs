using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static class TargetResolver {
        // ~1 Pathfinder grid square (5 ft). Offsets the spawn point away from the anchor
        // unit so summons don't spawn inside the unit's collision volume. Tune during
        // smoke test if Wrath's internal scale differs.
        const float SummonOffsetDistance = 1.5f;

        public static ResolvedTarget Resolve(TargetDef target, UnitEntityData owner) {
            if (target.Type == TargetType.PointAtSelf
                || target.Type == TargetType.PointAtConditionTarget) {
                var resolved = ResolvePoint(target.Type, owner);
                if (resolved.IsValid) {
                    var p = resolved.Point.Value;
                    Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> point({p.x:F1},{p.y:F1},{p.z:F1}) (type={target.Type})");
                } else {
                    Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> <no point> (type={target.Type})");
                }
                return resolved;
            }

            var unit = ResolveInternal(target, owner);
            if (unit != null) {
                float dist = (unit.Position - owner.Position).magnitude;
                Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> {unit.CharacterName} " +
                    $"(type={target.Type}, dist={dist:F1}, inCombat={unit.IsInCombat}, visible={unit.IsVisibleForPlayer})");
                return new ResolvedTarget(unit);
            }
            Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> <none> (type={target.Type})");
            return ResolvedTarget.None;
        }

        static ResolvedTarget ResolvePoint(TargetType type, UnitEntityData owner) {
            switch (type) {
                case TargetType.PointAtSelf: {
                    var forward = owner.OrientationDirection;
                    return new ResolvedTarget(owner.Position + forward * SummonOffsetDistance);
                }
                case TargetType.PointAtConditionTarget: {
                    var anchor = ConditionEvaluator.LastMatchedEnemy?.Position
                               ?? ConditionEvaluator.LastMatchedAlly?.Position;
                    if (!anchor.HasValue) return ResolvedTarget.None;

                    var delta = owner.Position - anchor.Value;
                    delta.y = 0;
                    var offsetDir = delta.sqrMagnitude < 0.01f
                        ? owner.OrientationDirection
                        : delta.normalized;
                    return new ResolvedTarget(anchor.Value + offsetDir * SummonOffsetDistance);
                }
                default:
                    return ResolvedTarget.None;
            }
        }

        static UnitEntityData ResolveInternal(TargetDef target, UnitEntityData owner) {
            switch (target.Type) {
                case TargetType.Self:               return owner;
                case TargetType.AllyLowestHp:       return GetAllyLowestHp(owner);
                case TargetType.AllyWithCondition:  return GetAllyWithCondition(owner, target.Filter);
                case TargetType.AllyMissingBuff:    return GetAllyMissingBuff(owner, target.Filter);
                case TargetType.EnemyNearest:       return GetEnemyNearest(owner);
                case TargetType.EnemyLowestHp:      return GetEnemyLowestHp(owner);
                case TargetType.EnemyHighestHp:     return GetEnemyHighestHp(owner);
                case TargetType.EnemyHighestAC:     return GetEnemyHighestAC(owner);
                case TargetType.EnemyLowestAC:      return GetEnemyLowestAC(owner);
                case TargetType.EnemyHighestFort:   return GetEnemyHighestFort(owner);
                case TargetType.EnemyLowestFort:    return GetEnemyLowestFort(owner);
                case TargetType.EnemyHighestReflex: return GetEnemyHighestReflex(owner);
                case TargetType.EnemyLowestReflex:  return GetEnemyLowestReflex(owner);
                case TargetType.EnemyHighestWill:   return GetEnemyHighestWill(owner);
                case TargetType.EnemyLowestWill:    return GetEnemyLowestWill(owner);
                case TargetType.EnemyHighestThreat: return GetEnemyHighestThreat(owner);
                case TargetType.EnemyCreatureType:  return GetEnemyByCreatureType(owner, target.Filter);
                case TargetType.ConditionTarget:    return GetConditionTarget(owner);
                case TargetType.EnemyHighestHD:     return GetEnemyHighestHD(owner);
                case TargetType.EnemyLowestHD:      return GetEnemyLowestHD(owner);
                default:                            return null;
            }
        }

        static UnitEntityData GetAllyLowestHp(UnitEntityData owner) {
            return GetAllies(owner)
                .Where(u => u != owner)
                .OrderBy(u => (float)u.HPLeft / System.Math.Max(1, u.Stats.HitPoints.ModifiedValue))
                .FirstOrDefault();
        }

        static UnitEntityData GetAllyWithCondition(UnitEntityData owner, string conditionName) {
            return GetAllies(owner)
                .Where(u => u != owner)
                .FirstOrDefault(u => ConditionEvaluator.HasConditionByName(u, conditionName));
        }

        static UnitEntityData GetAllyMissingBuff(UnitEntityData owner, string buffGuid) {
            return GetAllies(owner)
                .Where(u => u != owner)
                .FirstOrDefault(u => !u.Buffs.RawFacts.Any(b =>
                    b.Blueprint.AssetGuid.ToString() == buffGuid));
        }

        static UnitEntityData GetEnemyNearest(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => (e.Position - owner.Position).sqrMagnitude)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyLowestHp(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => (float)e.HPLeft / System.Math.Max(1, e.Stats.HitPoints.ModifiedValue))
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestHp(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => (float)e.HPLeft / System.Math.Max(1, e.Stats.HitPoints.ModifiedValue))
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestAC(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => e.Stats.AC.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyLowestAC(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => e.Stats.AC.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyLowestFort(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => e.Stats.SaveFortitude.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestFort(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => e.Stats.SaveFortitude.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyLowestReflex(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => e.Stats.SaveReflex.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestReflex(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => e.Stats.SaveReflex.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyLowestWill(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => e.Stats.SaveWill.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestWill(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => e.Stats.SaveWill.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestHD(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => UnitExtensions.GetHD(e))
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyLowestHD(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => UnitExtensions.GetHD(e))
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestThreat(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => ThreatCalculator.Calculate(e))
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyByCreatureType(UnitEntityData owner, string creatureType) {
            return GetVisibleEnemies(owner)
                .FirstOrDefault(e => e.Blueprint.Type?.ToString() == creatureType);
        }

        static UnitEntityData GetConditionTarget(UnitEntityData owner) {
            // Prefer enemy match, then ally match
            if (ConditionEvaluator.LastMatchedEnemy != null)
                return ConditionEvaluator.LastMatchedEnemy;
            if (ConditionEvaluator.LastMatchedAlly != null)
                return ConditionEvaluator.LastMatchedAlly;
            return null;
        }

        static IEnumerable<UnitEntityData> GetAllies(UnitEntityData owner) {
            return Game.Instance.Player.PartyAndPets.Where(u => u.IsInGame);
        }

        static IEnumerable<UnitEntityData> GetVisibleEnemies(UnitEntityData owner) {
            return Game.Instance.State.Units
                .Where(u => u.IsInGame && u.HPLeft > 0 && u.IsPlayersEnemy && u.IsInCombat);
        }
    }
}
