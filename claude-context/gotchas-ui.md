# UI: Widgets, Unity Layout, Pickers

Operative rules for `UI/` (TacticsPanel, RuleEditorWidget, ConditionRowWidget, PresetPanel, pickers, UIHelpers). Persistence routing rules (`onChanged` / `PersistEdit`) live in [`gotchas-persistence.md`](gotchas-persistence.md). Shared Unity/TMP gotchas: parent `wrath-mods/CLAUDE.md` reference (`docs/engine-api.md` §Unity UI & TMP).

## Unity Layout

- **Unity Rebuild pattern**: `Destroy()` on VLG/CSF is deferred — use `DestroyImmediate()` for layout components in `Rebuild()` to avoid duplicate layout calculators for one frame.
- **Clear-then-rescan: detach before `Destroy()`** — `Destroy()` lands end-of-frame; if the same frame re-iterates the container's children (`RefreshRuleList` → `ApplyFilter`), doomed cards still get counted. Fix: `SetParent(null, false)` before `Destroy`. NOT `SetActive(false)`+Destroy — `ApplyFilter` re-activates matching doomed cards.
- **Nested ScrollRects**: Inner steals scroll events from outer. Disable `inner.ScrollRect.enabled` unless content overflows; re-enable conditionally in `UpdateHeight()`.

## Widgets & Helpers

- **Input fields**: always use `UIHelpers.CreateTMPInputField` — auto-attaches `ManualInputCaret` and sets `onFocusSelectAll = false`. Rolling a fresh `TMP_InputField` resurrects invisible-caret + wipe-on-click bugs.
- **Hint/explainer strips: always use `UIHelpers.AddHintCard`** — FontScale-scaled height, wrap+ellipsis, raycastable (mouse wheel reaches the enclosing ScrollRect), and a `PanelSurface` background of its own. Its `preferredHeight` is fixed (default 52f, callers pass 40f): a reworded string longer than the one it replaces clips silently — keep replacements no longer, or raise the height.
- **Readability on the book-page art comes from a dark surface, not a text outline.** Any label drawn straight onto the parchment needs a background: `AddSurfaceLabel` (titles, status lines, empty states, list headers — `PanelSurface` / `PanelHeaderSurface`) or `AddHintCard`. Outline-only was tried twice and rejected twice in play-test — first as the v1.26.0 "white text on light background" defect, then as `AddPageLabel`, the fix for it. `AddPageLabel` now survives on exactly one label: treat it as legacy, not as the pattern. Labels already sitting on their own dark row (buttons, chips, swatches, member rows) keep plain `AddLabel`.
- **A group of related rows belongs in ONE dark container, not N individually-backed rows** — `PackPanel.CreateMemberEditor` computes the box height up front and parents every header/member/available row into a single `AddBackground`ed box, so the block reads as attached to the row that opened it. Per-row backgrounds read as loose fragments (play-test finding).
- **Fixed pixel heights on text rows must multiply `UIHelpers.FontScale`** — `AddLabel` scales fontSize with the game font slider (clamp 0.5–3.0) but `LayoutElement.preferredHeight` does NOT auto-scale; hardcoded heights overflow/truncate at raised scales (precedent: PopupSelector `36f * FontScale`).
- **UI display labels for compound enum names**: `ConditionRowWidget.PropertyLabel` maps identifiers like `SpellDCMinusSave` → `"DC − Save"`. New compound `ConditionProperty` needs a `PropertyLabel` case. Use Unicode minus `−` (U+2212), not ASCII `-`.
- Equality conditions use inline `=`/`!=` operator dropdowns. Extend the operator pattern to new properties (HasBuff, HasCondition, CreatureType, Alignment) rather than adding a perpendicular Negate/NOT button.

## Pickers

- **Buff picker search ranking**: `BuffPickerOverlay.RenderFilteredLayout` sorts by (prefix-match first, shorter-name first). Pure alphabetical breaks search.
