# Rule Packs UX Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the six usability defects the maintainer found when play-testing rule packs: unreadable text, a destructive chip, an opaque save flow, packs buried in the Presets tab, and duplicate rules from re-applying.

**Architecture:** No new subsystem. The pack model, disk layer and registry stay as they are. Four changes: (1) the apply de-duplication key moves from `PackId + PresetId` to `PresetId` alone, which removes the duplicate-spam at its source; (2) the chip stops being a one-click mass-delete and offers two explicit actions; (3) `PackPanel` moves out of the Presets tab into a tab of its own; (4) "Save List as Pack" becomes a dialog with a name field and per-rule checkboxes.

**Tech Stack:** C# 7.3+/.NET Framework 4.8.1, Newtonsoft.Json (game-bundled), Unity uGUI + TextMeshPro, xUnit (net481, mono runner on Linux), UMM + HarmonyLib.

## Why these changes (maintainer's play-test findings)

1. White text on the light book-page background is hard to read across the Presets area.
2. "Save List as Pack" gives no idea what will be saved — one click, no name, no preview.
3. Clicking the chip's `×` after saving a list deletes every rule instead of just removing the pack.
4. Packs belong in their own tab behind Presets, not inside the Presets tab.
5. The same pack content can be applied repeatedly, spamming the rule list.
6. Overall: "da musst du deutlich nacharbeiten."

**Root cause linking 3 and 5:** the dedup key is `PackId + PresetId` — deliberately, so that removing pack B could not strip pack A's rule. That ownership split exists ONLY because the chip deletes rules. Each `SaveListAsPack` click also mints a NEW pack id with the same members, so applying that second pack sees none of the first pack's rules as "already there" and appends everything again. Once the chip no longer deletes, ownership no longer needs defending and the dedup can be preset-based, which kills the spam.

## Maintainer decisions (locked — do not revisit)

| Question | Decision |
|---|---|
| What does the chip do? | **Both**: one action detaches the pack marking (rules stay, colour and chip go), one action removes the pack's rules. Maximum flexibility. |
| Save List as Pack | **Dialog** with a name field and a per-rule checkbox list (all pre-checked). |
| Duplicate handling | **Preset-based dedup** — a rule for a given preset never enters the same list twice, regardless of which pack asks. |
| Packs tab | Own tab, positioned **after** Presets. |

## Global Constraints

- Build: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/` — the trailing slash after `$(pwd)` is required on Linux. `NU1900` warnings are expected and harmless.
- Tests: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`. Baseline: **182 passing**. The mono runner is flaky — run-to-run varying mass-failures are a flake, not a regression; re-run up to 3× and trust the first all-green run.
- Every new user-facing string needs a key in **all five** locale files (`en_GB`, `de_DE`, `fr_FR`, `ru_RU`, `zh_CN`), all with an identical key set and valid JSON. **Terminology: "preset" is de `Preset`, fr `préréglage`, ru `пресет`, zh `预设`** (the maintainer migrated the old wording — do not reintroduce `Vorlage`/`modèle`/`шаблон`). zh_CN uses ASCII `:` `,` `(` `)` plus fullwidth `。`.
- Locale JSONs are EmbeddedResources — string changes need a rebuild to take effect.
- Persisted JSON is APPEND-ONLY: enums and index-valued fields (`TacticsPack.ColorIndex`) may only grow at the end. Missing fields deserialize to defaults; no migrations.
- **Rule priority is array position** — nothing may sort or reorder a rule list.
- **Preset-linked rules carry an empty body by design**; nothing may populate or "repair" `ConditionGroups`/`Action`/`Target`.
- **Never rebuild a panel from inside a `TMP_InputField.onEndEdit` callback** — the rebuild destroys the field mid-callback. Use the established `StartCoroutine(DeferredRebuild())` indirection.
- **Inside a `HorizontalLayoutGroup` with `childControlWidth = true`, size children via `LayoutElement.preferredWidth`/`flexibleWidth`, never anchors** — mixing them yields zero-width children.
- Persistence calls returning `bool` must have their failure surfaced to the user.
- `catch (Exception ex)` is reserved for per-frame guards, user-surface persistence, and static blueprint init; narrow everywhere else.
- Code style: K&R braces, 4-space indent, `var` when the type is apparent.
- No UI test harness exists (Unity + game DLLs unavailable to the test host) — do not write UI tests. Pure logic in `Engine/`, `Models/`, `Persistence/` IS unit-tested.
- **Do not bump the version.** `/release` reads the pre-bump version and bumps itself.

## File Structure

**Create:**
- `WrathTactics/UI/SaveAsPackOverlay.cs` — modal dialog: pack name + per-rule checkbox list. Follows `BuffPickerOverlay`'s static-`Open` + centred-popup + click-swallow pattern.

