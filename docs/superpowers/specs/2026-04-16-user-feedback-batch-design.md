# User Feedback Batch — Design Spec

Date: 2026-04-16

Three features from Nexus Mods user feedback, all building on existing patterns.

## Feature 1: Stat-Based Targeting Subjects

### Problem

`EnemyLowestHp` / `EnemyHighestHp` let users target enemies by health, but there is no way to target by AC or saving throws. Tactical use case: "cast Bane on enemy with lowest Will save."

### Design

Add 8 new `ConditionSubject` enum entries:

| Subject | Stat accessor |
|---|---|
| `EnemyLowestAC` | `Stats.AC.ModifiedValue` |
| `EnemyHighestAC` | `Stats.AC.ModifiedValue` |
| `EnemyLowestFort` | `Stats.SaveFortitude.ModifiedValue` |
| `EnemyHighestFort` | `Stats.SaveFortitude.ModifiedValue` |
| `EnemyLowestReflex` | `Stats.SaveReflex.ModifiedValue` |
| `EnemyHighestReflex` | `Stats.SaveReflex.ModifiedValue` |
| `EnemyLowestWill` | `Stats.SaveWill.ModifiedValue` |
| `EnemyHighestWill` | `Stats.SaveWill.ModifiedValue` |

### Files to change

- **`Models/Enums.cs`** — add 8 entries to `ConditionSubject`
- **`Engine/ConditionEvaluator.cs`** — add metric functions and min/max selection, following the `EnemyLowestHp`/`EnemyHighestHp` pattern
- **`Engine/TargetResolver.cs`** — resolve these subjects to the matching enemy unit for targeting

No model changes needed — existing `Condition.Value` / `Condition.Operator` fields work as-is since these subjects select a single enemy and then evaluate properties against it.

---

## Feature 2: HasBuff/MissingBuff Searchable Dropdown

### Problem

HasBuff/MissingBuff conditions require typing a GUID or name substring into a free-text field. Users don't know buff GUIDs and can't discover available buffs.

### Design

Replace the free-text input with a `PopupSelector` dropdown populated from all game `BlueprintBuff` blueprints.

#### BuffBlueprintProvider (new file)

Static class with lazy-loaded buff list:

```
static class BuffBlueprintProvider {
    static List<(string Name, string Guid)> cachedBuffs;
    static bool isLoading;

    static void EnsureLoaded()
        - First call: set isLoading = true, load all BlueprintBuff from
          ResourcesLibrary.s_BlueprintsBundle, sort alphabetically by Name,
          cache as List<(Name, Guid)>, set isLoading = false
        - Subsequent calls: return cached list immediately

    static List<(string Name, string Guid)> GetBuffs()
        - Returns cachedBuffs or empty list if still loading

    static bool IsLoading => isLoading
}
```

Loading indicator: when `IsLoading` is true, the UI shows a spinner or "Loading buffs..." label in place of the dropdown. Once loaded, the dropdown renders.

#### UI changes in ConditionRowWidget

- HasBuff / MissingBuff properties: replace `CreateTMPInputField` with `PopupSelector`
- Display: buff name in dropdown
- Storage: GUID string in `condition.Value` (same as before)
- PopupSelector already has ScrollRect + search built in (`UIHelpers.cs` PopupSelector overlay)

#### No backward compatibility

This is an alpha mod. Existing configs with manually entered GUIDs/substrings may break — users re-select from the dropdown.

### Files to change

- **`Engine/BuffBlueprintProvider.cs`** (new) — lazy enumeration + caching
- **`UI/ConditionRowWidget.cs`** — replace free-text with PopupSelector for HasBuff/MissingBuff

---

## Feature 3: Vertical Scrollbars in RuleEditor

### Problem

When a rule has many conditions, the `RuleEditorWidget` card grows unbounded in height. The outer panel scrolls between rules, but there's no scroll within a rule card.

### Design

Wrap the body container in a `ScrollRect` with vertical scrolling.

#### Layout change in RuleEditorWidget.BuildUI

Current: `Root → Body (VLG + ContentSizeFitter)`

New: `Root → Viewport (RectMask2D) → Body (VLG + ContentSizeFitter)` with `ScrollRect` on the viewport parent.

#### Height capping

- `UpdateHeight` caps `layoutElement.preferredHeight` at 500px
- Below 500px the card grows naturally (no scrollbar needed)
- Above 500px the scrollbar appears
- `scrollSensitivity = 30f`, consistent with `TacticsPanel`

### Files to change

- **`UI/RuleEditorWidget.cs`** — add ScrollRect wrapper in `BuildUI()`, cap height in `UpdateHeight()`
