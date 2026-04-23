# ABMinusAC Condition — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ConditionProperty.ABMinusAC` evaluating `partyBestAB - enemy.AC` via the engine's `RuleCalculateAttackBonusWithoutTarget`, usable in Tactics rules to gate expensive buffs/debuffs on the team's actual hit-difficulty.

**Architecture:** New enum value + two static helpers in `ConditionEvaluator` (mirroring the `SpellDCMinusSave` precedent) + dropdown inclusion in `ConditionRowWidget`. No persistence schema change — new enum entry appends at the tail of `ConditionProperty`.

**Tech Stack:** C# (.NET Framework 4.8.1), Unity UI, HarmonyLib, UMM. Build via `~/.dotnet/dotnet build … -p:SolutionDir=$(pwd)/`. Deploy via `./deploy.sh`. **No unit-test infrastructure** — verification is compile + manual smoke test on Steam Deck.

**Spec reference:** `docs/superpowers/specs/2026-04-23-abminusac-condition-design.md`

---

## File Structure

- **Modify** `WrathTactics/Models/Enums.cs` — add `ABMinusAC` to `ConditionProperty`
- **Modify** `WrathTactics/Engine/ConditionEvaluator.cs` — `PartyBestAB`, `ComputeABMinusAC`, evaluator case
- **Modify** `WrathTactics/UI/ConditionRowWidget.cs` — include property in dropdown when Subject is enemy-scope
- **Modify** `CLAUDE.md` — document the new property + the `RuleCalculateAttackBonusWithoutTarget` engine primitive

No new files.

---

## Task 1: Add `ABMinusAC` enum value

**Files:**
- Modify: `WrathTactics/Models/Enums.cs`

- [ ] **Step 1: Append enum value**

Find in `WrathTactics/Models/Enums.cs` the `ConditionProperty` enum. It currently ends:

```csharp
public enum ConditionProperty {
    // ... many entries ...
    HasClass,
    WithinRange
}
```

Replace with:

```csharp
public enum ConditionProperty {
    // ... many entries ...
    HasClass,
    WithinRange,
    ABMinusAC   // partyBestAB - enemy.AC — Enemy-scope only
}
```

Note the trailing comma after `WithinRange` and the comment on the new entry.

- [ ] **Step 2: Compile**
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**
```bash
git add WrathTactics/Models/Enums.cs
git commit -m "feat(model): add ABMinusAC ConditionProperty

Appended to the enum tail so existing tactics-*.json / preset JSON
(which serialize enum by numeric index via Newtonsoft) keep
deserializing correctly."
```

---

## Task 2: Implement evaluator helpers + case

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

- [ ] **Step 1: Verify required using-directives**

Open `WrathTactics/Engine/ConditionEvaluator.cs` and confirm these usings exist at the top. If any are missing, add them in the same block as the existing using-directives:

```csharp
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
```

`Kingmaker.EntitySystem.Entities` (for `UnitEntityData`) and the project's own `WrathTactics.Logging` / `WrathTactics.Models` imports should already be present.

- [ ] **Step 2: Add the two static helpers**

Find the existing `ComputeDCMinusSave(UnitEntityData target)` method (around line 287 — the `SpellDCMinusSave` precedent). Add the following TWO methods immediately after it (before the closing brace of the class / before the next method):