**Modify:**
- `WrathTactics/Engine/PackRegistry.cs` — `PlanApply` and `CountAlreadyApplied` switch to preset-based dedup.
- `WrathTactics.Tests/PackApplyTests.cs` — the two tests asserting the old ownership semantics are rewritten; new cases for the new rule.
- `WrathTactics/UI/TacticsPanel.cs` — Packs tab, chip with two actions, `UnstampPackFromList`, `SaveListAsPack` routed through the dialog.
- `WrathTactics/UI/PresetPanel.cs` — drop the `PackPanel.Build` mount.
- `WrathTactics/UI/PackPanel.cs` — becomes the Packs tab's own panel; page labels get contrast.
- `WrathTactics/UI/UIHelpers.cs` — `AddPageLabel` helper for text that sits directly on the book art.
- `WrathTactics/Localization/*.json` — new keys in all five.
- `README.md`, `claude-context/gotchas-ui.md`, `claude-context/gotchas-persistence.md` — behaviour changes.

---

### Task 1: Preset-based de-duplication

**Files:**
- Modify: `WrathTactics/Engine/PackRegistry.cs` (`PlanApply`, `CountAlreadyApplied`)
- Test: `WrathTactics.Tests/PackApplyTests.cs`

**Interfaces:**
- Consumes: `TacticsPack`, `TacticsRule.PackId`/`PresetId`.
- Produces: unchanged signatures — `PlanApply(TacticsPack, List<TacticsRule>, Func<string,bool>) → List<TacticsRule>`, `CountAlreadyApplied(TacticsPack, List<TacticsRule>) → int`. Only the membership rule changes.

**This task deliberately inverts two existing tests.** `PlanApply_still_adds_a_member_present_only_from_another_pack` and `PlanApply_still_adds_a_member_present_as_a_hand_built_link` assert the OLD semantics. They are not broken tests — they encode a decision that has been reversed. Rewrite them to assert the new behaviour and say so in the commit message.

- [ ] **Step 1: Rewrite the affected tests first (they must fail)**

In `WrathTactics.Tests/PackApplyTests.cs`, replace the two named tests with:

```csharp
        [Fact]
        public void PlanApply_skips_a_member_already_present_from_another_pack() {
            // Preset-based dedup: one rule per preset per list, no matter which pack asks.
            // Prevents the duplicate spam from applying two packs that share members.
            var existing = new List<TacticsRule> { Linked("p1", "B") };
            var plan = PackRegistry.PlanApply(Pack("A", "p1", "p2"), existing, AllExist);

            Assert.Single(plan);
            Assert.Equal("p2", plan[0].PresetId);
        }

        [Fact]
        public void PlanApply_skips_a_member_already_present_as_a_hand_built_link() {
            var existing = new List<TacticsRule> { Linked("p1", null) };
            var plan = PackRegistry.PlanApply(Pack("A", "p1"), existing, AllExist);

            Assert.Empty(plan);
        }

        [Fact]
        public void PlanApply_ignores_rules_without_a_preset_link_when_deduping() {
            // A standalone rule has no PresetId; it can never be "the same rule" as a member.
            var existing = new List<TacticsRule> { new TacticsRule { Name = "hand-built" } };
            var plan = PackRegistry.PlanApply(Pack("A", "p1"), existing, AllExist);

            Assert.Single(plan);
        }
```

And add, next to the existing `CountAlreadyApplied` tests:

```csharp
        [Fact]
        public void CountAlreadyApplied_counts_a_member_present_from_any_pack() {
            // Must use the same membership rule as PlanApply, or added + already-present
            // stops adding up and the status message lies again.
            var rules = new List<TacticsRule> { Linked("p1", "B"), Linked("p2", null) };
            Assert.Equal(2, PackRegistry.CountAlreadyApplied(Pack("A", "p1", "p2"), rules));
        }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/ --filter FullyQualifiedName~PackApplyTests`
Expected: the three rewritten/new dedup tests FAIL (the current code adds the rule instead of skipping it); everything else passes.

- [ ] **Step 3: Switch the membership rule**

In `WrathTactics/Engine/PackRegistry.cs`, in `PlanApply`, replace the `alreadyFromThisPack` set and its use:

```csharp
            var alreadyFromThisPack = new HashSet<string>(
                (existing ?? new List<TacticsRule>())
                    .Where(r => r != null && r.PackId == pack.Id && !string.IsNullOrEmpty(r.PresetId))
                    .Select(r => r.PresetId));
```

with:

```csharp
            // Preset-based dedup: one rule per preset per list, whatever pack asks for it.
            // The old key was PackId+PresetId, which let two packs sharing a member each
            // insert their own copy — and since every "Save List as Pack" mints a fresh pack
            // id over the same presets, re-saving and re-applying spammed the list. Rules
            // are no longer owned exclusively by a pack (the chip detaches instead of
            // deleting), so there is nothing left for per-pack ownership to protect.
            var alreadyLinked = new HashSet<string>(
                (existing ?? new List<TacticsRule>())
                    .Where(r => r != null && !string.IsNullOrEmpty(r.PresetId))
                    .Select(r => r.PresetId));
```

and rename the two uses inside the loop (`alreadyFromThisPack.Contains(presetId)` → `alreadyLinked.Contains(presetId)`, `alreadyFromThisPack.Add(presetId)` → `alreadyLinked.Add(presetId)`).

