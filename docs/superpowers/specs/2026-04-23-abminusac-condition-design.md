# ABMinusAC Condition — Design

**Date:** 2026-04-23
**Target:** `WrathTactics` — add a new `ConditionProperty.ABMinusAC` for threat-aware rule gating
**Scope:** Models + Engine + UI. No persistence migration, no API compat break.

## Problem

Users want Heal/Buff/Debuff rules that only fire when the party genuinely struggles to hit an enemy — so expensive resources (Evil Eye, Mark of Justice, Greater Invisibility) aren't wasted on trash mobs and scale automatically with party level / active buffs.

Current condition system exposes `EnemyBiggestThreat` as a subject and `AC` as a property, but there's no way to say "compare this enemy's AC against the party's own attack capability." The result: users hard-code `AC > 30` thresholds that break on level-up or after buffs land.

## Goal

Add a single numeric condition that evaluates to `partyBestAB − enemy.AC` for Enemy-scope subjects. `ABMinusAC < 0` means the best striker in the party needs at least an 11 on d20 to hit — the canonical "team is struggling" signal.

## Non-goals

- No target-aware AB (flanking, charge, bane-vs-type). Caller-side context is messy; the target-agnostic `RuleCalculateAttackBonusWithoutTarget` covers 95 % of cases.
- No "per-ally" AB selection UI. User confirmed Party-Best is the right model.
- No caching layer. `RuleCalculateAttackBonusWithoutTarget` is cheap; re-fire per tick.
- No changes to existing `AC` property or `EnemyBiggestThreat` subject.

## Design

### Enum addition

`WrathTactics/Models/Enums.cs` — `ConditionProperty` gets one new entry at the tail:

```csharp
public enum ConditionProperty {
    // ... existing entries ...
    WithinRange,
    ABMinusAC   // NEW: partyBestAB − enemy.AC, Enemy-scope only
}
```

Numeric index is appended (20). Existing serialized configs are unaffected (Newtonsoft serializes enum names? no, numeric indices per the CLAUDE.md gotcha — but appending never shifts indices).

### Evaluator integration

`WrathTactics/Engine/ConditionEvaluator.cs` gets two additions, mirroring the
`SpellDCMinusSave` precedent (lines 287–320).

**1. `PartyBestAB(UnitEntityData owner)` — static helper:**

```csharp
static float PartyBestAB(UnitEntityData owner) {
    int best = int.MinValue;
    foreach (var ally in GetAllPartyMembers(owner)) {
        if (ally == null || !ally.IsInGame) continue;
        if (ally.Descriptor?.State?.IsFinallyDead ?? false) continue;

        var weapon = ally.Body?.PrimaryHand?.MaybeWeapon
                  ?? ally.Body?.SecondaryHand?.MaybeWeapon
                  ?? ally.Body?.EmptyHandWeapon;
        if (weapon == null) continue;

        var rule = Rulebook.Trigger(new RuleCalculateAttackBonusWithoutTarget(ally, weapon, 0));
        if (rule.Result > best) best = rule.Result;
    }
    return best == int.MinValue ? float.NaN : (float)best;
}
```

Uses the engine-authoritative `RuleCalculateAttackBonusWithoutTarget` so BAB +
stat mod (correct for weapon type), weapon enhancement, feats, and active
buffs all roll in. `IsFinallyDead` filters permadead companions but keeps
downed-and-recoverable ones. `EmptyHandWeapon` covers monks / unarmed units.

**2. `ComputeABMinusAC(UnitEntityData enemy)` — static helper:**

```csharp
static float ComputeABMinusAC(UnitEntityData enemy) {
    if (enemy == null || CurrentOwner == null) return float.NaN;
    float ab = PartyBestAB(CurrentOwner);
    if (float.IsNaN(ab)) return float.NaN;
    int ac = enemy.Stats.AC.ModifiedValue;
    float margin = ab - ac;
    Log.Engine.Trace($"ABMinusAC: {enemy.CharacterName} AC={ac}, partyBestAB={ab} -> margin={margin}");
    return margin;
}
```

Reads `CurrentOwner` from the rule-scoped static — the `Evaluate(rule, owner)`
try/finally already cleans this up, same as `SpellDCMinusSave`.

**3. `EvaluateUnitProperty` switch extension:**

```csharp
case ConditionProperty.ABMinusAC: {
    if (!IsEnemyScope(condition.Subject)) {
        Log.Engine.Trace($"ABMinusAC: subject {condition.Subject} is not Enemy-scope, returning false");
        return false;
    }
    float margin = ComputeABMinusAC(unit);
    if (float.IsNaN(margin)) return false;
    return CompareFloat(margin, condition.Operator, ParseFloatValue(condition.Value));
}
```

`IsEnemyScope` is an existing helper that classifies subjects. `ABMinusAC`
against Self/Ally/Combat is nonsensical — return false deterministically.

