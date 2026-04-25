# Undead Healing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Heal action automatically picks negative-energy heals (Inflict / Harm / Channel-Negative) for undead and Dhampir targets, with a per-rule pin override (`Auto` / `Positive` / `Negative`).

**Architecture:** Three orthogonal changes. (1) Detection helper `IsNegativeEnergyAffine(unit)` reads `Blueprint.Type.name == "UndeadType"` OR a lazy-resolved `NegativeEnergyAffinity` feature-fact. (2) `IsHealingSpell` (boolean) becomes `ClassifyHeal` (returns `HealEnergyType.None / Positive / Negative`) with separate keyword tables. (3) `FindBestHealEx` gains `target` + `pin` parameters and filters per-candidate via the classification + affinity + pin combination. UI gets a third dropdown between HealMode and HealSources. Backward-compat is free: new field defaults to `Auto = 0`, unknown JSON fields deserialise to that.

**Tech Stack:** .NET Framework 4.8.1, HarmonyLib, Unity Mod Manager, Newtonsoft.Json (bundled, older). Build via `~/.dotnet/dotnet build -p:SolutionDir=$(pwd)/`. No automated tests — verification is in-game smoke-test on the Steam Deck.

**Spec:** `docs/superpowers/specs/2026-04-25-undead-healing-design.md`

---

## File Inventory

| File | Change |
|---|---|
| `WrathTactics/Models/Enums.cs` | Add `HealEnergyType` enum |
| `WrathTactics/Models/TacticsRule.cs` | Add `ActionDef.HealEnergy` field |
| `WrathTactics/Engine/ActionValidator.cs` | Add `IsNegativeEnergyAffine`, replace `IsHealingSpell` with `ClassifyHeal`, extend `FindBestHealEx` signature, update `CanExecute` call site |
| `WrathTactics/Engine/CommandExecutor.cs` | Update `ExecuteHeal` call site to pass target + pin |
| `WrathTactics/UI/RuleEditorWidget.cs` | Insert `HealEnergy` dropdown, rebalance anchor ranges, add `HealEnergyLabel` helper |
| `WrathTactics/Info.json` | Version 1.2.0 → 1.3.0 |
| `WrathTactics/WrathTactics.csproj` | Version 1.2.0 → 1.3.0 |

No new files. All changes scoped to seven existing files.

---

## Task 1: Add `HealEnergyType` enum and `ActionDef.HealEnergy` field

**Files:**
- Modify: `WrathTactics/Models/Enums.cs` (after `HealMode`)
- Modify: `WrathTactics/Models/TacticsRule.cs` (`ActionDef` class)

- [ ] **Step 1: Add `HealEnergyType` enum to `Enums.cs`**

Insert after the `HealMode` enum (line 72), before the `HealSourceMask` enum:

```csharp
    /// <summary>
    /// Pin for the Heal action. Auto detects via target's NegativeEnergyAffinity / CreatureType
    /// and picks Cure (living) or Inflict/Harm (undead) accordingly. Positive / Negative force
    /// a specific energy type regardless of target — power-user override.
    ///
    /// IMPORTANT (CLAUDE.md gotcha): Newtonsoft serialises numeric indices. Auto MUST stay at
    /// index 0 so missing JSON fields deserialise as Auto on legacy configs. Append new values
    /// at the END only — never insert in the middle.
    /// </summary>
    public enum HealEnergyType {
        Auto,       // Detect via target's NegativeEnergyAffinity / CreatureType (default)
        Positive,   // Force Cure / Heal / Channel-Positive only
        Negative    // Force Inflict / Harm / Channel-Negative only
    }
```

- [ ] **Step 2: Add `HealEnergy` field to `ActionDef`**

In `Models/TacticsRule.cs`, after the existing `HealSources` line (line 42), add:

```csharp
        [JsonProperty] public HealEnergyType HealEnergy { get; set; } = HealEnergyType.Auto;
```

- [ ] **Step 3: Build to verify the enum + field compile cleanly**

Run:
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded.` with no errors related to the new symbols. Pre-existing `findstr` warnings on Linux are harmless (parent CLAUDE.md).

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Models/Enums.cs WrathTactics/Models/TacticsRule.cs
git commit -m "$(cat <<'EOF'
feat(model): HealEnergyType enum + ActionDef.HealEnergy field

Auto = 0 default keeps existing JSON configs deserialising correctly.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add `IsNegativeEnergyAffine` detection helper

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs` (add static helper near the top of the class, before `CanExecute`)