In `CountAlreadyApplied`, replace the `fromThisPack` set:

```csharp
        var fromThisPack = new HashSet<string>(
            rules.Where(r => r != null && r.PackId == pack.Id && !string.IsNullOrEmpty(r.PresetId))
                 .Select(r => r.PresetId));
```

with:

```csharp
        // Same membership rule as PlanApply — if these two disagree, added + already-present
        // no longer sums to the pack's member count and the status message misreports again.
        var alreadyLinked = new HashSet<string>(
            rules.Where(r => r != null && !string.IsNullOrEmpty(r.PresetId))
                 .Select(r => r.PresetId));
```

and rename its use below (`fromThisPack.Contains(presetId)` → `alreadyLinked.Contains(presetId)`).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`
Expected: all green (182 + the new cases − none removed). Re-run on a mono flake.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Engine/PackRegistry.cs WrathTactics.Tests/PackApplyTests.cs
git commit -m "fix(packs): dedup applied rules by preset, not by pack ownership

Applying two packs that share a member, or re-applying a pack re-created by
Save List as Pack, appended duplicate rules. Ownership-scoped dedup only
existed to protect one pack's rules from another pack's removal; the chip no
longer deletes rules, so the protection is obsolete. Inverts two tests that
encoded the old decision."
```

---

### Task 2: Chip offers detach and remove instead of deleting on click

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs` (chip construction in `AddPackRow`, new `UnstampPackFromList`, existing `RemovePackFromList`)
- Modify: `WrathTactics/Localization/en_GB.json`

**Interfaces:**
- Consumes: `PackRegistry.AppliedPackIds`, `PackPalette.ColorAt`.
- Produces: private `TacticsPanel.ShowPackChipMenu(TacticsPack)`, `.UnstampPackFromList(TacticsPack)`; `RemovePackFromList` keeps its signature and behaviour but is now only reachable through the menu.

The chip currently deletes every rule of the pack on a single click of its `×`. Both actions the maintainer asked for go behind one click on the chip, which also removes the accidental-mass-delete risk: the menu itself is the confirmation.

- [ ] **Step 1: Add the en_GB strings**

In `WrathTactics/Localization/en_GB.json`, next to the other `pack.*` keys:

```json
  "pack.chip.menu_detach": "Remove pack marking (keep rules)",
  "pack.chip.menu_delete": "Delete this pack's {0} rule(s)",
  "status.pack_detached": "'{0}' unlinked from {1} rule(s) — the rules stayed",
```

- [ ] **Step 2: Replace the chip's click behaviour**

In `WrathTactics/UI/TacticsPanel.cs`, in `AddPackRow`, the chip is currently built as a container with a non-interactive label plus a `PackChipRemove` sub-button wired to `RemovePackFromList`. Replace that whole chip block with a single clickable chip that opens the action menu:

```csharp
                var (chip, _c) = UIHelpers.Create($"PackChip_{pack.Id}", row.transform);
                var chipLE = chip.AddComponent<LayoutElement>();
                chipLE.preferredWidth = 130;
                chipLE.flexibleWidth = 0;
                UIHelpers.AddBackground(chip, PackPalette.ColorAt(pack.ColorIndex));
                UIHelpers.AddLabel(chip, pack.Name + "  ▾", 13f, TextAlignmentOptions.Midline);
                // One click opens a menu with both actions. The menu IS the confirmation:
                // the previous design deleted every rule of the pack on a single click of a
                // control that read as a label (play-test finding).
                chip.AddComponent<Button>().onClick.AddListener(() => ShowPackChipMenu(captured));
```

- [ ] **Step 3: Add the menu and the detach action**

Add next to `RemovePackFromList` in `WrathTactics/UI/TacticsPanel.cs`:

```csharp
        // Both chip actions live behind one click. Order matters: the non-destructive
        // action is first, so a reflexive "click the top entry" cannot delete rules.
        void ShowPackChipMenu(TacticsPack pack) {
            var list = selectedUnitId == null
                ? ConfigManager.Current.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);
            int owned = list.Count(r => r != null && r.PackId == pack.Id);

            var options = new List<string> {
                "pack.chip.menu_detach".i18n(),
                string.Format("pack.chip.menu_delete".i18n(), owned),
            };

            PopupSelector.ShowPicker(options, idx => {
                if (idx == 0) UnstampPackFromList(pack);
                else if (idx == 1) RemovePackFromList(pack);
            });
        }

        // Clears the pack marking and leaves every rule in place, in order. This is what
        // "the pack is simply gone" means: the rules were built by the player and are not
        // the pack's property. They become unowned, so a later Save List as Pack can claim
        // them again.
        void UnstampPackFromList(TacticsPack pack) {
            var list = selectedUnitId == null
                ? ConfigManager.Current.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);

            int detached = 0;
            foreach (var rule in list) {
                if (rule == null || rule.PackId != pack.Id) continue;
                rule.PackId = null;
                detached++;
            }
            ConfigManager.Save();
            SetPackStatus(string.Format("status.pack_detached".i18n(), pack.Name, detached),
                new Color(0.6f, 0.85f, 0.6f));
            Log.UI.Info($"Detached pack '{pack.Name}' from {detached} rule(s)");
            RefreshRuleList();
        }
