# Wrath Tactics

## Overview

Dragon Age Origins-style companion tactics for Pathfinder: Wrath of the Righteous. UMM mod that lets players define prioritized rules per companion (and globally) that are evaluated in real-time combat and executed as actions.

## Build

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

**Release build** (produces distribution zip): add `-c Release` — the `CreateZip` target only runs in Release config; output `bin/WrathTactics-<version>.zip`. NU1900 warnings im Build = NuGet-Vulnerability-Index via Proxy unerreichbar — harmlos, kein Artefakt-Problem.

**Version bump requires TWO files**: `WrathTactics/Info.json` (UMM reads this) and `WrathTactics/WrathTactics.csproj` `<Version>`. Bumping only one ships a zip with the stale version in its name.

## Tests

Pure-logic xUnit suite in `WrathTactics.Tests/` (net481; mono hosts the runner on Linux — `sudo apt install mono-complete` one-time). Run before pushing changes to `ConditionEvaluator`, `BuffBlueprintProvider`, `CommonBuffRegistry`, or `Models/Enums.cs` (`RangeBrackets`).

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```

- **Flaky mono runner — loop until green before believing failures**: the first run after a build often crashes the mono host (mass-failures with run-to-run varying counts = flake signature, not regression; can flake several times in a row). `for i in 1 2 3; do ~/.dotnet/dotnet test --no-build WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/; done` — trust the first all-green run; only trust failures that reproduce.
- Game DLLs are copied to test output by an `AfterTargets="Build"` target (without it: `TypeLoadException`). `InternalsVisibleTo` in `WrathTactics/Properties/AssemblyInfo.cs` — promote private statics to `internal static` to test them. No CI by design (game DLLs unreachable from GitHub runners).

## Deploy

```bash
./deploy.sh
```

Builds and deploys DLL + Info.json to Steam Deck via SCP. Requires `deck-direct` SSH alias. **Dev-only** — Debug build for smoke-testing; release builds come from `/release`'s Release-config build.

## Architecture

```
WrathTactics/
  Main.cs              # UMM entry point, Harmony init, Update() tick loop
  Engine/              # Combat AI logic
    TacticsEvaluator   # Main tick loop — evaluates rules per companion each interval
    ConditionEvaluator # Evaluates rule conditions (HP%, buffs, saves, creature type)
    TargetResolver     # Resolves target selection (lowest HP, nearest, creature type)
    CommandExecutor    # Executes actions (cast spell, use item, toggle, attack)
    ActionValidator    # Pre-checks action validity (range, resources, cooldown)
    ThreatCalculator   # Computes per-enemy threat scores
    PlayerCommandGuard # Reference-tracks own commands; gates eval on foreign casts
    ActiveRuleTracker  # DAO priority gate (per-unit)
    TargetingRelations # IsTargeting/IsTargetedBy primitives
    ResolvedTarget     # Unit XOR Point wrapper returned by TargetResolver
    UnitExtensions     # GetHD / GetEffectiveHD / MatchesClassValue helpers
    ClassProvider      # SSoT for HasClass dropdown + matching
    CommonBuffRegistry # Shared buff blueprint lookup table
    DefaultPresets     # Factory for built-in presets (seeded once via .seeded-defaults)
    BuffBlueprintProvider # Buff blueprint data for condition checks
    PresetRegistry     # Built-in rule presets (heal, buff, attack patterns)
    PackRegistry       # Rule packs: cache, apply/sync planning, preset-delete cascade
    SplashItemRegistry # Tracks throwable splash weapons (Alchemist's Fire, etc.)
    SplashItemResolver # Resolves which splash item to use based on ThrowSplashMode
    AllyProvider       # Party-and-pet list for ally pickers; resolves pinned ally
    MetamagicRodResolver # First suitable metamagic rod on a unit (null → normal cast)
    UnitClusterMetrics # Positional cluster metrics (FindMostClustered for AoE targeting)
    RuleListSource     # Enum: Global vs. Character rule-list origin (priority gate)
    BuffIndexCache     # Persisted buff metadata index, game-version+locale stamped
    BuffPackScanner    # Full blueprint enumeration → buff metadata (main-thread, persisted)
    AssetLoader        # Loads PNGs as 9-slice Sprites for UI
  Models/              # TacticsRule, TacticsConfig, Enums
  Persistence/         # ConfigManager (per-save JSON), PresetManager, PackManager,
                       # SafeConditionConverter
  UI/                  # TacticsPanel, RuleEditorWidget, ConditionRowWidget, PresetPanel,
                       # PackPanel, PackPalette, SaveAsPackOverlay, BuffPickerOverlay,
                       # SpellPickerOverlay, SpellDropdownProvider, UIHelpers
  Compatibility/       # BubbleBuffsCompat (Buff It 2 The Limit integration)
  Localization/        # Strings + EnumLabels + 5 locale JSONs (en/de/fr/ru/zh)
  Logging/             # Category-based logging (Engine, Game, Persistence, UI)
```

### Core Data Flow

```
Main.OnUpdate() → TacticsEvaluator.Tick(gameTime)
  → for each party member with enabled rules:
    → evaluate rules by priority (ConditionEvaluator)
    → first matching rule → resolve target (TargetResolver)
    → validate action (ActionValidator)
    → execute (CommandExecutor: CastSpell/UseItem/Toggle/Attack)