- [ ] **Step 1: Resolve the `NegativeEnergyAffinity` feature GUID**

The Dhampir race uses a feature usually named `NegativeEnergyAffinityFeature`. Verify the exact blueprint name and GUID via `ilspycmd`:

```bash
DOTNET_ROOT=/home/pascal/.dotnet ~/.dotnet/tools/ilspycmd \
  GameInstall/Wrath_Data/Managed/Assembly-CSharp.dll -l c \
  | grep -iE "negativeenergyaffinity"
```

If a class match exists, locate the corresponding blueprint reference (`FeatureRefs` or similar). Otherwise, search the extracted blueprint JSON:

```bash
ssh deck-direct "unzip -p '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/blueprints.zip' Blueprints/' | grep -liE 'negativeenergyaffinity' | head -5"
```

Note the GUID of the feature blueprint. If verification fails, proceed with the type-only path: `IsNegativeEnergyAffine` will only match Vollform-Untote (post-Lich-Ascension MC, undead summons, vampire companions). Dhampir detection then needs a follow-up patch.

If the GUID is found, hard-code it as a constant — Dhampir feature GUIDs do not change between game patches.

- [ ] **Step 2: Add the static helper to `ActionValidator.cs`**

Insert near the top of the `ActionValidator` class (after the existing `using`s and class declaration, before `CanExecute` ~line 30). Use the GUID from Step 1; if Step 1 failed, use the placeholder `null` and the `if (..)` block becomes a no-op.

```csharp
        // Lazy-resolved Dhampir / pre-Ascension Lich race-feature. Resolved once on first
        // access; null tolerated (only CreatureType-based detection then). Replace the GUID
        // with the verified value from Task 2 Step 1.
        static BlueprintFeature s_NegativeEnergyAffinity;
        static bool s_NegativeEnergyAffinityResolved;
        const string NegativeEnergyAffinityGuid = "PUT_VERIFIED_GUID_HERE";

        static BlueprintFeature GetNegativeEnergyAffinityFeature() {
            if (s_NegativeEnergyAffinityResolved) return s_NegativeEnergyAffinity;
            s_NegativeEnergyAffinityResolved = true;
            try {
                var guid = new BlueprintGuid(System.Guid.Parse(NegativeEnergyAffinityGuid));
                s_NegativeEnergyAffinity = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(guid);
                if (s_NegativeEnergyAffinity == null) {
                    Log.Engine.Warn($"NegativeEnergyAffinity feature blueprint not found ({NegativeEnergyAffinityGuid}) — Dhampir detection disabled, only CreatureType=Undead matches");
                }
            } catch (System.Exception ex) {
                Log.Engine.Warn($"Failed to resolve NegativeEnergyAffinity GUID '{NegativeEnergyAffinityGuid}': {ex.Message}");
            }
            return s_NegativeEnergyAffinity;
        }

        /// <summary>
        /// True when positive energy damages and negative energy heals this unit. Matches
        /// Vollform-Untote via CreatureType, plus Dhampir / pre-Ascension Lich via the
        /// NegativeEnergyAffinity race-feature. Conservative: defaults false on null/unknown.
        /// </summary>
        public static bool IsNegativeEnergyAffine(UnitEntityData unit) {
            var d = unit?.Descriptor;
            if (d == null) return false;

            // Source 1: CreatureType (Lich-MC post-Ascension, vampire companions, undead summons)
            if (d.Blueprint?.Type != null && d.Blueprint.Type.name == "UndeadType") return true;

            // Source 2: NegativeEnergyAffinity feature-fact (Dhampir race-feature, mythic transition buffs)
            var feature = GetNegativeEnergyAffinityFeature();
            if (feature != null && d.HasFact(feature)) return true;

            return false;
        }
```

Confirm the `using` block of `ActionValidator.cs` already imports `Kingmaker.Blueprints` and `Kingmaker.Blueprints.Classes` (it does — verify by reading the top of the file).

