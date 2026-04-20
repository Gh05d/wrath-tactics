# WithinRange Condition + Group-AND Composition — Design

## Motivation

Users want rules that trigger based on enemy/ally proximity — primarily to gate cone/area spells ("Burning Hands when ≥3 enemies in cone reach") and to combine range with other filters ("enemy spellcaster in long range → Fireball"). The current condition system has no notion of distance.

The "spellcaster in long range" use case additionally exposes a latent limitation: multiple `Enemy.*` conditions in one group evaluate independently and may match different enemies, producing "true" groups that no single enemy actually satisfies. This design fixes that alongside adding WithinRange, because WithinRange is the property where the flaw becomes user-visible.

## User-Facing Behavior

### New property

On subjects `Enemy`, `EnemyCount`, `Ally`, `AllyCount`, a new property `WithinRange` appears. Operator is restricted to `=` (within) and `!=` (not within). Value is a dropdown of five brackets:

| Bracket | Max distance (m) | Typical use |
|---|---|---|
| Melee  | 2  | Adjacent / AoO range |
| Cone   | 5  | Burning Hands, Fire Breath |
| Short  | 10 | Fireball radius territory |
| Medium | 20 | Mid-range spells |
| Long   | 40 | Vision-bounded shots |

Labels in the dropdown include the distance hint (e.g. `"Cone (≤5 m)"`).

Distance is center-to-center, measured `Vector3.Distance(owner.Position, unit.Position)` where `owner` is the companion that owns the rule.

### Group-AND composition (semantic fix)

When a condition group contains multiple conditions whose subjects target the same scope (all `Enemy.*`, or all `Ally.*`), all of those conditions must match the **same** unit for the group to be satisfied.

Concretely:
- `Enemy.HasClass = group:spellcaster` AND `Enemy.WithinRange = Long` → the group is true iff there exists a single enemy that is both a spellcaster and within Long range.
- `EnemyCount.HpPercent < 30, count ≥ 2` AND `EnemyCount.WithinRange = Short` → count = number of enemies satisfying both property checks; group true iff count ≥ 2.
- A group with a single `Enemy.*` condition behaves identically to the old engine (no regression).

## Data Model

### New enum values

`Models/Enums.cs`:

```csharp
public enum RangeBracket { Melee, Cone, Short, Medium, Long }

public static class RangeBrackets {
    public static float MaxMeters(RangeBracket b) {
        switch (b) {
            case RangeBracket.Melee:  return 2f;
            case RangeBracket.Cone:   return 5f;
            case RangeBracket.Short:  return 10f;
            case RangeBracket.Medium: return 20f;
            case RangeBracket.Long:   return 40f;
            default:                  return float.PositiveInfinity;
        }
    }

    public static bool TryParse(string s, out RangeBracket b) {
        return System.Enum.TryParse(s, ignoreCase: true, result: out b);
    }
}

public enum ConditionProperty {
    HpPercent,
    AC,
    HasBuff,
    HasCondition,
    SpellSlotsAtLevel,
    SpellSlotsAboveLevel,
    Resource,
    CreatureType,
    CombatRounds,
    IsDead,
    SaveFortitude,
    SaveReflex,
    SaveWill,
    Alignment,
    IsInCombat,
    HitDice,
    SpellDCMinusSave,
    HasClass,
    WithinRange,   // new, appended at end (preserves numeric indices in existing preset JSON)
}
```

No new fields on `Condition`. The existing `Value` string holds the bracket name verbatim (`"Melee"`, `"Cone"`, `"Short"`, `"Medium"`, `"Long"`). String encoding (rather than numeric) matches the pattern used by `HasClass` and is robust against future enum reordering.

## Evaluation

### WithinRange property

In `ConditionEvaluator.EvaluateUnitProperty`, new case:

```csharp
case ConditionProperty.WithinRange:
    if (!RangeBrackets.TryParse(condition.Value, out var bracket)) {
        Log.Engine.Warn($"WithinRange: unknown bracket '{condition.Value}'");
        return false;
    }
    if (CurrentOwner == null) return false;
    float dist = Vector3.Distance(CurrentOwner.Position, unit.Position);
    bool within = dist <= RangeBrackets.MaxMeters(bracket);
    // Operator: Equal = within; NotEqual = not within. Other operators -> false.
    switch (condition.Operator) {
        case ConditionOperator.Equal:    return within;
        case ConditionOperator.NotEqual: return !within;
        default:                         return false;
    }
```

Uses the existing rule-scoped `CurrentOwner` static (set by `Evaluate(rule, owner)` in the try/finally; already used by `SpellDCMinusSave`).

### Group-AND composition

Replace `EvaluateGroup` with a bucketed evaluator:

```csharp
static bool EvaluateGroup(ConditionGroup group, UnitEntityData owner) {
    if (group.Conditions == null || group.Conditions.Count == 0) return true;

    var enemyConds  = new List<Condition>();
    var allyConds   = new List<Condition>();
    var otherConds  = new List<Condition>();

    foreach (var c in group.Conditions) {
        if (IsEnemyScope(c.Subject))      enemyConds.Add(c);
        else if (IsAllyScope(c.Subject))  allyConds.Add(c);
        else                              otherConds.Add(c);
    }

    foreach (var c in otherConds)
        if (!EvaluateCondition(c, owner)) return false;

    if (enemyConds.Count > 0 && !EvaluateEnemyBucket(enemyConds, owner)) return false;
    if (allyConds.Count  > 0 && !EvaluateAllyBucket(allyConds, owner))   return false;
    return true;
}
```

`IsEnemyScope`: `Enemy`, `EnemyCount`, all `EnemyLowest*` / `EnemyHighest*` / `EnemyBiggestThreat` / `EnemyLowestThreat`.
`IsAllyScope`: `Ally`, `AllyCount`.

#### Enemy bucket evaluation

1. Build candidate set: `GetVisibleEnemies(owner)` (in-combat enemies).
2. If bucket contains any Pick subjects (`EnemyLowestHp`, `EnemyBiggestThreat`, etc.), sort candidates by the **first** Pick subject's metric/direction. Remaining Pick subjects are treated as property filters (their property check still applies). Multiple Pick subjects in one bucket are rare; for now, first wins.
3. If bucket contains only `EnemyCount` conditions (no `Enemy` or Pick): count = number of enemies satisfying **all** `EnemyCount` property checks. Group-contribution true iff `count >= max(Value2 over all EnemyCount conditions in the bucket)`.
4. Mixed bucket (one or more `Enemy` / Pick + any `EnemyCount`): iterate enemies (sorted if Pick present); for each enemy, evaluate all property checks of `Enemy` / Pick conditions. First enemy passing **all** of them is the pick → set `LastMatchedEnemy`. If there is also an `EnemyCount` condition, count applies over the same "passes all Enemy-property checks" predicate.
5. Bucket satisfied iff the Enemy/Pick condition path found an enemy AND (if present) the EnemyCount threshold met.

Pure `Enemy`-only bucket (no Pick, no Count): same as step 4 without Pick sort and without count.

#### Ally bucket evaluation

Candidate set: `GetAllPartyMembers(owner)` (or existing ally-iteration helper). Same logic as enemy bucket, substituting `Ally` / `AllyCount` and setting `LastMatchedAlly`.

### Backwards compatibility

- Rules with a single `Enemy.*` condition: behavior identical (iterate enemies, first match wins, `LastMatchedEnemy` set).
- Rules with a single `Ally.*` condition: ditto.
- Rules with multiple `Enemy.*` conditions that previously got "true" via independent iteration on different enemies: now require same-enemy match. This is the intended semantic fix; release notes must flag it.
- `EnemyCount` threshold semantics unchanged when used alone (single condition in bucket).

## UI

