# Action-Slot Economy in the Evaluator — Design

**Date:** 2026-09-04
**Origin:** Nexus report — a witch cannot combine a standard action with a move action in the same round. Rules `1. buff tank → 2. attack nearest → 3. cackle` execute rule 1, then stall: the attack occupies the standard slot for ~4 s while the move action sits unused, and cackle never fires. Moving cackle up starves the attack instead.
**Decision:** Minimal variant approved by user. `ActiveRuleTracker` is **out of bounds** — no re-keying, no signature change, no test churn. Non-Standard rules become exempt from the priority gate and from the early return; a per-tick slot budget prevents same-slot collisions. No config toggle: the new behavior is unconditional.

## Goal

A unit may spend its standard, move, swift and free actions in the same tick, as Pathfinder's action economy allows:

```
Rule 1  IF   Ally "tank" · has no buff · Protective Luck      → Cast Spell: Protective Luck   [Standard]
Rule 2  IF   Enemy · count · >= 1                             → Attack nearest                [Standard, skipped]
Rule 3  IF   Self · has no buff · Cackle                      → Cast Ability: Cackle          [Move,     fires]
```

Today rule 1 executes and evaluation ends for the tick; on subsequent ticks the priority gate blocks rules 2 and 3 outright until the in-flight command finishes. After this change rule 1 and rule 3 both fire in one pass, and rule 2 is skipped because the standard slot is already claimed.

This is not a niche request. Swift-action abilities are pervasive in Wrath — Judgment, Smite Evil, Arcane Strike, Mutagen, Bardic Performance — and every rule of that shape currently starves the attack rule or is starved by it.

## Root Cause (mod side)

Two places, both deliberate, neither aware of action types:

- `TacticsEvaluator.TryExecuteRules` returns `true` on the first successful execution → **one action per unit per tick**, regardless of slot. In-combat tick interval is 3 s against 6 s rounds, so this caps a unit at ~2 actions per round even when the engine would allow four.
- `ActiveRuleTracker` gates *every* lower-priority rule while any mod-issued command is in flight (`TacticsEvaluator.cs:112-126`), regardless of which slot that command occupies.

The reporter's "~4 seconds buffered" is the engine holding the attack command in the standard slot until `Cooldown.StandardAction` expires. That is correct engine behavior; the defect is that it also blocks the move-action rule.

## Engine Semantics (IL-verified)

`UnitCommand.CommandType`: `Free = 0, Standard = 1, Swift = 2, Move = 3`.

- **Slots are independent.** `UnitCommands.m_Commands` is an array indexed by `CommandType`; `GetCommand(type)` is a plain `ldelem.ref`. Commands of different types coexist.
- **`Run` interrupts same-slot only.** `UnitCommands.Run` calls `InterruptAndRemoveCommand(cmd.Type)`. A move command does not disturb a running standard cast. Two commands of the *same* type do stomp each other — this is what the per-tick budget must prevent.
- **`CanRunCommand` vetoes narrowly:** only `Type == Standard` while the owner has `UnitCondition` 55 (`CantUseStandardActions`), or an unconscious owner.
- **`AbilityData.RuntimeActionType`** is the authoritative per-ability slot. It starts from `Blueprint.ActionType` and applies two runtime adjustments: Swift → Standard when Quicken metamagic is present but the swift action is already spent, and Standard → Move under the `MythicAbilitiesAsMoveAction` flag. Using it instead of `Blueprint.ActionType` gets both cases right for free.
- **`UnitCombatState.HasCooldownForCommand(CommandType)`** has a real RTWP branch (not turn-based-only): Free → never on cooldown; Swift/Move → their own cooldown timers; Standard → `true` while a **Move** command is running, else `Cooldown.StandardAction > 0`.

  Note the asymmetry: a running move command blocks standard, but a running standard does **not** block move. "Cast, then cackle" is engine-legal; "cackle, then cast" makes the cast wait. The reporter's original rule order was already the better one.

We do not call `HasCooldownForCommand` (see Out of Scope) — it is documented here because it is the reason buffering is safe rather than a bug.

## Design

### 1. New component: `Engine/ActionSlots.cs`

Pure classification. No engine state, no side effects, fully unit-testable.

```csharp
internal static CommandType? Classify(ActionType type, CommandType? abilitySlot)
internal static bool IsGated(CommandType? slot)   // true iff slot == Standard
```

| `ActionType` | Slot | Source |
|---|---|---|
| `CastSpell` / `CastAbility` | `ability.RuntimeActionType` | `ResolveCastSpellChain` |
| `UseItem` | `ability.RuntimeActionType` | `FindUseItemSource` |
| `Heal` | `ability.RuntimeActionType` | `FindBestHeal` |
| `AttackTarget` | Standard | issues `UnitAttack` |
| `ThrowSplash` | Standard | see below |
| `SwitchWeaponSet` | Free | `UnitSwitchHandEquipmentSet.CommandType = Free` |
| `ToggleActivatable` | *none* (`null`) | sets `IsOn`, issues no command |
| `DoNothing` | Standard, plus hard stop | preserves today's semantics |
| unresolvable / unknown | **Standard** | safety net |