```csharp
        // Engine-authoritative best attack bonus across all living party members.
        // RuleCalculateAttackBonusWithoutTarget returns the same AB the game uses at
        // attack time minus target-side modifiers (flanking, bane, etc.) — it includes
        // BAB, stat mod (correctly picked by weapon type), weapon enhancement, feats,
        // and active buffs. NaN on empty / fully-dead party.
        static float PartyBestAB(UnitEntityData owner) {
            int best = int.MinValue;
            foreach (var ally in GetAllPartyMembers(owner)) {
                if (ally == null || !ally.IsInGame) continue;
                if (ally.Descriptor?.State?.IsFinallyDead ?? false) continue;

                var weapon = ally.Body?.PrimaryHand?.MaybeWeapon
                          ?? ally.Body?.SecondaryHand?.MaybeWeapon
                          ?? ally.Body?.EmptyHandWeapon;
                if (weapon == null) continue;

                var rule = Rulebook.Trigger(new RuleCalculateAttackBonusWithoutTarget(ally, weapon, 0));
                if (rule.Result > best) best = rule.Result;
            }
            return best == int.MinValue ? float.NaN : (float)best;
        }

        // Computes partyBestAB - enemy.AC for the ABMinusAC condition property.
        // Rule-scoped: CurrentOwner is the rule's owning unit (set in Evaluate, cleared in finally).
        // NaN when the party has no eligible attacker or the enemy is null.
        static float ComputeABMinusAC(UnitEntityData enemy) {
            if (enemy == null || CurrentOwner == null) return float.NaN;
            float ab = PartyBestAB(CurrentOwner);
            if (float.IsNaN(ab)) return float.NaN;
            int ac = enemy.Stats.AC.ModifiedValue;
            float margin = ab - ac;
            Log.Engine.Trace($"ABMinusAC: {enemy.CharacterName} AC={ac}, partyBestAB={ab} -> margin={margin}");
            return margin;
        }
```

- [ ] **Step 3: Add the evaluator case**

Find the `EvaluateUnitProperty` method (the big `switch` on `condition.Property` around line 440+). It contains cases like `HpPercent`, `AC`, `SaveFortitude`, `SpellDCMinusSave`, etc.

Locate the `case ConditionProperty.SpellDCMinusSave` block. It looks like:

```csharp
                case ConditionProperty.SpellDCMinusSave: {
                    float margin = ComputeDCMinusSave(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, ParseFloatValue(condition.Value));
                }
```

Immediately after this block (before the next `case` or the `default:` / closing brace), add:

```csharp
                case ConditionProperty.ABMinusAC: {
                    if (!IsEnemyScope(condition.Subject)) {
                        Log.Engine.Trace($"ABMinusAC: subject {condition.Subject} is not Enemy-scope, returning false");
                        return false;
                    }
                    float margin = ComputeABMinusAC(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }
```

`IsEnemyScope(ConditionSubject)` is an existing helper in the file. `CompareFloat` is also existing. `threshold` is a local `float` declared at the top of `EvaluateUnitProperty` via `float.TryParse(condition.Value, ..., out threshold)` — use it directly, same as every other numeric case in the switch.

- [ ] **Step 4: Compile**
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**
```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine): evaluate ABMinusAC via RuleCalculateAttackBonusWithoutTarget

Two helpers (PartyBestAB, ComputeABMinusAC) mirror the
SpellDCMinusSave precedent: read the rule-scoped CurrentOwner static,
return float.NaN when uncomputable, log the margin on Trace level.
Party-Best semantics use the engine's authoritative rule so BAB, stat
mod, weapon enhancement, feats, and active buffs all scale correctly."
```

---

