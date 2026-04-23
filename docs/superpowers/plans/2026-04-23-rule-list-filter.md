# Rule-List Filter + Explicit Scrollbar — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a sticky filter input above the rule list in `TacticsPanel` and make the existing (invisible-by-default) right-edge scrollbar clearly visible — both on character/global tabs and on the Presets tab.

**Architecture:** UI-only change. Filter input lives as a panel-level sticky strip between the control row and the scroll area. Filtering acts via `GameObject.SetActive` on the already-instantiated cards / entries (no destroy / recreate) to preserve TMP input focus and avoid layout churn. Scrollbar is restyled in place: wider track, higher-contrast colors, `Permanent` visibility. `PresetPanel` gets a parallel `ApplyFilter` method because it owns its own entry rendering; `TacticsPanel` dispatches per active tab.

**Tech Stack:** C# (.NET Framework 4.8.1), Unity UI (UGUI), TextMeshPro, HarmonyLib (unrelated), UMM entry. Build via `~/.dotnet/dotnet build … -p:SolutionDir=$(pwd)/`. Deploy via `./deploy.sh`. **No unit-test infrastructure exists in the mod** — verification is compile + manual smoke test on Steam Deck at each task checkpoint.

**Spec reference:** `docs/superpowers/specs/2026-04-23-rule-list-filter-design.md`

---

## File Structure

- **Modify** `WrathTactics/UI/UIHelpers.cs` — add one static match-helper
- **Modify** `WrathTactics/UI/TacticsPanel.cs` — filter strip, empty label, `ApplyFilter`, scrollbar restyle, `EffectiveDisplayName`, hook `RefreshRuleList` / `SelectTab`
- **Modify** `WrathTactics/UI/PresetPanel.cs` — entries tracking, `ApplyFilter`, empty label, `Rebuild` / `BuildUI` tail calls
- **Modify** `WrathTactics/Info.json` — version bump
- **Modify** `WrathTactics/WrathTactics.csproj` — version bump

No new files.

---

## Task 1: Add `StringMatchesFilter` helper in `UIHelpers`

**Files:**
- Modify: `WrathTactics/UI/UIHelpers.cs`

- [ ] **Step 1: Add the static helper**

Add this method anywhere inside the `public static class UIHelpers` body (e.g. after `CreateTMPInputField`):

```csharp
public static bool StringMatchesFilter(string name, string query) {
    if (string.IsNullOrWhiteSpace(query)) return true;
    return (name ?? "").IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
}
```

- [ ] **Step 2: Compile**

Run:
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded.` with 0 errors. Warnings about `findstr` are harmless (Linux).

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/UIHelpers.cs
git commit -m "feat(ui): add StringMatchesFilter helper for list filtering

Case-insensitive substring match with whitespace-query shortcut.
Used by the upcoming rule-list filter in TacticsPanel and PresetPanel."
```

---

## Task 2: Scrollbar visibility fix in `CreateRuleList`

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs` (scrollbar block in `CreateRuleList`, currently TacticsPanel.cs:210-235)

- [ ] **Step 1: Widen the track**

Find in `CreateRuleList`:
```csharp
scrollbarRect.sizeDelta = new Vector2(8, 0);
UIHelpers.AddBackground(scrollbarObj, new Color(0.12f, 0.12f, 0.12f, 0.5f));
```

Replace with:
```csharp
scrollbarRect.sizeDelta = new Vector2(12, 0);
UIHelpers.AddBackground(scrollbarObj, new Color(0.15f, 0.15f, 0.15f, 0.85f));
```

- [ ] **Step 2: Brighten the handle**

Find:
```csharp
UIHelpers.AddBackground(handleObj, new Color(0.5f, 0.5f, 0.5f, 0.6f));
```

Replace with:
```csharp
UIHelpers.AddBackground(handleObj, new Color(0.7f, 0.7f, 0.7f, 1.0f));
```

- [ ] **Step 3: Flip scrollbar visibility to `Permanent`**

Find:
```csharp
scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
scroll.verticalScrollbarSpacing = 2f;
```

Replace with:
```csharp
scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
scroll.verticalScrollbarSpacing = 4f;
```

- [ ] **Step 4: Compile + deploy + smoke test**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
./deploy.sh
```

