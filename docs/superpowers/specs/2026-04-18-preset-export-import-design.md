# Preset Export/Import and Default Presets

## Problem

Three gaps in the existing preset system:

1. **No rule-to-preset promotion.** The `+ New Preset` button in `PresetPanel` starts from an empty stub. Users who have tuned a rule on a character cannot convert it to a shared preset without manually recreating it.
2. **No out-of-the-box presets.** New installs land users on an empty preset list, with no starter examples of what a preset looks like or what useful tactics might be.
3. **No in-app preset sharing.** Presets already live as individual JSON files in `{ModPath}/Presets/`, which is technically shareable via file copy, but there is no affordance to export/import through the UI. Power users can find the folder; everyone else cannot.

Cross-save persistence is already solved — presets are stored in `{ModPath}/Presets/` and are independent of `GameId`. This spec does not change that.

## Solution

Four coordinated additions:

1. **`↥ Preset` button on each rule row** — promotes a rule to a new linked preset in one click.
2. **Six hardcoded default presets** — seeded on first load; idempotent and non-intrusive on later loads.
3. **Clipboard export** — single uniform `[rule, ...]` JSON-array format for rules and for the full preset collection.
4. **Paste-based import** — a modal with a multiline text field; user pastes an array; each entry lands as a new preset.

## Scope

**In scope:**
- `TacticsRule.Description` is NOT added. No new model fields.
- UI: promote button, export-all button, per-rule export button, import modal, open-folder button, default-preset seeding.
- Engine: `PresetRegistry.SeedDefaults()` and a helper that materializes a rule as a preset with cascading linkage.
- Wire-format: uniform `List<TacticsRule>` JSON array for all export/import paths.

**Out of scope:**
- Multi-preset bundles with metadata (versioning, provenance).
- Preset versioning or schema upgrades inside the JSON.
- Nexus integration, cloud sync, or any network I/O.
- Partial import UI (e.g., "select which presets to import from the pasted bundle"). Import is all-or-nothing per paste.
- Info-text field on rules/presets; the rule structure is self-explanatory, and UI helper labels live only on export/import buttons.

## Design

### 1. Rule → Preset promotion

Every rule row in the character tab gets a new `↥ Preset` button (small, next to `Copy`/`X`).

**Behavior on click:**

