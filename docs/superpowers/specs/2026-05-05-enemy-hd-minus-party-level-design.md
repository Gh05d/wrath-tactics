# EnemyHDMinusPartyLevel Condition — Design

**Date:** 2026-05-05
**Target:** `WrathTactics` — add `ConditionProperty.EnemyHDMinusPartyLevel` for boss-vs-trash discrimination
**Scope:** Models + Engine + UI + i18n. No persistence migration, no API compat break.

## Problem

`EnemyBiggestThreat` always picks *some* enemy as the highest-threat target, even in fights where the toughest creature is still a comfortable margin below the party's level. Users report they burn high-level slots on trash mooks (high AC but low actual threat) because the existing conditions can't say "is this enemy actually a boss?"

`HitDice` exists as a numeric property but compares against an absolute threshold — players have to update their rules every level-up, and Mythic ranks make absolute thresholds unreliable in late Wrath where Mythic-buffed parties faceroll CR-equal encounters.

## Goal

Add a single numeric condition: `EnemyEffectiveHD − PartyMaxEffectiveLevel`, returning a margin. `> 3` reads as "this enemy outclasses our strongest character by 3+ effective levels — fire the big spell." Symmetric Mythic inclusion on both sides keeps the margin meaningful through Acts 3-5 where the player is mid-Ascension.

## Non-goals

- No change to existing `HitDice` property — vanilla HD-only stays load-bearing for engine HD-cap rules (Sleep, Color Spray, Hold Person), which the codebase deliberately mirrors via `UnitExtensions.GetHD`.
- No per-companion margin variant. User confirmed all party characters share XP/level in this campaign, so Party-Max is the only meaningful aggregate.
- No PartyLevel-as-standalone subject. Composing a single margin property covers the user's request; surfacing PartyLevel separately is YAGNI.
- No CR-based metric. Wrath stores enemy strength as `Progression.CharacterLevel`, not a separate CR field; matching the existing `GetHD` source keeps semantics consistent.

## Design

### Enum addition

`WrathTactics/Models/Enums.cs` — append at the tail of `ConditionProperty`:

```csharp
public enum ConditionProperty {
    // ... existing entries ...
    IsSummon,                // existing tail
    EnemyHDMinusPartyLevel   // NEW: enemy effective HD − party max effective level, Enemy-scope only
}
```

Numeric index is appended. Per `CLAUDE.md`, presets serialize enum indices numerically; appending never shifts existing indices, so all saved rules round-trip unchanged.

### EffectiveHD helper

`WrathTactics/Engine/UnitExtensions.cs` — add a sibling to `GetHD`:

```csharp
// Effective HD = CharacterLevel + MythicLevel. Used by margin-vs-PartyLevel
// comparisons so Mythic-buffed parties evaluate consistently against
// Mythic enemies. Distinct from GetHD() which deliberately excludes
// Mythic to mirror the engine's vanilla HD-cap rules (Sleep, Color
// Spray, Hold Person) — those caps must NOT include Mythic.
public static int GetEffectiveHD(UnitEntityData unit) {
    var p = unit?.Descriptor?.Progression;
    if (p == null) return 0;
    return p.CharacterLevel + p.MythicLevel;
}
```

`Progression.MythicLevel` returns 0 in pre-Mythic acts (verified via IL — getter at IL 131335 et al.), so the helper is safe before Mythic Ascension.

### Evaluator integration

`WrathTactics/Engine/ConditionEvaluator.cs` — three additions:

1. **Rule-scoped cache static** — mirror the `CurrentPartyBestAB` pattern:
   ```csharp
   static int CurrentPartyMaxLevel = -1;  // sentinel: not computed
   ```
   Cleared in the rule's outer `try/finally` next to the existing rule-scoped statics.

2. **`ComputePartyMaxEffectiveLevel()` helper** — placed next to `ComputeABMinusAC`:
   ```csharp
   static int ComputePartyMaxEffectiveLevel() {
       if (CurrentPartyMaxLevel >= 0) return CurrentPartyMaxLevel;
       int max = 0;
       foreach (var member in Game.Instance.Player.Party) {  // Party, not PartyAndPets
           int eff = UnitExtensions.GetEffectiveHD(member);
           if (eff > max) max = eff;
       }
       CurrentPartyMaxLevel = max;
       return max;
   }
   ```
   `Player.Party` is intentional: pets have separate level progression curves and would skew the max for parties with high-level Eidolons / Drakes; this calculation should reflect the *player squad's* level. Comment explicitly notes the asymmetry vs. the codebase's standard `PartyAndPets` convention.

