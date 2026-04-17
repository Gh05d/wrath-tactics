# Feedback Batch 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix buff-dropdown freeze, add combat-end cleanup (auto-toggle Rage off), and add alignment conditions.

**Architecture:** Three orthogonal features implemented in order of increasing risk. (1) Alignment is an additive condition property. (2) Combat-end cleanup adds a one-shot post-combat evaluation pass gated by a new static flag on the evaluator. (3) Buff picker replaces the monolithic `PopupSelector`-built overlay for HasBuff/MissingBuff with a dedicated search-first overlay class that renders 0–50 GameObjects instead of ~3000.

**Tech Stack:** C# / .NET 4.8.1 / Unity UGUI / TextMeshPro / Newtonsoft.Json / Kingmaker game API

**Build command:** `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

**Deploy command:** `./deploy.sh`

**Spec:** `docs/superpowers/specs/2026-04-17-feedback-batch-2-design.md`

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `WrathTactics/Models/Enums.cs` | Modify | Add `ConditionProperty.IsInCombat`, `ConditionProperty.Alignment` |
| `WrathTactics/Models/TacticsConfig.cs` | Modify | Add `RecentBuffGuids` persistence |
| `WrathTactics/Engine/ConditionEvaluator.cs` | Modify | `IsPostCombatPass` flag, `IsInCombat` branch, `CheckAlignment` helper, switch cases in both unit-evaluators |
| `WrathTactics/Engine/TacticsEvaluator.cs` | Modify | `RunPostCombatCleanup()` method, transition hook |
| `WrathTactics/Engine/CommonBuffRegistry.cs` | Create | Curated ally/enemy buff name lists with lazy name-to-GUID resolution |
| `WrathTactics/UI/BuffPickerOverlay.cs` | Create | Search-first overlay with recents + curated defaults + filter-capped-50 results |
| `WrathTactics/UI/ConditionRowWidget.cs` | Modify | Add `IsInCombat`/`Alignment` to property lists; value-renderers for both; replace `CreateBuffSelector` body |
| `CLAUDE.md` | Modify | New gotchas: `UnitAlignment.ValueRaw`, post-combat single-tick pattern |

---

## Phase 1: Alignment Condition

### Task 1: Add `Alignment` to `ConditionProperty` enum

**Files:**
- Modify: `WrathTactics/Models/Enums.cs:23-39`

- [ ] **Step 1: Add `Alignment` enum value**

In `WrathTactics/Models/Enums.cs`, inside `ConditionProperty`, add `Alignment` after `SaveWill` (line 38):

```csharp
    public enum ConditionProperty {
        HpPercent,
        AC,
        HasBuff,
        MissingBuff,
        HasCondition,
        HasDebuff,
        SpellSlotsAtLevel,
        SpellSlotsAboveLevel,
        Resource,
        CreatureType,
        CombatRounds,
        IsDead,
        SaveFortitude,
        SaveReflex,
        SaveWill,
        Alignment
    }
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)` (warnings about `findstr` are fine).

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/Enums.cs
git commit -m "feat(model): add ConditionProperty.Alignment"
```

---

### Task 2: Implement alignment evaluation in `ConditionEvaluator`

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs` — add `using`, add switch cases in `EvaluateUnitProperty` and `MatchesPropertyThreshold`, add `CheckAlignment` helper.

- [ ] **Step 1: Add `using` for the Alignment enum**

At the top of `WrathTactics/Engine/ConditionEvaluator.cs`, after the existing `using` block (after line 9), add:

```csharp
using Kingmaker.Enums;
using KmAlignment = Kingmaker.Enums.Alignment;
```

Rationale: `Kingmaker.Enums.Alignment` is an enum, and without the alias its name collides with the using-directive-free identifier `Alignment` if anything else in the file introduces it. The alias keeps things unambiguous.

- [ ] **Step 2: Add `Alignment` case in `EvaluateUnitProperty`**

In `WrathTactics/Engine/ConditionEvaluator.cs`, inside `EvaluateUnitProperty`, add a new case before `default:` (before line 248):

```csharp
                case ConditionProperty.Alignment:
                    bool alignMatch = CheckAlignment(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !alignMatch : alignMatch;
```

- [ ] **Step 3: Add `Alignment` case in `MatchesPropertyThreshold`**

In the same file, inside `MatchesPropertyThreshold`, add a new case before `default:` (before line 309):

```csharp
                case ConditionProperty.Alignment:
                    bool alignMatch2 = CheckAlignment(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !alignMatch2 : alignMatch2;
```

- [ ] **Step 4: Add `CheckAlignment` helper method**

In the same file, add this method directly after `CheckCreatureType` (after line 349):

```csharp
        static bool CheckAlignment(UnitEntityData unit, string component) {
            if (string.IsNullOrEmpty(component)) return false;
            var align = unit.Descriptor.Alignment.ValueRaw;
            switch (component.ToLowerInvariant()) {
                case "good":
                    return align == KmAlignment.LawfulGood
                        || align == KmAlignment.NeutralGood
                        || align == KmAlignment.ChaoticGood;
                case "evil":
                    return align == KmAlignment.LawfulEvil
                        || align == KmAlignment.NeutralEvil
                        || align == KmAlignment.ChaoticEvil;
                case "lawful":
                    return align == KmAlignment.LawfulGood
                        || align == KmAlignment.LawfulNeutral
                        || align == KmAlignment.LawfulEvil;
                case "chaotic":
                    return align == KmAlignment.ChaoticGood
                        || align == KmAlignment.ChaoticNeutral
                        || align == KmAlignment.ChaoticEvil;
                case "neutral":
                    // "Weder Good noch Evil": matches LN / TN / CN. Unaligned creatures
                    // (default = TrueNeutral) also match Neutral here — consistent with
                    // Pathfinder Detect Evil semantics (they don't match Good or Evil).
                    return align == KmAlignment.LawfulNeutral
                        || align == KmAlignment.TrueNeutral
                        || align == KmAlignment.ChaoticNeutral;
                default:
                    return false;
            }
        }
```

- [ ] **Step 5: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine): evaluate Alignment component conditions (Good/Evil/Lawful/Chaotic/Neutral)"
```

---

### Task 3: Expose Alignment property in the UI

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs` — add `Alignment` to property lists for all unit subjects, add value-dropdown renderer, constrain operator to `=`/`!=`.

- [ ] **Step 1: Add `Alignment` to property lists for all unit subjects**

In `WrathTactics/UI/ConditionRowWidget.cs`, extend the lists returned by `GetPropertiesForSubject` so that `Alignment` appears for every subject that evaluates unit properties. Replace the `Self`, `Ally`, `AllyCount` branches (lines 332–347) with:

```csharp
                case ConditionSubject.Self:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff, ConditionProperty.MissingBuff,
                        ConditionProperty.HasCondition, ConditionProperty.HasDebuff,
                        ConditionProperty.SpellSlotsAtLevel, ConditionProperty.SpellSlotsAboveLevel,
                        ConditionProperty.Alignment
                    };
                case ConditionSubject.Ally:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff, ConditionProperty.MissingBuff,
                        ConditionProperty.HasCondition, ConditionProperty.HasDebuff, ConditionProperty.IsDead,
                        ConditionProperty.Alignment
                    };
                case ConditionSubject.AllyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff, ConditionProperty.MissingBuff,
                        ConditionProperty.HasCondition, ConditionProperty.HasDebuff, ConditionProperty.IsDead,
                        ConditionProperty.Alignment
                    };
