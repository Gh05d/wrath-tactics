# Targeting-Relation Conditions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add four Yes/No `ConditionProperty` values (`IsTargetingSelf`, `IsTargetingAlly`, `IsTargetedByAlly`, `IsTargetedByEnemy`) backed by a `TargetingRelations` engine helper, so DAO-style coordination rules ("tank protects backline", "ranged DPS focuses tank's target", "cleric heals attacked ally") can be expressed without new TargetTypes.

**Architecture:** Single new helper class encapsulates the "X is hostile-targeting Y" detection (combines `UnitCommand.TargetUnit` for active intent with `UnitCombatState.EngagedUnits` for melee-engagement state). Four switch cases in `ConditionEvaluator.EvaluateUnitProperty` reuse it via a small `EqualsBool` helper. UI surfaces the four values via dropdown filter and `PropertyLabel` cases. Persistence is forward-compat — enum indices appended at tail.

**Tech Stack:** C# (.NET Framework 4.8.1), Unity UI, HarmonyLib, UMM. Build via `~/.dotnet/dotnet build … -p:SolutionDir=$(pwd)/`. Deploy via `./deploy.sh`. **No unit-test infrastructure** — verification is compile + manual smoke on Steam Deck.

**Spec reference:** `docs/superpowers/specs/2026-04-27-targeting-relations-design.md`

---

## File Structure

- **Create** `WrathTactics/Engine/TargetingRelations.cs` — single static helper class, one public method `Has(attacker, victim)`.
- **Modify** `WrathTactics/Models/Enums.cs` — append four entries to `ConditionProperty` after `ABMinusAC`.
- **Modify** `WrathTactics/Engine/ConditionEvaluator.cs` — add `EqualsBool` helper, four cases in `EvaluateUnitProperty`, optionally migrate the existing `IsDead` case to use the helper.
- **Modify** `WrathTactics/UI/ConditionRowWidget.cs` — add four cases to `PropertyLabel`, add the three Enemy-scope props to all Enemy-subject lists in `GetPropertiesForSubject`, add `IsTargetedByEnemy` to the `Ally` and `AllyCount` lists. The Yes/No operator/value rendering is already covered by the `isIsDead` code path (line ~202) — extend that boolean-property check to the new properties.
- **Modify** `CLAUDE.md` (wrath-tactics) — add a Game-API gotcha entry documenting `UnitCommand.TargetUnit` + `UnitCombatState.EngagedUnits` as the targeting-relation primitives, and note the approach-phase blind spot.

No changes to Persistence/, Models/ (other than Enums.cs), Compatibility/, or Logging/.

---

## Task 1: Create `TargetingRelations` helper

**Files:**
- Create: `WrathTactics/Engine/TargetingRelations.cs`

- [ ] **Step 1: Create the helper class**

Create `WrathTactics/Engine/TargetingRelations.cs`:

```csharp
using Kingmaker.EntitySystem.Entities;

namespace WrathTactics.Engine {
    /// <summary>
    /// Detects active hostile-targeting relations between two units.
    /// Used by the IsTargeting* / IsTargetedBy* condition properties.
    ///
    /// Combines two engine signals because each alone misses cases:
    ///   - Commands.Standard.TargetUnit catches casters / archers / movers
    ///     whose active command points at the victim.
    ///   - CombatState.EngagedUnits catches melee-locked pairs in between
    ///     attack-frames where the active command is briefly not a UnitAttack.
    ///
    /// Approach-phase units (running toward but not yet swinging or engaged)
    /// match neither and are intentionally out of scope — see spec.
    /// </summary>
    internal static class TargetingRelations {
        public static bool Has(UnitEntityData attacker, UnitEntityData victim) {
            if (attacker == null || victim == null || attacker == victim)
                return false;

            var cmdTarget = attacker.Commands?.Standard?.TargetUnit;
            if (cmdTarget == victim) return true;

            var engaged = attacker.CombatState?.EngagedUnits;
            if (engaged != null && engaged.ContainsKey(victim)) return true;

            return false;
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo`
Expected: `Build succeeded` with 0 errors. Warnings about `findstr` are pre-existing and harmless.

