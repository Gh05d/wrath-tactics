# Targeting-Relation Conditions — Design

**Date:** 2026-04-27
**Target:** `WrathTactics` — DAO-style "who attacks whom" conditions for cross-character coordination
**Scope:** Models + Engine + UI + CLAUDE.md. No persistence migration, no API compat break.

## Problem

Players coming from Dragon Age: Origins want two coordination patterns the current rule system can't express:

1. **Defensive switch** — frontline tank should auto-target whoever is hitting the backline (caster, ranged DPS, healer).
2. **Focus fire** — ranged DPS / debuffer should attack whatever the tank is attacking, so the party stacks damage on one threat instead of spreading.

The existing condition vocabulary (`HpPercent`, `AC`, `EnemyBiggestThreat`, `IsDead`, `WithinRange`, `ABMinusAC` …) is target-state-driven. None of it expresses **the relationship between two combatants** — who is attacking whom right now. `EnemyBiggestThreat` is a global score, not a per-target focus signal.

## Goal

Add four Yes/No condition properties expressing the active "is attacking" relationship in both directions, scoped to both Enemy- and Ally-sides:

| Property | Scope | Match condition |
|---|---|---|
| `IsTargetingSelf` | Enemy | this enemy is currently attacking the rule owner |
| `IsTargetingAlly` | Enemy | this enemy is currently attacking some ally (≠ owner) |
| `IsTargetedByAlly` | Enemy | some ally (≠ owner) is currently attacking this enemy |
| `IsTargetedByEnemy` | Ally | some enemy is currently attacking this ally |

`ConditionTarget` lets the action consume the matched unit — the existing same-unit-AND bucket semantics handles composition (`Enemy.IsTargetingAlly = Yes AND HpPercent < 30` matches a *single* enemy that satisfies both).

## Non-goals

- **No new `TargetType` shortcuts.** `ConditionTarget` reuse is the canonical pattern and gives free composition with HP/AC/range filters.
- **No "specific party member" picker.** Per-character ally selection (e.g. "the enemy attacking *Camellia* specifically") is roster-fragile and YAGNI for v1.
- **No approach-phase detection.** An enemy running toward the party but not yet swinging does not match. AI-plan inspection is fragile and expensive; the ~0.5 s latency until first attack-frame is acceptable.
- **No per-spell intent detection.** "Enemy is casting Fireball at us" is covered by command-target reads but we don't try to distinguish hostile from buff casts when the target is a faction member of the opposite side. The hostile-target-of-opposite-faction heuristic is good enough.
- **No new caching.** Per-tick, per-rule cost is O(EnemyCount × PartySize) ≈ 30 × 6 = 180 reference comparisons worst case — negligible.

## Design

### Engine primitive — central detection helper

New file `WrathTactics/Engine/TargetingRelations.cs`:

```csharp
internal static class TargetingRelations {
    // True iff `attacker` currently has a hostile relation directed at `victim`.
    // Combines two signals because each alone misses cases:
    //   - Standard.TargetUnit catches casters / archers / non-engaged attackers
    //     whose active command points at the victim.
    //   - EngagedUnits catches melee-locked pairs in between attack-frames where
    //     the active command is briefly not a UnitAttack.
    public static bool Has(UnitEntityData attacker, UnitEntityData victim) {
        if (attacker == null || victim == null || attacker == victim) return false;

        var cmdTarget = attacker.Commands?.Standard?.TargetUnit;
        if (cmdTarget == victim) return true;

        var engaged = attacker.CombatState?.EngagedUnits;
        if (engaged != null && engaged.ContainsKey(victim)) return true;

        return false;
    }
}
```

`UnitCommand.TargetUnit` (verified IL: `Kingmaker.UnitLogic.Commands.Base.UnitCommand`) is the engine-authoritative "who is this command pointing at" — works for `UnitAttack`, `UnitUseAbility` (spell casts), and any other command that derives from `UnitCommand` and sets a target. `UnitCombatState.EngagedUnits` is the symmetric melee-engagement map (`Dictionary<UnitEntityData, TimeSpan>`); we use `ContainsKey` purely as "is there mutual melee engagement between these two."

The `EngagedUnits` fallback being symmetric is intentional: in melee, "X attacks Y" and "Y attacks X" are equivalent for our DAO use cases. The asymmetric command-target check provides direction for ranged/casting cases where it matters.