```

Then replace the `Enemy` / `EnemyXxx` / `EnemyCount` branches (lines 348–371) with:

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
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
                        ConditionProperty.HasBuff, ConditionProperty.HasCondition,
                        ConditionProperty.HasDebuff, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment
                    };
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasDebuff, ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment
                    };
```

`Combat` stays unchanged — it doesn't evaluate unit properties.

- [ ] **Step 2: Treat Alignment like CreatureType in the non-count layout branch**

In `WrathTactics/UI/ConditionRowWidget.cs`, replace the `isCreatureType` logic block in the non-count branch (lines 194–229). Find the block beginning with `bool isCreatureType = condition.Property == ConditionProperty.CreatureType;` and replace the lines up to and including the `if (isCreatureType) { ... }` closing brace (line 229) with:

```csharp
                bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
                bool isAlignment = condition.Property == ConditionProperty.Alignment;
                bool isBuffProp = condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.MissingBuff;
                bool needsOperator = !isHasCondition && !isHasDebuff && !isCreatureType && !isBuffProp && !isAlignment;

                // Operator popup selector (hidden for dropdown-based properties)
                if (needsOperator) {
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.Create(root, "Operator", 0.38f, 0.50f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            ConfigManager.Save();
                        });
                } else if (isCreatureType || isAlignment) {
                    var eqOpNames = new List<string> { "=", "!=" };
                    int eqOpIdx = condition.Operator == ConditionOperator.NotEqual ? 1 : 0;
                    PopupSelector.Create(root, "EqOperator", 0.38f, 0.44f, eqOpNames, eqOpIdx, v => {
                        condition.Operator = v == 1 ? ConditionOperator.NotEqual : ConditionOperator.Equal;
                        ConfigManager.Save();
                    });
                } else {
                    condition.Operator = ConditionOperator.Equal;
                }

                if (isCreatureType) {
                    var creatureTypes = new List<string> {
                        "Aberration", "Animal", "Construct", "Dragon", "Fey",
                        "Humanoid", "MagicalBeast", "MonstrousHumanoid", "Ooze",
                        "Outsider", "Plant", "Swarm", "Undead", "Vermin"
                    };
                    int ctIdx = creatureTypes.IndexOf(condition.Value);
                    if (ctIdx < 0) { ctIdx = 0; condition.Value = creatureTypes[0]; }
                    PopupSelector.Create(root, "CreatureTypeValue", 0.45f, 0.88f, creatureTypes, ctIdx, v => {
                        condition.Value = creatureTypes[v];
                        ConfigManager.Save();
                    });
                } else if (isAlignment) {
                    var alignmentValues = new List<string> {
                        "Good", "Evil", "Lawful", "Chaotic", "Neutral"
                    };
                    int aIdx = alignmentValues.IndexOf(condition.Value);
                    if (aIdx < 0) { aIdx = 0; condition.Value = alignmentValues[0]; }
                    PopupSelector.Create(root, "AlignmentValue", 0.45f, 0.88f, alignmentValues, aIdx, v => {
                        condition.Value = alignmentValues[v];
                        ConfigManager.Save();
                    });
                } else if (isHasCondition) {
```

*Note:* this re-uses the existing `else if (isHasCondition)` branch — only insert up to and including the new `isAlignment` block, then leave the rest of the existing chain (`isHasCondition`, `isHasDebuff`, etc.) intact.

- [ ] **Step 3: Treat Alignment like CreatureType in the count-subject layout branch**

In the count-subject block (lines 126–143), the existing code handles `CreatureType` with operator dropdown + value dropdown at positions `0.58–0.64` and `0.65–0.88`. Extend it to also handle `Alignment`.