```

`System.Linq` is already imported in this file; if the build reports otherwise, add `using System.Linq;`.

- [ ] **Step 4: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: Build succeeded. Then run the suite once (expect unchanged pass count).

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs WrathTactics/Localization/en_GB.json
git commit -m "feat(packs): chip opens a menu with detach and remove instead of deleting"
```

---

### Task 3: Packs get their own tab behind Presets

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs` (tab bar, all `"presets"` special cases, `RefreshRuleList`)
- Modify: `WrathTactics/UI/PresetPanel.cs` (drop the `PackPanel.Build` mount)
- Modify: `WrathTactics/UI/PackPanel.cs` (own root layout, own status line)
- Modify: `WrathTactics/Localization/en_GB.json`

**Interfaces:**
- Consumes: `PackPanel.Build(Transform, Action, Action<string,Color>)` (unchanged signature).
- Produces: a new tab id `"packs"`, handled everywhere `"presets"` is.

`selectedUnitId` doubles as the tab id, with `"presets"` as a sentinel. There are **twelve** `"presets"` comparisons in `TacticsPanel.cs` (lines ~21, 22, 220, 247, 260, 383, 509, 689, 762, 853, 869, 878, 898 — verify with `grep -n '"presets"'`). Each one has to be considered: most mean "this is not a character list", which is now true for two sentinels.

- [ ] **Step 1: Add the en_GB strings**

```json
  "tab.packs": "Packs",
  "pack.tab_hint": "Packs bundle presets under one name and colour. Apply as many as you like to a companion — the rules stay individually editable, and removing a pack's marking leaves them in place.",
```

- [ ] **Step 2: Introduce a helper for the non-character tabs**

In `WrathTactics/UI/TacticsPanel.cs`, add next to the other small helpers:

```csharp
        // Both sentinel tabs show a panel instead of a character's rule list. Anything that
        // asks "is this a rule list?" must exclude both, or actions like AddNewRule fire
        // against a null character id.
        bool IsPanelTab => selectedUnitId == "presets" || selectedUnitId == "packs";
```

Then work through every `"presets"` comparison found by `grep -n '"presets"' WrathTactics/UI/TacticsPanel.cs` and replace it with `IsPanelTab` **except** these, which stay specific to one tab:
- the tab-bar construction (`AddTab(..., "presets", ...)`) — add the Packs tab beside it (Step 3);
- `RefreshRuleList`'s branch that instantiates `PresetPanel` — it gets a sibling branch for `PackPanel` (Step 4);
- `UpdateToggleLabel`'s branch that sets the header text — it gets a `"packs"` case using `"tab.packs".i18n()`;
- `SelectTab`'s `lastNonPresetUnitId` bookkeeping — that variable tracks the last character/global tab, so it must skip BOTH sentinels: change the guard to `if (!IsPanelTab)` before assigning, evaluated against the OLD value of `selectedUnitId`.

- [ ] **Step 3: Add the tab, after Presets**

In `RebuildTabs`, directly after the existing Presets tab line:

```csharp
            AddTab(tabBarTransform.gameObject, "tab.packs".i18n(), "packs", () => SelectTab("packs"));
```

- [ ] **Step 4: Render the Packs panel**

In `RefreshRuleList`, directly after the `selectedUnitId == "presets"` block that builds `PresetPanel`, add:

```csharp
            if (selectedUnitId == "packs") {
                var (packObj, _) = UIHelpers.Create("PackPanelRoot", ruleListContent);
                var panel = packObj.AddComponent<PackPanelHost>();
                panel.Init(() => RefreshRuleList());
                UpdateToggleLabel();
                ApplyFilter();
                return;
            }
```

- [ ] **Step 5: Give `PackPanel` a host component**

`PackPanel` is a static builder; the Packs tab needs a MonoBehaviour to own the root layout and the status line that `PresetPanel` used to provide. Add to `WrathTactics/UI/PackPanel.cs`:

```csharp
    /// <summary>
    /// Owns the Packs tab: root layout, status line, and the deferred rebuild that keeps
    /// a rename's onEndEdit callback off the stack while its input field is destroyed.
    /// </summary>
    public class PackPanelHost : MonoBehaviour {
        static string lastStatus;
        static Color lastStatusColor = Color.gray;
        Action onChanged;

        public void Init(Action onChanged) {
            this.onChanged = onChanged;
            BuildUI();
        }

        void BuildUI() {
            var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            var csf = gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIHelpers.AddHintCard(transform, "pack.tab_hint".i18n(), 40f);

            var (statusObj, _st) = UIHelpers.Create("PackTabStatus", transform);
            statusObj.AddComponent<LayoutElement>().preferredHeight = 24;
            UIHelpers.AddPageLabel(statusObj, lastStatus ?? "", 13f,
                TextAlignmentOptions.MidlineLeft, lastStatusColor);

            PackPanel.Build(transform, () => StartCoroutine(DeferredRebuild()),
                (text, color) => { lastStatus = text; lastStatusColor = color; });
        }

        IEnumerator DeferredRebuild() {
            yield return null;
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            var vlg = GetComponent<VerticalLayoutGroup>();
            if (vlg != null) DestroyImmediate(vlg);
            var csf = GetComponent<ContentSizeFitter>();
            if (csf != null) DestroyImmediate(csf);
            BuildUI();
        }
    }
