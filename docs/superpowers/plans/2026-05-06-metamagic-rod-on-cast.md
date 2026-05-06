# Metamagic Rod on Cast — Implementation Plan (v1.11.0)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a CastSpell rule optionally tag a metamagic type. At cast time, find an equipped+quickslotted rod that provides that metamagic, activate its `BlueprintActivatableAbility` via the unit's `ActivatableAbilities` collection, then issue the existing cast command. Engine handles metamagic application and charge spending.

**Architecture:** One nullable `Metamagic? MetamagicRod` field on `ActionDef`. New `MetamagicRodResolver` walks the engine's `UnitPartSpecialMetamagic` to find a `(rod, mechanics)` pair that matches metamagic + spell. `CommandExecutor.ExecuteCastSpell` calls the resolver and `TryStart()` on the matched activatable ability before running the existing cast command. UI gets one new `PopupSelector` plus an `ⓘ` info icon with a hover tooltip.

**Tech Stack:** C# / .NET Framework 4.8.1, Unity UI (TextMeshPro, EventTrigger), HarmonyLib, Newtonsoft.Json (bundled). No new packages.

**Spec:** `docs/superpowers/specs/2026-05-06-metamagic-rod-on-cast-design.md` (commit `7164bdc`).

**Verification model:** No automated tests — Wrath mods have no test pipeline. Each code task ends with `dotnet build` green. Final task runs the spec's manual smoke list on the Steam Deck.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `WrathTactics/Models/TacticsRule.cs` | Modify | Add `Metamagic? MetamagicRod` to `ActionDef`. |
| `WrathTactics/Engine/MetamagicRodResolver.cs` | New | `TryResolve(unit, ability, metamagic)` — engine-authoritative rod match. |
| `WrathTactics/Engine/CommandExecutor.cs` | Modify | `ExecuteCastSpell(ActionDef …)` activates the rod ability before issuing the cast command. |
| `WrathTactics/Localization/EnumLabels.cs` | Modify | New `KeysForMetamagic()` and `LabelsForMetamagic()` for the rod dropdown. |
| `WrathTactics/Localization/en_GB.json` | Modify | New `cast.rod.*` and `enum.metamagic.*` keys. |
| `WrathTactics/Localization/de_DE.json` | Modify | German native translations. |
| `WrathTactics/Localization/fr_FR.json` | Modify | French native translations. |
| `WrathTactics/Localization/ru_RU.json` | Modify | Russian native translations. |
| `WrathTactics/Localization/zh_CN.json` | Modify | Chinese native translations. |
| `WrathTactics/UI/UIHelpers.cs` | Modify | `AddSimpleTooltip(GameObject host, string text)` — `EventTrigger`-based hover tooltip. |
| `WrathTactics/UI/RuleEditorWidget.cs` | Modify | Rod dropdown + `ⓘ` icon when ActionType=CastSpell. Re-anchors the spell picker and Sources dropdown to make room. |

---

## Task 1: Add MetamagicRod field to ActionDef

**Files:**
- Modify: `WrathTactics/Models/TacticsRule.cs:33-47` (ActionDef class body)

The `Metamagic` enum already lives in `Kingmaker.UnitLogic.Abilities.Metamagic` (vanilla game enum, 10 values). We use it directly — no model-side enum mirror.

- [ ] **Step 1: Add the field**

Open `WrathTactics/Models/TacticsRule.cs`. Find the `ActionDef` class around line 33. Add a `using` for the engine namespace at the top of the file if it isn't there (it should be — check first), then add the new field after `ToggleMode`:

Top-of-file using (only add if missing):
```csharp
using Kingmaker.UnitLogic.Abilities;
```

Inside `ActionDef`, after the existing `ToggleMode` field (line 46):
```csharp
        // Optional metamagic-rod tag for CastSpell. When set, CommandExecutor activates the
        // matching rod's ActivatableAbility before issuing the cast — the engine then
        // applies the metamagic and spends one rod charge. Null = cast without rod
        // (legacy behaviour). Falls back silently to a normal cast when no usable rod is
        // equipped+quickslotted.
        [JsonProperty] public Metamagic? MetamagicRod { get; set; }
```

