# Spell/Ability Picker Search — Design

## Motivation

The CastSpell / CastAbility / UseItem / ToggleActivatable pickers use a `PopupSelector` with the full list rendered up-front. On well-developed companions this list grows long (Wizard L0–9 + variants, Kineticist infusions, mythic abilities). Scrolling to find a specific ability is tedious, and the user can't filter.

`BuffPickerOverlay` already solves this pattern for HasBuff conditions — a modal overlay with a search field, live-filtered list, and prefix-first ranking.

## Solution

New `UI/SpellPickerOverlay` that mirrors `BuffPickerOverlay`'s interaction model, but takes a pre-built `List<SpellDropdownProvider.SpellEntry>` directly (no blueprint-cache round trip). The RuleEditorWidget's spell-picker dropdown becomes a button that, when clicked, opens the overlay.

### Interaction

1. RuleEditor shows a button: `[icon] {currentEntryName} v`
2. User clicks → overlay opens centered on the canvas with:
   - Search input at top (auto-focused next frame)
   - Scrollable list of all entries, with icon + name
3. User types → filter runs on every keystroke:
   - Case-insensitive substring match
   - Tier-1: entries whose name starts with the query
   - Tier-2: entries containing the query
   - Within each tier: shorter names first, then alphabetical
4. User clicks a row → overlay closes, AbilityId is updated, button's label/icon refresh
5. `Esc` or click outside popup → close without change

### Differences from `BuffPickerOverlay`

- **No recents, no defaults section.** Spell lists are scoped to one unit × one ActionType; a global recents cache would mix results across contexts in a confusing way.
- **Entries passed in as `List<SpellEntry>`.** `BuffPickerOverlay` pulls `BuffBlueprintProvider.GetBuffs()` itself; spell entries are already computed from a `UnitEntityData` + `ActionType` in `RuleEditorWidget.GetSpellEntries`, so the overlay takes them as a parameter.
- **Rows render the icon.** `BuffPickerOverlay` uses text-only rows; the spell picker already has icons available (`SpellEntry.Icon`) and they help at a glance.
- **No filter cap message.** Spell lists max out at a few hundred entries (vs ~3000 buffs). No `MaxFilterResults` truncation needed; render all matches.

## Files

- **Create:** `WrathTactics/UI/SpellPickerOverlay.cs` — new overlay class, public API `Open(List<SpellEntry> entries, string currentGuid, Action<SpellEntry> onSelected)`.
- **Modify:** `WrathTactics/UI/RuleEditorWidget.cs` — replace both `PopupSelector.CreateWithIcons(...)` spell-picker sites and the `RefreshSpellSelector` rebuilder. Introduce a `SpellPickerButton` helper local to the widget that renders the button and wires up the click handler.

## Implementation Sketch

### `SpellPickerOverlay.Open`

```csharp
public static GameObject Open(List<SpellDropdownProvider.SpellEntry> entries,
    string currentGuid, Action<SpellDropdownProvider.SpellEntry> onSelected)
```

- Overlay layout + ScrollRect + search input: ported from `BuffPickerOverlay.BuildUI`.
- Row renderer: creates a 32px row with Image (icon, 28×28, left-aligned) + TMP label (MidlineLeft, margin 40px/4px for icon space); entire row is a Button that invokes `onSelected(entry)` then destroys the overlay.
- Filter logic: ported from `BuffPickerOverlay.RenderFilteredLayout`, adapted to `SpellEntry.Name`.
- Esc/outside-click close: same as buff overlay.

### `RuleEditorWidget` call-site replacement

Replace:

```csharp
spellSelector = PopupSelector.CreateWithIcons(row, "SpellPick", 0.39f, 1.0f,
    options, icons, initialIndex, idx => { ... });
```

with a call to a new local helper:

```csharp
spellPickerButton = CreateSpellPickerButton(row, 0.39f, 1.0f, rule.Action.Type);
```

Where `CreateSpellPickerButton` builds a background + icon + label + arrow button. Its click handler:

```csharp
SpellPickerOverlay.Open(currentSpellEntries, rule.Action.AbilityId, selected => {
    rule.Action.AbilityId = selected.Guid;
    UpdateSpellPickerButton(spellPickerButton, selected);
    PersistEdit();
});
```

`UpdateSpellPickerButton` refreshes the button's icon and label with the new entry.

`RefreshSpellSelector` becomes much simpler: rebuild `currentSpellEntries`, look up the current AbilityId's entry (or fall back to the first), and call `UpdateSpellPickerButton`.

## Persistence

No change. `rule.Action.AbilityId` remains the stored key. Pre-0.9.x configs load identically.

## Testing

Manual smoke on Steam Deck with a high-level Nenio save:

1. Open tactics panel, add a new rule with `CastSpell`.
2. Click the spell button. Overlay opens with search auto-focused.
3. Type `fire` → list narrows to Fireball, Burning Hands, etc.
4. Click Fireball. Overlay closes, button shows `[fireball icon] [L3] Fireball (Fireball)`.
5. Save, reload, re-open panel → button still shows Fireball.
6. Switch ActionType to `ToggleActivatable` → button refreshes to first activatable ability.
7. Repeat for `CastAbility` with a Kineticist companion → `Fire Blast (FireBlast)` and `Fire Blast (KineticBladeFireBlast)` are distinguishable in the filtered list.
8. Esc closes overlay without committing; outside-click closes overlay without committing.

## Non-Goals

- No inline fallback for small lists. Consistency with buff picker is more valuable than saving one click when the list is short.
- No keyboard navigation (↑/↓/Enter). The buff picker doesn't have this either; can be added later if needed.
- No global recents. Per-unit/per-ActionType context makes a shared recents cache confusing.

## Risks

- **Overlay-in-overlay.** RuleEditor is inside TacticsPanel; opening a modal over it is what BuffPickerOverlay already does for HasBuff conditions — same canvas parent (`Game.Instance.UI.Canvas.transform`). No new layering concerns.
- **Unit changes while overlay open.** If the user closes the tactics panel (which destroys the RuleEditor) while the overlay is up, the captured `rule` reference still points at the in-memory `TacticsConfig` entry — selection still lands safely but the visual refresh target is gone. Mitigation: overlay's callback null-checks the RuleEditor's button via `if (!spellPickerButton) return;` before updating. Matches how `BuffPickerOverlay` already handles this.
