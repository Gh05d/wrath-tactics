# Action-Slot Economy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a companion spend its standard, move, swift and free actions in the same evaluator tick instead of exactly one action per tick.

**Architecture:** A new pure classifier (`Engine/ActionSlots.cs`) maps each rule's `ActionType` — plus, for ability-backed types, the resolved `AbilityData.RuntimeActionType` — onto the `UnitCommand.CommandType` slot the rule will occupy. `ActionValidator.CanExecute` surfaces that ability slot through a new `out` parameter (it already resolves the `AbilityData` and throws it away). `TacticsEvaluator.TryExecuteRules` then iterates the full rule list, applies the existing `ActiveRuleTracker` priority gate **only** to Standard-slot rules, and bounds the tick with a `bool[4]` per-slot budget shared across the global and character lists.

**Tech Stack:** C# 7.3+ / .NET Framework 4.8.1, HarmonyLib, Unity Mod Manager, xUnit 2.9 on the mono runner.

**Spec:** `docs/superpowers/specs/2026-09-04-action-slot-economy-design.md`

## Global Constraints

- **`ActiveRuleTracker.cs` must not be modified.** No re-keying, no signature change. `WrathTactics.Tests/ActiveRuleTrackerTests.cs` must stay byte-identical and pass — that is the regression evidence for the component with the 1.17.4 "rules randomly stop firing" history.
- **`PlayerCommandGuard.cs` must not be modified.** Its narrowness is load-bearing and out of scope.
- **No enum changes.** `Models/Enums.cs` is append-only because preset/config JSON persists numeric indices; nothing in this plan touches it.
- **No i18n keys, no UI, no persistence changes.** Nothing reaches `Localization/`, `UI/`, or `Persistence/`.
- **No config toggle.** The new behavior is unconditional by user decision.
- **Never call `owner.Commands.Run` directly** — always `CommandExecutor.RunVerified`. This plan does not add any command-issuing code, so no new call sites.
- **Build command (Linux):** `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/` — the `-p:SolutionDir` is required or `GamePath.props` fails silently and every game DLL reference breaks. `findstr` warnings are harmless on Linux.
- **Test command:** `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`
- **The mono test runner is flaky.** The first run after a build often crashes the host with mass failures whose counts vary run-to-run. That is the flake signature, not a regression. Loop until green: `for i in 1 2 3; do ~/.dotnet/dotnet test --no-build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/; done`. Trust the first all-green run; only trust failures that reproduce.
- **Code style:** K&R braces (opening brace on the same line), 4-space indent, `var` when the type is apparent.
- **Version bump requires two files:** `WrathTactics/Info.json` and `WrathTactics/WrathTactics.csproj`.

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `WrathTactics/Engine/ActionSlots.cs` | create | Pure `ActionType` (+ ability slot) → `UnitCommand.CommandType?` classification, and the `IsGated` predicate. No engine state. |
| `WrathTactics/Engine/ActionValidator.cs` | modify | `CanExecute` gains `out UnitCommand.CommandType? abilitySlot`; each ability-backed branch stops discarding the `AbilityData` it already resolved. |
| `WrathTactics/Engine/ActionValidator.UseItem.cs` | modify | `CanUseItem` / `CanUseItemAtPoint` surface the `AbilityData` they already hold. |
| `WrathTactics/Engine/TacticsEvaluator.cs` | modify | Full-list iteration, per-rule gate restricted to Standard, per-tick slot budget, non-Standard busy guard, `DoNothing` hard stop, slot-aware logging. |
| `WrathTactics.Tests/ActionSlotsTests.cs` | create | Unit tests for the classifier. |
| `claude-context/gotchas-engine.md` | modify | Slot semantics, `RuntimeActionType` as classification source, why the busy guard is `UnitUseAbility`-only. |
| `claude-context/triage.md` | modify | Two new legitimate "rule didn't fire" causes and their log lines. |
| `WrathTactics/Info.json`, `WrathTactics/WrathTactics.csproj` | modify | Version bump 1.28.0 → 1.29.0. |

---

### Task 1: The `ActionSlots` classifier

**Files:**
- Create: `WrathTactics/Engine/ActionSlots.cs`
- Test: `WrathTactics.Tests/ActionSlotsTests.cs`