`Metamagic?` (nullable) is JSON-serialised as either omitted/`null` or the integer enum value. Newtonsoft handles legacy ActionDefs without this property by leaving it at `null` — see parent CLAUDE.md "Bundled Newtonsoft.Json" gotcha. No migration code.

- [ ] **Step 2: Build**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo
```

Expected: `Build succeeded. 0 Error(s)`. If `Metamagic` is unresolved, the `using Kingmaker.UnitLogic.Abilities;` is missing.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/TacticsRule.cs
git commit -m "feat(model): add MetamagicRod field to ActionDef

Optional Metamagic? — null on legacy actions, no migration needed.
Engine API consumes the field in CommandExecutor (next task)."
```

---

## Task 2: Localization keys (5 locale files)

**Files:**
- Modify: `WrathTactics/Localization/en_GB.json`
- Modify: `WrathTactics/Localization/de_DE.json`
- Modify: `WrathTactics/Localization/fr_FR.json`
- Modify: `WrathTactics/Localization/ru_RU.json`
- Modify: `WrathTactics/Localization/zh_CN.json`

Per parent CLAUDE.md i18n gotcha: each locale gets a native translation. Do **not** copy `en_GB` into the others.

The metamagic enum names (`Quicken`, `Empower`, etc.) are conventionally kept in English even in non-English locales — they're rule terms. The tooltip and label strings translate fully.

- [ ] **Step 1: en_GB**

Open `WrathTactics/Localization/en_GB.json`. Find a sensible insertion point (e.g. after the existing `cast.*` keys if present, otherwise near `source.*`). Add:

```json
  "cast.rod.label": "Rod",
  "cast.rod.none": "(none)",
  "cast.rod.tooltip": "Rod must sit in the caster's quickslot. The mod activates it before the cast; the engine applies the metamagic and spends one charge. Falls back to a normal cast silently if the rod is unavailable, has no charges, or the spell isn't eligible.",
  "enum.metamagic.Empower": "Empower",
  "enum.metamagic.Maximize": "Maximize",
  "enum.metamagic.Quicken": "Quicken",
  "enum.metamagic.Extend": "Extend",
  "enum.metamagic.Heighten": "Heighten",
  "enum.metamagic.Reach": "Reach",
  "enum.metamagic.Persistent": "Persistent",
  "enum.metamagic.Selective": "Selective",
  "enum.metamagic.Bolstered": "Bolstered",
  "enum.metamagic.CompletelyNormal": "Completely Normal",
```

JSON syntax note: if you insert these in the middle of the file, make sure the preceding line ends with a comma. The trailing comma rule is JSON-strict (no trailing comma on the LAST property in the object).

- [ ] **Step 2: de_DE**

```json
  "cast.rod.label": "Rute",
  "cast.rod.none": "(keine)",
  "cast.rod.tooltip": "Die Rute muss im Quickslot des Zaubernden liegen. Der Mod aktiviert sie vor dem Zauber; die Engine wendet die Metamagie an und verbraucht eine Ladung. Fällt stillschweigend auf einen normalen Wirkvorgang zurück, wenn die Rute nicht verfügbar ist, keine Ladungen hat oder der Zauber nicht in Frage kommt.",
  "enum.metamagic.Empower": "Empower",
  "enum.metamagic.Maximize": "Maximize",
  "enum.metamagic.Quicken": "Quicken",
  "enum.metamagic.Extend": "Extend",
  "enum.metamagic.Heighten": "Heighten",
  "enum.metamagic.Reach": "Reach",
  "enum.metamagic.Persistent": "Persistent",
  "enum.metamagic.Selective": "Selective",
  "enum.metamagic.Bolstered": "Bolstered",
  "enum.metamagic.CompletelyNormal": "Vollständig normal",
```

- [ ] **Step 3: fr_FR**