- [ ] **Step 3: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded`. If `BlueprintFeature` is unresolved, add `using Kingmaker.Blueprints.Classes;` to the top of the file. If `ResourcesLibrary.TryGetBlueprint` is unresolved, add `using Kingmaker.Blueprints;`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs
git commit -m "$(cat <<'EOF'
feat(engine): IsNegativeEnergyAffine detection helper

Reads Blueprint.Type.name == "UndeadType" plus a lazy-resolved
NegativeEnergyAffinity feature-fact (Dhampir, pre-Ascension Lich).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Replace `IsHealingSpell` with `ClassifyHeal`

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs` (~lines 719-740 — replace the existing `IsHealingSpell` and `MatchesHealKeyword`)
- Modify: `WrathTactics/Engine/ActionValidator.cs` (~lines 365, 385, 415, 439 — call sites in `FindBestHealEx`)

- [ ] **Step 1: Replace `IsHealingSpell` and `MatchesHealKeyword` with `ClassifyHeal` and split keyword helpers**

In `ActionValidator.cs`, replace the existing block (current lines 719-740, the methods `IsHealingSpell` and `MatchesHealKeyword`) with:

```csharp
        /// <summary>
        /// Classifies a heal blueprint by energy type. Returns None for non-heal spells.
        /// Keyword-based; matches both internal (English, stable) name and localised display
        /// name. The German positive/negative tables disambiguate "Wunden heilen" (Cure) vs
        /// "Wunden zufügen" (Inflict) which previously both matched the bare "wunden" keyword
        /// — pre-feature this was harmless because Inflict was never searched; now it would
        /// route Cure spells as Negative and break healing.
        /// </summary>
        static HealEnergyType ClassifyHeal(BlueprintAbility blueprint) {
            if (blueprint == null) return HealEnergyType.None;
            string n = (blueprint.name ?? "").ToLowerInvariant();
            string d = (blueprint.Name ?? "").ToLowerInvariant();

            // Negative first: "Channel Negative" should not be matched as Positive by the
            // "channel" substring (the Positive table requires "channel positive" specifically,
            // but defence-in-depth — order keeps results stable if the tables ever change).
            if (MatchesNegativeKeyword(n) || MatchesNegativeKeyword(d)) return HealEnergyType.Negative;
            if (MatchesPositiveKeyword(n) || MatchesPositiveKeyword(d)) return HealEnergyType.Positive;
            return HealEnergyType.None;
        }

        static bool MatchesPositiveKeyword(string n) {
            // "restoration" intentionally excluded — Lesser/Normal/Greater Restoration remove
            // ability damage / drain / negative levels but do NOT restore HP. Including them
            // made the Heal action burn 300-900g Restoration scrolls on a low-HP ally.
            // Known imprecision: "cure" matches Cure Disease / Cure Deafness / Neutralize Poison
            // — these are rare in typical inventories and the UMD gate limits mis-casts.
            // Component-based detection (look for ContextActionHealTarget) would be more correct
            // but is out of scope here.
            return n.Contains("cure")
                || n.Contains("heal")
                || n.Contains("lay on hands")
                || n.Contains("channel positive")
                // German display names — "wunden heilen" disambiguates from "wunden zufügen"
                || n.Contains("wunden heilen")
                || n.Contains("heilung")
                || n.Contains("auflegen");
        }

        static bool MatchesNegativeKeyword(string n) {
            return n.Contains("inflict")
                || n.Contains("harm")
                || n.Contains("channel negative")
                // German display names
                || n.Contains("wunden zufügen")
                || n.Contains("negative energie");
        }
```

- [ ] **Step 2: Update the four call sites of `IsHealingSpell` in `FindBestHealEx`**

Replace each `IsHealingSpell(x)` call (~lines 365, 385, 415, 439 in current file) with `ClassifyHeal(x) != HealEnergyType.None`. The classification result will be re-used in Task 4 — for now, just replace the boolean check.

For example, at ~line 365:

```csharp
                        // BEFORE:
                        if (IsHealingSpell(spell.Blueprint)) {
                        // AFTER:
                        if (ClassifyHeal(spell.Blueprint) != HealEnergyType.None) {
```

Apply the same transformation to lines ~385 (`if (!IsHealingSpell(ability.Blueprint)) continue;` → `if (ClassifyHeal(ability.Blueprint) == HealEnergyType.None) continue;`), ~415 (same as 385), and ~439 (`if (!IsHealingSpell(usable.Ability)) {` → `if (ClassifyHeal(usable.Ability) == HealEnergyType.None) {`).

