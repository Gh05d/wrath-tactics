# EnemyHDMinusPartyLevel Condition — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ConditionProperty.EnemyHDMinusPartyLevel` evaluating `enemyEffectiveHD − partyMaxEffectiveLevel` (Mythic-inclusive on both sides), usable in Tactics rules to identify boss-tier enemies and gate big spells accordingly.

**Architecture:** New enum value + new `GetEffectiveHD` helper in `UnitExtensions` + rule-scoped cache and two static compute helpers in `ConditionEvaluator` (mirroring the `ABMinusAC` precedent) + dropdown inclusion in `ConditionRowWidget` + i18n keys in 5 locales + CLAUDE.md gotcha. No persistence schema change — new enum entry appends at the tail of `ConditionProperty`.

**Tech Stack:** C# (.NET Framework 4.8.1), Unity UI, HarmonyLib, UMM. Build via `~/.dotnet/dotnet build … -p:SolutionDir=$(pwd)/`. Deploy via `./deploy.sh`. **No unit-test infrastructure** — verification is compile + manual smoke test on Steam Deck.

**Spec reference:** `docs/superpowers/specs/2026-05-05-enemy-hd-minus-party-level-design.md`

---

## File Structure

- **Modify** `WrathTactics/Models/Enums.cs` — add `EnemyHDMinusPartyLevel` to `ConditionProperty`
- **Modify** `WrathTactics/Engine/UnitExtensions.cs` — add `GetEffectiveHD(unit)` sibling to `GetHD`
- **Modify** `WrathTactics/Engine/ConditionEvaluator.cs` — `CurrentPartyMaxLevel` cache, `ComputePartyMaxEffectiveLevel`, `ComputeHDMinusPartyLevel`, two evaluator case branches, `finally`-cleanup wiring
- **Modify** `WrathTactics/UI/ConditionRowWidget.cs` — include property in `propNeedsOperator` and in Enemy/EnemyCount/Enemy*-picker dropdowns
- **Modify** `WrathTactics/Localization/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json` — add `enum.property.EnemyHDMinusPartyLevel = "HD − Party"` (math notation, identical across locales)
- **Modify** `CLAUDE.md` — document the `GetHD` vs `GetEffectiveHD` distinction

No new files.

---

## Task 1: Add `EnemyHDMinusPartyLevel` enum value

**Files:**
- Modify: `WrathTactics/Models/Enums.cs`

- [ ] **Step 1: Append enum value**

Find in `WrathTactics/Models/Enums.cs` the `ConditionProperty` enum. It currently ends:

```csharp
public enum ConditionProperty {
    // ... existing entries ...
    IsTargetedByEnemy,   // Ally-scope: an enemy targets this ally
    IsSummon             // Yes/No — UnitPartSummonedMonster present (excludes pets/companions)
}
```

Replace with:

```csharp
public enum ConditionProperty {
    // ... existing entries ...
    IsTargetedByEnemy,        // Ally-scope: an enemy targets this ally
    IsSummon,                 // Yes/No — UnitPartSummonedMonster present (excludes pets/companions)
    EnemyHDMinusPartyLevel    // enemyEffectiveHD − partyMaxEffectiveLevel — Enemy-scope only
}
```

Note the trailing comma added after `IsSummon` and the comment on the new entry.

- [ ] **Step 2: Compile**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/Enums.cs
git commit -m "$(cat <<'EOF'
feat(model): add EnemyHDMinusPartyLevel ConditionProperty

