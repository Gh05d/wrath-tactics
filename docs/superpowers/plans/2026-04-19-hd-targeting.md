# HD (Hit Dice) Targeting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add HD-based targeting and conditions to Wrath Tactics, mirroring the existing AC/Saves pattern so users can drive HD-gated spells (Sleep, Color Spray, Hold Person, Circle of Death).

**Architecture:** Additive-only. Three enums (`ConditionProperty`, `ConditionSubject`, `TargetType`) get new values APPENDED at the end — numeric JSON indices of existing entries must stay stable. HD read via `unit.Descriptor.Progression.CharacterLevel` (IL-verified against the game's own `ContextConditionHitDice`). One new helper file (`Engine/UnitExtensions.cs`) holds the accessor so future one-liners land there too.

**Tech Stack:** C# / .NET 4.8.1 / Unity UGUI / Kingmaker game API

**Build command:** `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

**Deploy command:** `./deploy.sh`

**Spec:** `docs/superpowers/specs/2026-04-19-hd-targeting-design.md`

**Testing model:** No unit-test harness in this repo. Each phase ends with a compile; Phase 5 is a manual Steam Deck smoke test.

**Spec correction:** The spec mentioned custom dropdown labels (`"Hit Dice"`, `"Enemy (highest HD)"`). The codebase uses raw enum names (`Enum.GetNames(...)`) everywhere else (see `ConditionRowWidget.cs:41`, `RuleEditorWidget.cs:598`). To stay consistent with existing `EnemyHighestHp`, `EnemyLowestAC` display behavior, the dropdowns will show raw names (`HitDice`, `EnemyHighestHD`, `EnemyLowestHD`). No label-mapping utility is added.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `WrathTactics/Models/Enums.cs` | Modify | Append `HitDice` to `ConditionProperty`; append `EnemyHighestHD`/`EnemyLowestHD` to `ConditionSubject` and `TargetType` |
| `WrathTactics/Engine/UnitExtensions.cs` | Create | Static `GetHD(UnitEntityData)` helper |
| `WrathTactics/Engine/ConditionEvaluator.cs` | Modify | Add `HitDice` case in `EvaluateUnitProperty` and `MatchesPropertyThreshold`; add `EnemyHighestHD`/`EnemyLowestHD` cases in the `EvaluateCondition` switch |
| `WrathTactics/Engine/TargetResolver.cs` | Modify | Add `EnemyHighestHD`/`EnemyLowestHD` cases in the `ResolveInternal` switch, plus two helper methods |
| `WrathTactics/UI/ConditionRowWidget.cs` | Modify | Add `HitDice` to property lists for Enemy-subjects in `GetPropertiesForSubject`; include `HitDice` in `propNeedsOperator` for the count-subject branch |
| `WrathTactics/Info.json` | Modify | Version bump to 0.7.0 |
| `WrathTactics/WrathTactics.csproj` | Modify | Version bump to 0.7.0 |

---

## Phase 1: Model — Append Enum Values

### Task 1: Append new enum values to `Models/Enums.cs`

**Files:**
- Modify: `WrathTactics/Models/Enums.cs`

**CRITICAL:** All new values are appended at the END of each enum. DO NOT insert mid-enum. Existing user configs serialize with numeric indices; any insertion shifts downstream indices and corrupts saved rules.

- [ ] **Step 1: Append `HitDice` to `ConditionProperty`**

Modify `WrathTactics/Models/Enums.cs`, replace the `ConditionProperty` enum body so it looks like:

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
    HitDice  // NEW, index 15
}
```

- [ ] **Step 2: Append `EnemyHighestHD` and `EnemyLowestHD` to `ConditionSubject`**

In the same file, `ConditionSubject` becomes:

```csharp
public enum ConditionSubject {
    Self,
    Ally,
    AllyCount,
    Enemy,
    EnemyCount,
    Combat,
    EnemyBiggestThreat,  // the single enemy with highest threat
    EnemyLowestThreat,   // the single enemy with lowest threat
    EnemyHighestHp,      // the single enemy with highest HP%
    EnemyLowestHp,       // the single enemy with lowest HP%
    EnemyLowestAC,       // the single enemy with lowest AC
    EnemyHighestAC,      // the single enemy with highest AC
    EnemyLowestFort,     // the single enemy with lowest Fortitude save
    EnemyHighestFort,    // the single enemy with highest Fortitude save
    EnemyLowestReflex,   // the single enemy with lowest Reflex save
    EnemyHighestReflex,  // the single enemy with highest Reflex save
    EnemyLowestWill,     // the single enemy with lowest Will save
    EnemyHighestWill,    // the single enemy with highest Will save
    EnemyHighestHD,      // NEW, index 18 — the single enemy with highest HD
    EnemyLowestHD        // NEW, index 19 — the single enemy with lowest HD
}
```

- [ ] **Step 3: Append `EnemyHighestHD` and `EnemyLowestHD` to `TargetType`**

Same file, `TargetType` becomes:

```csharp
public enum TargetType {
    Self,
    AllyLowestHp,
    AllyWithCondition,
    AllyMissingBuff,
    EnemyNearest,
    EnemyLowestHp,
    EnemyHighestHp,
    EnemyHighestAC,
    EnemyLowestAC,
    EnemyHighestFort,
    EnemyLowestFort,
    EnemyHighestReflex,
    EnemyLowestReflex,
    EnemyHighestWill,
    EnemyLowestWill,
    EnemyHighestThreat,
    EnemyCreatureType,
    ConditionTarget,    // the enemy/ally that matched the triggering condition
    EnemyHighestHD,     // NEW, index 18
    EnemyLowestHD       // NEW, index 19
}
```

- [ ] **Step 4: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The new enum members have no consumers yet, so compilation alone verifies.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Models/Enums.cs
git commit -m "$(cat <<'EOF'
feat(models): append HitDice and EnemyHighest/LowestHD enum values

Additive: ConditionProperty.HitDice (15), ConditionSubject.EnemyHighestHD
(18)/EnemyLowestHD (19), TargetType.EnemyHighestHD (18)/EnemyLowestHD (19).
All appended at end — existing JSON indices preserved.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 2: HD Accessor

### Task 2: Create `Engine/UnitExtensions.cs`

**Files:**
- Create: `WrathTactics/Engine/UnitExtensions.cs`

- [ ] **Step 1: Write the file**

Write to `WrathTactics/Engine/UnitExtensions.cs`:

```csharp
using Kingmaker.EntitySystem.Entities;

namespace WrathTactics.Engine {
    /// <summary>
    /// Shared one-line accessors for unit properties. Keep these null-safe so
    /// callers can use them directly in predicates/LINQ without guard clauses.
    /// </summary>
    public static class UnitExtensions {
        /// <summary>
        /// Hit Dice, matching the game's own ContextConditionHitDice check
        /// (IL-verified: reads UnitProgressionData.CharacterLevel). Racial HD
        /// is already folded into CharacterLevel for monsters. Mythic levels
        /// are NOT included, matching vanilla HD-gated spells.
        /// </summary>
        public static int GetHD(UnitEntityData unit) {
            var progression = unit?.Descriptor?.Progression;
            return progression?.CharacterLevel ?? 0;
        }
    }
}
```

- [ ] **Step 2: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/UnitExtensions.cs
git commit -m "$(cat <<'EOF'
feat(engine): add UnitExtensions.GetHD helper

Matches Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionHitDice
(IL-verified): unit.Descriptor.Progression.CharacterLevel. Null-safe,
returns 0 for units without progression data.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 3: Engine — Conditions and Targets

### Task 3: Wire HD into `ConditionEvaluator`

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

- [ ] **Step 1: Add HD metric helper in `ConditionEvaluator`**

In `WrathTactics/Engine/ConditionEvaluator.cs`, just after the existing `UnitWill` metric helper (around line 118 — right after the `UnitWill` method closes), add:

```csharp
        static float UnitHD(UnitEntityData unit) {
            return UnitExtensions.GetHD(unit);
        }
```

This sits alongside `UnitAC`, `UnitFort`, etc. — same function signature (`Func<UnitEntityData, float>`) so it drops straight into `EvaluateEnemyPick`.

- [ ] **Step 2: Add `EnemyHighestHD` / `EnemyLowestHD` subject cases**

In the same file, in the `EvaluateCondition` method's switch (around line 55), add two new cases RIGHT BEFORE the `case ConditionSubject.Combat` line:

```csharp
                    case ConditionSubject.EnemyHighestHD:     return EvaluateEnemyPick(condition, owner, UnitHD, biggest: true);
                    case ConditionSubject.EnemyLowestHD:      return EvaluateEnemyPick(condition, owner, UnitHD, biggest: false);
```

The resulting switch tail around those lines will read:

```csharp
                    case ConditionSubject.EnemyLowestWill:    return EvaluateEnemyPick(condition, owner, UnitWill, biggest: false);
                    case ConditionSubject.EnemyHighestWill:   return EvaluateEnemyPick(condition, owner, UnitWill, biggest: true);
                    case ConditionSubject.EnemyHighestHD:     return EvaluateEnemyPick(condition, owner, UnitHD, biggest: true);
                    case ConditionSubject.EnemyLowestHD:      return EvaluateEnemyPick(condition, owner, UnitHD, biggest: false);
                    case ConditionSubject.Combat:              return EvaluateCombat(condition);
                    default:                                   return false;
```

- [ ] **Step 3: Add `HitDice` case to `EvaluateUnitProperty`**

In the same file, in the `EvaluateUnitProperty` method's switch (around line 225), add a new case just before the `case ConditionProperty.IsDead:` line:

```csharp
                case ConditionProperty.HitDice:
                    return CompareFloat(UnitExtensions.GetHD(unit), condition.Operator, threshold);
```

- [ ] **Step 4: Add `HitDice` case to `MatchesPropertyThreshold`**

Still in `ConditionEvaluator.cs`, in `MatchesPropertyThreshold` (around line 285), add a new case just before the `case ConditionProperty.IsDead:` line:

```csharp
                case ConditionProperty.HitDice:
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    return CompareFloat(UnitExtensions.GetHD(unit), condition.Operator, threshold);
```

This powers `EnemyCount.HitDice ≤ X` — the dominant Sleep-style trigger.

- [ ] **Step 5: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "$(cat <<'EOF'
feat(engine): HitDice condition property and Enemy{Highest,Lowest}HD subjects

- EvaluateUnitProperty and MatchesPropertyThreshold get HitDice branch.
- EvaluateCondition switch gains EnemyHighestHD / EnemyLowestHD cases,
  wired through EvaluateEnemyPick with UnitHD metric.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 4: Wire HD into `TargetResolver`

**Files:**
- Modify: `WrathTactics/Engine/TargetResolver.cs`

- [ ] **Step 1: Add target-picker cases to `ResolveInternal` switch**

In `WrathTactics/Engine/TargetResolver.cs`, in the `ResolveInternal` switch (around line 22), add two new cases just before `case TargetType.EnemyCreatureType:`:

```csharp
                case TargetType.EnemyHighestHD:     return GetEnemyHighestHD(owner);
                case TargetType.EnemyLowestHD:      return GetEnemyLowestHD(owner);
```

- [ ] **Step 2: Add the two helper methods**

In the same file, add two new methods right after `GetEnemyHighestWill` (around line 129, before `GetEnemyHighestThreat`):

```csharp
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
```

- [ ] **Step 3: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/TargetResolver.cs
git commit -m "$(cat <<'EOF'
feat(engine): EnemyHighestHD / EnemyLowestHD target pickers

Mirrors the existing AC/Save target-picker pattern. Uses shared
UnitExtensions.GetHD to keep HD semantics consistent with the
ConditionEvaluator path.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 4: UI — Expose HD in Dropdowns

### Task 5: Enable HitDice in `ConditionRowWidget` property lists and operator path

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs`

The UI enumerates `ConditionSubject` and `TargetType` via `Enum.GetNames` (see `ConditionRowWidget.cs:41` and `RuleEditorWidget.cs:598`), so the two new subjects and target types show up automatically in their dropdowns. No label plumbing is needed for those.

Two adjustments ARE needed:

1. `GetPropertiesForSubject` filters which properties are offered per subject. Currently `HitDice` isn't in any of those lists, so the property dropdown won't offer it.
2. The count-subject (AllyCount / EnemyCount) render path (lines 106-107) decides whether to show an operator selector. `HitDice` needs the same numeric-operator treatment as `HpPercent` and `AC`.

- [ ] **Step 1: Add `HitDice` to Enemy-subject property list**

In `WrathTactics/UI/ConditionRowWidget.cs`, find the `GetPropertiesForSubject` method (around line 303). Inside the large `case ConditionSubject.Enemy: ... case ConditionSubject.EnemyHighestWill:` block (lines 324-336), add two more `case` labels for the new HD subjects, and add `ConditionProperty.HitDice` to the returned list:

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
                        ConditionProperty.HitDice
                    };
```

- [ ] **Step 2: Add `HitDice` to `EnemyCount` property list**

Still in `GetPropertiesForSubject`, add `ConditionProperty.HitDice` to the `EnemyCount` return list (around line 344):

```csharp
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice
                    };
```

- [ ] **Step 3: Include `HitDice` in the count-subject operator path**

In the same file, find the `propNeedsOperator` check in the count-subject render path (around line 106-107):

```csharp
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.AC;
```

Replace with:

```csharp
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.AC
                    || condition.Property == ConditionProperty.HitDice;
```

This makes the count path render `HitDice` with the `< > = != >= <=` operator selector plus a numeric value input — matching the Sleep use case `"HitDice ≤ 4, count ≥ 3"`.

- [ ] **Step 4: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "$(cat <<'EOF'
feat(ui): expose HitDice in condition dropdowns

- GetPropertiesForSubject includes HitDice for Enemy-subjects,
  EnemyCount, and the new EnemyHighestHD/LowestHD subjects.
- Count-subject render path treats HitDice like HpPercent/AC:
  operator selector + numeric value input.

TargetType.EnemyHighestHD/LowestHD surface automatically via the
existing Enum.GetNames enumeration in RuleEditorWidget.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 5: Smoke Test and Release

### Task 6: Deploy and smoke-test on Steam Deck

**Files:** none (manual verification)

- [ ] **Step 1: Deploy**

Run: `./deploy.sh`

Expected: `Deployed to Steam Deck.` at the end.

- [ ] **Step 2: In-game smoke test — property path (Sleep-style)**

On the deck:

1. Launch the game, load a save with at least one enemy encounter that spawns multiple low-HD enemies (most early-Act 1 mobs work).
2. Open Tactics panel (Ctrl+T).
3. Select a caster companion. Create a new rule:
   - Condition 1: `Subject=EnemyCount, Property=HitDice, Operator=<=, Value=4, Value2=3`
   - Action: `CastSpell` — pick `Sleep` (if caster has it) or any other spell; for verification purposes any spell confirms the trigger fires.
   - Target: `EnemyLowestHD`
4. Close the panel, trigger combat with 3+ low-HD enemies.
5. Verify in the session log (`<game>/Mods/WrathTactics/Logs/wrath-tactics-<date>.log`) that the rule matched and fired. The log line will show `EXECUTED -> <enemy name>`; cross-check that enemy is the lowest-HD target in the combat.

- [ ] **Step 3: In-game smoke test — single-subject path (Hold Person-style)**

1. Create a second rule on a caster:
   - Condition 1: `Subject=EnemyHighestHD, Property=CreatureType, Operator==, Value=Humanoid`
   - Action: `CastSpell` — Hold Person (or fallback: any spell targeting humanoids)
   - Target: `EnemyHighestHD`
2. Trigger combat against a mix of humanoids at different levels.
3. Verify the rule fires and targets the highest-HD humanoid present (check log + in-game behavior).

- [ ] **Step 4: Regression — existing AC/Save rules unchanged**

1. Verify that any existing rule using `EnemyLowestAC` or `EnemyHighestWill` still fires as before.
2. Load the current per-save config from `UserSettings/tactics-<gameid>.json` and confirm (either via the Tactics UI or by inspecting the file) that no existing rule was dropped during the 0.7.0 load — `"Some rules referenced removed condition properties"` warning must NOT appear in today's session log for previously-valid rules.

- [ ] **Step 5: Record verdict**

If any smoke-test step fails, STOP, diagnose, and fix before proceeding to Task 7. On success, continue.

### Task 7: Version bump + release

**Files:**
- Modify: `WrathTactics/Info.json`
- Modify: `WrathTactics/WrathTactics.csproj`

- [ ] **Step 1: Bump `Info.json`**

In `WrathTactics/Info.json`, change `"Version": "0.6.3"` to `"Version": "0.7.0"`.

Final file body:

```json
{
  "Id": "WrathTactics",
  "DisplayName": "Wrath Tactics",
  "Author": "Gh05d",
  "Version": "0.7.0",
  "ManagerVersion": "0.23.0",
  "GameVersion": "1.4.0",
  "EntryMethod": "WrathTactics.Main.Load",
  "AssemblyName": "WrathTactics.dll",
  "Requirements": [],
  "HomePage": ""
}
```

- [ ] **Step 2: Bump `WrathTactics.csproj`**

In `WrathTactics/WrathTactics.csproj`, change `<Version>0.6.3</Version>` to `<Version>0.7.0</Version>`. Only that single line changes.

- [ ] **Step 3: Release build (produces distribution zip)**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded.` and a `WrathTactics-0.7.0.zip` written under `WrathTactics/bin/`.

- [ ] **Step 4: Commit and tag**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj
git commit -m "$(cat <<'EOF'
chore: release 0.7.0 — HD targeting

Adds ConditionProperty.HitDice, ConditionSubject.EnemyHighestHD/LowestHD,
and TargetType.EnemyHighestHD/LowestHD. HD is read from the engine-canonical
UnitProgressionData.CharacterLevel (IL-verified), so mythic levels are not
included — matches vanilla HD-gated spells (Sleep, Color Spray, Circle of
Death, Hold Person).

All enum changes are append-only: existing 0.6.3 configs load unchanged.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
git tag v0.7.0
```

- [ ] **Step 5: Await user push authorization**

Per prior session policy, the user pushes master branch and tags themselves. Ask:

> "Release 0.7.0 committed + tagged locally. Push auf master + Tag freigeben? (`git push origin master && git push origin v0.7.0`)"

- [ ] **Step 6: Create GitHub release after user confirms tag is on remote**

```bash
gh release create v0.7.0 WrathTactics/bin/WrathTactics-0.7.0.zip --title "v0.7.0 — HD (Hit Dice) Targeting" --notes "$(cat <<'EOF'
## New

- **HitDice** condition property — drive rules off target HD (e.g. "enemies with HD ≤ 4", Sleep-style).
- **EnemyHighestHD / EnemyLowestHD** — new single-enemy condition subjects and target pickers, mirroring the existing AC/Save patterns.

## Use cases

- Sleep / Color Spray: trigger on `EnemyCount.HitDice ≤ 4, count ≥ 3`, target `EnemyLowestHD`.
- Hold Person: target `EnemyHighestHD` for the biggest humanoid caster.
- Circle of Death: gate on `EnemyCount.HitDice ≤ 9, count ≥ 4`.

## Compatibility

All enum values are appended at the end; existing 0.6.3 rules and presets load unchanged. Mythic levels do NOT count toward HD — matches the game's own `ContextConditionHitDice` check and PF RAW.
EOF
)"
```

- [ ] **Step 7: Update this plan — mark complete**

Replace the top of this file's header with a `**Status:** Completed YYYY-MM-DD` line so future readers see it's shipped.

---

## Done Criteria

- All 7 tasks' checkboxes ticked.
- Release `v0.7.0` live on GitHub with the ZIP attached.
- Deck smoke tests passed (Sleep-style trigger, Hold Person-style target, no regression in AC/Save rules).
- No `"Some rules referenced removed condition properties"` warning on load for valid pre-0.7.0 rules.
