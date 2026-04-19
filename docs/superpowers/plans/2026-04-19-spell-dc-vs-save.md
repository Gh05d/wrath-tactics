# Dynamic DC-vs-Save Condition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ConditionProperty.SpellDCMinusSave` so rules can gate cast-style actions on the *current* margin between the caster's spell DC and the target's matching save, auto-scaling as characters level up.

**Architecture:** One new enum value. One new helper on `UnitExtensions` (`GetSave`). Two new rule-scoped statics on `ConditionEvaluator` (`CurrentAction`, `CurrentOwner`) set in `Evaluate` via try/finally — matches the existing `LastMatched{Enemy,Ally}` pattern. Property evaluation uses `AbilityData.CalculateParams().DC` + `AbilityEffectRunAction.SavingThrowType` to auto-derive margin. UI exposes the property in Enemy-based subject dropdowns and wires the numeric-operator path in the count-subject render.

**Tech Stack:** C# / .NET 4.8.1 / Unity UGUI / Kingmaker game API

**Build command:** `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

**Deploy command:** `./deploy.sh`

**Spec:** `docs/superpowers/specs/2026-04-19-spell-dc-vs-save-design.md`

**Batch target:** 0.8.0 (already unreleased; HD + point-target code sit on master). No separate version bump — this feature rolls into the same 0.8.0 cycle. Deploy task is shared with the existing 0.8.0 smoke-test plan.

**Testing model:** No unit-test harness. Each task ends with a compile; Task 4 deploys; manual in-game verification happens during the combined 0.8.0 smoke test.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `WrathTactics/Models/Enums.cs` | Modify | Append `SpellDCMinusSave` to `ConditionProperty` (index 16) |
| `WrathTactics/Engine/UnitExtensions.cs` | Modify | Add `GetSave(unit, SavingThrowType)` helper next to existing `GetHD` |
| `WrathTactics/Engine/ConditionEvaluator.cs` | Modify | Add `CurrentAction` / `CurrentOwner` rule-scoped statics set in `Evaluate`; add `ComputeDCMinusSave` helper; add `SpellDCMinusSave` case in `EvaluateUnitProperty` and `MatchesPropertyThreshold` |
| `WrathTactics/UI/ConditionRowWidget.cs` | Modify | Add `SpellDCMinusSave` to Enemy-subject and `EnemyCount` property lists in `GetPropertiesForSubject`; include it in `propNeedsOperator` for the count-subject path |

---

## Task 1: Append enum value + GetSave helper

**Files:**
- Modify: `WrathTactics/Models/Enums.cs`
- Modify: `WrathTactics/Engine/UnitExtensions.cs`

- [ ] **Step 1: Append `SpellDCMinusSave` to `ConditionProperty`**

In `WrathTactics/Models/Enums.cs`, extend the `ConditionProperty` enum so the tail reads:

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
        IsInCombat,
        HitDice,
        SpellDCMinusSave  // NEW, index 16 — margin between caster's DC and target's matching save
    }
```

- [ ] **Step 2: Add `GetSave` to `UnitExtensions`**

In `WrathTactics/Engine/UnitExtensions.cs`, add a `using` for the save-type enum plus the helper. The full file body should read:

```csharp
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;

namespace WrathTactics.Engine {
    public static class UnitExtensions {
        // HD matches the game's own ContextConditionHitDice check (IL-verified):
        // reads UnitProgressionData.CharacterLevel. Racial HD is already folded
        // into CharacterLevel for monsters. Mythic levels are NOT included,
        // matching vanilla HD-gated spells (Sleep, Color Spray, Hold Person).
        public static int GetHD(UnitEntityData unit) {
            var progression = unit?.Descriptor?.Progression;
            return progression?.CharacterLevel ?? 0;
        }

        // Looks up the target's modified save for the given save type. Returns 0
        // for SavingThrowType.Unknown — callers must pre-check the enum so "no
        // save" doesn't silently compare against 0.
        public static int GetSave(UnitEntityData unit, SavingThrowType type) {
            if (unit == null) return 0;
            switch (type) {
                case SavingThrowType.Fortitude: return unit.Stats.SaveFortitude.ModifiedValue;
                case SavingThrowType.Reflex:    return unit.Stats.SaveReflex.ModifiedValue;
                case SavingThrowType.Will:      return unit.Stats.SaveWill.ModifiedValue;
                default:                        return 0;
            }
        }
    }
}
```

- [ ] **Step 3: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Models/Enums.cs WrathTactics/Engine/UnitExtensions.cs
git commit -m "$(cat <<'EOF'
feat(models): append SpellDCMinusSave; add UnitExtensions.GetSave

Additive: ConditionProperty.SpellDCMinusSave at index 16 — existing 0.8.0
configs load unchanged.

GetSave(unit, SavingThrowType) maps the IL-verified
Kingmaker.EntitySystem.Stats.SavingThrowType enum (Unknown/Fortitude/Reflex/
Will) onto the target's ModifiedValue save stats. Wired in later commits.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Wire rule-scoped statics + ComputeDCMinusSave into ConditionEvaluator

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