Appended to enum tail so existing tactics-*.json / preset JSON
(which serialize enum by numeric index via Newtonsoft) keep
deserializing correctly.
EOF
)"
```

---

## Task 2: Add `GetEffectiveHD` helper in `UnitExtensions`

**Files:**
- Modify: `WrathTactics/Engine/UnitExtensions.cs`

- [ ] **Step 1: Add helper next to `GetHD`**

In `WrathTactics/Engine/UnitExtensions.cs`, locate the existing `GetHD` method (line ~11). Below the `GetHD` closing brace and before `GetSave`, insert:

```csharp
        // Effective HD = CharacterLevel + MythicLevel. Used by margin-vs-party-level
        // comparisons (EnemyHDMinusPartyLevel) so Mythic-buffed parties evaluate
        // consistently against Mythic enemies. Distinct from GetHD() which deliberately
        // excludes Mythic to mirror the engine's vanilla HD-cap rules (Sleep, Color
        // Spray, Hold Person via ContextConditionHitDice) — those caps must NOT
        // include Mythic. Don't unify the two helpers.
        public static int GetEffectiveHD(UnitEntityData unit) {
            var p = unit?.Descriptor?.Progression;
            if (p == null) return 0;
            return p.CharacterLevel + p.MythicLevel;
        }
```

- [ ] **Step 2: Compile**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/UnitExtensions.cs
git commit -m "$(cat <<'EOF'
feat(engine): add GetEffectiveHD helper (CharacterLevel + MythicLevel)

Sibling to GetHD; the existing GetHD stays vanilla-HD-only because
ConditionProperty.HitDice mirrors the engine's HD-cap rules (Sleep,
Color Spray, Hold Person) which deliberately exclude Mythic.
GetEffectiveHD is for margin-vs-party-level comparisons where
symmetric Mythic inclusion is required for late-Wrath rules.
EOF
)"
```

---

## Task 3: Implement evaluator helpers, cache, and case branches

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

- [ ] **Step 1: Add the rule-scoped cache field**

In `WrathTactics/Engine/ConditionEvaluator.cs`, locate the existing `CurrentPartyBestAB` field (line ~31):

```csharp
        // Cached party-best-AB for the duration of a single Evaluate call. NaN means
        // "not yet computed"; once computed, the same value is reused across all
        // enemies in EvaluateEnemyBucket (AB is enemy-independent). Cleared in finally.
        static float CurrentPartyBestAB = float.NaN;
```

Append directly after that field:

```csharp
        // Cached party-max-effective-level (CharacterLevel + MythicLevel) for the
        // duration of a single Evaluate call. -1 means "not yet computed"; once
        // computed, reused across all enemies in EvaluateEnemyBucket. Cleared in
        // finally. Sentinel is -1 (not 0) because 0 is a legitimate empty-party
        // result that should propagate as NaN downstream.
        static int CurrentPartyMaxLevel = -1;
```

- [ ] **Step 2: Reset cache in the `finally` block**

Locate the `finally` block in `Evaluate(rule, owner)` at line ~50:

```csharp
            } finally {
                CurrentAction = null;
                CurrentOwner = null;
                CurrentPartyBestAB = float.NaN;
            }
```

Replace with:

```csharp
            } finally {
                CurrentAction = null;
                CurrentOwner = null;
                CurrentPartyBestAB = float.NaN;
                CurrentPartyMaxLevel = -1;
            }
```

- [ ] **Step 3: Add the two compute helpers**

Locate the existing `ComputeABMinusAC` method (line ~347). Below its closing brace (line ~360) and before the `EvaluateAlly` method that follows, insert:

```csharp
        // Computes max(GetEffectiveHD(member)) over Player.Party. Player.Party
        // (NOT PartyAndPets) is intentional: pets have separate level progression
        // curves and a high-HD Eidolon / Drake / Animal Companion would skew the
        // max. PartyLevel here means "the player squad's level" — pets are
        // explicitly excluded. Cached once per Evaluate call via CurrentPartyMaxLevel.
        static int ComputePartyMaxEffectiveLevel() {
            if (CurrentPartyMaxLevel >= 0) return CurrentPartyMaxLevel;
            int max = 0;
            var party = Game.Instance?.Player?.Party;
            if (party != null) {
                foreach (var member in party) {
                    int eff = UnitExtensions.GetEffectiveHD(member);
                    if (eff > max) max = eff;
                }
            }
            CurrentPartyMaxLevel = max;
            return max;
        }

        // Computes enemyEffectiveHD - partyMaxEffectiveLevel for the
        // EnemyHDMinusPartyLevel condition property. Mythic-inclusive on both
        // sides so the margin stays meaningful through late Wrath. Returns NaN
        // when the party is empty (theoretical — not reachable mid-combat) so
        // the row fails-closed rather than reading 0.
        static float ComputeHDMinusPartyLevel(UnitEntityData enemy) {
            if (enemy == null) return float.NaN;
            int partyMax = ComputePartyMaxEffectiveLevel();
            if (partyMax == 0) return float.NaN;
            int enemyHD = UnitExtensions.GetEffectiveHD(enemy);
            float margin = enemyHD - partyMax;
            Log.Engine.Trace($"EnemyHDMinusPartyLevel: {enemy.CharacterName} HD={enemyHD} vs PartyMax={partyMax} -> margin={margin}");
            return margin;
        }
```

