# Condition Negate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-condition `Negate` flag, remove `MissingBuff` and `HasDebuff`, and self-heal configs that still reference the removed enum values.

**Architecture:** Single `bool Negate` on `Condition`, applied as a single-exit wrap in `ConditionEvaluator.EvaluateCondition`. UI exposes a compact `nicht` toggle button between Subject and Property on every row. `MissingBuff` and `HasDebuff` enum values plus all their evaluator/UI branches are deleted outright (alpha hard-break). `ConfigManager.Load` gets a `SafeConditionConverter` that skips un-parseable conditions, filters empty groups/rules, and immediately re-saves the cleaned file so the next load is silent.

**Tech Stack:** C# / .NET 4.8.1 / Unity UGUI / TextMeshPro / Newtonsoft.Json / Kingmaker game API

**Build command:** `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

**Deploy command:** `./deploy.sh`

**Spec:** `docs/superpowers/specs/2026-04-18-condition-negate-design.md`

**Testing model:** No unit-test harness exists in this repo. Every phase ends with a build, and the final phase is a manual Steam Deck smoke test.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `WrathTactics/Models/TacticsRule.cs` | Modify | Add `Negate` bool to `Condition` |
| `WrathTactics/Models/Enums.cs` | Modify | Delete `MissingBuff` and `HasDebuff` from `ConditionProperty` |
| `WrathTactics/Engine/ConditionEvaluator.cs` | Modify | Wrap `EvaluateCondition` exit with `Negate`; delete `MissingBuff`/`HasDebuff` cases in both `EvaluateUnitProperty` and `MatchesPropertyThreshold` |
| `WrathTactics/UI/ConditionRowWidget.cs` | Modify | Delete `isHasDebuff` local + both debuff branches + `MissingBuff` dispatch branches; narrow Subject/Property anchors; add NOT button between Subject and Property in both count and non-count layouts; drop `MissingBuff`/`HasDebuff` from `GetPropertiesForSubject` |
| `WrathTactics/Persistence/SafeConditionConverter.cs` | Create | Custom `JsonConverter<Condition>` that returns `null` on unknown-enum failures |
| `WrathTactics/Persistence/ConfigManager.cs` | Modify | Register converter; post-load filter of `null` conditions / empty groups / empty rules; re-save on cleanup |
| `WrathTactics/Info.json` | Modify | Version bump |
| `WrathTactics/WrathTactics.csproj` | Modify | Version bump |

---

## Phase 1: Data model — add `Negate`

### Task 1: Add `Negate` property to `Condition`

**Files:**
- Modify: `WrathTactics/Models/TacticsRule.cs:22-28`

- [ ] **Step 1: Add `Negate` property**

In `WrathTactics/Models/TacticsRule.cs`, add `Negate` to the `Condition` class:

```csharp
    public class Condition {
        [JsonProperty] public ConditionSubject Subject { get; set; }
        [JsonProperty] public ConditionProperty Property { get; set; }
        [JsonProperty] public ConditionOperator Operator { get; set; }
        [JsonProperty] public string Value { get; set; } = "";
        [JsonProperty] public string Value2 { get; set; } = "";  // For AllyCount/EnemyCount: the count threshold
        [JsonProperty] public bool Negate { get; set; } = false;
    }
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)` (warnings about `findstr` are fine).

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/TacticsRule.cs
git commit -m "feat(model): add Negate flag to Condition"
```

---

## Phase 2: Evaluator — wrap with negation + delete dead cases

### Task 2: Wrap `EvaluateCondition` exit with `Negate`

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs:53-80`

- [ ] **Step 1: Refactor `EvaluateCondition` to apply `Negate` at a single exit point**

Replace the existing body of `EvaluateCondition` with a wrapper around a renamed inner method. In `WrathTactics/Engine/ConditionEvaluator.cs`, change lines 53–80:

```csharp
        static bool EvaluateCondition(Condition condition, UnitEntityData owner) {
            bool result = EvaluateConditionCore(condition, owner);
            return condition.Negate ? !result : result;
        }

        static bool EvaluateConditionCore(Condition condition, UnitEntityData owner) {
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
                    case ConditionSubject.Combat:              return EvaluateCombat(condition);
                    default:                                   return false;
                }
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Failed to evaluate {condition.Subject}.{condition.Property}");
                return false;
            }
        }