```

Add `using System;`, `using System.Collections;`, `using UnityEngine;`, `using UnityEngine.UI;`, `using TMPro;` to the file if they are not already present. `AddPageLabel` arrives in Task 4 — if you implement this task first, use `UIHelpers.AddLabel` here and switch it in Task 4.

- [ ] **Step 6: Unmount `PackPanel` from the Presets tab**

In `WrathTactics/UI/PresetPanel.cs`, delete the `PackPanel.Build(...)` call and its comment that sits after the separator block. Nothing else in that file changes.

- [ ] **Step 7: Build and verify tab behaviour compiles cleanly**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: Build succeeded. Run the suite once (unchanged pass count).

Then re-run `grep -n '"presets"' WrathTactics/UI/TacticsPanel.cs` and confirm every remaining hit is one of the four intentional cases from Step 2. Any leftover bare comparison in an action guard (`AddNewRule`, `AddFromPreset`, `ToggleTactics`, `SaveListAsPack`, `ShowPackPicker`) is a bug: those must use `IsPanelTab`.

- [ ] **Step 8: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs WrathTactics/UI/PresetPanel.cs WrathTactics/UI/PackPanel.cs WrathTactics/Localization/en_GB.json
git commit -m "feat(packs): move packs to their own tab behind presets"
```

---

### Task 4: Readable text on the book-page background

**Files:**
- Modify: `WrathTactics/UI/UIHelpers.cs` (new `AddPageLabel`)
- Modify: `WrathTactics/UI/PackPanel.cs`, `WrathTactics/UI/PresetPanel.cs`

**Interfaces:**
- Produces: `UIHelpers.AddPageLabel(GameObject parent, string text, float fontSize, TextAlignmentOptions align, Color color)` → `TextMeshProUGUI` — same shape as `AddLabel`, plus a black outline.

Labels that sit directly on the book art have no background behind them; white-on-parchment is what the maintainer could not read. The codebase already solves this ad hoc in two places (`TacticsPanel.CreateRuleFilterEmptyLabel`, `UpdateToggleLabel`) by setting `outlineWidth`/`outlineColor` — this task turns that into one helper and applies it.

- [ ] **Step 1: Add the helper**

In `WrathTactics/UI/UIHelpers.cs`, next to `AddLabel`:

```csharp
        /// <summary>
        /// Label for text drawn directly on the book-page art, which is light enough that
        /// white text washes out. Adds the black outline the panel already used ad hoc in
        /// TacticsPanel. Use AddLabel instead whenever the label sits on its own dark
        /// background — the outline is pure cost there.
        /// </summary>
        public static TextMeshProUGUI AddPageLabel(GameObject parent, string text,
            float fontSize = 20f,
            TextAlignmentOptions alignment = TextAlignmentOptions.Midline,
            Color? color = null) {
            var label = AddLabel(parent, text, fontSize, alignment, color);
            label.outlineWidth = 0.25f;
            label.outlineColor = new Color32(0, 0, 0, 255);
            return label;
        }
```

If `AddLabel`'s signature differs (check its parameter list and default values before writing this), mirror it exactly rather than inventing one.

- [ ] **Step 2: Apply it to every label that has no background behind it**

Work through `WrathTactics/UI/PackPanel.cs` and `WrathTactics/UI/PresetPanel.cs`. For each `UIHelpers.AddLabel(...)` call, check whether its GameObject also gets a `UIHelpers.AddBackground(...)`:
- **Has a background** (buttons, rows, chips, swatches) → leave as `AddLabel`.
- **No background** (section titles, hints, empty-state text, status lines, the member/available list headers) → switch to `AddPageLabel`.

At minimum these have no background and must switch: `PackTitle`, `PackEmpty`, the members title, the members-empty text, the available-presets title, the member row's name label, the available row's name label, `PresetTitle`, `IOStatus`, `Empty`, `EmptyMatch`, and `PackPanelHost`'s status label from Task 3.