```json
  "cast.rod.label": "Bâton",
  "cast.rod.none": "(aucun)",
  "cast.rod.tooltip": "Le bâton doit se trouver dans le slot rapide du lanceur. Le mod l'active avant l'incantation ; le moteur applique la métamagie et consomme une charge. Bascule silencieusement vers une incantation normale si le bâton est indisponible, n'a plus de charges ou si le sort n'est pas éligible.",
  "enum.metamagic.Empower": "Empower",
  "enum.metamagic.Maximize": "Maximize",
  "enum.metamagic.Quicken": "Quicken",
  "enum.metamagic.Extend": "Extend",
  "enum.metamagic.Heighten": "Heighten",
  "enum.metamagic.Reach": "Reach",
  "enum.metamagic.Persistent": "Persistent",
  "enum.metamagic.Selective": "Selective",
  "enum.metamagic.Bolstered": "Bolstered",
  "enum.metamagic.CompletelyNormal": "Complètement normal",
```

- [ ] **Step 4: ru_RU**

```json
  "cast.rod.label": "Жезл",
  "cast.rod.none": "(нет)",
  "cast.rod.tooltip": "Жезл должен находиться в быстром слоте заклинателя. Мод активирует его перед заклинанием; движок применяет метамагию и расходует один заряд. При отсутствии жезла, исчерпании зарядов или неподходящем заклинании молча возвращается к обычному наложению.",
  "enum.metamagic.Empower": "Empower",
  "enum.metamagic.Maximize": "Maximize",
  "enum.metamagic.Quicken": "Quicken",
  "enum.metamagic.Extend": "Extend",
  "enum.metamagic.Heighten": "Heighten",
  "enum.metamagic.Reach": "Reach",
  "enum.metamagic.Persistent": "Persistent",
  "enum.metamagic.Selective": "Selective",
  "enum.metamagic.Bolstered": "Bolstered",
  "enum.metamagic.CompletelyNormal": "Полностью обычное",
```

- [ ] **Step 5: zh_CN**

```json
  "cast.rod.label": "法杖",
  "cast.rod.none": "(无)",
  "cast.rod.tooltip": "法杖必须放在施法者的快捷栏中。模组会在施法前激活它；引擎应用超魔效果并消耗一次充能。当法杖不可用、无充能或法术不符合条件时，会静默回退到普通施法。",
  "enum.metamagic.Empower": "Empower",
  "enum.metamagic.Maximize": "Maximize",
  "enum.metamagic.Quicken": "Quicken",
  "enum.metamagic.Extend": "Extend",
  "enum.metamagic.Heighten": "Heighten",
  "enum.metamagic.Reach": "Reach",
  "enum.metamagic.Persistent": "Persistent",
  "enum.metamagic.Selective": "Selective",
  "enum.metamagic.Bolstered": "Bolstered",
  "enum.metamagic.CompletelyNormal": "完全正常",
```

- [ ] **Step 6: Build (locale files are embedded resources — must compile cleanly)**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo
```

Expected: `Build succeeded. 0 Error(s)`. If a JSON file has a syntax error, the build will warn or fail at the embedded-resource step.

- [ ] **Step 7: Commit**

```bash
git add WrathTactics/Localization/en_GB.json \
        WrathTactics/Localization/de_DE.json \
        WrathTactics/Localization/fr_FR.json \
        WrathTactics/Localization/ru_RU.json \
        WrathTactics/Localization/zh_CN.json
git commit -m "i18n: add cast.rod.* and enum.metamagic.* keys (5 locales, native)

cast.rod.{label,none,tooltip} translated per locale. Metamagic enum names
(Quicken, Empower, …) kept in English across all locales — they're rule
terms players already encounter in vanilla. Only CompletelyNormal is
translated since it's a sentence rather than a tabletop term."
```

---

## Task 3: EnumLabels helper for the rod dropdown options

**Files:**
- Modify: `WrathTactics/Localization/EnumLabels.cs`

We don't use the generic `NamesFor<Metamagic>()` because the dropdown also needs a leading "(none)" entry that doesn't map to a `Metamagic` value. Build the labels and the parallel value list in one place.

- [ ] **Step 1: Add the helper**

Open `WrathTactics/Localization/EnumLabels.cs`. Add `using Kingmaker.UnitLogic.Abilities;` to the top if missing. After `LabelsForCondition()` (around line 46), add:

```csharp
        /// <summary>
        /// The 10 vanilla Metamagic enum values — same order as the Metamagic enum,
        /// excluding None. Used for the CastSpell Rod dropdown.
        /// </summary>
        public static readonly Metamagic[] MetamagicValues = new[] {
            Metamagic.Empower,
            Metamagic.Maximize,
            Metamagic.Quicken,
            Metamagic.Extend,
            Metamagic.Heighten,
            Metamagic.Reach,
            Metamagic.Persistent,
            Metamagic.Selective,
            Metamagic.Bolstered,
            Metamagic.CompletelyNormal,
        };

        /// <summary>
        /// Dropdown labels for the CastSpell Rod selector: "(none)" first, then the
        /// 10 metamagic types. The integer index matches RodDropdownLabels[i] →
        /// (i==0 ? null : MetamagicValues[i-1]).
        /// </summary>
        public static List<string> RodDropdownLabels() {
            var labels = new List<string>(MetamagicValues.Length + 1);
            labels.Add("cast.rod.none".i18n());
            foreach (var v in MetamagicValues) labels.Add($"enum.metamagic.{v}".i18n());
            return labels;
        }
