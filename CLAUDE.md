# Wrath Tactics

## Overview

Dragon Age Origins-style companion tactics for Pathfinder: Wrath of the Righteous. UMM mod that lets players define prioritized rules per companion (and globally) that are evaluated in real-time combat and executed as actions.

Shared build/deploy/Nexus/release rules: → parent `wrath-mods/CLAUDE.md` (§Common Build Setup, §Steam Deck Deployment, §Nexus Mods, §Release Process). Incident history behind the rules here: `claude-context/incidents.md`.

## Build

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

**Release build** (produces distribution zip): add `-c Release` — the `CreateZip` target only runs in Release config; output `bin/WrathTactics-<version>.zip`. NU1900 warnings im Build = NuGet-Vulnerability-Index via Proxy unerreichbar — harmlos, kein Artefakt-Problem.

**Build-Ergebnis in Ketten prüfen**: `OUT=$(~/.dotnet/dotnet build … 2>&1); echo "$OUT" | grep -q ' error ' && exit 1` — ein `grep -E ' error |Build succeeded'` ist bei Fehlern trotzdem exit 0 und lässt `&& git commit && ./deploy.sh` weiterlaufen.

**Version files** (generic rule → parent §Release Process): `WrathTactics/Info.json` (UMM reads this), `WrathTactics/WrathTactics.csproj` `<Version>` (zip filename), `Repository.json`. Bumping only one ships a zip with the stale version in its name.

## Tests

