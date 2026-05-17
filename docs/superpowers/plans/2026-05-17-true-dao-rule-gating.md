# True DAO Rule-Gating Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the tactics evaluator from interrupting its own in-flight command with a lower-priority rule. Restore the DAO semantic: while a rule's command is still running, only strictly higher-priority rules may preempt it; lower-priority rules wait.

**Architecture:** Per-unit `ActiveRuleTracker` records `(RuleListSource, entry.Id, UnitCommand)` whenever `CommandExecutor.Execute` queues a `UnitCommand`. Each tick, `TacticsEvaluator.EvaluateUnit` consults the tracker; if a tracked command is still `!IsFinished`, it derives a priority gate (`globalGate` / `charGate`) that limits `TryExecuteRules` to rules strictly above the active rule in the global-then-character priority order. Tracker self-clears when `Command.IsFinished` flips, when `entry.Id` no longer resolves in its list (rule deleted mid-combat), and on combat-start / combat-end / `Reset()`. Toggles and `DoNothing` actions don't replace the tracker — they don't issue `UnitCommand`s and shouldn't release the gate held by a still-animating cast.

**Tech Stack:** .NET Framework 4.8.1, xUnit (test project, net481, runs under Mono on Linux), HarmonyLib (untouched), Newtonsoft.Json (untouched), `Kingmaker.UnitLogic.Commands.*` (game runtime — touched only via existing `UnitCommand` interface).

---

## File Structure

**Create:**
- `WrathTactics/Engine/RuleListSource.cs` — enum `{ Global, Character }`. Public to the assembly; used by both `TacticsEvaluator` and `ActiveRuleTracker`. One enum per file (matches existing conventions like `ResolvedTarget.cs`).
- `WrathTactics/Engine/ActiveRuleTracker.cs` — per-unit state + pure `Resolve` priority-gate helper. State accessed from `TacticsEvaluator` only; pure helper is `internal static` for test access via existing `InternalsVisibleTo`.
- `WrathTactics.Tests/ActiveRuleTrackerTests.cs` — xUnit pure-logic tests against `ActiveRuleTracker.Resolve` covering: globals-active grid, chars-active grid, stale lookups, empty lists, idx-0 boundary.

**Modify:**
- `WrathTactics/Engine/CommandExecutor.cs` — `Execute` and each `ExecuteXxx` sub-method gains `out UnitCommand issuedCommand`. Branches that call `owner.Commands.Run(command)` set `issuedCommand = command;` after the existing `PlayerCommandGuard.Track` line. Branches that succeed without issuing a `UnitCommand` (`ExecuteToggleActivatable`, the inventory/Rulebook fallback paths, `DoNothing`) leave `issuedCommand = null`.
- `WrathTactics/Engine/TacticsEvaluator.cs` — `EvaluateUnit` resolves the gate before calling `TryExecuteRules`. `TryExecuteRules` gains a `RuleListSource source` and `int priorityLimit` parameter (the signature already passes a `string source` for log labels — replace it with the enum and let the log site call `source.ToString()` or a small helper). On successful `CommandExecutor.Execute`, `TryExecuteRules` calls `ActiveRuleTracker.Record` when `issuedCmd != null`. Comment at L90-92 explaining the prior "self-interruption … intentional (DAO semantics)" rationale is rewritten to match the new behavior. `Reset()` adds `ActiveRuleTracker.Reset()`. The combat-start and combat-end transitions in `Tick` add `ActiveRuleTracker.Reset()` next to the existing `PlayerCommandGuard.Reset()` calls.

**Untouched but worth re-reading before starting:**
- `WrathTactics/Engine/PlayerCommandGuard.cs` — the foreign-cast guard stays in place. The new tracker is a separate concern; do not merge them. The comment on `EvaluateUnit` L90-92 conflates the two — make sure the rewritten comment keeps the distinction crisp.
- `WrathTactics/Models/TacticsRule.cs` — confirms `entry.Id` is a `string` GUID property that survives presets/linked rules (preset-linked entries each have their own `Id`; only `PresetId` is shared). The gate uses `entry.Id` as the lookup key, which is exactly right.
- `WrathTactics/Properties/AssemblyInfo.cs` — already exposes internals to `WrathTactics.Tests`. No further wiring needed for the `internal static Resolve` helper.

---

## Task 1: Add `RuleListSource` enum