`UI/ConditionRowWidget.cs`:

- When `Property == WithinRange`:
  - Operator dropdown lists only `=` and `!=`.
  - Value renders a bracket dropdown (Melee / Cone / Short / Medium / Long) with distance-hint labels (`"Melee (≤2 m)"`, etc.).
  - `Value2` textfield visible only for count subjects (`EnemyCount`, `AllyCount`) — unchanged.
- For other properties: unchanged.

Default value when switching a row to `Property=WithinRange`: bracket `Short`.

## Persistence

- `WithinRange` is appended to `ConditionProperty` → numeric indices of prior values unchanged → existing preset JSON loads without migration.
- `Condition.Value` for WithinRange is a plain bracket name string; `RangeBrackets.TryParse` is case-insensitive.
- Old mod versions loading configs with WithinRange rules: Newtonsoft throws on unknown enum value; the existing `ConfigManager.Load` exception handling drops the offending rule with a warn log. Downgrade is not a supported path.
- `.seeded-defaults` unaffected — no default preset ships with WithinRange in this release (users compose manually; a preset can be added in a follow-up).

## Testing / Smoke

No automated tests (Unity/UMM runtime). Manual on Steam Deck:

1. **Cone trigger**: Sorcerer with Burning Hands prepared. Rule: `EnemyCount.WithinRange = Cone, count ≥ 3` → `CastSpell(BurningHands)`, target `PointAtSelf`. Pull three mobs into cone reach, confirm cast fires; pull only two, confirm cast does not fire.
2. **Spellcaster + range composition**: Arcanist. Rule (single group): `Enemy.HasClass = group:spellcaster` AND `Enemy.WithinRange = Long`. Action `CastSpell(Fireball)`, target `PointAtConditionTarget`. Encounter A: caster out of Long + melee inside Long → rule does NOT fire (regression check for old buggy behavior). Encounter B: caster inside Long → rule fires at caster position.
3. **Regression, single condition**: Cleric with one-row rule `Enemy.HpPercent < 30` → prior behavior unchanged; rule fires on any wounded enemy.
4. **Ally count**: Cleric, rule: `AllyCount.WithinRange = Short` + `AllyCount.HpPercent < 50`, count ≥ 3 → Channel. Confirm channel fires only when three wounded allies are within 10 m.
5. **Not-within**: Rule: `Enemy.WithinRange != Melee` → `CastSpell(MagicMissile)`, target `EnemyLowestHp`. Confirm caster holds missiles when an enemy is adjacent and fires when disengaged.

## Out of Scope

- **Target-side range filtering** (e.g. `TargetType.EnemyLowestHpWithinShort`). Current TargetTypes are unchanged. If the globally-lowest-HP enemy is out of range, `ActionValidator` rejects the cast; users can combine condition-side WithinRange with `TargetType.ConditionTarget` today. A target-side filter can be added later if requested.
- **Distinct "ActionRange" bracket** that reads the rule's Ability's own `AbilityRange`. Considered and deferred; fixed meter brackets are simpler and stable across levels.
- **Edge-to-edge / hitbox-aware distance**. Center-to-center is sufficient for rule evaluation; AoE-authoritative checks remain the game's job.
- **Enemy-Pick as filter** (e.g. `EnemyLowestHp.WithinRange` to mean "lowest-HP enemy *among those in range*"). The new bucket evaluator applies Pick as a sort and property checks as filters in sequence; explicit Pick-within-filter is a nuance left for future work if it proves needed.

## Release Notes (draft)

- **New condition property**: `WithinRange` for Enemy/Ally subjects, with fixed brackets Melee (2 m), Cone (5 m), Short (10 m), Medium (20 m), Long (40 m).
- **Behavior change**: Rules with multiple Enemy conditions (or multiple Ally conditions) in the same AND-group now require all of those conditions to match the *same* unit. If your rule relied on independent matches across different enemies, split it into an OR-group.