- [ ] **Step 4: Add the bucket-path case branch in `EvaluateUnitProperty`**

Locate the existing `ABMinusAC` case in `EvaluateUnitProperty` (line ~517):

```csharp
                case ConditionProperty.ABMinusAC: {
                    if (!IsEnemyScope(condition.Subject)) {
                        Log.Engine.Trace($"ABMinusAC: subject {condition.Subject} is not Enemy-scope, returning false");
                        return false;
                    }
                    float margin = ComputeABMinusAC(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }
```

Directly after that closing brace (line ~525) and before the `IsTargetingSelf` case at line ~527, insert:

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

- [ ] **Step 5: Add the count-path case branch in `MatchesPropertyThreshold`**

Locate `MatchesPropertyThreshold` (line ~671). Find the existing `SpellDCMinusSave` case (line ~706):

```csharp
                case ConditionProperty.SpellDCMinusSave: {
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    float margin = ComputeDCMinusSave(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }
```

Directly after that closing brace and before the `IsDead` count case (line ~715), insert:

```csharp
                case ConditionProperty.EnemyHDMinusPartyLevel: {
                    if (!float.TryParse(condition.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold))
                        return false;
                    float margin = ComputeHDMinusPartyLevel(unit);
                    if (float.IsNaN(margin)) return false;
                    return CompareFloat(margin, condition.Operator, threshold);
                }
```

This branch is hit when an `EnemyCount` row with `EnemyHDMinusPartyLevel` as the per-enemy filter is evaluated — the count-path tallies enemies whose margin meets the threshold. Without this branch, count-path falls into `default: return false` and the user sees no enemies counted regardless of the actual margin. Mirror-pair must stay in sync per CLAUDE.md "Two `IsDead` cases" gotcha.

- [ ] **Step 6: Compile**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded.` 0 errors.

- [ ] **Step 7: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "$(cat <<'EOF'
feat(engine): EnemyHDMinusPartyLevel evaluator + party-max cache

CurrentPartyMaxLevel rule-scoped cache (sentinel -1) ensures
Player.Party is iterated exactly once per rule, not once per enemy
in the bucket scan. ComputePartyMaxEffectiveLevel uses Player.Party
(not PartyAndPets) — pets have separate progression and would skew
the max for parties with high-HD Eidolons/Drakes.

Two case branches added: EvaluateUnitProperty (bucket-path hot
evaluator) and MatchesPropertyThreshold (count-path used by
EnemyCount). Both gated by IsEnemyScope; both fail-closed on NaN.
EOF
)"
```

---

## Task 4: Surface property in UI dropdowns

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs`

- [ ] **Step 1: Append to `propNeedsOperator` list**

Locate the `propNeedsOperator` declaration (line ~115):

```csharp
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.AC
                    || condition.Property == ConditionProperty.HitDice
                    || condition.Property == ConditionProperty.SpellDCMinusSave
                    || condition.Property == ConditionProperty.ABMinusAC
                    || condition.Property == ConditionProperty.WithinRange;