On the Steam Deck, launch the game, open a save with a companion who has at least a couple of rules, press `Ctrl+T`, switch to that companion's tab. Expected: a clearly visible 12 px-wide grey scrollbar on the right edge of the rule list, visible even when the list doesn't overflow. Switch to the `Presets` tab — the same scrollbar covers the preset list (because `PresetPanel` is a child of `ruleListContent`).

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs
git commit -m "feat(ui): make rule-list scrollbar permanently visible

Previously AutoHideAndExpandViewport with 8px width and 50-60% alpha
left the scrollbar effectively invisible even when content overflowed.
Now Permanent, 12px, high-contrast handle. Also covers the Presets tab
via the shared ScrollRect."
```

---

## Task 3: Add filter-strip UI + state fields + empty label (no filter logic yet)

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs`

- [ ] **Step 1: Add state fields**

In the class body of `TacticsPanel`, near the other fields (around TacticsPanel.cs:15-23), add:

```csharp
// Filter state
string currentRuleFilter = "";
TMPro.TMP_InputField ruleFilterInput;
Button ruleFilterClearButton;
GameObject ruleFilterEmptyLabel;  // sibling of rule scroll, shown when filter hides everything
PresetPanel currentPresetPanel;    // tracks the active PresetPanel when presets tab is open
```

Add `using TMPro;` at the top if not already present (it is — line 5).

- [ ] **Step 2: Shrink the rule-scroll anchor and add the filter strip in `CreatePanel`**

Locate in `CreatePanel`:
```csharp
// Toggle + Add rule row
CreateControlRow(root.transform);

// Scrollable rule list
CreateRuleList(root.transform);
```

Replace with:
```csharp
// Toggle + Add rule row
CreateControlRow(root.transform);

// Filter strip (sticky — stays above the scroll area regardless of tab)
CreateFilterStrip(root.transform);

// Scrollable rule list
CreateRuleList(root.transform);

// Empty-state label for the rule list (hidden by default, driven by ApplyFilter)
CreateRuleFilterEmptyLabel(root.transform);
```

- [ ] **Step 3: Implement `CreateFilterStrip`**

Add this method to `TacticsPanel` (anywhere after `CreateControlRow`):

```csharp
void CreateFilterStrip(Transform parent) {
    var (strip, stripRect) = UIHelpers.Create("FilterStrip", parent);
    stripRect.SetAnchor(0.01, 0.99, 0.72, 0.76);
    stripRect.sizeDelta = Vector2.zero;
    UIHelpers.AddBackground(strip, new Color(0.14f, 0.14f, 0.14f, 1f));

    ruleFilterInput = UIHelpers.CreateTMPInputField(strip, "FilterInput",
        0.02, 0.85, "", 15f);
    var inputRect = ruleFilterInput.GetComponent<RectTransform>();
    inputRect.SetAnchor(0.02f, 0.85f, 0.1f, 0.9f);
    inputRect.sizeDelta = Vector2.zero;
    // Placeholder text
    var placeholder = ruleFilterInput.placeholder as TextMeshProUGUI;
    if (placeholder != null) {
        placeholder.text = "Filter rules…";
        placeholder.color = new Color(0.5f, 0.5f, 0.5f);
    }
    ruleFilterInput.onValueChanged.AddListener(v => {
        currentRuleFilter = v ?? "";
        UpdateFilterClearButton();
        ApplyFilter();
    });

    // Clear (×) button
    var (clearBtn, clearRect) = UIHelpers.Create("FilterClear", strip.transform);
    clearRect.SetAnchor(0.87f, 0.98f, 0.15f, 0.85f);
    clearRect.sizeDelta = Vector2.zero;
    UIHelpers.AddBackground(clearBtn, new Color(0.3f, 0.3f, 0.3f, 1f));
    UIHelpers.AddLabel(clearBtn, "✕", 16f, TextAlignmentOptions.Midline);
    ruleFilterClearButton = clearBtn.AddComponent<Button>();
    ruleFilterClearButton.onClick.AddListener(() => {
        ruleFilterInput.text = "";  // triggers onValueChanged -> ApplyFilter
    });
    ruleFilterClearButton.interactable = false;
}

void UpdateFilterClearButton() {
    if (ruleFilterClearButton == null) return;
    ruleFilterClearButton.interactable = !string.IsNullOrEmpty(currentRuleFilter);
}
```

