# Point-Target Summoning — Design

**Date:** 2026-04-19
**Status:** Approved, ready for implementation
**Target version:** 0.8.0 (after 0.7.0 HD ships)
**Depends on:** 0.7.0 (HD targeting) being merged first — both features touch `TargetResolver` and `Enums.cs`.

## Motivation

Wrath Tactics currently can't cast location-targeted spells. The command-execution path builds a `TargetWrapper(UnitEntityData)` and passes it to `UnitUseAbility.CreateCastCommand`. Summon spells (Summon Monster I–IX, Summon Nature's Ally, Planar Ally, etc.), AoE-control spells (Grease, Web, Wall of Fire), and AoE-damage spells (Cloudkill, Stinking Cloud) all take a `Vector3` point, not a unit. They silently no-op through our pipeline.

This spec covers summons only. AoE-control and AoE-damage are deferred (they need different intent modeling — cluster centroids, safe-zone avoidance for friendly fire — and warrant their own spec).

## Scope

- Add two new `TargetType` values that produce point targets: `PointAtSelf`, `PointAtConditionTarget`.
- Refactor `TargetResolver.Resolve` to return a `ResolvedTarget` struct that can carry either a `UnitEntityData` or a `Vector3` point.
- Teach `ActionValidator` and `CommandExecutor` to handle point targets by constructing `TargetWrapper(Vector3 point, ...)` instead of `TargetWrapper(UnitEntityData unit)`.
- Apply a ~1 grid-square (1.5 unit) offset from the anchor so summons don't spawn inside the anchor unit.

Not in scope: cluster centroids, multi-unit anchors, configurable offset distance, item-based point targets (scroll of summoning), AoE-control / AoE-damage automation.

## Game API Verification

IL-inspected via `ilspycmd -il` on the shipped `Assembly-CSharp.dll`:

```
Kingmaker.Utility.TargetWrapper ctors:
  .ctor (UnitEntityData unit)
  .ctor (Vector3 point, System.Nullable`1<float32> orientation = null, UnitEntityData unit = null)
  .field private initonly Vector3 m_Point
  .field public initonly EntityRef<UnitEntityData> UnitRef

Kingmaker.UnitLogic.Commands.UnitUseAbility.CreateCastCommand:
  static UnitCommand CreateCastCommand(AbilityData ability, TargetWrapper target)
  static UnitCommand CreateCastCommand(AbilityData ability, TargetWrapper target, UnitCommand.CommandType commandType)