```

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Localization/EnumLabels.cs
git commit -m "feat(i18n): MetamagicValues + RodDropdownLabels helper

Single source of truth for the rod-dropdown option order:
- index 0 = '(none)' (maps to Action.MetamagicRod = null)
- indices 1-10 = MetamagicValues[i-1]"
```

---

## Task 4: MetamagicRodResolver

**Files:**
- Create: `WrathTactics/Engine/MetamagicRodResolver.cs`

The resolver is the only consumer of `UnitPartSpecialMetamagic` in our codebase. It exposes one public method.

- [ ] **Step 1: Create the file**

```csharp
// WrathTactics/Engine/MetamagicRodResolver.cs
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Parts;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    /// <summary>
    /// Finds a metamagic rod on a unit that can apply to a given spell.
    /// Engine-authoritative: queries `UnitPartSpecialMetamagic` (the same engine-side
    /// list that vanilla UI uses) and `MetamagicRodMechanics.IsSuitableAbility`.
    /// Returns null on no match — callers fall back to a normal cast.
    /// </summary>
    public static class MetamagicRodResolver {
        /// <summary>
        /// First rod on `unit` whose `Metamagic == metamagic` and whose
        /// `IsSuitableAbility(ability)` returns true. Returns null if no rod is
        /// equipped+quickslotted, or none match the spell.
        /// The returned `EntityFact` is the rod's mechanics fact on the unit; its
        /// `MetamagicRodMechanics` carries `RodAbility` (the ActivatableAbility blueprint
        /// the caller activates).
        /// </summary>
        public static MetamagicRodMechanics TryResolve(UnitEntityData unit, AbilityData ability, Metamagic metamagic) {
            if (unit == null || ability == null) return null;
            var part = unit.Get<UnitPartSpecialMetamagic>();
            if (part == null) return null;
            // m_MetamagicRodMechanics is publicizer-accessible (private List<(EntityFact, MetamagicRodMechanics)>).
            var entries = part.m_MetamagicRodMechanics;
            if (entries == null || entries.Count == 0) return null;
            foreach (var entry in entries) {
                var mech = entry.Item2;
                if (mech == null) continue;
                if (mech.Metamagic != metamagic) continue;
                if (!mech.IsSuitableAbility(ability)) continue;
                Log.Engine.Debug($"MetamagicRodResolver: matched {mech.Metamagic} rod for {ability.Name} on {unit.CharacterName}");
                return mech;
            }
            return null;
        }
    }
}
```

Notes on the API used:
- `unit.Get<UnitPartSpecialMetamagic>()` — engine extension method, returns null when the unit has no rods at all.
- `part.m_MetamagicRodMechanics` — private field, exposed via the BepInEx publicizer (already enabled on Assembly-CSharp via `Publicize="true"` in csproj).
- `mech.IsSuitableAbility(ability)` — public method per IL inspection. Combines `MaxSpellLevel` + `AbilitiesWhiteList` checks.

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo
```

Expected: `Build succeeded. 0 Error(s)`. If `m_MetamagicRodMechanics` is unresolved, the publicizer didn't run for that type — `dotnet build` would have left the publicized DLL untouched. Re-run `dotnet build` after `rm -rf WrathTactics/obj WrathTactics/bin`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/MetamagicRodResolver.cs
git commit -m "feat(engine): MetamagicRodResolver

Wraps UnitPartSpecialMetamagic + MetamagicRodMechanics.IsSuitableAbility
behind a single TryResolve(unit, ability, metamagic) → MetamagicRodMechanics?
that returns null on any failure mode. CommandExecutor wires this in next."
```

