# Operator-Aware WithinRange Bracket Labels — Design

**Date:** 2026-07-14
**Origin:** Nexus bug report (imgur 4OJROBA): a user configured `EnemyCount < 1 with WithinRange < Short (10 m)` believing "< 10 m", but `<` on a bracket means "below the bracket's lower bound" (≤ 5 m for Short). Combined with same-unit-AND bucket composition his gate was always-true. The label "( 10 m )" is operator-blind and misleads.
**Target version:** 1.23.1 (patch).

## Summary

WithinRange bracket dropdowns show the EFFECTIVE meter interval for the
currently selected operator instead of the static bracket maximum, and the
labels update live when the operator changes.

## Decisions (approved)

- Scope: labels only. The count-vs-sibling contradiction warning stays
  backlog (documented in auto-memory / gotchas, not in this change).
- Purely symbolic label text (`≤ > ≠ – m` + digits) — NO new i18n keys.
- The bracket NAME part reuses the same source as the existing
  `RangeBrackets.Label()` so any localization there stays consistent; only
  the parenthesized part changes.
- Release as patch 1.23.1 after a short deck check (labels update live on
  operator change).

## Components

**1. `RangeBrackets.EffectiveLabel(RangeBracket b, ConditionOperator op)`**
(new, `WrathTactics/Models/Enums.cs`, next to `MaxMeters`/`LowerMeters`/`Label`).
Pure function; MUST derive lo/hi from `LowerMeters(b)`/`MaxMeters(b)` — the
same functions the evaluator uses (`ConditionEvaluator.UnitProperty.cs:398`)
— so label and behavior cannot drift. Mapping (lo=LowerMeters, hi=MaxMeters):

| op             | parenthesized part |
|----------------|--------------------|
| Equal          | `(lo–hi m)`        |
| NotEqual       | `(≠ lo–hi m)`      |
| LessOrEqual    | `(≤ hi m)`         |
| LessThan       | `(≤ lo m)`         |
| GreaterOrEqual | `(> lo m)`         |
| GreaterThan    | `(> hi m)`         |
| any other op   | fall back to existing `Label(b)` |

Numbers format with InvariantCulture, no trailing decimals for integers
(`"0.#"`). Melee (lo=0) intentionally renders the never-true trap visibly:
`<` → `Melee (≤ 0 m)`.

**2. `ConditionRowWidget`** — both WithinRange bracket-selector sites (the
count/AllyByName compressed layout and the normal row layout) build their
dropdown label list via `EffectiveLabel(bracket, condition.Operator)`. The
operator selector belonging to each site additionally calls `Rebuild()` when
`condition.Property == ConditionProperty.WithinRange` (mirrors the existing
Subject/Property `Rebuild()` pattern) so the bracket labels refresh live.
Persistence path unchanged: callbacks keep invoking `onChanged?.Invoke()`
exactly as today; `Rebuild()` is added AFTER `onChanged`.

## Testing

- New xUnit theory over `EffectiveLabel`: all six operators × Short and
  Melee, plus the fallback case. Pure logic, no game state.
- CLAUDE.md already mandates running the suite before pushing
  `Models/Enums.cs` (`RangeBrackets`) changes.
- Deck check before release: open a WithinRange row, cycle operators, labels
  update live in both the normal and the count layout.

## Out of scope

- Contradiction warning for count-row vs. sibling-row empty intersections.
- Changing evaluator semantics or bracket boundaries (labels only).
- Localizing the bracket enum names (unchanged from today).
