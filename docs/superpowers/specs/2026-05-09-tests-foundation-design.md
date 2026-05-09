# Wrath Tactics — Tests Foundation

**Date:** 2026-05-09
**Target version:** v1.15.0 (additive — no behaviour change)
**Type:** Tooling / safety-net

## Goal

Establish a unit-test project for `WrathTactics` that covers pure-logic
helpers (no Game-DLL dependencies). The test layer must catch the class
of regressions that surfaced in v1.14.1 (CountOperator hardcoded `>=`,
`<= 9` ceiling, ConditionOperator-misuse) automatically on the next
`dotnet build` cycle, without requiring a manual smoke test on the
Steam Deck.

## Scope and Non-Goals

### In scope

- New sibling project `WrathTactics.Tests/` targeting `net481`
- xUnit 2.x test framework
- Tests for four pure-logic surfaces:
  - `ConditionEvaluator.CompareCount(int, float, ConditionOperator)`
  - `BuffBlueprintProvider.IsCrusadeOnlyBuff(string)`
  - `CommonBuffRegistry.IsEnemySubject(ConditionSubject)` and
    `CommonBuffRegistry.GetDefaultGuids(ConditionSubject)`
  - `RangeBrackets.MaxMeters(RangeBracket)` lookup method
- Visibility: privates that are testable surface get promoted to
  `internal`; `WrathTactics/Properties/AssemblyInfo.cs` exposes them
  via `[assembly: InternalsVisibleTo("WrathTactics.Tests")]`
- Local-only execution via `dotnet test` — invoked manually before
  release / smoke-test on Steam Deck

### Out of scope

- Mocking of Unity/Game types (`UnitEntityData`, `BlueprintAbility`,
  `Spellbook`). Reserved for a future Phase-2b plan if ever needed.
- Engine-hot-path tests (`EvaluateUnitProperty`, `FindBestHealEx`,
  `TacticsEvaluator.Tick`). These need the game runtime; smoke-test
  on Steam Deck remains the only verification.
- Refactoring existing engine files. Reserved for Phase 2c.
- Test runner inside the game (UMM-mod-as-test-driver). Out of scope.
- **CI workflow.** Game-DLLs are unreachable from GitHub-Runners
  without bundling stub assemblies or extracting a Pure-Logic
  sub-project — both rejected as overkill for a solo-dev mod.
  Tests run locally before push; the existing release pipeline
  (Nexus upload via `nexus-upload.yml`) is unchanged.

## Constraints

- **Cannot reference `Assembly-CSharp.dll`** in the test project. Game
  DLLs need a fully-initialised `Game.Instance` plus Unity-specific
  runtime that is unavailable to `dotnet test`. Any test that touches
  a Game type fails at load.
- **Project must build on Linux without `findstr`-warning regressions**.
  The existing `WrathTactics.csproj` uses `-p:SolutionDir=$(pwd)/`; the
  test csproj must follow the same convention or be self-contained.
- **No new game DLLs in source control.** Test project must not need
  `GamePath.props` to build.

## Architecture

### Project layout

```
wrath-tactics/
  WrathTactics/
    WrathTactics.csproj          (existing)
    Properties/
      AssemblyInfo.cs            (new — InternalsVisibleTo only)
    Engine/
      ConditionEvaluator.cs      (modified — CompareCount → internal)
      BuffBlueprintProvider.cs   (modified — IsCrusadeOnlyBuff → internal)
      CommonBuffRegistry.cs      (modified — IsEnemySubject → internal)
    Models/
      RangeBrackets.cs           (no change — already public)

  WrathTactics.Tests/            (new project)
    WrathTactics.Tests.csproj
    CompareCountTests.cs
    BuffBlueprintProviderTests.cs
    CommonBuffRegistryTests.cs
    RangeBracketsTests.cs
```

The test project sits next to `WrathTactics/` and references it via
`<ProjectReference>`. The Mod's Game-DLL refs resolve through the
local `GameInstall/` symlink as today; tests run on the developer's
machine where that symlink exists.

### Test-project csproj

`net481` target, package references:

- `Microsoft.NET.Test.Sdk` (test host)
- `xunit` (framework)
- `xunit.runner.visualstudio` (CLI test discovery)

ProjectReference to `..\WrathTactics\WrathTactics.csproj`. No reference
to game DLLs. No `GamePath.props` import.

### Visibility strategy

`internal` + `[assembly: InternalsVisibleTo("WrathTactics.Tests")]` is
the .NET-idiomatic way to expose test surface without making it public
API. `WrathTactics/Properties/AssemblyInfo.cs` is a 2-line file that
holds only the attribute.

Verified state (grep on `static`-decl lines):

| Function | Current | Action |
|---|---|---|
| `ConditionEvaluator.CompareCount` | `static` (default = private) | promote to `internal static` |
| `BuffBlueprintProvider.IsCrusadeOnlyBuff` | `static` (default = private) | promote to `internal static` |
| `CommonBuffRegistry.IsEnemySubject` | `public static` | no change |
| `CommonBuffRegistry.GetDefaultGuids` | `public static` | no change |
| `RangeBrackets.MaxMeters` | `public static` | no change |

