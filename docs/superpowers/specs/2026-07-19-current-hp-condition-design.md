# Current HP (flat) Condition — Design

**Date:** 2026-07-19
**Origin:** Nexus feature request — flat HP thresholds for Power Word spells (Kill ≤100, Stun ≤150, Blind ≤200).
**Decision:** Generic numeric `ConditionProperty` (approach approved by user); no auto-eligibility mode, no new pick-subject.

## Goal

Let rules gate on a unit's absolute current hit points, e.g.:

```
IF   Enemy · Current HP · <= · 100
THEN Cast Spell: Power Word Kill
     Target: Condition target
```

## Semantics (IL-verified)

Metric is `unit.HPLeft`:

- `UnitDescriptor.get_HPLeft` = `Stats.HitPoints.ModifiedValue − Damage` (IL: `ModifiableValue::op_Implicit` returns `ModifiedValue`).
- The engine's own Power-Word target gate, `AbilityTargetHPCondition.IsTargetRestrictionPassed`, computes the **identical expression** (`HitPoints.ModifiedValue − Damage`) and passes on strict `hp < CurrentHPLessThan`.

Consequences:

- Temporary HP is excluded on **both** sides — the mod condition and the game's gate cannot drift.
- A rule `Current HP <= 100` matches exactly the set Power Word Kill (threshold 101) accepts.
- Mythic caveat: `AbilityTargetHPCondition` supports `OverrideCurrentHPLessThan` when the caster has `FactToCheck` (mythic Power Words get a looser threshold). A user rule with the base threshold is then merely conservative (fires less often than the game would allow), never wrong. Users can raise the rule value to match. Mention in the Nexus reply; no code impact.

## Behavior

- Numeric threshold property, direct sibling of `HpPercent`: operators =/≠/</≤/>/≥ via `CompareFloat`, value parsed with `float.TryParse` invariant-culture like all numeric properties.
- Value input is the existing free-text number field — no clamping, four-digit boss HP works unchanged. No UI-input changes.
- Dead handling mirrors `HpPercent`:
  - Hot path (`EvaluateUnitProperty`): negative `HPLeft` clamps to 0 before comparison (mirror of the `HPLeft <= 0` guard).
  - Count path (`MatchesPropertyThreshold`): `HPLeft <= 0` → `false` (dead/downed units never count).
- Available in the same scopes as `HpPercent`: Self, Ally, AllyCount, Enemy, EnemyCount. Dropdown position: directly next to "HP %" in each `GetPropertiesForSubject` list (UI order is free; enum order is not).

## Sites (numeric-property checklist)

1. `Models/Enums.cs`: `ConditionProperty.HpFlat` — **APPEND at enum end** (JSON persists numeric indices; append-only).
2. `Engine/ConditionEvaluator.UnitProperty.cs` — `EvaluateUnitProperty` case (hot path).
3. `Engine/ConditionEvaluator.UnitProperty.cs` — `MatchesPropertyThreshold` case (count path). Keep both textually parallel to the `HpPercent` cases.
4. `UI/ConditionRowWidget.cs`: `propNeedsOperator` chain + all five `GetPropertiesForSubject` lists (Self/Ally/AllyCount/Enemy/EnemyCount).
5. `Localization/EnumLabels.cs` + `enum.property.HpFlat` in all 5 locale JSONs (en "Current HP", de "Aktuelle TP", fr/ru/zh analogous). Locale JSONs are EmbeddedResources → rebuild + redeploy.
6. `WrathTactics.Tests`: threshold tests as siblings of the existing `HpPercent`/numeric-property tests (operator matrix, dead-unit clamp, count-path exclusion).

## Explicitly out of scope (YAGNI)

- No `EnemyLowestHpFlat`/`EnemyHighestHpFlat` pick-subjects — `EnemyLowestHp` (% sort) + a `Current HP` row in the same bucket covers "weakest eligible enemy" (sort metric differs when max HP differs; acceptable).
- No auto-eligibility condition reading `AbilityTargetHPCondition` from the `[Then]` ability (considered, rejected by user in favor of the transparent generic property).

## Testing

- xUnit: `CompareFloat` operator matrix for `HpFlat`, clamp-at-0 behavior, count-path dead exclusion.
- Manual smoke test on deck (when online): Power Word Kill rule on a low-HP enemy; verify no-fire above threshold, fire at/below, `Condition target` resolution.