```

Replace with:

```csharp
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.AC
                    || condition.Property == ConditionProperty.HitDice
                    || condition.Property == ConditionProperty.SpellDCMinusSave
                    || condition.Property == ConditionProperty.ABMinusAC
                    || condition.Property == ConditionProperty.EnemyHDMinusPartyLevel
                    || condition.Property == ConditionProperty.WithinRange;
```

- [ ] **Step 2: Append to the Enemy + Enemy*-picker property list in `GetPropertiesForSubject`**

Locate the bundled Enemy/Enemy*-picker case in `GetPropertiesForSubject` (line ~445). It currently ends:

```csharp
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
                        ConditionProperty.HasBuff, ConditionProperty.HasCondition,
                        ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.ABMinusAC,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsTargetingSelf,
                        ConditionProperty.IsTargetingAlly,
                        ConditionProperty.IsTargetedByAlly,
                        ConditionProperty.IsSummon
                    };
```

Replace with (insert `EnemyHDMinusPartyLevel` directly after `ABMinusAC`):

```csharp
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
                        ConditionProperty.HasBuff, ConditionProperty.HasCondition,
                        ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.ABMinusAC,
                        ConditionProperty.EnemyHDMinusPartyLevel,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsTargetingSelf,
                        ConditionProperty.IsTargetingAlly,
                        ConditionProperty.IsTargetedByAlly,
                        ConditionProperty.IsSummon
                    };
```

- [ ] **Step 3: Append to the `EnemyCount` property list**

Still in `GetPropertiesForSubject`, locate the `EnemyCount` case (line ~463). It currently ends:

```csharp
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.ABMinusAC,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsSummon
                    };
```

Replace with (insert `EnemyHDMinusPartyLevel` directly after `ABMinusAC`):

```csharp
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.ABMinusAC,
                        ConditionProperty.EnemyHDMinusPartyLevel,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsSummon
                    };
```

- [ ] **Step 4: Compile**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "$(cat <<'EOF'
feat(ui): surface EnemyHDMinusPartyLevel in enemy + count dropdowns

Wired into propNeedsOperator (numeric input + <>=) and added to
both Enemy/Enemy*-picker and EnemyCount property lists, mirroring
the ABMinusAC placement.
EOF
)"
```

---

## Task 5: Add i18n entries (5 locales)

**Files:**
- Modify: `WrathTactics/Localization/en_GB.json`
- Modify: `WrathTactics/Localization/de_DE.json`
- Modify: `WrathTactics/Localization/fr_FR.json`
- Modify: `WrathTactics/Localization/ru_RU.json`
- Modify: `WrathTactics/Localization/zh_CN.json`

The label uses Unicode minus (U+2212), matching the existing `"DC − Save"` and `"AB − AC"` entries. Math notation is universal — same value across all locales.

- [ ] **Step 1: en_GB**

In `WrathTactics/Localization/en_GB.json`, locate the line:

```json
  "enum.property.IsSummon": "Is summon",
```

Replace with:

```json
  "enum.property.IsSummon": "Is summon",
  "enum.property.EnemyHDMinusPartyLevel": "HD − Party",
```

- [ ] **Step 2: de_DE**

In `WrathTactics/Localization/de_DE.json`, locate:

```json
  "enum.property.IsSummon": "Ist Beschwörung",
```

Replace with:

```json
  "enum.property.IsSummon": "Ist Beschwörung",
  "enum.property.EnemyHDMinusPartyLevel": "HD − Party",
```

- [ ] **Step 3: fr_FR**

In `WrathTactics/Localization/fr_FR.json`, locate:

```json
  "enum.property.IsSummon": "Est une invocation",
```

Replace with:

```json
  "enum.property.IsSummon": "Est une invocation",
  "enum.property.EnemyHDMinusPartyLevel": "HD − Party",
```

- [ ] **Step 4: ru_RU**

In `WrathTactics/Localization/ru_RU.json`, locate:

```json
  "enum.property.IsSummon": "Призванное существо",
```

Replace with:

```json
  "enum.property.IsSummon": "Призванное существо",
  "enum.property.EnemyHDMinusPartyLevel": "HD − Party",
```

- [ ] **Step 5: zh_CN**

In `WrathTactics/Localization/zh_CN.json`, locate:

```json
  "enum.property.IsSummon": "是召唤生物",
```

Replace with:

```json
  "enum.property.IsSummon": "是召唤生物",
  "enum.property.EnemyHDMinusPartyLevel": "HD − Party",
```

- [ ] **Step 6: Compile (sanity check — JSON is loaded at runtime, but build verifies nothing broke)**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded.` 0 errors.

- [ ] **Step 7: Commit**

```bash
git add WrathTactics/Localization/en_GB.json WrathTactics/Localization/de_DE.json WrathTactics/Localization/fr_FR.json WrathTactics/Localization/ru_RU.json WrathTactics/Localization/zh_CN.json
git commit -m "$(cat <<'EOF'
i18n: EnemyHDMinusPartyLevel label "HD − Party" in 5 locales

Math notation with Unicode minus (U+2212) — matches existing
"DC − Save" and "AB − AC" entries. Same value across all locales.
EOF
)"
```

---

## Task 6: Document in CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Append the gotcha**

In `CLAUDE.md`, locate the existing "Summoned-creature detection" gotcha line under the **Game API Gotchas** section (it ends with `…keep both in sync if extending semantics.`).

Directly after that line, append a new bullet:

```markdown
- **`GetHD()` vs `GetEffectiveHD()`**: two parallel helpers in `Engine/UnitExtensions.cs`. `GetHD` returns `Progression.CharacterLevel` (vanilla HD only) — load-bearing for `ConditionProperty.HitDice` because the engine's own HD-cap rules (Sleep, Color Spray, Hold Person via `ContextConditionHitDice`) explicitly exclude Mythic ranks; including them in `HitDice` would cause Sleep rules to silently bypass HD limits on Mythic enemies. `GetEffectiveHD` adds `Progression.MythicLevel` and is for margin-vs-party-level comparisons (`ConditionProperty.EnemyHDMinusPartyLevel`) where symmetric Mythic inclusion is the *only* way to keep the margin meaningful in late Wrath. **Don't unify them**. Also note: `EnemyHDMinusPartyLevel` uses `Game.Instance.Player.Party` (NOT `PartyAndPets`) — pets have separate progression curves and would skew the max for parties with high-HD Eidolons/Drakes; the calculation should reflect the player squad's level only. This is the one documented exception to the project-wide "use PartyAndPets, never Player.Party" convention.
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "$(cat <<'EOF'
docs(claude): GetHD vs GetEffectiveHD gotcha + Player.Party exception

Records why two parallel HD helpers must not be unified (vanilla
HD-cap rules need plain HD; margin-vs-party-level needs Mythic-
inclusive). Also notes that EnemyHDMinusPartyLevel intentionally
uses Player.Party instead of the project's standard PartyAndPets,
because pets would skew the max-level calculation.
EOF
)"
```

---

## Task 7: Build, deploy, and smoke-test on Steam Deck

**Files:** none modified

- [ ] **Step 1: Final clean build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded.` 0 errors, 0 warnings.

- [ ] **Step 2: Deploy to deck**

```bash
./deploy.sh
```

Expected output ends with `Deployed to Steam Deck.`

- [ ] **Step 3: Verify deck DLL timestamp**

```bash
ssh deck-direct "stat -c '%y' '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/WrathTactics.dll'"
```

Expected: timestamp within the last minute. If older, `./deploy.sh` did not actually copy the new build — investigate before testing.

- [ ] **Step 4: In-game smoke test — boss check**

Launch the game, load a save with at least one Mythic level, enter combat against a Mythic boss (e.g. Playful Darkness, or any Act-3+ encounter with a CR-equivalent named enemy carrying mythic ranks).

Build a temporary rule on any companion:
- Subject: `EnemyBiggestThreat`
- Property: `HD − Party` (the new label)
- Operator: `>=`
- Value: `2`
- Action: anything observable (e.g. `Toggle: Power Attack On` or a no-cost cantrip)

