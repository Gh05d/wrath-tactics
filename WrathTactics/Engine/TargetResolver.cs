using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static class TargetResolver {
        public static UnitEntityData Resolve(TargetDef target, UnitEntityData owner) {
            var result = ResolveInternal(target, owner);
            if (result != null) {
                float dist = (result.Position - owner.Position).magnitude;
                Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> {result.CharacterName} " +
                    $"(type={target.Type}, dist={dist:F1}, inCombat={result.IsInCombat}, visible={result.IsVisibleForPlayer})");
            }
            return result;
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
            return Game.Instance.Player.Party.Where(u => u.IsInGame);
        }

        static IEnumerable<UnitEntityData> GetVisibleEnemies(UnitEntityData owner) {
            return Game.Instance.State.Units
                .Where(u => u.IsInGame && u.HPLeft > 0 && u.IsPlayersEnemy && u.IsInCombat);
        }
    }
}