### Enum additions

`WrathTactics/Models/Enums.cs` — `ConditionProperty` gets four entries appended after `ABMinusAC` (preserves all existing numeric indices for serialized JSON):

```csharp
public enum ConditionProperty {
    // ... existing entries ...
    ABMinusAC,
    IsTargetingSelf,    // NEW: Enemy-scope, this enemy targets the rule owner
    IsTargetingAlly,    // NEW: Enemy-scope, this enemy targets a non-owner ally
    IsTargetedByAlly,   // NEW: Enemy-scope, a non-owner ally targets this enemy
    IsTargetedByEnemy   // NEW: Ally-scope, an enemy targets this ally
}
```

### Evaluator integration

`WrathTactics/Engine/ConditionEvaluator.cs` — add four cases to `EvaluateUnitProperty` (line 481). All four use `ParseBoolValue(condition.Value)` paired with `condition.Operator ∈ {=, !=}`, mirroring `IsDead`:

```csharp
case ConditionProperty.IsTargetingSelf: {
    if (!IsEnemyScope(condition.Subject)) {
        Log.Engine.Trace($"IsTargetingSelf: subject {condition.Subject} not Enemy-scope, false");
        return false;
    }
    bool match = TargetingRelations.Has(unit, CurrentOwner);
    return EqualsBool(match, condition);
}

case ConditionProperty.IsTargetingAlly: {
    if (!IsEnemyScope(condition.Subject)) return false;
    bool match = false;
    foreach (var ally in GetAllPartyMembers(CurrentOwner)) {
        if (ally == null || ally == CurrentOwner) continue;
        if (ally.Descriptor?.State?.IsFinallyDead ?? false) continue;
        if (!ally.IsInGame) continue;
        if (TargetingRelations.Has(unit, ally)) { match = true; break; }
    }
    return EqualsBool(match, condition);
}

case ConditionProperty.IsTargetedByAlly: {
    if (!IsEnemyScope(condition.Subject)) return false;
    bool match = false;
    foreach (var ally in GetAllPartyMembers(CurrentOwner)) {
        if (ally == null || ally == CurrentOwner) continue;
        if (ally.Descriptor?.State?.IsFinallyDead ?? false) continue;
        if (!ally.IsInGame) continue;
        if (TargetingRelations.Has(ally, unit)) { match = true; break; }
    }
    return EqualsBool(match, condition);
}

case ConditionProperty.IsTargetedByEnemy: {
    if (!IsAllyScope(condition.Subject)) {
        Log.Engine.Trace($"IsTargetedByEnemy: subject {condition.Subject} not Ally-scope, false");
        return false;
    }
    bool match = false;
    foreach (var enemy in GetVisibleEnemies(CurrentOwner)) {
        if (TargetingRelations.Has(enemy, unit)) { match = true; break; }
    }
    return EqualsBool(match, condition);
}
```

`EqualsBool(actual, condition)` is a small new local helper that compresses the four-line `ParseBoolValue` + ternary that `IsDead` (line 533–536) inlines today:

```csharp
static bool EqualsBool(bool actual, Condition c) {
    bool wanted = ParseBoolValue(c.Value);
    bool match  = actual == wanted;
    return c.Operator == ConditionOperator.NotEqual ? !match : match;
}
```

Adding four more inline copies of that pattern alongside `IsDead` would invite drift; one helper keeps the bool-property convention in one place. The existing `IsDead` case can be migrated to call it too in the same change (cosmetic, low risk).

The `ally != CurrentOwner` filter is load-bearing for `IsTargetedByAlly`: without it, a Ranger with rule "attack any enemy targeted by an ally" would self-trigger off his own previous attack and lock onto the same target indefinitely.

`MatchesPropertyThreshold` (count-subject path, line ~601) is **not** extended — count subjects (`AllyCount`, `EnemyCount`) aren't per-unit, so targeting relations are nonsensical there.

### Scope classification

`IsEnemyScope` / `IsAllyScope` (lines 830, 854) are **subject-driven**, not property-driven, so they need no changes. The new properties only fire on existing Enemy/Ally subjects, and the bucketed evaluation in `EvaluateGroup` (line 73) already routes them correctly.

