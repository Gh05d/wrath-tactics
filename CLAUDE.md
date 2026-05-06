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
    PlayerCommandGuard # Reference-tracks own commands; gates eval on foreign casts
    TargetingRelations # IsTargeting/IsTargetedBy primitives (Standard.TargetUnit + EngagedUnits)
    ResolvedTarget     # Unit XOR Point wrapper returned by TargetResolver
    UnitExtensions     # GetHD / GetEffectiveHD / MatchesClassValue helpers
    ClassProvider      # Single source of truth for HasClass dropdown + matching
    CommonBuffRegistry # Shared buff blueprint lookup table
    DefaultPresets     # Factory for built-in presets (seeded once via .seeded-defaults)
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
    SafeConditionConverter # Newtonsoft converter that drops unknown enum indices on load
  UI/                  # Unity UI
    TacticsPanel       # Main panel (Ctrl+T toggle), HUD button
    RuleEditorWidget   # Rule editing: conditions, action, target dropdowns
    ConditionRowWidget # Single condition row in the rule editor
    PresetPanel        # Preset selection/management sub-panel
    BuffPickerOverlay  # Searchable buff blueprint picker
    SpellPickerOverlay # Searchable spell/ability picker
    SpellDropdownProvider # Populates spell/ability dropdowns from unit's spellbooks
    UIHelpers          # Shared UI utilities (incl. CreateTMPInputField + ManualInputCaret)
  Compatibility/       # Cross-mod compat
    BubbleBuffsCompat  # Integration with Buff It 2 The Limit
  Localization/        # i18n
    Strings            # Localization key → string lookup
    EnumLabels         # Enum-key tables (KeysForCondition etc.) driving dropdowns
    {en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json  # Locale files (5 supported)
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

> Compact rules below. IL evidence, version history, predecessor patterns, and incident reports: [`docs/wrath-api-deep-dive.md`](docs/wrath-api-deep-dive.md).

- `GameInstall/` is a symlink to `../wrath-epic-buffing/GameInstall` — do not commit
- `GamePath.props` is machine-specific — gitignored
- **Phantom log lines on deck**: deck DLL = last `./deploy.sh`'d, not HEAD. Trace message that doesn't `grep` in source = leftover instrumentation; re-deploy clean before trusting deck logs.
- **Verify deploy before diagnosing "doesn't work"**: First check `ssh deck-direct stat` on deployed DLL vs. local source mtime. Older deck DLL = fix isn't on system under test. ([deep-dive](docs/wrath-api-deep-dive.md#verify-deploy))
- **CodeGraph stale lock**: `database is locked` from any `codegraph_*` tool ⇒ killed CodeGraph process. Lock is a directory at `.codegraph/codegraph.db.lock/` — `rm -rf` clears it. Index intact unless `codegraph status` shows drift.
- No per-round EventBus events in RTWP mode — use `Game.Instance.Player.GameTime` in `Update()`.
- `UnitUseAbility.CreateCastCommand` rejects synthetic AbilityData — only works for real spellbook spells.
- **Unity Rebuild pattern**: `Destroy()` on VLG/CSF is deferred — use `DestroyImmediate()` for layout components in `Rebuild()` to avoid duplicate layout calculators for one frame.
- **Nested ScrollRects**: Inner steals scroll events from outer. Disable `inner.ScrollRect.enabled` unless content overflows; re-enable conditionally in `UpdateHeight()`.
- **Validator strictness is load-bearing**: New `ActionType` MUST validate up-front in `ActionValidator.CanExecute`, including `AbilityData.IsAvailable`. Casts queue then silently drop, blocking rule fall-through. ([deep-dive](docs/wrath-api-deep-dive.md#validator-strictness))
- **Input fields**: Always use `UIHelpers.CreateTMPInputField` — auto-attaches `ManualInputCaret` and sets `onFocusSelectAll = false`. Rolling a fresh `TMP_InputField` resurrects invisible-caret + wipe-on-click bugs.
- **Preset-edit mode in `RuleEditorWidget`**: `unitId == null` ⇒ editing a preset. Field-edit handlers must route through `PersistEdit()` — direct `ConfigManager.Save()` writes character-rules JSON and silently discards preset edits. **Never have `PersistEdit` self-recurse** — `StackOverflowException`, silent UI freeze.
- **Linked rules carry empty body by design**: `PresetId`-only rules with empty `ConditionGroups`/`Action`/`Target` are valid — `PresetRegistry.Resolve` substitutes the body at runtime. Cleanup passes MUST exempt `!string.IsNullOrEmpty(r.PresetId)`. ([deep-dive](docs/wrath-api-deep-dive.md#linked-rules-empty-body))
- **`StackOverflowException` is uncatchable in .NET** — Unity main thread dies silently, no log, panel stays rendered but stops processing input ("UI frozen but visible, Ctrl+T won't close it"). Diagnose via code search for `Foo() { ... Foo(); ... }` patterns, not via logs.
- **Idempotent default-seeding**: `{ModPath}/Presets/.seeded-defaults` (text, one ID per line) tracks ever-written defaults. Deletions stay deleted, manual edits stay edited, new version-bump defaults slot in once. Re-seeding from in-memory dict alone re-seeds user-deleted defaults every reload.
- **File-save failures must be surfaced**: `PresetManager.Save` / `ConfigManager.Save` catch all exceptions. Pattern: methods return `bool`, UI surfaces via status line that persists across `Rebuild` (see `PresetPanel.SetStatus` / `lastIOStatus`).
- **Default-preset factory body changes don't propagate**: `.seeded-defaults` is per-ID, not per-content-hash. Editing `DefaultPresets.Build()` only affects new installs — release notes must tell existing users to edit in-game OR delete the preset JSON + its line from `.seeded-defaults`.
- **Preset JSON uses numeric enum indices** (Newtonsoft default — no `StringEnumConverter`). Hand-patching needs cross-check against `Models/Enums.cs`; removals shift later indices. Safer: edit via Presets tab in-game.
- **`ResolvedTarget` wrapper**: `TargetResolver.Resolve` returns a struct (Unit XOR Point, both null = `None`). `ActionValidator.CanExecute` and `CommandExecutor.Execute` branch on `IsPoint`. `ResolvedTarget.None` from an unresolvable picker fails validation — no silent self-cast fallback.
- **HasClass condition value encoding**: `Condition.Value` is `group:<spellcaster|arcane|divine|martial>` or `class:<InternalName>` (blueprint `name` minus `Class` suffix, e.g. `class:Wizard`). Never store localized display name. `ClassProvider.GetAll()` and `UnitExtensions.MatchesClassValue` are SSoT. ([deep-dive](docs/wrath-api-deep-dive.md#hasclass-encoding))
- **Rule-scoped ambient statics in `ConditionEvaluator`**: `CurrentAction` / `CurrentOwner` / `LastMatchedEnemy` / `LastMatchedAlly` are private statics set in `Evaluate(rule, owner)`, cleared in `finally`. Always wrap in try/finally — leak between rules ⇒ cross-contamination.
- **Group-AND is same-unit for Enemy/Ally scopes**: Multiple `Enemy.*` conditions in one ConditionGroup must all match the *same* enemy (same for `Ally.*`). Different scopes still AND-compose across scopes. To express "different enemies": split into separate OR-groups. Implementation: `ConditionEvaluator.EvaluateEnemyBucket` / `EvaluateAllyBucket`.
- **WithinRange value encoding**: `Condition.Value` = bare `RangeBracket` enum name (`Melee`/`Cone`/`Short`/`Medium`/`Long`). Thresholds in `RangeBrackets.MaxMeters` (2/5/10/20/40 m). Operators: `= X` strict bracket; `<= X` cumulative ("within X or closer"); `>= X` at-or-farther; `<`/`>` strict outside. Use `<= Short` for "within 10 m", not `= Short`. ([deep-dive](docs/wrath-api-deep-dive.md#withinrange-encoding))
- **Widgets in `RuleEditorWidget` MUST invoke `onChanged?.Invoke()`**, never `ConfigManager.Save()` directly: parent's save-callback routes to `PresetRegistry.Save` (preset mode) or `ConfigManager.Save` (character mode). Direct `ConfigManager.Save()` always writes character-rules — preset edits vanish on reload. Applies to `ConditionRowWidget` and any future sub-widget.
- **New `ConditionSubject` must be classified in `IsEnemyScope` / `IsAllyScope`** (`ConditionEvaluator.cs`): `EvaluateGroup` buckets by scope and enforces same-unit AND. Only adding to the dispatch switch silently bypasses the same-unit fix. Also extend `PickMetric` for sort-picks (EnemyLowest*/EnemyHighest*). ([deep-dive](docs/wrath-api-deep-dive.md#subject-scope-classification))
- **Adding a new `UnitCondition` to HasCondition picker requires THREE sites synced**: `ConditionEvaluator.HasConditionByName` switch (lowercase string-key), `EnumLabels.KeysForCondition` (PascalCase), one i18n entry per locale (`enum.condition.<PascalCase>` × 5 locale files). ([deep-dive](docs/wrath-api-deep-dive.md#unitcondition-three-sites))
- **`PlayerCommandGuard`**: reference-tracks `Commands.Run` from `CommandExecutor`; gates `TacticsEvaluator.EvaluateUnit` on foreign active casts. Scope is **intentionally narrow**: Standard slot only, `UnitUseAbility` class only, AND filtered against `unit.Brain.AutoUseAbility.Blueprint`. Don't widen — each constraint exists to fix a specific over-block regression. ([deep-dive](docs/wrath-api-deep-dive.md#playercommandguard-scope))

## Game API Gotchas

> Compact rules below. Engine internals (constructor matrices, IL details, slot accessors) are catalogued in [`docs/wrath-api-deep-dive.md`](docs/wrath-api-deep-dive.md).

- **No `NegativeEnergyAffinity` component class in WotR**: healing-vs-undead inversion is engine-intern in `RuleHealDamage`. For mod-side detection, OR-combine: Blueprint.Type substring (`"undead"`) + Progression.Features blueprint-name substring (`"NegativeEnergyAffinity"` / `"Dhampir"`). See `ActionValidator.IsNegativeEnergyAffine`. Type-only misses Dhampir; feature-only misses vampire summons / NPC-undead.
- **Blueprint enumeration**: `ResourcesLibrary.s_BlueprintsBundle` doesn't exist (binary pack, not AssetBundle). Use `ResourcesLibrary.BlueprintsCache.ForEachLoaded((guid, bp) => ...)`. Returns only loaded (lazy) blueprints, not all.
- **Item consumption**: Always use `item.SpendCharges(caster.Descriptor)` — engine-authoritative across Wand/Potion/Scroll, removes 0-charge wands, honors bypass features. Hand-rolled `item.Charges--` underflows on stacked Utility items; `Inventory.Remove` skips wand cleanup. ([deep-dive](docs/wrath-api-deep-dive.md#item-consumption))
- **Synthetic AbilityData fallback**: Inventory items have synthetic AbilityData → `CreateCastCommand` silently drops them. Use `Rulebook.Trigger<RuleCastSpell>` (FX, no animation), or quickslot the item for animation path. UseItem scans BOTH `owner.Abilities.RawFacts` AND `Game.Instance.Player.Inventory`. **Dedup is per-(ability-GUID, item-type)**: potions and scrolls share GUIDs — naïve GUID-dedup drops one. Two-pass scan (potions first, scrolls second) in both `SpellDropdownProvider.GetItemAbilities` AND `ActionValidator.FindUseItemSource`. ([deep-dive](docs/wrath-api-deep-dive.md#synthetic-abilitydata))
- **Enemy enumeration**: `Game.Instance.State.Units` returns ALL units (80+). Filter on `IsInCombat`, else companions chase non-combat targets. Must be consistent in BOTH `ConditionEvaluator.GetVisibleEnemies` AND `TargetResolver.GetVisibleEnemies`.
- **CreatureType detection**: Many vanilla units (e.g. all swarms) have `Blueprint.Type = null`. Match via feature list (`SwarmDiminutiveFeature`, `SwarmTinyFeature`).
- **`ClassifyHeal` keyword tables**: returns `HealEnergyType.{Positive,Negative,None}`. Negative checked first. Known imprecision: `cure` matches Cure Disease/Deafness/Neutralize Poison; UMD-gate limits scroll mis-casts. `restoration` deliberately absent. ([deep-dive](docs/wrath-api-deep-dive.md#classifyheal))
- **UI display labels for compound enum names**: `ConditionRowWidget.PropertyLabel` maps identifiers like `SpellDCMinusSave` → `"DC − Save"`. Any new compound `ConditionProperty` needs a `PropertyLabel` case. Use Unicode minus `−` (U+2212), not ASCII `-`.
- **AbilityData ctors**: `(BlueprintAbility, UnitDescriptor)`, `(Ability)`, `(BlueprintAbility, Spellbook, int level)`, **`(AbilityData parent, BlueprintAbility variant)`** for `AbilityVariants` (Command, Plague Storm, Evil Eye — works for spellbook spells AND class abilities). No 3-param `(blueprint, descriptor, ItemEntity)` — use 2-param + `OverrideCasterLevel`/`OverrideSpellLevel`.
- **The 2-arg variant ctor silently drops `SpellLevelInSpellbook`** → `GetAvailableForCastSpellCount` returns 0, blocks cast. Spellbook variants (Greater Spell Dispelling, Create Undead, Command/Halt, Plague Storm, etc.) ALL break; class-ability variants (Evil Eye, LoH) unaffected. **Fix**: after `new AbilityData(parent, variant)`, copy: `data.SpellLevelInSpellbook = parent.SpellLevelInSpellbook`. Centralized in `ActionValidator.MakeVariantData`. `IsAvailable` is NOT a workaround. ([deep-dive](docs/wrath-api-deep-dive.md#variant-ctor-bug))
- **Spellbook max level**: loop to `book.MaxSpellLevel` (instance prop). Regular books cap at 9, mythic at 10. Never hardcode.
- **Spellbook stores spells in three parallel arrays** — `m_KnownSpells`, `m_CustomSpells`, `m_SpecialSpells`. `AddSpecialSpellList` factlogic (Cleric Domain, Sorcerer Bloodline, Witch Patron, Shaman Spirit) writes non-mythic lists exclusively to `m_SpecialSpells`. Any enumeration MUST iterate `book.GetSpecialSpells(level)` alongside `GetKnownSpells` / `GetCustomSpells`. Slot accounting is unified via `m_MemorizedSpells` with `SpellSlot.Type==Special`. ([deep-dive](docs/wrath-api-deep-dive.md#spellbook-storage-layout))
- **`GetAvailableForCastSpellCount` returns `-1` for cantrips** (level 0 sentinel). `0` = no slot or spell-not-in-book; positive = remaining slots. Validators must compare against `== 0` (fail) and `!= 0` (pass), never `<= 0` / `> 0` — treating `-1` as "no slots" silently rejects every cantrip rule. ([deep-dive](docs/wrath-api-deep-dive.md#getavailableforcastspellcount-cantrip-sentinel))
- **ActivatableAbility API**: Has `TryStart()` but NO `TryStop()`. Deactivate via `IsOn = false`.
- **Target-aware `ActivatableAbility`s exist** — Mount is the canonical example: `TryStart()` sets `IsWaitingForTarget=true`, then engine expects a target-unit click. Current `ToggleActivatable` action only handles self-targeted toggles and CANNOT drive Mount end-to-end. ([deep-dive](docs/wrath-api-deep-dive.md#mount-ability))
- **Spellbook slot counts**: `GetSpellsPerDay(level)` is MAX per-day capacity (never decrements) — wrong for "can I still cast?". Use `GetAvailableForCastSpellCount(ability)` — handles prepared, spontaneous, Arcanist-hybrid, opposition schools/descriptors.
- **Ability resource cost**: Use `AbilityResourceLogic.CalculateCost(ability)` not `.Amount` — honors `OverrideRequiredResource`, `IsSpendResource`, `ResourceCostIncreasing/DecreasingFacts`, custom `IAbilityResourceCostCalculator`. Matches engine `Spend()` path.
- **Alignment API**: `UnitDescriptor.Alignment` is a `UnitAlignment` object; the value is `.ValueRaw` of type `Kingmaker.Enums.Alignment` (9-value enum, NOT a flag). Not the same as `AlignmentMaskType` (flag). For component matching, enumerate the 3 member values explicitly.
- **`TargetWrapper` dual ctors**: `(UnitEntityData)` for unit, `(Vector3 point, float? orientation = null, UnitEntityData unit = null)` for point. `UnitUseAbility.CreateCastCommand(AbilityData, TargetWrapper)` takes either.
- **Spell point-castability**: `AbilityData.CanTargetPoint` / `BlueprintAbility.CanTargetPoint`. Also `.CanTargetSelf / CanTargetEnemies / CanTargetFriends`. `AbilityData.CanTarget(wrapper)` is the engine-authoritative combined check.
- **Live spell DC**: `AbilityData.CalculateParams()` → `AbilityParams` with `.DC`, `.CasterLevel`, `.SpellLevel`, `.Concentration`. Parameterless, cheap.
- **Spell save type**: `BlueprintAbility` has NO direct `SavingThrowType` field. Save type lives on `AbilityEffectRunAction`. Buffs/utility spells often lack the component (null-check). For dynamic resolution, prefer `ability.MagicHackData?.SavingThrowType ?? bp.GetComponent<AbilityEffectRunAction>()?.SavingThrowType ?? Unknown` — Magic Deceiver fused spells carry the live save on AbilityData, not blueprint. ([deep-dive](docs/wrath-api-deep-dive.md#dynamic-save-type))
- **`SavingThrowType` enum**: `{ Unknown=0, Fortitude=1, Reflex=2, Will=3 }`. `Unknown` = "no save" (Magic Missile, SR-only) — treat as "can't compute", not 0.
- **`RuleCalculateAttackBonusWithoutTarget(unit, weapon, penalty)`**: engine-authoritative full-AB minus target-side factors. `.Result` includes BAB, stat-mod, weapon enhancement, feats, active buffs. No random rolls. Use over manual `BAB + statMod` summation. `RuleCalculateAttackBonus` (with target) adds flanking/bane. ([deep-dive](docs/wrath-api-deep-dive.md#rulecalculateattackbonuswithouttarget))
- **Unit facing**: `UnitEntityData.OrientationDirection` is a public `Vector3` returning forward vector.
- **Continuous out-of-combat tick**: `TacticsEvaluator.Tick` runs in both states. Out-of-combat interval = `TacticsConfig.OutOfCombatTickIntervalSeconds` (default 2 s, JSON-only); pre-filtered through `RuleEnabledOutOfCombat` — only rules carrying `Combat.IsInCombat==false` somewhere pass the gate. Cooldowns operate purely in real-time across the boundary. Predecessor `IsPostCombatPass`/`RunPostCombatCleanup` removed entirely. ([deep-dive](docs/wrath-api-deep-dive.md#continuous-out-of-combat-tick-since-170))
- **Buff blueprint filtering**: `BuffBlueprintProvider.IsCrusadeOnlyBuff` skips `Army*`-prefixed names (crusade mini-game). Warpriest Blessings (`AirBlessingMinorBuff` etc.) are REAL — do NOT blanket-filter by "Blessing".
- **Buff picker search ranking**: `BuffPickerOverlay.RenderFilteredLayout` sorts by (prefix-match first, shorter-name first). Pure alphabetical breaks search (e.g. `AirBlessingMajorBuff` ahead of `BlessBuff` for "bless").
- **Class enumeration & tradition flags**: `Game.Instance.BlueprintRoot.Progression.AvailableCharacterClasses` / `AvailableCharacterMythics` are eager — use these, not `BlueprintsCache.ForEachLoaded`. Per-class flags: `IsArcaneCaster`, `IsDivineCaster`, `IsMythic`. `BlueprintSpellbook.IsArcane` exists; NO symmetric `IsDivine` — derive from class flag.
- **`new AbilityData(parent, variant)` sets `.Blueprint = variant`**: variant AbilityData's `Blueprint` is the variant, not parent. `GetComponent<X>` sees only variant-level components. Fall back via `ability.m_ConvertedFrom?.Blueprint` (publicizer-accessible).
- **`AbilityData.IsAvailable`** is the authoritative "can cast right now?": composes `IsAvailableInSpellbook && IsAvailableForCast && !TemporarilyDisabled`. Iterates `Blueprint.CasterRestrictions[]` (in-combat gates, silenced, polymorph, forbidden spellbooks, UMD). `GetUnavailableReason()` returns localized tooltip — useful for debug logs. **Filter ANY candidate-enumeration** over `RawFacts` or spellbook spells where the pick is later cast. ([deep-dive](docs/wrath-api-deep-dive.md#isavailable))
- **Two `IsDead` cases in `ConditionEvaluator.cs`**: `EvaluateUnitProperty` (~L468, hot path) and `MatchesPropertyThreshold` (~L576, count-subject path). Keep both in sync. Correct check: `unit.Descriptor?.State?.IsFinallyDead ?? false` — **not** `State.IsDead` (true for down-but-auto-recovering allies on Normal). ([deep-dive](docs/wrath-api-deep-dive.md#unitstateisdead-vs-isfinallydead))
- **Targeting-relation primitives**: `unit.Commands.Standard?.TargetUnit` (engine current command target — works for `UnitAttack`/`UnitUseAbility`/any `UnitCommand`) + `unit.CombatState.EngagedUnits` (returns `KeyCollection`, NOT Dictionary — query via LINQ `.Contains(victim)`; backing `m_EngagedUnits` is publicizer-accessible for O(1)). Centralized in `Engine/TargetingRelations.Has(attacker, victim)`. Approach-phase units match neither — accepted blind spot, latency ≤1 tick. ([deep-dive](docs/wrath-api-deep-dive.md#targeting-relation-primitives))
- **Summoned-creature detection**: `unit.Get<UnitPartSummonedMonster>() != null`. Engine adds via `EntityDataBase.Ensure<UnitPartSummonedMonster>()` during summon-spell flow. Does NOT cover pets/animal companions/Aivu/Eidolons/mod pets — those carry `UnitPartPetMaster`, never `UnitPartSummonedMonster`. Asymmetry is correct semantic for "limit summon spam" rules. ([deep-dive](docs/wrath-api-deep-dive.md#summon-detection))
- **`GetHD()` vs `GetEffectiveHD()`** (`Engine/UnitExtensions.cs`): `GetHD` = `Progression.CharacterLevel` only (used by `ConditionProperty.HitDice` since engine HD-cap rules exclude Mythic). `GetEffectiveHD` adds `MythicLevel` for `EnemyHDMinusPartyLevel` margin comparisons. **Don't unify.** Note: `EnemyHDMinusPartyLevel` uses `Game.Instance.Player.Party` (NOT `PartyAndPets`) — pets have separate progression curves. **The one documented exception** to the project-wide PartyAndPets convention. ([deep-dive](docs/wrath-api-deep-dive.md#gethd-vs-geteffectivehd))
- **`ABMinusAC` condition**: Evaluates `partyBestAB − enemy.AC` via `RuleCalculateAttackBonusWithoutTarget` across living party members (`!IsFinallyDead`, `IsInGame`, has weapon / `EmptyHandWeapon`). Enemy-scope-only. Party-best-AB cached in rule-scoped static (`CurrentPartyBestAB`) since `ComputeABMinusAC` is called once per enemy. New computed-delta conditions: scope-check, read `CurrentOwner` from rule-scoped static, return `float.NaN` → `false` on uncomputable, add Trace log for thresholds.
- **Use `Player.PartyAndPets`, never `Player.Party`** for active-group iteration covering companions and pets (animal companions, Aivu, Eidolons, mod pets). `Player.Party` excludes pets — symptom: "Pets don't get tactics tabs / aren't healed / don't count toward AllyCount". Single regression check: `grep 'Player.Party'` before merge. Exception: `EnemyHDMinusPartyLevel` (above). ([deep-dive](docs/wrath-api-deep-dive.md#partyandpets))

## Release Process

Follow parent `wrath-mods/CLAUDE.md` §Release Process. Remote is `origin`. The `/release` slash-command (`.claude/commands/release.md`) runs the full flow: bump → build → user-confirm gate → push → tag → GitHub Release → Nexus upload (auto via `.github/workflows/nexus-upload.yml`) → Discord-post generation.

Nexus mod-page: https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005 (ID 1005, file_group_id 4191).

`deploy.sh` is **dev-only** — Debug build SCP'd to Steam Deck for smoke-testing. Release builds come from `/release`'s Release-config build → distributable ZIP at `WrathTactics/bin/WrathTactics-X.Y.Z.zip`.

## Logs

- **Mod session logs**: `<game>/Mods/WrathTactics/Logs/wrath-tactics-YYYY-MM-DD-HHMMSS.log` (separate from `Player.log`). Latest: `ssh deck-direct "ls -t '<game>/Mods/WrathTactics/Logs/' | head -1"`.

## Code Style

- K&R brace style (opening brace on same line)
- 4-space indentation
- `var` when type is apparent
- UI strings are English-only. No mixed-language — use `Yes`/`No`, `!=`, etc.
- Equality conditions use inline `=`/`!=` operator dropdowns. Extend the operator pattern to new properties (HasBuff, HasCondition, CreatureType, Alignment) rather than adding a perpendicular Negate/NOT button or model flag.
- **i18n math-notation properties are LOCALIZED per locale** — never paste en-GB into all 5 files. Existing `ABMinusAC` / `SpellDCMinusSave` / `EnemyHDMinusPartyLevel` rows use locale-native abbreviations: de `AB − RK` / `SG − Rettung` / `TW − Gruppe`, fr `BAB − CA` / `DD − Sauvegarde` / `DV − Groupe`, ru `БА − КБ` / `СЛ − Спасбросок` / `КЗ − Группа`, zh mixes native (`生命骰`, `豁免`) with kept-English (`AB − AC`). Copy from existing `ABMinusAC` row in each locale.
