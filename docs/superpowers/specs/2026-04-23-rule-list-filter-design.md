# Rule-List Filter + Explicit Scrollbar — Design

**Date:** 2026-04-23
**Target:** `WrathTactics` — `TacticsPanel` rule list and `PresetPanel` preset list
**Scope:** UI-only. No model, persistence, or engine changes.

## Problem

Users who accumulate many rules per companion (or many presets) struggle to
locate a specific entry in the `TacticsPanel` rule list:

1. The existing vertical scrollbar in `TacticsPanel.CreateRuleList` is nominally
   wired (`scroll.verticalScrollbar = scrollbar`), but invisible in practice:
   `AutoHideAndExpandViewport` mode, 8 px track width, low-contrast colors
   (track 50 % alpha, handle 60 % alpha). Users fall back to mouse wheel, which
   is slow for long lists.
2. No textual filter exists — the only way to find a rule is to eyeball the
   list.

## Goal

- Give the user an always-visible scrollbar on the right edge of the rule list.
- Give the user a sticky filter input above the list that narrows the visible
  rules by name, in-place (edit / delete / toggle still works on filtered
  entries).
- Apply the same treatment to the Presets tab (which shares the outer
  `ScrollRect`).

## Non-goals

- Virtualization / lazy rendering of rule cards. Realistic per-character rule
  counts (≤ ~50) don't justify the complexity.
- Persisting the filter query across panel sessions or tab switches.
- Fuzzy / token search — plain case-insensitive substring is enough.
- Changes to the `+ From Preset` assignment popup (separate flow, separate UX,
  out of scope here).
- Filtering by action type, spell name, conditions, or any field other than the
  rule's display name.

## Design

### Layout changes in `TacticsPanel`

Current geometry in `CreatePanel`:

- Title bar — `SetAnchor(0, 1, 0.92, 1)`
- Tab bar — `SetAnchor(0, 1, 0.84, 0.91)`
- Control row (`+ New Rule`, `+ From Preset`) — `SetAnchor(0.01, 0.99, 0.77, 0.83)`
- Rule scroll area — `SetAnchor(0.01, 0.99, 0.02, 0.76)`

New geometry:

- Title bar (unchanged)
- Tab bar (unchanged)
- Control row (unchanged)
- **New filter strip** — `SetAnchor(0.01, 0.99, 0.72, 0.76)` (4 % panel
  height, sits between control row and rule list, always visible)
  - Left ~85 % of strip: `TMP_InputField` via `UIHelpers.CreateTMPInputField`,
    placeholder `"Filter rules…"`
  - Right ~15 % of strip: small `✕` clear-button. `Button.interactable =
    !string.IsNullOrEmpty(currentRuleFilter)`.
  - Inside a GameObject held as `filterStripRoot` on the panel so it can be
    referenced at runtime.
- Rule scroll area shrunk to `SetAnchor(0.01, 0.99, 0.02, 0.71)` — makes room
  for the filter strip with a 1 % gap on each side.

### Scrollbar visibility in `CreateRuleList`

In the existing `CreateRuleList` block that creates the vertical scrollbar
(TacticsPanel.cs:210-235), apply the following changes:

| Property | Before | After |
|---|---|---|
| Track width (`scrollbarRect.sizeDelta.x`) | 8 | 12 |
| Track color | `(0.12, 0.12, 0.12, 0.5)` | `(0.15, 0.15, 0.15, 0.85)` |
| Handle color | `(0.5, 0.5, 0.5, 0.6)` | `(0.7, 0.7, 0.7, 1.0)` |
| `verticalScrollbarVisibility` | `AutoHideAndExpandViewport` | `Permanent` |
| `verticalScrollbarSpacing` | 2 | 4 |

This scrollbar already covers the Presets tab because `PresetPanel` is
instantiated as a child of `ruleListContent` (see TacticsPanel.cs:247-252) and
shares the outer `ScrollRect`. No scrollbar work is needed inside `PresetPanel`.

### Filter wiring in `TacticsPanel`

New state on the panel:

- `string currentRuleFilter` — live query, mirrored from the input's value.
- `TMP_InputField ruleFilterInput` — the input field reference.
- `GameObject ruleFilterEmptyLabel` — "No matching rules" placeholder, used for
  Char/Global tabs. Created once in `CreatePanel` as a **sibling** of the rule
  scroll (NOT a child of `ruleListContent`) — otherwise `RefreshRuleList`'s
  destroy-children loop would kill it. Anchored over the scroll-area center,
  toggled via `SetActive`.
- `PresetPanel currentPresetPanel` — populated in `RefreshRuleList` when the
  presets tab is active, set to `null` in all other branches. Unity's
  destroyed-reference semantics mean a stale reference can still appear
  non-null in plain C# checks; always clear explicitly after destroying the
  preset panel's children.

Input wiring:

```csharp
ruleFilterInput.onValueChanged.AddListener(v => {
    currentRuleFilter = v ?? "";
    ApplyFilter();
});
```

The `✕` button:

```csharp
clearBtn.onClick.AddListener(() => {
    ruleFilterInput.text = "";          // fires onValueChanged, resets filter
});
```

`ApplyFilter()` dispatches by tab:

- `selectedUnitId == "presets"`:
  - If `currentPresetPanel != null`, call `currentPresetPanel.ApplyFilter(currentRuleFilter)`.
- Else (Char or Global tab):
  - Iterate direct children of `ruleListContent`. For each, resolve the
    underlying `TacticsRule` via the `RuleEditorWidget` component, compute the
    display name, and call `child.SetActive(MatchesFilter(displayName, currentRuleFilter))`.
  - Track visible count; toggle `ruleFilterEmptyLabel.SetActive(visibleCount == 0 && !string.IsNullOrWhiteSpace(currentRuleFilter))`.

Display-name resolution:

```csharp
static string EffectiveDisplayName(TacticsRule rule) {
    if (!string.IsNullOrEmpty(rule.PresetId)) {
        var preset = PresetRegistry.Get(rule.PresetId);
        if (preset != null) return preset.Name ?? "";
    }
    return rule.Name ?? "";
}
```

Match helper (in `UIHelpers` to allow reuse from both `TacticsPanel` and
`PresetPanel`):

```csharp
public static bool StringMatchesFilter(string name, string query) {
    if (string.IsNullOrWhiteSpace(query)) return true;
    return (name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
}
```

Integration points:

- `RefreshRuleList` — after building the card list (Char/Global) or spawning
  the `PresetPanel` (Presets), call `ApplyFilter()` so the current query
  re-applies to the freshly rendered children.
- `SelectTab` — set `ruleFilterInput.text = ""` (which fires onValueChanged and
  zeroes `currentRuleFilter`), then call `RefreshRuleList()`. Reset happens
  automatically via the existing text-change path.

### Filter wiring in `PresetPanel`

New state:

- `string currentFilter`
- `List<(GameObject entry, string name)> entries` — populated in
  `CreatePresetEntry` (append one tuple per entry), cleared in `Rebuild`.
- `GameObject emptyMatchLabel` — "No matching presets", shown when the filter
  hides every entry. Created once inside `BuildUI` below the entry block.

New method:

```csharp
public void ApplyFilter(string query) {
    currentFilter = query ?? "";
    int visible = 0;
    foreach (var (entry, name) in entries) {
        bool match = UIHelpers.StringMatchesFilter(name, currentFilter);
        entry.SetActive(match);
        if (match) visible++;
    }
    if (emptyMatchLabel != null) {
        emptyMatchLabel.SetActive(visible == 0 && !string.IsNullOrWhiteSpace(currentFilter));
    }
}
```

Re-entry points:

- `Rebuild()` — at the end, call `ApplyFilter(currentFilter)` so that new
  presets (after add / paste-import / delete) honor the filter.
- `BuildUI()` — at the end, call `ApplyFilter(currentFilter)` for the same
  reason on first build.

Header buttons (Title, Hint, Export All, Import, Status, New Preset, separator,
"Open Presets Folder") are **not** tracked in `entries` and stay visible
regardless of filter state — they are chrome, not searchable content.

### Data flow