3. **`ComputeHDMinusPartyLevel(enemy)` helper** — returns `float`:
   ```csharp
   static float ComputeHDMinusPartyLevel(UnitEntityData enemy) {
       int partyMax = ComputePartyMaxEffectiveLevel();
       if (partyMax == 0) return float.NaN;  // empty party — uncomputable
       int enemyHD = UnitExtensions.GetEffectiveHD(enemy);
       float margin = enemyHD - partyMax;
       Log.Engine.Trace($"EnemyHDMinusPartyLevel: {enemy.CharacterName} HD={enemyHD} vs PartyMax={partyMax} -> margin={margin}");
       return margin;
   }
   ```
   NaN sentinel mirrors `ComputeABMinusAC` / `ComputeDCMinusSave`.

4. **Two evaluator branches** — both gated by `IsEnemyScope` like `ABMinusAC`:

   `EvaluateUnitProperty` (~line 517, bucket-path hot evaluator):
   ```csharp
   case ConditionProperty.EnemyHDMinusPartyLevel: {
       if (!IsEnemyScope(condition.Subject)) {
           Log.Engine.Trace($"EnemyHDMinusPartyLevel: subject {condition.Subject} is not Enemy-scope, returning false");
           return false;
       }
       float margin = ComputeHDMinusPartyLevel(unit);
       if (float.IsNaN(margin)) return false;
       return CompareFloat(margin, condition.Operator, threshold);
   }
   ```

   `MatchesPropertyThreshold` (~line 720, count-subject path twin) — same body. Both cases must stay in sync per the existing CLAUDE.md "Two `IsDead` cases" gotcha.

### UI integration

`WrathTactics/UI/ConditionRowWidget.cs`:

1. **`propNeedsOperator` list (~line 115)** — append:
   ```csharp
   || condition.Property == ConditionProperty.EnemyHDMinusPartyLevel
   ```

2. **`GetPropertiesForSubject` (~line 401)** — append `EnemyHDMinusPartyLevel` to the same property list as `ABMinusAC`:
   - The Enemy + Enemy* picker bundle (line ~443)
   - The `EnemyCount` list (line ~459)

3. **No `PropertyLabel` switch addition needed** — `EnumLabels.For(ConditionProperty)` resolves via i18n keys (see below). The existing math-notation labels (`"DC − Save"`, `"AB − AC"`) live in the i18n files, not in code.

### i18n

5 locale files get one new key each. Math notation is universal — same value across all locales:

```jsonc
"enum.property.EnemyHDMinusPartyLevel": "HD − Party"
```

Unicode minus U+2212 mirrors the existing `"DC − Save"` / `"AB − AC"` entries.

Files: `en_GB.json`, `de_DE.json`, `fr_FR.json`, `ru_RU.json`, `zh_CN.json` — append after the `IsSummon` line just added.

### Documentation

Append to `CLAUDE.md` § "Game API Gotchas":

> - **`GetHD()` vs `GetEffectiveHD()`**: two parallel helpers in `UnitExtensions`. `GetHD` returns `Progression.CharacterLevel` (vanilla HD only) — load-bearing for `ConditionProperty.HitDice` because the engine's own HD-cap rules (Sleep, Color Spray, Hold Person via `ContextConditionHitDice`) explicitly exclude Mythic ranks; including them in `HitDice` would cause Sleep rules to silently bypass HD limits on Mythic enemies. `GetEffectiveHD` adds `Progression.MythicLevel` and is for margin-vs-party-level comparisons (`EnemyHDMinusPartyLevel`) where symmetric Mythic inclusion is the *only* way to keep the margin meaningful in late Wrath. Don't unify them.

## Edge cases