- [ ] **Step 3: Build and eyeball the diff**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: Build succeeded. Then `git diff` and confirm every switched call site is one where no `AddBackground` touches the same GameObject — a wrongly switched label costs nothing visually but signals the check was not done.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/UIHelpers.cs WrathTactics/UI/PackPanel.cs WrathTactics/UI/PresetPanel.cs
git commit -m "fix(ui): outline text drawn directly on the book-page background"
```

---

### Task 5: Save List as Pack becomes a dialog

**Files:**
- Create: `WrathTactics/UI/SaveAsPackOverlay.cs`
- Modify: `WrathTactics/UI/TacticsPanel.cs` (`SaveListAsPack`)
- Modify: `WrathTactics/Localization/en_GB.json`

**Interfaces:**
- Consumes: `PresetRegistry.PromoteRuleToPreset`, `PackRegistry.Save`, `PackPalette`.
- Produces: `SaveAsPackOverlay.Open(string suggestedName, List<TacticsRule> rules, Func<TacticsRule,string> describe, Action<string, List<TacticsRule>> onConfirm) → GameObject`.

Today the button promotes the entire list with an auto-generated name and no preview — "ich weiß nicht was ich speichern kann". The dialog shows the name up front and every rule with a checkbox, all pre-checked.

- [ ] **Step 1: Add the en_GB strings**

```json
  "pack.save_dialog.title": "Save rules as a pack",
  "pack.save_dialog.name_label": "Pack name",
  "pack.save_dialog.rules_label": "Rules to include",
  "pack.save_dialog.confirm": "Save Pack",
  "pack.save_dialog.cancel": "Cancel",
  "pack.save_dialog.none_selected": "Select at least one rule",
  "pack.save_dialog.linked_suffix": " (preset)",
