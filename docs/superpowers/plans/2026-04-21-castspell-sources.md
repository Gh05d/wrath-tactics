# CastSpell Sources Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a source-mask to the `CastSpell` action so rules can draw from Spellbook / Scroll / Potion (with implicit wand-in-quickslot fallback), mirroring the existing `Heal` source-mask pattern.

**Architecture:** New `SpellSourceMask` flag enum alongside `HealSourceMask`. New resolver `ActionValidator.FindCastSpellSource` mirrors the structure of `FindBestHealEx`: returns an `AbilityData` + optional `ItemEntity` to consume. `ActionValidator.CanExecute` and `CommandExecutor.ExecuteCastSpell` are rerouted through the resolver so CastSpell behaves exactly like Heal for the inventory-source path (synthetic AbilityData → `Rulebook.Trigger<RuleCastSpell>` + manual `ConsumeInventoryItem`). UI adds a 7-option dropdown next to the spell picker.

**Tech Stack:** C# 7, .NET Framework 4.8.1, Harmony/UMM mod for Pathfinder: Wrath of the Righteous. Build via `~/.dotnet/dotnet build`. No automated test framework — verification is compile + manual smoke on Steam Deck (`deck-direct` SSH alias) via `./deploy.sh`.

**Spec:** `docs/superpowers/specs/2026-04-21-castspell-sources-design.md`

---

## File Structure

| File | Change |
| --- | --- |
| `WrathTactics/Models/Enums.cs` | Add `SpellSourceMask` flag enum. |
| `WrathTactics/Models/TacticsRule.cs` | Add `Sources` field to `ActionDef`. |
| `WrathTactics/Engine/ActionValidator.cs` | Add `FindCastSpellSource` resolver. Reroute `CanExecute` CastSpell branch through it. |
| `WrathTactics/Engine/CommandExecutor.cs` | Reroute `ExecuteCastSpell` through the resolver; dual-path execution (Spellbook/Wand = animated cast, Scroll/Potion = Rulebook.Trigger + consume). |
| `WrathTactics/UI/RuleEditorWidget.cs` | Add "Sources:" dropdown in the CastSpell action-row branch. |
| `WrathTactics/Info.json` | Bump `Version` to `1.0.0`. |
| `WrathTactics/WrathTactics.csproj` | Bump `<Version>` to `1.0.0`. |

Reuse targets (do **not** refactor):
- `ActionValidator.FindBestHealEx` (lines ~263–395) — pattern to mirror for the new resolver.
- `ActionValidator.FindAbility` and `FindAbilityEx` — existing spellbook/ability lookup; use for spellbook path.
- `ActionValidator.CanCastSpellFromSpellbook` — used by Heal's UMD gate; reuse for the CastSpell scroll UMD gate.
- `CommandExecutor.ConsumeInventoryItem` (private static) — promote to internal static if the resolver path needs it; otherwise call via a mirrored helper copy. See Task 5.

---

## Task 1: Model — SpellSourceMask enum + ActionDef.Sources field

**Files:**
- Modify: `WrathTactics/Models/Enums.cs` (append enum)
- Modify: `WrathTactics/Models/TacticsRule.cs` (add field to `ActionDef`)

Behaviour-neutral: adds the field with default `SpellSourceMask.All`. Nothing reads it yet, so existing rules are unchanged. Build verifies serialization compiles.

- [ ] **Step 1: Add `SpellSourceMask` enum**

Append to `WrathTactics/Models/Enums.cs` (just before the closing namespace brace):

```csharp
/// <summary>
/// Which classes of source the CastSpell action may draw from. Flag-based so combinations
/// (Spell+Scroll etc.) are expressible. Default is All. Spell covers spellbook casts and
/// wands in quickslots. Structurally identical to HealSourceMask but kept separate so
/// Heal's code path is untouched; a future refactor can unify them.
/// </summary>
[System.Flags]
public enum SpellSourceMask {
    None   = 0,
    Spell  = 1,
    Scroll = 2,
    Potion = 4,
    All    = Spell | Scroll | Potion,
}
```

- [ ] **Step 2: Add `Sources` field to `ActionDef`**

In `WrathTactics/Models/TacticsRule.cs`, add inside `ActionDef`, right below `HealSources`:

```csharp
[JsonProperty] public SpellSourceMask Sources { get; set; } = SpellSourceMask.All;
```

