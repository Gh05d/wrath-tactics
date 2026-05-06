# Wrath Tactics — UI Re-Style & Optional ModMenu Integration

**Date:** 2026-05-06
**Target version:** v1.10.0 (Pillar A) and v1.11.0 (Pillar B)
**Type:** Minor feature, additive only

## Goal

Make the Wrath Tactics UI feel like a native part of Pathfinder: Wrath of the
Righteous and expose its few JSON-only settings through a discoverable in-game
options panel, without breaking any existing user's setup.

The work splits cleanly into two orthogonal pillars that ship as separate
releases.

## Scope and Non-Goals

### In scope

- Visual restyle of the main Tactics panel (chrome + buttons + tab headers).
- Bundled PNG sprite assets extracted from the game's UI atlas.
- Optional integration with [ModMenu](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/370)
  for five user-facing settings (Mod Menu detected at runtime; absent =
  current JSON-only behaviour).
- A clean, single-source `ThemeProvider` facade that all widgets call into.

### Explicitly out of scope (this iteration)

- Picker overlays (`BuffPickerOverlay`, `SpellPickerOverlay`) — stay Unity-default.
- Inner widgets in `ConditionRowWidget` (dropdowns, input fields) — stay Unity-default.
- HUD entry button at bottom-left — stays unchanged.
- Per-category logging toggles in ModMenu — single "Verbose Logging" master toggle only.
- Hard dependency on ModMenu — graceful fallback only.

Future iterations may pick these up; they are tracked at the bottom of this doc.

## Architecture

Two independent pillars, each shippable on its own.

### Pillar A: Asset-based panel restyle

A new `WrathTactics/Engine/AssetLoader.cs` (1:1 port from BuffIt2TheLimit's
`Utilities/AssetLoader.cs` — same project family, same author, same convention)
loads PNG sprites from `WrathTactics/Assets/icons/` once during mod init and
caches them in a `Dictionary<string, Sprite>`.

A new `WrathTactics/UI/ThemeProvider.cs` is the only consumer of those names
elsewhere in the code base. It exposes typed properties (`PanelBackground`,
`TitleBarBackground`, `CloseButton{Normal,Hover,Pressed}`, `ActionButton{...}`,
`TabHeader{Active,Inactive}`, `ScrollbarTrack`, `ScrollbarHandle{...}`) plus
two helper methods `ApplyPanel(GameObject)` and `ApplyButton(GameObject)` that
encapsulate `Image.sprite =` / `Button.spriteState =` plumbing.

Existing widgets call into `ThemeProvider` from `UIHelpers.MakeButton()`,
`UIHelpers.Create()`, `TacticsPanel` (title + tabs), `PresetPanel` (action
buttons), and `RuleEditorWidget` (action buttons). No widget directly touches
`AssetLoader`.

### Pillar B: Optional ModMenu integration

A new `WrathTactics/Compatibility/ModMenuCompat.cs` mirrors the existing
`Compatibility/BubbleBuffsCompat.cs` pattern: detect the partner mod
optionally, fail silently if absent.

Detection: `AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "ModMenu")`.

Registration uses **reflection only** (`Type.GetType("ModMenu.ModMenu, ModMenu")`,
`Type.GetType("ModMenu.Settings.SettingsBuilder, ModMenu")`, `MethodInfo.Invoke`)
so the Wrath Tactics assembly loads cleanly even when ModMenu is absent — no
direct `using ModMenu;` clauses anywhere in our code.