```

So the engine's own API already accepts point-target wrappers; we just need to build the right `TargetWrapper` on our side. No hacky dispatch.

## Model Changes (`Models/Enums.cs`)

Append two values at the END of `TargetType` (numeric JSON-index stability — same constraint as the HD spec):

```csharp
public enum TargetType {
    // existing 0..19 unchanged (including 0.7.0's EnemyHighestHD, EnemyLowestHD):
    Self, AllyLowestHp, AllyWithCondition, AllyMissingBuff, EnemyNearest,
    EnemyLowestHp, EnemyHighestHp, EnemyHighestAC, EnemyLowestAC,
    EnemyHighestFort, EnemyLowestFort, EnemyHighestReflex, EnemyLowestReflex,
    EnemyHighestWill, EnemyLowestWill, EnemyHighestThreat,
    EnemyCreatureType, ConditionTarget,
    EnemyHighestHD, EnemyLowestHD,
    PointAtSelf,              // new, index 20
    PointAtConditionTarget    // new, index 21
}
```

No change to `TargetDef`, `ConditionSubject`, `ConditionProperty`.

## New Type (`Engine/ResolvedTarget.cs`)

```csharp
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace WrathTactics.Engine {
    // Carries the output of TargetResolver.Resolve. Exactly one of Unit/Point
    // is set for a valid resolution; None (default) means "no valid target".
    public readonly struct ResolvedTarget {
        public readonly UnitEntityData Unit;
        public readonly Vector3? Point;

        public static readonly ResolvedTarget None = default;

        public ResolvedTarget(UnitEntityData unit) { Unit = unit; Point = null; }
        public ResolvedTarget(Vector3 point) { Unit = null; Point = point; }

        public bool IsValid => Unit != null || Point.HasValue;
        public bool IsPoint => Point.HasValue;
    }
}
```

Struct, not class — zero heap allocation per-tick (resolution runs inside the tick loop, one per rule per unit per tick).

## Engine Changes

### `TargetResolver`

Signature change: `Resolve` now returns `ResolvedTarget` instead of `UnitEntityData`. All existing unit-based cases wrap their result: `return new ResolvedTarget(unit)` (or `ResolvedTarget.None` when the picker returns null). Two new cases:

```csharp
case TargetType.PointAtSelf: {
    var anchor = owner.Position;
    var offset = owner.OrientationDirection * SummonOffsetDistance;
    return new ResolvedTarget(anchor + offset);
}
case TargetType.PointAtConditionTarget: {
    var anchor = ConditionEvaluator.LastMatchedEnemy?.Position
               ?? ConditionEvaluator.LastMatchedAlly?.Position;
    if (!anchor.HasValue) return ResolvedTarget.None;
    var direction = (owner.Position - anchor.Value);
    direction.y = 0;
    var offset = direction.sqrMagnitude < 0.01f
        ? owner.OrientationDirection * SummonOffsetDistance  // defensive: owner == anchor
        : direction.normalized * SummonOffsetDistance;
    return new ResolvedTarget(anchor.Value + offset);
}
```

`SummonOffsetDistance` is a `private const float = 1.5f` on `TargetResolver`. Start at 1.5 (≈ one Pathfinder square); tune during smoke test if in-game spawning feels off.

`UnitEntityData.OrientationDirection` is the game's exposed facing vector (IL-verified: `instance Vector3 OrientationDirection` property on `Kingmaker.EntitySystem.Entities.UnitEntityData`, called from 5+ engine sites). No fallback needed.

### `ActionValidator`

New signature: `CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target)`. Logic:

1. `!target.IsValid` → `false`.
2. `target.IsPoint` → action must be castable at a point. For `CastSpell`/`CastAbility`: resolve the `AbilityData`, check `ability.CanTargetPoint == true` (IL-verified property on `Kingmaker.UnitLogic.Abilities.AbilityData`, engine-authoritative). For non-cast actions (AttackTarget, Heal, etc.): `false` (they inherently need a unit).
3. `!target.IsPoint` (unit target) → existing unit-centric validation path.

Rationale for the `CanTargetPoint` check: if a user wires a unit-target spell (e.g., Magic Missile) to `PointAtSelf`, we must reject at validation so the rule falls through. Without this, `CreateCastCommand(magicMissile, new TargetWrapper(point))` produces a silently-dropped cast, which `TryExecuteRules` would interpret as success (validator-strictness gotcha from `CLAUDE.md`).

Optional belt-and-braces: after computing the wrapper in `CommandExecutor`, call `ability.CanTarget(wrapper)` (engine's own multi-factor check — point-capable + range + friend/foe/self flags). Skip for MVP since `CanTargetPoint` + our resolution path covers the failure modes we care about; add if smoke tests reveal edge cases.

### `CommandExecutor`

New signature: `Execute(ActionDef action, UnitEntityData owner, ResolvedTarget target)`. For `CastSpell` / `CastAbility`:

```csharp
var wrapper = target.IsPoint
    ? new TargetWrapper(target.Point.Value)
    : new TargetWrapper(target.Unit);
var command = UnitUseAbility.CreateCastCommand(ability, wrapper);
owner.Commands.Run(command);
```

Non-cast actions (`AttackTarget`, `Heal`, etc.) still require a unit; they get `target.Unit` directly — validator already blocks the `IsPoint` case for them.

### `TacticsEvaluator`

Call-site update only: `var target = TargetResolver.Resolve(rule.Target, unit);` now yields a `ResolvedTarget`. Propagate through to `ActionValidator.CanExecute` and `CommandExecutor.Execute`. No logic change.

### Log/debug affordance

`TacticsEvaluator` currently logs `EXECUTED -> {target.CharacterName ?? "self"}`. Extend for point targets: `EXECUTED -> point({x:F1},{y:F1},{z:F1})` when `target.IsPoint`.

## UI Changes

`RuleEditorWidget`'s TargetType dropdown uses `Enum.GetNames(typeof(TargetType))` (seen in 0.7.0's code: `RuleEditorWidget.cs:598`), so `PointAtSelf` and `PointAtConditionTarget` surface automatically.

No dropdown-filtering needed: both point-targets are always valid UI choices; validator at runtime rejects when the action can't take a point.

Consistent with the HD spec's correction, dropdowns display raw enum names (`PointAtSelf`, `PointAtConditionTarget`) — no custom label pipeline exists in this codebase.

## Data Flow Example

```
Rule body (user authored):
  ConditionGroups[0]:
    Subject=Ally, Property=HpPercent, Operator=<, Value=30
  Action: CastSpell (SummonMonsterIV)
  Target: PointAtConditionTarget
     ↓
TacticsEvaluator.Tick → EvaluateUnit(Arasmes):
  ConditionEvaluator.Evaluate matches Camellia (HP 28% < 30)
  → ConditionEvaluator.LastMatchedAlly = Camellia
     ↓
TargetResolver.Resolve(PointAtConditionTarget, Arasmes):
  anchor = Camellia.Position
  direction = Arasmes.Position - Camellia.Position (on XZ plane)
  point = Camellia.Position + direction.normalized * 1.5
  returns ResolvedTarget(point)
     ↓
ActionValidator.CanExecute(CastSpell SMIV, Arasmes, resolved):
  ability = SummonMonsterIV; ability.Blueprint.CanTargetPoint == true
  resources OK, not on cooldown
  → true
     ↓
