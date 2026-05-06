# Wrath Tactics — Metamagic Rod on Cast

**Date:** 2026-05-06
**Target version:** v1.11.0
**Type:** Minor feature, additive only

## Goal

Let a CastSpell rule optionally tag a metamagic type. At cast time, if the
caster has a matching rod equipped *and quickslotted*, the engine activates
the rod's ability before the cast — applying the metamagic and spending one
rod charge. If no usable rod is available, the cast still happens at its
normal level (silent fallback). The user keeps full control over rod usage
(charges are scarce, so opt-in per rule).

## Scope and Non-Goals

### In scope

- A `MetamagicRod` field on the CastSpell action: optional `Metamagic?`.
- Engine-side resolver that finds a usable rod for `(unit, ability, metamagic)`.
- Activating the rod's `BlueprintActivatableAbility` before the cast so the
  engine itself applies metamagic + spends the charge.
- UI: an extra "Rod" dropdown next to the spell picker when CastSpell is
  selected, plus a small `ⓘ` info icon explaining the quickslot requirement.

### Explicitly out of scope (this iteration)

- **Metamagic-as-condition** — the original ask included "if Quicken
  available, then …". The user concluded that with rod-on-cast working,
  conditions are redundant: the cast itself naturally falls back when no
  rod is around. Conditions can come back as a separate spec later.
- **Auto-quickslot the rod**. Modifying the player's loadout would be too
  invasive. The user is responsible for putting the rod in a quickslot once.
- **Combining multiple rod metamagics on one cast**. Vanilla Wrath does
  not allow stacking; we follow that.
- **Rod-aware spell pickers** (e.g. greying out rod choices that don't
  apply to the current spell). Best-effort fallback at cast time is enough.

## Background — engine API (verified by IL inspection)

- `Kingmaker.Designers.Mechanics.Facts.MetamagicRodMechanics` — fact
  component on rod blueprints. Public fields: `Metamagic Metamagic`,
  `bool ForceIgnoreSpellResist`, `int MaxSpellLevel`. Properties:
  `RodAbility` (`BlueprintActivatableAbility`), `AbilitiesWhiteList`
  (`BlueprintAbility[]`).
- `Kingmaker.UnitLogic.Parts.UnitPartSpecialMetamagic` — engine populates
  this UnitPart automatically when a rod's fact turns on. Holds
  `List<(EntityFact rodFact, MetamagicRodMechanics mech)>`.
- `MetamagicRodMechanics.IsSuitableAbility(AbilityData)` — engine-authoritative
  match: combines the rod's `MaxSpellLevel`, `AbilitiesWhiteList`, and any
  other vanilla constraints.
