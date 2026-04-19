# Point-Target Summoning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable Wrath Tactics to cast summon spells (and any other point-target ability) by introducing a `ResolvedTarget` abstraction that carries either a `UnitEntityData` or a `Vector3` point, plus two new `TargetType` values (`PointAtSelf`, `PointAtConditionTarget`).

**Architecture:** Three-phase refactor. Phase 1 introduces the `ResolvedTarget` struct and an atomic signature refactor of `TargetResolver` / `ActionValidator` / `CommandExecutor` / `TacticsEvaluator` — existing behavior preserved, all unit-target cases wrapped. Phase 2 appends the two new `TargetType` values with offset-aware position resolution. Phase 3 adds the point-target dispatch in validator (`AbilityData.CanTargetPoint`) and command executor (`new TargetWrapper(Vector3)`).

**Tech Stack:** C# / .NET 4.8.1 / Unity UGUI / Kingmaker game API

**Build command:** `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

**Deploy command:** `./deploy.sh`

**Spec:** `docs/superpowers/specs/2026-04-19-point-target-summoning-design.md`

**Depends on:** 0.7.0 HD targeting being merged first — both features touch `TargetResolver` and `Models/Enums.cs`. Do NOT start this plan until `v0.7.0` tag is on `origin` and the `master` branch is clean.

**Testing model:** No unit-test harness in this repo. Each phase ends with a compile; Phase 4 is a manual Steam Deck smoke test.

**Behavior-change note:** The refactor tightens an existing quirk. Currently, `CommandExecutor.ExecuteCastSpell` falls back to `new TargetWrapper(owner)` when the resolver returns null (e.g., `EnemyNearest` with no enemies present), causing a self-cast. After the refactor, `ResolvedTarget.None` fails `ActionValidator.CanExecute` and the rule falls through to the next one. This is a correctness improvement (a rule that says "cast Hold Person on nearest enemy" should NOT cast on self when there's no enemy). Call out in release notes.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `WrathTactics/Engine/ResolvedTarget.cs` | Create | Wrapper struct carrying Unit XOR Point + IsValid/IsPoint helpers |
| `WrathTactics/Engine/TargetResolver.cs` | Modify | `Resolve` returns `ResolvedTarget`; two new cases (`PointAtSelf`, `PointAtConditionTarget`); `SummonOffsetDistance` const |
| `WrathTactics/Engine/ActionValidator.cs` | Modify | `CanExecute` takes `ResolvedTarget`; `CanCastSpell`/`CanUseItem` learn the point branch via `AbilityData.CanTargetPoint`; internal helpers' signatures updated |
| `WrathTactics/Engine/CommandExecutor.cs` | Modify | `Execute` takes `ResolvedTarget`; `ExecuteCastSpell`/`ExecuteUseItem`/etc. pick the right `TargetWrapper` ctor for unit vs point |
| `WrathTactics/Engine/TacticsEvaluator.cs` | Modify | Call-site updates for resolver/validator/executor signatures; log line uses `ResolvedTarget` formatting |
| `WrathTactics/Models/Enums.cs` | Modify | Append `PointAtSelf` (index 20) and `PointAtConditionTarget` (index 21) to `TargetType` |
| `WrathTactics/Info.json` | Modify | Version bump to 0.8.0 |
| `WrathTactics/WrathTactics.csproj` | Modify | Version bump to 0.8.0 |

---

## Phase 1: Introduce `ResolvedTarget` and Atomic Signature Refactor

### Task 1: Create `Engine/ResolvedTarget.cs`

**Files:**
- Create: `WrathTactics/Engine/ResolvedTarget.cs`

- [ ] **Step 1: Write the file**

Write to `WrathTactics/Engine/ResolvedTarget.cs`:

```csharp
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace WrathTactics.Engine {
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

- [ ] **Step 2: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Struct has no consumers yet.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/ResolvedTarget.cs
git commit -m "$(cat <<'EOF'
feat(engine): add ResolvedTarget struct

Carries either a UnitEntityData (unit target) or a Vector3 (point target)
with an IsValid/IsPoint accessor pair. Enables TargetResolver to model
point-target summon spells uniformly with existing unit-target rules.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 2: Atomic refactor — switch resolver/validator/executor/tick to `ResolvedTarget`

**Files:**
- Modify: `WrathTactics/Engine/TargetResolver.cs`
- Modify: `WrathTactics/Engine/ActionValidator.cs`
- Modify: `WrathTactics/Engine/CommandExecutor.cs`
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs`

**Single commit** — intermediate states won't compile. Work through all steps before running the final build.

- [ ] **Step 1: Refactor `TargetResolver.Resolve` signature and wrap-at-boundary**

In `WrathTactics/Engine/TargetResolver.cs`, replace the top two methods (`Resolve` and `ResolveInternal`) with:

```csharp
        public static ResolvedTarget Resolve(TargetDef target, UnitEntityData owner) {
            var unit = ResolveInternal(target, owner);
            if (unit != null) {
                float dist = (unit.Position - owner.Position).magnitude;
                Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> {unit.CharacterName} " +
                    $"(type={target.Type}, dist={dist:F1}, inCombat={unit.IsInCombat}, visible={unit.IsVisibleForPlayer})");
                return new ResolvedTarget(unit);
            }
            Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> <none> (type={target.Type})");
            return ResolvedTarget.None;
        }

        static UnitEntityData ResolveInternal(TargetDef target, UnitEntityData owner) {
            switch (target.Type) {
                case TargetType.Self:               return owner;
                case TargetType.AllyLowestHp:       return GetAllyLowestHp(owner);
                case TargetType.AllyWithCondition:  return GetAllyWithCondition(owner, target.Filter);
                case TargetType.AllyMissingBuff:    return GetAllyMissingBuff(owner, target.Filter);
                case TargetType.EnemyNearest:       return GetEnemyNearest(owner);
                case TargetType.EnemyLowestHp:      return GetEnemyLowestHp(owner);
                case TargetType.EnemyHighestHp:     return GetEnemyHighestHp(owner);
                case TargetType.EnemyHighestAC:     return GetEnemyHighestAC(owner);
                case TargetType.EnemyLowestAC:      return GetEnemyLowestAC(owner);
                case TargetType.EnemyHighestFort:   return GetEnemyHighestFort(owner);
                case TargetType.EnemyLowestFort:    return GetEnemyLowestFort(owner);
                case TargetType.EnemyHighestReflex: return GetEnemyHighestReflex(owner);
                case TargetType.EnemyLowestReflex:  return GetEnemyLowestReflex(owner);
                case TargetType.EnemyHighestWill:   return GetEnemyHighestWill(owner);
                case TargetType.EnemyLowestWill:    return GetEnemyLowestWill(owner);
                case TargetType.EnemyHighestThreat: return GetEnemyHighestThreat(owner);
                case TargetType.EnemyCreatureType:  return GetEnemyByCreatureType(owner, target.Filter);
                case TargetType.ConditionTarget:    return GetConditionTarget(owner);
                case TargetType.EnemyHighestHD:     return GetEnemyHighestHD(owner);
                case TargetType.EnemyLowestHD:      return GetEnemyLowestHD(owner);
                default:                            return null;
            }
        }