**Interfaces:**
- Consumes: `WrathTactics.Models.ActionType` (existing enum), `Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType` (game enum: `Free = 0, Standard = 1, Swift = 2, Move = 3`).
- Produces:
  - `internal static UnitCommand.CommandType? ActionSlots.Classify(ActionType type, UnitCommand.CommandType? abilitySlot)`
  - `internal static bool ActionSlots.IsGated(UnitCommand.CommandType? slot)`

  `internal` is visible to the test project via the existing `[assembly: InternalsVisibleTo("WrathTactics.Tests")]` in `WrathTactics/Properties/AssemblyInfo.cs`.

- [ ] **Step 1: Write the failing test**

Create `WrathTactics.Tests/ActionSlotsTests.cs`:

```csharp
using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class ActionSlotsTests {
        [Theory]
        [InlineData(ActionType.CastSpell)]
        [InlineData(ActionType.CastAbility)]
        [InlineData(ActionType.UseItem)]
        [InlineData(ActionType.Heal)]
        public void ability_backed_types_use_the_supplied_ability_slot(ActionType type) {
            Assert.Equal(UnitCommand.CommandType.Swift,
                ActionSlots.Classify(type, UnitCommand.CommandType.Swift));
            Assert.Equal(UnitCommand.CommandType.Move,
                ActionSlots.Classify(type, UnitCommand.CommandType.Move));
            Assert.Equal(UnitCommand.CommandType.Free,
                ActionSlots.Classify(type, UnitCommand.CommandType.Free));
            Assert.Equal(UnitCommand.CommandType.Standard,
                ActionSlots.Classify(type, UnitCommand.CommandType.Standard));
        }

        [Theory]
        [InlineData(ActionType.CastSpell)]
        [InlineData(ActionType.CastAbility)]
        [InlineData(ActionType.UseItem)]
        [InlineData(ActionType.Heal)]
        public void ability_backed_types_fall_back_to_standard_when_slot_unknown(ActionType type) {
            Assert.Equal(UnitCommand.CommandType.Standard, ActionSlots.Classify(type, null));
        }

        [Theory]
        [InlineData(ActionType.AttackTarget)]
        [InlineData(ActionType.ThrowSplash)]
        [InlineData(ActionType.DoNothing)]
        public void fixed_standard_types_ignore_the_supplied_slot(ActionType type) {
            Assert.Equal(UnitCommand.CommandType.Standard, ActionSlots.Classify(type, null));
            Assert.Equal(UnitCommand.CommandType.Standard,
                ActionSlots.Classify(type, UnitCommand.CommandType.Move));
        }

        [Fact]
        public void switch_weapon_set_is_a_free_action() {
            Assert.Equal(UnitCommand.CommandType.Free,
                ActionSlots.Classify(ActionType.SwitchWeaponSet, null));
            Assert.Equal(UnitCommand.CommandType.Free,
                ActionSlots.Classify(ActionType.SwitchWeaponSet, UnitCommand.CommandType.Standard));
        }

        [Fact]
        public void toggle_activatable_claims_no_slot() {
            Assert.Null(ActionSlots.Classify(ActionType.ToggleActivatable, null));
            Assert.Null(ActionSlots.Classify(ActionType.ToggleActivatable,
                UnitCommand.CommandType.Standard));
        }

        [Fact]
        public void unknown_action_type_degrades_to_standard() {
            Assert.Equal(UnitCommand.CommandType.Standard, ActionSlots.Classify((ActionType)999, null));
        }

        [Fact]
        public void only_standard_is_gated() {
            Assert.True(ActionSlots.IsGated(UnitCommand.CommandType.Standard));
            Assert.False(ActionSlots.IsGated(UnitCommand.CommandType.Free));
            Assert.False(ActionSlots.IsGated(UnitCommand.CommandType.Swift));
            Assert.False(ActionSlots.IsGated(UnitCommand.CommandType.Move));
            Assert.False(ActionSlots.IsGated(null));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ --filter FullyQualifiedName~ActionSlotsTests
```

Expected: **compile error**, `CS0103: The name 'ActionSlots' does not exist in the current context` (one per call site). A compile failure is the correct "red" here — there is nothing to link against yet.

- [ ] **Step 3: Write the implementation**

Create `WrathTactics/Engine/ActionSlots.cs`:

```csharp
using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Maps a rule's ActionType — plus, for ability-backed types, the resolved
    /// AbilityData.RuntimeActionType — onto the UnitCommand slot the rule occupies.
    /// Pure: no engine state, no side effects, fully unit-testable.
    ///
    /// UnitCommand.CommandType is Free = 0, Standard = 1, Swift = 2, Move = 3, and
    /// UnitCommands.m_Commands is indexed by it, so (int)slot doubles as a budget index.
    /// </summary>
    internal static class ActionSlots {
        internal static UnitCommand.CommandType? Classify(
            ActionType type, UnitCommand.CommandType? abilitySlot) {
            switch (type) {
                case ActionType.CastSpell:
                case ActionType.CastAbility:
                case ActionType.UseItem:
                case ActionType.Heal:
                    // RuntimeActionType already folds in Quicken (Swift -> Standard once the
                    // swift action is spent) and MythicAbilitiesAsMoveAction (Standard -> Move).
                    // An unresolvable slot degrades to Standard so a classification miss
                    // behaves like the old one-action-per-tick evaluator instead of escaping
                    // the priority gate.
                    return abilitySlot ?? UnitCommand.CommandType.Standard;

                case ActionType.AttackTarget:
                    return UnitCommand.CommandType.Standard;

                // ThrowSplash bypasses Commands.Run entirely (Rulebook.Trigger plus manual
                // stack consumption), so it occupies no engine slot. It still claims Standard
                // in the tick budget: a thrown flask IS a standard action, and leaving it
                // unclaimed would let it fire on top of a cast AND an attack every tick.
                case ActionType.ThrowSplash:
                    return UnitCommand.CommandType.Standard;

                // UnitSwitchHandEquipmentSet is CommandType.Free (IL-verified).
                case ActionType.SwitchWeaponSet:
                    return UnitCommand.CommandType.Free;

                // Sets ActivatableAbility.IsOn — issues no command, so it claims nothing and
                // is exempt from both the gate and the budget. A toggle rule stops matching
                // once its activatable reaches the requested state, so this does not spam.
                case ActionType.ToggleActivatable:
                    return null;

                // Claiming Standard is cosmetic — DoNothing hard-stops the whole tick — but
                // it keeps the classification total and honest.
                case ActionType.DoNothing:
                    return UnitCommand.CommandType.Standard;

                default:
                    return UnitCommand.CommandType.Standard;
            }
        }

        /// <summary>
        /// True for the slot the ActiveRuleTracker priority gate governs. Only Standard-slot
        /// rules participate in DAO-style preemption; move/swift/free rules bypass the gate.
        /// </summary>
        internal static bool IsGated(UnitCommand.CommandType? slot) {
            return slot == UnitCommand.CommandType.Standard;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ --filter FullyQualifiedName~ActionSlotsTests
```

Expected: PASS, 15 tests (4 + 4 + 3 theory cases plus 4 facts).

If the run dies with a mono host crash or a `TypeLoadException` on `UnitCommand`, that is the known flaky-runner / stale-DLL-copy signature, not a real failure. Rebuild and loop:

```bash
~/.dotnet/dotnet build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
for i in 1 2 3; do ~/.dotnet/dotnet test --no-build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ --filter FullyQualifiedName~ActionSlotsTests; done
```

Trust the first all-green run.

- [ ] **Step 5: Run the whole suite to confirm nothing else broke**

```bash
for i in 1 2 3; do ~/.dotnet/dotnet test --no-build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/; done
```

Expected: all green, `ActiveRuleTrackerTests` included and unmodified.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/ActionSlots.cs WrathTactics.Tests/ActionSlotsTests.cs
git commit -F - <<'EOF'
feat(engine): classify rule actions by UnitCommand slot

ActionSlots maps ActionType (plus AbilityData.RuntimeActionType for
ability-backed types) onto the command slot a rule occupies, and marks
Standard as the only gated slot. Pure classification; no caller yet.

Unresolvable slots degrade to Standard so a misclassification behaves
like the current one-action-per-tick evaluator rather than escaping the
priority gate.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SsuT8h9y2UPVKeaz18Eg6H
EOF
```

---

### Task 2: `ActionValidator.CanExecute` surfaces the ability slot

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.UseItem.cs:10-34`
- Modify: `WrathTactics/Engine/ActionValidator.cs:8-60`

**Interfaces:**
- Consumes: `ActionSlots` is **not** used here — this task only surfaces the raw ability slot. `ResolveCastSpellChain(owner, target, action, out ItemEntity, out string)` and `FindBestHeal(owner, target, HealMode, HealSourceMask, HealEnergyType)` already return `AbilityData`; `FindUseItemSource(owner, abilityGuid, out ItemEntity)` already returns `AbilityData`.
- Produces:
  - `public static bool ActionValidator.CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target, out UnitCommand.CommandType? abilitySlot)` — `abilitySlot` is the resolved ability's `RuntimeActionType`, or `null` for action types that are not ability-backed (`AttackTarget`, `ToggleActivatable`, `ThrowSplash`, `SwitchWeaponSet`, `DoNothing`) and on every `false` return.
  - `static bool CanUseItemAtPoint(string abilityGuid, UnitEntityData owner, out AbilityData ability)`
  - `static bool CanUseItem(string abilityGuid, UnitEntityData owner, UnitEntityData target, out AbilityData ability)`