- [ ] **Step 3: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded`. No call to `IsHealingSpell` remains; if the build fails on `IsHealingSpell` being undefined, the Step 2 grep missed a call site — re-grep `grep -n "IsHealingSpell\|MatchesHealKeyword" WrathTactics/Engine/ActionValidator.cs` and fix.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs
git commit -m "$(cat <<'EOF'
refactor(engine): IsHealingSpell -> ClassifyHeal + Negative table

Splits keyword tables for Positive (cure/heal/channel positive) and
Negative (inflict/harm/channel negative). DE display names corrected:
"wunden heilen" / "wunden zufügen" disambiguates Cure vs Inflict.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Extend `FindBestHealEx` with target + pin filtering

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs` (`FindBestHeal`, `FindBestHealEx` signatures + filter loop, `CanExecute` heal case)
- Modify: `WrathTactics/Engine/CommandExecutor.cs` (`ExecuteHeal` call site)

- [ ] **Step 1: Update `FindBestHeal` and `FindBestHealEx` signatures**

In `ActionValidator.cs` ~line 339, replace the two-method block:

```csharp
        public static AbilityData FindBestHeal(
            UnitEntityData owner,
            UnitEntityData target,
            HealMode mode = HealMode.Any,
            HealSourceMask sources = HealSourceMask.All,
            HealEnergyType pin = HealEnergyType.Auto) {
            return FindBestHealEx(owner, target, mode, sources, pin, out _);
        }

        /// <summary>
        /// Returns best heal ability plus the inventory ItemEntity it came from (null for
        /// spellbook spells, class abilities, and quickslot/equipped wands). Caller must
        /// consume the item via Inventory.Remove after casting — synthesized AbilityData
        /// from inventory doesn't auto-consume through Rulebook.Trigger.
        ///
        /// `target` drives Auto-mode energy detection (undead → Negative, else Positive).
        /// `pin` overrides the auto-pick: Positive / Negative force a specific energy type
        /// regardless of target affinity (power-user override; null result if no match).
        /// `sources` masks which classes of heal are eligible (Spell / Scroll / Potion).
        /// </summary>
        public static AbilityData FindBestHealEx(
            UnitEntityData owner,
            UnitEntityData target,
            HealMode mode,
            HealSourceMask sources,
            HealEnergyType pin,
            out ItemEntity inventorySource) {
```

- [ ] **Step 2: Add per-candidate energy-filter helper inside `FindBestHealEx`**

Right after the local `wantSpell / wantScroll / wantPotion` declarations (~line 358 in current file), insert:

```csharp
            // Resolve target affinity once for Auto mode. For Positive / Negative pins, the
            // target check is bypassed entirely (user-explicit override).
            bool autoUndead = (pin == HealEnergyType.Auto) && IsNegativeEnergyAffine(target);
            HealEnergyType requiredEnergy =
                pin == HealEnergyType.Positive ? HealEnergyType.Positive
                : pin == HealEnergyType.Negative ? HealEnergyType.Negative
                : (autoUndead ? HealEnergyType.Negative : HealEnergyType.Positive);

            // Local helper: "is this candidate's energy type acceptable for this rule?"
            bool MatchesEnergy(BlueprintAbility bp) {
                var t = ClassifyHeal(bp);
                return t == requiredEnergy;
            }
```

- [ ] **Step 3: Update each candidate-add site to filter by energy**

Wrap each existing `ClassifyHeal != None` check (from Task 3) with the additional energy-match check. The candidate is only added if both the classification is non-None AND it matches the required energy.

At ~line 365 (the spellbook spell loop), replace:

```csharp
                        // BEFORE (after Task 3):
                        if (ClassifyHeal(spell.Blueprint) != HealEnergyType.None) {
                            if (book.GetAvailableForCastSpellCount(spell) <= 0) continue;
                            ...
                        }
                        // AFTER:
                        if (!MatchesEnergy(spell.Blueprint)) continue;
                        if (book.GetAvailableForCastSpellCount(spell) <= 0) continue;
                        if (!spell.IsAvailable) {
                            Log.Engine.Trace($"Skipping heal spell {spell.Blueprint.name} for {owner.CharacterName}: engine-unavailable ({spell.GetUnavailableReason()})");
                            continue;
                        }
                        heals.Add((spell, 100 + level * 10, null, HealSourceMask.Spell));
```

