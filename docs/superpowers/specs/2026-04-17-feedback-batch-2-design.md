---
status: draft
date: 2026-04-17
topic: User feedback batch 2 — buff dropdown perf, combat-end cleanup, alignment
---

# Design: Feedback Batch 2

Three independent features from user feedback:

1. **Buff-Dropdown Performance** — eliminate the freeze when opening HasBuff/MissingBuff pickers.
2. **Combat-End Cleanup** — let rules fire once when combat ends (e.g. auto-toggle Rage off for Bloodrager/Fleshdrinker).
3. **Alignment Condition** — target/filter by alignment components (Good/Evil/Lawful/Chaotic/Neutral).

Each feature is orthogonal; implementation can proceed in any order.

---

## 1. Buff-Dropdown Performance

### Problem

`CreatePickerOverlay` in `UI/UIHelpers.cs:248` instantiates one GameObject per option eagerly. With ~3000 loaded `BlueprintBuff` entries, every open of a HasBuff/MissingBuff dropdown creates 3000 Button+Image+TMP children under a `ContentSizeFitter` + `VerticalLayoutGroup`. Symptoms observed by user:

- Slow every time the popup opens (not a one-time enumeration cost; the enumeration is cached).
- List does not scroll.
- UI not responsive while popup is open.

Root cause is GameObject count + layout passes over those children, not blueprint enumeration.

### Solution

New dedicated overlay type **`BuffPickerOverlay`** (separate file under `UI/`) that replaces the generic `CreatePickerOverlay` specifically for buff-GUID selection. Uses a **search-first** pattern with curated defaults and recently-used memory.

#### Overlay Layout

```
┌─────────────────────────────┐
│ [🔍 Search...            X] │   TMP_InputField, autofocus on open
├─────────────────────────────┤
│ ★ Recents (0–5)             │   shown only if non-empty
│   • Haste                   │
│   • Mirror Image            │
├─────────────────────────────┤
│ Common Enemy Buffs (20)     │   header text depends on Subject
│   • Displacement            │
│   • Blur                    │
│   …                         │
└─────────────────────────────┘
```

- Empty search → show (up to 5 recents) + (20 curated defaults), dedupe, max 25 rows total.
- User types → filter mode: case-insensitive substring match across `BuffBlueprintProvider.GetBuffs()`, capped at **50 results**. Recents/Defaults sections hidden.
- Clear search → return to default layout.
- Click row → `onSelected(guid)`, push guid to top of recents (dedupe, trim to 5).

#### Curated Default Lists

New `Engine/CommonBuffRegistry.cs`:

```csharp
public static class CommonBuffRegistry {
    public static readonly List<string> AllyCommon;    // ~20 buff GUIDs
    public static readonly List<string> EnemyCommon;   // ~20 buff GUIDs

    public static List<string> ForSubject(ConditionSubject subject) {
        return IsEnemySubject(subject) ? EnemyCommon : AllyCommon;
    }
}
```

**AllyCommon** (20): Haste, Bless, Good Hope, Heroism, Prayer, Shield of Faith, Mage Armor, **Shield**, **Aid**, Barkskin, Stoneskin, Bull's Strength, Bear's Endurance, Cat's Grace, Owl's Wisdom, Mirror Image, Displacement, Blur, Freedom of Movement, Protection from Evil.

*Dropped vs. first proposal:* Eagle's Splendor, Fox's Cunning (nischiger in der Praxis, Platz für Shield + Aid).

**EnemyCommon** (20): Haste, Mirror Image, Displacement, Blur, Stoneskin, Bless, Bloodlust, Heroism, Mage Armor, Protection from Evil, Greater Heroism, Unbreakable Heart, Death Ward, Mind Blank, Freedom of Movement, Protection from Arrows, Resist Energy, Energy Resistance Communal, Shield, Barkskin.

GUIDs will be resolved at mod-load time via `BuffBlueprintProvider` (lookup by buff-name → AssetGuid). Missing blueprints are logged and silently skipped; the list degrades gracefully.