- [ ] **Step 4: Implement `CreateRuleFilterEmptyLabel`**

Add this method to `TacticsPanel`:

```csharp
void CreateRuleFilterEmptyLabel(Transform parent) {
    var (obj, rect) = UIHelpers.Create("RuleFilterEmpty", parent);
    // Same anchor as the rule scroll so the label overlays its center
    rect.SetAnchor(0.01, 0.99, 0.02, 0.71);
    rect.sizeDelta = Vector2.zero;
    UIHelpers.AddLabel(obj, "No matching rules", 16f,
        TextAlignmentOptions.Midline, new Color(0.6f, 0.6f, 0.6f));
    obj.SetActive(false);
    ruleFilterEmptyLabel = obj;
}
```

- [ ] **Step 5: Shrink the rule scroll anchor**

Locate in `CreateRuleList`:
```csharp
scrollRect.SetAnchor(0.01, 0.99, 0.02, 0.76);
```

Replace with:
```csharp
scrollRect.SetAnchor(0.01, 0.99, 0.02, 0.71);
```

- [ ] **Step 6: Add `EffectiveDisplayName` static helper**

Add to the `TacticsPanel` class (near the bottom is fine):

```csharp
static string EffectiveDisplayName(Models.TacticsRule rule) {
    if (rule == null) return "";
    if (!string.IsNullOrEmpty(rule.PresetId)) {
        var preset = Engine.PresetRegistry.Get(rule.PresetId);
        if (preset != null) return preset.Name ?? "";
    }
    return rule.Name ?? "";
}
```

- [ ] **Step 7: Add a stub `ApplyFilter`**

Add to `TacticsPanel`:

```csharp
void ApplyFilter() {
    // Filled in by the next two tasks; stub for now so the input listener compiles.
    if (ruleFilterEmptyLabel != null) ruleFilterEmptyLabel.SetActive(false);
}
```

- [ ] **Step 8: Compile + deploy + smoke test**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
./deploy.sh
```

On Steam Deck: open panel, confirm the filter strip appears between the control row and the rule list. Click the input, type a few characters — no crash, cursor visible (ManualInputCaret via `CreateTMPInputField`). ✕ button is disabled until you type, then enabled, and clicking it clears the input. The rule list is pushed down slightly but still scrollable.

- [ ] **Step 9: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs
git commit -m "feat(ui): add filter strip scaffolding to TacticsPanel

Sticky filter input + clear button above the rule scroll area,
empty-state label overlaying the scroll area, state fields for the
filter query and preset-panel reference. ApplyFilter is a stub;
actual filtering wired up in the next commits."
```

---

