# WithinRange Condition + Group-AND Composition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `WithinRange` condition property with fixed distance brackets (Melee/Cone/Short/Medium/Long) for Enemy/Ally subjects, and fix the latent multi-Enemy-condition bug so multiple `Enemy.*` (or `Ally.*`) conditions in one AND-group must match the same unit.

**Architecture:** New `RangeBracket` enum + `RangeBrackets` helper in Models. `ConditionProperty.WithinRange` appended to existing enum. Distance check uses `Vector3.Distance(CurrentOwner.Position, unit.Position)` via the rule-scoped ambient owner. `ConditionEvaluator.EvaluateGroup` is rewritten to bucket conditions by scope (Enemy / Ally / Other) and evaluate each bucket collectively, so same-scope conditions AND over the same unit. UI in `ConditionRowWidget` renders a bracket dropdown for `WithinRange`.

**Tech Stack:** C# on .NET Framework 4.8.1, Harmony 2.x via UMM, Unity UI (TMP), Newtonsoft.Json (bundled). No test framework exists in this repo — verification is via build + smoke test on Steam Deck.

**Spec:** `docs/superpowers/specs/2026-04-20-within-range-condition-design.md`

---

## File Structure

- **Modify:** `WrathTactics/Models/Enums.cs` — append `WithinRange` to `ConditionProperty`, add `RangeBracket` enum + `RangeBrackets` helper.
- **Modify:** `WrathTactics/Engine/ConditionEvaluator.cs` — add `WithinRange` branch to `EvaluateUnitProperty` and `MatchesPropertyThreshold`; rewrite `EvaluateGroup` to bucket-based evaluation; add `IsEnemyScope` / `IsAllyScope` helpers; add `EvaluateEnemyBucket` / `EvaluateAllyBucket`; add `using UnityEngine;`.
- **Modify:** `WrathTactics/UI/ConditionRowWidget.cs` — add `WithinRange` to applicable property lists, render bracket dropdown for non-count and count layouts, restrict operator to `=`/`!=`.
- **Modify:** `WrathTactics/Info.json` and `WrathTactics/WrathTactics.csproj` — version bump to `0.11.0`.
- **Modify:** `CLAUDE.md` (project `wrath-tactics/CLAUDE.md`) — add a gotcha note on group-AND composition + WithinRange value encoding.

---

## Task 1: Append `WithinRange` to `ConditionProperty` and add `RangeBracket`

**Files:**
- Modify: `WrathTactics/Models/Enums.cs`

- [ ] **Step 1: Append `WithinRange` to `ConditionProperty` enum**

Edit `WrathTactics/Models/Enums.cs`. The current enum ends:

```csharp
SpellDCMinusSave,
HasClass
```

Change to add a trailing comma after `HasClass` and append `WithinRange`:

```csharp
SpellDCMinusSave,
HasClass,
WithinRange
```

(Appending at end preserves numeric indices of all prior values — existing preset JSONs continue to load. See CLAUDE.md Gotcha: "Preset JSON uses numeric enum indices".)

- [ ] **Step 2: Add `RangeBracket` enum + `RangeBrackets` helper**

Still in `WrathTactics/Models/Enums.cs`, append at the end of the namespace (after the `TargetType` enum, before the closing `}`):

```csharp
    public enum RangeBracket { Melee, Cone, Short, Medium, Long }

    public static class RangeBrackets {
        public static float MaxMeters(RangeBracket b) {
            switch (b) {
                case RangeBracket.Melee:  return 2f;
                case RangeBracket.Cone:   return 5f;
                case RangeBracket.Short:  return 10f;
                case RangeBracket.Medium: return 20f;
                case RangeBracket.Long:   return 40f;
                default:                  return float.PositiveInfinity;
            }
        }

        public static bool TryParse(string s, out RangeBracket b) {
            return System.Enum.TryParse(s, ignoreCase: true, result: out b);
        }

        public static string Label(RangeBracket b) {
            switch (b) {
                case RangeBracket.Melee:  return "Melee (≤2 m)";
                case RangeBracket.Cone:   return "Cone (≤5 m)";
                case RangeBracket.Short:  return "Short (≤10 m)";
                case RangeBracket.Medium: return "Medium (≤20 m)";
                case RangeBracket.Long:   return "Long (≤40 m)";
                default:                  return b.ToString();
            }
        }
    }
```

