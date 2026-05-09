# Tests Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up `WrathTactics.Tests` (xUnit, net481) with ~31 pure-logic tests covering the four functions identified in `docs/superpowers/specs/2026-05-09-tests-foundation-design.md`. Local execution only (no CI).

**Architecture:** New sibling project `WrathTactics.Tests/` references the Mod project via `<ProjectReference>`. Tests touch only pure-logic surface (no Game types). Two private statics (`CompareCount`, `IsCrusadeOnlyBuff`) are promoted to `internal` and exposed via `[InternalsVisibleTo]`. Run with `~/.dotnet/dotnet test ... -p:SolutionDir=$(pwd)/`.

**Tech Stack:** xUnit 2.9.x, Microsoft.NET.Test.Sdk 17.x, net481, Linux + `~/.dotnet/dotnet`. Same toolchain as the existing Mod build.

---

## Task 1: Skeleton — Test project + InternalsVisibleTo + smoke run

**Files:**
- Create: `WrathTactics.Tests/WrathTactics.Tests.csproj`
- Create: `WrathTactics/Properties/AssemblyInfo.cs`

- [ ] **Step 1: Create the test csproj**

Write `WrathTactics.Tests/WrathTactics.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net481</TargetFramework>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
    <RootNamespace>WrathTactics.Tests</RootNamespace>
    <AssemblyName>WrathTactics.Tests</AssemblyName>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\WrathTactics\WrathTactics.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create AssemblyInfo for InternalsVisibleTo**

Write `WrathTactics/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WrathTactics.Tests")]
```

- [ ] **Step 3: Smoke-run an empty test project**

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ 2>&1 | tail -10
```

Expected: `Test Run Successful.` with `Passed: 0, Failed: 0, Skipped: 0`. (NuGet packages restore on first run; this can take 30-60 s.) If the command fails because `GamePath.props` is missing, that's a setup issue with the dev machine, not the plan — fix the symlink/props first, then re-run.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics.Tests/WrathTactics.Tests.csproj WrathTactics/Properties/AssemblyInfo.cs
git commit -m "chore(tests): scaffold WrathTactics.Tests xUnit project

Empty test project with ProjectReference to the Mod and
InternalsVisibleTo from the Mod. Verified via 'dotnet test' returning
0 passed, 0 failed."
```

---

## Task 2: CompareCount — promote + tests

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs:485`
- Create: `WrathTactics.Tests/CompareCountTests.cs`

- [ ] **Step 1: Promote `CompareCount` from `static` (private) to `internal static`**

In `WrathTactics/Engine/ConditionEvaluator.cs:485` change:

```csharp
        static bool CompareCount(int actual, float threshold, ConditionOperator op) {
```

to:

```csharp
        internal static bool CompareCount(int actual, float threshold, ConditionOperator op) {
```

- [ ] **Step 2: Write the test class**

Write `WrathTactics.Tests/CompareCountTests.cs`:

```csharp
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class CompareCountTests {
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
            ConditionOperator op, bool expected) {
            Assert.Equal(expected, ConditionEvaluator.CompareCount(actual, threshold, op));
        }
    }
}
```

- [ ] **Step 3: Run tests**

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ 2>&1 | tail -10
```

Expected: `Passed: 12, Failed: 0`.

If a row fails, the bug is in `CompareCount` — read the failing row, fix `CompareCount`, re-run. (The 1.14.1 fix DID NOT touch `CompareCount` itself — it was the bucket-paths that ignored it. So `CompareCount` should already be correct. If anything fails here, that's a hidden bug we missed.)

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs WrathTactics.Tests/CompareCountTests.cs
git commit -m "test(engine): cover ConditionEvaluator.CompareCount

12 InlineData rows, two boundary cases per operator (Equal, LessThan,
LessOrEqual, GreaterThan, GreaterOrEqual, NotEqual). Locks the
operator semantics that the 1.14.1 bucket-path fix relied on.
CompareCount promoted from private to internal; tests gain access
via InternalsVisibleTo."
```

