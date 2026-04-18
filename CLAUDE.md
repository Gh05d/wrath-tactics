# Wrath Tactics

## Overview

Dragon Age Origins-style companion tactics for Pathfinder: Wrath of the Righteous. UMM mod that lets players define prioritized rules per companion (and globally) that are evaluated in real-time combat and executed as actions.

## Build

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

**Release build** (produces zip for distribution):
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/
```

`CreateZip` target only runs in Release config — output: `bin/WrathTactics-<version>.zip`.

**Version bump** requires TWO files: `WrathTactics/Info.json` (UMM reads this) and `WrathTactics/WrathTactics.csproj` `<Version>` (drives the release zip filename). Bumping only one ships a zip with the stale version in its name.

## Deploy

```bash
./deploy.sh
```

Builds and deploys DLL + Info.json to Steam Deck via SCP. Requires `deck-direct` SSH alias.

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
    BuffBlueprintProvider # Provides buff blueprint data for condition checks
    PresetRegistry     # Built-in rule presets (heal, buff, attack patterns)
    SplashItemRegistry # Tracks throwable splash weapons (Alchemist's Fire, etc.)
    SplashItemResolver # Resolves which splash item to use based on ThrowSplashMode
  Models/              # Data structures
    TacticsRule        # Single rule: conditions → action → target
    TacticsConfig      # Per-save config (rules per unit + global rules)
    Enums              # ConditionSubject, ConditionProperty, ActionType, TargetType
  Persistence/         # Save/load
    ConfigManager      # Per-save JSON at {ModPath}/UserSettings/tactics-{GameId}.json
    PresetManager      # Manages user-created and built-in presets
  UI/                  # Unity UI
    TacticsPanel       # Main panel (Ctrl+T toggle), HUD button
    RuleEditorWidget   # Rule editing: conditions, action, target dropdowns
    ConditionRowWidget # Single condition row in the rule editor
    PresetPanel        # Preset selection/management sub-panel
    SpellDropdownProvider # Populates spell/ability dropdowns from unit's spellbooks
    UIHelpers          # Shared UI utilities
  Compatibility/       # Cross-mod compat
    BubbleBuffsCompat  # Integration with Buff It 2 The Limit
  Logging/             # Structured logging
    Logger/Log/DebugLog # Category-based logging (Engine, Game, Persistence, UI)
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

## Gotchas

- `GameInstall/` is a symlink to `../wrath-epic-buffing/GameInstall` — do not commit
- `GamePath.props` is machine-specific — gitignored
- No per-round EventBus events in RTWP mode — use `Game.Instance.Player.GameTime` in `Update()`
- `UnitUseAbility.CreateCastCommand` rejects synthetic AbilityData — only works for real spellbook spells
- **Unity Rebuild pattern**: `Destroy()` on VLG/CSF is deferred — use `DestroyImmediate()` for layout components in Rebuild() methods to avoid duplicate layout calculators for one frame
- **Nested ScrollRects**: Inner ScrollRect steals scroll events from outer. Disable inner `ScrollRect.enabled` unless content actually overflows; re-enable conditionally in `UpdateHeight()`
- **Validator strictness is load-bearing**: `CommandExecutor.Execute` returns `true` as soon as the command is queued — the game may silently drop the cast later (no slot, no resource). `TryExecuteRules` then returns `true` and blocks fall-through to backup rules. Any new `ActionType` MUST validate resources/availability in `ActionValidator.CanExecute` up front — downstream silent-fail produces infinite loops.
- **Input fields**: Always use `UIHelpers.CreateTMPInputField` — it auto-attaches `ManualInputCaret` (TMP_Text-based blinking cursor positioned via `xAdvance + parent.width/2`) and sets `onFocusSelectAll = false`. Rolling a fresh `TMP_InputField` will resurrect the invisible-caret and wipe-on-click bugs.

## Game API Gotchas

- **Blueprint enumeration**: `ResourcesLibrary.s_BlueprintsBundle` does NOT exist in this game version (binary pack, not AssetBundle). Use `ResourcesLibrary.BlueprintsCache.ForEachLoaded((guid, bp) => ...)` to enumerate loaded blueprints. Only returns what's already in memory (lazy-loaded), not all game blueprints.
- **Item consumption**: `IsSpendCharges=True` is per-instance for stacked Utility items (Alchemist's Fire etc.) where `Charges=1`. Decrementing underflows. Only `UsableItemType.Wand` should use `Charges--`; Potion/Scroll/Other use `Game.Instance.Player.Inventory.Remove(item, 1)`.
- **Synthetic AbilityData fallback**: Inventory items have synthetic AbilityData → `CreateCastCommand` silently drops them. Use `Rulebook.Trigger<RuleCastSpell>` — fires effect FX at target but no throw/drink animation. For animation, item must be in a quickslot (then it's registered in `owner.Abilities.RawFacts` with `SourceItem != null`).
- **Enemy enumeration**: `Game.Instance.State.Units` returns ALL units in scene (80+). Filter on `IsInCombat` to get only actively engaging enemies, else companions chase non-combat targets.
- **Enemy filter consistency**: Both `ConditionEvaluator.GetVisibleEnemies` AND `TargetResolver.GetVisibleEnemies` must use the same filter (`IsInCombat`). Mismatch causes companions to target non-combat enemies.
- **CreatureType detection**: Many vanilla units (e.g. all swarms) have `Blueprint.Type = null`. Match via the unit's feature list (`SwarmDiminutiveFeature`, `SwarmTinyFeature`) instead of `Blueprint.Type.name`.
- **AbilityData ctors**: `(BlueprintAbility, UnitDescriptor)`, `(Ability)`, `(BlueprintAbility, Spellbook, int level)`, **`(AbilityData parent, BlueprintAbility variant)`** for `AbilityVariants` (Command → Halt/Prone/Approach/Flee, Plague Storm → disease variants, Evil Eye → AC/Attack/Saves — works on both spellbook spells AND class abilities). No 3-param `(blueprint, descriptor, ItemEntity)` exists — use 2-param + `OverrideCasterLevel`/`OverrideSpellLevel`.
- **Spellbook max level**: loop to `book.MaxSpellLevel` (instance prop). Regular books cap at 9, mythic at 10. Never hardcode.
- **ActivatableAbility API**: Has `TryStart()` but NO `TryStop()`. Deactivate via `IsOn = false` only.
- **Spellbook slot counts**: `GetSpellsPerDay(level)` is MAX per-day capacity (never decrements) — wrong for "can I still cast?" checks. Use `GetAvailableForCastSpellCount(ability)` — handles prepared (memorized+Available), spontaneous (`GetSpontaneousSlots`), Arcanist-hybrid, and opposition schools/descriptors.
- **Ability resource cost**: Use `AbilityResourceLogic.CalculateCost(ability)` not `.Amount` — honors `OverrideRequiredResource`, `IsSpendResource`, `ResourceCostIncreasing/DecreasingFacts`, and custom `IAbilityResourceCostCalculator` components. Matches the game's internal `Spend()` path.
- **Alignment API**: `UnitDescriptor.Alignment` is a `Kingmaker.UnitLogic.Alignments.UnitAlignment` object; the actual alignment value is `.ValueRaw` of type `Kingmaker.Enums.Alignment` (9-value enum: LawfulGood..ChaoticEvil, NOT a flag). Don't confuse with `Kingmaker.UnitLogic.Alignments.AlignmentMaskType` which is a flag enum but is NOT what `UnitAlignment` exposes. For component matching (Good/Evil/Lawful/Chaotic), enumerate the 3 member values explicitly.
- **Post-combat evaluation**: `TacticsEvaluator.Tick` early-returns when `!Player.IsInCombat`. To let rules fire on the combat-end transition, `RunPostCombatCleanup()` runs a single evaluation pass with `ConditionEvaluator.IsPostCombatPass = true`, which makes `Combat.IsInCombat == false` conditions match regardless of transient game state. Cooldowns are skipped in this pass and cleared immediately after.
- **Buff blueprint filtering**: `BuffBlueprintProvider.IsCrusadeOnlyBuff` skips `Army*`-prefixed names (crusade/tactical-combat mini-game). Warpriest Blessings (`AirBlessingMinorBuff`, `ArtificeBlessingMinorBuff`, etc.) are REAL roleplay buffs — do NOT blanket-filter by "Blessing".
- **Buff picker search ranking**: `BuffPickerOverlay.RenderFilteredLayout` sorts by (prefix-match first, shorter-name first). Pure alphabetical puts `AirBlessingMajorBuff` ahead of `BlessBuff` when searching "bless" — keep the custom sort.

## Logs

- **Mod session logs**: `<game>/Mods/WrathTactics/Logs/wrath-tactics-YYYY-MM-DD-HHMMSS.log` (separate from `Player.log`). Latest: `ssh deck-direct "ls -t '<game>/Mods/WrathTactics/Logs/' | head -1"`.

## Code Style

- K&R brace style (opening brace on same line)
- 4-space indentation
- `var` when type is apparent
- UI strings are English-only. No mixed-language (no `"Ja"/"Nein"`, no `"nicht"`) — use `Yes`/`No`, `!=`, etc.
- Equality conditions use inline `=`/`!=` operator dropdowns. Extend the operator pattern to new properties (HasBuff, HasCondition, CreatureType, Alignment) rather than adding a perpendicular Negate/NOT button or model flag.