**Files:**
- Create: `WrathTactics/Engine/RuleListSource.cs`

- [ ] **Step 1: Create the enum file**

```csharp
namespace WrathTactics.Engine {
    /// <summary>
    /// Source list of a tactics rule, used by ActiveRuleTracker to derive priority
    /// gates. Globals are unconditionally higher priority than Characters — the
    /// evaluator iterates GlobalRules before character rules each tick.
    /// </summary>
    public enum RuleListSource {
        Global,
        Character,
    }
}
```

- [ ] **Step 2: Build to verify the new file compiles**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/RuleListSource.cs
git commit -m "feat(engine): add RuleListSource enum for priority gating"
```

---

## Task 2: Write failing tests for `ActiveRuleTracker.Resolve`

**Files:**
- Create: `WrathTactics.Tests/ActiveRuleTrackerTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System.Collections.Generic;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class ActiveRuleTrackerTests {
        static TacticsRule R(string id) => new TacticsRule { Id = id };

        static List<TacticsRule> List(params string[] ids) {
            var list = new List<TacticsRule>(ids.Length);
            foreach (var id in ids) list.Add(R(id));
            return list;
        }

        [Fact]
        public void active_in_globals_middle_gates_globals_below_and_skips_chars() {
            var globals = List("g0", "g1", "g2", "g3");
            var chars   = List("c0", "c1");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "g2", globals, chars);

            Assert.False(res.Stale);
            Assert.Equal(2, res.GlobalGate);   // i < 2  => g0, g1 only
            Assert.Equal(0, res.CharGate);     // i < 0  => no chars at all
        }

        [Fact]
        public void active_in_globals_top_blocks_everything() {
            var globals = List("g0", "g1");
            var chars   = List("c0");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "g0", globals, chars);

            Assert.False(res.Stale);
            Assert.Equal(0, res.GlobalGate);   // nothing higher than g0
            Assert.Equal(0, res.CharGate);
        }

        [Fact]
        public void active_in_chars_lets_all_globals_run_and_gates_chars_below() {
            var globals = List("g0", "g1");
            var chars   = List("c0", "c1", "c2");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Character, "c1", globals, chars);

            Assert.False(res.Stale);
            Assert.Equal(int.MaxValue, res.GlobalGate);  // no gate on globals
            Assert.Equal(1, res.CharGate);               // only c0
        }

        [Fact]
        public void active_in_chars_top_lets_all_globals_run_and_blocks_chars_below() {
            var globals = List("g0");
            var chars   = List("c0", "c1");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Character, "c0", globals, chars);

            Assert.False(res.Stale);
            Assert.Equal(int.MaxValue, res.GlobalGate);
            Assert.Equal(0, res.CharGate);
        }

        [Fact]
        public void active_id_missing_from_globals_is_stale() {
            var globals = List("g0", "g1");
            var chars   = List("c0");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "ghost", globals, chars);

            Assert.True(res.Stale);
        }

        [Fact]
        public void active_id_missing_from_chars_is_stale() {
            var globals = List("g0");
            var chars   = List("c0", "c1");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Character, "ghost", globals, chars);

            Assert.True(res.Stale);
        }

        [Fact]
        public void empty_lists_with_global_lookup_are_stale() {
            var globals = new List<TacticsRule>();
            var chars   = new List<TacticsRule>();

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "g0", globals, chars);

            Assert.True(res.Stale);
        }

        [Fact]
        public void active_in_globals_does_not_use_chars_id_collision() {
            // Sanity: an entry-id only present in chars must not match when activeSource = Global.
            var globals = List("g0");
            var chars   = List("c0", "g0");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "c0", globals, chars);

            Assert.True(res.Stale);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail with "type or member not defined"**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`
Expected: Build error referencing `ActiveRuleTracker` (type not found).

---

## Task 3: Implement `ActiveRuleTracker` (state + pure helper)

**Files:**
- Create: `WrathTactics/Engine/ActiveRuleTracker.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Per-unit memory of the last rule that issued a UnitCommand. Used by
    /// TacticsEvaluator to gate evaluation while a mod-issued command is still
    /// in flight: only rules strictly above the active rule (in global-then-
    /// character priority order) may preempt; lower-priority rules wait until
    /// the current command finishes. This is true DAO-style tactics behaviour
    /// (higher-prio interrupt = ok, lower-prio interrupt = forbidden).
    ///
    /// PlayerCommandGuard remains responsible for the orthogonal foreign-cast
    /// gate (player- or other-mod-issued casts in the Standard slot).
    /// </summary>
    public static class ActiveRuleTracker {
        public struct Entry {
            public RuleListSource Source;
            public string EntryId;
            public UnitCommand Command;
        }

        public struct Resolution {
            /// <summary>True iff the active EntryId no longer exists in its list
            /// (e.g. user deleted the rule mid-combat). Caller should Clear().</summary>
            public bool Stale;
            /// <summary>Exclusive upper bound for the globals iteration.
            /// int.MaxValue = no gate; 0 = skip all globals.</summary>
            public int GlobalGate;
            /// <summary>Exclusive upper bound for the character-rules iteration.</summary>
            public int CharGate;
        }

        static readonly Dictionary<string, Entry> activeByUnit = new Dictionary<string, Entry>();

        public static void Record(UnitEntityData unit, RuleListSource source, string entryId, UnitCommand cmd) {
            if (unit == null || cmd == null || string.IsNullOrEmpty(entryId)) return;
            activeByUnit[unit.UniqueId] = new Entry {
                Source = source,
                EntryId = entryId,
                Command = cmd,
            };
        }

        /// <summary>
        /// Returns the active entry iff the tracked command is still in flight
        /// (not null and not IsFinished). Auto-clears finished/null commands.
        /// </summary>
        public static Entry? GetActive(UnitEntityData unit) {
            if (unit == null) return null;
            if (!activeByUnit.TryGetValue(unit.UniqueId, out var entry)) return null;
            if (entry.Command == null || entry.Command.IsFinished) {
                activeByUnit.Remove(unit.UniqueId);
                return null;
            }
            return entry;
        }

        public static void Clear(UnitEntityData unit) {
            if (unit == null) return;
            activeByUnit.Remove(unit.UniqueId);
        }

        public static void Reset() {
            activeByUnit.Clear();
        }

        /// <summary>
        /// Pure helper: given the active tracker entry and the two rule lists,
        /// return per-list priority gates. globalRules has priority over charRules.
        /// </summary>
        internal static Resolution Resolve(
            RuleListSource activeSource,
            string activeEntryId,
            IReadOnlyList<TacticsRule> globalRules,
            IReadOnlyList<TacticsRule> charRules)
        {
            var list = activeSource == RuleListSource.Global ? globalRules : charRules;
            int idx = -1;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].Id == activeEntryId) { idx = i; break; }
            }
            if (idx < 0) {
                return new Resolution {
                    Stale = true,
                    GlobalGate = int.MaxValue,
                    CharGate = int.MaxValue,
                };
            }
            if (activeSource == RuleListSource.Global) {
                return new Resolution { GlobalGate = idx, CharGate = 0 };
            }
            return new Resolution { GlobalGate = int.MaxValue, CharGate = idx };
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`
Expected: All 8 `ActiveRuleTrackerTests` pass, and existing tests still pass.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/ActiveRuleTracker.cs WrathTactics.Tests/ActiveRuleTrackerTests.cs
git commit -m "feat(engine): ActiveRuleTracker + Resolve priority-gate helper"
```

