# Operator-Aware WithinRange Bracket Labels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WithinRange bracket dropdowns show the effective meter interval for the selected operator (`<` on Short reads "Short (≤ 5 m)", not "( 10 m )") and refresh live on operator change.

**Architecture:** A pure `RangeBrackets.EffectiveHint(bracket, op)` (tested) plus a thin `EffectiveLabel` composing the localized bracket name with that hint; `ConditionRowWidget`'s two WithinRange selector sites consume it, and their operator selectors trigger the existing `Rebuild()` pattern when the property is WithinRange. Spec: `docs/superpowers/specs/2026-07-14-range-labels-design.md`.

**Tech Stack:** C# / .NET Framework 4.8.1, xUnit (mono host), Unity UI.

## Global Constraints

- Build: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/` (SolutionDir REQUIRED on Linux).
- Tests: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/` — first run after a build may crash the mono host (mass-failures with varying counts = flake); loop `--no-build` up to 3× and trust the first all-green run.
- Label text is purely symbolic (`≤ > ≠ – m` + digits) — NO new i18n keys. Numbers format InvariantCulture `"0.#"`.
- lo/hi MUST come from `RangeBrackets.LowerMeters`/`MaxMeters` — the same functions the evaluator uses (no drift).
- Persistence path unchanged: every callback keeps `onChanged?.Invoke()` exactly as today; `Rebuild()` is added AFTER `onChanged`.
- Code style: K&R braces, 4-space indent. No version bump (`/release` owns it — target 1.23.1 after the deck check).
- `ConditionOperator` order (index-cast by the op dropdowns, do not reorder): LessThan, GreaterThan, Equal, NotEqual, GreaterOrEqual, LessOrEqual.

---

### Task 1: `RangeBrackets.EffectiveHint` / `EffectiveLabel` (TDD)

**Files:**
- Modify: `WrathTactics/Models/Enums.cs` (class `RangeBrackets`, after `Label` ~line 202)
- Test: `WrathTactics.Tests/RangeBracketsTests.cs` (extend existing file)

**Interfaces:**
- Consumes: existing `RangeBrackets.LowerMeters(RangeBracket)`, `MaxMeters(RangeBracket)`, `Label(RangeBracket)` (localized, e.g. "Short ( 10 m )").
- Produces: `public static string EffectiveHint(RangeBracket b, ConditionOperator op)` — returns e.g. `"≤ 5 m"`, or `null` for operators outside the six comparison values. `public static string EffectiveLabel(RangeBracket b, ConditionOperator op)` — `"<name> (<hint>)"`, falling back to `Label(b)` when hint is null. Task 2 calls `EffectiveLabel`.

- [ ] **Step 1: Write the failing tests**

Append to the existing class in `WrathTactics.Tests/RangeBracketsTests.cs`:

```csharp
        [Theory]
        // Short: lo=5, hi=10 — the report's trap: "<" means BELOW the bracket.
        [InlineData(RangeBracket.Short, ConditionOperator.LessThan,       "≤ 5 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.LessOrEqual,    "≤ 10 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.GreaterOrEqual, "> 5 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.GreaterThan,    "> 10 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.Equal,          "5–10 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.NotEqual,       "≠ 5–10 m")]
        // Melee: lo=0 — "<" yields the visibly-never-true "≤ 0 m".
        [InlineData(RangeBracket.Melee, ConditionOperator.LessThan,       "≤ 0 m")]
        [InlineData(RangeBracket.Melee, ConditionOperator.Equal,          "0–2 m")]
        public void EffectiveHint_maps_operator_to_evaluator_interval(
            RangeBracket b, ConditionOperator op, string expected) {
            Assert.Equal(expected, RangeBrackets.EffectiveHint(b, op));
        }

        [Fact]
        public void EffectiveHint_unknown_operator_returns_null() {
            Assert.Null(RangeBrackets.EffectiveHint(RangeBracket.Short, (ConditionOperator)99));
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ 2>&1 | tail -5`
Expected: build FAILS with `error CS0117: 'RangeBrackets' does not contain a definition for 'EffectiveHint'`.