- [ ] **Step 3: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Models/Enums.cs WrathTactics/Models/TacticsRule.cs
git commit -m "feat(model): add SpellSourceMask + ActionDef.Sources (default All)"
```

---

## Task 2: Resolver — skeleton with Spellbook + Wand paths

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs` (add `FindCastSpellSource` after `FindBestHealEx`)

Implements **only** the `Spell` bit (spellbook slots + quickslot wands with matching blueprint and remaining charges). Scroll and Potion branches come in Tasks 4 and 5. Not yet wired — Task 3 does that so CanExecute/Executor changes can be reviewed as one unit.

- [ ] **Step 1: Add the resolver**

Insert into `WrathTactics/Engine/ActionValidator.cs`, directly below the closing brace of `FindBestHealEx` (around line 396, before `CanCastSpellFromSpellbook`):

```csharp
/// <summary>
/// Resolves which source should cast the requested spell. Mirrors FindBestHealEx but
/// the spell is fixed (not "best heal"); selects the first viable source in priority order:
///   1. Spellbook slot         (Spell bit)
///   2. Wand in quickslot      (Spell bit, implicit fallback like Heal)
///   3. Scroll from inventory  (Scroll bit, UMD-gated)
///   4. Potion from inventory  (Potion bit, self-only)
/// Matching is STRICT on blueprint GUID + variant + metamagic — the compoundKey contains
/// all three and FindAbility parses them.
///
/// Returns null if no source matches. Sets `inventorySource` to a consumable ItemEntity
/// for Scroll/Potion picks (callers must call ConsumeInventoryItem); null for spellbook
/// and wand picks (wand charges decrement via the cast command pipeline automatically).
/// </summary>
public static AbilityData FindCastSpellSource(
    UnitEntityData owner,
    ResolvedTarget target,
    string compoundKey,
    SpellSourceMask mask,
    out ItemEntity inventorySource) {

    inventorySource = null;
    if (string.IsNullOrEmpty(compoundKey)) return null;

    bool wantSpell  = (mask & SpellSourceMask.Spell)  != 0;
    bool wantScroll = (mask & SpellSourceMask.Scroll) != 0;
    bool wantPotion = (mask & SpellSourceMask.Potion) != 0;

    // 1. Spellbook slot — use the existing FindAbility which parses level/variant/metamagic.
    if (wantSpell) {
        var ability = FindAbility(owner, compoundKey);
        if (ability != null
            && ability.Spellbook != null
            && ability.Spellbook.GetAvailableForCastSpellCount(ability) > 0) {
            return ability;
        }

        // Class ability path (no spellbook, no inventory source, resource-gated).
        // Guard against wand abilities, which also have Spellbook==null but carry a SourceItem —
        // those must go through the wand branch below for a proper charge check.
        if (ability != null && ability.Spellbook == null && ability.SourceItem == null) {
            var resource = ability.Blueprint.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityResourceLogic>();
            if (resource == null || !resource.IsSpendResource) return ability;
            var required = (Kingmaker.Blueprints.BlueprintScriptableObject)ability.OverrideRequiredResource
                ?? resource.RequiredResource;
            if (required == null) return ability;
            int available = owner.Resources.GetResourceAmount(required);
            int cost = resource.CalculateCost(ability);
            if (available >= cost) return ability;
            // resource exhausted -> fall through to other sources
        }

        // 2. Wand in quickslot — search owner.Abilities.RawFacts for an item-backed ability
        // whose blueprint GUID matches the parsed rule key and that has charges remaining.
        var parsed = UI.SpellDropdownProvider.ParseKey(compoundKey);
        foreach (var fact in owner.Abilities.RawFacts) {
            var data = fact.Data;
            if (data?.SourceItem == null) continue;
            if (data.SourceItem.Charges <= 0) continue;
            if (fact.Blueprint.AssetGuid.ToString() != parsed.BlueprintGuid) continue;
            // Strict match: wands never carry metamagic or variant in Wrath.
            if (parsed.MetamagicMask != 0) continue;
            if (!string.IsNullOrEmpty(parsed.VariantGuid)) continue;
            return data;
        }
    }

    // Scroll + Potion branches added in Tasks 4 and 5.
    return null;
}
```

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs
git commit -m "feat(engine): add FindCastSpellSource resolver (Spellbook + Wand paths)"
```

---

## Task 3: Wire resolver into CanExecute and ExecuteCastSpell

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs` (`CanExecute` CastSpell branches — both unit and point paths; `CanCastSpell` signature stays, add wrapper pre-check)
- Modify: `WrathTactics/Engine/CommandExecutor.cs` (`ExecuteCastSpell` — dual-path based on `inventorySource`)