---

## Task 4: Plumb `out UnitCommand` through `CommandExecutor`

**Files:**
- Modify: `WrathTactics/Engine/CommandExecutor.cs:17-42` (public `Execute`)
- Modify: `WrathTactics/Engine/CommandExecutor.cs:50-108` (`ExecuteCastSpell`)
- Modify: `WrathTactics/Engine/CommandExecutor.cs:133-169` (`ExecuteUseItem`)
- Modify: `WrathTactics/Engine/CommandExecutor.cs:171-190` (`ExecuteToggleActivatable`)
- Modify: `WrathTactics/Engine/CommandExecutor.cs:192-245` (`ExecuteHeal`)
- Modify: `WrathTactics/Engine/CommandExecutor.cs:263-299` (`ExecuteThrowSplash`)
- Modify: `WrathTactics/Engine/CommandExecutor.cs:301-309` (`ExecuteAttack`)

- [ ] **Step 1: Change `Execute` to expose the issued command**

Replace the public dispatch:

```csharp
public static bool Execute(ActionDef action, UnitEntityData owner, ResolvedTarget target, out UnitCommand issuedCommand) {
    issuedCommand = null;
    try {
        switch (action.Type) {
            case ActionType.CastSpell:
            case ActionType.CastAbility:
                return ExecuteCastSpell(action, owner, target, out issuedCommand);
            case ActionType.UseItem:
                return ExecuteUseItem(action.AbilityId, owner, target, out issuedCommand);
            case ActionType.ToggleActivatable:
                return ExecuteToggleActivatable(action.AbilityId, owner, action.ToggleMode);
            case ActionType.AttackTarget:
                return ExecuteAttack(owner, target.Unit, out issuedCommand);
            case ActionType.Heal:
                return ExecuteHeal(action, owner, target.Unit, out issuedCommand);
            case ActionType.ThrowSplash:
                return ExecuteThrowSplash(action, owner, target.Unit);
            case ActionType.DoNothing:
                return true;
            default:
                return false;
        }
    } catch (Exception ex) {
        Log.Engine.Error(ex, $"Failed to execute {action.Type} for {owner.CharacterName}");
        return false;
    }
}
```