(The `if (!MatchesEnergy(...)) continue;` line replaces the Task-3 `!= None` test outright — `MatchesEnergy` is stricter and already returns false for None.)

Apply the same transformation at ~line 385 (class-ability loop), ~line 415 (wand loop), and ~line 439 (inventory-item loop). For the inventory loop, the Trace-log line that mentions "NOT a healing spell" should be updated to reflect the energy mismatch instead:

```csharp
                    if (!MatchesEnergy(usable.Ability)) {
                        Log.Engine.Trace($"  inventory item {itemName} (ability '{abilityName}'): wrong energy type for required {requiredEnergy}");
                        continue;
                    }
                    invHealing++;
```

- [ ] **Step 4: Update the `CanExecute` call site for `ActionType.Heal`**

In `ActionValidator.cs` ~line 57 (the heal case in `CanExecute`), replace:

```csharp
                case ActionType.Heal:
                    // Heal needs a unit target; FindBestHeal also validates that something heal-worthy exists.
                    return target.Unit != null
                        && FindBestHeal(owner, target.Unit, action.HealMode, action.HealSources, action.HealEnergy) != null;
```

(If the existing line uses `FindBestHeal(owner, ...)` without a target arg — match the current line and replace.) Verify with `grep -n "FindBestHeal" WrathTactics/Engine/ActionValidator.cs` after the edit; only `FindBestHeal` and `FindBestHealEx` definitions and one call should remain.

- [ ] **Step 5: Update the `CommandExecutor.ExecuteHeal` call site**

In `CommandExecutor.cs` ~line 193, replace:

```csharp
            var ability = ActionValidator.FindBestHealEx(
                owner,
                target ?? owner,                       // Auto-mode needs target; self-heal when no explicit target
                action.HealMode,
                action.HealSources,
                action.HealEnergy,
                out inventorySource);
```

The `target ?? owner` fallback mirrors the existing `target != null ? new TargetWrapper(target) : new TargetWrapper(owner)` pattern further down in the same method — when the rule resolves to no explicit target, the heal is treated as a self-cast.

- [ ] **Step 6: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded`. Common failures: stale `IsHealingSpell` reference (re-grep), missing `using` for `BlueprintFeature` (Task 2). Fix in place, no need to re-commit Task 2.

- [ ] **Step 7: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs WrathTactics/Engine/CommandExecutor.cs
git commit -m "$(cat <<'EOF'
feat(engine): target+pin-aware Heal source selection

FindBestHealEx now takes the heal target and an energy-pin parameter.
Auto mode picks Negative for undead/Dhampir targets, Positive otherwise.
Positive/Negative pins force the energy type regardless of affinity.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Add HealEnergy dropdown to RuleEditorWidget

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.cs` (~lines 393-424 — Heal action branch in `SetupSpellSelector`)

- [ ] **Step 1: Add `HealEnergyLabel` helper**

In `RuleEditorWidget.cs`, near the existing dropdown-label helpers (top of class or wherever similar `*Label` static helpers live — search for `static string` near label definitions). Insert:

```csharp
        static string HealEnergyLabel(HealEnergyType t) {
            switch (t) {
                case HealEnergyType.Auto:     return "Auto";
                case HealEnergyType.Positive: return "Positive";
                case HealEnergyType.Negative: return "Negative";
                default: return t.ToString();
            }
        }
```

(Short labels — the dropdown column is narrow. The dropdown title / surrounding `Energy:` label provides the context.)

- [ ] **Step 2: Replace the Heal-action branch in `SetupSpellSelector`**

In `RuleEditorWidget.cs` ~lines 393-424, replace the entire `if (rule.Action.Type == ActionType.Heal) { ... return; }` block with:

```csharp
            // For Heal action, show HealMode + HealEnergy + HealSources selectors instead of spell picker.
            // Three slots squeezed across the action-row: Mode (0.39-0.50), Energy (0.51-0.65), Sources (0.66-0.88).
            if (rule.Action.Type == ActionType.Heal) {
                var healModeNames = Enum.GetNames(typeof(HealMode)).ToList();
                PopupSelector.Create(row, "HealMode", 0.39f, 0.50f, healModeNames,
                    (int)rule.Action.HealMode, idx => {
                        rule.Action.HealMode = (HealMode)idx;
                        PersistEdit();
                    });

                // Energy pin: Auto (default) / Positive / Negative
                var energyValues = new List<HealEnergyType> {
                    HealEnergyType.Auto, HealEnergyType.Positive, HealEnergyType.Negative,
                };
                var energyLabels = energyValues.Select(HealEnergyLabel).ToList();
                int energyIdx = energyValues.IndexOf(rule.Action.HealEnergy);
                if (energyIdx < 0) energyIdx = 0;
                PopupSelector.Create(row, "HealEnergy", 0.51f, 0.65f, energyLabels, energyIdx, idx => {
                    rule.Action.HealEnergy = energyValues[idx];
                    PersistEdit();
                });

                // Source mask selector — 7 curated combinations (2^3 - "None" sentinel).
                var sourceLabels = new List<string> {
                    "All sources", "Spell only", "Scroll only", "Potion only",
                    "Spell + Scroll", "Spell + Potion", "Scroll + Potion",
                };
                var sourceValues = new List<HealSourceMask> {
                    HealSourceMask.All,
                    HealSourceMask.Spell,
                    HealSourceMask.Scroll,
                    HealSourceMask.Potion,
                    HealSourceMask.Spell  | HealSourceMask.Scroll,
                    HealSourceMask.Spell  | HealSourceMask.Potion,
                    HealSourceMask.Scroll | HealSourceMask.Potion,
                };
                int srcIdx = sourceValues.IndexOf(rule.Action.HealSources);
                if (srcIdx < 0) srcIdx = 0;
                PopupSelector.Create(row, "HealSources", 0.66f, 0.88f, sourceLabels, srcIdx, idx => {
                    rule.Action.HealSources = sourceValues[idx];
                    PersistEdit();
                });
                return;
            }
```

Critical: the new dropdown uses the parent's `PersistEdit()` callback (CLAUDE.md gotcha — direct `ConfigManager.Save()` writes to the wrong file in preset-edit mode).

- [ ] **Step 3: Verify required `using`s**

`HealEnergyType` lives in `WrathTactics.Models` (already imported at the top of `RuleEditorWidget.cs` via `using WrathTactics.Models;` — confirm with `head -20 WrathTactics/UI/RuleEditorWidget.cs`).

