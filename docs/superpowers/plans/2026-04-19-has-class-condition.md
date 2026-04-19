# HasClass Condition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `HasClass` condition that matches a subject unit against class groups (Spellcaster / Arcane Caster / Divine Caster / Martial) or specific base/mythic classes, enabling tactic rules like "Enemy HasClass = Spellcaster → Cast Fireball".

**Architecture:** New enum value appended to `ConditionProperty`, matches via new `UnitExtensions.MatchesClassValue(unit, value)` helper. Value is a prefixed string (`group:<name>` or `class:<InternalName>`). Dropdown is populated from a new `ClassProvider` that enumerates `Game.Instance.BlueprintRoot.Progression.AvailableCharacterClasses` + `AvailableCharacterMythics`. UI in `ConditionRowWidget` mirrors the existing `CreatureType` / `Alignment` branch.

**Tech Stack:** C# on .NET Framework 4.8.1, Harmony 2.x via UMM, Unity UI (TMP), Newtonsoft.Json (bundled). No test framework exists in this repo — verification is via build + smoke test on Steam Deck.

**Spec:** `docs/superpowers/specs/2026-04-19-has-class-condition-design.md`

---

## File Structure

- **Create:** `WrathTactics/Engine/ClassProvider.cs` — static provider for the class+group list, cached after first call.
- **Modify:** `WrathTactics/Models/Enums.cs` — append `HasClass` to `ConditionProperty`.
- **Modify:** `WrathTactics/Engine/UnitExtensions.cs` — add `MatchesClassValue(unit, value)`.
- **Modify:** `WrathTactics/Engine/ConditionEvaluator.cs` — add `HasClass` branch to both `EvaluateUnitProperty` (self/ally path) and `MatchesPropertyThreshold` (enemy-pick/count path).
- **Modify:** `WrathTactics/UI/ConditionRowWidget.cs` — add to applicable property lists, add `isHasClass` branch, extend `GetValueOptionsForProperty`.
- **Modify:** `WrathTactics/Info.json` and `WrathTactics/WrathTactics.csproj` — version bump to `0.9.0`.
- **Modify:** `CLAUDE.md` (project root `wrath-tactics/CLAUDE.md`) — add a gotcha note about the HasClass value encoding.

---

## Task 1: Append `HasClass` to enum

**Files:**
- Modify: `WrathTactics/Models/Enums.cs`

- [ ] **Step 1: Append the enum value**

Edit `WrathTactics/Models/Enums.cs`, in the `ConditionProperty` enum, append `HasClass` as the last member (preserving index stability for existing preset JSONs).

Current last three entries:

```csharp
HitDice,
SpellDCMinusSave
```

Change the `SpellDCMinusSave` line to add a comma, then add `HasClass`:

```csharp
HitDice,
SpellDCMinusSave,
HasClass
```

- [ ] **Step 2: Build to confirm no compile error**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.` (zero errors; `findstr` warnings are harmless per CLAUDE.md).

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/Enums.cs
git commit -m "feat(models): append HasClass to ConditionProperty (0.9.0)"
```

---

## Task 2: Create `ClassProvider`

**Files:**
- Create: `WrathTactics/Engine/ClassProvider.cs`

- [ ] **Step 1: Write the provider**

Create `WrathTactics/Engine/ClassProvider.cs` with this exact content:

```csharp
using System.Collections.Generic;
using System.Linq;
using Kingmaker;

namespace WrathTactics.Engine {
    /// <summary>
    /// Static list of class-and-group values for the HasClass condition dropdown.
    /// Groups (Spellcaster/Arcane/Divine/Martial) are sentinel strings; concrete
    /// classes use the blueprint's internal name with trailing "Class" stripped
    /// (locale-independent, stable across game patches).
    /// </summary>
    public static class ClassProvider {
        public struct ClassEntry {
            public string Value;   // "group:spellcaster" or "class:Wizard"
            public string Label;   // "[Group] Spellcaster" / "Wizard" / "Lich (Mythic)"
            public bool IsGroup;
        }

        static List<ClassEntry> cache;

        public static IReadOnlyList<ClassEntry> GetAll() {
            if (cache != null) return cache;

            var list = new List<ClassEntry> {
                new ClassEntry { Value = "group:spellcaster", Label = "[Group] Spellcaster",   IsGroup = true },
                new ClassEntry { Value = "group:arcane",      Label = "[Group] Arcane Caster", IsGroup = true },
                new ClassEntry { Value = "group:divine",      Label = "[Group] Divine Caster", IsGroup = true },
                new ClassEntry { Value = "group:martial",     Label = "[Group] Martial",       IsGroup = true },
            };

            var root = Game.Instance?.BlueprintRoot?.Progression;
            if (root != null) {
                if (root.AvailableCharacterClasses != null) {
                    foreach (var bp in root.AvailableCharacterClasses
                        .Where(b => b != null)
                        .OrderBy(b => StripSuffix(b.name))) {
                        var stripped = StripSuffix(bp.name);
                        list.Add(new ClassEntry {
                            Value = $"class:{stripped}",
                            Label = stripped,
                        });
                    }
                }
                if (root.AvailableCharacterMythics != null) {
                    foreach (var bp in root.AvailableCharacterMythics
                        .Where(b => b != null)
                        .OrderBy(b => StripSuffix(b.name))) {
                        var stripped = StripSuffix(bp.name);
                        list.Add(new ClassEntry {
                            Value = $"class:{stripped}",
                            Label = $"{stripped} (Mythic)",
                        });
                    }
                }
            }

            cache = list;
            return cache;
        }

        public static string StripSuffix(string name) {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            return name.EndsWith("Class") ? name.Substring(0, name.Length - 5) : name;
        }

        /// <summary>Resolve a stored value to its display label; returns the value itself if unknown.</summary>
        public static string GetLabel(string value) {
            if (string.IsNullOrEmpty(value)) return "";
            foreach (var e in GetAll())
                if (e.Value == value) return e.Label;
            return value;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/ClassProvider.cs
git commit -m "feat(engine): ClassProvider enumerates groups + base/mythic classes"
```

---

## Task 3: Add `MatchesClassValue` to `UnitExtensions`

**Files:**
- Modify: `WrathTactics/Engine/UnitExtensions.cs`

- [ ] **Step 1: Extend using directives**

In `WrathTactics/Engine/UnitExtensions.cs`, add two `using` lines at the top so the `System.Linq` and Progression types are available. Final top block:

```csharp
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
```

- [ ] **Step 2: Add `MatchesClassValue`**

At the end of the `UnitExtensions` class body (after `GetSave`), insert:

```csharp
        // Matches a subject unit against a HasClass condition value.
        // Value format: "group:<spellcaster|arcane|divine|martial>" or "class:<InternalName>".
        // Groups resolve via blueprint flags (IsArcaneCaster/IsDivineCaster/IsMythic) or
        // the presence of any Spellbook. Specific classes match the unit's Progression.Classes
        // list against the stripped blueprint name.
        public static bool MatchesClassValue(UnitEntityData unit, string value) {
            if (string.IsNullOrEmpty(value)) return false;
            var classes = unit?.Descriptor?.Progression?.Classes;
            if (classes == null || classes.Count == 0) return false;

            if (value.StartsWith("group:")) {
                var group = value.Substring(6);
                switch (group) {
                    case "spellcaster":
                        return unit.Spellbooks != null && unit.Spellbooks.Any();
                    case "arcane":
                        return classes.Any(c => c?.CharacterClass != null && c.CharacterClass.IsArcaneCaster);
                    case "divine":
                        return classes.Any(c => c?.CharacterClass != null && c.CharacterClass.IsDivineCaster);
                    case "martial":
                        return classes.Any(c => c?.CharacterClass != null
                            && !c.CharacterClass.IsArcaneCaster
                            && !c.CharacterClass.IsDivineCaster
                            && !c.CharacterClass.IsMythic);
                    default:
                        return false;
                }
            }
            if (value.StartsWith("class:")) {
                var stripped = value.Substring(6);
                return classes.Any(c =>
                    c?.CharacterClass != null
                    && ClassProvider.StripSuffix(c.CharacterClass.name) == stripped);
            }
            return false;
        }
```