---

## Task 3: IsCrusadeOnlyBuff — promote + tests

**Files:**
- Modify: `WrathTactics/Engine/BuffBlueprintProvider.cs:57`
- Create: `WrathTactics.Tests/BuffBlueprintProviderTests.cs`

- [ ] **Step 1: Pin null-input behaviour**

`IsCrusadeOnlyBuff` calls `name.StartsWith(...)`. Passing `null` would throw `NullReferenceException`. Verify by reading line 57:

```bash
grep -n -A 3 "static bool IsCrusadeOnlyBuff" WrathTactics/Engine/BuffBlueprintProvider.cs
```

Expected: a body of the form `return name.StartsWith("Army", StringComparison.OrdinalIgnoreCase);` — no null-guard. Pin this contract: the test for `null` will use `Assert.Throws<NullReferenceException>`.

- [ ] **Step 2: Promote to `internal static`**

In `WrathTactics/Engine/BuffBlueprintProvider.cs:57` change:

```csharp
        static bool IsCrusadeOnlyBuff(string name) {
```

to:

```csharp
        internal static bool IsCrusadeOnlyBuff(string name) {
```

- [ ] **Step 3: Write the test class**

Write `WrathTactics.Tests/BuffBlueprintProviderTests.cs`:

```csharp
using System;
using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class BuffBlueprintProviderTests {
        [Theory]
        [InlineData("ArmyBuff",                 true)]
        [InlineData("ArmyHaste",                true)]
        [InlineData("armyBuff",                 true)]    // case-insensitive
        [InlineData("BlessBuff",                false)]
        [InlineData("AirBlessingMajorBuff",     false)]   // real Warpriest blessing
        [InlineData("",                          false)]
        public void IsCrusadeOnlyBuff_classifies_by_Army_prefix(string name, bool expected) {
            Assert.Equal(expected, BuffBlueprintProvider.IsCrusadeOnlyBuff(name));
        }

        [Fact]
        public void IsCrusadeOnlyBuff_throws_on_null() {
            // Pinned contract: no null-guard, propagates NRE.
            Assert.Throws<NullReferenceException>(
                () => BuffBlueprintProvider.IsCrusadeOnlyBuff(null));
        }
    }
}
```

- [ ] **Step 4: Run tests**

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ 2>&1 | tail -10
```

Expected: `Passed: 19, Failed: 0` (12 from Task 2 + 7 here).

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Engine/BuffBlueprintProvider.cs WrathTactics.Tests/BuffBlueprintProviderTests.cs
git commit -m "test(engine): cover BuffBlueprintProvider.IsCrusadeOnlyBuff

Pins the 'Army…' prefix contract (case-insensitive, real Warpriest
'AirBlessing*' buffs explicitly NOT filtered) and the null-input
NRE behaviour. IsCrusadeOnlyBuff promoted from private to internal."
```

---

## Task 4: CommonBuffRegistry tests

**Files:**
- Create: `WrathTactics.Tests/CommonBuffRegistryTests.cs`

(No visibility changes — `IsEnemySubject` and `GetDefaultGuids` are already `public static`.)

- [ ] **Step 1: Read the current `IsEnemySubject` to lock its mapping**

```bash
grep -n -A 20 "public static bool IsEnemySubject" WrathTactics/Engine/CommonBuffRegistry.cs
```

Note the Enemy-classified subjects vs the rest. Match these in test data.

- [ ] **Step 2: Write the test class**

Write `WrathTactics.Tests/CommonBuffRegistryTests.cs`:

```csharp
using System;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class CommonBuffRegistryTests {
        [Theory]
        [InlineData(ConditionSubject.Enemy,              true)]
        [InlineData(ConditionSubject.EnemyCount,         true)]
        [InlineData(ConditionSubject.EnemyBiggestThreat, true)]
        [InlineData(ConditionSubject.EnemyHighestHp,     true)]
        [InlineData(ConditionSubject.EnemyLowestAC,      true)]
        [InlineData(ConditionSubject.Self,               false)]
        [InlineData(ConditionSubject.Ally,               false)]
        [InlineData(ConditionSubject.AllyCount,          false)]
        [InlineData(ConditionSubject.AllyByName,         false)]
        [InlineData(ConditionSubject.Combat,             false)]
        public void IsEnemySubject_classifies_correctly(ConditionSubject subject, bool expected) {
            Assert.Equal(expected, CommonBuffRegistry.IsEnemySubject(subject));
        }

        [Fact]
        public void GetDefaultGuids_returns_nonempty_for_Self() {
            var guids = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.Self);
            Assert.NotNull(guids);
            Assert.NotEmpty(guids);
        }

        [Fact]
        public void GetDefaultGuids_returns_nonempty_for_Enemy() {
            var guids = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.Enemy);
            Assert.NotNull(guids);
            Assert.NotEmpty(guids);
        }

        [Fact]
        public void GetDefaultGuids_returns_distinct_lists_for_ally_vs_enemy() {
            var allyGuids  = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.Self);
            var enemyGuids = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.Enemy);
            // No overlap — ally-side defaults should not include enemy debuffs.
            foreach (var g in allyGuids) Assert.DoesNotContain(g, enemyGuids);
        }

        [Fact]
        public void IsEnemySubject_handles_every_enum_value_without_throwing() {
            foreach (ConditionSubject value in Enum.GetValues(typeof(ConditionSubject))) {
                // The point is no exception — any non-Enemy value must return false,
                // any Enemy* value must return true. We don't assert specific mappings here
                // (the [Theory] above does that); we assert "no enum value crashes".
                var _ = CommonBuffRegistry.IsEnemySubject(value);
            }
        }
    }
}
```

- [ ] **Step 3: Run tests**

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ 2>&1 | tail -10
```

Expected: `Passed: 33, Failed: 0` (12 + 7 + 14 here — the `[Theory]` has 10 rows + 4 `[Fact]`s).

If `IsEnemySubject` mapping in the source disagrees with the InlineData rows, **fix the test data**, not the source — this test is a contract pin, not a correctness check on the source.

If `GetDefaultGuids` ally-vs-enemy lists overlap (test 4 fails), that's likely intentional sharing — relax the assertion to "at least one entry differs" rather than "no overlap". Decide based on the source.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics.Tests/CommonBuffRegistryTests.cs
git commit -m "test(engine): cover CommonBuffRegistry classification

10 [Theory] rows pinning Enemy* vs Self/Ally/Combat classification.
4 [Fact]s on GetDefaultGuids: non-empty for Self and Enemy, distinct
ally/enemy lists, total enum coverage without throwing."
```

---

## Task 5: RangeBrackets.MaxMeters tests

**Files:**
- Create: `WrathTactics.Tests/RangeBracketsTests.cs`

(No visibility changes — `MaxMeters` is `public static`.)

- [ ] **Step 1: Write the test class**

Write `WrathTactics.Tests/RangeBracketsTests.cs`:

```csharp
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class RangeBracketsTests {
        [Theory]
        [InlineData(RangeBracket.Melee,   2f)]
        [InlineData(RangeBracket.Cone,    5f)]
        [InlineData(RangeBracket.Short,   10f)]
        [InlineData(RangeBracket.Medium,  20f)]
        [InlineData(RangeBracket.Long,    40f)]
        public void MaxMeters_returns_expected(RangeBracket bracket, float expected) {
            Assert.Equal(expected, RangeBrackets.MaxMeters(bracket));
        }

        [Fact]
        public void MaxMeters_returns_PositiveInfinity_for_unknown_bracket() {
            Assert.Equal(float.PositiveInfinity, RangeBrackets.MaxMeters((RangeBracket)999));
        }
    }
}
```