- [ ] **Step 1: Add rule-scoped statics and `try/finally` to `Evaluate`**

In `WrathTactics/Engine/ConditionEvaluator.cs`, add two private statics near the top of the class (just after the existing `LastMatchedEnemy` / `LastMatchedAlly` properties and their comment):

```csharp
        // Rule-scoped ambient state — set in Evaluate(rule, owner), cleared in finally.
        // Accessed by SpellDCMinusSave evaluation so the property helper stays one-arg
        // (matches HpPercent/AC shape). A stray access outside an active Evaluate
        // reads null and falls through to float.NaN → condition returns false.
        static ActionDef CurrentAction;
        static UnitEntityData CurrentOwner;
```

Then replace the existing `Evaluate` method with a try/finally that populates and clears them:

```csharp
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
            }
        }
```

- [ ] **Step 2: Add `ComputeDCMinusSave` helper**

Still in `ConditionEvaluator.cs`, add the helper immediately after the existing `UnitWill` / `UnitHD` metric helpers (around line 122 — after the `UnitHD` method closes, before `EvaluateAlly`):

```csharp
        // Returns (currentSpellDC − target's matching save). Returns float.NaN for
        // any disqualifying condition (non-cast action, unresolvable ability, spell
        // with no save). Callers must check IsNaN before comparing.
        static float ComputeDCMinusSave(UnitEntityData target) {
            if (target == null || CurrentOwner == null || CurrentAction == null) return float.NaN;
            if (CurrentAction.Type != ActionType.CastSpell && CurrentAction.Type != ActionType.CastAbility)
                return float.NaN;

            var ability = ActionValidator.FindAbility(CurrentOwner, CurrentAction.AbilityId);
            if (ability == null) return float.NaN;

            var runAction = ability.Blueprint
                .GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>();
            var saveType = runAction?.SavingThrowType
                ?? Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown;
            if (saveType == Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown) return float.NaN;

            int dc = ability.CalculateParams().DC;
            int save = UnitExtensions.GetSave(target, saveType);
            return dc - save;
        }
```

- [ ] **Step 3: Add `SpellDCMinusSave` case to `EvaluateUnitProperty`**

Still in `ConditionEvaluator.cs`, in the `EvaluateUnitProperty` method's switch, add a new case just before `case ConditionProperty.IsDead:` (the `HitDice` case from 0.7.0 lives immediately above it; place `SpellDCMinusSave` right after `HitDice` so the numeric cases stay grouped):

```csharp
                case ConditionProperty.HitDice:
                    return CompareFloat(UnitExtensions.GetHD(unit), condition.Operator, threshold);

                case ConditionProperty.SpellDCMinusSave: {
                    float margin = ComputeDCMinusSave(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }

                case ConditionProperty.IsDead:
```

- [ ] **Step 4: Add `SpellDCMinusSave` case to `MatchesPropertyThreshold`**

