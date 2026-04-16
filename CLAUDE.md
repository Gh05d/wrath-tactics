# Wrath Tactics

## Overview

Dragon Age Origins-style companion tactics for Pathfinder: Wrath of the Righteous. UMM mod that lets players define priorisierte Regeln pro Companion (und global) die im Echtzeit-Kampf automatisch ausgewertet und als Aktionen ausgefuehrt werden.

## Build

~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/

- `dotnet` is not on PATH — always use `~/.dotnet/dotnet`
- `-p:SolutionDir` is required on Linux — without it, GamePath.props import fails silently

## Deploy

./deploy.sh

Builds and deploys DLL + Info.json to Steam Deck via SCP. Requires `deck-direct` SSH alias.

## UI

- **Keybind:** `Ctrl+T` toggles the Tactics panel, `ESC` closes it when open
- **HUD button:** Small "Tactics" button at bottom-left (10px from left, 80px from bottom), created lazily once `Game.Instance.UI.Canvas` is available

## Gotchas

- `GameInstall/` is a symlink to `../wrath-epic-buffing/GameInstall` — do not commit
- `GamePath.props` is machine-specific — gitignored
- `findstr` warnings from build are normal on Linux — ignore
- No per-round EventBus events in RTWP mode — use Game.Instance.Player.GameTime in Update()
- `UnitUseAbility.CreateCastCommand` rejects synthetic AbilityData — only works for real spellbook spells
- Newtonsoft.Json is old (game-bundled) — no generic JsonConverter<T>, use non-generic base class
- .NET 4.8.1 missing APIs: no Dictionary.GetValueOrDefault(), no Index/Range syntax
- **Unity Rebuild pattern**: `Destroy()` on VLG/CSF is deferred — use `DestroyImmediate()` for layout components in Rebuild() methods to avoid duplicate layout calculators for one frame
- **Nested ScrollRects**: Inner ScrollRect steals scroll events from outer. Disable inner `ScrollRect.enabled` unless content actually overflows; re-enable conditionally in `UpdateHeight()`

## Game API Gotchas

- **Blueprint enumeration**: `ResourcesLibrary.s_BlueprintsBundle` does NOT exist in this game version (binary pack, not AssetBundle). Use `ResourcesLibrary.BlueprintsCache.ForEachLoaded((guid, bp) => ...)` to enumerate loaded blueprints. Only returns what's already in memory (lazy-loaded), not all game blueprints.
- **Item consumption**: `IsSpendCharges=True` is per-instance for stacked Utility items (Alchemist's Fire etc.) where `Charges=1`. Decrementing underflows. Only `UsableItemType.Wand` should use `Charges--`; Potion/Scroll/Other use `Game.Instance.Player.Inventory.Remove(item, 1)`.
- **Synthetic AbilityData fallback**: Inventory items have synthetic AbilityData → `CreateCastCommand` silently drops them. Use `Rulebook.Trigger<RuleCastSpell>` — fires effect FX at target but no throw/drink animation. For animation, item must be in a quickslot (then it's registered in `owner.Abilities.RawFacts` with `SourceItem != null`).
- **Enemy enumeration**: `Game.Instance.State.Units` returns ALL units in scene (80+). Filter on `IsInCombat` to get only actively engaging enemies, else companions chase non-combat targets.
- **Enemy filter consistency**: Both `ConditionEvaluator.GetVisibleEnemies` AND `TargetResolver.GetVisibleEnemies` must use the same filter (`IsInCombat`). Mismatch causes companions to target non-combat enemies.
- **CreatureType detection**: Many vanilla units (e.g. all swarms) have `Blueprint.Type = null`. Match via the unit's feature list (`SwarmDiminutiveFeature`, `SwarmTinyFeature`) instead of `Blueprint.Type.name`.
- **AbilityData ctors**: `(BlueprintAbility, UnitDescriptor)`, `(Ability)`, `(BlueprintAbility, Spellbook, int level)`. No 3-param `(blueprint, descriptor, ItemEntity)` exists — use 2-param + `OverrideCasterLevel`/`OverrideSpellLevel`.
- **ActivatableAbility API**: Has `TryStart()` but NO `TryStop()`. Deactivate via `IsOn = false` only.

## Build & Logs

- **Zip output requires Release config**: `CreateZip` target is `Condition="'$(Configuration)' == 'Release'"`. Use `~/.dotnet/dotnet build ... -c Release` to produce `bin/WrathTactics-<version>.zip` for GitHub release upload.
- **Mod session logs**: `<game>/Mods/WrathTactics/Logs/wrath-tactics-YYYY-MM-DD-HHMMSS.log` (separate from `Player.log`). Latest: `ssh deck-direct "ls -t '<game>/Mods/WrathTactics/Logs/' | head -1"`.

## Code Style

- K&R brace style (opening brace on same line)
- 4-space indentation
- `var` when type is apparent