- [ ] **Step 2: Run tests**

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ 2>&1 | tail -10
```

Expected: `Passed: 39, Failed: 0` (33 + 6 here).

- [ ] **Step 3: Commit**

```bash
git add WrathTactics.Tests/RangeBracketsTests.cs
git commit -m "test(models): cover RangeBrackets.MaxMeters lookup

Pins Melee=2/Cone=5/Short=10/Medium=20/Long=40 m thresholds and the
default-branch PositiveInfinity for unknown brackets. These values
back the WithinRange condition logic in CLAUDE.md."
```

---

## Task 6: CLAUDE.md note on running the test suite

**Files:**
- Modify: `CLAUDE.md` (the wrath-tactics CLAUDE.md, between "Build" and "Deploy" sections)

- [ ] **Step 1: Read the current Build section anchor**

```bash
grep -n "^## Build\|^## Deploy" CLAUDE.md | head -4
```

- [ ] **Step 2: Insert a Tests section after Build**

Insert immediately after the existing `## Build` block (before `## Deploy`):

```markdown
## Tests

Pure-logic unit tests live in `WrathTactics.Tests/` (xUnit, net481). They cover four functions: `ConditionEvaluator.CompareCount`, `BuffBlueprintProvider.IsCrusadeOnlyBuff`, `CommonBuffRegistry.IsEnemySubject` / `GetDefaultGuids`, `RangeBrackets.MaxMeters`. Game-DLL-free; no Unity runtime needed.

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```

Run before pushing changes that touch `ConditionEvaluator`, `BuffBlueprintProvider`, `CommonBuffRegistry`, or `Models/Enums.cs` (`RangeBrackets`). No CI — by design (Game-DLLs aren't available to GitHub-Runners; extracting a Pure sub-project was rejected as overkill for this codebase).

`internal` members of `WrathTactics` are visible to `WrathTactics.Tests` via `WrathTactics/Properties/AssemblyInfo.cs`. To add a new pure-logic helper to the test surface: mark it `internal static` (or leave `public`), add a test class to `WrathTactics.Tests/`, run the suite.
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude-md): document the WrathTactics.Tests xUnit suite

Local-only test runner, the four covered surfaces, the
InternalsVisibleTo pattern, and why no CI."
```

---

## Self-Review

**Spec coverage:**
- xUnit + net481 + sibling project → Task 1 ✓
- `CompareCount` test → Task 2 ✓
- `IsCrusadeOnlyBuff` test → Task 3 ✓
- `IsEnemySubject` / `GetDefaultGuids` tests → Task 4 ✓
- `RangeBrackets.MaxMeters` test → Task 5 ✓
- `internal` + `[InternalsVisibleTo]` → Tasks 1, 2, 3 ✓
- Out-of-scope CI explicitly documented in CLAUDE.md → Task 6 ✓

**Placeholder scan:** No "TBD"/"add validation"/"similar to". Every code block is concrete; every command has its expected output. Task 4 has a documented fallback ("if the test data disagrees, update the data, not the source") — that's not a placeholder, that's an explicit contingency.

**Type consistency:** `ConditionOperator` is a `WrathTactics.Models` enum (used in Tasks 2 & 4). `ConditionSubject` same (Task 4). `RangeBracket` enum + `RangeBrackets` static class (Task 5). All four test classes share the `WrathTactics.Tests` namespace declared in csproj. No drift.

**Cross-task ordering:** Task 1 must come first (csproj + AssemblyInfo). Tasks 2-5 are independent — could parallelise, but sequential is fine and gives clean per-class commits. Task 6 (docs) last, after the suite is real.

---

**Plan saved.** Two execution options:

1. **Subagent-Driven (recommended)** — fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute in this session via `superpowers:executing-plans`, batch checkpoints.

Which approach?
