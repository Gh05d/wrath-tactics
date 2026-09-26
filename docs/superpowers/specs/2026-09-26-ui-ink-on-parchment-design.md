# UI Redesign: Ink on Parchment

**Date:** 2026-09-26
**Status:** approved design, awaiting implementation plan
**Scope:** visual layer of the Tactics panel only. No behaviour, persistence, or engine change.

## 1. Problem

The panel mixes three visual languages: Owlcat sprites (title bar, tabs, `+ New Rule`, `+ From Preset`), flat dark-grey admin surfaces (filter strip, hint cards, pack row, rule headers, dropdowns, popups), and web-style coloured text buttons (green ON, blue Copy, purple Export, red X, green `+ Condition` bars, gold `Unlink & Edit` bar). Roughly 30 colour literals are scattered across `UI/*.cs`; three shades of red mean "delete", four greens mean "add". Text glyphs (`^`, `v`, `X`, `▾`) stand in for icons. Condition rows are laid out with per-property fractional anchors, and most row heights are fixed pixels that ignore the game's font-scale slider.

The user's verdict: "looks hacked together", primarily the style clash (option 1 of the three named problems), secondarily density and structure.

## 2. Decisions (all confirmed by the user in the visual brainstorm)

| Question | Decision |
|---|---|
| Direction | **A: Ink on parchment.** Keep the open book; remove every dark admin surface; everything inside the book reads as ink and Owlcat brush strokes on paper. |
| Dropdown style | **Brush band** (`UI_Settings_BackValue`, the value background of the game's Settings menu), light text, chevron. |
| Copy / Export / Preset in the rule header | **Text links inside the band** (small, light, italic). Not hidden behind a menu. |
| Popups (dropdown lists, spell/buff picker, save-as-pack, pack-chip menu) | **Parchment note** (`UI_BackgroundTooltipPaper`), ink text, selected row as brush band, book dimmed behind. |

Rendered mockups (HTML built with the extracted sprites, screenshotted): `2026-09-26-ui-mockups/rule-card.png`, `global-tab.png`, `popup.png`.

## 3. Visual system

### 3.1 Surfaces

- **Backdrop** behind the book: semi-transparent black (`0,0,0,0.6`) over the game instead of opaque black. Removes the hard black letterbox bars from the 2:1 AspectRatioFitter.
- **Page sheet**: one continuous `InnerParchment` image spans the whole content area under the tab bar (filter row, hint, settings, pack row, rule list). No text is ever drawn on the book illustration itself. The per-card parchment goes away; cards sit on the sheet.
- **Rule card**: 1 px ink border (`Ink` at 55 % alpha) on the sheet, faint inner highlight. Header band inside the border.
- **Hint card**: `UI_Journal_Annotation` 9-slice over a dark backing colour (the sprite fill alone is too transparent on parchment), light italic text.
- **Popup**: `UI_BackgroundTooltipPaper` 9-slice, ink text, full-screen dim (`0,0,0,0.35`) behind it.

### 3.2 Controls

| Role | Sprite / treatment | Replaces |
|---|---|---|
| Rule header | `UI_Settings_BackValue` band (mauve). `UI_Settings_BackValueBlue` for preset-linked rules. Pack rules: mauve band tinted with `PackPalette.HeaderTint`. | grey / blue-grey / brown header boxes |
| ON/OFF | `UI_PointButtonBig_Hover` (on) / `UI_PointButtonBig_Default` at 70 % (off) | green/grey "ON" text button |
| Move up / down | `UI_RoundButtonNextIcon_Default` rotated −90° / +90°, `_Hover` for hover | `^` / `v` text buttons |
| Delete rule | `UI_CharScreen_IconDelete` (red brush X) | red "X" box |
| Delete condition / fallback, close popup | `UI_EscIcon_Default` / `_Hover` (ink X) | red "X" box / "X" label |
| copy · export · preset | italic light text links inside the band, dotted underline on hover | blue / purple / green boxes |
| Dropdown (`PopupSelector`, spell/buff pickers' trigger) | `UI_Settings_BackValue` band, light text, chevron = `UI_CheckIcon_Default` scaled to 11 px | flat grey box with "v" |
| Text / number input | ink frame: `UI_Loot_Slots` 9-slice if it holds up at row height, else 1 px `Ink` border on `InsetPaper` | flat dark box |
| Checkbox (Hide HUD button) | 18 px ink-framed square, `UI_CheckIcon_Hover` when checked | full-width Owlcat button |
| Inline actions (`+ Condition`, `+ Or-group`, `+ Fallback`, `Unlink & edit`, `+ Apply pack`, `Save list as pack`, pack-chip menu items) | italic `InkMuted` text link; add-actions prefixed with `UI_CharScreen_IconAdd` at 20 px | full-width green/blue/gold bars |
| Primary actions (`+ New Rule`, `+ From Preset`, popup Cancel/Select, preset/pack tab primary buttons) | existing `ApplyActionButton` (Owlcat `UI_Button_*`) | unchanged / flat boxes |
| Tabs, title bar, close | unchanged | — |
| OR divider | `UI_WightLine_Simple` line, "or" in `InkMuted` italic centred | "– OR –" label |
| Section divider (between top controls and list; between IF block and THEN block) | `UI_CharScreen_Separator2` at 55 % | 4 px spacer |
| Pack chip | rounded ink-framed pill, colour dot from `PackPalette`, name + `▾` | coloured rectangle |
| Preset / pack list rows (Presets tab, Packs tab, pack member editor) | same anatomy as a collapsed linked rule card: band header (name, origin, inline links), one-line ink summary | grey rows with 80 px Edit/Delete boxes |

### 3.3 Typography

- Labels `IF`, `THEN`, `TARGET`, `COOLDOWN`, `PACKS`: small caps, letter-spacing 0.08 em, `Ink`, 15 px.
- Rule name in band: 19 px, weight 500, light, 1 px dark shadow.
- Band text (dropdown values, header links): `BandText`, never below `14 × FontScale`.
- Body text: `Ink`. Secondary (`rounds`, origin, summaries): `InkMuted` italic.
- Status lines: `StatusOk` / `StatusWarn` / `StatusError` from the theme, not inline literals.

### 3.4 Colour tokens (`UI/Theme.cs`)

| Token | Value | Use |
|---|---|---|
| `Ink` | `#2B1D12` | body text, frames |
| `InkMuted` | `#6B4D33` | secondary text, inline links |
| `InkLabel` | `#4A3220` | section labels |
| `InsetPaper` | `#FFFAEE` at 55 % | input / checkbox fill |
| `BandText` | `#F3E9D8` | text on any band |
| `HintBacking` | `#302819` at 90 % | behind `UI_Journal_Annotation` |
| `Destructive` | only via `IconDelete` sprite; no red boxes remain | — |
| `StatusOk` / `StatusWarn` / `StatusError` | `#3F6B3A` / `#8A5A1A` / `#8A2A2A` | status labels on parchment |
| `Dim` | `#000000` at 35 % (popup) / 60 % (backdrop) | overlays |
| `PackPalette` | unchanged (append-only, indices persisted) | band tint |

Every existing `new Color(...)` in `UI/` outside `Theme.cs` and `PackPalette.cs` is removed.

### 3.5 Metrics tokens

All multiplied by `UIHelpers.FontScale`:

| Token | Base px |
|---|---|
| `RowHeight` (condition / action / target / cooldown rows) | 30 |
| `HeaderHeight` (rule band) | 40 |
| `CollapsedBodyHeight` (linked card summary row) | 30 |
| `InlineRowHeight` (`+ Condition` rows) | 24 |
| `DividerHeight` | 14 |
| `IconSmall` / `IconMedium` | 18 / 24 |
| `BandPaddingX` (inset so text clears the soft band edge) | 14 |
| `PopupWidth` / `PopupHeight` (spell picker) | 480 / 540 |
| `RowGap` / `CardGap` | 6 / 12 |

`RuleEditorWidget.UpdateHeight` sums these tokens instead of its private constants. Its clamp (`160–500`) becomes `MinCardHeight` / `MaxCardHeight` tokens.

## 4. Architecture

### 4.1 `ThemeProvider` (extended)

Loads the new sprites with their 9-slice borders (Unity order: left, bottom, right, top). Values are the `m_Border` metadata read via UnityPy from `sharedassets0.assets` (game build of 2026-02-06):

| File | Source sprite | Size | Border L,B,R,T |
|---|---|---|---|
| `band_mauve.png` | `UI_Settings_BackValue` | 391×63 | 112,0,112,0 |
| `band_blue.png` | `UI_Settings_BackValueBlue` | 391×63 | 112,0,112,0 |
| `popup_paper.png` | `UI_BackgroundTooltipPaper` | 554×358 | 139,101,145,89 |
| `hint_annotation.png` | `UI_Journal_Annotation` | 725×285 | 72,58,72,67 |
| `icon_add.png` | `UI_CharScreen_IconAdd` | 60×62 | — |
| `icon_delete.png` | `UI_CharScreen_IconDelete` | 56×61 | — |
| `icon_x.png` / `icon_x_hover.png` | `UI_EscIcon_Default` / `_Hover` | 49×49 | — |
| `icon_check.png` / `icon_check_hover.png` | `UI_CheckIcon_Default` / `_Hover` | 40×43 | — |
| `icon_arrow.png` / `icon_arrow_hover.png` | `UI_RoundButtonNextIcon_Default` / `_Hover` | 48×44 | — |
| `toggle_on.png` / `toggle_off.png` | `UI_PointButtonBig_Hover` / `_Default` | 40×40 | — |
| `divider_flourish.png` | `UI_CharScreen_Separator2` | 1041×46 | 114,0,94,0 |
| `divider_line.png` | `UI_WightLine_Simple` | 586×7 | 16,0,16,0 |
| `input_frame.png` | `UI_Loot_Slots` | 135×135 | 12,11,13,13 |

Adds about 420 KB to the release ZIP. `Assets/icons/SOURCES.md` gets a row per file. Every `Apply*` helper stays a no-op when its sprite failed to load, and the flat-colour fallbacks are the theme tokens, not ad-hoc greys.

Sprites that are no longer referenced after the migration (`tab_header_*` stay; check `inner_parchment` usage) are removed from `Assets/icons/` in the cleanup layer.

### 4.2 `UI/Theme.cs` (new)

Static class holding the colour and metric tokens from §3.4 and §3.5. Metrics are properties that read `UIHelpers.FontScale` at access time, so a font-scale change followed by the existing panel rebuild picks them up.

### 4.3 `UI/Widgets.cs` (new)

Factory methods named by role. Each returns the root `GameObject` (or the TMP / input component where callers need it) and wires hover states itself.

```
BandHeader(parent, BandStyle style, Color? tint)          // rule / list-row header
BandDropdown(parent, label, onClick) : PopupSelector-compatible trigger
IconButton(parent, Icon icon, onClick)                    // Icon.Add | Delete | X | ArrowUp | ArrowDown
ToggleDot(parent, bool on, onClick)
InlineLink(parent, text, onClick, Icon? prefix)
Checkbox(parent, label, bool value, onChanged)
InkInput(parent, name, placeholder, ...)                  // wraps CreateTMPInputField
HintCard(parent, text)                                    // replaces AddHintCard
SectionLabel(parent, text)                                // IF / THEN / …
OrDivider(parent) / FlourishDivider(parent)
PaperPopup(title, width, height) : (root, contentParent, closeButton)
StatusLabel(parent, Status kind, text)
PackChip(parent, pack, onClick)
```

Rules:

- `Widgets` and `Theme` are the only places in `UI/` that construct `Color` values or call `AddBackground`.
- `PopupSelector` keeps its API but its trigger is built by `BandDropdown` and its list by `PaperPopup`.
- `SpellPickerOverlay`, `BuffPickerOverlay`, `SaveAsPackOverlay`, the pack-chip menu and `PopupSelector`'s list all use `PaperPopup`. The picker search ranking, `ManualInputCaret`, and `onFocusSelectAll = false` are untouched.
- Widgets keep invoking `onChanged?.Invoke()`; nothing new calls `ConfigManager.Save()`.

### 4.4 Layout changes bundled with the restyle

- `ConditionRowWidget`: fractional anchors replaced by a `HorizontalLayoutGroup`; subject / property / operator get `preferredWidth` tokens, the value selector gets `flexibleWidth = 1`, the delete icon is fixed-size. The post-creation repositioning of the property selector goes away.
- `RuleEditorWidget.Action/Target/Cooldown`: same HLG treatment.
- `TacticsPanel` control row: filter input and the two Owlcat buttons share one HLG row; `ControlRow` and `FilterStrip` merge.
- The `Hide HUD button` row becomes a `Checkbox`.
- `PackPanel.CreateMemberEditor` keeps its hand-computed box height (documented reason: CSF fights the host VLG) but draws with the list-row anatomy.

### 4.5 Removed

`AddPageLabel`, `AddSurfaceLabel`, `AddHintCard`, `EnsureAllHoverable`, `MakeButton`'s dead `bgColor` parameter, the three hand-rolled outline copies (`TacticsPanel.cs` title / empty-label, `PortraitToggleOverlay.cs`), the `TabNormal` / `TabSelected` fallback colours, and `PanelSurface` / `PanelHeaderSurface`.

`PortraitToggleBadge` / `PortraitToggleOverlay` (party-portrait badges) are out of scope except for dropping their outline copy; they live on the HUD, not in the book.

## 5. Implementation layers

Each layer builds, deploys with `./deploy.sh`, and is smoke-tested on the Deck before the next starts. A partially migrated panel is acceptable between layers (old and new styles side by side), but every layer ends in a working panel.

1. **Foundation** — extract and bundle sprites, `SOURCES.md`, `ThemeProvider` entries, `Theme.cs`, `Widgets.cs`, backdrop dim. No caller migrated yet; verify sprites load (`ThemeProvider initialised — N/N`).
2. **Panel chrome** — page sheet, merged filter/control row, `HintCard`, `Checkbox`, pack row with chips and inline links, flourish divider.
3. **Rule card** — band header with toggle dot, links, arrows, delete icon; body rows on HLG with `BandDropdown`, `InkInput`, section labels, inline links, OR divider; collapsed linked-card body; `UpdateHeight` on tokens.
4. **Popups** — `PaperPopup` for `PopupSelector` list, spell picker, buff picker, save-as-pack, pack-chip menu.
5. **Presets and Packs tabs** — list rows on the card anatomy, member editor restyled.
6. **Cleanup** — remove §4.5 helpers and unused sprites, `grep -n 'new Color' WrathTactics/UI` must hit only `Theme.cs` and `PackPalette.cs`, update `claude-context/gotchas-ui.md` (surface rules are superseded by "use `Widgets`"), version bump.

## 6. Testing

No unit coverage exists or is added for UI. Verification per layer on the Steam Deck with the synthetic smoke pack (`docs/testing/deck-smoke`):

- Panel opens via Ctrl+T and HUD button; closes via ESC and the X.
- Every tab renders; a rule with two OR groups, a fallback, and a cooldown fits its card at font scale 0.5, 1.0, and 2.0 (Settings → font size slider), no clipped band text, no overlap with the scrollbar gutter.
- Every dropdown opens its paper popup, selection persists after close/reopen.
- Spell picker and buff picker: search, select, cancel; a 300+ entry buff list scrolls.
- Preset link / unlink, pack apply / detach / remove, save list as pack, export / import round-trip.
- Screenshot of Global tab and one expanded character rule saved to `docs/superpowers/specs/2026-09-26-ui-mockups/` as `after-*.png` at the end of layer 5.

## 7. Risks

- **Readability of light text on the band at small font scale.** Mitigation: `BandText` minimum size, `BandPaddingX` token, screenshot check at scale 0.5 before layer 3 is called done.
- **Card height drift.** Bands have soft edges and the HLG rows change intrinsic heights; `UpdateHeight` constants must be re-measured on the Deck, not derived from the mockup.
- **`UI_Loot_Slots` as input frame may look wrong at 30 px height** (the sprite is a 135 px square). Fallback is decided in layer 1: if it squishes, inputs use a 1 px ink border.
- **Scope.** About 6000 lines of UI code are touched. The layer order keeps the mod releasable after each layer; if the work stalls, layers 1–3 alone already remove the style clash on the character tabs.
- **Game patch changes sprite metadata.** Same exposure as the existing sprites; the re-extraction procedure in `SOURCES.md` covers it.

## 8. Out of scope

- Any new feature, condition, action, or persistence change.
- HUD button and portrait badges beyond removing the duplicated outline code.
- Controller / console UI mode.
- Localization changes (no new strings; `+ Or-group` reuses the existing `+ OR (new group)` key or gets a wording-only edit in `en_GB` and the other locales).