| Case | Behavior |
|---|---|
| Pre-Mythic acts (MythicLevel = 0 for all) | Margin reduces to vanilla HD vs. CharacterLevel — identical to a non-Mythic Pathfinder calculation. No special-case. |
| Empty `Player.Party` (theoretical, not reachable in combat) | `partyMax = 0` → `NaN` → row fails to match → engine trace logged. |
| Enemy with no `Progression` (rare scripted entity) | `GetEffectiveHD = 0` → margin = `−partyMax` → legitimate negative number, "this is a weak target" semantic. Acceptable. |
| Negative margins (player party stronger than enemy) | Valid float. User can compose `< -3` to gate "trash mob → use cantrip" rules. Bonus use-case for free. |
| Many enemies in scope (e.g. EnemyCount loop over 10 mooks) | `CurrentPartyMaxLevel` cache means `Player.Party` is iterated exactly once per rule, not 10 times. |
| `EnemyCount` subject with a single condition | Goes through `MatchesPropertyThreshold` branch; same compute helper, same cache. |

## Symmetry verification

| Scenario | Party (max effective) | Enemy effective HD | Margin | Reads as |
|---|---|---|---|---|
| L10 + M6 vs CR15 Hyena | 16 | 15 | −1 | not a boss ✓ |
| L10 + M6 vs Mythic CR15 + 4M | 16 | 19 | +3 | **boss** ✓ |
| L20 + M10 vs Deskari | 30 | ~30+ | ≥0 | endboss-tier ✓ |
| L8 + M3 vs Drezen Cultist L4 | 11 | 4 | −7 | trash ✓ |
| L4 + M0 vs Vavakia (pre-Drezen-Encounter) | 4 | 13 | +9 | overwhelming ✓ |

## Persistence

- Numeric index appended at end → existing presets and saved rules round-trip identically.
- `Condition.Value` stores threshold as numeric string, parsed via existing `float.TryParse(condition.Value, out threshold)` path used by other margin properties.
- `Value2` and `CountOperator` already supported by the count-path machinery for `EnemyCount` use.

## Verification

Manual smoke test on the Steam Deck (no automated test harness in this repo; matches existing release flow):

1. `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
2. `./deploy.sh`
3. **Trash check** — Drezen Act 3, low-HD cultists: rule `Enemy.EnemyHDMinusPartyLevel > 0 → CastSpell: Acid Splash` should NOT fire (margin negative).
4. **Boss check** — Playful Darkness or any mid-Wrath Mythic boss: `EnemyBiggestThreat.EnemyHDMinusPartyLevel >= 2 → CastSpell: Heal/big-finisher` should fire.
5. **Cache check** — log spot-check during a 5+ enemy combat: only one `EnemyHDMinusPartyLevel: ... PartyMax=N` line per rule fire (not N lines), confirming the rule-scoped cache works.
6. **Pre-Mythic regression** — Act 1 fight: rule with margin `>= 3` fires on appropriately-tougher enemies (CR vs party level). Act 1 has no Mythic, so behavior collapses to vanilla HD comparison.
7. **`EnemyCount` regression** — `EnemyCount[EnemyHDMinusPartyLevel >= 1] >= 3 → CastSpell: AOE` fires only when 3+ enemies are at-or-above party effective level. Confirm cache still computes once.
8. **Pet exclusion** — party with a high-level pet (e.g. Animal Companion at HD 12 while master is HD 10) should still report PartyMax = highest *player* HD; pet's HD doesn't dominate.

No JSON migration required. Existing presets are byte-identical post-change.

## Files to modify

| File | Change |
|---|---|
| `WrathTactics/Models/Enums.cs` | Append `EnemyHDMinusPartyLevel` to `ConditionProperty`. |
| `WrathTactics/Engine/UnitExtensions.cs` | Add `GetEffectiveHD(unit)` sibling to `GetHD`. |
| `WrathTactics/Engine/ConditionEvaluator.cs` | Add `CurrentPartyMaxLevel` static, `ComputePartyMaxEffectiveLevel`, `ComputeHDMinusPartyLevel`. Add the case branch to both `EvaluateUnitProperty` and `MatchesPropertyThreshold`. Wire cache reset into the rule's `try/finally`. |
| `WrathTactics/UI/ConditionRowWidget.cs` | Append to `propNeedsOperator`. Append to Enemy + Enemy*-picker + EnemyCount lists in `GetPropertiesForSubject`. |
| `WrathTactics/Localization/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json` | Add `enum.property.EnemyHDMinusPartyLevel = "HD − Party"`. |
| `CLAUDE.md` | Add `GetHD` vs `GetEffectiveHD` gotcha to the Game API Gotchas section. |

Estimated diff: ~50 LOC + 5 i18n lines + ~5 lines of doc.