## Task 4: Wire filter for Char/Global tabs

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs`

- [ ] **Step 1: Replace the stub `ApplyFilter`**

Find the stub from Task 3:
```csharp
void ApplyFilter() {
    // Filled in by the next two tasks; stub for now so the input listener compiles.
    if (ruleFilterEmptyLabel != null) ruleFilterEmptyLabel.SetActive(false);
}
```

Replace with:
```csharp
void ApplyFilter() {
    if (ruleListContent == null) return;

    // Presets tab branch — delegated in Task 6. For now, hide the char/global
    // empty label when the presets tab is active.
    if (selectedUnitId == "presets") {
        if (ruleFilterEmptyLabel != null) ruleFilterEmptyLabel.SetActive(false);
        return;
    }

    int visible = 0;
    int total = 0;
    for (int i = 0; i < ruleListContent.childCount; i++) {
        var child = ruleListContent.GetChild(i).gameObject;
        var widget = child.GetComponent<UI.RuleEditorWidget>();
        if (widget == null) continue;  // safety — only rule cards are counted
        total++;
        string name = EffectiveDisplayName(widget.Rule);
        bool match = UIHelpers.StringMatchesFilter(name, currentRuleFilter);
        child.SetActive(match);
        if (match) visible++;
    }

    bool filterActive = !string.IsNullOrWhiteSpace(currentRuleFilter);
    if (ruleFilterEmptyLabel != null)
        ruleFilterEmptyLabel.SetActive(filterActive && total > 0 && visible == 0);
}
```

- [ ] **Step 2: Expose `Rule` on `RuleEditorWidget`**

The widget currently holds the rule privately. Open `WrathTactics/UI/RuleEditorWidget.cs` and find the field that stores the rule (named `rule`, set in `Init`). Add a public property above or below it:

```csharp
public Models.TacticsRule Rule => rule;
```

(If the field is already named differently, match that name. Do not rename existing fields.)

- [ ] **Step 3: Reset filter on tab switch**

Find `SelectTab` in `TacticsPanel.cs`:
```csharp
void SelectTab(string unitId) {
    if (selectedUnitId != "presets")
        lastNonPresetUnitId = selectedUnitId;
    selectedUnitId = unitId;
    RebuildTabs();
    RefreshRuleList();
}
```

Replace with:
```csharp
void SelectTab(string unitId) {
    if (selectedUnitId != "presets")
        lastNonPresetUnitId = selectedUnitId;
    selectedUnitId = unitId;

    // Reset the filter on tab switch (fires onValueChanged -> sets currentRuleFilter = "").
    if (ruleFilterInput != null)
        ruleFilterInput.text = "";

    RebuildTabs();
    RefreshRuleList();
}
```

- [ ] **Step 4: Re-apply filter after each `RefreshRuleList`**

Find at the bottom of `RefreshRuleList` (after the `for` loop that creates rule cards):

```csharp
            for (int i = 0; i < rules.Count; i++) {
                var (card, _) = UIHelpers.Create($"Rule_{i}", ruleListContent);
                var widget = card.AddComponent<RuleEditorWidget>();
                var capturedRules = rules;
                widget.Init(rules[i], i, capturedRules, () => RefreshRuleList(), selectedUnitId);
            }
        }
```

Replace with:

```csharp
            for (int i = 0; i < rules.Count; i++) {
                var (card, _) = UIHelpers.Create($"Rule_{i}", ruleListContent);
                var widget = card.AddComponent<RuleEditorWidget>();
                var capturedRules = rules;
                widget.Init(rules[i], i, capturedRules, () => RefreshRuleList(), selectedUnitId);
            }

            ApplyFilter();
        }
```

- [ ] **Step 5: Compile + deploy + smoke test**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
./deploy.sh
```

On Steam Deck:

1. Open panel, pick a companion with several rules.
2. Type a substring of one rule's name → only matching cards remain visible; others are hidden; focus stays in the input.
3. ✕ clears the input → all cards visible again.
4. Type garbage (`zzz`) → "No matching rules" label appears.
5. Switch to a different companion tab → filter input is empty, full rule list visible.
6. Switch to Presets tab → empty-state label goes away (rule-filter branch no longer fires).
7. Linked rule whose preset-name matches the query → visible. Linked rule whose rule.Name would match but preset-name doesn't → hidden (preset-name is authoritative for linked rules).

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs WrathTactics/UI/RuleEditorWidget.cs
git commit -m "feat(ui): filter rule list by effective display name