Note: `ExecuteToggleActivatable`, `ExecuteThrowSplash`, and the `DoNothing`/`default` arms intentionally don't take or set `issuedCommand` — toggles flip a flag (no `UnitCommand`), and `ThrowSplash` uses `Rulebook.Trigger` with a synthetic `AbilityData` (no `UnitCommand` queued either). Leaving `issuedCommand = null` for those is the desired behaviour: the gate should not engage for fire-and-forget actions.

Add `using Kingmaker.UnitLogic.Commands.Base;` at the top of the file (next to the existing `using Kingmaker.UnitLogic.Commands;`) so `UnitCommand` resolves without ambiguity.

- [ ] **Step 2: Update `ExecuteCastSpell` to surface the queued command**

Change the signature to `static bool ExecuteCastSpell(ActionDef action, UnitEntityData owner, ResolvedTarget target, out UnitCommand issuedCommand)`. Initialise `issuedCommand = null;` as the first line of the method body.

In the spellbook/wand branch (currently L88-98), after `PlayerCommandGuard.Track(owner, command);` add:

```csharp
issuedCommand = command;
```

Do **not** set `issuedCommand` in the inventory branch (L72-86) or the `Rulebook.Trigger` fallback (L100-107) — those don't queue a `UnitCommand` and the gate must not engage for them.

- [ ] **Step 3: Update `ExecuteUseItem` to surface the queued command**

Change the signature to `static bool ExecuteUseItem(string abilityGuid, UnitEntityData owner, ResolvedTarget target, out UnitCommand issuedCommand)`. Initialise `issuedCommand = null;` as the first line.

In the equipped-source branch (currently L158-168), after `PlayerCommandGuard.Track(owner, command);` add:

```csharp
issuedCommand = command;
```

Inventory branch unchanged (no `UnitCommand` queued).

- [ ] **Step 4: Update `ExecuteHeal` to surface the queued command**

Change the signature to `static bool ExecuteHeal(ActionDef action, UnitEntityData owner, UnitEntityData target, out UnitCommand issuedCommand)`. Initialise `issuedCommand = null;` as the first line.

In the animated-cast branch (currently L228-235), after `PlayerCommandGuard.Track(owner, command);` add:

```csharp
issuedCommand = command;
```

Inventory and `Rulebook.Trigger` fallback branches unchanged.

- [ ] **Step 5: Update `ExecuteAttack` to surface the queued command**

Change the signature to `static bool ExecuteAttack(UnitEntityData owner, UnitEntityData target, out UnitCommand issuedCommand)`. Initialise `issuedCommand = null;` as the first line, then after `PlayerCommandGuard.Track(owner, command);` add:

```csharp
issuedCommand = command;
```