**Note:** there is exactly one caller of `CanExecute` (`TacticsEvaluator.cs:170`) and it is updated in Task 3. This task therefore leaves the build **broken** at that one line — Step 4 fixes it with a temporary `out _` so the task is independently verifiable, and Task 3 replaces it with the real usage.

- [ ] **Step 1: Surface the `AbilityData` from the UseItem validators**

In `WrathTactics/Engine/ActionValidator.UseItem.cs`, replace the two methods at lines 10-34 with:

```csharp
        static bool CanUseItemAtPoint(string abilityGuid, UnitEntityData owner, out AbilityData ability) {
            ability = null;
            var found = FindUseItemSource(owner, abilityGuid, out _);
            if (found == null) return false;
            if (!found.CanTargetPoint) return false;
            if (found.SourceItem != null && found.SourceItem.Charges <= 0) return false;
            if (!found.IsAvailable) {
                Log.Engine.Trace($"CanUseItemAtPoint: {owner.CharacterName} {found.Name} engine-unavailable ({found.GetUnavailableReason()})");
                return false;
            }
            ability = found;
            return true;
        }

        static bool CanUseItem(string abilityGuid, UnitEntityData owner, UnitEntityData target, out AbilityData ability) {
            ability = null;
            var found = FindUseItemSource(owner, abilityGuid, out _);
            if (found == null) return false;
            if (found.SourceItem != null && found.SourceItem.Charges <= 0) return false;
            // Inventory-source items rely on stack Count > 0, which FindUseItemSource already enforces.
            if (!found.IsAvailable) {
                Log.Engine.Trace($"CanUseItem: {owner.CharacterName} {found.Name} engine-unavailable ({found.GetUnavailableReason()})");
                return false;
            }
            if (target != null && !found.CanTarget(new TargetWrapper(target)))
                return false;
            ability = found;
            return true;
        }
```

`ability` is assigned only on the success path so a caller can never read a validator-rejected object.

- [ ] **Step 2: Add the `out` parameter to `CanExecute` and populate it**

In `WrathTactics/Engine/ActionValidator.cs`, add `using Kingmaker.UnitLogic.Commands.Base;` to the using block, then replace the whole `CanExecute` method (lines 8-60) with:

```csharp
        public static bool CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target,
                                      out UnitCommand.CommandType? abilitySlot) {
            abilitySlot = null;

            if (!target.IsValid && RequiresValidTarget(action.Type))
                return false;

            if (target.IsPoint) {
                switch (action.Type) {
                    case ActionType.CastSpell:
                    case ActionType.CastAbility: {
                        ItemEntity _unused;
                        string _unusedId;
                        var ability = ResolveCastSpellChain(owner, target, action, out _unused, out _unusedId);
                        if (ability == null) return false;
                        if (!ability.CanTargetPoint) {
                            Log.Engine.Trace($"CanCastAbilityAtPoint: {owner.CharacterName} ability '{ability.Name}' is not point-castable");
                            return false;
                        }
                        abilitySlot = ability.RuntimeActionType;
                        return true;
                    }
                    case ActionType.UseItem: {
                        if (!CanUseItemAtPoint(action.AbilityId, owner, out var itemAbility)) return false;
                        abilitySlot = itemAbility.RuntimeActionType;
                        return true;
                    }
                    default:
                        return false;
                }
            }

            var unit = target.Unit;
            switch (action.Type) {
                case ActionType.CastSpell:
                case ActionType.CastAbility: {
                    ItemEntity _unused;
                    string _unusedId;
                    var ability = ResolveCastSpellChain(owner, target, action, out _unused, out _unusedId);
                    if (ability == null) return false;
                    abilitySlot = ability.RuntimeActionType;
                    return true;
                }
                case ActionType.UseItem: {
                    if (!CanUseItem(action.AbilityId, owner, unit, out var itemAbility)) return false;
                    abilitySlot = itemAbility.RuntimeActionType;
                    return true;
                }
                case ActionType.ToggleActivatable:
                    return CanToggleActivatable(action.AbilityId, owner, action.ToggleMode);
                case ActionType.AttackTarget:
                    return unit != null && unit.HPLeft > 0;
                case ActionType.Heal: {
                    // Self-heal when no explicit target is resolved — mirrors ExecuteHeal's
                    // `target ?? owner` fallback. Auto-mode reads the unit for affinity check.
                    var heal = FindBestHeal(owner, unit ?? owner, action.HealMode, action.HealSources, action.HealEnergy);
                    if (heal == null) return false;
                    abilitySlot = heal.RuntimeActionType;
                    return true;
                }
                case ActionType.ThrowSplash:
                    return unit != null && SplashItemResolver.FindBest(owner, action.SplashMode).HasValue;
                case ActionType.SwitchWeaponSet:
                    return CanSwitchWeaponSet(owner, action.WeaponSetIndex);
                case ActionType.DoNothing:
                    return true;
                default:
                    return false;
            }
        }
```