- [ ] **Step 3: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/UnitExtensions.cs
git commit -m "feat(engine): UnitExtensions.MatchesClassValue (group + class check)"
```

---

## Task 4: Wire `HasClass` into `ConditionEvaluator`

`ConditionEvaluator` has TWO evaluation paths for unit-property conditions:
1. `EvaluateUnitProperty` — used by `Self` / `Ally` subjects (single resolved unit).
2. `MatchesPropertyThreshold` — used by `Enemy*` pickers and `EnemyCount` / `AllyCount` (threshold filter across candidates).

Both paths must gain a `HasClass` branch.

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs`

- [ ] **Step 1: Add `HasClass` to `EvaluateUnitProperty`**

Locate the `EvaluateUnitProperty` method (around line 262). Inside its `switch (condition.Property)`, directly *above* the `default:` label (which is right after the `Alignment` case around line 327–329), insert:

```csharp
                case ConditionProperty.HasClass:
                    bool hasClassMatch = UnitExtensions.MatchesClassValue(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !hasClassMatch : hasClassMatch;
```

- [ ] **Step 2: Add `HasClass` to `MatchesPropertyThreshold`**

Locate the `MatchesPropertyThreshold` method (around line 336). Inside its `switch (condition.Property)`, directly *above* the `default:` label (right after the `Alignment` case around line 399–401), insert:

```csharp
                case ConditionProperty.HasClass:
                    bool hasClassMatch2 = UnitExtensions.MatchesClassValue(unit, condition.Value);
                    return condition.Operator == ConditionOperator.NotEqual ? !hasClassMatch2 : hasClassMatch2;
```

- [ ] **Step 3: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine): HasClass evaluation in both property paths"
```

---

## Task 5: UI — property list entries

Add `HasClass` to the property dropdowns for every subject where `Alignment` is currently offered. Per `GetPropertiesForSubject` at the bottom of `ConditionRowWidget.cs`, that's: `Self`, `Ally`, `AllyCount`, every `Enemy*` single picker (the shared fall-through block), and `EnemyCount`.

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs`

- [ ] **Step 1: Add to `Self`**

Find the `case ConditionSubject.Self:` block. The current list ends with `ConditionProperty.Alignment`. Change it to:

```csharp
                case ConditionSubject.Self:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition,
                        ConditionProperty.SpellSlotsAtLevel, ConditionProperty.SpellSlotsAboveLevel,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass
                    };
```

- [ ] **Step 2: Add to `Ally`**

Update the `Ally` case:

```csharp
                case ConditionSubject.Ally:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass
                    };
```

- [ ] **Step 3: Add to `AllyCount`**

Update the `AllyCount` case:

```csharp
                case ConditionSubject.AllyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass
                    };
```

- [ ] **Step 4: Add to the shared `Enemy*` fall-through block**

Update the shared block that handles `Enemy`, `EnemyBiggestThreat`, `EnemyLowestThreat`, and all the other `Enemy*` pickers:

```csharp
                case ConditionSubject.Enemy:
                case ConditionSubject.EnemyBiggestThreat:
                case ConditionSubject.EnemyLowestThreat:
                case ConditionSubject.EnemyHighestHp:
                case ConditionSubject.EnemyLowestHp:
                case ConditionSubject.EnemyLowestAC:
                case ConditionSubject.EnemyHighestAC:
                case ConditionSubject.EnemyLowestFort:
                case ConditionSubject.EnemyHighestFort:
                case ConditionSubject.EnemyLowestReflex:
                case ConditionSubject.EnemyHighestReflex:
                case ConditionSubject.EnemyLowestWill:
                case ConditionSubject.EnemyHighestWill:
                case ConditionSubject.EnemyHighestHD:
                case ConditionSubject.EnemyLowestHD:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
                        ConditionProperty.HasBuff, ConditionProperty.HasCondition,
                        ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.HasClass
                    };
```