Replace the `else if (condition.Property == ConditionProperty.CreatureType) { ... }` block (lines 126–143) with:

```csharp
                } else if (condition.Property == ConditionProperty.CreatureType
                    || condition.Property == ConditionProperty.Alignment) {
                    var eqOpNames = new List<string> { "=", "!=" };
                    int eqOpIdx = condition.Operator == ConditionOperator.NotEqual ? 1 : 0;
                    PopupSelector.Create(root, "CountEqOp", 0.58f, 0.64f, eqOpNames, eqOpIdx, v => {
                        condition.Operator = v == 1 ? ConditionOperator.NotEqual : ConditionOperator.Equal;
                        ConfigManager.Save();
                    });

                    List<string> valueOptions;
                    if (condition.Property == ConditionProperty.CreatureType) {
                        valueOptions = new List<string> {
                            "Aberration", "Animal", "Construct", "Dragon", "Fey",
                            "Humanoid", "MagicalBeast", "MonstrousHumanoid", "Ooze",
                            "Outsider", "Plant", "Swarm", "Undead", "Vermin"
                        };
                    } else {
                        valueOptions = new List<string> {
                            "Good", "Evil", "Lawful", "Chaotic", "Neutral"
                        };
                    }
                    int valIdx = valueOptions.IndexOf(condition.Value);
                    if (valIdx < 0) { valIdx = 0; condition.Value = valueOptions[0]; }
                    PopupSelector.Create(root, "CountValueDropdown", 0.65f, 0.88f, valueOptions, valIdx, v => {
                        condition.Value = valueOptions[v];
                        ConfigManager.Save();
                    });
```

- [ ] **Step 4: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Deploy and smoke-test alignment**

Run: `./deploy.sh`

In-game smoke test:
1. Open Tactics panel (Ctrl+T) on any character.
2. Add new rule, set Subject = `EnemyBiggestThreat`, Property = `Alignment`.
3. Verify operator shows `=`/`!=` dropdown, value shows `Good/Evil/Lawful/Chaotic/Neutral` dropdown.
4. Set operator `=`, value `Evil`, Action = Attack, Target = `ConditionTarget`.
5. Enter combat with an evil enemy (demon) → rule should fire.
6. Switch value to `Good`, enter combat with same demon → rule should NOT fire.
7. Close panel and reopen — verify selection persisted.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): Alignment condition property with Good/Evil/Lawful/Chaotic/Neutral dropdown"
```

---

## Phase 2: Combat-End Cleanup

### Task 4: Add `IsInCombat` property and evaluator support

**Files:**
- Modify: `WrathTactics/Models/Enums.cs:38` (add `IsInCombat`)
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs` — add `IsPostCombatPass` flag, extend `EvaluateCombat`.

- [ ] **Step 1: Add `IsInCombat` to `ConditionProperty` enum**

In `WrathTactics/Models/Enums.cs`, add `IsInCombat` after `Alignment`:

```csharp
    public enum ConditionProperty {
        HpPercent,
        AC,
        HasBuff,
        MissingBuff,
        HasCondition,
        HasDebuff,
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

- [ ] **Step 2: Add `IsPostCombatPass` flag to `ConditionEvaluator`**

In `WrathTactics/Engine/ConditionEvaluator.cs`, just after the `public static UnitEntityData LastMatchedAlly` declaration (after line 15), add:

```csharp
        /// <summary>
        /// Set to true during TacticsEvaluator.RunPostCombatCleanup() so that
        /// `Combat.IsInCombat` evaluates to false during the one-shot cleanup pass,
        /// regardless of transient game state.
        /// </summary>
        public static bool IsPostCombatPass { get; set; }
```

- [ ] **Step 3: Extend `EvaluateCombat` to handle `IsInCombat`**

In the same file, replace the entire `EvaluateCombat` method (lines 172–183) with:

```csharp
        static bool EvaluateCombat(Condition condition) {
            if (condition.Property == ConditionProperty.IsInCombat) {
                bool inCombat = !IsPostCombatPass && Game.Instance.Player.IsInCombat;
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
                default:
                    return false;
            }
        }
```

- [ ] **Step 4: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Models/Enums.cs WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine): Combat.IsInCombat condition + IsPostCombatPass flag"
```

---

### Task 5: Wire post-combat cleanup tick in `TacticsEvaluator`

**Files:**
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs` — hook transition, add `RunPostCombatCleanup()`.

- [ ] **Step 1: Replace the combat-end branch to call cleanup**

In `WrathTactics/Engine/TacticsEvaluator.cs`, replace lines 21–29 (the `if (!Game.Instance.Player.IsInCombat) { ... return; }` block at the top of `Tick`) with:

```csharp
            if (!Game.Instance.Player.IsInCombat) {
                if (wasInCombat) {
                    wasInCombat = false;
                    RunPostCombatCleanup(gameTimeSec);
                    cooldowns.Clear();
                    Log.Engine.Info("Combat ended, post-combat cleanup ran, cooldowns cleared");
                }
                return;
            }
