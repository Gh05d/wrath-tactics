using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.Enums;
using UnityEngine;
using WrathTactics.Logging;
using WrathTactics.Models;
using KmAlignment = Kingmaker.Enums.Alignment;

namespace WrathTactics.Engine {
    public static partial class ConditionEvaluator {
        // Set during evaluation — the last entity that matched an Enemy/Ally condition
        public static UnitEntityData LastMatchedEnemy { get; private set; }
        public static UnitEntityData LastMatchedAlly { get; private set; }

        // Rule-scoped ambient state — set in Evaluate(rule, owner), cleared in finally.
        // Accessed by SpellDCMinusSave evaluation so the property helper stays one-arg
        // (matches HpPercent/AC shape). A stray access outside an active Evaluate
        // reads null and falls through to float.NaN → condition returns false.
        static ActionDef CurrentAction;
        static UnitEntityData CurrentOwner;
        // Cached party-best-AB for the duration of a single Evaluate call. NaN means
        // "not yet computed"; once computed, the same value is reused across all
        // enemies in EvaluateEnemyBucket (AB is enemy-independent). Cleared in finally.
        static float CurrentPartyBestAB = float.NaN;
        // Cached party-max-effective-level (CharacterLevel + MythicLevel) for the
        // duration of a single Evaluate call. -1 means "not yet computed"; once
        // computed, reused across all enemies in EvaluateEnemyBucket. Cleared in
        // finally. Sentinel is -1 (not 0) because 0 is a legitimate empty-party
        // result that should propagate as NaN downstream.
        static int CurrentPartyMaxLevel = -1;

        public static void ClearMatchedEntities() {
            LastMatchedEnemy = null;
            LastMatchedAlly = null;
        }

        public static bool Evaluate(TacticsRule rule, UnitEntityData owner) {
            if (rule.ConditionGroups == null || rule.ConditionGroups.Count == 0)
                return true;

            CurrentAction = rule.Action;
            CurrentOwner = owner;
            try {
                foreach (var group in rule.ConditionGroups) {
                    if (EvaluateGroup(group, owner))
                        return true;
                }
                return false;
            } finally {
                CurrentAction = null;
                CurrentOwner = null;
                CurrentPartyBestAB = float.NaN;
                CurrentPartyMaxLevel = -1;
            }
        }

        static bool EvaluateGroup(ConditionGroup group, UnitEntityData owner) {
            if (group.Conditions == null || group.Conditions.Count == 0)
                return true;

            var enemyConds = new List<Condition>();
            var allyConds  = new List<Condition>();
            var otherConds = new List<Condition>();

            foreach (var c in group.Conditions) {
                if (IsEnemyScope(c.Subject))      enemyConds.Add(c);
                else if (IsAllyScope(c.Subject))  allyConds.Add(c);
                else                              otherConds.Add(c);
            }

            foreach (var c in otherConds) {
                if (!EvaluateCondition(c, owner)) return false;
            }

            if (enemyConds.Count > 0 && !EvaluateEnemyBucket(enemyConds, owner)) return false;
            if (allyConds.Count  > 0 && !EvaluateAllyBucket(allyConds, owner))   return false;
            return true;
        }

