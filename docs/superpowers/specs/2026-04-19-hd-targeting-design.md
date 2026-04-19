# HD (Hit Dice) Targeting — Design

**Date:** 2026-04-19
**Status:** Approved, ready for implementation
**Target version:** 0.7.0

## Motivation

Several PF spells target specific Hit Dice (HD):

- **Sleep** — 4 HD or fewer
- **Color Spray** — weaker effects on 5+ HD
- **Hold Person** — caster wants biggest humanoid
- **Circle of Death** — 9 HD or fewer, cone-AoE
- **Power Word** spells — HP-thresholded (not HD), but HD-proxy useful for single-target picks
- **Prismatic Spray** — HD-gated effect roll

Current engine has AC/Saves/HP condition- and target-selection but no HD. Users can't express "cast Sleep when 3+ weak enemies are visible" or "cast Hold Person on the highest-HD caster".

## Scope

Mirror the existing AC/Saves pattern for HD — full symmetry across `ConditionProperty`, `ConditionSubject`, and `TargetType`. No new presets, no ally-side HD (players know their party's levels). Mythic level is NOT included in HD, matching the engine's own `ContextConditionHitDice` (IL-verified) and PF RAW.

## HD API Verification

`Kingmaker.UnitLogic.Mechanics.Conditions.ContextConditionHitDice.CheckCondition()` (the engine's internal HD-gate for Sleep, Color Spray, Hold Person) reads HD via:

```il
IL_002f: ldloc.0                                    // target UnitEntityData
IL_0030: callvirt get_Descriptor()
IL_0035: ldfld   UnitDescriptor::Progression
IL_003a: callvirt UnitProgressionData::get_CharacterLevel()
```

Canonical HD accessor:

```csharp
static int GetHD(UnitEntityData u) =>
    u?.Descriptor?.Progression?.CharacterLevel ?? 0;
```

Racial HD is already folded into `CharacterLevel` for monsters in this engine. Mythic levels are a separate property and are excluded by design.

## Model Changes (`Models/Enums.cs`)

**All new enum values APPENDED at the end** — Wrath Tactics persists presets and rules with numeric enum indices (Newtonsoft default). Inserting values mid-enum shifts downstream indices and corrupts existing configs. See CLAUDE.md gotcha on numeric enum indices.

```csharp
public enum ConditionProperty {
    // existing 0..14 unchanged:
    HpPercent, AC, HasBuff, HasCondition, SpellSlotsAtLevel,
    SpellSlotsAboveLevel, Resource, CreatureType, CombatRounds,
    IsDead, SaveFortitude, SaveReflex, SaveWill, Alignment, IsInCombat,
    HitDice,  // new, index 15
}

public enum ConditionSubject {
    // existing 0..17 unchanged:
    Self, Ally, AllyCount, Enemy, EnemyCount, Combat,
    EnemyBiggestThreat, EnemyLowestThreat, EnemyHighestHp, EnemyLowestHp,
    EnemyLowestAC, EnemyHighestAC, EnemyLowestFort, EnemyHighestFort,
    EnemyLowestReflex, EnemyHighestReflex, EnemyLowestWill, EnemyHighestWill,
    EnemyHighestHD,  // new, index 18
    EnemyLowestHD,   // new, index 19
}

public enum TargetType {
    // existing 0..17 unchanged:
    Self, AllyLowestHp, AllyWithCondition, AllyMissingBuff, EnemyNearest,
    EnemyLowestHp, EnemyHighestHp, EnemyHighestAC, EnemyLowestAC,
    EnemyHighestFort, EnemyLowestFort, EnemyHighestReflex, EnemyLowestReflex,
    EnemyHighestWill, EnemyLowestWill, EnemyHighestThreat,
    EnemyCreatureType, ConditionTarget,
    EnemyHighestHD,  // new, index 18
    EnemyLowestHD,   // new, index 19
}
```

## Engine Changes

### `ConditionEvaluator`

1. Add `ConditionProperty.HitDice` branch in the property-evaluation switch: reads HD via the helper, compares against `condition.Value` with the rule's operator. Same skeleton as the `AC` branch.
2. Add `EnemyHighestHD` / `EnemyLowestHD` cases in the single-enemy subject resolver: `GetVisibleEnemies().OrderByDescending(GetHD).FirstOrDefault()` (and mirror for lowest). Same skeleton as `EnemyHighestAC` / `EnemyLowestAC`.

### `TargetResolver`

Add `TargetType.EnemyHighestHD` / `EnemyLowestHD` cases. Same pattern as `EnemyHighestAC` / `EnemyLowestAC`: enumerate visible enemies (using the shared `IsInCombat` filter per existing enemy-filter-consistency gotcha), order by HD, return first.

### HD Accessor Placement

Add `GetHD` as a static helper in a shared utility. Candidate locations:

- `Engine/ThreatCalculator.cs` — already deals with per-enemy scoring, but HD is orthogonal to threat
- `Engine/UnitExtensions.cs` (new file) — dedicated helper class, cleanest

**Decision:** new file `Engine/UnitExtensions.cs`. Single tiny static class, room for future one-line accessors.

## UI Changes

`RuleEditorWidget` and `SpellDropdownProvider` enumerate enum values for dropdowns. Add display labels for the new entries (the existing code likely uses reflection or a switch — implementation plan decides). Labels:

- `ConditionProperty.HitDice` → `"Hit Dice"`
- `ConditionSubject.EnemyHighestHD` → `"Enemy (highest HD)"`
- `ConditionSubject.EnemyLowestHD` → `"Enemy (lowest HD)"`
- `TargetType.EnemyHighestHD` → `"Enemy (highest HD)"`
- `TargetType.EnemyLowestHD` → `"Enemy (lowest HD)"`

Operator dropdown for `HitDice` uses the existing numeric operator set (`<`, `≤`, `=`, `≠`, `≥`, `>`) — same as AC/HP, no new operator variants.

## Data Flow

```
User builds rule in UI:
  Subject=EnemyCount, Property=HitDice, Operator=LessOrEqual, Value=4, Value2=3
    ↓
ConditionEvaluator.Evaluate:
  GetVisibleEnemies()                             // IsInCombat filter
    .Count(e => GetHD(e) <= 4)                    // HitDice property branch
    >= 3                                          // Value2 threshold
    ↓ match
TargetResolver.Resolve(EnemyLowestHD):
  GetVisibleEnemies().OrderBy(GetHD).First()
    ↓
ActionValidator.CanExecute(CastSpell Sleep, caster, target): true
    ↓
CommandExecutor.Execute → Sleep cast on low-HD enemy
```

## Error Handling

- `GetHD` null-safe (returns 0 if any link in the chain is null) — matches existing defensive null-checks in `ConditionEvaluator`.
- Empty enemy set: `FirstOrDefault` returns null, handled by existing `ActionValidator` "no target" path.
- HD = 0 is a valid return (e.g., a construct with no HD); condition comparisons still work numerically.

## Testing

Manual Deck smoke tests (no unit test infrastructure in this mod — behavior verification in-game is the standard per project norms):

1. **Sleep trigger + target**
   Rule: Subject=EnemyCount, Property=HitDice, Op=≤, Value=4, Value2=3; Action=CastSpell(Sleep); Target=EnemyLowestHD.
   Expected: caster uses Sleep when ≥3 low-HD enemies visible, targets the weakest.
2. **Hold Person on biggest threat**
   Rule: Subject=Enemy, Property=CreatureType, Op=Equal, Value="Humanoid"; Target=EnemyHighestHD.
   Expected: caster targets the highest-HD humanoid.
3. **Circle of Death gating**
   Rule: Subject=EnemyCount, Property=HitDice, Op=≤, Value=9, Value2=4; Target=ConditionTarget.
   Expected: only fires when ≥4 HD-9-or-below enemies present.
4. **Regression**: existing AC/Save/HP conditions and targets behave identically (enum-append didn't shift indices).

## Risk & Compatibility

- **No schema migration needed**: append-only enum changes preserve all existing 0.6.3 preset and rule JSON indices.
- **Mythic-exclusion may surprise power-gamers**: documented in release notes. Engine-authoritative behavior; changing it would diverge from vanilla spell checks.
- **HD = 0 edge case**: some scene-only units have no progression. `GetHD`'s null-safety + `?? 0` makes those never match an HD-threshold trigger, which is the desired behavior (they're typically non-combatants or scripted dummies).

## Out of Scope

- **Default presets** for HD-targeting spells — add later if user demand emerges.
- **Mythic-inclusive HD variant** — no RAW use case; revisit if users ask.
- **Ally-side HD conditions** — players know their party's levels; no clear use case.
- **HD-range conditions** (e.g., "3 ≤ HD ≤ 7") — expressible today via compound conditions in a ConditionGroup. No new operator needed.