```

Rationale: a single wrap guarantees `Negate` is honored for every subject, including count-based subjects (inverts the full count predicate) and the fallback `default` path.

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine): apply Negate at single EvaluateCondition exit"
```

---

### Task 3: Delete `MissingBuff` and `HasDebuff` from `ConditionProperty`

**Files:**
- Modify: `WrathTactics/Models/Enums.cs:23-41`

- [ ] **Step 1: Remove the two enum members**

In `WrathTactics/Models/Enums.cs`, delete lines `MissingBuff,` and `HasDebuff,` so the enum becomes:

```csharp
    public enum ConditionProperty {
        HpPercent,
        AC,
        HasBuff,
        HasCondition,
        SpellSlotsAtLevel,
        SpellSlotsAboveLevel,
        Resource,
        CreatureType,
        CombatRounds,
        IsDead,
        SaveFortitude,
        SaveReflex,
        SaveWill,
        Alignment,
        IsInCombat
    }
```

- [ ] **Step 2: Build to see the expected breakages**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: Build **fails** with errors referencing `ConditionProperty.MissingBuff` and `ConditionProperty.HasDebuff` in `ConditionEvaluator.cs` and `ConditionRowWidget.cs`. Do not commit yet — Tasks 4 and 5 will fix these.

---

### Task 4: Remove `MissingBuff` and `HasDebuff` cases from evaluator

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs` (both `EvaluateUnitProperty` and `MatchesPropertyThreshold`)

- [ ] **Step 1: Delete `MissingBuff` and `HasDebuff` cases from `EvaluateUnitProperty`**

In `WrathTactics/Engine/ConditionEvaluator.cs`, delete lines 254–256 (`case ConditionProperty.MissingBuff:` and its 3-line body) and lines 261–266 (`case ConditionProperty.HasDebuff:` and its 5-line body). The resulting `switch` block around the buff/condition cluster should read:

```csharp
                case ConditionProperty.HasBuff:
                    return unit.Buffs.RawFacts.Any(b =>
                        b.Blueprint.AssetGuid.ToString() == condition.Value);

                case ConditionProperty.HasCondition:
                    return HasConditionByName(unit, condition.Value);

                case ConditionProperty.SpellSlotsAtLevel:
                    int level = (int)threshold;
                    return CountAvailableSlotsAtLevel(unit, level) > 0;
```

- [ ] **Step 2: Delete `MissingBuff` and `HasDebuff` cases from `MatchesPropertyThreshold`**

Still in `ConditionEvaluator.cs`, delete lines 327–332 (`case ConditionProperty.HasDebuff:` and its 5-line body) and lines 339–342 (`case ConditionProperty.MissingBuff:` and its 3-line body). The resulting `switch` block should read:

```csharp
                case ConditionProperty.HasCondition:
                    return HasConditionByName(unit, condition.Value);

                case ConditionProperty.HasBuff:
                    return !string.IsNullOrEmpty(condition.Value) && unit.Buffs.RawFacts.Any(b =>
                        b.Blueprint.AssetGuid.ToString() == condition.Value ||
                        (b.Blueprint.name?.Contains(condition.Value) ?? false));

                case ConditionProperty.CreatureType:
```

- [ ] **Step 3: Build — ConditionEvaluator should now be clean; UI still errors**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: Build still fails, but only with errors in `ConditionRowWidget.cs`. Do not commit yet.

---

### Task 5: Remove `MissingBuff` and `HasDebuff` from `ConditionRowWidget`

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs` (deletions across multiple regions)

- [ ] **Step 1: Drop the `isHasDebuff` local and the count-subject debuff/missing-buff dispatch**

In `WrathTactics/UI/ConditionRowWidget.cs`:

a) Delete line 69 (`bool isHasDebuff = condition.Property == ConditionProperty.HasDebuff;`).

b) Delete lines 167–188 (the `else if (condition.Property == ConditionProperty.HasDebuff)` branch inside the count-subject block).

c) Change the next branch (lines 189–192) from:

```csharp
                } else if (condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.MissingBuff) {
                    condition.Operator = ConditionOperator.Equal;
                    CreateBuffSelector(root, 0.58f, 0.88f);
```