        // Evaluates all Enemy-scope conditions as a single bucket: the bucket is satisfied
        // iff there exists a single enemy that passes every non-Count condition, AND the
        // count of enemies that pass every non-Count condition meets the Count threshold.
        // If a Pick subject is present, its metric sorts the iteration and its property
        // check is still applied (Pick acts as both sort hint and filter).
        static bool EvaluateCondition(Condition condition, UnitEntityData owner) {
            try {
                switch (condition.Subject) {
                    case ConditionSubject.Self:                return EvaluateUnitProperty(condition, owner);
                    case ConditionSubject.Ally:                return EvaluateAlly(condition, owner);
                    case ConditionSubject.AllyCount:           return EvaluateAllyCount(condition, owner);
                    case ConditionSubject.Enemy:               return EvaluateEnemy(condition, owner);
                    case ConditionSubject.EnemyCount:          return EvaluateEnemyCount(condition, owner);
                    case ConditionSubject.EnemyBiggestThreat:  return EvaluateEnemyPick(condition, owner, e => ThreatCalculator.Calculate(e), biggest: true);
                    case ConditionSubject.EnemyLowestThreat:   return EvaluateEnemyPick(condition, owner, e => ThreatCalculator.Calculate(e), biggest: false);
                    case ConditionSubject.EnemyHighestHp:      return EvaluateEnemyPick(condition, owner, HpPercent, biggest: true);
                    case ConditionSubject.EnemyLowestHp:       return EvaluateEnemyPick(condition, owner, HpPercent, biggest: false);
                    case ConditionSubject.EnemyLowestAC:      return EvaluateEnemyPick(condition, owner, UnitAC, biggest: false);
                    case ConditionSubject.EnemyHighestAC:     return EvaluateEnemyPick(condition, owner, UnitAC, biggest: true);
                    case ConditionSubject.EnemyLowestFort:    return EvaluateEnemyPick(condition, owner, UnitFort, biggest: false);
                    case ConditionSubject.EnemyHighestFort:   return EvaluateEnemyPick(condition, owner, UnitFort, biggest: true);
                    case ConditionSubject.EnemyLowestReflex:  return EvaluateEnemyPick(condition, owner, UnitReflex, biggest: false);
                    case ConditionSubject.EnemyHighestReflex: return EvaluateEnemyPick(condition, owner, UnitReflex, biggest: true);
                    case ConditionSubject.EnemyLowestWill:    return EvaluateEnemyPick(condition, owner, UnitWill, biggest: false);
                    case ConditionSubject.EnemyHighestWill:   return EvaluateEnemyPick(condition, owner, UnitWill, biggest: true);
                    case ConditionSubject.EnemyHighestHD:     return EvaluateEnemyPick(condition, owner, UnitHD, biggest: true);
                    case ConditionSubject.EnemyLowestHD:      return EvaluateEnemyPick(condition, owner, UnitHD, biggest: false);
                    case ConditionSubject.Combat:              return EvaluateCombat(condition);
                    default:                                   return false;
                }
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Failed to evaluate {condition.Subject}.{condition.Property}");
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
            // Value  = property threshold (e.g., "60" for HP < 60%)
            // Value2 = count threshold (e.g., "2" for 2 allies)
            // Operator      = comparison for the property (e.g., HP < 60)
            // CountOperator = comparison for the count itself (e.g., count >= 2)
            float countThreshold;
            if (!float.TryParse(condition.Value2, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out countThreshold))
                countThreshold = 1; // default: at least 1

            int count = 0;
            foreach (var ally in GetAllPartyMembers(owner)) {
                if (MatchesPropertyThreshold(condition, ally))
                    count++;
            }
            return CompareCount(count, countThreshold, condition.CountOperator);
        }

        static bool EvaluateEnemy(Condition condition, UnitEntityData owner) {
            int checkedCount = 0;
            var uniqueTypes = new HashSet<string>();
            foreach (var enemy in GetVisibleEnemies(owner)) {
                checkedCount++;
                string typeName = enemy.Blueprint?.Type?.name ?? "<no-type>";
                uniqueTypes.Add($"{enemy.CharacterName}({typeName})");
                if (EvaluateUnitProperty(condition, enemy)) {
                    LastMatchedEnemy = enemy;
                    return true;
                }
            }
            Log.Engine.Trace($"  EvaluateEnemy({condition.Property}={condition.Value}) for {owner.CharacterName}: checked {checkedCount} in-combat enemies, no match. All: {string.Join(", ", uniqueTypes)}");
            return false;
        }

        static bool EvaluateEnemyCount(Condition condition, UnitEntityData owner) {
            // Value2 = count threshold; Value = property threshold; CountOperator = comparison for the count.
            float countThreshold;
            if (!float.TryParse(condition.Value2, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out countThreshold))
                countThreshold = 1; // default: at least 1

            int count = 0;
            foreach (var enemy in GetVisibleEnemies(owner)) {
                if (MatchesPropertyThreshold(condition, enemy))
                    count++;
            }
            return CompareCount(count, countThreshold, condition.CountOperator);
        }

        internal static bool CompareCount(int actual, float threshold, ConditionOperator op) {
            int t = (int)threshold;
            switch (op) {
                case ConditionOperator.LessThan:       return actual <  threshold;
                case ConditionOperator.LessOrEqual:    return actual <= threshold;
                case ConditionOperator.Equal:          return actual == t;
                case ConditionOperator.NotEqual:       return actual != t;
                case ConditionOperator.GreaterThan:    return actual >  threshold;
                case ConditionOperator.GreaterOrEqual: return actual >= threshold;
                default:                                return actual >= threshold;
            }
        }

        static bool EvaluateCombat(Condition condition) {
            if (condition.Property == ConditionProperty.IsInCombat) {
                bool inCombat = Game.Instance.Player.IsInCombat;
                bool wanted = ParseBoolValue(condition.Value);
                bool match = inCombat == wanted;
                return condition.Operator == ConditionOperator.NotEqual ? !match : match;
            }

            if (condition.Property != ConditionProperty.CombatRounds) return false;

            float threshold;
            if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out threshold))
                return false;

