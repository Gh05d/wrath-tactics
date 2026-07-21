# Engine: Evaluator, Validator, Executor, Trackers

Operative rules for `TacticsEvaluator`, `ActionValidator`, `CommandExecutor`, `PlayerCommandGuard`, `ActiveRuleTracker`, activatables, and blueprint infrastructure. IL evidence and incident reports: [`../docs/wrath-api-deep-dive.md`](../docs/wrath-api-deep-dive.md).

## Commands & Trackers

- **Never call `owner.Commands.Run` directly in `CommandExecutor` — route through `RunVerified`**: the engine can silently DISCARD the command instead of slotting it (`CanRunCommand` veto on unconscious/`CantUseStandardActions`, or `TryMergeInto` folding a same-Ability `UnitUseAbility` into a running `PreviousCommand`). A discarded command stays `IsStarted=false`/`IsFinished=false` forever — tracking it wedged `ActiveRuleTracker`'s gate until combat end (1.17.4 "rules randomly stop firing" bug). `RunVerified` checks `Commands.Raw` slot residency and returns the live in-flight command (merged occupant on merge, null on veto).
- **`PlayerCommandGuard`** reference-tracks `Commands.Run` from `CommandExecutor`; gates eval on foreign active casts. Scope is **intentionally narrow**: Standard slot only, `UnitUseAbility` class only, AND filtered against `unit.Brain.AutoUseAbility.Blueprint`. Don't widen — each constraint exists to fix a specific over-block regression. ([deep-dive](../docs/wrath-api-deep-dive.md#playercommandguard-scope))
- **`ActiveRuleTracker` (DAO priority gate)**: tracks `(RuleListSource, entry.Id, UnitCommand)` per-unit; while tracked command is `!IsFinished`, `TryExecuteRules` iterates only strictly higher-priority rules. Separate from `PlayerCommandGuard` (foreign casts vs. own commands). Toggles / `ThrowSplash` / `DoNothing` don't replace the entry — `Clear`-on-any-success reintroduces the lower-priority-preempt bug. ([deep-dive](../docs/wrath-api-deep-dive.md#active-rule-tracker))
- **Rule priority = list/array position** (top-down eval in `TacticsEvaluator.TryExecuteRules`). There is NO `Priority` field — it was vestigial (set at creation, never read) and removed; `RuleEditorWidget.MoveRule` reorders the array, which IS the eval order. UI display order + session-log "Rule N" both equal the array index.

## Validation & Execution

- **Validator strictness is load-bearing**: new `ActionType` must validate up-front in `ActionValidator.CanExecute` (incl. `AbilityData.IsAvailable`). Chain-capable types (CastSpell, CastAbility) MUST route through `ResolveCastSpellChain` — single-GUID validation misses the chain. ([deep-dive](../docs/wrath-api-deep-dive.md#validator-strictness))
- **Cast fallback chain spans CastSpell + CastAbility (since 1.15)**: `ResolveCastSpellChain` is the authoritative validator + executor entry point for both Cast types — never re-introduce single-GUID validation on either branch (breaks rule fall-through when primary unavailable). ([deep-dive](../docs/wrath-api-deep-dive.md#cast-fallback-chain))
- **`ResolvedTarget` wrapper**: `TargetResolver.Resolve` returns a struct (Unit XOR Point, both null = `None`). `ActionValidator.CanExecute` and `CommandExecutor.Execute` branch on `IsPoint`. `ResolvedTarget.None` fails validation — no silent self-cast fallback.
- **`TargetWrapper` dual ctors**: `(UnitEntityData)` for unit, `(Vector3 point, float? orientation = null, UnitEntityData unit = null)` for point. `UnitUseAbility.CreateCastCommand(AbilityData, TargetWrapper)` takes either.
- **Cast fires once per round, AttackTarget full-attacks**: `CastSpell`/`CastAbility` issue `UnitUseAbility` — a standard action, ONE activation per command. `AttackTarget` issues `new UnitAttack(target, null)` — the same command class the engine's auto-engage uses, i.e. a full attack with iteratives. Kinetic blasts are weapons under the hood: blast as in-game default attack + rule action AttackTarget ⇒ ~2 blasts/round at BAB 6+ (user-corroborated, Nexus v1.25.0 thread; not IL-traced end-to-end). Triage: "ability only fires once per round" reports → recommend AttackTarget, not Cast.

## Activatables

- **ActivatableAbility API**: has `TryStart()` but NO `TryStop()`. Deactivate via `IsOn = false`.
- **Target-aware `ActivatableAbility`s exist** (Mount is canonical): `TryStart()` sets `IsWaitingForTarget=true`, then engine expects a target-unit click. Current `ToggleActivatable` action only handles self-targeted toggles and CANNOT drive Mount end-to-end. ([deep-dive](../docs/wrath-api-deep-dive.md#mount-ability))

## Tick & Timing

- No per-round EventBus events in RTWP mode — use `Game.Instance.Player.GameTime` in `Update()`.
- **Continuous out-of-combat tick (since 1.7.0)**: `TacticsEvaluator.Tick` runs in both states. Out-of-combat interval = `TacticsConfig.OutOfCombatTickIntervalSeconds` (default 2 s, JSON-only), pre-filtered through `RuleEnabledOutOfCombat`. Cooldown clock = `CooldownRounds * 6f` against `GameTime.TotalSeconds`; same clock in RTWP and turn-based. ([deep-dive](../docs/wrath-api-deep-dive.md#continuous-out-of-combat-tick-since-170))

## Blueprint Infrastructure & Misc

- **Blueprint enumeration**: `ResourcesLibrary.s_BlueprintsBundle` doesn't exist (binary pack, not AssetBundle). Use `ResourcesLibrary.BlueprintsCache.ForEachLoaded((guid, bp) => ...)`. **`ForEachLoaded` is misnamed**: it iterates the FULL index (`m_LoadedBlueprints`, ~236k entries) and passes `entry.Blueprint` **with no null filter** (verified IL) — `bp` is `null` for any blueprint not yet lazily loaded. So a `bp is BlueprintBuff` check silently skips every unloaded buff; callers see only the session's already-touched subset. To list ALL of a type, force-load first: iterate `m_LoadedBlueprints.Keys` + `BlueprintsCache.Load(guid)` (publicizer-accessible) — but that's ~236k loads (~79s, all resident; eviction via `RemoveCachedBlueprint` is destructive — drops the index Offset). The buff picker pays this once and persists metadata to disk. ([deep-dive](../docs/wrath-api-deep-dive.md#blueprint-full-enumeration))
- **Unit facing**: `UnitEntityData.OrientationDirection` is a public `Vector3` returning forward vector.
