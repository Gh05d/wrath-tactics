# CastAbility Fallback Chain + Picker Scrollbar — Design

**Date:** 2026-05-16
**Feature target:** Wrath Tactics v1.15.0

## Context

User report (post-1.14.1): "Dragon breaths are all over the place, and while you got the great choice for fallback spells, there is no fallback for abilities." User suggested an "Ability Group" concept — define multiple abilities (e.g. Dragon Breath cone variants, Bloodline elemental alternatives like Burning Hands Cold) that act as a single rule choice, tried in order.

The mod already has the mechanism: `ActionDef.FallbackAbilityIds` is a list of GUIDs tried in order when the primary `AbilityId` cannot resolve. The chain is consulted by `ActionValidator.ResolveCastSpellChain` (in `Engine/ActionValidator.Cast.cs`) and threaded through `CommandExecutor.ExecuteCastSpell(ActionDef, ...)`. The UI in `RuleEditorWidget.Action.cs` renders fallback rows below the primary picker with a `+ Fallback` button.

But the chain is gated to `ActionType.CastSpell` in three places (executor dispatch, validator dispatch, UI gate), so class abilities — Dragon Breath, Bloodline Powers, Mythic Abilities — never benefit. This spec unlocks the existing chain for `ActionType.CastAbility` with the minimum number of edits and no new model.

A side request is included: the "From preset" picker (`PopupSelector.ShowPicker`) has `ScrollRect` but no visible `Scrollbar` element, so users on Steam Deck do not discover that the list is scrollable when their preset collection grows past the popup height. A visible scrollbar is added to the shared overlay builder, benefiting every popup dropdown in the mod.

## Scope

### In scope

- Extend `FallbackAbilityIds` semantics to `ActionType.CastAbility` (executor, validator, UI gate, picker entries, height calc).
- Update inline comment on `ActionDef.FallbackAbilityIds` to reflect the new applicability.
- Add visible vertical `Scrollbar` to `CreatePickerOverlay` so all popup pickers expose scrollability.
- Update `WrathTactics/CLAUDE.md` Gotchas: chain is type-homogeneous and applies to both Cast types.

### Out of scope

- `UseItem`, `ToggleActivatable`, `ThrowSplash`, `Heal`, `AttackTarget`, `DoNothing` — chain stays CastSpell + CastAbility only.
- Mixed-type chains (primary CastAbility + CastSpell fallback). The chain stays type-homogeneous because `ActionDef.Type` is a single field; mixed entries would require per-entry Type metadata. Both user use cases (Dragon Breath types, Bloodline Burning Hands variants) are homogeneous.
- Named "Ability Group" entity (separate file, registry, group picker UI). The existing preset system already covers reuse: a preset is a full `TacticsRule` with action and chain — a "Dragon Breath Cone" preset is functionally a named group. No new abstraction.
- Migration of legacy rules. `FallbackAbilityIds` is empty on CastAbility rules today; behavior is identical to current after the change.
- New i18n strings. The existing `button.add_fallback` string is type-neutral and reused.

## Model

No changes to `ActionDef` or any enum. The only model-adjacent edit is a comment refresh on `FallbackAbilityIds` to document the broadened applicability.

`Models/TacticsRule.cs:37-40`:

```csharp
// CastSpell / CastAbility fallback chain: tried in order after AbilityId when the
// primary resolver misses (no slot, no scroll, UMD fail, resource exhausted, etc.).
// Each entry goes through the full Sources mask, so a fallback can still fall through
// Spellbook -> Wand -> Scroll -> Potion for itself (CastSpell only; class abilities
// only match the Spell branch). Empty on legacy rules.
[JsonProperty] public List<string> FallbackAbilityIds { get; set; } = new();
```

## Behavior

### Executor

`Engine/CommandExecutor.cs:19-23`:

Current:
```csharp
case ActionType.CastSpell:
    return ExecuteCastSpell(action, owner, target);
case ActionType.CastAbility:
    return ExecuteCastSpell(action.AbilityId, owner, target);  // bypasses chain
```

After:
```csharp
case ActionType.CastSpell:
case ActionType.CastAbility:
    return ExecuteCastSpell(action, owner, target);
```

The `ExecuteCastSpell(string, ...)` GUID-only overload is removed if no remaining call site references it. (Quick grep confirmed at design time: only CastAbility dispatch uses it.)

