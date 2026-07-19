# Current HP (flat) Condition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a numeric `ConditionProperty.HpFlat` ("Current HP") so rules can gate on absolute hit points (Power Word Kill ≤100 etc.), per spec `docs/superpowers/specs/2026-07-19-current-hp-condition-design.md`.

**Architecture:** Direct sibling of `HpPercent`: one enum member (appended), one case in each of the two evaluator switches (hot path + count path), operator-chain + five subject-list entries in `ConditionRowWidget`, one i18n key × 5 locales. Metric is `unit.HPLeft` (IL-verified identical to the engine's `AbilityTargetHPCondition` gate expression: `HitPoints.ModifiedValue − Damage`, temp HP excluded).

**Tech Stack:** C# / .NET Framework 4.8.1, UMM + Harmony mod, xUnit on mono.

## Global Constraints

- **Enums are APPEND-ONLY** — persisted JSON stores numeric indices; `HpFlat` goes at the END of `ConditionProperty`, after `NegativeLevels`.
- **The two evaluator sites must stay textually parallel** (same rule as HpPercent/IsDead pairs in `gotchas-conditions.md`).
- Build from repo root `/home/pascal/Code/wrath-mods/wrath-tactics`: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/` (dotnet is NOT on PATH; `-p:SolutionDir` is mandatory on Linux).
- Tests: mono runner is flaky — loop `for i in 1 2 3; do ~/.dotnet/dotnet test --no-build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/; done`; trust the FIRST all-green run; only trust failures that reproduce.
- Locale JSONs are EmbeddedResources — label changes need rebuild + redeploy to show in-game.
- Code style: K&R braces, 4-space indent. Widgets never call `ConfigManager.Save()` directly (no save calls are added in this plan).
- No version bump in this plan — `/release` does the bump; csproj must stay at the pre-bump version.
- Every commit message ends with the trailer line `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: CompareFloat regression tests (promote to internal)

`CompareFloat` is the pure primitive `HpFlat` rides on. It exists and is correct — these are regression tests locking its semantics (incl. the 0.01 epsilon on `=`/`≠`), not red-green TDD. Promotion to `internal` follows the established `InternalsVisibleTo` pattern (`CompareCount` precedent).

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs:239`
- Create: `WrathTactics.Tests/CompareFloatTests.cs`

**Interfaces:**
- Consumes: `ConditionEvaluator.CompareFloat(float left, ConditionOperator op, float right)` (currently `static`, becomes `internal static`), `ConditionOperator` members `LessThan/GreaterThan/Equal/NotEqual/GreaterOrEqual/LessOrEqual`.
- Produces: `internal static bool CompareFloat(...)` — Task 2's evaluator cases call it (already-existing call sites unaffected).

- [ ] **Step 1: Promote CompareFloat to internal**

In `WrathTactics/Engine/ConditionEvaluator.cs` line 239 change:

```csharp
        static bool CompareFloat(float left, ConditionOperator op, float right) {
```

to:

```csharp
        internal static bool CompareFloat(float left, ConditionOperator op, float right) {
```

- [ ] **Step 2: Write the test file**

Create `WrathTactics.Tests/CompareFloatTests.cs` (pattern-mirror of `CompareCountTests.cs`):

```csharp
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class CompareFloatTests {
        [Theory]
        // Power-Word-style flat thresholds: the 100-HP gate
        [InlineData(100f, ConditionOperator.LessOrEqual,    100f, true)]
        [InlineData(101f, ConditionOperator.LessOrEqual,    100f, false)]
        [InlineData( 99f, ConditionOperator.LessThan,       100f, true)]
        [InlineData(100f, ConditionOperator.LessThan,       100f, false)]
        [InlineData(150f, ConditionOperator.GreaterOrEqual, 150f, true)]
        [InlineData(149f, ConditionOperator.GreaterOrEqual, 150f, false)]
        [InlineData(151f, ConditionOperator.GreaterThan,    150f, true)]
        [InlineData(150f, ConditionOperator.GreaterThan,    150f, false)]
        // Equal/NotEqual use a 0.01 epsilon
        [InlineData(100f,     ConditionOperator.Equal,    100f, true)]
        [InlineData(100.005f, ConditionOperator.Equal,    100f, true)]
        [InlineData(100.5f,   ConditionOperator.Equal,    100f, false)]
        [InlineData(100.5f,   ConditionOperator.NotEqual, 100f, true)]
        [InlineData(100f,     ConditionOperator.NotEqual, 100f, false)]
        // Clamp-at-zero boundary: dead/downed units compare as 0 on the hot path
        [InlineData(0f, ConditionOperator.LessOrEqual, 100f, true)]
        [InlineData(0f, ConditionOperator.GreaterThan,   0f, false)]
        public void CompareFloat_returns_expected(float left, ConditionOperator op,
            float right, bool expected) {
            Assert.Equal(expected, ConditionEvaluator.CompareFloat(left, op, right));
        }
    }
}
```

- [ ] **Step 3: Build + run the suite**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
for i in 1 2 3; do ~/.dotnet/dotnet test --no-build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/; done
```

Expected: first all-green run counts (mono flake rule). All CompareFloatTests rows PASS.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs WrathTactics.Tests/CompareFloatTests.cs
git commit -m "test(engine): lock CompareFloat semantics; promote to internal

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Enum member + both evaluator cases

**Files:**
- Modify: `WrathTactics/Models/Enums.cs` (end of `ConditionProperty`, after `NegativeLevels`)
- Modify: `WrathTactics/Engine/ConditionEvaluator.UnitProperty.cs:16-20` (hot path) and `:289-295` (count path)

**Interfaces:**
- Consumes: `ConditionEvaluator.CompareFloat` (internal, Task 1); `unit.HPLeft` (`UnitEntityData`, int).
- Produces: `ConditionProperty.HpFlat` — Tasks 3–4 reference this exact member name.

- [ ] **Step 1: Append enum member**

In `WrathTactics/Models/Enums.cs`, change the last `ConditionProperty` member:

```csharp
        NegativeLevels            // Yes/No — UnitPartNegativeLevels.Count > 0 (temporary + permanent energy drain); for Restoration rules.
    }
```

to:

```csharp
        NegativeLevels,           // Yes/No — UnitPartNegativeLevels.Count > 0 (temporary + permanent energy drain); for Restoration rules.
        HpFlat                    // numeric — current hit points, flat (HPLeft = HitPoints.ModifiedValue − Damage; temp HP excluded, same expression as the engine's AbilityTargetHPCondition Power-Word gate)
    }
```

- [ ] **Step 2: Hot-path case**

In `WrathTactics/Engine/ConditionEvaluator.UnitProperty.cs`, directly after the `HpPercent` case in `EvaluateUnitProperty` (after line 20 `return CompareFloat(hpPct, condition.Operator, threshold);`), insert:

```csharp
                case ConditionProperty.HpFlat:
                    return CompareFloat(Math.Max(0, unit.HPLeft), condition.Operator, threshold);
```

(`threshold` is already parsed at method top; `Math.Max` clamps negative HPLeft — Death's-Door units compare as 0, mirroring the HpPercent guard.)

- [ ] **Step 3: Count-path case**

Same file, directly after the `HpPercent` case in `MatchesPropertyThreshold` (after line 295 `return CompareFloat(hpPct, condition.Operator, threshold);`), insert:

```csharp
                case ConditionProperty.HpFlat:
                    if (unit.HPLeft <= 0) return false; // Don't count dead as "low HP"
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    return CompareFloat(unit.HPLeft, condition.Operator, threshold);
```

- [ ] **Step 4: Build**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: Build succeeded, 0 errors (findstr warnings are harmless).

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Models/Enums.cs WrathTactics/Engine/ConditionEvaluator.UnitProperty.cs
git commit -m "feat(engine): HpFlat condition property — flat current-HP threshold

Metric is unit.HPLeft, IL-verified identical to the engine's
AbilityTargetHPCondition gate (HitPoints.ModifiedValue - Damage).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Widget wiring (operator chain + five subject lists)

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs:124-130` (`propNeedsOperator`) and `:520-621` (`GetPropertiesForSubject`)

**Interfaces:**
- Consumes: `ConditionProperty.HpFlat` (Task 2).
- Produces: `HpFlat` selectable in Self / Ally / AllyByName / AllyCount / Enemy-family / EnemyCount dropdowns, rendered with operator selector + free number input (both plain and count forms share the `propNeedsOperator` path).

- [ ] **Step 1: Extend the operator chain**

In `ConditionRowWidget.cs` change:

```csharp
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.AC
```

to:

```csharp
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.HpFlat
                    || condition.Property == ConditionProperty.AC
```

- [ ] **Step 2: Add to Self/Ally/AllyCount lists (one replace-all)**

Replace ALL 3 occurrences (Self L524, Ally/AllyByName L541, AllyCount L558) of:

```csharp
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
```

with:

```csharp
                        ConditionProperty.HpPercent, ConditionProperty.HpFlat, ConditionProperty.HasBuff,
```

- [ ] **Step 3: Add to Enemy-family list**

Change (L589, unique — followed by the `SaveFortitude` line):

```csharp
                        ConditionProperty.HpPercent, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
```

to:

```csharp
                        ConditionProperty.HpPercent, ConditionProperty.HpFlat, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
```

- [ ] **Step 4: Add to EnemyCount list**

Change (L612, unique — three members on one line):

```csharp
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
```

to:

```csharp
                        ConditionProperty.HpPercent, ConditionProperty.HpFlat, ConditionProperty.AC, ConditionProperty.HasBuff,
```

- [ ] **Step 5: Build**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): surface HpFlat in all five condition scopes

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: i18n keys × 5 locales

**Files:**
- Modify: `WrathTactics/Localization/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json` (each: line 141 area, after the `enum.property.HpPercent` entry)

**Interfaces:**
- Consumes: label resolution is automatic — `EnumLabels.For(ConditionProperty)` builds `enum.property.{name}` keys; no `EnumLabels.cs` change needed for plain numeric properties.
- Produces: `enum.property.HpFlat` in all 5 locale files.

- [ ] **Step 1: Add the key to each locale**

Directly AFTER the line `"enum.property.HpPercent": …` add in each file:

`en_GB.json`:
```json
  "enum.property.HpFlat": "Current HP",
```
`de_DE.json`:
```json
  "enum.property.HpFlat": "Aktuelle TP",
```
`fr_FR.json`:
```json
  "enum.property.HpFlat": "PV actuels",
```
`ru_RU.json`:
```json
  "enum.property.HpFlat": "Текущие HP",
```
`zh_CN.json`:
```json
  "enum.property.HpFlat": "当前HP",
```

(Each locale mirrors its own HpPercent style: en/ru/zh use "HP", de "TP", fr "PV".)

- [ ] **Step 2: Validate JSON + build (locales are EmbeddedResources)**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
for f in WrathTactics/Localization/*.json; do python3 -m json.tool "$f" > /dev/null || echo "INVALID: $f"; done
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: no "INVALID" line; Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Localization/en_GB.json WrathTactics/Localization/de_DE.json WrathTactics/Localization/fr_FR.json WrathTactics/Localization/ru_RU.json WrathTactics/Localization/zh_CN.json
git commit -m "feat(i18n): labels for HpFlat (Current HP) in 5 locales

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Full verification + deck smoke test

**Files:**
- No source changes. Runs `deploy.sh` (existing).

**Interfaces:**
- Consumes: everything from Tasks 1–4.
- Produces: verified build + (deck online) in-game smoke evidence.

- [ ] **Step 1: Full test suite (flake loop)**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
for i in 1 2 3; do ~/.dotnet/dotnet test --no-build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/; done
```

Expected: first all-green run counts; only reproducing failures are real.

- [ ] **Step 2: Deploy to deck**

```bash
./deploy.sh
```

If the deck is unreachable ("no route to host" often just means suspend — wake it first), note the missed smoke test per CLAUDE.md precedent (1.21.0–1.22.1): release may proceed, but the gap must be disclosed in the Nexus reply and recorded in auto-memory.

- [ ] **Step 3: In-game smoke test (user, when deck online)**

Rule: `Enemy · Current HP · <= · 100 → Cast Spell: Power Word Kill → Target: Condition target`.
Verify: (a) no fire while all enemies >100 HP; (b) fires once an enemy drops to ≤100; (c) dropdown label shows "Current HP" (locale: "Aktuelle TP" on de); (d) count form `EnemyCount >= 2 with Current HP < 50` renders operator + number field.

---

## Self-Review (done at plan time)

- **Spec coverage:** enum append (T2), both evaluator cases (T2), operator chain + 5 lists (T3), i18n × 5 (T4), tests (T1 — deviation: spec said "siblings of existing HpPercent tests", but NO HpPercent evaluator tests exist since `EvaluateUnitProperty` needs game state; the testable pure surface is `CompareFloat`, locked in T1; evaluator cases covered by T5 smoke test), manual smoke (T5). Mythic-override caveat is Nexus-reply content, no code impact. ✓
- **Placeholder scan:** none. ✓
- **Type consistency:** `HpFlat` spelled identically in all tasks; `CompareFloat(float, ConditionOperator, float)` signature matches call sites. ✓
