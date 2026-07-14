# Per-Character Tactics Quick-Toggle on Party Portraits — Design

**Date:** 2026-07-14
**Origin:** Nexus feature request by grieva (13 Jul 2026): a per-character on/off toggle near the portraits to quickly suspend all rules for one character ("if I really wanted Nenio to cast Magic Missile … the relevant rule just takes over after 2-3 seconds").
**Target version:** 1.23.0 (minor).

## Summary

A small always-visible toggle badge on each party portrait cell that flips the
existing per-character master switch `TacticsConfig.TacticsEnabled[unitId]`.
Pure UI feature — the engine side already exists:

- `TacticsConfig.TacticsEnabled` (`Dictionary<string,bool>`, persisted per save)
  with `IsEnabled(unitId)`.
- `TacticsEvaluator.cs:72` already skips disabled units each tick.
- `TacticsPanel.cs:556` is the existing toggle site whose persistence path the
  badge reuses.

## Decisions (approved)

- **Placement:** badge anchored to a corner (top-left initially; may be tuned
  during the deck smoke test) of each portrait cell — approach "A", chosen over
  a detached toggle strip and over Ctrl+click-on-portrait.
- **OFF semantics:** only future evaluations stop. An in-flight mod-issued
  command finishes (the player's manual order replaces it anyway). No engine
  changes.
- **Toggleable feature:** new `bool ShowPortraitToggles = true` on
  `TacticsConfig` + checkbox in the panel. Missing field in legacy saves
  deserializes to `true` (Newtonsoft ignores unknown/missing fields — no
  migration).
- **Out of scope (v1):** no badge for pets (no own portrait cell; panel still
  covers them), no tooltip, no keybind variant.

## Game-UI facts (IL evidence, Assembly-CSharp.dll)

- Per-character cell: `Kingmaker.UI.MVVM._PCView.Party.PartyCharacterPCView`,
  public `RectTransform` property — the parenting target. Derives from shared
  base `PartyCharacterView<TBuffView>` which owns `m_PortraitView`
  (`UnitPortraitPartView`), `m_LevelUpButton` / `m_MythicButton` (the game's
  own per-portrait overlay precedent).
- Bar: `PartyPCView` with `m_Content` (container), `m_Characters`
  (`List<PartyCharacterPCView>`, pooled — cells are never destroyed, only
  Bind/Unbind-cycled), `m_Next`/`m_Prev` paging buttons.
- Rebind lifecycle: `PartyPCView.UpdateCharacterBindings()` runs at bind time
  and subscribes to `PartyVM.StartIndex` (paging) and `PartyVM.GroupChanged`
  (composition). Cells get re-bound to different VMs on paging ⇒ **badges must
  never cache a unit**; new cells are instantiated when the party grows ⇒
  overlay attachment must be idempotent/re-scanning.
- No usable name-path string literals in IL (prefab-wired) ⇒ discovery is
  component-type based, not `transform.Find`.
- Console/gamepad mode has a parallel tree (`PartyConsoleView` /
  `PartyCharacterConsoleView`) bound to the same `PartyVM`, sharing the
  `PartyCharacterView` base ⇒ handle both concrete cell types via a shared
  helper that works on the base type.

## Component: `UI/PortraitToggleOverlay.cs` (new, static, ~150 LOC)

**Sync loop.** `PortraitToggleOverlay.Sync()` called from `Main.OnUpdate()`
alongside the existing `TacticsPanel.Update()` call, following the lazy
HUD-button pattern in `TacticsPanel.Update()` (guard on `Game.Instance?.UI?.Canvas`, retry timer,
Unity destroyed-equality checks, no recreation on `!activeInHierarchy`):

1. Cached `PartyPCView` (and `PartyConsoleView` if present) reference; when
   Unity-null, re-find via `FindObjectOfType` on a throttle timer.
2. Iterate the bar's `m_Characters` (publicized field access). For each cell:
   ensure a badge child (`WT_Toggle`) exists under `cell.RectTransform`;
   create it if missing.
3. Per sync pass, each badge re-reads its cell's **current**
   `ViewModel → UnitEntityData → UniqueId`:
   - ViewModel null (surplus pooled cell) → hide badge.
   - Else badge visible; render state from `config.IsEnabled(unitId)`.
4. `ShowPortraitToggles == false` → hide all badges (cheap early-out).

**Badge widget.** 9-slice background image (existing `AssetLoader` pattern) +
TMP label "T", ~22 px × `UIHelpers.FontScale` (fixed pixel sizes must scale
with the game font slider — see `gotchas-ui.md`). Green = enabled; grey +
strikethrough = disabled.
State is always derived from config at sync time, so the panel checkbox and
badge can never drift (single source of truth), regardless of where the flag
is flipped.

**Click.** Resolve the cell's unit at click time (not creation time), then
exactly the `TacticsPanel.cs:556` path: flip `TacticsEnabled[unitId]`, persist
the same way the panel toggle does.

**Error handling.** Entire `Sync()` body in the per-frame catch guard (one of
the three sanctioned `catch (Exception)` patterns). Null-guards throughout;
rely on Unity's destroyed-object equality for stale references.

## Testing & release

- No meaningful pure-logic surface → no new xUnit tests.
- **Deck smoke test is mandatory before release** (UI feature): badge renders
  on all portraits, click toggles + persists, state matches panel checkbox,
  paging with >6 party members re-syncs correctly, behavior in console/gamepad
  UI mode, `ShowPortraitToggles` off hides everything.
- Deck currently offline: build + deploy when it is back; release only after
  the smoke test passes.

## Risks / open points

- Exact badge corner and size may need tuning against the real portrait art
  (level-up button occupies part of the cell) — adjust during smoke test.
- Console-mode tree could not be runtime-verified offline (IL strongly implies
  an input-mode swap of parallel views); the shared-base helper covers both,
  but the deck test must confirm.