- Cast flow (player's perspective): rod sits in quickslot → player clicks
  the quickslot → rod's `RodAbility` toggles ON → next spell cast applies
  the metamagic; engine spends the rod charge via
  `IActivatableAbilitySpendResourceLogic.ManualSpendResource()` and
  auto-deactivates the ability.

Our mod replicates the click step programmatically (`TryStart()` on the
ActivatableAbility on the unit) right before issuing the spell command.

## Architecture (one orthogonal pillar, additive)

CastSpell rules gain a single optional field. At cast time, if the field is
set, a resolver finds a usable rod, activates its ability, then casts. The
existing CastSpell path is unchanged when `MetamagicRod == null` (no perf
or behavioral regression for any existing rule).

### Components

| File | Status | Responsibility |
|---|---|---|
| `Engine/MetamagicRodResolver.cs` | New | `TryResolve(UnitEntityData unit, AbilityData ability, Metamagic mm) → MetamagicRodMechanics?`. Iterates `unit.Get<UnitPartSpecialMetamagic>()` (publicizer-accessible), filters on `mech.Metamagic == mm && mech.IsSuitableAbility(ability)`, then verifies the corresponding rod ItemEntity has charges > 0 (engine `IActivatableAbilitySpendResourceLogic` returns false at activation otherwise — but we pre-check to avoid a wasted `TryStart` round-trip and to keep the validator deterministic). |
| `Models/TacticsAction.cs` | Modify | New nullable field `Metamagic? MetamagicRod` (Newtonsoft handles null/missing transparently — no migration code per parent CLAUDE.md "Bundled Newtonsoft.Json" gotcha). |
| `Engine/ActionValidator.cs` | Modify | CastSpell branch: if `MetamagicRod != null`, **do not** make the rule fail when the rod is unavailable. Resolver presence is informational only — fallback is normal cast. The validator's job is "can we cast THIS spell at all", which is unchanged. |
| `Engine/CommandExecutor.cs` | Modify | CastSpell branch: before `Commands.Run(UnitUseAbility)`, if `MetamagicRod != null`, call `MetamagicRodResolver.TryResolve` and, on hit, `unit.ActivatableAbilities.Find(mech.RodAbility)?.TryStart()`. The cast command stays unchanged — same `AbilityData`, engine handles metamagic at resolve time. |
| `UI/RuleEditorWidget.cs` | Modify | When `ActionType == CastSpell`, render an extra `PopupSelector` labelled "Rod" between the spell picker and the existing "Sources" dropdown. Options: `(none)` plus the 10 vanilla metamagic enum values (`Empower`, `Maximize`, `Quicken`, `Extend`, `Heighten`, `Reach`, `Persistent`, `Selective`, `Bolstered`, `CompletelyNormal`). Plus a small `ⓘ` icon to its right with hover tooltip explaining the quickslot requirement. |
| `Localization/*.json` (5 locales) | Modify | New keys: `cast.rod.label`, `cast.rod.none`, `cast.rod.tooltip`, plus ten `cast.rod.<metamagic>` labels (`cast.rod.quicken`, `cast.rod.empower`, …). **All five locale files (`en_GB`, `de_DE`, `fr_FR`, `ru_RU`, `zh_CN`) must carry locale-native translations** — do not paste `en_GB` into the others (per parent CLAUDE.md i18n gotcha). The metamagic names follow the convention already established in the codebase: where the in-game term is conventionally English (e.g. "Quicken"), keep it; for the tooltip text, translate fully. The tooltip's English source: *"Rod must sit in the caster's quickslot. The mod activates it before the cast; the engine applies the metamagic and spends one charge. Falls back to a normal cast silently if the rod is unavailable, has no charges, or the spell isn't eligible."* |

### Why no condition for rod presence

Three reasons:

1. **Behaviour is already correct without it.** A rule like "cast Magic
   Missile, Rod=Quicken" naturally falls back to normal cast when no
   Quicken rod is around. The cast still goes through — the only
   "condition" the user might want is "cast ONLY if rod available", which
   is already expressible by combining the existing `Combat.HasItem` /
   `Self.Resource` patterns if a future user really needs it.
2. **Halves the implementation surface.** No new condition subject /
   property / scope-classification rows, no new EnumLabels entries, no
   new evaluator branches.
3. **Future-friendly.** Adding `HasMetamagicRod` later as a condition
   composes cleanly on top — it doesn't conflict with anything in this
   spec.

## Data flow (one evaluation tick)

1. `TacticsEvaluator` matches a rule with `Action = CastSpell { Spell, MetamagicRod = Quicken }`.
2. `ActionValidator.CanExecute` — same path as today (spell `IsAvailable`,
   resources, range, etc.). Rod presence is **not** part of the validator.
3. `CommandExecutor.Execute(action, owner, target)` — CastSpell branch:
   1. Build the `AbilityData` exactly as today.
   2. If `action.MetamagicRod` is set:
      - `var mech = MetamagicRodResolver.TryResolve(owner, ability, action.MetamagicRod.Value);`
      - If `mech != null`: `owner.ActivatableAbilities.Find(mech.RodAbility)?.TryStart();`
      - If `mech == null`: skip — fall through to normal cast.
   3. `Commands.Run(new UnitUseAbility(ability, target))` — unchanged.
4. Engine: if the rod ability is now ON, the cast resolves with metamagic,
   `ManualSpendResource()` spends one rod charge, the ability auto-deactivates.
   If the rod ability is OFF, the spell casts at its normal level.

## UI

```
THEN: [CastSpell ▾]  [Spell: Magic Missile ▾]  [Rod: Quicken ▾] ⓘ  [Sources: All ▾]
```

- The Rod dropdown is only present when `ActionType == CastSpell`.
- Default value: `(none)` — every existing CastSpell rule keeps its current
  behaviour after upgrading.
- The `ⓘ` icon sits flush right of the Rod dropdown. Hovering shows the
  tooltip from `cast.rod.tooltip`. Implementation: a small TMP label with
  the `ⓘ` glyph plus an `EventTrigger` on `PointerEnter`/`PointerExit`
  that toggles a child Tooltip GameObject (transient, parented to the
  panel root so it isn't clipped by the rule scroll viewport).
- The hover tooltip is the *only* place we tell the user about the
  quickslot requirement — no nag, no warning text on the rule itself.

## Error handling and edge cases

| Case | Behaviour |
|---|---|
| `MetamagicRod = null` (existing rules) | Identical path as before. No new code runs. |
| Rod not equipped / not quickslotted | `UnitPartSpecialMetamagic` doesn't list it → `TryResolve` returns `null` → cast proceeds at normal level. |
| Rod charges depleted | `IActivatableAbilitySpendResourceLogic` blocks `TryStart` → normal cast. We pre-check charges in `TryResolve` to avoid the wasted call but the post-call fallback is the same. |
| Spell level > rod's `MaxSpellLevel` | `IsSuitableAbility` filters it out → normal cast. |
| Spell not in rod's `AbilitiesWhiteList` (specialty rods) | Same — `IsSuitableAbility` returns false → normal cast. |
| Rod ability is already ON from a prior tick | Defensive: `TryStart` is a no-op when already on. Engine still spends the charge on the next cast — correct behaviour. |
| Rod activation fires but the cast is interrupted (e.g. target dies, range fails after activation) | Engine's `ManualSpendResource` is gated on the cast actually resolving. No charge spent on a failed cast. (Verified by reading `IActivatableAbilitySpendResourceLogic` semantics — spend is post-cast, not on activation.) |
| Multiple Quicken rods equipped (Lesser + Greater) | `TryResolve` returns the first match. Robustness over preference. If a user wants to prefer the higher-MaxSpellLevel rod, they'd need a follow-up feature. |

## Save-format compat

`Models/TacticsAction.cs` gains a nullable `Metamagic?` field. Newtonsoft's
default missing-property handling (silently default to null per parent
CLAUDE.md) covers existing JSON. No migration step. Removing the field in a
future version is also free for the same reason.

## Testing

No automated tests — Wrath mods have no test pipeline. Verification:

- `dotnet build` green.
- Manual smoke on Steam Deck:
  1. Equip a Lesser Rod of Quicken on Arasmes; quickslot it.
  2. Rule: `IF Combat.IsInCombat = Yes THEN CastSpell { Magic Missile, Rod = Quicken }`.
  3. Trigger combat. Expect: Magic Missile cast as a swift action (Quicken
     applied), rod loses one charge in the inventory.
  4. Remove rod from quickslot (still in inventory) → expect normal cast,
     no crash, no metamagic.
  5. Drain rod charges to 0 → expect normal cast.
  6. Set spell level > rod's `MaxSpellLevel` (e.g. cast a level 6 spell
     with a Lesser rod, max 3) → expect normal cast.
- Code review (`/review`) before tag.

## Future iterations (NOT in this spec)

- `HasMetamagicRod` condition subject for "if rod with Empower available".
- Auto-prefer higher-`MaxSpellLevel` rod when multiple match.
- Rod-aware spell picker (grey out rod choices that don't apply to the
  selected spell).
- Wand/scroll variants of the rod-as-modifier pattern (out of scope; no
  vanilla equivalent).

## Open Questions

None at design time. The IL evidence covers all engine-side branches; UI
specifics (tooltip styling, exact dropdown width) are implementation
details that will be settled during the writing-plans pass.