## Task 3: UI — include in dropdown for enemy-scope subjects

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs`

- [ ] **Step 1: Read the existing property-dropdown construction**

Open `WrathTactics/UI/ConditionRowWidget.cs`. Find where the `ConditionProperty` dropdown options are built — grep for `ConditionProperty.` inside the file to locate the dropdown-population code. The method is most likely named `GetAvailableProperties`, `BuildPropertyDropdown`, or similar, and it filters properties based on the currently-selected `Subject`.

There will be two patterns in this file for subject-dependent property visibility:

1. An explicit whitelist per subject category (e.g., `if (IsEnemyScope(subject)) options.Add(ConditionProperty.AC)`).
2. A blanket list with per-property guards.

**Do not restructure — add your entry in the same style as existing entries.**

- [ ] **Step 2: Add `ABMinusAC` to the enemy-scope properties**

Wherever the dropdown gates properties by enemy-scope (look for cases that include `ConditionProperty.AC`, `SaveFortitude`, `SaveReflex`, `SaveWill`, or the existing enemy-relevant properties), add `ConditionProperty.ABMinusAC` to the same list / branch.

If `ConditionProperty.SpellDCMinusSave` is also gated for enemy-scope only in this file, add `ABMinusAC` immediately next to it — same ordering rationale.

Also add a display label. If the file has a `PropertyDisplayName(ConditionProperty)` or similar helper, add the mapping:

```csharp
case ConditionProperty.ABMinusAC: return "AB − AC";
```

Mirrors the existing `"DC − Save"` label for `SpellDCMinusSave` (Unicode minus sign `−`, not ASCII `-` — match the existing convention).

- [ ] **Step 3: Verify the value input is numeric**

Confirm that when `ABMinusAC` is selected, the value input accepts integers (negative and positive). The widget probably dispatches input type by property; `SpellDCMinusSave` is the nearest analogue and should already expose a numeric input. Match its behavior.

If the widget uses a `IsNumericProperty(ConditionProperty)` helper or a switch to pick input widgets, include `ABMinusAC` in the numeric branch alongside `SpellDCMinusSave`, `HpPercent`, `AC`, `SaveFortitude`, etc.

- [ ] **Step 4: Compile**
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**
```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): surface ABMinusAC in the Condition property dropdown

Visible only for enemy-scope subjects (Enemy, EnemyBiggestThreat,
EnemyHighest*, EnemyLowest*). Numeric input widget, same as SpellDCMinusSave.
Label 'AB − AC' matches the 'DC − Save' convention."
```

---

## Task 4: CLAUDE.md documentation

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add the engine-primitive note**

Find the existing "Game API Gotchas" section of `CLAUDE.md`. Look for the block that documents `AbilityData.CanTarget`, `ability.CalculateParams()`, `BlueprintAbility.GetComponent<AbilityEffectRunAction>`, etc.

Add this bullet at a fitting location (near the other rule-based engine primitives):

```markdown
- **`RuleCalculateAttackBonusWithoutTarget(UnitEntityData, ItemEntityWeapon, int penalty)`**: engine-authoritative full-AB computation for a unit with a given weapon, minus target-side factors (no flanking, no bane). Returns `.Result` as int. Includes BAB, stat-mod (correct per weapon type — finesse/ranged uses Dex), weapon enhancement, feats (Weapon Focus etc.), and active buffs (Bless, Prayer, Haste, Inspire Courage). Cheap — no random rolls, no side effects, fires via `Rulebook.Trigger`. Use this instead of manually summing `BaseAttackBonus.ModifiedValue + stat mod` when scale-correctness matters. `RuleCalculateAttackBonus` (with target) adds flanking / bane / target-specific modifiers if needed.
```

- [ ] **Step 2: Document the `ABMinusAC` condition gotcha**

Near the existing condition-system notes (look for the section documenting `IsEnemyScope`, `MatchesPropertyThreshold`, or `SpellDCMinusSave`-related patterns), add:

```markdown
- **`ABMinusAC` condition**: Evaluates `partyBestAB − enemy.AC` via `RuleCalculateAttackBonusWithoutTarget` across all living party members (`!IsFinallyDead`, `IsInGame`, has a weapon / `EmptyHandWeapon`). Enemy-scope-only — Self/Ally/Combat subjects return false. Pair with `AllyCount` in the same ConditionGroup to gate expensive debuffs on "big fight AND team struggles to hit." New `ConditionEvaluator` cases with computed-delta semantics should follow the same pattern: (a) scope-check the subject, (b) read `CurrentOwner` from the rule-scoped static, (c) return `float.NaN` → `false` on uncomputable, (d) add a Trace log so users can debug thresholds from the session log.
```

- [ ] **Step 3: Commit**
```bash
git add CLAUDE.md
git commit -m "docs(claude): ABMinusAC condition + RuleCalculateAttackBonusWithoutTarget

Captures the new condition property and the engine primitive behind
its computation, so future computed-delta conditions reuse the same
pattern instead of re-deriving BAB + stat mod manually."
```

---

## Release

No version bump in this plan. Bundled with the next minor (v1.2.0) — new user-visible feature, no breaking changes.
