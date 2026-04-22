# CastSpell Sources — Design

**Date:** 2026-04-21
**Feature target:** Wrath Tactics v1.0.0

## Context

The `CastSpell` action today only ever resolves spells out of a unit's spellbook. When slots run out, the rule goes silent until a rest refills them. The `Heal` action already has a `HealSourceMask` (Spell / Scroll / Potion) that lets the engine fall back onto inventory items when spellbook heals are spent. This design adds the same concept to `CastSpell`, so a rule like "Cast Magic Missile on Enemy Highest HP" can keep firing from a stack of scrolls after the caster's prepared slots are gone.

## Scope

- **In scope:** New `Sources` mask on `ActionDef`, consumed only by `ActionType.CastSpell`.
- **Source categories:** Spellbook slots, Scrolls, Potions. Wands are searched *implicitly* alongside Spellbook (mirrors Heal — Wand-in-quickslot with charges available). No separate `Wand` bit.
- **Out of scope (deliberate):**
    - Unifying `HealSourceMask` and new `SpellSourceMask` into one enum. Heal code stays untouched. Future refactor possible.
    - Multi-select checkbox UI. Mirrors the existing curated single-select dropdown from Heal for consistency.
    - Wand as an independently togglable source.
    - Global inventory wand lookup (only quickslotted wands count, mirroring Heal).

## Model

### New enum: `SpellSourceMask` (Models/Enums.cs)

```csharp
[System.Flags]
public enum SpellSourceMask {
    None   = 0,
    Spell  = 1,   // Spellbook slots + implicit wand-in-quickslot fallback
    Scroll = 2,
    Potion = 4,
    All    = Spell | Scroll | Potion,
}
```

Structurally identical to `HealSourceMask` but separate, to keep the Heal code path untouched. Consolidation into a single shared enum is a later refactor and explicitly not in this spec.

### New field on `ActionDef` (Models/TacticsRule.cs)

```csharp
[JsonProperty] public SpellSourceMask Sources { get; set; } = SpellSourceMask.All;
```

**Default = `All`** for *all* rules, including pre-existing ones loaded from save files that lack the field. Rationale: user's explicit choice (`Default A`); trades zero-surprise legacy behavior for the more useful default going forward.

**Migration impact:** existing `CastSpell` rules stored before v1.0 will, on first load, start drawing from Scrolls/Potions and the implicit Wand-in-quickslot path. Release notes must flag this so users can dial back rules that should remain Spellbook-only.

## Engine Resolver

New method in `Engine/ActionValidator.cs`:

```csharp
public static AbilityData FindCastSpellSource(
    UnitEntityData owner,
    TargetWrapper target,        // needed for Potion self-only filter
    string compoundKey,          // spell GUID + level + variant + metamagic
    SpellSourceMask mask,
    out ItemEntity inventorySource)
```

Returns the `AbilityData` to cast and the inventory `ItemEntity` to consume (null for Spellbook / Wand sources — those consume via the command pipeline).

### Resolution order (fixed, not user-configurable)

1. **Spellbook slot** (requires `Spell` bit)
    - `FindAbility(owner, compoundKey)` — reuses existing lookup, respects variant + metamagic + slot-level.
    - `GetAvailableForCastSpellCount(ability) > 0` → pick. `inventorySource = null`.
2. **Wand in quickslot** (requires `Spell` bit, implicit fallback like Heal)
    - Scan quickslots for `BlueprintItemEquipmentUsable` where ability blueprint GUID matches the parsed `compoundKey`, charges remaining.
    - Strict match: metamagic mask on the rule key must equal wand's ability metamagic (wands never carry metamagic in Wrath → rules with metamagic won't match a wand).
    - Pick wand's `AbilityData`. `inventorySource = null` (wand charge decrements through the cast command).
3. **Scroll from inventory** (requires `Scroll` bit)
    - Iterate `Game.Instance.Player.Inventory` for usables whose ability blueprint GUID matches the rule's key.
    - **Strict match on blueprint GUID + variant + metamagic.** Rules with metamagic / variants effectively won't find scrolls (Wrath ships none with metamagic).
    - UMD gate mirrored from Heal: if the spell is not on the caster's class list, run the UMD skill check against the scroll's DC. Fail → scroll is **not** selected (silent skip; no "risky burn" fallback, unlike Heal).
    - Synthesize `AbilityData` with item's CL/SpellLevel overrides. `inventorySource = ItemEntity` for manual consume.
4. **Potion from inventory** (requires `Potion` bit)
    - Same inventory scan, filtered to `UsableItemType.Potion`.
    - **Extra filter: self-only.** Only selected when `target.Unit == owner`. Otherwise skip silently (would be noisy to warn on every AoE rule tick).
    - `inventorySource = ItemEntity`.

### Matching semantics (strict)