- [ ] **Step 3: Build to confirm no compile error**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.` (zero errors; `findstr` warnings are harmless per CLAUDE.md).

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Models/Enums.cs
git commit -m "feat(models): add WithinRange property and RangeBracket enum"
```

---

## Task 2: Add `WithinRange` branches in `ConditionEvaluator`

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

- [ ] **Step 1: Add `using UnityEngine;`**

At the top of `WrathTactics/Engine/ConditionEvaluator.cs`, add `using UnityEngine;` after the existing `using Kingmaker.Enums;` line. The imports should look like:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.Enums;
using UnityEngine;
using WrathTactics.Logging;
using WrathTactics.Models;
using KmAlignment = Kingmaker.Enums.Alignment;
```

- [ ] **Step 2: Add `WithinRange` case to `EvaluateUnitProperty`**

Locate the `switch (condition.Property)` block in `EvaluateUnitProperty` (around line 267). Find the `case ConditionProperty.HasClass:` block (around line 331–333) and insert a new case immediately after it, before `default:`:

```csharp
                case ConditionProperty.WithinRange: {
                    if (CurrentOwner == null) return false;
                    if (!RangeBrackets.TryParse(condition.Value, out var bracket)) {
                        Log.Engine.Warn($"WithinRange: unknown bracket '{condition.Value}' on {unit.CharacterName}");
                        return false;
                    }
                    float dist = Vector3.Distance(CurrentOwner.Position, unit.Position);
                    bool within = dist <= RangeBrackets.MaxMeters(bracket);
                    switch (condition.Operator) {
                        case ConditionOperator.Equal:    return within;
                        case ConditionOperator.NotEqual: return !within;
                        default:                         return false;
                    }
                }
```

- [ ] **Step 3: Add `WithinRange` case to `MatchesPropertyThreshold`**

Locate the `switch (condition.Property)` block in `MatchesPropertyThreshold` (around line 342). Find the `case ConditionProperty.HasClass:` block (around line 407–409) and insert a new case immediately after it, before `default:`:

```csharp
                case ConditionProperty.WithinRange: {
                    if (CurrentOwner == null) return false;
                    if (!RangeBrackets.TryParse(condition.Value, out var bracket)) return false;
                    float dist = Vector3.Distance(CurrentOwner.Position, unit.Position);
                    bool within = dist <= RangeBrackets.MaxMeters(bracket);
                    return condition.Operator == ConditionOperator.NotEqual ? !within : within;
                }
```

- [ ] **Step 4: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine): evaluate WithinRange condition against owner position"
```

---

## Task 3: Rewrite `EvaluateGroup` with same-scope bucketing

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

This task implements the semantic fix. Multiple `Enemy.*` or `Ally.*` conditions in one group must match the same unit. Pick subjects (EnemyLowestHp, etc.) act as both filter and sort hint within their bucket.

- [ ] **Step 1: Add `IsEnemyScope` / `IsAllyScope` helpers**

In `WrathTactics/Engine/ConditionEvaluator.cs`, add two static helpers just above the existing `GetLivingPartyMembers` helper (around line 549):

