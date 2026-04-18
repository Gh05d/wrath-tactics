# Condition Negate Toggle

## Problem

Users want to write rules like "cast Mark of Death on an enemy that does NOT have Mark of Death." Today this is impossible on enemy subjects:

- `MissingBuff` exists but is only exposed for `Self`/`Ally`/`AllyCount` in `GetPropertiesForSubject` (`UI/ConditionRowWidget.cs:416-428`), not for any `Enemy*` subject.
- `HasDebuff` exists but is a narrow convenience: hardcoded list of 7 Evil-Eye buff names, substring-matched against blueprint names. It cannot express "target does not have buff X" for arbitrary X.

The game engine does not distinguish "buff" from "debuff" structurally — both are `BlueprintBuff` instances on `unit.Buffs.RawFacts`. The distinction is semantic. A generic per-condition negation toggle removes the need for both parallel paths (`MissingBuff`, `HasDebuff`) and covers arbitrary targets uniformly.

## Solution

Add a `Negate` boolean to `Condition`, evaluated as a single negation at the end of the condition evaluator. Expose it in the UI as a small "NOT" toggle on each condition row. Delete `MissingBuff` and `HasDebuff` from the `ConditionProperty` enum. Self-heal configs that still reference the removed enum values.

## Scope

**In scope:**
- Per-condition `Negate` flag, applies to any `ConditionProperty`.
- UI toggle on `ConditionRowWidget`.
- Removal of `ConditionProperty.MissingBuff` and `ConditionProperty.HasDebuff`.
- Self-healing load path that strips un-parseable conditions and rewrites the config file clean.

**Out of scope:**
- Rule-level, group-level, or target-level negation.
- Removal of `ConditionOperator.NotEqual` — stays as an orthogonal feature for numeric comparisons. Redundancy with `Negate` is accepted.
- Migration of old `MissingBuff`/`HasDebuff` conditions to `HasBuff + Negate`. This is an alpha; a hard break is acceptable. Users re-author affected rules.
- Backup files of pre-cleanup configs.

## Design

### Data model

`Models/TacticsRule.cs` — extend `Condition`:

```csharp
public class Condition {
    [JsonProperty] public ConditionSubject Subject { get; set; }
    [JsonProperty] public ConditionProperty Property { get; set; }
    [JsonProperty] public ConditionOperator Operator { get; set; }
    [JsonProperty] public string Value { get; set; } = "";
    [JsonProperty] public string Value2 { get; set; } = "";
    [JsonProperty] public bool Negate { get; set; } = false;  // NEW
}
```

`Models/Enums.cs` — remove two members from `ConditionProperty`:
- `MissingBuff`
- `HasDebuff`

### Evaluator

`Engine/ConditionEvaluator.cs` — wrap the existing switch result at a single exit point:

```csharp
bool result = EvaluateCore(condition, unit, ...);
return condition.Negate ? !result : result;
```

Details:
- Delete the `MissingBuff` case from the self/ally evaluation path (`ConditionEvaluator.cs:254-256`).
- Delete the `MissingBuff` case from the enemy evaluation path (`ConditionEvaluator.cs:339-342`).
- Delete both `HasDebuff` cases (`ConditionEvaluator.cs:261-265` and `ConditionEvaluator.cs:327-332`).
- `HasBuff` is unchanged. "Ally is missing Bless" is now `Subject=Ally, Property=HasBuff, Value=<Bless GUID>, Negate=true`.
- Negate applies to all properties. `HpPercent < 50, Negate=true` is semantically `HpPercent >= 50`. This overlaps with `Operator.NotEqual`/`Operator.GreaterOrEqual`; we accept the redundancy rather than policing it.
- For count subjects (`AllyCount`, `EnemyCount`), `Negate` inverts the full count predicate, e.g. `NOT (AllyCount >= 2 with HpPercent < 60)` = "fewer than 2 allies are below 60% HP." This falls out naturally from the single-exit-point negation.

### UI

`UI/ConditionRowWidget.cs` — insert a NOT toggle button between Subject and Property.

Current non-count layout:
```
[Subject 0.00→0.15] [Property 0.16→0.37] [...rest...]
```

New non-count layout:
```
[Subject 0.00→0.13] [NOT 0.14→0.18] [Property 0.19→0.38] [...rest...]
```