```

- [ ] **Step 2: Add `RunPostCombatCleanup` method**

In the same file, add this method directly before `GetCombatRoundsElapsed` (before line 128):

```csharp
        static void RunPostCombatCleanup(float gameTimeSec) {
            var config = ConfigManager.Current;
            ConditionEvaluator.IsPostCombatPass = true;
            try {
                foreach (var unit in Game.Instance.Player.Party) {
                    if (!unit.IsInGame || unit.HPLeft <= 0) continue;
                    if (!config.IsEnabled(unit.UniqueId)) continue;

                    var globalRules = config.GlobalRules;
                    var charRules = config.GetRulesForCharacter(unit.UniqueId);

                    // Same ordering as combat tick: globals first, then character rules.
                    // Cooldowns are skipped here — this is a one-shot pass, and we clear
                    // cooldowns immediately after.
                    if (TryExecuteRulesIgnoringCooldown(globalRules, unit, "post-combat:global", gameTimeSec))
                        continue;
                    TryExecuteRulesIgnoringCooldown(charRules, unit, "post-combat:" + unit.CharacterName, gameTimeSec);
                }
            } catch (Exception ex) {
                Log.Engine.Error(ex, "RunPostCombatCleanup failed");
            } finally {
                ConditionEvaluator.IsPostCombatPass = false;
            }
        }

        static bool TryExecuteRulesIgnoringCooldown(List<TacticsRule> rules, UnitEntityData unit,
            string source, float gameTimeSec) {
            for (int i = 0; i < rules.Count; i++) {
                var entry = rules[i];
                if (!entry.Enabled) continue;

                var rule = PresetRegistry.Resolve(entry);
                ConditionEvaluator.ClearMatchedEntities();

                if (!ConditionEvaluator.Evaluate(rule, unit)) continue;

                var target = TargetResolver.Resolve(rule.Target, unit);
                if (!ActionValidator.CanExecute(rule.Action, unit, target)) {
                    Log.Engine.Warn($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): MATCH but action not executable");
                    continue;
                }

                if (CommandExecutor.Execute(rule.Action, unit, target)) {
                    Log.Engine.Info($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): EXECUTED -> {target?.CharacterName ?? "self"}");
                    return true;
                }
            }
            return false;
        }
```

- [ ] **Step 3: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/TacticsEvaluator.cs
git commit -m "feat(engine): one-shot post-combat cleanup tick on combat-end transition"
```

---

### Task 6: Expose `IsInCombat` in the UI

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs` — add `IsInCombat` to the `Combat` property list and render it with a Yes/No dropdown.

- [ ] **Step 1: Add `IsInCombat` to the `Combat` property list**

In `WrathTactics/UI/ConditionRowWidget.cs`, replace the `Combat` case in `GetPropertiesForSubject` (line 372–373) with:

```csharp
                case ConditionSubject.Combat:
                    return new List<ConditionProperty> {
                        ConditionProperty.CombatRounds,
                        ConditionProperty.IsInCombat
                    };
```

- [ ] **Step 2: Render `IsInCombat` with a Yes/No dropdown**

The `Combat` subject does not use the count-subject layout (it's not in `isCountSubject`). It falls into the non-count else-branch. Currently `CombatRounds` is a plain numeric property that uses the default operator + text-value layout.

We need to special-case `IsInCombat` inside the non-count branch. Locate the final `else` branch in the non-count block (lines 287–295), which currently reads:

```csharp
                } else {
                    // Normal single value input
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.51, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        ConfigManager.Save();
                    });
                }
```

Replace it with:

```csharp
                } else if (condition.Property == ConditionProperty.IsInCombat) {
                    var yesNo = new List<string> { "Ja", "Nein" };
                    // Map: "true" -> index 0 (Ja), anything else -> index 1 (Nein)
                    int yIdx = string.Equals(condition.Value, "true", StringComparison.OrdinalIgnoreCase)
                        ? 0 : 1;
                    if (string.IsNullOrEmpty(condition.Value)) condition.Value = "true";
                    PopupSelector.Create(root, "IsInCombatValue", 0.51f, 0.88f, yesNo, yIdx, v => {
                        condition.Value = v == 0 ? "true" : "false";
                        ConfigManager.Save();
                    });
                } else {
                    // Normal single value input
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.51, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        ConfigManager.Save();
                    });
                }
```

- [ ] **Step 3: Add `IsInCombat` to the operator-hiding logic**

In the same non-count branch, update the `needsOperator` boolean (line 197 in the original, adjusted for earlier edits in Task 3) to also hide the operator for `IsInCombat`. Find the `needsOperator` line added in Task 3:

```csharp
                bool needsOperator = !isHasCondition && !isHasDebuff && !isCreatureType && !isBuffProp && !isAlignment;
```

Change it to:

```csharp
                bool isInCombat = condition.Property == ConditionProperty.IsInCombat;
                bool needsOperator = !isHasCondition && !isHasDebuff && !isCreatureType && !isBuffProp && !isAlignment && !isInCombat;
```

And in the `else` of the operator-selection chain, where `condition.Operator = ConditionOperator.Equal;` is set, leave as-is — `IsInCombat` with `Equal` + "true"/"false" covers the Yes/No case. (NotEqual semantics would be redundant: `IsInCombat == false` is the same as `IsInCombat != true`.)

- [ ] **Step 4: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Deploy and smoke-test combat-end cleanup**

Run: `./deploy.sh`

In-game smoke test (Fleshdrinker/Bloodrager preferred, any character with an activatable works):
1. Give a character the Rage activatable (or any toggle ability).
2. Open Tactics panel, add a rule:
   - Condition 1: `Combat`.`IsInCombat` = `Nein`
   - Condition 2: `Self`.`HasBuff` = `Rage` (or the matching buff for the activatable)
   - Action: `Toggle Activatable` = Rage, Mode = `Off`
   - Target: `Self`
3. Enter combat, manually activate Rage.
4. Kill all enemies — combat ends.
5. Within one tick (≤ `TickIntervalSeconds`), Rage should toggle off.
6. Walk around in town: verify Rage does NOT auto-activate/deactivate continuously (i.e., no chatter in `Logs/wrath-tactics-*.log` from this rule).
7. Check session log for `post-combat cleanup ran` entry.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): Combat.IsInCombat condition with Ja/Nein dropdown"
```

