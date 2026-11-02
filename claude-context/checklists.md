# Checklists: Adding Conditions, Subjects, Properties, Actions

Read the matching checklist COMPLETELY before adding a new enum member — every one of these encodes sites that were missed at least once and shipped broken. Enum members are APPEND-ONLY (JSON persists numeric indices, see [`gotchas-persistence.md`](gotchas-persistence.md)).

## New `ConditionSubject` (scope classification)

- **New `ConditionSubject` must be classified in `IsEnemyScope` / `IsAllyScope`**: only adding to the dispatch switch silently bypasses the same-unit AND fix. Also extend `PickMetric` for sort-picks. ([deep-dive](../docs/wrath-api-deep-dive.md#subject-scope-classification))

## New pick `ConditionSubject` (SIX sites)

- **New pick `ConditionSubject` checklist** (mirror `EnemyNearest`, 1.19.0): SIX sites — enum (APPEND at end); `PickMetric` case (bucket path); `IsEnemyScope`; `CommonBuffRegistry.IsEnemySubject` + test `InlineData`; `ConditionRowWidget.GetPropertiesForSubject`; i18n `enum.subject.X` × 5. Do NOT add an `EvaluateCondition` dispatch case: that switch is dead for enemy/ally-scope subjects (`EvaluateGroup` buckets them first) — a case there only masks a forgotten `IsEnemyScope` entry by silently routing through the legacy pick path (bypasses same-unit-AND + latch). Remaining dead switch cases (Enemy/Ally/picks) are deletion backlog since 1.21.1. Subject dropdown is enum-automatic (`EnumLabels.NamesFor`) — no UI registration. Same-named `TargetType` exists ⇒ copy its `enum.target.X` translations. Owner-relative metrics use the `CurrentOwner` static (see `DistanceToOwner`).

## New `UnitCondition` in the HasCondition picker (THREE sites)

- **Adding a new `UnitCondition` to HasCondition picker requires THREE sites synced**: `ConditionEvaluator.HasConditionByName` switch (lowercase), `EnumLabels.KeysForCondition` (PascalCase), one i18n entry per locale × 5 files. ([deep-dive](../docs/wrath-api-deep-dive.md#unitcondition-three-sites))

## New positional `ConditionProperty` (SIX sites)

- **Position-Conditions checklist**: a new positional `ConditionProperty` (e.g. `IsFlanked`, `AdjacentEnemyCount`) needs SIX sites: enum entry, `EvaluateUnitProperty`, `MatchesPropertyThreshold`, both `isBool` chains in `ConditionRowWidget` (bool props only), `GetPropertiesForSubject` (per scope it should appear in — mirror `IsSummon`/`IsPet`, NOT `IsFlanked`), i18n × 5. Numeric props also go into `propNeedsOperator` in `ConditionRowWidget`, else the AllyCount path forces `=`. **The `GetPropertiesForSubject` site was missing from this checklist through 1.19.0** — both `IsFlanked` and `AdjacentEnemyCount` shipped wired everywhere EXCEPT the subject lists, so they were never selectable in the dropdown (JSON-only) until 1.19.x backfilled them. ([deep-dive](../docs/wrath-api-deep-dive.md#position-conditions))

## New value-list `ConditionProperty` (NINE sites)

- **Value-list `ConditionProperty` checklist** (property + value-from-list + =/≠, the HasCondition/CreatureType/Alignment family): NINE sites — enum entry; BOTH evaluator switches (`EvaluateUnitProperty` + `MatchesPropertyThreshold` in `ConditionEvaluator.UnitProperty.cs`); `ConditionRowWidget` (count-path eq-op group + `usesEqOp` + a render branch + `GetValueKeysForProperty` + `GetValueLabelsForProperty` + `GetPropertiesForSubject` per scope); `EnumLabels` Keys/Labels; i18n × 5. Mirror `HasDescriptorEffect` / `ImmuneToEnergy` (1.17.3).
- **Ability-filter property** (`IsBeingCastOnByAlly`, value = ability GUID picked from a list): same eq-op sites, but the render branch is a picker button (`CreateAbilityFilterSelector` → `SpellPickerOverlay` fed by `SpellDropdownProvider.GetPartyFilterEntries`), NOT the `GetValueKeysForProperty`/`GetValueLabelsForProperty` dropdown, and it must stay OUT of both `isBool` chains (that dropdown writes "true"/"false" into `Value` and would clobber the GUID). It sits in `GetPropertiesForSubject` for Ally, AllyByName, AllyCount, Enemy + all pick subjects and EnemyCount; the Value2 ally pin exists only on the single-unit subjects (Value2 is the count / named ally on the others).

## New `ActionType`

- New file `ActionValidator.<Type>.cs` as `partial class ActionValidator` (see Code Style in CLAUDE.md). Must validate up-front in `CanExecute` incl. `AbilityData.IsAvailable`; chain-capable types route through `ResolveCastSpellChain` — see [`gotchas-engine.md`](gotchas-engine.md) §Validation.
- Alle Stellen (MoveAwayFromTarget, 2026-10-20): `Enums.cs` (append), `ActionValidator.cs` Dispatcher — BEIDE Branches (`target.IsPoint` und Unit) + ggf. `RequiresValidTarget`, `CommandExecutor.Execute` switch, `ActionSlots` (`Classify`, `NeedsCrossSlotCheck`/`IssuesAnimatedCommand`, `UsesActionBudget`), `TacticsEvaluator` nur bei Walk-Typen (`IsWalkAction`/`WalkHold`), `TargetDefaults.ForAction`, `RuleEditorWidget.Action.cs` (Action-Switch-Handler, `SetupSpellSelector`-Zweig, `showSelector`, `RefreshSpellSelector` Rebuild-Liste + `showSpell`, `GetSpellEntries`), `enum.action.<Type>` in 5 Locales, Tests `ActionSlotsTests` + `TargetDefaultsTests`, README §Glossary-Zeile + `tools/gen_nexus_glossary.py`.
- New compound enum display names need a `ConditionRowWidget.PropertyLabel` case — see [`gotchas-ui.md`](gotchas-ui.md).