This avoids the latent gotcha documented in `wrath-tactics/CLAUDE.md` line 106 — that gotcha applies to new `ConditionSubject` values, not new properties on existing subjects.

### UI integration

`WrathTactics/UI/ConditionRowWidget.cs`:

1. **Property dropdown filter:** `IsTargetingSelf` / `IsTargetingAlly` / `IsTargetedByAlly` appear when the selected Subject is Enemy-scope (per `IsEnemyScope`). `IsTargetedByEnemy` appears when Subject is Ally-scope. Mirrors how `ABMinusAC` is filtered to Enemy-scope subjects.

2. **PropertyLabel cases** (rendered in the dropdown):

   ```csharp
   case ConditionProperty.IsTargetingSelf:    return "Targeting me";
   case ConditionProperty.IsTargetingAlly:    return "Targeting ally";
   case ConditionProperty.IsTargetedByAlly:   return "Targeted by ally";
   case ConditionProperty.IsTargetedByEnemy:  return "Targeted by enemy";
   ```

3. **Operator dropdown:** restricted to `{ Equal, NotEqual }`, default `Equal`. Same pattern as `IsDead` / `IsInCombat`.

4. **Value dropdown:** `Yes` / `No`, default `Yes`.

### Data flow (worked example: Tank protects backline)

User-built rule on the Paladin:
- Priority 50, Active in combat
- ConditionGroup: `Enemy.IsTargetingAlly = Yes`
- Action: `AttackTarget`, Target: `ConditionTarget`

```
TacticsEvaluator.Tick (every interval ms)
  → ConditionEvaluator.Evaluate(rule, paladin)        [sets CurrentOwner = paladin]
    → EvaluateGroup(group, paladin)
      → enemyConds = [{Subject: Enemy, Property: IsTargetingAlly, Op: =, Value: Yes}]
      → EvaluateEnemyBucket
        → for each enemy in GetVisibleEnemies(paladin):
          → EvaluateUnitProperty(c, enemy)
            → case IsTargetingAlly:
              → for each ally != paladin:
                → if TargetingRelations.Has(enemy, ally) → match = true; break
              → EqualsBool(match, c) → true if any ally is being targeted
          → if match: bucket records `enemy` as the same-unit anchor
      → first matching enemy → LastMatchedEnemy = enemy (rule-scoped static)
    → resolve TargetType.ConditionTarget → enemy
    → ActionValidator + CommandExecutor → AttackTarget on enemy
  → finally: clear CurrentOwner / LastMatchedEnemy
```

### Edge cases

- **Self-loop on `IsTargetedByAlly`** — Ranger attacks Goblin. Next tick: would Goblin satisfy `IsTargetedByAlly` because of Ranger's own attack? No — the `ally != CurrentOwner` filter excludes the rule owner from the iteration. Tank attacks count, Wizard attacks count, Ranger himself doesn't.

- **Charm / dominate** — when a companion is charmed, it's faction-flipped and `GetVisibleEnemies` includes them. The relation reads naturally: a charmed Camellia firing at Seelah makes Seelah satisfy `IsTargetedByEnemy` and Camellia (now an enemy) satisfy `IsTargetingAlly`. Correct emergent behavior.

- **Approach phase** — Enemy running toward party, no `UnitAttack` issued yet, not yet engaged. `Standard?.TargetUnit` is null (active command is `UnitMoveTo`), `EngagedUnits` is empty. No match. Acceptable: latency until first swing is ≤1 attack interval.

- **Spell-on-friend** — Enemy caster buffing another enemy. `Standard?.TargetUnit` is the friend-enemy, not on our roster. Iteration over allies and over visible enemies excludes it. Correct.

- **Dead/finally-dead allies** — `IsFinallyDead` filter on the ally iteration prevents matching against grey-portrait companions. Downed-but-recoverable allies (`IsDead && !IsFinallyDead`) **are** still iterated — they can still be targets of enemy attacks (coup-de-grace etc.) and the rule should fire to peel attackers off them.

- **Out of combat** — `TacticsEvaluator.Tick` early-returns when `!Player.IsInCombat`, so the cost is paid only during combat. The post-combat-cleanup pass (`IsPostCombatPass = true`) will iterate but `EngagedUnits` and active commands are typically clear at that boundary; properties read as `No` and combat-end cleanup rules with `IsTargetingSelf = No` etc. fire correctly.

