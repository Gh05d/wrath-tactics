# ToggleMode & CreatureType Operator

## Problem

1. `ToggleActivatable` only turns abilities ON (`IsOn = true`). No way to turn them OFF via rules. Use case: Quiver of Cold Iron should activate only against Outsiders/demons.
2. `CreatureType` condition is hardcoded to `==`. No way to express "NOT Outsider" for the OFF rule.

## Changes

### 1. ToggleMode Enum

New enum in `Enums.cs`:

```csharp
public enum ToggleMode {
    On,
    Off
}
```

### 2. ActionDef Extension

New property on `ActionDef` in `TacticsRule.cs`:

```csharp
[JsonProperty] public ToggleMode ToggleMode { get; set; } = ToggleMode.On;
```

Default `On` preserves backward compatibility with existing saved rules.

### 3. ActionValidator Changes

`CanToggleActivatable()` in `ActionValidator.cs`:

- **On mode**: execute only if `IsOn == false` (current behavior)
- **Off mode**: execute only if `IsOn == true`

### 4. CommandExecutor Changes

`ExecuteToggleActivatable()` in `CommandExecutor.cs`:

- **On mode**: `activatable.IsOn = true` + `TryStart()` (current behavior)
- **Off mode**: `activatable.IsOn = false` + `TryStop()` if started

### 5. UI — ToggleMode Selector

In `RuleEditorWidget.SetupSpellSelector()`: when ActionType is `ToggleActivatable`, show a `ToggleMode` dropdown alongside the ability picker. Same pattern as `HealMode` and `ThrowSplashMode`, but the ability picker remains visible (unlike Heal/ThrowSplash which replace it).

### 6. CreatureType Operator Support

In `ConditionRowWidget` — both places where CreatureType is handled (the count-subject branch ~line 114 and the normal-subject branch ~line 195):

- Show an operator dropdown with only `=` and `!=` (numeric operators like `<`, `>` don't apply to creature types)
- Remove the hardcoded `condition.Operator = ConditionOperator.Equal`

In `ConditionEvaluator` — at both `CheckCreatureType()` call sites (~line 245 and ~line 305):

- When `condition.Operator == ConditionOperator.NotEqual`, invert the result
- `CheckCreatureType()` itself stays unchanged

## Example Setup

| Prio | Rule | Condition | Action | Mode |
|------|------|-----------|--------|------|
| 1 | Cold Iron ON | Enemy → CreatureType == Outsider | ToggleActivatable: Quiver of Cold Iron | On |
| 2 | Cold Iron OFF | Enemy → CreatureType != Outsider | ToggleActivatable: Quiver of Cold Iron | Off |

## Files Touched

| File | Change |
|------|--------|
| `Models/Enums.cs` | Add `ToggleMode` enum |
| `Models/TacticsRule.cs` | Add `ToggleMode` property to `ActionDef` |
| `Engine/ActionValidator.cs` | On/Off logic in `CanToggleActivatable()` |
| `Engine/CommandExecutor.cs` | On/Off logic in `ExecuteToggleActivatable()` |
| `UI/RuleEditorWidget.cs` | ToggleMode dropdown in action row |
| `UI/ConditionRowWidget.cs` | `=`/`!=` operator for CreatureType |
| `Engine/ConditionEvaluator.cs` | Respect operator at `CheckCreatureType()` call sites |