to:

```csharp
                } else if (condition.Property == ConditionProperty.HasBuff) {
                    condition.Operator = ConditionOperator.Equal;
                    CreateBuffSelector(root, 0.58f, 0.88f);
```

- [ ] **Step 2: Drop `MissingBuff`/`HasDebuff` from the non-count dispatch**

Still in `ConditionRowWidget.cs`:

a) In the `isBuffProp` declaration (lines 206–207), remove the `MissingBuff` part:

```csharp
                bool isBuffProp = condition.Property == ConditionProperty.HasBuff;
```

b) In the `needsOperator` line (line 209), remove the now-undefined `isHasDebuff` reference:

```csharp
                bool needsOperator = !isHasCondition && !isCreatureType && !isBuffProp && !isAlignment && !isInCombat;
```

c) Delete the `isHasDebuff` value-renderer block (lines 266–306, the `else if (isHasDebuff)` branch with its `debuffNames` / `displayNames` list).

d) Change the `HasBuff || MissingBuff` branch (lines 307–309) from:

```csharp
                } else if (condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.MissingBuff) {
                    CreateBuffSelector(root, 0.38f, 0.88f);
```

to:

```csharp
                } else if (condition.Property == ConditionProperty.HasBuff) {
                    CreateBuffSelector(root, 0.38f, 0.88f);
```

- [ ] **Step 3: Drop `MissingBuff`/`HasDebuff` from `GetPropertiesForSubject`**

Still in `ConditionRowWidget.cs`, update the `GetPropertiesForSubject` method so each case's returned list no longer contains `MissingBuff` or `HasDebuff`:

```csharp
                case ConditionSubject.Self:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition,
                        ConditionProperty.SpellSlotsAtLevel, ConditionProperty.SpellSlotsAboveLevel,
                        ConditionProperty.Alignment
                    };
                case ConditionSubject.Ally:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment
                    };
                case ConditionSubject.AllyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment
                    };
```

And for the enemy-facing cases (around lines 416–428):

```csharp
                case ConditionSubject.Enemy:
                /* … EnemyBiggestThreat / EnemyLowestThreat / EnemyHighestHp / EnemyLowestHp /
                     EnemyLowestAC / EnemyHighestAC / EnemyLowestFort / EnemyHighestFort /
                     EnemyLowestReflex / EnemyHighestReflex / EnemyLowestWill / EnemyHighestWill
                     — all collapse to the same block: */
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
                        ConditionProperty.HasBuff, ConditionProperty.HasCondition,
                        ConditionProperty.CreatureType,
                        ConditionProperty.Alignment
                    };
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment
                    };
```

If the existing code has each Enemy-pick subject as its own `case` with its own returned list, delete `MissingBuff` and `HasDebuff` entries from each list. Do not consolidate the cases — preserve the existing structure; only drop the two tokens from each list.

- [ ] **Step 4: Build the full project**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Models/Enums.cs WrathTactics/Engine/ConditionEvaluator.cs WrathTactics/UI/ConditionRowWidget.cs
git commit -m "refactor: remove MissingBuff and HasDebuff ConditionProperty"
```

---

## Phase 3: UI — NOT toggle button

### Task 6: Add `NOT` toggle button and shift anchors in the non-count layout

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs:40-65` (Subject/Property anchor changes) and the spot just after the Property selector

- [ ] **Step 1: Narrow the Subject selector to `0.00-0.13`**

In `WrathTactics/UI/ConditionRowWidget.cs`, change the `PopupSelector.Create` call for Subject (around line 42) from:

```csharp
            PopupSelector.Create(root, "Subject", 0f, 0.15f, subjectNames,
```

to:

```csharp
            PopupSelector.Create(root, "Subject", 0f, 0.13f, subjectNames,
```

- [ ] **Step 2: Shift the initial Property anchors to `0.19-0.38`**

Change the Property `PopupSelector.Create` call (around line 58) from:

```csharp
            propertySelector = PopupSelector.Create(root, "Property", 0.16f, 0.37f,
```

to:

```csharp
            propertySelector = PopupSelector.Create(root, "Property", 0.19f, 0.38f,
```

