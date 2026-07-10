# UI: Widgets, Unity Layout, Pickers

Operative rules for `UI/` (TacticsPanel, RuleEditorWidget, ConditionRowWidget, PresetPanel, pickers, UIHelpers). Persistence routing rules (`onChanged` / `PersistEdit`) live in [`gotchas-persistence.md`](gotchas-persistence.md). Shared Unity/TMP gotchas: parent `wrath-mods/CLAUDE.md` reference (`docs/engine-api.md` §Unity UI & TMP).

## Unity Layout

- **Unity Rebuild pattern**: `Destroy()` on VLG/CSF is deferred — use `DestroyImmediate()` for layout components in `Rebuild()` to avoid duplicate layout calculators for one frame.
- **Clear-then-rescan: detach before `Destroy()`** — `Destroy()` lands end-of-frame; if the same frame re-iterates the container's children (`RefreshRuleList` → `ApplyFilter`), doomed cards still get counted. Fix: `SetParent(null, false)` before `Destroy`. NOT `SetActive(false)`+Destroy — `ApplyFilter` re-activates matching doomed cards.
- **Nested ScrollRects**: Inner steals scroll events from outer. Disable `inner.ScrollRect.enabled` unless content overflows; re-enable conditionally in `UpdateHeight()`.

## Widgets & Helpers

- **Input fields**: always use `UIHelpers.CreateTMPInputField` — auto-attaches `ManualInputCaret` and sets `onFocusSelectAll = false`. Rolling a fresh `TMP_InputField` resurrects invisible-caret + wipe-on-click bugs.
- **Hint/explainer strips: always use `UIHelpers.AddHintCard`** — FontScale-scaled height, wrap+ellipsis, raycastable (mouse wheel reaches the enclosing ScrollRect), outlined. General rule: any label sitting directly on the book-page art (not on an InnerParchment card) needs the outline pattern (`outlineWidth 0.25f` + black) — plain grey washes out on the light parchment (title/toggle labels are the precedent).
- **Fixed pixel heights on text rows must multiply `UIHelpers.FontScale`** — `AddLabel` scales fontSize with the game font slider (clamp 0.5–3.0) but `LayoutElement.preferredHeight` does NOT auto-scale; hardcoded heights overflow/truncate at raised scales (precedent: PopupSelector `36f * FontScale`).
- **UI display labels for compound enum names**: `ConditionRowWidget.PropertyLabel` maps identifiers like `SpellDCMinusSave` → `"DC − Save"`. New compound `ConditionProperty` needs a `PropertyLabel` case. Use Unicode minus `−` (U+2212), not ASCII `-`.
- Equality conditions use inline `=`/`!=` operator dropdowns. Extend the operator pattern to new properties (HasBuff, HasCondition, CreatureType, Alignment) rather than adding a perpendicular Negate/NOT button.

## Pickers

- **Buff picker search ranking**: `BuffPickerOverlay.RenderFilteredLayout` sorts by (prefix-match first, shorter-name first). Pure alphabetical breaks search.
