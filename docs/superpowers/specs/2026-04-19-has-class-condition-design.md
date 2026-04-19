# HasClass Condition — Design

## Motivation

Users want to target enemies by class role, e.g. "Enemy is a Spellcaster → cast Fireball". Current conditions expose HP, AC, saves, creature type, and alignment, but no class/role filter. The primary use case is broad groups (Spellcaster, Martial); individual classes (Wizard, Cleric) are secondary.

## User-Facing Behavior

On any subject where `CreatureType` / `Alignment` are already available (`Self`, `Ally`, `Enemy`, all `Enemy*`-specific pickers), a new property `HasClass` appears. Operator is `=` / `!=`. The value dropdown lists:

1. Four groups (prefix `[Group]` in label): Spellcaster, Arcane Caster, Divine Caster, Martial
2. All player-available base classes (alphabetical), e.g. Wizard, Cleric, Fighter
3. All mythic paths (suffix `(Mythic)` in label), e.g. Lich (Mythic), Azata (Mythic)

Example rule: `Enemy HasClass = [Group] Spellcaster` AND `Enemy HpPercent > 50` → `Cast Fireball at Enemy`.

## Data Model

Append to `ConditionProperty` enum:

```csharp
public enum ConditionProperty {
    ...
    SpellDCMinusSave,
    HasClass,   // new, index 17
}
```

No new fields on `Condition`. The existing `Value` string holds a prefixed identifier:

- `group:spellcaster` / `group:arcane` / `group:divine` / `group:martial`
- `class:<InternalName>` where `InternalName` is the blueprint's Unity-object name with trailing `Class` stripped (e.g. `class:Wizard`, `class:Cleric`, `class:Lich`)

### Why internal names over GUIDs or localized strings

- Localized display names drift with game locale and translation updates.
- Blueprint GUIDs are stable but opaque in JSON and break if Owlcat ever re-issues an asset.
- `BlueprintScriptableObject.name` is a code identifier — English by convention, never localized, stable across game patches (classes don't get renamed internally).

### Persistence stability

- New enum value is appended last → pre-existing preset/config JSON stays readable (numeric enum indices, per CLAUDE.md gotcha).
- No migration needed.

## Engine

### `UnitExtensions.MatchesClassValue(unit, value)`

Single entry point used by `ConditionEvaluator`:

```csharp
public static bool MatchesClassValue(UnitEntityData unit, string value) {
    if (string.IsNullOrEmpty(value)) return false;
    var classes = unit?.Descriptor?.Progression?.Classes;
    if (classes == null || classes.Count == 0) return false;

    if (value.StartsWith("group:")) {
        switch (value.Substring(6)) {
            case "spellcaster": return unit.Spellbooks.Any();
            case "arcane":      return classes.Any(c => c.CharacterClass.IsArcaneCaster);
            case "divine":      return classes.Any(c => c.CharacterClass.IsDivineCaster);
            case "martial":     return classes.Any(c =>
                !c.CharacterClass.IsArcaneCaster
                && !c.CharacterClass.IsDivineCaster
                && !c.CharacterClass.IsMythic);
        }
        return false;
    }
    if (value.StartsWith("class:")) {
        var stripped = value.Substring(6);
        return classes.Any(c =>
            ClassProvider.StripSuffix(c.CharacterClass.name) == stripped);
    }
    return false;
}
```

Group semantics:

- **Spellcaster** = has at least one `Spellbook` instance. Catches Alchemist (extracts), Wizard, Cleric, etc. Intentionally misses monsters with SLAs only.
- **Arcane** = any class with `IsArcaneCaster = true` in the unit's `Progression.Classes`. Independent of whether that class has spell slots at current level (low-level Paladin still counts as Divine).
- **Divine** = same pattern with `IsDivineCaster`.
- **Martial** = at least one class that is neither arcane, nor divine, nor mythic. Bard is `IsArcaneCaster` so not martial. Rogue/Fighter/Barbarian/Slayer are martial.

### `ConditionEvaluator`

Add a `HasClass` branch in the property switch for subject-resolved units (`Self`, `Ally*`, `Enemy*`). The branch calls `UnitExtensions.MatchesClassValue(subjectUnit, condition.Value)` and applies the `Equal`/`NotEqual` operator. Reuses the existing resolved-subject pipeline — no new ambient statics.

## Class List Provider

New file `Engine/ClassProvider.cs`:

```csharp
public static class ClassProvider {
    public struct ClassEntry {
        public string Value;    // "group:spellcaster" or "class:Wizard"
        public string Label;    // "[Group] Spellcaster" or "Wizard" or "Lich (Mythic)"
        public bool IsGroup;
    }

    static List<ClassEntry> cache;

    public static IReadOnlyList<ClassEntry> GetAll() {
        if (cache != null) return cache;
        var list = new List<ClassEntry> {
            new() { Value = "group:spellcaster", Label = "[Group] Spellcaster",   IsGroup = true },
            new() { Value = "group:arcane",      Label = "[Group] Arcane Caster", IsGroup = true },
            new() { Value = "group:divine",      Label = "[Group] Divine Caster", IsGroup = true },
            new() { Value = "group:martial",     Label = "[Group] Martial",       IsGroup = true },
        };

        var root = Game.Instance?.BlueprintRoot?.Progression;
        if (root != null) {
            foreach (var bp in root.AvailableCharacterClasses.OrderBy(b => StripSuffix(b.name)))
                list.Add(new ClassEntry {
                    Value = $"class:{StripSuffix(bp.name)}",
                    Label = StripSuffix(bp.name),
                });
            foreach (var bp in root.AvailableCharacterMythics.OrderBy(b => StripSuffix(b.name)))
                list.Add(new ClassEntry {
                    Value = $"class:{StripSuffix(bp.name)}",
                    Label = $"{StripSuffix(bp.name)} (Mythic)",
                });
        }
        cache = list;
        return cache;
    }

    public static string StripSuffix(string name) =>
        name.EndsWith("Class") ? name.Substring(0, name.Length - 5) : name;
}
```

Rationale for `AvailableCharacterClasses` + `AvailableCharacterMythics` over `BlueprintsCache.ForEachLoaded`: these enumerables are populated eagerly by `ProgressionRoot` and don't rely on lazy blueprint loading (which only returns what's in memory — the known-bad path called out in `wrath-mods/CLAUDE.md`). Scope is "PC-playable classes" — sufficient for tactic-rule authoring, since users don't write rules against NPC-exclusive monster classes.