---

## Task 5: CommandExecutor wiring

**Files:**
- Modify: `WrathTactics/Engine/CommandExecutor.cs:51-104` (`ExecuteCastSpell(ActionDef …)` method)

Activate the rod's `ActivatableAbility` between resolving the spell and issuing the cast command. The activation has to happen on the unit's `ActivatableAbilities` collection (where the rod-fact registered its ability when the rod was equipped); we look up the matching `ActivatableAbility` instance by blueprint reference.

- [ ] **Step 1: Add the helper method**

Open `WrathTactics/Engine/CommandExecutor.cs`. After the closing brace of `ExecuteCastSpell(ActionDef …)` (just before `ExecuteCastSpell(string abilityGuid, …)` around line 106), add:

```csharp
        /// <summary>
        /// If `action.MetamagicRod` is set and a matching rod is equipped+quickslotted,
        /// activates the rod's ActivatableAbility on `owner` so the next cast picks it up.
        /// Silent no-op when the field is null or no rod matches — caller proceeds with
        /// a normal cast.
        /// </summary>
        static void MaybeActivateRod(ActionDef action, UnitEntityData owner, AbilityData ability) {
            if (action.MetamagicRod == null) return;
            var mech = MetamagicRodResolver.TryResolve(owner, ability, action.MetamagicRod.Value);
            if (mech == null) return;
            var rodAbilityBp = mech.RodAbility;
            if (rodAbilityBp == null) return;
            foreach (var aa in owner.ActivatableAbilities) {
                if (aa.Blueprint != rodAbilityBp) continue;
                if (aa.IsOn) return; // already toggled — engine will spend the charge on the upcoming cast
                if (aa.TryStart()) {
                    Log.Engine.Info($"Activated rod {rodAbilityBp.name} for {owner.CharacterName} ({mech.Metamagic} on {ability.Name})");
                }
                return;
            }
        }
```

- [ ] **Step 2: Call the helper from both cast paths in `ExecuteCastSpell(ActionDef …)`**

In the same file, find the `ExecuteCastSpell(ActionDef action, …)` method (line 51). Insert the helper call immediately after the `ability` variable is resolved and the `targetWrapper` is built — i.e. right before the inventory-source `if` (currently line 68). The exact insertion site: AFTER `var targetWrapper = BuildTargetWrapper(target, owner);` (line 64), BEFORE `if (inventorySource != null) {` (line 68).

```csharp
            var targetWrapper = BuildTargetWrapper(target, owner);

            // Rod activation (no-op when action.MetamagicRod == null or no rod matches).
            // Done after resolution, before any cast path — applies whether we end up in
            // the inventory branch, the spellbook branch, or the Rulebook fallback.
            MaybeActivateRod(action, owner, ability);

            // Inventory source (scroll/potion) — synthetic AbilityData, Rulebook.Trigger + manual consume.
            // Mirror of ExecuteHeal's inventory path.
            if (inventorySource != null) {
                ...
```

