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

**Version bump** requires TWO files: `WrathTactics/Info.json` (UMM reads this) and `WrathTactics/WrathTactics.csproj` `<Version>`. Bumping only one ships a zip with the stale version in its name.

## Tests

Pure-logic xUnit suite in `WrathTactics.Tests/` (net481). Covers `ConditionEvaluator.CompareCount`, `BuffBlueprintProvider.IsCrusadeOnlyBuff`, `CommonBuffRegistry.IsEnemySubject` / `GetDefaultGuids`, `RangeBrackets.MaxMeters`. Mono hosts the .NET-Framework runner on Linux; no Game.Instance / Unity needed.

```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```

Run before pushing changes to `ConditionEvaluator`, `BuffBlueprintProvider`, `CommonBuffRegistry`, or `Models/Enums.cs` (`RangeBrackets`).

- **Mono required**: `sudo apt install mono-complete` (one-time). Without it: "Could not find 'mono' host".
- **Game DLLs at test runtime**: test csproj's `AfterTargets="Build"` target copies `GameInstall/Wrath_Data/Managed/*.dll` + `WrathTactics/obj/Debug/publicized/*.dll` to test output. Without the copy, `TypeLoadException` on first `ConditionEvaluator` field load.
- **InternalsVisibleTo**: `WrathTactics/Properties/AssemblyInfo.cs` exposes internals to the test assembly. Promote private statics to `internal static` to test them.
- **No CI by design**: game DLLs unreachable from GitHub runners. Run tests locally before release.

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
    ActiveRuleTracker  # DAO priority gate (per-unit, see deep-dive)
    TargetingRelations # IsTargeting/IsTargetedBy primitives
    ResolvedTarget     # Unit XOR Point wrapper returned by TargetResolver
    UnitExtensions     # GetHD / GetEffectiveHD / MatchesClassValue helpers
    ClassProvider      # SSoT for HasClass dropdown + matching
    CommonBuffRegistry # Shared buff blueprint lookup table
    DefaultPresets     # Factory for built-in presets (seeded once via .seeded-defaults)
    BuffBlueprintProvider # Buff blueprint data for condition checks
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
    SafeConditionConverter # Drops unknown enum indices on load
  UI/                  # Unity UI (TacticsPanel, RuleEditorWidget, ConditionRowWidget,
                       # PresetPanel, BuffPickerOverlay, SpellPickerOverlay,
                       # SpellDropdownProvider, UIHelpers)
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

## Gotchas

> Compact rules below. IL evidence, version history, predecessor patterns, and incident reports: [`docs/wrath-api-deep-dive.md`](docs/wrath-api-deep-dive.md).