Trigger combat. Open the session log:

```bash
ssh deck-direct "ls -t '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/' | head -1"
```

Then tail it:

```bash
ssh deck-direct "tail -50 '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/<latest-log>'"
```

Expected: at least one trace line `EnemyHDMinusPartyLevel: <bossname> HD=<N> vs PartyMax=<M> -> margin=<N-M>` with margin >= 2, and a corresponding `EXECUTED -> ...` line.

- [ ] **Step 5: In-game smoke test — trash check**

In the same combat (or a low-HD trash encounter), build:
- Subject: `Enemy`
- Property: `HD − Party`
- Operator: `<`
- Value: `0`
- Action: `CastSpell: Acid Splash` (or any cantrip)

Trigger an encounter with low-HD mooks. Expected log entry: `EnemyHDMinusPartyLevel: <mookname> HD=<N> vs PartyMax=<M> -> margin=<negative>` AND `EXECUTED -> ...`. Confirms negative margins work for "use cheap spell on trash" gating.

- [ ] **Step 6: Cache verification**

In a 5+ enemy combat with the boss-check rule still active, count `EnemyHDMinusPartyLevel:` log lines for one rule fire. Expected: **N lines** (one per enemy in scope), each ending with the same `PartyMax=<M>` value. The `Player.Party` iteration happens once per rule call thanks to `CurrentPartyMaxLevel`. If `PartyMax=` differs between consecutive lines for the same rule, the cache is broken.

- [ ] **Step 7: Pet exclusion check**

With a companion that has an active animal companion or Eidolon, run any rule using `EnemyHDMinusPartyLevel`. Verify `PartyMax=<M>` in the log matches the highest *player character's* effective level — NOT the pet's HD. (Pet HD is typically lower than master, so the visible difference may be small; if a Drake or high-HD pet exists, this check is more meaningful.)

- [ ] **Step 8: Pre-Mythic regression**

Load a pre-Mythic-ascension save (Act 1 or early Act 2). Repeat the boss-check rule. Expected: behavior collapses to vanilla HD comparison (MythicLevel=0 on both sides, margin = enemy HD − party highest character level). Existing rules using `HitDice` continue to work unchanged.

- [ ] **Step 9: Rule round-trip persistence**

Save a rule using `EnemyHDMinusPartyLevel`. Exit to main menu, reload the save, reopen the Tactics panel. Expected: the rule shows the same property selected, same operator, same value. JSON file `tactics-{GameId}.json` should contain a numeric Property index equal to the new enum tail position.

- [ ] **Step 10: No commit needed**

This task is verification-only — no code changes. Only proceed to the release flow after all 9 verification steps pass.

---

## Self-Review Checklist (run before considering plan complete)

- ✅ **Spec coverage:** Every section in `2026-05-05-enemy-hd-minus-party-level-design.md` maps to a task — Enum addition (Task 1), `GetEffectiveHD` (Task 2), evaluator integration with cache + 2 case branches + cleanup (Task 3), UI surface (Task 4), i18n (Task 5), CLAUDE.md gotcha (Task 6), verification steps for the 7 cases listed in the spec's Verification section (Task 7 steps 4-9).
- ✅ **Placeholder scan:** No TBD/TODO/"appropriate handling"/etc. Every code block is concrete.
- ✅ **Type consistency:** Method names match across tasks (`GetEffectiveHD`, `ComputePartyMaxEffectiveLevel`, `ComputeHDMinusPartyLevel`, `CurrentPartyMaxLevel`). Enum value `EnemyHDMinusPartyLevel` is identical in every reference.
- ✅ **Existing-method references verified against current code:** `IsEnemyScope`, `CompareFloat`, `Log.Engine.Trace`, `UnitExtensions.GetHD` all live in the codebase exactly as called. `Game.Instance.Player.Party` is the engine-canonical access path (publicizer-exposed).

No issues found.