Note: rods can apply to scroll/potion casts the same as spellbook casts (the engine's `IsSuitableAbility` check uses spell level, not source). Activating before all three branches keeps the behaviour consistent.

- [ ] **Step 3: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/CommandExecutor.cs
git commit -m "feat(engine): wire MetamagicRodResolver into ExecuteCastSpell

MaybeActivateRod runs after spell + target resolution, before all three
cast branches (inventory, spellbook, Rulebook fallback). When the action
carries a MetamagicRod tag and a matching rod is equipped+quickslotted,
its ActivatableAbility is started — engine then applies the metamagic
and spends one charge on the upcoming cast. Silent no-op otherwise."
```

---

## Task 6: Tooltip helper in UIHelpers

**Files:**
- Modify: `WrathTactics/UI/UIHelpers.cs`

The codebase has no tooltip pattern yet — this task adds the first one. Keep it minimal: a transient `TextMeshProUGUI` inside an `Image` panel, parented to the panel root so it doesn't get clipped by the rule scroll viewport, shown on `PointerEnter` and hidden on `PointerExit`.

- [ ] **Step 1: Add the helper**

Open `WrathTactics/UI/UIHelpers.cs`. Add `using UnityEngine.EventSystems;` near the top if missing. After `MakeButton` (around line 120), add:

```csharp
        /// <summary>
        /// Attaches a hover-activated text tooltip to `host`. The tooltip GameObject is
        /// created lazily on first hover and parented to `host`'s root canvas so it isn't
        /// clipped by ScrollRect / Mask2D ancestors. Position: above-and-right of `host`.
        /// </summary>
        public static void AddSimpleTooltip(GameObject host, string text) {
            if (host == null || string.IsNullOrEmpty(text)) return;
            var trigger = host.GetComponent<EventTrigger>() ?? host.AddComponent<EventTrigger>();
            GameObject tooltip = null;

            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener(_ => {
                if (tooltip == null) {
                    tooltip = BuildTooltip(host, text);
                }
                tooltip.SetActive(true);
            });
            trigger.triggers.Add(enterEntry);

            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener(_ => {
                if (tooltip != null) tooltip.SetActive(false);
            });
            trigger.triggers.Add(exitEntry);
        }

        static GameObject BuildTooltip(GameObject host, string text) {
            // Parent to root canvas to escape any RectMask2D clipping.
            var canvas = host.GetComponentInParent<Canvas>();
            var parent = canvas != null ? canvas.transform : host.transform.root;

            var (root, rootRect) = Create("Tooltip", parent);
            // Anchor at host's top-right; nudge up so it doesn't overlap the cursor.
            rootRect.anchorMin = new Vector2(0, 0);
            rootRect.anchorMax = new Vector2(0, 0);
            rootRect.pivot = new Vector2(0, 0);
            var hostRect = host.GetComponent<RectTransform>();
            var hostWorld = (Vector2)hostRect.position + new Vector2(hostRect.rect.width * 0.5f, hostRect.rect.height + 8f);
            rootRect.position = hostWorld;
            rootRect.sizeDelta = new Vector2(360f * FontScale, 0f);

            // Background
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.04f, 0.03f, 0.92f);
            bg.raycastTarget = false;

            // Padded label
            var (labelObj, labelRect) = Create("Label", root.transform);
            labelRect.FillParent();
            var tmp = labelObj.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 14f * FontScale;
            tmp.alignment = TMPro.TextAlignmentOptions.TopLeft;
            tmp.color = new Color(0.95f, 0.92f, 0.85f);
            tmp.enableWordWrapping = true;
            tmp.margin = new Vector4(8f, 6f, 8f, 6f);
            tmp.raycastTarget = false;

            // Auto-size root to fit text height after layout
            var fitter = root.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            root.SetActive(false);
            return root;
        }
```

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo
```

Expected: `Build succeeded. 0 Error(s)`. `TMPro` namespace is already used elsewhere in this file.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/UIHelpers.cs
git commit -m "feat(ui): AddSimpleTooltip helper

Hover-activated text tooltip via EventTrigger. Lazy-built on first hover
and parented to the root Canvas so RectMask2D ancestors don't clip it.
Used by the rod dropdown's ⓘ icon (next task); generic enough for any
future tooltip needs."
```

---

## Task 7: Rod dropdown + ⓘ icon in RuleEditorWidget

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.cs:474-510` (the CastSpell action row build)

The current CastSpell row uses anchors `0.39-0.65` for the spell picker and `0.66-1.0` for SpellSources. We compress these and slot the Rod dropdown + info icon between them.

New layout:
- `0.39-0.55` Spell picker
- `0.56-0.72` Rod dropdown
- `0.73-0.76` ⓘ info icon
- `0.77-1.0` SpellSources

- [ ] **Step 1: Update the CastSpell branch**

Open `WrathTactics/UI/RuleEditorWidget.cs`. Find the block starting at line 477 (`bool isCastSpell = …`). Replace from line 477 through line 502 (end of the current `if (isCastSpell)` block) with:

```csharp
            bool isCastSpell = rule.Action.Type == ActionType.CastSpell;
            float pickerXMax = isCastSpell ? 0.55f : 1.0f;
            BuildSpellPickerButton(row, 0.39f, pickerXMax);

            if (isCastSpell) {
                // Rod dropdown — index 0 = (none) -> Action.MetamagicRod = null,
                // indices 1..10 = MetamagicValues[i-1].
                var rodLabels = EnumLabels.RodDropdownLabels();
                int rodIdx = rule.Action.MetamagicRod == null
                    ? 0
                    : System.Array.IndexOf(EnumLabels.MetamagicValues, rule.Action.MetamagicRod.Value) + 1;
                if (rodIdx < 0) rodIdx = 0;
                PopupSelector.Create(row, "MetamagicRod", 0.56f, 0.72f, rodLabels, rodIdx, idx => {
                    rule.Action.MetamagicRod = idx == 0
                        ? (Kingmaker.UnitLogic.Abilities.Metamagic?)null
                        : EnumLabels.MetamagicValues[idx - 1];
                    PersistEdit();
                });

                // ⓘ info icon explaining the quickslot requirement (hover tooltip).
                var (infoObj, infoRect) = UIHelpers.Create("RodInfo", row.transform);
                infoRect.SetAnchor(0.73f, 0.76f, 0, 1);
                infoRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(infoObj, "ⓘ", 18f, TMPro.TextAlignmentOptions.Midline,
                    new Color(0.15f, 0.10f, 0.06f));
                // Image so EventTrigger has a raycast target.
                var infoBg = infoObj.AddComponent<Image>();
                infoBg.color = new Color(0, 0, 0, 0); // invisible; raycast only
                infoBg.raycastTarget = true;
                UIHelpers.AddSimpleTooltip(infoObj, "cast.rod.tooltip".i18n());

                // Source mask dropdown — 7 curated combinations, same pattern as HealSources.
                var sourceLabels = new List<string> {
                    "source.all".i18n(), "source.spell_only".i18n(), "source.scroll_only".i18n(), "source.potion_only".i18n(),
                    "source.spell_scroll".i18n(), "source.spell_potion".i18n(), "source.scroll_potion".i18n(),
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
                PopupSelector.Create(row, "SpellSources", 0.77f, 1.0f, sourceLabels, srcIdx, idx => {
                    rule.Action.Sources = sourceValues[idx];
                    PersistEdit();
                });
            }
```

If the file uses `using Kingmaker.UnitLogic.Abilities;` already (check the top of the file), drop the fully-qualified `Kingmaker.UnitLogic.Abilities.Metamagic?` and just use `Metamagic?`. Otherwise leave the fully-qualified type or add the using once.

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ --nologo
```

Expected: `Build succeeded. 0 Error(s)`. If `EnumLabels.MetamagicValues` is unresolved, Task 3 didn't land — re-check.

- [ ] **Step 3: Deploy + visual smoke**

```bash
./deploy.sh
```

In-game smoke (no rods needed yet for this UI check):
1. Press Ctrl+T, pick a companion tab.
2. Add Rule, set ActionType=CastSpell, pick any spell.
3. Verify the row shows: `[Spell picker] [Rod ▾ (none)] ⓘ [Sources ▾ All]`.
4. Hover the `ⓘ` — tooltip text appears above the row, explaining the quickslot requirement.
5. Open the Rod dropdown — 11 entries: `(none)`, then the 10 metamagic types.
6. Pick `Quicken`, close the panel, reopen — Rod stays `Quicken` (persisted to JSON).
7. Switch ActionType to something else (e.g. Heal) — Rod dropdown disappears (only shown when CastSpell).

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.cs
git commit -m "feat(ui): rod dropdown + ⓘ tooltip on CastSpell action row

Compresses spell picker (0.39-0.55) and Sources (0.77-1.0) anchors to slot
in a Rod dropdown (0.56-0.72) and info icon (0.73-0.76). The icon hosts a
hover tooltip from cast.rod.tooltip explaining that the rod must sit in
the caster's quickslot for the activation to find it. The dropdown only
renders when ActionType == CastSpell."
```

---

## Task 8: Smoke test, code review, release

**Files:** none (verification + release pipeline).

- [ ] **Step 1: End-to-end deck smoke (per spec)**

Equip Arasmes (or any companion with a spell) with a Lesser Rod of Quicken; place the rod in a quickslot. Save state with that loadout.