```

- [ ] **Step 2: Write the overlay**

Create `WrathTactics/UI/SaveAsPackOverlay.cs`. Follow `BuffPickerOverlay`'s structure: static `Open` builds a full-screen dimmed overlay that closes on click, a centred popup that swallows clicks, and a controller MonoBehaviour on the popup.

```csharp
using System;
using System.Collections.Generic;
using Kingmaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    /// <summary>
    /// Modal for "Save List as Pack": a name field plus one checkbox per rule, all
    /// pre-checked. Replaces the previous one-click flow, which promoted the whole list
    /// under an auto-generated name with no preview of what would be saved.
    /// </summary>
    public class SaveAsPackOverlay : MonoBehaviour {
        readonly List<TacticsRule> selected = new List<TacticsRule>();
        TMP_InputField nameInput;
        TextMeshProUGUI errorLabel;
        Action<string, List<TacticsRule>> onConfirm;
        bool closed;

        public static GameObject Open(string suggestedName, List<TacticsRule> rules,
            Func<TacticsRule, string> describe, Action<string, List<TacticsRule>> onConfirm) {

            var canvas = Game.Instance.UI.Canvas.transform;
            var (overlay, overlayRect) = UIHelpers.Create("SaveAsPackOverlay", canvas);
            overlayRect.FillParent();
            UIHelpers.AddBackground(overlay, new Color(0, 0, 0, 0.4f));
            overlay.AddComponent<Button>().onClick.AddListener(() => Destroy(overlay));

            var (popup, popupRect) = UIHelpers.Create("Popup", overlay.transform);
            UIHelpers.AddBackground(popup, new Color(0.12f, 0.12f, 0.12f, 0.99f));
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(460f, 520f);

            // Swallow clicks so the popup itself does not close the overlay.
            var swallow = popup.AddComponent<Button>();
            swallow.targetGraphic = popup.GetComponent<Image>();

            var controller = popup.AddComponent<SaveAsPackOverlay>();
            controller.onConfirm = (name, chosen) => {
                if (controller.closed) return;
                controller.closed = true;
                onConfirm?.Invoke(name, chosen);
                Destroy(overlay);
            };
            controller.selected.AddRange(rules);   // everything pre-checked
            controller.BuildUI(popup, suggestedName, rules, describe);
            return overlay;
        }

        void BuildUI(GameObject popup, string suggestedName, List<TacticsRule> rules,
            Func<TacticsRule, string> describe) {

            var vlg = popup.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.padding = new RectOffset(12, 12, 12, 12);

            var (titleObj, _t) = UIHelpers.Create("Title", popup.transform);
            titleObj.AddComponent<LayoutElement>().preferredHeight = 30;
            UIHelpers.AddLabel(titleObj, "pack.save_dialog.title".i18n(), 18f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            var (nameLabel, _nl) = UIHelpers.Create("NameLabel", popup.transform);
            nameLabel.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddLabel(nameLabel, "pack.save_dialog.name_label".i18n(), 14f,
                TextAlignmentOptions.MidlineLeft, new Color(0.8f, 0.8f, 0.8f));

            var (nameHolder, _nh) = UIHelpers.Create("NameHolder", popup.transform);
            nameHolder.AddComponent<LayoutElement>().preferredHeight = 32;
            nameInput = UIHelpers.CreateTMPInputField(nameHolder, "NameInput", 0, 1,
                suggestedName, 16f);

            var (rulesLabel, _rl) = UIHelpers.Create("RulesLabel", popup.transform);
            rulesLabel.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddLabel(rulesLabel, "pack.save_dialog.rules_label".i18n(), 14f,
                TextAlignmentOptions.MidlineLeft, new Color(0.8f, 0.8f, 0.8f));

            foreach (var rule in rules) {
                var captured = rule;
                var (row, _r) = UIHelpers.Create($"Rule_{rule.Id}", popup.transform);
                row.AddComponent<LayoutElement>().preferredHeight = 28;
                UIHelpers.AddBackground(row, new Color(0.16f, 0.16f, 0.16f, 1f));

                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 6;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.padding = new RectOffset(6, 6, 2, 2);
                hlg.childAlignment = TextAnchor.MiddleLeft;

                var (box, _b) = UIHelpers.Create("Check", row.transform);
                var boxLE = box.AddComponent<LayoutElement>();
                boxLE.preferredWidth = 28;
                boxLE.minWidth = 28;
                boxLE.flexibleWidth = 0;
                UIHelpers.AddBackground(box, new Color(0.25f, 0.4f, 0.25f, 1f));
                var boxLabel = UIHelpers.AddLabel(box, "x", 15f, TextAlignmentOptions.Midline);
                box.AddComponent<Button>().onClick.AddListener(() => {
                    if (selected.Contains(captured)) {
                        selected.Remove(captured);
                        boxLabel.text = "";
                    } else {
                        selected.Add(captured);
                        boxLabel.text = "x";
                    }
                    if (errorLabel != null) errorLabel.text = "";
                });

                var (nameObj, _n) = UIHelpers.Create("RuleName", row.transform);
                var nameLE = nameObj.AddComponent<LayoutElement>();
                nameLE.flexibleWidth = 1;
                nameLE.preferredWidth = 300;
                UIHelpers.AddLabel(nameObj, describe(captured), 13f,
                    TextAlignmentOptions.MidlineLeft, Color.white);
            }

            var (errObj, _e) = UIHelpers.Create("Error", popup.transform);
            errObj.AddComponent<LayoutElement>().preferredHeight = 20;
            errorLabel = UIHelpers.AddLabel(errObj, "", 13f,
                TextAlignmentOptions.MidlineLeft, new Color(1f, 0.5f, 0.4f));

            var (buttons, _bt) = UIHelpers.Create("Buttons", popup.transform);
            buttons.AddComponent<LayoutElement>().preferredHeight = 36;
            var bhlg = buttons.AddComponent<HorizontalLayoutGroup>();
            bhlg.spacing = 8;
            bhlg.childForceExpandWidth = true;
            bhlg.childForceExpandHeight = true;
            bhlg.childControlWidth = true;
            bhlg.childControlHeight = true;

            var (cancel, _c) = UIHelpers.Create("Cancel", buttons.transform);
            UIHelpers.AddBackground(cancel, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(cancel, "pack.save_dialog.cancel".i18n(), 15f,
                TextAlignmentOptions.Midline);
            cancel.AddComponent<Button>().onClick.AddListener(() => Destroy(transform.parent.gameObject));

            var (confirm, _cf) = UIHelpers.Create("Confirm", buttons.transform);
            UIHelpers.AddBackground(confirm, new Color(0.25f, 0.45f, 0.3f, 1f));
            UIHelpers.AddLabel(confirm, "pack.save_dialog.confirm".i18n(), 15f,
                TextAlignmentOptions.Midline);
            confirm.AddComponent<Button>().onClick.AddListener(() => {
                if (selected.Count == 0) {
                    errorLabel.text = "pack.save_dialog.none_selected".i18n();
                    return;
                }
                // Preserve list order rather than click order — rule order is priority.
                var ordered = new List<TacticsRule>();
                foreach (var rule in rules) if (selected.Contains(rule)) ordered.Add(rule);
                onConfirm(nameInput.text?.Trim(), ordered);
            });

            UIHelpers.EnsureAllHoverable(popup);
        }
    }
}
```

- [ ] **Step 3: Route `SaveListAsPack` through the dialog**

In `WrathTactics/UI/TacticsPanel.cs`, keep the existing guards (`IsPanelTab`, filter guard, empty-list guard) and the entire promotion body, but move the promotion into a private method that takes the chosen name and rules. Replace the body after the guards with:

```csharp
            SaveAsPackOverlay.Open(
                string.Format("pack.saved_list_name".i18n(),
                    selectedUnitId == null ? "tab.global".i18n() : GetCharacterName(selectedUnitId)),
                new List<TacticsRule>(list),
                DescribeRuleForDialog,
                (name, chosen) => CommitListAsPack(name, chosen));
            return;
```

Then add:

```csharp
        // Linked rules show their preset's name; standalone rules their own. Index prefix
        // matches the rule cards, so the dialog and the list read the same way.
        string DescribeRuleForDialog(TacticsRule rule) {
            var list = selectedUnitId == null
                ? ConfigManager.Current.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);
            int idx = list.IndexOf(rule);
            string name = EffectiveDisplayName(rule);
            if (!string.IsNullOrEmpty(rule.PresetId))
                name += "pack.save_dialog.linked_suffix".i18n();
            return idx >= 0 ? $"{idx + 1}. {name}" : name;
        }
```

`CommitListAsPack(string name, List<TacticsRule> chosen)` is the previous `SaveListAsPack` body with three changes: it iterates `chosen` instead of the whole list; it uses `name` for `pack.Name` when non-empty, falling back to the previous auto-generated name when the user cleared the field; and it keeps every existing status branch (success, partial, save-failed, nothing-promoted) untouched.

Verify `EffectiveDisplayName` exists in this file (it is used by `ApplyFilter`); if its name differs, use whatever `ApplyFilter` calls.

- [ ] **Step 4: Build and run the suite**

Run the build, then the tests (unchanged pass count).

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/SaveAsPackOverlay.cs WrathTactics/UI/TacticsPanel.cs WrathTactics/Localization/en_GB.json
git commit -m "feat(packs): save-as-pack dialog with name field and rule selection"
```

