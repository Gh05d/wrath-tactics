# UI: Widgets, Unity Layout, Pickers

Operative rules for `UI/` (TacticsPanel, RuleEditorWidget, ConditionRowWidget, PresetPanel, pickers, UIHelpers). Persistence routing rules (`onChanged` / `PersistEdit`) live in [`gotchas-persistence.md`](gotchas-persistence.md). Shared Unity/TMP gotchas: parent `wrath-mods/CLAUDE.md` reference (`docs/engine-api.md` §Unity UI & TMP).

## Unity Layout

- **Unity Rebuild pattern**: `Destroy()` on VLG/CSF is deferred — use `DestroyImmediate()` for layout components in `Rebuild()` to avoid duplicate layout calculators for one frame.
- **Clear-then-rescan: detach before `Destroy()`** — `Destroy()` lands end-of-frame; if the same frame re-iterates the container's children (`RefreshRuleList` → `ApplyFilter`), doomed cards still get counted. Fix: `SetParent(null, false)` before `Destroy`. NOT `SetActive(false)`+Destroy — `ApplyFilter` re-activates matching doomed cards.
- **Nested ScrollRects**: Inner steals scroll events from outer. Disable `inner.ScrollRect.enabled` unless content overflows; re-enable conditionally in `UpdateHeight()`.

## Widgets & Helpers

- **Input fields**: always use `UIHelpers.CreateTMPInputField` (or `CreateTMPInputFieldInRow` inside a layout-group row) — auto-attaches `ManualInputCaret` and sets `onFocusSelectAll = false`. Rolling a fresh `TMP_InputField` resurrects invisible-caret + wipe-on-click bugs.
- **Every surface and control comes from `UI/Widgets.cs`, every colour and metric from `UI/Theme.cs`** (spec `docs/superpowers/specs/2026-09-26-ui-ink-on-parchment-design.md`). No `new Color` and no `AddBackground` outside those two files (`PackPalette` excepted, `UIHelpers.AddBackground` is the primitive); regression check before merge: `grep -rn 'new Color' WrathTactics/UI | grep -v -E 'Theme|PackPalette|Widgets|UIHelpers'` must be empty.
- **Nothing is drawn on the book illustration** — the page sheet (`TacticsPanel.CreatePanel`, "PageSheet") sits under everything below the tab bar. Text goes on the sheet in ink (`Widgets.InkLabel`), on a band in `BandText`, or in a hint card. The 2026-07 outline/dark-surface experiments are history.
- **Band sprites multiply `Image.color`** — tint a band only through `Theme.PackBandTint` (channel-normalised, unit-tested in `ThemeTests`), never with a raw palette colour: rust/plum would black the band out.
- **Never put a `LayoutElement` with `preferredWidth >= 0` on a GameObject that also carries a LayoutGroup** — `LayoutElement` (priority 1) beats the group's own content size (priority 0), so the group collapses to its padding; `Widgets.InlineLink` shipped like that once (review finding, 2026-09-26) and every inline link was 6 px wide and unclickable. Let the group report its size, or set only `flexibleWidth`.
- **TMP inside a nested LayoutGroup reports 0 width in the game's TMP build** — a link/button that should size to its text must measure it (`tmp.GetPreferredValues(text).x`, font assigned first) and pin the root's `LayoutElement.preferredWidth`; `Widgets.InlineLink` / `ActionButton` do this. Observed on the Deck 2026-09-26: every inline link collapsed to its padding.
- **A LayoutGroup with `childForceExpandHeight = true` reports `flexibleHeight = 1` to ITS parent** — a button row inside a VLG then shares the spare height with the scroll list (Save-as-Pack buttons grew to 110 px). Pin `LayoutElement.flexibleHeight = 0` on such rows; conversely a child of a row with force-expand OFF needs `flexibleHeight = 1` or a `preferredHeight`, or it is 0 px tall (rule names vanished).
- **`Image.color` multiplies the sprite — a tint can only darken.** Grey glyphs (chevron, arrows, X) on the mauve band are tinted to `Theme.Ink`, never to a light colour; pack colours go through `Theme.PackBandTint` onto the pale `toggle_off` circle / the band, never onto the purple `toggle_on` sprite (near-black dots).
- **Band sprites have ~112 px brush-stroke fades at both ends** — `Widgets.ApplyBand` renders them at `pixelsPerUnitMultiplier = 2` and `Theme.BandPaddingX` (26 × scale) keeps text out of the fade; band text carries a soft ink outline (`Theme.BandTextOutline`) so it survives the translucent ends. Neighbouring bands with less padding overlap each other.
- **`Theme.BaseScale` (1.15) is folded into `UIHelpers.FontScale`** on top of the game's font slider (which caps at 1.2); every Theme metric and font size already includes it.
- **Rows are HorizontalLayoutGroups with proportional `flexibleWidth`** (`Widgets.Row` + `Widgets.InRow`; `PopupSelector.CreateInRow`, `CreateTMPInputFieldInRow`). Sibling order = visual order — reposition with `SetSiblingIndex`, not anchors. The fractional-anchor call sites are gone; do not reintroduce `SetAnchor` inside a row.
- **Popups are `Widgets.PaperPopup`** — dim + paper + inset content; callers attach outside-click to `OverlayButton` and their own Escape handler (`EscapeCloser` for transient pickers). The controller MonoBehaviour sits on `Popup`, so `transform.parent` is the overlay.
- **`Widgets.HintCard` keeps a fixed `preferredHeight`** (`Theme.HintHeight` / `HintHeightShort`): a longer string clips silently — keep replacements no longer, or raise the height.
- **Fixed pixel heights on text rows must multiply `UIHelpers.FontScale`** — take them from `Theme` (`RowHeight`, `HeaderHeight`, …), which already do; `LayoutElement.preferredHeight` does NOT auto-scale on its own.
- **UI display labels for compound enum names**: `ConditionRowWidget.PropertyLabel` maps identifiers like `SpellDCMinusSave` → `"DC − Save"`. New compound `ConditionProperty` needs a `PropertyLabel` case. Use Unicode minus `−` (U+2212), not ASCII `-`.
- Equality conditions use inline `=`/`!=` operator dropdowns. Extend the operator pattern to new properties (HasBuff, HasCondition, CreatureType, Alignment) rather than adding a perpendicular Negate/NOT button.

