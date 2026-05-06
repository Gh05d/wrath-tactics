# Wrath Tactics — API Deep Dive

Long-form companion notes to the compact rules in `CLAUDE.md`. IL evidence, version history, predecessor patterns, and incident reports live here.

> **Read CLAUDE.md first.** This document is the "why" + "how I verified it" backing for the rules over there. Anchors below match the deep-dive links in CLAUDE.md.

---

## Gotchas (Mod Internal)

### `validator-strictness`

`CommandExecutor.Execute` returns `true` as soon as the command is queued — but the game may silently drop the cast later. Reasons for silent drop fall into four buckets:

1. **No slot available** — spellbook prepared/spontaneous slot count is 0 (see `GetAvailableForCastSpellCount` notes below).
2. **No resource available** — `AbilityResourceLogic.CalculateCost` returns more than the unit has on `UnitDescriptor.Resources`.
3. **Caster restriction fails** — `Blueprint.CasterRestrictions[]` (e.g. `AbilityCasterInCombat{Not=true}` for out-of-combat-only abilities, `AbilityCasterHasFacts`, forbidden-spellbook gates).
4. **`UnitState.CanCast == false`** — silenced, stunned, polymorph-restricted, tactical-combat mode mismatch.

`AbilityData.IsAvailable` composes all four into a single check: `IsAvailableInSpellbook && IsAvailableForCast && !TemporarilyDisabled`. `IsAvailableForCast → CanBeCastByCaster` is the one that iterates `CasterRestrictions[]` and reads `UnitState.CanCast`.

When `CommandExecutor.Execute` returns `true` after queueing a doomed cast, `TacticsEvaluator.TryExecuteRules` also returns `true` and **blocks fall-through to backup rules**. Result: infinite loop where the rule "fires" every tick but nothing happens.

**Rule for new `ActionType`**: validate everything up front in `ActionValidator.CanExecute`. Slots/resources are a *subset* of availability — `IsAvailable` is the safer gate.