---

### Task 6: Localisation and documentation

**Files:**
- Modify: `WrathTactics/Localization/{de_DE,fr_FR,ru_RU,zh_CN}.json`
- Modify: `README.md`, `claude-context/gotchas-ui.md`, `claude-context/gotchas-persistence.md`

- [ ] **Step 1: List what needs translating**

Run: `rtk proxy grep -n '"pack\.\|"status\.pack\|"tab\.packs' WrathTactics/Localization/en_GB.json`
Every key that appears there and is missing from the other four files needs a translation. Tasks 2, 3 and 5 each added keys; work from the file, not from memory.

- [ ] **Step 2: Translate into all four locales**

Terminology is fixed: "preset" is de `Preset`, fr `préréglage`, ru `пресет`, zh `预设`. Match each file's existing register and punctuation conventions (zh_CN: ASCII `:` `,` `(` `)` plus fullwidth `。`). Placeholders (`{0}`, `{1}`) must survive with the same set; word order may change.

- [ ] **Step 3: Verify**

```bash
for f in WrathTactics/Localization/*.json; do python3 -c "import json;json.load(open('$f'))" && echo "$f OK"; done
python3 - <<'EOF'
import json, re
en = json.load(open('WrathTactics/Localization/en_GB.json'))
want = {k for k in en if k.startswith(('pack.','status.pack','tab.packs'))}
for loc in ['de_DE','fr_FR','ru_RU','zh_CN']:
    d = json.load(open(f'WrathTactics/Localization/{loc}.json'))
    missing = sorted(k for k in want if k not in d)
    same = sorted(k for k in want if k in d and d[k] == en[k] and len(en[k]) > 3)
    ph = sorted(k for k in want if k in d and set(re.findall(r'\{\d+\}', d[k])) != set(re.findall(r'\{\d+\}', en[k])))
    print(loc, '| missing:', missing or 'none', '| english:', same or 'none', '| placeholder-mismatch:', ph or 'none')
EOF
```
All three lists must be `none` (a short glyph-only value may legitimately match English).

- [ ] **Step 4: Update the documentation to match the new behaviour**

`README.md` — the Rule Packs section currently describes the old chip (`×` deletes) and the old save flow. Rewrite those parts: packs live in their own tab; the chip opens a menu offering "remove the marking" (rules stay) or "delete this pack's rules"; saving a list opens a dialog where the name and the included rules are chosen; a rule for a preset that is already in the list is not added twice.

`claude-context/gotchas-persistence.md` — the pack bullets state the `PackId + PresetId` dedup rule. Replace with the preset-based rule and the reason it changed (ownership-scoped dedup existed only to protect rules from another pack's delete; the chip no longer deletes).

`claude-context/gotchas-ui.md` — add: labels drawn directly on the book-page art need `UIHelpers.AddPageLabel`, not `AddLabel`; white-on-parchment is unreadable and this was a shipped defect.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Localization/ README.md claude-context/
git commit -m "docs+i18n: pack UX rework"
```

---

## Final Verification

- [ ] **Suite green:** `for i in 1 2 3; do ~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/; done` — at least one all-green run.
- [ ] **Release build:** `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/` produces `bin/WrathTactics-<version>.zip`.
- [ ] **Deploy:** `./deploy.sh`
- [ ] **In-game smoke test** (the maintainer runs this; it is the acceptance test for every finding):
  1. Presets and Packs tabs: all text readable against the page background.
  2. Packs tab sits after Presets and holds the whole pack UI; the Presets tab no longer shows packs.
  3. "Save List as Pack" opens the dialog: name pre-filled and editable, every rule listed with a checkbox, unchecking excludes that rule, Cancel does nothing, saving with no rule checked shows the hint instead of saving.
  4. Applying the same pack twice adds nothing the second time; applying two packs that share presets does not duplicate rules.
  5. Chip click offers both actions; "remove marking" leaves every rule in place and in order, losing only colour and chip; "delete rules" removes exactly that pack's rules.
  6. Pack rules still fire in combat (`Mods/WrathTactics/Logs/wrath-tactics-*.log`).

## Notes for the implementer

- `PopupSelector.ShowPicker(List<string>, Action<int>)` is the existing modal picker (see `AddFromPreset`).
- `TacticsPanel` holds `lastPackStatus`/`lastPackStatusColor` plus `SetPackStatus`; the pack row renders the message. Keep using it — the character tabs have no other status surface.
- A rule that is skipped by the new dedup keeps the `PackId` of whichever pack inserted it first. Applying a second pack whose members are all already present therefore adds no chip for that second pack; the status message is what tells the user it was already covered. That is intended — do not add multi-pack ownership to "fix" it.
