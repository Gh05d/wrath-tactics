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

- **Keybind:** `Ctrl+T` toggles the Tactics panel
- **HUD button:** Small "Tactics" button at bottom-left (10px from left, 80px from bottom), created lazily once `Game.Instance.UI.Canvas` is available

## Gotchas

- `GameInstall/` is a symlink to `../wrath-epic-buffing/GameInstall` — do not commit
- `GamePath.props` is machine-specific — gitignored
- `findstr` warnings from build are normal on Linux — ignore
- No per-round EventBus events in RTWP mode — use Game.Instance.Player.GameTime in Update()
- `UnitUseAbility.CreateCastCommand` rejects synthetic AbilityData — only works for real spellbook spells
- Newtonsoft.Json is old (game-bundled) — no generic JsonConverter<T>, use non-generic base class
- .NET 4.8.1 missing APIs: no Dictionary.GetValueOrDefault(), no Index/Range syntax

## Code Style

- K&R brace style (opening brace on same line)
- 4-space indentation
- `var` when type is apparent