```
user keystroke
    → TMP_InputField.onValueChanged
    → TacticsPanel.currentRuleFilter = v
    → TacticsPanel.ApplyFilter()
        → (char/global) iterate ruleListContent children, SetActive per match
        → (presets)    currentPresetPanel.ApplyFilter(query)
                       → iterate entries list, SetActive per match
```

No destroy / recreate happens on keystroke → TMP focus is preserved → rapid
typing doesn't re-focus the field after each character.

### Edge cases

- **Linked rule whose preset was deleted / failed to resolve:**
  `EffectiveDisplayName` returns `rule.Name ?? ""`. The card is still matchable
  by its own Name field (usually blank for linked rules, so it falls through to
  empty-string — matches any empty filter, doesn't match any non-empty filter).
  Acceptable: orphaned linked rules are already a degraded state that the
  existing `RenderLinkedSummary` shows ("Linked to preset: <name>" with a broken
  link).
- **Null/empty name:** Match helper coalesces to `""`. Empty filter matches
  everything (including empty names); non-empty filter never matches an empty
  name.
- **Whitespace-only filter:** Treated as empty by `string.IsNullOrWhiteSpace`.
- **Permanent scrollbar + short list:** Handle fills the full track height.
  Unity renders this correctly as a fully-occupied track.
- **Rapid typing:** `SetActive` is cheap; layout rebuild is scoped to the
  VerticalLayoutGroup + ContentSizeFitter in `ruleListContent`. No debouncing.

### Reset behavior

- **Tab switch** (`SelectTab`): filter input cleared via `.text = ""`, which
  fires the listener and zeroes `currentRuleFilter`. `ApplyFilter` becomes a
  no-op pass that re-shows everything.
- **Panel toggle off/on**: the panel is not destroyed, only hidden. On re-open
  the filter query is whatever it was — but since the typical flow is
  Ctrl+T → close → reopen later, and the user explicitly reported wanting
  "quickly find a rule", preserving the filter across panel hide is fine. (If
  this turns out to be annoying in practice, clearing on show is a one-liner
  addition.)
- **Config reload / game switch**: `TacticsPanel` is recreated, fresh state.

### Persistence

No persistence. `currentRuleFilter` lives in memory only. No config-schema
changes.

## File changes

- `WrathTactics/UI/TacticsPanel.cs` — filter strip, state, `ApplyFilter`,
  scrollbar restyle, `RefreshRuleList` / `SelectTab` hooks.
- `WrathTactics/UI/PresetPanel.cs` — entries-tracking list, `ApplyFilter`
  method, `emptyMatchLabel`, `Rebuild` tail-call to `ApplyFilter`.
- `WrathTactics/UI/UIHelpers.cs` — `StringMatchesFilter(string, string)` static
  helper.

No new files. No model, persistence, or engine file touched.

## Testing

Manual smoke test on Steam Deck (no UI test infra in the mod):

1. Open `TacticsPanel` on a companion with > 10 rules. Confirm the right-edge
   scrollbar is clearly visible from the moment the panel opens.
2. Type a substring matching half the rules → only matching cards remain
   visible; scrollbar resizes to the shorter list. Other cards stay
   instantiated (no destroy flicker, no TMP focus loss).
3. Clear filter via `✕` button → all cards re-appear.
4. Clear filter by deleting characters → same result.
5. Switch to a different companion tab → filter input is empty, full rule list
   is visible.
6. Linked rule (has `PresetId`): typing the preset's name matches it; typing
   the rule's own `Name` (usually blank) does not.
7. Switch to Presets tab → same filter behavior on preset entries. Header
   buttons (Export, Import, New Preset, etc.) stay visible.
8. Presets tab, filter produces zero matches → "No matching presets" label
   appears below the header block.
9. Char tab, filter produces zero matches → "No matching rules" label appears.
10. Rapid typing (10+ chars/sec) → no focus loss, no dropped characters, UI
    stays responsive.
11. Regression: `+ New Rule` still adds a rule. `+ From Preset` popup still
    opens, still assigns. Edit / delete / toggle on a filtered-visible card
    still works. Presets Tab Create / Delete / Import / Export still work and
    honor the active filter after rebuild.
12. Post-combat pass and rule evaluation engine unaffected (UI-only change).
