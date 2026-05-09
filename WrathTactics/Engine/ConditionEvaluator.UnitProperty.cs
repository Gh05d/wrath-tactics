using System;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ConditionEvaluator {
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

                case ConditionProperty.IsFlanked: {
                    // Engine-authoritative positional flank: UnitCombatState.IsFlanked
                    // tracks current threat-from-multiple-attackers state.
                    bool flanked = unit.CombatState?.IsFlanked ?? false;
                    return EqualsBool(flanked, condition);
                }

                case ConditionProperty.AdjacentEnemyCount: {
                    // Counts visible in-combat enemies within Melee range (≤2 m / 5 ft) of
                    // the evaluated unit. Re-uses RangeBrackets.Melee for consistency with
                    // the WithinRange property.
                    if (CurrentOwner == null) return false;
                    float meleeRange = RangeBrackets.MaxMeters(RangeBracket.Melee);
                    int count = 0;
                    foreach (var e in GetVisibleEnemies(CurrentOwner)) {
                        if (Vector3.Distance(unit.Position, e.Position) <= meleeRange)
                            count++;
                    }
                    return CompareFloat(count, condition.Operator, threshold);
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

                case ConditionProperty.IsFlanked: {
                    bool flanked = unit.CombatState?.IsFlanked ?? false;
                    bool wantFlanked = ParseBoolValue(condition.Value);
                    bool match = flanked == wantFlanked;
                    return condition.Operator == ConditionOperator.NotEqual ? !match : match;
                }

                case ConditionProperty.AdjacentEnemyCount: {
                    if (CurrentOwner == null) return false;
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    float meleeRange = RangeBrackets.MaxMeters(RangeBracket.Melee);
                    int count = 0;
                    foreach (var e in GetVisibleEnemies(CurrentOwner)) {
                        if (Vector3.Distance(unit.Position, e.Position) <= meleeRange)
                            count++;
                    }
                    return CompareFloat(count, condition.Operator, threshold);
                }

                default:
                    return false;
            }
        }
    }
}