- NOT button shows label `nicht` when `condition.Negate == true` (colored red for visibility), or blank/dim-grey when `false`.
- Click toggles `condition.Negate`, calls `ConfigManager.Save()`, then `Rebuild()`.
- For count subjects (`AllyCount`/`EnemyCount`): current layout is
  `[Subject][>=][count][with][Property][Op][Value][X]` in anchors 0.00-1.00. Insert the NOT toggle between `with` and `Property`:
  `[Subject 0→0.13][>=][count][with][NOT][Property][Op][Value][X]`. Reads naturally as `AllyCount >= 2 with NOT HpPercent < 60`. Exact anchor values to be finalized during implementation, preserving overall row proportions.

`GetPropertiesForSubject` — remove `MissingBuff` and `HasDebuff` from every subject's returned list.

Dead code to remove alongside the enum changes:
- `ConditionRowWidget.cs:69` — `isHasDebuff` local.
- `ConditionRowWidget.cs:167-188` — count-subject hardcoded `debuffNames` branch.
- `ConditionRowWidget.cs:266-306` — non-count hardcoded `debuffNames` branch.
- `ConditionRowWidget.cs:189-192, 307-309` — `MissingBuff` branches in the buff-picker dispatch (fold into the existing `HasBuff` case).

### Self-healing config load

`Persistence/ConfigManager.Load` — two-layer safety net.

**Layer 1: Per-condition resilience.** Register a custom `JsonConverter<Condition>` that:
- Attempts normal deserialization.
- On `JsonSerializationException` from an unknown enum value (primarily `ConditionProperty`), logs a warning with the owning rule-id and the raw JSON snippet of the bad condition, then returns `null`.
- Any other exception re-throws (do not swallow unrelated JSON errors).

After deserialization, walk the loaded config:
- Remove `null` entries from every `ConditionGroup.Conditions`.
- Remove `ConditionGroup`s whose `Conditions` is empty.
- Remove `TacticsRule`s whose `ConditionGroups` is empty.
- If any filtering happened, call `ConfigManager.Save()` immediately. The next load is silent.

**Layer 2: Structural fallback.** Wrap the top-level `JsonConvert.DeserializeObject<TacticsConfig>` call in try/catch. On any other deserialization failure (not from Layer 1), log an error with the exception detail and load an empty `TacticsConfig`. The empty config is then saved, overwriting the corrupt file.

**Side effects considered:**
- Legitimate hand-edits with unrelated typos will not be silently stripped — Layer 1 only fires on enum-value failures from the custom converter. Any other error flows to Layer 2 and replaces the whole file, which is loud (one error log) rather than silent.
- No concurrent access during load: `Load` runs synchronously on save-game-load before the tick loop starts.
- Healthy configs: converter is a no-op on non-failing paths; byte-for-byte behavior is unchanged.

## Testing

Manual Steam Deck verification (mod is UMM + Unity, no unit test harness in this repo):

1. **Negate on ally buff check.** Rule: `Self HasBuff Bless, Negate=true → Cast Bless on Self`. Without Bless, rule fires; after Bless is active, rule stops firing. Flip `Negate=false` → behavior inverts.
2. **Negate on enemy buff (Mark of Death use case).** Rule: `EnemyLowestHp HasBuff MarkOfDeathBuff, Negate=true → Cast Mark of Death on ConditionTarget`. On first evaluation, rule fires; after the buff lands, the same target no longer matches and the rule does not re-cast on it.
3. **Negate on numeric.** Rule: `Self HpPercent < 50, Negate=true`. Fires only when HP ≥ 50.
4. **Count subject with Negate.** Rule: `AllyCount >= 2 with HpPercent < 60, Negate=true`. Fires when fewer than 2 allies are below 60% HP.
5. **Self-healing load, single bad condition.** Hand-edit a saved `tactics-{GameId}.json` to contain `"Property": "MissingBuff"` in one condition. Load the save. Expected: one warning in the mod log naming the rule; that condition is stripped; the rest of the rule (and the file) loads cleanly; the file on disk is rewritten without the bad entry.
6. **Self-healing load, structurally corrupt file.** Truncate the JSON mid-object. Load. Expected: one error in the mod log; config loads empty; file is overwritten with an empty config. Game does not crash.
7. **BubbleBuffs compat regression.** With `wrath-epic-buffing` (BubbleBuffs) installed alongside, confirm no new interaction issues on buff/debuff checks.

## Open questions

None identified. Ready for implementation.