## Test inventory

### CompareCountTests (~12 tests)

Direct 1.14.1-regression coverage. Six operators × two boundary cases each.

```csharp
[Theory]
[InlineData(3, 3f, ConditionOperator.Equal,           true)]
[InlineData(2, 3f, ConditionOperator.Equal,           false)]
[InlineData(2, 3f, ConditionOperator.LessThan,        true)]
[InlineData(3, 3f, ConditionOperator.LessThan,        false)]
[InlineData(3, 3f, ConditionOperator.LessOrEqual,     true)]
[InlineData(4, 3f, ConditionOperator.LessOrEqual,     false)]
[InlineData(4, 3f, ConditionOperator.GreaterThan,     true)]
[InlineData(3, 3f, ConditionOperator.GreaterThan,     false)]
[InlineData(3, 3f, ConditionOperator.GreaterOrEqual,  true)]
[InlineData(2, 3f, ConditionOperator.GreaterOrEqual,  false)]
[InlineData(2, 3f, ConditionOperator.NotEqual,        true)]
[InlineData(3, 3f, ConditionOperator.NotEqual,        false)]
public void CompareCount_returns_expected(int actual, float threshold,
    ConditionOperator op, bool expected)
{
    Assert.Equal(expected, ConditionEvaluator.CompareCount(actual, threshold, op));
}
```

### BuffBlueprintProviderTests (~6 tests)

```
IsCrusadeOnlyBuff("ArmyBuff")           → true
IsCrusadeOnlyBuff("armyBuff")           → true   (case-insensitive prefix)
IsCrusadeOnlyBuff("BlessBuff")          → false
IsCrusadeOnlyBuff("AirBlessingMajorBuff") → false  (real Warpriest blessing)
IsCrusadeOnlyBuff("")                   → false
IsCrusadeOnlyBuff(null)                 → false (or throws — pin behaviour)
```

The `null` case: pin whatever the current implementation does. If it
throws, document it; if it returns false, document it. Either is fine,
but the test fixes the contract.

### CommonBuffRegistryTests (~8 tests)

```
IsEnemySubject(ConditionSubject.Enemy)              → true
IsEnemySubject(ConditionSubject.EnemyBiggestThreat) → true
IsEnemySubject(ConditionSubject.EnemyHighestHp)     → true
IsEnemySubject(ConditionSubject.Self)               → false
IsEnemySubject(ConditionSubject.Ally)               → false
IsEnemySubject(ConditionSubject.AllyByName)         → false

GetDefaultGuids(Self).Count            > 0
GetDefaultGuids(Enemy).Count           > 0
```

Plus a parametric "every ConditionSubject value returns a defined
result" test — guards against new enum values landing without a
classification.

### RangeBracketsTests (~6 tests)

```
MaxMeters(Melee)   == 2f
MaxMeters(Cone)    == 5f
MaxMeters(Short)   == 10f
MaxMeters(Medium)  == 20f
MaxMeters(Long)    == 40f
MaxMeters((RangeBracket)999) == float.PositiveInfinity   // default branch
```

Pure switch-table check. Trivial but pins the meter values referenced
by the WithinRange logic in CLAUDE.md, plus the documented default
behaviour for unknown brackets.

**Total: ~31 tests, ~250 LOC.**

## Local execution

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj \
  -p:SolutionDir=$(pwd)/
```

`SolutionDir` is required so the transitively-loaded
`WrathTactics.csproj` can find `GamePath.props` and resolve its
Game-DLL references. The test code itself does not touch any Game
type, but the Mod assembly must build for the `ProjectReference` to
resolve.

Convention: run before every `git push` on a feature branch, and as
part of the `/release` flow's pre-flight check.

## Error handling

Tests are pure-logic; no I/O, no Game.Instance, no exceptions to
recover from. xUnit's `Assert.Throws` covers the documented-throwing
cases (e.g. `null` to `IsCrusadeOnlyBuff` if that's the pinned contract).

## Future work (out of scope here)

- **Phase 2b — Engine-path tests with mocks**: extract Game-type
  dependencies behind interfaces (`ISpellbook`, `IUnit`); test via
  hand-rolled mocks. Open question whether the maintenance cost is
  justified for a single-developer mod.
- **Phase 2c — Fat-file refactor**: split `ConditionEvaluator.cs`
  (1086 LOC), `RuleEditorWidget.cs` (1009 LOC), `ActionValidator.cs`
  (902 LOC). Should run AFTER 2a so the test layer catches regressions
  during the split.

## Open questions

None. Scope is small, decisions are made:
- xUnit (industry standard for net481 + dotnet test)
- `internal` + `InternalsVisibleTo` (idiomatic .NET)
- One test class per SUT class
- CI on push + PR via GitHub Actions