End-to-end wiring for the `Spell` bit: if Sources=Spell (or All, currently behaves the same since Scroll/Potion not yet implemented), behavior equals legacy. If Sources=Spell+anything, it still works through the Spellbook/Wand path.

- [ ] **Step 1: Update `CanExecute` CastSpell branches in `ActionValidator.cs`**

Replace the existing `case ActionType.CastSpell:` in the point branch (line ~22–23) and the unit branch (line ~33–34) so they both route through `FindCastSpellSource`:

Point branch (~line 19–28):

```csharp
if (target.IsPoint) {
    switch (action.Type) {
        case ActionType.CastSpell: {
            ItemEntity _unused;
            var ability = FindCastSpellSource(owner, target, action.AbilityId, action.Sources, out _unused);
            if (ability == null) return false;
            if (!ability.CanTargetPoint) {
                Log.Engine.Trace($"CanCastAbilityAtPoint: {owner.CharacterName} ability '{ability.Name}' is not point-castable");
                return false;
            }
            return true;
        }
        case ActionType.CastAbility:
            return CanCastAbilityAtPoint(action.AbilityId, owner);
        case ActionType.UseItem:
            return CanUseItemAtPoint(action.AbilityId, owner);
        default:
            return false;
    }
}
```

Unit branch (`case ActionType.CastSpell:` near line 33):

```csharp
case ActionType.CastSpell: {
    ItemEntity _unused;
    return FindCastSpellSource(owner, target, action.AbilityId, action.Sources, out _unused) != null;
}
```

`CastAbility` (class abilities — non-spell, no inventory equivalent) stays on the existing `CanCastSpell` path untouched.

- [ ] **Step 2: Update `ExecuteCastSpell` in `CommandExecutor.cs`**

Replace the body of `ExecuteCastSpell` (lines ~51–79) with a resolver-driven version that forks on `inventorySource`:

```csharp
static bool ExecuteCastSpell(ActionDef action, UnitEntityData owner, ResolvedTarget target) {
    ItemEntity inventorySource;
    var ability = ActionValidator.FindCastSpellSource(owner, target, action.AbilityId, action.Sources, out inventorySource);
    if (ability == null) {
        Log.Engine.Warn($"FindCastSpellSource returned null for {owner.CharacterName}, guid={action.AbilityId}");
        return false;
    }

    var targetWrapper = BuildTargetWrapper(target, owner);

    // Inventory source (scroll/potion) — synthetic AbilityData, Rulebook.Trigger + manual consume.
    // Mirror of ExecuteHeal's inventory path.
    if (inventorySource != null) {
        try {
            Rulebook.Trigger(new RuleCastSpell(ability, targetWrapper));
            var usable = inventorySource.Blueprint as BlueprintItemEquipmentUsable;
            if (usable != null) ConsumeInventoryItem(inventorySource, usable);
            string tgtDesc = target.IsPoint
                ? $"point({target.Point.Value.x:F1},{target.Point.Value.z:F1})"
                : (target.Unit?.CharacterName ?? "self");
            Log.Engine.Info($"Cast (inventory): {inventorySource.Blueprint.name} -> {ability.Name} on {owner.CharacterName} -> {tgtDesc}");
            return true;
        } catch (Exception ex) {
            Log.Engine.Error(ex, $"CastSpell inventory trigger failed for {inventorySource.Blueprint.name}");
            return false;
        }
    }

    // Spellbook / Wand / class ability — animated cast command.
    var command = UnitUseAbility.CreateCastCommand(ability, targetWrapper);
    if (command != null) {
        owner.Commands.Run(command);
        string tgtDesc = target.IsPoint
            ? $"point({target.Point.Value.x:F1},{target.Point.Value.z:F1})"
            : (target.Unit?.CharacterName ?? "self");
        Log.Engine.Debug($"Queued spell {ability.Name} on {owner.CharacterName} -> {tgtDesc}");
        return true;
    }

    try {
        Rulebook.Trigger<RuleCastSpell>(new RuleCastSpell(ability, targetWrapper));
        Log.Engine.Debug($"Rulebook-triggered {ability.Name} on {owner.CharacterName} (no animation)");
        return true;
    } catch (Exception ex) {
        Log.Engine.Error(ex, $"Rulebook.Trigger fallback failed for {ability.Name}");
        return false;
    }
}
```

- [ ] **Step 3: Update the `switch` in `CommandExecutor.Execute` so CastSpell passes the whole action**