```bash
./deploy.sh
```

Then in-game:

1. Open Tactics panel for Arasmes; add a rule:
   - IF: Combat.IsInCombat = Yes
   - THEN: CastSpell = Magic Missile (or any L1 arcane spell), Rod = Quicken
   - TARGET: Enemy biggest threat
2. Trigger combat. Expect:
   - Magic Missile resolves as a swift action (Quicken applied)
   - Rod loses one charge in inventory
   - Mod log line: `Activated rod <RodAbilityName> for Arasmes (Quicken on Magic Missile)`
3. Drain the rod to 0 charges. Repeat — expect normal cast (no metamagic), no crash.
4. Move the rod out of the quickslot (still in inventory). Repeat — expect normal cast, no crash, no metamagic, no log line.
5. Set the rule to a spell above the rod's MaxSpellLevel (Lesser rod = max L3, try a L4 spell). Expect normal cast.

If any step fails, **stop and diagnose before proceeding**. Common issues:
- `MetamagicRodResolver` returns null when it shouldn't → add a Log.Engine.Debug at the top to trace `entries.Count`. Most likely cause: rod actually not quickslotted; the engine only registers `UnitPartSpecialMetamagic` after the rod's fact turns on, which depends on the quickslot.
- Rod activates but cast doesn't apply metamagic → check engine's `UnitUseAbility.CreateCastCommand` — if it's the synthetic-AbilityData branch, the activation may fire too late. Move `MaybeActivateRod` earlier or fall back to `Rulebook.Trigger`.
- Tooltip clips inside the rule scroll → check that `BuildTooltip` parented to canvas root, not host; verify with `Hierarchy` debug.

- [ ] **Step 2: Code review**

```bash
# In Claude session:
/review
```

Address any blocking findings before release.

- [ ] **Step 3: Run /release for v1.11.0**

```bash
# In Claude session:
/release minor
```

Per CLAUDE.md `/release` Pre-condition: do NOT manually pre-bump the version — the script does it. Today's csproj is at 1.10.0; minor bump → 1.11.0.

- [ ] **Step 4: Post-release verification**

After `/release` reports success:
1. Visit https://github.com/Gh05d/wrath-tactics/releases/tag/v1.11.0 — release exists, ZIP attached.
2. Visit https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005 — new file version visible after the GitHub Action completes.
3. Watch Discord/Nexus for rod-edge-case reports (rare metamagic types, custom rods from mods, multi-rod configurations) over the next 1-2 weeks.

---

## Out-of-band items

- **Multi-rod tie-break**: when a unit has both a Lesser and a Greater Quicken rod, `MetamagicRodResolver` returns the first match (insertion order in `m_MetamagicRodMechanics`). If a future user wants "prefer Greater for high-level spells, Lesser for low-level", that's a follow-up.
- **`HasMetamagicRod` condition**: not in this spec. Composes cleanly on top later — no change here would block it.
- **Specialty rods with `AbilitiesWhiteList`**: handled transparently — `IsSuitableAbility` returns false when the chosen spell isn't in the whitelist, fallback kicks in.

---

## Spec coverage check (self-review)

| Spec section | Plan task |
|---|---|
| Architecture: one nullable field | Task 1 |
| Components: MetamagicRodResolver | Task 4 |
| Components: ActionDef field | Task 1 |
| Components: ActionValidator changes | None — spec explicitly says validator does NOT participate (rod presence is informational only) |
| Components: CommandExecutor wiring | Task 5 |
| Components: RuleEditorWidget UI | Task 7 |
| Components: i18n keys (5 locales) | Task 2 (data) + Task 3 (helper) |
| Data flow: resolve → activate → cast | Task 5 |
| UI: dropdown + ⓘ + tooltip | Task 7 (dropdown + icon), Task 6 (tooltip helper) |
| Error handling: rod absent / charges 0 / wrong level | All covered by `IsSuitableAbility` + `TryStart` fallback in Task 5 — no extra code needed |
| Save-format compat | Newtonsoft default-null handling — no explicit migration |
| Testing: dotnet build per task | After every code task |
| Testing: smoke test on deck | Task 8, Step 1 |
| Testing: code review | Task 8, Step 2 |