- [ ] **Step 6: Build to verify everything compiles**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.` (TacticsEvaluator will still call the old `Execute(action, unit, target)` signature — compiler error is expected there if we stopped here. The next task fixes it.)

If the build fails *only* at `TacticsEvaluator.cs:147` complaining about a missing `out` argument, that is the expected state — proceed to Task 5. If anything else fails, fix it before continuing.

- [ ] **Step 7: Commit**

```bash
git add WrathTactics/Engine/CommandExecutor.cs
git commit -m "refactor(engine): CommandExecutor.Execute returns issued UnitCommand via out param"
```

---

## Task 5: Integrate `ActiveRuleTracker` into `TacticsEvaluator`

**Files:**
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs:27-44` (combat-start/end transitions — add `ActiveRuleTracker.Reset()`)
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs:89-106` (`EvaluateUnit` — resolve gates)
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs:108-154` (`TryExecuteRules` — `RuleListSource source`, `int priorityLimit`, `Record` on success)
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs:161-167` (`Reset` — add tracker reset)

- [ ] **Step 1: Reset the tracker on combat transitions**

In the combat-end branch (L27-31), add the tracker reset next to the existing guard reset:

```csharp
if (!inCombat && wasInCombat) {
    wasInCombat = false;
    PlayerCommandGuard.Reset();
    ActiveRuleTracker.Reset();
    Log.Engine.Info("Combat ended");
}
```

Mirror the same addition in the combat-start branch (L34-44):

```csharp
if (inCombat && !wasInCombat) {
    wasInCombat = true;
    combatStartTime = gameTimeSec;
    PlayerCommandGuard.Reset();
    ActiveRuleTracker.Reset();
    Log.Engine.Info("Combat started");
    // ...rest unchanged...
}
```

- [ ] **Step 2: Rewrite `EvaluateUnit` to derive gates from the tracker**

Replace the body of `EvaluateUnit` (currently L89-106) with:

```csharp
static void EvaluateUnit(UnitEntityData unit, TacticsConfig config, float gameTimeSec, bool inCombat) {
    // Foreign-cast gate: player- or other-mod-issued casts in the Standard slot
    // suppress evaluation entirely. Orthogonal to our own in-flight rule (handled
    // below via ActiveRuleTracker).
    if (PlayerCommandGuard.HasForeignActiveCommand(unit)) {
        Log.Engine.Trace($"  Skip {unit.CharacterName}: player/foreign command active");
        return;
    }

    var globalRules = config.GlobalRules;
    var charRules = config.GetRulesForCharacter(unit.UniqueId);

    // Priority gate: while our own previously-issued UnitCommand is still in flight,
    // only rules strictly above it in the global-then-character priority order may
    // preempt. Lower-priority matches wait until the current command finishes.
    int globalGate = int.MaxValue;
    int charGate = int.MaxValue;
    var active = ActiveRuleTracker.GetActive(unit);
    if (active.HasValue) {
        var res = ActiveRuleTracker.Resolve(
            active.Value.Source, active.Value.EntryId, globalRules, charRules);
        if (res.Stale) {
            // Entry was deleted/edited out of its list mid-combat — drop the gate.
            ActiveRuleTracker.Clear(unit);
        } else {
            globalGate = res.GlobalGate;
            charGate = res.CharGate;
            Log.Engine.Trace($"  {unit.CharacterName}: active rule {active.Value.Source}/{active.Value.EntryId} -> gate G<{globalGate} C<{charGate}>");
        }
    }

    Log.Engine.Trace($"  Evaluating {unit.CharacterName} (hp={unit.HPLeft}/{unit.Stats.HitPoints.ModifiedValue}, id={unit.UniqueId}, inCombat={inCombat})");

    if (TryExecuteRules(globalRules, unit, RuleListSource.Global, gameTimeSec, inCombat, globalGate))
        return;
    TryExecuteRules(charRules, unit, RuleListSource.Character, gameTimeSec, inCombat, charGate);
}
```

- [ ] **Step 3: Update `TryExecuteRules` to honor `priorityLimit` and record fires**

Replace `TryExecuteRules` (currently L108-154) with:

```csharp
static bool TryExecuteRules(List<TacticsRule> rules, UnitEntityData unit,
    RuleListSource source, float gameTimeSec, bool inCombat, int priorityLimit) {
    int upper = System.Math.Min(rules.Count, priorityLimit);
    for (int i = 0; i < upper; i++) {
        var entry = rules[i];
        if (!entry.Enabled) continue;

        var rule = PresetRegistry.Resolve(entry);

        // Out-of-combat opt-in gate. Rules without a Combat.IsInCombat==false
        // condition keep their pre-1.7.0 behavior (in-combat-only).
        if (!inCombat && !RuleEnabledOutOfCombat(rule)) {
            continue;
        }

        // Check cooldown — key on entry.Id so linked copies cooldown independently
        var cooldownKey = (unit.UniqueId, entry.Id);
        float cooldownSec = rule.CooldownRounds * 6f;
        if (cooldowns.TryGetValue(cooldownKey, out float lastFired)) {
            if (gameTimeSec - lastFired < cooldownSec) {
                Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\": on cooldown ({gameTimeSec - lastFired:F1}s / {cooldownSec:F0}s)");
                continue;
            }
        }

        ConditionEvaluator.ClearMatchedEntities();

        bool match = ConditionEvaluator.Evaluate(rule, unit);
        if (!match) {
            Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): conditions not met");
            continue;
        }

        var target = TargetResolver.Resolve(rule.Target, unit);

        if (!ActionValidator.CanExecute(rule.Action, unit, target)) {
            Log.Engine.Warn($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): MATCH but action not executable");
            continue;
        }

        if (CommandExecutor.Execute(rule.Action, unit, target, out var issuedCmd)) {
            cooldowns[cooldownKey] = gameTimeSec;
            if (issuedCmd != null) {
                ActiveRuleTracker.Record(unit, source, entry.Id, issuedCmd);
            }
            // Toggles / DoNothing / ThrowSplash succeed without queueing a UnitCommand:
            // they don't replace any prior tracker entry — a still-animating cast keeps
            // its gate. This is deliberate; see plan §Task 5 step 3.
            Log.Engine.Info($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): EXECUTED -> {FormatTarget(target)}");
            return true;
        }
    }
    return false;
}
```

Note: the `source` parameter changed from `string` to `RuleListSource`. The log lines previously used the string directly; `RuleListSource`'s default `ToString()` ("Global" / "Character") is fine for log output and matches the prior strings closely enough.

- [ ] **Step 4: Reset the tracker in the public `Reset()`**

In `Reset()` (currently L161-167) add a single line next to `cooldowns.Clear()`:

```csharp
public static void Reset() {
    lastTickTime = 0;
    combatStartTime = 0;
    wasInCombat = false;
    tickCounter = 0;
    cooldowns.Clear();
    ActiveRuleTracker.Reset();
}
```

- [ ] **Step 5: Build and run all tests**

Run:
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ \
&& ~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```
Expected: `Build succeeded.` followed by all tests passing (the existing `ActiveRuleTrackerTests`, plus `ConditionEvaluator.CompareCount`, `BuffBlueprintProvider`, `CommonBuffRegistry`, `RangeBrackets`).