If this fails with "type or namespace `UnitCommands`/`UnitCombatState` not found": the `Kingmaker.UnitLogic.Commands.UnitCommands` and `Kingmaker.Controllers.Combat.UnitCombatState` types are accessed via the `Commands` and `CombatState` properties on `UnitEntityData` and don't need a `using` directive — only `Kingmaker.EntitySystem.Entities` is needed. Re-check the using.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/TargetingRelations.cs
git commit -m "feat(engine): TargetingRelations helper for hostile-target detection"
```

---

## Task 2: Add four enum values

**Files:**
- Modify: `WrathTactics/Models/Enums.cs`

- [ ] **Step 1: Append four entries**

In `WrathTactics/Models/Enums.cs`, find the `ConditionProperty` enum. It currently ends:

```csharp
public enum ConditionProperty {
    // ... existing entries ...
    HasClass,
    WithinRange,
    ABMinusAC   // partyBestAB - enemy.AC — Enemy-scope only
}
```

Replace the closing block with:

```csharp
public enum ConditionProperty {
    // ... existing entries ...
    HasClass,
    WithinRange,
    ABMinusAC,           // partyBestAB - enemy.AC — Enemy-scope only
    IsTargetingSelf,     // Enemy-scope: this enemy targets the rule owner
    IsTargetingAlly,     // Enemy-scope: this enemy targets a non-owner ally
    IsTargetedByAlly,    // Enemy-scope: a non-owner ally targets this enemy
    IsTargetedByEnemy    // Ally-scope: an enemy targets this ally
}
```

Trailing comma after `ABMinusAC` is required because it's no longer the last entry.

- [ ] **Step 2: Verify compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo`
Expected: `Build succeeded`. The new values are unreferenced yet, no warnings expected.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/Enums.cs
git commit -m "feat(model): IsTargeting/IsTargetedBy ConditionProperty values"
```

---

## Task 3: Add `EqualsBool` helper + `IsTargetingSelf` evaluator case

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

- [ ] **Step 1: Add `EqualsBool` helper near `ParseBoolValue` (around line 462)**

In `WrathTactics/Engine/ConditionEvaluator.cs`, locate `static bool ParseBoolValue(string raw)` (line ~462). Immediately after that method's closing brace, add:

```csharp
        // Compares an actual boolean against the condition's Yes/No value, honoring the
        // Equal/NotEqual operator. Used by all bool-valued properties (IsDead, IsInCombat,
        // IsTargetingSelf/Ally, IsTargetedBy*).
        static bool EqualsBool(bool actual, Condition c) {
            bool wanted = ParseBoolValue(c.Value);
            bool match = actual == wanted;
            return c.Operator == ConditionOperator.NotEqual ? !match : match;
        }
```

- [ ] **Step 2: Add `IsTargetingSelf` case to `EvaluateUnitProperty`**

Locate `EvaluateUnitProperty` (line ~481) and find the existing `case ConditionProperty.ABMinusAC` block (line ~515). Immediately after that block's closing brace, insert:

```csharp
                case ConditionProperty.IsTargetingSelf: {
                    if (!IsEnemyScope(condition.Subject)) {
                        Log.Engine.Trace($"IsTargetingSelf: subject {condition.Subject} not Enemy-scope, returning false");
                        return false;
                    }
                    bool match = TargetingRelations.Has(unit, CurrentOwner);
                    Log.Engine.Trace($"IsTargetingSelf: {unit?.CharacterName} targets {CurrentOwner?.CharacterName}? {match}");
                    return EqualsBool(match, condition);
                }
```

- [ ] **Step 3: Verify compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo`
Expected: `Build succeeded`. No errors. The other three new enum values trigger no warnings since C#'s switch is non-exhaustive on enums.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine): EqualsBool helper + IsTargetingSelf evaluator case"
```

---

## Task 4: Add `IsTargetingAlly` and `IsTargetedByAlly` evaluator cases

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

- [ ] **Step 1: Add both cases after `IsTargetingSelf`**

In `WrathTactics/Engine/ConditionEvaluator.cs`, after the `IsTargetingSelf` case added in Task 3, insert:

```csharp
                case ConditionProperty.IsTargetingAlly: {
                    if (!IsEnemyScope(condition.Subject)) {
                        Log.Engine.Trace($"IsTargetingAlly: subject {condition.Subject} not Enemy-scope, returning false");
                        return false;
                    }
                    bool match = false;
                    foreach (var ally in GetAllPartyMembers(CurrentOwner)) {
                        if (ally == null || ally == CurrentOwner) continue;
                        if (!ally.IsInGame) continue;
                        if (ally.Descriptor?.State?.IsFinallyDead ?? false) continue;
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
                        Log.Engine.Trace($"IsTargetedByAlly: subject {condition.Subject} not Enemy-scope, returning false");
                        return false;
                    }
                    bool match = false;
                    foreach (var ally in GetAllPartyMembers(CurrentOwner)) {
                        if (ally == null || ally == CurrentOwner) continue;
                        if (!ally.IsInGame) continue;
                        if (ally.Descriptor?.State?.IsFinallyDead ?? false) continue;
                        if (TargetingRelations.Has(ally, unit)) {
                            Log.Engine.Trace($"IsTargetedByAlly: {ally.CharacterName} targets {unit?.CharacterName}");
                            match = true;
                            break;
                        }
                    }
                    return EqualsBool(match, condition);
                }