Cache is frozen after first call. Total entries ≈ 4 groups + ~30 base classes + ~7 mythics ≈ 41 — comfortable in a `PopupSelector`, no search overlay needed.

## UI — `ConditionRowWidget`

Mirror the existing `CreatureType` / `Alignment` pattern:

1. Add `HasClass` to the property-option lists for every subject where `CreatureType` and `Alignment` appear (grep for `ConditionProperty.CreatureType` in `ConditionRowWidget.cs` — add `ConditionProperty.HasClass` in the same positions).
2. Introduce a `isHasClass` flag: `bool isHasClass = condition.Property == ConditionProperty.HasClass;`
3. Extend `usesEqOp`: `bool usesEqOp = isHasCondition || isCreatureType || isBuffProp || isAlignment || isHasClass;`
4. After the existing `isAlignment` / `isHasCondition` branches, add:

   ```csharp
   } else if (isHasClass) {
       var entries = ClassProvider.GetAll();
       var labels = entries.Select(e => e.Label).ToList();
       int idx = entries.ToList().FindIndex(e => e.Value == condition.Value);
       if (idx < 0) { idx = 0; condition.Value = entries[0].Value; }
       PopupSelector.Create(root, "HasClassValue", 0.45f, 0.88f, labels, idx, v => {
           condition.Value = entries[v].Value;
           PersistEdit();
           RebuildAll();
       });
   }
   ```

5. Extend `GetValueOptionsForProperty` with a case for `HasClass` (returns the label list — keeps parity with how CreatureType/Alignment populate the options list).

Default initial value when user first switches property to `HasClass`: `"group:spellcaster"` (first entry, most useful default).

## Testing Plan

Manual smoke test on Steam Deck via `./deploy.sh`:

1. **Group — Spellcaster**: Rule `Enemy HasClass = [Group] Spellcaster → Cast Fireball at Enemy`. Enter combat with mixed Wizard + melee enemies. Expected: rule fires on visible-enemy pass, Fireball centered on the Wizard (or point-target path if configured).
2. **Group — Divine split**: Rule `Enemy HasClass = [Group] Divine Caster`. Expected: fires against Cleric enemies, not against Wizards.
3. **Specific class**: Rule `Enemy HasClass = Wizard`. Expected: fires against Wizard enemies, not against Sorcerers.
4. **Equal/NotEqual flip**: Rule `Enemy HasClass != [Group] Spellcaster → Attack Target`. Expected: picks non-caster enemies first.
5. **Combined condition**: Group `Enemy HasClass = Spellcaster` AND `Enemy HpPercent > 50` → AND-logic within group, rule only fires when both true.
6. **Persistence**: Save rule, reload game, verify rule persists with correct class label displayed.
7. **Session log** (`Mods/WrathTactics/Logs/wrath-tactics-…log`): `TacticsEvaluator` trace lines should show the rule matching on the intended unit and nothing else.

## Non-Goals

- No archetype filtering. Archetypes (`BlueprintArchetype`) are skipped — enemies rarely carry archetypes, and including them would bloat the dropdown to 200+ entries without proportional value.
- No `MeleeClass` / `RangedClass` / `TankClass` tags. These are unclear (Ranger covers both, a Magus is melee-arcane) and would drag design into role-tagging territory beyond what "class" means.
- No multi-select ("has any of {Wizard, Sorcerer}"). Users needing this can write two OR-ed condition groups.
- No class-level filter ("has ≥5 levels in Wizard"). Out of scope; revisit if a use case emerges.

## Risks

- `BlueprintScriptableObject.name` assumption: if Owlcat ever renames a base class's internal blueprint name (very unusual for gameplay classes), saved rules referencing that class stop matching. Mitigation: accepted — would be a Wrath engine break affecting far more than this mod.
- `AvailableCharacterMythics` may include paths not yet unlocked in the current playthrough. That's fine — users can still author rules against mythic enemies regardless of their own mythic path.
- `IsArcaneCaster` / `IsDivineCaster` flags: trusting Owlcat's own class tagging. Any mistagged class (none observed) would show under the wrong group.