Validation logic is unchanged throughout — every branch keeps its original condition, ordering and log line. The only additions are the `abilitySlot` assignments and the braces/locals needed to hold the already-resolved objects.

- [ ] **Step 3: Temporarily fix the one caller so the build compiles**

In `WrathTactics/Engine/TacticsEvaluator.cs:170`, change:

```csharp
                if (!ActionValidator.CanExecute(rule.Action, unit, target)) {
```

to:

```csharp
                if (!ActionValidator.CanExecute(rule.Action, unit, target, out _)) {
```

Task 3 replaces this line with the real slot usage.

- [ ] **Step 4: Build to verify it compiles**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded`. `findstr` and NU1900 warnings are expected and harmless.

- [ ] **Step 5: Run the test suite**

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```

Expected: all green (behavior is unchanged so far). Loop on a mono host crash per the Global Constraints.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs WrathTactics/Engine/ActionValidator.UseItem.cs WrathTactics/Engine/TacticsEvaluator.cs
git commit -F - <<'EOF'
refactor(engine): surface the ability's command slot from CanExecute

CanExecute already resolved the AbilityData for every ability-backed
action type and discarded it. It now hands back the ability's
RuntimeActionType through an out parameter so the evaluator can reason
about which command slot a rule will occupy.

Validation logic is untouched: same conditions, same ordering, same log
lines. Behavior is unchanged — the single caller discards the slot.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SsuT8h9y2UPVKeaz18Eg6H
EOF
```

---

### Task 3: Per-slot evaluation in `TacticsEvaluator`

**Files:**
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs` — `EvaluateUnit` and `TryExecuteRules`

**Interfaces:**
- Consumes: `ActionSlots.Classify` / `ActionSlots.IsGated` (Task 1); `ActionValidator.CanExecute(..., out UnitCommand.CommandType? abilitySlot)` (Task 2); `ActiveRuleTracker.GetActive` / `Resolve` / `Clear` / `Record` (**unchanged**); `CommandExecutor.Execute(ActionDef, UnitEntityData, ResolvedTarget, out UnitCommand)`.
- Produces: nothing consumed by later tasks. `TryExecuteRules`' return value changes meaning from "a rule executed" to "**stop evaluating this unit for this tick**" (only `DoNothing` sets it).

- [ ] **Step 1: Add the required usings**

In `WrathTactics/Engine/TacticsEvaluator.cs`, add to the using block (after `using Kingmaker.EntitySystem.Entities;`):

```csharp
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
```

`Kingmaker.UnitLogic.Commands` provides `UnitUseAbility`; `...Commands.Base` provides `UnitCommand`.

- [ ] **Step 2: Create the per-tick slot budget in `EvaluateUnit`**

At the end of `EvaluateUnit`, replace the `Evaluating ...` trace line and the two `TryExecuteRules` calls that follow it with:

```csharp
            Log.Engine.Trace($"  Evaluating {unit.CharacterName} (hp={unit.HPLeft}/{unit.Stats.HitPoints.ModifiedValue}, id={unit.UniqueId}, inCombat={inCombat})");

            // Per-tick action-slot budget, indexed by (int)UnitCommand.CommandType
            // (Free = 0, Standard = 1, Swift = 2, Move = 3). Shared across the global and
            // character lists so a standard-action rule in the global list still blocks a
            // standard-action rule in the character list within the same tick.
            var slotUsed = new bool[4];

            // TryExecuteRules now returns "stop evaluating this unit", not "something fired".
            if (TryExecuteRules(globalRules, unit, RuleListSource.Global, gameTimeSec, inCombat, globalGate, slotUsed))
                return;
            TryExecuteRules(charRules, unit, RuleListSource.Character, gameTimeSec, inCombat, charGate, slotUsed);
```

