# Dynamic DC-vs-Save Condition — Design

**Date:** 2026-04-19
**Status:** Approved, ready for implementation
**Target version:** 0.8.0 (batched with HD + point-target summoning in the current unreleased cycle).

## Motivation

Tactics rules currently express "can my CC spell land on this enemy?" with static numeric save thresholds: `Enemy.SaveFortitude ≤ 20`. That number is only valid at one character level. On level-up, the caster's DC rises; the enemy's saves also rise. The user has to manually re-tune every DC-gated rule across the party to keep up. For a long campaign this is pure churn.

The user's proposal: compare the target's save against the *current* spell DC with a user-chosen margin, so rules stay correct as characters grow.

## Scope

- One new `ConditionProperty.SpellDCMinusSave` value.
- Evaluates to `currentSpellDC - target.Save` where the save type is determined from the spell's blueprint.
- Works on all Enemy-based subjects (mirrors existing AC/Save property behaviour).
- Active only when the rule's Action is `CastSpell` or `CastAbility` and the spell actually has a save.
- Fails gracefully (condition returns false) on: non-cast actions, unresolvable ability, spell with no save, missing target.

Not in scope: dynamic-value tokens elsewhere (`DC-5` in the raw Value field), `SpellCheckMinusSR` for SR-gated spells, heal DCs, per-variant DCs (rare — single Blueprint-level DC is authoritative).

## Game API Verification

IL-inspected via `ilspycmd -il` on the shipped `Assembly-CSharp.dll`:

```
Kingmaker.UnitLogic.Abilities.AbilityData:
  instance AbilityParams CalculateParams()       // parameterless — fully computed

Kingmaker.UnitLogic.Abilities.AbilityParams:
  .property instance int32 DC                    // the effective DC
  .property instance int32 CasterLevel
  .property instance int32 SpellLevel
  .property instance int32 Concentration

Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction:  // BlueprintComponent on BlueprintAbility
  .field public SavingThrowType SavingThrowType  // Unknown | Fortitude | Reflex | Will
  .field public ActionList Actions

Kingmaker.EntitySystem.Stats.SavingThrowType:
  Unknown = 0, Fortitude = 1, Reflex = 2, Will = 3
```

So DC reads as `ability.CalculateParams().DC`, and the save type reads directly from the `AbilityEffectRunAction` component on the spell's blueprint — no action-graph walk required. The existing `ActionValidator.FindAbility(owner, abilityGuid)` already gives us the `AbilityData` instance we need.

## Model Changes (`Models/Enums.cs`)

Append one value to `ConditionProperty`:

```csharp
public enum ConditionProperty {
    // existing 0..15 unchanged (including 0.7.0's HitDice at index 15):
    HpPercent, AC, HasBuff, HasCondition, SpellSlotsAtLevel,
    SpellSlotsAboveLevel, Resource, CreatureType, CombatRounds,
    IsDead, SaveFortitude, SaveReflex, SaveWill, Alignment, IsInCombat,
    HitDice,
    SpellDCMinusSave  // new, index 16
}
```

No changes to `ConditionSubject` or `TargetType`.

## New Helper (`Engine/UnitExtensions.cs`)

Add a static helper alongside the existing `GetHD`:

```csharp
public static int GetSave(UnitEntityData unit, SavingThrowType type) {
    if (unit == null) return 0;
    switch (type) {
        case SavingThrowType.Fortitude: return unit.Stats.SaveFortitude.ModifiedValue;
        case SavingThrowType.Reflex:    return unit.Stats.SaveReflex.ModifiedValue;
        case SavingThrowType.Will:      return unit.Stats.SaveWill.ModifiedValue;
        default:                        return 0;  // Unknown — caller must pre-check
    }
}
```

Single place for "map `SavingThrowType` to a stat" — reused in `ConditionEvaluator`.

## Rule-Context Threading

`ConditionEvaluator` currently doesn't know which Action the rule will run; it only receives the rule via the top-level `Evaluate(rule, owner)` and discards it before descending. `SpellDCMinusSave` needs the action's ability id to resolve the DC and save type.

Follow the existing `LastMatchedEnemy`/`LastMatchedAlly` pattern — a `static ActionDef CurrentAction` private field set in `Evaluate` and cleared in `finally`:

```csharp
static ActionDef CurrentAction;

public static bool Evaluate(TacticsRule rule, UnitEntityData owner) {
    CurrentAction = rule?.Action;
    try {
        // existing body
    } finally {
        CurrentAction = null;
    }
}
```

Side-channel static state is ugly but matches the existing pattern for rule-scoped ambient data. Adding a parameter to `EvaluateGroup`/`EvaluateCondition`/`EvaluateUnitProperty`/`MatchesPropertyThreshold` would touch five call sites for one property — not worth the churn. Note in a comment that `CurrentAction` is rule-scoped ambient and must stay scoped via the try/finally.

## Property Evaluation

