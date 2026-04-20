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
- **Preset-edit mode in RuleEditorWidget**: `unitId == null` means the widget is editing a preset (not a character rule). Field-edit handlers must route through the `PersistEdit()` helper — plain `ConfigManager.Save()` writes `tactics-{GameId}.json` and silently discards preset edits (next reload shows factory content). `PersistEdit` picks `PresetRegistry.Save(rule)` or `ConfigManager.Save()` based on mode. The character-rule branch must call `ConfigManager.Save()` directly — **never recurse into `PersistEdit()` itself**. A self-call produces infinite recursion → `StackOverflowException` → silent UI freeze (see SOE gotcha).
- **Linked rules carry empty body by design**: `AddFromPreset` and `PromoteRuleToPreset` produce rules where only `PresetId` is set; `ConditionGroups`/`Action`/`Target` are left as empty defaults because `PresetRegistry.Resolve(rule)` substitutes the preset's body at runtime. Any cleanup/validation pass that walks `CharacterRules`/`GlobalRules` MUST exempt rules with `!string.IsNullOrEmpty(r.PresetId)`, or it strips legitimate preset assignments on every `ConfigManager.Load`. The first such bug (0.6.0–0.6.2, fixed in 0.6.3) wiped all preset assignments on reload via `RemoveRulesWithNoGroups`. Check `PresetId` before treating "empty body" as "invalid rule".
- **`StackOverflowException` is uncatchable in .NET — Unity main thread dies silently**: No log line, no exception surface, panel stays rendered but stops processing input. Symptom: "UI frozen but visible, Ctrl+T won't close it either". Common trigger: typo in a self-referential helper (a method's else-branch calling itself instead of the intended target). Diagnose via code search for `Foo() { ... Foo(); ... }` patterns, not via logs — the log is empty because the thread that would have written it is dead.
- **Idempotent default-seeding**: `{ModPath}/Presets/.seeded-defaults` (plain text, one ID per line) tracks which defaults have ever been written. Checking only the in-memory dict re-seeds user-deleted defaults on every reload. On first seed, all default IDs go into the sentinel; deletions stay deleted, manual edits stay edited, new version-bump defaults slot in once.
- **File-save failures must be surfaced**: `PresetManager.Save` / `ConfigManager.Save` catch all exceptions and log to the mod log only. A write-protected mod dir produces a phantom-save where the UI looks fine but the file isn't updated — user sees "data reset after restart." Pattern: make persistence methods return `bool`, UI panel surfaces via a status line field that persists across `Rebuild` (see `PresetPanel.SetStatus` / `lastIOStatus`).
- **Default-preset factory body changes don't propagate**: `.seeded-defaults` is per-ID, not per-content-hash. Editing `DefaultPresets.Build()` only affects new installs. Release notes for such changes MUST tell existing users to edit in-game via the Presets tab OR delete the preset JSON + its line from `.seeded-defaults` before reload. For your own deck, SSH-patch the file directly.
- **Preset JSON uses numeric enum indices** (Newtonsoft default — no `StringEnumConverter`). Hand-patching a preset file requires cross-checking against `Models/Enums.cs` — removals shift every later index. In 0.5.0 we dropped `MissingBuff` and `HasDebuff` from `ConditionProperty`, so pre-0.5.0 indices for CreatureType onward are no longer valid. Safer than hand-editing: open the Presets tab in-game and edit via dropdowns.
- **`ResolvedTarget` wrapper** (since 0.8.0): `TargetResolver.Resolve` returns a `ResolvedTarget` struct (Unit XOR Point, both null = `None`). `ActionValidator.CanExecute` and `CommandExecutor.Execute` branch on `IsPoint` — point-path requires `AbilityData.CanTargetPoint == true` and builds `TargetWrapper(Vector3)`; unit-path builds `TargetWrapper(Unit)`. `ResolvedTarget.None` from an unresolvable unit-picker fails validation — no silent self-cast fallback (this was a quirk pre-0.8.0).
- **HasClass condition value encoding** (since 0.9.0): `Condition.Value` for `ConditionProperty.HasClass` is a prefixed string: `group:<spellcaster|arcane|divine|martial>` for groups, `class:<InternalName>` for specific classes (blueprint `name` with `Class` suffix stripped — e.g. `class:Wizard`, `class:Lich`). Never store the localized display name here — `BlueprintCharacterClass.name` is the code identifier and locale-independent. `ClassProvider.GetAll()` is the single source of truth for the dropdown list; `UnitExtensions.MatchesClassValue` is the single matching helper. Group `spellcaster` = `unit.Spellbooks.Any()`; `arcane`/`divine` read `BlueprintCharacterClass.IsArcaneCaster`/`IsDivineCaster` on each `Progression.Classes` entry; `martial` = any class that is neither arcane, divine, nor mythic.
- **Rule-scoped ambient statics in `ConditionEvaluator`**: `CurrentAction` / `CurrentOwner` / `LastMatchedEnemy` / `LastMatchedAlly` are private statics set in `Evaluate(rule, owner)` and cleared in `finally`. Property helpers (`ComputeDCMinusSave`, target-pickers) read them to keep one-arg signatures. Always wrap in try/finally — a leak between rules produces cross-contamination (wrong action's DC used against a different rule's target).
- **Group-AND is same-unit for Enemy/Ally scopes** (since 0.11.0): Multiple `Enemy.*` conditions in one ConditionGroup must all match the *same* enemy. Same for `Ally.*`. Implementation lives in `ConditionEvaluator.EvaluateEnemyBucket` / `EvaluateAllyBucket`. Conditions of different scopes still AND-compose across scopes (Enemy AND Ally AND Self ...). To express "different enemies" semantics, split into separate OR-groups. Prior to 0.11.0 each condition iterated independently, so two Enemy rows could pass on unrelated enemies — this was the latent bug fixed alongside WithinRange.
- **WithinRange value encoding** (since 0.11.0): `Condition.Value` for `ConditionProperty.WithinRange` is the plain `RangeBracket` enum name (`"Melee"`, `"Cone"`, `"Short"`, `"Medium"`, `"Long"`). Fixed meter thresholds live in `RangeBrackets.MaxMeters` (2/5/10/20/40 m); `RangeBrackets.LowerMeters` gives the previous bracket's upper bound (0 for Melee). The UI dropdown shows distance-hint labels (`"Cone (≤5 m)"`) but persists the bare name. Operators follow strict-bracket semantics: `= X` means `lower(X) < d ≤ upper(X)`; `<= X` is cumulative ("within X or closer"); `>= X` is "at X or farther"; `<`/`>` are strict before/past bracket. For cumulative "within 10m" intent use `<= Short`, not `= Short`.
- **Widgets embedded in `RuleEditorWidget` MUST invoke `onChanged?.Invoke()`**, never `ConfigManager.Save()` directly: the parent passes a save-callback that routes to `PresetRegistry.Save(rule)` in preset-edit mode (`unitId == null`) or `ConfigManager.Save()` in character-rule mode. Direct `ConfigManager.Save()` always writes the character-rules file — in preset mode this means edits silently vanish on reload (preset JSON untouched). Rule applies to `ConditionRowWidget` and any future Action/Target/sub-widget. See `PersistEdit` in `RuleEditorWidget` for the routing logic. Fixed for condition rows in 0.11.0; pre-existing bug masked by the PersistEdit work on the editor level itself.
- **New `ConditionSubject` must be classified in `IsEnemyScope` / `IsAllyScope`** (`ConditionEvaluator.cs`): since 0.11.0, `EvaluateGroup` buckets conditions by scope and enforces same-unit AND within each bucket. A new Enemy/Ally-scoped subject that's only added to `EvaluateCondition`'s dispatch switch routes through the legacy per-condition path, silently bypassing the same-unit fix. Also extend `PickMetric` if the new subject is a sort-pick (EnemyLowest* / EnemyHighest* family). The legacy methods (`EvaluateEnemy`, `EvaluateEnemyPick`, etc.) remain in the file but are unreachable from the bucketed path — do not rely on them for new code.

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
- **`TargetWrapper` dual ctors**: `(UnitEntityData unit)` for unit targets, `(Vector3 point, float? orientation = null, UnitEntityData unit = null)` for point targets. `UnitUseAbility.CreateCastCommand(AbilityData, TargetWrapper)` takes either — same command-execution pipeline for unit and location casts. Game's own summon/AoE flow uses the point-ctor.
- **Spell point-castability**: `AbilityData.CanTargetPoint` (property) / `BlueprintAbility.CanTargetPoint` (bool field). Also `.CanTargetSelf / CanTargetEnemies / CanTargetFriends`. `AbilityData.CanTarget(wrapper)` is the engine-authoritative combined check (point-capable + range + friend/foe flags).
- **Live spell DC**: `AbilityData.CalculateParams()` → `AbilityParams` with `.DC`, `.CasterLevel`, `.SpellLevel`, `.Concentration`. Parameterless, cheap (engine tooltips/AI call it constantly). Use for dynamic DC-vs-save comparisons.
- **Spell save type**: `BlueprintAbility` has NO direct `SavingThrowType` field. The save type lives on the `AbilityEffectRunAction` component: `bp.GetComponent<AbilityEffectRunAction>()?.SavingThrowType`. Direct field on the component — no action-graph walk. Buffs and utility spells often lack the component entirely (null-check).
- **`SavingThrowType` enum**: `Kingmaker.EntitySystem.Stats.SavingThrowType { Unknown=0, Fortitude=1, Reflex=2, Will=3 }`. `Unknown` means "no save" (Magic Missile, SR-only) — treat as "can't compute" rather than defaulting to 0, otherwise comparisons silently succeed.
- **Unit facing**: `UnitEntityData.OrientationDirection` is a public `Vector3` property returning the unit's forward vector. Use for positional offsets (point-target spawn placement, flanking calculations).
- **Post-combat evaluation**: `TacticsEvaluator.Tick` early-returns when `!Player.IsInCombat`. To let rules fire on the combat-end transition, `RunPostCombatCleanup()` runs a single evaluation pass with `ConditionEvaluator.IsPostCombatPass = true`, which makes `Combat.IsInCombat == false` conditions match regardless of transient game state. Cooldowns are skipped in this pass and cleared immediately after.
- **Buff blueprint filtering**: `BuffBlueprintProvider.IsCrusadeOnlyBuff` skips `Army*`-prefixed names (crusade/tactical-combat mini-game). Warpriest Blessings (`AirBlessingMinorBuff`, `ArtificeBlessingMinorBuff`, etc.) are REAL roleplay buffs — do NOT blanket-filter by "Blessing".
- **Buff picker search ranking**: `BuffPickerOverlay.RenderFilteredLayout` sorts by (prefix-match first, shorter-name first). Pure alphabetical puts `AirBlessingMajorBuff` ahead of `BlessBuff` when searching "bless" — keep the custom sort.
- **Class enumeration & tradition flags**: `Game.Instance.BlueprintRoot.Progression.AvailableCharacterClasses` / `AvailableCharacterMythics` are eager `IEnumerable<BlueprintCharacterClass>` — use these, not `BlueprintsCache.ForEachLoaded` (lazy, only returns in-memory blueprints). Per-class flags: `IsArcaneCaster`, `IsDivineCaster`, `IsMythic`. `BlueprintSpellbook.IsArcane` exists but there's NO symmetric `IsDivine` — derive divine from the class flag (`classes.Any(c => c.CharacterClass.IsDivineCaster)`), not the spellbook.
- **Dynamic save type with MagicHack precedence**: `BlueprintAbility.GetComponent<AbilityEffectRunAction>()?.SavingThrowType` is NOT authoritative. The game's resolver (`AbilityEffectRunAction.GetSavingThrowTypeInContext`, IL-visible) returns `ability.MagicHackData?.SavingThrowType` first, falling back to the blueprint component only when MagicHackData is null. Magic Deceiver fused spells and other hack-altered casts carry their live save type on the `AbilityData`, not the blueprint. Mirror the precedence exactly: `ability.MagicHackData?.SavingThrowType ?? ability.Blueprint.GetComponent<AbilityEffectRunAction>()?.SavingThrowType ?? Unknown`. Missing the MagicHack branch causes fused-spell rules to silently return `Unknown` → NaN → cast skipped.
- **`new AbilityData(parent, variant)` sets `.Blueprint` = variant**: when `FindAbility` returns a variant AbilityData via the 2-arg `(AbilityData, BlueprintAbility)` ctor (used for `AbilityVariants` — Command → Halt/Prone, Evil Eye → AC/Attack/Saves, Plague Storm → disease variants), `ability.Blueprint` is the VARIANT blueprint, not the parent. `GetComponent<X>` on `.Blueprint` sees only variant-level components. If the effect/save lives on the parent, fall back via `ability.m_ConvertedFrom?.Blueprint` (publicizer-accessible private field set by the same ctor).

## Release Process

Override of parent `wrath-mods/CLAUDE.md` §Release Process step 5: this mod is NOT on Nexus and has no `.github/workflows/`. After `git tag -a vX.Y.Z` + `git push origin master && git push origin vX.Y.Z`, publish manually: `gh release create vX.Y.Z --title "..." --notes "..." WrathTactics/bin/WrathTactics-X.Y.Z.zip` (the Release build target produces the zip).

## Logs

- **Mod session logs**: `<game>/Mods/WrathTactics/Logs/wrath-tactics-YYYY-MM-DD-HHMMSS.log` (separate from `Player.log`). Latest: `ssh deck-direct "ls -t '<game>/Mods/WrathTactics/Logs/' | head -1"`.

## Code Style

- K&R brace style (opening brace on same line)
- 4-space indentation
- `var` when type is apparent
- UI strings are English-only. No mixed-language (no `"Ja"/"Nein"`, no `"nicht"`) — use `Yes`/`No`, `!=`, etc.
- Equality conditions use inline `=`/`!=` operator dropdowns. Extend the operator pattern to new properties (HasBuff, HasCondition, CreatureType, Alignment) rather than adding a perpendicular Negate/NOT button or model flag.