Setting values continue to live in `ConfigManager` and its JSON file — ModMenu
is purely a UI front-end. Without ModMenu installed, the JSON file is the
only knob (= today's status quo, no regression).

### Why two pillars

Pillar A's risk surface is the PNG-loading pipeline, 9-slice borders, and
layout shifts in every restyled widget. Pillar B's risk surface is reflection
against a third-party API. Coupling them in one release means a bug in either
delays both. Splitting into v1.10.0 and v1.11.0 keeps the diagnosis cheap.

## Components

### New files

| Path | Purpose |
|---|---|
| `WrathTactics/Engine/AssetLoader.cs` | PNG → Sprite loader, ported from BuffIt. Static cache `Dictionary<string, Sprite>`. |
| `WrathTactics/UI/ThemeProvider.cs` | Single-source facade for all themed visuals. Properties + `ApplyPanel`/`ApplyButton` helpers. |
| `WrathTactics/Compatibility/ModMenuCompat.cs` | Soft-detection + reflective registration. Empty/no-op when ModMenu absent. |
| `WrathTactics/Assets/icons/*.png` | ~12 sprite PNGs. See *Asset bundle* section below. |

### Modified files

| File | Change |
|---|---|
| `WrathTactics/Main.cs` | Init hooks: `AssetLoader.Init()`, `ThemeProvider.Init()`, `ModMenuCompat.TryRegister()`. |
| `WrathTactics/UI/UIHelpers.cs` | `MakeButton()` calls `ThemeProvider.ApplyButton()`. `Create()` (panel root) calls `ThemeProvider.ApplyPanel()`. `RefreshFontScale()` honours `FontScaleOverride`. |
| `WrathTactics/UI/TacticsPanel.cs` | Title bar uses `ThemeProvider.TitleBarBackground` + close-button sprites. Tab headers use `ThemeProvider.TabHeaderActive/Inactive`. |
| `WrathTactics/UI/PresetPanel.cs` | Action buttons via `ThemeProvider.ActionButton*`. |
| `WrathTactics/UI/RuleEditorWidget.cs` | "Add Rule"/"Save Preset" buttons via `ThemeProvider.ActionButton*`. |
| `WrathTactics/Models/TacticsConfig.cs` | Four new persisted fields on the config model (see *Settings* section). `ConfigManager` continues to handle load/save; the model owns the schema. |
| `WrathTactics/Engine/TacticsEvaluator.cs` | `Tick()` gated on `Config.MasterEnabled`. In-combat path reads `InCombatTickIntervalSeconds`. |
| `WrathTactics/WrathTactics.csproj` | `<None Include="Assets\icons\*.png">` with `CopyToOutputDirectory=PreserveNewest`. Optional `<Reference Include="$(WrathInstallDir)\Mods\ModMenu\ModMenu.dll" Private="false">` with `Condition="Exists(...)"` for build-time IntelliSense only. |
| `WrathTactics/Info.json` | **Unchanged.** No new `Requirements` entry. ModMenu is soft-detected. |

## Asset Bundle

Bundled PNGs under `WrathTactics/Assets/icons/`:

| File | Purpose | Size (px) | 9-slice |
|---|---|---|---|
| `panel_background.png` | Main panel background | 32×32 | yes |
| `titlebar_background.png` | Top title bar | 32×40 | yes |
| `close_button_normal.png` | Close × normal | 32×32 | no |
| `close_button_hover.png` | Close × hover | 32×32 | no |
| `close_button_pressed.png` | Close × pressed | 32×32 | no |
| `action_button_normal.png` | "Add Rule"/"Save"/etc. normal | 32×40 | yes |
| `action_button_hover.png` | Action button hover | 32×40 | yes |
| `action_button_pressed.png` | Action button pressed | 32×40 | yes |
| `tab_header_active.png` | Active companion tab | 24×40 | yes |
| `tab_header_inactive.png` | Inactive tab | 24×40 | yes |
| `scrollbar_track.png` | Scrollbar background | 16×8 | yes |
| `scrollbar_handle.png` | Scrollbar handle | 16×16 | yes |

Expected total: ~50–150 KB.

### 9-slice handling

`Sprite.Create()` alone does not set 9-slice borders. `AssetLoader` sets them
explicitly per asset:

```csharp
sprite.border = new Vector4(left, bottom, right, top);
```

Per-asset border constants live next to the load call. Components consuming
these sprites must use `Image.type = Image.Type.Sliced` for stretch to honour
the borders.

### Source / extraction

Owlcat UI sprites live in `Wrath_Data/sharedassets*.assets` and
`Wrath_Data/resources.assets`. Standard WoTR-modding workflow:

1. **AssetStudio** (open source) opens the bundle, exports sprites as PNG
   including original 9-slice borders. Look for `UI_Window_Common_Bg`,
   `UI_Button_Common_{Normal,Hover,Pressed}`, `UI_Tab_{Active,Inactive}`.
2. Fallback: **uTinyRipper** if AssetStudio fails on the current Wrath build.
3. Cross-reference BuffIt2TheLimit's `Assets/icons/` for established naming
   conventions and 9-slice metrics (their `UI_HudCharacterFrameBorder_*` is
   HUD-frame, not panel-chrome — reference only).

Extraction is a manual one-time step on the laptop (with `GameInstall/`
symlinked from the wrath-epic-buffing repo). Result: PNG files committed
under `Assets/icons/` plus a small `Assets/icons/SOURCES.md` documenting
where each sprite came from and any substitutions made.

**Risk:** if a target sprite name doesn't exist verbatim in Wrath 1.4, we
substitute the closest visual match and document it in `SOURCES.md`.

## Settings (Pillar B)

The five settings exposed via the ModMenu tab "Wrath Tactics":

| Setting | Type | Default | Range | `TacticsConfig` field |
|---|---|---|---|---|
| Enable Wrath Tactics | Toggle | `true` | — | `MasterEnabled` |
| In-Combat Tick Interval | Slider | `0.5s` | `0.1`–`2.0`, step `0.1` | `InCombatTickIntervalSeconds` |
| Out-of-Combat Tick Interval | Slider | `2.0s` | `0.5`–`10.0`, step `0.5` | `OutOfCombatTickIntervalSeconds` (already exists) |
| Verbose Logging | Toggle | `false` | — | `VerboseLogging` |
| UI Font Scale Override | Slider | `0` (= use game) | `0`–`2.0`, step `0.1` | `FontScaleOverride` |

Notes:

- `FontScaleOverride == 0f` means "no override; defer to the game's UI Text
  Scale" (today's v1.9.0 behaviour). Sentinel value because ModMenu sliders
  have no `null` state.
- Settings keys use prefix `wrath-tactics-` (e.g. `wrath-tactics-master-enabled`)
  to avoid collisions with other mods.

### Sync flow (with ModMenu)

1. `Main.Load()` → `ConfigManager.Load()` (reads JSON).
2. `Main.Load()` → `ModMenuCompat.TryRegister()` (reads current values from
   `ConfigManager.Current` and passes them as defaults to each setting builder).
3. User edits a slider in the in-game options menu →
   `OnValueChanged(newValue)` callback → writes to
   `ConfigManager.Current.<Field>` → calls `ConfigManager.Save()`.
4. Game restart → JSON reloaded → ModMenu shows persisted values.

### Sync flow (without ModMenu)

Step 2 is skipped (early return on `!ModMenuPresent`). All settings remain
JSON-only — today's status quo. No "ModMenu missing" warning surfaced;
this is purely additive.

## Migration / Release Plan

### v1.10.0 — Pillar A: Asset-based restyle

Diff:
- New: `AssetLoader.cs`, `ThemeProvider.cs`, all PNGs in `Assets/icons/`.
- Modified: `Main.cs` (init hook), `UIHelpers.cs`, `TacticsPanel.cs`,
  `PresetPanel.cs`, `RuleEditorWidget.cs`, `WrathTactics.csproj`.

Smoke test on Steam Deck after deploy:
1. Mod loads with no `[ERROR]` in `Player.log` or session log.
2. Ctrl+T opens panel; new background + title bar render.
3. Close-button hover swaps sprite.
4. Tab switch between companions; active tab visually highlighted.
5. Add Rule, edit a condition, save preset; reload preserves.

Rollback path on missing asset: `ThemeProvider` properties cache `null`,
`ApplyButton`/`ApplyPanel` no-op, widget falls back to Unity-default look.
**Never crash on a missing sprite.**

### v1.11.0 — Pillar B: ModMenu integration

Diff:
- New: `Compatibility/ModMenuCompat.cs`.
- Modified: `Main.cs` (additional init hook), `Models/TacticsConfig.cs`
  (four new fields on the config model), `Engine/TacticsEvaluator.cs`
  (`MasterEnabled` gate + `InCombatTickIntervalSeconds` lookup),
  `UI/UIHelpers.cs` (`FontScaleOverride` lookup), `WrathTactics.csproj`
  (optional ModMenu.dll reference, `Condition="Exists(...)"`).

Smoke test:
1. Without ModMenu installed: mod loads with no exception.
2. Drop ModMenu DLL alongside; restart → "Wrath Tactics" tab appears in
   game options menu, all 5 values pre-filled from JSON.
3. Change each slider/toggle, click Apply → JSON contains new values.
4. Master-toggle off → no tactics-engine ticks logged for 30s.

### Pause point

After v1.10.0 ships to Nexus, collect 1–2 weeks of feedback (Discord,
Nexus comments) for asset-loading issues across GPU/driver configurations.
Address any v1.10.x patches before starting v1.11.0. Pillar B can be
prepared on a feature branch in parallel.

## Error Handling

### Asset loading failures

- `AssetLoader.LoadInternal` catches `FileNotFoundException` / `IOException`,
  logs once via `Log.UI.Warn($"Sprite '{name}' missing — falling back to Unity default.")`,
  returns `null`.
- `ThemeProvider` properties propagate `null` through.
- `ApplyButton` / `ApplyPanel` no-op when given `null` — widget keeps its
  Unity-default visual.

Result: mod always starts; missing PNGs degrade visuals without functional loss.

### ModMenu reflection failures

`ModMenuCompat.TryRegister` is fully wrapped in try/catch. Concrete failure
modes covered:

- `TypeLoadException` — ModMenu version with renamed types.
- `MethodAccessException` — API breaking change.
- `ArgumentException` — setting-key collision (mitigated by `wrath-tactics-` prefix; near-zero in practice).

On any failure: `Log.UI.Error(ex, "ModMenu integration failed")`, mod
continues without a settings tab.

### Save-format compat

New `TacticsConfig` fields rely on Newtonsoft's silently-default-missing
behaviour (per parent CLAUDE.md "Bundled Newtonsoft.Json" gotcha). No
migration code needed.

Defaults:
- `MasterEnabled = true`
- `InCombatTickIntervalSeconds = 0.5f`
- `VerboseLogging = false`
- `FontScaleOverride = 0f`

## Testing Strategy

No automated tests — Wrath mods have no test pipeline (no test project, no
CI for unit tests). Verification:

- `dotnet build` green.
- Manual Steam Deck smoke per release plan above.
- `/review` or `superpowers:requesting-code-review` before tagging release
  (per parent `wrath-mods/CLAUDE.md` §Release Process).

## Future Iterations (NOT in this spec)

Tracked here so they don't get lost; each gets its own future spec:

- **v1.12.x**: Picker overlays + dropdowns to native look (option C from
  the brainstorming session).
- **v1.13.x**: HUD entry button with native sprite + hover animation.
- **v1.14.x**: Per-category logging toggles in ModMenu, optional config
  reset button, per-save settings flag for `MasterEnabled`.

## Open Questions

None at design time. Asset-name verification against Wrath 1.4's actual
sharedassets bundle happens during implementation (Pillar A, first day),
with `SOURCES.md` documenting any substitutions.