            float gameTimeSec = (float)Game.Instance.Player.GameTime.TotalSeconds;
            float combatRounds = TacticsEvaluator.GetCombatRoundsElapsed(gameTimeSec);
            return CompareFloat(combatRounds, condition.Operator, threshold);
        }

        static bool ParseBoolValue(string raw) {
            if (string.IsNullOrEmpty(raw)) return false;
            switch (raw.Trim().ToLowerInvariant()) {
                case "true":
                case "1":
                case "yes":
                case "ja":
                    return true;
                case "false":
                case "0":
                case "no":
                case "nein":
                    return false;
                default:
                    Log.Engine.Warn($"ParseBoolValue: unrecognized value '{raw}', defaulting to false");
                    return false;
            }
        }

        // Compares an actual boolean against the condition's Yes/No value, honoring the
        // Equal/NotEqual operator. Used by all bool-valued properties (IsDead, IsInCombat,
        // IsTargetingSelf/Ally, IsTargetedByAlly/Enemy).
        static bool EqualsBool(bool actual, Condition c) {
            bool wanted = ParseBoolValue(c.Value);
            bool match = actual == wanted;
            return c.Operator == ConditionOperator.NotEqual ? !match : match;
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

                case ConditionProperty.SaveFortitude:
                    return CompareFloat(unit.Stats.SaveFortitude.ModifiedValue, condition.Operator, threshold);

                case ConditionProperty.SaveReflex:
                    return CompareFloat(unit.Stats.SaveReflex.ModifiedValue, condition.Operator, threshold);

                case ConditionProperty.SaveWill:
                    return CompareFloat(unit.Stats.SaveWill.ModifiedValue, condition.Operator, threshold);

                case ConditionProperty.HitDice:
                    return CompareFloat(UnitExtensions.GetHD(unit), condition.Operator, threshold);

                case ConditionProperty.SpellDCMinusSave: {
                    float margin = ComputeDCMinusSave(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }

                case ConditionProperty.ABMinusAC: {
                    if (!IsEnemyScope(condition.Subject)) {
                        Log.Engine.Trace($"ABMinusAC: subject {condition.Subject} is not Enemy-scope, returning false");
                        return false;
                    }
                    float margin = ComputeABMinusAC(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }

                case ConditionProperty.EnemyHDMinusPartyLevel: {
                    if (!IsEnemyScope(condition.Subject)) {
                        Log.Engine.Trace($"EnemyHDMinusPartyLevel: subject {condition.Subject} is not Enemy-scope, returning false");
                        return false;
                    }
                    float margin = ComputeHDMinusPartyLevel(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }

                case ConditionProperty.IsTargetingSelf: {
                    if (!IsEnemyScope(condition.Subject)) {
                        Log.Engine.Trace($"IsTargetingSelf: subject {condition.Subject} is not Enemy-scope, returning false");
                        return false;
                    }
                    bool match = TargetingRelations.Has(unit, CurrentOwner);
                    Log.Engine.Trace($"IsTargetingSelf: {unit?.CharacterName} targets {CurrentOwner?.CharacterName}? {match}");
                    return EqualsBool(match, condition);
                }

                case ConditionProperty.IsTargetingAlly: {
                    if (!IsEnemyScope(condition.Subject)) {
                        Log.Engine.Trace($"IsTargetingAlly: subject {condition.Subject} is not Enemy-scope, returning false");
                        return false;
                    }
                    // Value2 = optional UniqueId pin. Empty = any ally (legacy behaviour).
                    var pinned = AllyProvider.Resolve(condition.Value2);
                    bool match = false;
                    foreach (var ally in GetAllPartyMembers(CurrentOwner)) {
                        if (ally == null || ally == CurrentOwner) continue;
                        if (!ally.IsInGame) continue;
                        if (ally.Descriptor?.State?.IsFinallyDead ?? false) continue;
                        if (pinned != null && ally != pinned) continue;
                        if (TargetingRelations.Has(unit, ally)) {
                            Log.Engine.Trace($"IsTargetingAlly: {unit?.CharacterName} targets {ally.CharacterName}");
                            match = true;
                            break;
                        }
                    }
                    return EqualsBool(match, condition);
                }

                case ConditionProperty.IsTargetedByAlly: {
                    if (!IsEnemyScope(condition.Subject)) {
                        Log.Engine.Trace($"IsTargetedByAlly: subject {condition.Subject} is not Enemy-scope, returning false");
                        return false;
                    }
                    var pinned = AllyProvider.Resolve(condition.Value2);
                    bool match = false;
                    foreach (var ally in GetAllPartyMembers(CurrentOwner)) {
                        if (ally == null || ally == CurrentOwner) continue;
                        if (!ally.IsInGame) continue;
                        if (ally.Descriptor?.State?.IsFinallyDead ?? false) continue;
                        if (pinned != null && ally != pinned) continue;
                        if (TargetingRelations.Has(ally, unit)) {
                            Log.Engine.Trace($"IsTargetedByAlly: {ally.CharacterName} targets {unit?.CharacterName}");
                            match = true;
                            break;
                        }
                    }
                    return EqualsBool(match, condition);
                }

                case ConditionProperty.IsTargetedByEnemy: {
                    if (!IsAllyScope(condition.Subject)) {
                        Log.Engine.Trace($"IsTargetedByEnemy: subject {condition.Subject} is not Ally-scope, returning false");
                        return false;
                    }
                    bool match = false;
                    foreach (var enemy in GetVisibleEnemies(CurrentOwner)) {
                        if (TargetingRelations.Has(enemy, unit)) {
                            Log.Engine.Trace($"IsTargetedByEnemy: {enemy.CharacterName} targets {unit?.CharacterName}");
                            match = true;
                            break;
                        }
                    }
                    return EqualsBool(match, condition);
                }

                case ConditionProperty.IsDead: {
                    // Value is the "true"/"false" payload written by the Yes/No dropdown.
                    // UnitState.IsDead is just LifeState==Dead — set in-combat when a companion
                    // drops past -CON on Normal difficulty, even though the companion will
                    // auto-revive at combat end (red portrait, IsFinallyDead=false). Using
                    // IsDead caused BoL to fire on down-but-recovering allies. IsFinallyDead
                    // is the persisted flag the game pairs with CompanionState.Dead for the
                    // greyed-portrait / permadeath state that genuinely needs resurrection.
                    bool isDead = unit.Descriptor?.State?.IsFinallyDead ?? false;
                    return EqualsBool(isDead, condition);
                }

                case ConditionProperty.IsSummon: {
                    // UnitPartSummonedMonster is engine-added (Ensure<>+Init) on every Summon
                    // Monster / Animate Dead / Create Undead / etc. cast. Pets, animal companions,
                    // Aivu, and Eidolons carry UnitPartPetMaster/UnitPartCompanion but NOT this
                    // part — so this check excludes them, matching the user-facing semantic.
                    bool isSummon = unit.Get<Kingmaker.UnitLogic.Parts.UnitPartSummonedMonster>() != null;
                    return EqualsBool(isSummon, condition);
                }

                case ConditionProperty.IsPet: {
                    // UnitPartPet is the engine's canonical pet marker — covers all PetType
                    // values (AnimalCompanion / MythicSkeletalChampion / AzataHavocDragon / Clone
                    // / NightHag) plus Eidolons. Symmetric to IsSummon for filtering pets out of
                    // (or into) global rules.
                    bool isPet = unit.Get<Kingmaker.UnitLogic.Parts.UnitPartPet>() != null;
                    return EqualsBool(isPet, condition);
                }

                case ConditionProperty.HasBuff: {
                    bool hasBuff = unit.Buffs.RawFacts.Any(b =>
                        b.Blueprint.AssetGuid.ToString() == condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !hasBuff : hasBuff;
                }

                case ConditionProperty.HasCondition: {
                    bool hasCond = HasConditionByName(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !hasCond : hasCond;
                }

                case ConditionProperty.SpellSlotsAtLevel:
                    int level = (int)threshold;
                    return CountAvailableSlotsAtLevel(unit, level) > 0;

                case ConditionProperty.SpellSlotsAboveLevel:
                    int minLevel = (int)threshold;
                    return CountAvailableSlotsAboveLevel(unit, minLevel) > 0;

                case ConditionProperty.Resource:
                    return HasResource(unit, condition.Value);

                case ConditionProperty.CreatureType:
                    bool ctMatch = CheckCreatureType(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !ctMatch : ctMatch;

                case ConditionProperty.Alignment:
                    bool alignMatch = CheckAlignment(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !alignMatch : alignMatch;

                case ConditionProperty.HasClass:
                    bool hasClassMatch = UnitExtensions.MatchesClassValue(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !hasClassMatch : hasClassMatch;

                case ConditionProperty.WithinRange: {
                    if (CurrentOwner == null) return false;
                    if (!RangeBrackets.TryParse(condition.Value, out var bracket)) {
                        Log.Engine.Warn($"WithinRange: unknown bracket '{condition.Value}' on {unit.CharacterName}");
                        return false;
                    }
                    float d = Vector3.Distance(CurrentOwner.Position, unit.Position);
                    float lo = RangeBrackets.LowerMeters(bracket);
                    float hi = RangeBrackets.MaxMeters(bracket);
                    switch (condition.Operator) {
                        case ConditionOperator.Equal:          return d > lo && d <= hi;
                        case ConditionOperator.NotEqual:       return !(d > lo && d <= hi);
                        case ConditionOperator.LessOrEqual:    return d <= hi;
                        case ConditionOperator.LessThan:       return d <= lo;
                        case ConditionOperator.GreaterOrEqual: return d > lo;
                        case ConditionOperator.GreaterThan:    return d > hi;
                        default:                               return false;
                    }
                }

                default:
                    return false;
            }
        }

        static bool MatchesPropertyThreshold(Condition condition, UnitEntityData unit) {
            float threshold;
            switch (condition.Property) {
                case ConditionProperty.HpPercent:
                    if (unit.HPLeft <= 0) return false; // Don't count dead as "low HP"
                    float hpPct = (float)unit.HPLeft / Math.Max(1, unit.Stats.HitPoints.ModifiedValue) * 100f;
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    return CompareFloat(hpPct, condition.Operator, threshold);

                case ConditionProperty.AC:
                    float ac = unit.Stats.AC.ModifiedValue;
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    return CompareFloat(ac, condition.Operator, threshold);

                case ConditionProperty.SaveFortitude:
                case ConditionProperty.SaveReflex:
                case ConditionProperty.SaveWill:
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    int saveVal = condition.Property == ConditionProperty.SaveFortitude ? unit.Stats.SaveFortitude.ModifiedValue
                        : condition.Property == ConditionProperty.SaveReflex ? unit.Stats.SaveReflex.ModifiedValue
                        : unit.Stats.SaveWill.ModifiedValue;
                    return CompareFloat(saveVal, condition.Operator, threshold);

                case ConditionProperty.HitDice:
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    return CompareFloat(UnitExtensions.GetHD(unit), condition.Operator, threshold);

                case ConditionProperty.SpellDCMinusSave: {
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    float margin = ComputeDCMinusSave(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }

                case ConditionProperty.EnemyHDMinusPartyLevel: {
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    float margin = ComputeHDMinusPartyLevel(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }

                case ConditionProperty.IsDead: {
                    // See note in EvaluateUnitProperty.IsDead: use State.IsFinallyDead, not
                    // State.IsDead — the latter fires for auto-recovering downed companions.
                    bool dead = unit.Descriptor?.State?.IsFinallyDead ?? false;
                    bool wantDead = ParseBoolValue(condition.Value);
                    bool match = dead == wantDead;
                    return condition.Operator == ConditionOperator.NotEqual ? !match : match;
                }

                case ConditionProperty.IsSummon: {
                    bool isSummon = unit.Get<Kingmaker.UnitLogic.Parts.UnitPartSummonedMonster>() != null;
                    bool wantSummon = ParseBoolValue(condition.Value);
                    bool match = isSummon == wantSummon;
                    return condition.Operator == ConditionOperator.NotEqual ? !match : match;
                }

                case ConditionProperty.IsPet: {
                    bool isPet = unit.Get<Kingmaker.UnitLogic.Parts.UnitPartPet>() != null;
                    bool wantPet = ParseBoolValue(condition.Value);
                    bool match = isPet == wantPet;
                    return condition.Operator == ConditionOperator.NotEqual ? !match : match;
                }

                case ConditionProperty.HasCondition: {
                    bool hasCond = HasConditionByName(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !hasCond : hasCond;
                }

                case ConditionProperty.HasBuff: {
                    bool hasBuff = !string.IsNullOrEmpty(condition.Value) && unit.Buffs.RawFacts.Any(b =>
                        b.Blueprint.AssetGuid.ToString() == condition.Value ||
                        (b.Blueprint.name?.Contains(condition.Value) ?? false));
                    return condition.Operator == ConditionOperator.NotEqual ? !hasBuff : hasBuff;
                }

                case ConditionProperty.CreatureType:
                    bool ctMatch2 = CheckCreatureType(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !ctMatch2 : ctMatch2;

                case ConditionProperty.Alignment:
                    bool alignMatch2 = CheckAlignment(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !alignMatch2 : alignMatch2;

                case ConditionProperty.HasClass:
                    bool hasClassMatch2 = UnitExtensions.MatchesClassValue(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !hasClassMatch2 : hasClassMatch2;

                case ConditionProperty.WithinRange: {
                    if (CurrentOwner == null) return false;
                    if (!RangeBrackets.TryParse(condition.Value, out var bracket)) return false;
                    float d = Vector3.Distance(CurrentOwner.Position, unit.Position);
                    float lo = RangeBrackets.LowerMeters(bracket);
                    float hi = RangeBrackets.MaxMeters(bracket);
                    switch (condition.Operator) {
                        case ConditionOperator.Equal:          return d > lo && d <= hi;
                        case ConditionOperator.NotEqual:       return !(d > lo && d <= hi);
                        case ConditionOperator.LessOrEqual:    return d <= hi;
                        case ConditionOperator.LessThan:       return d <= lo;
                        case ConditionOperator.GreaterOrEqual: return d > lo;
                        case ConditionOperator.GreaterThan:    return d > hi;
                        default:                               return false;
                    }
                }

                default:
                    return false;
            }
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

        static bool IsEnemyScope(ConditionSubject s) {
            switch (s) {
                case ConditionSubject.Enemy:
                case ConditionSubject.EnemyCount:
                case ConditionSubject.EnemyBiggestThreat:
                case ConditionSubject.EnemyLowestThreat:
                case ConditionSubject.EnemyHighestHp:
                case ConditionSubject.EnemyLowestHp:
                case ConditionSubject.EnemyLowestAC:
                case ConditionSubject.EnemyHighestAC:
                case ConditionSubject.EnemyLowestFort:
                case ConditionSubject.EnemyHighestFort:
                case ConditionSubject.EnemyLowestReflex:
                case ConditionSubject.EnemyHighestReflex:
                case ConditionSubject.EnemyLowestWill:
                case ConditionSubject.EnemyHighestWill:
                case ConditionSubject.EnemyHighestHD:
                case ConditionSubject.EnemyLowestHD:
                    return true;
                default:
                    return false;
            }
        }

        static bool IsAllyScope(ConditionSubject s) {
            return s == ConditionSubject.Ally
                || s == ConditionSubject.AllyCount
                || s == ConditionSubject.AllyByName;
        }


        static IEnumerable<UnitEntityData> GetAllPartyMembers(UnitEntityData owner) {
            return Game.Instance.Player.PartyAndPets.Where(u => u.IsInGame);
        }

        static IEnumerable<UnitEntityData> GetVisibleEnemies(UnitEntityData owner) {
            // Only consider enemies actively in combat with the party.
            // Without IsInCombat, companions would run off to attack enemies
            // that aren't even engaged (seen across the map).
            return Game.Instance.State.Units
                .Where(u => u.IsInGame
                    && u.HPLeft > 0
                    && u.IsPlayersEnemy
                    && u.IsInCombat);
        }
    }
}
