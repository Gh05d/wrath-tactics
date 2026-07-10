# Persistence: ConfigManager, PresetManager, Seeding, JSON

Operative rules for `Persistence/` and every code path that saves rules or presets. IL evidence and incident reports: [`../docs/wrath-api-deep-dive.md`](../docs/wrath-api-deep-dive.md).

## Save Routing (the #1 trap)

- **Widgets in `RuleEditorWidget` MUST invoke `onChanged?.Invoke()`**, never `ConfigManager.Save()` directly: parent routes to `PresetRegistry.Save` (preset mode) or `ConfigManager.Save` (character mode). Direct save always writes character-rules — preset edits vanish on reload.
- **Preset-edit mode in `RuleEditorWidget`**: `unitId == null` ⇒ editing a preset. Field-edit handlers must route through `PersistEdit()` — direct `ConfigManager.Save()` writes character-rules JSON and silently discards preset edits. **Never have `PersistEdit` self-recurse** — `StackOverflowException`, silent UI freeze.
- **`StackOverflowException` is uncatchable in .NET** — Unity main thread dies silently, no log, panel stays rendered but stops processing input. Diagnose via code search for `Foo() { ... Foo(); ... }`, not via logs.
- **File-save failures must be surfaced**: `PresetManager.Save` / `ConfigManager.Save` catch all exceptions. Methods return `bool`, UI surfaces via status line that persists across `Rebuild` (see `PresetPanel.SetStatus` / `lastIOStatus`).

## Presets & Seeding

- **Linked rules carry empty body by design**: `PresetId`-only rules with empty `ConditionGroups`/`Action`/`Target` are valid — `PresetRegistry.Resolve` substitutes the body at runtime. Cleanup passes MUST exempt `!string.IsNullOrEmpty(r.PresetId)`. ([deep-dive](../docs/wrath-api-deep-dive.md#linked-rules-empty-body))
- **Idempotent default-seeding**: `{ModPath}/Presets/.seeded-defaults` tracks ever-written defaults (one ID per line). Deletions stay deleted, manual edits stay edited, new version-bump defaults slot in once. Re-seeding from in-memory dict alone re-seeds user-deleted defaults every reload.
- **Default-preset factory body changes don't propagate**: `.seeded-defaults` is per-ID, not per-content-hash. Editing `DefaultPresets.Build()` only affects new installs — release notes must tell existing users to edit in-game OR delete the preset JSON + its line from `.seeded-defaults`.

## JSON Format

- **Preset JSON uses numeric enum indices** (Newtonsoft default — no `StringEnumConverter`). Hand-patching needs cross-check against `Models/Enums.cs`; removals shift later indices. Safer: edit via Presets tab in-game. Consequence: **enum members are APPEND-ONLY** — never reorder or delete.
- Config lives per-save at `{ModPath}/UserSettings/tactics-{GameId}.json` (`ConfigManager`); `SafeConditionConverter` drops unknown enum indices on load.