- [ ] **Step 5: Add to `EnemyCount`**

Update the `EnemyCount` case:

```csharp
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.HasClass
                    };
```

- [ ] **Step 6: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): expose HasClass property in condition dropdowns"
```

---

## Task 6: UI — value dropdown for `HasClass`

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs`

`HasClass` uses the same `=` / `!=` operator flow as `CreatureType` and `Alignment`. Value selection is via a `PopupSelector` over `ClassProvider.GetAll()`. The dropdown must appear both in the non-count branch (single subject) and in the count branch (AllyCount/EnemyCount).

- [ ] **Step 1: Add `isHasClass` flag and extend `usesEqOp` in the non-count branch**

Find the block inside the `else {` branch (non-count) starting around line 155. Extend the property flags:

```csharp
                bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
                bool isAlignment = condition.Property == ConditionProperty.Alignment;
                bool isBuffProp = condition.Property == ConditionProperty.HasBuff;
                bool isInCombat = condition.Property == ConditionProperty.IsInCombat;
                bool isHasClass = condition.Property == ConditionProperty.HasClass;
                bool usesEqOp = isHasCondition || isCreatureType || isBuffProp || isAlignment || isHasClass;
                bool needsOperator = !usesEqOp && !isInCombat;
```

- [ ] **Step 2: Add the `HasClass` value-selector branch in the non-count path**

Immediately after the `else if (isHasCondition) { ... }` block (around line 192–199) and before the `else if (condition.Property == ConditionProperty.HasBuff)` block, insert:

```csharp
                } else if (isHasClass) {
                    var entries = ClassProvider.GetAll();
                    var labels = entries.Select(e => e.Label).ToList();

                    if (labels.Count == 0) {
                        // Safety fallback: blueprint root not yet available (e.g. main menu).
                        var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                            0.45, 0.88, condition.Value ?? "", 16f);
                        valueInput.onEndEdit.AddListener(v => {
                            condition.Value = v;
                            ConfigManager.Save();
                        });
                    } else {
                        int idx = -1;
                        for (int i = 0; i < entries.Count; i++) {
                            if (entries[i].Value == condition.Value) { idx = i; break; }
                        }
                        if (idx < 0) { idx = 0; condition.Value = entries[0].Value; }
                        PopupSelector.Create(root, "HasClassValue", 0.45f, 0.88f, labels, idx, v => {
                            condition.Value = entries[v].Value;
                            ConfigManager.Save();
                        });
                    }
```

Note: the existing chain uses `} else if (...)` — make sure the inserted block starts with `} else if (isHasClass) {` so it chains correctly with the preceding branches.

- [ ] **Step 3: Handle `HasClass` in the count branch**

Find the count-subject block (`if (isCountSubject)` around line 70). Inside its property-widget chain (around line 127, the block `else if (condition.Property == ConditionProperty.CreatureType || ... HasCondition)`), extend the conditional to include `HasClass` and add a branch that uses the `ClassProvider`:

Replace:

```csharp
                } else if (condition.Property == ConditionProperty.CreatureType
                    || condition.Property == ConditionProperty.Alignment
                    || condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.HasCondition) {
                    CreateEqOperator(root, 0.58f, 0.64f, "CountEqOp");

                    if (condition.Property == ConditionProperty.HasBuff) {
                        CreateBuffSelector(root, 0.65f, 0.88f);
                    } else {
                        var valueOptions = GetValueOptionsForProperty(condition.Property);
                        int valIdx = valueOptions.IndexOf(condition.Value);
                        if (valIdx < 0) { valIdx = 0; condition.Value = valueOptions[0]; }
                        PopupSelector.Create(root, "CountValueDropdown", 0.65f, 0.88f, valueOptions, valIdx, v => {
                            condition.Value = valueOptions[v];
                            ConfigManager.Save();
                        });
                    }
                }
```

with:

```csharp
                } else if (condition.Property == ConditionProperty.CreatureType
                    || condition.Property == ConditionProperty.Alignment
                    || condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.HasCondition
                    || condition.Property == ConditionProperty.HasClass) {
                    CreateEqOperator(root, 0.58f, 0.64f, "CountEqOp");

                    if (condition.Property == ConditionProperty.HasBuff) {
                        CreateBuffSelector(root, 0.65f, 0.88f);
                    } else if (condition.Property == ConditionProperty.HasClass) {
                        var entries = ClassProvider.GetAll();
                        var labels = entries.Select(e => e.Label).ToList();
                        int idx = -1;
                        for (int i = 0; i < entries.Count; i++) {
                            if (entries[i].Value == condition.Value) { idx = i; break; }
                        }
                        if (idx < 0 && entries.Count > 0) { idx = 0; condition.Value = entries[0].Value; }
                        if (labels.Count == 0) {
                            var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                                0.65, 0.88, condition.Value ?? "", 16f);
                            valueInput.onEndEdit.AddListener(v => {
                                condition.Value = v;
                                ConfigManager.Save();
                            });
                        } else {
                            PopupSelector.Create(root, "CountHasClassValue", 0.65f, 0.88f, labels, idx, v => {
                                condition.Value = entries[v].Value;
                                ConfigManager.Save();
                            });
                        }
                    } else {
                        var valueOptions = GetValueOptionsForProperty(condition.Property);
                        int valIdx = valueOptions.IndexOf(condition.Value);
                        if (valIdx < 0) { valIdx = 0; condition.Value = valueOptions[0]; }
                        PopupSelector.Create(root, "CountValueDropdown", 0.65f, 0.88f, valueOptions, valIdx, v => {
                            condition.Value = valueOptions[v];
                            ConfigManager.Save();
                        });
                    }
                }
```

- [ ] **Step 4: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): HasClass value dropdown in single + count branches"
```

---

## Task 7: Version bump

**Files:**
- Modify: `WrathTactics/Info.json`
- Modify: `WrathTactics/WrathTactics.csproj`

- [ ] **Step 1: Bump `Info.json`**

Edit `WrathTactics/Info.json` — change `"Version": "0.8.0"` to `"Version": "0.9.0"`.

- [ ] **Step 2: Bump `.csproj`**

Edit `WrathTactics/WrathTactics.csproj` — change `<Version>0.8.0</Version>` to `<Version>0.9.0</Version>` (only the one at line 7 — line 37 is a ZipDirectory reference that reads `$(Version)`).

- [ ] **Step 3: Build and verify the release zip name**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded.` and `ls WrathTactics/bin/` shows `WrathTactics-0.9.0.zip`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj
git commit -m "chore(release): bump version to 0.9.0"
```

---

## Task 8: Update `CLAUDE.md` with gotcha note

**Files:**
- Modify: `CLAUDE.md` (the `wrath-tactics/CLAUDE.md` at the project root, not the parent-directory one)

- [ ] **Step 1: Append a bullet under the existing `## Gotchas` section**