1. Snapshot the current rule body (ConditionGroups, Action, Target, Name, Priority, Enabled, CooldownRounds) via round-trip JSON clone.
2. Create a new `TacticsRule` with a fresh `Guid.NewGuid()` id, Name = `<original name>` (no suffix), and the cloned body.
3. `PresetRegistry.Save(newPreset)`.
4. Mutate the original rule in-place: set `PresetId = newPreset.Id`, clear `ConditionGroups` / `Action` / `Target` (they are now resolved via `Resolve()` through the preset).
5. `ConfigManager.Save()`.
6. Rebuild the character tab so the original row re-renders in its linked-rule state (showing the preset's content via `Resolve`).

**Why A (convert in place) and not B (duplicate):** If the user promotes then edits the preset, the change propagates to the character tab automatically — which is the point of the preset system. `BreakLink` remains available if the user wants to de-couple later.

Guard: the button is hidden when the rule is already linked (`PresetId != null`) — double-promoting is nonsensical.

### 2. Export / Import

**Wire format:**

A JSON array of `TacticsRule` objects. Identical shape whether the export is one rule or the whole preset collection:

```json
[
  {
    "Id": "…",
    "Name": "Emergency Self-Heal",
    "Enabled": true,
    "Priority": 0,
    "CooldownRounds": 1,
    "ConditionGroups": [ … ],
    "Action": { … },
    "Target": { … },
    "PresetId": null
  }
]
```

**Export paths (all write to `GUIUtility.systemCopyBuffer`):**

- `Export All Presets` button at the top of `PresetPanel`: serializes `PresetRegistry.All()` as a JSON array. Log: *"Copied N presets to clipboard."*
- `Export` button on each rule row (character tab) and on each preset row (`PresetPanel` edit mode): serializes `[rule]` — single-element array, same format. Log: *"Copied rule '<name>' to clipboard."*

**Import path:**

Single `Import` button at the top of `PresetPanel`. Click opens a modal overlay:

- Full-width overlay with dim backdrop, centered panel.
- Multiline `TMP_InputField` (~60% of viewport height) for pasting JSON.
- Inline error area below the field (hidden when empty).
- `Import` button and `Cancel` button at the bottom.

**Validation cascade on `Import` click:**

1. If field is empty → inline error "Paste a JSON array first."
2. `JsonConvert.DeserializeObject<List<TacticsRule>>(text)` wrapped in try/catch.
   - `JsonException` → inline error: "Invalid JSON: {message}". Modal stays open.
   - Returns null → inline error: "Expected a JSON array."
3. For each imported rule:
   - Regenerate `Id = Guid.NewGuid().ToString()` — never overwrite local presets silently.
   - Clear `PresetId = null` — imported content is always a standalone preset, not a link.
   - Name-conflict check (case-insensitive): if any existing preset has the same `Name`, append ` (imported)`. If that also collides, ` (imported 2)`, `…3`, etc.
   - `PresetRegistry.Save(preset)`.
4. Close modal on success. Toast/log: *"Imported N presets (M renamed due to name conflicts)."*
5. Rebuild `PresetPanel`.

No partial import: if step 2 fails, nothing is saved. If step 2 succeeds but individual rules are somehow malformed (e.g., null conditions), the `SafeConditionConverter` path (already wired into `PresetManager.LoadAll` on next mod reload) scrubs them on next load. On import itself we save what deserialized.

**Open Presets Folder:**

Button at the bottom of `PresetPanel`: `Application.OpenURL("file://{ModPath}/Presets/")`. On Linux/Deck this opens the default file manager; on Windows it opens Explorer; on platforms where the call is a no-op, the user is unaffected.

### 3. Default presets

`PresetRegistry.SeedDefaults()` runs once as the last step of `Reload()`. For each default:

- Check if a file `{ModPath}/Presets/<fixed-id>.json` exists. Fixed IDs (not random GUIDs) make the check idempotent.
- If missing → `PresetManager.Save(defaultPreset)`.
- If present (including manually edited) → skip.
- User-deleted defaults stay deleted — seeding only fills gaps, never restores.

New defaults added in later mod versions slot in the same way: only the new IDs get written.

**The six defaults:**

| Fixed ID | Name | Condition Groups | Action | Target |
|---|---|---|---|---|
| `default-emergency-self-heal` | Emergency Self-Heal | `Self HpPercent < 30` | `Heal` (HealMode=Any) | Self |
| `default-party-channel-heal` | Party Heal (Channel Positive) | `AllyCount >= 2 with HpPercent < 60` | `CastAbility` AbilityId=`f5fc9a1a2a3c1a946a31b320d1dd31b2` | Self |
| `default-counter-swarms` | Counter Swarms (Splash) | `Enemy CreatureType = Swarm` | `ThrowSplash` (SplashMode=Strongest) | EnemyNearest |
| `default-coup-de-grace` | Coup de Grace on Helpless | G1: `Enemy HasCondition = Sleeping` **OR** G2: `Enemy HasCondition = Paralyzed` | `AttackTarget` | ConditionTarget |
| `default-channel-vs-undead` | Channel Against Undead | `Enemy CreatureType = Undead` | `CastAbility` AbilityId=`279447a6bf2d3544d93a0a39c3b8e91d` | Self |
| `default-smite-evil` | Smite Evil | `Enemy Alignment = Evil` | `CastAbility` AbilityId=`7bb9eb2042e67bf489ccd1374423cdec` | EnemyHighestThreat |

All `Priority = 0`, `Enabled = true`, `CooldownRounds = 1`.

Presets 2/5/6 reference specific class blueprints (Cleric Channel variants and Paladin Smite Evil). On characters without those abilities, `ActionValidator.FindAbilityEx` returns null and the rule silently does not fire — no crash, no log spam beyond the existing validator log. Users can edit the preset in-place if they want to swap the ability for an equivalent.

### 4. File Map

| File | Change |
|---|---|
| `WrathTactics/Engine/PresetRegistry.cs` | Add `SeedDefaults()`, `PromoteRuleToPreset(rule, config)` helper. Call `SeedDefaults()` at the end of `Reload()`. |
| `WrathTactics/Engine/DefaultPresets.cs` | **New.** Static factory returning the six `TacticsRule` instances with their fixed IDs. |
| `WrathTactics/UI/PresetPanel.cs` | Add `Export All`, `Import`, `Open Folder` buttons at the top/bottom. Per-row `Export` button. |
| `WrathTactics/UI/RuleEditorWidget.cs` | Add `↥ Preset` button on each non-linked rule row (visibility gated on `PresetId == null`). Add `Export` button that serializes `[rule]`. |
| `WrathTactics/UI/ImportPresetOverlay.cs` | **New.** Modal with multiline TMP input + validation + error rendering. Mirrors the `BuffPickerOverlay` structure for consistency. |

`PresetManager`, `TacticsConfig`, `TacticsRule` are unchanged.

### 5. Testing

Manual Steam Deck verification (no unit test harness in this repo):

1. **Promote path.** Build a 2-group rule on a Cleric, click `↥ Preset`. Preset panel shows the new preset with matching content. Character tab shows the original rule as linked (renders preset data via `Resolve`). Edit the preset name — character tab label updates.
2. **Export rule.** Click `Export` on a rule. Paste clipboard into a text editor — must be a JSON array with one element, valid format.
3. **Export all presets.** Click `Export All Presets`. Paste clipboard — must be a JSON array with all presets, valid format.
4. **Import happy path.** Paste an exported array into the Import modal → `Import` → presets show up in the panel. Re-click Import and paste the same array again → entries appear with ` (imported)` suffix, no overwrite.
5. **Import invalid JSON.** Paste `not json` → inline error "Invalid JSON: …". Modal stays open. Paste `{}` (object, not array) → "Expected a JSON array."
6. **Import empty.** Click Import without typing → inline error "Paste a JSON array first."
7. **Default seeding fresh install.** Delete `{ModPath}/Presets/` entirely, reload mod → six default presets appear.
8. **Default deletion persists.** Delete one default preset, reload mod → it stays gone.
9. **Default manual edit persists.** Rename `Emergency Self-Heal` → `My Heal`, reload mod → `My Heal` preserved.
10. **Non-cleric + Channel default.** Equip a pure Fighter, link Default 5 (`Channel Against Undead`) via `+ From Preset`, spawn an undead near party → rule is sibling in the panel but does not fire; validator log shows no-match.
11. **Open folder.** Click Open Presets Folder → file manager opens at `{ModPath}/Presets/`.

## Open questions

None identified. Ready for implementation plan.