- [ ] **Step 4: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded`. If `HealEnergyType` is unresolved, add `using WrathTactics.Models;` to the top of the file.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.cs
git commit -m "$(cat <<'EOF'
feat(ui): HealEnergy dropdown on Heal action

Three-slot layout: Mode / Energy / Sources. Auto is default;
Positive / Negative force the energy type for power-user overrides.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Version bump and on-deck smoke test

**Files:**
- Modify: `WrathTactics/Info.json`
- Modify: `WrathTactics/WrathTactics.csproj`

- [ ] **Step 1: Bump version in `Info.json`**

```bash
sed -i 's/"Version": "1.2.0"/"Version": "1.3.0"/' WrathTactics/Info.json
```

Verify:
```bash
grep '"Version"' WrathTactics/Info.json
```
Expected: `  "Version": "1.3.0",`

- [ ] **Step 2: Bump version in `.csproj`**

```bash
sed -i 's|<Version>1.2.0</Version>|<Version>1.3.0</Version>|' WrathTactics/WrathTactics.csproj
```

Verify:
```bash
grep '<Version>' WrathTactics/WrathTactics.csproj
```
Expected: `		<Version>1.3.0</Version>`

- [ ] **Step 3: Deploy to Steam Deck**

```bash
./deploy.sh
```

Expected: `Build succeeded.`, then `[deploy] OK` or similar success line. Watch for SCP failures (Steam Deck not reachable / wrong path) — re-run after fixing the connection.

- [ ] **Step 4: Smoke-test in-game on the Steam Deck**

Required scenarios:

| Setup | Expected Behavior |
|---|---|
| **Standard Cleric, EmergencySelfHeal default rule, low HP** | Casts Cure on self (regression check — no change from 1.2.0) |
| **Lich-MC post-Ascension, EmergencySelfHeal at low HP, has Inflict spell prepared** | Casts Inflict on self (heals because Type=Undead) |
| **Dhampir companion, EmergencySelfHeal at low HP, Cleric in party with Inflict** | Cleric casts Inflict on Dhampir if Dhampir-detection works (pending Step 1 of Task 2 GUID resolution) |
| **Living target, Heal rule with `HealEnergy = Negative` pinned** | Casts Inflict on target (deals damage — gewollt; pin = harter Override, no safety-net) |
| **Cleric without Inflict spells, Lich-MC ally low HP, EmergencySelfHeal triggered for Lich-target** | Rule does not fire (`FindBestHeal` returns null), falls through to next rule |

After each scenario, check the latest mod log for the cast trace:

```bash
ssh deck-direct "ls -t '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/' | head -1"
ssh deck-direct "tail -30 '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/<latest>'"
```

If a Lich-MC scenario casts Cure instead of Inflict, the detection failed — check the log for `IsNegativeEnergyAffine` traces (none today — add a `Log.Engine.Trace` in the helper if needed for debugging).

- [ ] **Step 5: Backward-compat verification**

Pre-update saved configs must load cleanly. On the Steam Deck:

```bash
ssh deck-direct "ls '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/UserSettings/' | head -5"
```

Pick one `tactics-*.json`, copy locally, inspect:

```bash
scp deck-direct:'<path>/tactics-XXX.json' /tmp/pre-update-config.json
grep -c "HealEnergy" /tmp/pre-update-config.json
```

Expected: `0` (the file pre-dates the feature). After loading the save in-game and saving once via UI changes (toggle a rule on/off), re-pull and re-grep:

Expected after roundtrip: `> 0` (each Heal action now serialises `"HealEnergy": 0`). Existing rule Action / Target / ConditionGroups remain unchanged in shape.

- [ ] **Step 6: Commit version bump**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj
git commit -m "$(cat <<'EOF'
chore: bump version to 1.3.0

Healing-for-undead feature complete; Auto-detect for Lich/Dhampir,
per-rule pin override (Auto/Positive/Negative).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 7: CLAUDE.md update if a new gotcha surfaced**

If the smoke-test surfaced a new gotcha (e.g. wrong NegativeEnergyAffinity GUID, an unexpected Blueprint.Type.name string for Lich-Ascension state, etc.), append it to `wrath-tactics/CLAUDE.md` under the `Game API Gotchas` section before tagging the release. Skip if no new gotcha — release is ready for `/release`.

---

## Self-Review

**Spec coverage:**
- §1 Detection-Logik → Task 2 ✓
- §2 Heal-Source-Klassifikation → Task 3 ✓
- §3 Engine-Filterlogik → Task 4 ✓
- §4 Datenmodell-Erweiterung → Task 1 ✓
- §5 UI — RuleEditorWidget → Task 5 ✓
- §6 Backward-Compat & Migration → Task 6 Step 5 (verification only — no code, by design) ✓
- Acceptance Criteria 1-6 → covered by Task 6 Step 4 + Step 5 ✓
- Version bump (release-process per `wrath-mods/CLAUDE.md`) → Task 6 Steps 1-2 ✓

**Placeholder scan:**
- "PUT_VERIFIED_GUID_HERE" in Task 2 — explicit placeholder requiring Step 1 verification, by design. Engineer fills in or accepts the type-only fallback documented in Step 1.
- No "TBD", "implement later", "handle edge cases" found.

**Type consistency:**
- `HealEnergyType` enum: `Auto / Positive / Negative` — consistent across Tasks 1, 4, 5
- `IsNegativeEnergyAffine(unit)` — single method name, used in Task 4 only
- `ClassifyHeal(blueprint) -> HealEnergyType` — defined Task 3, called Task 4
- `MatchesEnergy(bp)` — local helper, scoped to `FindBestHealEx` only
- `FindBestHealEx` 6-arg signature: `(owner, target, mode, sources, pin, out source)` — consistent in Tasks 4 and 6
- `FindBestHeal` 5-arg signature: `(owner, target, mode, sources, pin)` — consistent

**Additional checks:**
- Heal call sites grep: `FindBestHeal` definition (1), `FindBestHealEx` definition (1), `CanExecute` call (1), `CommandExecutor.ExecuteHeal` call (1), `FindBestHeal` log line (~line 484, 488 — string interpolation, not method calls). After Task 4 the only method invocations of these names should be the two definitions and the two callers.
- No test infrastructure exists — verification is manual smoke-test on the Steam Deck (Task 6 Step 4). This is consistent with the project's CLAUDE.md and the existing release flow.