Add two cases — one in `EvaluateUnitProperty`, one in `MatchesPropertyThreshold`. Same logic, same parse-threshold rules as `HitDice`:

```csharp
case ConditionProperty.SpellDCMinusSave: {
    float margin = ComputeDCMinusSave(unit);
    if (float.IsNaN(margin)) return false;
    return CompareFloat(margin, condition.Operator, threshold);
}
```

`ActionValidator.FindAbility` needs the caster (the rule's owner), not the target unit. To keep `ComputeDCMinusSave(target)` a one-argument helper that matches the existing `HpPercent`/`AC` evaluation shape, stash the owner as a second rule-scoped static alongside `CurrentAction`:

```csharp
static ActionDef CurrentAction;
static UnitEntityData CurrentOwner;

public static bool Evaluate(TacticsRule rule, UnitEntityData owner) {
    CurrentAction = rule?.Action;
    CurrentOwner = owner;
    try { /* existing body */ }
    finally { CurrentAction = null; CurrentOwner = null; }
}
```

The helper lives on `ConditionEvaluator`:

```csharp
static float ComputeDCMinusSave(UnitEntityData target) {
    if (target == null || CurrentOwner == null || CurrentAction == null) return float.NaN;
    if (CurrentAction.Type != ActionType.CastSpell && CurrentAction.Type != ActionType.CastAbility)
        return float.NaN;

    var ability = ActionValidator.FindAbility(CurrentOwner, CurrentAction.AbilityId);
    if (ability == null) return float.NaN;

    var runAction = ability.Blueprint
        .GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>();
    var saveType = runAction?.SavingThrowType ?? SavingThrowType.Unknown;
    if (saveType == SavingThrowType.Unknown) return float.NaN;  // no save → condition can't express DC margin

    int dc = ability.CalculateParams().DC;
    int save = UnitExtensions.GetSave(target, saveType);
    return dc - save;
}
```

## Subject Scope

`SpellDCMinusSave` is exposed for Enemy-based subjects only — the property only makes sense on units the caster is trying to affect with a save-gated spell. In `ConditionRowWidget.GetPropertiesForSubject`:

- Added to the `Enemy` / `EnemyBiggestThreat` / `EnemyLowestThreat` / `EnemyHighest*` / `EnemyLowest*` / `EnemyHighestHD` / `EnemyLowestHD` block
- Added to the `EnemyCount` block
- **Not** added to `Self`, `Ally`, `AllyCount`, `Combat`

Count-subject operator path: `propNeedsOperator` (line ~106 in `ConditionRowWidget`) gains `|| condition.Property == ConditionProperty.SpellDCMinusSave`, so the count-subject render shows the numeric-operator dropdown plus a numeric value input.

Non-count subjects already pick the numeric-operator path for any property not in the `usesEqOp` set — no change needed there.

## Data Flow Example

```
Party at level 9: Wizard's DC for Hold Person = 19 (10 + 4 [spell level] + 5 [CHA mod])
Rule: Cast Hold Person when it's likely to stick
  Subject=EnemyNearest, Property=SpellDCMinusSave, Op=≥, Value=3
  Action: CastSpell (Hold Person), Target=ConditionTarget
     ↓
Encounter: Cultist (Will save 12)
  ConditionEvaluator.Evaluate(rule, wizard) → sets CurrentAction, CurrentOwner
    EvaluateEnemyPick (EnemyNearest) picks Cultist
      EvaluateUnitProperty(SpellDCMinusSave, Cultist):
        FindAbility(wizard, HoldPersonGuid) → abilityData
        abilityData.CalculateParams().DC → 19
        abilityData.Blueprint.GetComponent<AbilityEffectRunAction>().SavingThrowType → Will
        GetSave(Cultist, Will) → 12
        margin = 19 - 12 = 7
        CompareFloat(7, GreaterOrEqual, 3) → true
     ↓ rule fires
Hold Person queued on Cultist.

Six hours of play later, party hits level 16:
Wizard's DC for Hold Person = 27 (10 + 4 + 13 CHA).
Same rule body, untouched.
New encounter: Sergeant (Will save 22).
  margin = 27 - 22 = 5
  5 ≥ 3 → still fires.

If Sergeant had Will 25: margin = 2, rule doesn't fire, fall through to a backup.
```

## Error Handling

- `CurrentAction` or `CurrentOwner` null (shouldn't happen if the property is only evaluated during `Evaluate`): return `NaN` → false.
- Action is not CastSpell / CastAbility: return `NaN` → false. The property can't express anything meaningful for AttackTarget/Heal/Toggle.
- `FindAbility` returns null (ability id empty, not on caster): return `NaN` → false. Matches the warning path already used by `CanCastSpell`.
- Spell's blueprint has no `AbilityEffectRunAction` component: return `NaN` → false. Applies to buffs, self-targeted utility spells.
- `SavingThrowType == Unknown`: return `NaN` → false. Magic Missile, SR-only attack spells.
- Target is null: return `NaN` → false. Shouldn't happen because callers already guard null targets, but defensive.

Every failure mode falls through to the next rule instead of firing. No exception surfaces.

## UI

- Property dropdown for Enemy-based subjects surfaces `SpellDCMinusSave` automatically via `GetPropertiesForSubject` update.
- Count-subject render path treats `SpellDCMinusSave` like `HpPercent`/`AC`/`HitDice` — operator dropdown (`< > = != ≥ ≤`) plus numeric value input.
- Non-count Enemy subjects already render numeric operators for any property not in `usesEqOp`; `SpellDCMinusSave` falls into the default numeric path, no change needed.
- Raw enum name `SpellDCMinusSave` displayed (consistent with `EnemyHighestHD`, `EnemyLowestAC`, etc. — the codebase has no label-mapping layer).

## Testing (manual Steam Deck)

No unit-test harness in this repo. Phases end on compile; feature smoke tests run in-game.

1. **Happy path — scales with caster growth.**
   Rule: `Subject=EnemyNearest, Property=SpellDCMinusSave, Operator=≥, Value=3; Action=CastSpell(Hold Person); Target=ConditionTarget.`
   At caster level N with DC `D`, expect fire only against enemies whose Will ≤ `D - 3`. Reload a higher-level save from the same campaign (or level the character up via a trainer), confirm the same rule still fires appropriately without editing.

2. **No-save spell — rejected.**
   Rule: same condition, but `Action=CastSpell(Magic Missile)`.
   Expected: rule never fires. `ComputeDCMinusSave` returns `NaN` because Magic Missile's `AbilityEffectRunAction` has `SavingThrowType = Unknown`.

3. **Non-cast action — rejected.**
   Rule: same condition, but `Action=AttackTarget`.
   Expected: rule never fires. `CurrentAction.Type` check rejects upfront.

4. **Save-type auto-picks correctly.**
   Rule A: `Action=CastSpell(Hold Person)` → `margin = DC - target.Will`.
   Rule B (separate): `Action=CastSpell(Sleep)` → `margin = DC - target.Will`.
   Rule C: `Action=CastSpell(Glitterdust)` → `margin = DC - target.Will`.
   Rule D: `Action=CastSpell(Fireball)` → `margin = DC - target.Reflex`.
   Confirm each uses the correct save via the log trace (add a one-time debug log in `ComputeDCMinusSave` if needed during verification, then remove).

5. **EnemyCount-Subject.**
   Rule: `Subject=EnemyCount, Property=SpellDCMinusSave, Operator=≥, Value=3, Value2=2; Action=CastSpell(Confusion); Target=ConditionTarget.`
   Expected: fires only when ≥2 enemies each have `DC - Will ≥ 3`. Test in a 4-enemy encounter where two low-Will cultists and two high-Will sergeants are present; should fire if the two cultists push count ≥ 2.

6. **Regression.**
   Existing rules using static save thresholds (`SaveFortitude ≤ 20`) still fire identically. The ambient-state additions (`CurrentAction`, `CurrentOwner`) are opt-in — if the property is never `SpellDCMinusSave`, nothing changes.

## Risk & Compatibility

- **Side-channel static state (`CurrentAction`, `CurrentOwner`):** Follows the existing `LastMatchedEnemy/Ally` pattern. The try/finally in `Evaluate` ensures the statics never leak between rules; a stray access outside an active `Evaluate` call reads null and returns `NaN` (safe). Risk is Tester-forgot-to-look-at-CLAUDE.md level, mitigated by a comment above the field declaration and the `Evaluate` block.
- **`CalculateParams()` cost:** The engine's own tooltips, spellbook UI, and AI repeatedly call `CalculateParams()` — it's not expensive. Per-tick-per-rule cost is negligible (few μs range).
- **JSON stability:** Append-only `ConditionProperty` addition. Existing 0.7.0/0.8.0 configs load unchanged.
- **`FindAbility` failures:** Already-failing cases (ability not on caster, synthetic variants without the `AbilityEffectRunAction` component) return `NaN` and skip the rule. Matches the existing graceful-fail convention.

## Out of Scope

- Dynamic-value tokens (`DC-5`, `CasterLevel+3`) in arbitrary properties' Value fields — YAGNI; revisit if multiple properties need dynamic comparison.
- `SpellCheckMinusSR` for spell-resistance-gated spells — separate property; SR check semantics differ (1d20 + caster level vs. SR).
- Heal DCs — `FindBestHeal` returns an `AbilityData`; a future feature could drive heal-gated rules by DC, but no current pain reported.
- Per-variant DC (e.g., a spell with AbilityVariants that each carry a different DC) — rare; if it surfaces, the variant `AbilityData` constructed via the `(parent, variant)` ctor still calls `CalculateParams` against the variant's own blueprint, so the effective DC is correct by accident.
- Arbitrary expression parsing (`(DC - enemy.Will) * 2 >= 10`) — explicit non-goal.