Note: for count-subjects the property selector is immediately repositioned to `(0.38, 0.58)` by the existing reposition block (line 103). That reposition is still correct after Task 7; leave it alone.

- [ ] **Step 3: Insert the NOT button between Subject and Property**

Immediately **after** the Property selector is created (after the closing `});` around line 64 and **before** the `bool isCountSubject = ...` line), add:

```csharp
            // NOT toggle — inverts the final result of this condition
            var notColor = condition.Negate
                ? new Color(0.55f, 0.15f, 0.15f, 1f)
                : new Color(0.20f, 0.20f, 0.20f, 1f);
            var notLabel = condition.Negate ? "nicht" : "";
            var notBtn = UIHelpers.MakeButton(root.transform, "NegateBtn", notLabel, 13f, notColor, () => {
                condition.Negate = !condition.Negate;
                ConfigManager.Save();
                Rebuild();
            });
            var notRect = notBtn.GetComponent<RectTransform>();
            notRect.SetAnchor(0.14, 0.18, 0, 1);
            notRect.sizeDelta = Vector2.zero;
```

Rationale: the toggle lives in both count and non-count layouts. For the count layout Task 7 repositions it to a later anchor range.

- [ ] **Step 4: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): add NOT toggle on condition rows (non-count layout)"
```

---

### Task 7: Reposition NOT button for count-subject layout

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs` — inside the `if (isCountSubject) { ... }` block, right after the existing "with" label creation, before the Property re-anchor block.

- [ ] **Step 1: Reposition the NOT button inside the count-subject branch**

In `ConditionRowWidget.cs`, inside the `if (isCountSubject)` block, immediately **after** the "with" label is created (line 97) and **before** the `if (propertySelector != null)` property reposition (line 101), add:

```csharp
                // Reposition NOT toggle for count layout: between "with" and Property
                var notBtnTransform = root.transform.Find("NegateBtn");
                if (notBtnTransform != null) {
                    var notBtnCountRect = notBtnTransform.GetComponent<RectTransform>();
                    notBtnCountRect.SetAnchor(0.38, 0.41, 0, 1);
                    notBtnCountRect.sizeDelta = Vector2.zero;
                }
```

Then change the Property reposition block (line 103) from:

```csharp
                    if (psRect != null) psRect.SetAnchor(0.38, 0.58, 0, 1);
```

to:

```csharp
                    if (psRect != null) psRect.SetAnchor(0.42, 0.58, 0, 1);
```

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): reposition NOT toggle for AllyCount/EnemyCount layout"
```

---

## Phase 4: Self-healing ConfigManager

### Task 8: Create `SafeConditionConverter`

**Files:**
- Create: `WrathTactics/Persistence/SafeConditionConverter.cs`

- [ ] **Step 1: Create the converter file**

Write to `WrathTactics/Persistence/SafeConditionConverter.cs`:

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Persistence {
    /// <summary>
    /// Deserializes a Condition but returns null when the JSON references
    /// an enum value that no longer exists in the current code (e.g. an
    /// old MissingBuff / HasDebuff entry). Any other deserialization error
    /// is re-thrown so structural corruption is caught at the outer level.
    /// </summary>
    public class SafeConditionConverter : JsonConverter<Condition> {
        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, Condition value, JsonSerializer serializer) {
            throw new NotImplementedException("CanWrite is false");
        }

        public override Condition ReadJson(JsonReader reader, Type objectType,
            Condition existingValue, bool hasExistingValue, JsonSerializer serializer) {
            JObject obj;
            try {
                obj = JObject.Load(reader);
            } catch (Exception ex) {
                Log.Persistence.Warn($"Skipping condition — could not parse JSON object: {ex.Message}");
                return null;
            }

            var condition = new Condition();
            try {
                // Populate via a nested serializer pass without this converter, so
                // enum-parse failures here throw instead of recursing.
                var inner = new JsonSerializer();
                foreach (var c in serializer.Converters) {
                    if (!(c is SafeConditionConverter)) inner.Converters.Add(c);
                }
                using (var sub = obj.CreateReader()) {
                    inner.Populate(sub, condition);
                }
                return condition;
            } catch (JsonSerializationException ex) {
                Log.Persistence.Warn($"Dropping condition due to unknown enum value. Raw JSON: {obj.ToString(Formatting.None)} — {ex.Message}");
                return null;
            }
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Persistence/SafeConditionConverter.cs
git commit -m "feat(persistence): add SafeConditionConverter for resilient loads"
```