### Validator

`Engine/ActionValidator.cs:25-26, 41-42`:

Both the point-target branch and the unit-target branch dispatch CastAbility into `ResolveCastSpellChain(owner, target, action, ...)` rather than checking only `action.AbilityId`. The Point branch additionally re-validates `CanTargetPoint` on the resolved ability (same as current CastSpell point branch).

Resolver re-use: `ResolveCastSpellChain` and `FindCastSpellSource` are unchanged. `FindCastSpellSource` already has the class-ability branch (Spellbook==null + SourceItem==null + AbilityResourceLogic check), and class-ability GUIDs do not match any scroll or potion blueprint, so Scroll/Potion branches naturally no-op for CastAbility entries.

### UI

`UI/RuleEditorWidget.Action.cs:241` — `SetupFallbackRows` gate:

```csharp
if (rule.Action.Type != ActionType.CastSpell && rule.Action.Type != ActionType.CastAbility) return;
```

`UI/RuleEditorWidget.Action.cs:262` — `BuildFallbackRow` entries hardcode:

```csharp
var entries = GetSpellEntries(rule.Action.Type);  // was: GetSpellEntries(ActionType.CastSpell)
```

Picker side-effect: `SpellPickerOverlay` is type-blind (it renders whatever list it gets). When `rule.Action.Type == CastAbility`, the picker shows class abilities; when CastSpell, it shows spells + wands + scrolls + potions per `GetSpellEntries`. No `SpellPickerOverlay` change.

`UI/RuleEditorWidget.cs:234-237` — fallback-row height calc:

```csharp
int fallbackCount = (rule.Action.Type == ActionType.CastSpell
                     || rule.Action.Type == ActionType.CastAbility)
    ? (rule.Action.FallbackAbilityIds?.Count ?? 0)
    : 0;
bool showAddFallback = rule.Action.Type == ActionType.CastSpell
                    || rule.Action.Type == ActionType.CastAbility;
```

Optionally extract `static bool ActionDefHelpers.IsChainCapable(ActionType t)` if a third call site shows up; for two sites the inline disjunction is clearer.

`UI/RuleEditorWidget.Action.cs:336` — `RefreshSpellSelector` body-rebuild list does NOT need updating. The branch already rebuilds the full body for CastSpell when source/fallback rows change; the same rebuild path is correct for CastAbility because both pass through `SetupFallbackRows`.

## Scrollbar — `CreatePickerOverlay`

`UI/UIHelpers.cs:436-528`:

Current overlay layout (vertical):
```
Popup (350px wide × dynamic height)
└── Scroll (ScrollRect, vertical, no scrollbar widget)
    └── Viewport (RectMask2D)
        └── Content (VLG + CSF)
            └── Option_0..N (buttons)
```

After:
```
Popup (350px wide × dynamic height)
└── Scroll (ScrollRect, vertical, verticalScrollbar = bar)
    ├── Viewport (RectMask2D, right-inset 12px)
    │   └── Content (VLG + CSF)
    │       └── Option_0..N (buttons)
    └── Scrollbar (vertical, 12px wide, anchored to right edge)
        └── SlidingArea
            └── Handle (Image)
```

Scrollbar specs:
- Width: `12f * UIHelpers.FontScale`
- Background: `new Color(0.10f, 0.10f, 0.10f, 0.7f)` (dark slot)
- Handle: `new Color(0.45f, 0.45f, 0.45f, 1f)` (mid-gray) with `Handle Rect`
- `direction = Scrollbar.Direction.BottomToTop`
- Wired via `scroll.verticalScrollbar = sb; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport`

`AutoHideAndExpandViewport` makes the scrollbar disappear when content fits inside the viewport (no clutter for short popups), and the viewport auto-expands to fill the popup when the bar hides. The viewport right-inset becomes effective only when the bar is shown.

Side benefits: every other popup using `CreatePickerOverlay` (condition Subject/Property/Operator/Value selectors, RangeBracket pickers, Class pickers, etc.) inherits the same visible scrollbar. No per-callsite changes.

Touch / wheel input: existing `ScrollRect` already accepts drag and wheel. The scrollbar is purely a visual indicator + click-to-scroll affordance, no input-handler conflict on Steam Deck.

## Test plan