Two classifications deserve their reasoning recorded:

- **`ThrowSplash` claims Standard even though it issues no command.** It bypasses `Commands.Run` entirely (`Rulebook.Trigger(new RuleCastSpell(...))` plus manual stack consumption). Left unclassified it would claim no slot and therefore fire *in addition to* a cast and an attack every tick — free extra damage, and a behavior regression versus today, where its success ends the tick. Standard is the honest budget for a thrown flask.
- **Unresolvable → Standard is the safety net.** A classification bug degrades to today's behavior (gated, one-per-tick) rather than to an ungated free-for-all.

`ToggleActivatable` claiming no slot means toggle rules become gate-exempt: turning on Power Attack no longer waits for a cast to finish. A toggle rule stops matching once its activatable is in the requested state, so this does not spam.

### 2. `ActionValidator.CanExecute` gains `out CommandType? slot`

```csharp
public static bool CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target,
                              out CommandType? slot)
```

**No change to validation logic.** The dispatcher already resolves the `AbilityData` for every ability-backed action type; the slot falls out of `RuntimeActionType` on the object it already holds. Supporting changes:

- `CanUseItem` / `CanUseItemAtPoint` gain `out AbilityData ability` — both are private with one caller each, and both already hold the object via `FindUseItemSource`.
- `ResolveCastSpellChain` and `FindBestHeal` already return `AbilityData`; the dispatcher just stops discarding it.

Synthetic inventory `AbilityData` (`new AbilityData(usable.Ability, owner.Descriptor)` in `FindInventoryUsable`) carries a valid `Caster`, so `RuntimeActionType` resolves normally on it.

### 3. `TacticsEvaluator`: gate exemption + per-tick slot budget

`ActiveRuleTracker` is untouched — same keys, same `Resolve`, same `IsTrackedCommandDead`, same tests.

- The loop iterates the **full** rule list instead of `upper = min(rules.Count, priorityLimit)`. `priorityLimit` is applied per rule and **only when `IsGated(slot)`**, i.e. only to Standard-slot rules. Move/Swift/Free and slot-less rules bypass the gate.
- A `bool[4] slotUsed` budget (indexed by `(int)CommandType`) is created once per unit per tick and **shared across the global and character lists**. A rule whose slot is already claimed is skipped. This is what stops two swift rules in one tick from destroying each other via `InterruptAndRemoveCommand(Swift)`.
- A successful execution no longer ends evaluation. The only hard stop is `DoNothing`.
- `ActiveRuleTracker.Record` is called only for gated (Standard) rules, so the tracker's contents keep exactly their present meaning.

`EvaluateUnit` correspondingly stops returning after the global list succeeds; it passes the same budget into the character list.

**Ordering constraint:** the slot is only known once `ActionValidator.CanExecute` has resolved the `AbilityData`, so both the per-rule gate check and the budget check must run *after* validation and *before* execution. Sequence per rule: enabled → OOC gate → cooldown → conditions → target → validate (yields slot) → gate check → budget check → busy guard (§4) → execute.

**Precise claim about backward compatibility:** Standard rules behave as today *relative to other Standard rules* — same gate, same one-per-tick, same preemption. What changes is that the tick continues afterwards and non-Standard rules below now fire. That is the feature, not a side effect.

Rules below the gate now have their conditions evaluated every tick where previously the loop never reached them. This is safe: `ConditionEvaluator.ClearMatchedEntities()` runs before each rule and `EvaluateGroup` clears bucket latches at group start, so an evaluated-then-skipped rule leaks nothing into the next. The cooldown map is only written on execution, so gated rules still do not consume cooldown.

### 4. Cross-tick self-interrupt guard for non-Standard rules

Without a tracker entry, a move rule could re-fire on the next tick and interrupt its own still-running command. Stateless guard: skip a non-Standard rule when `unit.Commands.Raw[(int)slot]` holds an **unfinished `UnitUseAbility`**.

The guard is deliberately source-agnostic: it also skips the rule when the ability command in that slot was issued by the player or the engine, which is the correct outcome either way.

The `UnitUseAbility` restriction is load-bearing. The move slot is near-permanently occupied by engine-issued `UnitMoveTo` (approach, formation). Checking bare occupancy would mean cackle never fires while the witch is repositioning — precisely the over-block regression already documented for `PlayerCommandGuard`.

Consequence: running a move-action ability while the unit is walking cancels the walk (`Run` interrupts the move slot). That matches what the game does when a player clicks a move-action ability mid-walk.

### 5. Logging

The `EXECUTED` line gains the slot, and the skip paths get trace lines naming the reason:

```
Ember Rule 3 "Cackle" (Character): EXECUTED [Move] -> Ember
Ember Rule 2 "Attack" (Character): slot Standard already used this tick
Ember Rule 4 "Judgment" (Character): slot Swift busy (UnitUseAbility in flight)
```

Triage recipes in `claude-context/triage.md` need the new lines documented; "rule didn't fire" now has two additional legitimate causes.

## Out of Scope

Each of these was considered and cut deliberately:

- **`PlayerCommandGuard` stays unchanged.** Narrowing it so a manual player cast blocks only the standard slot is attractive and consistent with this feature, but the guard's narrowness is documented as load-bearing with its own regression history. Separate change, separate release.
- **No `HasCooldownForCommand` pre-check.** Commands whose action is still on cooldown keep being issued and buffered in their slot, firing the instant the action frees up. Under slot gating the occupied slot no longer blocks unrelated rules, so buffering is no longer harmful.
- **No preemption between Move/Swift rules.** Requires per-slot rule tracking, i.e. re-keying `ActiveRuleTracker`. Deferred; see Known Limitations.
- **No config toggle.** User decision: unconditional. Rollback path for a field defect is a version downgrade.

## Known Limitations

- A higher-priority move/swift rule cannot interrupt a lower-priority move/swift rule's in-flight command; it waits for the slot instead (guard in §4). Move and swift commands are near-instantaneous, so the window is milliseconds wide. Standard-action preemption — where it actually matters, e.g. an emergency heal cutting off an attack — is unchanged.
- `ThrowSplash` claims the standard slot in the tick budget but records no tracker entry, so it constrains only rules in the same tick, not across ticks. Same as today.
- Free-slot rules other than `SwitchWeaponSet` (i.e. toggles) claim nothing and are therefore unbounded per tick. Their conditions govern them.

## Sites Checklist

1. `WrathTactics/Engine/ActionSlots.cs` — new.
2. `WrathTactics/Engine/ActionValidator.cs` — `CanExecute` signature, slot derivation per branch, both the point-target and unit-target switches.
3. `WrathTactics/Engine/ActionValidator.UseItem.cs` — `CanUseItem` / `CanUseItemAtPoint` surface `AbilityData`.
4. `WrathTactics/Engine/TacticsEvaluator.cs` — `EvaluateUnit` budget creation and pass-through; `TryExecuteRules` full-list iteration, per-rule gate, budget check, non-Standard busy guard, `DoNothing` stop, `Record` restricted to gated rules, log lines.
5. `WrathTactics.Tests/ActionSlotsTests.cs` — new.
6. `claude-context/gotchas-engine.md` — slot semantics, the `UnitUseAbility` restriction rationale, `RuntimeActionType` as the classification source.
7. `claude-context/triage.md` — new skip reasons in the session log.
8. `WrathTactics/Info.json` + `WrathTactics/WrathTactics.csproj` — version bump (both files).

No i18n keys, no UI, no persistence, no enum additions — nothing in this change reaches `Models/Enums.cs` or config JSON.

## Testing

**Unit (`ActionSlotsTests.cs`):**

- Every `ActionType` maps to its expected slot, both with and without a supplied `abilitySlot`.
- Ability-backed types prefer the supplied `abilitySlot` over the fallback.
- `null` / unknown `abilitySlot` on an ability-backed type falls back to Standard.
- `IsGated` is true only for Standard; false for Free, Swift, Move and `null`.

**Regression evidence:** `ActiveRuleTrackerTests` must pass **unmodified**. That is the mechanical proof the fragile component was not touched.

**Deck smoke test** (the reporter's setup on a witch):

1. `buff ally → attack nearest → cackle` fires buff and cackle in one tick, skips the attack, and the attack lands on the following tick.
2. Reordering to `cackle → buff → attack` still yields all three over two ticks and no longer starves the attack.
3. A pure-Standard rule list (cast → attack → heal) behaves exactly as before the change — one action per tick, gate intact.
4. A rule list with two swift rules fires only one of them per tick, and the first is not cut short.
5. Emergency-heal preemption still works: a high-priority heal interrupts a low-priority attack.

## Risk & Rollback

The change is contained to classification plus the evaluation loop. The component with the regression history (`ActiveRuleTracker`, cause of the 1.17.4 "rules randomly stop firing" wedge and the lower-priority-preempt bug) is not modified, and its unchanged green test suite proves it.

The realistic failure mode is misclassification: an ability whose `RuntimeActionType` is not what a user expects fires in a slot they did not intend. The unresolvable-→-Standard fallback bounds the blast radius to today's behavior.

Blast radius is genuinely wider than "most users see nothing": swift-action rules are common, so many existing configs will see changed timing. That is the intended improvement, but it means the release notes should say so plainly rather than describing this as a bugfix.

Rollback is a version downgrade — no toggle by design.