Everything before that in `EvaluateUnit` — the `PlayerCommandGuard` check and the whole `ActiveRuleTracker` gate block — stays exactly as it is.

- [ ] **Step 3: Rewrite `TryExecuteRules`**

Replace the entire `TryExecuteRules` method with:

```csharp
        // Returns true when evaluation of this unit must stop for the whole tick
        // (DoNothing only). A successful execution no longer ends the pass — the loop
        // continues so the unit can spend its remaining action slots.
        static bool TryExecuteRules(List<TacticsRule> rules, UnitEntityData unit,
            RuleListSource source, float gameTimeSec, bool inCombat, int priorityLimit,
            bool[] slotUsed) {
            for (int i = 0; i < rules.Count; i++) {
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

                // The slot is only known once the AbilityData is resolved, so validation
                // must run before the gate and budget checks.
                if (!ActionValidator.CanExecute(rule.Action, unit, target, out var abilitySlot)) {
                    Log.Engine.Warn($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): MATCH but action not executable");
                    continue;
                }

                var slot = ActionSlots.Classify(rule.Action.Type, abilitySlot);

                // Priority gate: only Standard-slot rules participate in DAO-style
                // preemption, because ActiveRuleTracker only ever records those.
                // Move/swift/free rules are exempt — that is the whole feature.
                if (ActionSlots.IsGated(slot) && i >= priorityLimit) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): gated by active rule (limit {priorityLimit})");
                    continue;
                }

                // Per-tick budget: one command per slot. Without it two swift rules in the
                // same tick would destroy each other via InterruptAndRemoveCommand(Swift).
                if (slot.HasValue && slotUsed[(int)slot.Value]) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): slot {slot.Value} already used this tick");
                    continue;
                }

                // Cross-tick self-interrupt guard. Non-Standard rules leave no tracker entry,
                // so without this they would re-fire next tick and cut off their own
                // still-running command.
                if (slot.HasValue && !ActionSlots.IsGated(slot) && IsSlotBusyWithAbility(unit, slot.Value)) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): slot {slot.Value} busy (UnitUseAbility in flight)");
                    continue;
                }

                if (CommandExecutor.Execute(rule.Action, unit, target, out var issuedCmd)) {
                    cooldowns[cooldownKey] = gameTimeSec;
                    if (slot.HasValue) slotUsed[(int)slot.Value] = true;
                    // Only gated (Standard) rules go into the tracker, so its contents keep
                    // exactly their present meaning and ActiveRuleTracker stays untouched.
                    if (issuedCmd != null && ActionSlots.IsGated(slot)) {
                        ActiveRuleTracker.Record(unit, source, entry.Id, issuedCmd);
                    }
                    Log.Engine.Info($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): EXECUTED [{SlotLabel(slot)}] -> {FormatTarget(target)}");

                    // DoNothing is the only hard stop: it means "this unit does nothing else
                    // this tick", which is exactly the pre-change semantics.
                    if (rule.Action.Type == ActionType.DoNothing) return true;
                }
            }
            return false;
        }

        // True when the given slot holds an unfinished ability command. Deliberately
        // restricted to UnitUseAbility: the Move slot is near-permanently occupied by
        // engine-issued UnitMoveTo (approach, formation), so a bare occupancy check would
        // mean a move-action ability never fires while the unit walks — the same over-block
        // regression already documented for PlayerCommandGuard. Source-agnostic on purpose:
        // it also skips when the ability command in that slot came from the player.
        static bool IsSlotBusyWithAbility(UnitEntityData unit, UnitCommand.CommandType slot) {
            var slots = unit.Commands?.Raw;
            if (slots == null) return false;
            int idx = (int)slot;
            if (idx < 0 || idx >= slots.Length) return false;
            return slots[idx] is UnitUseAbility cmd && !cmd.IsFinished;
        }

        static string SlotLabel(UnitCommand.CommandType? slot) {
            return slot.HasValue ? slot.Value.ToString() : "no-slot";
        }
```