```

The internal helpers (`GetAllyLowestHp`, etc.) stay unchanged and keep returning `UnitEntityData`. Only the boundary wraps.

- [ ] **Step 2: Refactor `ActionValidator.CanExecute` signature**

In `WrathTactics/Engine/ActionValidator.cs`, replace the `CanExecute` method (currently lines 15-38) with:

```csharp
        public static bool CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
            if (!target.IsValid && RequiresValidTarget(action.Type))
                return false;
            var unit = target.Unit;
            switch (action.Type) {
                case ActionType.CastSpell:
                    return CanCastSpell(action.AbilityId, owner, unit);
                case ActionType.CastAbility:
                    return CanCastSpell(action.AbilityId, owner, unit);
                case ActionType.UseItem:
                    return CanUseItem(action.AbilityId, owner, unit);
                case ActionType.ToggleActivatable:
                    return CanToggleActivatable(action.AbilityId, owner, action.ToggleMode);
                case ActionType.AttackTarget:
                    return unit != null && unit.HPLeft > 0;
                case ActionType.Heal:
                    return FindBestHeal(owner, action.HealMode, action.HealSources) != null;
                case ActionType.ThrowSplash:
                    return unit != null && SplashItemResolver.FindBest(owner, action.SplashMode).HasValue;
                case ActionType.DoNothing:
                    return true;
                default:
                    return false;
            }
        }

        static bool RequiresValidTarget(ActionType type) {
            // ToggleActivatable, Heal, DoNothing operate on owner and don't require a resolved target.
            return type != ActionType.ToggleActivatable
                && type != ActionType.Heal
                && type != ActionType.DoNothing;
        }