If the build fails because `System.Math` is not in scope at the `TryExecuteRules` call site, add `using System;` at the top of `TacticsEvaluator.cs` (it should already be there — verify L1).

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/TacticsEvaluator.cs
git commit -m "feat(engine): gate eval to higher-priority rules while own command in flight

True DAO semantics — a rule whose UnitCommand is still !IsFinished
cannot be preempted by lower-priority rules. Higher-priority rules
above it in the global-then-character order remain free to interrupt.
Fixes the user-reported case where 'auto-attack biggest threat' at
the bottom of a ranger's list was overriding in-progress buff casts
above it.

Toggles, ThrowSplash, and DoNothing succeed without queueing a
UnitCommand and therefore don't replace a still-running tracker
entry — a buff cast keeps its gate even if a higher-priority toggle
fires alongside it."
```

---

## Task 6: Manual smoke test on Steam Deck

**Files:** none (runtime verification only)

- [ ] **Step 1: Deploy the Debug build**

Run: `./deploy.sh`
Expected: SCP completes; `wrath-tactics-*.log` will be re-created on next game launch.

- [ ] **Step 2: Reproduce the original bug report**

Set up a ranger (or any caster) with this rule order:

1. **Cast Bless** (or any short-cast buff) — condition: `Ally.HasBuff Bless = false` (or equivalent "missing buff" predicate)
2. *(optional)* one or two more buff rules below it
3. **Last rule:** Attack biggest threat — condition: `Combat.IsInCombat = true` (always-on)

Engage combat with the rule list active.

Expected behaviour after the fix:
- Rule 1 fires; the Bless cast plays to completion (animation reaches its natural end).
- Only after the Bless `UnitCommand` reports `IsFinished` does the auto-attack rule fire.
- In the mod session log (`<game>/Mods/WrathTactics/Logs/wrath-tactics-*.log`), you should see a single `EXECUTED -> ...` line for the Bless cast, then a Trace gate line on subsequent ticks (`gate G<… C<…>`), then the auto-attack `EXECUTED` once the cast finishes.

Failure modes to watch for:
- Bless still interrupted by attack mid-animation → gate not engaging; check that `issuedCmd` is non-null in the executed path (add a Trace log if unclear).
- Unit permanently stops acting after one cast → tracker not auto-clearing on `IsFinished`. Inspect `ActiveRuleTracker.GetActive`'s self-clear branch.
- Combat-end leaves a stale tracker entry that blocks next combat → confirm `ActiveRuleTracker.Reset()` is called in both combat transitions.

- [ ] **Step 3: Verify higher-priority interruption still works**

Reorder the rules:

1. **Heal self** — condition: `Self.HPPercent < 50%`
2. **Cast Bless** — condition: as above
3. **Attack biggest threat**

Take the ranger to ~80% HP and engage. Mid-Bless-cast, drop their HP below 50% (e.g. by stepping into a hazard or being attacked).

Expected: Rule 1 (Heal) interrupts the in-flight Bless cast. The cast does not need to complete first.

If the heal does NOT interrupt: gate is too restrictive — verify `Resolve` returns the correct `GlobalGate`/`CharGate` for an active rule at the cast's position (it should leave room for any rule above it).

- [ ] **Step 4: Verify out-of-combat behaviour is unchanged**

Out of combat, set up a single buff rule with a `Combat.IsInCombat = false` condition. Trigger the tick (wait `OutOfCombatTickIntervalSeconds`, default 2 s). Confirm the buff fires and is not interrupted by anything afterwards (out-of-combat ticks are sparse, so this is mostly a regression check that the OOC path still routes through the gate without misbehaving).

- [ ] **Step 5: Capture a log snippet for the release notes**

Pull a fresh log:
```bash
ssh deck-direct "ls -t '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/' | head -1"
```
Then `scp` it back. Look for the gate-trace lines and a clean `EXECUTED → finished → next-rule-EXECUTED` sequence. Save the snippet for the eventual release note / Nexus changelog.

---

## Task 7: Update CLAUDE.md with the new invariant

**Files:**
- Modify: `CLAUDE.md` (project root) — add an entry under "## Gotchas" describing the gate and the `ActiveRuleTracker` ⇄ `PlayerCommandGuard` separation of concerns.

- [ ] **Step 1: Add a gotcha entry**

After the existing `PlayerCommandGuard` gotcha (the one starting "`PlayerCommandGuard`: reference-tracks `Commands.Run` from `CommandExecutor`; gates `TacticsEvaluator.EvaluateUnit` on foreign active casts."), insert a sibling bullet:

```markdown
- **`ActiveRuleTracker` (true DAO gating)**: per-unit, records `(RuleListSource, entry.Id, UnitCommand)` whenever `CommandExecutor.Execute` queues a `UnitCommand` for a fired rule. While the tracked command is still `!IsFinished`, `TacticsEvaluator.EvaluateUnit` derives a priority gate (`globalGate` / `charGate`) and `TryExecuteRules` iterates only rules with `i < gate` — strictly higher priority than the active rule in the global-then-character order. Lower-priority rules cannot preempt an in-flight command; higher-priority rules can (correct DAO semantic). Tracker self-clears when the command finishes, when the entry-id no longer resolves in its list (rule deleted), on `Reset()`, and at every combat-start/end transition. **Separate from `PlayerCommandGuard`** — that one gates on *foreign* active casts (player/other-mod), while `ActiveRuleTracker` gates on *our own* still-running commands. Don't merge them; they answer different questions.
- **Toggles / `ThrowSplash` / `DoNothing` don't replace the tracker entry** — they succeed without queueing a `UnitCommand`, so `issuedCmd == null` is returned through `CommandExecutor.Execute`'s out param. `TryExecuteRules` deliberately does NOT `Clear` the tracker in that case: an in-flight cast keeps its gate even when a higher-priority toggle fires alongside it. Replacing this with "clear on any successful Execute" reintroduces the original bug where the next tick re-fires a lower-priority rule and interrupts the still-animating cast.
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude): document ActiveRuleTracker gate and toggle-doesn't-clear invariant"
```

---

## Task 8: Release prep (optional — only if shipping immediately)

**Files:**
- Modify: `WrathTactics/Info.json` (version bump)
- Modify: `WrathTactics/WrathTactics.csproj` (`<Version>` bump)

- [ ] **Step 1: Bump version**

Per project convention (parent CLAUDE.md §Release Process and project CLAUDE.md `/release` pre-condition note): leave the csproj at the PRE-bump version and invoke `/release patch` to bump. Do **not** hand-edit the version on a feature branch — the `/release` script does it idempotently and won't double-bump if csproj already matches the target.

- [ ] **Step 2: Run `/release patch`**

Invoke the slash command from the wrath-tactics root. Follow the gated prompts.

Expected: bumps patch version (currently 1.15.0 → 1.15.1), commits + tags, pushes, triggers GitHub Release, Nexus upload runs via workflow.

- [ ] **Step 3: Update release notes**

In the release-notes section the `/release` flow generates, include the user-facing summary:

> Fixes a long-standing bug where a lower-priority rule (typically an "auto-attack biggest threat" rule at the bottom of the list) could interrupt a higher-priority rule's in-progress action (e.g. a buff cast). Tactics now behave like DAO: while a rule's command is in flight, only strictly higher-priority rules may preempt it.

---

## Self-Review

### Spec coverage

| Requirement | Task |
|---|---|
| Per-unit memory of last fired rule's command | Task 3 (`ActiveRuleTracker.Record` / `GetActive` / `Clear`) |
| Pure-logic priority gate (testable without `Game.Instance`) | Task 3 (`Resolve` + Task 2 tests) |
| Globals strictly higher priority than character rules | Task 3 (`Resolve` returns `GlobalGate = int.MaxValue` for active-in-chars; `CharGate = 0` for active-in-globals) + Task 2 tests |
| Strictly-higher rules may preempt; lower-or-equal must wait | Task 5 (gate in `TryExecuteRules`) |
| Auto-clear when tracked command finishes | Task 3 (`GetActive` self-clear) |
| Auto-clear when rule deleted/edited mid-combat | Task 5 step 2 (`if res.Stale → Clear`) |
| Reset on combat-start, combat-end, `Reset()` | Task 5 steps 1, 4 |
| `CommandExecutor` surfaces the queued `UnitCommand` | Task 4 |
| Toggles / `ThrowSplash` / `DoNothing` don't release the gate | Task 4 step 1 (signatures intentionally don't carry `out`) + Task 5 step 3 (caller only `Record`s when `issuedCmd != null`, never `Clear`s on success without command) |
| Misleading "self-interruption … intentional (DAO semantics)" comment removed | Task 5 step 2 (`EvaluateUnit` comment rewritten) |
| Foreign-cast gate (`PlayerCommandGuard`) preserved as separate concern | Task 5 step 2 (kept in place; new comment in tracker file documents the separation) |
| CLAUDE.md updated | Task 7 |

### Placeholder scan

No "TBD", "later", "appropriate error handling", or "similar to Task N" entries. Every code change is fully spelled out at its insertion site. The "if anything else fails" line in Task 4 step 6 is a debugging hint, not a placeholder — the expected build state is precisely described.

### Type / signature consistency

- `RuleListSource` introduced in Task 1, used identically in Tasks 2, 3, 5, 7. No drift.
- `ActiveRuleTracker.Resolution { Stale, GlobalGate, CharGate }` — same fields referenced in Task 2 tests and Task 5 caller.
- `ActiveRuleTracker.Entry { Source, EntryId, Command }` — same fields used in `GetActive` return type and `EvaluateUnit` consumer.
- `CommandExecutor.Execute(..., out UnitCommand issuedCommand)` — same signature used by `TryExecuteRules` (`out var issuedCmd`). All 4 sub-methods with new `out` params (`ExecuteCastSpell`, `ExecuteUseItem`, `ExecuteHeal`, `ExecuteAttack`) share the same pattern.
- `TryExecuteRules(..., RuleListSource source, ..., int priorityLimit)` — both new params used at the callsite in `EvaluateUnit`. Log lines route through `source.ToString()` implicitly.

### Risks not covered by tests (manual-only)

- Tracker correctness across actual Unity-tick boundaries (`UnitCommand.IsFinished` flip timing) — covered by Task 6 smoke tests.
- Interaction with `BubbleBuffsCompat.IsExecuting()` early-return at L60 — unchanged path, no risk.
- Interaction with foreign-cast gate when both fire on the same tick — both are early-returns, foreign-gate runs first (unchanged ordering), so no path interleaving.
- `PartyAndPets` iteration unchanged, pet semantics unchanged.

No spec gaps identified.