## Pickers

- **Buff picker search ranking**: `BuffPickerOverlay.RenderFilteredLayout` sorts by (prefix-match first, shorter-name first). Pure alphabetical breaks search.

## Target defaults on action change (v1.31 dev)

- **`TargetDefaults` (Models) is the SSoT for "what target does a fresh rule get when the action changes"** — Attack/ThrowSplash → `EnemyHighestThreat`, MoveToTarget → `EnemyNearest`, enemy-only ability pick (`CanTargetEnemies && !Friends && !Self`, variant blueprint wins over parent) → `EnemyHighestThreat`. It only ever replaces `TargetType.Self` (the constructor default, enum index 0), so an explicit player choice survives. Extend the switch there, not in the widget; unit-tested in `TargetDefaultsTests`.
- The action-type handler in `RuleEditorWidget.Action.cs` calls `RebuildBody()` when a default applied (the target row is a separate `PopupSelector` and would otherwise show the stale value). Rebuilding from inside a popup callback is the established pattern (target-type handler does the same). The auto-first-entry paths (`SetupSpellSelector`, `RefreshSpellSelector`) deliberately do NOT apply the ability default — the first spell in a book is arbitrary and would flip the target before the player picked anything.
- **Deploy check for identifiers**: `strings -el` finds only UTF-16 user strings (log text). Method/type names live in UTF-8 metadata — use plain `strings` for those.