Pure-logic xUnit suite in `WrathTactics.Tests/` (net481; mono hosts the runner on Linux — `sudo apt install mono-complete` one-time). Run before pushing changes to `ConditionEvaluator`, `BuffBlueprintProvider`, `CommonBuffRegistry`, or `Models/Enums.cs` (`RangeBrackets`).

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```

- **Flaky mono runner — loop until green before believing failures** (`for i in 1 2 3; do … test --no-build …; done`; mass-failures with varying counts = flake, not regression). Test-csproj facts (game DLLs copied to output by `AfterTargets="Build"`, compile reference to non-publicized `Assembly-CSharp.dll` + `UnityEngine.CoreModule.dll`, `InternalsVisibleTo` → `internal static`, no CI by design) and the Deck smoke-test pack (`docs/testing/deck-smoke/`): `claude-context/testing.md`.
- **UI-Layout ist nur am Deck verifizierbar** — jede Schicht UI-Arbeit endet mit einem Screenshot vom User; ohne den gilt sie als ungetestet (`testing.md`).

## Deploy

```bash
./deploy.sh
```

Rules (timeout 240, never in an `&&` chain before `git commit`, SSH probe first, `strings -el` verification): → parent §Steam Deck Deployment. Tactics-specific: `deploy.sh` also creates `<game>/Mods/WrathTactics/Assets/icons/` and ships `Assets/icons/*.png` (required by `AssetLoader`) next to DLL + Info.json.

## Architecture

```
WrathTactics/
  Main.cs              # UMM entry point, Harmony init, Update() tick loop
  Assets/icons/        # PNG sprites for the UI (deployed by deploy.sh, loaded by AssetLoader)
  Engine/              # Combat AI logic
    TacticsEvaluator   # Main tick loop — evaluates rules per companion each interval
    ConditionEvaluator # Evaluates rule conditions (HP%, buffs, saves, creature type);
                       # partials .Buckets / .Helpers / .PickMetrics / .UnitProperty
    TargetResolver     # Resolves target selection (lowest HP, nearest, creature type)
    CommandExecutor    # Executes actions (cast spell, use item, toggle, attack)
    ActionValidator    # Pre-checks action validity (range, resources, cooldown); partials per Action type (§Code Style)
    ActionSlots        # Paired-slot conflict model (Move ↔ Standard), CheckConflict
    CommandDiagnostics # Logs interrupt callers + TickCommand interrupt inputs (§Logs)
    IssuedCommandOutcome # Result of an issued command (accepted/discarded/…)
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
  Models/              # TacticsRule, TacticsConfig, TacticsPack, Enums, TargetDefaults (action-dependent index-0 defaults)
  Persistence/         # ConfigManager (per-save JSON), PresetManager, PackManager,
                       # ModSettingsManager, SafeConditionConverter
  UI/                  # TacticsPanel, RuleEditorWidget (+ partials .Action/.Cooldown/.Header/.Target),
                       # ConditionRowWidget, PresetPanel, PackPanel, PackPalette, SaveAsPackOverlay,
                       # BuffPickerOverlay, SpellPickerOverlay, SpellDropdownProvider,
                       # PortraitToggleBadge/PortraitToggleOverlay, UIHelpers,
                       # Theme (alle Farben/Maße) + ThemeProvider + Widgets (alle Controls) — SSoT, s. gotchas-ui.md
  Compatibility/       # BubbleBuffsCompat (Buff It 2 The Limit integration)
  Localization/        # Strings + EnumLabels + 5 locale JSONs (en/de/fr/ru/zh)
  Logging/             # Category-based logging (Engine, Game, Persistence, UI)
tools/extract_sprites.py  # (repo root) Owlcat-Sprites + 9-Slice-Borders aus sharedassets0.assets (UnityPy-venv)
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
- **HUD button:** helmet-sprite button parented into the game's HUD `GridLayoutGroup` (next to BubbleBuffs' buttons when installed); standalone fallback bottom-left `anchoredPosition (20,120)`, 48×48; created lazily once `Game.Instance.UI.Canvas` is available and re-created only if destroyed (BubbleBuffs rebuilds the container). Source: `UI/TacticsPanel.cs` ~1018-1107.

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
| Test suite, mono runner, Deck smoke-test pack | `claude-context/testing.md` |
| Why a Top Gotcha exists (dated regressions) | `claude-context/incidents.md` |

IL evidence, version history, and incident reports: `docs/wrath-api-deep-dive.md`.

**Maintenance rule:** new gotcha → matching topic file. This file only gets a one-liner if violating the rule causes silent corruption (§Top Gotchas). Update the table only if the routing itself changes.

## Top Gotchas (always apply)

- **Never `owner.Commands.Run` directly in `CommandExecutor`** — route through `RunVerified` (engine can silently discard commands; details `gotchas-engine.md`).
- **Use `Player.PartyAndPets`, never `Player.Party`** (excludes pets; one documented exception — `gotchas-conditions.md`). Regression check: `grep 'Player.Party'` before merge.
- **`IsFinallyDead`, not `IsDead`** — and the two `IsDead` sites in `ConditionEvaluator` must stay in sync (`gotchas-conditions.md`).
- **Widgets MUST invoke `onChanged?.Invoke()`, never `ConfigManager.Save()` directly** — direct save silently discards preset edits (`gotchas-persistence.md`).
- **Enums are APPEND-ONLY** — preset/config JSON persists numeric indices (`gotchas-persistence.md`).
- **`PresetId`-only rules have empty bodies by design** — cleanup passes must exempt them (`gotchas-persistence.md`).
- **Packs live in their own directory (`Packs/`), never `Presets/`** — `PresetManager.LoadAll` globs `Presets/*.json` and would silently parse a pack file as a malformed rule (`gotchas-persistence.md`).
- **Blueprint-Matching ist exact-only (GUID oder voller Name), nie `Contains`** — Substring matcht versteckte Item-/Aura-Facts; traf HasBuff UND CreatureType (`incidents.md`). Details `gotchas-conditions.md`.
- **Move und Standard sind gepaarte Slots**: `UnitCommands.Run` eines Move-Kommandos löscht das Standard-Kommando (pending oder laufend) und umgekehrt; nur Swift/Free sind unabhängig. Eine Move-Regel darf nie über einem eigenen Cast feuern (`ActionSlots.CheckConflict`, `gotchas-engine.md`).
- **KI-Kommando vs. Spielerklick = `UnitCommand.AiAction`**: `AiBrainController.SelectAction` stempelt jedes eigene Kommando, ein Klick nie. Nie über Blueprint (`Brain.AutoUseAbility`) unterscheiden — der Spieler castet denselben Zauber auch explizit (`gotchas-engine.md`).
- **Rule priority = array position** — no `Priority` field; log "Rule N" = array index.
- No per-round EventBus events in RTWP — use `Game.Instance.Player.GameTime` in `Update()`.
- New i18n keys need en_GB at minimum; locale JSONs are EmbeddedResources → rebuild + redeploy (`i18n.md`).
- **„Muss ich jedes Mal einstellen"-Reports = Default-Frage, kein Feature-Gap**: jedes Rule-Feld startet auf Enum-Index 0 (`TargetType.Self`, …). Action-abhängige Defaults gehören in `Models/TargetDefaults` (rein, getestet), nie in den Widget-Handler; nur den Index-0-Wert ersetzen, explizite Wahl nie überschreiben.
- **CodeGraph stale lock**: `database is locked` ODER Agenten melden „not initialized" trotz vorhandener `.codegraph/` ⇒ `rm -rf .codegraph/codegraph.db.lock/`.

## Release Process

→ parent §Release Process / §Nexus Mods. Remote is `origin`. `/release` (`.claude/commands/release.md`): bump → build → user-confirm gate → push → tag → GitHub Release → Nexus upload (auto). Mod-page: https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005.

## Logs

- **Mod session logs**: `<game>/Mods/WrathTactics/Logs/wrath-tactics-*.log` (separate from `Player.log`). Latest: `ssh deck-direct "ls -t '<game>/Mods/WrathTactics/Logs/' | head -1"`.
- Triage recipes ("rule didn't fire", preemption, deploy verification): `claude-context/triage.md`.
- **„Wer hat mein Kommando unterbrochen?"**: `grep 'interrupted (started='` im Mod-Log — `CommandDiagnostics` loggt Aufrufer-Frames und die drei `TickCommand`-Interrupt-Eingaben. `EXECUTED` belegt nur die Ausgabe, `ended: Success` den Effekt (`triage.md`).

## Code Style

- Shared style (K&R braces, 4-space indent, `var` when apparent): → parent §Code Style.
- **`ActionValidator` is `partial`**: `ActionValidator.cs` (dispatcher) + `.Cast/.UseItem/.Toggle/.Heal/.SwitchWeaponSet/.MoveToTarget/.Find.cs` — one Action-type per file; new Action-type ⇒ new `ActionValidator.<Type>.cs`. Don't merge back (`incidents.md`).
- **`catch (Exception ex)` is reserved for three patterns**: per-tick/per-frame guards, user-surface persistence, static/sentinel blueprint init. Everything else narrows. (`docs/wrath-api-deep-dive.md#catch-discipline`)