CommandExecutor.Execute(CastSpell SMIV, Arasmes, resolved):
  wrapper = new TargetWrapper(point)
  Arasmes.Commands.Run(UnitUseAbility.CreateCastCommand(ability, wrapper))
     ↓
Log: "Arasmes Rule N 'Bodyguard Summon' (Arasmes): EXECUTED -> point(12.3, 0.0, 45.7)"
```

## Error Handling

- `PointAtSelf`: `owner.Position` always valid (owner is non-null at evaluation time — `TacticsEvaluator` guards via `unit.IsInGame && unit.HPLeft > 0` before calling resolver).
- `PointAtConditionTarget` with no condition match: both `LastMatchedEnemy` and `LastMatchedAlly` are null after `ClearMatchedEntities` → resolver returns `ResolvedTarget.None` → validator rejects → rule falls through to the next one. No exception, no silent drop.
- Action is unit-target (e.g., AttackTarget) with point-target chosen by user: validator rejects (step 2 vs step 3 above). Same fall-through.
- Action is point-castable spell with unit-target chosen: existing unit-validation path; no change.
- Game's native placement collision resolution: handled by the engine — if our computed point is occluded or on unwalkable terrain, the summon blueprint's own radius-based placement may nudge the spawn. If this misbehaves during smoke test, adjust `SummonOffsetDistance` (first lever) before adding collision-aware logic (out of scope).

## Testing (manual Steam Deck)

No unit-test harness in this repo. Phases end on compile; the feature smoke test runs in-game.

1. **Summon at self** — caster with Summon Monster I–IX prepared.
   Rule: no conditions, `Action=CastSpell(Summon Monster III)`, `Target=PointAtSelf`.
   Expected: on combat start, caster casts SM III; monster spawns roughly 1 square in front of caster. Log shows `EXECUTED -> point(...)`.

2. **Summon at wounded ally** — caster with a dedicated bodyguard-summon rule.
   Rule: condition `Subject=Ally, Property=HpPercent, Operator=<, Value=40`; `Target=PointAtConditionTarget`; `Action=CastSpell(Summon Monster IV)`.
   Expected: when an ally drops below 40% HP, summon spawns between caster and that ally. Verify the monster's starting position is on the caster's side of the wounded ally (not behind the ally in the direction of enemies).

3. **Summon near specific enemy** — caster responding to an enemy type.
   Rule: condition `Subject=Enemy, Property=CreatureType, Value=Undead`; `Target=PointAtConditionTarget`; `Action=CastSpell(Summon Monster III)`.
   Expected: summon spawns on the caster's side of the matched undead, engaging it.

4. **Invalid pairing rejected** — user wires a unit-target spell to a point target.
   Rule: condition `Subject=Enemy, Property=HpPercent, Operator=<=, Value=100`; `Target=PointAtSelf`; `Action=CastSpell(Magic Missile)`.
   Expected: rule fails validator (`CanTargetPoint == false` for Magic Missile), falls through. No exception, no silent cast dropped by the engine.

5. **Fall-through on no match** — `PointAtConditionTarget` rule with no matching condition target.
   Rule: condition `Subject=Enemy, Property=CreatureType, Value=Dragon` in an encounter with no dragons.
   Expected: `ResolvedTarget.None`, rule doesn't fire, next rule in the list evaluates.

6. **Regression** — all existing 0.7.0 unit-target rules (AC, HP, HD, Saves, etc.) continue to fire identically. The `ResolvedTarget` refactor must not introduce behavior drift for unit targets.

## Risk & Compatibility

- **Struct-refactor blast radius**: all `TargetResolver.Resolve` callers need updating. Known call-sites after grep: `TacticsEvaluator.Tick` (single use). Low risk, caught at compile time. `ActionValidator.CanExecute` and `CommandExecutor.Execute` signature changes similarly compile-verified.
- **JSON stability**: only additive enum changes. 0.7.0 configs load identically in 0.8.0.
- **Offset heuristic**: 1.5 units may be wrong if Wrath's internal scale differs. Mitigation: value is a single `const` on `TargetResolver`; smoke-test adjustment is one-line. No architectural rework.
- **Native placement collision**: if the game's summon-blueprint placement logic fights our offset, that surfaces during smoke test as summons spawning at weird positions. First response: adjust `SummonOffsetDistance`. Second response: use `owner.Position` as anchor for self, skip offset entirely for condition-target and let the engine place.

## Out of Scope

- **AoE-control / AoE-damage placement** (Grease, Web, Wall of Fire, Cloudkill) — needs cluster centroids and safe-zone avoidance; separate spec.
- **Configurable offset distance** — single `const` suffices for MVP.
- **Item-based point targets** (scroll of summoning, point-target grenades) — item path is a separate code branch; revisit if demand emerges.
- **Multi-anchor points** (e.g., "midpoint between two enemies") — out of scope per the brainstorm.
- **Summon placement strategy variants** (behind caster, flanking, far from friendly AoE) — YAGNI until user asks.