- `GameInstall/` is a symlink to `../wrath-epic-buffing/GameInstall` — do not commit
- `GamePath.props` is machine-specific — gitignored
- **Phantom log lines on deck**: deck DLL = last `./deploy.sh`'d, not HEAD. Trace message that doesn't `grep` in source = leftover instrumentation; re-deploy clean before trusting deck logs.
- **Verify deploy before diagnosing "doesn't work"**: `ssh deck-direct stat` on deployed DLL vs. local source mtime. Older deck DLL = fix isn't on system under test. ([deep-dive](docs/wrath-api-deep-dive.md#verify-deploy))
- **Check master vs. user-reported version first**: user reports often lag HEAD by 1–2 releases. Run `git log v<user-version>..HEAD --oneline -- <suspect-file>` before Phase-1 investigation — bug may already be fixed.
- **CodeGraph stale lock**: `database is locked` from any `codegraph_*` tool ⇒ killed CodeGraph process. Lock is a directory at `.codegraph/codegraph.db.lock/` — `rm -rf` clears it.
- No per-round EventBus events in RTWP mode — use `Game.Instance.Player.GameTime` in `Update()`.
- `UnitUseAbility.CreateCastCommand` rejects synthetic AbilityData — only works for real spellbook spells.
- **Unity Rebuild pattern**: `Destroy()` on VLG/CSF is deferred — use `DestroyImmediate()` for layout components in `Rebuild()` to avoid duplicate layout calculators for one frame.
- **Nested ScrollRects**: Inner steals scroll events from outer. Disable `inner.ScrollRect.enabled` unless content overflows; re-enable conditionally in `UpdateHeight()`.
- **Validator strictness is load-bearing**: new `ActionType` must validate up-front in `ActionValidator.CanExecute` (incl. `AbilityData.IsAvailable`). Chain-capable types (CastSpell, CastAbility) MUST route through `ResolveCastSpellChain` — single-GUID validation misses the chain. ([deep-dive](docs/wrath-api-deep-dive.md#validator-strictness))
- **Input fields**: always use `UIHelpers.CreateTMPInputField` — auto-attaches `ManualInputCaret` and sets `onFocusSelectAll = false`. Rolling a fresh `TMP_InputField` resurrects invisible-caret + wipe-on-click bugs.
- **Preset-edit mode in `RuleEditorWidget`**: `unitId == null` ⇒ editing a preset. Field-edit handlers must route through `PersistEdit()` — direct `ConfigManager.Save()` writes character-rules JSON and silently discards preset edits. **Never have `PersistEdit` self-recurse** — `StackOverflowException`, silent UI freeze.
- **Linked rules carry empty body by design**: `PresetId`-only rules with empty `ConditionGroups`/`Action`/`Target` are valid — `PresetRegistry.Resolve` substitutes the body at runtime. Cleanup passes MUST exempt `!string.IsNullOrEmpty(r.PresetId)`. ([deep-dive](docs/wrath-api-deep-dive.md#linked-rules-empty-body))
- **`StackOverflowException` is uncatchable in .NET** — Unity main thread dies silently, no log, panel stays rendered but stops processing input. Diagnose via code search for `Foo() { ... Foo(); ... }`, not via logs.
- **Idempotent default-seeding**: `{ModPath}/Presets/.seeded-defaults` tracks ever-written defaults (one ID per line). Deletions stay deleted, manual edits stay edited, new version-bump defaults slot in once. Re-seeding from in-memory dict alone re-seeds user-deleted defaults every reload.
- **File-save failures must be surfaced**: `PresetManager.Save` / `ConfigManager.Save` catch all exceptions. Methods return `bool`, UI surfaces via status line that persists across `Rebuild` (see `PresetPanel.SetStatus` / `lastIOStatus`).
- **Default-preset factory body changes don't propagate**: `.seeded-defaults` is per-ID, not per-content-hash. Editing `DefaultPresets.Build()` only affects new installs — release notes must tell existing users to edit in-game OR delete the preset JSON + its line from `.seeded-defaults`.
- **Preset JSON uses numeric enum indices** (Newtonsoft default — no `StringEnumConverter`). Hand-patching needs cross-check against `Models/Enums.cs`; removals shift later indices. Safer: edit via Presets tab in-game.
- **`ResolvedTarget` wrapper**: `TargetResolver.Resolve` returns a struct (Unit XOR Point, both null = `None`). `ActionValidator.CanExecute` and `CommandExecutor.Execute` branch on `IsPoint`. `ResolvedTarget.None` fails validation — no silent self-cast fallback.
- **HasClass value encoding**: `Condition.Value` is `group:<spellcaster|arcane|divine|martial>` or `class:<InternalName>` (blueprint `name` minus `Class` suffix). Never store localized display name. ([deep-dive](docs/wrath-api-deep-dive.md#hasclass-encoding))
- **Rule-scoped ambient statics in `ConditionEvaluator`**: `CurrentAction` / `CurrentOwner` / `LastMatchedEnemy` / `LastMatchedAlly` are private statics set in `Evaluate(rule, owner)`, cleared in `finally`. Always wrap in try/finally — leak between rules ⇒ cross-contamination.
- **Group-AND is same-unit for Enemy/Ally scopes**: multiple `Enemy.*` conditions in one ConditionGroup must all match the *same* enemy (same for `Ally.*`). To express "different enemies": split into separate OR-groups. Implementation: `EvaluateEnemyBucket` / `EvaluateAllyBucket`.
- **Bucket count-path must use `CompareCount`, not raw `<` (1.14.1 regression)**: `EvaluateEnemyBucket` / `EvaluateAllyBucket` once hardcoded `>=` via `if (count < threshold) return false`. Multi-condition groups using `EnemyCount</<=/==/!=` returned wrong verdicts. Bucket UI emits one count row, so use `countConds[0].CountOperator` with `CompareCount`.
- **WithinRange value encoding**: `Condition.Value` = bare `RangeBracket` enum name (`Melee`/`Cone`/`Short`/`Medium`/`Long`); thresholds in `RangeBrackets.MaxMeters` (2/5/10/20/40 m). Operators: `= X` strict bracket; `<= X` cumulative. Use `<= Short` for "within 10 m", not `= Short`. ([deep-dive](docs/wrath-api-deep-dive.md#withinrange-encoding))
- **Widgets in `RuleEditorWidget` MUST invoke `onChanged?.Invoke()`**, never `ConfigManager.Save()` directly: parent routes to `PresetRegistry.Save` (preset mode) or `ConfigManager.Save` (character mode). Direct save always writes character-rules — preset edits vanish on reload.
- **New `ConditionSubject` must be classified in `IsEnemyScope` / `IsAllyScope`**: only adding to the dispatch switch silently bypasses the same-unit AND fix. Also extend `PickMetric` for sort-picks. ([deep-dive](docs/wrath-api-deep-dive.md#subject-scope-classification))
- **Adding a new `UnitCondition` to HasCondition picker requires THREE sites synced**: `ConditionEvaluator.HasConditionByName` switch (lowercase), `EnumLabels.KeysForCondition` (PascalCase), one i18n entry per locale × 5 files. ([deep-dive](docs/wrath-api-deep-dive.md#unitcondition-three-sites))
- **Position-Conditions checklist**: a new positional `ConditionProperty` (e.g. `IsFlanked`, `AdjacentEnemyCount`) needs FIVE sites: enum entry, `EvaluateUnitProperty`, `MatchesPropertyThreshold`, both `isBool` chains in `ConditionRowWidget`, i18n × 5. ([deep-dive](docs/wrath-api-deep-dive.md#position-conditions))
- **Value-list `ConditionProperty` checklist** (property + value-from-list + =/≠, the HasCondition/CreatureType/Alignment family): NINE sites — enum entry; BOTH evaluator switches (`EvaluateUnitProperty` + `MatchesPropertyThreshold` in `ConditionEvaluator.UnitProperty.cs`); `ConditionRowWidget` (count-path eq-op group + `usesEqOp` + a render branch + `GetValueKeysForProperty` + `GetValueLabelsForProperty` + `GetPropertiesForSubject` per scope); `EnumLabels` Keys/Labels; i18n × 5. Mirror `HasDescriptorEffect` / `ImmuneToEnergy` (1.17.3).
- **`PlayerCommandGuard`** reference-tracks `Commands.Run` from `CommandExecutor`; gates eval on foreign active casts. Scope is **intentionally narrow**: Standard slot only, `UnitUseAbility` class only, AND filtered against `unit.Brain.AutoUseAbility.Blueprint`. Don't widen — each constraint exists to fix a specific over-block regression. ([deep-dive](docs/wrath-api-deep-dive.md#playercommandguard-scope))
- **`ActiveRuleTracker` (DAO priority gate)**: tracks `(RuleListSource, entry.Id, UnitCommand)` per-unit; while tracked command is `!IsFinished`, `TryExecuteRules` iterates only strictly higher-priority rules. Separate from `PlayerCommandGuard` (foreign casts vs. own commands). Toggles / `ThrowSplash` / `DoNothing` don't replace the entry — `Clear`-on-any-success reintroduces the lower-priority-preempt bug. ([deep-dive](docs/wrath-api-deep-dive.md#active-rule-tracker))
- **Cast fallback chain spans CastSpell + CastAbility (since 1.15)**: `ResolveCastSpellChain` is the authoritative validator + executor entry point for both Cast types — never re-introduce single-GUID validation on either branch (breaks rule fall-through when primary unavailable). ([deep-dive](docs/wrath-api-deep-dive.md#cast-fallback-chain))

## Game API Gotchas

> Compact rules below. Engine internals (constructor matrices, IL details, slot accessors) are catalogued in [`docs/wrath-api-deep-dive.md`](docs/wrath-api-deep-dive.md).

- **NegativeEnergyAffinity detection**: heal/damage flip is driven by the canonical fact `d5ee498e19722854198439629c1841a5`. Query via `UnitHelper.HasFact(descriptor, bp)` from `ActionValidator.IsNegativeEnergyAffine`. Don't reintroduce `Type.name.Contains("undead")` — vanilla undead carry specific subtype names. ([deep-dive](docs/wrath-api-deep-dive.md#negative-energy-affinity))
- **Descriptor-effect detection**: Poison/Disease/Bleed are NOT `UnitCondition`s — they're descriptor-flagged buffs. Detect via `BlueprintBuff.SpellDescriptor` over `unit.Buffs.RawFacts` (`ConditionEvaluator.HasBuffWithDescriptor`); one descriptor check catches every poison/disease buff. "Weakened" isn't a status at all → ability damage, lives under HasBuff.
- **Energy immunity**: `unit.Get<UnitPartDamageReduction>().IsImmune(DamageEnergyType)` — clean pre-cast check. Castable energies: Fire/Cold/Electricity/Acid/Sonic (no Force in `DamageEnergyType`). Effect/descriptor immunity (`BuffDescriptorImmunity` / `SpellImmunityType.IsImmune`) needs a `MechanicsContext` → NOT checkable before an actual cast; don't try.
- **Blueprint enumeration**: `ResourcesLibrary.s_BlueprintsBundle` doesn't exist (binary pack, not AssetBundle). Use `ResourcesLibrary.BlueprintsCache.ForEachLoaded((guid, bp) => ...)`. **`ForEachLoaded` is misnamed**: it iterates the FULL index (`m_LoadedBlueprints`, ~236k entries) and passes `entry.Blueprint` **with no null filter** (verified IL) — `bp` is `null` for any blueprint not yet lazily loaded. So a `bp is BlueprintBuff` check silently skips every unloaded buff; callers see only the session's already-touched subset. To list ALL of a type, force-load first: iterate `m_LoadedBlueprints.Keys` + `BlueprintsCache.Load(guid)` (publicizer-accessible) — but that's ~236k loads (~79s, all resident; eviction via `RemoveCachedBlueprint` is destructive — drops the index Offset). The buff picker pays this once and persists metadata to disk. ([deep-dive](docs/wrath-api-deep-dive.md#blueprint-full-enumeration))
- **Item consumption**: always `item.SpendCharges(caster.Descriptor)` — engine-authoritative across Wand/Potion/Scroll, removes 0-charge wands, honors bypass features. Hand-rolled `Charges--` / `Inventory.Remove` are wrong. ([deep-dive](docs/wrath-api-deep-dive.md#item-consumption))
- **`UsableItemType` enum (5 values)**: `Other=0 / Wand=1 / Scroll=2 / Potion=3 / Utility=4`. Activatable rods (e.g. Skeletal Finger) are `Type=Other` with `m_Ability=null` — those belong to ToggleActivatable, not UseItem. ([deep-dive](docs/wrath-api-deep-dive.md#usableitemtype-enum))
- **Synthetic AbilityData fallback**: inventory items have synthetic AbilityData → `CreateCastCommand` silently drops them. Use `Rulebook.Trigger<RuleCastSpell>` (FX, no animation) OR quickslot the item (animation path). UseItem scans BOTH `owner.Abilities.RawFacts` AND `Game.Instance.Player.Inventory`; dedup is per-(ability-GUID, item-type), four-pass POTION → SCROLL → WAND → UTILITY. ([deep-dive](docs/wrath-api-deep-dive.md#synthetic-abilitydata))
- **Enemy enumeration**: `Game.Instance.State.Units` returns ALL units (80+). Filter on `IsInCombat`, else companions chase non-combat targets. Keep consistent in BOTH `ConditionEvaluator.GetVisibleEnemies` AND `TargetResolver.GetVisibleEnemies`.
- **CreatureType detection**: many vanilla units (all swarms) have `Blueprint.Type = null`. Match via feature list (`SwarmDiminutiveFeature`, `SwarmTinyFeature`).
- **`ClassifyHeal` keyword tables**: returns `HealEnergyType.{Positive,Negative,None}`; Negative checked first. Known imprecision: `cure` matches Cure Disease/Deafness/Neutralize Poison (UMD-gate limits mis-casts); `restoration` deliberately absent. ([deep-dive](docs/wrath-api-deep-dive.md#classifyheal))
- **UI display labels for compound enum names**: `ConditionRowWidget.PropertyLabel` maps identifiers like `SpellDCMinusSave` → `"DC − Save"`. New compound `ConditionProperty` needs a `PropertyLabel` case. Use Unicode minus `−` (U+2212), not ASCII `-`.
- **AbilityData ctors**: `(BlueprintAbility, UnitDescriptor)`, `(Ability)`, `(BlueprintAbility, Spellbook, int level)`, **`(AbilityData parent, BlueprintAbility variant)`** for `AbilityVariants` (works for spellbook spells AND class abilities). No 3-param `(blueprint, descriptor, ItemEntity)`.
- **Variant ctor bug**: 2-arg `new AbilityData(parent, variant)` silently drops `SpellLevelInSpellbook` → `GetAvailableForCastSpellCount` returns 0. Fix: copy `data.SpellLevelInSpellbook = parent.SpellLevelInSpellbook` after construction. Centralized in `ActionValidator.MakeVariantData`. `IsAvailable` is NOT a workaround. ([deep-dive](docs/wrath-api-deep-dive.md#variant-ctor-bug))
- **Variant slot-lookup mismatch**: `Spellbook.GetAvailableForCastSpellCount` compares blueprint refs strictly; variant AbilityData has `Blueprint=variant` while prepared slot is keyed on parent — count returns 0, validator rejects. Pass `ability.ConvertedFrom ?? ability` to the slot probe. Use the public `ConvertedFrom` property, NEVER the `m_ConvertedFrom` field (kills test runtime). ([deep-dive](docs/wrath-api-deep-dive.md#variant-slot-lookup))
- **Variant components (`AbilityVariants` + `AbilityShadowSpell`)**: any variant-aware feature must enumerate both across `GetKnownSpells` + `GetCustomSpells` + `GetSpecialSpells` (SIX sites: `SpellDropdownProvider.GetSpells` and `ActionValidator.Find.FindAbilityEx`). Custom-spells loop is the metamagic-prepared / fused-spell path. Known gap: item/inventory branch still skips variants. ([deep-dive](docs/wrath-api-deep-dive.md#variant-component-handling))
- **"Spell X missing from picker" triage**: ask which spellbook list it lives in — `GetKnownSpells` / `GetCustomSpells` / `GetSpecialSpells` are three different code paths; bugs usually affect one. ([deep-dive](docs/wrath-api-deep-dive.md#variant-component-handling))
- **Modded metamagic enum scanning**: `Enum.GetValues(typeof(Metamagic))` misses raw-bit modded values (`1 << N` bare consts). `BuildMetamagicTag` post-scans the mask for leftover bits after the foreach — don't remove that loop. ([deep-dive](docs/wrath-api-deep-dive.md#metamagic-leftover-bits))
- **Spellbook max level**: loop to `book.MaxSpellLevel` (instance prop) — never hardcode `<= 9`. Mythic caps at 10. 1.14.1 regression: `CountAvailableSlotsAboveLevel` hardcoded 9, dropped mythic level-10 slots from `SpellSlotsAboveLevel`.
- **Three-array spellbook storage**: `m_KnownSpells` + `m_CustomSpells` + `m_SpecialSpells`. ANY enumeration MUST hit all three (`GetKnownSpells` + `GetCustomSpells` + `GetSpecialSpells`). 1.14.1 heal-picker regression: `FindBestHealEx` missed `GetCustomSpells`. ([deep-dive](docs/wrath-api-deep-dive.md#spellbook-storage-layout))
- **`GetAvailableForCastSpellCount` returns `-1` for cantrips** (level 0 sentinel). `0` = no slot or spell-not-in-book. Validators must compare `== 0` (fail) / `!= 0` (pass), never `<= 0` / `> 0` — treating `-1` as "no slots" silently rejects every cantrip rule. ([deep-dive](docs/wrath-api-deep-dive.md#getavailableforcastspellcount-cantrip-sentinel))
- **ActivatableAbility API**: has `TryStart()` but NO `TryStop()`. Deactivate via `IsOn = false`.
- **Target-aware `ActivatableAbility`s exist** (Mount is canonical): `TryStart()` sets `IsWaitingForTarget=true`, then engine expects a target-unit click. Current `ToggleActivatable` action only handles self-targeted toggles and CANNOT drive Mount end-to-end. ([deep-dive](docs/wrath-api-deep-dive.md#mount-ability))
- **Spellbook slot counts**: `GetSpellsPerDay(level)` is MAX per-day capacity (never decrements) — wrong for "can I still cast?". Use `GetAvailableForCastSpellCount(ability)`.
- **Ability resource cost**: use `AbilityResourceLogic.CalculateCost(ability)` not `.Amount` — honors overrides, `IsSpendResource`, `ResourceCostIncreasing/DecreasingFacts`, custom `IAbilityResourceCostCalculator`. Matches engine `Spend()` path.
- **Alignment API**: `UnitDescriptor.Alignment` is a `UnitAlignment` object; the value is `.ValueRaw` of type `Kingmaker.Enums.Alignment` (9-value enum, NOT a flag). Different from `AlignmentMaskType` (flag).
- **`TargetWrapper` dual ctors**: `(UnitEntityData)` for unit, `(Vector3 point, float? orientation = null, UnitEntityData unit = null)` for point. `UnitUseAbility.CreateCastCommand(AbilityData, TargetWrapper)` takes either.
- **Spell point-castability**: `AbilityData.CanTargetPoint` / `BlueprintAbility.CanTargetPoint`. Also `.CanTargetSelf / CanTargetEnemies / CanTargetFriends`. `AbilityData.CanTarget(wrapper)` is the engine-authoritative combined check.
- **Live spell DC**: `AbilityData.CalculateParams()` → `AbilityParams` with `.DC`, `.CasterLevel`, `.SpellLevel`, `.Concentration`. Parameterless, cheap.
- **Spell save type**: `BlueprintAbility` has NO direct `SavingThrowType` field — lives on `AbilityEffectRunAction` (often null on buffs/utility). Authoritative resolver: `ability.MagicHackData?.SavingThrowType ?? bp.GetComponent<AbilityEffectRunAction>()?.SavingThrowType ?? Unknown`. Magic Deceiver fused spells carry the live save on AbilityData. ([deep-dive](docs/wrath-api-deep-dive.md#dynamic-save-type))
- **`SavingThrowType` enum**: `{ Unknown=0, Fortitude=1, Reflex=2, Will=3 }`. `Unknown` = "no save" (Magic Missile, SR-only) — treat as "can't compute".
- **`RuleCalculateAttackBonusWithoutTarget(unit, weapon, penalty)`**: engine-authoritative full-AB minus target-side factors. Includes BAB, stat-mod, weapon enhancement, feats, active buffs. No random rolls. Use over manual summation. `RuleCalculateAttackBonus` (with target) adds flanking/bane. ([deep-dive](docs/wrath-api-deep-dive.md#rulecalculateattackbonuswithouttarget))
- **Unit facing**: `UnitEntityData.OrientationDirection` is a public `Vector3` returning forward vector.
- **Continuous out-of-combat tick (since 1.7.0)**: `TacticsEvaluator.Tick` runs in both states. Out-of-combat interval = `TacticsConfig.OutOfCombatTickIntervalSeconds` (default 2 s, JSON-only), pre-filtered through `RuleEnabledOutOfCombat`. Cooldown clock = `CooldownRounds * 6f` against `GameTime.TotalSeconds`; same clock in RTWP and turn-based. ([deep-dive](docs/wrath-api-deep-dive.md#continuous-out-of-combat-tick-since-170))
- **Buff blueprint filtering**: `BuffBlueprintProvider.IsCrusadeOnlyBuff` skips `Army*`-prefixed names (crusade mini-game). Warpriest Blessings (`AirBlessingMinorBuff` etc.) are REAL — do NOT blanket-filter by "Blessing".
- **Buff picker search ranking**: `BuffPickerOverlay.RenderFilteredLayout` sorts by (prefix-match first, shorter-name first). Pure alphabetical breaks search.
- **Class enumeration & tradition flags**: `Game.Instance.BlueprintRoot.Progression.AvailableCharacterClasses` / `AvailableCharacterMythics` are eager — use these, not `BlueprintsCache.ForEachLoaded`. Per-class flags: `IsArcaneCaster`, `IsDivineCaster`, `IsMythic`. `BlueprintSpellbook.IsArcane` exists; NO symmetric `IsDivine` — derive from class flag. Pet/companion classes never appear; for "is this unit a pet?" use `UnitPartPet`, not `HasClass`.
- **`new AbilityData(parent, variant)` sets `.Blueprint = variant`**: `GetComponent<X>` sees only variant-level components. Fall back via `ability.m_ConvertedFrom?.Blueprint` (publicizer-accessible).
- **`AbilityData.IsAvailable`** is the authoritative "can cast right now?": composes `IsAvailableInSpellbook && IsAvailableForCast && !TemporarilyDisabled`. Iterates `CasterRestrictions[]` (in-combat gates, silenced, polymorph, forbidden spellbooks, UMD). **Filter ANY candidate-enumeration** over `RawFacts` or spellbook spells. ([deep-dive](docs/wrath-api-deep-dive.md#isavailable))
- **Two `IsDead` cases in `ConditionEvaluator.cs`**: `EvaluateUnitProperty` (~L468, hot path) and `MatchesPropertyThreshold` (~L576, count-subject path). Keep both in sync. Correct check: `unit.Descriptor?.State?.IsFinallyDead ?? false` — **not** `State.IsDead` (true for down-but-auto-recovering allies on Normal). ([deep-dive](docs/wrath-api-deep-dive.md#unitstateisdead-vs-isfinallydead))
- **Targeting-relation primitives**: `unit.Commands.Standard?.TargetUnit` (engine current command target) + `unit.CombatState.EngagedUnits` (returns `KeyCollection`, NOT Dictionary — `.Contains(victim)` via LINQ). Centralized in `Engine/TargetingRelations.Has(attacker, victim)`. Approach-phase units match neither — accepted blind spot, latency ≤1 tick. ([deep-dive](docs/wrath-api-deep-dive.md#targeting-relation-primitives))
- **Summoned-creature detection**: `unit.Get<UnitPartSummonedMonster>() != null`. Does NOT cover pets / animal companions / Aivu / Eidolons (those carry `UnitPartPet`) — asymmetry is correct semantic for "limit summon spam" rules. ([deep-dive](docs/wrath-api-deep-dive.md#summon-detection))
- **Pet detection**: `unit.Get<UnitPartPet>() != null` (part lives on the *pet*, not the master). `PetType` enum: `AnimalCompanion=0`, `MythicSkeletalChampion=1` (Lich), `AzataHavocDragon=2` (Aivu), `Clone=3`, `NightHag=4`. Summoner Eidolons also carry `UnitPartPet`. Engine canonical probe is `ContextConditionIsAnimalCompanion` (`Get<UnitPartPet>` then `Type == 0`).
- **`GetHD()` vs `GetEffectiveHD()`**: `GetHD` = `Progression.CharacterLevel` only (used by `HitDice` — engine HD-cap rules exclude Mythic). `GetEffectiveHD` adds `MythicLevel` for `EnemyHDMinusPartyLevel` margin comparisons. **Don't unify.** `EnemyHDMinusPartyLevel` uses `Player.Party` (NOT `PartyAndPets`) — the one documented exception. ([deep-dive](docs/wrath-api-deep-dive.md#gethd-vs-geteffectivehd))
- **`ABMinusAC` condition**: enemy-scope-only. `Value2` is an optional ally pin (empty ⇒ party-best AB, cached rule-scoped; set ⇒ specific ally via `AllyProvider.Resolve`, uncached). Pattern for new computed-delta conditions: scope-check, read `CurrentOwner` from rule-scoped static, NaN→false on uncomputable, Trace log for thresholds. ([deep-dive](docs/wrath-api-deep-dive.md#abminusac-condition))
- **Use `Player.PartyAndPets`, never `Player.Party`** for active-group iteration (companions + pets). `Player.Party` excludes pets — symptom: "Pets don't get tactics tabs / aren't healed / don't count toward AllyCount". Regression check: `grep 'Player.Party'` before merge. Exception: `EnemyHDMinusPartyLevel`. ([deep-dive](docs/wrath-api-deep-dive.md#partyandpets))

## Release Process

Follow parent `wrath-mods/CLAUDE.md` §Release Process. Remote is `origin`. The `/release` slash-command (`.claude/commands/release.md`) runs the full flow: bump → build → user-confirm gate → push → tag → GitHub Release → Nexus upload (auto via `.github/workflows/nexus-upload.yml`) → Discord-post generation.

Nexus mod-page: https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005 (ID 1005, file_group_id 4191).

`deploy.sh` is **dev-only** — Debug build SCP'd to Steam Deck for smoke-testing. Release builds come from `/release`'s Release-config build → `WrathTactics/bin/WrathTactics-X.Y.Z.zip`.

## Logs

- **Mod session logs**: `<game>/Mods/WrathTactics/Logs/wrath-tactics-YYYY-MM-DD-HHMMSS.log` (separate from `Player.log`). Latest: `ssh deck-direct "ls -t '<game>/Mods/WrathTactics/Logs/' | head -1"`.
- **`"MATCH but action not executable"` is the triage smoking gun for "Rule didn't fire" reports**: rule matched, target resolved, but `ActionValidator.CanExecute` rejected — most commonly `"No suitable spell slots"`. Grep for it BEFORE asking the user for setup details — most reports resolve to "spell not prepared".
- **For CastSpell rules also grep `engine-unavailable` and `no available slots`** (TRACE-level): fired from `FindCastSpellSource` when an ability matched but isn't currently castable. `engine-unavailable` carries `GetUnavailableReason()` (silenced, polymorphed, opposition school, UMD fail); `no available slots` carries the variant GUID and metamagic mask.

## Code Style

- K&R brace style (opening brace on same line)
- 4-space indentation
- `var` when type is apparent
- **Partial-class file split for fat engine files**: `ActionValidator` is `partial` across `ActionValidator.cs` (top-level `CanExecute` dispatcher), `.Cast.cs`, `.UseItem.cs`, `.Toggle.cs`, `.Heal.cs`, `.Find.cs` — each file owns one Action-type's worth of `Can*` / `Find*` methods. When adding a new Action-type: new file `ActionValidator.<Type>.cs`, `partial class ActionValidator`, file-local `using`s. Don't merge back — it grew to 902 LOC once.
- **`catch (Exception ex)` is reserved for three patterns**: per-tick/per-frame guards (Unity main-thread protection), user-surface persistence (status-line surfaces), and static/sentinel blueprint init. Everything else narrows to a specific exception type. ([deep-dive](docs/wrath-api-deep-dive.md#catch-discipline))
- UI strings are English-only. No mixed-language — use `Yes`/`No`, `!=`, etc.
- Equality conditions use inline `=`/`!=` operator dropdowns. Extend the operator pattern to new properties (HasBuff, HasCondition, CreatureType, Alignment) rather than adding a perpendicular Negate/NOT button.
- **`.i18n()` falls back to en_GB, then to the raw key** (`Localization/Strings.cs`): en_GB is the mandatory pack; missing keys in other locales degrade gracefully to English. A new key needs en_GB at minimum — other locales are optional (untranslated → English).
- **i18n math-notation properties are LOCALIZED per locale** — never paste en-GB into all 5 files. Existing rows use locale-native abbreviations: de `AB − RK` / `SG − Rettung` / `TW − Gruppe`, fr `BAB − CA` / `DD − Sauvegarde` / `DV − Groupe`, ru `БА − КБ` / `СЛ − Спасбросок` / `КЗ − Группа`, zh mixes native (`生命骰`, `豁免`) with kept-English (`AB − AC`). Copy from existing `ABMinusAC` row in each locale.