```csharp
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
            return s == ConditionSubject.Ally || s == ConditionSubject.AllyCount;
        }

        static Func<UnitEntityData, float> PickMetric(ConditionSubject s, out bool biggest) {
            biggest = false;
            switch (s) {
                case ConditionSubject.EnemyBiggestThreat:  biggest = true;  return e => ThreatCalculator.Calculate(e);
                case ConditionSubject.EnemyLowestThreat:   biggest = false; return e => ThreatCalculator.Calculate(e);
                case ConditionSubject.EnemyHighestHp:      biggest = true;  return HpPercent;
                case ConditionSubject.EnemyLowestHp:       biggest = false; return HpPercent;
                case ConditionSubject.EnemyHighestAC:      biggest = true;  return UnitAC;
                case ConditionSubject.EnemyLowestAC:       biggest = false; return UnitAC;
                case ConditionSubject.EnemyHighestFort:    biggest = true;  return UnitFort;
                case ConditionSubject.EnemyLowestFort:     biggest = false; return UnitFort;
                case ConditionSubject.EnemyHighestReflex:  biggest = true;  return UnitReflex;
                case ConditionSubject.EnemyLowestReflex:   biggest = false; return UnitReflex;
                case ConditionSubject.EnemyHighestWill:    biggest = true;  return UnitWill;
                case ConditionSubject.EnemyLowestWill:     biggest = false; return UnitWill;
                case ConditionSubject.EnemyHighestHD:      biggest = true;  return UnitHD;
                case ConditionSubject.EnemyLowestHD:       biggest = false; return UnitHD;
                default:                                   return null;
            }
        }
```

- [ ] **Step 2: Replace `EvaluateGroup` with bucketed version**

Locate the existing `EvaluateGroup` (around line 56–65):

```csharp
        static bool EvaluateGroup(ConditionGroup group, UnitEntityData owner) {
            if (group.Conditions == null || group.Conditions.Count == 0)
                return true;

            foreach (var condition in group.Conditions) {
                if (!EvaluateCondition(condition, owner))
                    return false;
            }
            return true;
        }
```

Replace it with the bucketed version:

```csharp
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
```

- [ ] **Step 3: Add `EvaluateEnemyBucket`**

Insert immediately after the new `EvaluateGroup`:

```csharp
        // Evaluates all Enemy-scope conditions as a single bucket: the bucket is satisfied
        // iff there exists a single enemy that passes every non-Count condition, AND the
        // count of enemies that pass every non-Count condition meets the Count threshold.
        // If a Pick subject is present, its metric sorts the iteration and its property
        // check is still applied (Pick acts as both sort hint and filter).
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
                LastMatchedEnemy = matchedEnemy;
            }

            // Count path: count enemies that pass every non-Count condition AND every Count
            // condition's property-threshold. Threshold = max Value2 across Count conditions.
            if (countConds.Count > 0) {
                float countThreshold = 1f;
                foreach (var cc in countConds) {
                    if (float.TryParse(cc.Value2, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out float v) && v > countThreshold)
                        countThreshold = v;
                }

                int count = 0;
                foreach (var enemy in enemies) {
                    bool allPass = true;
                    foreach (var c in nonCountConds) {
                        if (!EvaluateUnitProperty(c, enemy)) { allPass = false; break; }
                    }
                    if (!allPass) continue;
                    foreach (var cc in countConds) {
                        if (!MatchesPropertyThreshold(cc, enemy)) { allPass = false; break; }
                    }
                    if (allPass) count++;
                }
                if (count < countThreshold) return false;
            }

            return true;
        }
```

- [ ] **Step 4: Add `EvaluateAllyBucket`**

Insert immediately after `EvaluateEnemyBucket`:

```csharp
        // Ally analogue of EvaluateEnemyBucket — no Pick subjects exist for Ally scope,
        // so the logic is simpler: a matching Ally (for the non-Count path) and/or a
        // satisfied Count threshold.
        static bool EvaluateAllyBucket(List<Condition> conds, UnitEntityData owner) {
            var allies = GetAllPartyMembers(owner).Where(a => a != owner).ToList();
            if (allies.Count == 0 && conds.All(c => c.Subject != ConditionSubject.AllyCount))
                return false;

            var nonCountConds = conds.Where(c => c.Subject != ConditionSubject.AllyCount).ToList();
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
                LastMatchedAlly = matchedAlly;
            }

            if (countConds.Count > 0) {
                float countThreshold = 1f;
                foreach (var cc in countConds) {
                    if (float.TryParse(cc.Value2, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out float v) && v > countThreshold)
                        countThreshold = v;
                }

                int count = 0;
                // AllyCount historically includes self; keep that behavior (use GetAllPartyMembers
                // without filtering owner) for Count, to match the previous EvaluateAllyCount.
                foreach (var ally in GetAllPartyMembers(owner)) {
                    bool allPass = true;
                    foreach (var c in nonCountConds) {
                        if (!EvaluateUnitProperty(c, ally)) { allPass = false; break; }
                    }
                    if (!allPass) continue;
                    foreach (var cc in countConds) {
                        if (!MatchesPropertyThreshold(cc, ally)) { allPass = false; break; }
                    }
                    if (allPass) count++;
                }
                if (count < countThreshold) return false;
            }

            return true;
        }
```