#### Recents Persistence

New property on `Persistence/TacticsConfig`:

```csharp
[JsonProperty] public List<string> RecentBuffGuids { get; set; } = new();
```

- Max length 5, global (not per-subject — keeps it simple and matches usage pattern).
- Updated via `BuffPickerOverlay.OnSelect`: `Insert(0, guid); Distinct; Take(5)`. Writes via existing `ConfigManager.Save()`.

#### Wiring

`ConditionRowWidget.CreateBuffSelector` changes:

- Additional parameter: `ConditionSubject subject` (already available on the condition).
- Replaces current `PopupSelector.Create(..., names, ...)` call with `BuffPickerOverlay.Open(condition.Value, subject, guid => { condition.Value = guid; ConfigManager.Save(); })`.

### Performance Impact

- Overlay opens with 0–25 GameObjects instead of 3000.
- Filter pass is O(n) over ~3000 short strings, <5ms — subjectively instant.
- Scrolling works because the viewport content is always small.

### Non-Goals

- Virtualized full-list browsing. If ever needed, can be added as an "Alle anzeigen"-Button later (additive, no refactor of A required).
- Fuzzy search / ranking. Plain substring is sufficient.

---

## 2. Combat-End Cleanup — `IsInCombat` Condition + Post-Combat Tick

### Problem

`TacticsEvaluator.Tick` early-returns when `!Game.Instance.Player.IsInCombat`. This means no rule can fire out of combat — in particular, a rule like "HasBuff(Rage) → Toggle Rage Off" cannot automatically deactivate Fleshdrinker's Rage after combat ends, leading to continued HP damage.

### Solution

Two coupled changes:

1. **New condition property `IsInCombat`** (boolean-style, same pattern as `IsDead`).
2. **One-shot post-combat evaluation pass** triggered on the `wasInCombat → false` transition.

No general out-of-combat eval loop — keeps CPU near zero and avoids surprise rule fires in town.

#### Model Changes

`Models/Enums.cs`:

```csharp
public enum ConditionProperty {
    // …existing…
    IsInCombat,
}
```

No other model changes. `Condition.Value` stores `"true"` or `"false"` (same serialization pattern as `IsDead`).

#### Engine Changes

`Engine/TacticsEvaluator.cs` — `Tick`:

```csharp
if (!Game.Instance.Player.IsInCombat) {
    if (wasInCombat) {
        wasInCombat = false;
        RunPostCombatCleanup();
        cooldowns.Clear();
    }
    return;
}
```

New `RunPostCombatCleanup()`:

- Sets `ConditionEvaluator.IsPostCombatPass = true`.
- Iterates `Game.Instance.Player.Party` where `IsInGame && HPLeft > 0 && config.IsEnabled(unit.UniqueId)`.
- For each unit: evaluates global rules then character-specific rules exactly once, **bypassing cooldown checks** (cooldowns are cleared immediately after anyway).
- First matching rule fires via the existing `CommandExecutor.Execute` pipeline.
- Finally resets `IsPostCombatPass = false`.

New static field `ConditionEvaluator.IsPostCombatPass` and updated `EvaluateCombat`:

```csharp
static bool EvaluateCombat(Condition condition) {
    if (condition.Property == ConditionProperty.IsInCombat) {
        bool inCombat = !IsPostCombatPass && Game.Instance.Player.IsInCombat;
        bool wanted = ParseBool(condition.Value);  // "true" | "false"
        return CompareEq(inCombat, wanted, condition.Operator);
    }
    // existing CombatRounds branch
}
```

The `IsPostCombatPass` flag ensures the evaluator reports `IsInCombat = false` during the cleanup pass regardless of transient `Game.Instance.Player.IsInCombat` state.

#### UI Changes

`UI/ConditionRowWidget.cs`:

- `GetPropertiesForSubject(Combat)` adds `IsInCombat` alongside existing `CombatRounds`.
- Value renderer for `IsInCombat`: `PopupSelector` with `["Ja", "Nein"]` → maps to `"true"`/`"false"` stored in `Condition.Value`.
- Operator dropdown: only `Equal`/`NotEqual` shown for `IsInCombat` (same filtering pattern already used elsewhere).

### Edge Cases

- **Unit paralyzed/stunned at combat end**: `ActionValidator.CanToggleActivatable` returns false → cleanup is skipped for that unit this round. Next combat-end edge re-attempts. Self-healing.
- **Area transition ends combat**: `SaveLoadWatcher.OnAreaDidLoad` calls `TacticsEvaluator.Reset()` which zeros `wasInCombat`. No cleanup runs because no edge transition is observed. Documented as known limitation; next natural combat-end cleans up.
- **BubbleBuffs executing at transition**: `BubbleBuffsCompat.IsExecuting()` check happens inside `TryExecuteRules`; we inherit the same guard. Cleanup will still run the evaluation; execution is skipped if BubbleBuffs is mid-queue. Acceptable — fires on next combat end.

### Usage Example

```
Rule: "Stop Raging After Combat"
  Condition: Combat.IsInCombat == Nein (false)
  Condition: Self.HasBuff(Rage)
  Action: Toggle Activatable = Rage, Mode = Off
  Target: Self
```

---

## 3. Alignment as Condition Property

### Problem

No way to constrain targets or check own/ally alignment. Users want rules like "Smite the biggest Evil threat", "Protect Good allies with Protection from Evil", "Avoid Word-of-Chaos on Lawful allies".

### Solution

New `ConditionProperty.Alignment` on unit subjects. Value is one of 5 component strings. Operators `Equal` / `NotEqual`.

#### Model Changes

`Models/Enums.cs`:

```csharp
public enum ConditionProperty {
    // …existing…
    Alignment,
}
```

`Condition.Value` holds one of: `"Good"`, `"Evil"`, `"Lawful"`, `"Chaotic"`, `"Neutral"` (case-insensitive match in evaluator).

#### Engine Changes

`Engine/ConditionEvaluator.cs` — new case in `EvaluateUnitProperty`:

```csharp
case ConditionProperty.Alignment:
    bool match = CheckAlignment(unit, condition.Value);
    return condition.Operator == ConditionOperator.NotEqual ? !match : match;
```

and in `MatchesPropertyThreshold` (same pattern, for Count-subjects):

```csharp
case ConditionProperty.Alignment:
    bool match2 = CheckAlignment(unit, condition.Value);
    return condition.Operator == ConditionOperator.NotEqual ? !match2 : match2;
```

Helper (uses `UnitDescriptor.Alignment` of type `UnitAlignment`; `ValueRaw` returns the 9-value `Kingmaker.Enums.Alignment` enum — not a flag, so we match component membership explicitly):

```csharp
static bool CheckAlignment(UnitEntityData unit, string component) {
    var align = unit.Descriptor.Alignment.ValueRaw;
    switch (component?.ToLowerInvariant()) {
        case "good":
            return align == Alignment.LawfulGood
                || align == Alignment.NeutralGood
                || align == Alignment.ChaoticGood;
        case "evil":
            return align == Alignment.LawfulEvil
                || align == Alignment.NeutralEvil
                || align == Alignment.ChaoticEvil;
        case "lawful":
            return align == Alignment.LawfulGood
                || align == Alignment.LawfulNeutral
                || align == Alignment.LawfulEvil;
        case "chaotic":
            return align == Alignment.ChaoticGood
                || align == Alignment.ChaoticNeutral
                || align == Alignment.ChaoticEvil;
        case "neutral":
            // "weder Good noch Evil": matches LN, TN, CN
            return align == Alignment.LawfulNeutral
                || align == Alignment.TrueNeutral
                || align == Alignment.ChaoticNeutral;
        default: return false;
    }
}
```