---

### Task 9: Wire the converter into `ConfigManager.Load` and filter empty rules

**Files:**
- Modify: `WrathTactics/Persistence/ConfigManager.cs:26-41`

- [ ] **Step 1: Replace `Load` body with converter + post-load filter**

In `WrathTactics/Persistence/ConfigManager.cs`, replace the existing `Load` method body with:

```csharp
        public static void Load() {
            var path = GetConfigPath();
            if (path == null || !File.Exists(path)) {
                current = new TacticsConfig();
                Log.Persistence.Info("No existing config, using defaults");
                return;
            }

            try {
                var json = File.ReadAllText(path);
                var settings = new JsonSerializerSettings();
                settings.Converters.Add(new SafeConditionConverter());
                current = JsonConvert.DeserializeObject<TacticsConfig>(json, settings) ?? new TacticsConfig();
                Log.Persistence.Info($"Loaded config from {path}");

                if (CleanupInvalidRules(current)) {
                    Log.Persistence.Warn("Some rules referenced removed condition properties — saving cleaned config.");
                    Save();
                }
            } catch (Exception ex) {
                Log.Persistence.Error(ex, "Failed to load config — resetting to defaults and overwriting file.");
                current = new TacticsConfig();
                Save();
            }
        }

        /// <summary>
        /// Walks the loaded config and strips null conditions / empty groups / empty rules
        /// that the SafeConditionConverter produced for un-parseable entries.
        /// Returns true if anything was removed (caller should re-save).
        /// </summary>
        static bool CleanupInvalidRules(TacticsConfig config) {
            if (config == null) return false;
            bool changed = false;

            foreach (var rule in EnumerateAllRules(config)) {
                if (rule.ConditionGroups == null) continue;
                for (int g = rule.ConditionGroups.Count - 1; g >= 0; g--) {
                    var grp = rule.ConditionGroups[g];
                    if (grp?.Conditions == null) {
                        rule.ConditionGroups.RemoveAt(g);
                        changed = true;
                        continue;
                    }
                    int before = grp.Conditions.Count;
                    grp.Conditions.RemoveAll(c => c == null);
                    if (grp.Conditions.Count != before) changed = true;
                    if (grp.Conditions.Count == 0) {
                        rule.ConditionGroups.RemoveAt(g);
                        changed = true;
                    }
                }
            }

            RemoveRulesWithNoGroups(config, ref changed);
            return changed;
        }

        static System.Collections.Generic.IEnumerable<TacticsRule> EnumerateAllRules(TacticsConfig config) {
            if (config.GlobalRules != null)
                foreach (var r in config.GlobalRules) yield return r;
            if (config.CharacterRules != null)
                foreach (var kv in config.CharacterRules)
                    if (kv.Value != null)
                        foreach (var r in kv.Value) yield return r;
        }

        static void RemoveRulesWithNoGroups(TacticsConfig config, ref bool changed) {
            if (config.GlobalRules != null) {
                int before = config.GlobalRules.Count;
                config.GlobalRules.RemoveAll(r => r.ConditionGroups == null || r.ConditionGroups.Count == 0);
                if (config.GlobalRules.Count != before) changed = true;
            }
            if (config.CharacterRules != null) {
                foreach (var kv in config.CharacterRules) {
                    if (kv.Value == null) continue;
                    int before = kv.Value.Count;
                    kv.Value.RemoveAll(r => r.ConditionGroups == null || r.ConditionGroups.Count == 0);
                    if (kv.Value.Count != before) changed = true;
                }
            }
        }
```

Note: `EnumerateAllRules` walks `config.GlobalRules` (List) and `config.CharacterRules` (Dictionary of per-unit rule lists) — both exist on `WrathTactics/Models/TacticsConfig.cs`.

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Persistence/ConfigManager.cs
git commit -m "feat(persistence): self-heal configs with removed condition props"
```

---

## Phase 5: Manual verification on Steam Deck

### Task 10: Deploy and smoke-test

**Files:** none modified; uses `./deploy.sh`.

- [ ] **Step 1: Release build + deploy**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/
./deploy.sh
```