- [ ] **Step 5: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

If the build fails with "`EvaluateCondition` reachable only via `otherConds`" or similar, re-check that the existing case-dispatch in `EvaluateCondition` (line ~67) still contains the Enemy/Ally/Pick branches — they must remain callable so Section 2 of the old code path still exists for anything that reaches it. We leave it in place as a fallback (never called for scope-bucketed conditions in the new flow).

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "fix(engine): same-scope Enemy/Ally conditions AND on same unit

Group evaluation was iterating each condition independently, so two
Enemy.* rows could pass on different enemies. New bucketed flow:
- Enemy/Ally conditions evaluated together, must match one unit.
- EnemyCount / AllyCount counts units passing the joined predicate.
- Pick subjects (EnemyLowestHp etc) act as sort + filter inside the
  bucket.

Existing single-Enemy-condition rules are behaviorally identical."
```

---

## Task 4: UI — render bracket dropdown for `WithinRange`

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs`

- [ ] **Step 1: Add `WithinRange` to `GetValueOptionsForProperty`**

Locate `GetValueOptionsForProperty` (around line 287). Add a new case before `default:`:

```csharp
                case ConditionProperty.WithinRange:
                    return new List<string> {
                        RangeBrackets.Label(RangeBracket.Melee),
                        RangeBrackets.Label(RangeBracket.Cone),
                        RangeBrackets.Label(RangeBracket.Short),
                        RangeBrackets.Label(RangeBracket.Medium),
                        RangeBrackets.Label(RangeBracket.Long),
                    };
```

These labels are display-only — the persisted `Condition.Value` uses the bare enum name (`"Melee"`, `"Cone"`, …). The two lists are mapped index-to-index below.

- [ ] **Step 2: Add `WithinRange` to applicable property lists in `GetPropertiesForSubject`**

Locate `GetPropertiesForSubject` (around line 351). Extend four subjects:

For `ConditionSubject.Ally` (around line 361–367):

```csharp
                case ConditionSubject.Ally:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange
                    };
```

For `ConditionSubject.AllyCount` (around line 368–374):

```csharp
                case ConditionSubject.AllyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange
                    };
```

For the big Enemy/EnemyPick block (around line 375–399, the list starting with `ConditionSubject.Enemy:` through `EnemyLowestHD:`):

```csharp
                case ConditionSubject.Enemy:
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
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
                        ConditionProperty.HasBuff, ConditionProperty.HasCondition,
                        ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange
                    };
```

For `ConditionSubject.EnemyCount` (around line 400–408):

```csharp
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange
                    };
```

- [ ] **Step 3: Render bracket dropdown in the non-count layout**

Locate the non-count branch (starts around line 176 with `} else {`). The branch uses `usesEqOp` to group properties that only need `=`/`!=` operators. Extend it to include `isWithinRange`:

Find this block (around line 177–183):

```csharp
                bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
                bool isAlignment = condition.Property == ConditionProperty.Alignment;
                bool isBuffProp = condition.Property == ConditionProperty.HasBuff;
                bool isInCombat = condition.Property == ConditionProperty.IsInCombat;
                bool isHasClass = condition.Property == ConditionProperty.HasClass;
                bool usesEqOp = isHasCondition || isCreatureType || isBuffProp || isAlignment || isHasClass;
                bool needsOperator = !usesEqOp && !isInCombat;
```