```

## UI

- **Keybind:** `Ctrl+T` toggles the Tactics panel, `ESC` closes it when open
- **HUD button:** Small "Tactics" button at bottom-left (10px from left, 80px from bottom), created lazily once `Game.Instance.UI.Canvas` is available

## Topic Index

Detailed gotchas live in `claude-context/` — **read the matching file BEFORE editing an area**:

| Touching... | Read first |
|---|---|
| `ConditionEvaluator*` (buckets, latches, encodings), detection APIs (IsDead, pets, HasBuff, …) | `claude-context/gotchas-conditions.md` |
| `TacticsEvaluator` / `ActionValidator` / `CommandExecutor`, trackers/guards, activatables, blueprint enumeration | `claude-context/gotchas-engine.md` |
| Spellbook / AbilityData / variants / items / heal (`ActionValidator.Find`, `SpellDropdownProvider`) | `claude-context/gotchas-casting.md` |
| UI widgets, Unity layout, pickers | `claude-context/gotchas-ui.md` |
| `ConfigManager` / `PresetManager`, seeding, preset JSON | `claude-context/gotchas-persistence.md` |
| Adding a new ConditionSubject / Property / UnitCondition / ActionType | `claude-context/checklists.md` |
| Bug reports, log analysis, "rule didn't fire" | `claude-context/triage.md` |
| Locale files, new strings | `claude-context/i18n.md` |

IL evidence, version history, and incident reports: [`docs/wrath-api-deep-dive.md`](docs/wrath-api-deep-dive.md).

**Maintenance rule:** new gotcha → matching topic file. This file only gets a one-liner if violating the rule causes silent corruption (§Top Gotchas). Update the table only if the routing itself changes.

## Top Gotchas (always apply)

- `GameInstall/` is a symlink to `../wrath-epic-buffing/GameInstall` — do not commit. `GamePath.props` is machine-specific — gitignored.
- **Never `owner.Commands.Run` directly in `CommandExecutor`** — route through `RunVerified` (engine can silently discard commands; details `gotchas-engine.md`).
- **Use `Player.PartyAndPets`, never `Player.Party`** (excludes pets; one documented exception — `gotchas-conditions.md`). Regression check: `grep 'Player.Party'` before merge.
- **`IsFinallyDead`, not `IsDead`** — and the two `IsDead` sites in `ConditionEvaluator` must stay in sync (`gotchas-conditions.md`).
- **Widgets MUST invoke `onChanged?.Invoke()`, never `ConfigManager.Save()` directly** — direct save silently discards preset edits (`gotchas-persistence.md`).
- **Enums are APPEND-ONLY** — preset/config JSON persists numeric indices (`gotchas-persistence.md`).
- **`PresetId`-only rules have empty bodies by design** — cleanup passes must exempt them (`gotchas-persistence.md`).
- **Packs live in their own directory (`Packs/`), never `Presets/`** — `PresetManager.LoadAll` globs `Presets/*.json` and would silently parse a pack file as a malformed rule (`gotchas-persistence.md`).
- **Blueprint-Matching ist exact-only (GUID oder voller Name), nie `Contains`** — Substring matcht versteckte Item-/Aura-Facts (`WrathOfTheUndeadCountBuff` machte Golems zu Untoten); Bug-Klasse traf HasBuff (pre-1.17.4) UND CreatureType (pre-1.23.3). Details `gotchas-conditions.md`.
- **Rule priority = array position** — no `Priority` field; log "Rule N" = array index.
- No per-round EventBus events in RTWP — use `Game.Instance.Player.GameTime` in `Update()`.
- New i18n keys need en_GB at minimum; locale JSONs are EmbeddedResources → rebuild + redeploy (`i18n.md`).
- **CodeGraph stale lock**: `database is locked` ODER Agenten melden „not initialized" trotz vorhandener `.codegraph/` ⇒ `rm -rf .codegraph/codegraph.db.lock/`.

## Release Process

Follow parent `wrath-mods/CLAUDE.md` §Release Process. Remote is `origin`. The `/release` slash-command (`.claude/commands/release.md`) runs the full flow: bump → build → user-confirm gate → push → tag → GitHub Release → Nexus upload (auto via `.github/workflows/nexus-upload.yml`).

Nexus mod-page: https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005 (ID 1005, `file_id` = `7334711`, repo var `NEXUSMODS_FILE_ID` — see parent `docs/nexus.md`).

**Nexus-upload action fails with Cloudflare 504**: transient Nexus-side timeout, not a workflow problem. Fix: `gh run rerun <run-id> --repo Gh05d/wrath-tactics --failed`; await outcome via `gh run watch <run-id> --repo Gh05d/wrath-tactics --exit-status` (don't poll `gh run list`).

**Deck offline blockiert einen Release nicht** (Präzedenz 1.21.0–1.22.1), aber: fehlenden Smoke-Test im Nexus-Reply offenlegen, im Auto-Memory vermerken, In-Game-Test nachholen sobald das Deck online ist.

## Logs

- **Mod session logs**: `<game>/Mods/WrathTactics/Logs/wrath-tactics-*.log` (separate from `Player.log`). Latest: `ssh deck-direct "ls -t '<game>/Mods/WrathTactics/Logs/' | head -1"`.
- Triage recipes ("rule didn't fire", preemption, deploy verification): `claude-context/triage.md`.

## Code Style

- Shared style (K&R braces, 4-space indent, `var` when apparent): parent `wrath-mods/CLAUDE.md` §Code Style.
- **Partial-class file split for fat engine files**: `ActionValidator` is `partial` across `ActionValidator.cs` (dispatcher) + `.Cast/.UseItem/.Toggle/.Heal/.SwitchWeaponSet/.Find.cs` — one Action-type per file. New Action-type ⇒ new `ActionValidator.<Type>.cs`. Don't merge back — it grew to 902 LOC once.
- **`catch (Exception ex)` is reserved for three patterns**: per-tick/per-frame guards, user-surface persistence, static/sentinel blueprint init. Everything else narrows. ([deep-dive](docs/wrath-api-deep-dive.md#catch-discipline))