- [ ] **Step 3: Implement**

In `WrathTactics/Models/Enums.cs`, inside `public static class RangeBrackets`, directly after the `Label` method:

```csharp
        // Effective interval the evaluator actually checks for (bracket, op) —
        // mirrors the WithinRange operator switch in ConditionEvaluator
        // (Equal: lo<d<=hi; LessThan: d<=lo; GreaterOrEqual: d>lo; ...).
        // Null for operators outside the six comparison values.
        public static string EffectiveHint(RangeBracket b, ConditionOperator op) {
            float lo = LowerMeters(b);
            float hi = MaxMeters(b);
            switch (op) {
                case ConditionOperator.Equal:          return $"{M(lo)}–{M(hi)} m";
                case ConditionOperator.NotEqual:       return $"≠ {M(lo)}–{M(hi)} m";
                case ConditionOperator.LessOrEqual:    return $"≤ {M(hi)} m";
                case ConditionOperator.LessThan:       return $"≤ {M(lo)} m";
                case ConditionOperator.GreaterOrEqual: return $"> {M(lo)} m";
                case ConditionOperator.GreaterThan:    return $"> {M(hi)} m";
                default:                               return null;
            }
        }

        // Localized bracket name + effective interval: "Short (≤ 5 m)". The
        // static "( 10 m )" part of Label() misled users into reading "< Short
        // (10 m)" as "closer than 10 m" (it means "below the bracket": ≤ 5 m).
        public static string EffectiveLabel(RangeBracket b, ConditionOperator op) {
            var hint = EffectiveHint(b, op);
            if (hint == null) return Label(b);
            var label = Label(b);
            int paren = label.IndexOf('(');
            var name = paren > 0 ? label.Substring(0, paren).Trim() : label.Trim();
            return $"{name} ({hint})";
        }

        static string M(float meters) =>
            meters.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ 2>&1 | tail -3`
Expected: all green (92 existing + 9 new = 101). Mono-flake caveat from Global Constraints applies.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Models/Enums.cs WrathTactics.Tests/RangeBracketsTests.cs
git commit -m "feat(models): operator-aware effective labels for range brackets"
```

---

### Task 2: Wire both WithinRange selector sites + live refresh

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs` (count site ~lines 134-149, normal site ~lines 256-262 + 330-338, label builder ~line 415)

**Interfaces:**
- Consumes: `RangeBrackets.EffectiveLabel(RangeBracket, ConditionOperator)` (Task 1); existing `Rebuild()` (line ~34), `RangeBracketNames` (line ~19).
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the label-list helper**

In `ConditionRowWidget`, next to `GetValueLabelsForProperty` (~line 415):

```csharp
        // Bracket labels are operator-dependent (EffectiveLabel): the same
        // bracket reads "Short (≤ 5 m)" under "<" but "Short (≤ 10 m)" under
        // "<=". Order must match RangeBracketNames (index-mapped).
        static List<string> GetRangeBracketLabels(ConditionOperator op) {
            return new List<string> {
                RangeBrackets.EffectiveLabel(RangeBracket.Melee, op),
                RangeBrackets.EffectiveLabel(RangeBracket.Cone, op),
                RangeBrackets.EffectiveLabel(RangeBracket.Short, op),
                RangeBrackets.EffectiveLabel(RangeBracket.Medium, op),
                RangeBrackets.EffectiveLabel(RangeBracket.Long, op),
            };
        }
```

- [ ] **Step 2: Count-layout site**

Replace the operator callback and bracket labels in the `propNeedsOperator` block (~lines 134-149):