- [ ] **Step 4: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded`.

If the DLL timestamp does not move despite the edits, `dotnet build` skipped the rebuild on a source-mtime miss. Force it: `touch WrathTactics/Engine/TacticsEvaluator.cs` and rebuild.

- [ ] **Step 5: Run the test suite — `ActiveRuleTrackerTests` must be green and unmodified**

```bash
git diff --exit-code WrathTactics/Engine/ActiveRuleTracker.cs WrathTactics.Tests/ActiveRuleTrackerTests.cs && echo "UNTOUCHED OK"
for i in 1 2 3; do ~/.dotnet/dotnet test --no-build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/; done
```

Expected: `UNTOUCHED OK` printed, then an all-green run. That combination is the regression evidence required by the spec.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/TacticsEvaluator.cs
git commit -F - <<'EOF'
feat(engine): spend every free action slot in one evaluation tick

The evaluator stopped after the first successful rule and gated every
lower-priority rule behind ActiveRuleTracker regardless of which command
slot was busy, so a unit could never combine a standard action with a
move or swift action. Reported on Nexus: "buff tank -> attack nearest ->
cackle" fired the buff, buffered the attack for ~4 s, and never cackled.

The loop now walks the full rule list and applies the priority gate only
to Standard-slot rules. A per-tick bool[4] budget, shared across the
global and character lists, keeps one command per slot so two swift rules
cannot interrupt each other. Non-Standard rules get a stateless
self-interrupt guard instead of a tracker entry: skip when the slot holds
an unfinished UnitUseAbility. DoNothing remains a hard stop.

ActiveRuleTracker and PlayerCommandGuard are untouched.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SsuT8h9y2UPVKeaz18Eg6H
EOF
```

---

### Task 4: Document the new semantics

**Files:**
- Modify: `claude-context/gotchas-engine.md` (append to the `## Commands & Trackers` section)
- Modify: `claude-context/triage.md` (append to the `## Log Locations & Recipes` section)

**Interfaces:**
- Consumes: the behavior implemented in Tasks 1-3. Produces nothing code-facing.

- [ ] **Step 1: Add the slot-economy gotchas**

Append these bullets to the end of the `## Commands & Trackers` section of `claude-context/gotchas-engine.md` (before the next `##` heading):

```markdown
- **Action slots are independent; only Standard is gated.** `UnitCommands.m_Commands` is an array indexed by `UnitCommand.CommandType` (`Free = 0, Standard = 1, Swift = 2, Move = 3`) and `Run` calls `InterruptAndRemoveCommand(cmd.Type)` — **same slot only**. Since v1.29.0 `TacticsEvaluator` walks the full rule list and applies the `ActiveRuleTracker` priority gate only to rules `ActionSlots.IsGated` says are Standard. `ActiveRuleTracker.Record` is likewise called only for Standard rules, so the tracker's contents mean exactly what they always did.
- **Classify from `AbilityData.RuntimeActionType`, never `Blueprint.ActionType`.** `RuntimeActionType` folds in Quicken metamagic (Swift → Standard once the swift action is spent) and the `MythicAbilitiesAsMoveAction` flag (Standard → Move). `ActionSlots.Classify` falls back to Standard when the slot is unresolvable, so a classification miss degrades to the old one-action-per-tick behavior instead of escaping the gate.
- **The per-tick slot budget is `bool[4]` and is shared across the global and character rule lists.** Without it, two swift rules in one tick would destroy each other through `InterruptAndRemoveCommand(Swift)`. `ThrowSplash` claims Standard in the budget even though it bypasses `Commands.Run` — otherwise a thrown flask fires *on top of* a cast and an attack every tick. `ToggleActivatable` claims nothing: it issues no command.
- **The non-Standard busy guard checks for `UnitUseAbility` only — never bare slot occupancy.** The Move slot is near-permanently filled by engine-issued `UnitMoveTo` (approach, formation), so a bare occupancy check would mean a move-action ability never fires while the unit is walking. Same over-block class as the `PlayerCommandGuard` regressions. Consequence by design: running a move-action ability mid-walk cancels the walk, exactly as clicking one does.
- **`HasCooldownForCommand` is deliberately NOT pre-checked.** Commands whose action is still on cooldown keep being issued and buffered in their slot, firing the instant it frees. Its RTWP branch is real (not turn-based-only) and asymmetric: a running **Move** command blocks Standard, but a running Standard does not block Move — so "cast, then cackle" works while "cackle, then cast" makes the cast wait.
```

- [ ] **Step 2: Add the new "rule didn't fire" causes**

Append to the end of the `## Log Locations & Recipes` section of `claude-context/triage.md` (before the next `##` heading):