---

## Phase 3: Buff Picker Rewrite

### Task 7: Add `RecentBuffGuids` to `TacticsConfig`

**Files:**
- Modify: `WrathTactics/Models/TacticsConfig.cs`

- [ ] **Step 1: Add the property**

In `WrathTactics/Models/TacticsConfig.cs`, add this property inside the `TacticsConfig` class after `DebugLogging` (after line 10):

```csharp
        [JsonProperty] public List<string> RecentBuffGuids { get; set; } = new();
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/TacticsConfig.cs
git commit -m "feat(model): persist RecentBuffGuids for buff picker"
```

---

### Task 8: Create `CommonBuffRegistry`

**Files:**
- Create: `WrathTactics/Engine/CommonBuffRegistry.cs`

Rationale: we don't have the exact in-game blueprint internal names memorized. The registry stores a list of "preferred names" (substrings / display names) and resolves them to actual GUIDs lazily via a case-insensitive `Contains` match against `BuffBlueprintProvider.GetBuffs()`. When multiple buffs match a name, we pick the **shortest-named match** (heuristic: the plain version is usually shortest, e.g. "Haste" vs "HasteGreater" vs "HasteMass").

- [ ] **Step 1: Create `CommonBuffRegistry.cs`**

Create `WrathTactics/Engine/CommonBuffRegistry.cs` with this content:

```csharp
using System.Collections.Generic;
using System.Linq;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Curated lists of commonly-checked buffs, shown as default entries in the
    /// BuffPickerOverlay when the search field is empty. Names are resolved to
    /// GUIDs lazily via BuffBlueprintProvider (substring match, shortest wins).
    /// </summary>
    public static class CommonBuffRegistry {
        // Preferred names for ally-side buffs (Self / Ally / AllyCount contexts).
        // Order matters — it's the display order in the picker.
        static readonly List<string> AllyCommonNames = new List<string> {
            "Haste",
            "Bless",
            "Good Hope",
            "Heroism",
            "Prayer",
            "Shield of Faith",
            "Mage Armor",
            "Shield",
            "Aid",
            "Barkskin",
            "Stoneskin",
            "Bull's Strength",
            "Bear's Endurance",
            "Cat's Grace",
            "Owl's Wisdom",
            "Mirror Image",
            "Displacement",
            "Blur",
            "Freedom of Movement",
            "Protection from Evil"
        };

        // Preferred names for enemy-side buffs (Enemy* subjects).
        static readonly List<string> EnemyCommonNames = new List<string> {
            "Haste",
            "Mirror Image",
            "Displacement",
            "Blur",
            "Stoneskin",
            "Bless",
            "Bloodlust",
            "Heroism",
            "Mage Armor",
            "Protection from Evil",
            "Greater Heroism",
            "Unbreakable Heart",
            "Death Ward",
            "Mind Blank",
            "Freedom of Movement",
            "Protection from Arrows",
            "Resist Energy",
            "Energy Resistance Communal",
            "Shield",
            "Barkskin"
        };

        static List<string> cachedAllyGuids;
        static List<string> cachedEnemyGuids;

        public static List<string> GetDefaultGuids(ConditionSubject subject) {
            if (IsEnemySubject(subject)) {
                if (cachedEnemyGuids == null) cachedEnemyGuids = Resolve(EnemyCommonNames);
                return cachedEnemyGuids;
            }
            if (cachedAllyGuids == null) cachedAllyGuids = Resolve(AllyCommonNames);
            return cachedAllyGuids;
        }

        /// <summary>
        /// Force cache rebuild on next access — call when the BuffBlueprintProvider
        /// cache may have been invalidated (currently unused).
        /// </summary>
        public static void Invalidate() {
            cachedAllyGuids = null;
            cachedEnemyGuids = null;
        }

        static bool IsEnemySubject(ConditionSubject subject) {
            switch (subject) {
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
                    return true;
                default:
                    return false;
            }
        }

        static List<string> Resolve(List<string> preferredNames) {
            var buffs = BuffBlueprintProvider.GetBuffs();
            var result = new List<string>();
            foreach (var preferred in preferredNames) {
                var guid = FindBestMatch(buffs, preferred);
                if (guid != null) result.Add(guid);
                else Log.Engine.Warn($"CommonBuffRegistry: no match for \"{preferred}\"");
            }
            return result;
        }

        static string FindBestMatch(List<BuffBlueprintProvider.BuffEntry> buffs, string preferred) {
            // Normalize: strip spaces + apostrophes for matching, but keep the original
            // cache entry's Name for the comparison target (we normalize both sides).
            string needle = Normalize(preferred);
            BuffBlueprintProvider.BuffEntry best = default;
            bool found = false;
            foreach (var entry in buffs) {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (!Normalize(entry.Name).Contains(needle)) continue;
                if (!found || entry.Name.Length < best.Name.Length) {
                    best = entry;
                    found = true;
                }
            }
            return found ? best.Guid : null;
        }

        static string Normalize(string s) {
            return s.Replace(" ", "").Replace("'", "").ToLowerInvariant();
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/CommonBuffRegistry.cs
git commit -m "feat(engine): CommonBuffRegistry with curated ally/enemy buff defaults"
```

---

### Task 9: Create `BuffPickerOverlay`

