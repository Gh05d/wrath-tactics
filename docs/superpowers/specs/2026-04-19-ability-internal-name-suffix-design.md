# Ability Dropdown — Internal Name Suffix

## Motivation

Kineticist (and other spec classes) expose multiple abilities with the same localized display name. Example seen in `blueprints.zip`:

- `FireBlastAbility.jbp` — the ranged Fire Blast
- `KineticBladeFireBlastAbility.jbp` — the Kinetic-Blade activatable toggle
- `ExplodingArrowsFireBlastAbility.jbp`, `PiercingArrowsFireBlastAbility.jbp` — Kinetic Sharpshooter archetype variants
- `KineticBladeFireBlastBurnAbility.jbp` — Burn-on version

All may display as "Fire Blast" in the game. When a user sets up a tactic rule, the dropdown shows indistinguishable entries.

## Solution

Append the blueprint's internal code name (Unity `Object.name`, trailing `Ability` stripped) to every dropdown row in `SpellDropdownProvider`.

### Format

`{existing label} ({internal name})`

Examples:

| Before | After |
|---|---|
| `[L3] Fireball` | `[L3] Fireball (Fireball)` |
| `Fire Blast` | `Fire Blast (FireBlast)` |
| `Fire Blast` (Kinetic Blade toggle) | `Fire Blast (KineticBladeFireBlast)` |
| `Command: Halt` | `Command: Halt (CommandHalt)` |
| `Potion of CMW (Potion)` | `Potion of CMW (Potion) (CureModerateWounds)` |

### Internal-name source

- Regular abilities and variants: `bp.name`, where `bp` is the blueprint being rendered (the variant's own blueprint, not the parent, when rendering a variant).
- Activatables: `bp.name`.
- Items: the ability's blueprint `name` (not the item's) — keeps the suffix aligned with the spell behavior, not the container.
- Fallback: when `bp.name` is null/empty, omit the suffix (keep the existing label unchanged).

### `Ability` suffix stripping

Apply in a single helper:

```csharp
static string StripAbilitySuffix(string name) {
    if (string.IsNullOrEmpty(name)) return name ?? "";
    return name.EndsWith("Ability") ? name.Substring(0, name.Length - 7) : name;
}
```

Rationale: `Ability` is present on nearly every class-ability blueprint and adds no disambiguation value. `KineticBladeFireBlastBurnAbility` → `KineticBladeFireBlastBurn` is still unique against `KineticBladeFireBlastAbility` → `KineticBladeFireBlast`.

## Implementation Surface

One helper + four call sites in `WrathTactics/UI/SpellDropdownProvider.cs`.

### Helper

```csharp
static string FormatWithInternal(string displayName, BlueprintScriptableObject bp) {
    if (bp == null || string.IsNullOrEmpty(bp.name)) return displayName;
    return $"{displayName} ({StripAbilitySuffix(bp.name)})";
}
```

### Call-site changes

All four getters already build a `displayName` string before constructing `SpellEntry`. Wrap each `displayName` with `FormatWithInternal(displayName, <bp>)`:

1. **`GetSpells`** — two branches (with variants / without):
   - Variant branch: `bp = variant` (the variant blueprint).
   - Non-variant branch: `bp = spell.Blueprint`.
   - Custom-spells branch: `bp = spell.Blueprint`.
2. **`GetAbilities`** — two branches:
   - Variant branch: `bp = variant`.
   - Plain branch: `bp = ability.Blueprint`.
3. **`GetActivatables`** — single branch: `bp = activatable.Blueprint`.
4. **`GetItemAbilities`** — single branch: `bp = ability.Blueprint` (the ability, not the item).

## Persistence

No changes. `SpellEntry.Guid` (used as the config key) is the same as before — this is a display-only change. Existing rules keep working; only their visible labels get more informative.

## Non-Goals

- No per-ability tags like `[Ranged]` / `[AoE]` / `[Cone]` / `[Burn]`. Deferred; can be added on top of the internal-name suffix later if the technical name alone proves insufficient.
- No hover tooltip with blueprint description. Unity TMP in our custom popup has no tooltip support; nontrivial.
- No deduplication / "only show suffix when display name collides". Always-on keeps logic simple and the suffix identifies the exact blueprint — useful even without a collision when debugging rule configs.

## Risks

- Long labels may wrap or get truncated in narrow dropdown cells. Accepted — existing `[Lx]` + `Name: Variant` strings are already sometimes long; users can make the tactics panel wider.
- If an Owlcat patch renames a blueprint's internal `name`, rules keep working (GUID unchanged) but the suffix shifts. Acceptable — internal names are stable across normal patches, and a rename would only affect the cosmetic label.