Rule-side keys already encode `spellGuid[@Llevel][>Vvariant][#metamagicMask]` via `SpellDropdownProvider.MakeKey`. Item-side matches must agree on **blueprint GUID + variant GUID + metamagic mask**. No silent drop-metamagic fallback. Documented consequence: metamagic and variant rules effectively lock to Spellbook only.

## UI

In `UI/RuleEditorWidget.cs`, inside the `ActionType.CastSpell` action-row branch:

- Add a `Sources:` label + `PopupSelector` with the same seven curated combinations used by Heal's `HealSources`:
    - All sources / Spell only / Scroll only / Potion only / Spell + Scroll / Spell + Potion / Scroll + Potion
- Single-select, initial index resolved from current `Sources` value; default to the `All` entry for freshly created rules.
- Persist via the existing `onChanged` callback (handles preset-mode vs character-mode routing automatically).
- Layout slot: next to the existing spell-picker dropdown; exact anchors TBD during implementation but must match the visual weight of Heal's source dropdown.

## Execution & Consumption

In `Engine/CommandExecutor.cs` CastSpell branch:

1. Call `FindCastSpellSource`. If null → `CanExecute` returns false, rule falls through to next priority.
2. If `inventorySource == null` (Spellbook or Wand): queue via `UnitUseAbility.CreateCastCommand(ability, target)` — existing code path, no change.
3. If `inventorySource != null` (Scroll or Potion): trigger `RuleCastSpell` directly (synthetic AbilityData path), then call `ConsumeInventoryItem(inventorySource, owner)` — mirrors Heal's inventory-source path (`CommandExecutor.cs:138–148`).

`ActionValidator.CanExecute` for CastSpell: pre-checks `FindCastSpellSource(...) != null` so that `TryExecuteRules` correctly falls through when no source is available (validator strictness gotcha — see CLAUDE.md).

## Error Handling & Edge Cases

| Case | Behavior |
| --- | --- |
| No source available | Resolver returns null, `CanExecute` false, rule skipped. |
| Scroll UMD check fails | Scroll skipped silently; resolver continues to next source bit. No burn-on-risk. |
| Potion with non-self target | Potion skipped silently. |
| Metamagic / variant rule | Scrolls/Potions/Wands practically unreachable (strict match); falls through to Spellbook or skips. |
| Wand in quickslot with 0 charges | Skipped; resolver continues. |
| Spellbook has slot but spell not prepared (prepared caster) | Existing `GetAvailableForCastSpellCount` returns 0, skipped. |

## Release Notes (v1.0.0)

> **Behavior change — CastSpell default sources.** Existing `CastSpell` rules now default to using all available sources (Spellbook slots, quickslotted Wands, Scrolls, Potions). If you want a rule to stay Spellbook-only, open it in the Tactics panel and change the new `Sources:` dropdown to "Spell only". Newly created rules start with the same `All sources` default.

## Test Plan (manual smoke on Steam Deck)

1. **Spellbook path (baseline):** Wizard with Magic Missile prepared, rule `CastSpell Magic Missile on Enemy Highest HP`, `Sources = All`. Fires from spellbook, slot consumed.
2. **Scroll fallback:** Same wizard, 0 Magic Missile slots left, 1 Scroll of Magic Missile in inventory. Rule fires the scroll, inventory count drops.
3. **Potion self-only:** Cleric, rule `CastSpell Bless on Self`, `Sources = Potion only`, 1 Potion of Bless. Fires; if target flipped to Lowest HP ally, rule silently skips.
4. **UMD gate:** Paladin (no Magic Missile on class list), Scroll of Magic Missile, UMD too low. Rule skipped (no scroll burn).
5. **Metamagic strict:** Sorcerer with Empowered Magic Missile rule (Slot 2), 0 spell slots, Scroll of MM in inventory. Rule skipped (strict match rejects MM scroll against Empowered rule).
6. **All empty:** No slots, no scrolls, no potions. Rule skips, next-priority rule evaluated.
7. **Migration:** Pre-v1.0 save with an existing CastSpell rule → load → verify rule's `Sources` reads as `All` in the UI.

## Files Touched

- `Models/Enums.cs` — new `SpellSourceMask` enum.
- `Models/TacticsRule.cs` — new `Sources` field on `ActionDef`.
- `Engine/ActionValidator.cs` — new `FindCastSpellSource` + resolver-aware `CanExecute` for `CastSpell`.
- `Engine/CommandExecutor.cs` — CastSpell branch switched to resolver + dual-path execution (Spellbook/Wand vs inventory).
- `UI/RuleEditorWidget.cs` — `Sources` dropdown in the CastSpell action section.
- `Info.json` + `WrathTactics.csproj` — version bump to 1.0.0.
- Release notes — migration warning.

## Open Items for Implementation Phase

- Exact UI layout anchors for the `Sources:` dropdown — decided during plan/implementation, matching the visual weight of Heal's dropdown.
- Whether to add a lightweight trace line on successful source fallback (e.g. `Engine.Trace: cast Magic Missile from Scroll, N-1 left in stack`). Convenient for debugging inventory drain, off by default.