- **Multi-target spells / cleave** — `Standard?.TargetUnit` only captures the primary target. A cleric channeling negative energy harms many but `TargetUnit` is the channel center. We don't try to enumerate AoE victims. The melee fallback via `EngagedUnits` covers most adjacent cases anyway.

### Performance

Per tick, per rule that uses any of the four properties:

- `IsTargetingSelf`: O(1) — single relation check.
- `IsTargetingAlly` / `IsTargetedByAlly`: O(PartySize) ≤ 6 relation checks per evaluated enemy.
- `IsTargetedByEnemy`: O(EnemyCount) ≤ ~30 relation checks per evaluated ally.
- `EvaluateEnemyBucket` evaluates the property for each of N enemies (anchor selection); `IsTargetingAlly` thus costs O(N × PartySize) ≈ 30 × 6 = 180 ref comparisons worst case.

Each relation check is two field reads + one dict lookup. Far below the budget already absorbed by `ABMinusAC`'s `Rulebook.Trigger` calls. No cache layer needed.

### Persistence

`ConditionProperty` index appends preserve all existing JSON values. Pre-existing `tactics-{GameId}.json` and preset JSONs deserialize unchanged. New configs that use the new properties are simply not deserializable on older versions of the mod — same forward-compat policy as every previous property addition.

### Testing

Manual smoke on Steam Deck (no unit-test infra):

1. **Tank protects backline** — Paladin rule: `Enemy.IsTargetingAlly = Yes` → `AttackTarget` on `ConditionTarget`. Spawn fight where two enemies engage Camellia in the back row. Paladin should switch off whatever he's hitting and target one of them within ≤1 tick interval.

2. **Ranger focuses tank** — Ranger rule: `Enemy.IsTargetedByAlly = Yes` → `AttackTarget` on `ConditionTarget`, priority below "kill weak"-style rules. Walk into a mob; once the Paladin engages, Ranger should fire at the same enemy on the next tick.

3. **Cleric heals attacked ally** — Cleric rule: `Ally.IsTargetedByEnemy = Yes AND HpPercent < 50` → `Heal` on `ConditionTarget`. Verify the heal lands on the unit being attacked, not just any low-HP companion.

4. **Self-defense reaction** — Any character: `Enemy.IsTargetingSelf = Yes AND HpPercent < 70` → `CastSpell` (Mirror Image / Shield / etc.). Verify reaction-buff fires only when actually targeted.

5. **Negation works** — rule with `Enemy.IsTargetingAlly = No` confirms `!=` semantics flip correctly.

6. **No self-loop** — Ranger with rule using `IsTargetedByAlly` shouldn't lock onto its own first target indefinitely. Verify the rule re-evaluates when allies' targets change.

7. **Approach-phase miss is acceptable** — confirm visually: an enemy spawning at distance and running toward Camellia doesn't trigger the Paladin's switch until the enemy starts swinging or engages. Document the expected latency in release notes if user-visible.

8. **Out of combat** — confirm panel doesn't grey-out / no errors when reading these properties during the post-combat cleanup pass.

No automated tests. `Log.Engine.Trace` lines from each property case let users verify behavior from the session log.

## File changes

- `WrathTactics/Engine/TargetingRelations.cs` — **new file**, single static helper class.
- `WrathTactics/Models/Enums.cs` — append four entries to `ConditionProperty`.
- `WrathTactics/Engine/ConditionEvaluator.cs` — four cases in `EvaluateUnitProperty`, optional `EqualsBool` helper.
- `WrathTactics/UI/ConditionRowWidget.cs` — Property dropdown filter, four `PropertyLabel` cases, operator/value dropdown wiring (likely already covered by the IsDead Yes/No code path — reuse).
- `CLAUDE.md` (wrath-tactics) — add a Game-API gotcha entry describing `UnitCommand.TargetUnit` + `UnitCombatState.EngagedUnits` as the targeting-relation primitives, and note the approach-phase blind spot as a known design limit.

No new files in Models/UI/Persistence.

## Release

Bundled with next minor (v1.4.0) — new feature, backwards-compatible JSON, no breaking changes.