```markdown
### "Rule didn't fire" — slot-economy causes (v1.29.0+)

A unit can now spend one command per action slot per tick. Two of the skip lines are legitimate and must not be chased as bugs:

```
<Name> Rule 2 "Attack" (Character): slot Standard already used this tick
<Name> Rule 4 "Judgment" (Character): slot Swift busy (UnitUseAbility in flight)
```

- **`slot X already used this tick`** — an earlier rule already spent that slot. The rule fires on a later tick. Working as intended; if the user wants the *other* rule instead, they need to reorder.
- **`slot X busy (UnitUseAbility in flight)`** — a non-Standard rule's own previous command is still running. Move/swift commands are near-instantaneous, so a persistent occurrence means something else is holding that slot.
- **`gated by active rule (limit N)`** — the classic priority gate; since v1.29.0 it only ever appears on Standard-slot rules. If it shows up on a rule the user believes is a move or swift action, the ability's `RuntimeActionType` disagrees with them (Quicken, mythic flags) or the classification fell back to Standard because the `AbilityData` did not resolve.

The `EXECUTED` line now carries the slot: `EXECUTED [Move] -> Ember`. `[no-slot]` means `ToggleActivatable`, which claims no slot at all.
```

- [ ] **Step 3: Commit**

```bash
git add claude-context/gotchas-engine.md claude-context/triage.md
git commit -F - <<'EOF'
docs: record slot-economy semantics and the new skip reasons

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SsuT8h9y2UPVKeaz18Eg6H
EOF
```

---

### Task 5: Version bump and Steam Deck smoke test

**Files:**
- Modify: `WrathTactics/Info.json:5`
- Modify: `WrathTactics/WrathTactics.csproj:7`

**Interfaces:**
- Consumes: everything from Tasks 1-4. Produces the deployable build.

**Note:** this is a **feature** release, so 1.28.0 → **1.29.0** (minor, not patch). Do **not** run `/release` on top of this commit — the release script bumps again on its own. See parent `wrath-mods/CLAUDE.md`.

- [ ] **Step 1: Bump both version files**

```bash
sed -i 's|<Version>1.28.0</Version>|<Version>1.29.0</Version>|' WrathTactics/WrathTactics.csproj
sed -i 's|"Version": "1.28.0"|"Version": "1.29.0"|' WrathTactics/Info.json
rtk proxy grep -n '1\.29\.0' WrathTactics/WrathTactics.csproj WrathTactics/Info.json
```

Expected: one hit in each file.

- [ ] **Step 2: Deploy to the Steam Deck**

```bash
./deploy.sh
```

If this fails with "no route to host", the deck is probably just suspended — the USB interface stays UP and `getent hosts steamdeck.local` still resolves. Ask the user to wake it and retry before concluding it is offline.

- [ ] **Step 3: Run the smoke test in-game**

Set up a witch (or any caster with a move-action ability such as Cackle) with:

```
Rule 1  Ally "tank" · has no buff · Protective Luck   → Cast Spell: Protective Luck
Rule 2  Enemy · count · >= 1                          → Attack nearest
Rule 3  Self · has no buff · Cackle                   → Cast Ability: Cackle
```

Verify each of the following:

1. Rule 1 and rule 3 both fire in the same tick; rule 2 is skipped with `slot Standard already used this tick` and lands on the next tick.
2. Reordering to `cackle → buff → attack` still produces all three over two ticks and no longer starves the attack.
3. A pure-Standard list (cast → attack → heal) behaves exactly as before: one action per tick, gate intact.
4. A list with two swift rules fires only one of them per tick, and the first is not cut short.
5. Emergency-heal preemption still works: a high-priority heal interrupts a low-priority attack.

Pull the session log for evidence:

```bash
ssh deck-direct "ls -t '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/' | head -1"
```

Then grep it for `EXECUTED \[` and the two new skip lines.

If the deck is unreachable, the release is not blocked (precedent 1.21.0-1.22.1) — but the missing smoke test must be stated in the Nexus reply and recorded in auto-memory, and the in-game test done once the deck is back.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj
git commit -F - <<'EOF'
chore: bump version to 1.29.0

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SsuT8h9y2UPVKeaz18Eg6H
EOF
```

---

## Release Notes Wording (for the GitHub release and the Nexus reply)

Per the spec's Risk section, this must **not** be described as a plain bugfix. Swift-action rules (Judgment, Smite Evil, Arcane Strike, Mutagen, Bardic Performance) are common, so many existing configs will see changed timing — that is the intended improvement, but users should be told plainly:

> Companions now use their standard, move, swift and free actions in the same pass instead of one action per evaluation tick. Rules are still evaluated top-down; a rule is skipped when the action slot it needs is already spent that tick. If you have swift-action rules (Judgment, Smite Evil, Arcane Strike, Bardic Performance) below an attack rule, they will now fire alongside the attack rather than waiting for it.