Change to add `isWithinRange`:

```csharp
                bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
                bool isAlignment = condition.Property == ConditionProperty.Alignment;
                bool isBuffProp = condition.Property == ConditionProperty.HasBuff;
                bool isInCombat = condition.Property == ConditionProperty.IsInCombat;
                bool isHasClass = condition.Property == ConditionProperty.HasClass;
                bool isWithinRange = condition.Property == ConditionProperty.WithinRange;
                bool usesEqOp = isHasCondition || isCreatureType || isBuffProp || isAlignment || isHasClass || isWithinRange;
                bool needsOperator = !usesEqOp && !isInCombat;
```

Now add the bracket-picker branch. Find the block ending with the `} else if (condition.Property == ConditionProperty.IsInCombat) {` block (around line 248). Add a new `else if` for WithinRange **before** `isInCombat`, so the chain stays in the same style. Concretely, after the `else if (condition.Property == ConditionProperty.HasBuff)` block and before `else if (condition.Property == ConditionProperty.IsInCombat)`:

```csharp
                } else if (isWithinRange) {
                    var bracketNames = new List<string> {
                        nameof(RangeBracket.Melee),
                        nameof(RangeBracket.Cone),
                        nameof(RangeBracket.Short),
                        nameof(RangeBracket.Medium),
                        nameof(RangeBracket.Long)
                    };
                    var bracketLabels = GetValueOptionsForProperty(ConditionProperty.WithinRange);
                    int brIdx = bracketNames.IndexOf(condition.Value);
                    if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; } // default: Short
                    PopupSelector.Create(root, "RangeBracketValue", 0.45f, 0.88f, bracketLabels, brIdx, v => {
                        condition.Value = bracketNames[v];
                        ConfigManager.Save();
                    });
```

The `bracketNames` list holds persisted values (`"Melee"` etc.); `bracketLabels` holds display labels with distance hints — index-aligned.

- [ ] **Step 4: Render bracket dropdown in the count layout**

Locate the count branch (around line 70–175). Inside the `if (propNeedsOperator) { ... } else if (condition.Property == ConditionProperty.CreatureType || ... ) { ... }` chain, the `else if` block around lines 127–165 lists properties that use the Eq operator + dropdown value. Extend this condition:

Find this block (around line 127–131):

```csharp
                } else if (condition.Property == ConditionProperty.CreatureType
                    || condition.Property == ConditionProperty.Alignment
                    || condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.HasCondition
                    || condition.Property == ConditionProperty.HasClass) {
```

Change to include WithinRange:

```csharp
                } else if (condition.Property == ConditionProperty.CreatureType
                    || condition.Property == ConditionProperty.Alignment
                    || condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.HasCondition
                    || condition.Property == ConditionProperty.HasClass
                    || condition.Property == ConditionProperty.WithinRange) {
```

Then, inside that branch, find the existing `else` (the final one, around lines 157–164) that creates the generic value dropdown via `GetValueOptionsForProperty`. This already handles WithinRange automatically via the case added in Step 1 — but we need to map display-label index → persisted enum-name. Modify the final `else` block to special-case WithinRange:

Find this block (around lines 157–165):

```csharp
                    } else {
                        var valueOptions = GetValueOptionsForProperty(condition.Property);
                        int valIdx = valueOptions.IndexOf(condition.Value);
                        if (valIdx < 0) { valIdx = 0; condition.Value = valueOptions[0]; }
                        PopupSelector.Create(root, "CountValueDropdown", 0.65f, 0.88f, valueOptions, valIdx, v => {
                            condition.Value = valueOptions[v];
                            ConfigManager.Save();
                        });
                    }
```

Replace with:

```csharp
                    } else if (condition.Property == ConditionProperty.WithinRange) {
                        var bracketNames = new List<string> {
                            nameof(RangeBracket.Melee),
                            nameof(RangeBracket.Cone),
                            nameof(RangeBracket.Short),
                            nameof(RangeBracket.Medium),
                            nameof(RangeBracket.Long)
                        };
                        var bracketLabels = GetValueOptionsForProperty(ConditionProperty.WithinRange);
                        int brIdx = bracketNames.IndexOf(condition.Value);
                        if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; }
                        PopupSelector.Create(root, "CountRangeBracketValue", 0.65f, 0.88f, bracketLabels, brIdx, v => {
                            condition.Value = bracketNames[v];
                            ConfigManager.Save();
                        });
                    } else {
                        var valueOptions = GetValueOptionsForProperty(condition.Property);
                        int valIdx = valueOptions.IndexOf(condition.Value);
                        if (valIdx < 0) { valIdx = 0; condition.Value = valueOptions[0]; }
                        PopupSelector.Create(root, "CountValueDropdown", 0.65f, 0.88f, valueOptions, valIdx, v => {
                            condition.Value = valueOptions[v];
                            ConfigManager.Save();
                        });
                    }
```

- [ ] **Step 5: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): bracket dropdown for WithinRange condition"
```

---

## Task 5: Version bump + CLAUDE.md gotcha note

**Files:**
- Modify: `WrathTactics/Info.json`
- Modify: `WrathTactics/WrathTactics.csproj`
- Modify: `CLAUDE.md` (project root `wrath-tactics/CLAUDE.md`)

- [ ] **Step 1: Bump `Info.json`**

Edit `WrathTactics/Info.json`. Current version is `"Version": "0.10.0"`. Change to:

```json
"Version": "0.11.0",
```

- [ ] **Step 2: Bump `WrathTactics.csproj`**

Edit `WrathTactics/WrathTactics.csproj`. Find the `<Version>` element and change it to `0.11.0`:

```xml
<Version>0.11.0</Version>
```

If your csproj uses a different element name (e.g., `<VersionPrefix>`), apply the change there — match whatever `0.10.0` currently lives in.

- [ ] **Step 3: Add CLAUDE.md gotcha entries**

Edit `CLAUDE.md` (the wrath-tactics project-level one, `/home/pascal/Code/wrath-mods/wrath-tactics/CLAUDE.md`). In the `## Gotchas` section, append two new bullet entries at the end of the list (before the next `##` section):

```markdown
- **Group-AND is same-unit for Enemy/Ally scopes** (since 0.11.0): Multiple `Enemy.*` conditions in one ConditionGroup must all match the *same* enemy. Same for `Ally.*`. Implementation lives in `ConditionEvaluator.EvaluateEnemyBucket` / `EvaluateAllyBucket`. Conditions of different scopes still AND-compose across scopes (Enemy AND Ally AND Self ...). To express "different enemies" semantics, split into separate OR-groups. Prior to 0.11.0 each condition iterated independently, so two Enemy rows could pass on unrelated enemies — this was the latent bug fixed alongside WithinRange.
- **WithinRange value encoding** (since 0.11.0): `Condition.Value` for `ConditionProperty.WithinRange` is the plain `RangeBracket` enum name (`"Melee"`, `"Cone"`, `"Short"`, `"Medium"`, `"Long"`). Fixed meter thresholds live in `RangeBrackets.MaxMeters` (2/5/10/20/40 m). The UI dropdown shows distance-hint labels (`"Cone (≤5 m)"`) but persists the bare name. Operator is restricted to `=`/`!=` — other operators evaluate to false.
```