`Alignment` is `Kingmaker.Enums.Alignment`; `UnitAlignment` is `Kingmaker.UnitLogic.Alignments.UnitAlignment`. Because every `UnitDescriptor` initializes its `UnitAlignment` field eagerly, `ValueRaw` is always well-defined (no null check needed). Creatures with no meaningful alignment typically default to `TrueNeutral` — matches Neutral, does not match Good/Evil/Lawful/Chaotic (consistent with Pathfinder Detect Evil semantics). Documented inline.

#### UI Changes

`UI/ConditionRowWidget.cs`:

- `GetPropertiesForSubject` adds `Alignment` for all unit subjects: `Self`, `Ally`, `AllyCount`, `Enemy`, `EnemyCount`, and all `EnemyXxxThreat/Hp/AC/Save*`.
- NOT added for `Combat` subject.
- Value renderer for `Alignment`: `PopupSelector` with `["Good", "Evil", "Lawful", "Chaotic", "Neutral"]`.
- Operator filtering: only `Equal` / `NotEqual` for this property.

### Targeting

No new `TargetType` values. Users compose alignment-gated targeting via existing pattern:

- Condition: `EnemyBiggestThreat.Alignment == Evil` → sets `LastMatchedEnemy`.
- Target: `ConditionTarget` → uses that unit.

### Usage Examples

```
Rule: "Smite Biggest Evil Threat"
  Condition: EnemyBiggestThreat.Alignment == Evil
  Action: Cast Smite Evil
  Target: ConditionTarget

Rule: "Buff Good Allies with Protection from Evil"
  Condition: Ally.Alignment == Good
  Condition: Ally.MissingBuff(Protection from Evil)
  Action: Cast Protection from Evil
  Target: ConditionTarget

Rule: "Heal Non-Evil Allies Below 40%"
  Condition: Ally.HpPercent < 40
  Condition: Ally.Alignment != Evil
  Action: Heal (Any)
  Target: ConditionTarget
```

---

## File Change Summary

New files:
- `WrathTactics/Engine/CommonBuffRegistry.cs`
- `WrathTactics/UI/BuffPickerOverlay.cs`

Modified files:
- `WrathTactics/Models/Enums.cs` — `ConditionProperty.IsInCombat`, `ConditionProperty.Alignment`
- `WrathTactics/Engine/ConditionEvaluator.cs` — `IsPostCombatPass` flag, `EvaluateCombat` extension, `CheckAlignment` helper, new case in `EvaluateUnitProperty` + `MatchesPropertyThreshold`
- `WrathTactics/Engine/TacticsEvaluator.cs` — `RunPostCombatCleanup()`, transition hook
- `WrathTactics/Persistence/TacticsConfig.cs` — `RecentBuffGuids` property
- `WrathTactics/UI/ConditionRowWidget.cs` — replace `CreateBuffSelector` body, add `IsInCombat`/`Alignment` property entries per subject, add value renderers, constrain operators

Updated `CLAUDE.md`:
- Add gotcha for `UnitDescriptor.Alignment.ValueRaw` → 9-value `Kingmaker.Enums.Alignment` enum (NOT a flag, despite the existence of `AlignmentMaskType`). Component checks must enumerate the 3 members explicitly.
- Add gotcha for post-combat single-tick evaluation pattern (`ConditionEvaluator.IsPostCombatPass` flag).

## Testing Plan

- **Buff dropdown**: Open HasBuff with Subject=Enemy on a mid-campaign save. Verify popup opens instantly, default shows recents + 20 EnemyCommon, typing filters immediately, selection persists, recents update after selection and show on next open.
- **Combat-end cleanup**: Bloodrager with Toggle-Off Rage rule. Enter combat, activate Rage, end combat (kill all enemies). Verify Rage toggles off within one tick after combat ends, no HP drain in town. Verify rule does not fire while in town walking around.
- **Alignment**: Write rule with `EnemyBiggestThreat.Alignment == Evil` against a demon, verify match. Against a TN elemental, verify no match. Verify `Alignment != Good` matches evil enemies and unaligned swarms.

## Open Questions

None at this stage — all design decisions resolved during brainstorm.