ApplyFilter iterates ruleListContent children and toggles each card's
visibility via SetActive — keeps TMP focus across keystrokes, no
destroy/recreate. Filter is cleared on tab switch and re-applied after
RefreshRuleList. Linked rules match against their linked preset's Name,
inline rules against rule.Name."
```

---

## Task 5: Track entries + `ApplyFilter` in `PresetPanel`

**Files:**
- Modify: `WrathTactics/UI/PresetPanel.cs`

- [ ] **Step 1: Add state fields**

In the `PresetPanel` class body, near other fields, add:

```csharp
// Filter state — driven from TacticsPanel via ApplyFilter(string).
string currentFilter = "";
readonly List<(GameObject entry, string name)> entries = new List<(GameObject, string)>();
GameObject emptyMatchLabel;
```

- [ ] **Step 2: Clear `entries` at the start of `BuildUI`**

Find the start of `BuildUI`:
```csharp
void BuildUI() {
    var root = gameObject;

    var vlg = root.AddComponent<VerticalLayoutGroup>();
```

Replace with:
```csharp
void BuildUI() {
    entries.Clear();
    emptyMatchLabel = null;
    var root = gameObject;

    var vlg = root.AddComponent<VerticalLayoutGroup>();
```

- [ ] **Step 3: Populate `entries` in `CreatePresetEntry`**

Find the start of `CreatePresetEntry`:
```csharp
void CreatePresetEntry(Transform parent, TacticsRule preset) {
    var (row, _) = UIHelpers.Create($"Preset_{preset.Id}", parent);
    row.AddComponent<LayoutElement>().preferredHeight = 40;
    UIHelpers.AddBackground(row, new Color(0.18f, 0.18f, 0.18f, 1f));
```

Replace with:
```csharp
void CreatePresetEntry(Transform parent, TacticsRule preset) {
    var (row, _) = UIHelpers.Create($"Preset_{preset.Id}", parent);
    row.AddComponent<LayoutElement>().preferredHeight = 40;
    UIHelpers.AddBackground(row, new Color(0.18f, 0.18f, 0.18f, 1f));
    entries.Add((row, preset.Name ?? ""));
```

- [ ] **Step 4: Add the empty-match label at the end of `BuildUI`**

Find the end of `BuildUI` (after the "Open Presets Folder" button block, just before the closing brace of the method). The exact current ending:

```csharp
            folderBtn.AddComponent<Button>().onClick.AddListener(() => {
                var dir = System.IO.Path.Combine(Main.ModPath, "Presets");
                try {
                    System.IO.Directory.CreateDirectory(dir);
                    Application.OpenURL("file://" + dir);
                } catch (Exception ex) {
                    Log.UI.Warn($"Could not open presets folder '{dir}': {ex.Message}");
                }
            });
        }
```

Replace with:

```csharp
            folderBtn.AddComponent<Button>().onClick.AddListener(() => {
                var dir = System.IO.Path.Combine(Main.ModPath, "Presets");
                try {
                    System.IO.Directory.CreateDirectory(dir);
                    Application.OpenURL("file://" + dir);
                } catch (Exception ex) {
                    Log.UI.Warn($"Could not open presets folder '{dir}': {ex.Message}");
                }
            });

            // Empty-match label — shown by ApplyFilter when the filter hides every entry.
            var (emptyObj, _em) = UIHelpers.Create("EmptyMatch", root.transform);
            emptyObj.AddComponent<LayoutElement>().preferredHeight = 28;
            UIHelpers.AddLabel(emptyObj, "No matching presets", 15f,
                TextAlignmentOptions.MidlineLeft, new Color(0.6f, 0.6f, 0.6f));
            emptyObj.SetActive(false);
            emptyMatchLabel = emptyObj;

            ApplyFilter(currentFilter);
        }
```

- [ ] **Step 5: Handle the early-return path in `BuildUI` (empty preset list)**

Find the block that handles "no presets yet":

```csharp
            var presets = PresetRegistry.All();
            if (presets.Count == 0) {
                var (empty, _e) = UIHelpers.Create("Empty", root.transform);
                empty.AddComponent<LayoutElement>().preferredHeight = 28;
                UIHelpers.AddLabel(empty, "No presets yet.", 15f,
                    TextAlignmentOptions.MidlineLeft, Color.gray);
                return;
            }
```

The `return` here skips the empty-match-label construction from Step 4. For the zero-presets state we don't need the filter empty label (there's nothing to filter). Leave this block unchanged.

- [ ] **Step 6: Add the `ApplyFilter` method**

Add as a public method to `PresetPanel`:

```csharp
public void ApplyFilter(string query) {
    currentFilter = query ?? "";
    int visible = 0;
    foreach (var pair in entries) {
        bool match = UIHelpers.StringMatchesFilter(pair.name, currentFilter);
        if (pair.entry != null) pair.entry.SetActive(match);
        if (match) visible++;
    }
    bool filterActive = !string.IsNullOrWhiteSpace(currentFilter);
    if (emptyMatchLabel != null)
        emptyMatchLabel.SetActive(filterActive && entries.Count > 0 && visible == 0);
}
```

- [ ] **Step 7: Re-apply filter at the end of `Rebuild`**

Find `Rebuild`:
```csharp
void Rebuild() {
    for (int i = transform.childCount - 1; i >= 0; i--)
        Destroy(transform.GetChild(i).gameObject);
    // DestroyImmediate for layout components — deferred Destroy leaves duplicates
    // that fight over sizing for one frame, causing broken layout.
    var vlg = GetComponent<VerticalLayoutGroup>();
    if (vlg != null) DestroyImmediate(vlg);
    var csf = GetComponent<ContentSizeFitter>();
    if (csf != null) DestroyImmediate(csf);
    BuildUI();
}
```

No change needed — `BuildUI` now calls `ApplyFilter(currentFilter)` internally at its tail (Step 4). Leave `Rebuild` as-is.

- [ ] **Step 8: Compile + deploy + smoke test**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
./deploy.sh
```

On Steam Deck: open panel, go to Presets tab. Confirm existing behavior is unchanged (entries render, expand/collapse works, create/delete works). The filter path is not wired yet from `TacticsPanel` — filter input does nothing on this tab yet. Task 6 finishes the wire-up.

- [ ] **Step 9: Commit**

```bash
git add WrathTactics/UI/PresetPanel.cs
git commit -m "feat(ui): add ApplyFilter + entries tracking to PresetPanel

Populates an entries list during CreatePresetEntry, adds an empty-match
label, and exposes ApplyFilter(string) for TacticsPanel to call. Filter
state persists across Rebuild via BuildUI's tail call. Not yet wired —
next commit connects TacticsPanel's filter input."
```

---

## Task 6: Dispatch filter to `PresetPanel` from `TacticsPanel`

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs`

- [ ] **Step 1: Track the active `PresetPanel` in `RefreshRuleList`**

Find in `RefreshRuleList`:
```csharp
void RefreshRuleList() {
    if (ruleListContent == null) return;

    // Clear existing cards
    for (int i = ruleListContent.childCount - 1; i >= 0; i--)
        Destroy(ruleListContent.GetChild(i).gameObject);

    if (selectedUnitId == "presets") {
        var (presetObj, _) = UIHelpers.Create("PresetPanel", ruleListContent);
        var presetPanel = presetObj.AddComponent<PresetPanel>();
        presetPanel.Init(lastNonPresetUnitId, ruleListContent, () => RefreshRuleList());
        UpdateToggleLabel();
        return;
    }
```

Replace with:
```csharp
void RefreshRuleList() {
    if (ruleListContent == null) return;

    // Clear existing cards (this destroys any prior PresetPanel too).
    for (int i = ruleListContent.childCount - 1; i >= 0; i--)
        Destroy(ruleListContent.GetChild(i).gameObject);
    currentPresetPanel = null;

    if (selectedUnitId == "presets") {
        var (presetObj, _) = UIHelpers.Create("PresetPanel", ruleListContent);
        var presetPanel = presetObj.AddComponent<PresetPanel>();
        presetPanel.Init(lastNonPresetUnitId, ruleListContent, () => RefreshRuleList());
        currentPresetPanel = presetPanel;
        UpdateToggleLabel();
        ApplyFilter();
        return;
    }
```

- [ ] **Step 2: Extend `ApplyFilter` with the presets branch**

Find the current `ApplyFilter` (from Task 4):
```csharp
void ApplyFilter() {
    if (ruleListContent == null) return;

    // Presets tab branch — delegated in Task 6. For now, hide the char/global
    // empty label when the presets tab is active.
    if (selectedUnitId == "presets") {
        if (ruleFilterEmptyLabel != null) ruleFilterEmptyLabel.SetActive(false);
        return;
    }
```

Replace with:
```csharp
void ApplyFilter() {
    if (ruleListContent == null) return;

    if (selectedUnitId == "presets") {
        // Hide the char/global empty label — it's not ours on this tab.
        if (ruleFilterEmptyLabel != null) ruleFilterEmptyLabel.SetActive(false);
        // Unity's "==" operator returns true for destroyed MonoBehaviours, so
        // this null-check catches both a never-set reference and a destroyed one.
        if (currentPresetPanel != null)
            currentPresetPanel.ApplyFilter(currentRuleFilter);
        return;
    }
```

(The rest of the method — the char/global loop — stays exactly as written in Task 4.)

- [ ] **Step 3: Compile + deploy + smoke test (full matrix)**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
./deploy.sh
```

On Steam Deck, walk through the spec's test matrix:

1. Open panel on a companion with > 10 rules → scrollbar visible from the first frame (Task 2).
2. Type a substring matching half the rules → only matching cards remain visible; scrollbar resizes; no focus loss; other cards not destroyed.
3. Clear via ✕ → all cards re-appear; ✕ becomes non-interactable.
4. Clear via backspace → same as ✕.
5. Switch to a different companion → filter is empty, full list.
6. Linked rule whose preset name matches → visible; linked rule whose rule.Name matches but preset name doesn't → hidden.
7. Presets tab → same filtering on preset entries; header (Title, Hint, Export, Import, Status, New Preset, separator, Open Folder) stays visible.
8. Presets tab, filter returns zero matches → "No matching presets" label.
9. Char tab, filter returns zero matches → "No matching rules" overlay label.
10. Rapid typing → no focus loss, no dropped characters.
11. Regression: `+ New Rule` adds a rule (and the new rule respects the current filter). `+ From Preset` popup still opens and assigns. Edit / delete / toggle on a visible card still works.
12. Presets tab: `+ New Preset`, Import, Delete all still work, filter is re-applied after each Rebuild (new preset is visible if its name matches the current filter, hidden otherwise).

If any item fails, fix before committing.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs
git commit -m "feat(ui): dispatch rule-list filter to PresetPanel

TacticsPanel now tracks the active PresetPanel instance and forwards
the current filter query to it when the Presets tab is open. Sticky
filter strip works uniformly across char/global tabs and the Presets
tab."
```

---

## Task 7: Version bump + final verification

**Files:**
- Modify: `WrathTactics/Info.json`
- Modify: `WrathTactics/WrathTactics.csproj`

- [ ] **Step 1: Bump `Info.json` version**

Find in `WrathTactics/Info.json`:
```json
"Version": "1.0.1",
```

Replace with:
```json
"Version": "1.1.0",
```

(Minor bump: new user-facing feature, no breaking changes.)

- [ ] **Step 2: Bump csproj version**

Find in `WrathTactics/WrathTactics.csproj`:
```xml
<Version>1.0.1</Version>
```

Replace with:
```xml
<Version>1.1.0</Version>
```

- [ ] **Step 3: Release build to verify zip output**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded.` and a fresh `WrathTactics/bin/WrathTactics-1.1.0.zip`. Confirm the zip filename matches the bumped version.

- [ ] **Step 4: Deploy Debug build for one last smoke pass**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
./deploy.sh
```

Spot-check items 1, 2, 7, 9 from Task 6 Step 3 once more on the Steam Deck.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj
git commit -m "chore: bump version to 1.1.0

User-visible change: sticky filter input above the rule list and an
always-visible scrollbar on the right edge, covering both character
tabs and the Presets tab."
```

---

## Release (out-of-band)

Not part of this plan. After merging, run the `/release` slash-command when ready — it bundles push / tag / GitHub Release / Nexus upload / Discord post per the project's standard flow.