```

`CanCastSpell`, `CanUseItem`, etc. retain their `UnitEntityData` signatures — they receive `target.Unit` which may be null (Phase 3 handles the point-target branch).

- [ ] **Step 3: Refactor `CommandExecutor.Execute` signature**

In `WrathTactics/Engine/CommandExecutor.cs`, replace the top `Execute` method (currently lines 17-43) with:

```csharp
        public static bool Execute(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
            var unit = target.Unit;
            try {
                switch (action.Type) {
                    case ActionType.CastSpell:
                        return ExecuteCastSpell(action.AbilityId, owner, unit);
                    case ActionType.CastAbility:
                        return ExecuteCastSpell(action.AbilityId, owner, unit);
                    case ActionType.UseItem:
                        return ExecuteUseItem(action.AbilityId, owner, unit);
                    case ActionType.ToggleActivatable:
                        return ExecuteToggleActivatable(action.AbilityId, owner, action.ToggleMode);
                    case ActionType.AttackTarget:
                        return ExecuteAttack(owner, unit);
                    case ActionType.Heal:
                        return ExecuteHeal(action, owner, unit);
                    case ActionType.ThrowSplash:
                        return ExecuteThrowSplash(action, owner, unit);
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

All private `Execute*` helpers keep their `UnitEntityData` parameters. Phase 3 adds the point-target handling inside `ExecuteCastSpell`.

- [ ] **Step 4: Update `TacticsEvaluator` call sites + log format**

In `WrathTactics/Engine/TacticsEvaluator.cs`, update the `TryExecuteRules` method — find the block (currently around lines 110-124):

```csharp
                // Resolve target
                var target = TargetResolver.Resolve(rule.Target, unit);

                // Validate action
                if (!ActionValidator.CanExecute(rule.Action, unit, target)) {
                    Log.Engine.Warn($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): MATCH but action not executable");
                    continue;
                }

                // Execute!
                if (CommandExecutor.Execute(rule.Action, unit, target)) {
                    cooldowns[cooldownKey] = gameTimeSec;
                    Log.Engine.Info($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): EXECUTED -> {target?.CharacterName ?? "self"}");
                    return true;
                }
```

Replace the `EXECUTED` log line (the last one) with a `ResolvedTarget`-aware formatter:

```csharp
                // Execute!
                if (CommandExecutor.Execute(rule.Action, unit, target)) {
                    cooldowns[cooldownKey] = gameTimeSec;
                    Log.Engine.Info($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): EXECUTED -> {FormatTarget(target)}");
                    return true;
                }
```

Then do the same replacement in `TryExecuteRulesIgnoringCooldown` (around lines 154-176, same EXECUTED log pattern).

Finally add this helper at the bottom of the class (just before the closing brace):

```csharp
        static string FormatTarget(ResolvedTarget target) {
            if (target.Unit != null) return target.Unit.CharacterName;
            if (target.Point.HasValue) {
                var p = target.Point.Value;
                return $"point({p.x:F1},{p.y:F1},{p.z:F1})";
            }
            return "self";
        }
```

- [ ] **Step 5: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

If the build fails with a `CanExecute` / `Execute` overload-resolution error in a call site I didn't predict, grep for `ActionValidator.CanExecute` and `CommandExecutor.Execute` to find every caller and update them to pass `ResolvedTarget`.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/TargetResolver.cs WrathTactics/Engine/ActionValidator.cs WrathTactics/Engine/CommandExecutor.cs WrathTactics/Engine/TacticsEvaluator.cs
git commit -m "$(cat <<'EOF'
refactor(engine): atomic ResolvedTarget switchover

TargetResolver.Resolve now returns ResolvedTarget; ActionValidator.CanExecute
and CommandExecutor.Execute take ResolvedTarget; TacticsEvaluator call-sites
updated. All existing unit-target cases preserved — internal helpers still
deal in UnitEntityData, boundary wraps only.

Behavior change: rules that resolved to a null target (e.g. EnemyNearest
with no enemies) previously fell back to self-cast in ExecuteCastSpell.
They now fall through to the next rule (ResolvedTarget.None fails
CanExecute). This is a correctness tightening; document in release notes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 2: Append Point TargetTypes + Resolver Cases

### Task 3: Append `PointAtSelf` and `PointAtConditionTarget` to `TargetType`

**Files:**
- Modify: `WrathTactics/Models/Enums.cs`

- [ ] **Step 1: Append the two values**

In `WrathTactics/Models/Enums.cs`, replace the `TargetType` enum body so it ends with:

```csharp
    public enum TargetType {
        Self,
        AllyLowestHp,
        AllyWithCondition,
        AllyMissingBuff,
        EnemyNearest,
        EnemyLowestHp,
        EnemyHighestHp,
        EnemyHighestAC,
        EnemyLowestAC,
        EnemyHighestFort,
        EnemyLowestFort,
        EnemyHighestReflex,
        EnemyLowestReflex,
        EnemyHighestWill,
        EnemyLowestWill,
        EnemyHighestThreat,
        EnemyCreatureType,
        ConditionTarget,    // the enemy/ally that matched the triggering condition
        EnemyHighestHD,     // from 0.7.0
        EnemyLowestHD,      // from 0.7.0
        PointAtSelf,            // NEW, index 20 — ~1 square in front of caster
        PointAtConditionTarget  // NEW, index 21 — ~1 square toward caster from matched unit
    }
```

- [ ] **Step 2: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. `TargetResolver.ResolveInternal`'s `default` branch silently absorbs the new values (returns null) until Task 4 wires them up — that's intentional and harmless while nothing is using them yet.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/Enums.cs
git commit -m "$(cat <<'EOF'
feat(models): append PointAtSelf and PointAtConditionTarget TargetTypes

Additive: indices 20 and 21. Existing 0.7.0 configs load unchanged.
Resolver wiring follows in the next commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 4: Wire the two point TargetTypes into `TargetResolver`

**Files:**
- Modify: `WrathTactics/Engine/TargetResolver.cs`

- [ ] **Step 1: Add the offset constant**

In `WrathTactics/Engine/TargetResolver.cs`, add a constant at the top of the `TargetResolver` class, immediately after the `public static class TargetResolver {` line:

```csharp
        // ~1 Pathfinder grid square (5 ft). Offsets the spawn point away from the anchor
        // unit so summons don't spawn inside the unit's collision volume. Tune during
        // smoke test if Wrath's internal scale differs.
        const float SummonOffsetDistance = 1.5f;
```

- [ ] **Step 2: Change `Resolve` to call point-resolution for the two new types**

Still in `TargetResolver.cs`, replace the `Resolve` method so it branches on point-target types BEFORE falling through to `ResolveInternal`:

```csharp
        public static ResolvedTarget Resolve(TargetDef target, UnitEntityData owner) {
            if (target.Type == TargetType.PointAtSelf
                || target.Type == TargetType.PointAtConditionTarget) {
                var resolved = ResolvePoint(target.Type, owner);
                if (resolved.IsValid) {
                    var p = resolved.Point.Value;
                    Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> point({p.x:F1},{p.y:F1},{p.z:F1}) (type={target.Type})");
                } else {
                    Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> <no point> (type={target.Type})");
                }
                return resolved;
            }

            var unit = ResolveInternal(target, owner);
            if (unit != null) {
                float dist = (unit.Position - owner.Position).magnitude;
                Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> {unit.CharacterName} " +
                    $"(type={target.Type}, dist={dist:F1}, inCombat={unit.IsInCombat}, visible={unit.IsVisibleForPlayer})");
                return new ResolvedTarget(unit);
            }
            Log.Engine.Trace($"TargetResolver: {owner.CharacterName} -> <none> (type={target.Type})");
            return ResolvedTarget.None;
        }

        static ResolvedTarget ResolvePoint(TargetType type, UnitEntityData owner) {
            switch (type) {
                case TargetType.PointAtSelf: {
                    var forward = owner.OrientationDirection;
                    return new ResolvedTarget(owner.Position + forward * SummonOffsetDistance);
                }
                case TargetType.PointAtConditionTarget: {
                    var anchor = ConditionEvaluator.LastMatchedEnemy?.Position
                               ?? ConditionEvaluator.LastMatchedAlly?.Position;
                    if (!anchor.HasValue) return ResolvedTarget.None;

                    var delta = owner.Position - anchor.Value;
                    delta.y = 0;
                    var offsetDir = delta.sqrMagnitude < 0.01f
                        ? owner.OrientationDirection
                        : delta.normalized;
                    return new ResolvedTarget(anchor.Value + offsetDir * SummonOffsetDistance);
                }
                default:
                    return ResolvedTarget.None;
            }
        }
```

- [ ] **Step 3: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/TargetResolver.cs
git commit -m "$(cat <<'EOF'
feat(engine): resolve PointAtSelf and PointAtConditionTarget

PointAtSelf → owner.Position + OrientationDirection * 1.5
PointAtConditionTarget → anchor + (owner - anchor).normalized * 1.5
  (falls back to OrientationDirection when owner == anchor; defensive)

Offset ~1 PF square so summons don't spawn inside the anchor unit.
SummonOffsetDistance is a single tunable const.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 3: Validator and Executor Point Branches

### Task 5: Teach `ActionValidator` and `CommandExecutor` about point targets

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs`
- Modify: `WrathTactics/Engine/CommandExecutor.cs`

**Single commit** — validator and executor changes must land together so a point-target rule validates as false if the executor can't handle it.

- [ ] **Step 1: Update `ActionValidator.CanExecute` to branch on `IsPoint`**

In `WrathTactics/Engine/ActionValidator.cs`, replace the `CanExecute` method body so point targets only flow through cast/ability actions and are validated against `CanTargetPoint`:

```csharp
        public static bool CanExecute(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
            if (!target.IsValid && RequiresValidTarget(action.Type))
                return false;

            if (target.IsPoint) {
                // Point targets only make sense for cast-style actions. Other action
                // types inherently need a unit and would silently no-op downstream.
                switch (action.Type) {
                    case ActionType.CastSpell:
                    case ActionType.CastAbility:
                        return CanCastAbilityAtPoint(action.AbilityId, owner);
                    case ActionType.UseItem:
                        return CanUseItemAtPoint(action.AbilityId, owner);
                    default:
                        return false;
                }
            }

            var unit = target.Unit;
            switch (action.Type) {
                case ActionType.CastSpell:
                    return CanCastSpell(action.AbilityId, owner, unit);
                case ActionType.CastAbility:
                    return CanCastSpell(action.AbilityId, owner, unit);
                case ActionType.UseItem:
                    return CanUseItem(action.AbilityId, owner, unit);
                case ActionType.ToggleActivatable:
                    return CanToggleActivatable(action.AbilityId, owner, action.ToggleMode);
                case ActionType.AttackTarget:
                    return unit != null && unit.HPLeft > 0;
                case ActionType.Heal:
                    return FindBestHeal(owner, action.HealMode, action.HealSources) != null;
                case ActionType.ThrowSplash:
                    return unit != null && SplashItemResolver.FindBest(owner, action.SplashMode).HasValue;
                case ActionType.DoNothing:
                    return true;
                default:
                    return false;
            }
        }

        static bool CanCastAbilityAtPoint(string abilityGuid, UnitEntityData owner) {
            var ability = FindAbility(owner, abilityGuid);
            if (ability == null) return false;
            if (!ability.CanTargetPoint) {
                Log.Engine.Trace($"CanCastAbilityAtPoint: {owner.CharacterName} ability '{ability.Name}' is not point-castable");
                return false;
            }
            if (ability.Spellbook != null
                && ability.Spellbook.GetAvailableForCastSpellCount(ability) <= 0)
                return false;
            var resource = ability.Blueprint.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityResourceLogic>();
            if (resource != null && resource.IsSpendResource) {
                var required = (Kingmaker.Blueprints.BlueprintScriptableObject)ability.OverrideRequiredResource
                    ?? resource.RequiredResource;
                if (required != null) {
                    int available = owner.Resources.GetResourceAmount(required);
                    int cost = resource.CalculateCost(ability);
                    if (available < cost) return false;
                }
            }
            return true;
        }

        static bool CanUseItemAtPoint(string abilityGuid, UnitEntityData owner) {
            var ability = FindAbilityFromItem(owner, abilityGuid);
            if (ability == null) return false;
            if (!ability.CanTargetPoint) return false;
            if (ability.SourceItem != null && ability.SourceItem.Charges <= 0) return false;
            return true;
        }
```

The spell-slot and resource checks in `CanCastAbilityAtPoint` mirror `CanCastSpell`'s existing logic — duplicated on purpose, since `CanCastSpell` inlines a unit-specific `ability.CanTarget(new TargetWrapper(target))` call at the end that we skip for points.

- [ ] **Step 2: Update `CommandExecutor.ExecuteCastSpell` to accept a point branch**

In `WrathTactics/Engine/CommandExecutor.cs`, replace the top `Execute` method body and `ExecuteCastSpell`/`ExecuteUseItem` with versions that pass `ResolvedTarget` down and build the right `TargetWrapper`:

First, replace the top `Execute` method:

```csharp
        public static bool Execute(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
            try {
                switch (action.Type) {
                    case ActionType.CastSpell:
                        return ExecuteCastSpell(action.AbilityId, owner, target);
                    case ActionType.CastAbility:
                        return ExecuteCastSpell(action.AbilityId, owner, target);
                    case ActionType.UseItem:
                        return ExecuteUseItem(action.AbilityId, owner, target);
                    case ActionType.ToggleActivatable:
                        return ExecuteToggleActivatable(action.AbilityId, owner, action.ToggleMode);
                    case ActionType.AttackTarget:
                        return ExecuteAttack(owner, target.Unit);
                    case ActionType.Heal:
                        return ExecuteHeal(action, owner, target.Unit);
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

Then replace `ExecuteCastSpell`:

```csharp
        static bool ExecuteCastSpell(string abilityGuid, UnitEntityData owner, ResolvedTarget target) {
            bool isSynthetic;
            var ability = ActionValidator.FindAbilityEx(owner, abilityGuid, out isSynthetic);
            if (ability == null) {
                Log.Engine.Warn($"Spell {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            var targetWrapper = BuildTargetWrapper(target, owner);

            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command != null) {
                owner.Commands.Run(command);
                string tgtDesc = target.IsPoint
                    ? $"point({target.Point.Value.x:F1},{target.Point.Value.z:F1})"
                    : (target.Unit?.CharacterName ?? "self");
                Log.Engine.Debug($"Queued ANIMATED {(isSynthetic ? "VARIANT" : "spell")} {ability.Name} on {owner.CharacterName} -> {tgtDesc}");
                return true;
            }

            try {
                Rulebook.Trigger<RuleCastSpell>(new RuleCastSpell(ability, targetWrapper));
                Log.Engine.Debug($"Rulebook-triggered {ability.Name} on {owner.CharacterName} (no animation)");
                return true;
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Rulebook.Trigger fallback failed for {ability.Name}");
                return false;
            }
        }
```

Then replace `ExecuteUseItem`:

```csharp
        static bool ExecuteUseItem(string abilityGuid, UnitEntityData owner, ResolvedTarget target) {
            var ability = owner.Abilities.RawFacts
                .FirstOrDefault(a => a.Blueprint.AssetGuid.ToString() == abilityGuid && a.Data.SourceItem != null)
                ?.Data;

            if (ability == null) {
                Log.Engine.Warn($"Item ability {abilityGuid} not found on {owner.CharacterName}");
                return false;
            }

            var targetWrapper = BuildTargetWrapper(target, owner);

            var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
            if (command == null) {
                Log.Engine.Warn($"CreateCastCommand failed for item ability");
                return false;
            }

            owner.Commands.Run(command);
            Log.Engine.Info($"Queued item use on {owner.CharacterName}");
            return true;
        }
```

Finally, add this helper immediately after `ExecuteCastSpell` (before `ExecuteUseItem`):

```csharp
        static TargetWrapper BuildTargetWrapper(ResolvedTarget target, UnitEntityData owner) {
            if (target.IsPoint) return new TargetWrapper(target.Point.Value);
            if (target.Unit != null) return new TargetWrapper(target.Unit);
            return new TargetWrapper(owner); // fallback preserves pre-refactor "no target = self" behavior
        }
```

The `owner` fallback is the belt-and-braces tail: `ActionValidator` should have rejected `ResolvedTarget.None` for any action that needs a valid target, but if a path reaches the executor with `None` we self-wrap rather than throwing.

- [ ] **Step 3: Compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs WrathTactics/Engine/CommandExecutor.cs
git commit -m "$(cat <<'EOF'
feat(engine): validate and execute point-target casts

ActionValidator.CanExecute branches on target.IsPoint: cast/ability/item
require AbilityData.CanTargetPoint, everything else rejects. Resource and
slot checks mirror the unit-target path, minus the unit-specific CanTarget
ability check.

CommandExecutor.ExecuteCastSpell and ExecuteUseItem build
TargetWrapper(Vector3) when target.IsPoint, else TargetWrapper(Unit),
else fallback TargetWrapper(owner) as a defensive rail.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 4: Smoke Test and Release

### Task 6: Deploy and smoke-test on Steam Deck

**Files:** none (manual verification)

Pre-req: Steam Deck must be reachable via `deck-direct`. If `./deploy.sh` fails with `ssh: Connection refused`, the deck is offline — stop and surface to the user.

- [ ] **Step 1: Deploy**

Run: `./deploy.sh`

Expected: `Deployed to Steam Deck.` at the end.

- [ ] **Step 2: In-game smoke test — summon at self**

1. Launch the game, load a save where the main character (or any party member) knows a summon spell (Summon Monster I–IX, Summon Nature's Ally, etc.).
2. Open Tactics panel (Ctrl+T), select the caster.
3. Create a new rule:
   - No conditions (leave the default `Subject=Self, Property=HpPercent, Operator=<, Value=100` placeholder alone — matches always).
   - Action: `CastSpell`, pick `Summon Monster III` (or another known summon).
   - Target: `PointAtSelf`.
4. Enter a combat encounter. The caster should attempt the summon on the first tick they're eligible.
5. Verify in-game: summoned creature appears roughly 1 square in front of the caster, not inside them, not across the map.
6. Verify in the session log (`<game>/Mods/WrathTactics/Logs/wrath-tactics-<date>.log`): line `EXECUTED -> point(<x>,<y>,<z>)` with coordinates near the caster's position.

- [ ] **Step 3: In-game smoke test — summon at wounded ally (bodyguard)**

1. Create a second rule on the same caster:
   - Condition: `Subject=Ally, Property=HpPercent, Operator=<, Value=40`.
   - Action: `CastSpell`, pick another summon (use a lower-level one so slots aren't shared with test 2).
   - Target: `PointAtConditionTarget`.
2. Enter combat; let an ally's HP drop below 40%.
3. Verify: summon spawns between caster and the wounded ally (caster's side of the ally, not behind the ally facing enemies).
4. Log: `EXECUTED -> point(...)` with coordinates between the two units.

- [ ] **Step 4: In-game smoke test — summon near specific enemy type**

1. Third rule:
   - Condition: `Subject=Enemy, Property=CreatureType, Operator==, Value=Undead` (adjust to a type present in your chosen encounter).
   - Action: `CastSpell`, a summon.
   - Target: `PointAtConditionTarget`.
2. Encounter with matching enemy → summon should spawn on the caster's side of that enemy.

- [ ] **Step 5: Invalid-pairing smoke test (graceful rejection)**

1. Fourth rule, deliberately wrong:
   - Condition: `Subject=Self, Property=HpPercent, Operator=<, Value=100` (always matches).
   - Action: `CastSpell`, pick `Magic Missile` (or any unit-target-only spell the caster has).
   - Target: `PointAtSelf`.
2. Enter combat. The rule should NOT fire — `CanTargetPoint` is false for Magic Missile, validator rejects.
3. Log: no `EXECUTED` entry for this rule; subsequent rules (if any) continue to evaluate.

- [ ] **Step 6: Fall-through smoke test (no match)**

1. Fifth rule:
   - Condition: `Subject=Enemy, Property=CreatureType, Operator==, Value=Dragon` (pick an encounter with NO dragons).
   - Action: `CastSpell`, a summon.
   - Target: `PointAtConditionTarget`.
2. Enter that encounter. Rule should not fire. `TargetResolver` logs `<no point>`; no executor log for this rule.

- [ ] **Step 7: Regression — existing 0.7.0 unit-target rules**

1. Any existing rule from your current config (e.g., the Sleep/Hold Person/Power Word tests from 0.7.0, or your production heal/buff rules) should fire identically.
2. Confirm no `MATCH but action not executable` warnings appear for rules that worked in 0.7.0.
3. Confirm no `"Some rules referenced removed condition properties"` warning on load.

- [ ] **Step 8: Edge case — "no target" rule (behavior-change verification)**

This verifies the refactor's tightening. Create a rule:
   - Condition: `Subject=Enemy, Property=CreatureType, Operator==, Value=Nonexistent` (guaranteed no match).
   - Action: `CastSpell`, `Magic Missile` (or any unit-target spell).
   - Target: `EnemyNearest` (guaranteed to produce null when alone).

Actually simpler: pause combat, confirm a rule whose `Target=EnemyNearest` is set but no enemies exist in scene → rule does NOT fire. Pre-0.7.0 it would have self-cast. The log should NOT contain an EXECUTED line for this rule when no enemies are present.

If this misbehaves (rule self-casts anyway), the refactor regressed somewhere — stop and diagnose.

- [ ] **Step 9: Record verdict**

If any step fails, STOP and diagnose before proceeding to Task 7. On full pass, continue.

### Task 7: Version bump + release

**Files:**
- Modify: `WrathTactics/Info.json`
- Modify: `WrathTactics/WrathTactics.csproj`

- [ ] **Step 1: Bump `Info.json`**

In `WrathTactics/Info.json`, change `"Version": "0.7.0"` to `"Version": "0.8.0"`.

Final file body:

```json
{
  "Id": "WrathTactics",
  "DisplayName": "Wrath Tactics",
  "Author": "Gh05d",
  "Version": "0.8.0",
  "ManagerVersion": "0.23.0",
  "GameVersion": "1.4.0",
  "EntryMethod": "WrathTactics.Main.Load",
  "AssemblyName": "WrathTactics.dll",
  "Requirements": [],
  "HomePage": ""
}
```

- [ ] **Step 2: Bump `WrathTactics.csproj`**

In `WrathTactics/WrathTactics.csproj`, change `<Version>0.7.0</Version>` to `<Version>0.8.0</Version>`.

- [ ] **Step 3: Release build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/`

Expected: `Build succeeded.` and `WrathTactics-0.8.0.zip` written under `WrathTactics/bin/`.

- [ ] **Step 4: Commit and tag**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj
git commit -m "$(cat <<'EOF'
chore: release 0.8.0 — point-target summoning

New TargetTypes PointAtSelf and PointAtConditionTarget let rules drive
summon spells (and any other point-castable ability). Uses the game's
native TargetWrapper(Vector3) ctor and UnitUseAbility.CreateCastCommand;
IL-verified against the engine's own point-target path.

Refactor note: TargetResolver.Resolve now returns ResolvedTarget. Rules
whose target resolved to null (e.g. EnemyNearest with no enemies) used
to silently self-cast. They now fall through to the next rule. This is
a correctness improvement; review your rule lists if any previously
depended on the self-cast quirk.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
git tag v0.8.0
```

- [ ] **Step 5: Await user push authorization**

Ask:

> "Release 0.8.0 committed + tagged locally. Push auf master + Tag freigeben? (`git push origin master && git push origin v0.8.0`)"

- [ ] **Step 6: Create GitHub release after tag is pushed**

```bash
gh release create v0.8.0 WrathTactics/bin/WrathTactics-0.8.0.zip --title "v0.8.0 — Point-Target Summoning" --notes "$(cat <<'EOF'
## New

- **PointAtSelf** — target type that spawns summons ~1 square in front of the caster.
- **PointAtConditionTarget** — places the point at the unit that matched your rule's condition (ally or enemy), offset ~1 square toward the caster. Works for summons, walls, and any other point-castable ability.

## Use cases

- Bodyguard summon: Condition `Ally.HpPercent < 40` + Target `PointAtConditionTarget` → summon appears between caster and the wounded ally.
- Flanker summon: Condition `Enemy.CreatureType=Humanoid` + Target `PointAtConditionTarget` → summon engages the matched enemy from the caster's side.
- Self-summon: no condition + Target `PointAtSelf` → summon spawns in front of the caster.

## Behavior change

Rules whose target resolved to nothing (e.g., `EnemyNearest` with no enemies on the field) previously silently self-cast the action. In 0.8.0 they fall through to the next rule. Review any rule where this quirk was load-bearing.

## Out of scope

AoE-control (Grease, Web, Wall of Fire) and AoE-damage (Cloudkill, Stinking Cloud) are not covered by this release — they need cluster-centroid placement and friendly-fire avoidance. Planned for a later spec.

## Compatibility

All enum values appended at the end; existing 0.7.0 rules and presets load unchanged.
EOF
)"
```

- [ ] **Step 7: Update this plan — mark complete**

Replace the top of this file's header with `**Status:** Completed YYYY-MM-DD` so future readers see it's shipped.

---

## Done Criteria

- All 7 tasks' checkboxes ticked.
- Release `v0.8.0` live on GitHub with the ZIP attached.
- All six smoke tests pass.
- No regression in unit-target rules (Task 6 Step 7).
- Behavior change (self-cast quirk removal) documented in release notes.