**4. `MatchesPropertyThreshold` (count-subject path) — NOT extended.** Count
subjects (AllyCount / EnemyCount) aren't per-unit, so ABMinusAC has no target.
Users compose with AllyCount via separate Condition rows in the same group
(existing AND semantics).

### UI integration

`WrathTactics/UI/ConditionRowWidget.cs` — the Property dropdown is already
subject-dependent. `ABMinusAC` gets filtered in the dropdown construction so it
only appears when Subject ∈ `{ Enemy, EnemyBiggestThreat, EnemyLowestThreat,
EnemyHighest*, EnemyLowest* }` (i.e. single-enemy-scope subjects from the
`IsEnemyScope` classification).

Value input: numeric TMP_InputField (same widget as `SpellDCMinusSave`).
Default value suggestion: `0` (threshold where the best striker breaks even).

Operator: all six operators, default `<` (the primary use case is "struggling").

Label: "AB − AC" (parity with the existing "DC − Save" label).

### Data flow

```
TacticsEvaluator.Tick
  → ConditionEvaluator.Evaluate(rule, owner)       [sets CurrentOwner]
    → EvaluateEnemyBucket(group, owner)
      → for each enemy that passed single-enemy-pick (e.g. EnemyBiggestThreat):
        → EvaluateUnitProperty(condition, enemy)
          → case ABMinusAC:
            → ComputeABMinusAC(enemy)
              → PartyBestAB(CurrentOwner)
                → for each living ally:
                  → Rulebook.Trigger(RuleCalculateAttackBonusWithoutTarget)
              → return (partyBestAB − enemy.AC)
            → CompareFloat(margin, operator, threshold)
  → finally: CurrentOwner = null
```

### Edge cases

- **No living party members:** `PartyBestAB` returns NaN → `ComputeABMinusAC`
  returns NaN → condition fails. Correct (solo with permadead party is a
  degenerate state; no rule should fire).
- **Ally with no weapon and no `EmptyHandWeapon`:** skipped; best across
  remaining allies is used. If ALL allies lack a weapon blueprint: `best`
  stays `int.MinValue` → NaN.
- **`enemy.Stats.AC` null:** unreachable on `UnitEntityData` in Wrath, but the
  null-check on `enemy` at the top catches null-enemy first.
- **`RuleCalculateAttackBonusWithoutTarget` firing mid-combat:** the rule
  reads active buffs via the same path the engine uses for real attacks.
  Transient buffs (Haste end-of-round) may flicker the AB by 1–2; the
  threshold-compare tolerates that naturally.
- **Non-melee-focused parties:** the helper picks whatever hand-weapon is
  equipped. A pure caster party's "best AB" will be the Magus's dagger AB
  or the Cleric's mace — still a sensible proxy for "can we hit physically."

### Persistence

No schema change. `ConditionProperty.ABMinusAC` is added as a new enum index
at the tail; existing `tactics-*.json` / preset JSONs deserialize unchanged
(Newtonsoft stores numeric indices per the CLAUDE.md gotcha, but appending
never shifts earlier indices).

### Testing

Manual smoke on Steam Deck (no unit test infra):

1. **Earlygame earsnack:** Azata lv 3, Seelah with Longsword +0, vs. mook
   AC 14. Party best AB ~5. Rule "`EnemyBiggestThreat.ABMinusAC < 0` → Cast
   Bless" should fire.
2. **Buffed endgame:** Party with Haste + Prayer + Bless + Bard song active.
   Fighter's AB jumps +6. Rule that previously fired now doesn't (margin
   crossed zero). Demonstrates the auto-scaling the user asked for.
3. **Boss fight:** Seelah AB 28, boss AC 45. Margin −17. Rule "`ABMinusAC <
   -10`" fires, expensive Evil-Eye-type debuff lands.
4. **Solo / permadead party:** Single active member with `IsFinallyDead=true`
   on all others. NaN path — no rule fires.
5. **Paired with AllyCount:** user's stated example — `AllyCount > 3 AND
   EnemyBiggestThreat.ABMinusAC < 0`. Both rows in one ConditionGroup, AND
   composition is automatic.

No automated tests. Session log `Log.Engine.Trace` entries confirm the
computed margin per evaluation.

## File changes

- `WrathTactics/Models/Enums.cs` — add `ABMinusAC` to `ConditionProperty`
- `WrathTactics/Engine/ConditionEvaluator.cs` — add `PartyBestAB`,
  `ComputeABMinusAC`, case in `EvaluateUnitProperty`
- `WrathTactics/UI/ConditionRowWidget.cs` — include `ABMinusAC` in the
  property dropdown when Subject is single-enemy-scope
- `CLAUDE.md` — document the new property, the `RuleCalculateAttackBonusWithoutTarget`
  engine primitive, and the Party-Best semantic

No new files.

## Release

Bundled with next minor (v1.2.0) — new feature, no breaking changes.