In `CommandExecutor.cs` replace the `case ActionType.CastSpell:` dispatch (line 20–21) with:

```csharp
case ActionType.CastSpell:
    return ExecuteCastSpell(action, owner, target);
case ActionType.CastAbility:
    return ExecuteCastSpell(action.AbilityId, owner, target);
```

Keep the existing 3-arg `ExecuteCastSpell(string abilityGuid, …)` overload for `CastAbility` — rename it to `ExecuteCastAbility` if you prefer clarity, or leave it (C# overload resolution picks correctly).

- [ ] **Step 4: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5: Manual smoke (Steam Deck)**

```bash
./deploy.sh
```

Verify on the deck:
1. Load a save where a Wizard has Magic Missile prepared. Build a rule `CastSpell Magic Missile on Enemy Highest HP`, leave `Sources` default (All). Fires from spellbook (slot consumed). Expected log: `Queued spell Magic Missile on …`.
2. Same rule with zero remaining slots → rule skipped, next-priority rule evaluates. Expected log: `FindCastSpellSource returned null`.

If step 2 fails (rule still fires somehow), do **not** proceed — the validator bypass is the classic infinite-loop foot-gun (CLAUDE.md gotcha: "Validator strictness is load-bearing").

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs WrathTactics/Engine/CommandExecutor.cs
git commit -m "feat(engine): route CastSpell through FindCastSpellSource (Spell bit only)"
```

---

## Task 4: Resolver — Scroll path with UMD gate

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs` (add scroll branch inside `FindCastSpellSource`)

Mirrors Heal's scroll logic (`FindBestHealEx` lines 321–367) but simpler: no priority competition, no "risky burn" fallback — if the UMD check fails the scroll is skipped entirely.

- [ ] **Step 1: Add the scroll branch**

In `FindCastSpellSource`, replace the closing comment `// Scroll + Potion branches added in Tasks 4 and 5.` and the `return null;` with this (keep the closing brace for the method):

```csharp
    // 3. Scroll from inventory — strict match on blueprint GUID + metamagic + variant.
    // UMD-gated: if the spell is not on the caster's class list, require UMD + 11 >= DC.
    // Unlike Heal, no "risky fallback" — scroll is simply skipped on UMD fail.
    var inventory = Kingmaker.Game.Instance?.Player?.Inventory;
    if (inventory != null && (wantScroll || wantPotion)) {
        var parsedInv = UI.SpellDropdownProvider.ParseKey(compoundKey);
        foreach (var item in inventory) {
            if (item == null || item.Count <= 0) continue;
            var usable = item.Blueprint as Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable;
            if (usable?.Ability == null) continue;
            if (usable.Ability.AssetGuid.ToString() != parsedInv.BlueprintGuid) continue;
            // Strict match: scrolls/potions never carry metamagic or variant.
            if (parsedInv.MetamagicMask != 0) continue;
            if (!string.IsNullOrEmpty(parsedInv.VariantGuid)) continue;

            bool isScroll = usable.Type == Kingmaker.Blueprints.Items.Equipment.UsableItemType.Scroll;
            bool isPotion = usable.Type == Kingmaker.Blueprints.Items.Equipment.UsableItemType.Potion;

            if (isScroll && !wantScroll) continue;
            if (isPotion && !wantPotion) continue;
            if (!isScroll && !isPotion) continue;

            if (isScroll) {
                // UMD gate mirrors Heal but skips on fail (no fallback-burn).
                bool canCastNatively = CanCastSpellFromSpellbook(owner, usable.Ability);
                if (!canCastNatively) {
                    int dc = 20 + usable.CasterLevel;
                    int umd = owner.Stats.SkillUseMagicDevice.ModifiedValue;
                    if (umd + 11 < dc) {
                        Log.Engine.Trace($"CastSpell scroll {item.Blueprint.name}: UMD {umd} vs DC {dc} (< 50%), skipping");
                        continue;
                    }
                }

                var scrollAbility = new AbilityData(usable.Ability, owner.Descriptor) {
                    OverrideCasterLevel = usable.CasterLevel,
                    OverrideSpellLevel = usable.SpellLevel,
                };
                inventorySource = item;
                return scrollAbility;
            }

            // Potion branch — Task 5 fills in the self-only filter and return path.
        }
    }

    return null;
```

The Potion branch inside the same loop is scaffolded but still falls through to `continue` via the surrounding conditions; Task 5 wires it.

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Manual smoke (Steam Deck)**

```bash
./deploy.sh
```

Verify:
1. Wizard with 0 Magic Missile slots, 1 Scroll of Magic Missile in inventory. Rule fires the scroll (log: `Cast (inventory): Scroll_MagicMissile …`). Inventory count drops by 1.
2. Paladin (no Magic Missile on class list, UMD likely too low), same scroll. Rule skips silently. Log: `CastSpell scroll … UMD X vs DC Y …, skipping`.
3. Rule with `Sources = "Spell only"` and no slots — scroll is NOT picked even if present in inventory.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs
git commit -m "feat(engine): FindCastSpellSource — Scroll path with UMD gate"
```

---

## Task 5: Resolver — Potion path with self-only filter

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs` (fill in the Potion branch inside `FindCastSpellSource`)

- [ ] **Step 1: Fill in the Potion branch**

Replace the placeholder comment `// Potion branch — Task 5 fills in the self-only filter and return path.` with:

```csharp
            if (isPotion) {
                // Potions are self-only in this model (Wrath's potion ability data almost always
                // has CanTargetSelf=true only). Skip silently when target isn't the owner.
                bool targetIsSelf = !target.IsPoint && target.Unit == owner;
                if (!targetIsSelf) {
                    Log.Engine.Trace($"CastSpell potion {item.Blueprint.name}: target is not self, skipping");
                    continue;
                }
                var potionAbility = new AbilityData(usable.Ability, owner.Descriptor) {
                    OverrideCasterLevel = usable.CasterLevel,
                    OverrideSpellLevel = usable.SpellLevel,
                };
                inventorySource = item;
                return potionAbility;
            }
```

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Manual smoke (Steam Deck)**

```bash
./deploy.sh
```

Verify:
1. Cleric, rule `CastSpell Bless on Self`, `Sources = Potion only`, 1 Potion of Bless in inventory. Fires, potion count drops.
2. Same cleric, rule target switched to `AllyLowestHp` (not self). Rule skips silently. Log: `CastSpell potion … target is not self, skipping`.
3. Cleric with 1 Bless slot + 1 Potion + `Sources = All`. Spellbook wins (spellbook consumed, potion untouched).

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs
git commit -m "feat(engine): FindCastSpellSource — Potion path (self-only)"
```

---

## Task 6: UI — Sources dropdown in RuleEditorWidget

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.cs` (`SetupSpellSelector` — add Sources row when action is CastSpell)

Mirror of Heal's source dropdown. The CastSpell action's spell-picker button currently spans `0.39f → 1.0f` (line 441). Split it: picker `0.39f → 0.65f`, Sources dropdown `0.66f → 1.0f`.

- [ ] **Step 1: Add the Sources dropdown for CastSpell**

In `WrathTactics/UI/RuleEditorWidget.cs`, modify the end of `SetupSpellSelector` (around line 441). Replace:

```csharp
            BuildSpellPickerButton(row, 0.39f, 1.0f);

            // Hide if not applicable
            bool showSelector = rule.Action.Type != ActionType.AttackTarget &&
                                rule.Action.Type != ActionType.DoNothing &&
                                rule.Action.Type != ActionType.ThrowSplash;
            if (spellPickerButton != null)
                spellPickerButton.SetActive(showSelector);
```

with:

```csharp
            bool isCastSpell = rule.Action.Type == ActionType.CastSpell;
            float pickerXMax = isCastSpell ? 0.65f : 1.0f;
            BuildSpellPickerButton(row, 0.39f, pickerXMax);

            if (isCastSpell) {
                // Source mask dropdown — 7 curated combinations, same pattern as HealSources.
                var sourceLabels = new List<string> {
                    "All sources", "Spell only", "Scroll only", "Potion only",
                    "Spell + Scroll", "Spell + Potion", "Scroll + Potion",
                };
                var sourceValues = new List<SpellSourceMask> {
                    SpellSourceMask.All,
                    SpellSourceMask.Spell,
                    SpellSourceMask.Scroll,
                    SpellSourceMask.Potion,
                    SpellSourceMask.Spell  | SpellSourceMask.Scroll,
                    SpellSourceMask.Spell  | SpellSourceMask.Potion,
                    SpellSourceMask.Scroll | SpellSourceMask.Potion,
                };
                int srcIdx = sourceValues.IndexOf(rule.Action.Sources);
                if (srcIdx < 0) srcIdx = 0;
                PopupSelector.Create(row, "SpellSources", 0.66f, 1.0f, sourceLabels, srcIdx, idx => {
                    rule.Action.Sources = sourceValues[idx];
                    PersistEdit();
                });
            }

            // Hide if not applicable
            bool showSelector = rule.Action.Type != ActionType.AttackTarget &&
                                rule.Action.Type != ActionType.DoNothing &&
                                rule.Action.Type != ActionType.ThrowSplash;
            if (spellPickerButton != null)
                spellPickerButton.SetActive(showSelector);
```

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Manual smoke (Steam Deck)**

```bash
./deploy.sh
```

Verify:
1. Ctrl+T → edit a CastSpell rule. Dropdown on the right shows "All sources" by default, picker is narrower but readable.
2. Change to "Spell only" → click away → reopen: persists as "Spell only".
3. Check a preset-mode rule (Presets tab): the Sources dropdown also persists (routes through `PersistEdit` → `PresetRegistry.Save`).
4. Heal rules are unaffected: HealMode + HealSources dropdowns still show and work.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.cs
git commit -m "feat(ui): add Sources dropdown for CastSpell rules"
```

---

## Task 7: Version bump + release notes

**Files:**
- Modify: `WrathTactics/Info.json`
- Modify: `WrathTactics/WrathTactics.csproj`

v1.0.0 as discussed. Both files must bump together (CLAUDE.md gotcha: "Bumping only one ships a zip with the stale version in its name").

- [ ] **Step 1: Bump `Info.json`**

Change line 5:

```json
  "Version": "1.0.0",
```

- [ ] **Step 2: Bump `WrathTactics.csproj`**

Change line 7:

```xml
		<Version>1.0.0</Version>
```

- [ ] **Step 3: Release build to verify zip name**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/
```

Expected: output zip at `WrathTactics/bin/WrathTactics-1.0.0.zip`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj
git commit -m "chore: bump version to 1.0.0"
```

- [ ] **Step 5: Tag + GitHub release (only if user requests)**

Per CLAUDE.md: only commit/tag/release on explicit user request. When green-lit:

```bash
git tag -a v1.0.0 -m "v1.0.0 — CastSpell source mask + release polish"
git push origin master
git push origin v1.0.0
gh release create v1.0.0 \
  --title "v1.0.0 — CastSpell sources + polish" \
  --notes-file - <<'EOF'
## New

- **CastSpell can now draw from Scrolls, Potions, and Wands.** Each CastSpell rule has a new `Sources:` dropdown with seven combinations (All, Spell only, Scroll only, Potion only, and the three pairs). Default is `All sources`.
- **Count conditions got the full operator palette.** `AllyCount` / `EnemyCount` rules now support `<`, `<=`, `=`, `!=`, `>`, `>=` (previously hardcoded to `>=`).
- **Preset-mode spell dropdowns include reserve companions.** When editing a preset (Presets tab), the Spell / Ability / Item / Activatable dropdowns now aggregate over every living player character (`Player.AllCharacters`), not just the active 6-party.

## Behavior change (read this)

Existing `CastSpell` rules saved before v1.0 will load with `Sources = All` and start using Scrolls / Potions / quickslot Wands automatically. If you want a rule to stay Spellbook-only, open it and set `Sources:` to `Spell only`. Newly created rules also default to `All sources`.

## Known limitations

- Metamagic / variant rules (e.g. Maximized Magic Missile) only match the spellbook — scrolls, potions, and wands in Wrath don't carry metamagic or variants, so strict matching locks those rules to the spellbook as before.
- Potions are only picked when the rule targets the caster themselves.
- Scrolls not on the caster's class list are skipped if UMD is below 50% success — no risky burns.
EOF
```

---

## Post-Implementation Checklist

After all seven tasks commit cleanly:

- [ ] Full build green (`~/.dotnet/dotnet build … -c Release`).
- [ ] Manual smoke of each of the 7 test cases in spec §Test Plan passes on Steam Deck.
- [ ] Log spot-check: no new `WARN`/`ERROR` lines on mod load or when opening the rule editor with the Sources dropdown.
- [ ] Legacy save without `Sources` field in JSON loads cleanly with `Sources = All` applied (migration sanity).
- [ ] Ask user whether to tag + release v1.0.0 now or hold. Do not tag or push without explicit approval.

## Rollback

If any task breaks on the deck and can't be diagnosed in a short iteration, the fastest rollback is to revert the offending commit (`git revert <sha>`), redeploy, and re-open the task. All tasks are designed to be small enough that a single-commit revert restores the last green state.
