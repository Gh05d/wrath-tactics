using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static class ConditionEvaluator {
        // Set during evaluation — the last entity that matched an Enemy/Ally condition
        public static UnitEntityData LastMatchedEnemy { get; private set; }
        public static UnitEntityData LastMatchedAlly { get; private set; }

        public static void ClearMatchedEntities() {
            LastMatchedEnemy = null;
            LastMatchedAlly = null;
        }

        public static bool Evaluate(TacticsRule rule, UnitEntityData owner) {
            if (rule.ConditionGroups == null || rule.ConditionGroups.Count == 0)
                return true;

            foreach (var group in rule.ConditionGroups) {
                if (EvaluateGroup(group, owner))
                    return true;
            }
            return false;
        }

        static bool EvaluateGroup(ConditionGroup group, UnitEntityData owner) {
            if (group.Conditions == null || group.Conditions.Count == 0)
                return true;

            foreach (var condition in group.Conditions) {
                if (!EvaluateCondition(condition, owner))
                    return false;
            }
            return true;
        }

        static bool EvaluateCondition(Condition condition, UnitEntityData owner) {
            try {
                switch (condition.Subject) {
                    case ConditionSubject.Self:       return EvaluateUnitProperty(condition, owner);
                    case ConditionSubject.Ally:       return EvaluateAlly(condition, owner);
                    case ConditionSubject.AllyCount:  return EvaluateAllyCount(condition, owner);
                    case ConditionSubject.Enemy:      return EvaluateEnemy(condition, owner);
                    case ConditionSubject.EnemyCount: return EvaluateEnemyCount(condition, owner);
                    case ConditionSubject.Combat:     return EvaluateCombat(condition);
                    default:                          return false;
                }
            } catch (Exception ex) {
                Main.Error(ex, $"[Condition] Failed to evaluate {condition.Subject}.{condition.Property}");
                return false;
            }
        }

        static bool EvaluateAlly(Condition condition, UnitEntityData owner) {
            foreach (var ally in GetAllPartyMembers(owner)) {
                if (ally == owner) continue;
                if (EvaluateUnitProperty(condition, ally)) {
                    LastMatchedAlly = ally;
                    return true;
                }
            }
            return false;
        }

        static bool EvaluateAllyCount(Condition condition, UnitEntityData owner) {
            // Value = property threshold (e.g., "60" for HP < 60%)
            // Value2 = count threshold (e.g., "2" for 2+ allies)
            // Operator = comparison for the count (e.g., >= 2)
            float countThreshold;
            if (!float.TryParse(condition.Value2, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out countThreshold))
                countThreshold = 1; // default: at least 1

            int count = 0;
            foreach (var ally in GetAllPartyMembers(owner)) {
                if (MatchesPropertyThreshold(condition, ally))
                    count++;
            }
            // Count comparison is ALWAYS >= (hardcoded, UI shows "count >=")
            return count >= countThreshold;
        }

        static bool EvaluateEnemy(Condition condition, UnitEntityData owner) {
            foreach (var enemy in GetVisibleEnemies(owner)) {
                if (EvaluateUnitProperty(condition, enemy)) {
                    LastMatchedEnemy = enemy;
                    return true;
                }
            }
            return false;
        }

        static bool EvaluateEnemyCount(Condition condition, UnitEntityData owner) {
            // Value2 = count threshold; Value = property threshold (currently unused for enemy count)
            float countThreshold;
            if (!float.TryParse(condition.Value2, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out countThreshold))
                countThreshold = 1; // default: at least 1

            int count = GetVisibleEnemies(owner).Count();
            // Count comparison is ALWAYS >= (hardcoded)
            return count >= countThreshold;
        }

        static bool EvaluateCombat(Condition condition) {
            if (condition.Property != ConditionProperty.CombatRounds) return false;

            float threshold;
            if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out threshold))
                return false;

            float gameTimeSec = (float)Game.Instance.Player.GameTime.TotalSeconds;
            float combatRounds = TacticsEvaluator.GetCombatRoundsElapsed(gameTimeSec);
            return CompareFloat(combatRounds, condition.Operator, threshold);
        }

        static bool EvaluateUnitProperty(Condition condition, UnitEntityData unit) {
            float threshold;
            float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out threshold);

            switch (condition.Property) {
                case ConditionProperty.HpPercent:
                    if (unit.HPLeft <= 0 && unit.Stats.HitPoints.ModifiedValue > 0)
                        return CompareFloat(0, condition.Operator, threshold);
                    float hpPct = (float)unit.HPLeft / unit.Stats.HitPoints.ModifiedValue * 100f;
                    return CompareFloat(hpPct, condition.Operator, threshold);

                case ConditionProperty.AC:
                    int ac = unit.Stats.AC.ModifiedValue;
                    return CompareFloat(ac, condition.Operator, threshold);

                case ConditionProperty.IsDead:
                    bool isDead = unit.HPLeft <= 0;
                    bool wantDead = threshold > 0;
                    return isDead == wantDead;

                case ConditionProperty.HasBuff:
                    return unit.Buffs.RawFacts.Any(b =>
                        b.Blueprint.AssetGuid.ToString() == condition.Value);

                case ConditionProperty.MissingBuff:
                    return !unit.Buffs.RawFacts.Any(b =>
                        b.Blueprint.AssetGuid.ToString() == condition.Value);

                case ConditionProperty.HasCondition:
                    return HasConditionByName(unit, condition.Value);

                case ConditionProperty.HasDebuff:
                    if (string.IsNullOrEmpty(condition.Value)) return false;
                    string debuffSearch = condition.Value.ToLowerInvariant();
                    return unit.Buffs.RawFacts.Any(b =>
                        b.Blueprint.name != null &&
                        b.Blueprint.name.ToLowerInvariant().Contains(debuffSearch));

                case ConditionProperty.SpellSlotsAtLevel:
                    int level = (int)threshold;
                    return CountAvailableSlotsAtLevel(unit, level) > 0;

                case ConditionProperty.SpellSlotsAboveLevel:
                    int minLevel = (int)threshold;
                    return CountAvailableSlotsAboveLevel(unit, minLevel) > 0;

                case ConditionProperty.Resource:
                    return HasResource(unit, condition.Value);

                case ConditionProperty.CreatureType:
                    return unit.Blueprint.Type?.ToString() == condition.Value;

                default:
                    return false;
            }
        }

        static bool MatchesPropertyThreshold(Condition condition, UnitEntityData unit) {
            switch (condition.Property) {
                case ConditionProperty.HpPercent:
                    if (unit.HPLeft <= 0) return false; // Don't count dead as "low HP"
                    float hpPct = (float)unit.HPLeft / Math.Max(1, unit.Stats.HitPoints.ModifiedValue) * 100f;
                    float threshold;
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    return hpPct < threshold;

                case ConditionProperty.IsDead:
                    return unit.HPLeft <= 0;

                case ConditionProperty.HasCondition:
                    return HasConditionByName(unit, condition.Value);

                case ConditionProperty.HasDebuff:
                    if (string.IsNullOrEmpty(condition.Value)) return false;
                    string debuffMatch = condition.Value.ToLowerInvariant();
                    return unit.Buffs.RawFacts.Any(b =>
                        b.Blueprint.name != null &&
                        b.Blueprint.name.ToLowerInvariant().Contains(debuffMatch));

                default:
                    return false;
            }
        }

        public static bool HasConditionByName(UnitEntityData unit, string conditionName) {
            switch (conditionName?.ToLowerInvariant()) {
                case "paralyzed":  return unit.State.HasCondition(UnitCondition.Paralyzed);
                case "stunned":    return unit.State.HasCondition(UnitCondition.Stunned);
                case "frightened": return unit.State.HasCondition(UnitCondition.Frightened);
                case "nauseated":  return unit.State.HasCondition(UnitCondition.Nauseated);
                case "confused":   return unit.State.HasCondition(UnitCondition.Confusion);
                case "blinded":    return unit.State.HasCondition(UnitCondition.Blindness);
                case "prone":      return unit.State.HasCondition(UnitCondition.Prone);
                case "entangled":  return unit.State.HasCondition(UnitCondition.Entangled);
                case "exhausted":  return unit.State.HasCondition(UnitCondition.Exhausted);
                case "fatigued":   return unit.State.HasCondition(UnitCondition.Fatigued);
                case "shaken":     return unit.State.HasCondition(UnitCondition.Shaken);
                case "sickened":   return unit.State.HasCondition(UnitCondition.Sickened);
                case "sleeping":   return unit.State.HasCondition(UnitCondition.Sleeping);
                case "petrified":  return unit.State.HasCondition(UnitCondition.Petrified);
                default:           return false;
            }
        }

        static int CountAvailableSlotsAtLevel(UnitEntityData unit, int level) {
            int total = 0;
            foreach (var book in unit.Spellbooks) {
                if (book.Blueprint.Spontaneous) {
                    total += book.GetSpontaneousSlots(level);
                } else {
                    foreach (var slot in book.GetMemorizedSpells(level)) {
                        if (slot.Spell != null && slot.Available)
                            total++;
                    }
                }
            }
            return total;
        }

        static int CountAvailableSlotsAboveLevel(UnitEntityData unit, int minLevel) {
            int total = 0;
            for (int l = minLevel; l <= 9; l++) {
                total += CountAvailableSlotsAtLevel(unit, l);
            }
            return total;
        }

        static bool HasResource(UnitEntityData unit, string resourceGuid) {
            if (string.IsNullOrEmpty(resourceGuid)) return false;
            foreach (var resource in unit.Resources.PersistantResources) {
                if (resource.Blueprint.AssetGuid.ToString() == resourceGuid && resource.Amount > 0)
                    return true;
            }
            return false;
        }

        static bool CompareFloat(float left, ConditionOperator op, float right) {
            switch (op) {
                case ConditionOperator.LessThan:      return left < right;
                case ConditionOperator.GreaterThan:   return left > right;
                case ConditionOperator.Equal:         return Math.Abs(left - right) < 0.01f;
                case ConditionOperator.NotEqual:      return Math.Abs(left - right) >= 0.01f;
                case ConditionOperator.GreaterOrEqual: return left >= right;
                case ConditionOperator.LessOrEqual:   return left <= right;
                default:                              return false;
            }
        }

        static IEnumerable<UnitEntityData> GetLivingPartyMembers(UnitEntityData owner) {
            return Game.Instance.Player.Party.Where(u => u.IsInGame && u.HPLeft > 0);
        }

        static IEnumerable<UnitEntityData> GetAllPartyMembers(UnitEntityData owner) {
            return Game.Instance.Player.Party.Where(u => u.IsInGame);
        }

        static IEnumerable<UnitEntityData> GetVisibleEnemies(UnitEntityData owner) {
            return Game.Instance.State.Units
                .Where(u => u.IsInGame
                    && u.HPLeft > 0
                    && u.IsPlayersEnemy
                    && u.IsVisibleForPlayer);
        }
    }
}
