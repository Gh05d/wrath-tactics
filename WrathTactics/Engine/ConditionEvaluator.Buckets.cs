using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ConditionEvaluator {
        static bool EvaluateEnemyBucket(List<Condition> conds, UnitEntityData owner) {
            var enemies = GetVisibleEnemies(owner).ToList();
            if (enemies.Count == 0) return false;

            var nonCountConds = conds.Where(c => c.Subject != ConditionSubject.EnemyCount).ToList();
            var countConds    = conds.Where(c => c.Subject == ConditionSubject.EnemyCount).ToList();

            // Sort by the first Pick subject's metric (if any).
            Condition pickCond = nonCountConds.FirstOrDefault(c => PickMetric(c.Subject, out _) != null);
            IEnumerable<UnitEntityData> ordered = enemies;
            if (pickCond != null) {
                var metric = PickMetric(pickCond.Subject, out bool biggest);
                ordered = biggest
                    ? enemies.OrderByDescending(metric)
                    : enemies.OrderBy(metric);
            }

            // Pick-or-Enemy path: find first enemy that passes every non-Count condition.
            UnitEntityData matchedEnemy = null;
            if (nonCountConds.Count > 0) {
                foreach (var enemy in ordered) {
                    bool allPass = true;
                    foreach (var c in nonCountConds) {
                        if (!EvaluateUnitProperty(c, enemy)) { allPass = false; break; }
                    }
                    if (allPass) { matchedEnemy = enemy; break; }
                }
                if (matchedEnemy == null) return false;
            }

            // Count path: count enemies that pass every non-Count condition AND every Count
            // condition's property-threshold. Threshold = max Value2 across Count conditions.
            // Operator = first Count row's CountOperator — bucket UI emits one count row, so
            // mixed operators across multiple Count rows is a misuse; take the first.
            if (countConds.Count > 0) {
                float countThreshold = 1f;
                foreach (var cc in countConds) {
                    if (float.TryParse(cc.Value2, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out float v) && v > countThreshold)
                        countThreshold = v;
                }
                var countOp = countConds[0].CountOperator;

                int count = 0;
                UnitEntityData firstCounted = null;
                foreach (var enemy in enemies) {
                    bool allPass = true;
                    foreach (var c in nonCountConds) {
                        if (!EvaluateUnitProperty(c, enemy)) { allPass = false; break; }
                    }
                    if (!allPass) continue;
                    foreach (var cc in countConds) {
                        if (!MatchesPropertyThreshold(cc, enemy)) { allPass = false; break; }
                    }
                    if (allPass) {
                        count++;
                        if (firstCounted == null) firstCounted = enemy;
                    }
                }
                if (!CompareCount(count, countThreshold, countOp)) return false;
                // Count-only groups (no per-enemy conditions) previously latched nothing,
                // so TargetType.ConditionTarget resolved to None and the rule silently
                // skipped at validation ("rule matches but nobody attacks" report). Fall
                // back to the first counted match. Stays null for pass-with-zero gates
                // (e.g. EnemyCount < 3 passing on count=0) — no enemy satisfied the
                // predicate, so there is no ConditionTarget to offer.
                if (matchedEnemy == null) matchedEnemy = firstCounted;
            }

            // Latch only after the whole bucket passed — latching before the count
            // gate leaks a unit from a failing group into later OR-groups.
            if (matchedEnemy != null) LastMatchedEnemy = matchedEnemy;
            return true;
        }

        // Ally analogue of EvaluateEnemyBucket — no Pick subjects exist for Ally scope,
        // so the logic is simpler: a matching Ally (for the non-Count path) and/or a
        // satisfied Count threshold.
        static bool EvaluateAllyBucket(List<Condition> conds, UnitEntityData owner) {
            var allies = GetAllPartyMembers(owner).Where(a => a != owner).ToList();
            if (allies.Count == 0 && conds.All(c => c.Subject != ConditionSubject.AllyCount))
                return false;

            // AllyByName conditions pin a specific ally per condition (Value2 = UniqueId).
            // They short-circuit the iterative match below: each must pass against its
            // own pinned ally, or the whole group fails. LastMatchedAlly latches onto
            // the first pinned ally (at the end, once the whole bucket passed) so
            // ConditionTarget / PointAtConditionTarget can find it downstream.
            UnitEntityData firstPinned = null;
            var byNameConds = conds.Where(c => c.Subject == ConditionSubject.AllyByName).ToList();
            foreach (var c in byNameConds) {
                var pinned = AllyProvider.Resolve(c.Value2);
                if (pinned == null) {
                    Log.Engine.Trace($"AllyByName: cannot resolve UniqueId='{c.Value2}' (Property={c.Property})");
                    return false;
                }
                if (!EvaluateUnitProperty(c, pinned)) return false;
                if (firstPinned == null) firstPinned = pinned;
            }

            var nonCountConds = conds.Where(c =>
                c.Subject != ConditionSubject.AllyCount
                && c.Subject != ConditionSubject.AllyByName).ToList();
            var countConds    = conds.Where(c => c.Subject == ConditionSubject.AllyCount).ToList();

            UnitEntityData matchedAlly = null;
            if (nonCountConds.Count > 0) {
                foreach (var ally in allies) {
                    bool allPass = true;
                    foreach (var c in nonCountConds) {
                        if (!EvaluateUnitProperty(c, ally)) { allPass = false; break; }
                    }
                    if (allPass) { matchedAlly = ally; break; }
                }
                if (matchedAlly == null) return false;
            }

            if (countConds.Count > 0) {
                float countThreshold = 1f;
                foreach (var cc in countConds) {
                    if (float.TryParse(cc.Value2, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out float v) && v > countThreshold)
                        countThreshold = v;
                }
                var countOp = countConds[0].CountOperator;

                int count = 0;
                UnitEntityData firstCounted = null;
                var perAlly = new List<string>();
                // AllyCount historically includes self; keep that behavior (use GetAllPartyMembers
                // without filtering owner) for Count, to match the previous EvaluateAllyCount.
                foreach (var ally in GetAllPartyMembers(owner)) {
                    bool allPass = true;
                    string failReason = null;
                    foreach (var c in nonCountConds) {
                        if (!EvaluateUnitProperty(c, ally)) { allPass = false; failReason = $"non-count {c.Property}"; break; }
                    }
                    if (allPass) {
                        foreach (var cc in countConds) {
                            if (!MatchesPropertyThreshold(cc, ally)) { allPass = false; failReason = $"count {cc.Property}={cc.Value}"; break; }
                        }
                    }
                    if (allPass) {
                        count++;
                        if (firstCounted == null) firstCounted = ally;
                    }
                    int hpPct = ally.Stats.HitPoints.ModifiedValue > 0
                        ? (int)(100f * ally.HPLeft / ally.Stats.HitPoints.ModifiedValue) : 0;
                    float dist = UnityEngine.Vector3.Distance(owner.Position, ally.Position);
                    perAlly.Add($"{ally.CharacterName}(hp={hpPct}%,d={dist:F1}m):{(allPass ? "pass" : "fail@" + failReason)}");
                }
                if (!CompareCount(count, countThreshold, countOp)) {
                    Log.Engine.Trace($"AllyBucket miss: count={count} {countOp} threshold={countThreshold} [{string.Join(", ", perAlly)}]");
                    return false;
                }
                // Count-only fallback so ConditionTarget resolves — see EvaluateEnemyBucket.
                // Note: may latch the owner (AllyCount includes self by design).
                if (matchedAlly == null) matchedAlly = firstCounted;
            }

            // Latch only after the whole bucket passed (see EvaluateEnemyBucket).
            // Pinned-ally precedence over iterative match mirrors the pre-refactor
            // "first writer wins" order: byName latched before the non-count loop.
            var latch = firstPinned ?? matchedAlly;
            if (latch != null) LastMatchedAlly = latch;
            return true;
        }
    }
}