```csharp
                if (propNeedsOperator) {
                    // Operator selector where the "<" label was
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.Create(root, "CountOperator", 0.58f, 0.66f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            onChanged?.Invoke();
                            // Bracket labels are operator-dependent — rebuild so
                            // they refresh live (same pattern as Subject/Property).
                            if (condition.Property == ConditionProperty.WithinRange) Rebuild();
                        });

                    if (condition.Property == ConditionProperty.WithinRange) {
                        var bracketNames = RangeBracketNames;
                        var bracketLabels = GetRangeBracketLabels(condition.Operator);
                        int brIdx = bracketNames.IndexOf(condition.Value);
                        if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; } // default: Short
                        PopupSelector.Create(root, "CountRangeBracketValue", 0.67f, 0.88f, bracketLabels, brIdx, v => {
                            condition.Value = bracketNames[v];
                            onChanged?.Invoke();
                        });
                    } else {
```

(The trailing `} else {` and the value-input block stay untouched.)

- [ ] **Step 3: Normal-layout site**

Operator selector (~lines 256-262) gains the same conditional rebuild:

```csharp
                if (needsOperator) {
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.Create(root, "Operator", 0.38f, 0.50f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            onChanged?.Invoke();
                            // Bracket labels are operator-dependent — rebuild so
                            // they refresh live (same pattern as Subject/Property).
                            if (condition.Property == ConditionProperty.WithinRange) Rebuild();
                        });
                } else if (usesEqOp) {
```

Bracket selector (~lines 330-338):

```csharp
                } else if (isWithinRange) {
                    var bracketNames = RangeBracketNames;
                    var bracketLabels = GetRangeBracketLabels(condition.Operator);
                    int brIdx = bracketNames.IndexOf(condition.Value);
                    if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; } // default: Short
                    PopupSelector.Create(root, "RangeBracketValue", 0.51f, 0.88f, bracketLabels, brIdx, v => {
                        condition.Value = bracketNames[v];
                        onChanged?.Invoke();
                    });
                } else if (isBoolProperty) {
```

- [ ] **Step 4: Remove the now-unreached WithinRange case from `GetValueLabelsForProperty`**

Both former callers now use `GetRangeBracketLabels`; leaving the old static-label case invites exactly the drift this feature fixes. In `GetValueLabelsForProperty` (~line 422) delete:

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

Verify no other caller passes WithinRange: `rtk proxy grep -n "GetValueLabelsForProperty" WrathTactics/UI/ConditionRowWidget.cs` — remaining call sites must be the CreatureType/Alignment/HasCondition/DescriptorEffect/ImmuneToEnergy paths and the generic count-value dropdown (~line 193), which is unreachable for WithinRange (the ~141 branch handles it first).

- [ ] **Step 5: Build + full test suite**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ 2>&1 | tail -3`
Expected: `0 Error(s)`.
Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ 2>&1 | tail -3`
Expected: all green (101).

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): show effective meters in WithinRange bracket labels"
```

---

### Task 3: Deck check + release handoff

**Files:** none (verification only; deck currently online).

- [ ] **Step 1: Deploy**

Run: `./deploy.sh` — expect "Deployed to Steam Deck." If behavior didn't land, compare DLL mtime vs. source mtime (stale-build gotcha), `touch` + rebuild.

- [ ] **Step 2: In-game check (user)**

1. Open a rule with a WithinRange condition (normal row): cycle `< > = != >= <=` — bracket label updates live (e.g. Short: `≤ 5 m` / `> 10 m` / `5–10 m` / `≠ 5–10 m` / `> 5 m` / `≤ 10 m`), dropdown items show the same op-adjusted hints, selection sticks.
2. Same on an EnemyCount row ("with WithinRange ..." compressed layout).
3. Melee + `<` shows `Melee (≤ 0 m)` (the visible never-true trap).
4. Row still persists correctly after toggling operators (close/reopen panel, value kept).

- [ ] **Step 3: Hand off to release**

All green → user runs `/release patch` (→ 1.23.1). The Nexus reply to the bug reporter goes out AFTER release, naming 1.23.1 and the corrected rule setup (`EnemyCount < 1 with WithinRange <= Short (≤ 10 m)`, Target `Enemy: nearest`).