Expected: deploy script completes without SSH/SCP errors.

- [ ] **Step 2: Start a save on the Steam Deck and run these scenarios**

Launch the game, load a save with an active party. In each case, open the Tactics panel (Ctrl+T), create the described rule on an appropriate companion, close the panel, and observe behavior in combat.

1. **Negate on ally buff check.** Rule: `Self HasBuff Bless, Negate=ON → Cast Bless on Self`. Before casting Bless, rule fires and casts. After Bless is active, rule does not re-fire. Toggle `Negate=OFF` and confirm the same rule now only fires while Bless IS active.
2. **Negate on enemy buff (Mark-of-Death use case).** Rule: `EnemyLowestHp HasBuff <Mark of Death buff GUID>, Negate=ON → Cast Mark of Death on ConditionTarget`. Rule fires on the first eligible enemy; after the buff lands, the same target no longer matches and the caster stops re-casting on it.
3. **Negate on numeric.** Rule: `Self HpPercent < 50, Negate=ON`. Confirm the rule only fires while HP ≥ 50.
4. **Count subject with Negate.** Rule: `AllyCount >= 2 with HpPercent < 60, Negate=ON`. Confirm the rule only fires when fewer than 2 allies are below 60% HP.
5. **Self-healing load — single bad condition.** Quit to desktop. On Steam Deck, edit the save's `tactics-{GameId}.json` directly (via SSH): change one condition's `"Property"` to `"MissingBuff"`. Relaunch the game and reload the save. Expected: mod log contains one Warning naming the rule and raw condition JSON; on disk the file is rewritten without that condition; the rest of the rule loads cleanly; no crash.
6. **Self-healing load — structurally corrupt file.** Truncate the JSON mid-object. Reload. Expected: one Error in the mod log; config loads empty; the file is overwritten with a valid empty config; game does not crash.
7. **BubbleBuffs compat sanity.** If `wrath-epic-buffing` is also installed, confirm existing HasBuff-driven rules and BubbleBuffs buffing routines co-exist without new errors in the log.

- [ ] **Step 3: If all seven pass, proceed to release. If any fails, open a follow-up fix before tagging.**

---

## Phase 6: Release

### Task 11: Version bump and commit

**Files:**
- Modify: `WrathTactics/Info.json`
- Modify: `WrathTactics/WrathTactics.csproj`

- [ ] **Step 1: Decide version**

Read the current `Version` in both `WrathTactics/Info.json` and `WrathTactics/WrathTactics.csproj`. This change is a user-visible feature plus a breaking config change; bump the minor version (e.g. `0.4.0` → `0.5.0`). Use the same version string in both files — the CLAUDE.md note "Version bump requires both Info.json and csproj" applies.

- [ ] **Step 2: Edit both files**

In `WrathTactics/Info.json`, update the `Version` field. In `WrathTactics/WrathTactics.csproj`, update the `<Version>` element.

- [ ] **Step 3: Build release**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/
```

Expected: `bin/WrathTactics-<new-version>.zip` exists.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj
git commit -m "chore: release <new-version> — condition Negate + remove MissingBuff/HasDebuff"
```

- [ ] **Step 5: Hand back to user for tagging + push (do not tag/push autonomously).**

Print a short summary: which tasks passed, the new zip path, and the new version string. Let the user drive `git tag`, `git push`, and the Nexus-upload GitHub release.

---

## Notes for the implementer

- **Build warnings about `findstr`** on Linux are harmless — the csproj auto-detection target uses the Windows-only `findstr` binary.
- **`TacticsConfig` layout** used in Task 9: `GlobalRules` (`List<TacticsRule>`) and `CharacterRules` (`Dictionary<string, List<TacticsRule>>`), per `WrathTactics/Models/TacticsConfig.cs`.
- **Anchor numbers are advisory.** If the NOT button looks cramped on the Deck's 1280×800 display, widen it slightly by adjusting Task 6's `(0.14, 0.18)` and Task 7's `(0.38, 0.41)` — keep Subject/Property readable.
- **No CLAUDE.md update** is planned unless manual testing reveals a new gotcha; if it does, add a short entry under "Unity UI & TextMeshPro Gotchas" or "Game API Gotchas" as appropriate.