```

The `ally != CurrentOwner` filter is load-bearing for `IsTargetedByAlly`: without it, a Ranger with rule "attack any enemy targeted by an ally" would self-trigger off his own previous attack.

- [ ] **Step 2: Verify compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine): IsTargetingAlly + IsTargetedByAlly evaluator cases"
```

---

## Task 5: Add `IsTargetedByEnemy` evaluator case

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

- [ ] **Step 1: Add the case after `IsTargetedByAlly`**

In `WrathTactics/Engine/ConditionEvaluator.cs`, after the `IsTargetedByAlly` case added in Task 4, insert:

```csharp
                case ConditionProperty.IsTargetedByEnemy: {
                    if (!IsAllyScope(condition.Subject)) {
                        Log.Engine.Trace($"IsTargetedByEnemy: subject {condition.Subject} not Ally-scope, returning false");
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
```

`GetVisibleEnemies` already filters to combat-active hostiles (see `wrath-tactics/CLAUDE.md` enemy-filter-consistency note).

- [ ] **Step 2: Verify compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine): IsTargetedByEnemy evaluator case (Ally-scope)"
```

---

## Task 6: UI integration — `PropertyLabel` and `GetPropertiesForSubject`

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs`

- [ ] **Step 1: Add `PropertyLabel` cases**

In `WrathTactics/UI/ConditionRowWidget.cs`, locate `PropertyLabel` (line ~321):

```csharp
        static string PropertyLabel(ConditionProperty property) {
            switch (property) {
                case ConditionProperty.SpellDCMinusSave: return "DC − Save";
                case ConditionProperty.ABMinusAC:        return "AB − AC";
                default:                                 return property.ToString();
            }
        }
```

Replace with:

```csharp
        static string PropertyLabel(ConditionProperty property) {
            switch (property) {
                case ConditionProperty.SpellDCMinusSave:    return "DC − Save";
                case ConditionProperty.ABMinusAC:           return "AB − AC";
                case ConditionProperty.IsTargetingSelf:     return "Targeting me";
                case ConditionProperty.IsTargetingAlly:     return "Targeting ally";
                case ConditionProperty.IsTargetedByAlly:    return "Targeted by ally";
                case ConditionProperty.IsTargetedByEnemy:   return "Targeted by enemy";
                default:                                    return property.ToString();
            }
        }
```

- [ ] **Step 2: Add the three Enemy-scope props to the Enemy subject lists**

Locate `GetPropertiesForSubject` (line ~401). Find the Enemy + EnemyBiggestThreat + … combined case (line ~427–453) returning the list ending with `WithinRange`. Replace its body so the list now appends the three Enemy-scope new properties:

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
                        ConditionProperty.ABMinusAC,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsTargetingSelf,
                        ConditionProperty.IsTargetingAlly,
                        ConditionProperty.IsTargetedByAlly
                    };
```

`EnemyCount` (line ~454) is **not** modified — the targeting properties are per-unit and don't make sense in the count-aggregate path.

- [ ] **Step 3: Add `IsTargetedByEnemy` to the Ally subject list**

In the same `GetPropertiesForSubject`, locate the `case ConditionSubject.Ally:` branch (line ~411). Replace its body:

```csharp
                case ConditionSubject.Ally:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsTargetedByEnemy
                    };
```

`AllyCount` is **not** modified for the same reason as `EnemyCount`.

- [ ] **Step 4: Wire boolean operator/value dropdowns**

The widget has a flag `isIsDead` (line ~202) that gates the Yes/No operator/value rendering. Locate it:

```csharp
                bool isIsDead = condition.Property == ConditionProperty.IsDead;