- [ ] **Step 4: Build to confirm version strings don't break anything**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj CLAUDE.md
git commit -m "chore: bump to 0.11.0, document WithinRange + group-AND gotchas"
```

---

## Task 6: Deploy + smoke-test on Steam Deck

**Files:**
- None modified — deployment only.

- [ ] **Step 1: Run deploy**

Run: `./deploy.sh`
Expected: Build completes (Release config produces `bin/WrathTactics-0.11.0.zip`), then SCP copies DLL + Info.json to the Deck.

- [ ] **Step 2: Start Wrath on Deck, load a save with at least one caster companion**

Launch the game via Steam on the Deck (via Moonlight from laptop if streaming). Wait for main menu → load a save where at least one party member can cast Burning Hands or Fireball, and that has a WrathTactics rule list visible via `Ctrl+T`.

- [ ] **Step 3: Smoke test 1 — Cone trigger**

On the caster, create a new rule:
- Condition group 1 with one row:
  - Subject: `EnemyCount`
  - Property: `WithinRange`
  - `count >= 3`
  - Operator: `=`
  - Value: `Cone (≤5 m)`
- Action: `CastSpell` → `BurningHands` (or a learned cone spell)
- Target: `PointAtSelf`

Save, enter combat, pull 3+ mobs into adjacent/cone range. Confirm the spell fires.
Pull only 2 mobs into cone range. Confirm the spell does NOT fire.

- [ ] **Step 4: Smoke test 2 — Spellcaster + range composition**

On an Arcanist (or any Fireball-capable caster), create a new rule:
- Condition group 1 with two rows:
  - Row A: Subject `Enemy`, Property `HasClass`, `=`, Value `[Group] Spellcaster`
  - Row B: Subject `Enemy`, Property `WithinRange`, `=`, Value `Long (≤40 m)`
- Action: `CastSpell` → `Fireball`
- Target: `PointAtConditionTarget`

Encounter A (enemy caster far, melee close): confirm the rule does NOT fire (melee passes range, caster passes HasClass — but no single enemy passes both).
Encounter B (enemy caster within Long range): confirm Fireball fires at the caster's position.

- [ ] **Step 5: Smoke test 3 — Regression (single-condition rule)**

Confirm a pre-existing rule with one condition (e.g., `Enemy.HpPercent < 30`) still fires as before. No regression expected.

- [ ] **Step 6: Smoke test 4 — Ally-count composition**

On a Cleric, create a rule:
- Condition group with two rows:
  - Row A: Subject `AllyCount`, Property `HpPercent`, `<`, Value `50`, count `>= 3`
  - Row B: Subject `AllyCount`, Property `WithinRange`, `=`, Value `Short (≤10 m)`, count `>= 3`
- Action: `CastSpell` → Channel Positive Energy
- Target: `Self`

Bunch up the party and damage them. Confirm the channel fires when ≥3 allies are both wounded AND within 10 m.

- [ ] **Step 7: Smoke test 5 — Not-within**

On any caster:
- Row: Subject `Enemy`, Property `WithinRange`, `!=`, Value `Melee (≤2 m)`
- Action: `CastSpell` → `MagicMissile`
- Target: `EnemyLowestHp`

Confirm the caster does NOT fire magic missiles when an enemy is adjacent, and does fire when enemies are all at least a few steps away.

- [ ] **Step 8: Check session log for errors**

On laptop:

```bash
ssh deck-direct "ls -t '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/' | head -1"
```

Then `cat` the latest log via SSH and scan for `ERROR` or repeated `WARN` lines (especially `WithinRange: unknown bracket` — should appear zero times in a healthy run).

- [ ] **Step 9: If smoke test fails, triage and return**

If any smoke test fails, stop here. Investigate via the mod log + Unity player log (see CLAUDE.md "Logs" section). Do **not** proceed to release until the failure is understood and either the plan is amended or the regression fixed.

---

## Task 7: Release

**Files:**
- None modified — git tag + manual GitHub release.

- [ ] **Step 1: Tag and push**

```bash
git tag -a v0.11.0 -m "v0.11.0 — WithinRange condition + group-AND fix"
git push origin master
git push origin v0.11.0
```

- [ ] **Step 2: Create GitHub release with the built zip**

The Release build from Task 5 / Task 6 produces `WrathTactics/bin/WrathTactics-0.11.0.zip`. If the zip is not current (e.g., only Debug builds happened), rebuild:

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/
```

Then:

```bash
gh release create v0.11.0 \
  --title "v0.11.0 — WithinRange condition + group-AND composition fix" \
  --notes "$(cat <<'EOF'
## New

- **WithinRange condition property** for Enemy/Ally subjects. Fixed distance brackets:
  - **Melee** — ≤2 m (adjacent / AoO range)
  - **Cone** — ≤5 m (Burning Hands, Fire Breath)
  - **Short** — ≤10 m (Fireball-radius territory)
  - **Medium** — ≤20 m (mid-range spells)
  - **Long** — ≤40 m (vision-bounded shots)

## Bug fixes

- **Preset condition edits no longer silently revert on reload.** `ConditionRowWidget` was always writing to the character-rules config file, even when the user was editing a preset. Preset JSON never got the edit, so the next reload restored the preset's first-saved state. The widget now routes saves through the parent's callback, which picks the correct target file.
- **`SpellDCMinusSave` now works for Magic Deceiver fused spells.** The mod was reading the save type from the static blueprint component only, missing the `AbilityData.MagicHackData.SavingThrowType` override the game uses for hack-altered casts. Fused-spell rules returned NaN and silently fell through to melee. Fixed to mirror the game's own save-type resolution. If the issue persists for any specific spell, the new `Trace` log line in the session log (`SpellDCMinusSave: '<name>' …`) records exactly which lookup path was used and what DC/save/margin resulted — paste it into a bug report.

## Behavior changes

- **Same-unit AND for Enemy/Ally conditions in one group.** Rules with multiple `Enemy.*` conditions (or multiple `Ally.*` conditions) in the same AND-group now require all of those conditions to match the *same* unit. Previously each condition iterated independently, so two Enemy rows could pass on different enemies. Single-condition rules are unaffected. If you want "different enemies" semantics, split into separate OR-groups.
- **Pick-subject fall-through.** Pick subjects (`EnemyLowestHp`, `EnemyBiggestThreat`, `EnemyHighestAC`, …) now sort-then-filter: if the top-ranked unit fails the condition's property check, the engine tries the next-ranked unit, and so on. Previously only the single metric-best unit was checked. Single-condition Pick rules that always pass are unaffected. Rules of the form `EnemyLowestHp.SomeProperty` may now fire on a different enemy than before if the weakest one fails the property check.

## Example

"Enemy spellcaster in Long range → Fireball":

- Subject `Enemy`, Property `HasClass`, `=`, `[Group] Spellcaster`
- Subject `Enemy`, Property `WithinRange`, `=`, `Long (≤40 m)`
- Action: `CastSpell` → `Fireball`, Target: `PointAtConditionTarget`
EOF
)" \
  WrathTactics/bin/WrathTactics-0.11.0.zip
```

- [ ] **Step 3: Verify release appears on GitHub**

```bash
gh release view v0.11.0
```

Expected: release page with the zip attached, notes rendered as above.

---

## Self-Review Notes

- **Spec coverage.** WithinRange property (Tasks 1+2+4), group-AND composition fix (Task 3), UI bracket dropdown (Task 4), persistence (nothing new needed — string encoding handles it), smoke tests (Task 6), release (Task 7). Out-of-scope items in spec (target-side range filter, ActionRange bracket, edge-to-edge distance, Pick-within-filter) are intentionally omitted.
- **Type consistency.** `RangeBracket` / `RangeBrackets` naming consistent across all tasks. `nameof(RangeBracket.Melee)` → `"Melee"` consistent in both UI branches. Persisted value = bare enum name; display label = `RangeBrackets.Label(...)` with distance hint.
- **Indices.** `WithinRange` appended at end of `ConditionProperty` — no existing index shifted.
- **CurrentOwner.** Already set by `Evaluate(rule, owner)` in the existing try/finally — WithinRange reads it the same way `SpellDCMinusSave` does. No new plumbing.
- **Old code paths.** The old `EvaluateEnemy`, `EvaluateEnemyCount`, `EvaluateAlly`, `EvaluateAllyCount`, `EvaluateEnemyPick` methods remain in the file but are no longer called from the new `EvaluateGroup` path — they're reachable only via `EvaluateCondition`, which the new flow does not invoke for Enemy/Ally scopes. They could be deleted later, but the plan leaves them in place to minimize diff. Flagging here so the reviewer isn't surprised.