The Pure-logic test suite (`WrathTactics.Tests/`) does not cover engine execution paths or UI. Verification is manual on Steam Deck:

1. **Smoke: Spell Fallback regression** — open an existing CastSpell rule with a fallback configured (e.g. one of the default presets that uses fallback), confirm the `+ Fallback` button still renders, fallback entries still pick correctly, executor still falls back when primary is unavailable. Compare log output `CastSpell fallback hit for X` against current behavior.
2. **CastAbility Fallback happy path** — Dragon-Bloodline Sorcerer, create rule: Action=CastAbility, primary=Dragon Breath Form (Cone Acid), fallback=Cone Cold. Confirm `+ Fallback` button now shows under CastAbility (was hidden before). Confirm fallback row picker lists abilities (not spells). Trigger combat where primary is on cooldown, confirm fallback fires and log shows `CastSpell fallback hit` with the variant GUID.
3. **CastAbility primary still works without fallback** — empty `FallbackAbilityIds` on a CastAbility rule (default state), confirm executor path unchanged: ability casts, log lines identical to current.
4. **Type-homogeneous picker** — switch a CastSpell rule with fallbacks to CastAbility via the Action-Type dropdown. After type-switch, the existing fallback GUIDs are still in the list but no longer resolve (different blueprint pool). Confirm log warns "ResolveCastSpellChain returned null" rather than crashing. Optional follow-up: clear chain on type switch — left out of v1.15.0 to keep diff minimal; document in release notes as "switching Action type does not clear fallbacks — adjust manually".
5. **Picker scrollbar visible** — open "From preset" picker with 15+ presets, confirm vertical scrollbar appears on the right edge. Drag the handle, confirm content scrolls. Use a 3-preset list, confirm scrollbar auto-hides (AutoHideAndExpandViewport).
6. **Other dropdowns inherit scrollbar** — open condition Property dropdown (the long list with HpPercent...AdjacentEnemyCount), confirm scrollbar shows. Quick eyeball pass on Subject, Operator, RangeBracket pickers.

## Risks

- **`ExecuteCastSpell(string, ...)` removal** — if a stale call site exists outside the dispatch switch, build fails. Mitigation: grep before delete; spec assumes only-call-site verification.
- **Picker entries blueprint pool difference** — switching action type after defining fallbacks leaves stale GUIDs. Behavior is "fallback misses, log warns" rather than crash, but a user editing in-game might be confused. Accept for v1.15.0, mention in release notes.
- **Scrollbar layout on Steam Deck portrait** — popup width is `350 * FontScale`; subtracting 12px viewport inset leaves ~338px for option text. Existing label margin is 4px; new effective option width ~330px. Option labels (preset names, condition labels) fit comfortably in tests at FontScale=1.0 and 1.25 (Steam Deck default).
- **`AutoHideAndExpandViewport` visual glitch** — Unity's auto-hide can flicker the viewport bounds for one frame on first show. Acceptable trade-off vs. AlwaysVisible (clutters short popups) or Permanent (always reserves 12px even when not needed).

## CLAUDE.md updates

After the change lands, append two notes to `WrathTactics/CLAUDE.md`:

1. **"Cast fallback chain — applies to both CastSpell and CastAbility (since 1.15)"** under Game API Gotchas. Note that chain is type-homogeneous (no mixed-type entries), that resolver `ResolveCastSpellChain` is authoritative for both, and that switching Action type does not auto-clear stale GUIDs.
2. **Strengthen the Validator-Strictness entry**: existing note says "New `ActionType` MUST validate up-front... including `AbilityData.IsAvailable`" — reaffirm by example, since CastAbility's pre-change validator only inspected `action.AbilityId` and missed `FallbackAbilityIds`. The fix routes through the chain-aware resolver to keep validator and executor in lock-step.

## Implementation order

1. **Executor + Validator dispatch** — single commit, both file edits, run existing tests (none touch engine, will pass).
2. **UI gate + height calc** — second commit.
3. **Scrollbar** — third commit, isolated to `UIHelpers.CreatePickerOverlay`.
4. **Comment + CLAUDE.md updates** — fourth commit.
5. **Manual smoke test** on Steam Deck (test plan above).
6. **Version bump + release** via `/release` slash command — bumps both `Info.json` and `WrathTactics.csproj`.