```

Rename the variable and broaden the check to cover all four new bool properties + the existing `IsDead` and `IsInCombat`:

```csharp
                bool isBoolProperty = condition.Property == ConditionProperty.IsDead
                    || condition.Property == ConditionProperty.IsInCombat
                    || condition.Property == ConditionProperty.IsTargetingSelf
                    || condition.Property == ConditionProperty.IsTargetingAlly
                    || condition.Property == ConditionProperty.IsTargetedByAlly
                    || condition.Property == ConditionProperty.IsTargetedByEnemy;
```

Then find every reference to `isIsDead` further down in the file (use `grep -n isIsDead WrathTactics/UI/ConditionRowWidget.cs`) and rename each to `isBoolProperty`.

If the existing code uses `isInCombat` as a separate flag for the same purpose, consolidate the two into `isBoolProperty` and remove the redundant `isInCombat` checks. Read lines ~195–305 carefully — the boolean-property rendering branch may already cover `IsInCombat` via the `isInCombat` flag, in which case both flags merge into `isBoolProperty`.

**Verification subscript:** after the rename, the file should have zero references to `isIsDead`. Run `grep -c isIsDead WrathTactics/UI/ConditionRowWidget.cs` — expected: `0`.

- [ ] **Step 5: Verify compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo`
Expected: `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): targeting-relation conditions in property dropdown"
```

---

## Task 7: CLAUDE.md gotcha entry

**Files:**
- Modify: `CLAUDE.md` (wrath-tactics root)

- [ ] **Step 1: Add the API gotcha**

In `wrath-tactics/CLAUDE.md`, find the `## Game API Gotchas` section. Append a new bullet entry near the related combat/AI bullets (after the `Two IsDead cases` and `ABMinusAC condition` entries):

```markdown
- **Targeting-relation primitives**: `unit.Commands.Standard?.TargetUnit` (engine-authoritative current command target — works for `UnitAttack`, `UnitUseAbility`, anything deriving from `UnitCommand`) plus `unit.CombatState.EngagedUnits` (returns `Dictionary<UnitEntityData, TimeSpan>.KeyCollection`, NOT a Dictionary — query via LINQ `.Contains(victim)` since `KeyCollection` has no `ContainsKey`; backing `m_EngagedUnits` is publicizer-accessible if O(1) `ContainsKey` is needed) are the two signals behind `IsTargetingSelf` / `IsTargetingAlly` / `IsTargetedByAlly` / `IsTargetedByEnemy`. Centralized in `Engine/TargetingRelations.Has(attacker, victim)`. Approach-phase units (running toward a target but not yet swinging or melee-locked) match neither — accepted blind spot, latency until first attack-frame is ≤1 tick interval. AI-plan inspection would close the gap but is fragile and out of scope.
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude): targeting-relation API primitives + approach-phase blind spot"
```

---

## Task 8: Smoke test on Steam Deck

**Files:** none modified. This task verifies the integrated behavior.

- [ ] **Step 1: Deploy to Steam Deck**

```bash
./deploy.sh
```

Expected: build succeeds, DLL + Info.json scp'd to deck. If the deck is offline, skip this task and document in the release PR that smoke is pending.

- [ ] **Step 2: Verify property dropdown surfaces**

In-game (Ctrl+T → Tactics panel → New Rule):
1. Set Subject to `Enemy` (or any single-enemy variant). The Property dropdown should now list `Targeting me`, `Targeting ally`, `Targeted by ally` near the bottom.
2. Set Subject to `Ally`. Property dropdown should list `Targeted by enemy`.
3. Set Subject to `EnemyCount` or `AllyCount` — the new properties should NOT appear (count-subjects exclude them).

If a label shows the raw enum identifier (e.g. `IsTargetingSelf`), the `PropertyLabel` switch is missing a case — re-check Task 6 step 1.

- [ ] **Step 3: Verify "tank protects backline" rule**

Build a rule on the Paladin / frontline character:
- Priority: 60
- Active in combat
- Group: `Enemy.IsTargetingAlly = Yes`
- Action: `AttackTarget`, Target: `ConditionTarget`

Walk into a fight where two melee enemies engage Camellia in the back row. The Paladin should switch off whatever he was hitting and attack one of the enemies engaging Camellia within ≤1 tick interval. Check the session log:

```bash
ssh deck-direct "ls -t '<game>/Mods/WrathTactics/Logs/' | head -1"
```

Look for `IsTargetingAlly: <enemy> targets <ally>` trace lines.

- [ ] **Step 4: Verify "ranger focuses tank" rule**

Build a rule on the Ranger:
- Priority: 50 (below "kill weakest" if any)
- Active in combat
- Group: `Enemy.IsTargetedByAlly = Yes`
- Action: `AttackTarget`, Target: `ConditionTarget`

Engage a mob. Once the Paladin starts attacking one enemy, the Ranger should fire at that same enemy on the next tick. Trace log: `IsTargetedByAlly: <ally> targets <enemy>`.

**Self-loop check:** Watch the Ranger across a ~10-second combat. If the Ranger were self-loop-anchoring on his own target, he'd never switch even after the Paladin re-engages. He should follow the Paladin's target changes. If he doesn't, the `ally != CurrentOwner` filter is wrong — re-check Task 4.

- [ ] **Step 5: Verify "cleric heals attacked ally" rule**

Build a rule on the Cleric:
- Priority: 80 (high, defensive)
- Active in combat
- Group: `Ally.IsTargetedByEnemy = Yes AND HpPercent < 60`
- Action: `Heal`, Target: `ConditionTarget`

Trigger a fight where one ally is attacked AND has been damaged. The cleric should heal that specific ally, not just any low-HP companion. Trace log: `IsTargetedByEnemy: <enemy> targets <ally>`.

- [ ] **Step 6: Verify negation works**

Add a temporary rule with `Enemy.IsTargetingSelf = No` AND another condition that always matches. The rule should fire when no enemy targets the rule owner, and stop firing the moment one does. Quick verification of the `!=` ↔ `=` flip.

- [ ] **Step 7: Verify approach-phase miss is acceptable**

Spawn a fight where enemies start at distance and run toward the party. During the run-in (no swings yet), the "tank protects backline" rule should NOT match. Once enemies arrive and swing, it triggers. This is the documented blind spot — verify it visually. Latency should be ≤1 attack-frame, not multi-second.

- [ ] **Step 8: Verify out-of-combat is benign**

Open the Tactics panel out of combat, scroll through rules using the new properties — no errors, no log spam, dropdowns render normally.

- [ ] **Step 9: Verify config persistence**

Save the game, exit to main menu, reload. The rules with new properties should load with their conditions intact. Check `<game>/Mods/WrathTactics/UserSettings/tactics-<gameid>.json` — the new ConditionProperty values serialize as numeric indices `20`, `21`, `22`, `23` (post-`ABMinusAC = 19`).

If a rule loads with empty conditions, persistence has been confused — check that no validation pass strips the new properties as "unknown." There shouldn't be one (the only known stripping pass exempts `PresetId`-bearing rules), but verify with a reload cycle.

- [ ] **Step 10: Final commit (if any session-log instrumentation was tweaked during smoke)**

If the smoke phase revealed anything that needed adjustment (extra Trace log granularity, an edge case in the ally filter), commit that here. Otherwise: no commit, mark smoke pass complete.

---

## Self-Review Checklist (run after writing the plan, fix inline)

- [x] **Spec coverage:** Every property and helper from the spec maps to a task. `TargetingRelations.Has` → Task 1. Enum values → Task 2. Four eval cases → Tasks 3, 4, 5. UI → Task 6. CLAUDE.md → Task 7. Smoke → Task 8.
- [x] **Placeholder scan:** No TBDs, TODOs, or "implement appropriate X" patterns. All code blocks complete.
- [x] **Type/method consistency:** `TargetingRelations.Has(attacker, victim)` argument order is consistent across all four eval cases. `EqualsBool(actual, condition)` signature is the same wherever called. `EvaluateUnitProperty` parameter is `(condition, unit)` matching the existing convention.
- [x] **No dangling references:** `IsEnemyScope`, `IsAllyScope`, `GetAllPartyMembers`, `GetVisibleEnemies`, `CurrentOwner`, `Log.Engine.Trace`, `ParseBoolValue` all already exist in `ConditionEvaluator.cs` (verified at lines 830, 854, 883, 887, plus the `CurrentOwner` ambient-static documented in `wrath-tactics/CLAUDE.md`).