**Files:**
- Create: `WrathTactics/UI/BuffPickerOverlay.cs`

Design notes:
- Static `Open(currentGuid, subject, onSelected)` creates a full-screen overlay attached to `Game.Instance.UI.Canvas`.
- The overlay contains a header with a `TMP_InputField` autofocused on open.
- Below the header, a `ScrollRect` holds the results list. The list is a `VerticalLayoutGroup` + `ContentSizeFitter`.
- Empty search → render recent section header + rows (0–5), then defaults section header + rows (20).
- Non-empty search → hide section headers, render filtered results capped at 50.
- Selection updates `TacticsConfig.RecentBuffGuids` (insert at 0, dedupe, trim to 5) via `ConfigManager.Save()`.
- Escape closes the overlay without selection.
- Clicking outside the popup container closes the overlay without selection (existing pattern from `PopupSelector`).

- [ ] **Step 1: Create `BuffPickerOverlay.cs`**

Create `WrathTactics/UI/BuffPickerOverlay.cs` with this content:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Kingmaker;
using WrathTactics.Engine;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    /// <summary>
    /// Search-first overlay for selecting a BlueprintBuff GUID.
    /// Replaces the generic PopupSelector for HasBuff/MissingBuff conditions to
    /// avoid instantiating ~3000 rows up front.
    /// </summary>
    public class BuffPickerOverlay : MonoBehaviour {
        const int MaxFilterResults = 50;
        const int MaxRecents = 5;

        ConditionSubject subject;
        Action<string> onSelected;
        TMP_InputField searchInput;
        GameObject rowsContainer;
        string currentQuery = "";

        public static GameObject Open(string currentGuid, ConditionSubject subject,
            Action<string> onSelected) {
            var canvas = Game.Instance.UI.Canvas.transform;

            var (overlay, overlayRect) = UIHelpers.Create("BuffPickerOverlay", canvas);
            overlayRect.FillParent();
            UIHelpers.AddBackground(overlay, new Color(0, 0, 0, 0.4f));
            overlay.AddComponent<Button>().onClick.AddListener(() => Destroy(overlay));

            var (popup, popupRect) = UIHelpers.Create("Popup", overlay.transform);
            UIHelpers.AddBackground(popup, new Color(0.12f, 0.12f, 0.12f, 0.99f));
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(420f, 500f);

            // Prevent clicks on the popup from bubbling to the overlay (which would close it).
            // AddBackground above already attached an Image; Button uses it as its target graphic.
            popup.AddComponent<Button>(); // swallow clicks

            var controller = popup.AddComponent<BuffPickerOverlay>();
            controller.subject = subject;
            controller.onSelected = guid => {
                RecordRecent(guid);
                onSelected?.Invoke(guid);
                Destroy(overlay);
            };
            controller.BuildUI(popup);
            controller.RenderList(); // initial render with empty query

            return overlay;
        }

        void BuildUI(GameObject popup) {
            // Header: search input
            var (header, headerRect) = UIHelpers.Create("Header", popup.transform);
            headerRect.SetAnchor(0, 1, 1, 1);
            headerRect.sizeDelta = new Vector2(0, 40);
            headerRect.anchoredPosition = new Vector2(0, -20);
            UIHelpers.AddBackground(header, new Color(0.18f, 0.18f, 0.18f, 1f));

            searchInput = UIHelpers.CreateTMPInputField(header, "Search",
                0.02, 0.98, "", 16f);
            var inputRect = searchInput.GetComponent<RectTransform>();
            inputRect.SetAnchor(0.02f, 0.98f, 0.1f, 0.9f);
            inputRect.sizeDelta = Vector2.zero;
            searchInput.onValueChanged.AddListener(v => {
                currentQuery = v ?? "";
                RenderList();
            });
            // Autofocus on next frame (Unity quirk — calling now can no-op before the
            // EventSystem picks up the new object).
            StartCoroutine(FocusSearchNextFrame());

            // Body: scroll view
            var (scrollObj, scrollRect) = UIHelpers.Create("Scroll", popup.transform);
            scrollRect.SetAnchor(0, 1, 0, 1);
            scrollRect.sizeDelta = new Vector2(0, -40);
            scrollRect.anchoredPosition = new Vector2(0, 0);

            var (viewport, viewportRect) = UIHelpers.Create("Viewport", scrollObj.transform);
            viewportRect.FillParent();
            viewport.AddComponent<RectMask2D>();

            var (content, contentRect) = UIHelpers.Create("Content", viewport.transform);
            contentRect.SetAnchor(0, 1, 1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0, 0);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 1;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.padding = new RectOffset(4, 4, 4, 4);

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

            rowsContainer = content;
        }

        System.Collections.IEnumerator FocusSearchNextFrame() {
            yield return null;
            if (searchInput != null) {
                searchInput.Select();
                searchInput.ActivateInputField();
            }
        }

        void Update() {
            if (Input.GetKeyDown(KeyCode.Escape)) {
                var overlay = transform.parent != null ? transform.parent.gameObject : gameObject;
                Destroy(overlay);
            }
        }

        void RenderList() {
            if (rowsContainer == null) return;

            // Clear existing rows
            for (int i = rowsContainer.transform.childCount - 1; i >= 0; i--) {
                Destroy(rowsContainer.transform.GetChild(i).gameObject);
            }

            if (string.IsNullOrEmpty(currentQuery)) {
                RenderDefaultsLayout();
            } else {
                RenderFilteredLayout(currentQuery);
            }
        }

        void RenderDefaultsLayout() {
            var recents = GetRecentEntries();
            var defaults = GetDefaultEntries();

            // Dedupe: if a buff is in recents, don't repeat it in defaults.
            var recentGuids = new HashSet<string>(recents.Select(e => e.Guid));
            var dedupedDefaults = defaults.Where(e => !recentGuids.Contains(e.Guid)).ToList();

            if (recents.Count > 0) {
                AddSectionHeader("★ Recents");
                foreach (var e in recents) AddRow(e);
            }

            if (dedupedDefaults.Count > 0) {
                string label = CommonBuffRegistry.GetType() != null // always true, kept for symmetry
                    ? (IsEnemySubjectLocal(subject) ? "Common Enemy Buffs" : "Common Ally Buffs")
                    : "Common Buffs";
                AddSectionHeader(label);
                foreach (var e in dedupedDefaults) AddRow(e);
            }

            if (recents.Count == 0 && dedupedDefaults.Count == 0) {
                AddInfoLabel("(no suggestions available — start typing to search)");
            }
        }

        void RenderFilteredLayout(string query) {
            var all = BuffBlueprintProvider.GetBuffs();
            string needle = query.ToLowerInvariant();
            var matches = new List<BuffBlueprintProvider.BuffEntry>();
            foreach (var entry in all) {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (entry.Name.ToLowerInvariant().Contains(needle)) {
                    matches.Add(entry);
                    if (matches.Count >= MaxFilterResults) break;
                }
            }

            if (matches.Count == 0) {
                AddInfoLabel($"No matches for \"{query}\"");
                return;
            }

            foreach (var entry in matches) AddRow(entry);

            if (matches.Count == MaxFilterResults) {
                AddInfoLabel($"(showing first {MaxFilterResults} matches — refine your search for more)");
            }
        }

        List<BuffBlueprintProvider.BuffEntry> GetRecentEntries() {
            var cfg = ConfigManager.Current;
            var all = BuffBlueprintProvider.GetBuffs();
            var result = new List<BuffBlueprintProvider.BuffEntry>();
            foreach (var guid in cfg.RecentBuffGuids) {
                var entry = all.FirstOrDefault(b => b.Guid == guid);
                if (!string.IsNullOrEmpty(entry.Guid)) result.Add(entry);
            }
            return result;
        }

        List<BuffBlueprintProvider.BuffEntry> GetDefaultEntries() {
            var all = BuffBlueprintProvider.GetBuffs();
            var guids = CommonBuffRegistry.GetDefaultGuids(subject);
            var result = new List<BuffBlueprintProvider.BuffEntry>();
            foreach (var guid in guids) {
                var entry = all.FirstOrDefault(b => b.Guid == guid);
                if (!string.IsNullOrEmpty(entry.Guid)) result.Add(entry);
            }
            return result;
        }

        void AddSectionHeader(string text) {
            var (hdr, _) = UIHelpers.Create("Header_" + text, rowsContainer.transform);
            hdr.AddComponent<LayoutElement>().preferredHeight = 24;
            UIHelpers.AddBackground(hdr, new Color(0.08f, 0.08f, 0.08f, 1f));
            var label = UIHelpers.AddLabel(hdr, text, 13f, TextAlignmentOptions.MidlineLeft,
                new Color(0.7f, 0.7f, 0.7f));
            label.margin = new Vector4(6, 0, 4, 0);
        }

        void AddInfoLabel(string text) {
            var (info, _) = UIHelpers.Create("Info", rowsContainer.transform);
            info.AddComponent<LayoutElement>().preferredHeight = 28;
            var label = UIHelpers.AddLabel(info, text, 13f, TextAlignmentOptions.MidlineCenter,
                new Color(0.55f, 0.55f, 0.55f));
            label.margin = new Vector4(8, 0, 8, 0);
        }

        void AddRow(BuffBlueprintProvider.BuffEntry entry) {
            var (row, _) = UIHelpers.Create("Row_" + entry.Guid, rowsContainer.transform);
            row.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddBackground(row, new Color(0.2f, 0.2f, 0.2f, 1f));
            var label = UIHelpers.AddLabel(row, entry.Name, 14f, TextAlignmentOptions.MidlineLeft);
            label.margin = new Vector4(8, 0, 4, 0);
            var guid = entry.Guid;
            row.AddComponent<Button>().onClick.AddListener(() => onSelected?.Invoke(guid));
        }

        static bool IsEnemySubjectLocal(ConditionSubject subject) {
            switch (subject) {
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
                    return true;
                default:
                    return false;
            }
        }

        static void RecordRecent(string guid) {
            if (string.IsNullOrEmpty(guid)) return;
            var cfg = ConfigManager.Current;
            cfg.RecentBuffGuids.Remove(guid);
            cfg.RecentBuffGuids.Insert(0, guid);
            while (cfg.RecentBuffGuids.Count > MaxRecents) {
                cfg.RecentBuffGuids.RemoveAt(cfg.RecentBuffGuids.Count - 1);
            }
            ConfigManager.Save();
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/BuffPickerOverlay.cs
git commit -m "feat(ui): BuffPickerOverlay — search-first buff selector with recents + defaults"
```

---

### Task 10: Wire `BuffPickerOverlay` into `CreateBuffSelector`

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs:307-328` (replace `CreateBuffSelector`)

- [ ] **Step 1: Replace `CreateBuffSelector` body**

In `WrathTactics/UI/ConditionRowWidget.cs`, replace the entire `CreateBuffSelector` method (lines 307–328) with:

```csharp
        void CreateBuffSelector(GameObject root, float xMin, float xMax) {
            var buffs = BuffBlueprintProvider.GetBuffs();

            // Fallback to text input if blueprint cache is empty (e.g. main-menu state).
            if (buffs.Count == 0) {
                var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                    xMin, xMax, condition.Value ?? "", 16f);
                valueInput.onEndEdit.AddListener(v => {
                    condition.Value = v;
                    ConfigManager.Save();
                });
                return;
            }

            // Button showing the current selection, click opens BuffPickerOverlay.
            var (btnObj, btnRect) = UIHelpers.Create("BuffPickerButton", root.transform);
            btnRect.SetAnchor(xMin, xMax, 0, 1);
            btnRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(btnObj, new Color(0.22f, 0.22f, 0.22f, 1f));

            string currentLabel = BuffBlueprintProvider.GetName(condition.Value);
            if (string.IsNullOrEmpty(currentLabel) || currentLabel == condition.Value)
                currentLabel = string.IsNullOrEmpty(condition.Value) ? "(pick a buff)" : currentLabel;
            var label = UIHelpers.AddLabel(btnObj, currentLabel, 14f, TextAlignmentOptions.MidlineLeft);
            label.margin = new Vector4(6, 0, 20, 0);

            var (arrow, arrowRect) = UIHelpers.Create("Arrow", btnObj.transform);
            arrowRect.SetAnchor(0.88, 1, 0, 1);
            arrowRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(arrow, "v", 14f, TextAlignmentOptions.Midline,
                new Color(0.6f, 0.6f, 0.6f));

            var subjectForPicker = condition.Subject;
            btnObj.AddComponent<Button>().onClick.AddListener(() => {
                BuffPickerOverlay.Open(condition.Value, subjectForPicker, guid => {
                    condition.Value = guid;
                    ConfigManager.Save();
                    label.text = BuffBlueprintProvider.GetName(guid);
                });
            });
        }
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Deploy and smoke-test the buff picker**

Run: `./deploy.sh`

In-game smoke test:
1. Load a mid-campaign save (so the blueprint cache is warm).
2. Open Tactics panel, add a rule with Subject=`Enemy`, Property=`HasBuff`.
3. Click the buff button → overlay opens **instantly** with search field focused, recents section (empty on first open), and `Common Enemy Buffs` with up to 20 rows.
4. Type `has` → filter kicks in, shows buffs containing "has" (Haste, etc.), capped at 50.
5. Clear search → back to default layout.
6. Click `Haste` → overlay closes, button label updates to `Haste`.
7. Re-open the picker → Recents section shows `Haste` at the top.
8. Select 5 more buffs → recents list grows to max 5, oldest drops off.
9. Scrolling: verify the list scrolls smoothly within the 20-row default list and within filter results.
10. Press Escape or click outside the popup → overlay closes without selection.
11. Switch Subject to `Self` → click buff button → verify `Common Ally Buffs` header and ally-side list appears.

Check `wrath-tactics-*.log`:
- No `CommonBuffRegistry: no match for ...` warnings for commonly-existing buffs (some exotic names may not match — acceptable, noted in the log).

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): wire BuffPickerOverlay into HasBuff/MissingBuff picker"
```

---

## Phase 4: Documentation

### Task 11: Update `CLAUDE.md` with gotchas

**Files:**
- Modify: `CLAUDE.md` (append to `## Game API Gotchas` section)

- [ ] **Step 1: Add two new gotcha bullets**

In `CLAUDE.md`, in the `## Game API Gotchas` section, append these two bullets at the end of the bullet list:

```markdown
- **Alignment API**: `UnitDescriptor.Alignment` is a `Kingmaker.UnitLogic.Alignments.UnitAlignment` object; the actual alignment value is `.ValueRaw` of type `Kingmaker.Enums.Alignment` (9-value enum: LawfulGood..ChaoticEvil, NOT a flag). Don't confuse with `Kingmaker.UnitLogic.Alignments.AlignmentMaskType` which is a flag enum but is NOT what `UnitAlignment` exposes. For component matching (Good/Evil/Lawful/Chaotic), enumerate the 3 member values explicitly.
- **Post-combat evaluation**: `TacticsEvaluator.Tick` early-returns when `!Player.IsInCombat`. To let rules fire on the combat-end transition, `RunPostCombatCleanup()` runs a single evaluation pass with `ConditionEvaluator.IsPostCombatPass = true`, which makes `Combat.IsInCombat == false` conditions match regardless of transient game state. Cooldowns are skipped in this pass and cleared immediately after.
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: gotchas for UnitAlignment.ValueRaw and post-combat eval pattern"
```

---

## Final Verification

- [ ] **Step 1: Full build and release package**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)` and a zip created at `bin/WrathTactics-<version>.zip`.

- [ ] **Step 2: Deploy and run end-to-end smoke tests from each phase**

Re-run the three smoke tests from Tasks 3 (alignment), 6 (combat-end), 10 (buff picker) sequentially on one save. Verify no regressions in each others' features.

- [ ] **Step 3: Check logs for unexpected warnings/errors**

On the Steam Deck:
```bash
ssh deck-direct "tail -200 '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/'\$(ssh deck-direct \"ls -t '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/' | head -1\")"
```

Expected: no unhandled exceptions related to the new features. Warnings from `CommonBuffRegistry: no match for "<name>"` are acceptable for exotic buff names that don't exist in the game's blueprint DB — review and consider dropping them from the curated list if the list looks too short in-game.

- [ ] **Step 4: Final commit (if CLAUDE.md wasn't committed yet)**

Only if there are uncommitted changes from test tweaks; otherwise skip.