Find the `## Gotchas` section in `CLAUDE.md`. After the existing `ResolvedTarget` bullet and before the `Rule-scoped ambient statics` bullet (or at the end of the section — location isn't critical, but keep it in Gotchas), add:

```markdown
- **HasClass condition value encoding** (since 0.9.0): `Condition.Value` for `ConditionProperty.HasClass` is a prefixed string: `group:<spellcaster|arcane|divine|martial>` for groups, `class:<InternalName>` for specific classes (blueprint `name` with `Class` suffix stripped — e.g. `class:Wizard`, `class:Lich`). Never store the localized display name here — `BlueprintCharacterClass.name` is the code identifier and locale-independent. `ClassProvider.GetAll()` is the single source of truth for the dropdown list; `UnitExtensions.MatchesClassValue` is the single matching helper. Group `spellcaster` = `unit.Spellbooks.Any()`; `arcane`/`divine` read `BlueprintCharacterClass.IsArcaneCaster`/`IsDivineCaster` on each `Progression.Classes` entry; `martial` = any class that is neither arcane, divine, nor mythic.
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude): HasClass value encoding gotcha"
```

---

## Task 9: Smoke test on Steam Deck

There is no automated test framework in this repo; verification is manual via the deployed mod.

**Files:** none.

- [ ] **Step 1: Deploy**

Run: `./deploy.sh`
Expected: build output + `scp` transfer of `WrathTactics.dll` and `Info.json` to `deck-direct:/run/media/.../Mods/WrathTactics/`.

- [ ] **Step 2: Launch Wrath on the Steam Deck, load a save with an active caster in the party (Seelah + Nenio is fine), and open the Tactics panel (`Ctrl+T`).**

- [ ] **Step 3: Create a rule — Group=Spellcaster path**

On a companion, add a rule:
- Condition: `Enemy HasClass = [Group] Spellcaster`
- Action: `Cast Spell` → any AoE (Fireball, Hellfire Ray, etc.) or `Attack Target`
- Target: `ConditionTarget` (or `EnemyBiggestThreat` — for smoke-test the label match is what we're verifying)

- [ ] **Step 4: Enter combat against a mixed group (look for an encounter with a Cultist Leader / any spellcaster enemy)**

Expected behaviour:
- Tactic-evaluator log (`Mods/WrathTactics/Logs/wrath-tactics-*.log`) shows the rule matching against the caster enemy and NOT matching against non-caster enemies.
- Rule fires within ~1 tick after the spellcaster enemy becomes visible.

Pull latest log:

```bash
ssh deck-direct "ls -t '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/' | head -1"
```

Then copy and inspect:

```bash
scp "deck-direct:/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/<filename>" /tmp/
grep -E "HasClass|MatchesClassValue|Rule.*match" /tmp/<filename> | head -40
```

- [ ] **Step 5: Verify Arcane vs Divine split**

Change the rule's value to `[Group] Divine Caster`. Enter combat with a divine-caster enemy (Cleric, Inquisitor). Rule should fire on them; should NOT fire on a wizard-only encounter.

- [ ] **Step 6: Verify specific-class match**

Change the rule's value to `Wizard`. Rule should fire on wizard enemies and not on sorcerers, clerics, etc.

- [ ] **Step 7: Verify persistence**

Save game, quit to main menu, reload. Open Tactics panel. The rule's value dropdown should still display the same class/group label (no revert to first entry).

- [ ] **Step 8: Fix any regressions surfaced by the smoke test**

If the rule never fires or matches wrong units, pull the log and inspect `UnitExtensions.MatchesClassValue` traces. Iterate — do NOT skip this step. Only mark this task complete when all six sub-checks pass on device.

---

## Self-Review Checklist

**Spec coverage** — mapping spec sections to tasks:
- §"Data Model" → Task 1 (enum append), Task 2 (ClassProvider value shape).
- §"Engine" → Task 2 (ClassProvider), Task 3 (MatchesClassValue), Task 4 (ConditionEvaluator branches in both paths).
- §"Class List Provider" → Task 2 in full.
- §"UI — ConditionRowWidget" → Task 5 (property list entries), Task 6 (value selector in both single + count branches).
- §"Testing Plan" → Task 9 (all 6 numbered tests mapped to sub-steps 3–7).
- §"Non-Goals" — nothing to implement; no tasks required.
- §"Risks" — documented in CLAUDE.md via Task 8.

**Placeholder scan:** None. Every step shows exact code, exact commands, or exact file edits.

**Type consistency:**
- `MatchesClassValue(UnitEntityData, string)` declared in Task 3, called with `unit, condition.Value` args in Task 4. ✓
- `ClassProvider.GetAll()` return type `IReadOnlyList<ClassEntry>` consumed via indexer + `Count` in Task 6. ✓
- `ClassEntry.Value` / `.Label` / `.IsGroup` — all three fields used consistently. ✓
- `ClassProvider.StripSuffix(string)` declared in Task 2, called from Task 3. ✓
- `ConditionProperty.HasClass` — declared in Task 1, referenced in Tasks 4, 5, 6. ✓