See also: [Prestige Plus Auto Heal incident](#prestige-plus-auto-heal).

### `hasclass-encoding`

`Condition.Value` for `ConditionProperty.HasClass` is a prefixed string with two namespaces:

- `group:<spellcaster|arcane|divine|martial>` — group buckets
- `class:<InternalName>` — specific class, where `<InternalName>` is `BlueprintCharacterClass.name` with the `Class` suffix stripped (e.g. `class:Wizard`, `class:Lich`, `class:Trickster`)

**Never store the localized display name.** `BlueprintCharacterClass.name` is the code identifier and locale-independent; the display name changes per locale.

`ClassProvider.GetAll()` is the single source of truth for the dropdown list. `UnitExtensions.MatchesClassValue` is the single matching helper. New tradition flags or class buckets go there, not in scattered switch statements.

Group resolution:
- `spellcaster` = `unit.Spellbooks.Any()`
- `arcane` = `unit.Progression.Classes.Any(c => c.CharacterClass.IsArcaneCaster)`
- `divine` = `unit.Progression.Classes.Any(c => c.CharacterClass.IsDivineCaster)`
- `martial` = any class that is neither arcane, divine, nor mythic

Note: `BlueprintSpellbook.IsArcane` exists but there's NO symmetric `IsDivine` field. Derive divine from the class flag.

### `withinrange-encoding`

`Condition.Value` for `ConditionProperty.WithinRange` is the bare `RangeBracket` enum name (`"Melee"`, `"Cone"`, `"Short"`, `"Medium"`, `"Long"`).

The UI dropdown shows distance-hint labels (`"Cone (≤5 m)"`) but persists the bare name. Localizing the dropdown changes labels only — values stay locale-independent.

Fixed meter thresholds in `RangeBrackets`:
- `MaxMeters`: Melee=2, Cone=5, Short=10, Medium=20, Long=40
- `LowerMeters`: previous bracket's upper bound (Melee=0, Cone=2, Short=5, ...)

Operator semantics (strict-bracket):
- `= X` means `lower(X) < d ≤ upper(X)` — exactly within the bracket
- `<= X` cumulative: "within X or closer"
- `>= X`: "at X or farther"
- `<` / `>`: strict before/past the bracket boundary

**Common mistake**: For "within 10 m" intent, use `<= Short`, not `= Short`. The latter excludes anything ≤ 5 m.

### `playercommandguard-scope`

`Engine/PlayerCommandGuard.cs` (since 1.5.1) reference-tracks every `Commands.Run` issued from `CommandExecutor` and gates `TacticsEvaluator.EvaluateUnit` on detecting a **foreign** active cast.

Scope is intentionally narrow:
- **Standard slot only**
- **`UnitUseAbility` class only**
- **AND** filtered against `unit.Brain.AutoUseAbility.Blueprint`

Each constraint exists to fix a specific over-block regression that landed in pre-1.5.1 versions:

1. **Why Standard-only, not Move/Swift/Free**: The engine fills `Move` slot with auto-approach `UnitMoveTo` for any engaged unit. Checking Move would block tactics every tick a companion is moving toward an enemy. (Also note: `Move` self-nulls finished commands in its accessor; `Standard`/`Free`/`Swift` return raw `m_Commands[i]` — asymmetric.)
2. **Why `UnitUseAbility`-only, not all `UnitCommand`**: The engine fills `Standard` with auto-attack `UnitAttack` for any engaged unit. Checking non-`UnitUseAbility` would block tactics during normal melee.
3. **Why filter against AutoUseAbility blueprint**: The engine re-fires the player's right-click default action (e.g. Ember casting Magic Missile on auto-repeat) through the SAME `Commands.Run(UnitUseAbility)` path as a manual click. Without this filter, any caster character with a default action configured would deadlock the whole tactics system.

**Trade-off the current scope accepts**: player-issued attacks/moves and re-clicks of the same default action can still get preempted by tactics. Only spell casts that *differ* from the configured default action are protected. Matches the Nexus-reported complaint ("midcast" cancellation) and DAO-style priority semantics.

`UnitCommand.IsRunning = IsStarted && !IsFinished` — use this for "is slot occupant still active?", not `!IsFinished` alone. Queued-but-not-started commands have BOTH flags false; an `!IsFinished`-only filter incorrectly treats them as active.

`UnitCommand` has no source-tagging field. IL fields are `Executor`/`Target`/`Type`/`Result`/`IsStarted`/`IsFinished`/`DidRun`/`IsActed`/`ReactionAction`. No `IsPlayerIssued`/`Source`/`Initiator`. Mod-issued, player-issued, and engine-auto-issued commands all flow through the same `Commands.Run(cmd)` path. **Reference-tracking** (store the `UnitCommand` instance you passed to `Run`) is the only way for a mod to identify its own commands.

### `verify-deploy`

When the user reports a behavior bug after a code change, the FIRST diagnostic step is:

```bash
ssh deck-direct "stat -c '%y' '<game>/Mods/WrathTactics/WrathTactics.dll'"
ls -l WrathTactics/bin/Debug/WrathTactics.dll
```

If the deck DLL is older than the local source change, `./deploy.sh` wasn't run and the fix literally isn't on the system under test.

This is the **inverse** of the "Phantom log lines on deck" gotcha:
- Phantom logs: deck has *stale instrumentation* the source no longer has
- Verify-deploy: deck is *missing fresh code* the source has

**Cost of skipping this check** (real incident): a session was burned IL-decompiling `Player.UpdateCharacterLists` to investigate why pets weren't appearing in tabs, when the actual cause was a 3-day-old deck DLL with no pet support code at all.

Related gotcha: `dotnet build` has been observed to skip rebuild on source-mtime miss — returned "Build succeeded" without recompiling edited sources (DLL mtime stayed at a previous build). If `./deploy.sh` ran but the fix still doesn't land, compare `ls -l bin/Debug/*.dll` vs. source mtime; `touch <modified>.cs` and rebuild to force.

### `linked-rules-empty-body`

`AddFromPreset` and `PromoteRuleToPreset` produce rules where only `PresetId` is set; `ConditionGroups`/`Action`/`Target` are left as empty defaults because `PresetRegistry.Resolve(rule)` substitutes the preset's body at runtime.

**Cleanup/validation passes** that walk `CharacterRules`/`GlobalRules` MUST exempt rules with `!string.IsNullOrEmpty(r.PresetId)`, or they strip legitimate preset assignments on every `ConfigManager.Load`.

**Incident**: 0.6.0–0.6.2 had `RemoveRulesWithNoGroups` wiping all preset assignments on reload. Fixed in 0.6.3 by adding the `PresetId` exemption check. Before treating "empty body" as "invalid rule", check `PresetId`.

### `unitcondition-three-sites`

Adding a new `UnitCondition` to the HasCondition picker requires THREE sites synced:

1. `ConditionEvaluator.HasConditionByName` switch — lowercase string-key match against `condition.Value` (the lookup runs `.ToLowerInvariant()`)
2. `EnumLabels.KeysForCondition` — PascalCase key list driving the dropdown
3. One i18n entry per locale: `enum.condition.<PascalCase>` in all 5 locale files (`en_GB`, `de_DE`, `fr_FR`, `ru_RU`, `zh_CN`)

Failure modes if any site is missed:
- Switch missing: dropdown lists the row but selection silently always returns false
- `KeysForCondition` missing: row is invisible in the dropdown
- i18n missing: row shows the raw `enum.condition.X` key as label

All three must match in casing convention (PascalCase keys, lowercase switch cases).

### `subject-scope-classification`

When adding a new `ConditionSubject`, classify it in `IsEnemyScope` / `IsAllyScope` (`ConditionEvaluator.cs`) or it routes through the legacy per-condition path and silently bypasses the same-unit AND fix.

Since 0.11.0, `EvaluateGroup` buckets conditions by scope and enforces same-unit AND within each bucket. Skipping classification means new Enemy/Ally-scoped subjects don't participate in the bucketing — multiple conditions iterate independently and can pass on unrelated units.

Also extend `PickMetric` if the new subject is a sort-pick (EnemyLowest* / EnemyHighest* family).

The legacy methods (`EvaluateEnemy`, `EvaluateEnemyPick`, etc.) remain in the file but are unreachable from the bucketed path. Don't extend them — extend the bucket evaluators.

---

## Game API Gotchas

### `item-consumption`

For inventory item consumption (potions, scrolls, wands, splash items):

**Always use `item.SpendCharges(caster.Descriptor)`** — the engine-authoritative method (same one `AbilityData.Spend()` calls on Path A). It:

- Handles Wand / Potion / Scroll uniformly
- Removes 0-charge wands from inventory (the `Charges--`-only path leaves a dead wand stuck forever)
- Respects `IsSpendCharges=false` and `RestoreChargesOnRest` flags
- Honors Trickster-UMD / Hand-of-Magus bypass features

Hand-rolled alternatives are **wrong**:

- `item.Charges--` only works for `UsableItemType.Wand`. For stacked Utility items (Alchemist's Fire etc.) where `Charges=1`, decrementing underflows. `IsSpendCharges=True` is per-instance for these.
- `Game.Instance.Player.Inventory.Remove(item, 1)` works for Potion/Scroll/Other but bypasses the wand cleanup and the bypass-features.

`Rulebook.Trigger(RuleCastSpell)` does NOT consume — verified in `RuleCastSpell.OnTrigger` IL — so there's no double-consume risk when manually calling `SpendCharges` after.

### `synthetic-abilitydata`

Inventory items have synthetic AbilityData → `UnitUseAbility.CreateCastCommand` silently drops them. Two paths:

1. **`Rulebook.Trigger<RuleCastSpell>`** — fires effect FX at target but no throw/drink animation
2. **Quickslot path** — if the item is registered in `owner.Abilities.RawFacts` with `SourceItem != null` (i.e. assigned to a quickslot), `CreateCastCommand` works and you get the throw/drink animation

The quickslot path is preferred when available.

`AbilityData.Spend()` (IL-authoritative): in order runs `SpendMaterialComponent` → `SourceItem.SpendCharges(Caster)` if non-null → `SpendFromSpellbook` → `AbilityCastRateUtils.SpendResources`. Wand charges decrement automatically when cast via `UnitUseAbility.CreateCastCommand` on a quickslot-registered AbilityData. Synthetic AbilityData (inventory scrolls/potions without `SourceItem`) needs manual `ConsumeInventoryItem` / `SpendCharges` invocation.

**Dual-pool inventory scan for UseItem**:

`ActionValidator.FindUseItemSource` and `SpellDropdownProvider.GetItemAbilities` scan BOTH:
- `owner.Abilities.RawFacts` (equipped wands/scrolls)
- `Game.Instance.Player.Inventory` (general-pool potions/scrolls)

Older versions only scanned RawFacts, so Potion-of-X rules never found anything when the potion sat in the party stash. Any new action over ability-facts must make the same dual decision: fact-only (Toggle, where quickslot semantics matter) or fact+inventory (UseItem/Heal/anything that mirrors Wrath's "drink from stash" UX).

**Dedup is per-(ability-GUID, item-type), not per-GUID alone**:

Potion of X and Scroll of X share an ability blueprint GUID. Naïve GUID-dedup in storage-iteration order silently drops whichever came second (typically the potions — Wrath inventories lean scroll-heavy).

Fix: two-pass inventory scans — potions first (no UMD gate, no silence gate, CL1 but reliable), scrolls second — and apply the same ordering in BOTH `SpellDropdownProvider.GetItemAbilities` AND `ActionValidator.FindUseItemSource` so the displayed source equals the consumed source.

### `classifyheal`

`ClassifyHeal` (since 1.3.0; replaces `IsHealingSpell`) returns `HealEnergyType.{Positive, Negative, None}` via two substring tables.

**Positive table**: `cure`, `heal`, `lay on hands`, `channel positive`, DE `wunden heilen`, `heilung`, `auflegen`

**Negative table**: `inflict`, `harm`, `channel negative`, DE `wunden zufügen`, `negative energie`

**Negative is checked first** for stability. Pre-1.3.0 the bare `wunden` keyword was harmless because Inflict was never searched; once Negative-energy detection went live, `wunden` had to split into `wunden heilen` / `wunden zufügen` to disambiguate Cure from Inflict on the DE client.

Known imprecision (kept):
- `cure` still matches Cure Disease / Cure Deafness / Neutralize Poison. UMD-gate (for scrolls) limits mis-casts.
- `restoration` is intentionally absent (1.2.0 fix — was matching Lesser/Greater Restoration which aren't HP heals).

Before adding keywords, verify they match ONLY HP-heals of the right energy type. Component-based detection (`ContextActionHealTarget` walk) would be more correct but remains out of scope.

### `variant-ctor-bug`

The 2-arg variant ctor `new AbilityData(parent, variant)` silently drops `SpellLevelInSpellbook` — the engine field that anchors a memorized/spontaneous spell to its book level.

The ctor delegates to the 4-arg base `(blueprint, caster, fact, spellbookBlueprint)` which never sets `SpellLevelInSpellbook`; only the explicit 3-arg `(blueprint, Spellbook, level)` ctor does. The 2-arg ctor sets:
- `m_ConvertedFrom = parent`
- `Blueprint = variant`
- `m_SpellbookBlueprint`

…but the level backing field stays null.

**Consequence chain**:
1. `Spellbook.GetSpellLevel(variantData)` falls through to `GetMinSpellLevel(variant.Blueprint)`
2. Variant blueprints aren't in `m_KnownSpellLevels` (only parents are) → returns -1
3. `GetAvailableForCastSpellCount` short-circuits on level<0 → returns 0
4. Slot-count gate fails → cast blocked

**Affected ability families**: spellbook-spell variants ALL break — Greater Spell Dispelling, Create Undead, Command/Halt, Plague Storm/Bubonic, etc.

**Unaffected**: class-ability variants (Evil Eye, Lay on Hands variants) — their `Spellbook==null` path skips the gate entirely.

**Fix**: After `new AbilityData(parent, variant)`, copy the level:

```csharp
data.SpellLevelInSpellbook = parent.SpellLevelInSpellbook; // publicizer-exposed setter
```

The mod centralizes this in `ActionValidator.MakeVariantData`.

**`IsAvailable` is NOT a workaround** — it composes `IsAvailableInSpellbook` which DOES recurse via `m_ConvertedFrom`, but the slot-count check uses `GetAvailableForCastSpellCount` directly and bypasses that recursion.

### `mount-ability`

Target-aware `ActivatableAbility`s exist — `BlueprintRoot.SystemMechanics.MountAbility` is the canonical example.

**Engine flow** (verified at IL `SaddledUnitController` ~111051):
1. `ability.TryStart()` puts the ability into `ability.IsWaitingForTarget == true`
2. Engine then expects a target-unit click to complete activation
3. The engine reads `IsWaitingForTarget` and dispatches a click via `ActionsState.Use`

The mod's current `ToggleActivatable` action only handles **self-targeted** toggles (Power Attack, Combat Expertise, Smite Evil etc.) and **CANNOT drive Mount end-to-end** — calling `TryStart` leaves the ability stuck waiting for a target that never arrives.

A future "MountPet" action (or `ToggleActivatable` target-extension) would need:
1. `TryStart`
2. Verify `IsWaitingForTarget`
3. Set the target via the engine's click-handler path (specific API still TBD — recheck IL)
4. Gate via `AbilityTargetIsSuitableMount.CanMount(rider, pet)` + `AbilityTargetIsSuitableMountSize.CanMount`

### `dynamic-save-type`

`BlueprintAbility.GetComponent<AbilityEffectRunAction>()?.SavingThrowType` is **NOT authoritative**.

The game's resolver `AbilityEffectRunAction.GetSavingThrowTypeInContext` (IL-visible) returns:

```csharp
ability.MagicHackData?.SavingThrowType
  ?? blueprint.GetComponent<AbilityEffectRunAction>()?.SavingThrowType
  ?? Unknown
```

Magic Deceiver fused spells and other hack-altered casts carry their live save type on the `AbilityData`, not the blueprint. **Mirror the precedence exactly.** Missing the MagicHack branch causes fused-spell rules to silently return `Unknown` → NaN → cast skipped.

Related: the 2-arg variant ctor sets `data.Blueprint = variant`, so `GetComponent<X>` on the variant data sees only variant-level components. If the effect/save lives on the parent blueprint, fall back via `ability.m_ConvertedFrom?.Blueprint` (publicizer-accessible private field).

### `isavailable`

`AbilityData.IsAvailable` is the authoritative "can cast right now?" check. Composes:

```
IsAvailableInSpellbook && IsAvailableForCast && !TemporarilyDisabled
```

**`IsAvailableInSpellbook`**: spell is known/memorized in a real spellbook (recurses via `m_ConvertedFrom` for variants).

**`IsAvailableForCast → CanBeCastByCaster`** iterates `Blueprint.CasterRestrictions[]`:
- `AbilityCasterInCombat{Not=true}` — out-of-combat-only abilities (Auto Heal, repair tools)
- `AbilityCasterHasFacts` — needs a feature/buff
- Forbidden-spellbook gates
- `UnitState.CanCast` — silenced, stunned, polymorph rules
- Tactical-combat mode mismatches
- UMD checks (for scroll users)

Slots and resource-cost are separate subsystems that `IsAvailable` *also* folds in.

`GetUnavailableReason()` returns the localized UI tooltip string when the ability is greyed out — useful for debug logs.

**Use `IsAvailable` in ANY candidate-enumeration** over `owner.Abilities.RawFacts` or spellbook spells where a picked candidate is later cast via `UnitUseAbility.CreateCastCommand` / `Rulebook.Trigger<RuleCastSpell>`. Without this filter, abilities the engine would grey out still get queued, silently drop at execution, and block rule fall-through.

### `prestige-plus-auto-heal`

Auto Heal (solo `C02FA1FA-25CE-46FF-802A-21B9D7BDA125`, group `46B4F85D-5E83-4383-A7E1-09275454EC44`) is an `AbilityType.Special` with no spellbook and no `AbilityResourceLogic`, injected globally via `FeatureRefs.SkillAbilities`. Carries `AbilityCasterInCombat{Not=true}`.

**Pre-fix behavior**: `FindBestHealEx` matched it as a class-ability healing candidate (priority 80), picked it as the best in-combat heal, the engine silently refused the cast (caster restriction fails — out of combat only), and the Heal rule blocked fall-through. User-reported symptom: "Heal orders not executing, then characters top-heal right after combat ends."

**Fix**: `IsAvailable` filter at each candidate-enumeration site:
- `FindBestHealEx`
- `FindCastSpellSource`
- `CanCastSpell`
- `CanCastAbilityAtPoint`

This is the canonical example of why [validator-strictness](#validator-strictness) requires `IsAvailable` checks (not just slot/resource counting).

### `partyandpets`

**Use `Player.PartyAndPets`, never `Player.Party`** for any iteration that should cover companions and their pets.

Affected: animal companions, Aivu, Eidolons, mod-added pets like ExpandedContent's Drake or Homebrew's Undead Wizard.

`Player.Party` excludes pets implicitly. Symptom of the bug: "Pets don't get tactics tabs / aren't healed / don't count toward AllyCount."

**`PartyAndPets` is engine-maintained**:
1. `get_PartyAndPets` calls `Player.UpdateCharacterLists()` first
2. `UpdateCharacterLists` walks `PartyCharacters` + `CrossSceneState.AllEntityData`
3. Each unit gets dispatched into `m_Party` / `m_PartyAndPets` / `m_RemoteCompanions` via `AddCharacterToLists`

A pet lands in `m_PartyAndPets` iff:
- `unit.Master.UnitPartCompanion.State == Active` (master in active 6-slot party)
- `unit.IsInGame == true`

Pets of reserve-bench masters do NOT appear — that's correct (they're not on the field).

**All 9 iteration sites in this codebase use `PartyAndPets`**:
- `TacticsEvaluator` combat tick + post-combat
- `TargetResolver` allies
- `ConditionEvaluator` ally-scope helpers
- `TacticsPanel` tab list + name lookup

Adding a new iteration over the active group must follow the same convention. Single regression check: `grep 'Player.Party'` before merge.

**Documented exception**: `EnemyHDMinusPartyLevel` uses `Game.Instance.Player.Party` (NOT `PartyAndPets`) — pets have separate progression curves and would skew the max for parties with high-HD Eidolons/Drakes; the calculation should reflect the player squad's level only.

**Alternative pattern**: BubbleBuffs uses `unit.Get<UnitPartPetMaster>().Pets` expansion. Equivalent for the active-party case.

---

## Engine Internals (Verified References)

Stable engine APIs that the mod relies on. Non-obvious behavior verified by IL inspection.

### `AbilityData.Spend()`

IL-authoritative call order:

1. `SpendMaterialComponent`
2. `SourceItem.SpendCharges(Caster)` if non-null
3. `SpendFromSpellbook`
4. `AbilityCastRateUtils.SpendResources`

Wand charges decrement automatically when cast via `UnitUseAbility.CreateCastCommand` on a quickslot-registered AbilityData (because Path 2 runs).

### `UnitCommands` slot accessors

| Slot | Behavior |
|------|----------|
| `Standard` / `Free` / `Swift` | Return raw `m_Commands[i]` — may linger as `IsFinished=true` until the next `Run` replaces |
| `Move` | Self-nulls finished commands in its accessor (asymmetric — don't assume parity) |

`Commands.Run(cmd)` unconditionally interrupts the existing slot occupant and replaces (no queue-on-occupied path).

**Player click-to-attack flow**: distant enemy click lands `UnitMoveTo` in `Move` first (approach phase), then `UnitAttack` in `Standard` once in range. A Standard-only check misses approach-phase intent.

### `unit.Brain.AutoUseAbility`

The player's right-click "default action" (e.g. Ember casting Magic Missile on auto-repeat). The engine re-fires it via the SAME `Commands.Run(UnitUseAbility)` path as a manual click — class+slot identical, no source flag.

Distinguish by blueprint match: if a slot's `UnitUseAbility.Ability.Blueprint == unit.Brain.AutoUseAbility.Blueprint`, it's the engine repeating the default action.

Without this filter, any mod that gates on "is something foreign in the slot?" deadlocks for caster characters with default actions configured. See [playercommandguard-scope](#playercommandguard-scope).

### `UnitState.IsDead` vs `IsFinallyDead`

| Property | Definition | Use for |
|----------|------------|---------|
| `IsDead` | `LifeState == Dead` | (rarely useful directly) |
| `IsFinallyDead` | Persisted bool, paired with `CompanionState.Dead` | "Greyed-portrait / Raise-Dead-territory" |

`UnitLifeState`: `Conscious=0, Unconscious=1, Dead=2`.

On Normal difficulty, `LifeState==Dead` fires whenever a companion's HP drops past `-CON` during combat, even though the companion will auto-revive at combat end (red portrait, `IsFinallyDead=false`).

**Use `State.IsFinallyDead` for rez-only semantics**; `State.IsDead` is too permissive (also true for downed-but-auto-recovering allies).

`HPLeft <= 0` ALSO matches `Unconscious` (Death's Door) — not what you want either.

### `EngagedUnits` collection type

`unit.CombatState.EngagedUnits` returns `Dictionary<UnitEntityData, TimeSpan>.KeyCollection`, **NOT a Dictionary**. Query via LINQ `.Contains(victim)` since `KeyCollection` has no `ContainsKey`.

For O(1) lookups, the backing `m_EngagedUnits` is publicizer-accessible.

### `Spellbook` storage layout

Spellbook stores spells in three parallel arrays:

| Array | Source | Mythic? |
|-------|--------|---------|
| `m_KnownSpells` | Standard class progression | Both |
| `m_CustomSpells` | Items, scrolls converted to spellbook | Both |
| `m_SpecialSpells` | `AddSpecialSpellList` factlogic (Cleric Domain, Sorcerer Bloodline, Witch Patron, Shaman Spirit) | Non-mythic only |

Mythic-side special lists go to `m_KnownSpells` instead. The split is exclusively non-mythic.

Any enumeration / lookup MUST iterate `book.GetSpecialSpells(level)` alongside `GetKnownSpells` / `GetCustomSpells`. Owlcat's own `SpellBookView` always reads all three.

**Slot accounting is unified**: prepared special spells live in the regular `m_MemorizedSpells` with `SpellSlot.Type == Special`, so `GetAvailableForCastSpellCount` already counts them — only the *spell list* is split.

### `GetAvailableForCastSpellCount` cantrip sentinel

Returns `-1` for cantrips (level 0), unconditional engine sentinel — IL: `level==0` → `ldc.i4.m1; ret`.

| Return | Meaning |
|--------|---------|
| `-1` | Cantrip (level 0) — always castable |
| `0` | No slot or spell-not-in-book |
| Positive | Remaining prepared/spontaneous slots |

Validators must compare against `== 0` (fail) and `!= 0` (pass), never `<= 0` / `> 0`. Treating `-1` as "no slots" silently rejects every cantrip rule even though manual casting works fine.

WotR convention is consistent across spontaneous and memorize spellbooks.

### `AbilityData` constructor matrix

| Ctor | Use case |
|------|----------|
| `(BlueprintAbility, UnitDescriptor)` | Generic synthesized data |
| `(Ability)` | From an existing Ability fact |
| `(BlueprintAbility, Spellbook, int level)` | Spellbook spell at known level |
| `(AbilityData parent, BlueprintAbility variant)` | `AbilityVariants` family — Command → Halt/Prone, Plague Storm → disease variants, Evil Eye → AC/Attack/Saves. Works for spellbook spells AND class abilities. |

No 3-param `(blueprint, descriptor, ItemEntity)` exists — use 2-param + `OverrideCasterLevel` / `OverrideSpellLevel`.

**Variant ctor caveat**: see [variant-ctor-bug](#variant-ctor-bug).

### Continuous out-of-combat tick (since 1.7.0)

`TacticsEvaluator.Tick` runs in both states.

| State | Interval | Source |
|-------|----------|--------|
| In-combat | `TacticsConfig.TickIntervalSeconds` | UI-configurable |
| Out-of-combat | `TacticsConfig.OutOfCombatTickIntervalSeconds` (default 2 s) | JSON-only |

Out-of-combat ticks pre-filter rules through `RuleEnabledOutOfCombat(rule)` — only rules carrying a `Combat.IsInCombat==false` condition somewhere in their `ConditionGroups` pass the gate. Rules without that condition stay in-combat-only (backwards-compat with all pre-1.7.0 rules and seeded defaults).

Cooldowns operate purely in real-time across the combat boundary — no `cooldowns.Clear()` on combat-end any more, so a heal that fired in the last second of combat won't immediately re-fire on the cleanup tick.

**Predecessor mechanism** (`IsPostCombatPass` single-pass + `RunPostCombatCleanup` + `TryExecuteRulesIgnoringCooldown`) has been removed entirely; do not look for it in IL.

### `RuleCalculateAttackBonusWithoutTarget`

`(UnitEntityData, ItemEntityWeapon, int penalty)` — engine-authoritative full-AB computation for a unit with a given weapon, minus target-side factors (no flanking, no bane).

Returns `.Result` as int. Includes:
- BAB
- Stat-mod (correct per weapon type — finesse/ranged uses Dex)
- Weapon enhancement
- Feats (Weapon Focus etc.)
- Active buffs (Bless, Prayer, Haste, Inspire Courage)

Cheap — no random rolls, no side effects, fires via `Rulebook.Trigger`.

Use this instead of manually summing `BaseAttackBonus.ModifiedValue + stat mod` when scale-correctness matters. `RuleCalculateAttackBonus` (with target) adds flanking / bane / target-specific modifiers.

### Targeting-relation primitives

Two engine signals behind `IsTargetingSelf` / `IsTargetingAlly` / `IsTargetedByAlly` / `IsTargetedByEnemy`:

1. `unit.Commands.Standard?.TargetUnit` — engine-authoritative current command target. Works for `UnitAttack`, `UnitUseAbility`, anything deriving from `UnitCommand`.
2. `unit.CombatState.EngagedUnits` — see [EngagedUnits collection type](#engagedunits-collection-type).

Centralized in `Engine/TargetingRelations.Has(attacker, victim)`.

**Approach-phase blind spot**: units running toward a target but not yet swinging or melee-locked match neither signal — accepted blind spot, latency until first attack-frame is ≤1 tick interval. AI-plan inspection would close the gap but is fragile and out of scope.

### Summon detection

`unit.Get<Kingmaker.UnitLogic.Parts.UnitPartSummonedMonster>() != null` is the engine-authoritative test.

The part is added by Owlcat via `EntityDataBase.Ensure<UnitPartSummonedMonster>()` + `Init(summoner)` during the summon-spell flow (verified at IL 571132); Summoner is exposed via the `Summoner` property.

**Does NOT cover** pets / animal companions / Aivu / Eidolons / mod pets — those carry `UnitPartPetMaster` on the master and `UnitPartCompanion`-flavored parts on themselves but never `UnitPartSummonedMonster`.

That asymmetry is the right semantic for player-facing "limit my summon spam" rules: persistent companions don't count toward the cap.

`ConditionProperty.IsSummon` (since 1.7.2) wraps this check and follows the standard `IsDead` Yes/No pattern — mirrored in both `EvaluateUnitProperty` (bucket-path hot evaluator) AND `MatchesPropertyThreshold` (count-subject path used by `AllyCount` / `EnemyCount`); keep both in sync if extending semantics.

### `GetHD` vs `GetEffectiveHD`

Two parallel helpers in `Engine/UnitExtensions.cs`:

| Helper | Returns | Use case |
|--------|---------|----------|
| `GetHD` | `Progression.CharacterLevel` | `ConditionProperty.HitDice` (engine HD-cap rules — Sleep, Color Spray, Hold Person via `ContextConditionHitDice` — exclude Mythic) |
| `GetEffectiveHD` | `CharacterLevel + MythicLevel` | `ConditionProperty.EnemyHDMinusPartyLevel` (margin comparison; symmetric Mythic inclusion is the only way to keep margin meaningful in late Wrath) |

**Don't unify them.** Including Mythic in `HitDice` would cause Sleep rules to silently bypass HD limits on Mythic enemies.

### Two `IsDead` cases in `ConditionEvaluator.cs`

| Site | Path |
|------|------|
| `EvaluateUnitProperty` (~L468) | Hot path called from `EvaluateAllyBucket` / `EvaluateEnemyBucket` for Self/Ally/Enemy scopes |
| `MatchesPropertyThreshold` (~L576) | Count-subject path — currently unreachable for IsDead since it's not in the count-property list, but drift is confusing |

Keep both in sync. Correct check: `unit.Descriptor?.State?.IsFinallyDead ?? false` combined with `ParseBoolValue(condition.Value)` for the Yes/No dropdown semantic.

**Not** `State.IsDead` — see [UnitState.IsDead vs IsFinallyDead](#unitstateisdead-vs-isfinallydead).