Still in `ConditionEvaluator.cs`, add the matching case in `MatchesPropertyThreshold` — place it immediately after the existing `HitDice` case (pattern identical to the `HitDice` case's shape):

```csharp
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

                case ConditionProperty.IsDead:
```

- [ ] **Step 5: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "$(cat <<'EOF'
feat(engine): SpellDCMinusSave condition evaluation

Adds CurrentAction / CurrentOwner rule-scoped statics (set in Evaluate,
cleared in finally) so property helpers can resolve the running rule's
cast ability without threading a new parameter through five call sites —
matches the existing LastMatched{Enemy,Ally} side-channel pattern.

ComputeDCMinusSave derives the margin = AbilityData.CalculateParams().DC
- UnitExtensions.GetSave(target, runAction.SavingThrowType). Returns
float.NaN (→ condition false) for non-cast actions, unresolvable
abilities, and spells whose AbilityEffectRunAction.SavingThrowType is
Unknown (e.g. Magic Missile).

SpellDCMinusSave cases wired into both EvaluateUnitProperty (single-
subject path) and MatchesPropertyThreshold (count-subject path),
mirroring HitDice placement.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Expose SpellDCMinusSave in UI dropdowns

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs`

- [ ] **Step 1: Add `SpellDCMinusSave` to the Enemy-subject property list**

In `WrathTactics/UI/ConditionRowWidget.cs`, find `GetPropertiesForSubject`. In the big `case ConditionSubject.Enemy: ... case ConditionSubject.EnemyLowestHD:` block, add `ConditionProperty.SpellDCMinusSave` to the returned list:

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
                        ConditionProperty.SpellDCMinusSave
                    };
```

- [ ] **Step 2: Add `SpellDCMinusSave` to the `EnemyCount` property list**

Still in `GetPropertiesForSubject`, update the `EnemyCount` return list:

```csharp
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave
                    };
```

- [ ] **Step 3: Include `SpellDCMinusSave` in the count-subject operator path**

Still in `ConditionRowWidget.cs`, find the `propNeedsOperator` check in the count-subject render branch (currently around line 106-108 — already extended for `HitDice` in 0.7.0):

```csharp
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.AC
                    || condition.Property == ConditionProperty.HitDice;
```

Extend to:

```csharp
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.AC
                    || condition.Property == ConditionProperty.HitDice
                    || condition.Property == ConditionProperty.SpellDCMinusSave;
```

This makes `EnemyCount` with `SpellDCMinusSave` show the `< > = != ≥ ≤` operator dropdown plus a numeric value input, matching the Sleep/Confusion-style AoE-gating use case.

- [ ] **Step 4: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "$(cat <<'EOF'
feat(ui): expose SpellDCMinusSave in condition dropdowns

GetPropertiesForSubject offers SpellDCMinusSave on all Enemy-based
subjects (Enemy / EnemyBiggest/LowestThreat / EnemyHighest/LowestHp /
AC / Saves / HD) and on EnemyCount. Count-subject render path treats
it like HpPercent/AC/HitDice: operator selector + numeric value.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Deploy to Steam Deck

**Files:** none (deployment only)

The 0.8.0 smoke test already has six tests from HD + point-target; this task adds the DC-vs-save scenarios to that combined list. No separate version bump — `Info.json` and `csproj` already say 0.8.0 from the earlier point-target release commit.

Pre-req: `deck-direct` reachable. If `./deploy.sh` fails with `ssh: Connection refused`, surface to the user — deck is offline.

- [ ] **Step 1: Deploy**

Run: `./deploy.sh`

Expected: `Deployed to Steam Deck.` at the end.

- [ ] **Step 2: In-game smoke test — happy path scaling**

1. Launch the game, load a save with a caster who has a save-gated CC spell (Hold Person is the canonical test — Will save).
2. Open Tactics panel (Ctrl+T), select the caster.
3. Create a new rule:
   - Condition: `Subject=EnemyNearest, Property=SpellDCMinusSave, Operator=≥, Value=3`.
   - Action: `CastSpell`, pick `Hold Person` (or any save-gated enchantment).
   - Target: `ConditionTarget`.
4. Enter combat with a mix of high-Will and low-Will humanoids.
5. Verify the caster only Hold-Persons enemies where `(caster's HP DC) − (enemy's Will) ≥ 3`. Log line `EXECUTED -> <name>` should only appear against the lower-Will targets.
6. If available, repeat on a higher-level save from the same campaign or level up the caster via a trainer — confirm the same rule still works unmodified at the new DC/save scale.

- [ ] **Step 3: No-save spell rejected**

1. Duplicate the rule from Step 2; change `Action=CastSpell(Magic Missile)` (or any spell with no save).
2. Enter combat. Verify the rule NEVER fires. Log should show `ComputeDCMinusSave` returning NaN → property false (you won't see explicit NaN in logs, but the absence of an `EXECUTED` entry for this rule confirms).

- [ ] **Step 4: Non-cast action rejected**

1. New rule: `Subject=EnemyNearest, Property=SpellDCMinusSave, Op=≥, Value=0; Action=AttackTarget; Target=EnemyNearest`.
2. Rule must NOT fire — `CurrentAction.Type != CastSpell/CastAbility` gate.

- [ ] **Step 5: Save-type auto-picks correctly**

1. Create three rules on the same caster, each triggering a different-save spell:
   - Rule A: Hold Person (Will) — condition `SpellDCMinusSave ≥ 5`.
   - Rule B: Grease or other Reflex-save spell — same condition.
   - Rule C: a Fortitude-save spell (Slow, Waves of Fatigue, etc.) — same condition.
2. Encounter with enemies that have a clear split in Fort/Ref/Will saves (a priest with high Will but low Reflex is ideal).
3. Verify each rule picks targets where the RELEVANT save is low enough. Rule A skips priest (high Will), Rule B fires on priest (low Reflex).

- [ ] **Step 6: EnemyCount subject**

1. Rule: `Subject=EnemyCount, Property=SpellDCMinusSave, Operator=≥, Value=3, Value2=2; Action=CastSpell(Confusion); Target=ConditionTarget`.
2. Enter a 4+ enemy encounter where exactly two enemies have low-enough Will.
3. Rule should fire when ≥2 enemies satisfy the margin. In a room with only one weak-Will target, rule should NOT fire.

- [ ] **Step 7: Regression**

All existing rules (static `SaveFortitude ≤ 20`, HD-gated, HP-gated, point-target summons, unit-target attacks, heals) continue to fire as before. The `CurrentAction`/`CurrentOwner` statics are ambient — no existing property touches them, so zero behavior drift for non-`SpellDCMinusSave` rules.

- [ ] **Step 8: Record verdict**

On full pass, the 0.8.0 feature set (HD + point-target + DC-vs-save) is ready for release whenever the user chooses. On any failure, stop and diagnose before pushing/releasing.

---

## Done Criteria

- All 4 tasks' checkboxes ticked.
- Smoke tests 2–7 pass on the deck.
- No regression in existing rules (static saves, HD, point-target, AC/HP/etc.).
- No new warning or error logs appear on load for unchanged 0.6.3 or mid-0.8.0 configs.
